using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// <summary>
    /// **Runners: the camp's barrow crew (2026-10-02, design approved by
    /// Kevin).** Hauling used to be everybody's spare minute -- a sawyer
    /// leaving his bench to fetch one log, an idle hand walking a single
    /// plank to the store -- and the island read as people wandering about
    /// with one thing each.
    ///
    /// A RUNNER is a hand on the Work order at the store hut
    /// (`IsRunner`: `target == BuildPlans.Storage.id`, position "runner"),
    /// so the Hand, `Outpost.Assign` and the station sheet staff it like
    /// any post. A store hut takes 2 / 4 / 6 runners at levels 1 / 2 / 3
    /// (`StoreRunners`, `StationCapacity`). Runners push
    /// wheelbarrows (`Res.BarrowArmful`) and do ALL the logistics
    /// (`RunnerDay`): dropped loads, the player's ship transfers, the
    /// stations' bays (urgent, then pre-stocked), racks that block, the
    /// blueprints' materials, full racks home -- straight from a rack to a
    /// bay or site that wants it where they can (`FindHaulerChore` has the
    /// ladder). A runner with nothing to do waits at the store ("Runner,
    /// waiting"); he is not idle. Runners never gather, build or man a
    /// bench.
    ///
    /// **While a runner is on the island (`RunnersOn`)** nobody else starts
    /// a haul (`MayHaul`): stationed workers stay at their benches
    /// (`WorkerFetches`) -- unless the bench has stood idle for
    /// `RunnerFallbackQuanta` with no runner bringing anything, when the
    /// worker goes himself as before, so nothing stalls; builders only
    /// build (and cut/quarry straight to a site a material nobody has in
    /// store or on a rack); free hands build, then top the store up from
    /// the field (`TopUpDay`), then rest at the fire, truly Idle.
    ///
    /// **Everyone who hauls (runner or not)**: full loads only -- a trip
    /// leaves with the carrier's full armful unless it finishes the job,
    /// empties the source or fills the destination; racks go home only
    /// with a full load on them (`RackLoad`), when they block the bench, or
    /// in the work day's last hour (`LastWorkOfDay`); bays are pre-stocked
    /// only once a real load fits (`PrestockLoad`); nearest chore first
    /// within a rung.
    ///
    /// Same code path watched and unwatched (D2): every decision is the
    /// ledger's, made inside `Step`; the bodies mime `HaulOf`.
    /// </summary>
    public partial class OutpostLedger
    {
        // --- tuning ---------------------------------------------------------------

        /// The store hut's plan id: a Work hand there is a runner.
        public static string StorageId => BuildPlans.Storage.id;

        // --- the store hut: runner progression (2026-10-04) ------------------
        //
        // **Kevin, 2026-10-04: the Storehouse is merged into the store hut**
        // ("merge them into the right building"). The store hut's levels now
        // carry the runner progression the Storehouse had (2026-10-03):
        //
        //   Store hut  | runner posts | perk for EVERY runner on the island
        //   L1 (built) | 2            | --
        //   L2         | 4            | barrow +1 armful, jog 10 % faster
        //   L3 (new)   | 6            | barrow +2 armfuls, jog 20 % faster
        //
        // Perks come from the HIGHEST-level store hut standing on the camp (a
        // second copy adds its posts, never a second perk). They derive from
        // building levels alone, so nothing new is saved. An old save's
        // Storehouse is taken down and refunded on load (`MergeStorehouses`).

        /// Runner posts one store hut takes at `level` (1..3): 2 / 4 / 6.
        public static int StoreRunners(int level) => 2 * Mathf.Clamp(level, 1, 3);

        /// Extra barrow armfuls and jog multiplier a store hut at `level`
        /// gives every runner on its island; level 0 = none standing, level 1
        /// none either. Static so the upgrade row can quote the NEXT level's.
        public static (int extraArmfuls, float speedMul) PerksAt(int level)
        {
            if (level <= 1) return (0, 1f);
            if (level == 2) return (1, 1.1f);
            return (2, 1.2f);
        }

        /// **The camp-wide runner perks right now** (`PerksAt` of the highest
        /// store hut standing here). Read by the barrow (`CarryArmful`), the
        /// books (`WalkSpeedOf`) and the body (`CampWorker` -> `VillagerActing
        /// .BarrowSpeedMul`) alike, so a watched and an unwatched camp agree.
        public (int extraArmfuls, float speedMul) RunnerPerks() => PerksAt(StoreHutLevel());

        /// The highest level of any store hut standing here, 0 when none.
        public int StoreHutLevel() =>
            CountBuilt(StorageId) > 0 ? Mathf.Max(1, LevelOf(StorageId)) : 0;

        /// **What raising a store hut to `toLevel` adds**, for the upgrade
        /// row and its goal ("+2 runner posts · barrow +1 armful · jog 10%
        /// faster"); null for any other plan. Only what is NEW at that level.
        public static string RunnerUpgradeWords(string planId, int toLevel)
        {
            if (planId != StorageId || toLevel < 2) return null;
            int posts = StoreRunners(toLevel) - StoreRunners(toLevel - 1);
            var (a0, s0) = PerksAt(toLevel - 1);
            var (a1, s1) = PerksAt(toLevel);
            string w = $"+{posts} runner posts";
            if (a1 > a0) w += a1 == 1 ? " · barrow +1 armful" : $" · barrow +{a1} armfuls";
            if (s1 > s0 + 0.001f) w += $" · jog {Mathf.RoundToInt((s1 - 1f) * 100f)}% faster";
            return w;
        }

        /// "barrow +1 armful · jog 10% faster" -- a perk for the UI, or "".
        public static string PerkWords(int level)
        {
            var (n, mul) = PerksAt(level);
            string a = n <= 0 ? null : n == 1 ? "barrow +1 armful" : $"barrow +{n} armfuls";
            int pct = Mathf.RoundToInt((mul - 1f) * 100f);
            string b = pct > 0 ? $"jog {pct}% faster" : null;
            if (a != null && b != null) return a + " · " + b;
            return a ?? b ?? "";
        }

        /// The refusal for one runner too many.
        public const string RunnersFullReason = "Every barrow at this store hut is taken";

        /// **Quanta a bench may stand idle waiting on a runner** before its
        /// worker fetches for himself (0.02 work-day each, ~3.6 s of the
        /// day). Tunable; the fallback that keeps a busy island from
        /// stalling a station.
        public const int RunnerFallbackQuanta = 10;

        /// The last game-hours of the work day, when a rack goes home
        /// whatever is on it (rule 2).
        public const float LastWorkHours = 1f;

        // --- the API (the UI codes against exactly this) -------------------------

        /// A plan whose Work hands are runners: the store hut.
        public static bool IsRunnerPost(string planId) => planId == StorageId;

        /// A hand on the Work order at a store hut.
        public static bool IsRunner(OutpostHand h) =>
            h != null && h.order == OutpostOrder.Work && IsRunnerPost(h.target);

        /// Runner places over every store hut standing here.
        public int RunnerSlots() => RunnerSlotsAt(StorageId);

        /// Runner places over every standing copy of a runner plan (the
        /// Hand's "Runner" tile counts them).
        public int RunnerSlotsAt(string planId)
        {
            if (!IsRunnerPost(planId)) return 0;
            int n = CountBuilt(planId), k = 0;
            for (int o = 0; o < n; o++) k += StationCapacity(planId, o);
            return k;
        }

        /// A store hut stands here: runners have a post.
        bool AnyRunnerPost => CountBuilt(StorageId) > 0;

        /// Hands on the runner order right now.
        public int RunnerCount()
        {
            int n = 0;
            if (hands != null)
                foreach (var h in hands) if (IsRunner(h)) n++;
            return n;
        }

        /// **Hands with "No job"**: up and not busy (downed, pouting,
        /// rescuing, the raid), not runners, carrying nothing, with no job
        /// -- no order, or a gatherer told nothing. **Not the player's
        /// reserve, not a builder waiting (2026-10-03, villager review group
        /// 3):** the reserve is the player's own choice (the chip nagged him
        /// to undo it), and a builder with no plot is held up for a material
        /// -- "Builder — waiting for stone", whose fix is the material
        /// ("Builders short of stone"), not a new job. Same set as the
        /// status word `NoJobWord`.
        public int IdleCount()
        {
            int n = 0;
            if (hands != null)
                foreach (var h in hands) if (TrulyIdle(h)) n++;
            return n;
        }

        /// The same set as `IdleCount`, in the hand list's order (stable,
        /// for "tap to cycle").
        public IEnumerable<OutpostHand> IdleHands()
        {
            if (hands == null) yield break;
            foreach (var h in hands) if (TrulyIdle(h)) yield return h;
        }

        /// A runner with no chore right now (waiting at the store).
        public bool RunnerWaiting(OutpostHand h) => IsRunner(h) && !h.Hauling && !h.Busy;

        /// **His load rides in a barrow** (for the villager mime): a runner
        /// carrying anything but his own meal.
        public bool OnBarrow(OutpostHand h) => IsRunner(h) && h.Hauling && !h.eating;

        /// What this hand takes in one trip of `res`: a runner's barrow, or
        /// an armful.
        /// A runner's barrow grows with the store hut's level (`RunnerPerks`,
        /// 2026-10-04).
        public int CarryArmful(OutpostHand h, string res) =>
            IsRunner(h) ? Res.BarrowArmful(res, RunnerPerks().extraArmfuls) : Res.Armful(res);

        /// **A runner's jog in m/s, books and body alike** (2026-10-03): the
        /// barrow pace times the store hut's perk, held under the walk
        /// clip's rate window even on a road (`VillagerGaits.BarrowAt`).
        public float RunnerJogSpeed() => VillagerGaits.BarrowAt(RunnerPerks().speedMul);

        bool TrulyIdle(OutpostHand h)
        {
            if (h == null || h.Busy || h.downed || IsRunner(h) || h.Hauling) return false;
            switch (h.order)
            {
                case OutpostOrder.Idle: return !Reserve(h);
                case OutpostOrder.Gather: return string.IsNullOrEmpty(h.target);
                default: return false;
            }
        }

        // --- the island has runners ---------------------------------------------

        /// Runners able to run this quantum (`CountRunners`, once a `Step`).
        [System.NonSerialized] int activeRunners;

        /// At least one runner is up and his post stands: the rest of
        /// the camp leaves the hauling to the runners.
        bool RunnersOn => activeRunners > 0;

        /// May this hand START a haul? A runner always; anybody else only on
        /// an island with no runner.
        bool MayHaul(OutpostHand h) => IsRunner(h) || !RunnersOn;

        void CountRunners()
        {
            activeRunners = 0;
            if (hands == null || !AnyRunnerPost) return;
            // His OWN post must stand (2026-10-03: a runner whose post was
            // taken down is no runner on the ground).
            foreach (var h in hands)
                if (IsRunner(h) && !h.Busy && CountBuilt(h.target) > 0) activeRunners++;
        }

        // --- the runner's day ----------------------------------------------------

        /// **A runner's share of the quantum**: the load in his barrow
        /// first, then the backhaul, the player's transfers, and the chore
        /// ladder (`FindHaulerChore`), trip after trip; nothing to carry =
        /// he goes back to the store and waits there.
        void RunnerDay(OutpostHand h, ref float budget)
        {
            if (CountBuilt(h.target) <= 0) { if (h.Hauling) AdvanceHaul(h, ref budget); return; }
            // A planned trip to or from a station now known to be cut off
            // is given back before it is walked (2026-10-05, the store
            // front runner loop): live or catching up, the same books.
            DropCutOffTrip(h);
            for (int guard = 0; guard < 64 && budget > Eps; guard++)
            {
                if (h.Hauling)
                {
                    if (AdvanceHaul(h, ref budget)) continue;
                    // **A runner never stands holding a load the store has
                    // no room for (2026-10-02).** His trip reserved the room,
                    // but something else (a harvest, a hunt, a ship landing)
                    // can fill it first; `DepositHaul` gave back what the
                    // source would take and the rest would sit in his barrow
                    // "until room comes" while every bench waited on him. He
                    // sets it down by the store instead -- a dropped load,
                    // physical and visible, that the ladder's dropped-load
                    // rung brings in once there is room -- and runs on.
                    // Dormant since 2026-10-03 (Kevin, infinite stacking):
                    // the island store takes every load, so this only fires
                    // on bare ground with no store at all.
                    if (WaitingAtStore(h) && h.haulTo == HaulPlace.Store && !h.eating && h.haulPicked
                        && RoomFor(h.haulRes) <= 0)
                    {
                        AddGroundLoad(h.haulRes, h.haulCount, HandAt(h));
                        ClearHaul(h);
                        continue;
                    }
                    break;
                }
                if (StartBackhaul(h)) continue;
                // **The ship waits on the benches (2026-10-03, review fix
                // D).** Transfers used to come before the whole ladder, so a
                // "transfer all" took every runner for as long as the hold
                // had cargo and every bench starved. Now, while a manned
                // station needs a runner NOW (`StationsUrgent`: rung 1a or
                // 1b), at most ONE runner is on the gangway at a time, and he
                // never takes two transfer trips in a row -- so a lone
                // runner alternates: the station's load, then an armful for
                // the ship, then the station's. No urgent station = every
                // runner may transfer, as before.
                bool urgent = StationsUrgent(h);
                bool mayTransfer = !urgent
                    || (!transferredLast.Contains(h) && !OtherRunnerOnTransfer(h));
                if (mayTransfer && StartTransferTrip(h)) { transferredLast.Add(h); continue; }
                transferredLast.Remove(h);
                if (FindHaulerChore(h, out var c)) { BeginChore(h, c); continue; }
                // Nothing to carry: wait at HIS post -- the store hut copy he
                // is posted at, the copy his body walks to
                // (`CampWorker.TickWorkAt` -> `RunnerWaitSpot`). It was the
                // first store standing, which put a second hut's runners in
                // the books at the wrong door.
                if (RunnerPostAt(h, out var sAt) || StoreAt(out sAt)) WalkTo(h, sAt, ref budget, WorkFactor(h));
                break;
            }
        }

        /// Where this runner's own post stands (his copy of his plan).
        bool RunnerPostAt(OutpostHand h, out Vector3 at)
        {
            at = default;
            if (!IsRunner(h)) return false;
            return PlanPlace(h.target, Mathf.Max(0, OrdinalOfHand(h)), out at);
        }

        /// Runners whose last trip was a transfer armful (`RunnerDay`'s
        /// alternation). Not saved: a reload just starts the alternation
        /// over.
        [System.NonSerialized] readonly HashSet<OutpostHand> transferredLast = new HashSet<OutpostHand>();

        /// Another runner is walking a transfer armful (store <-> ship).
        bool OtherRunnerOnTransfer(OutpostHand h)
        {
            if (hands == null) return false;
            foreach (var o in hands)
                if (o != null && o != h && IsRunner(o) && o.Hauling && !o.eating
                    && (o.haulTo == HaulPlace.Ship || o.haulFrom == HaulPlace.Ship))
                    return true;
            return false;
        }

        /// **A manned station needs this runner now** -- the ladder's rung
        /// 1a (a selected spot's bay short of one batch, net of loads
        /// walking in, with stock he could bring) or 1b (a rack blocking a
        /// manned bench that he could take away). Only what HE could do: a
        /// bay short of something the camp does not have is no reason to
        /// keep the ship waiting (her cargo may be the very thing).
        bool StationsUrgent(OutpostHand h)
        {
            int ns = stations != null ? stations.Count : 0;
            if (ns == 0) return false;
            Vector3 at = HandAt(h);
            Chore c = default;
            float best = float.MaxValue;
            int bestAge = -1;
            bool found = false;
            for (int i = 0; i < ns; i++)
                if (BayChore(h, i, true, at, true, ref c, ref best, ref bestAge, ref found)) return true;
            for (int i = 0; i < ns; i++)
                if (Manned(stations[i]) && RackBlocking(stations[i]) && RackChore(i, out _, h, true)) return true;
            return false;
        }

        /// **Rule 4, the backhaul**: he just filled this station's bay, so
        /// its rack rides back with him -- any amount, to where it is wanted
        /// (`RackDest`), if there is any and somewhere has room.
        bool StartBackhaul(OutpostHand h)
        {
            int i = h.backhaulStation - 1;
            h.backhaulStation = 0;
            if (i < 0 || stations == null || i >= stations.Count) return false;
            if (!RackChore(i, out var c, h, true)) return false;
            BeginChore(h, c);
            return true;
        }

        // --- cut off from the store (2026-10-05) --------------------------------
        //
        // **Kevin's Day 853 save: the fishing hut and pier stand outside the
        // palisade, below a cliff band, and nothing walkable joins them to
        // the inside.** The camp already said "Walled off · needs a gate",
        // but runners were still booked trips to and from the fishing hut;
        // each one stood at the store hut with no route until the body gave
        // the trip up, and the next was booked at once -- "the store front
        // runner loop". Kevin's fix:
        //
        //  1. hauls skip a station with no route from the store hut
        //     (`StationReachable`, gated in every rung that names a station:
        //     `BayChore`, `RackChore`, `RackDest`, `BayShortFor`,
        //     `SiteChore`'s racks, rung 5, `RunnerNeed`, `RackOutbound`, the
        //     worker's own store fetch), re-checked when the walls, gates or
        //     buildings change (`Outpost.SaveStationReach`);
        //  2. a hauler whose walk has had no route for ~5 s gives the trip
        //     back (`HaulNoRoute`): nothing moves, and the station is
        //     skipped until the walls change;
        //  3. the station's worker says "cut off by the wall · needs a gate"
        //     when his own walk there has no route (`CampWorker`) -- and
        //     stays posted (assignments are permanent);
        //  4. the station's stall line says "walled off from the store"
        //     (`StationStallCause`), beside the camp's walled-off chip.
        //
        // **Unwatched / no grid**: nobody marks anything, so every station is
        // reachable (the old behaviour) until a grid answers; marks made
        // while watched stay on the rows, so the time-away catch-up skips
        // what the last look said was cut off (D2: the same gates, read by
        // the same `Step`).

        /// **Can the store's people walk to station `si`?** True when
        /// nothing has said otherwise (no grid, a probe's ledger, an index
        /// out of range).
        public bool StationReachable(int si) =>
            stations == null || si < 0 || si >= stations.Count || StationReachable(stations[si]);

        public bool StationReachable(StationStock s) => s == null || !(s.cutOffMap || s.cutOffBody);

        /// The stall line of a station the store cannot reach, or null:
        /// `MissingWords.WalledOffFromStore` when a wall is the cause, else
        /// `MissingWords.NoWayFromStore`.
        public string StationCutOffWords(StationStock s)
        {
            if (StationReachable(s)) return null;
            return s.cutOffWalled || s.cutOffBody ? Economy.MissingWords.WalledOffFromStore : Economy.MissingWords.NoWayFromStore;
        }

        /// **The scene's answer for station `si`** (`Outpost.SaveStationReach`):
        /// whether the store hut's door routes to its bays, whether a wall is
        /// what stops it, and the wall + building revision it was asked
        /// under. A new revision also lifts a body's mark (`HaulNoRoute`):
        /// the walls changed, so the station is tried again.
        public void SetStationReach(int si, bool reachable, bool walled, int rev)
        {
            if (stations == null || si < 0 || si >= stations.Count) return;
            var s = stations[si];
            if (s == null) return;
            if (s.reachRev != rev) s.cutOffBody = false;
            s.reachRev = rev;
            s.cutOffMap = !reachable;
            s.cutOffWalled = !reachable && walled;
        }

        /// **A body's walk for this hand's trip has had no route for
        /// `CampWorker.HandBackSeconds`** (part 2 of Kevin's fix). Before
        /// the pickup the trip is given back exactly as a cancelled plan
        /// (`CancelPlanned`: nothing was taken, so nothing moves; a transfer
        /// order gets its units back) and a station pickup is marked cut off
        /// until the walls or buildings change. After the pickup only the
        /// destination station is marked: the load stays with him and the
        /// body's existing give-up (`DropCarriedLoadNow`) puts it down where
        /// he stands, still booked. A trip to or from his OWN station (the
        /// fisher's catch) never marks it -- his post is not a runner's
        /// chore. True = the trip was given back.
        public bool HaulNoRoute(OutpostHand h)
        {
            if (h == null || !h.Hauling || h.eating) return false;
            var own = StationOfHand(h);
            if (!h.haulPicked)
            {
                bool stationPick = h.haulFrom == HaulPlace.Station && !OwnStation(own, h.haulFromStation);
                if (!stationPick && !IsRunner(h)) return false;
                if (stationPick) MarkCutOff(h.haulFromStation);
                CancelPlanned(h);
                return true;
            }
            if (h.haulTo == HaulPlace.Station && !OwnStation(own, h.haulToStation)) MarkCutOff(h.haulToStation);
            return false;
        }

        bool OwnStation(StationStock own, int si) =>
            own != null && stations != null && si >= 0 && si < stations.Count && ReferenceEquals(stations[si], own);

        void MarkCutOff(int si)
        {
            if (stations == null || si < 0 || si >= stations.Count || stations[si] == null) return;
            stations[si].cutOffBody = true;
        }

        /// **A planned trip an invisible walker must not start walking**: a
        /// runner's or spare hand's haul booked before its station was known
        /// to be cut off (or before the walls closed), not picked up yet --
        /// given back (`CancelPlanned`: nothing was taken, nothing moves).
        /// A load already picked up is left to the existing logic, as the
        /// body's is. Bodies settle their own trips (`CampWorker` ->
        /// `HaulNoRoute`); a station worker's trip to or from his own
        /// station is his post, left alone.
        void DropCutOffTrip(OutpostHand h)
        {
            if (h == null || !h.Hauling || h.haulPicked || h.driven || h.eating || stations == null) return;
            var own = StationOfHand(h);
            bool fromCut = h.haulFrom == HaulPlace.Station && !StationReachable(h.haulFromStation)
                           && !OwnStation(own, h.haulFromStation);
            bool toCut = h.haulTo == HaulPlace.Station && !StationReachable(h.haulToStation)
                         && !OwnStation(own, h.haulToStation);
            if (fromCut || toCut) CancelPlanned(h);
        }

        // --- aging: the longest-waiting request first ---------------------------

        /// **Quanta of waiting that lift a request one step up its rung
        /// (2026-10-03, Kevin's villager review, group 4: "the longer a
        /// blueprint or bay waits, the higher it climbs in the runners'
        /// list, so nothing starves forever").** Within one rung of
        /// `FindHaulerChore` the chore whose request has waited the most
        /// whole `AgeStepQuanta` wins; equal steps fall back to the nearest
        /// pickup, as before. 5 quanta = 0.1 work-day (~18 s of a 180 s
        /// work day): two bays that went short together still go nearest
        /// first, but the far sawmill whose bay has stood short while the
        /// near one was refilled three times now beats it. Coarse on
        /// purpose: an exact "oldest first" would send a barrow across the
        /// island for a 0.001-day difference. The rung order itself is
        /// untouched (an idle bench before a dropped load before a blocking
        /// rack ... before pre-stocking). Tunable.
        public const int AgeStepQuanta = 5;

        /// Steps taken by this ledger since it was loaded: the aging clock.
        /// Not saved (nor are the "since" marks below): a reload starts every
        /// request at zero wait, which only means one round of nearest-first.
        [System.NonSerialized] int haulStepNo;

        /// The step a bay row (station, resource) went SHORT of one batch of
        /// a selected recipe (rung 1a's test, net of loads walking in).
        [System.NonSerialized] readonly Dictionary<(StationStock, string), int> bayShortSince =
            new Dictionary<(StationStock, string), int>();
        /// The step a bay row last had room to pre-stock (rung 3).
        [System.NonSerialized] readonly Dictionary<(StationStock, string), int> bayRoomSince =
            new Dictionary<(StationStock, string), int>();
        /// The step a station's rack last went from empty to holding
        /// something (rungs 1b and 4).
        [System.NonSerialized] readonly Dictionary<StationStock, int> rackSince =
            new Dictionary<StationStock, int>();
        /// Reused by `AgeHaulRequests` to drop stale marks (allocation-free).
        [System.NonSerialized] readonly List<(StationStock, string)> staleBays = new List<(StationStock, string)>(8);
        [System.NonSerialized] readonly List<StationStock> staleRacks = new List<StationStock>(4);
        /// Marks touched this step, so the stale ones can be dropped.
        [System.NonSerialized] readonly HashSet<(StationStock, string)> seenShort = new HashSet<(StationStock, string)>();
        [System.NonSerialized] readonly HashSet<(StationStock, string)> seenRoom = new HashSet<(StationStock, string)>();

        /// **Once a `Step`**: advance the aging clock and mark when every
        /// manned bay row went short / got room and every rack got goods;
        /// a request that is met loses its mark, so the next time it starts
        /// at zero. O(stations x spots x inputs), allocation-free after the
        /// first steps (the dictionaries keep their buckets).
        void AgeHaulRequests()
        {
            haulStepNo++;
            seenShort.Clear();
            seenRoom.Clear();
            int ns = stations != null ? stations.Count : 0;
            for (int i = 0; i < ns; i++)
            {
                var s = stations[i];
                if (s == null) continue;
                if (s.RackTotal > 0) { if (!rackSince.ContainsKey(s)) rackSince[s] = haulStepNo; }
                else rackSince.Remove(s);
                if (!Manned(s)) continue;
                s.EnsureSpotRows();
                foreach (var sp in s.spots)
                {
                    var r = sp != null && sp.Selected ? sp.Recipe : null;
                    if (r == null) continue;
                    foreach (var line in r.takes)
                    {
                        if (line.n <= 0) continue;
                        var key = (s, line.res);
                        int have = s.BayCount(line.res) + InFlightTo(HaulPlace.Station, i, line.res);
                        if (have < line.n)
                        {
                            seenShort.Add(key);
                            if (!bayShortSince.ContainsKey(key)) bayShortSince[key] = haulStepNo;
                        }
                        if (have < s.InputCap)
                        {
                            seenRoom.Add(key);
                            if (!bayRoomSince.ContainsKey(key)) bayRoomSince[key] = haulStepNo;
                        }
                    }
                }
            }
            DropStale(bayShortSince, seenShort);
            DropStale(bayRoomSince, seenRoom);
            staleRacks.Clear();
            foreach (var kv in rackSince)
                if (kv.Key == null || kv.Key.removed || stations == null || !stations.Contains(kv.Key)) staleRacks.Add(kv.Key);
            foreach (var k in staleRacks) rackSince.Remove(k);
        }

        void DropStale(Dictionary<(StationStock, string), int> marks, HashSet<(StationStock, string)> seen)
        {
            staleBays.Clear();
            foreach (var kv in marks) if (!seen.Contains(kv.Key)) staleBays.Add(kv.Key);
            foreach (var k in staleBays) marks.Remove(k);
        }

        /// Whole `AgeStepQuanta` this bay row has waited (0 = unmarked).
        int BayAge(StationStock s, string res, bool urgent)
        {
            var marks = urgent ? bayShortSince : bayRoomSince;
            return marks.TryGetValue((s, res), out int since) ? (haulStepNo - since) / AgeStepQuanta : 0;
        }

        /// Whole `AgeStepQuanta` this station's rack has held goods.
        int RackAge(StationStock s) =>
            s != null && rackSince.TryGetValue(s, out int since) ? (haulStepNo - since) / AgeStepQuanta : 0;

        /// **How long a bench has stood waiting on the barrows**, in quanta
        /// (`benchWait`, ticked by its worker): the Problems list names a
        /// station past `RunnerSlowQuanta` ("Sawmill · waiting on the
        /// runners for logs"), whose fix is more runners.
        public int BenchWaitQuanta(StationStock s) =>
            s != null && benchWait.TryGetValue(s, out int n) ? n : 0;

        /// Quanta a bench may wait on the runners before the camp's Problems
        /// list names it (2026-10-03, group 4): 15 = 0.3 work-day, ~54 s of
        /// a 180 s work day. A barrow on its way is normal; this long means
        /// the runners cannot keep up. Tunable.
        public const int RunnerSlowQuanta = 15;

        /// **What a bench has waited on the runners for too long, or null**
        /// (2026-10-03, group 4, the camp's Problems list): runners are on,
        /// the bench has stood idle `RunnerSlowQuanta` with something a
        /// runner could bring or take (`benchWait`), and this is it.
        public string RunnersSlowFor(StationStock s)
        {
            if (!RunnersOn || s == null || stations == null || BenchWaitQuanta(s) < RunnerSlowQuanta) return null;
            int si = stations.IndexOf(s);
            return si < 0 ? null : RunnerWaitItem(s, si);
        }

        // --- the bench waiting on a runner ---------------------------------------

        /// Quanta each station's bench has stood idle with something a
        /// runner could bring (or take). Not saved: a reload waits again.
        [System.NonSerialized] readonly Dictionary<StationStock, int> benchWait =
            new Dictionary<StationStock, int>();

        /// Once a quantum per stationed worker (`StepStations`).
        void TickBenchWait(OutpostHand h, StationStock s, int si)
        {
            if (!RunnersOn || h.Hauling || !BenchWantsRunner(s, si)) { benchWait.Remove(s); return; }
            benchWait.TryGetValue(s, out int n);
            benchWait[s] = n + 1;
        }

        /// An idle bench with something for a runner to bring or take (the
        /// fisher's: his box full, the one thing a runner does for him).
        bool BenchWantsRunner(StationStock s, int si)
        {
            if (s == null) return false;
            // A station the store cannot walk to waits on no runner
            // (2026-10-05): none is coming. Its stall line says why
            // (`StationStallCause` -> "walled off from the store").
            if (!StationReachable(si)) return false;
            // Only a box a runner can take somewhere (2026-10-02 play check:
            // Finch read "rack full · runners taking it away" while the store
            // was full of fish and the runners stood at the store with
            // nothing to carry). A box with nowhere to go is the store's
            // stall ("rack and store are full of fish"), not a runner's.
            if (FishesAtShore(s)) return RackBlocking(s) && RackOutbound(s, si) != null;
            s.EnsureSpotRows();
            return !AnySpotBusy(s) && RunnerNeed(s, si) != null;
        }

        /// **The first rack row of station `si` a runner could take
        /// somewhere** (a bay, a site, or the store with room), or null.
        string RackOutbound(StationStock s, int si)
        {
            if (s == null || s.rack == null) return null;
            // Nobody can fetch it from a cut-off station (2026-10-05).
            if (!StationReachable(si)) return null;
            foreach (var row in s.rack)
            {
                if (row == null || row.whole <= 0) continue;
                int one = 1;
                if (RackDest(si, row.resource, ref one, out _, out _, out _)) return row.resource;
            }
            return null;
        }

        /// What most of a rack holds (its biggest row), else what a finished
        /// bench is holding; null when both are empty. "Store full of fish".
        static string RackHeldMost(StationStock s)
        {
            if (s == null) return null;
            string most = null;
            int n = 0;
            if (s.rack != null)
                foreach (var row in s.rack)
                    if (row != null && row.whole > n && !string.IsNullOrEmpty(row.resource)) { n = row.whole; most = row.resource; }
            if (most != null) return most;
            s.EnsureSpotRows();
            foreach (var sp in s.spots)
                if (sp != null && sp.benchState == BenchState.Finished && sp.Recipe != null) return sp.Recipe.makes;
            return null;
        }

        /// **Does this station's worker do his own fetching?** Only on an
        /// island with no runner. **With a runner, never (2026-10-02,
        /// Kevin):** *"DESPITE HAVING TWO RUNNERS ... hunting lodge assignee
        /// still went straight to the storage house to get the resource."*
        /// That was the `RunnerFallbackQuanta` fallback (b6242988): a bench
        /// idle ~10 quanta sent its worker to the store himself. Now he
        /// stays at his bench ("Waiting for a runner"); the runners'
        /// ladder puts an idle bench's input first (rung 1). He still
        /// moves his own rack to his own bay (`OwnRackFeed`, at his bench)
        /// and cuts a raw the store has none of (no runner cuts).
        /// `benchWait` / `RunnerFallbackQuanta` are kept only as a count.
        bool WorkerFetches(StationStock s) => !RunnersOn;

        /// **What a runner should bring this idle bench, or null**: its rack
        /// blocking it (the rack's first resource), else the first input a
        /// selected spot is short of that a runner could fetch (the store
        /// or another station's rack has it), net of loads walking in.
        string RunnerNeed(StationStock s, int si)
        {
            if (s == null) return null;
            // No runner can bring to or take from a cut-off station (2026-10-05).
            if (!StationReachable(si)) return null;
            s.EnsureSpotRows();
            // Only a rack something can take: a full store is the store's
            // stall ("store is full of boards"), not "waiting for a runner"
            // (play check 2026-10-02: sawyer and runner both waited for
            // minutes with nowhere to put the planks).
            if (RackBlocking(s))
            {
                string outbound = RackOutbound(s, si);
                if (outbound != null) return outbound;
            }
            if (RackJam(s) != null) return null;
            foreach (var sp in s.spots)
            {
                var r = sp != null && sp.Selected ? sp.Recipe : null;
                if (r == null || LockOf(s, r) != null || HoldOf(r) != null) continue;
                if (r.tool != null && HeldOf(r.tool) <= 0f) continue;
                foreach (var line in r.takes)
                {
                    if (line.n <= 0) continue;
                    if (s.BayCount(line.res) + InFlightTo(HaulPlace.Station, si, line.res) >= line.n) continue;
                    if (StoreFree(line.res) > 0) return line.res;
                    if (stations != null)
                        for (int j = 0; j < stations.Count; j++)
                            if (j != si && StationReachable(j) && RowFree(j, stations[j]?.Rack(line.res), false) > 0) return line.res;
                }
            }
            return null;
        }

        /// **What a waiting bench is waiting for**, for its worker's
        /// status ("waiting for hide · runners bringing it"): what a runner
        /// is walking in, else what one should bring (`RunnerNeed`).
        string RunnerWaitItem(StationStock s, int si)
        {
            if (hands != null)
                foreach (var h in hands)
                    if (IsRunner(h) && h.Hauling && h.haulTo == HaulPlace.Station && h.haulToStation == si)
                        return h.haulRes;
            return RunnerNeed(s, si);
        }

        /// A runner is walking something to station `si` right now.
        bool RunnerBound(int si)
        {
            if (hands == null) return false;
            foreach (var h in hands)
                if (IsRunner(h) && h.Hauling && h.haulTo == HaulPlace.Station && h.haulToStation == si) return true;
            return false;
        }

        /// **"Waiting for a runner"**: a stationed worker standing at an idle
        /// bench that a runner is bringing to, or should be.
        bool WaitingForRunner(OutpostHand h)
        {
            if (!RunnersOn || h == null || h.Hauling || h.order != OutpostOrder.Work || !IsStation(h.target)) return false;
            var s = StationOfHand(h);
            if (s == null || stations == null) return false;
            int si = stations.IndexOf(s);
            if (BenchWantsRunner(s, si)) return true;
            if (FishesAtShore(s)) return false;
            return !AnySpotBusy(s) && RunnerBound(si);
        }

        // --- full loads ----------------------------------------------------------

        /// **The smallest load worth pre-stocking a bay with**: the
        /// carrier's armful, or half the bay when the bay is smaller --
        /// never a 1-unit top-up.
        static int PrestockLoad(StationStock s, int armful) =>
            Mathf.Min(armful, Mathf.Max(1, s.InputCap / 2));

        /// **A rack worth carrying home**: the carrier's full load, or three
        /// quarters of a rack too small to hold one.
        static int RackLoad(StationStock s, int armful) =>
            Mathf.Min(armful, Mathf.Max(1, s.OutputCap * 3 / 4));

        /// The work day's last `LastWorkHours`: racks go home whatever is on
        /// them. False in a probe's hourless step.
        static bool LastWorkOfDay =>
            ActiveHourKnown && Life.CampLifeTuning.IsAwakeHour(ActiveHour)
            && ActiveHour >= Life.CampLifeTuning.EveningStartHour - LastWorkHours;

        /// **What full pile of the store is holding this hand up, or null**
        /// (2026-10-02, the camp's one "Store full of boards" chip): a
        /// stationed worker whose rack can go nowhere (`RackJam`, the
        /// "rack and store are full of boards" stall), a gatherer whose
        /// pile is at the ceiling, or anybody standing at the store with a
        /// load it has no room for. The fix for every one of them is more
        /// store (a store hut, or raising one).
        ///
        /// **Always null since 2026-10-03** (Kevin: "remove storage limits.
        /// infinite stacking is allowed"): nothing is ever held up by a full
        /// island store, so there is no chip to raise. Body kept for the day
        /// a cap returns.
        public string StoreFullFor(OutpostHand h)
        {
            if (!IslandStoreCapped) return null;
            if (h == null || h.downed || h.walkingIn) return null;
            if (h.Hauling && h.haulTo == HaulPlace.Store && WaitingAtStore(h)) return h.haulRes;
            if (h.order == OutpostOrder.Gather && GatherBlocked(h))
                return h.target == Res.Game ? Res.Meat : h.target;
            if (h.order == OutpostOrder.Work && IsStation(h.target) && !h.Hauling)
            {
                var s = StationOfHand(h);
                if (s == null) return null;
                s.EnsureSpotRows();
                if (AnySpotBusy(s)) return null;
                // Only a bench with work to do (`StationStallCause`'s test):
                // a full rack with no order given is that stall, not this.
                bool wants = false;
                foreach (var sp in s.spots)
                    if (sp != null && (sp.Selected || sp.benchState == BenchState.Finished)) { wants = true; break; }
                return wants ? RackJam(s) : null;
            }
            return null;
        }

        /// "6 boards to Sawmill": a runner's trip, for his status word.
        string RunWords(OutpostHand h)
        {
            string what = $"{h.haulCount} {ResLabel(h.haulRes).ToLowerInvariant()}";
            string to;
            switch (h.haulTo)
            {
                case HaulPlace.Store: to = "the store"; break;
                case HaulPlace.Ship: to = "the ship"; break;
                case HaulPlace.Station:
                {
                    var st = stations != null && h.haulToStation >= 0 && h.haulToStation < stations.Count
                        ? stations[h.haulToStation] : null;
                    to = st != null ? Capital(BuildPlans.Named(st.planId).label) : "a bench";
                    break;
                }
                case HaulPlace.Site:
                {
                    var site = SiteWanting(h.haulRes);
                    to = site != null ? Capital(BuildPlans.Named(site.planId).label) : "a blueprint";
                    break;
                }
                default: to = "the store"; break;
            }
            return $"Running {what} to {to}";
        }

        static string Capital(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
