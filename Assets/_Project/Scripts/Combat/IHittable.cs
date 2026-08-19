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
        /// Centre of the target volume, in world space.
        Vector3 HitCentre { get; }
        float HitRadius { get; }

        /// Half the body's length, along the body, in world space. Zero means
        /// "just a sphere". A ship is 21m long and 5m wide: wrapping that in a
        /// single sphere either misses the bow and stern entirely or swallows
        /// the water beside it, so anything long reports an axis and gets
        /// tested as a capsule instead.
        Vector3 HitAxis { get; }
        bool Alive { get; }

        /// Enough for a readout to draw a health bar without knowing or caring
        /// what kind of thing it is looking at.
        float Health01 { get; }
        int HitPoints { get; }
        int DamageTaken { get; }

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

                float r = t.HitRadius + ballRadius;
                Vector3 axis = t.HitAxis;

                bool hit;
                float hitT;
                if (axis.sqrMagnitude < 1e-4f)
                    hit = SegmentSphere(a, b, t.HitCentre, r, out hitT);
                else
                    hit = SegmentCapsule(a, b, t.HitCentre - axis, t.HitCentre + axis, r, out hitT);

                if (hit && hitT < bestT) { bestT = hitT; best = t; }
            }

            if (best != null) hitPoint = Vector3.Lerp(a, b, bestT);
            return best;
        }

        /// Shot path against a capsule: the closest approach between the two
        /// segments, compared against the radius. `t` is the fraction along
        /// the shot at that closest approach — near enough to the entry point
        /// for a ball crossing a hull, and far cheaper than solving the
        /// quadratic against a swept capsule.
        static bool SegmentCapsule(Vector3 a, Vector3 b, Vector3 p, Vector3 q,
            float radius, out float t)
        {
            Vector3 d1 = b - a;      // the shot
            Vector3 d2 = q - p;      // the body
            Vector3 r = a - p;

            float A = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);

            float s;
            if (A < 1e-6f && e < 1e-6f) { t = 0f; s = 0f; }
            else if (A < 1e-6f) { t = 0f; s = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e < 1e-6f) { s = 0f; t = Mathf.Clamp01(-c / A); }
                else
                {
                    float bb = Vector3.Dot(d1, d2);
                    float denom = A * e - bb * bb;
                    t = denom > 1e-6f ? Mathf.Clamp01((bb * f - c * e) / denom) : 0f;
                    s = (bb * t + f) / e;
                    if (s < 0f) { s = 0f; t = Mathf.Clamp01(-c / A); }
                    else if (s > 1f) { s = 1f; t = Mathf.Clamp01((bb - c) / A); }
                }
            }

            Vector3 c1 = a + d1 * t;
            Vector3 c2 = p + d2 * s;
            return (c1 - c2).sqrMagnitude <= radius * radius;
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
