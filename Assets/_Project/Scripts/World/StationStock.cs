using UnityEngine;
using System.Collections.Generic;

namespace SeaSick.World
{
    /// What is on a station's bench right now (Astra's contract, Kevin
    /// 2026-09-23). A job starts only with its input in the bay; loading moves
    /// that input from the bay onto the bench; a job on the bench finishes
    /// even if the bay runs dry; a finished unit that the output rack has no
    /// room for stays on the bench and blocks the next job.
    public enum BenchState
    {
        /// Nothing on it.
        Empty,
        /// Inputs laid on, no work done yet.
        Loaded,
        /// Being worked: `StationStock.benchProgress` runs 0..1.
        Working,
        /// Done, waiting for rack room: `StationStock.benchOut` units of the
        /// recipe's output are sitting on it.
        Finished,
    }

    /// One end of a hand's haul trip.
    public enum HaulPlace
    {
        None,
        /// The camp store: the square by the fire, or the storage building
        /// once one stands (`OutpostLedger.HasStorageBuilding`).
        Store,
        /// A station's input bay or output rack; the index is into
        /// `OutpostLedger.Stations`.
        Station,
        /// The island itself: a stationed worker gathering his own raw.
        Field,
        /// A queued blueprint (`OutpostLedger.sites`), 2026-09-23: a
        /// builder's armful. Not tied to one row -- it lands in the oldest
        /// site short of it (`OutpostLedger.DeliverToSite`).
        Site,
        /// **The ship at this camp's landing, 2026-09-24** (Kevin: unload
        /// the ship to the island and back, *physically carried*). Its
        /// world point is the foot of the gangway (`ICargoSide.GangwayAt`):
        /// the pier ROOT when she lies at a camp pier -- `CampPath` has no
        /// pier deck, so a hand cannot path out to the head -- or the
        /// plank's landing on a beach. Appended LAST: saved as an int.
        Ship,
        /// **A load dropped on the ground (death/rescue phase 2,
        /// 2026-09-27)**: a hand who goes down or dies drops whatever he
        /// had picked up exactly where he stood. The index is into
        /// `OutpostLedger.groundLoads` (the same way `Station`'s index is
        /// into `Stations`). Appended LAST too, for the same save reason.
        Ground,
        /// **The fishing hut's spot at the water's edge (2026-09-30).**
        /// Kevin: *"I want the fisherman to walk to the closest water when
        /// he fishes. After catching a fish he places it in the product part
        /// of his building."* A catch trip is `Shore -> Station`: he walks
        /// to the hut's shore spot (`StationStock.shoreX/Z`, found by
        /// `Outpost.SaveShoreSpots`), fishes there for the catch's timer,
        /// and carries it to the hut's output box. The index is the
        /// station's, as for `Station`. Appended LAST, for the save reason.
        Shore,
    }

    /// **A production station's own stock, one per BUILT instance
    /// (2026-09-23).** Input bay (per resource, `InputCap` units each), a
    /// bench holding one job, and an output rack (`OutputCap` units, all
    /// resources together), plus the player's ORDER. Nothing is worked here
    /// without an order. **Since 2026-09-30 the bench and the order are per
    /// SPOT** (`spots`, `StationSpots`): each spot its own recipe and bench,
    /// the bay and rack shared.
    ///
    /// Keyed by `planId` + `ordinal` (the nth built of that plan), not by the
    /// `raised` index, because `Outpost.Adopt` rebuilds `raised` and
    /// `Demolish` removes rows from it. `OutpostLedger.StationForRaised`
    /// translates a `raised` row to its station.
    ///
    /// Saved inside the ledger (`OutpostLedger.stations`), so everything here
    /// is JsonUtility-shaped: lists, public fields, no dictionaries.
    /// How a queued station order runs (2026-09-27, food rework phase 2).
    public enum OrderMode
    {
        /// Make `n`, then the entry is done.
        Count = 0,
        /// Make until stopped (blocks the entries under it).
        Repeat = 1,
        /// **Keep `n` in stock**: work while the camp holds fewer than `n`
        /// and every ingredient is to hand; otherwise idle (with a reason)
        /// and let the next entry run.
        Keep = 2,
    }

    /// One line of a station's short order queue, worked top-down.
    [System.Serializable]
    public class QueuedOrder
    {
        public string recipe = "";
        public OrderMode mode;
        public int n;
    }

