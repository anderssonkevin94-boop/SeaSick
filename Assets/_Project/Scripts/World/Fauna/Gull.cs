using UnityEngine;

namespace SeaSick.World
{
    /// **Gulls over the shore, and the shore telling you the ship has arrived.**
    ///
    /// A coast with birds on it is a coast; a coast without them is a mesh.
    /// This is the cheapest living thing in the game -- a point on a circle,
    /// evaluated from a phase -- and it does more for how an island reads from
    /// the water than anything else in this pass, because it is the only
    /// motion the player sees BEFORE he lands.
    ///
    /// **The scatter is the payload.** When the ship comes inside 60 m of the
    /// anchor the birds climb and widen out for twenty seconds. That is the
    /// island reacting to an arrival without a single line of UI, and it is
    /// the same beat the herd plays inland: the world noticed you.
    ///
    /// No state to speak of: position is a pure function of phase, so a gull
    /// switched off by `FaunaLod` and switched back on is exactly where it
    /// should be. The banking roll is taken from the turn direction, not
    /// integrated, for the same reason.
    public class Gull : MonoBehaviour
    {
        // ---- placeholder tuning ------------------------------------------
        const float Speed = 6f;          // m/s along the circle
        const float Bank = 25f;          // deg of roll into the turn
        const float Wobble = 0.35f;      // m of vertical breathing
        const float AlarmRange = 60f;    // m, ship near the anchor
        const float AlarmFor = 20f;      // s of climbing and widening
        const float AlarmClimb = 10f;    // m
        const float Settle = 3f;         // s to ease back down

        FaunaLod field;
        Vector3 anchor;
        /// **A gull that follows the ship.** Kevin, 2026-09-22: *"will they
        /// fly around on the ocean too?"* Real gulls follow a boat for the
        /// wake, so a few follow her: the anchor is re-read from `follow`
        /// every frame, a little astern, and the circle rides along.
        Transform follow;
        float followAstern;
        float radius, alt, phase, spin;
        float alarm;      // seconds of alarm left
        float ease;       // 0 calm .. 1 alarmed, so the change is not a snap
        float wob;

        public void Bind(Vector3 shoreAnchor, float circleRadius, float altitude,
                         float startPhase, float direction)
        {
            anchor = shoreAnchor;
            radius = circleRadius;
            alt = altitude;
            phase = startPhase;
            spin = direction;   // +1 or -1: half the flock turns the other way
            wob = startPhase * 3.1f;
            field = GetComponentInParent<FaunaLod>();
            Fly(0f);
        }

        /// Circle a moving thing instead of a fixed shore point.
        public void Follow(Transform what, float astern)
        {
            follow = what;
            followAstern = astern;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (follow != null)
            {
                Vector3 back = follow.forward; back.y = 0f;
                if (back.sqrMagnitude > 0.001f) back.Normalize();
                anchor = follow.position - back * followAstern;
                anchor.y = 0f;
            }

            // The ship check rides `FaunaLod`'s one-second scan, so this costs
            // a compare rather than a search.
            if (field != null && field.Ship != null)
            {
                Vector3 d = field.ShipAt - anchor; d.y = 0f;
                if (d.sqrMagnitude < AlarmRange * AlarmRange) alarm = AlarmFor;
            }
            if (alarm > 0f) alarm -= dt;
            ease = Mathf.MoveTowards(ease, alarm > 0f ? 1f : 0f, dt / Settle);

            Fly(dt);
        }

        void Fly(float dt)
        {
            // Alarm doubles the circle and lifts it; speed is constant, so a
            // wider circle is also a slower-looking turn, which is what a bird
            // riding away from something actually does.
            float r = radius * Mathf.Lerp(1f, 2f, ease);
            float y = anchor.y + alt + AlarmClimb * ease;

            phase += spin * (Speed / Mathf.Max(r, 1f)) * dt;
            wob += dt * 1.3f;

            var at = new Vector3(anchor.x + Mathf.Sin(phase) * r,
                                 y + Mathf.Sin(wob) * Wobble,
                                 anchor.z + Mathf.Cos(phase) * r);

            // Tangent of the circle, which is the heading by definition.
            var fwd = new Vector3(Mathf.Cos(phase) * spin, 0f, -Mathf.Sin(phase) * spin);
            transform.position = at;
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up)
                               * Quaternion.Euler(0f, 0f, Bank * spin);
        }
    }
}
