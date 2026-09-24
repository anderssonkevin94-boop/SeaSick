using System;
using UnityEngine;

namespace SeaSick.Ship
{
    /// Conservative straight-water corridor, not an island-routing planner.
    public static class SailingCourse
    {
        public const float MaxRange = 800f;

        public static bool Clear(Vector3 from, Vector3 to, float radius, float depth,
            Func<float, float, float> terrain)
        {
            if (terrain == null || !Finite(from) || !Finite(to)) return false;
            Vector3 delta = to - from;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance > MaxRange) return false;
            Vector3 side = distance > 0.01f
                ? new Vector3(delta.z, 0f, -delta.x) / distance : Vector3.right;
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / 2f));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, (float)i / steps);
                for (int j = -1; j <= 1; j++)
                {
                    Vector3 q = p + side * (j * radius);
                    float height = terrain(q.x, q.z);
                    if (float.IsNaN(height) || float.IsInfinity(height) || height > -depth)
                        return false;
                }
            }
            return true;
        }

        public static float SpeedOrder(float distance, float arrival, float speed,
            float maxSpeed, float headingError)
        {
            float remaining = Mathf.Max(0f, distance - arrival);
            float desired = Mathf.Min(maxSpeed * 0.65f, Mathf.Sqrt(2f * 0.65f * remaining));
            desired *= Mathf.Lerp(1f, 0.16f, Mathf.InverseLerp(15f, 110f, Mathf.Abs(headingError)));
            // The existing engine ramps slowly. Coast early instead of continuing
            // to push until the exact destination and circling back after overshoot.
            if (speed > desired + 0.4f) return 0f;
            return Mathf.Clamp(desired / Mathf.Max(1f, maxSpeed), 0f, 0.65f);
        }

        static bool Finite(Vector3 p) =>
            !float.IsNaN(p.x) && !float.IsNaN(p.z) &&
            !float.IsInfinity(p.x) && !float.IsInfinity(p.z);
    }
}
