using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// <summary>
    /// **Station spots, the economy half (Kevin, 2026-09-30, Melvor-style
    /// stations).** Every station has one or more spots (`StationSpots`),
    /// each with its own selected recipe and its own bench, all running AT
    /// THE SAME TIME under the station's ONE worker, each at its full rate
    /// (the worker's effort is not divided -- `WorkerDay`). The input bay and
    /// the output rack stay the station's and are SHARED by the spots, at
    /// full capacity (see `StationStock.spots`).
    ///
    /// **Select a recipe on a spot** (`SelectRecipe`): it runs until an input
    /// runs out or its output has nowhere to go, then AUTO-PAUSES with a
    /// reason (`SpotState.pauseReason`, worked out every step by
    /// `RefreshSpots`) and resumes by itself when the reason clears. **Stop**
    /// (`StopSpot`) clears the spot: an unfinished batch's inputs go back
    /// into the bay; a finished one still goes onto the rack. This replaces
    /// the per-station order with amount chips as the player-facing model;
    /// the legacy `PlaceOrder(count)` / queue API is kept for probes and old
    /// callers and lands on the recipe's own spot (a selected spot is "∞").
    ///
    /// Pause reasons, in the order they are checked:
    /// "no cook" / "no smith" / "no worker" (nobody works the station -- its
    /// spots do not advance); "needs the fire at II" / "needs the kitchen at
    /// level 2" (locked); "store full of X" (the rack is full and nothing on
    /// it can go to the store); the fishing hut's own shore/box reasons;
    /// "waiting for saw blade" (the tool is not in the pile); "waiting for
    /// onion" (an input is neither in the bay, on its way, in the store nor
    /// standing to be gathered -- including one another station makes: no
    /// auto-chaining). A batch already on the bench is never paused by an
    /// input or the store: it finishes.
    /// </summary>
    public partial class OutpostLedger
    {
        // --- the write API (the station screen) ---------------------------------

        /// **Select `recipeId` on spot `spotIndex` of station `st`**, to run
        /// until stopped. Refused (false, `refusal` says why) for a recipe
        /// of another station or another spot, or one locked by the fire's
        /// or this building's level. A missing tool or input is NOT a
        /// refusal: the spot is selected and pauses until it comes. A batch
        /// of a DIFFERENT recipe still unfinished on the spot's bench comes
        /// off it (inputs back to the bay) so the new one starts at once.
        public bool SelectRecipe(StationStock st, int spotIndex, string recipeId, out string refusal)
            => SetSpot(st, spotIndex, recipeId, 0, out refusal);

        /// **Stop spot `spotIndex`**: no recipe, and an unfinished batch's
        /// inputs go back into the bay. A finished batch stays on the bench
        /// until the rack takes it (it is made; nothing is lost).
        public void StopSpot(StationStock st, int spotIndex)
        {
            if (st == null) return;
            var sp = st.SpotAt(spotIndex);
            if (sp == null) return;
            sp.Stop();
            if (sp.BenchBusy) ReturnBenchInputs(st, sp);
            st.queue?.Clear();
            RefreshSpots(st);
        }

        /// The one door every selection goes through. `count` 0 = until
        /// stopped; > 0 = the legacy count order (`PlaceOrder`).
        bool SetSpot(StationStock st, int spotIndex, string recipeId, int count, out string refusal)
        {
            refusal = null;
            EnsureStations();
            if (st == null || !IsLive(st)) { refusal = "no such building"; return false; }
            var sp = st.SpotAt(spotIndex);
            if (sp == null) { refusal = "no such spot"; return false; }
            var r = Economy.Recipes.Named(recipeId);
            if (r == null) { refusal = "no such recipe"; return false; }
            if (r.station != st.planId)
            { refusal = $"not made at the {BuildPlans.Named(st.planId).label}"; return false; }
            string home = StationSpots.SpotOf(r);
            if (home != sp.spot) { refusal = $"made at the {home}"; return false; }
            string locked = LockOf(st, r);
            if (locked != null) { refusal = locked; return false; }
            if (sp.BenchBusy && sp.benchRecipe != r.id) ReturnBenchInputs(st, sp);
            sp.recipeId = r.id;
            sp.count = Mathf.Max(0, count);
            st.queue?.Clear();
            RefreshSpots(st);
            return true;
        }

        // --- reading the spots -----------------------------------------------------

        /// Fire level and this building's own level; null when neither locks it.
        string LockOf(StationStock st, Economy.Recipe r)
        {
            if (r == null) return "no such recipe";
            if (CampfireLevel < r.campfireLevel)
                return $"needs the fire at {Economy.RecipeGraph.Roman(r.campfireLevel)}";
            if (st != null && LevelOf(st.planId, st.ordinal) < r.stationLevel)
                return $"needs the {BuildPlans.Named(st.planId).label} at level {r.stationLevel}";
            return null;
        }

        /// Bench progress a day for one batch of `r` here at full pace: the
        /// recipe's rate at this building's level and the camp's priority,
        /// over its yield -- exactly what the single bench was paid.
        float BenchPerDay(StationStock st, Economy.Recipe r)
        {
            if (st == null || r == null) return 0f;
            float rate = r.ratePerDay * Economy.Techs.RateMul(st.planId, LevelOf(st.planId, st.ordinal))
                         * PriorityMultiplier(r.makes);
            return rate / Mathf.Max(1, r.yield);
        }

        /// **What jams the rack, or null.** Null while it has room, or while
        /// something on it can still go to the store (a hauler will make
        /// room). Otherwise the resource most of the rack is -- "store full
        /// of" that. Shared rack: one spot's output can jam another's.
        string RackJam(StationStock st)
        {
            if (st == null || st.RackRoom > 0) return null;
            string jam = null;
            int most = 0;
            if (st.rack != null)
                foreach (var row in st.rack)
                {
                    if (row == null || row.whole <= 0 || string.IsNullOrEmpty(row.resource)) continue;
                    if (StoreRoomNet(row.resource) > 0) return null;
                    if (row.whole > most) { most = row.whole; jam = row.resource; }
                }
            return jam;
        }

        static string Word(string res) =>
            string.IsNullOrEmpty(res) ? "supplies" : Economy.ResDefs.Label(res).ToLowerInvariant();

        /// The first input of `r` this station cannot get: not in the bay
        /// or on its way, none in the store, and not standing to be gathered.
        string MissingInput(StationStock st, int si, Economy.Recipe r)
        {
            foreach (var line in r.takes)
            {
                if (line.n <= 0) continue;
                if (st.BayCount(line.res) + InFlightTo(HaulPlace.Station, si, line.res) >= line.n) continue;
                if (StoreCountOf(line.res) > 0) continue;
                if (Res.IsGatherable(line.res) && line.res != Res.Game && FieldFree(line.res) > 0) continue;
                return line.res;
            }
            return null;
        }

        /// Why a selected spot is not running, or null (see the class doc).
        string SpotPause(StationStock st, int si, SpotState sp, OutpostHand worker)
        {
            if (!sp.Selected) return null;
            var r = sp.Recipe;
            if (r == null) return "recipe gone";
            if (worker == null) return "no " + StationSpots.WorkerNoun(st.planId);
            if (sp.BenchBusy) return null;
            string locked = LockOf(st, r);
            if (locked != null) return locked;
            string jam = RackJam(st);
            if (jam != null) return $"store full of {Word(jam)}";
            if (sp.benchState == BenchState.Finished) return null;   // unloading this step
            if (FishesAtShore(st)) return CatchStallCause(st);
            if (r.tool != null && HeldOf(r.tool) <= 0f) return $"waiting for {Word(r.tool)}";
            string miss = MissingInput(st, si, r);
            return miss != null ? $"waiting for {Word(miss)}" : null;
        }

        /// The Work hand dealt to this station, or null.
        OutpostHand WorkerAt(StationStock st)
        {
            if (st == null || hands == null) return null;
            foreach (var h in hands)
                if (h != null && h.order == OutpostOrder.Work && h.target == st.planId && StationOfHand(h) == st)
                    return h;
            return null;
        }

        /// **Work out every spot's pause reason and seconds left** on this
        /// station, and rewrite the legacy mirror. Pure reading otherwise:
        /// the books never depend on it (watched == unwatched).
        public void RefreshSpots(StationStock st)
        {
            if (st == null) return;
            st.EnsureSpotRows();
            RefreshSpots(st, stations != null ? stations.IndexOf(st) : -1, WorkerAt(st));
        }

        void RefreshSpots(StationStock st, int si, OutpostHand worker)
        {
            foreach (var sp in st.spots)
            {
                if (sp == null) continue;
                sp.pauseReason = SpotPause(st, si, sp, worker);
                var r = sp.BenchBusy ? sp.BenchRecipe : sp.Recipe;
                float perDay = BenchPerDay(st, r);
                if (perDay <= 0f || (!sp.Selected && !sp.BenchBusy)) { sp.SecondsLeft = 0f; continue; }
                float left = sp.BenchBusy ? Mathf.Clamp01(1f - sp.progress01) : 1f;
                float pace = worker != null ? WorkFactor(worker) : 0f;
                if (pace <= 0f) pace = 1f;          // off-hours / nobody: the full-pace figure
                sp.SecondsLeft = left / perDay / pace * TimeOfDay.WorkDaySeconds;
            }
            st.SyncLegacy();
        }

        [System.NonSerialized] readonly List<OutpostHand> spotWorkers = new List<OutpostHand>();

        /// Every station's spots, once per step (end of `StepStations`).
        void RefreshAllSpots()
        {
            if (stations == null) return;
            spotWorkers.Clear();
            for (int i = 0; i < stations.Count; i++) spotWorkers.Add(null);
            if (hands != null)
                foreach (var h in hands)
                {
                    if (h == null || h.order != OutpostOrder.Work || !IsStation(h.target)) continue;
                    int i = stations.IndexOf(StationOfHand(h));
                    if (i >= 0 && spotWorkers[i] == null) spotWorkers[i] = h;
                }
            for (int i = 0; i < stations.Count; i++)
            {
                var st = stations[i];
                if (st == null) continue;
                st.EnsureSpotRows();
                RefreshSpots(st, i, spotWorkers[i]);
            }
        }

        // --- a spot's bench -----------------------------------------------------------

        /// A loaded/working batch comes off: its inputs go back into the bay
        /// (over the cap if need be -- they exist; an idle hand walks any
        /// the station no longer wants home).
        void ReturnBenchInputs(StationStock st, SpotState sp)
        {
            var r = sp.BenchRecipe;
            if (r != null)
                foreach (var line in r.takes)
                    if (line.n > 0) st.Bay(line.res, true).whole += line.n;
            sp.EmptyBench();
        }

        /// A finished batch onto the (shared) rack as far as it has room.
        /// True when the spot's bench is clear.
        static bool UnloadSpot(StationStock st, SpotState sp)
        {
            if (sp.benchState != BenchState.Finished) return sp.benchState == BenchState.Empty;
            string makes = sp.BenchMakes;
            if (makes == null || sp.benchOut <= 0) { sp.EmptyBench(); return true; }
            int move = Mathf.Min(st.RackRoom, sp.benchOut);
            if (move > 0) { st.Rack(makes, true).whole += move; sp.benchOut -= move; }
            if (sp.benchOut <= 0) { sp.EmptyBench(); return true; }
            return false;
        }

        /// **The spot's next batch onto its bench**: its recipe selected and
        /// unlocked, the tool in the pile, the rack not jammed (the
        /// auto-pause on a full store: nothing is started that has nowhere
        /// to go), and every input in the (shared) bay.
        bool TryLoadSpot(StationStock st, SpotState sp)
        {
            if (sp.benchState != BenchState.Empty || !sp.Selected) return false;
            var r = sp.Recipe;
            if (r == null) { sp.Stop(); return false; }
            if (LockOf(st, r) != null) return false;
            if (r.tool != null && HeldOf(r.tool) <= 0f) return false;
            if (RackJam(st) != null) return false;
            foreach (var line in r.takes)
                if (line.n > 0 && st.BayCount(line.res) < line.n) return false;
            foreach (var line in r.takes)
                if (line.n > 0) st.Bay(line.res).whole -= line.n;
            sp.benchRecipe = r.id;
            sp.benchState = BenchState.Loaded;
            sp.progress01 = 0f;
            sp.benchOut = 0;
            return true;
        }

        void FinishSpotJob(StationStock st, SpotState sp, Economy.Recipe r)
        {
            int yield = Mathf.Max(1, r.yield);
            sp.benchState = BenchState.Finished;
            sp.progress01 = 1f;
            sp.benchOut = yield;
            if (r.tool != null && r.toolWear > 0f) DrawHeld(r.tool, r.toolWear * yield);
            away.Add(r.makes, yield);
            CountDown(sp, r, yield);
            UnloadSpot(st, sp);
        }

        /// A legacy count order runs down; at zero the spot clears itself.
        static void CountDown(SpotState sp, Economy.Recipe r, int made)
        {
            if (sp == null || r == null || sp.recipeId != r.id || sp.count <= 0) return;
            sp.count -= made;
            if (sp.count <= 0) sp.Stop();
        }

        /// **The worker fetches for a spot that cannot load** (an empty
        /// spot short of an input): from the store, else -- a gatherable raw
        /// the store has none of -- off the island, straight into the bay.
        /// An input made at another station and not in the store is not
        /// fetched (no auto-chaining); nor is anything for a spot whose
        /// output has nowhere to go.
        ///
        /// `fieldOnly` (2026-10-02, runners on the island): the store is the
        /// runners' to bring from -- he only goes for a gatherable raw the
        /// store has none of, which no runner would fetch.
        bool StartInputFetch(OutpostHand h, StationStock st, int si, bool fieldOnly = false)
        {
            if (RackJam(st) != null) return false;
            foreach (var sp in st.spots)
            {
                if (sp == null || !sp.Selected || sp.benchState != BenchState.Empty) continue;
                var r = sp.Recipe;
                if (r == null || LockOf(st, r) != null) continue;
                if (r.tool != null && HeldOf(r.tool) <= 0f) continue;
                foreach (var line in r.takes)
                {
                    if (line.n <= 0) continue;
                    int have = st.BayCount(line.res) + InFlightTo(HaulPlace.Station, si, line.res);
                    if (have >= line.n) continue;
                    int space = st.InputCap - have;
                    if (space <= 0) continue;
                    int inStore = StoreFree(line.res);
                    if (inStore > 0 && fieldOnly) continue;      // a runner's
                    if (inStore > 0)
                    {
                        int n = Mathf.Min(Res.Armful(line.res), Mathf.Min(space, inStore));
                        StartTimedTrip(h, line.res, n, HaulPlace.Store, -1, HaulPlace.Station, si);
                        return true;
                    }
                    // **The store has none: he gathers it himself** (Kevin's
                    // call) and his armful goes straight into his own bay.
                    if (Res.IsGatherable(line.res) && line.res != Res.Game)
                    {
                        int standing = FieldFree(line.res);
                        if (standing > 0)
                        {
                            int n = Mathf.Min(Res.Armful(line.res), Mathf.Min(space, standing));
                            StartTimedTrip(h, line.res, n, HaulPlace.Field, -1, HaulPlace.Station, si);
                            return true;
                        }
                    }
                    break;                   // made elsewhere, or none left here
                }
            }
            return false;
        }

        /// Does any selected spot's recipe take `res`? (What an idle hauler
        /// may fill the bay with, and what stays in it.)
        static bool SpotsWant(StationStock st, string res)
        {
            if (st?.spots == null) return false;
            foreach (var sp in st.spots)
                if (sp != null && sp.Selected && Wants(sp.Recipe, res)) return true;
            return false;
        }
    }
}
