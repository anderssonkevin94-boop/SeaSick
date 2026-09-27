using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Standing orders (food rework phase 2, 2026-09-27).** A station holds
    /// a short queue of orders worked top-down: make N, repeat, or KEEP N IN
    /// STOCK -- the station works while the camp holds fewer than N of the
    /// output and every ingredient is to hand, otherwise it idles with a
    /// reason ("stocked 10/10", "waiting for onions") and the next entry
    /// runs. Two slots at station level I, three at II (`QueueSlots`).
    ///
    /// The queue is projected into the old single-order fields each step
    /// (`ResolveOrders`), so the bench, the haulers and the stall reasons are
    /// untouched: they read `orderRecipe`/`orderLeft`/`orderRepeat` as ever.
    public partial class OutpostLedger
    {
        public const int MaxQueueSlots = 4;

        /// Order slots at a station: 1 + its level (Kitchen I = 2, II = 3).
        public int QueueSlots(int stationIndex)
        {
            var s = StationAt(stationIndex);
            if (s == null) return 0;
            return Mathf.Clamp(1 + LevelOf(s.planId, s.ordinal), 1, MaxQueueSlots);
        }

        public IReadOnlyList<QueuedOrder> QueueAt(int stationIndex)
        {
            var s = StationAt(stationIndex);
            return s?.queue != null ? s.queue : (IReadOnlyList<QueuedOrder>)System.Array.Empty<QueuedOrder>();
        }

        /// Add an order to the bottom of the queue. False when the queue is
        /// full or the recipe is not workable here yet.
        public bool QueueOrder(int stationIndex, string recipeId, OrderMode mode, int n)
        {
            var s = StationAt(stationIndex);
            if (s == null) return false;
            if (s.queue == null) s.queue = new List<QueuedOrder>();
            if (!RecipeWorkableAt(s, recipeId)) return false;
            if (mode != OrderMode.Repeat && n <= 0) return false;
            // The same recipe twice is one line: update it.
            foreach (var q in s.queue)
                if (q.recipe == recipeId) { q.mode = mode; q.n = n; ResolveOrder(s, stationIndex); return true; }
            // An old single order becomes the queue's first line.
            AdoptSingleOrder(s);
            if (s.queue.Count >= QueueSlots(stationIndex)) return false;
            s.queue.Add(new QueuedOrder { recipe = recipeId, mode = mode, n = n });
            ResolveOrder(s, stationIndex);
            return true;
        }

        public void UnqueueOrder(int stationIndex, int index)
        {
            var s = StationAt(stationIndex);
            if (s?.queue == null || index < 0 || index >= s.queue.Count) return;
            s.queue.RemoveAt(index);
            if (s.queue.Count == 0) s.ClearOrder();
            else ResolveOrder(s, stationIndex);
        }

        /// Nudge a line's N (keep target or count left), floor 1.
        public void SetQueuedN(int stationIndex, int index, int n)
        {
            var s = StationAt(stationIndex);
            if (s?.queue == null || index < 0 || index >= s.queue.Count) return;
            s.queue[index].n = Mathf.Clamp(n, 1, 999);
            ResolveOrder(s, stationIndex);
        }

        public void MoveQueued(int stationIndex, int index, int delta)
        {
            var s = StationAt(stationIndex);
            if (s?.queue == null) return;
            int to = index + delta;
            if (index < 0 || index >= s.queue.Count || to < 0 || to >= s.queue.Count) return;
            var q = s.queue[index];
            s.queue.RemoveAt(index);
            s.queue.Insert(to, q);
            ResolveOrder(s, stationIndex);
        }

        void AdoptSingleOrder(StationStock s)
        {
            if (s.queue.Count > 0 || !s.HasOrder) return;
            s.queue.Add(new QueuedOrder
            {
                recipe = s.orderRecipe,
                mode = s.orderRepeat ? OrderMode.Repeat : OrderMode.Count,
                n = s.orderLeft,
            });
        }

        bool RecipeWorkableAt(StationStock s, string recipeId)
        {
            var r = Economy.Recipes.Named(recipeId);
            return r != null && r.station == s.planId && RecipeAvailable(r, out _)
                   && LevelOf(s.planId, s.ordinal) >= r.stationLevel;
        }

        /// Units of `res` the camp holds for a keep target: store, racks,
        /// a finished bench, and loads walking home.
        int KeepCount(string res) => CountOf(res) + CarriedOf(res);

        /// Can a batch of `r` be had here right now: every line in the bay,
        /// on its way there, or in the store.
        bool IngredientsToHand(StationStock s, int si, Economy.Recipe r, out string missing)
        {
            missing = null;
            if (r.tool != null && HeldOf(r.tool) <= 0f) { missing = r.tool; return false; }
            foreach (var line in r.takes)
            {
                if (line.n <= 0) continue;
                int have = s.BayCount(line.res) + InFlightTo(HaulPlace.Station, si, line.res) + StoreFree(line.res);
                if (have < line.n) { missing = line.res; return false; }
            }
            return true;
        }

        /// The status line for queue entry `index`: what it is doing or why
        /// it is waiting.
        public string QueueStatus(int stationIndex, int index)
        {
            var s = StationAt(stationIndex);
            if (s?.queue == null || index < 0 || index >= s.queue.Count) return "";
            var q = s.queue[index];
            var r = Economy.Recipes.Named(q.recipe);
            if (r == null) return "recipe gone";
            bool active = s.HasOrder && s.orderRecipe == q.recipe;
            if (q.mode == OrderMode.Keep)
            {
                int have = KeepCount(r.makes);
                if (have >= q.n) return $"stocked {have}/{q.n}";
                if (!IngredientsToHand(s, stationIndex, r, out string miss))
                    return $"{have}/{q.n} · waiting for {Economy.ResDefs.Label(miss)}";
                return active ? $"{have}/{q.n} · cooking" : $"{have}/{q.n} · next";
            }
            if (q.mode == OrderMode.Repeat) return active ? "repeating" : "waits its turn";
            return active ? $"{q.n} left" : $"{q.n} to make";
        }

        /// Every station with a queue: project its first workable line into
        /// the single-order fields. Called each step before the bench pass.
        void ResolveOrders()
        {
            if (stations == null) return;
            for (int i = 0; i < stations.Count; i++)
            {
                var s = stations[i];
                if (s == null || s.queue == null || s.queue.Count == 0) continue;
                ResolveOrder(s, i);
            }
        }

        void ResolveOrder(StationStock s, int si)
        {
            for (int k = s.queue.Count - 1; k >= 0; k--)
            {
                var q = s.queue[k];
                if (q == null || Economy.Recipes.Named(q.recipe) == null
                    || (q.mode == OrderMode.Count && q.n <= 0))
                    s.queue.RemoveAt(k);
            }
            foreach (var q in s.queue)
            {
                var r = Economy.Recipes.Named(q.recipe);
                if (!RecipeWorkableAt(s, q.recipe)) continue;
                switch (q.mode)
                {
                    case OrderMode.Count:
                        Project(s, q.recipe, false, q.n);
                        return;
                    case OrderMode.Repeat:
                        Project(s, q.recipe, true, 0);
                        return;
                    case OrderMode.Keep:
                        // A batch already on the bench counts toward the target.
                        int have = KeepCount(r.makes);
                        if ((s.benchState == BenchState.Loaded || s.benchState == BenchState.Working)
                            && s.benchRecipe == r.id) have += Mathf.Max(1, r.yield);
                        if (have >= q.n) continue;
                        if (!IngredientsToHand(s, si, r, out _)) continue;
                        Project(s, q.recipe, true, 0);
                        return;
                }
            }
            s.ClearOrder();
        }

        static void Project(StationStock s, string recipe, bool repeat, int left)
        {
            s.orderRecipe = recipe;
            s.orderRepeat = repeat;
            s.orderLeft = repeat ? 0 : left;
        }

        /// A finished batch comes off a Count line of the queue.
        void QueueFinished(StationStock s, Economy.Recipe r, int made)
        {
            if (s.queue == null) return;
            foreach (var q in s.queue)
                if (q.recipe == r.id && q.mode == OrderMode.Count && q.n > 0)
                {
                    q.n -= made;
                    break;
                }
        }
    }
}
