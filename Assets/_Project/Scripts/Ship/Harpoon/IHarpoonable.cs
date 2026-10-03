using UnityEngine;

namespace SeaSick.Ship.Harpoon
{
    /// **Something the bow harpoon can hook** (docs/PLAN-harpoon.md §3A,
    /// phase 1). Every phase-1 target is also an `Overboard.IOverboardTarget`,
    /// and the gun delivers through that haul path (`BeingHauled`,
    /// `HaulAnchor`, `OnHauled`), so a harpooned crate pays out exactly what
    /// a sailed-over one does. Targets register themselves with
    /// `HarpoonRegistry` while they float.
    public interface IHarpoonable
    {
        Transform Transform { get; }

        /// World point the barb aims at and the line ties to.
        Vector3 HookPoint { get; }

        /// Short and lowercase, for the 🪝 button and the marker:
        /// "crate", "bottle", "castaway", "flotsam", "loot".
        string HarpoonLabel { get; }

        /// False once it is resolved, or while something else (a crew haul,
        /// the jolly boat, the castaway's own pull) already holds it.
        bool CanBeHarpooned { get; }

        /// Relative weight: a bottle ~0.2, a crate ~1, a heavy load up to ~3.
        /// Heavier comes in slower and strains the line more.
        float HarpoonMass { get; }

        /// Units this load needs in the hold once aboard (0 for a bottle or a
        /// person). The gun keeps it at the rail while it would not fit.
        int HarpoonHoldUnits { get; }
    }
}
