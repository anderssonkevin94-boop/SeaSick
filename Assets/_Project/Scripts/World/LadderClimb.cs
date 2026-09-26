using UnityEngine;

namespace SeaSick.World
{
    /// **A walker going up (or down) a ladder chain (2026-09-27).**
    ///
    /// Owned by a walker (`CampWorker`, `Combat.RaidWalker`), one per body,
    /// and asked at the top of its `Walk`: while `Active` the climb has the
    /// body and the walk does nothing else. It starts when the walker stands
    /// by one end of a chain and its next corner lies at the other end
    /// (`Outpost.LadderLeg`) -- which is exactly the leg `CampPath.Route`
    /// makes over a link, since both ends of a hop are always kept as
    /// corners.
    ///
    /// The pose is kept minimal and out of `VillagerActing`: the body walks
    /// the chain's own path (`LadderLayout.Shape.path`), faces the ladder on
    /// a flight (up and down alike -- nobody climbs down a ladder facing
    /// out), goes up rung by rung (a short pause per rung, not a glide),
    /// stands a moment on each landing, and walks across it. Standing a
    /// little out from the rails so the body is in front of the ladder, not
    /// inside it.
    ///
    /// Never traps anybody: a body moved by anything else mid-climb (the
    /// Hand, a park, a teleport home) drops the climb; a chain torn down
    /// mid-climb is finished from the shape already held.
    public sealed class LadderClimb
    {
        LadderLayout.Shape shape;
        Ladder ladder;
        bool up;
        int seg;
        float segT;
        Vector3 lastSet;
        Ladder cooldownOn;
        float cooldownUntil;

        /// Metres from a chain's end a walker may be and still start on it
        /// (a route corner is a cell centre, up to ~1.4 m from the end, and
        /// the corner is passed at `CornerReach` 1.4 m).
        public static float Reach = 3f;

        /// Metres out from the rails the body climbs at.
        public const float Standoff = 0.32f;

        public bool Active => shape != null;
        public Ladder On => ladder;
        public bool GoingUp => up;

        /// **Start a climb if this leg is one.** True when it started (and
        /// has already moved the body this frame).
        public bool TryBegin(Outpost camp, Transform body, Vector3 here, Vector3 aim, float dt)
        {
            if (camp == null || body == null || camp.Ladders.Count == 0) return false;
            var l = camp.LadderLeg(here, aim, Reach, out bool goingUp);
            if (l == null || l.Shape == null) return false;
            if (l == cooldownOn && Time.time < cooldownUntil) return false;
            shape = l.Shape;
            ladder = l;
            up = goingUp;
            seg = 0;
            segT = 0f;
            // The first leg starts from where the body actually is, not the
            // chain's end: `From(0)` is read live below.
            start = here;
            lastSet = here;
            Tick(camp, body, dt);
            return true;
        }

        Vector3 start;

        int Count => shape.path.Count;

        Vector3 Point(int i)
        {
            if (i == 0) return start;
            return up ? shape.path[i] : shape.path[Count - 1 - i];
        }

        LadderLayout.Leg LegOf(int i) => up ? shape.legs[i] : shape.legs[Count - 2 - i];

        /// Drop the climb where it is (the caller has the body).
        public void Cancel()
        {
            shape = null;
            ladder = null;
        }

        /// One frame of the climb. True when it has finished (the body is at
        /// the far end, on the ground).
        public bool Tick(Outpost camp, Transform body, float dt)
        {
            if (shape == null) return true;
            // Something else moved him: the climb is over, wherever he is.
            if ((body.position - lastSet).sqrMagnitude > 1.0f) { Cancel(); return true; }

            segT += dt;
            while (seg < Count - 1)
            {
                Vector3 a = Point(seg), b = Point(seg + 1);
                var leg = LegOf(seg);
                float dur = LadderLayout.LegSeconds(a, b, leg);
                if (segT < dur)
                {
                    Place(camp, body, a, b, leg, segT / dur, dt);
                    return false;
                }
                segT -= dur;
                seg++;
            }

            // Off the chain, on the ground at the far end.
            Vector3 end = Point(Count - 1);
            if (camp != null) end.y = camp.GroundAt(end);
            body.position = end;
            lastSet = end;
            cooldownOn = ladder;
            cooldownUntil = Time.time + 2f;
            Cancel();
            return true;
        }

        void Place(Outpost camp, Transform body, Vector3 a, Vector3 b, LadderLayout.Leg leg, float u, float dt)
        {
            Vector3 p;
            Vector3 face;
            switch (leg)
            {
                case LadderLayout.Leg.Climb:
                {
                    // Rung by rung: each rung is reached with an ease and
                    // held for a beat.
                    float rise = Mathf.Abs(b.y - a.y);
                    int rungs = Mathf.Max(1, Mathf.RoundToInt(rise / LadderLayout.RungStep));
                    float x = u * rungs;
                    int k = Mathf.Min(rungs - 1, Mathf.FloorToInt(x));
                    float f = Mathf.Clamp01((x - k) * 1.4f);
                    float e = (k + f * f * (3f - 2f * f)) / rungs;
                    p = Vector3.Lerp(a, b, e) - shape.run * Standoff;
                    face = shape.run;
                    break;
                }
                case LadderLayout.Leg.Deck:
                {
                    // Walk across, then stand (the pause is the tail of the
                    // leg's time).
                    float walk = LadderLayout.Flat(a, b) / LadderLayout.DeckSpeed;
                    float total = walk + LadderLayout.LandingPause;
                    float w = total > 0f ? Mathf.Clamp01(u * total / Mathf.Max(0.01f, walk)) : 1f;
                    p = Vector3.Lerp(a, b, w) - shape.run * Standoff;
                    face = w < 1f ? (b - a) : shape.run;
                    break;
                }
                default:
                {
                    p = Vector3.Lerp(a, b, u);
                    if (camp != null) p.y = camp.GroundAt(p);
                    face = b - a;
                    break;
                }
            }
            body.position = p;
            lastSet = p;
            face.y = 0f;
            if (face.sqrMagnitude > 1e-4f)
                body.rotation = Quaternion.Slerp(body.rotation,
                    Quaternion.LookRotation(face.normalized, Vector3.up), 1f - Mathf.Exp(-10f * dt));
        }
    }
}
