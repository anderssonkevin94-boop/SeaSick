using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Draws the ledger's named takes gone (2026-09-27).** See
    /// `OutpostLedger.GroundTaken.cs` for what the set is and why it exists
    /// (the gather party's takes, which are not a prefix of any camp order).
    ///
    /// Called from `Outpost.CatchUp` after the stone nodes are stood and
    /// before the camp's own count-based pictures (`SyncFelling`,
    /// `GatherSync`), which leave every named source out of their orders.
    /// Pure in the ledger and idempotent: a fresh bake after a load (every
    /// rock and tree standing again) comes back to the same picture on the
    /// first call, and a call with nothing changed does a few hash lookups.
    ///
    /// - a named loose rock is hidden in the scenery mesh (`SetHidden`; the
    ///   camp's `SceneryStone` stands no node on it, so nothing shows it
    ///   again),
    /// - a named tree is felled in the mesh with the plain `Fell` (not the
    ///   ledger's, so `DrawWood` can never stand it back up),
    /// - any node standing on a named source is marked taken
    ///   (`ResourceNode.MarkTaken`: no target, a kit deposit shows its
    ///   remnant).
    public static class GroundTaken
    {
        public static void Apply(Outpost o)
        {
            if (o == null || o.Ledger == null || !o.Ledger.HasGroundTaken) return;
            var l = o.Ledger;
            var isle = o.Island;
            if (isle == null) return;

            ApplyBeds(o);

            var rocks = Terrain.SceneryRocks.On(isle);
            if (rocks != null && l.takenRocks != null)
                foreach (int i in l.takenRocks)
                    if (!rocks.IsHidden(i)) rocks.SetHidden(i, true);

            var wood = isle.GetComponentInChildren<Terrain.SceneryWood>();
            if (wood != null && l.takenTrees != null)
                foreach (int i in l.takenTrees)
                    if (i >= 0 && i < wood.TreeCount && !wood.TreeAt(i).felled) wood.Fell(i);

            var all = ResourceNode.All;
            for (int k = all.Count - 1; k >= 0; k--)
            {
                var n = all[k];
                if (n == null || n.Harvested) continue;
                if (n.Home != isle && !UnderIsland(n, isle)) continue;
                if (l.Taken(n)) n.MarkTaken();
            }
        }

        static readonly List<int> regrown = new List<int>();

        /// **The party's picked berry bushes (2026-10-03).** Those whose
        /// regrow day has come leave the named set and stand again (their
        /// yield back in the Food stock, `OutpostLedger.ExpireBeds`); the
        /// rest are drawn stripped and held out of the field's units
        /// (`SceneryCrops.HoldPicked`), which a fresh bake after a load
        /// needs. Idempotent; a few lookups when nothing changed. Also called
        /// by `Ship.GatherParty` on its survey, since a camp-less island's
        /// `CatchUp` may not have run since the load.
        public static void ApplyBeds(Outpost o)
        {
            if (o == null || o.Ledger == null || o.Island == null) return;
            var l = o.Ledger;
            if (l.takenBeds == null || l.takenBeds.Count == 0) return;
            var crops = Terrain.SceneryCrops.On(o.Island);
            if (crops == null) return;
            regrown.Clear();
            double today = TimeOfDay.Seconds / TimeOfDay.WorkDaySeconds;
            bool due = false;
            for (int k = 0; l.takenBedDay != null && k < l.takenBedDay.Count && !due; k++) due = today >= l.takenBedDay[k];
            if (due)
            {
                l.ExpireBeds(today, crops.YieldOf, regrown);
                for (int k = 0; k < regrown.Count; k++) crops.ReleaseHeld(regrown[k]);
            }
            for (int k = 0; k < l.takenBeds.Count; k++)
            {
                int i = l.takenBeds[k];
                if (i >= 0 && i < crops.BedCount && !crops.IsHeld(i)) crops.HoldPicked(i);
            }
        }

        /// A `Home`-less node (the party's own rock nodes) under this island.
        static bool UnderIsland(ResourceNode n, Island isle)
            => n.Home == null && n.transform.IsChildOf(isle.transform);
    }
}
