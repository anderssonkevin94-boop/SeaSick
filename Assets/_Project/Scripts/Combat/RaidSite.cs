using UnityEngine;

namespace SeaSick.Combat
{
    /// Where a raid happens: the camp being hit, and the two points
    /// `Outpost.ShoreNear` found for the ship to beach at — dry sand for the
    /// party to stand on, a wet spot for the hull to ground in.
    public struct RaidSite
    {
        public World.Outpost camp;
        public Vector3 water;
        public Vector3 shore;
    }
}
