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
    }

    /// **A production station's own stock, one per BUILT instance
    /// (2026-09-23).** Input bay (per resource, `InputCap` units each), a
    /// bench holding one job, and an output rack (`OutputCap` units, all
    /// resources together), plus the player's ORDER. Nothing is worked here
    /// without an order.
    ///
    /// Keyed by `planId` + `ordinal` (the nth built of that plan), not by the
    /// `raised` index, because `Outpost.Adopt` rebuilds `raised` and
    /// `Demolish` removes rows from it. `OutpostLedger.StationForRaised`
    /// translates a `raised` row to its station.
    ///
    /// Saved inside the ledger (`OutpostLedger.stations`), so everything here
    /// is JsonUtility-shaped: lists, public fields, no dictionaries.
    [System.Serializable]
    public class StationStock
    {
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

        public string benchRecipe = "";
        public BenchState benchState = BenchState.Empty;
        /// 0..1 through the current job.
        public float benchProgress;
        /// Output units sitting on the bench when `benchState == Finished`.
        public int benchOut;

        /// The player's order: recipe id, units still to make, or repeat.
        public string orderRecipe = "";
        public int orderLeft;
        public bool orderRepeat;

        /// **Flagged guess** for a station plan whose `inputSlots` is 0.
        public const int DefaultInputSlots = 6;
        /// **Flagged guess** for a station plan whose `outputSlots` is 0.
        public const int DefaultOutputSlots = 12;

        public bool HasOrder => !string.IsNullOrEmpty(orderRecipe) && (orderRepeat || orderLeft > 0);

        public int InputCap
        {
            get { int n = BuildPlans.Named(planId).inputSlots; return n > 0 ? n : DefaultInputSlots; }
        }

        public int OutputCap
        {
            get { int n = BuildPlans.Named(planId).outputSlots; return n > 0 ? n : DefaultOutputSlots; }
        }

        public Economy.Recipe OrderRecipe => HasOrder ? Economy.Recipes.Named(orderRecipe) : null;
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
            if (benchState == BenchState.Finished && benchOut > 0 && BenchMakes == res) n += benchOut;
            return n;
        }

        /// Whole units the camp may SPEND from here: the rack and a finished
        /// bench. Never the bay -- that is this station's queued input.
        public int SpendableOf(string res)
        {
            int n = RackCount(res);
            if (benchState == BenchState.Finished && benchOut > 0 && BenchMakes == res) n += benchOut;
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
            if (benchState == BenchState.Finished && benchOut > 0 && BenchMakes == res) n += benchOut;
            return n;
        }

        /// Everything countable here, all kinds together.
        public int Total
        {
            get
            {
                int n = RackTotal;
                if (bay != null) foreach (var s in bay) if (s != null) n += s.whole;
                if (benchState == BenchState.Finished) n += benchOut;
                return n;
            }
        }

        public void ClearOrder()
        {
            orderRecipe = "";
            orderLeft = 0;
            orderRepeat = false;
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
        /// 0 when the trip starts (at the drop-off, empty-handed), 1 when
        /// the load is put down there.
        public float progress01;
        /// The route below was booked (false for a trip from an old save).
        public bool placed;
        /// Pickup point (store / rack / bay; the camp centre for a Field
        /// trip -- the body picks its own tree, the leg is
        /// `OutpostLedger.SourceMetres`), world x,z with y 0.
        public Vector3 fromAt;
        /// Drop-off point, where the trip starts and ends. y 0.
        public Vector3 toAt;
        /// progress01 at which the hand reaches the pickup (walking empty
        /// from `toAt` to `fromAt` until here).
        public float walkOutEnd01;
        /// progress01 at which cutting/picking up ends and the carry back
        /// to `toAt` starts.
        public float workEnd01;
        /// Seconds of game time the whole trip takes at full work factor.
        public float totalSeconds;
    }
}
