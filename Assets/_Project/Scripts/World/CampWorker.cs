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
    /// It READS the ledger in three places and writes it in none: the row's
    /// order and target (what to mime), `Ledger.pending` (where a blueprint
    /// is), and `Ledger.Stalled(row)` (whether to mime anything at all). A
    /// stalled mill has to READ as stalled — a sawyer sawing at a mill with no
    /// timber in it is the animation lying about the numbers, which is worse
    /// than a sawyer standing still.
    ///
    /// Only exists while the ship is here: `Outpost.ShowHands` adds and
    /// removes it. A camp three kilometres astern costs nothing.
    ///
    /// It moves the transform directly and does not touch `CrewAgent`'s state
    /// machine, which is safe for exactly one reason worth writing down:
    /// **a parked hand's state is `Station`, and `Station` does nothing** —
    /// and `CrewAgent`'s walk cycle is driven by how fast the body is actually
    /// moving rather than by which state it is in, so the legs come along for
    /// free. `CrewAgent.Puppeted`, set here, is what stops `Station` writing
    /// the body's rotation back on top of the direction they are walking in.
    [RequireComponent(typeof(Crew.CrewAgent))]
    public class CampWorker : MonoBehaviour
    {
        Outpost camp;
        Crew.CrewAgent agent;
        VillagerActing acting;

        Vector3 home;          // where ArrangeHands says they belong
        Vector3 lookAt;        // and what they face while they are standing in it
        Vector3 target;        // what they are walking to
        Vector3 dropAt;        // and where the load goes afterwards
        string carrying;
        float wait;
        float landLeft;
        Phase phase;
        Building preferred;    // the Hand's choice of WHICH sawmill

        enum Phase { Resting, Going, Working, Coming, Held, Landing }

        [Tooltip("Metres a second. Slower than the shore party's 6.5 — nobody at their own camp is in a hurry.")]
        const float Speed = 2.6f;
        const float SwingSeconds = 2.4f;
        const float RestSeconds = 1.1f;
        /// How far they will wander for something to work at.
        const float Reach = 34f;

        /// How long a stint at a building lasts before the made goods are
        /// walked to the pile. Long enough to be seen working, short enough
        /// that the carry leg happens while the player is still watching.
        const float ShiftShortest = 6f, ShiftLongest = 10f;

        /// **Replicated from `CampPiles.Radius`, deliberately.** The stacks
        /// are laid out by a component that owns the drawing of them and
        /// exposes no position, and reaching into it for one number would make
        /// this file a second reason that file cannot change. The angle rule
        /// is copied with it: taken from the resource NAME, so the same
        /// resource lands in the same place at every camp. If the piles ever
        /// move, these two lines move with them.
        const float PileRadius = 5.2f;

        /// Where the acting stops and the drop happens: a pace short of the
        /// stack, so nobody stands inside their own timber.
        const float PileStandOff = 0.9f;

        /// How long the stagger lasts after the Hand sets somebody down.
        /// Matches `VillagerActing.LandSeconds` on purpose — the phase machine
        /// and the pose come back at the same moment.
        const float LandSeconds = 0.6f;

        public static void Attach(Outpost outpost, Crew.CrewAgent hand)
        {
            if (outpost == null || hand == null) return;
            var w = hand.GetComponent<CampWorker>();

            // **Only a NEW worker gets its state seeded.** `PuppetsToWork` is
            // called after every single order write, and the first version of
            // this reset `home` to wherever the body happened to be standing
            // on every one of those calls — which threw away the spot
            // `ArrangeHands` had just worked out one line earlier, because
            // `ArrangeHands` runs first.
            bool fresh = w == null;
            if (fresh) w = hand.gameObject.AddComponent<CampWorker>();
            w.camp = outpost;
            w.agent = hand;
            w.acting = VillagerActing.On(hand);
            hand.Puppeted = true;

            if (fresh)
            {
                w.home = hand.transform.position;
                w.lookAt = outpost.CampCentre;
                w.phase = Phase.Resting;
                w.wait = Random.Range(0f, RestSeconds);
            }
        }

        public static void Remove(Crew.CrewAgent hand)
        {
            var w = hand != null ? hand.GetComponent<CampWorker>() : null;
            if (w == null) return;
            w.Drop();
            hand.Puppeted = false;
            var act = hand.GetComponent<VillagerActing>();
            if (act != null) { act.Set(VillagerActing.Mode.None); Destroy(act); }
            Destroy(w);
        }

        // --- contract surface ------------------------------------------------

        /// The worker on this body, or null if it is not being puppeted.
        public static CampWorker Of(Crew.CrewAgent hand) =>
            hand != null ? hand.GetComponent<CampWorker>() : null;

        /// Move where this hand belongs WITHOUT teleporting them there: they
        /// walk to it on their next rest. What `Outpost.ArrangeHands` calls
        /// for a body that is on its feet and being watched.
        public void SetHome(Vector3 spot, Vector3 lookTowards)
        {
            home = spot;
            lookAt = lookTowards;
        }

        /// Which building to be seen working at. Visual only: the ledger
        /// knows a sawyer by plan id, and with two sawmills standing that
        /// says nothing about which door he walks to.
        public void PreferWorkplace(Building b) { preferred = b; }

        /// The Hand has lifted them: stop walking, drop what is carried.
        ///
        /// The phase machine freezes where it is and the transform stops being
        /// written at all — the Hand owns the body until it lets go, and two
        /// writers on one transform is a body that jitters between them.
        public void PickedUp()
        {
            Drop();
            phase = Phase.Held;
            acting?.Set(VillagerActing.Mode.Dangle);
        }

        /// The Hand has set them down here: stagger, recover, carry on.
        ///
        /// **Re-plans from the CURRENT order rather than resuming the old
        /// phase**, because the whole reason a player picks somebody up is to
        /// change what they are doing, and the drop writes the new order in
        /// the same frame. Resuming would send them back to the tree they were
        /// walking to when they were lifted.
        public void PutDown(Vector3 at)
        {
            Drop();
            if (camp != null) at.y = camp.GroundAt(at);
            transform.position = at;
            phase = Phase.Landing;
            landLeft = LandSeconds;
            wait = 0f;
            acting?.Set(VillagerActing.Mode.Land);
        }

        /// What the phase machine is doing, for the probes. Read-only: nothing
        /// outside this file may push it about.
        public string PhaseName => phase.ToString();

        /// Where `ArrangeHands` last said this hand belongs.
        public Vector3 Home => home;

        /// **Where somebody working at this building stands.**
        ///
        /// Just outside the footprint on the side facing the fire, which is
        /// the door side of every building in the kit. Shared with
        /// `Outpost.ArrangeHands` rather than copied into it: the spot a hand
        /// is PUT and the spot a hand WALKS to have to be the same spot, or
        /// every order write nudges the sawyer a metre sideways.
        public static Vector3 WorkSpot(Outpost outpost, Building b)
        {
            if (outpost == null || b == null) return Vector3.zero;
            var plan = BuildPlans.Named(b.Id);
            Vector3 toFire = outpost.CampCentre - b.transform.position;
            toFire.y = 0f;
            if (toFire.sqrMagnitude < 0.01f) toFire = Vector3.forward;
            float reach = 0.6f + 0.5f * Mathf.Max(plan.footprint.x, plan.footprint.y);
            Vector3 spot = b.transform.position + toFire.normalized * reach;
            spot.y = outpost.GroundAt(spot);
            return spot;
        }

        void OnDisable() { Drop(); }

        void Drop()
        {
            carrying = null;
            acting?.Set(VillagerActing.Mode.None);
        }

        // --- the row ---------------------------------------------------------

        OutpostHand row;
        float rowChecked;

        /// The row this body is drawing.
        ///
        /// **Cached, and re-checked twice a second anyway.** `HandNamed` is a
        /// linear scan of the camp's roster and this used to run it every
        /// frame per hand. Caching on the name alone is not enough on its own:
        /// `Recall` takes the row out of the ledger without touching the
        /// worker, and a cached object with the right name in it would go on
        /// walking a body that is back aboard the ship. The 0.5 s re-resolve
        /// is what makes the cache safe to hold.
        OutpostHand Row
        {
            get
            {
                if (camp == null || agent == null) return null;
                if (row == null || row.name != agent.DisplayName
                    || Time.time >= rowChecked)
                {
                    row = camp.HandNamed(agent.DisplayName);
                    rowChecked = Time.time + 0.5f;
                }
                return row;
            }
        }

        // --- the loop ---------------------------------------------------------

        OutpostOrder lastOrder;
        string lastTarget;
        bool seenOrder;

        void Update()
        {
            if (camp == null || agent == null) return;

            // The Hand has them: it owns the transform until it lets go, and
            // it may well have taken the body out of the camp's hierarchy to
            // do it — so this comes before the parentage check below.
            if (phase == Phase.Held) return;

            // **Not a child of this camp any more.** `Recall` re-parents the
            // body to the ship and puts it back on its station, and the row
            // lookup below is cached for half a second — long enough for this
            // to drag a man who is standing at his gun off the deck by his
            // world position. One reference compare closes it.
            if (transform.parent != camp.transform) { Drop(); Remove(agent); return; }

            float dt = Time.deltaTime;

            if (phase == Phase.Landing)
            {
                landLeft -= dt;
                if (landLeft > 0f) return;
                phase = Phase.Resting;
                wait = 0f;               // straight back to work, not a pause
            }

            var r = Row;
            if (r == null)
            {
                // **The row is gone, so this body is not ours any more.**
                // `Outpost.Recall` takes the row out of the ledger and walks
                // the crewman back aboard without telling anybody here, and a
                // worker that merely went quiet would leave `Puppeted` set on
                // a man standing at his gun — no sway, no sickness, for the
                // rest of the voyage. Tidy up after ourselves instead.
                Drop();
                Remove(agent);
                return;
            }

            // A new order means a new errand. Without this a hand told to go
            // to the mill finishes walking to the tree first.
            //
            // The FIRST sight of an order is not a change: it must not eat the
            // staggered rest `Attach` seeded, which is the only thing stopping
            // a whole camp setting off on the same frame.
            if (!seenOrder)
            {
                seenOrder = true;
                lastOrder = r.order;
                lastTarget = r.target;
            }
            else if (r.order != lastOrder || r.target != lastTarget)
            {
                lastOrder = r.order;
                lastTarget = r.target;
                Drop();
                phase = Phase.Resting;
                wait = 0f;
            }

            switch (r.order)
            {
                case OutpostOrder.Idle: TickIdle(dt); return;
                case OutpostOrder.Work: TickWork(r, dt); return;
                default: TickErrand(r, dt); return;
            }
        }

        /// Nothing to do: stand where the camp put you, and shift about a bit
        /// so an idle camp does not read as a photograph.
        void TickIdle(float dt)
        {
            acting?.Set(VillagerActing.Mode.None);
            if (Walk(home, dt)) FaceRest(dt, Mathf.Sin(Time.time * 0.4f + home.x) * 32f);
        }

        /// **Gathering, and building, which is gathering with a different
        /// destination.** Out to the work, act at it, carry the load back to
        /// the pile it belongs on, rest, repeat.
        void TickErrand(OutpostHand r, float dt)
        {
            switch (phase)
            {
                case Phase.Resting:
                    acting?.Set(VillagerActing.Mode.None);
                    // Walk back to wherever the camp now says they belong.
                    // This is the whole of what `SetHome` buys: an order
                    // written while somebody is out at a tree moves the spot,
                    // not the body. The rest counts down while they walk, so a
                    // home they cannot reach is never a hand who stops working.
                    if (Walk(home, dt)) FaceRest(dt, 0f);
                    wait -= dt;
                    if (wait > 0f) return;
                    target = FindSomethingToWorkAt(r);
                    phase = Phase.Going;
                    return;

                case Phase.Going:
                    acting?.Set(VillagerActing.Mode.None);
                    if (!Walk(target, dt)) return;
                    phase = Phase.Working;
                    wait = SwingSeconds * Random.Range(0.85f, 1.35f);
                    acting?.Set(ModeFor(WhatFor(r)));
                    return;

                case Phase.Working:
                    Face(target - transform.position, dt);
                    wait -= dt;
                    if (wait > 0f) return;
                    // What they carry back is what the row says they are
                    // after — the only place this component reads an order
                    // for anything but a picture.
                    carrying = WhatFor(r);
                    dropAt = Dropoff(r, carrying);
                    phase = Phase.Coming;
                    acting?.Set(VillagerActing.Mode.Carry, carrying);
                    return;

                case Phase.Coming:
                    if (!Walk(dropAt, dt)) return;
                    Drop();
                    phase = Phase.Resting;
                    wait = RestSeconds;
                    return;
            }
        }

        /// **A hand in a position.** Walk to the door, work the shift, carry
        /// what the building makes to its stack, come back, do it again.
        ///
        /// Before any of that: if the ledger says this hand is stalled — no
        /// timber for the saw, no room for the boards — they stand at the door
        /// and do nothing. That is not an oversight in the animation, it IS
        /// the animation: a mill that has run dry should look like one from
        /// the air, without opening a sheet.
        void TickWork(OutpostHand r, float dt)
        {
            Building post = preferred != null && preferred.Id == r.target
                ? preferred : camp.WorkplaceOf(r);
            Vector3 door = post != null ? WorkSpot(camp, post) : home;
            Vector3 face = post != null ? post.transform.position : lookAt;

            if (camp.Ledger != null && camp.Ledger.Stalled(r))
            {
                Drop();
                phase = Phase.Resting;
                if (Walk(door, dt)) Face(face - transform.position, dt);
                return;
            }

            switch (phase)
            {
                case Phase.Resting:
                    acting?.Set(VillagerActing.Mode.None);
                    if (!Walk(door, dt)) return;
                    wait -= dt;
                    Face(face - transform.position, dt);
                    if (wait > 0f) return;
                    target = door;
                    phase = Phase.Going;
                    return;

                case Phase.Going:
                    acting?.Set(VillagerActing.Mode.None);
                    if (!Walk(door, dt)) return;
                    phase = Phase.Working;
                    wait = Random.Range(ShiftShortest, ShiftLongest);
                    acting?.Set(ModeAt(r.target));
                    return;

                case Phase.Working:
                    Face(face - transform.position, dt);
                    wait -= dt;
                    if (wait > 0f) return;
                    carrying = BuildPlans.Named(r.target).makes;
                    if (string.IsNullOrEmpty(carrying))
                    {
                        // A building with no output: the shift just runs again.
                        phase = Phase.Resting;
                        wait = RestSeconds;
                        return;
                    }
                    dropAt = PileSpot(carrying);
                    phase = Phase.Coming;
                    acting?.Set(VillagerActing.Mode.Carry, carrying);
                    return;

                case Phase.Coming:
                    if (!Walk(dropAt, dt)) return;
                    Drop();
                    phase = Phase.Resting;
                    wait = RestSeconds;
                    return;
            }
        }

        // --- what to mime -----------------------------------------------------

        /// What this row is after. Building is always timber: a blueprint is
        /// paid in logs whatever else the island has.
        static string WhatFor(OutpostHand r) =>
            r.order == OutpostOrder.Build ? Res.Timber : r.target;

        /// The swing that suits the material. An axe for wood, a pick-like
        /// hammer for the things that come out of rock, a hoe for what is
        /// picked off the ground.
        static VillagerActing.Mode ModeFor(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return VillagerActing.Mode.Chop;
            if (resource == Res.Timber || resource == Res.Boards)
                return VillagerActing.Mode.Chop;
            if (resource == Res.Spice || resource == Res.Food)
                return VillagerActing.Mode.Hoe;
            return VillagerActing.Mode.Hammer;      // stone, ore, anything mined
        }

        /// The trade, from the position the building offers. Keyed off
        /// `BuildPlans.PositionAt` rather than the plan id, so a second
        /// building that also employs a sawyer needs no entry here.
        static VillagerActing.Mode ModeAt(string planId)
        {
            string post = BuildPlans.PositionAt(planId);
            switch (post)
            {
                case "sawyer": return VillagerActing.Mode.Saw;
                case "smith": return VillagerActing.Mode.Hammer;
                case "farmhand": return VillagerActing.Mode.Hoe;
                case "cook": return VillagerActing.Mode.Stir;
                default: return VillagerActing.Mode.Hammer;
            }
        }

        /// Where the load goes: the blueprint if one is going up, otherwise
        /// the stack that resource belongs on.
        Vector3 Dropoff(OutpostHand r, string resource)
        {
            if (r.order == OutpostOrder.Build && camp.Ledger?.pending != null)
            {
                Vector3 p = camp.Ledger.pending.At;
                p.y = camp.GroundAt(p);
                return p;
            }
            return PileSpot(resource);
        }

        /// **The stack this resource is kept on.** See `PileRadius`: the angle
        /// comes from the resource name, exactly as `CampPiles` lays it out,
        /// so a man carrying boards walks to the boards.
        Vector3 PileSpot(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return home;
            float a = Mathf.Abs(resource.GetHashCode() % 360) * Mathf.Deg2Rad;
            var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 at = camp.CampCentre + outward * (PileRadius - PileStandOff);
            at.y = camp.GroundAt(at);
            return at;
        }

        /// Something on this island worth walking to.
        ///
        /// For timber, the nearest tree still standing — which means the ring
        /// of stumps the camp has already cut pushes them further out as the
        /// days go by, for free. For anything else, the nearest unharvested
        /// prop of that kind. Falls back to a spot near the fire rather than
        /// refusing to move: a hand with nothing to walk to should still look
        /// like somebody at a camp, not like a statue.
        ///
        /// **Measured from the CAMP, not from the man.** It is the camp that
        /// works outward, and asking from where each hand happens to be
        /// standing would send somebody who has just walked home back to the
        /// same tree the pile came from.
        Vector3 FindSomethingToWorkAt(OutpostHand r)
        {
            string what = WhatFor(r);
            Vector3 from = camp.CampCentre;

            if (what == Res.Timber)
            {
                // Through the grid rather than a scan of every tree: the wood
                // is a few hundred trunks and this ran once a trip, but the
                // Hand asks the same question every frame the cursor moves and
                // there is no reason for two answers to the same question.
                if (trees == null || trees.Wood == null) trees = TreeIndex.For(camp);
                if (trees != null && trees.NearestStanding(from, Reach, out Vector3 baseAt))
                    return Stand(baseAt);
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

        TreeIndex trees;

        /// Beside the thing, not inside it — and on a bearing of this hand's
        /// own, so three cutters sent to the same trunk ring it instead of
        /// standing in one another.
        Vector3 Stand(Vector3 at)
        {
            Vector3 away = at - camp.CampCentre;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            away.Normalize();

            float spread = agent != null
                ? (Mathf.Abs(agent.DisplayName.GetHashCode() % 140) - 70f) : 0f;
            away = Quaternion.Euler(0f, spread, 0f) * away;

            Vector3 p = at - away * 1.1f;
            p.y = camp.GroundAt(p);
            return p;
        }

        // --- moving and facing -------------------------------------------------

        /// Walk toward a point, facing the way they are going. True on arrival
        /// — and true immediately if they are already there, so a caller can
        /// use it as "am I in place yet".
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
            Face(d, dt);
            return false;
        }

        /// Turn toward a direction, smoothed. Every facing in this file goes
        /// through here, which is what keeps a hand from snapping round when
        /// the thing they are looking at changes.
        void Face(Vector3 dir, float dt)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir.normalized, Vector3.up),
                1f - Mathf.Exp(-8f * dt));
        }

        /// Standing at home: face whatever `ArrangeHands` said to face, with
        /// an optional drift so a camp at rest is not a photograph.
        void FaceRest(float dt, float driftDegrees)
        {
            Vector3 dir = lookAt - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            Quaternion want = Quaternion.LookRotation(dir.normalized, Vector3.up)
                * Quaternion.Euler(0f, driftDegrees, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, want,
                1f - Mathf.Exp(-4f * dt));
        }
    }
}