    [System.Serializable]
    public class StationStock
    {
        /// **The order queue (phase 2, 2026-09-27) -- RETIRED 2026-09-30.**
        /// Only an old save has entries: `EnsureSpotRows` moves them onto the
        /// spots once and empties it. Kept for JsonUtility.
        public List<QueuedOrder> queue = new List<QueuedOrder>();
        public string planId = "";
        /// Which of this plan's built instances (0 for the first).
        public int ordinal;
        /// Set (and the row emptied) when the ledger drops this station --
        /// its building came down. A cached view reads it, or asks
        /// `OutpostLedger.IsLive`. A saved row never has it: removed rows
        /// leave the list.
        public bool removed;

        /// Input bay: one row per resource. Capacity is `InputCap` PER
        /// resource, so a two-input recipe can hold `InputCap` of each.
        public List<OutpostStore> bay = new List<OutpostStore>();
        /// Output rack: finished goods waiting to be carried to the store.
        /// Capacity `OutputCap` across all rows together.
        public List<OutpostStore> rack = new List<OutpostStore>();

        /// **The spots (2026-09-30, Melvor-style stations).** One per
        /// `StationSpots.SpotsFor(planId)`, in that order: each has its own
        /// selected recipe and its own bench, all running at once under the
        /// station's one worker. THE bay and THE rack above stay the
        /// station's, SHARED by every spot at full capacity (a bay row is
        /// per resource anyway; the rack is one pile of `OutputCap`) --
        /// never split, so a one-spot station is exactly what it was.
        /// Saved. Old saves: empty, and `EnsureSpotRows` builds it from the
        /// legacy bench/order/queue fields below.
        public List<SpotState> spots = new List<SpotState>();

        /// **LEGACY MIRROR since 2026-09-30** -- the spots own the benches.
        /// These four show the station's "shown" bench (`ShownSpot`: the
        /// first spot working, else the first finished, else spot 0), for
        /// the readers that only know one bench (the villager mime, the
        /// bench visual `StationStockView`, older sheets). Rewritten by
        /// `SyncLegacy` after every ledger step; writing them does nothing.
        /// Still saved: an OLD save's bench is read from here once.
        public string benchRecipe = "";
        public BenchState benchState = BenchState.Empty;
        /// 0..1 through the current job.
        public float benchProgress;
        /// Output units sitting on the bench when `benchState == Finished`.
        public int benchOut;

        /// **LEGACY MIRROR since 2026-09-30** of the first selected spot:
        /// its recipe, its count (`orderLeft`), and "until stopped"
        /// (`orderRepeat`). An OLD save's order is read from here once.
        public string orderRecipe = "";
        public int orderLeft;
        public bool orderRepeat;

        /// **Where this station's worker fishes (2026-09-30, the fishing hut
        /// only).** 0 = not looked for yet (an old save, a probe's ledger:
        /// he fishes beside the hut until `Outpost.SaveShoreSpots` has
        /// looked), 1 = found (`shoreX/Z` is the dry spot he stands on,
        /// `waterX/Z` what he faces), 2 = no reachable water (`shoreWhy`
        /// says why; no catch trips until the walls or the hut change).
        /// Saved, so an unwatched camp keeps fishing where it last did.
        /// Old saves: 0, the box empty -- JsonUtility-safe.
        public int shore;
        public float shoreX, shoreZ, waterX, waterZ;
        /// Where the hut stood when `shore` was worked out: a different
        /// hut behind the same row (a demolish shifted the ordinals) is
        /// looked for again.
        public float shoreForX, shoreForZ;
        public string shoreWhy = "";
        /// `CampPath.WallRevision` the spot was checked against. Not saved:
        /// a loaded camp re-checks once against its fresh grid.
        [System.NonSerialized] public int shoreRev = int.MinValue;

        /// **Cut off from the store (2026-10-05, Kevin's Day 853 save: the
        /// fishing hut and pier outside the palisade, below the cliffs).**
        /// `cutOffMap`: the scene's route check from the store hut found no
        /// way to this station's bays (`Outpost.SaveStationReach`);
        /// `cutOffBody`: a hauler's walk had no route for
        /// `CampWorker.HandBackSeconds` and gave the trip back
        /// (`OutpostLedger.HaulNoRoute`). Either one = no haul is booked to
        /// or from here (`OutpostLedger.StationReachable`). `cutOffWalled`:
        /// the ground alone would get there, so a wall is the cause (the
        /// "walled off" words). `reachRev` is the wall + building revision
        /// the answer was worked out under; when it moves, both marks are
        /// asked again. Not saved: a loaded camp re-checks against its
        /// fresh grid, and until it has one every station is reachable.
        [System.NonSerialized] public bool cutOffMap, cutOffBody, cutOffWalled;
        [System.NonSerialized] public int reachRev = int.MinValue;

