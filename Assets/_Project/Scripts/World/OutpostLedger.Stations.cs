using System.Collections.Generic;
using UnityEngine;
using SeaSick.World.Life;

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
    /// `days = seconds / TimeOfDay.WorkDaySeconds`. A leg is the straight line
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
    /// **Since 2026-09-30 (station spots, `OutpostLedger.Spots.cs`)** the bench
    ///   and the order are per SPOT: `StationStock.Spots` (`SpotState`: recipe,
    ///   progress, pause reason, seconds left); the single-bench and order
    ///   fields above are a mirror of the shown spot. The screen's write API
    ///   is `SelectRecipe(station, spot, recipe, out refusal)` / `StopSpot`.
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

        /// Metres a second a villager walks. **The body's own pace**
        /// (2026-10-01: `VillagerGaits.Books`, out brisk and back carrying,
        /// ~0.93 m/s at the 1.5x cadence; it was 2.6 while the feet skated) -- the body and the
        /// books must agree on how long a leg takes.
        public static readonly float WalkMetresPerSecond = VillagerGaits.Books;
        /// **This hand's walking speed in the books** (2026-10-03, Kevin: "make
        /// sure that the runners are a lot faster than the normal villager"):
        /// a runner jogs behind his barrow, loaded or empty, at
        /// `VillagerGaits.Barrow` (his body's own `CruiseSpeed`, so a watched
        /// and an unwatched camp keep the same pace); everybody else at
        /// `WalkMetresPerSecond`.
        public static float WalkSpeedOf(OutpostHand h) => IsRunner(h) ? VillagerGaits.Barrow : WalkMetresPerSecond;
        /// Straight line to walked path. **Unused by the books since
        /// 2026-09-27** (legs are measured on `CampPath` through `router`,
        /// or walked straight); kept for old callers.
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
                case HaulPlace.Shore: return ShoreOf(station, out at, out _);
                case HaulPlace.Site:
                    if (site == null) return false;
                    at = new Vector3(site.x, 0f, site.z);
                    return true;
                case HaulPlace.Field: return CentreAt(out at);
                // The foot of the gangway (2026-09-24 transfers): the ship
                // end is the bound `ICargoSide`'s; none bound = unplaced, and
                // the leg falls back to `DefaultLegMetres`.
                case HaulPlace.Ship: return cargo != null && cargo.GangwayAt(out at);
                case HaulPlace.Ground:
                    if (groundLoads == null || station < 0 || station >= groundLoads.Count) return false;
                    var gl = groundLoads[station];
                    if (gl == null) return false;
                    at = new Vector3(gl.x, 0f, gl.z);
                    return true;
            }
            return false;
        }

        // --- dropped ground loads (death/rescue phase 2, 2026-09-27) --------
        //
        // docs/DELIVERY-ON-ARRIVAL.md's rule extended to a hand who goes
        // down or dies: "only when a villager has delivered the object will
        // the object be counted" -- a load he had picked up is just as
        // physical lying in the grass as it is on his shoulders. One row
        // per drop; the FIRST rung of the idle-hand ladder (`FindHaulerChore`)
        // sends an idle hand to walk it home, the ordinary way (`HaulPlace.
        // Ground`, an index into this list, exactly the way `Station`'s
        // index is into `Stations`).

        /// One load on the ground, saved. `claimed` mirrors the pattern
        /// every other source uses (`Claimed`): true once a hand is walking
        /// to fetch it, so two hands never go for the same pile.
        [System.Serializable]
        public class GroundLoad
        {
            public string res;
            public int count;
            public float x, z;
        }

        /// Saved. Old saves: empty (JsonUtility-safe).
        public List<GroundLoad> groundLoads = new List<GroundLoad>();

        void AddGroundLoad(string res, int count, Vector3 at)
        {
            if (string.IsNullOrEmpty(res) || count <= 0) return;
            if (groundLoads == null) groundLoads = new List<GroundLoad>();
            groundLoads.Add(new GroundLoad { res = res, count = count, x = at.x, z = at.z });
        }

        /// Metres of one leg between pickup and drop-off, **for the display
        /// estimates only** (`TripDays`). Since 2026-09-27 the straight line
        /// -- exactly what an invisible walker with no path grid walks -- so
        /// a forecast is never slower than the real thing without a route.
        float LegMetres(string res, HaulPlace from, int fromStation, HaulPlace to, int toStation, PendingBuild site)
        {
            if (from == HaulPlace.Field) return SourceMetres(res);
            if (PlaceOf(from, fromStation, site, out var a) && PlaceOf(to, toStation, site, out var b))
            {
                Vector3 d = b - a; d.y = 0f;
                return d.magnitude;
            }
            return DefaultLegMetres;
        }

        static float SecondsToDays(float seconds) => seconds / TimeOfDay.WorkDaySeconds;

        /// **Game-days of one trip -- a DISPLAY estimate only** (2026-09-27:
        /// nothing books through this; trips are walked, docs/DELIVERY-ON-ARRIVAL.md).
        public float TripDays(string res, int n, HaulPlace from, int fromStation,
            HaulPlace to, int toStation, PendingBuild site = null)
        {
            float walk = LegMetres(res, from, fromStation, to, toStation, site) / WalkMetresPerSecond;
            float work = HandleSeconds + (from == HaulPlace.Field ? n * GatherSecondsPerUnit(res) : 0f);
            return SecondsToDays(2f * walk + work);
        }

        /// **DISPLAY estimate only (2026-09-27)** -- nothing books through it;
        /// what really came in is `DeliveredPerDay`.
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

        /// **A station has a worker at it**: some Work hand's `StationOfHand`
        /// is this row.
        ///
        /// 2026-10-03 (review fix B): this used to be "more than `ordinal`
        /// hands work the plan", which ignored `workPin`. Two sawmills, A
        /// pinned to copy 0 and B to copy 1; A moved to the farm -> one hand
        /// left, so copy 0 read manned and copy 1 (where B really stands)
        /// unmanned: runners never fed B's bay and he waited forever. Now
        /// the very deal `OrdinalOfHand` + `StationOfHand` make (pins first,
        /// the unpinned dealt round the copies in hand-list order, then
        /// `% stations`), walked once here so it stays O(hands) -- the
        /// runners' ladder asks this per station per input.
        public bool Manned(StationStock s)
        {
            if (s == null || s.removed || hands == null || !IsStation(s.planId)) return false;
            int copies = CountBuilt(s.planId);
            int rows = StationCountOfPlan(s.planId);
            if (copies <= 0 || rows <= 0) return false;
            int k = 0;                       // unpinned hands dealt so far
            foreach (var x in hands)
            {
                if (x == null || x.order != OutpostOrder.Work || x.target != s.planId) continue;
                bool pinned = x.workPin > 0 && x.workPin <= copies;
                int o = pinned ? x.workPin - 1 : (k++ % copies);
                if (o % rows == s.ordinal) return true;
            }
            return false;
        }

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
            // Pins follow the copies (2026-09-28): a hand pinned to the copy
            // that went is dealt again, later copies move down one.
            {
                int count = CountBuilt(planId);
                int gone = count - 1;
                if (raisedIndex >= 0)
                {
                    gone = 0;
                    for (int i = 0; i < raisedIndex; i++)
                        if (raised[i] != null && raised[i].planId == planId) gone++;
                }
                if (count > 0 && hands != null)
                    foreach (var x in hands)
                    {
                        if (x == null || x.target != planId || x.workPin <= 0) continue;
                        if (x.workPin - 1 == gone) x.workPin = 0;
                        else if (x.workPin - 1 > gone) x.workPin--;
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
            s.SyncLegacy();
        }

        /// The station instance a Work hand stands at: Work hands on one plan
        /// are dealt round the plan's instances in hand-list order.
        public StationStock StationOfHand(OutpostHand h)
        {
            if (h == null || h.order != OutpostOrder.Work || !IsStation(h.target)) return null;
            int n = StationCountOfPlan(h.target);
            if (n <= 0) return null;
            // One deal for the books and the body (`OrdinalOfHand`, which
            // `Outpost.WorkplaceOf` also reads), pins included (2026-09-28).
            int o = OrdinalOfHand(h);
            return StationOf(h.target, (o < 0 ? 0 : o) % n);
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

            // One row per spot (2026-09-30); an old save's bench, order and
            // queue move onto their spots here, once.
            foreach (var s in stations) s?.EnsureSpotRows();

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
                    var sp = s.SpotAt(StationSpots.SpotIndexOf(r));
                    if (r == null || sp == null) continue;
                    sp.recipeId = r.id;
                    sp.count = 0;
                    s.SyncLegacy();
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
            // Every spot's bench (2026-09-30).
            s.EnsureSpotRows();
            foreach (var sp in s.spots)
            {
                var rec = sp?.BenchRecipe;
                if (rec == null) continue;
                if (sp.benchState == BenchState.Finished && sp.benchOut > 0)
                    Store(rec.makes, true).whole += sp.benchOut;
                else if (sp.BenchBusy)
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
            // **Spots, 2026-09-30**: the order lands on the recipe's own
            // spot (the station's only one, bar the kitchen and the forge);
            // repeat = until stopped, a count runs down and clears the spot.
            return SetSpot(s, StationSpots.SpotIndexOf(r), r.id, count < 0 ? 0 : count, out _);
        }

        public bool PlaceOrder(string planId, string recipeId, int count, int ordinal = 0)
        {
            EnsureStations();
            return PlaceOrder(StationIndex(planId, ordinal), recipeId, count);
        }

        /// Stop a station's order: every spot stopped (`StopSpot`; since
        /// 2026-09-30 an unfinished batch's inputs go back into the bay).
        public void StopOrder(int stationIndex)
        {
            var s = StationAt(stationIndex);
            if (s == null) return;
            for (int k = 0; k < s.Spots.Count; k++) StopSpot(s, k);
            s.queue?.Clear();
        }

        public void StopOrder(string planId, int ordinal = 0)
        {
            EnsureStations();
            StopOrder(StationIndex(planId, ordinal));
        }

        public StationOrder OrderAt(int stationIndex)
        {
            var s = StationAt(stationIndex);
            if (s == null) return default;
            // The first selected spot (2026-09-30).
            foreach (var sp in s.Spots)
                if (sp != null && sp.Selected)
                    return new StationOrder { recipe = sp.Recipe, remaining = sp.count, repeat = sp.count <= 0 };
            return default;
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
                s.EnsureSpotRows();
                bool took = false;
                foreach (var sp in s.spots)
                {
                    if (sp == null || got >= n) continue;
                    if (sp.benchState == BenchState.Finished && sp.benchOut > 0 && sp.BenchMakes == res)
                    {
                        int t = Mathf.Min(sp.benchOut, n - got);
                        sp.benchOut -= t; got += t; took = true;
                        if (sp.benchOut <= 0) sp.EmptyBench();
                    }
                }
                if (took) s.SyncLegacy();
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
            MigrateTrip(h);
            return new HaulView
            {
                active = true,
                resource = h.haulRes,
                count = h.haulCount,
                from = h.haulFrom,
                fromStation = h.haulFrom == HaulPlace.Station || h.haulFrom == HaulPlace.Shore
                    ? h.haulFromStation : -1,
                to = h.haulTo,
                toStation = h.haulTo == HaulPlace.Station ? h.haulToStation : -1,
                leg = h.Leg,
                picked = h.haulPicked,
                placed = h.haulPlaced,
                fromAt = new Vector3(h.haulFromX, 0f, h.haulFromZ),
                toAt = new Vector3(h.haulToX, 0f, h.haulToZ),
            };
        }

        /// Units of `res` in hands' arms right now -- PICKED UP, on him
        /// (2026-09-27: a load he is still walking out to fetch is still at
        /// its source). Not in `CountOf`.
        public int CarriedOf(string res)
        {
            int n = 0;
            if (hands != null)
                foreach (var h in hands)
                    if (h != null && h.Hauling && h.haulPicked && h.haulRes == res && !h.HuntTrip) n += h.haulCount;
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
            // The slots' capacity (2026-10-03), not the old uniform ceiling.
            return (CapacityOf(res) - whole - InFlightTo(HaulPlace.Store, -1, res)) - part;
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
            // **A meal** (food rework, 2026-09-27): picked up at the store,
            // eaten there -- never put down.
            if (h.eating) { EatMeal(h); return; }
            // **Nothing to put down** (2026-09-27): a load he never picked up
            // was only planned -- the source still has it.
            if (!h.haulPicked) { CancelPlanned(h); return; }
            // **A builder's armful into a blueprint** (2026-09-23): through
            // `DeliverToSite`, so it fills the oldest site short of it up to
            // its need and any surplus (a visitor beat him to it, the site
            // was cancelled) goes on the pile -- never past a need, never lost.
            if (h.haulTo == HaulPlace.Site)
            {
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
            // **A catch lands in the hut's output box** (2026-09-30), never
            // its input bay and never the store: `LandCatch`.
            if (dest != null && h.haulFrom == HaulPlace.Shore) { LandCatch(h, dest); return; }
            if (dest != null)
            {
                dest.Bay(h.haulRes, true).whole += h.haulCount;
                // **The backhaul (2026-10-02):** a runner who just filled a
                // bay takes that station's rack with him if there is any.
                if (IsRunner(h)) h.backhaulStation = h.haulToStation + 1;
                ClearHaul(h);
                return;
            }

            var st = Store(h.haulRes, true);
            // What the slots still take (2026-10-03): `CapacityOf` already
            // nets out the other resources of the family and their loads
            // walking in, and leaves this load's own reservation out.
            int put = force ? h.haulCount : Mathf.Clamp(CapacityOf(h.haulRes) - st.whole, 0, h.haulCount);
            st.whole += put;
            h.haulCount -= put;
            if (put > 0 && h.haulFrom == HaulPlace.Field && h.haulTo == HaulPlace.Store)
            {
                // **A gatherer's armful lands** (2026-09-23): its trees go
                // over now, as a builder's do (the body cut through the
                // trip), and it is what the camp gathered while away. Booked
                // per unit put down, so a load that waits at a full store is
                // booked once, as it goes in.
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
            if (h.haulTo == HaulPlace.Store) NoteDelivered(h.haulRes, put);
            if (h.haulCount <= 0) { ClearHaul(h); return; }
            h.tripLeg = (int)TripLeg.AtDrop;  // at the store, waiting for room
        }

        /// A hand standing at a full store with a load it cannot put down.
        static bool WaitingAtStore(OutpostHand h) => h.Hauling && h.Leg == TripLeg.AtDrop;

        /// **A hand leaves the camp's books** (recalled aboard, carried to a
        /// berth): whatever is in their arms is put down first -- at its
        /// destination, or the store over the ceiling if need be -- so the
        /// armful never leaves with them. Returns whether the hand was on
        /// the list.
        public bool RemoveHand(OutpostHand h)
        {
            if (h == null || hands == null) return false;
            if (h.Hauling) DepositHaul(h, true);
            // **And off every plot he held (2026-09-28).** The site ladder's
            // per-hand books (`workSite`, the stall guard) are keyed by the
            // row; left in, a recalled or dead clearer kept counting as the
            // crew on his plot, so `PickPlot` never sent anyone else and the
            // blueprint's trees stood forever. `Die` comes through here.
            ForgetSiteWork(h);
            return hands.Remove(h);
        }

        // --- downed / dead (death/rescue phase 1, 2026-09-27) ----------------
        //
        // docs/PLAN-DEATH-RESCUE.md, "Deaths": always downed before dead. A
        // downed hand lies where he fell with a lenient timer; if nobody
        // comes in time (phase 2's drag-to-hut) he dies.

        /// **What a camp label reads as** in a life event or story, until
        /// camps have names of their own: the fire's key. Good enough for
        /// phase 1's templates ("hungry ... at 12,-4"); a later pass can
        /// swap this for an authored name without touching any save data,
        /// since the string is generated, not stored per event as a lookup
        /// key.
        public string CampLabel => string.IsNullOrEmpty(campName) ? keyX + "," + keyZ : campName;

        /// The island's name, set by `Outpost.WireWalkerGuard`; not saved
        /// (the scene re-supplies it every load).
        [System.NonSerialized] public string campName;

        /// **Drop whatever is picked up, right here** (death/rescue phase
        /// 2): a hand who goes down or dies is physical, same as any other
        /// carrier (docs/DELIVERY-ON-ARRIVAL.md) -- his load does not
        /// vanish and does not ride him into the ground; it becomes a
        /// `GroundLoad` at his own spot (`HandAt`) for somebody else to
        /// collect. A load only PLANNED (not yet picked up) is simply
        /// cancelled -- the source still has it, nobody has to fetch it
        /// from the grass.
        void DropCarriedLoad(OutpostHand h)
        {
            if (h == null || !h.Hauling) return;
            if (!h.haulPicked) { CancelPlanned(h); return; }
            // A hunt/meal/site/ship/station load all carry the same way: a
            // resource and a count in his arms. Ground loads are plain
            // stock, so a hunt trip's carcass (not yet Meat/Hide) is left
            // to spill as `Res.Game` -- fine, it is still "a load on the
            // ground", collected the same way as everything else.
            if (h.haulCount > 0 && !string.IsNullOrEmpty(h.haulRes))
                AddGroundLoad(h.haulRes, h.haulCount, HandAt(h));
            ClearHaul(h);
        }

        /// **Public door onto `DropCarriedLoad`** for `CampWorker.TickDefend`
        /// (phase 9): a hand pulled into a fight drops whatever he was
        /// carrying exactly the way a hand pulled into a rescue does.
        public void DropCarriedLoadNow(OutpostHand h) => DropCarriedLoad(h);

        /// Knock this hand down. No-op if he already is, or is not on the
        /// roster. **Drops whatever he had picked up where he stands**
        /// (phase 2) -- a planned-but-not-picked load is just cancelled.
        public bool Down(OutpostHand h, string cause = "")
        {
            if (h == null || hands == null || !hands.Contains(h) || h.downed) return false;
            h.downed = true;
            h.reached = false;
            h.downedLeft = LifeTuning.DownedSeconds;
            h.downedCause = cause ?? "";
            DropCarriedLoad(h);
            // A rescuer already on his way to somewhere else, downed
            // himself: his rescue resets (docs: "if the rescuer is himself
            // downed, the downed hand's rescue resets") -- `DispatchRescuers`
            // picks somebody new next tick.
            h.rescuing = "";
            // **A pouting hand knocked down (phase 4, 2026-09-28):** `Doing`
            // already reads `downed` ahead of `pouting`, but leaving
            // `pouting` true would let `PoutStep` go on ticking his (now
            // meaningless) sulk in the background and would count him
            // against the floor twice over. Clear it outright, same as
            // `rescuing` above; no cooldown penalty either way, since he
            // never finished the pout he was on.
            h.pouting = false;
            h.poutLeft = 0f;
            // **Village defence (phase 9, 2026-09-28):** a hand downed
            // fighting is not defending any more, and his tally against
            // `hitsToDown` starts fresh if he is ever downed again.
            h.defending = false;
            h.defendSpear = null;
            h.raidHitsTaken = 0;
            Life.Lives.Log(h.name, Life.LifeEvents.Downed, CampLabel);
            return true;
        }

        /// **A raider's jab connects** (phase 9). Counts a hit toward
        /// `hitsToDown` (the raid-fight tuning, owned by `Combat` and
        /// handed in rather than read here -- this file never references
        /// `SeaSick.Combat`) and downs him at the limit, cause "Raid" --
        /// `Die` maps that to `LifeEvents.KilledInRaid` if the downed timer
        /// runs out. No-op on a hand already down or off the roster.
        public bool HitDefender(OutpostHand h, int hitsToDown)
        {
            if (h == null || hands == null || !hands.Contains(h) || h.downed) return false;
            h.raidHitsTaken++;
            if (h.raidHitsTaken < Mathf.Max(1, hitsToDown)) return false;
            Down(h, "Raid");
            return true;
        }

        /// **A killed raider's loot, dropped where he fell** (phase 9) --
        /// the same `GroundLoad` pile a dropped hand's load becomes
        /// (`DropCarriedLoad`), so an idle hauler collects it exactly the
        /// same way. `AddGroundLoad` itself is private; `Combat.RaidWalker`
        /// is the only caller outside this file.
        public void DropRaiderLoot(string res, int count, Vector3 at)
        {
            if (count > 0 && !string.IsNullOrEmpty(res)) AddGroundLoad(res, count, at);
        }

        /// Bring a downed hand back (phase 2/dev use -- nothing in phase 1
        /// calls this on its own; the dev panel does). Clears the rescue/
        /// recovery state too, whichever of it applies.
        public void Revive(OutpostHand h)
        {
            if (h == null) return;
            h.downed = false;
            h.downedLeft = 0f;
            h.downedCause = "";
            h.reached = false;
            h.dragged = false;
            h.recovering = false;
            h.recoverLeft = 0f;
            ClearRescuerOf(h);
        }

        /// Whoever was rescuing `h` is freed to go back to his own work.
        void ClearRescuerOf(OutpostHand h)
        {
            if (h == null || hands == null) return;
            foreach (var o in hands) if (o != null && o.rescuing == h.name) o.rescuing = "";
        }

        /// **The timer runs out, or the dev panel says "Kill".** Logs the
        /// death, writes the grave, drops whatever load he was carrying
        /// where he stood (phase 2, same as `Down`), removes him from the
        /// roster the same way `RemoveHand` does, and raises `Lives.Died`
        /// for phase 3's tombstone flow. Safe to call on a hand who was
        /// never downed (the dev panel's "Kill" button).
        public void Die(OutpostHand h, string cause = "")
        {
            if (h == null || hands == null || !hands.Contains(h)) return;
            string label = CampLabel;
            if (string.IsNullOrEmpty(cause)) cause = h.downedCause;
            // **Phase 9:** `Down`'s raid cause is the plain string "Raid";
            // the story/grave cause needs the `LifeEvents` constant so
            // `LifeStory.CauseLines` recognises it.
            if (cause == "Raid") cause = Life.LifeEvents.KilledInRaid;
            var record = Lives.Record(h.name);
            int bornDay = record != null ? record.bornDay : -1;
            // **Phase 3's first-pass grave spot.** Captured before
            // `RemoveHand` drops his row -- `HandAt` is pure ledger data (no
            // scene reference), so this is safe on a headless/unwatched camp
            // too. `GravePlacementFlow` offers this as the default ghost
            // spot and the player may move it before confirming.
            Vector3 diedAt = HandAt(h);
            var grave = new GraveRecord
            {
                name = h.name,
                camp = label,
                bornDay = bornDay,
                diedDay = TimeOfDay.Day,
                cause = cause ?? "",
                x = diedAt.x,
                z = diedAt.z,
            };
            grave.story = LifeStory.Build(record, grave);

            // Whatever he was carrying is dropped where he stood, before he
            // leaves the books -- see the class doc: nothing vanishes.
            DropCarriedLoad(h);
            ClearRescuerOf(h);
            RemoveHand(h);
            // **No grief pout (2026-10-02, second pass).** A death used to
            // send every survivor to the fire for half a day; Kevin: *"you
            // assign someone somewhere, thats what they do"* -- nobody
            // leaves his job over it now (`PoutStep`).

            Lives.Bury(grave);
        }

        /// **The downed timer, real seconds, watched-and-running only.**
        /// Called once a frame from `Outpost.Update` while this camp is
        /// watched, guarded there against a paused game and an offline
        /// catch-up run -- never from `Step`'s game-day quanta, which is
        /// how an away camp and a paused menu both leave a downed hand
        /// exactly as they found him (D2: "nobody dies while I'm away").
        /// **Stopped for good once his rescuer reaches him** (`reached`,
        /// phase 2) -- from then on the drag itself decides his fate, not
        /// this clock.
        public void TickDowned(float realDeltaSeconds)
        {
            if (hands == null || realDeltaSeconds <= 0f) return;
            // Copy first: `Die` mutates `hands`, which this loop is walking.
            downedScratch.Clear();
            foreach (var h in hands) if (h != null && h.downed && !h.reached) downedScratch.Add(h);
            foreach (var h in downedScratch)
            {
                h.downedLeft -= realDeltaSeconds;
                if (h.downedLeft <= 0f) Die(h, h.downedCause);
            }
        }

        [System.NonSerialized] readonly List<OutpostHand> downedScratch = new List<OutpostHand>();

        // --- the rescuer (death/rescue phase 2, 2026-09-27) ------------------
        //
        // Picking who goes is the ledger's job (plain data, same as every
        // other dispatch in this file); the walk itself is the rescuer's
        // own `CampWorker` -- see `CampWorker.TickRescue`. Called every
        // watched-and-running frame right beside `TickDowned`
        // (`Outpost.Update`), so a rescuer is sent within the same frame a
        // hand goes down.

        /// Send the nearest free hand after every downed hand who does not
        /// have one yet: **prefer idle, then any hand not downed, dragged,
        /// recovering or already rescuing somebody** (`OutpostHand.Busy`
        /// covers the first three; the fourth is `rescuing` itself). Only
        /// one rescuer per downed hand. A hand chosen mid-haul drops his
        /// load where he stands first, the same way `Down` does.
        public void DispatchRescuers()
        {
            if (hands == null) return;
            // Orphaned rescues first: a rescuer whose man is gone from the
            // roster (recalled, died) or back on his feet stands down.
            foreach (var o in hands)
            {
                if (o == null || string.IsNullOrEmpty(o.rescuing)) continue;
                var t = Hand(o.rescuing);
                if (t == null || !t.downed) o.rescuing = "";
            }
            foreach (var down in hands)
            {
                // A reached hand whose rescuer vanished (recalled, downed
                // himself) is sent a new one; his timer stays stopped.
                if (down == null || !down.downed) continue;
                bool has = false;
                foreach (var o in hands) if (o != null && o.rescuing == down.name) { has = true; break; }
                if (has) continue;

                OutpostHand best = null;
                bool bestIdle = false;
                float bestDist = float.MaxValue;
                Vector3 at = HandAt(down);
                foreach (var o in hands)
                {
                    if (o == null || o == down || o.Busy) continue;
                    bool idle = o.order == OutpostOrder.Idle;
                    float d = Vector3.SqrMagnitude(HandAt(o) - at);
                    if (best == null || (idle && !bestIdle) || (idle == bestIdle && d < bestDist))
                    {
                        best = o; bestIdle = idle; bestDist = d;
                    }
                }
                if (best == null) continue;
                // He drops whatever he was carrying first (docs: "a hand
                // carrying a picked load drops it where he stands first").
                DropCarriedLoad(best);
                best.rescuing = down.name;
            }
        }

        // --- recovery (death/rescue phase 2, 2026-09-27) ---------------------

        /// **Recovery ticks in the ordinary game-day quantum** (`Step`),
        /// unlike the downed timer and the drag itself -- docs: "recovery...
        /// MAY advance in game time, unwatched too, that only helps." Stands
        /// him back up once `recoverLeft` runs out.
        void StepRecovery(float days)
        {
            if (hands == null || days <= 0f) return;
            foreach (var h in hands)
            {
                if (h == null || !h.recovering) continue;
                h.recoverLeft -= days;
                if (h.recoverLeft <= 0f)
                {
                    h.recovering = false;
                    h.recoverLeft = 0f;
                    h.dragged = false;
                }
            }
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
            h.tripLeg = 0;
            h.haulPicked = false;
            h.legLeft = h.workLeft = 0f;
            h.eating = false;
        }

        /// Every load put down now, before station indices shift.
        void FlushAllHauls()
        {
            if (hands == null) return;
            foreach (var h in hands) if (h != null && h.Hauling) DepositHaul(h, true);
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
            /// The blueprint a `to == Site` chore walks to (2026-10-02: a
            /// runner's barrow, or a rack straight to a site).
            public PendingBuild site;
        }

        /// **Hauling, by the approved ladder (2026-10-02)** -- runners, and
        /// spare hands on an island with no runner (`MayHaul`). Rung by
        /// rung; within a rung the chore whose pickup is NEAREST the hand
        /// wins (straight line from `HandAt`; `h` null = "is there any",
        /// first found):
        ///
        /// 0. a dropped load home (death/rescue phase 2: urgent and small)
        ///    -- since 2026-10-02 AFTER 1a's idle benches;
        /// 1. manned stations kept supplied: a bay short of its next batch
        ///    (from the store, or straight off another station's rack),
        ///    and a rack that blocks its bench (any amount);
        /// 2. blueprints -- runners only (builders fetch for themselves
        ///    when there is no runner): oldest site first, whole loads;
        /// 3. pre-stocking bays while the bench works, only once a real
        ///    load fits (`PrestockLoad`) -- never a 1-unit top-up;
        /// 4. racks holding a full load (`RackLoad`), or anything at all in
        ///    the last hour of the work day -- to a bay or site that wants
        ///    it, else the store (rule 5, direct delivery);
        /// 5. bay stock no order wants back to the store.
        ///
        /// Loads are the carrier's (`CarryArmful`: a runner's barrow)
        /// capped only by what finishes the job, empties the source or
        /// fills the destination. Allocation-free; O(chores) per call.
        ///
        /// **Aging within a rung (2026-10-03, villager review group 4):**
        /// rungs 1a, 1b, 3 and 4 no longer go purely nearest-first -- the
        /// request that has waited the most whole `AgeStepQuanta` wins
        /// (`OfferAged`; a bay's wait from `BayAge`, a rack's from
        /// `RackAge`), and only equal waits fall back to the nearest
        /// pickup. Two sawmills with one runner: the near one, refilled
        /// over and over, can no longer keep the far one's bay empty all
        /// day. Rung 2 was already oldest-first (the site queue's order);
        /// 0 and 5 stay nearest-first (nothing waits on them).
        bool FindHaulerChore(out Chore c) => FindHaulerChore(null, out c);

        bool FindHaulerChore(OutpostHand h, out Chore c)
        {
            c = default;
            bool runner = IsRunner(h);
            bool any = h == null;
            Vector3 at = any ? Vector3.zero : HandAt(h);
            float best = float.MaxValue;
            int bestAge = -1;
            bool found = false;

            int ns = stations != null ? stations.Count : 0;
            // 1a first (2026-10-02, Kevin: runners are the supply line and a
            // worker never fetches while there is one): a manned bay short
            // of its next batch -- an idle bench -- beats even a dropped
            // load, which a full store can leave lying by the dozen.
            for (int i = 0; i < ns; i++)
                if (BayChore(h, i, true, at, any, ref c, ref best, ref bestAge, ref found) && any) return true;
            if (found) return true;

            // 0. A dropped load home.
            if (groundLoads != null)
                for (int i = 0; i < groundLoads.Count; i++)
                {
                    var g = groundLoads[i];
                    if (g == null || g.count <= 0 || string.IsNullOrEmpty(g.res)) continue;
                    int free = g.count - Claimed(HaulPlace.Ground, i, g.res, false);
                    if (free <= 0) continue;
                    int room = StoreRoomNet(g.res);
                    if (room <= 0) continue;
                    // Capped by what he carries, like every other rung
                    // (2026-10-03, review fix A): a pile bigger than his
                    // armful is fetched in several trips -- the pickup now
                    // takes only his load off the row and leaves the rest.
                    var k = new Chore
                    {
                        res = g.res, n = Mathf.Min(CarryArmful(h, g.res), Mathf.Min(free, room)),
                        from = HaulPlace.Ground, fromStation = i, to = HaulPlace.Store, toStation = -1,
                    };
                    if (Offer(ref c, ref best, k, at, any, new Vector3(g.x, 0f, g.z))) return true;
                    found = true;
                }
            if (found) return true;

            // 1b. A rack blocking its bench (the longest-held rack first).
            best = float.MaxValue; bestAge = -1;
            for (int i = 0; i < ns; i++)
            {
                if (!RackBlocking(stations[i]) || !RackChore(i, out var k, h, true)) continue;
                if (OfferAged(ref c, ref best, ref bestAge, k, at, any, StationAtOr(i, at), RackAge(stations[i]))) return true;
                found = true;
            }
            if (found) return true;

            // 2. Blueprints (runners).
            if (runner && SiteChore(h, out var sc)) { c = sc; return true; }

            // 3. Pre-stock bays while the bench works.
            best = float.MaxValue; bestAge = -1;
            for (int i = 0; i < ns; i++)
                if (BayChore(h, i, false, at, any, ref c, ref best, ref bestAge, ref found) && any) return true;
            if (found) return true;

            // 4. Racks holding a full load (or the day's last hour).
            best = float.MaxValue; bestAge = -1;
            for (int i = 0; i < ns; i++)
            {
                if (!RackChore(i, out var k, h, false)) continue;
                if (OfferAged(ref c, ref best, ref bestAge, k, at, any, StationAtOr(i, at), RackAge(stations[i]))) return true;
                found = true;
            }
            if (found) return true;

            // 5. Bay stock no order wants, home.
            for (int i = 0; i < ns; i++)
            {
                var s = stations[i];
                if (s == null || s.bay == null) continue;
                // An unmanned station's order wants nothing: its bay goes home.
                bool manned = Manned(s);
                foreach (var row in s.bay)
                {
                    int free = RowFree(i, row, true);
                    if (row == null || free <= 0 || (manned && SpotsWant(s, row.resource))) continue;
                    int room = StoreRoomNet(row.resource);
                    if (room <= 0) continue;
                    var k = new Chore
                    {
                        res = row.resource, n = Mathf.Min(CarryArmful(h, row.resource), Mathf.Min(free, room)),
                        source = row, from = HaulPlace.Station, fromStation = i,
                        to = HaulPlace.Store, toStation = -1, fromBay = true,
                    };
                    if (Offer(ref c, ref best, k, at, any, StationAtOr(i, at))) return true;
                    found = true;
                    break;
                }
            }
            return found;
        }

        /// Keep `k` if its pickup is nearer than the best so far. True =
        /// the caller only asked whether there is any (take it, stop).
        static bool Offer(ref Chore c, ref float best, Chore k, Vector3 at, bool any, Vector3 pickup)
        {
            if (any) { c = k; return true; }
            float dx = pickup.x - at.x, dz = pickup.z - at.z;
            float d = dx * dx + dz * dz;
            if (d < best) { best = d; c = k; }
            return false;
        }

        /// **`Offer` with aging (2026-10-03, group 4)**: the longer-waiting
        /// request (`age`, whole `AgeStepQuanta`) wins outright; equal ages
        /// keep the nearer pickup. `bestAge` starts at -1 per rung.
        static bool OfferAged(ref Chore c, ref float best, ref int bestAge, Chore k, Vector3 at, bool any,
            Vector3 pickup, int age)
        {
            if (any) { c = k; return true; }
            float dx = pickup.x - at.x, dz = pickup.z - at.z;
            float d = dx * dx + dz * dz;
            if (age > bestAge || (age == bestAge && d < best)) { bestAge = age; best = d; c = k; }
            return false;
        }

        Vector3 StationAtOr(int i, Vector3 fallback) => StationPlace(i, out var p) ? p : fallback;

        /// **A bay chore at station `i`** (rungs 1 and 3). `urgent`: a
        /// selected spot's input short of one batch (bay + loads walking
        /// in), filled up to `InputCap` with what the carrier takes.
        /// Otherwise pre-stocking: only when at least `PrestockLoad` fits
        /// and is there to take. The source is the nearest of the store and
        /// every other station's rack (rule 5); the station must be manned
        /// and its rack not jammed. Offers into `c`/`best` like `Offer`;
        /// true when something was offered (with `any`, the first one).
        bool BayChore(OutpostHand h, int i, bool urgent, Vector3 at, bool any,
            ref Chore c, ref float best, ref int bestAge, ref bool found)
        {
            var s = stations[i];
            if (s == null || !Manned(s)) return false;
            s.EnsureSpotRows();
            if (RackJam(s) != null) return false;
            bool offered = false;
            Vector3 storeAt = StoreAt(out var sa) ? sa : Vector3.zero;
            foreach (var sp in s.spots)
            {
                var r = sp != null && sp.Selected ? sp.Recipe : null;
                if (r == null || LockOf(s, r) != null) continue;
                if (r.tool != null && HeldOf(r.tool) <= 0f) continue;
                foreach (var line in r.takes)
                {
                    if (line.n <= 0) continue;
                    int have = s.BayCount(line.res) + InFlightTo(HaulPlace.Station, i, line.res);
                    if (urgent != (have < line.n)) continue;
                    int space = s.InputCap - have;
                    if (space <= 0) continue;
                    int armful = CarryArmful(h, line.res);
                    int load = Mathf.Min(armful, space);
                    // **Part loads for a short bay (verified 2026-10-03,
                    // group 4):** urgent asks only 1, so a store or a rack
                    // holding less than a barrow still feeds an idle bench
                    // (the barrow shows the fewer items: `LoadCount` is the
                    // trip's count). Pre-stocking keeps the full-load rule.
                    int need = urgent ? 1 : PrestockLoad(s, armful);
                    int age = BayAge(s, line.res, urgent);
                    // From the store...
                    int inStore = StoreFree(line.res);
                    if (inStore > 0 && Mathf.Min(load, inStore) >= need)
                    {
                        var k = new Chore
                        {
                            res = line.res, n = Mathf.Min(load, inStore),
                            source = Store(line.res), from = HaulPlace.Store, fromStation = -1,
                            to = HaulPlace.Station, toStation = i,
                        };
                        found = offered = true;
                        if (OfferAged(ref c, ref best, ref bestAge, k, at, any, storeAt, age)) return true;
                    }
                    // ...or straight off another station's rack.
                    for (int j = 0; j < stations.Count; j++)
                    {
                        if (j == i) continue;
                        var row = stations[j]?.Rack(line.res);
                        int free = RowFree(j, row, false);
                        if (free <= 0 || Mathf.Min(load, free) < need) continue;
                        var k = new Chore
                        {
                            res = line.res, n = Mathf.Min(load, free),
                            source = row, from = HaulPlace.Station, fromStation = j,
                            to = HaulPlace.Station, toStation = i,
                        };
                        found = offered = true;
                        if (OfferAged(ref c, ref best, ref bestAge, k, at, any, StationAtOr(j, at), age)) return true;
                    }
                }
            }
            return offered;
        }

        /// **A runner's blueprint delivery**: the oldest site short of
        /// timber, stone or brick (the queue's own order, which is the
        /// order `DeliverToSite` fills in), a barrow capped at what it is
        /// still short of net of loads walking, from the nearest of the
        /// store and the racks.
        bool SiteChore(OutpostHand h, out Chore c)
        {
            c = default;
            if (sites == null) return false;
            Vector3 at = HandAt(h);
            foreach (var site in sites)
            {
                if (site == null || site.Complete || site.Stocked) continue;
                for (int k = 0; k < 3; k++)
                {
                    string res = k == 0 ? Res.Timber : k == 1 ? Res.Stone : Res.Brick;
                    int need = NetShort(site, res);
                    if (need <= 0) continue;
                    int cap = Mathf.Min(CarryArmful(h, res), need);
                    float best = float.MaxValue;
                    bool got = false;
                    int pileFree = StoreFree(res);
                    if (pileFree > 0)
                    {
                        Vector3 sAt = StoreAt(out var sa) ? sa : Vector3.zero;
                        Offer(ref c, ref best, new Chore
                        {
                            res = res, n = Mathf.Min(cap, pileFree), source = Store(res),
                            from = HaulPlace.Store, fromStation = -1, to = HaulPlace.Site, toStation = -1, site = site,
                        }, at, false, sAt);
                        got = true;
                    }
                    if (stations != null)
                        for (int i = 0; i < stations.Count; i++)
                        {
                            var row = stations[i]?.Rack(res);
                            int free = RowFree(i, row, false);
                            if (free <= 0) continue;
                            Offer(ref c, ref best, new Chore
                            {
                                res = res, n = Mathf.Min(cap, free), source = row,
                                from = HaulPlace.Station, fromStation = i, to = HaulPlace.Site, toStation = -1, site = site,
                            }, at, false, StationAtOr(i, at));
                            got = true;
                        }
                    if (got) return true;
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

        /// **A rack in the way of its bench**: a finished batch it could
        /// not take, or no room left at all.
        static bool RackBlocking(StationStock s)
        {
            if (s == null || s.rack == null) return false;
            if (s.RackRoom <= 0) return s.RackTotal > 0;
            s.EnsureSpotRows();
            foreach (var sp in s.spots)
                if (sp != null && sp.benchState == BenchState.Finished && sp.benchOut > 0) return true;
            return false;
        }

        /// **Rack of station `i` to where it is wanted** (rules 2 and 5,
        /// 2026-10-02). `anyAmount` (the rack blocks the bench, a backhaul)
        /// takes whatever is there; otherwise only a full load
        /// (`RackLoad`), or anything in the last hour of the work day. The
        /// load goes straight to a manned bay that wants all of it, else
        /// the oldest site short of all of it, else the store as far as it
        /// has room.
        bool RackChore(int i, out Chore c, OutpostHand h = null, bool anyAmount = true)
        {
            c = default;
            var s = stations[i];
            if (s == null || s.rack == null) return false;
            bool lastHour = LastWorkOfDay;
            foreach (var row in s.rack)
            {
                int free = RowFree(i, row, false);
                if (row == null || free <= 0) continue;
                int armful = CarryArmful(h, row.resource);
                if (!anyAmount && !lastHour && free < RackLoad(s, armful)) continue;
                int n = Mathf.Min(armful, free);
                if (!RackDest(i, row.resource, ref n, out var to, out int toStation, out var site)) continue;
                c = new Chore
                {
                    res = row.resource, n = n,
                    source = row, from = HaulPlace.Station, fromStation = i,
                    to = to, toStation = toStation, site = site,
                };
                return true;
            }
            return false;
        }

        /// Where `n` of `res` off station `i`'s rack goes: a manned bay
        /// (not `i`'s) whose order takes it and that has room for all `n`;
        /// else a manned bay SHORT of one batch, with as much as it has room
        /// for (a part load that fills it); else the oldest site short of
        /// it, if it takes all `n`; else the store, `n` clamped to its room.
        /// False = nowhere.
        ///
        /// **The short bay's part load (2026-10-03, villager review group
        /// 4, "rack straight to bay"):** a full rack load used to skip any
        /// bay without room for ALL of it and go to the store, so a bay one
        /// batch short waited for a second trip out of the store. Now the
        /// runner tips what that bay can take straight into it and the rest
        /// stays on the rack (the full-load rule allows a load that "fills
        /// the destination"). Only for a bay short of a batch (`BayShortFor`):
        /// a bay that is merely pre-stocking keeps the full-load rule.
        bool RackDest(int i, string res, ref int n, out HaulPlace to, out int toStation, out PendingBuild site)
        {
            to = HaulPlace.Store;
            toStation = -1;
            site = null;
            int partAt = -1, partSpace = 0;
            if (stations != null)
                for (int j = 0; j < stations.Count; j++)
                {
                    var d = stations[j];
                    if (j == i || d == null || !Manned(d) || !SpotsWant(d, res) || RackJam(d) != null) continue;
                    int space = d.InputCap - d.BayCount(res) - InFlightTo(HaulPlace.Station, j, res);
                    if (space < n)
                    {
                        if (space > partSpace && partAt < 0 && BayShortFor(d, j, res)) { partAt = j; partSpace = space; }
                        continue;
                    }
                    to = HaulPlace.Station;
                    toStation = j;
                    return true;
                }
            if (partAt >= 0)
            {
                n = partSpace;
                to = HaulPlace.Station;
                toStation = partAt;
                return true;
            }
            if (sites != null && (res == Res.Timber || res == Res.Stone || res == Res.Brick))
                foreach (var p in sites)
                {
                    if (p == null || p.Complete || p.Stocked) continue;
                    int need = NetShort(p, res);
                    if (need <= 0) continue;
                    // The oldest short site takes it first (`DeliverToSite`).
                    if (need < n) break;
                    to = HaulPlace.Site;
                    site = p;
                    return true;
                }
            int room = StoreRoomNet(res);
            if (room <= 0) return false;
            n = Mathf.Min(n, room);
            return true;
        }

        /// **A selected, unlocked spot of station `j` is short of one batch
        /// of `res`** (bay + loads walking in): rung 1a's urgent test, for
        /// `RackDest`'s part load.
        bool BayShortFor(StationStock d, int j, string res)
        {
            d.EnsureSpotRows();
            int have = d.BayCount(res) + InFlightTo(HaulPlace.Station, j, res);
            foreach (var sp in d.spots)
            {
                var r = sp != null && sp.Selected ? sp.Recipe : null;
                if (r == null || LockOf(d, r) != null) continue;
                if (r.tool != null && HeldOf(r.tool) <= 0f) continue;
                foreach (var line in r.takes)
                    if (line.n > 0 && line.res == res && have < line.n) return true;
            }
            return false;
        }

        void BeginChore(OutpostHand h, Chore c)
        {
            // The source gives the load up at the PICKUP, not now.
            StartTimedTrip(h, c.res, c.n, c.from, c.fromStation, c.to, c.toStation, c.site, c.fromBay);
        }

        /// Is there station hauling a spare hand could do right now?
        public bool HasHaulChore() => FindHaulerChore(out _);

        /// **Station hauling a SPARE hand may take** (a gatherer whose store
        /// is full): none once the island has a runner (2026-10-02) -- the
        /// runners carry.
        bool SpareHaulChore() => !RunnersOn && HasHaulChore();

        void HaulerDay(OutpostHand h, ref float budget)
        {
            for (int guard = 0; guard < 64 && budget > Eps; guard++)
            {
                if (h.Hauling) { if (!AdvanceHaul(h, ref budget)) break; continue; }
                // **With runners on the island, nobody else starts a haul**
                // (2026-10-02); the load in his arms is still walked above.
                if (!MayHaul(h)) break;
                // The player's transfer orders first (2026-09-24): Kevin
                // asked for them; the station hauling is background.
                if (StartTransferTrip(h)) continue;
                if (!FindHaulerChore(h, out var c)) break;
                BeginChore(h, c);
            }
        }

        // --- the bench (one per spot since 2026-09-30) -------------------------------

        /// Every spot's bench emptied (a station torn down, an old save's
        /// half-done catch). Nothing is put anywhere: the caller has.
        static void EmptyBench(StationStock s)
        {
            s.EnsureSpotRows();
            foreach (var sp in s.spots) sp?.EmptyBench();
            s.SyncLegacy();
        }

        /// Every finished spot onto the (shared) rack as far as it has
        /// room. True when no bench is left holding a finished batch.
        static bool UnloadBench(StationStock s)
        {
            bool clear = true;
            s.EnsureSpotRows();
            foreach (var sp in s.spots)
                if (sp != null && sp.benchState == BenchState.Finished && !UnloadSpot(s, sp)) clear = false;
            s.SyncLegacy();
            return clear;
        }

        static bool AnySpotBusy(StationStock s)
        {
            foreach (var sp in s.spots) if (sp != null && sp.BenchBusy) return true;
            return false;
        }

        /// **A stationed worker's day** (spots, 2026-09-30): unload every
        /// finished spot, load every empty selected spot that has its inputs,
        /// then work ALL the loaded spots AT ONCE, each at its own full rate
        /// -- one worker tends every spot and his effort is not divided (a
        /// spot's timer runs only while he stands at the station, as the
        /// single bench's did). A spot short of an input is fetched for
        /// first (store, or off the island for a gatherable raw -- the other
        /// spots wait while he walks). With nothing loaded, he hauls: the
        /// rack to the store when it blocks, inputs into the bay, and the
        /// rack home when there is no more to fetch.
        void WorkerDay(OutpostHand h, StationStock s, int si, ref float budget)
        {
            // The fisher works at the water, not at a bench (2026-09-30).
            if (FishesAtShore(s)) { CatchDay(h, s, si, ref budget); return; }
            s.EnsureSpotRows();
            var spots = s.spots;
            for (int guard = 0; guard < 128 && budget > Eps; guard++)
            {
                if (h.Hauling) { if (!AdvanceHaul(h, ref budget)) break; continue; }

                foreach (var sp in spots)
                    if (sp != null && sp.benchState == BenchState.Finished) UnloadSpot(s, sp);
                foreach (var sp in spots)
                {
                    if (sp == null) continue;
                    // A recipe removed from the game: its batch is let go.
                    if (sp.BenchBusy && sp.BenchRecipe == null) sp.EmptyBench();
                    if (sp.benchState == BenchState.Empty) TryLoadSpot(s, sp);
                }

                if (!AnySpotBusy(s))
                {
                    if (!StartWorkerChore(h, s, si)) break;
                    continue;
                }
                // A spot starved of an input: fetch it before working on.
                // (With runners on the island the busy spots go on; a
                // runner brings the starved one's input, 2026-10-02.)
                if (StartInputFetch(h, s, si, !WorkerFetches(s))) continue;

                // **At the bench to work it** (2026-09-27): the job's timer
                // runs only while he stands there.
                if (StationPlace(si, out var benchAt) && !WalkTo(h, benchAt, ref budget, WorkFactor(h))) break;
                if (budget <= Eps) break;

                // Every loaded spot advances by the same span of his time:
                // up to the first batch to finish, or the whole budget.
                float step = budget;
                bool any = false;
                foreach (var sp in spots)
                {
                    if (sp == null || !sp.BenchBusy) continue;
                    float perDay = BenchPerDay(s, sp.BenchRecipe);
                    if (perDay <= 0f) continue;
                    any = true;
                    step = Mathf.Min(step, Mathf.Max(0f, (1f - sp.progress01) / perDay));
                }
                if (!any) break;
                foreach (var sp in spots)
                {
                    if (sp == null || !sp.BenchBusy) continue;
                    var r = sp.BenchRecipe;
                    float perDay = BenchPerDay(s, r);
                    if (perDay <= 0f) continue;
                    sp.benchState = BenchState.Working;
                    float need = (1f - sp.progress01) / perDay;
                    if (step >= need - Eps) FinishSpotJob(s, sp, r);
                    else sp.progress01 += step * perDay;
                }
                budget -= Mathf.Min(budget, step);
            }
            s.SyncLegacy();
        }

        /// **The worker's own hauling, the bench idle** (2026-10-02 rules):
        /// a rack blocking the bench goes (any amount); a short input is
        /// fetched; the rack goes home only with a full load on it
        /// (`RackLoad`) or in the day's last hour -- never a 1-unit walk.
        /// **With runners on the island he stays at the bench**
        /// (`WorkerFetches`): the runners bring and take, and he only goes
        /// himself for a raw the store has none of (runners do not cut), or
        /// once his bench has stood idle `RunnerFallbackQuanta` with no
        /// runner on the way.
        bool StartWorkerChore(OutpostHand h, StationStock s, int si)
        {
            bool self = WorkerFetches(s);
            // A bench is blocked by a full rack: carry the rack home.
            if (self)
                foreach (var sp in s.spots)
                    if (sp != null && sp.benchState == BenchState.Finished)
                    {
                        if (RackChore(si, out var rc, h, true)) { BeginChore(h, rc); return true; }
                        break;
                    }

            if (StartInputFetch(h, s, si, !self)) return true;
            if (!self) return false;

            // No more raw (no order, no tool, an input nobody can fetch), or
            // the inputs are already walking in: take the rack home -- a
            // full load of it (2026-10-02).
            if (RackChore(si, out var home, h, false)) { BeginChore(h, home); return true; }
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
            int standing = FieldFree(res);
            int n = Mathf.Min(Res.Armful(res), Mathf.Min(RoomFor(res), standing));
            if (n <= 0) return false;
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
            // Re-ordered off the hunt mid-trip: a beast not yet jabbed is let
            // be; a carcass on his shoulders is still carried home (below).
            if (h.HuntTrip && !h.huntKilled) ClearHaul(h);
            float scale = WorkFactorOn(h, h.target) * PriorityMultiplier(h.target);
            float budget = days * scale;
            for (int guard = 0; guard < 64 && budget > Eps; guard++)
            {
                if (h.Hauling) { if (!AdvanceHaul(h, ref budget, scale)) return; continue; }
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
                // **Downed, recovering, dragged, off rescuing, or pouting
                // (death/rescue): no new work, no new trips.** `WorkFactor`
                // is already 0 for him, but a busy hand should not even be
                // READ by the dispatcher -- skip him outright rather than
                // trust a zero budget alone.
                if (h.Busy)
                {
                    // **A pouting hand may still be mid-meal (phase 4,
                    // 2026-09-28).** `EatStep` starts a hungry hand's
                    // store-and-back trip before it knows about `Busy` (it
                    // only ever excludes `downed`), so a hand can already be
                    // walking a meal home the instant he turns angry enough
                    // to pout. Left alone, `Busy` would skip him outright
                    // from here on and the food would sit in his arms
                    // forever, going neither into him nor back on the pile.
                    // One explicit advance, at full pace and ignoring
                    // `WorkFactor`'s (correct) zero for a pouting hand,
                    // finishes that one trip so `AdvanceHaul` hands off to
                    // `EatMeal` -- nothing else about his day runs from
                    // here. Downed/recovering/dragged/rescuing hands never
                    // arrive here with `h.eating` set (downed never starts
                    // one; nothing starts one for the others either), so in
                    // practice this only ever fires for a pouting hand.
                    if (h.pouting && h.eating && h.Hauling)
                    {
                        float mealBudget = days;
                        AdvanceHaul(h, ref mealBudget, 1f);
                    }
                    continue;
                }
                float budget = days * WorkFactor(h);
                if (IsRunner(h))
                {
                    // **A runner's day** (2026-10-02, OutpostLedger.Runners.cs).
                    RunnerDay(h, ref budget);
                }
                else if (h.order == OutpostOrder.Work && IsStation(h.target))
                {
                    var s = StationOfHand(h);
                    if (s == null) { if (h.Hauling) AdvanceHaul(h, ref budget); continue; }
                    int si = stations.IndexOf(s);
                    // Once a quantum: how long his bench has waited on a runner.
                    TickBenchWait(h, s, si);
                    WorkerDay(h, s, si, ref budget);
                }
                else if (TripGatherer(h))
                {
                    // `GatherDay` already spent his quantum, arms and all.
                }
                else if ((h.order == OutpostOrder.Idle && !Reserve(h))
                         || (gatherersHaul && GatherBlocked(h)))
                {
                    // (A hand the PLAYER stood down is held in reserve,
                    // 2026-09-28: no chores for him -- only a load already in
                    // his arms is walked, by the branch below.)
                    HaulerDay(h, ref budget);
                    // Nothing to haul: the stock top-up (2026-09-28).
                    if (h.order == OutpostOrder.Idle && !Reserve(h) && budget > Eps && !h.Hauling)
                        TopUpDay(h, ref budget);
                }
                else if (h.Hauling && !builderScratch.Contains(h))
                {
                    // **A load in his arms is walked where it was going**
                    // (2026-09-27): re-ordered mid-trip, he still carries
                    // it there. (A builder's loads are the builder pass's.)
                    AdvanceHaul(h, ref budget);
                }
            }
        }

        // --- stall reasons -----------------------------------------------------

        string GatherFullReason(OutpostHand h)
        {
            string into = h.target == Res.Game ? Res.Meat : h.target;
            string head = h.target == Res.Game
                ? $"store is full of {Friendly(Res.Meat)} and {Friendly(Res.Hide)}"
                : $"store is full of {Friendly(into)}";
            if (SpareHaulChore()) return head + ", hauling for the stations";
            if (Focus != null) return head + ", helping build";
            return head;
        }

        /// Why a stationed worker is not making anything, or null. Across
        /// every spot since 2026-09-30: stalled only when NO spot can go on
        /// (the first spot's cause is the one said).
        string StationStallCause(OutpostHand h)
        {
            var s = StationOfHand(h);
            if (s == null) return "the building is not standing";
            s.EnsureSpotRows();
            if (AnySpotBusy(s)) return null;
            bool anySelected = false, anyFinished = false;
            foreach (var sp in s.spots)
            {
                if (sp == null) continue;
                if (sp.Selected) anySelected = true;
                if (sp.benchState == BenchState.Finished) anyFinished = true;
            }
            if (anyFinished || anySelected)
            {
                string jam = RackJam(s);
                // With the fix, as the store-full chip says it (2026-10-02).
                if (jam != null) return StoreFullWords(jam);
                if (anyFinished) return null;
            }
            // Checked before the haul: a worker carrying his rack home with
            // no order is still a station with nothing to make.
            // "no recipe chosen · open the hunting lodge and pick one"
            // (2026-10-02): the fix, at the station, by its name.
            if (!anySelected) return NoRecipeWords(s);
            if (h.Hauling) return null;
            if (FishesAtShore(s))
            {
                string shoreCause = CatchStallCause(s);
                if (shoreCause != null) return shoreCause;
            }
            int si = stations.IndexOf(s);
            string first = null;
            foreach (var sp in s.spots)
            {
                if (sp == null || !sp.Selected) continue;
                string cause = SpotInputCause(s, si, sp.Recipe);
                if (cause == null) return null;
                if (first == null) first = cause;
            }
            return first;
        }

        /// One spot's recipe: why it cannot start, in the worker's words.
        string SpotInputCause(StationStock s, int si, Economy.Recipe r)
        {
            if (r == null) return "no order given";
            string locked = LockOf(s, r);
            if (locked != null) return locked;
            if (r.tool != null && HeldOf(r.tool) <= 0f) return $"needs a {Friendly(r.tool)} in the pile";
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
                // Made right here (the sawmill's fine boards eat its own
                // boards): say what to pick, not "made at the sawmill" to the
                // sawyer standing in it (Kevin's camp, 2026-10-02).
                if (makers.Count > 0 && makers[0].station == s.planId)
                    return $"out of {Friendly(line.res)} · pick {makers[0].label} here to make more";
                if (makers.Count > 0)
                    return $"waiting for {Friendly(line.res)} (made at the {BuildPlans.Named(makers[0].station).label})";
                return $"waiting for {Friendly(line.res)}";
            }
            return null;
        }
    }
}
