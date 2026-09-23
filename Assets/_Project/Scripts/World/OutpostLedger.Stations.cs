using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// <summary>
    /// **Stations, orders and hauling (2026-09-23, Kevin's storage-hub rules).**
    ///
    /// The camp store (`stores`) is the hub: before a Storage/Storehouse
    /// stands it is the square by the fire, afterwards it is inside that
    /// building (`HasStorageBuilding` tells the visuals which). Every gathered
    /// unit lands there. Production stations (`IsStation`: a plan with a
    /// position AND a recipe table -- Sawmill, Quarry, Fletcher, Kitchen,
    /// Blacksmith; NOT the Farm, whose field is its input, and NOT the
    /// Watchtower) each keep their own `StationStock` per built instance:
    /// input bay, one-job bench, output rack, and the player's order.
    /// Nothing is worked without an order. All hauling is ledger arithmetic
    /// (armful per trip, `StationTripDays` per trip), never driven by bodies
    /// -- D2 holds because each trip's remaining time is saved on the hand.
    ///
    /// READ API (visuals, part B / villager mime, part D):
    /// <list type="bullet">
    /// <item>`HasStorageBuilding` -- where to draw the store.</item>
    /// <item>`Stations` (list; the index is the station id everywhere),
    ///   `StationOf(planId, ordinal)`, `StationIndex(planId, ordinal)`,
    ///   `StationForRaised(raisedIndex)`, `StationOfHand(hand)`.</item>
    /// <item>Per `StationStock`: `InputCap`, `OutputCap`, `BayCount(res)`,
    ///   `bay` rows, `benchState`, `BenchRecipe`, `benchProgress` (0..1),
    ///   `benchOut`, `RackCount(res)`, `RackTotal`, `rack` rows, `HasOrder`,
    ///   `orderRecipe`/`orderLeft`/`orderRepeat`.</item>
    /// <item>`OrderAt(stationIndex)` -> `StationOrder {recipe, remaining, repeat, Active}`.</item>
    /// <item>`HaulOf(hand)` -> `HaulView {active, resource, count, from, fromStation,
    ///   to, toStation, progress01}`; from/to are Store, Station (index) or Field.</item>
    /// <item>`CarriedOf(res)` -- units in hands' arms right now (not in `CountOf`).</item>
    /// </list>
    /// WRITE API: `PlaceOrder(stationIndex | planId, recipeId, count /* -1 = repeat */)`,
    /// `StopOrder(stationIndex | planId)`. `ChooseRecipe(planId, recipeId)` is kept
    /// and now places a REPEAT order on every station of that plan.
    /// </summary>
    public partial class OutpostLedger
    {
        /// One row per built station instance. Rebuilt against `built` by
        /// `EnsureStations` at the top of every `Step`.
        public List<StationStock> stations = new List<StationStock>();

        /// False in a save from before stations: the first `EnsureStations`
        /// gives every station that has a Work hand on it a Repeat order for
        /// its current recipe, so an old camp keeps producing.
        public bool stationsMigrated;

        /// Order count meaning "until told to stop".
        public const int RepeatOrder = -1;

        /// **Game-days one haul trip takes** (walk there, pick up, walk
        /// back), in the hand's effective working days. A flagged guess:
        /// 0.15 day = 27 s of a 180 s day. With a 2-log armful that is ~13
        /// logs a day, in line with the builders' `HaulPerHandPerDay` (12).
        public const float StationTripDays = 0.15f;

        const float Eps = 1e-5f;

        // --- identity ----------------------------------------------------------

        /// A plan that turns inputs into outputs on a bench: has a position
        /// and a recipe table.
        public static bool IsStation(string planId) =>
            !string.IsNullOrEmpty(planId) && BuildPlans.HasPosition(planId)
            && Economy.Recipes.StationHasRecipes(planId);

        /// Storage or Storehouse is standing: the store is drawn in it.
        public bool HasStorageBuilding =>
            built != null && (built.Contains(BuildPlans.Storage.id) || built.Contains(BuildPlans.Storehouse.id));

        public IReadOnlyList<StationStock> Stations
        {
            get { EnsureStations(); return stations; }
        }

        public StationStock StationAt(int index)
        {
            EnsureStations();
            return index >= 0 && index < stations.Count ? stations[index] : null;
        }

        public int StationIndex(string planId, int ordinal = 0)
        {
            if (stations == null) return -1;
            for (int i = 0; i < stations.Count; i++)
            {
                var s = stations[i];
                if (s != null && s.planId == planId && s.ordinal == ordinal) return i;
            }
            return -1;
        }

        public StationStock StationOf(string planId, int ordinal = 0)
        {
            int i = StationIndex(planId, ordinal);
            return i >= 0 ? stations[i] : null;
        }

        /// The station a `raised` row stands for (the nth of its plan), or
        /// null for a non-station building.
        public StationStock StationForRaised(int raisedIndex)
        {
            if (raised == null || raisedIndex < 0 || raisedIndex >= raised.Count) return null;
            var r = raised[raisedIndex];
            if (r == null || !IsStation(r.planId)) return null;
            int ordinal = 0;
            for (int i = 0; i < raisedIndex; i++)
                if (raised[i] != null && raised[i].planId == r.planId) ordinal++;
            EnsureStations();
            return StationOf(r.planId, ordinal);
        }

        int StationCountOfPlan(string planId)
        {
            int n = 0;
            if (stations != null)
                foreach (var s in stations) if (s != null && s.planId == planId) n++;
            return n;
        }

        /// The station instance a Work hand stands at: Work hands on one plan
        /// are dealt round the plan's instances in hand-list order.
        public StationStock StationOfHand(OutpostHand h)
        {
            if (h == null || h.order != OutpostOrder.Work || !IsStation(h.target)) return null;
            int n = StationCountOfPlan(h.target);
            if (n <= 0) return null;
            int k = 0;
            foreach (var x in hands)
            {
                if (x == h) break;
                if (x != null && x.order == OutpostOrder.Work && x.target == h.target) k++;
            }
            return StationOf(h.target, k % n);
        }

        /// Keep one `StationStock` per built station instance. A station
        /// whose building is gone spills everything back into the store
        /// (over the ceiling if need be -- it exists). Idempotent.
        public void EnsureStations()
        {
            if (stations == null) stations = new List<StationStock>();
            if (built == null) built = new List<string>();

            bool anyGone = false;
            for (int i = 0; i < stations.Count; i++)
                if (StationGone(i)) { anyGone = true; break; }
            if (anyGone)
            {
                FlushAllHauls();
                for (int i = stations.Count - 1; i >= 0; i--)
                {
                    if (!StationGone(i)) continue;
                    SpillStation(stations[i]);
                    stations.RemoveAt(i);
                }
            }

            foreach (var p in BuildPlans.AtACamp)
            {
                if (!IsStation(p.id)) continue;
                int n = CountBuilt(p.id);
                for (int k = 0; k < n; k++)
                    if (StationIndex(p.id, k) < 0)
                        stations.Add(new StationStock { planId = p.id, ordinal = k });
            }

            if (!stationsMigrated)
            {
                stationsMigrated = true;
                foreach (var s in stations)
                {
                    if (s == null || s.HasOrder || HandsOn(OutpostOrder.Work, s.planId) <= 0) continue;
                    var r = LegacyRecipeAt(s.planId);
                    if (r == null) continue;
                    s.orderRecipe = r.id;
                    s.orderRepeat = true;
                    s.orderLeft = 0;
                }
            }
        }

        bool StationGone(int i)
        {
            var s = stations[i];
            if (s == null || !IsStation(s.planId)) return true;
            if (s.ordinal < 0 || s.ordinal >= CountBuilt(s.planId)) return true;
            // A duplicate row (same plan and ordinal earlier in the list).
            for (int j = 0; j < i; j++)
                if (stations[j] != null && stations[j].planId == s.planId && stations[j].ordinal == s.ordinal)
                    return true;
            return false;
        }

        void SpillStation(StationStock s)
        {
            if (s == null) return;
            if (s.bay != null) foreach (var b in s.bay) SpillRow(b);
            if (s.rack != null) foreach (var r in s.rack) SpillRow(r);
            var rec = s.BenchRecipe;
            if (rec != null)
            {
                if (s.benchState == BenchState.Finished && s.benchOut > 0)
                    Store(rec.makes, true).whole += s.benchOut;
                else if (s.benchState == BenchState.Loaded || s.benchState == BenchState.Working)
                    foreach (var line in rec.takes)
                        if (line.n > 0) Store(line.res, true).whole += line.n;
            }
        }

        void SpillRow(OutpostStore row)
        {
            if (row == null || string.IsNullOrEmpty(row.resource)) return;
            var st = Store(row.resource, true);
            st.whole += row.whole;
            st.part += row.part;
            int w = Mathf.FloorToInt(st.part);
            if (w > 0) { st.whole += w; st.part -= w; }
        }

        // --- orders --------------------------------------------------------------

        /// Give a station an order: make `count` units of `recipeId`, or
        /// repeat until stopped when `count` is `RepeatOrder` (-1, or any
        /// negative). Refused (false) for a recipe this station does not make
        /// or cannot yet (fire level, station level, missing tool). A job
        /// already on the bench is left to finish.
        public bool PlaceOrder(int stationIndex, string recipeId, int count)
        {
            var s = StationAt(stationIndex);
            if (s == null || count == 0) return false;
            var r = Economy.Recipes.Named(recipeId);
            if (r == null || r.station != s.planId || !RecipeAvailable(r, out _)) return false;
            s.orderRecipe = r.id;
            s.orderRepeat = count < 0;
            s.orderLeft = count < 0 ? 0 : count;
            return true;
        }

        public bool PlaceOrder(string planId, string recipeId, int count, int ordinal = 0)
        {
            EnsureStations();
            return PlaceOrder(StationIndex(planId, ordinal), recipeId, count);
        }

        /// Stop a station's order. A job already on the bench still finishes.
        public void StopOrder(int stationIndex)
        {
            var s = StationAt(stationIndex);
            if (s != null) s.ClearOrder();
        }

        public void StopOrder(string planId, int ordinal = 0)
        {
            EnsureStations();
            StopOrder(StationIndex(planId, ordinal));
        }

        public StationOrder OrderAt(int stationIndex)
        {
            var s = StationAt(stationIndex);
            if (s == null || !s.HasOrder) return default;
            return new StationOrder { recipe = s.OrderRecipe, remaining = s.orderLeft, repeat = s.orderRepeat };
        }

        public StationOrder OrderAt(string planId, int ordinal = 0)
        {
            EnsureStations();
            return OrderAt(StationIndex(planId, ordinal));
        }

        /// The first station of this plan with an order, for `RecipeAt`.
        Economy.Recipe OrderedRecipe(string planId)
        {
            if (stations == null) return null;
            foreach (var s in stations)
                if (s != null && s.planId == planId && s.HasOrder) return s.OrderRecipe;
            return null;
        }

        // --- station stock in the camp totals ---------------------------------

        /// Whole units of `res` in the store only (no station stock). What
        /// the ceiling is measured against.
        public int StoreCountOf(string resource)
        {
            var s = Store(resource);
            return s != null ? s.whole : 0;
        }

        int StationCountOf(string res)
        {
            int n = 0;
            if (stations != null) foreach (var s in stations) if (s != null) n += s.CountOf(res);
            return n;
        }

        float StationHeldOf(string res)
        {
            float n = 0f;
            if (stations != null) foreach (var s in stations) if (s != null) n += s.HeldOf(res);
            return n;
        }

        int StationTotal()
        {
            int n = 0;
            if (stations != null) foreach (var s in stations) if (s != null) n += s.Total;
            return n;
        }

        /// Whole units out of the stations after the store ran short:
        /// racks, then finished benches, then bays.
        int TakeFromStations(string res, int n)
        {
            if (stations == null || n <= 0) return 0;
            int got = 0;
            foreach (var s in stations)
            {
                if (s == null || got >= n) continue;
                var r = s.Rack(res);
                if (r != null) { int t = Mathf.Min(r.whole, n - got); r.whole -= t; got += t; }
            }
            foreach (var s in stations)
            {
                if (s == null || got >= n) continue;
                if (s.benchState == BenchState.Finished && s.benchOut > 0 && s.BenchMakes == res)
                {
                    int t = Mathf.Min(s.benchOut, n - got);
                    s.benchOut -= t; got += t;
                    if (s.benchOut <= 0) EmptyBench(s);
                }
            }
            foreach (var s in stations)
            {
                if (s == null || got >= n) continue;
                var b = s.Bay(res);
                if (b != null) { int t = Mathf.Min(b.whole, n - got); b.whole -= t; got += t; }
            }
            return got;
        }

        /// **Take a FRACTION of `res` from wherever the camp holds it** --
        /// store, racks, bays -- for wear (a spear per kill, a tool per
        /// brick) and for arrows loosed. Returns what was drawn.
        float DrawHeld(string res, float amount)
        {
            if (amount <= 0f) return 0f;
            float left = DrawFrom(Store(res), amount);
            if (stations != null)
            {
                foreach (var s in stations) if (s != null && left > 0f) left = DrawFrom(s.Rack(res), left);
                foreach (var s in stations) if (s != null && left > 0f) left = DrawFrom(s.Bay(res), left);
            }
            return amount - left;
        }

        static float DrawFrom(OutpostStore s, float amount)
        {
            if (s == null || amount <= 0f) return amount;
            float have = s.whole + s.part;
            float got = Mathf.Min(have, amount);
            if (got <= 0f) return amount;
            have -= got;
            s.whole = Mathf.Max(0, Mathf.FloorToInt(have + 1e-5f));
            s.part = Mathf.Max(0f, have - s.whole);
            return amount - got;
        }

        // --- hauling ---------------------------------------------------------------

        public HaulView HaulOf(OutpostHand h)
        {
            if (h == null || !h.Hauling) return new HaulView { fromStation = -1, toStation = -1 };
            float total = Mathf.Max(Eps, h.haulDays);
            return new HaulView
            {
                active = true,
                resource = h.haulRes,
                count = h.haulCount,
                from = h.haulFrom,
                fromStation = h.haulFrom == HaulPlace.Station ? h.haulFromStation : -1,
                to = h.haulTo,
                toStation = h.haulTo == HaulPlace.Station ? h.haulToStation : -1,
                progress01 = Mathf.Clamp01(1f - h.haulLeft / total),
            };
        }

        /// Units of `res` in hands' arms right now. Not in `CountOf`.
        public int CarriedOf(string res)
        {
            int n = 0;
            if (hands != null)
                foreach (var h in hands)
                    if (h != null && h.Hauling && h.haulRes == res) n += h.haulCount;
            return n;
        }

        int InFlightTo(HaulPlace to, int station, string res)
        {
            int n = 0;
            if (hands == null) return 0;
            foreach (var h in hands)
            {
                if (h == null || !h.Hauling || h.haulRes != res || h.haulTo != to) continue;
                if (to == HaulPlace.Station && h.haulToStation != station) continue;
                n += h.haulCount;
            }
            return n;
        }

        /// Store room for `res`, net of loads already walking there.
        int StoreRoomNet(string res) =>
            Mathf.Max(0, RoomFor(res) - InFlightTo(HaulPlace.Store, -1, res));

        void StartTrip(OutpostHand h, string res, int n, HaulPlace from, int fromStation,
            HaulPlace to, int toStation, float tripDays)
        {
            h.haulRes = res;
            h.haulCount = n;
            h.haulFrom = from;
            h.haulFromStation = fromStation;
            h.haulTo = to;
            h.haulToStation = toStation;
            h.haulDays = Mathf.Max(Eps, tripDays);
            h.haulLeft = h.haulDays;
        }

        /// Put the load down where it was going (the store if that station
        /// is gone). Never drops anything.
        void DepositHaul(OutpostHand h)
        {
            if (h == null || !h.Hauling) { if (h != null) ClearHaul(h); return; }
            StationStock dest = null;
            if (h.haulTo == HaulPlace.Station && stations != null
                && h.haulToStation >= 0 && h.haulToStation < stations.Count)
                dest = stations[h.haulToStation];
            if (dest != null) dest.Bay(h.haulRes, true).whole += h.haulCount;
            else Store(h.haulRes, true).whole += h.haulCount;
            ClearHaul(h);
        }

        static void ClearHaul(OutpostHand h)
        {
            h.haulRes = "";
            h.haulCount = 0;
            h.haulFrom = HaulPlace.None;
            h.haulTo = HaulPlace.None;
            h.haulFromStation = -1;
            h.haulToStation = -1;
            h.haulLeft = 0f;
            h.haulDays = 0f;
        }

        void FlushAllHauls()
        {
            if (hands == null) return;
            foreach (var h in hands) if (h != null && h.Hauling) DepositHaul(h);
        }

        void AdvanceHaul(OutpostHand h, ref float budget)
        {
            float d = Mathf.Min(budget, h.haulLeft);
            h.haulLeft -= d;
            budget -= d;
            if (h.haulLeft <= Eps) DepositHaul(h);
        }

        /// A job an idle hand (or a gatherer whose store is full) could do.
        struct Chore
        {
            public string res;
            public int n;
            public OutpostStore source;
            public HaulPlace from, to;
            public int fromStation, toStation;
        }

        /// **Idle hauling, in priority order**: fill the bays of stations
        /// with an active order from the store; then empty racks into the
        /// store; then carry bay stock no order wants back to the store.
        bool FindHaulerChore(out Chore c)
        {
            c = default;
            if (stations == null || stations.Count == 0) return false;
            for (int i = 0; i < stations.Count; i++)
            {
                var s = stations[i];
                var r = s?.OrderRecipe;
                if (r == null) continue;
                foreach (var line in r.takes)
                {
                    if (line.n <= 0) continue;
                    int space = s.InputCap - s.BayCount(line.res) - InFlightTo(HaulPlace.Station, i, line.res);
                    int inStore = StoreCountOf(line.res);
                    if (space <= 0 || inStore <= 0) continue;
                    c = new Chore
                    {
                        res = line.res, n = Mathf.Min(Res.Armful(line.res), Mathf.Min(space, inStore)),
                        source = Store(line.res), from = HaulPlace.Store, fromStation = -1,
                        to = HaulPlace.Station, toStation = i,
                    };
                    return true;
                }
            }
            for (int i = 0; i < stations.Count; i++)
                if (RackChore(i, out c)) return true;
            for (int i = 0; i < stations.Count; i++)
            {
                var s = stations[i];
                if (s == null || s.bay == null) continue;
                var r = s.OrderRecipe;
                foreach (var row in s.bay)
                {
                    if (row == null || row.whole <= 0 || Wants(r, row.resource)) continue;
                    int room = StoreRoomNet(row.resource);
                    if (room <= 0) continue;
                    c = new Chore
                    {
                        res = row.resource, n = Mathf.Min(Res.Armful(row.resource), Mathf.Min(row.whole, room)),
                        source = row, from = HaulPlace.Station, fromStation = i,
                        to = HaulPlace.Store, toStation = -1,
                    };
                    return true;
                }
            }
            return false;
        }

        static bool Wants(Economy.Recipe r, string res)
        {
            if (r == null) return false;
            foreach (var line in r.takes) if (line.res == res) return true;
            return false;
        }

        /// Rack of station `i` into the store, if the store has room.
        bool RackChore(int i, out Chore c)
        {
            c = default;
            var s = stations[i];
            if (s == null || s.rack == null) return false;
            foreach (var row in s.rack)
            {
                if (row == null || row.whole <= 0) continue;
                int room = StoreRoomNet(row.resource);
                if (room <= 0) continue;
                c = new Chore
                {
                    res = row.resource, n = Mathf.Min(Res.Armful(row.resource), Mathf.Min(row.whole, room)),
                    source = row, from = HaulPlace.Station, fromStation = i,
                    to = HaulPlace.Store, toStation = -1,
                };
                return true;
            }
            return false;
        }

        void BeginChore(OutpostHand h, Chore c)
        {
            c.source.whole -= c.n;
            StartTrip(h, c.res, c.n, c.from, c.fromStation, c.to, c.toStation, StationTripDays);
        }

        /// Is there station hauling a spare hand could do right now?
        public bool HasHaulChore() => FindHaulerChore(out _);

        void HaulerDay(OutpostHand h, ref float budget)
        {
            for (int guard = 0; guard < 64 && budget > Eps; guard++)
            {
                if (h.Hauling) { AdvanceHaul(h, ref budget); continue; }
                if (!FindHaulerChore(out var c)) break;
                BeginChore(h, c);
            }
        }

        // --- the bench -------------------------------------------------------------

        static void EmptyBench(StationStock s)
        {
            s.benchState = BenchState.Empty;
            s.benchRecipe = "";
            s.benchProgress = 0f;
            s.benchOut = 0;
        }

        /// Move a finished job onto the rack as far as it has room. True when
        /// the bench is clear.
        static bool UnloadBench(StationStock s)
        {
            if (s.benchState != BenchState.Finished) return s.benchState == BenchState.Empty;
            string makes = s.BenchMakes;
            if (makes == null || s.benchOut <= 0) { EmptyBench(s); return true; }
            int move = Mathf.Min(s.RackRoom, s.benchOut);
            if (move > 0) { s.Rack(makes, true).whole += move; s.benchOut -= move; }
            if (s.benchOut <= 0) { EmptyBench(s); return true; }
            return false;
        }

        /// The order's next batch goes onto the bench, if every input is in
        /// the bay and the tool is somewhere in the camp.
        bool TryLoad(StationStock s)
        {
            if (s.benchState != BenchState.Empty || !s.HasOrder) return false;
            var r = s.OrderRecipe;
            if (r == null) { s.ClearOrder(); return false; }
            if (r.tool != null && HeldOf(r.tool) <= 0f) return false;
            foreach (var line in r.takes)
                if (line.n > 0 && s.BayCount(line.res) < line.n) return false;
            foreach (var line in r.takes)
                if (line.n > 0) s.Bay(line.res).whole -= line.n;
            s.benchRecipe = r.id;
            s.benchState = BenchState.Loaded;
            s.benchProgress = 0f;
            s.benchOut = 0;
            return true;
        }

        void FinishJob(StationStock s, Economy.Recipe r)
        {
            int yield = Mathf.Max(1, r.yield);
            s.benchState = BenchState.Finished;
            s.benchProgress = 1f;
            s.benchOut = yield;
            if (r.tool != null && r.toolWear > 0f) DrawHeld(r.tool, r.toolWear * yield);
            away.Add(r.makes, yield);
            if (!s.orderRepeat && s.orderRecipe == r.id)
            {
                s.orderLeft -= yield;
                if (s.orderLeft <= 0) s.ClearOrder();
            }
            UnloadBench(s);
        }

        /// **A stationed worker's day**: finish/unload the bench, load the
        /// next batch, work it; when the bench cannot go on, haul -- rack to
        /// store when it is full, raw from the store (or off the island, if
        /// the store has none and it is gatherable) into the bay, and the
        /// rack home when there is no more raw.
        void WorkerDay(OutpostHand h, StationStock s, int si, ref float budget)
        {
            for (int guard = 0; guard < 128 && budget > Eps; guard++)
            {
                if (h.Hauling) { AdvanceHaul(h, ref budget); continue; }

                if (s.benchState == BenchState.Finished) UnloadBench(s);

                if (s.benchState == BenchState.Loaded || s.benchState == BenchState.Working)
                {
                    var r = s.BenchRecipe;
                    if (r == null) { EmptyBench(s); continue; }   // recipe removed from the game
                    float rate = r.ratePerDay * Economy.Techs.RateMul(s.planId, LevelOf(s.planId))
                                 * PriorityMultiplier(r.makes);
                    if (rate <= 0f) break;
                    float perDay = rate / Mathf.Max(1, r.yield);        // bench progress per day
                    float need = (1f - s.benchProgress) / perDay;
                    s.benchState = BenchState.Working;
                    if (budget >= need - Eps)
                    {
                        budget -= Mathf.Min(budget, need);
                        FinishJob(s, r);
                    }
                    else
                    {
                        s.benchProgress += budget * perDay;
                        budget = 0f;
                    }
                    continue;
                }

                if (TryLoad(s)) continue;
                if (!StartWorkerChore(h, s, si)) break;
            }
        }

        bool StartWorkerChore(OutpostHand h, StationStock s, int si)
        {
            // The bench is blocked by a full rack: carry the rack home.
            if (s.benchState == BenchState.Finished)
            {
                if (RackChore(si, out var rc)) { BeginChore(h, rc); return true; }
                return false;
            }

            var r = s.OrderRecipe;
            if (r != null && (r.tool == null || HeldOf(r.tool) > 0f))
            {
                foreach (var line in r.takes)
                {
                    if (line.n <= 0) continue;
                    int have = s.BayCount(line.res) + InFlightTo(HaulPlace.Station, si, line.res);
                    if (have >= line.n) continue;
                    int space = s.InputCap - have;
                    if (space <= 0) continue;
                    int inStore = StoreCountOf(line.res);
                    if (inStore > 0)
                    {
                        int n = Mathf.Min(Res.Armful(line.res), Mathf.Min(space, inStore));
                        Store(line.res).whole -= n;
                        StartTrip(h, line.res, n, HaulPlace.Store, -1, HaulPlace.Station, si, StationTripDays);
                        return true;
                    }
                    // **The store has none: he gathers it himself** (Kevin's
                    // call) and his armful goes straight into his own bay.
                    if (Res.IsGatherable(line.res) && line.res != Res.Game)
                    {
                        var stock = Stock(line.res);
                        int standing = stock != null ? Mathf.FloorToInt(stock.standing) : 0;
                        if (standing > 0)
                        {
                            int n = Mathf.Min(Res.Armful(line.res), Mathf.Min(space, standing));
                            stock.standing -= n;
                            if (line.res == Res.Timber) timberTaken += n;
                            float days = StationTripDays + n / Mathf.Max(0.01f, Res.GatherRate(line.res));
                            StartTrip(h, line.res, n, HaulPlace.Field, -1, HaulPlace.Station, si, days);
                            return true;
                        }
                    }
                    break;                   // made elsewhere, or none left here
                }
            }

            // No more raw (no order, no tool, an input nobody can fetch), or
            // the inputs are already walking in: take the rack home.
            if (RackChore(si, out var home)) { BeginChore(h, home); return true; }
            return false;
        }

        /// A gatherer who cannot put another unit in the store.
        bool GatherBlocked(OutpostHand h)
        {
            if (h == null || h.order != OutpostOrder.Gather || string.IsNullOrEmpty(h.target)) return false;
            string into = h.target == Res.Game ? Res.Food : h.target;
            var st = Store(into);
            float room = ceilingPer - (st != null ? st.whole + st.part : 0f);
            return room <= 0f;
        }

        /// The station pass of `Step`: every stationed worker's day, every
        /// idle hand's (and store-blocked gatherer's) hauling, and any load
        /// in the arms of a hand whose job changed put down at once.
        void StepStations(float days, bool gatherersHaul)
        {
            if (hands == null) return;
            foreach (var h in hands)
            {
                if (h == null) continue;
                float budget = days * WorkFactor(h);
                if (h.order == OutpostOrder.Work && IsStation(h.target))
                {
                    var s = StationOfHand(h);
                    if (s == null) { if (h.Hauling) DepositHaul(h); continue; }
                    WorkerDay(h, s, stations.IndexOf(s), ref budget);
                }
                else if (h.order == OutpostOrder.Idle
                         || (gatherersHaul && GatherBlocked(h)))
                {
                    HaulerDay(h, ref budget);
                }
                else if (h.Hauling)
                {
                    DepositHaul(h);
                }
            }
        }

        // --- stall reasons -----------------------------------------------------

        string GatherFullReason(OutpostHand h)
        {
            string into = h.target == Res.Game ? Res.Food : h.target;
            string head = $"store is full of {Friendly(into)}";
            if (HasHaulChore()) return head + ", hauling for the stations";
            if (Focus != null) return head + ", helping build";
            return head;
        }

        string StationStallCause(OutpostHand h)
        {
            var s = StationOfHand(h);
            if (s == null) return "the building is not standing";
            if (s.benchState == BenchState.Loaded || s.benchState == BenchState.Working) return null;
            if (s.benchState == BenchState.Finished)
            {
                string makes = s.BenchMakes;
                return s.RackFull && StoreRoomNet(makes) <= 0
                    ? $"rack and store are full of {Friendly(makes)}" : null;
            }
            var r = s.OrderRecipe;
            // Checked before the haul: a worker carrying his rack home with
            // no order is still a station with nothing to make.
            if (r == null) return "no order given";
            if (h.Hauling) return null;
            if (r.tool != null && HeldOf(r.tool) <= 0f) return $"needs a {Friendly(r.tool)} in the pile";
            int si = stations.IndexOf(s);
            foreach (var line in r.takes)
            {
                if (line.n <= 0) continue;
                if (s.BayCount(line.res) + InFlightTo(HaulPlace.Station, si, line.res) >= line.n) continue;
                if (StoreCountOf(line.res) > 0) return null;
                if (Res.IsGatherable(line.res))
                {
                    var stock = Stock(line.res);
                    if (stock != null && stock.standing >= 1f) return null;
                    return $"waiting for {Friendly(line.res)}: none left here";
                }
                var makers = Economy.Recipes.Making(line.res);
                if (makers.Count > 0)
                    return $"waiting for {Friendly(line.res)} (made at the {BuildPlans.Named(makers[0].station).label})";
                return $"waiting for {Friendly(line.res)}";
            }
            return null;
        }
    }
}