        /// **Flagged guess** for a station plan whose `inputSlots` is 0.
        public const int DefaultInputSlots = 6;
        /// **Flagged guess** for a station plan whose `outputSlots` is 0.
        public const int DefaultOutputSlots = 12;

        /// Some spot has a recipe selected (2026-09-30: was the one order).
        public bool HasOrder
        {
            get
            {
                EnsureSpotRows();
                foreach (var sp in spots) if (sp != null && sp.Selected) return true;
                return false;
            }
        }

        /// One per spot, in `StationSpots.SpotsFor(planId)` order.
        public IReadOnlyList<SpotState> Spots
        {
            get { EnsureSpotRows(); return spots; }
        }

        /// The spot at `index`, or null.
        public SpotState SpotAt(int index)
        {
            EnsureSpotRows();
            return index >= 0 && index < spots.Count ? spots[index] : null;
        }

        /// Is some spot making `recipeId`?
        public bool Selects(string recipeId)
        {
            if (string.IsNullOrEmpty(recipeId)) return false;
            EnsureSpotRows();
            foreach (var sp in spots) if (sp != null && sp.recipeId == recipeId) return true;
            return false;
        }

        /// The spot whose bench the single-bench readers see: the first one
        /// working, else the first finished, else the first selected, else
        /// spot 0.
        public SpotState ShownSpot
        {
            get
            {
                EnsureSpotRows();
                foreach (var sp in spots) if (sp != null && sp.BenchBusy) return sp;
                foreach (var sp in spots) if (sp != null && sp.benchState == BenchState.Finished) return sp;
                foreach (var sp in spots) if (sp != null && sp.Selected) return sp;
                return spots.Count > 0 ? spots[0] : null;
            }
        }

        /// **Keep `spots` one row per spot, in order (2026-09-30).** Cheap
        /// when nothing changed. The first time on a row with none (an OLD
        /// save, a fresh row) it takes over the legacy single bench, the
        /// single order and the phase-2 order queue, each onto the spot its
        /// recipe belongs to: a Count line keeps its count, Repeat and Keep
        /// lines become "until stopped" (auto-pause replaces keep-N). A
        /// spot the game no longer has hands its bench and selection to the
        /// spot its recipe now lives on, or puts the batch back in the bay /
        /// on the rack -- nothing is lost. Returns whether anything changed.
        public bool EnsureSpotRows()
        {
            if (spots == null) spots = new List<SpotState>();
            var names = StationSpots.SpotsFor(planId);
            bool same = spots.Count == names.Count;
            for (int i = 0; same && i < names.Count; i++)
                same = spots[i] != null && spots[i].spot == names[i];
            if (same)
            {
                // JsonUtility loads a null string as "": back to null = idle.
                foreach (var sp in spots) if (sp.recipeId != null && sp.recipeId.Length == 0) sp.recipeId = null;
                return false;
            }
            if (names.Count == 0) return false;

            bool fresh = spots.Count == 0;
            var old = new List<SpotState>(spots);
            spots.Clear();
            foreach (var name in names)
            {
                SpotState keep = null;
                foreach (var o in old) if (o != null && o.spot == name) { keep = o; break; }
                if (keep != null) old.Remove(keep);
                else keep = new SpotState { spot = name };
                if (keep.recipeId != null && keep.recipeId.Length == 0) keep.recipeId = null;
                if (keep.recipeId != null) keep.recipeId = Economy.Recipes.Canonical(keep.recipeId);
                if (keep.benchRecipe == null) keep.benchRecipe = "";
                spots.Add(keep);
            }
            foreach (var o in old)
            {
                if (o == null) continue;
                var r = o.Selected ? o.Recipe : null;
                var to = SpotFor(r);
                if (to != null && !to.Selected) { to.recipeId = o.recipeId; to.count = o.count; }
                if (o.benchState != BenchState.Empty) PutBenchBack(o.benchRecipe, o.benchState, o.benchOut);
            }

            if (fresh)
            {
                // Renamed recipe ids (2026-10-02, `Recipes.Canonical`).
                benchRecipe = Economy.Recipes.Canonical(benchRecipe);
                orderRecipe = Economy.Recipes.Canonical(orderRecipe);
                // The legacy single bench.
                if (benchState != BenchState.Empty && !string.IsNullOrEmpty(benchRecipe))
                {
                    var to = SpotFor(Economy.Recipes.Named(benchRecipe));
                    if (to != null && to.benchState == BenchState.Empty)
                    {
                        to.benchRecipe = benchRecipe;
                        to.benchState = benchState;
                        to.progress01 = benchProgress;
                        to.benchOut = benchOut;
                    }
                    else PutBenchBack(benchRecipe, benchState, benchOut);
                }
                // The legacy single order.
                if (!string.IsNullOrEmpty(orderRecipe) && (orderRepeat || orderLeft > 0))
                {
                    var to = SpotFor(Economy.Recipes.Named(orderRecipe));
                    if (to != null && !to.Selected)
                    {
                        to.recipeId = orderRecipe;
                        to.count = orderRepeat ? 0 : orderLeft;
                    }
                }
                // The phase-2 queue (2026-09-27), top line first per spot.
                if (queue != null)
                {
                    foreach (var q in queue)
                    {
                        if (q == null) continue;
                        q.recipe = Economy.Recipes.Canonical(q.recipe);
                        var to = SpotFor(Economy.Recipes.Named(q.recipe));
                        if (to == null || to.Selected) continue;
                        if (q.mode == OrderMode.Count && q.n <= 0) continue;
                        to.recipeId = q.recipe;
                        to.count = q.mode == OrderMode.Count ? q.n : 0;
                    }
                    queue.Clear();
                }
            }
            SyncLegacy();
            return true;
        }

