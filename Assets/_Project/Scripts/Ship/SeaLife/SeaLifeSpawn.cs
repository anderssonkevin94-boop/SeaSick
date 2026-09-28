using UnityEngine;
using SeaSick.World;

namespace SeaSick.Ship.SeaLife
{
    /// Shared spawn-point helpers for everything in this folder — a point
    /// ahead of the bow within a cone, and the "not near land" refusal every
    /// spawner has to make (build brief: "nothing spawns within 60 m of an
    /// island shore or inside a camp's harbour"). A camp only ever sits on
    /// an island's own coast, so keeping clear of every island's shore by
    /// the same margin keeps clear of every harbour on it too — one check
    /// instead of two.
    public static class SeaLifeSpawn
    {
        /// A random point `minAhead`..`maxAhead` metres in front of `origin`,
        /// within `halfConeDeg` either side of `forward`.
        public static Vector3 AheadOfBow(Vector3 origin, Vector3 forward, float minAhead, float maxAhead, float halfConeDeg)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            float ang = Random.Range(-halfConeDeg, halfConeDeg);
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * forward;
            float dist = Random.Range(minAhead, maxAhead);
            return origin + dir * dist;
        }

        /// True if `worldPos` is far enough from every island's shore (and
        /// therefore every harbour on it) to spawn something there.
        public static bool ClearOfLand(Vector3 worldPos)
        {
            float margin = SeaLifeTuning.MinDistanceFromShoreMetres;
            foreach (var isle in Island.All)
            {
                if (isle == null) continue;
                Vector3 c = isle.transform.position;
                float dx = worldPos.x - c.x, dz = worldPos.z - c.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist < isle.Radius + margin) return false;
            }
            return true;
        }

        /// `AheadOfBow` retried a few times against `ClearOfLand` — good
        /// enough for an open, mostly-sea archipelago; if every try lands
        /// near land the caller just skips this spawn tick.
        public static bool TryFindSpot(Vector3 origin, Vector3 forward, float minAhead, float maxAhead, float halfConeDeg, out Vector3 spot)
        {
            for (int i = 0; i < 6; i++)
            {
                spot = AheadOfBow(origin, forward, minAhead, maxAhead, halfConeDeg);
                if (ClearOfLand(spot)) return true;
            }
            spot = default;
            return false;
        }
    }
}
