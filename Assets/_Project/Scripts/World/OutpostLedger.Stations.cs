using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Playtest dials, 2026-09-23.** Numbers Kevin set for testing on the
    /// phone, not balance. Grep for `Playtest.` before a release.
    public static class Playtest
    {
        /// Seconds of game time to cut ONE log at the tree. Kevin: *"cutting
        /// logs should take 5 seconds for the purpose of playtesting."*
        /// Live since 2026-09-27: the tuning asset's `cutSecondsTimber`
        /// over FEEL's gather speed.
        public static float CutSecondsPerLog => Economy.EconomyTuning.CutSeconds(Res.Timber) / Economy.EconomyFeel.GatherSpeed;

        /// Seconds of builder time to take ONE tree off a building plot.
        /// Kevin, 2026-09-23: 5 s a tree. Read through
        /// `OutpostLedger.ClearTreeHandDays`. The tuning asset's
        /// `clearSecondsPerTree` since 2026-09-27.
        public static float ClearSecondsPerTree => Economy.EconomyTuning.ClearSecondsPerTree;

        /// Seconds of builder time to break ONE rock off a plot. **A guess,
        /// not Kevin's number**: the tree's 5 s scaled by the stone/timber
        /// cut ratio (`Res.GatherRate`: 4 / 2.5), i.e. what a unit of stone
        /// takes to quarry (`GatherSecondsPerUnit(Stone)` = 8 s). Ore rocks
        /// use it too. The tuning asset's `clearSecondsPerRock`.
        public static float ClearSecondsPerRock => Economy.EconomyTuning.ClearSecondsPerRock;
    }

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
    /// (armful per trip, each trip's time from its real distance -- see
    /// "trip timing" below), never driven by bodies -- D2 holds because each
    /// trip's time is fixed when it starts and its remainder is saved on the
    /// hand.
    ///
    /// **Trip timing (Kevin, 2026-09-23 phone playtest: *"storage trips
    /// should be based on the time it takes to walk to the storage (or wood
    /// source) and back, not some arbitrary timer. cutting logs should take
    /// 5 seconds"*).** Every trip -- station haul, site stocking, a stationed
    /// worker fetching his own raw, a gatherer's armful (below) -- starts at its DROP-OFF, walks to its
    /// PICKUP, cuts/quarries there if the pickup is the island (`Field`),
    /// and walks back:
    /// `seconds = 2 * leg / WalkMetresPerSecond + HandleSeconds
    ///            + (Field ? n * GatherSecondsPerUnit(res) : 0)`,
    /// `days = seconds / TimeOfDay.DayLength`. A leg is the straight line
    /// between the two ledger positions times `PathFactor` (1.15: the ledger
    /// has no A*, the bodies walk round things). Positions the ledger knows:
    /// the STORE (first Storage/Storehouse `raised` row, else the campfire's
    /// `raised` row, else the campfire's site, else the centre `Outpost`
    /// saved), a STATION (its `raised` row, by ordinal), a SITE (its x,z).
    /// The island's sources have no position in the books: `Outpost` saves
    /// the metres from the camp centre to the nearest standing tree / rock
    /// per resource (`sourceDistance`) whenever the camp is watched, and a
    /// Field leg uses that number from anywhere in camp (the town is within
    /// `Outpost.TownRadius`); never watched = `DefaultSourceMetres`. A leg
    /// with an unknown end = `DefaultLegMetres`. The route is saved on the
    /// hand at the start (`haulFromX/Z`, `haulToX/Z`, `haulWalkDays`,
    /// `haulWorkDays`) so the mime walks the leg the books paid for.
    ///
    /// **Gathering is trips too (Kevin, 2026-09-23, decision A).** A hand on
    /// a plain `Gather` order (anything but `Res.Game` -- hunting and the farm
    /// stay per-day rates) walks from the store to the source, cuts/picks an
    /// armful (`Res.Armful` x `GatherSecondsPerUnit`), walks back and puts
    /// it in the STORE: `HaulPlace.Field -> Store` through the same
    /// `StartTimedTrip`/`HaulOf` as every other trip (`GatherDay`). The
    /// field (`OutpostStock.standing`) loses the armful at pickup; the
    /// armful is capped by `RoomFor` (net of every load walking to the
    /// store), so the ceiling holds. No room: the rest of his day hauls for
    /// the stations or helps build (Kevin's full-store rule), and he
    /// resumes when room comes back. `Res.GatherRate` is no longer a per-day
    /// accrual for these -- only the cut-time ratio (`GatherSecondsPerUnit`).
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
    ///   to, toStation, progress01, placed, fromAt, toAt, walkOutEnd01, workEnd01,
    ///   totalSeconds}`; from/to are Store, Station (index), Site or Field. The
    ///   trip is: progress 0..walkOutEnd01 walk empty from `toAt` to `fromAt`,
    ///   ..workEnd01 cut/pick up at `fromAt`, ..1 carry back to `toAt`.
    ///   `fromAt` of a Field trip is the camp centre (the ledger has no tree):
    ///   the body picks its own tree and the leg length is `SourceMetres`.</item>
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

        // --- trip timing (2026-09-23; see the class doc) ---------------------

        /// Metres a second a villager walks. **The same number as
        /// `CampWorker.Speed` (2.6)** -- the body and the books must agree on
        /// how long a leg takes; change both together.
        public const float WalkMetresPerSecond = 2.6f;
        /// Straight line to walked path. The ledger has no A*; bodies route
        /// round huts and rocks (`CampPath`). A guess.
        public const float PathFactor = 1.15f;
        /// Seconds per trip for picking the load up (off a store pile, a
        /// rack, a bay -- or the cut armful off the ground) and putting it
        /// down. **3 -> 1, 2026-09-24** (Kevin, phone playtest: picking a
        /// log up at the store should take about a second): the walk is the
        /// cost, the handling is a second. Cutting at the source is a
        /// separate number (`GatherSecondsPerUnit` / `Playtest.CutSecondsPerLog`)
        /// and is untouched.
        public const float HandleSeconds = 1f;
        /// Metres from the camp centre to the nearest source of a resource
        /// for a camp nobody has ever watched (no `sourceDistance` row).
        public const float DefaultSourceMetres = 25f;
        /// A leg whose end the ledger cannot place (no centre saved, a
        /// station with no `raised` row -- a probe's hand-written ledger).
        public const float DefaultLegMetres = 12f;

        /// One row per gathered resource: straight-line metres from the camp
        /// centre to the nearest usable source, as `Outpost` last saw it.
        [System.Serializable]
        public class SourceDistance
        {
            public string resource;
            public float metres;
        }

        /// Saved. Written by `Outpost.CatchUp` while the camp is watched;
        /// an unwatched camp (and an old save: empty) uses what is here or
        /// `DefaultSourceMetres`.
        public List<SourceDistance> sourceDistance = new List<SourceDistance>();

        /// The camp centre as `Outpost` last saw it (`Outpost.CampCentre`).
        /// Saved; the store's position when neither a storage building nor
        /// the campfire has a row. False in an old save.
        public bool hasCentre;
        public float centreX, centreZ;

        public void SetCentre(Vector3 at) { hasCentre = true; centreX = at.x; centreZ = at.z; }

        /// Straight-line metres from the centre to the nearest `res` source.
        public float SourceMetres(string res)
        {
            if (sourceDistance != null)
                foreach (var d in sourceDistance)
                    if (d != null && d.resource == res) return Mathf.Max(0f, d.metres);
            return DefaultSourceMetres;
        }

        public void SetSourceMetres(string res, float metres)
        {
            if (string.IsNullOrEmpty(res)) return;
            if (sourceDistance == null) sourceDistance = new List<SourceDistance>();
            foreach (var d in sourceDistance)
                if (d != null && d.resource == res) { d.metres = Mathf.Max(0f, metres); return; }
            sourceDistance.Add(new SourceDistance { resource = res, metres = Mathf.Max(0f, metres) });
        }

        /// **Seconds to cut / quarry ONE unit at the source.** Timber is
        /// Kevin's playtest number (`Playtest.CutSecondsPerLog`); the rest
        /// keep `Res.GatherRate`'s relation to timber (stone 8 s, ore 10 s,
        /// spice 6.7 s), so the playtest speed-up is the same for all.
        public static float GatherSecondsPerUnit(string res) =>
            // 2026-09-27: per resource from the tuning asset (5/8/10/6.7 s),
            // over FEEL's gather speed.
            Economy.EconomyTuning.CutSeconds(res) / Economy.EconomyFeel.GatherSpeed;

        /// The camp centre: the campfire's `raised` row, its site, or the
        /// saved centre.
        public bool CentreAt(out Vector3 at)
        {
            at = default;
            string fire = BuildPlans.Campfire.id;
            if (raised != null)
                foreach (var r in raised)
                    if (r != null && r.planId == fire) { at = r.At; return true; }
            if (sites != null)
                foreach (var p in sites)
                    if (p != null && p.planId == fire) { at = new Vector3(p.x, 0f, p.z); return true; }
            if (hasCentre) { at = new Vector3(centreX, 0f, centreZ); return true; }
            return false;
        }

        /// Where the store is: inside the first Storage/Storehouse standing,
        /// else the square by the fire (the centre).
        public bool StoreAt(out Vector3 at)
        {
            if (raised != null)
                foreach (var r in raised)
                    if (r != null && (r.planId == BuildPlans.Storage.id || r.planId == BuildPlans.Storehouse.id))
                    { at = r.At; return true; }
            return CentreAt(out at);
        }

        /// Where station `index` stands: its plan's `raised` row of the same
        /// ordinal.
        public bool StationPlace(int index, out Vector3 at)
        {
            at = default;
            if (stations == null || index < 0 || index >= stations.Count || raised == null) return false;
            var s = stations[index];
            if (s == null) return false;
            int k = 0;
            foreach (var r in raised)
            {
                if (r == null || r.planId != s.planId) continue;
                if (k++ == s.ordinal) { at = r.At; return true; }
            }
            return false;
        }

        bool PlaceOf(HaulPlace place, int station, PendingBuild site, out Vector3 at)
        {
            at = default;
            switch (place)
            {
                case HaulPlace.Store: return StoreAt(out at);
                case HaulPlace.Station: return StationPlace(station, out at);
                case HaulPlace.Site:
                    if (site == null) return false;
                    at = new Vector3(site.x, 0f, site.z);
                    return true;
                case HaulPlace.Field: return CentreAt(out at);
                // The foot of the gangway (2026-09-24 transfers): the ship
                // end is the bound `ICargoSide`'s; none bound = unplaced, and
                // the leg falls back to `DefaultLegMetres`.
                case HaulPlace.Ship: return cargo != null && cargo.GangwayAt(out at);
            }
            return false;
        }

        /// Walked metres of one leg between pickup and drop-off.
        float LegMetres(string res, HaulPlace from, int fromStation, HaulPlace to, int toStation, PendingBuild site)
        {
            if (from == HaulPlace.Field) return SourceMetres(res) * PathFactor;
            if (PlaceOf(from, fromStation, site, out var a) && PlaceOf(to, toStation, site, out var b))
            {
                Vector3 d = b - a; d.y = 0f;
                return d.magnitude * PathFactor;
            }
            return DefaultLegMetres * PathFactor;
        }

        static float SecondsToDays(float seconds) => seconds / Mathf.Max(0.0001f, TimeOfDay.DayLength);

        /// **Start a trip whose time is its distance.** Walk from the
        /// drop-off to the pickup, cut `n` there if it is the island, pick
        /// up, walk back. The route and phases go on the hand (saved).
        void StartTimedTrip(OutpostHand h, string res, int n, HaulPlace from, int fromStation,
            HaulPlace to, int toStation, PendingBuild site = null, bool fromBay = false)
        {
            float leg = LegMetres(res, from, fromStation, to, toStation, site);
            float walk = leg / WalkMetresPerSecond;
            float work = HandleSeconds + (from == HaulPlace.Field ? n * GatherSecondsPerUnit(res) : 0f);
            StartTrip(h, res, n, from, fromStation, to, toStation, SecondsToDays(2f * walk + work), fromBay);
            h.haulWalkDays = SecondsToDays(walk);
            h.haulWorkDays = SecondsToDays(work);
            h.haulPlaced = PlaceOf(from, fromStation, site, out var fa) & PlaceOf(to, toStation, site, out var ta);
            h.haulFromX = fa.x; h.haulFromZ = fa.z;
            h.haulToX = ta.x; h.haulToZ = ta.z;
        }

        /// **Game-days of one trip** (for sheets and probes): the same
        /// arithmetic `StartTimedTrip` books.
        public float TripDays(string res, int n, HaulPlace from, int fromStation,
            HaulPlace to, int toStation, PendingBuild site = null)
        {
            float walk = LegMetres(res, from, fromStation, to, toStation, site) / WalkMetresPerSecond;
            float work = HandleSeconds + (from == HaulPlace.Field ? n * GatherSecondsPerUnit(res) : 0f);
            return SecondsToDays(2f * walk + work);
        }

        /// **Units a day one full-strength gatherer brings in** of `res`:
        /// an armful per gather trip (`Field -> Store`). What the readouts
        /// (`RatePerDay`, `MakeRatePerDay`) show in place of the old flat
        /// `Res.GatherRate`; it ignores the store filling up and the field
        /// running short, which `Stalled` covers.
        public float GatherTripPerDay(string res)
        {
            int n = Mathf.Max(1, Res.Armful(res));
            float d = TripDays(res, n, HaulPlace.Field, -1, HaulPlace.Store, -1);
            return d > 0f ? n / d : 0f;
        }

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

        /// **Is this row still a station of this camp?** False for null, for
        /// a row removed by `DemolishBuilt` / `EnsureStations` (its `removed`
        /// flag is set and it is emptied), and for a row this ledger's list
        /// no longer holds. A view that cached a `StationStock` asks this
        /// before drawing it.
        public bool IsLive(StationStock s) =>
            s != null && !s.removed && stations != null && stations.Contains(s);

        /// A station has a worker at it: Work hands on a plan are dealt round
        /// its instances in hand-list order (`StationOfHand`), so the nth
        /// instance is manned when more than n hands work that plan.
        public bool Manned(StationStock s) =>
            s != null && !s.removed && HandsOn(OutpostOrder.Work, s.planId) > s.ordinal;

        /// **A building came down (Outpost.Demolish).** Takes the plan's one
        /// `built` id and, when `raisedIndex` is a row of it, that `raised`
        /// row out -- and with it the station row THAT building was (its
        /// ordinal is its place among the plan's `raised` rows), spilled into
        /// the store and marked dead. Higher ordinals of the plan shift down
        /// one, so every surviving station keeps its own stock. A
        /// `raisedIndex` of -1 takes the plan's last instance.
        public void DemolishBuilt(string planId, int raisedIndex)
        {
            if (string.IsNullOrEmpty(planId)) return;
            if (built == null) built = new List<string>();
            if (raised == null || raisedIndex < 0 || raisedIndex >= raised.Count
                || raised[raisedIndex] == null || raised[raisedIndex].planId != planId)
                raisedIndex = -1;
            if (IsStation(planId) && built.Contains(planId))
            {
                EnsureStations();
                int n = CountBuilt(planId);
                int ordinal = n - 1;
                if (raisedIndex >= 0)
                {
                    ordinal = 0;
                    for (int i = 0; i < raisedIndex; i++)
                        if (raised[i] != null && raised[i].planId == planId) ordinal++;
                    if (ordinal >= n) ordinal = n - 1;
                }
                int idx = StationIndex(planId, ordinal);
                if (idx >= 0)
                {
                    FlushAllHauls();         // station indices are about to shift
                    var gone = stations[idx];
                    SpillStation(gone);
                    KillStation(gone);
                    stations.RemoveAt(idx);
                    foreach (var o in stations)
                        if (o != null && o.planId == planId && o.ordinal > ordinal) o.ordinal--;
                }
            }
            built.Remove(planId);
            if (raisedIndex >= 0) raised.RemoveAt(raisedIndex);
        }

        /// Empty a removed row and flag it, so a view holding it sees a dead
        /// station, never stock that has already gone back to the store.
        static void KillStation(StationStock s)
        {
            if (s == null) return;
            s.removed = true;
            if (s.bay != null) s.bay.Clear();
            if (s.rack != null) s.rack.Clear();
            EmptyBench(s);
            s.ClearOrder();
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
                    KillStation(stations[i]);
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
                    // Only a MANNED instance: with more sawmills than sawyers
                    // the spare ones stay order-less instead of pulling stock
                    // into bays nobody works.
                    if (s == null || s.HasOrder || !Manned(s)) continue;
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
            // Per building since 2026-09-27: a level-2 recipe wants THIS
            // copy at level 2, not merely some copy of the plan.
            if (LevelOf(s.planId, s.ordinal) < r.stationLevel) return false;
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

        /// Whole units the camp may SPEND out of its stations: racks and
        /// finished benches (what `TakeFromStations` draws), never bays.
        int StationSpendableOf(string res)
        {
            int n = 0;
            if (stations != null) foreach (var s in stations) if (s != null) n += s.SpendableOf(res);
            return n;
        }

        int StationCountOf(string res)
        {
            int n = 0;
            if (stations != null) foreach (var s in stations) if (s != null) n += s.CountOf(res);
            return n;
        }

        /// Whole and part of `res` in racks and finished benches -- the
        /// station half of `HeldOf`, which (like `DrawHeld`) leaves bays alone.
        float StationHeldOf(string res)
        {
            float n = 0f;
            if (stations != null) foreach (var s in stations) if (s != null) n += s.SpendableHeldOf(res);
            return n;
        }

        int StationTotal()
        {
            int n = 0;
            if (stations != null) foreach (var s in stations) if (s != null) n += s.Total;
            return n;
        }

        /// Whole units out of the stations after the store ran short:
        /// racks, then finished benches. **Never bays** -- a bay is a
        /// station's queued input, not the camp's to spend.
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
            return got;
        }

        /// **Take a FRACTION of `res` from wherever the camp may spend it** --
        /// store, then racks (never bays, same as `Take`) -- for wear (a spear per kill, a tool per
        /// brick) and for arrows loosed. Returns what was drawn.
        float DrawHeld(string res, float amount)
        {
            if (amount <= 0f) return 0f;
            float left = DrawFrom(Store(res), amount);
            if (stations != null)
            {
                foreach (var s in stations) if (s != null && left > 0f) left = DrawFrom(s.Rack(res), left);
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
                placed = h.haulPlaced,
                fromAt = new Vector3(h.haulFromX, 0f, h.haulFromZ),
                toAt = new Vector3(h.haulToX, 0f, h.haulToZ),
                walkOutEnd01 = Mathf.Clamp01(h.haulWalkDays / total),
                workEnd01 = Mathf.Clamp01((h.haulWalkDays + h.haulWorkDays) / total),
                totalSeconds = h.haulDays * TimeOfDay.DayLength,
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

        /// Store room for `res`, net of loads already walking there
        /// (`RoomFor` reserves them).
        int StoreRoomNet(string res) => RoomFor(res);

        /// Fractional store room for gathering: the ceiling less the pile
        /// (whole and part) less every load walking to the store, so a
        /// gatherer never fills room a hauler is already carrying into.
        float StoreRoomF(string res)
        {
            // **Whole units first, then the fraction** -- the exact order the
            // gather loop used before this helper existed. `whole + part`
            // summed first rounds 9 + 0.99999 up to 10.0 in float, the room
            // reads 0, and the last unit never tips over into `whole`: the
            // pile sticks at 9 of 10 forever (LedgerProbe
            // `each-resource-has-its-own-ceiling`, 2026-09-23).
            var st = Store(res);
            int whole = st != null ? st.whole : 0;
            float part = st != null ? st.part : 0f;
            return (ceilingPer - whole - InFlightTo(HaulPlace.Store, -1, res)) - part;
        }

        void StartTrip(OutpostHand h, string res, int n, HaulPlace from, int fromStation,
            HaulPlace to, int toStation, float tripDays, bool fromBay = false)
        {
            h.haulSerial++;
            h.haulFromBay = fromBay;
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
        /// is gone). Never drops anything. **The store's ceiling holds**: a
        /// store-bound load that no longer fits (something else filled the
        /// room) puts down what fits, goes back to the bay or rack it came
        /// from as far as that has room, and the rest stays in the hand's
        /// arms (`haulLeft` 0, retried every step). `force` (the hand is
        /// leaving the camp, or the station is being torn down) puts it all
        /// in the store, over the ceiling if need be -- it exists.
        void DepositHaul(OutpostHand h, bool force = false)
        {
            if (h == null || !h.Hauling) { if (h != null) ClearHaul(h); return; }
            // A hunt trip (2026-09-27): a killed carcass lands at the store
            // as meat and hide, an unkilled one was never there.
            if (h.HuntTrip) { DepositCarcass(h); return; }
            // **A builder's armful into a blueprint** (2026-09-23): through
            // `DeliverToSite`, so it fills the oldest site short of it up to
            // its need and any surplus (a visitor beat him to it, the site
            // was cancelled) goes on the pile -- never past a need, never lost.
            if (h.haulTo == HaulPlace.Site)
            {
                if (h.haulFrom == HaulPlace.Field && h.haulRes == Res.Timber) timberTaken += h.haulCount;
                DeliverToSite(h.haulRes, h.haulCount);
                ClearHaul(h);
                return;
            }
            // **A store -> ship armful** (2026-09-24): into the hold as far as
            // she takes it, the rest back to the store -- see
            // `OutpostLedger.Transfers`.
            if (h.haulTo == HaulPlace.Ship) { DepositToShip(h, force); return; }
            StationStock dest = null;
            if (h.haulTo == HaulPlace.Station && stations != null
                && h.haulToStation >= 0 && h.haulToStation < stations.Count)
                dest = stations[h.haulToStation];
            if (dest != null) { dest.Bay(h.haulRes, true).whole += h.haulCount; ClearHaul(h); return; }

            var st = Store(h.haulRes, true);
            int put = force ? h.haulCount : Mathf.Clamp(ceilingPer - st.whole, 0, h.haulCount);
            st.whole += put;
            h.haulCount -= put;
            if (put > 0 && h.haulFrom == HaulPlace.Field && h.haulTo == HaulPlace.Store)
            {
                // **A gatherer's armful lands** (2026-09-23): its trees go
                // over now, as a builder's do (the body cut through the
                // trip), and it is what the camp gathered while away. Booked
                // per unit put down, so a load that waits at a full store is
                // booked once, as it goes in.
                if (h.haulRes == Res.Timber) timberTaken += put;
                away.Add(h.haulRes, put);
            }
            if (h.haulFrom == HaulPlace.Ship)
            {
                // **A ship -> store armful** (2026-09-24): what went in is
                // landed; what did not goes back aboard while she is still
                // here (and back on the order), else it waits in his arms
                // at the store like any store-bound load.
                transferredAshore += put;
                if (h.haulCount > 0 && ShipHere)
                {
                    int back = Mathf.Clamp(cargo.Give(h.haulRes, h.haulCount), 0, h.haulCount);
                    h.haulCount -= back;
                    ReturnToOrder(h.haulRes, false, back);
                }
            }
            if (h.haulCount > 0 && h.haulFrom == HaulPlace.Station && stations != null
                && h.haulFromStation >= 0 && h.haulFromStation < stations.Count)
            {
                var src = stations[h.haulFromStation];
                int back = h.haulFromBay
                    ? src.InputCap - src.BayCount(h.haulRes) - InFlightTo(HaulPlace.Station, h.haulFromStation, h.haulRes)
                    : src.RackRoom;
                back = Mathf.Clamp(back, 0, h.haulCount);
                if (back > 0)
                {
                    var row = h.haulFromBay ? src.Bay(h.haulRes, true) : src.Rack(h.haulRes, true);
                    row.whole += back;
                    h.haulCount -= back;
                }
            }
            if (h.haulCount <= 0) { ClearHaul(h); return; }
            h.haulLeft = 0f;                 // at the store, waiting for room
        }

        /// A hand standing at a full store with a load it cannot put down.
        static bool WaitingAtStore(OutpostHand h) => h.Hauling && h.haulLeft <= Eps;

        /// **A hand leaves the camp's books** (recalled aboard, carried to a
        /// berth): whatever is in their arms is put down first -- at its
        /// destination, or the store over the ceiling if need be -- so the
        /// armful never leaves with them. Returns whether the hand was on
        /// the list.
        public bool RemoveHand(OutpostHand h)
        {
            if (h == null || hands == null) return false;
            if (h.Hauling) DepositHaul(h, true);
            return hands.Remove(h);
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
            h.haulFromBay = false;
            h.haulPlaced = false;
            h.haulWalkDays = h.haulWorkDays = 0f;
            h.huntKilled = h.huntArmed = false;
            h.haulFromX = h.haulFromZ = h.haulToX = h.haulToZ = 0f;
        }

        /// Every load put down now, before station indices shift.
        void FlushAllHauls()
        {
            if (hands == null) return;
            foreach (var h in hands) if (h != null && h.Hauling) DepositHaul(h, true);
        }

        /// False when the hand is stuck at a full store: its day stops there.
        bool AdvanceHaul(OutpostHand h, ref float budget)
        {
            float d = Mathf.Min(budget, h.haulLeft);
            h.haulLeft -= d;
            budget -= d;
            if (h.haulLeft <= Eps) DepositHaul(h);
            return !WaitingAtStore(h);
        }

        /// A job an idle hand (or a gatherer whose store is full) could do.
        struct Chore
        {
            public string res;
            public int n;
            public OutpostStore source;
            public HaulPlace from, to;
            public int fromStation, toStation;
            public bool fromBay;
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
                // Only a station somebody works: an unmanned bay would lock
                // the stock away from the builders and the costs.
                if (r == null || !Manned(s)) continue;
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
                // An unmanned station's order wants nothing: its bay goes home.
                var r = Manned(s) ? s.OrderRecipe : null;
                foreach (var row in s.bay)
                {
                    if (row == null || row.whole <= 0 || Wants(r, row.resource)) continue;
                    int room = StoreRoomNet(row.resource);
                    if (room <= 0) continue;
                    c = new Chore
                    {
                        res = row.resource, n = Mathf.Min(Res.Armful(row.resource), Mathf.Min(row.whole, room)),
                        source = row, from = HaulPlace.Station, fromStation = i,
                        to = HaulPlace.Store, toStation = -1, fromBay = true,
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
            StartTimedTrip(h, c.res, c.n, c.from, c.fromStation, c.to, c.toStation, null, c.fromBay);
        }

        /// Is there station hauling a spare hand could do right now?
        public bool HasHaulChore() => FindHaulerChore(out _);

        void HaulerDay(OutpostHand h, ref float budget)
        {
            for (int guard = 0; guard < 64 && budget > Eps; guard++)
            {
                if (h.Hauling) { if (!AdvanceHaul(h, ref budget)) break; continue; }
                // The player's transfer orders first (2026-09-24): Kevin
                // asked for them; the station hauling is background.
                if (StartTransferTrip(h)) continue;
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
                if (h.Hauling) { if (!AdvanceHaul(h, ref budget)) break; continue; }

                if (s.benchState == BenchState.Finished) UnloadBench(s);

                if (s.benchState == BenchState.Loaded || s.benchState == BenchState.Working)
                {
                    var r = s.BenchRecipe;
                    if (r == null) { EmptyBench(s); continue; }   // recipe removed from the game
                    float rate = r.ratePerDay * Economy.Techs.RateMul(s.planId, LevelOf(s.planId, s.ordinal))
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
                        StartTimedTrip(h, line.res, n, HaulPlace.Store, -1, HaulPlace.Station, si);
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
                            StartTimedTrip(h, line.res, n, HaulPlace.Field, -1, HaulPlace.Station, si);
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

        /// A gatherer who cannot put another unit in the store. A trip
        /// gatherer walking home with his own armful is not blocked -- that
        /// armful already holds its room (`RoomFor` counts it) -- unless it
        /// is standing at a store something else filled.
        bool GatherBlocked(OutpostHand h)
        {
            if (h == null || h.order != OutpostOrder.Gather || string.IsNullOrEmpty(h.target)) return false;
            // A carcass is meat and hide: full only when neither fits (2026-09-26).
            if (h.target == Res.Game) return !h.HuntTrip && HuntStoreFull();
            if (h.Hauling && h.haulFrom == HaulPlace.Field && h.haulTo == HaulPlace.Store
                && !WaitingAtStore(h)) return false;
            return RoomFor(h.target) <= 0;
        }

        /// **A gatherer who works by trips** (everyone on a Gather order;
        /// the hunter too since 2026-09-27): `GatherDay` spends his whole
        /// quantum, whatever is in his arms, so the builder and station
        /// passes leave him alone.
        static bool TripGatherer(OutpostHand h) =>
            h != null && h.order == OutpostOrder.Gather && !string.IsNullOrEmpty(h.target);

        /// One gather trip: from the store out to the source, an armful cut
        /// there, back into the store. The armful is the least of
        /// `Res.Armful`, the store's room net of every load walking to it,
        /// and what stands in the field -- which gives it up now, at pickup.
        /// False when the store is full or the field is bare.
        bool StartGatherTrip(OutpostHand h)
        {
            string res = h.target;
            var stock = Stock(res);
            int standing = stock != null ? Mathf.FloorToInt(stock.standing + 1e-4f) : 0;
            int n = Mathf.Min(Res.Armful(res), Mathf.Min(RoomFor(res), standing));
            if (n <= 0) return false;
            stock.standing = Mathf.Max(0f, stock.standing - n);
            StartTimedTrip(h, res, n, HaulPlace.Field, -1, HaulPlace.Store, -1);
            return true;
        }

        /// **A gatherer's quantum, by trips (Kevin, 2026-09-23).** Finish
        /// whatever is in his arms (his own armful, or a haul/site load from
        /// helping), then trip after trip while the store has room and the
        /// field has stock. With the store full, the rest of the quantum
        /// helps: builds when `helpBuild` (no station hauling wanted and a
        /// site in the queue -- `Step` decides once per quantum), else hauls
        /// for the stations. A bare field just stops him ("nothing left to
        /// cut here"). The work clock is scaled as the old rate was
        /// (`WorkFactorOn` + `PriorityMultiplier` of what he gathers); the
        /// help is paid at plain `WorkFactor`, like any builder or hauler.
        void GatherDay(OutpostHand h, float days, bool helpBuild)
        {
            if (h.target == Res.Game) { HuntDay(h, days, helpBuild); return; }
            // Re-ordered off the hunt mid-trip: the carcass (if any) lands now.
            if (h.HuntTrip) DepositCarcass(h);
            float scale = WorkFactorOn(h, h.target) * PriorityMultiplier(h.target);
            float budget = days * scale;
            for (int guard = 0; guard < 64 && budget > Eps; guard++)
            {
                if (h.Hauling) { if (!AdvanceHaul(h, ref budget)) return; continue; }
                if (!StartGatherTrip(h)) break;
            }
            if (budget <= Eps || h.Hauling || RoomFor(h.target) > 0 || scale <= 0f) return;
            float help = budget / scale * WorkFactor(h);
            if (helpBuild)
            {
                if (sites != null) BuilderDay(h, ref help);
                if (help > Eps && !h.Hauling) TransferDay(h, ref help);
            }
            else HaulerDay(h, ref help);
        }

        /// The station pass of `Step`: every stationed worker's day, every
        /// idle hand's (and store-blocked hunter's) hauling, and any load
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
                else if (TripGatherer(h))
                {
                    // `GatherDay` already spent his quantum, arms and all.
                }
                else if (h.order == OutpostOrder.Idle
                         || (gatherersHaul && GatherBlocked(h)))
                {
                    HaulerDay(h, ref budget);
                }
                else if (h.Hauling && h.haulTo != HaulPlace.Site
                         && !(IsTransferHaul(h) && builderScratch.Contains(h)))
                {
                    // (A builder's site load is the builder pass's own, and
                    // so is a transfer armful a builder is walking.)
                    DepositHaul(h);
                }
            }
        }

        // --- stall reasons -----------------------------------------------------

        string GatherFullReason(OutpostHand h)
        {
            string into = h.target == Res.Game ? Res.Food : h.target;
            string head = h.target == Res.Game
                ? $"store is full of {Friendly(Res.Food)} and {Friendly(Res.Hide)}"
                : $"store is full of {Friendly(into)}";
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
