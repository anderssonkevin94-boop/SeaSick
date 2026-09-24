using UnityEngine;

namespace SeaSick.World
{
    /// **A goat or a boar, grazing where it was born and running when we come.**
    ///
    /// The whole of the behaviour is one thing: an animal that reacts to the
    /// player is alive and one that ignores him is scenery. A herd that
    /// scatters when a landing party walks up the beach tells the player the
    /// island was there before he was, which is the entire point of putting
    /// animals on it.
    ///
    /// **Bound to an anchor, not free.** Graze and Wander both stay within a
    /// short leash of the herd's anchor, so a herd is still a herd an hour
    /// later. Flee is the only thing that breaks the leash, and it ends by
    /// grazing wherever it stopped -- which drifts a herd over time, slowly,
    /// the way a real one drifts.
    ///
    /// **Goats go uphill, boar go straight.** A goat's away-vector is bent
    /// toward the local gradient; it is one line of code and it is why a
    /// spooked goat ends up on the skyline of the ridge instead of in the sea,
    /// which is the single most legible thing a goat can do.
    ///
    /// Cheap by construction: threats are scanned twice a second off a list
    /// `FaunaLod` gathers once a second for the whole island, the ground is
    /// one height sample a frame, and nothing allocates once it is running.
    public class Animal : MonoBehaviour
    {
        public enum Kind { Goat, Boar }

        /// Set by `FaunaField` at spawn.
        public Kind kind;

        enum State { Graze, Wander, Flee, Rest, Dying }

        /// **A hunter has this one.** Set by the man walking at it and read by
        /// `Outpost.SyncHunting`, which kills the claimed animal in preference
        /// to any other so that the beast that drops is the beast he was
        /// stalking. While it is set the animal does not run from crew -- see
        /// `Threatened`.
        public bool Hunted { get; set; }

        /// Killed. True the instant `Die` is called, which is three seconds
        /// before the body is destroyed: everything that counts a herd counts
        /// on this rather than on the object still existing.
        public bool Dead { get; private set; }

        // ---- placeholder tuning ------------------------------------------
        const float GrazeMin = 3f, GrazeMax = 8f;   // s between wanders
        const float WanderLeash = 12f;              // m from the anchor
        const float FleeMin = 3f, FleeMax = 5f;     // s of running
        const float CrewFlee = 12f;                 // m, a crewman is close
        const float ShipFlee = 40f;                 // m, a MOVING ship
        const float NightRest = 0.6f;               // Night01 above this -> settle
        const float TurnRate = 220f;                // deg/s
        const float ScanEvery = 0.5f;               // s between threat checks
        const float FlopSeconds = 0.4f;             // s to go over, once killed
        const float FlopSink = 0.2f;                // m it settles into the turf

        FaunaLod field;
        System.Random rng;
        Vector3 anchor, target;
        State state;
        float until, scan;
        Transform head;
        Vector3 headHome;
        float bob;

        float WalkSpeed => kind == Kind.Goat ? 1.2f : 1.0f;
        float RunSpeed => 4.5f;
        float MaxSlope => kind == Kind.Goat ? FaunaField.GoatMaxSlope : FaunaField.BoarMaxSlope;