        /// The spot `r` is worked on at this station, or null (no recipe,
        /// another station's recipe).
        SpotState SpotFor(Economy.Recipe r)
        {
            if (r == null || r.station != planId) return null;
            string name = StationSpots.SpotOf(r);
            foreach (var sp in spots) if (sp != null && sp.spot == name) return sp;
            return null;
        }

        /// A bench with nowhere to go: a finished batch onto the rack, a
        /// loaded one's inputs back into the bay (over their caps if need
        /// be -- they exist).
        void PutBenchBack(string recipe, BenchState state, int outN)
        {
            var r = Economy.Recipes.Named(recipe);
            if (r == null) return;
            if (state == BenchState.Finished) { if (outN > 0) Rack(r.makes, true).whole += outN; }
            else foreach (var line in r.takes) if (line.n > 0) Bay(line.res, true).whole += line.n;
        }

        /// **Rewrite the legacy mirror fields from the spots.**
        public void SyncLegacy()
        {
            if (spots == null) return;
            SpotState sel = null;
            foreach (var sp in spots) if (sp != null && sp.Selected) { sel = sp; break; }
            orderRecipe = sel != null ? sel.recipeId : "";
            orderRepeat = sel != null && sel.count <= 0;
            orderLeft = sel != null && sel.count > 0 ? sel.count : 0;
            SpotState shown = null;
            foreach (var sp in spots) if (sp != null && sp.BenchBusy) { shown = sp; break; }
            if (shown == null) foreach (var sp in spots) if (sp != null && sp.benchState == BenchState.Finished) { shown = sp; break; }
            if (shown == null) shown = sel;
            if (shown == null && spots.Count > 0) shown = spots[0];
            benchRecipe = shown != null ? shown.benchRecipe ?? "" : "";
            benchState = shown != null ? shown.benchState : BenchState.Empty;
            benchProgress = shown != null ? shown.progress01 : 0f;
            benchOut = shown != null ? shown.benchOut : 0;
        }

        /// Output units sitting on finished benches that make `res`.
        public int FinishedOf(string res)
        {
            int n = 0;
            if (string.IsNullOrEmpty(res)) return 0;
            EnsureSpotRows();
            foreach (var sp in spots)
                if (sp != null && sp.benchState == BenchState.Finished && sp.benchOut > 0 && sp.BenchMakes == res)
                    n += sp.benchOut;
            return n;
        }

        public int InputCap
        {
            get { int n = BuildPlans.Named(planId).inputSlots; return n > 0 ? n : DefaultInputSlots; }
        }

        public int OutputCap
        {
            get { int n = BuildPlans.Named(planId).outputSlots; return n > 0 ? n : DefaultOutputSlots; }
        }

        /// The first selected spot's recipe (2026-09-30: was the one order).
        public Economy.Recipe OrderRecipe
        {
            get
            {
                EnsureSpotRows();
                foreach (var sp in spots) if (sp != null && sp.Selected) return sp.Recipe;
                return null;
            }
        }
        /// The SHOWN bench's recipe (the legacy mirror, `ShownSpot`).
        public Economy.Recipe BenchRecipe =>
            string.IsNullOrEmpty(benchRecipe) ? null : Economy.Recipes.Named(benchRecipe);
        /// What the bench is making (or has made), or null when it is empty.
        public string BenchMakes => BenchRecipe?.makes;

