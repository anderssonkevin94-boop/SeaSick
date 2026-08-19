using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Combat
{
    /// Anything a round shot can bite.
    ///
    /// Kept deliberately small. The whole project resolves collision with
    /// distance maths rather than physics colliders (HullIntegrity, Island.Nearest,
    /// Reef.Nearest all work this way, and every primitive gets its collider
    /// destroyed on creation), so shot tests are no different. When raiders
    /// arrive they implement this and the guns need no changes.
    public interface IHittable
    {
        /// Centre of the target sphere, in world space.
        Vector3 HitCentre { get; }
        float HitRadius { get; }
        bool Alive { get; }

        /// Returns true if the hit counted. A dying target absorbs nothing.
        bool TakeHit(Vector3 point, float damage);
    }

    /// Live targets. They add themselves; nothing has to go looking.
    public static class HitTargets
    {
        public static readonly List<IHittable> All = new List<IHittable>();

        public static void Register(IHittable t)
        {
            if (t != null && !All.Contains(t)) All.Add(t);
        }

        public static void Unregister(IHittable t) => All.Remove(t);

        /// Nearest living target on the horizontal plane.
        public static IHittable Nearest(Vector3 pos, out float distance)
        {
            IHittable best = null;
            float bestSq = float.MaxValue;
            foreach (var t in All)
            {
                if (t == null || !t.Alive) continue;
                Vector3 d = t.HitCentre - pos;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = t; }
            }
            distance = best != null ? Mathf.Sqrt(bestSq) : float.PositiveInfinity;
            return best;
        }

        /// Swept test: does the segment a->b clip a target? A round shot covers
        /// most of a metre per frame, so testing the ball's position alone would
        /// let fast shots tunnel straight through a target.
        public static IHittable SweepFirst(Vector3 a, Vector3 b, float ballRadius, out Vector3 hitPoint)
        {
            IHittable best = null;
            float bestT = float.MaxValue;
            hitPoint = b;

            foreach (var t in All)
            {
                if (t == null || !t.Alive) continue;
                if (!SegmentSphere(a, b, t.HitCentre, t.HitRadius + ballRadius, out float hitT)) continue;
                if (hitT < bestT) { bestT = hitT; best = t; }
            }

            if (best != null) hitPoint = Vector3.Lerp(a, b, bestT);
            return best;
        }

        /// Standard segment/sphere intersection, clamped to the segment.
        /// `t` comes back as the fraction along a->b of the first crossing.
        static bool SegmentSphere(Vector3 a, Vector3 b, Vector3 centre, float radius, out float t)
        {
            t = 0f;
            Vector3 d = b - a;
            Vector3 m = a - centre;
            float dd = Vector3.Dot(d, d);
            if (dd < 1e-6f) return m.sqrMagnitude <= radius * radius;

            float bb = Vector3.Dot(m, d);
            float c = Vector3.Dot(m, m) - radius * radius;

            // Already inside at the start of the segment.
            if (c <= 0f) return true;
            // Heading away from the sphere.
            if (bb > 0f) return false;

            float disc = bb * bb - dd * c;
            if (disc < 0f) return false;

            t = (-bb - Mathf.Sqrt(disc)) / dd;
            return t >= 0f && t <= 1f;
        }
    }
}