        public void Bind(FaunaLod f, Vector3 herdAnchor, int seed)
        {
            field = f;
            anchor = herdAnchor;
            rng = new System.Random(seed);

            // A head, if the model brought one. The bob is the only animation
            // in this pass; a model without a named head simply stands still,
            // which reads fine at the range these are seen from.
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t == transform) continue;
                if (t.name.ToLowerInvariant().Contains("head")) { head = t; headHome = t.localPosition; break; }
            }
            Graze();
        }

        void Update()
        {
            // **Bind first** (2026-09-24): a one-off NullReferenceException in
            // FaunaField.TryPoint came from an animal updating before `Bind`
            // ran (no field, no seed). Nothing to do until it is bound.
            if (field == null || rng == null) return;
            // Dead: the flop owns the transform until it is done with it, and
            // `Ground()` at the bottom of this would lift the carcass back out
            // of the turf every frame.
            if (state == State.Dying) return;
            float dt = Time.deltaTime;

            scan -= dt;
            if (scan <= 0f) { scan = ScanEvery; if (state != State.Flee) Threatened(); }

            switch (state)
            {
                case State.Rest:
                case State.Graze:
                    until -= dt;
                    if (head != null && state == State.Graze)
                    {
                        // Tiny, slow, and off the same clock as everyone
                        // else's -- a herd bobbing in unison is worse than a
                        // herd not bobbing at all.
                        bob += dt * 1.7f;
                        head.localPosition = headHome + new Vector3(0f, Mathf.Sin(bob) * 0.03f, 0f);
                    }
                    if (until <= 0f)
                    {
                        if (Night()) { Rest(); break; }
                        // A wander that would take it through a palisade is
                        // not taken (2026-09-24): it grazes on and picks
                        // again, so a herd inside a pen stays in and one
                        // outside a camp stays out.
                        if (FaunaField.TryPoint(field, anchor, WanderLeash, MaxSlope, rng, out var p)
                            && !CampPath.BlocksAnimal(transform.position, p, 0f))
                        { target = p; state = State.Wander; }
                        else Graze();
                    }
                    break;

                case State.Wander:
                    if (Night()) { Rest(); break; }
                    if (Step(target, WalkSpeed, dt)) Graze();
                    break;

                case State.Flee:
                    until -= dt;
                    Step(target, RunSpeed, dt);
                    if (until <= 0f) Graze();
                    break;
            }

            Ground();
        }

        // ---- states ---------------------------------------------------------

        void Graze()
        {
            state = Night() ? State.Rest : State.Graze;
            until = Mathf.Lerp(GrazeMin, GrazeMax, (float)rng.NextDouble());
        }

        void Rest()
        {
            // Settled for the night: it still flees, and it still re-checks,
            // so dawn puts the herd back to grazing without anything waking it.
            state = State.Rest;
            until = 4f;
        }

        /// **Taken.** The ledger's call and nobody else's: a hunter swings at
        /// an animal, and the books decide whether that swing landed (see
        /// `Outpost.SyncHunting`). One kill in the numbers is one beast off
        /// the crag, and this is the beast coming off it.
        ///
        /// It goes over rather than blinking out. Four tenths of a second on
        /// its side and a hand's breadth into the turf is the whole of the
        /// animation, and it is enough: the eye reads a shape that has stopped
        /// standing up. Three seconds later there is nothing there, which is
        /// long enough for the man who killed it to have shouldered the meat
        /// and short enough that a worked crag is not a field of carcasses.
        ///
        /// Idempotent, because two things count the herd and either may get
        /// here first.
        public void Die()
        {
            if (Dead) return;
            Dead = true;
            Hunted = false;
            // Out of the herd BEFORE the flop, not after: while it is in the
            // list it is still a goat to everything that counts one, and the
            // gate sweep would switch its renderers about under the fall.
            if (field != null) field.Remove(this);
            state = State.Dying;
            StartCoroutine(Flop());
            Destroy(gameObject, 3f);
        }

        System.Collections.IEnumerator Flop()
        {
            Quaternion from = transform.rotation;
            // About its own forward axis, so a goat lands on its flank
            // whichever way it happened to be facing.
            Quaternion to = from * Quaternion.AngleAxis(90f, Vector3.forward);
            Vector3 up = transform.position;
            Vector3 down = up + Vector3.down * FlopSink;

            for (float t = 0f; t < FlopSeconds; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / FlopSeconds);
                transform.rotation = Quaternion.Slerp(from, to, k);
                transform.position = Vector3.Lerp(up, down, k);
                yield return null;
            }

            transform.rotation = to;
            transform.position = down;
            // Nothing left to think about. The renderers stay on: this is a
            // carcass, and it is visible until it is destroyed.
            enabled = false;
        }

        bool Night()
        {
            var sky = SkyDirector.Instance;
            return sky != null && sky.Night01 > NightRest;
        }

        /// Anything close enough to run from? The ship only counts while it is
        /// UNDER WAY -- a hull sitting at anchor for ten minutes should not
        /// keep a herd pinned against the far shore.
        void Threatened()
        {
            Vector3 me = transform.position;
            Vector3 from = Vector3.zero;
            bool run = false;

            // **A stalked animal stands.** The flee radius is twelve metres
            // and a hunter has to get to arm's length, so an animal that bolts
            // from the man sent to kill it is an errand that can never finish
            // -- he would herd it round the island for ever. It still runs
            // from the ship, which is the one thing that would otherwise let
            // a claim pin a goat in place through a whole landing.
            if (!Hunted)
            {
                var crew = field.Crew;
                for (int i = 0; i < crew.Count; i++)
                {
                    var c = crew[i];
                    if (c == null || !c.isActiveAndEnabled) continue;
                    Vector3 d = me - c.transform.position; d.y = 0f;
                    if (d.sqrMagnitude > CrewFlee * CrewFlee) continue;
                    from += d.normalized; run = true;
                }
            }

            if (field.ShipMoving)
            {
                Vector3 d = me - field.ShipAt; d.y = 0f;
                if (d.sqrMagnitude < ShipFlee * ShipFlee) { from += d.normalized; run = true; }
            }

            if (!run) return;
            Flee(from);
        }

        void Flee(Vector3 away)
        {
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = transform.forward;
            away.Normalize();

            if (kind == Kind.Goat)
            {
                // Bend uphill. Sampled over four metres, which is the scale of
                // a slope you can see, not of the noise on the mesh.
                Vector3 me = transform.position;
                var up = new Vector3(
                    field.Height(me.x + 2f, me.z) - field.Height(me.x - 2f, me.z), 0f,
                    field.Height(me.x, me.z + 2f) - field.Height(me.x, me.z - 2f));
                if (up.sqrMagnitude > 1e-4f) away = (away + up.normalized * 0.7f).normalized;
            }

            // Aim at a point rather than a direction, so the run has an end and
            // the shared Step() can keep it off the beach.
            // A run that would go through a wall counts as cornered too
            // (2026-09-24), so a spooked goat turns along the palisade
            // rather than into it. If no bearing is clear it runs at the
            // first one anyway and `Step` stops it at the wall.
            Vector3 me0 = transform.position;
            Vector3 want = me0 + away * 25f;
            if (!FaunaField.PointOk(field, want, MaxSlope) || CampPath.BlocksAnimal(me0, want, 0f))
            {
                // Cornered: try a few bearings before giving up and just
                // standing -- better than running into the sea.
                for (int i = 1; i <= 6; i++)
                {
                    Vector3 alt = me0 + (Quaternion.Euler(0f, i * 40f, 0f) * away) * 20f;
                    if (FaunaField.PointOk(field, alt, MaxSlope) && !CampPath.BlocksAnimal(me0, alt, 0f))
                    { want = alt; break; }
                }
            }
            target = want;
            state = State.Flee;
            until = Mathf.Lerp(FleeMin, FleeMax, (float)rng.NextDouble());
        }

        // ---- motion ---------------------------------------------------------

        /// Walk toward `to`, turning into the move. True once it has arrived.
        bool Step(Vector3 to, float speed, float dt)
        {
            Vector3 d = to - transform.position; d.y = 0f;
            float dist = d.magnitude;
            if (dist < 0.4f) return true;

            Vector3 dir = d / dist;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(dir, Vector3.up), TurnRate * dt);

            Vector3 next = transform.position + dir * Mathf.Min(speed * dt, dist);
            // Never step below the beach, whatever the target said: the one
            // place an animal must never end up is in the water.
            if (field.Height(next.x, next.z) < field.SandTop) return true;
            // Nor through a wall (2026-09-24, Kevin: "animals walk through
            // the walls that I've built"). Treated like the beach: the walk
            // is over where it stands, a wander goes back to grazing and a
            // flight stands at the palisade until its timer runs out. One
            // call per frame: a box reject per segment of each walled camp,
            // no allocation (`CampPath.BlocksAnimal`).
            if (CampPath.BlocksAnimal(transform.position, next, AnimalClearance)) return true;
            transform.position = next;
            return false;
        }

        /// Metres of body kept off a palisade: a goat or a boar is wider
        /// than a man, so a little more than `CampPath.WallClearance`.
        const float AnimalClearance = 0.5f;

        /// Feet on the ground. One sample a frame, and no smoothing -- these
        /// walk at 1 m/s over ground that is smooth at that scale.
        void Ground()
        {
            Vector3 p = transform.position;
            p.y = field.Height(p.x, p.z);
            transform.position = p;
        }
    }
}
