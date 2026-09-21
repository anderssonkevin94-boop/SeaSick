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
    /// **That holds for the felling too, and it is the whole of the
    /// 2026-09-20 pass.** Kevin: *"when collecting wood they seem to cut at
    /// random areas while other, random trees disappear."* A man now walks to
    /// the tree the LEDGER's order says is next (`Outpost.ClaimTree`) and
    /// swings at it until it goes over — but he never fells it. The fall is
    /// still `Outpost.SyncFelling` paying out `timberTaken`; what changed is
    /// that it now waits for him to be standing there. Which trees come down,
    /// and how many, is untouched.
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

        int claimedTree = -1;  // the trunk the camp gave him, by index
        Vector3 claimAt;       // and where it stands
        float chopFor;         // how long he has been swinging at it
        bool hauling;          // carrying from the pile rather than cutting

        Vector3 flyVel;        // thrown: metres a second, integrated here
        Vector3 flyOverLand;   // the last point under him that was island
        float flySpin, flyFor;

        enum Phase { Resting, Going, Working, Coming, Held, Landing, Flying }

        /// **Tunables, as plain statics.** This component is added at runtime
        /// by `Outpost.PuppetsToWork`, so a `[SerializeField]` on it is a dial
        /// nobody can turn. Same shape as `Hand.Feel`, which owns the other
        /// half of the throw.
        public static class Feel
        {
            /// Metres per second squared on a thrown villager. Earth, times a
            /// little, because a real 9.81 arc over eighteen metres reads
            /// floaty at the zoom a camp is watched from.
            public static float throwGravity = 9.81f * 1.35f;

            /// e-folds a second of air drag. Small: this is a man, not a
            /// feather, and all it is for is stopping a clamped launch from
            /// carrying him the length of the island.
            public static float throwDrag = 0.35f;

            /// Degrees a second of tumble about his own right axis.
            public static float throwSpinMin = 140f, throwSpinMax = 320f;

            /// Ground below this is beach or water, and nobody gets thrown
            /// into the sea -- the horizontal motion stops over the last of
            /// the island and he slides down onto it.
            public static float throwShoreY = 0.5f;

            /// How long a man will swing at a tree that is not coming down
            /// before he walks back to the fire and has another go. The pile
            /// being full is the usual reason; an eternal chop at a tree the
            /// ledger has no use for is the animation lying about the numbers.
            public static float chopPatience = 30f;
        }

        [Tooltip("Metres a second. Slower than the shore party's 6.5 — nobody at their own camp is in a hurry.")]
        const float Speed = 2.6f;
        const float SwingSeconds = 2.4f;
        const float RestSeconds = 1.1f;
        /// Long enough to read as picking a log up off a stack.
        const float LoadSeconds = 0.9f;
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
            // Hand his tree back BEFORE the component goes: `Destroy` is
            // deferred to the end of the frame, so a worker who merely stopped
            // existing would still be holding the front of the felling order
            // for everybody else.
            w.ReleaseClaim();
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
            ReleaseClaim();
            phase = Phase.Held;
            acting?.Set(VillagerActing.Mode.Dangle);
        }

        /// **Let go of him while the hand was moving.**
        ///
        /// Kevin, 2026-09-20: *"i want villagers / items to retain some
        /// momentum if i drop them mid grab."* Black & White 2 throws: you
        /// swing a man at the place you want him and he arrives there, rather
        /// than being set down like a chess piece wherever the cursor stopped.
        ///
        /// **This is show and nothing else.** The order was resolved, written
        /// and committed by `Hand.DropAt` at the RELEASE point, in the release
        /// frame, before this is called -- the preview the cursor showed is
        /// what got written, and where he comes to rest cannot change it. A
        /// throw never writes a second order on landing, and landing in the
        /// sea is not a thing that can happen: `TickFlight` stops the
        /// horizontal motion at the last point that was over the island and
        /// lets him slide down onto it.
        ///
        /// Touchdown goes through the existing `PutDown`, so a thrown man and
        /// a placed man recover identically -- stagger, then re-plan from the
        /// order he is CARRYING, which is the one the drop just gave him.
        public void Throw(Vector3 from, Vector3 velocity)
        {
            Drop();
            ReleaseClaim();
            transform.position = from;
            flyVel = velocity;
            flyOverLand = new Vector3(from.x, Ground(from), from.z);
            flySpin = Random.Range(Feel.throwSpinMin, Feel.throwSpinMax)
                * (Random.value < 0.5f ? -1f : 1f);
            flyFor = 0f;
            phase = Phase.Flying;
            acting?.Set(VillagerActing.Mode.Dangle);
        }

        /// **Is this man swinging at THIS tree right now?**
        ///
        /// The one question `Outpost.SyncFelling` asks of a body before it
        /// drops a trunk. Deliberately says nothing about distance: he is at
        /// the tree because `Stand` put him a pace off its base and `Walk` got
        /// him there, and a distance test here would make the probe's gate --
        /// which measures exactly that distance -- agree with itself.
        public bool IsFellingNow(int treeIndex)
            => phase == Phase.Working && claimedTree >= 0 && claimedTree == treeIndex
               && isActiveAndEnabled;

        /// Hand the claimed trunk back to the camp so somebody else can have
        /// it.
        void ReleaseClaim()
        {
            if (camp != null) camp.ReleaseTree(this);
            claimedTree = -1;
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

        void OnDisable() { Drop(); ReleaseClaim(); }

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

            // In the air, and nothing else is true of him: no order to read,
            // no home to walk to, no row lookup. He is a body on a ballistic
            // arc until he touches the ground, and then he is a man again.
            if (phase == Phase.Flying) { TickFlight(dt); return; }

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
                // **Only let go of the tree if the new job is not cutting.**
                // A man re-told to cut timber keeps his claim across the
                // change: dropping out of the camp's claim table even for one
                // frame would read to `SyncFelling` as "nobody is cutting
                // here" and empty the front of the wood behind him.
                if (!Cutting(r)) ReleaseClaim();
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
            // Cutting is its own loop now: the man does not choose the tree
            // and does not decide when it falls. See `TickCutting`.
            if (Cutting(r)) { TickCutting(r, dt); return; }

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
                    // **A builder goes to the PILE first.** The ledger has
                    // paid blueprints out of the timber lying beside the fire
                    // since 2026-09-20 (`OutpostLedger.Step`, haul then cut),
                    // and a man walking past ten logs to fell a fresh one is
                    // the animation contradicting the arithmetic.
                    hauling = r.order == OutpostOrder.Build && PileHasTimber();
                    target = hauling ? PileSpot(Res.Timber) : FindSomethingToWorkAt(r);
                    phase = Phase.Going;
                    return;

                case Phase.Going:
                    acting?.Set(VillagerActing.Mode.None);
                    if (!Walk(target, dt)) return;
                    phase = Phase.Working;
                    // Hoisting a log off a stack is a moment, not a shift.
                    wait = hauling ? LoadSeconds : SwingSeconds * Random.Range(0.85f, 1.35f);
                    acting?.Set(hauling ? VillagerActing.Mode.None : ModeFor(WhatFor(r)));
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

        /// **Cutting wood: the tree that comes down is the one he is swinging
        /// at, and it comes down while he is swinging at it.**
        ///
        /// Kevin, 2026-09-20: *"when collecting wood they seem to cut at
        /// random areas while other, random trees disappear, not wanted
        /// behavior."* He was describing two independent choosers. This
        /// component used to walk each man to "the nearest standing tree",
        /// while `Outpost.SyncFelling` took trees down nearest-the-CAMP the
        /// instant the ledger's count rose -- two orderings that agree only by
        /// accident, and with three hands out they bunched on one trunk
        /// besides.
        ///
        /// So he does not choose any more. **The camp hands him the front of
        /// the felling order** (`Outpost.ClaimTree`), which is the tree the
        /// ledger is about to take down anyway, and no two men are ever given
        /// the same one. He walks to it, he stands a pace off it on a bearing
        /// of his own so three cutters ring a trunk rather than standing
        /// inside one another, and he swings **until it falls** -- he never
        /// fells it himself. The fall is still the ledger's, which is D2 and
        /// is not negotiable; all he does is be there for it.
        ///
        /// Three things can go otherwise, and all three are ordinary:
        ///
        /// - His tree is taken by the flush while he is still walking to it
        ///   (the clock was scrubbed, or she has just arrived). He re-claims
        ///   where he stands rather than finishing his walk to a stump.
        /// - Nothing is standing within `Reach` of the camp -- the wood here
        ///   is cut out. He goes back to the fire and waits, because a man
        ///   swinging at a tree that will never fall is worse than a man
        ///   doing nothing.
        /// - It does not fall for `Feel.chopPatience`. The usual reason is a
        ///   full pile, and the ledger has stopped paying; he walks home, has
        ///   a breather and comes back at it.
        void TickCutting(OutpostHand r, float dt)
        {
            switch (phase)
            {
                case Phase.Resting:
                {
                    acting?.Set(VillagerActing.Mode.None);
                    bool there = Walk(home, dt);
                    wait -= dt;
                    if (wait > 0f) { if (there) FaceRest(dt, 0f); return; }
                    if (!Claim())
                    {
                        // Nothing standing in reach: the wood here is cut out,
                        // or the whole island is. He potters about near the
                        // fire and asks again in a moment -- the timber grows
                        // back. A man who simply STOPPED would be the more
                        // literal reading of "idle by the fire" and the wrong
                        // one: a camp of statues is what `CampWorker` exists
                        // to have stopped being.
                        Vector2 off = Random.insideUnitCircle.normalized * Random.Range(6f, 12f);
                        target = Stand(camp.CampCentre + new Vector3(off.x, 0f, off.y));
                        phase = Phase.Going;
                        return;
                    }
                    target = Stand(claimAt);
                    phase = Phase.Going;
                    return;
                }

                case Phase.Going:
                    acting?.Set(VillagerActing.Mode.None);
                    // Somebody else's flush had it. Turn round where he is.
                    if (claimedTree >= 0 && camp.TreeIsFelled(claimedTree)) { Reclaim(); return; }
                    if (!Walk(target, dt)) return;
                    if (claimedTree < 0)
                    {
                        // He was only stretching his legs.
                        phase = Phase.Resting;
                        wait = RestSeconds;
                        return;
                    }
                    phase = Phase.Working;
                    chopFor = 0f;
                    acting?.Set(VillagerActing.Mode.Chop);
                    return;

                case Phase.Working:
                    // His claim was taken off him while he was swinging -- a
                    // new order, or the camp letting him go. No log: he never
                    // saw one fall.
                    if (claimedTree < 0) { Drop(); phase = Phase.Resting; wait = RestSeconds; return; }
                    Face(claimAt - transform.position, dt);
                    if (camp.TreeIsFelled(claimedTree))
                    {
                        // It went over while he was swinging at it. Shoulder a
                        // log and take it where it belongs -- the pile, or the
                        // blueprint if he is building.
                        carrying = Res.Timber;
                        dropAt = Dropoff(r, carrying);
                        phase = Phase.Coming;
                        acting?.Set(VillagerActing.Mode.Carry, carrying);
                        return;
                    }
                    chopFor += dt;
                    if (chopFor < Feel.chopPatience) return;
                    Drop();
                    phase = Phase.Resting;
                    wait = RestSeconds;
                    return;

                case Phase.Coming:
                    if (!Walk(dropAt, dt)) return;
                    Drop();
                    phase = Phase.Resting;
                    wait = RestSeconds;
                    return;
            }
        }

        /// Is this row cutting standing timber? Gatherers of timber always
        /// are; a builder is whenever the pile has nothing left to carry, and
        /// that is the same order the ledger's own step takes (haul, then cut).
        bool Cutting(OutpostHand r)
        {
            if (r == null) return false;
            if (r.order == OutpostOrder.Build) return !PileHasTimber();
            return r.order == OutpostOrder.Gather && r.target == Res.Timber;
        }

        bool PileHasTimber() =>
            camp != null && camp.Ledger != null && camp.Ledger.CountOf(Res.Timber) > 0;

        bool Claim() => camp.ClaimTree(this, Reach, out claimedTree, out claimAt);

        /// His tree went down without him. Take the next one from where he is
        /// standing rather than walking the rest of the way to a stump.
        void Reclaim()
        {
            if (Claim()) { target = Stand(claimAt); return; }
            Drop();
            phase = Phase.Resting;
            wait = RestSeconds;
        }

        // --- thrown ------------------------------------------------------------

        /// One step of a ballistic arc, and the rule that he cannot leave the
        /// island on it.
        ///
        /// The clamp is not a safety net bolted on: a villager who could be
        /// thrown into the sea would be a villager the player can delete by
        /// accident, and the ledger row would go on producing from a body
        /// floating off the shore. So the moment the point under him stops
        /// being island -- too low to be anything but beach and water, or past
        /// the shore on this bearing -- the horizontal motion stops at the last
        /// point that WAS island and he falls onto that instead.
        void TickFlight(float dt)
        {
            if (dt <= 0f) return;

            flyVel += Vector3.down * Feel.throwGravity * dt;
            flyVel *= Mathf.Exp(-Feel.throwDrag * dt);

            Vector3 next = transform.position + flyVel * dt;
            float ground;
            if (OverLand(next, out ground))
            {
                flyOverLand = new Vector3(next.x, ground, next.z);
            }
            else
            {
                next.x = flyOverLand.x;
                next.z = flyOverLand.z;
                flyVel.x = 0f;
                flyVel.z = 0f;
                ground = flyOverLand.y;
            }

            if (next.y <= ground)
            {
                next.y = ground;
                PutDown(next);
                return;
            }

            transform.position = next;
            flyFor += dt;

            // Facing the way he is going, turning over his own right axis.
            Vector3 dir = flyVel;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up)
                    * Quaternion.Euler(flySpin * flyFor, 0f, 0f);
        }

        /// Is the ground under this point island a man can land on?
        bool OverLand(Vector3 at, out float ground)
        {
            ground = Ground(at);
            if (ground < Feel.throwShoreY) return false;
            var isle = camp != null ? camp.Island : null;
            if (isle != null && isle.HasProfile
                && Island.FlatDistance(at, isle.transform.position) > isle.RadiusToward(at))
                return false;
            return true;
        }

        /// The height field, the way the rest of the island interface asks for
        /// it. **Null-safe by design**: `GroundPick.Height` is a
        /// non-serialisable static and a script recompile in play mode nulls
        /// it, at which point `GroundAt` hands back the point's own height and
        /// a throw simply lands where it was let go -- which is the old
        /// behaviour, and the right thing to degrade to.
        float Ground(Vector3 at)
        {
            var h = CameraRig.GroundPick.Height;
            if (h != null) return h(at.x, at.z);
            return camp != null ? camp.GroundAt(at) : at.y;
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

        /// Something on this island worth walking to — **stone, ore and spice,
        /// and nothing else.**
        ///
        /// Timber used to come out of here too, as "the nearest tree still
        /// standing", and that is exactly the half of Kevin's complaint about
        /// random trees: it was a second opinion about which tree mattered,
        /// competing with the ledger's. Cutting goes through `TickCutting` and
        /// the camp's claim table now. Everything else is unchanged: the
        /// nearest unharvested prop of the kind the row is after, falling back
        /// to a spot near the fire rather than refusing to move, because a
        /// hand with nothing to walk to should still look like somebody at a
        /// camp and not like a statue.
        ///
        /// **Measured from the CAMP, not from the man.** It is the camp that
        /// works outward, and asking from where each hand happens to be
        /// standing would send somebody who has just walked home back to the
        /// same place the pile came from.
        Vector3 FindSomethingToWorkAt(OutpostHand r)
        {
            string what = WhatFor(r);
            Vector3 from = camp.CampCentre;

            if (!string.IsNullOrEmpty(what) && what != Res.Timber)
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
