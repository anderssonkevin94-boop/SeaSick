using UnityEngine;

namespace SeaSick.Terrain
{
    /// **The layer the ground's colliders live on, so the hull can ignore it.**
    ///
    /// Two things used to stop the ship at a beach: the terrain chunks'
    /// `MeshCollider`s, which PhysX treats as a ramp the hull climbs, and
    /// `HullIntegrity`'s shore-depth grounding, which pushes her off. They
    /// fought, and what the player saw was a ship half up the sand being
    /// kicked back into the water. The ground's colliders go on this layer
    /// and the hull's collider (`Shipyard.Refit`) excludes it, so the depth
    /// field is the only thing that says where the sea ends for a ship.
    /// Everything else -- a raycast from a probe, a crew member, a shot --
    /// still sees the ground: the exclusion is on the hull, not the layer.
    public static class LandLayer
    {
        public const string Name = "Land";

        static int cached = -2;

        /// The layer index, or 0 (Default) if the project has no "Land"
        /// layer, so a missing layer costs the old behaviour and never a
        /// broken build.
        public static int Index
        {
            get
            {
                if (cached == -2)
                {
                    int i = LayerMask.NameToLayer(Name);
                    if (i < 0) Debug.LogWarning("LandLayer: no '" + Name + "' layer in TagManager; land colliders stay on Default");
                    cached = Mathf.Max(0, i);
                }
                return cached;
            }
        }

        /// Mask for `Collider.excludeLayers`; empty when the layer is missing,
        /// so the hull never excludes Default by accident.
        public static LayerMask Mask => Index > 0 ? (LayerMask)(1 << Index) : (LayerMask)0;
    }
}
