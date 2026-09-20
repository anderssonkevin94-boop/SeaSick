using UnityEngine;

namespace SeaSick.World
{
    /// **The animation of a hand who lives here.**
    ///
    /// Kevin, playing it 2026-09-19: *"i can tell that things get done, but no
    /// one is doing it."* They were standing in a ring like ornaments while
    /// the pile filled itself.
    ///
    /// **This produces NOTHING, and that is the whole design.** The outpost is
    /// numbers; a crewman carrying a log is the picture of an increment the
    /// ledger already made. If this component fell trees or added to a pile,
    /// then a camp would pay differently depending on whether anybody was
    /// watching it — which is the exact thing the absentee loop was built to
    /// avoid. So it walks, it waits, it carries something back, and it touches
    /// no state at all.
    ///
    /// Only exists while the ship is here: `Outpost.ShowHands` adds and
    /// removes it. A camp three kilometres astern costs nothing.
    ///
    /// It moves the transform directly and does not touch `CrewAgent`'s state
    /// machine, which is safe for exactly one reason worth writing down:
    /// **a parked hand's state is `Station`, and `Station` does nothing** —
    /// and `CrewAgent`'s walk cycle is driven by how fast the body is actually
    /// moving rather than by which state it is in, so the legs come along for
    /// free.
    [RequireComponent(typeof(Crew.CrewAgent))]
    public class CampWorker : MonoBehaviour
    {
        Outpost camp;
        Crew.CrewAgent agent;

        Vector3 home;          // where ArrangeHands put them: the ring, or their shed
        Vector3 target;
        GameObject carried;
        float wait;
        Phase phase;

        enum Phase { Resting, Going, Working, Coming }

        [Tooltip("Metres a second. Slower than the shore party's 6.5 — nobody at their own camp is in a hurry.")]
        const float Speed = 2.6f;
        const float SwingSeconds = 2.4f;
        const float RestSeconds = 1.1f;
        /// How far they will wander for something to work at.
        const float Reach = 34f;

        public static void Attach(Outpost outpost, Crew.CrewAgent hand)
        {
            if (outpost == null || hand == null) return;
            var w = hand.GetComponent<CampWorker>();
            if (w == null) w = hand.gameObject.AddComponent<CampWorker>();
            w.camp = outpost;
            w.agent = hand;
            w.home = hand.transform.position;
            w.phase = Phase.Resting;
            w.wait = Random.Range(0f, RestSeconds);
        }

        public static void Remove(Crew.CrewAgent hand)
        {
            var w = hand != null ? hand.GetComponent<CampWorker>() : null;
            if (w != null) { w.Drop(); Destroy(w); }
        }

        void OnDisable() { Drop(); }

        void Drop()
        {
            if (carried != null) Destroy(carried);
            carried = null;
        }

        /// The row this body is drawing. Looked up every time rather than
        /// cached: orders change from a menu while this is running, and a
        /// cached row is a row that outlives the hand.
        OutpostHand Row => camp != null && agent != null
            ? camp.HandNamed(agent.DisplayName) : null;

        void Update()
        {
            if (camp == null || agent == null) return;
            var row = Row;
            if (row == null) return;

            float dt = Time.deltaTime;

            // Somebody assigned to a building stays at it. ArrangeHands has
            // already stood them in the right place; a sawyer who wandered off
            // to a tree would be telling the player the opposite of the truth.
            if (row.order == OutpostOrder.Work)
            {
                Drop();
                phase = Phase.Resting;
                return;
            }

            if (row.order == OutpostOrder.Idle)
            {
                Drop();
                // Shift about a bit so an idle camp does not read as a
                // photograph.
                transform.rotation = Quaternion.Euler(0f,
                    Mathf.Sin(Time.time * 0.4f + home.x) * 35f, 0f);
                return;
            }

            switch (phase)
            {
                case Phase.Resting:
                    wait -= dt;
                    if (wait > 0f) return;
                    target = FindSomethingToWorkAt(row);
                    phase = Phase.Going;
                    return;

                case Phase.Going:
                    if (Walk(target, dt))
                    {
                        phase = Phase.Working;
                        wait = SwingSeconds;
                    }
                    return;

                case Phase.Working:
                    wait -= dt;
                    // Face the work, and lean into it a little on each swing.
                    if (wait <= 0f)
                    {
                        phase = Phase.Coming;
                        // What they are carrying back is what the row says
                        // they are after -- which is the only place this
                        // component reads an order for anything but a picture.
                        string what = row.order == OutpostOrder.Build
                            ? Res.Timber : row.target;
                        if (!string.IsNullOrEmpty(what))
                        {
                            carried = CargoVisual.Build(what, transform);
                            if (carried != null)
                            {
                                carried.transform.localPosition = new Vector3(0f, 1.45f, 0.45f);
                                carried.transform.localScale = Vector3.one * 0.7f;
                            }
                        }
                    }
                    return;

                case Phase.Coming:
                    if (Walk(Dropoff(row), dt))
                    {
                        Drop();
                        phase = Phase.Resting;
                        wait = RestSeconds;
                        // Back to where the camp says they belong, so a body
                        // that has been walking about all day is still in the
                        // ring when the player zooms in on it.
                        home = Dropoff(row);
                    }
                    return;
            }
        }

