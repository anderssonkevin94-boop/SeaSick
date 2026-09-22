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
    /// **And for the hunting, 2026-09-22.** A man sent after the herd walks
    /// the beast down and clubs it, and the beast dies when
    /// `Outpost.SyncHunting` says so -- never because he got there. Same
    /// division as the felling and for the same reason: a crag worth more
    /// goats while somebody is watching it is the arithmetic showing through.
    /// The ledger kills; he only mimes. See `TickHunting`.
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
        Animal quarry;         // the beast he has claimed, if he is hunting

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
        /// A stint of clubbing at a beast. Shorter than a swing at a tree:
        /// the kill is the ledger's, and all this has to be is long enough to
        /// read as the man doing the work between arriving and carrying.
        const float HuntSeconds = 2f;
        /// Arm's length. He closes to this and no further -- walking to the
        /// animal's own position would put him inside it.
        const float HuntReach = 1.2f;
        /// Long enough to read as picking a log up off a stack.
        const float LoadSeconds = 0.9f;
        /// How far they will wander for a PROP to work at -- stone, ore,
        /// spice. Trees are not bounded by this any more: a cutter is handed
        /// the ledger's next tree wherever on the island it stands
        /// (`Outpost.ClaimTree`, 2026-09-21).
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
            var w = Live(hand);

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
            var w = Live(hand);
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
            // **Dead the moment we say so, not at the end of the frame.**
            // `Destroy` is deferred, and until it lands `GetComponent` still
            // hands this component back -- so an `Attach` in the same frame
            // (leave and arrive in one call; a re-order the frame a row
            // vanished) found it, took it for a live worker, skipped the
            // seeding, and then watched it be destroyed under the body. The
            // hand had no worker at all after that: nobody claimed a tree,
            // `cutters` read 0, and every tree the ledger paid for was flushed
            // instead of cut (CampLifeProbe, 2026-09-21: "4 came down, 0 with
            // a man swinging at them"). `Live` is the other half.
            w.enabled = false;
            Destroy(w);
        }

        /// The worker on this body that is actually alive. A component
        /// `Remove` has handed to `Destroy` is disabled first and skipped here,
        /// so a body can be stripped and re-puppeted in one frame and end up
        /// with exactly one working `CampWorker` on it.
        static CampWorker Live(Crew.CrewAgent hand)
        {
            if (hand == null) return null;
            var w = hand.GetComponent<CampWorker>();
            if (w == null || w.enabled) return w;
            // The first one is dying; look past it. Rare enough that the
            // array this allocates is not worth avoiding.
            foreach (var c in hand.GetComponents<CampWorker>())
                if (c != null && c.enabled) return c;
            return null;
        }

        // --- contract surface ------------------------------------------------

        /// The worker on this body, or null if it is not being puppeted.
        public static CampWorker Of(Crew.CrewAgent hand) => Live(hand);

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

        /// **The beast this man is stalking, or null.** The felling pair of
        /// this is `IsFellingNow`, and the difference is where the claim
        /// lives: a tree is claimed in the camp's table, an animal is claimed
        /// on the animal (`Animal.Hunted`), because the herd is the only list
        /// of animals there is. `Outpost.SyncHunting` reads the flag rather
        /// than asking here.
        public Animal Quarry => quarry;

        /// Is this man clubbing THIS animal right now? Same shape, and the
        /// same deliberate silence about distance, as `IsFellingNow`.
        public bool IsHuntingNow(Animal a)
            => phase == Phase.Working && a != null && ReferenceEquals(a, quarry)
               && isActiveAndEnabled;

        /// Hand the claimed trunk back to the camp so somebody else can have
        /// it.
        void ReleaseClaim()
        {
            if (camp != null) { camp.ReleaseTree(this); camp.ReleaseBed(this); }
            claimedTree = -1;
            Unclaim();
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
            // Hunting likewise: the quarry walks about while he closes on it,
            // and what he carries home is not what he was sent after. See
            // `TickHunting`.
            if (Hunting(r)) { TickHunting(r, dt); return; }

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
                    string wantB = WhatFor(r);
                    hauling = r.order == OutpostOrder.Build && PileHas(wantB);
                    target = hauling ? PileSpot(wantB) : FindSomethingToWorkAt(r);
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
                    carrying = Carries(r);
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

        /// **Hunting: he walks the beast down, clubs it, and carries meat
        /// home -- and he never kills anything.**
        ///
        /// Same division as the felling, for the same reason (see the class
        /// note): the ledger owns what a camp produces, and a beast that died
        /// because a man reached it would make a herd worth more when
        /// somebody is watching. `Outpost.SyncHunting` takes animals off the
        /// crag to match the books, preferring the one a hunter has claimed,
        /// so the beast that drops is the beast he is standing over. All this
        /// does is be there for it.
        ///
        /// **The target moves**, which is the one thing no other errand has
        /// to deal with. A grazing goat drifts a dozen metres off its anchor
        /// and a fleeing one goes twenty-five, so the walk is re-aimed at the
        /// animal's CURRENT position every frame rather than at a spot taken
        /// once when he set off. It does not run from him: `Animal.Hunted`
        /// takes his own body out of that animal's flee scan the moment he
        /// claims it, without which an errand at twelve-metre flee range and
        /// arm's-length reach could never finish.
        ///
        /// **He carries Food, not Game.** The row says Game -- that is what
        /// is standing on the island -- and the stock the yield lands in is
        /// Food, so the pile he walks to, the load in his hands and the
        /// stack it goes on all have to say Food. `Carries` is the split:
        /// what he swings at and what he shoulders are two questions and
        /// this is the one row where they have different answers.
        void TickHunting(OutpostHand r, float dt)
        {
            // It died -- to his club, to another hunter's, or to a cull the
            // ledger ran while he was walking. Let it go and take the next.
            if (quarry != null && quarry.Dead) Unclaim();

            switch (phase)
            {
                case Phase.Resting:
                {
                    acting?.Set(VillagerActing.Mode.None);
                    bool there = Walk(home, dt);
                    wait -= dt;
                    if (wait > 0f) { if (there) FaceRest(dt, 0f); return; }
                    if (!ClaimQuarry())
                    {
                        // Nothing alive on the island, or every beast left
                        // has a man on it. He potters near the fire and asks
                        // again in a moment -- the same answer the cutter
                        // gives an island with no wood left on it.
                        Vector2 off = Random.insideUnitCircle.normalized * Random.Range(6f, 12f);
                        target = Stand(camp.CampCentre + new Vector3(off.x, 0f, off.y));
                        phase = Phase.Going;
                        return;
                    }
                    phase = Phase.Going;
                    return;
                }

                case Phase.Going:
                    acting?.Set(VillagerActing.Mode.None);
                    if (quarry == null)
                    {
                        // He was only stretching his legs.
                        if (!Walk(target, dt)) return;
                        phase = Phase.Resting;
                        wait = RestSeconds;
                        return;
                    }
                    target = quarry.transform.position;
                    // Arm's length, tested before the step: `Walk` stops at
                    // 0.35 m, which is inside the animal.
                    if (!Near(target, HuntReach)) { Walk(target, dt); return; }
                    phase = Phase.Working;
                    wait = HuntSeconds;
                    // **A club, because there is no spear.** `VillagerActing`
                    // has no hunting pose; Hammer is the overhand swing the
                    // miners use and it is the nearest thing in the set. If a
                    // spear ever goes in, this is the one line that changes.
                    acting?.Set(VillagerActing.Mode.Hammer);
                    return;

                case Phase.Working:
                    if (quarry == null)
                    {
                        // Claim taken off him mid-swing: a re-order, or the
                        // beast is gone. No meat -- he never finished.
                        Drop();
                        phase = Phase.Resting;
                        wait = RestSeconds;
                        return;
                    }
                    Face(quarry.transform.position - transform.position, dt);
                    wait -= dt;
                    if (wait > 0f) return;
                    carrying = Carries(r);
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

        /// Is this row out after the herd?
        bool Hunting(OutpostHand r) =>
            r != null && r.order == OutpostOrder.Gather && r.target == Res.Game;

        /// **Take the nearest unclaimed beast, and hold it.**
        ///
        /// Nearest to the CAMP rather than to the man, for the same reason
        /// `FindSomethingToWorkAt` measures from there: it is the camp that
        /// works outward, and measuring from wherever somebody happens to be
        /// standing sends a man who has just walked home straight back out to
        /// the far side of the crag.
        bool ClaimQuarry()
        {
            Unclaim();
            var herd = camp != null ? camp.FaunaHere() : null;
            if (herd == null) return false;

            var animals = herd.Animals;
            if (animals == null) return false;

            Vector3 from = camp.CampCentre;
            Animal best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < animals.Count; i++)
            {
                var a = animals[i];
                if (a == null || a.Dead || a.Hunted) continue;
                Vector3 d = a.transform.position - from;
                d.y = 0f;
                float m = d.sqrMagnitude;
                if (m < bestSq) { bestSq = m; best = a; }
            }
            if (best == null) return false;

            quarry = best;
            quarry.Hunted = true;
            return true;
        }

        /// Let the beast go back to being a goat. Safe on one that has been
        /// destroyed under him: Unity's null covers it.
        void Unclaim()
        {
            if (quarry != null) quarry.Hunted = false;
            quarry = null;
        }

        /// Flat distance test, for a target that is too small to walk to.
        bool Near(Vector3 to, float within)
        {
            Vector3 d = to - transform.position;
            d.y = 0f;
            return d.sqrMagnitude <= within * within;
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
        /// - Nothing is standing anywhere on the island -- the wood is cut
        ///   out. He goes back to the fire and waits, because a man swinging
        ///   at a tree that will never fall is worse than a man doing
        ///   nothing. (Until 2026-09-21 this fired at `Reach`, and a builder
        ///   whose near wood was gone pottered while trees stood forty metres
        ///   off; now he walks to them.)
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
                        // Nothing standing anywhere on the island (or every
                        // tree left has a man on it already). He potters about
                        // near the fire and asks again in a moment. A man who
                        // simply STOPPED would be the more literal reading of
                        // "idle by the fire" and the wrong one: a camp of
                        // statues is what `CampWorker` exists to have stopped
                        // being. This is the only potter a cutter or a builder
                        // ever does now, and the sheet's "NO TIMBER LEFT"
                        // agrees with it (`Outpost.ReconcileWood`).
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
            // **A builder only cuts while it is LOGS he is short of.** Once
            // the timber part of the blueprint is paid the ledger's answer
            // changes to stone (`OutpostLedger.BuilderWants`), and a man at
            // a tree would then be swinging for something the arithmetic is
            // no longer buying -- so he leaves the wood and goes to the
            // rocks through the ordinary errand loop instead.
            if (r.order == OutpostOrder.Build)
                return WhatFor(r) == Res.Timber && !PileHas(Res.Timber);
            return r.order == OutpostOrder.Gather && r.target == Res.Timber;
        }

        bool PileHas(string resource) =>
            !string.IsNullOrEmpty(resource) && camp != null && camp.Ledger != null
            && camp.Ledger.CountOf(resource) > 0;

        bool Claim() => camp.ClaimTree(this, out claimedTree, out claimAt);

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

        /// **What this row is after.** A gatherer is after what they were
        /// told to get; a builder is after whatever half of the blueprint's
        /// price is still unpaid -- logs first, then stone
        /// (`OutpostLedger.BuilderWants`), which is the same order and the
        /// same answer the ledger's own `Step` uses. Asking the ledger
        /// rather than deciding here is the whole of why the body and the
        /// books cannot disagree about which material a builder is carrying.
        ///
        /// Falls back to timber when there is no blueprint left to read, so
        /// a builder in the frame between finishing and being re-ordered
        /// mimes an axe rather than nothing.
        string WhatFor(OutpostHand r)
        {
            if (r == null) return null;
            if (r.order != OutpostOrder.Build) return r.target;
            string want = camp != null && camp.Ledger != null ? camp.Ledger.BuilderWants : null;
            return string.IsNullOrEmpty(want) ? Res.Timber : want;
        }

        /// **What ends up on his shoulder**, which is not always what he was
        /// sent after. One row splits the two: a hunter is sent after Game
        /// and comes back with Food, because Game is counted in animals on
        /// the crag and the yield lands in the larder (`OutpostLedger`). The
        /// pile he walks to, the sack in his hands and the stack he sets it
        /// on all come off this, so all three agree.
        ///
        /// Everything else carries what `WhatFor` says, unchanged.
        string Carries(OutpostHand r) => Hunting(r) ? Res.Food : WhatFor(r);

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

            // Wheat: the camp hands out the bed the ledger will cut next,
            // nearest the fire outward, one hand a bed (`Outpost.ClaimBed`).
            if (what == Res.Food && camp.ClaimBed(this, out _, out Vector3 bedAt))
                return Stand(bedAt);

            if (!string.IsNullOrEmpty(what) && what != Res.Timber)
            {
                ResourceNode near = null;
                // **A builder is not bounded by `Reach`.** A gatherer who
                // has to walk further than 34 m is a gatherer the player
                // told to do the wrong thing, and pottering by the fire says
                // so. A builder was told to build THIS drawing, the ledger
                // is already paying for the stone whatever the distance, and
                // the handful of boulders a camp has stand just outside the
                // clearing -- which on a wide clearing is past 34 m. So he
                // walks to the nearest one wherever it is, and the body goes
                // on agreeing with the books.
                float best = r.order == OutpostOrder.Build
                    ? float.MaxValue : Reach * Reach;
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
        ///
        /// **Every errand in this file walks through here**, which is why the
        /// routing went in here and nowhere else: hauling, building, felling,
        /// hunting and going home all got a route the day this one function
        /// learnt to ask for one. See `CampPath` for why the map is a grid
        /// over the camp's own height field rather than a baked NavMesh.
        ///
        /// The arrival test is unchanged and is still about the REAL target,
        /// never a waypoint, so nothing upstream can be surprised by it.
        bool Walk(Vector3 to, float dt)
        {
            Vector3 here = transform.position;
            Vector3 d = to - here;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist < 0.35f) { ClearRoute(); return true; }

            // Where to head THIS frame: the next corner of the route if there
            // is one, otherwise the target itself — which is exactly the
            // straight line this used to be, and is what a failed plan falls
            // back to.
            Vector3 aim = NextCorner(here, to, dist, dt);

            Vector3 leg = aim - here;
            leg.y = 0f;
            float legLen = leg.magnitude;
            if (legLen < 0.0001f) return false;

            Vector3 step = leg / legLen * Mathf.Min(Speed * dt, legLen);
            Vector3 next = here + step;
            next.y = camp.GroundAt(next);
            transform.position = next;
            Face(leg, dt);
            return false;
        }

        // --- routing -----------------------------------------------------------

        /// The corners left to walk, and which one is next. Empty means "no
        /// route" and the walk is the old straight line.
        readonly System.Collections.Generic.List<Vector3> route
            = new System.Collections.Generic.List<Vector3>();
        int routeAt;
        Vector3 routeFor;        // the destination this route was planned for
        bool hasRoute;
        float routeAge;

        /// Seconds before a route is re-planned even though the destination
        /// has not moved. Cheap insurance against a stale map; long enough
        /// that a camp of hands is nowhere near the per-frame plan budget.
        const float RePlanSeconds = 1.2f;

        /// How far a destination may drift before the route is thrown away.
        /// A hunted beast moves every frame, so this is what stops `TickHunting`
        /// re-planning sixty times a second.
        const float RePlanMoved = 2.5f;

        /// Close enough to a waypoint to call it passed. Wider than the
        /// arrival tolerance on purpose: a corner is a suggestion, and
        /// pivoting exactly over one looks like a man checking a map.
        const float CornerReach = 1.4f;

        void ClearRoute()
        {
            route.Clear();
            routeAt = 0;
            hasRoute = false;
        }

        Vector3 NextCorner(Vector3 here, Vector3 to, float dist, float dt)
        {
            // Close in, or a hop not worth a search: go straight. Most steps
            // a camp ever takes are this one.
            if (dist < 6f) { ClearRoute(); return to; }

            routeAge += dt;

            bool stale = !hasRoute
                || routeAt >= route.Count
                || routeAge >= RePlanSeconds
                || Vector3.SqrMagnitude(new Vector3(to.x - routeFor.x, 0f, to.z - routeFor.z))
                       > RePlanMoved * RePlanMoved;

            if (stale && CampPath.Budget())
            {
                var map = CampPath.For(camp);
                routeAge = 0f;
                routeFor = to;
                routeAt = 0;
                // A failed plan leaves `route` empty, which IS the straight
                // line. Nobody can be stranded by this call.
                hasRoute = map != null && map.Plan(here, to, route) && route.Count > 0;
                if (!hasRoute) route.Clear();
            }

            if (!hasRoute || routeAt >= route.Count) return to;

            // Retire corners we are already on top of, and never let the last
            // one stand in for the target.
            while (routeAt < route.Count - 1)
            {
                Vector3 c = route[routeAt];
                float dx = c.x - here.x, dz = c.z - here.z;
                if (dx * dx + dz * dz > CornerReach * CornerReach) break;
                routeAt++;
            }

            return routeAt >= route.Count - 1 ? to : route[routeAt];
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
