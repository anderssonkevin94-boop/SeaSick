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
    /// any post. A store hut takes `RunnersPerStoreL1` at level 1 and
    /// `RunnersPerStoreL2` from level 2 (`StationCapacity`). Runners push
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

        /// Runners one store hut takes, at level 1 and from level 2.
        public const int RunnersPerStoreL1 = 2;
        public const int RunnersPerStoreL2 = 4;

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

        /// A hand on the Work order at the store hut.
        public static bool IsRunner(OutpostHand h) =>
            h != null && h.order == OutpostOrder.Work && h.target == BuildPlans.Storage.id;

        /// Runner places over every store hut standing here.
        public int RunnerSlots()
        {
            int n = CountBuilt(StorageId), k = 0;
            for (int o = 0; o < n; o++) k += StationCapacity(StorageId, o);
            return k;
        }

        /// Hands on the runner order right now.
        public int RunnerCount()
        {
            int n = 0;
            if (hands != null)
                foreach (var h in hands) if (IsRunner(h)) n++;
            return n;
        }

        /// **Hands truly idle**: up and not busy (downed, pouting,
        /// rescuing, the raid), not runners, carrying nothing, with no job
        /// -- no order, the player's own Idle (his reserve counts: they are
        /// idle too), a builder with no plot to work, a gatherer told
        /// nothing.
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
        public int CarryArmful(OutpostHand h, string res) =>
            IsRunner(h) ? Res.BarrowArmful(res) : Res.Armful(res);

        bool TrulyIdle(OutpostHand h)
        {
            if (h == null || h.Busy || h.downed || IsRunner(h) || h.Hauling) return false;
            switch (h.order)
            {
                case OutpostOrder.Idle: return true;
                case OutpostOrder.Build: return BuildSiteFor(h) == null;
                case OutpostOrder.Gather: return string.IsNullOrEmpty(h.target);
                default: return false;
            }
        }

        // --- the island has runners ---------------------------------------------

        /// Runners able to run this quantum (`CountRunners`, once a `Step`).
        [System.NonSerialized] int activeRunners;

        /// At least one runner is up and his store hut stands: the rest of
        /// the camp leaves the hauling to the runners.
        bool RunnersOn => activeRunners > 0;

        /// May this hand START a haul? A runner always; anybody else only on
        /// an island with no runner.
        bool MayHaul(OutpostHand h) => IsRunner(h) || !RunnersOn;

        void CountRunners()
        {
            activeRunners = 0;
            if (hands == null || CountBuilt(StorageId) <= 0) return;
            foreach (var h in hands)
                if (IsRunner(h) && !h.Busy) activeRunners++;
        }

        // --- the runner's day ----------------------------------------------------

        /// **A runner's share of the quantum**: the load in his barrow
        /// first, then the backhaul, the player's transfers, and the chore
        /// ladder (`FindHaulerChore`), trip after trip; nothing to carry =
        /// he goes back to the store and waits there.
        void RunnerDay(OutpostHand h, ref float budget)
        {
            if (CountBuilt(StorageId) <= 0) { if (h.Hauling) AdvanceHaul(h, ref budget); return; }
            for (int guard = 0; guard < 64 && budget > Eps; guard++)
            {
                if (h.Hauling) { if (!AdvanceHaul(h, ref budget)) break; continue; }
                if (StartBackhaul(h)) continue;
                if (StartTransferTrip(h)) continue;
                if (FindHaulerChore(h, out var c)) { BeginChore(h, c); continue; }
                // Nothing to carry: wait at the store.
                if (StoreAt(out var sAt)) WalkTo(h, sAt, ref budget, WorkFactor(h));
                break;
            }
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
            if (FishesAtShore(s)) return RackBlocking(s);
            s.EnsureSpotRows();
            return !AnySpotBusy(s) && RunnerNeed(s, si) != null;
        }

        /// **Does this station's worker do his own fetching?** Always on an
        /// island with no runner; with runners, only once his bench has
        /// waited `RunnerFallbackQuanta`.
        bool WorkerFetches(StationStock s) =>
            !RunnersOn || (s != null && benchWait.TryGetValue(s, out int n) && n >= RunnerFallbackQuanta);

        /// **What a runner should bring this idle bench, or null**: its rack
        /// blocking it (the rack's first resource), else the first input a
        /// selected spot is short of that a runner could fetch (the store
        /// or another station's rack has it), net of loads walking in.
        string RunnerNeed(StationStock s, int si)
        {
            if (s == null) return null;
            s.EnsureSpotRows();
            // Only a rack something can take: a full store is the store's
            // stall ("store is full of boards"), not "waiting for a runner"
            // (play check 2026-10-02: sawyer and runner both waited for
            // minutes with nowhere to put the planks).
            if (RackBlocking(s))
                foreach (var row in s.rack)
                {
                    if (row == null || row.whole <= 0) continue;
                    int one = 1;
                    if (RackDest(si, row.resource, ref one, out _, out _, out _)) return row.resource;
                }
            if (RackJam(s) != null) return null;
            foreach (var sp in s.spots)
            {
                var r = sp != null && sp.Selected ? sp.Recipe : null;
                if (r == null || LockOf(s, r) != null) continue;
                if (r.tool != null && HeldOf(r.tool) <= 0f) continue;
                foreach (var line in r.takes)
                {
                    if (line.n <= 0) continue;
                    if (s.BayCount(line.res) + InFlightTo(HaulPlace.Station, si, line.res) >= line.n) continue;
                    if (StoreFree(line.res) > 0) return line.res;
                    if (stations != null)
                        for (int j = 0; j < stations.Count; j++)
                            if (j != si && RowFree(j, stations[j]?.Rack(line.res), false) > 0) return line.res;
                }
            }
            return null;
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
