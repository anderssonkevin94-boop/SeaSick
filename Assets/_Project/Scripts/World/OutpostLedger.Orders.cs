using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Standing orders (food rework phase 2, 2026-09-27) -- RETIRED
    /// 2026-09-30 for station spots** (`OutpostLedger.Spots.cs`, Kevin's
    /// Melvor-style stations: select a recipe on a spot, it runs until an
    /// input runs out or the store is full, auto-pauses, resumes). An old
    /// save's queue is moved onto the spots once (`StationStock.EnsureSpotRows`:
    /// a Count line keeps its count, Repeat and Keep lines run until
    /// stopped). The queue API below is kept so older callers still work:
    /// it reads and writes the SELECTED SPOTS -- one "line" per selected
    /// spot, in spot order.
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

        /// The selected spots as queue lines (a fresh list): Count with the
        /// units left, else Repeat.
        public IReadOnlyList<QueuedOrder> QueueAt(int stationIndex)
        {
            var s = StationAt(stationIndex);
            var list = new List<QueuedOrder>();
            if (s == null) return list;
            foreach (var sp in s.Spots)
                if (sp != null && sp.Selected)
                    list.Add(new QueuedOrder
                    {
                        recipe = sp.recipeId,
                        mode = sp.count > 0 ? OrderMode.Count : OrderMode.Repeat,
                        n = sp.count,
                    });
            return list;
        }

        /// Select the recipe on its own spot: Count keeps `n`, Repeat and
        /// Keep run until stopped. False when it is not workable here yet.
        public bool QueueOrder(int stationIndex, string recipeId, OrderMode mode, int n)
        {
            var s = StationAt(stationIndex);
            if (s == null || !RecipeWorkableAt(s, recipeId)) return false;
            if (mode == OrderMode.Count && n <= 0) return false;
            var r = Economy.Recipes.Named(recipeId);
            return SetSpot(s, StationSpots.SpotIndexOf(r), recipeId, mode == OrderMode.Count ? n : 0, out _);
        }

        /// The `index`-th selected spot, or -1.
        int SelectedSpotIndex(StationStock s, int index)
        {
            if (s == null || index < 0) return -1;
            var spots = s.Spots;
            for (int k = 0, seen = 0; k < spots.Count; k++)
                if (spots[k] != null && spots[k].Selected && seen++ == index) return k;
            return -1;
        }

        public void UnqueueOrder(int stationIndex, int index)
        {
            var s = StationAt(stationIndex);
            int k = SelectedSpotIndex(s, index);
            if (k >= 0) StopSpot(s, k);
        }

        /// Nudge a Count line's units left, floor 1. An until-stopped spot
        /// has no count to nudge.
        public void SetQueuedN(int stationIndex, int index, int n)
        {
            var s = StationAt(stationIndex);
            var sp = s?.SpotAt(SelectedSpotIndex(s, index));
            if (sp == null || sp.count <= 0) return;
            sp.count = Mathf.Clamp(n, 1, 999);
            RefreshSpots(s);
        }

        /// Spots run side by side: there is no order to move.
        public void MoveQueued(int stationIndex, int index, int delta) { }

        bool RecipeWorkableAt(StationStock s, string recipeId)
        {
            var r = Economy.Recipes.Named(recipeId);
            return r != null && r.station == s.planId && RecipeAvailable(r, out _)
                   && LevelOf(s.planId, s.ordinal) >= r.stationLevel;
        }

        /// The status line for "line" `index` (the index-th selected spot):
        /// its pause reason, else what it is doing.
        public string QueueStatus(int stationIndex, int index)
        {
            var s = StationAt(stationIndex);
            var sp = s?.SpotAt(SelectedSpotIndex(s, index));
            if (sp == null) return "";
            if (sp.Recipe == null) return "recipe gone";
            if (sp.pauseReason != null) return sp.pauseReason;
            return sp.count > 0 ? $"{sp.count} left" : "running";
        }
    }
}
