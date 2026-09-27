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

        /// A `Home`-less node (the party's own rock nodes) under this island.
        static bool UnderIsland(ResourceNode n, Island isle)
            => n.Home == null && n.transform.IsChildOf(isle.transform);
    }
}
