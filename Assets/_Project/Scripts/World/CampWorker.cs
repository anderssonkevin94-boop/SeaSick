using System.Collections.Generic;
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
    /// **And for hauling, 2026-09-23.** `OutpostLedger.HaulOf(row)` books a
    /// whole trip's route and timing the moment it starts -- station input
    /// and output, a builder's site stocking, a stationed worker fetching his
    /// own raw. This used to layer a second, local schedule on top of that
    /// (`haulSerial`/"mime it once" bookkeeping, a builder's own pile-fetch
    /// simulation) which could only ever approximate what the books had
    /// already decided. It reads `HaulOf` fresh every frame instead and
    /// walks exactly the trip the ledger is running, at whatever stage its
    /// `progress01` says -- so a hand who becomes watched mid-trip picks up
    /// precisely where the unwatched clock left him rather than starting a
    /// trip of his own. See `TickHaul`.
    ///
    /// **And the walk is PACED to the books, 2026-09-24** (Kevin's fletcher
    /// report: a hauler stood in the middle of the building for ~40 s
    /// before putting the logs down; the fletcher "made arrows" before the
    /// logs were there; finished arrows went to the campfire). The ledger
    /// only moves on 0.1-day steps (18 s at the playtest day length) and
    /// times a trip on its own abstract legs at the hand's work factor, so
    /// "walk while `progress01` says walk" had the body at the drop-off long
    /// before the books put the load down. The body now works out WHEN the
    /// ledger will deposit (`SecondsToDeposit`) and leaves the pickup its
    /// own walk-back before that; the station worker mimes only while his
    /// bench is Loaded/Working and walks each finished job to the station's
    /// own output rack, timed to the step that puts it there
    /// (`TickWork`). Station ends of a trip use Astra's markers
    /// (`Input_Pickup`, `Output_Dropoff`, `Worker_Stand`; see `MarksOf`).
    ///
    /// It READS the ledger and writes it in none: the row's order and target
    /// (what to mime), `Ledger.pending` (where a blueprint is),
    /// `Ledger.Stalled(row)` (whether to mime anything at all), and
    /// `Ledger.HaulOf(row)` (the trip in progress, above). A stalled mill has
    /// to READ as stalled — a sawyer sawing at a mill with no timber in it is
    /// the animation lying about the numbers, which is worse than a sawyer
    /// standing still.
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
        /// **Standing the thing up rather than fetching for it, 2026-09-23.**
        /// True for a builder whose site has every material in: he walks to
        /// the drawing, swings a hammer at it and carries nothing. See
        /// `PendingBuild.built` -- the ledger's second phase, mimed.
        bool raising;
        /// **Clearing an obstruction off a build site, 2026-09-23.** Kevin's
        /// rule: any blueprint may be dropped over standing trees or loose
        /// rock, and the camp clears them before anybody raises a frame.
        /// Same shape as `claimedTree`/`claimAt` above but the claim lives at
        /// the SITE (`Outpost.ClaimClearing`) rather than on the open
        /// island, and there is no index to hand back -- `clearing` is the
        /// sentinel instead. `clearIsRock` picks the swing; there is no
        /// separate pick/strike animation yet (`VillagerActing.Mode` has
        /// none), so both trees and rock get the axe.
        bool clearing;
        Vector3 clearAt;
        bool clearIsRock;
        float clearFor;         // how long he has been swinging at it
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
            ForgetTrip();
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
            ForgetTrip();
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
            if (camp != null) { camp.ReleaseTree(this); camp.ReleaseBed(this); camp.ReleaseClearing(this); }
            claimedTree = -1;
            clearing = false;
            fieldNode = null;
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
        ///
        /// **Astra's `Worker_Stand` wins when the model has one**
        /// (2026-09-24): the fletcher, forge, kitchen and quarry each carry
        /// the spot their worker stands at, under the canopy at the bench,
        /// which is where the bench mime belongs. Kit/extruded models keep
        /// the door-side spot below.
        public static Vector3 WorkSpot(Outpost outpost, Building b)
        {
            if (outpost == null || b == null) return Vector3.zero;
            var marks = MarksOf(b);
            if (marks.stand != null)
            {
                Vector3 at = marks.stand.position;
                at.y = outpost.GroundAt(at);
                return at;
            }
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
            CancelDelivery();
            acting?.Set(VillagerActing.Mode.None);
        }

        // --- the row ---------------------------------------------------------

        OutpostHand row;
        /// The ledger trip (`OutpostHand.haulSerial`) this body is currently
        /// miming. Checked every frame in `TickHaul`: when it does not match
        /// the row's own serial any more -- a new trip started, or this row
        /// got handed a stale claim from whatever it was doing before it
        /// started hauling -- whatever this body had claimed (a tree, a
        /// prop) is let go before the new trip's pickup is worked out.
        int mimedTrip = -1;
        /// Was `HaulOf(row).active` last frame. The one-frame edge this
        /// catches: the trip just finished (or was cut short by a
        /// re-order) and nobody told this body to put the load down and go
        /// idle, because the row's own order/target never changed.
        bool wasHauling;
        /// The prop a Field pickup is walking to when it is not timber --
        /// stone, ore, spice. Re-picked (see `HaulPickupSpot`) once it is
        /// harvested out from under him or a new trip starts.
        ResourceNode fieldNode;
        /// Cooldown on a failed tree claim / prop search, so a hand with
        /// nothing left to fetch on the island does not re-scan the camp's
        /// trees or `ResourceNode.All` every single frame.
        float fieldRetryAt;
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

        /// Seconds a body may spend walking in from the ship before the
        /// books count it anyway -- a man who cannot find a path must not
        /// freeze the camp. Generous: a long walk inland is the point.
        public const float WalkInLimit = 120f;
        float walkInFor;

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

            // **Arrived** (`OutpostHand.walkingIn`, 2026-09-23): the books
            // start paying this hand the moment the body is at its first
            // piece of work, not while it is still coming up from the ship.
            var walker = Row;
            if (walker != null && walker.walkingIn)
            {
                walkInFor += Time.deltaTime;
                if (phase == Phase.Working || walkInFor > WalkInLimit)
                    walker.walkingIn = false;
            }

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
                // **Clearing outranks the same protection**, 2026-09-23: an
                // order change straight from clearing a site into cutting
                // free timber reads as `Cutting(r)` true on the NEW order,
                // which would otherwise skip the release and leave the
                // obstruction claim standing forever.
                if (!Cutting(r) || clearing) ReleaseClaim();
                phase = Phase.Resting;
                wait = 0f;
            }

            // **A haul outranks the order it is running under, 2026-09-23.**
            // `OutpostLedger.HaulOf` is the one source of truth for a
            // hauling hand -- station input/output, a builder's site
            // stocking, a stationed worker fetching his own raw -- and it
            // can be running under Idle, Gather, Work or Build alike (a
            // spare hand or a store-blocked gatherer doing station hauling,
            // a sawyer walking his own boards in, a builder's armful). See
            // `TickHaul`.
            //
            // The bench is watched every frame whatever the body is doing
            // (hauling, delivering), so the "a job just came off it" edge in
            // `WatchBench` is measured against last frame's books and never
            // against a baseline that went stale during a trip.
            WatchBench(r);
            // A load the books have put down (or are about to) being walked
            // the last few steps and set down. Runs ahead of everything,
            // including the next trip: it lasts a second or two and the
            // next trip's own pickup slack absorbs it. See `TickDelivery`.
            if (TickDelivery(dt)) return;
            if (camp.Ledger != null && r.Hauling)
            {
                TickHaul(r, dt);
                wasHauling = true;
                return;
            }
            if (wasHauling)
            {
                // The trip just ended, or was cut short by a re-order that
                // did not touch `order`/`target` -- nobody else is going to
                // tell this body to put the load down. Straight back to
                // Resting, which is where every order's own loop decides
                // what happens next.
                wasHauling = false;
                ReleaseClaim();
                phase = Phase.Resting;
                wait = RestSeconds;
                // **Put it down where it went**, not wherever he happens to
                // be: a body standing at the drop-off sets the load down
                // (the ledger deposited it this very step), one a few steps
                // short walks them first. Anything else lets go of it.
                if (!EndTripMime()) Drop();
                else return;
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
                    // **The build phase first.** A site with everything in
                    // it is being RAISED, and there is nothing left to
                    // fetch for it -- the man goes to the drawing and works
                    // on it. Same order the ledger spends its hand-days in
                    // (`OutpostLedger.Step`), so the picture and the books
                    // agree about what the camp is doing.
                    var focus = camp != null && camp.Ledger != null ? camp.Ledger.Focus : null;

                    // **Clearing before raising, and before hauling,
                    // 2026-09-23.** Kevin's rule: any blueprint may be
                    // dropped over standing trees or loose rock, and the
                    // camp clears the ground before anybody starts building
                    // on it. `Outpost.ClaimClearing` hands out obstructions
                    // the same way `ClaimTree` hands out trunks -- distinct
                    // per hand where possible, false when there is nothing
                    // left for THIS hand to claim (every obstruction is
                    // already somebody else's, or the site is clear), in
                    // which case he falls straight through to the ordinary
                    // haul/raise decision below.
                    clearing = r.order == OutpostOrder.Build && focus != null && !focus.Cleared
                        && camp.ClaimClearing(focus, this, out clearAt, out clearIsRock);
                    if (clearing)
                    {
                        target = Stand(clearAt);
                        raising = false;
                        clearFor = 0f;
                        phase = Phase.Going;
                        return;
                    }

                    // **Not Cleared yet is not Stocked either, as far as the
                    // picture goes.** Construction labour does not accrue
                    // until `Cleared` (`OutpostLedger`), so a man hammering
                    // a frame that still has a tree standing through it
                    // would be the animation lying about the numbers again.
                    raising = r.order == OutpostOrder.Build && focus != null
                        && focus.Cleared && focus.Stocked;
                    if (raising)
                    {
                        Vector3 sp = focus.At;
                        sp.y = camp.GroundAt(sp);
                        target = sp;
                        phase = Phase.Going;
                        return;
                    }
                    // **A builder with nothing to clear, raise or fetch right
                    // now waits by the fire, 2026-09-23.** Every trip he
                    // might be sent on -- store or rack or his own cut off
                    // the island, to the site -- is a ledger haul, and
                    // `Update` already sent it to `TickHaul` this frame if
                    // one was running. Reaching here with a Build order means
                    // there simply isn't one THIS tick; try again next.
                    if (r.order == OutpostOrder.Build) { wait = RestSeconds; return; }

                    target = FindSomethingToWorkAt(r);
                    phase = Phase.Going;
                    return;

                case Phase.Going:
                    acting?.Set(VillagerActing.Mode.None);
                    // Somebody else's claim took it, or the ledger cleared
                    // it out from under him while he was still walking.
                    // Turn round where he is rather than finish the walk to
                    // nothing standing (same shape as `TickCutting`'s
                    // mid-walk reclaim).
                    if (clearing && camp.ClearingTargetGone(this))
                    {
                        if (!NextClearing()) { phase = Phase.Resting; wait = RestSeconds; return; }
                        return;
                    }
                    if (!Walk(target, dt)) return;
                    phase = Phase.Working;
                    wait = SwingSeconds * Random.Range(0.85f, 1.35f);
                    clearFor = 0f;
                    acting?.Set(clearing ? VillagerActing.Mode.Chop
                        : raising ? VillagerActing.Mode.Hammer : ModeFor(WhatFor(r)));
                    return;

                case Phase.Working:
                    Face(target - transform.position, dt);
                    // **Swing until the ledger says it is gone, not for a
                    // fixed shift.** Same division as `TickCutting`'s
                    // `TreeIsFelled`: he never fells the tree or breaks the
                    // rock himself, and `Feel.chopPatience` is the same
                    // "full pile, stop swinging" timeout it uses.
                    if (clearing)
                    {
                        if (camp.ClearingTargetGone(this))
                        {
                            if (!NextClearing()) { Drop(); phase = Phase.Resting; wait = RestSeconds; return; }
                            phase = Phase.Going;
                            return;
                        }
                        clearFor += dt;
                        if (clearFor < Feel.chopPatience) return;
                        camp.ReleaseClearing(this);
                        clearing = false;
                        Drop();
                        phase = Phase.Resting;
                        wait = RestSeconds;
                        return;
                    }
                    wait -= dt;
                    if (wait > 0f) return;
                    // Raising it: he swings at the frame and walks nothing
                    // anywhere. The progress is the ledger's
                    // (`OutpostLedger.PayBuild`); this is the picture of it.
                    if (raising)
                    {
                        Drop();
                        phase = Phase.Resting;
                        wait = RestSeconds * 0.5f;
                        return;
                    }
                    // What they carry back is what the row says they are
                    // after — the only place this component reads an order
                    // for anything but a picture.
                    carrying = Carries(r);
                    dropAt = Dropoff(r, carrying);
                    phase = Phase.Coming;
                    acting?.Set(VillagerActing.Mode.Carry, carrying, CarryCount(r));
                    return;

                case Phase.Coming:
                    // **Re-aimed every step, 2026-09-23.** Kevin: *"if the
                    // building needed 1 more wood and all 4 villagers were
                    // carrying wood to the building site they deposited the
                    // wood even though the amount was already reached."*
                    // Four men set off with the site short of one log; by
                    // the time the second arrives it is short of none.
                    // `Dropoff` asks the ledger which site still WANTS what
                    // is on this man's shoulder, so the other three turn
                    // mid-walk and take it to the pile (or to the next
                    // drawing that is short of it) instead.
                    dropAt = Dropoff(r, carrying);
                    // The armful can shrink mid-walk (the store filled while
                    // he was on his way and `DepositHaul` put down what fit),
                    // so the stack he shoulders is re-read every step too —
                    // `Set` is a no-op unless it actually changed.
                    acting?.Set(VillagerActing.Mode.Carry, carrying, CarryCount(r));
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
                    // **No spear, no hunt, 2026-09-23.** Kevin: "to hunt,
                    // you need a spear." Checked before `ClaimQuarry` so an
                    // unarmed hand never claims a beast it cannot take.
                    bool unarmed = camp != null && camp.Ledger != null && camp.Ledger.HunterBlocker() != null;
                    if (unarmed || !ClaimQuarry())
                    {
                        // Nothing alive on the island, every beast left has
                        // a man on it, or nobody has a spear. He potters near
                        // the fire and asks again in a moment -- the same
                        // answer the cutter gives an island with no wood
                        // left on it.
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
                    // **A spear exists in the ledger now (2026-09-23), but
                    // not in the pose set.** `VillagerActing` has no hunting
                    // pose; Hammer is the overhand swing the miners use and
                    // it is the nearest thing there is. The gate that
                    // requires the spear lives in `Phase.Resting` above --
                    // this is only the mime, and it stays Hammer until a
                    // hunting pose is animated.
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
                    acting?.Set(VillagerActing.Mode.Carry, carrying, CarryCount(r));
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
                        acting?.Set(VillagerActing.Mode.Carry, carrying, CarryCount(r));
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

        /// Is this row cutting standing timber ON ITS OWN CLOCK -- the direct
        /// rate-based gather loop, never a booked ledger trip? Only a plain
        /// Gather-Timber order now: a builder's own cut off the island is a
        /// Field haul (`OutpostLedger.StartTimedTrip`), and `Update` sends
        /// any active haul to `TickHaul` before this is ever asked, so the
        /// Build case that used to live here can no longer fire.
        bool Cutting(OutpostHand r) => r != null && r.order == OutpostOrder.Gather && r.target == Res.Timber;

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

        /// One obstruction down (felled or broken by the ledger, same as
        /// `Reclaim`'s tree) -- claim the next one off this site and aim the
        /// walk at it. False means the site has nothing left for THIS hand
        /// to claim (cleared, or every obstruction left is somebody else's):
        /// callers drop the claim and go back to `Resting`, where the
        /// ordinary errand decision (raise, now that it may be `Cleared`, or
        /// haul) picks up again.
        bool NextClearing()
        {
            var focus = camp != null && camp.Ledger != null ? camp.Ledger.Focus : null;
            if (focus != null && camp.ClaimClearing(focus, this, out clearAt, out clearIsRock))
            {
                target = Stand(clearAt);
                clearFor = 0f;
                return true;
            }
            camp.ReleaseClearing(this);
            clearing = false;
            return false;
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
        ///
        /// **A production STATION is its bench, 2026-09-24.** Kevin, phone
        /// playtest: *"the assigned worker started making arrows before wood
        /// logs were physically at his station"* and *"when he completes
        /// arrows he goes and puts them at the campfire ... instead of
        /// placing them in the finished arrow section of the fletchery."*
        /// The shift clock and the walk to the pile below were this
        /// component's own schedule, older than `StationStock`. For a station
        /// the ledger now says everything:
        ///
        /// - He stands at the station's `Worker_Stand` (see `WorkSpot`) and
        ///   mimes the trade ONLY while `benchState` is Loaded or Working.
        ///   The bench loads in the same ledger step that deposits the raw
        ///   (`WorkerDay`: `AdvanceHaul` then `TryLoad`), and the haul mime
        ///   now sets the raw down at that step (`TickHaul`), so the swing
        ///   visibly starts after the logs land.
        /// - A finished job goes on the station's own RACK in the books
        ///   (`FinishJob` -> `UnloadBench`), so he carries it a few steps to
        ///   the rack (`OutputSpot`) and puts it there -- timed so the set-down
        ///   lands on the step that fills the rack (`SecondsToJobDone`). The
        ///   walk to the store is a real `HaulOf` trip (rack -> store) and
        ///   `TickHaul` draws it; nothing here goes near the fire.
        ///
        /// Anything that employs somebody but is not a station (the farm,
        /// the watchtower) keeps the old shift loop, `TickWorkAt`.
        void TickWork(OutpostHand r, float dt)
        {
            var ledger = camp.Ledger;
            var st = ledger != null ? ledger.StationOfHand(r) : null;
            if (st == null || st.removed) { TickWorkAt(r, dt); return; }

            Building post = PostOf(r, ledger, st);
            Vector3 stand = post != null ? WorkSpot(camp, post) : home;
            Vector3 face = post != null ? BenchPoint(post, stand) : lookAt;

            // A job came off the bench a moment ago and nobody walked it to
            // the rack ahead of time (see the paced carry below): walk it
            // there now, from wherever he is. A stale one (he was out on a
            // trip when it finished) is let go -- a man arriving back with
            // arrows made half a minute ago would be a second lie.
            if (owedCarry)
            {
                owedCarry = false;
                if (post != null && Time.time - owedAt <= OwedCarrySeconds)
                {
                    Vector3 outAt = OutputSpot(post, out Vector3 outFace);
                    StartDelivery(outAt, outFace, owedRes, owedCount, 0f);
                    return;
                }
            }

            if (!Walk(stand, dt))
            {
                acting?.Set(VillagerActing.Mode.None);
                phase = Phase.Going;
                return;
            }
            // At his post: `Working` whatever the bench is doing, because
            // that is the phase `Update`'s walk-in check reads -- a new hand
            // whose bench cannot load until the books start paying him must
            // not wait out `WalkInLimit` for a swing that needs him paid.
            phase = Phase.Working;
            Face(face - transform.position, dt);

            bool busy = st.benchState == BenchState.Loaded || st.benchState == BenchState.Working;
            if (!busy)
            {
                // Empty bench (inputs still walking in, no order, nothing to
                // make), or a finished job the full rack will not take: he
                // waits at the bench. Idle IS the picture of that.
                acting?.Set(VillagerActing.Mode.None);
                return;
            }
            acting?.Set(ModeAt(r.target));

            // **The rack carry, paced to the books.** The job comes off the
            // bench on a ledger step (`SecondsToJobDone` says which, in real
            // seconds from now); leave the bench one short walk before it so
            // the set-down and the rack filling are the same moment. Only
            // when the rack has room for the yield -- otherwise `UnloadBench`
            // leaves it on the bench and there is nothing to carry.
            if (post == null || jobCarried) return;
            var rec = st.BenchRecipe;
            if (rec == null) return;
            int yield = Mathf.Max(1, rec.yield);
            if (st.RackRoom < yield) return;
            Vector3 rackAt = OutputSpot(post, out Vector3 rackFace);
            float walk = FlatDistance(transform.position, rackAt) / Speed;
            float due = SecondsToJobDone(r, st);
            if (due > walk + LeaveLead) return;
            jobCarried = true;
            StartDelivery(rackAt, rackFace, rec.makes, yield, due);
        }

        /// **The old shift loop**, for a building that employs somebody but
        /// keeps no `StationStock` (the farm, whose field is its input and
        /// whose yield is a per-day rate into the store; the watchtower).
        /// Walk to the door, work the shift, carry what the building makes to
        /// its stack by the fire, come back -- unchanged from before
        /// 2026-09-24, because for these the pile by the fire IS where the
        /// books put it.
        void TickWorkAt(OutpostHand r, float dt)
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
                    acting?.Set(VillagerActing.Mode.Carry, carrying, CarryCount(r));
                    return;

                case Phase.Coming:
                    if (!Walk(dropAt, dt)) return;
                    Drop();
                    phase = Phase.Resting;
                    wait = RestSeconds;
                    return;
            }
        }

        // --- hauling (2026-09-23) ----------------------------------------------

        /// **Mime the ledger's own trip.** `OutpostLedger.HaulOf(row)` is the
        /// one account of where a hauling hand is and what is on his
        /// shoulder -- station input and output, a builder's site stocking, a
        /// stationed worker fetching his own raw, all the same shape (see the
        /// "trip timing" doc block on `OutpostLedger.Stations`). This reads
        /// it fresh every frame and paints the picture; it keeps no walk
        /// schedule of its own that could drift from the books.
        ///
        /// **Paced to the deposit, 2026-09-24.** Kevin, phone playtest: *"a
        /// villager brought logs to the fletchery and stood there, in the
        /// middle of the asset, for about 40 seconds before placing them."*
        /// The old mime walked each stage when `progress01` said so and then
        /// held at the drop-off until it read 1. Three things put daylight
        /// between that and the books:
        ///
        /// 1. The ledger only moves in 0.1-day steps (`QuantumDays`, 18 s
        ///    at the playtest day length), so `progress01` is a staircase:
        ///    the "carry" stage was seen up to a step late and the deposit
        ///    lands up to a step after the continuous clock reaches 1.
        /// 2. The trip is paid at the hand's work factor (hunger floors it
        ///    at 0.35), so a 10 s leg in the books can be 29 s of real time
        ///    while the body still walks it in 10.
        /// 3. The books time abstract legs (straight line x `PathFactor`,
        ///    a Field leg measured from the camp centre); the body walks its
        ///    own tree, its own route.
        ///
        /// So the body stops reading the stage off `progress01` and instead
        /// asks **when the ledger will put this load down**, in real seconds
        /// from now (`SecondsToDeposit`, the exact step arithmetic of
        /// `AdvanceHaul`). The schedule is then its own walk:
        ///
        ///   - walk out at once;
        ///   - at the pickup, work it (chop at a tree, a stoop at a store or
        ///     rack) until `due <= walkBack + LeaveLead`, where `walkBack` is
        ///     the body's REAL route from the pickup to the drop-off over
        ///     `Speed` (`BackSeconds`, a `CampPath` plan like `Walk` uses);
        ///   - carry it back, arriving as the step lands;
        ///   - on arrival still ahead of the books, hold it at most
        ///     `MaxHoldSeconds` and then set it down anyway (Kevin's rule:
        ///     never stand holding it);
        ///   - the moment the books deposit (the trip ends), set it down
        ///     (`EndTripMime`) -- the same frame the bay/rack/pile fills.
        ///
        /// Equivalently, in the brief's terms: leave the pickup once
        /// `progress01 >= 1 - walkBack / tripRealSeconds`, where
        /// `tripRealSeconds` is the trip's length on the real clock at this
        /// hand's work factor, rounded to the ledger's step grid.
        ///
        /// A clock the body cannot predict (time paused, a hand the books
        /// are not paying) falls back to the ledger's own stage thresholds.
        ///
        /// **Teleport-free catch-up, still.** A hand who goes from unwatched
        /// to watched mid-trip walks from wherever he stands; one the books
        /// already have carrying, with no time left to fetch it properly,
        /// shoulders it where he is. One that is still short of the drop-off
        /// when the books deposit walks the last few metres and sets it down
        /// (`TailMetres`), rather than having it vanish from his arms.
        void TickHaul(OutpostHand r, float dt)
        {
            var view = camp.Ledger.HaulOf(r);
            if (!view.active) return;

            // A different trip than the one this body was last picturing --
            // a fresh haul, or a row that was doing something else (its own
            // claimed tree, a bed, a clearing) right up until it picked this
            // one up. The last trip's load is put down where it was going
            // first (it went into the books this step), and whatever the old
            // trip or the old errand had claimed is not this trip's to keep.
            if (r.haulSerial != mimedTrip)
            {
                bool settingDown = EndTripMime();
                ReleaseClaim();
                mimedTrip = r.haulSerial;
                BeginTripMime(r, view);
                if (settingDown) return;     // `TickDelivery` owns the next moment
            }

            // Real seconds until the ledger step that puts this load down.
            // Infinity when it cannot be known; 0 when the books already have
            // him standing at a full store with it.
            float due = SecondsToDeposit(r);
            bool clockKnown = !float.IsInfinity(due);
            bool booksCarrying = view.progress01 >= view.workEnd01;

            if (!mimeLoaded)
            {
                Vector3 pick = view.from == HaulPlace.Field ? HaulPickupSpot(view) : mimePick;
                bool atPick = Walk(pick, dt);
                if (atPick)
                {
                    // Early at the pickup (the usual case): work it until the
                    // walk back will land on the deposit.
                    Face(PickFace(view) - transform.position, dt);
                    acting?.Set(PickMode(view));
                    bool go = clockKnown
                        ? due <= BackSeconds(pick, mimeDrop) + LeaveLead
                        : booksCarrying;
                    if (!go) return;
                }
                else
                {
                    acting?.Set(VillagerActing.Mode.None);
                    // Still walking out. Normally that is all -- even when
                    // his own walk is longer than the books' and they have
                    // him carrying already, he goes on to the pickup: a man
                    // who turns round on the path with a load he never
                    // picked up is a worse lie than one who is late. The one
                    // exception is a body that joined this trip already past
                    // its walk out (he came on screen mid-trip, or the Hand
                    // just set him down) with too little time left to walk
                    // to the pickup and back: he shoulders it where he is.
                    if (!booksCarrying || !mimeJoinedLate) return;
                    if (clockKnown)
                    {
                        float fetch = FlatDistance(transform.position, pick) / Speed
                                      + BackSeconds(pick, mimeDrop);
                        if (due >= fetch) return;
                    }
                }
                mimeLoaded = true;
            }

            // Set down early (held `MaxHoldSeconds` and the books still had
            // not caught up): he waits beside it, empty-handed, until the
            // step that deposits it ends the trip.
            if (mimePlaced)
            {
                acting?.Set(VillagerActing.Mode.None);
                Face(mimeFace - transform.position, dt);
                return;
            }

            acting?.Set(VillagerActing.Mode.Carry, view.resource, Mathf.Max(1, view.count));
            if (!mimeArrived)
            {
                if (!Walk(mimeDrop, dt)) return;
                mimeArrived = true;
            }
            Face(mimeFace - transform.position, dt);

            // At the drop-off ahead of the books. At a full store the books
            // say exactly this -- standing there holding it until room comes
            // (`DepositHaul`, `haulLeft` 0) -- so he holds it as long as that
            // lasts. Otherwise a moment's grace for the step to land, then
            // the load goes down.
            if (view.progress01 >= 1f - 1e-4f) return;
            mimeHold += dt;
            if (mimeHold < MaxHoldSeconds) return;
            mimePlaced = true;
            StartPlace(mimeFace);
        }

        // --- pacing the mime to the books (2026-09-24) ---------------------

        /// Seconds early a body leaves the pickup / the bench: the deposit
        /// is SEEN a little after its ledger step (`Outpost.CatchUp` runs
        /// four times a second), and arriving a hair early is a set-down on
        /// the step; a hair late is a load that vanishes a pace short.
        const float LeaveLead = 0.15f;
        /// Longest a body stands at the drop-off holding a load the books
        /// have not put down yet. Kevin's rule: then set it down anyway.
        const float MaxHoldSeconds = 1f;
        /// How long the set-down stoop lasts.
        const float PlaceSeconds = 0.6f;
        /// A load the books deposited while the body was still this far
        /// from the drop-off is walked the rest of the way and set down; any
        /// further and it is let go where he is (he was hopelessly behind).
        const float TailMetres = 10f;
        /// A job that came off the bench more than this long ago is not
        /// carried to the rack after the fact (he was away on a trip).
        const float OwedCarrySeconds = 1f;
        /// Mean latency between a ledger step and a `CatchUp` seeing it
        /// (every 0.25 s unscaled): added to every "due" so the aim is the
        /// moment the books are SEEN to change.
        const float CatchUpLag = 0.1f;

        // The trip being drawn (`mimedTrip` is its serial).
        bool mimeLoaded;          // shouldered: the carry leg
        bool mimeArrived;         // at the drop-off with it
        bool mimePlaced;          // set down ahead of the books (the hold ran out)
        bool mimeJoinedLate;      // first seen past its walk out (caught up mid-trip)
        float mimeHold;           // seconds held at the drop-off so far
        Vector3 mimePick, mimePickFace;   // a store/station pickup (Field picks its own)
        Vector3 mimeDrop, mimeFace;       // where the load goes, and what to face there
        string mimeRes;
        int mimeCount;

        // The body's own walk back from pickup to drop-off, cached per trip.
        bool backKnown;
        float backSeconds;
        Vector3 backFrom, backTo;
        readonly List<Vector3> backRoute = new List<Vector3>();

        /// Resolve this trip's two ends to where a body actually stands:
        /// Astra's `Input_Pickup`/`Output_Dropoff` at a station (never the
        /// middle of the model, which is where the books' `fromAt`/`toAt`
        /// point), the resource's own stack by the fire (or the storage
        /// building's door) for the store. Once per trip: nothing here moves
        /// during one. A Field pickup is the body's own tree/prop and is
        /// re-picked live in `HaulPickupSpot`.
        void BeginTripMime(OutpostHand r, HaulView view)
        {
            mimeLoaded = mimeArrived = mimePlaced = false;
            mimeHold = 0f;
            backKnown = false;
            mimeJoinedLate = view.progress01 > view.walkOutEnd01;
            mimeRes = view.resource;
            mimeCount = Mathf.Max(1, view.count);
            mimeDrop = TripEnd(view.to, view.toStation, view.toAt, view.resource, true, out mimeFace);
            if (view.from != HaulPlace.Field)
                mimePick = TripEnd(view.from, view.fromStation, view.fromAt, view.resource,
                    view.from == HaulPlace.Station && r.haulFromBay, out mimePickFace);
        }

        /// The last trip is over in the books (the load went down on the
        /// step just seen). If this body had it shouldered: at the drop-off,
        /// the set-down stoop now; a few steps short, walk them then set it
        /// down. True when a delivery was started (the caller must not
        /// `Drop()`, which would cancel it).
        bool EndTripMime()
        {
            bool holding = mimeLoaded && !mimePlaced;
            mimeLoaded = mimeArrived = mimePlaced = false;
            mimeHold = 0f;
            backKnown = false;
            if (!holding) return false;
            float left = FlatDistance(transform.position, mimeDrop);
            if (left <= 0.6f) { StartPlace(mimeFace); return true; }
            if (left <= TailMetres)
            {
                StartDelivery(mimeDrop, mimeFace, mimeRes, mimeCount, 0f);
                return true;
            }
            return false;
        }

        /// Thrown, lifted, or otherwise yanked out of the trip: the next
        /// `TickHaul` starts drawing it afresh from wherever he lands.
        void ForgetTrip()
        {
            mimedTrip = -1;
            mimeLoaded = mimeArrived = mimePlaced = false;
            mimeHold = 0f;
            backKnown = false;
            CancelDelivery();
        }

        /// Where a body stands at one end of a trip, and what it faces.
        /// `inputSide` picks the station's input bay over its output rack
        /// (a delivery TO a station, or a bay-to-store return).
        Vector3 TripEnd(HaulPlace place, int station, Vector3 at, string res, bool inputSide,
            out Vector3 face)
        {
            face = at;
            switch (place)
            {
                case HaulPlace.Station:
                {
                    var b = StationBuilding(station);
                    if (b == null) break;
                    return inputSide ? InputSpot(b, out face) : OutputSpot(b, out face);
                }
                case HaulPlace.Store:
                    return StoreSpot(res, at, out face);
            }
            // A site (the drawing itself), or a station whose building the
            // body cannot find (a probe's hand-written ledger): the books'
            // own point.
            return Grounded(at);
        }

        /// The store's end of a trip. Before a Storage/Storehouse stands the
        /// store IS the stacks by the fire (`CampPiles`), so the load goes to
        /// its own stack (`PileSpot`), not into the fire. After, the storage
        /// building's `Input_Pickup` if Astra gave it one, else the side of
        /// it that faces the fire.
        Vector3 StoreSpot(string res, Vector3 at, out Vector3 face)
        {
            var ledger = camp.Ledger;
            if (ledger != null && ledger.HasStorageBuilding)
            {
                var b = BuildingAt(BuildPlans.Storage.id, at) ?? BuildingAt(BuildPlans.Storehouse.id, at);
                if (b != null)
                {
                    face = b.transform.position;
                    var m = MarksOf(b);
                    if (m.inPick != null) return Grounded(m.inPick.position);
                    return EdgeBeyond(b, camp.CampCentre);
                }
            }
            face = PileAt(res);
            return PileSpot(res);
        }

        /// Real seconds until the ledger step that deposits this hand's
        /// load, or +infinity when that cannot be predicted.
        ///
        /// Exactly `AdvanceHaul`'s arithmetic: every step
        /// (`OutpostLedger.QuantumDays` of game time) spends
        /// `QuantumDays x TripFactor` of the trip's `haulLeft` (game-days of
        /// work), first thing in the hand's day, and the load goes down in
        /// the step that takes `haulLeft` to zero. So the deposit is
        /// `ceil(haulLeft / perStep)` steps after the ledger's last one
        /// (`lastTicked`), converted from the game clock to real seconds.
        float SecondsToDeposit(OutpostHand r)
        {
            // Already at zero: standing at a full store in the books.
            if (r.haulLeft <= 1e-5f) return 0f;
            return SecondsUntilSpent(r.haulLeft, OutpostLedger.QuantumDays * TripFactor(r));
        }

        /// Real seconds until the bench's current job comes off it (the step
        /// in which `WorkerDay` calls `FinishJob`), or +infinity. Same step
        /// arithmetic as the haul, over bench progress: `WorkerDay` advances
        /// a Working bench by `rate / yield x days x WorkFactor` a step,
        /// `rate = ratePerDay x Techs.RateMul x PriorityMultiplier(makes)`.
        float SecondsToJobDone(OutpostHand r, StationStock st)
        {
            var rec = st != null ? st.BenchRecipe : null;
            var ledger = camp.Ledger;
            if (rec == null || ledger == null) return float.PositiveInfinity;
            float rate = rec.ratePerDay * Economy.Techs.RateMul(st.planId, ledger.LevelOf(st.planId))
                         * ledger.PriorityMultiplier(rec.makes);
            float perStep = rate / Mathf.Max(1, rec.yield)
                            * OutpostLedger.QuantumDays * OutpostLedger.WorkFactor(r);
            return SecondsUntilSpent(1f - st.benchProgress, perStep);
        }

        /// **The ledger's step grid on the real clock.** `left` units that
        /// are spent `perStep` a step run out in `ceil(left / perStep)`
        /// steps; step k lands at game time `lastTicked + k x quantum`
        /// (`OutpostLedger.Tick` keeps `lastTicked` on the grid), which is
        /// `(that - TimeOfDay.Seconds) / clockRate` real seconds from now,
        /// plus the mean `CatchUp` latency.
        float SecondsUntilSpent(float left, float perStep)
        {
            var ledger = camp.Ledger;
            if (ledger == null || perStep <= 1e-7f) return float.PositiveInfinity;
            float rate = ClockRate();
            if (rate <= 0.01f) return float.PositiveInfinity;          // time stopped
            float steps = Mathf.Ceil((left - 1e-5f) / perStep);
            if (steps > 10000f) return float.PositiveInfinity;
            steps = Mathf.Max(1f, steps);
            double quantum = OutpostLedger.QuantumDays * (double)Mathf.Max(0.0001f, TimeOfDay.DayLength);
            double landsAt = ledger.lastTicked + steps * quantum;
            float gameLeft = (float)(landsAt - TimeOfDay.Seconds);
            return Mathf.Max(0f, gameLeft / rate) + CatchUpLag;
        }

        /// **Which work factor the ledger spends this hand's trip at.** A
        /// replica of the dispatch in `OutpostLedger.Step`, deliberately:
        /// a trip gatherer's arms are advanced by `GatherDay` at
        /// `WorkFactorOn(target) x PriorityMultiplier(target)`; everybody
        /// else's (a station worker in `WorkerDay`, a hauler in
        /// `HaulerDay`, a builder in `BuilderDay`) at plain `WorkFactor`. If
        /// the ledger's passes change, this changes with them -- the cost
        /// of getting it wrong is a body that arrives a step early or late,
        /// never a wrong number.
        float TripFactor(OutpostHand r)
        {
            if (r.order == OutpostOrder.Gather && !string.IsNullOrEmpty(r.target) && r.target != Res.Game)
                return OutpostLedger.WorkFactorOn(r, r.target) * camp.Ledger.PriorityMultiplier(r.target);
            return OutpostLedger.WorkFactor(r);
        }

        /// Game seconds per real (scaled) second: `TimeOfDay` is advanced by
        /// `SkyDirector` at its own time scale, which nothing here should
        /// hard-code. Measured once a frame for every worker, smoothed, and
        /// blind to scrubs (a dev tool jumping the clock).
        static int clockFrame = -1;
        static double clockLast = double.NaN;
        static float clockRate = 1f;

        static float ClockRate()
        {
            int f = Time.frameCount;
            if (f == clockFrame) return clockRate;
            clockFrame = f;
            double now = TimeOfDay.Seconds;
            float dt = Time.deltaTime;
            if (!double.IsNaN(clockLast) && dt > 1e-5f)
            {
                double d = now - clockLast;
                if (d >= 0.0 && d <= 100.0 * dt + 0.5)
                    clockRate = Mathf.Lerp(clockRate, (float)(d / dt), 1f - Mathf.Exp(-3f * dt));
            }
            clockLast = now;
            return clockRate;
        }

        /// **The body's own walk time from pickup to drop-off**, over the
        /// same route `Walk` will take: straight under 6 m (`NextCorner`),
        /// else a `CampPath` plan. Planned once per trip under the shared
        /// plan budget; while the budget is spent this frame, the straight
        /// line x `PathFactor` stands in and the plan is tried again next
        /// frame. (`Walk` retires corners within `CornerReach`, so the real
        /// walk is a touch shorter than the polyline: the body arrives a
        /// little early and the hold covers it.)
        float BackSeconds(Vector3 from, Vector3 to)
        {
            if (backKnown
                && FlatDistance(from, backFrom) < 1f && FlatDistance(to, backTo) < 1f)
                return backSeconds;

            float straight = FlatDistance(from, to);
            backFrom = from;
            backTo = to;
            if (straight < 6f)
            {
                backSeconds = straight / Speed;
                backKnown = true;
                return backSeconds;
            }

            var map = CampPath.For(camp);
            if (map != null && CampPath.Budget())
            {
                backKnown = true;
                if (map.Route(from, to, CampPath.Walker.Hand, backRoute) && backRoute.Count > 0)
                {
                    float len = 0f;
                    Vector3 p = from;
                    for (int i = 0; i < backRoute.Count; i++)
                    {
                        len += FlatDistance(p, backRoute[i]);
                        p = backRoute[i];
                    }
                    len += FlatDistance(p, to);
                    backSeconds = len / Speed;
                    return backSeconds;
                }
                // No route worth having: `Walk` goes straight, so does this.
                backSeconds = straight / Speed;
                return backSeconds;
            }
            backKnown = false;
            return straight * OutpostLedger.PathFactor / Speed;
        }

        /// The pose at a pickup: the swing that suits the material at the
        /// island's tree or rock, a stoop over a store's stack or a
        /// station's bay or rack.
        static VillagerActing.Mode PickMode(HaulView view) =>
            view.from == HaulPlace.Field ? ModeFor(view.resource) : VillagerActing.Mode.Bend;

        /// What to face at a pickup: the tree or prop itself on the island
        /// (the stand spot is a pace off it), else the pickup's own anchor.
        Vector3 PickFace(HaulView view)
        {
            if (view.from != HaulPlace.Field) return mimePickFace;
            if (view.resource == Res.Timber) return claimedTree >= 0 ? claimAt : camp.CampCentre;
            return fieldNode != null ? fieldNode.transform.position : camp.CampCentre;
        }

        // --- delivering: the last steps and the set-down ------------------------

        bool delivering;
        Vector3 deliverTo, deliverFace;
        string deliverRes;
        int deliverCount;
        float deliverFor, deliverMax, deliverDueAt, deliverHold;
        float placeLeft;
        Vector3 placeFace;

        /// Walk `count` of `res` to `to` and set it down there. `dueIn` > 0:
        /// the books put it there that many seconds from now (a carry paced
        /// ahead of the step), so on arrival he holds it until then, at most
        /// `MaxHoldSeconds`. 0: the books already have it there.
        void StartDelivery(Vector3 to, Vector3 face, string res, int count, float dueIn)
        {
            delivering = true;
            deliverTo = to;
            deliverFace = face;
            deliverRes = res;
            deliverCount = Mathf.Max(1, count);
            deliverFor = 0f;
            deliverHold = 0f;
            deliverDueAt = dueIn > 0f ? Time.time + dueIn : 0f;
            // Generous, and only a backstop: nothing may strand him.
            deliverMax = FlatDistance(transform.position, to) / Speed + 2f;
            placeLeft = 0f;
            acting?.Set(VillagerActing.Mode.Carry, res, deliverCount);
        }

        /// The set-down itself: a short stoop with empty arms (the carried
        /// stack is gone once the pose changes).
        void StartPlace(Vector3 face)
        {
            delivering = false;
            placeLeft = PlaceSeconds;
            placeFace = face;
            acting?.Set(VillagerActing.Mode.Bend);
        }

        void CancelDelivery()
        {
            delivering = false;
            placeLeft = 0f;
            deliverHold = 0f;
        }

        /// One frame of a delivery or a set-down. True while either owns the
        /// body (the caller does nothing else this frame).
        bool TickDelivery(float dt)
        {
            if (placeLeft > 0f)
            {
                placeLeft -= dt;
                Face(placeFace - transform.position, dt);
                if (placeLeft > 0f) { acting?.Set(VillagerActing.Mode.Bend); return true; }
                acting?.Set(VillagerActing.Mode.None);
                return true;
            }
            if (!delivering) return false;

            deliverFor += dt;
            acting?.Set(VillagerActing.Mode.Carry, deliverRes, deliverCount);
            bool there = Walk(deliverTo, dt);
            if (!there && deliverFor < deliverMax) return true;
            if (there && Time.time < deliverDueAt && deliverHold < MaxHoldSeconds)
            {
                deliverHold += dt;
                Face(deliverFace - transform.position, dt);
                return true;
            }
            StartPlace(deliverFace);
            return true;
        }

        // --- the bench, watched --------------------------------------------------

        StationStock benchRow;    // the station this body last looked at
        bool benchBusy;           // Loaded/Working last frame
        float benchProgressWas;
        int benchMadeWas;         // rack + finished bench, last frame
        string benchMakes;        // what the job on the bench makes
        int benchYield = 1;
        bool jobCarried;          // this job's output already walked to the rack, ahead of the step
        bool owedCarry;           // a job came off unannounced: carry it (TickWork)
        float owedAt;
        string owedRes;
        int owedCount;

        /// **Did a job just come off the bench?** Read off the books every
        /// frame: the bench was busy and is not any more, or its progress
        /// went backwards (finished and the next batch loaded in the same
        /// step), or the rack grew. A carry already paced ahead of it
        /// (`jobCarried`) is consumed; otherwise one is owed, provided the
        /// job actually went onto the rack (a full rack leaves it on the
        /// bench, `BenchState.Finished`, and there is nothing to carry).
        void WatchBench(OutpostHand r)
        {
            var ledger = camp.Ledger;
            var st = ledger != null && r.order == OutpostOrder.Work ? ledger.StationOfHand(r) : null;
            if (st == null || st.removed)
            {
                benchRow = null;
                jobCarried = owedCarry = false;
                return;
            }

            bool busy = st.benchState == BenchState.Loaded || st.benchState == BenchState.Working;
            int made = st.RackTotal + (st.benchState == BenchState.Finished ? st.benchOut : 0);
            if (!ReferenceEquals(st, benchRow))
            {
                benchRow = st;
                jobCarried = owedCarry = false;
            }
            else
            {
                bool done = made > benchMadeWas
                    || (benchBusy && (!busy || st.benchProgress + 1e-4f < benchProgressWas));
                if (done)
                {
                    if (jobCarried) jobCarried = false;
                    else if (st.benchState != BenchState.Finished && !string.IsNullOrEmpty(benchMakes))
                    {
                        owedCarry = true;
                        owedAt = Time.time;
                        owedRes = benchMakes;
                        owedCount = benchYield;
                    }
                }
            }
            benchBusy = busy;
            benchProgressWas = st.benchProgress;
            benchMadeWas = made;
            var rec = st.BenchRecipe;
            if (busy && rec != null) { benchMakes = rec.makes; benchYield = Mathf.Max(1, rec.yield); }
        }

        // --- stations on the ground: markers ------------------------------------

        Building workPost;
        StationStock workPostRow;
        float workPostChecked;

        /// The building a stationed hand works at: the ledger's own instance
        /// (`StationOfHand` deals hands round a plan's buildings), found on
        /// the ground by its `raised` row's position. Falls back to the
        /// Hand's choice / the plan's first building. Re-resolved twice a
        /// second, like `Row`.
        Building PostOf(OutpostHand r, OutpostLedger ledger, StationStock st)
        {
            if (workPost != null && ReferenceEquals(workPostRow, st) && Time.time < workPostChecked)
                return workPost;
            workPostRow = st;
            workPostChecked = Time.time + 0.5f;
            Building b = ledger.stations != null ? StationBuilding(ledger.stations.IndexOf(st)) : null;
            if (b == null)
                b = preferred != null && preferred.Id == r.target ? preferred : camp.WorkplaceOf(r);
            workPost = b;
            return b;
        }

        /// The standing building of station `index`: its plan's `raised`
        /// row of the same ordinal (`OutpostLedger.StationPlace`, the very
        /// point `Outpost.Raise` stood it at), matched against `Built` the
        /// way `StationStockView.ResolveStation` matches it. Read-only: the
        /// ledger's `stations` list is read directly rather than through
        /// `StationAt`, which may re-sync the books.
        Building StationBuilding(int index)
        {
            var ledger = camp.Ledger;
            if (ledger == null || ledger.stations == null || index < 0 || index >= ledger.stations.Count)
                return null;
            var s = ledger.stations[index];
            if (s == null || !ledger.StationPlace(index, out Vector3 at)) return null;
            return BuildingAt(s.planId, at);
        }

        /// The building of this plan standing at this point (half a metre's
        /// grace), or null. A null plan matches anything.
        Building BuildingAt(string planId, Vector3 at)
        {
            var built = camp.Built;
            if (built == null) return null;
            Building best = null;
            float bestSq = 0.25f;
            for (int i = 0; i < built.Count; i++)
            {
                var b = built[i];
                if (b == null || (planId != null && b.Id != planId)) continue;
                Vector3 d = b.transform.position - at;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = b; }
            }
            return best;
        }

        /// **Astra's station markers**, found once per model. Names may
        /// carry FBX `.001` suffixes, so they match on the stem
        /// (`BuildingFactory.Stem`, as `StationStockView` does):
        /// `Input_Pickup` (where a hauler stands to deliver to / fetch from
        /// the input bay), `Output_Dropoff` (in front of the rack),
        /// `Worker_Stand`, and the slot groups `Input_Container`/
        /// `Input_Anchor`, `Output_Container`/`Output_Anchor` and the
        /// bench (`Bench_Anchor`, or the kits' own work anchors). The
        /// fletcher, forge and kitchen carry only the `_Anchor` groups and
        /// `Worker_Stand`; the quarry carries all of them.
        sealed class Marks
        {
            public Transform inPick, inGroup, outDrop, outGroup, stand, bench;
            public bool Stale =>
                Dead(inPick) || Dead(inGroup) || Dead(outDrop) || Dead(outGroup) || Dead(stand) || Dead(bench);
            static bool Dead(Transform t) => !ReferenceEquals(t, null) && t == null;
        }

        static readonly Dictionary<Building, Marks> marksOf = new Dictionary<Building, Marks>();

        static Marks MarksOf(Building b)
        {
            if (marksOf.TryGetValue(b, out var m) && !m.Stale) return m;
            // Demolished buildings leave dead keys behind; a camp has a few
            // dozen buildings, so an occasional clear is cheaper than
            // tracking them.
            if (marksOf.Count > 64) marksOf.Clear();
            m = new Marks();
            Transform benchAlt = null;
            foreach (var t in b.GetComponentsInChildren<Transform>(true))
            {
                switch (BuildingFactory.Stem(t.name))
                {
                    case "Input_Pickup": m.inPick = t; break;
                    case "Output_Dropoff": m.outDrop = t; break;
                    case "Worker_Stand": m.stand = t; break;
                    case "Input_Container": m.inGroup = t; break;
                    case "Output_Container": m.outGroup = t; break;
                    case "Input_Anchor": if (m.inGroup == null) m.inGroup = t; break;
                    case "Output_Anchor": if (m.outGroup == null) m.outGroup = t; break;
                    case "Bench_Anchor": m.bench = t; break;
                    // The kits that name their bench for what happens at
                    // it: the forge's anvil, the fletcher's shaving horse,
                    // the kitchen's prep table.
                    case "Anvil_Anchor":
                    case "Shaping_Anchor":
                    case "Prep_Anchor":
                        if (benchAlt == null) benchAlt = t;
                        break;
                }
            }
            if (m.bench == null) m.bench = benchAlt;
            marksOf[b] = m;
            return m;
        }

        /// Where a hauler stands at a station's INPUT bay, and what he
        /// faces: `Input_Pickup`; else just outside the footprint on the
        /// line from the centre through the input group; else the model's
        /// +X side (Astra's convention: input +X, output -X, front +Z).
        Vector3 InputSpot(Building b, out Vector3 face)
        {
            var m = MarksOf(b);
            face = m.inGroup != null ? m.inGroup.position : b.transform.position;
            if (m.inPick != null) return Grounded(m.inPick.position);
            if (m.inGroup != null) return EdgeBeyond(b, m.inGroup.position);
            return EdgeBeyond(b, b.transform.position + Flat(b.transform.right));
        }

        /// The same for the OUTPUT rack: `Output_Dropoff`, else beyond the
        /// output group, else the model's -X side.
        Vector3 OutputSpot(Building b, out Vector3 face)
        {
            var m = MarksOf(b);
            face = m.outGroup != null ? m.outGroup.position : b.transform.position;
            if (m.outDrop != null) return Grounded(m.outDrop.position);
            if (m.outGroup != null) return EdgeBeyond(b, m.outGroup.position);
            return EdgeBeyond(b, b.transform.position - Flat(b.transform.right));
        }

        /// What a worker at his stand faces: the bench anchor, else the
        /// building's middle, else (standing on the middle, as Astra's
        /// kitchen worker does) the model's front.
        static Vector3 BenchPoint(Building b, Vector3 stand)
        {
            var m = MarksOf(b);
            if (m.bench != null) return m.bench.position;
            Vector3 c = b.transform.position;
            Vector3 d = c - stand;
            d.y = 0f;
            if (d.sqrMagnitude > 0.25f) return c;
            return stand + Flat(b.transform.forward);
        }

        /// **Just outside the footprint**, on the ray from the building's
        /// middle through `through`: the footprint's half extents in the
        /// building's own frame (`Building.Footprint`: x along the ridge =
        /// local X, y across = local Z), yaw only, plus a pace.
        Vector3 EdgeBeyond(Building b, Vector3 through)
        {
            Vector3 c = b.transform.position;
            Vector3 d = through - c;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) d = Flat(b.transform.right);
            d.Normalize();
            Vector2 fp = b.Footprint;
            if (fp.x <= 0f || fp.y <= 0f) fp = BuildPlans.Named(b.Id).footprint;
            float hx = Mathf.Max(0.5f, fp.x * 0.5f), hz = Mathf.Max(0.5f, fp.y * 0.5f);
            Vector3 local = Quaternion.Euler(0f, -b.transform.eulerAngles.y, 0f) * d;
            float ax = Mathf.Abs(local.x), az = Mathf.Abs(local.z);
            float reach = Mathf.Min(ax > 1e-4f ? hx / ax : float.MaxValue,
                                    az > 1e-4f ? hz / az : float.MaxValue);
            Vector3 p = c + d * (reach + 0.6f);
            p.y = camp.GroundAt(p);
            return p;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        Vector3 Grounded(Vector3 p) { p.y = camp.GroundAt(p); return p; }

        /// **Where a hauling hand actually stands to pick the load up.**
        /// Store, station and site pickups are exactly where the ledger says
        /// (`HaulView.fromAt`). A Field pickup is the camp centre in the
        /// books -- there is no tree or boulder in the ledger's accounts,
        /// only a metres-from-camp number (`OutpostLedger.SourceMetres`) --
        /// so the body picks its own: the front of the felling order
        /// (`Outpost.ClaimTree`, the same claim table `TickCutting` uses, so
        /// a hauler and a plain gatherer never swing at the same trunk) for
        /// timber, or the nearest unharvested prop of the kind for anything
        /// else, same rule `FindSomethingToWorkAt` uses for a builder.
        Vector3 HaulPickupSpot(HaulView view)
        {
            if (view.from != HaulPlace.Field) return Grounded(view.fromAt);

            if (view.resource == Res.Timber)
            {
                if ((claimedTree < 0 || camp.TreeIsFelled(claimedTree)) && Time.time >= fieldRetryAt)
                {
                    if (!Claim()) fieldRetryAt = Time.time + 1f;
                }
                return claimedTree >= 0 ? Stand(claimAt) : Stand(camp.CampCentre);
            }

            if ((fieldNode == null || fieldNode.Harvested) && Time.time >= fieldRetryAt)
            {
                fieldNode = NearestNode(view.resource);
                if (fieldNode == null) fieldRetryAt = Time.time + 1f;
            }
            return fieldNode != null ? Stand(fieldNode.transform.position) : Stand(camp.CampCentre);
        }

        /// Nearest unharvested prop of this resource to the CAMP CENTRE, no
        /// `Reach` bound -- an armful the ledger is already paying for gets
        /// fetched from wherever it stands, same as a builder's own gather in
        /// `FindSomethingToWorkAt`.
        ResourceNode NearestNode(string resource)
        {
            ResourceNode near = null;
            float best = float.MaxValue;
            Vector3 from = camp.CampCentre;
            foreach (var n in ResourceNode.All)
            {
                if (n == null || n.Harvested || n.Resource != resource) continue;
                Vector3 d = n.transform.position - from;
                d.y = 0f;
                float m = d.sqrMagnitude;
                if (m < best) { best = m; near = n; }
            }
            return near;
        }

        // --- what to mime -----------------------------------------------------

        /// **What this row is after.** What they were told to get -- Timber,
        /// Stone, Game, whatever `target` says.
        ///
        /// **Only ever asked of a Gather row now, 2026-09-23.** A builder's
        /// own answer used to live here too (logs first, then stone,
        /// `OutpostLedger.BuilderWants`), for the direct-carry loop below;
        /// that loop is a ledger haul now (`TickHaul` reads `HaulOf(r)`,
        /// which already knows exactly what is on the books and skips this
        /// entirely), so a Build row never reaches this any more -- but see
        /// `Carries`, called from the hunting and station-work mimes too,
        /// which is why this still takes the general row rather than just a
        /// resource name.
        string WhatFor(OutpostHand r) => r?.target;

        /// **What ends up on his shoulder**, which is not always what he was
        /// sent after. One row splits the two: a hunter is sent after Game
        /// and comes back with Food, because Game is counted in animals on
        /// the crag and the yield lands in the larder (`OutpostLedger`). The
        /// pile he walks to, the sack in his hands and the stack he sets it
        /// on all come off this, so all three agree.
        ///
        /// Everything else carries what `WhatFor` says, unchanged.
        string Carries(OutpostHand r) => Hunting(r) ? Res.Food : WhatFor(r);

        /// **How many units are in his arms**, for `VillagerActing.Set`'s
        /// visible stack (Kevin, 2026-09-23: *"if they carry 3 logs, you see
        /// three logs"*). A real ledger haul (`OutpostHand.Hauling` —
        /// builder site trips fetched from a pile or a station) says the
        /// true armful; a single swing at a tree, a rock or a beast — which
        /// never went through the haul system — is one unit, same as it
        /// always looked.
        static int CarryCount(OutpostHand r) => r != null && r.Hauling ? Mathf.Max(1, r.haulCount) : 1;

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

        /// Where a load from the direct-rate gather loop goes: the stack
        /// that resource belongs on. A builder's carry to a SITE is a ledger
        /// haul now (`TickHaul` walks straight to `HaulOf(r).toAt`, which the
        /// books already aimed at "the oldest site that still wants this" --
        /// `OutpostLedger.SiteWanting` -- when the trip was booked), so this
        /// is only ever asked for a Gather row's own pile.
        Vector3 Dropoff(OutpostHand r, string resource) => PileSpot(resource);

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

        /// The stack itself (what somebody standing at `PileSpot` faces).
        Vector3 PileAt(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return camp.CampCentre;
            float a = Mathf.Abs(resource.GetHashCode() % 360) * Mathf.Deg2Rad;
            return camp.CampCentre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * PileRadius;
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
                // **A hand, explicitly.** Walls block a hand and gates do
                // not; a raider gets the same call with `Walker.Raider`
                // and is stopped by both (D3). `Plan` still means this,
                // and is left standing for anything that has not learnt to
                // say whose feet it is.
                hasRoute = map != null
                    && map.Route(here, to, CampPath.Walker.Hand, route)
                    && route.Count > 0;
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