        public OutpostStore Bay(string res, bool create = false) => Row(bay, res, create);
        public OutpostStore Rack(string res, bool create = false) => Row(rack, res, create);

        public int BayCount(string res) { var s = Bay(res); return s != null ? s.whole : 0; }
        public int RackCount(string res) { var s = Rack(res); return s != null ? s.whole : 0; }

        public int RackTotal
        {
            get
            {
                int n = 0;
                if (rack != null) foreach (var s in rack) if (s != null) n += s.whole;
                return n;
            }
        }

        public int RackRoom => System.Math.Max(0, OutputCap - RackTotal);
        public bool RackFull => RackRoom <= 0;

        /// Whole units of `res` this station holds where the camp can count
        /// them: bay, finished bench, rack. A LOADED/WORKING bench is not
        /// counted -- its input is committed.
        public int CountOf(string res)
        {
            int n = BayCount(res) + RackCount(res);
            n += FinishedOf(res);
            return n;
        }

        /// Whole units the camp may SPEND from here: the rack and a finished
        /// bench. Never the bay -- that is this station's queued input.
        public int SpendableOf(string res)
        {
            int n = RackCount(res);
            n += FinishedOf(res);
            return n;
        }

        /// Whole and part on the rack: what wear (`OutpostLedger.DrawHeld`)
        /// can draw from this station.
        public float SpendableHeldOf(string res)
        {
            var r = Rack(res);
            return r != null ? r.whole + r.part : 0f;
        }

        public float HeldOf(string res)
        {
            float n = 0f;
            var b = Bay(res); if (b != null) n += b.whole + b.part;
            var r = Rack(res); if (r != null) n += r.whole + r.part;
            n += FinishedOf(res);
            return n;
        }

        /// Everything countable here, all kinds together.
        public int Total
        {
            get
            {
                int n = RackTotal;
                if (bay != null) foreach (var s in bay) if (s != null) n += s.whole;
                EnsureSpotRows();
                foreach (var sp in spots)
                    if (sp != null && sp.benchState == BenchState.Finished) n += sp.benchOut;
                return n;
            }
        }

        /// Every spot stopped (2026-09-30). Benches are left as they are --
        /// the ledger's `StopSpot` is the one that clears a bench.
        public void ClearOrder()
        {
            EnsureSpotRows();
            foreach (var sp in spots) sp?.Stop();
            queue?.Clear();
            SyncLegacy();
        }

        static OutpostStore Row(List<OutpostStore> list, string res, bool create)
        {
            if (list == null || string.IsNullOrEmpty(res)) return null;
            foreach (var s in list) if (s != null && s.resource == res) return s;
            if (!create) return null;
            var made = new OutpostStore { resource = res };
            list.Add(made);
            return made;
        }
    }

    /// A station's order, read-only. `recipe` null means no order.
    public struct StationOrder
    {
        public Economy.Recipe recipe;
        /// Units still to make; meaningless when `repeat`.
        public int remaining;
        public bool repeat;
        public bool Active => recipe != null && (repeat || remaining > 0);
    }

    /// What a hand is carrying right now, for the villager mime.
    public struct HaulView
    {
        public bool active;
        public string resource;
        public int count;
        public HaulPlace from;
        /// Station index when `from == Station`, else -1.
        public int fromStation;
        public HaulPlace to;
        /// Station index when `to == Station`, else -1.
        public int toStation;
        /// Which part of the walk he is in (2026-09-27): out to the pickup,
        /// working it, carrying, at the drop-off. The books change at the
        /// end of `AtPickup` (pickup) and on arrival (drop-off) only.
        public TripLeg leg;
        /// The load is on him (false while he walks out to fetch it).
        public bool picked;
        /// The route's two ends are known.
        public bool placed;
        /// Pickup point (store / rack / bay / the island source the ledger
        /// measured; a body picks its own tree), world x,z with y 0.
        public Vector3 fromAt;
        /// Drop-off point. y 0.
        public Vector3 toAt;
    }

    /// **The parts of a walked trip (2026-09-27, docs/DELIVERY-ON-ARRIVAL.md).**
    public enum TripLeg
    {
        None = 0,
        /// Walking out to the source, empty-handed.
        ToPickup = 1,
        /// Standing at the source working it (cutting, a stoop, a jab).
        AtPickup = 2,
        /// Carrying the load to its drop-off.
        ToDrop = 3,
        /// At the drop-off, holding it until there is room.
        AtDrop = 4,
    }
}
