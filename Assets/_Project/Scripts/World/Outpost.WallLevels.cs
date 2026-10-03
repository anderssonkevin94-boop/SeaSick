using SeaSick.World.Economy;
using UnityEngine;

namespace SeaSick.World
{
    /// **Raising a wall segment or a gate to level 2 (2026-10-03).**
    ///
    /// The upgrade unit is the SEGMENT, the same thing that is tapped,
    /// sheeted, breached and mended (`WallSegment`); its level is on its own
    /// `BuiltWall` row. Paid like any building's upgrade: from what the camp
    /// can spend (`OutpostLedger.SpendableOf` / `Take`), no labour, no site,
    /// the fire at II. The posts never move.
    public partial class Outpost
    {
        /// The next level's step for this segment, or null at the top.
        public UpgradeStep WallUpgradeOf(WallSegment seg)
            => seg == null ? null : WallUpgrades.Next(seg.Length, seg.IsGate, seg.Level);

        public bool CanUpgradeWall(WallSegment seg, out string why)
        {
            if (seg == null || ledger == null) { why = "no wall here"; return false; }
            var next = WallUpgradeOf(seg);
            if (next == null) { why = "already at its top"; return false; }
            if (seg.Breached) { why = "repair it first"; return false; }
            if (WallWorkQueued(seg)) { why = "wait for the work ordered on it"; return false; }
            if (ledger.CampfireLevel < next.campfireLevel)
            { why = "needs the fire at " + RecipeGraph.Roman(next.campfireLevel); return false; }
            var missing = Cost.Missing(next.cost, ledger.SpendableOf);
            if (missing.Count > 0) { why = "needs " + ResDefs.Counted(missing[0].res, missing[0].n); return false; }
            why = null;
            return true;
        }

        /// Pay and raise. False (nothing paid) when `CanUpgradeWall` says no.
        public bool UpgradeWall(WallSegment seg)
        {
            if (!CanUpgradeWall(seg, out _)) return false;
            var next = WallUpgradeOf(seg);
            foreach (var line in next.cost) ledger.Take(line.res, line.n);
            seg.RaiseLevel(next.toLevel);
            return true;
        }

        /// A site queued on this segment's own posts (a repair, or the gate
        /// that replaces it): its drawing is about to change anyway, so the
        /// upgrade waits for it.
        bool WallWorkQueued(WallSegment seg)
        {
            if (ledger.sites == null) return false;
            foreach (var row in ledger.sites)
            {
                if (row == null || !row.isWall) continue;
                bool same = (row.postA - seg.A).sqrMagnitude < 0.05f && (row.postB - seg.B).sqrMagnitude < 0.05f;
                bool flipped = (row.postA - seg.B).sqrMagnitude < 0.05f && (row.postB - seg.A).sqrMagnitude < 0.05f;
                if (same || flipped) return true;
            }
            return false;
        }
    }
}