        /// Where the load goes: the blueprint if one is going up, the fire
        /// otherwise. The piles sit round the fire, so that is where a carried
        /// log is going anyway.
        Vector3 Dropoff(OutpostHand row)
        {
            if (row.order == OutpostOrder.Build && camp.Ledger?.pending != null)
            {
                Vector3 p = camp.Ledger.pending.At;
                p.y = camp.GroundAt(p);
                return p;
            }
            return home;
        }

        /// Something on this island worth walking to.
        ///
        /// For timber, the nearest tree still standing — which means the ring
        /// of stumps the camp has already cut pushes them further out as the
        /// days go by, for free. For anything else, the nearest unharvested
        /// prop of that kind. Falls back to a spot near the fire rather than
        /// refusing to move: a hand with nothing to walk to should still look
        /// like somebody at a camp, not like a statue.
        Vector3 FindSomethingToWorkAt(OutpostHand row)
        {
            string what = row.order == OutpostOrder.Build ? Res.Timber : row.target;
            Vector3 from = camp.CampCentre;

            if (what == Res.Timber)
            {
                var wood = camp.GetComponentInChildren<Terrain.SceneryWood>();
                if (wood != null)
                {
                    float best = Reach * Reach;
                    Vector3 found = Vector3.zero;
                    bool any = false;
                    for (int i = 0; i < wood.TreeCount; i++)
                    {
                        var t = wood.TreeAt(i);
                        if (t.felled) continue;
                        Vector3 d = t.baseAt - from;
                        d.y = 0f;
                        float m = d.sqrMagnitude;
                        if (m < best) { best = m; found = t.baseAt; any = true; }
                    }
                    if (any) return Stand(found);
                }
            }
            else if (!string.IsNullOrEmpty(what))
            {
                ResourceNode near = null;
                float best = Reach * Reach;
                foreach (var n in ResourceNode.All)
                {
                    if (n == null || n.Harvested || n.Resource != what) continue;
                    Vector3 d = n.transform.position - from;
                    d.y = 0f;
                    float m = d.sqrMagnitude;
                    if (m < best) { best = m; near = n; }
                }
                if (near != null) return Stand(near.transform.position);
            }

            // Nothing in reach: potter about near the fire.
            Vector2 off = Random.insideUnitCircle.normalized * Random.Range(6f, 12f);
            return Stand(camp.CampCentre + new Vector3(off.x, 0f, off.y));
        }

        /// Beside the thing, not inside it.
        Vector3 Stand(Vector3 at)
        {
            Vector3 away = at - camp.CampCentre;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            Vector3 p = at - away.normalized * 1.1f;
            p.y = camp.GroundAt(p);
            return p;
        }

        /// Walk toward a point, facing the way they are going. True on arrival.
        bool Walk(Vector3 to, float dt)
        {
            Vector3 here = transform.position;
            Vector3 d = to - here;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist < 0.35f) return true;

            Vector3 step = d / dist * Mathf.Min(Speed * dt, dist);
            Vector3 next = here + step;
            next.y = camp.GroundAt(next);
            transform.position = next;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(d / dist, Vector3.up),
                1f - Mathf.Exp(-8f * dt));
            return false;
        }
    }
}
