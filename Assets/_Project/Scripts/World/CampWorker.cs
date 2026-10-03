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
    /// **The ledger decides; this body walks what it decided.** The outpost
    /// is numbers, and a camp must pay the same whether anybody is watching
    /// it or not -- which is the exact thing the absentee loop was built for.
    /// This component never invents a unit, never fells a tree and never adds
    /// to a pile. What it DOES write, since 2026-09-27
    /// (docs/DELIVERY-ON-ARRIVAL.md), is where the feet are: while the camp
    /// is watched this body IS the ledger's walker, and it reports
    /// `OutpostLedger.BodyAt` (where he stands), `BodyArrived` (at a pickup
    /// or a drop-off) and `BodyWorked` (seconds of work at a pickup). The
    /// ledger moves stock on those reports and nowhere else.
    ///
    /// **Honest bodies (Kevin, 2026-10-03): a villager never carries anything
    /// the ledger didn't hand him.** A load on his shoulder is a load the
    /// books have on him (`OutpostHand.Hauling`, drawn by `TickHaul`); with
    /// no trip he works or rests at his post and his arms are empty -- the
    /// status line (`OutpostLedger.StallReason`) already says why. The old
    /// "mime" loops that walked a made-up armful home between trips (the
    /// farmhand's potato crate, the gatherer's stone, the cutter's log) are
    /// gone; see `TickWorkAt`, `TickErrand`, `TickCutting`.
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
    /// only moves on `QuantumDays` steps (0.02 day = 3.6 s at the playtest day length; 0.1 day = 18 s until 2026-09-24) and
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
    public partial class CampWorker : MonoBehaviour
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
        float stallPollAt;     // next `Ledger.Stalled` ask while swinging (2026-10-03)
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
        /// sentinel instead. `clearIsRock` picks the swing: the axe (`Chop`)
        /// for a tree, the pick (`Mine`, the v15 clip, 2026-10-01) for rock.
        bool clearing;
        Vector3 clearAt;
        /// A rock on a plot is a kit deposit now (2-4 m across,
        /// `StoneDeposit`): stand outside it, not in it.
        const float ClearRockStandOff = 2f;
        bool clearIsRock;
        float clearFor;         // how long he has been swinging at it
        Animal quarry;         // the beast he has claimed, if he is hunting

        Vector3 flyVel;        // thrown: metres a second, integrated here
        Vector3 flyOverLand;   // the last point under him that was island
        float flySpin, flyFor;

        enum Phase { Resting, Going, Working, Coming, Held, Landing, Flying, Downed }

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

        /// **No fixed walking speed any more (2026-10-01).** Each walk moves
        /// at its own clip's ground speed (`VillagerGaits`, via
        /// `VillagerActing.CruiseSpeed`) so the feet stay planted; the
        /// errand walk is 1.29 m/s, a carry 0.53, a run 3.0 (1.5x cadence).
        float Cruise => acting != null ? acting.CruiseSpeed() : VillagerGaits.Cruise(VillagerGaits.BriskClip);
        /// The carrying walk, for a delivery's timing.
        static float CarrySpeed => VillagerGaits.Carry;
        /// The body's turn-then-walk stride (`Stride`).
        readonly Stride stride = new Stride();
        /// Heading bias from `Sidestep` (degrees, + = right), this frame.
        float passSteer;
        int passSteerFrame = -10;
        const float SwingSeconds = 2.4f;
        const float RestSeconds = 1.1f;
        /// Arm's length. He closes to this and no further -- walking to the
        /// animal's own position would put him inside it.
        const float HuntReach = 1.2f;

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
            // **All clear, unwatched fallback (death/rescue phase 12):** a
            // hand walking a spear home loses his body the instant the camp
            // stops being watched -- book it into the store right now
            // rather than leave him holding a phantom spear nobody can see
            // (docs: "if the camp becomes unwatched ... add it to the store
            // immediately").
            var row = w.Row;
            if (row != null && row.returningSpear) SeaSick.Combat.RaidAlarm.SettleReturn(w.camp, row);
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
            OffTower();
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
            OffTower();
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
            Unclaim();   // the beast too: a re-ordered hunter lets it be a goat again
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
            OffTower();
            // Dropped on a building: on the ground beside it (2026-10-01).
            if (camp != null && CampPath.PushOut(camp, at, out Vector3 outside)) at = outside;
            if (camp != null) at.y = WorkerPad.Foot(at, camp.GroundAt(at));
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
            // A tower on the wall: its door is on the camp side of the wall
            // (2026-09-27), whatever side the model's own mark is on.
            // A watchtower with a ladder (2026-09-27): the lookout's ground
            // spot is a stride out from the ladder's foot, where he starts
            // his climb -- kept clear of any wall (`TowerLadderFoot`).
            if (b.Id == OutpostLedger.WatchtowerId && outpost.TowerLadderFoot(b, out Vector3 foot))
                return foot;
            if (outpost.IsWallTower(b)) return outpost.WallTowerDoor(b);
            var marks = MarksOf(b);
            if (marks.stand != null)
            {
                // **On the pad, not through it (2026-09-28):** the level 1
                // lumber mill's stand is 0.16 m up on a timber floor; the
                // terrain alone would sink him into the boards. `WorkerPad`
                // is the terrain on every building without one.
                Vector3 at = marks.stand.position;
                at.y = WorkerPad.Foot(at, outpost.GroundAt(at));
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

        /// **Every live villager body**, for the things that want to know who
        /// is near without a scene search: `GateLeaves` swings for a hand
        /// within a few metres. Kept by OnEnable/OnDisable, so a component
        /// `Remove` has handed to `Destroy` drops out the same frame.
        public static readonly List<CampWorker> Bodies = new List<CampWorker>();

        void OnEnable() { if (!Bodies.Contains(this)) Bodies.Add(this); }
        void OnDisable()
        {
            Bodies.Remove(this);
            Drop();
            ReleaseClaim();
            // The invisible walker takes over from where this body stood
            // (2026-09-27, docs/DELIVERY-ON-ARRIVAL.md).
            if (row != null && camp != null && camp.Ledger != null) camp.Ledger.BodyReleased(row);
        }

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

            // **Downed (death/rescue phase 1, 2026-09-27).** Lie flat where
            // he stands, snapped to the terrain, and stop -- no walking, no
            // work, until `Revive` or `Die` says otherwise. Restorable: this
            // is pose only, driven every frame off `r.downed`, so a load
            // that happens mid-down just reapplies it once the body ticks.
            if (r.downed)
            {
                if (phase != Phase.Downed)
                {
                    // **Empty-handed on the ground (2026-10-03, honest
                    // bodies).** `OutpostLedger.Down` already dropped his
                    // load where he fell (a `GroundLoad` somebody else
                    // collects) or cancelled a planned one, so the carry
                    // pose he went down in is a load nobody has. Clear it,
                    // forget the trip he was drawing (so getting back up
                    // never "sets it down" at the old drop-off, see
                    // `EndTripMime`) and hand back any tree/bed/site claim.
                    // `StepOffJob` (CampWorker.Alarm.cs) also puts down a
                    // hunter's carcass -- the same clean-up the alarm, a
                    // fight and a rescue use.
                    StepOffJob();
                    phase = Phase.Downed;
                    Vector3 p = transform.position;
                    p.y = WorkerPad.Foot(p, camp.GroundAt(p));
                    transform.position = p;
                    transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 90f);
                }
                return;
            }
            if (phase == Phase.Downed)
            {
                // Revived: stand back up where he was laid down.
                phase = Phase.Resting;
                transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                wait = 0f;
            }

            // **Never under the ground (2026-09-30).** Something outside the
            // camp that moves a body (a deck snap, `CrewAgent.Start`) can
            // leave it buried, and a body that then never steps -- pinned by
            // the wall guard, working at a pickup, holding at a full store --
            // is never re-grounded by `Walk`. `BodyAt` already refuses to
            // book a spot that deep (`Outpost.WalkerSpotOk`), so the books
            // still have him where he really was: put the body back there.
            HealBuried(r);
            HealInsideSolid();

            // **This body is the walker** (2026-09-27): where he stands is
            // where the books have him, and the ledger leaves his legs to him.
            camp.Ledger?.BodyAt(r, transform.position);

            // **How he walks (v15 gaits, 2026-10-01):** running to hide is
            // panic (RunScared); the spear fetch, a fight or a rescue run
            // (Run); a low-mood hand drags his feet (WalkTired, mood < 0.5,
            // the same line `OutpostLedger.WorkFactor` slows him at); an
            // errand is brisk, an idle wander a stroll.
            if (acting != null)
                acting.WalkGait = r.hidingHut || r.hidingCrouch ? VillagerActing.Gait.Scared
                    : r.fetchingSpear || r.defending || !string.IsNullOrEmpty(r.rescuing) ? VillagerActing.Gait.Run
                    : r.mood < 0.5f ? VillagerActing.Gait.Tired
                    : phase == Phase.Resting ? VillagerActing.Gait.Stroll
                    : VillagerActing.Gait.Errand;

            // A store runner pushes his wheelbarrow (visual only, `RunnerBarrow`).
            // **Parked where it cannot go with him** (2026-10-03, Kevin: "they
            // always use their wheelbarrow"): up a ladder chain, or lying
            // down by the fire, it is set down where he left it.
            RunnerBarrow.Sync(this, r, acting, climb.Active || lyingByFire);

            // **The rescuer (death/rescue phase 2), ahead of everything
            // else** -- a hand sent to drag somebody home is not doing his
            // ordinary job right now, the same priority `TickTower` has
            // over the rest of the loop.
            if (TickRescue(r, dt)) return;

            // **Pouting (death/rescue phase 4)**, next in priority: a hand
            // sulking at the fire is not doing his ordinary job either,
            // same reasoning as the rescue check just above.
            if (TickPout(r, dt)) return;

            // **The alarm (death/rescue phase 10, 2026-09-28)**, same
            // priority: fetching a spear or running to hide is not the
            // ordinary job either, and comes before he could be armed enough
            // for `TickDefend` to want him.
            if (TickAlarmRole(r, dt)) return;

            // **Village defence (death/rescue phase 9, 2026-09-28)**, same
            // priority as the rescue/pout checks just above: an armed hand
            // fighting off a live raid is not doing his ordinary job either.
            if (TickDefend(r, dt)) return;

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
            // **Up the watchtower (2026-09-27).** A lookout on his platform,
            // or on its ladder either way, belongs to `TickTower` until he is
            // back on the ground -- relieved or re-posted, he climbs down
            // before anything else gets his legs.
            if (TickTower(r, dt)) return;

            WatchBench(r);
            // A load the books have put down (or are about to) being walked
            // the last few steps and set down. Runs ahead of everything,
            // including the next trip: it lasts a second or two and the
            // next trip's own pickup slack absorbs it. See `TickDelivery`.
            if (TickDelivery(dt)) return;
            // A hunt trip is mimed by `TickHunting` (stalk, strike, carry),
            // not as an armful.
            if (camp.Ledger != null && r.Hauling && (!r.HuntTrip || !Hunting(r)))
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

            // **Villagers with a day (2026-09-28).** The evening/sleep
            // routine, right where the ordinary dispatch below would
            // otherwise take over -- a haul, a tower shift or a raid role
            // above this point in `Update` already ran and returned this
            // frame if it applied, so by here the leg he was on (if any)
            // is finished and this is the first free moment to send him to
            // the fire. `TickRoutine` itself skips a hand the player has
            // just ordered through the night (`OutpostHand.orderOverride`).
            if (TickRoutine(r, dt)) return;

            switch (r.order)
            {
                case OutpostOrder.Idle: TickIdle(dt); return;
                case OutpostOrder.Work: TickWork(r, dt); return;
                default: TickErrand(r, dt); return;
            }
        }

        /// Deeper than this under the terrain is buried, not a foot on a slope.
        const float BuriedMetres = 1f;

        /// **A buried body goes back to where the books have him
        /// (2026-09-30)** -- his booked spot (`OutpostLedger.HandAt`, which
        /// `BodyAt` never overwrote with the buried one), on the ground;
        /// straight up where he stands if that spot is buried too. One
        /// terrain sample a frame. See the call in `Update`.
        void HealBuried(OutpostHand r)
        {
            if (!camp.HasGround) return;
            Vector3 p = transform.position;
            if (p.y >= camp.GroundAt(p) - BuriedMetres) return;
            Vector3 to = p;
            var ledger = camp.Ledger;
            if (ledger != null && r.wHas)
            {
                Vector3 booked = ledger.HandAt(r);
                if (camp.WalkerSpotOk(booked, false)) to = booked;
            }
            to.y = WorkerPad.Foot(to, camp.GroundAt(to));
            transform.position = to;
            ClearRoute();
        }

        /// The buildings' revision this body last checked itself against.
        int solidRevSeen = -1;

        /// **Never inside a building (2026-10-01).** When the camp's
        /// buildings change (raised, moved, turned -- or this body is new,
        /// e.g. a save loading), a body standing inside one of their boxes
        /// steps out to the nearest face (`CampPath.PushOut`). The building
        /// never moves. One int compare a frame otherwise. A lookout on his
        /// tower is above the box, not in it.
        void HealInsideSolid()
        {
            if (OnTower || climb.Active || bodyHidden) return;
            var map = CampPath.For(camp);
            if (map == null) return;
            int rev = map.SolidRevisionNow();
            if (rev == solidRevSeen) return;
            solidRevSeen = rev;
            if (!CampPath.PushOut(camp, transform.position, out Vector3 to)) return;
            to.y = WorkerPad.Foot(to, camp.GroundAt(to));
            transform.position = to;
            ClearRoute();
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
                    // **The plot the BOOKS sent him to** (2026-09-27 ladder),
                    // not the oldest site: the books only clear/hammer while
                    // his body stands at the plot they chose.
                    var focus = camp != null && camp.Ledger != null ? camp.Ledger.BuildSiteFor(r) : null;

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
                        target = Stand(clearAt, clearIsRock ? ClearRockStandOff : 1.1f);
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

                    // **A gatherer between trips (2026-10-03, honest
                    // bodies).** Every armful a gatherer brings home is a
                    // ledger trip (`OutpostLedger.GatherDay` ->
                    // `StartGatherTrip`), drawn by `TickHaul`; reaching here
                    // means there is none on him THIS step. Stalled -- store
                    // full, nothing left standing -- he rests at home
                    // (`StallReason` says why on his sheet). Otherwise he
                    // may go and swing at his rock until the next trip
                    // takes him, but never with anything to carry back. No
                    // rock to swing at: rest here too (this used to be a
                    // potter to a random spot by the fire, a pick swung at
                    // the grass and a stone walked home that never existed).
                    // (Home and stalled is "arrived": the sheet then says
                    // why he rests, not "still on the way up from the ship"
                    // -- `StallReason` reads `walkingIn` first.)
                    if (camp.Ledger != null && camp.Ledger.Stalled(r))
                    {
                        if (Near(home, 1f)) r.walkingIn = false;
                        wait = RestSeconds;
                        return;
                    }
                    if (!FindSomethingToWorkAt(r, out target))
                    {
                        if (Near(home, 1f)) r.walkingIn = false;
                        wait = RestSeconds;
                        return;
                    }
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
                    acting?.Set(clearing ? (clearIsRock ? VillagerActing.Mode.Mine : VillagerActing.Mode.Chop)
                        : raising ? VillagerActing.Mode.Build : ModeFor(WhatFor(r)));
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
                        // **He stays on the site while there is hammering
                        // to do** (2026-09-27): the books hammer only while
                        // a builder is ON it (`OutpostLedger.WalkTo`), so a
                        // rest walk home between swings would pause it.
                        var f = camp.Ledger != null ? camp.Ledger.BuildSiteFor(r) : null;
                        if (r.order == OutpostOrder.Build && f != null && f.Cleared && f.Stocked && !f.Complete)
                        {
                            wait = SwingSeconds * Random.Range(0.85f, 1.35f);
                            return;
                        }
                        Drop();
                        phase = Phase.Resting;
                        wait = RestSeconds * 0.5f;
                        return;
                    }
                    // **No armful at the end of a swing (2026-10-03, honest
                    // bodies).** This used to shoulder the row's target and walk
                    // it to the store with no ledger call -- a stone the
                    // books never had. The real armful is the ledger's next
                    // gather trip, and `Update` hands him to `TickHaul` the
                    // frame it starts (pickup at this same kind of rock,
                    // `HaulPickupSpot`). Until then: stalled -> home to rest;
                    // the rock worked out from under him -> rest and pick
                    // another; else another swing where he stands.
                    if ((camp.Ledger != null && camp.Ledger.Stalled(r))
                        || (workNode != null && workNode.Harvested))
                    {
                        Drop();
                        phase = Phase.Resting;
                        wait = RestSeconds;
                        return;
                    }
                    wait = SwingSeconds * Random.Range(0.85f, 1.35f);
                    return;

                case Phase.Coming:
                    // Nothing reaches this any more (2026-10-03): the swing
                    // above never shoulders a load. Kept so a phase left
                    // over from anywhere else can only ever put down what
                    // is in his hands and rest -- never walk it anywhere.
                    Drop();
                    phase = Phase.Resting;
                    wait = RestSeconds;
                    return;
            }
        }

        // `TickHunting` lives in CampWorker.Hunting.cs (2026-09-26: stalk to
        // the books' clock, strike, the beast dies, shoulder the carcass home).

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
                // A goat on a crag the hunter cannot climb is not quarry
                // (2026-09-27); the next nearest is.
                if (m < bestSq && CampPath.Reachable(camp, a.transform.position)) { bestSq = m; best = a; }
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
                    // **Stalled: rest, don't swing (2026-10-03, honest
                    // bodies).** Store full of timber, or nothing left
                    // standing: the ledger starts no trip, so no tree will
                    // come down for him and no log is coming home. He rests
                    // at home with his hands empty and gives the trunk back
                    // (`StallReason` names why on his sheet).
                    if (camp.Ledger != null && camp.Ledger.Stalled(r))
                    {
                        if (claimedTree >= 0) ReleaseClaim();
                        wait = RestSeconds;
                        if (there) { r.walkingIn = false; FaceRest(dt, 0f); }
                        return;
                    }
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
                        // **It went over -- and no log goes on his shoulder
                        // (2026-10-03, honest bodies).** This used to
                        // shoulder a log and walk it home with no ledger
                        // call. A log only comes home on a ledger timber
                        // trip (`TickHaul`; the tree falls at that trip's
                        // pickup, `Outpost.SyncFelling`). Between trips he
                        // takes the next tree in the felling order and
                        // swings at that one.
                        Reclaim();
                        if (phase == Phase.Working) phase = Phase.Going;
                        return;
                    }
                    chopFor += dt;
                    // Stalled while swinging (the store filled, the wood ran
                    // out): no trip is coming, so stop and rest -- polled
                    // once a second, `Stalled` is not free.
                    if (Time.time >= stallPollAt)
                    {
                        stallPollAt = Time.time + 1f;
                        if (camp.Ledger != null && camp.Ledger.Stalled(r))
                        {
                            ReleaseClaim();
                            Drop();
                            phase = Phase.Resting;
                            wait = RestSeconds;
                            return;
                        }
                    }
                    if (chopFor < Feel.chopPatience) return;
                    Drop();
                    phase = Phase.Resting;
                    wait = RestSeconds;
                    return;

                case Phase.Coming:
                    // Nothing reaches this any more (2026-10-03): a felled
                    // tree is never shouldered above. Kept so a leftover
                    // phase only ever empties his hands -- never walks a
                    // log the ledger does not have.
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
            var focus = camp != null && camp.Ledger != null ? camp.Ledger.BuildSiteFor(Row) : null;
            if (focus != null && camp.ClaimClearing(focus, this, out clearAt, out clearIsRock))
            {
                target = Stand(clearAt, clearIsRock ? ClearRockStandOff : 1.1f);
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
            // The level 2 sawmill's crank (2026-10-01): he stands square to
            // the building's front, as at every bench, and the `Crank` clip
            // turns him the 90 degrees along the saw table itself (README:
            // the marker has no facing).
            if (Cranks(post)) face = stand + Flat(post.transform.forward);

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
            // **Onto the crank's own spot (2026-10-01).** A walk "arrives"
            // 0.35 m short; the bench mimes do not mind, but the `Crank`
            // clip's fists land on the handle only from the marker itself.
            // He shuffles the last of it at a slow step.
            if (Cranks(post))
            {
                Vector3 p = transform.position;
                Vector3 onto = Vector3.MoveTowards(new Vector3(p.x, stand.y, p.z), stand, 0.6f * dt);
                if ((onto - p).sqrMagnitude > 1e-8f) transform.position = onto;
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
            acting?.Set(JobMode(r.target, post));
            // The tool's stroke lands on the bench's own work spot (the
            // forge's anvil, the sawhorse), not a guess in front of him.
            acting?.WorkAt(face);

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
            float walk = FlatDistance(transform.position, rackAt) / CarrySpeed;
            float due = SecondsToJobDone(r, st);
            if (due > walk + LeaveLead) return;
            jobCarried = true;
            StartDelivery(rackAt, rackFace, rec.makes, yield, due);
        }

        /// **The old shift loop**, for a building that employs somebody but
        /// keeps no `StationStock` (the farm, whose field is its input and
        /// whose yield goes into the farmhand's basket; the watchtower).
        /// Walk to the door, work the shift, straighten up, work again.
        /// **No carry in this loop since 2026-10-03** (honest bodies): the
        /// basket goes to the store only as a ledger trip (`CarryBasket`),
        /// drawn by `TickHaul` like every other load.
        void TickWorkAt(OutpostHand r, float dt)
        {
            Building post = WorkPostOf(r);
            Vector3 door = post != null ? WorkSpot(camp, post) : home;
            Vector3 face = post != null ? post.transform.position : lookAt;

            // A lookout's shift is the platform: walk to the ladder, climb.
            if (post != null && post.Id == OutpostLedger.WatchtowerId && TowerFootWalk(post, door, dt)) return;

            // **A runner with nothing to carry (2026-10-02)** waits at the
            // store hut's door, hands empty -- the ledger's next barrow
            // trip (`HaulOf`) takes him from here.
            if (OutpostLedger.IsRunner(r))
            {
                acting?.Set(VillagerActing.Mode.None);
                Vector3 stand = post != null ? RunnerWaitSpot(r, post, door) : door;
                if (!Walk(stand, dt)) { phase = Phase.Going; return; }
                // At his post (the phase the walk-in check reads).
                phase = Phase.Working;
                // **Facing out into the yard (2026-10-03)**: his barrow is
                // parked a step in front of him and reaches ~1.3 m; facing
                // the hut it stood inside the wall. Out, it is ready to go.
                Face(post != null ? transform.position - face : face - transform.position, dt);
                return;
            }

            if (camp.Ledger != null && camp.Ledger.Stalled(r))
            {
                Drop();
                phase = Phase.Resting;
                // At his post and stalled is arrived (2026-10-03): the sheet
                // says "waiting for crops", not "on the way up from the ship".
                if (Walk(door, dt)) { r.walkingIn = false; Face(face - transform.position, dt); }
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
                    // No building stood yet (assigned to a post before it is
                    // built): `door` fell back to `home`, often the fire
                    // ring, and there is no bench, forge or pot to work --
                    // Kevin, 2026-09-27, saw a "cook" stirring a stick at the
                    // campfire this way. Idle hands, not a tool mimed on
                    // nothing.
                    acting?.Set(post != null ? JobMode(r.target, post) : VillagerActing.Mode.None);
                    return;

                case Phase.Working:
                    Face(face - transform.position, dt);
                    wait -= dt;
                    if (wait > 0f) return;
                    // **The end of a shift is a breather, never a crate
                    // (2026-10-03, honest bodies).** Kevin's farm: every 6-10
                    // s the farmhand shouldered a crate of potatoes
                    // (`BuildPlans.Named(target).makes`) and walked it to the
                    // store with no ledger call -- a load the books never
                    // had. Every unit a non-station post makes reaches the
                    // store as a ledger basket trip (`OutpostLedger.CarryBasket`
                    // -> a picked-up Field->Store haul), which `Update` hands
                    // to `TickHaul` the frame it starts, so that is the ONLY
                    // carry he does. Here he straightens up at the plot
                    // (Resting at the door, `RestSeconds`) and goes back to
                    // it; a post with no output (the watchtower) is the same.
                    phase = Phase.Resting;
                    wait = RestSeconds;
                    return;

                case Phase.Coming:
                    // Nothing reaches this any more (2026-10-03): the shift
                    // above never shoulders a load. A leftover phase only
                    // ever empties his hands.
                    Drop();
                    phase = Phase.Resting;
                    wait = RestSeconds;
                    return;
            }
        }

        // --- hauling (2026-09-23) ----------------------------------------------

        /// **Walk the trip -- the body IS the walker (2026-09-27).**
        ///
        /// Kevin: *"A villager walking with that resource has that resource
        /// on him and it gets where it gets when it gets there ... Only when
        /// a villager has delivered the object will the object be counted."*
        /// The ledger no longer times a trip; while the camp is watched this
        /// body walks it and tells the books the two events
        /// (docs/DELIVERY-ON-ARRIVAL.md):
        ///
        ///   - `ToPickup`: walk his own route to the pickup (his own tree or
        ///     prop on the island; the stack, rack or bay otherwise), then
        ///     `BodyArrived`;
        ///   - `AtPickup`: the stationary work (a chop per unit at Kevin's
        ///     dials, a stoop at a pile) paid by `BodyWorked` in game
        ///     seconds; its end is the PICKUP (the source gives it up now);
        ///   - `ToDrop`: carry it (the load is on him), then `BodyArrived` --
        ///     the DROP-OFF: the bay/rack/pile/site/hold fills this frame, and
        ///     he sets it down;
        ///   - `AtDrop`: a full store; he stands holding it until room comes.
        ///
        /// Where he stands goes into the books every frame (`BodyAt`, in
        /// `Update`), so when nobody is watching any more the invisible
        /// walker carries on from exactly here.
        void TickHaul(OutpostHand r, float dt)
        {
            var ledger = camp.Ledger;
            var view = ledger.HaulOf(r);
            if (!view.active) return;

            if (r.haulSerial != mimedTrip)
            {
                bool settingDown = EndTripMime();
                ReleaseClaim();
                mimedTrip = r.haulSerial;
                BeginTripMime(r, view);
                haulTickFrame = Time.frameCount;
                if (settingDown) return;     // `TickDelivery` owns the next moment
            }
            // Drawn this frame -- AFTER the check above, which needs last
            // frame's: `EndTripMime` calls a set-down honest only straight
            // after a frame that had him walking this trip (2026-10-03).
            haulTickFrame = Time.frameCount;

            switch (view.leg)
            {
                case TripLeg.ToPickup:
                {
                    phase = Phase.Going;
                    acting?.Set(VillagerActing.Mode.None);
                    Vector3 pick = view.from == HaulPlace.Field ? HaulPickupSpot(view) : mimePick;
                    if (!Walk(pick, dt)) return;
                    r.walkingIn = false;
                    ledger.BodyArrived(r);
                    return;
                }
                case TripLeg.AtPickup:
                {
                    // **His tree went over under somebody else's armful
                    // (2026-10-03).** A timber armful is 2 logs and a tree
                    // 1, so a pickup fells his tree AND the next in the
                    // order (`Outpost.TimberCutHere`) -- which may be the one
                    // this cutter is swinging at. Don't chop the stump: walk
                    // to the next standing tree (`HaulPickupSpot` re-claims)
                    // and only then work; the pickup's work timer waits
                    // while he walks.
                    if (view.from == HaulPlace.Field && view.resource == Res.Timber
                        && claimedTree >= 0 && camp.TreeIsFelled(claimedTree))
                    {
                        phase = Phase.Going;
                        acting?.Set(VillagerActing.Mode.None);
                        if (!Walk(HaulPickupSpot(view), dt)) return;
                    }
                    phase = Phase.Working;
                    r.walkingIn = false;
                    Face(PickFace(view) - transform.position, dt);
                    // A store/station pickup is the v15 `PickUp` one-shot,
                    // with the load shown on the ground and lifted. A MEAL
                    // is not a haul (2026-10-01): a reach to the counter at
                    // waist height, the dish into his hand.
                    if (mimeEating) acting?.Set(VillagerActing.Mode.Reach, view.resource, 1);
                    else acting?.Set(PickMode(view), view.resource, Mathf.Max(1, view.count));
                    ledger.BodyWorked(r, dt * ClockRate());
                    return;
                }
                case TripLeg.ToDrop:
                {
                    phase = Phase.Coming;
                    mimeLoaded = true;
                    if (mimeEating) acting?.Set(VillagerActing.Mode.Reach, view.resource, 1);
                    else acting?.Set(VillagerActing.Mode.Carry, view.resource, Mathf.Max(1, view.count));
                    if (!Walk(mimeDrop, dt)) return;
                    mimeArrived = true;
                    Face(mimeFace - transform.position, dt);
                    ledger.BodyArrived(r);           // the drop-off event
                    if (!r.Hauling || r.haulSerial != mimedTrip)
                    {
                        // Delivered: set it down, here, now -- or, a meal,
                        // eat it standing where he took it.
                        mimeLoaded = mimeArrived = false;
                        if (mimeEating) StartEat(mimeFace, view.resource);
                        else StartPlace(mimeFace, view.resource, Mathf.Max(1, view.count));
                    }
                    return;
                }
                default:
                    // At a full store: he holds it until room comes. The
                    // books have him AT the drop-off with it (a body that
                    // became the walker mid-wait included), so the step
                    // that finds room is a real deposit here (`EndTripMime`).
                    mimeLoaded = mimeArrived = true;
                    Face(mimeFace - transform.position, dt);
                    if (mimeEating) acting?.Set(VillagerActing.Mode.Reach, view.resource, 1);
                    else acting?.Set(VillagerActing.Mode.Carry, view.resource, Mathf.Max(1, view.count));
                    return;
            }
        }

        // --- pacing the mime to the books (2026-09-24) ---------------------

        /// Seconds early a body leaves the bench: a job's end is SEEN a
        /// little after its ledger step (`Outpost.CatchUp` runs four times a
        /// second).
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
        int haulTickFrame = -10;  // last frame `TickHaul` drew an active trip (2026-10-03)
        bool mimeLoaded;          // shouldered: the carry leg
        bool mimeArrived;         // at the drop-off with it
        bool mimePlaced;          // set down ahead of the books (the hold ran out)
        bool mimeJoinedLate;      // first seen past its walk out (caught up mid-trip)
        float mimeHold;           // seconds held at the drop-off so far
        Vector3 mimePick, mimePickFace;   // a store/station pickup (Field picks its own)
        Vector3 mimeDrop, mimeFace;       // where the load goes, and what to face there
        string mimeRes;
        int mimeCount;
        /// The trip is a hungry hand taking his meal (`OutpostHand.eating`,
        /// read at its start: the books clear it the moment he has eaten).
        bool mimeEating;


        /// Resolve this trip's two ends to where a body actually stands:
        /// Astra's `Input_Pickup`/`Output_Dropoff` at a station (never the
        /// middle of the model, which is where the books' `fromAt`/`toAt`
        /// point), the resource's own stack by the fire (or the storage
        /// building's door) for the store. Once per trip -- nothing here moves
        /// during one, unless the player moves the building itself
        /// (`AimTripMime`, 2026-09-30). A Field pickup is the body's own tree/prop and is
        /// re-picked live in `HaulPickupSpot`.
        void BeginTripMime(OutpostHand r, HaulView view)
        {
            mimeLoaded = mimeArrived = mimePlaced = false;
            mimeHold = 0f;
            mimeJoinedLate = view.picked;
            mimeRes = view.resource;
            mimeCount = Mathf.Max(1, view.count);
            mimeEating = r.eating;
            AimTripMime(r, view);
        }

        /// The two ends of the trip being drawn, as a body stands at them.
        /// `BeginTripMime` once per trip; `OnBuildingMoved` again when the
        /// building at one end was moved under it (2026-09-30).
        void AimTripMime(OutpostHand r, HaulView view)
        {
            // **Onto the rack, not into the bay** (2026-09-30): a catch goes
            // in the fishing hut's output box, and a meal off a rack is
            // eaten where it was picked up.
            bool toRack = view.from == HaulPlace.Shore
                || (r.eating && view.from == HaulPlace.Station && view.to == HaulPlace.Station);
            mimeDrop = TripEnd(view.to, view.toStation, view.toAt, view.resource, !toRack, out mimeFace);
            if (view.from != HaulPlace.Field)
                mimePick = TripEnd(view.from, view.fromStation, view.fromAt, view.resource,
                    view.from == HaulPlace.Station && r.haulFromBay, out mimePickFace);
        }

        /// The last trip is over in the books (the load went down on the
        /// step just seen). If this body had it shouldered: at the drop-off,
        /// the set-down stoop now; a few steps short, walk them then set it
        /// down. True when a delivery was started (the caller must not
        /// `Drop()`, which would cancel it).
        ///
        /// **Only where the ledger actually put it (2026-10-03, honest
        /// bodies).** While the camp is watched this body is the walker, so
        /// the books deposit a load only on his `BodyArrived` at the
        /// drop-off (`TickHaul` plays that set-down itself) or, at a full
        /// store, on a later step while he stands there holding it
        /// (`AtDrop`). Any other end -- a raid or a rescue or a fight made
        /// the ledger drop it on the ground where he stood
        /// (`DropCarriedLoad`), a walled-off trip given up -- is NOT a
        /// deposit, and this used to walk the load to the old drop-off and
        /// "set it down" anyway whenever he was within `TailMetres` of it.
        /// So: a set-down only if he had reached the drop-off (`mimeArrived`)
        /// AND the trip was being drawn up to last frame (`haulTickFrame`:
        /// nothing -- an alarm, a pout, a rescue -- took him away from it
        /// first). Anything else just empties his hands (the caller `Drop`s).
        bool EndTripMime()
        {
            bool holding = mimeLoaded && !mimePlaced;
            bool deposited = mimeArrived && Time.frameCount - haulTickFrame <= 1;
            mimeLoaded = mimeArrived = mimePlaced = false;
            mimeHold = 0f;
            if (!holding || !deposited) return false;
            // A meal in his hand: he eats it where he stands.
            if (mimeEating) { StartEat(mimeFace, mimeRes); return true; }
            float left = FlatDistance(transform.position, mimeDrop);
            if (left <= 0.6f) { StartPlace(mimeFace, mimeRes, mimeCount); return true; }
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
                    // The books' `at` is NOT used to find the building: a
                    // trip booked before the hut stood carries the fire as
                    // its store point, and matching the hut against that
                    // found nothing and sent the load to the fire ring.
                    return StoreSpot(res, out face);
                case HaulPlace.Shore:
                    // **The fisher's spot at the water's edge** (2026-09-30):
                    // the books' own point (`Outpost.SaveShoreSpots` found it
                    // on this very ground), facing out over the water.
                    if (camp.Ledger != null && camp.Ledger.ShoreOf(station, out _, out var sea)) face = sea;
                    return Grounded(at);
                case HaulPlace.Ship:
                    // **The foot of the gangway** (2026-09-24 transfers): the
                    // books' own point (`ICargoSide.GangwayAt` -- the plank's
                    // landing, the pier ROOT at a pier: `CampPath` has no
                    // pier deck to walk out on). He faces the ship to pick up
                    // / set down.
                    if (ShipCargoSide.ShipAt(out var hull)) face = hull;
                    return Grounded(at);
            }
            // A site (the drawing itself), or a station whose building the
            // body cannot find (a probe's hand-written ledger): the books'
            // own point.
            return Grounded(at);
        }

        /// **The store's end of ANY carry** -- a ledger haul, a gatherer's
        /// armful, a hunter's meat, a farmhand's yield. Before a
        /// Storage/Storehouse stands the store IS the stacks by the fire
        /// (`CampPiles`), so the load goes to its own stack (`PileSpot`), not
        /// into the fire. After, the storage building's `Input_Pickup` if
        /// Astra gave it one, else the side of it that faces the fire.
        ///
        /// Kevin, phone playtest 2026-09-24: *"there are still villagers
        /// dropping food, rocks and other things by the campfire despite
        /// there being a storage hut."* Only ledger hauls came through here;
        /// the gather/cut/hunt loops (`Dropoff`) and the farm/watchtower
        /// shift (`TickWorkAt`) went straight to `PileSpot`. Every store
        /// drop-off and pickup is this one function now.
        Vector3 StoreSpot(string res, out Vector3 face)
        {
            var ledger = camp.Ledger;
            if (ledger != null && ledger.HasStorageBuilding)
            {
                var b = CampPiles.StoreBuildingOf(camp);
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
            float rate = rec.ratePerDay * Economy.Techs.RateMul(st.planId, ledger.LevelOf(st.planId, st.ordinal))
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
            double quantum = OutpostLedger.QuantumDays * (double)TimeOfDay.WorkDaySeconds;
            double landsAt = ledger.lastTicked + steps * quantum;
            float gameLeft = (float)(landsAt - TimeOfDay.Seconds);
            return Mathf.Max(0f, gameLeft / rate) + CatchUpLag;
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

        /// The pose at a pickup: the swing that suits the material at the
        /// island's tree or rock, a stoop over a store's stack or a
        /// station's bay or rack.
        static VillagerActing.Mode PickMode(HaulView view) =>
            view.from == HaulPlace.Field ? ModeFor(view.resource) : VillagerActing.Mode.PickUp;

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
            // Paths bend and he turns at corners: twice the straight line.
            deliverMax = 2f * FlatDistance(transform.position, to) / CarrySpeed + 4f;
            placeLeft = 0f;
            acting?.Set(VillagerActing.Mode.Carry, res, deliverCount);
        }

        /// The set-down itself: a short stoop with empty arms (the carried
        /// stack is gone once the pose changes).
        void StartPlace(Vector3 face, string res = null, int count = 1)
        {
            delivering = false;
            placeLeft = PlaceSeconds;
            placeFace = face;
            placeRes = res;
            placeCount = Mathf.Max(1, count);
            // The v15 `SetDown` one-shot: he lets go on frame 3 and the load
            // lands 0.68 m ahead on frame 16 (0.53 s), inside the stoop.
            acting?.Set(VillagerActing.Mode.SetDown, placeRes, placeCount);
        }

        string placeRes;
        int placeCount = 1;

        /// **Eating the meal he just took** (2026-10-01): stands where he
        /// took it, the dish at his chest, `VillagerActing.EatSeconds` of
        /// bites; the books already counted it eaten (`EatMeal`), this is
        /// only the picture. The next trip waits for him the way it waits
        /// for a set-down (the walker is paced by the body).
        void StartEat(Vector3 face, string meal)
        {
            delivering = false;
            placeLeft = 0f;
            eatLeft = VillagerActing.EatSeconds;
            eatFace = face;
            eatRes = meal;
            acting?.Set(VillagerActing.Mode.Eat, eatRes, 1);
        }

        float eatLeft;
        Vector3 eatFace;
        string eatRes;

        void CancelDelivery()
        {
            delivering = false;
            placeLeft = 0f;
            deliverHold = 0f;
            eatLeft = 0f;
        }

        /// One frame of a delivery or a set-down. True while either owns the
        /// body (the caller does nothing else this frame).
        bool TickDelivery(float dt)
        {
            if (eatLeft > 0f)
            {
                eatLeft -= dt;
                Face(eatFace - transform.position, dt);
                if (eatLeft > 0f) { acting?.Set(VillagerActing.Mode.Eat, eatRes, 1); return true; }
                acting?.Set(VillagerActing.Mode.None);
                return true;
            }
            if (placeLeft > 0f)
            {
                placeLeft -= dt;
                Face(placeFace - transform.position, dt);
                if (placeLeft > 0f) { acting?.Set(VillagerActing.Mode.SetDown, placeRes, placeCount); return true; }
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
            StartPlace(deliverFace, deliverRes, deliverCount);
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
            // The fisher has no bench to watch (2026-09-30): every catch is a
            // walked trip into the box (`TickHaul`), never a bench carry.
            if (st == null || st.removed || OutpostLedger.FishesAtShore(st))
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

        /// Drop `b`'s cached marks: its model was just swapped
        /// (`BuildingFactory.ShowLevel`), so the next ask reads the new one.
        public static void ForgetMarks(Building b) { if (b != null) marksOf.Remove(b); }

        static Marks MarksOf(Building b)
        {
            if (marksOf.TryGetValue(b, out var m) && !m.Stale) return m;
            // Demolished buildings leave dead keys behind; a camp has a few
            // dozen buildings, so an occasional clear is cheaper than
            // tracking them.
            if (marksOf.Count > 64) marksOf.Clear();
            m = new Marks();
            Transform benchAlt = null, cookLine = null;
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
                    // The fishing hut's cutting board (2026-09-27).
                    case "Catch_Anchor":
                        if (benchAlt == null) benchAlt = t;
                        break;
                    // The level 1 kitchen (V6, 2026-10-01): the cook faces
                    // his cauldron and grill, straight ahead between the two
                    // fires, not the prep board at his right hand.
                    case "Cook_Line_Anchor": cookLine = t; break;
                }
            }
            if (m.bench == null) m.bench = cookLine != null ? cookLine : benchAlt;
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
            return fieldNode != null ? Stand(fieldNode.transform.position, fieldNode.StandOff) : Stand(camp.CampCentre);
        }

        /// Nearest unharvested prop of this resource to the CAMP CENTRE, no
        /// `Reach` bound -- an armful the ledger is already paying for gets
        /// fetched from wherever it stands, same as a builder's own gather in
        /// `FindSomethingToWorkAt`.
        ///
        /// **This island's props only** (2026-09-24): `ResourceNode.All` is
        /// every island's, and with no rock at home the nearest boulder was
        /// across the water -- the body set off for it and mimed at the
        /// shore. `Harvested` covers a worked-out deposit (its remnant), so
        /// the target is always a rock with units left.
        ResourceNode NearestNode(string resource)
        {
            ResourceNode near = null;
            float best = float.MaxValue;
            Vector3 from = camp.CampCentre;
            var isle = camp.Island;
            foreach (var n in ResourceNode.All)
            {
                if (n == null || n.Harvested || n.Resource != resource) continue;
                if (isle != null && n.Home != isle) continue;
                Vector3 d = n.transform.position - from;
                d.y = 0f;
                float m = d.sqrMagnitude;
                if (m < best && CampPath.Reachable(camp, n.transform.position)) { best = m; near = n; }
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
        /// entirely), so a Build row never reaches this any more. (`Carries`
        /// and `CarryCount`, which sized the made-up armful of the old
        /// gather/farm/cut loops, went with them on 2026-10-03: every load
        /// shown now is a ledger trip's, `HaulView.count`.)
        string WhatFor(OutpostHand r) => r?.target;

        /// The swing that suits the material. An axe for wood, the pick
        /// (`Mine`, 2026-10-01: the v15 clip, was a hammer) for the things
        /// that come out of rock, a hoe for what is picked off the ground.
        static VillagerActing.Mode ModeFor(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return VillagerActing.Mode.Chop;
            if (resource == Res.Timber || resource == Res.Boards)
                return VillagerActing.Mode.Chop;
            // v15 `Forage` (2026-10-01): crouch, pick from the bush, into
            // the basket (was the hoe).
            if (resource == Res.Spice || resource == Res.Food)
                return VillagerActing.Mode.Forage;
            return VillagerActing.Mode.Mine;        // stone, ore, anything mined
        }

        /// The trade, from the position the building offers. Keyed off
        /// `BuildPlans.PositionAt` rather than the plan id, so a second
        /// building that also employs a sawyer needs no entry here.
        /// The trade at THIS building: `ModeAt`, except that a sawyer at a
        /// sawmill wearing the level 2 saw shed (its model carries
        /// `MillCrankWheels`) winds the crank instead of sawing.
        VillagerActing.Mode JobMode(string planId, Building post)
        {
            var m = ModeAt(planId);
            return m == VillagerActing.Mode.Saw && Cranks(post) ? VillagerActing.Mode.Crank : m;
        }

        /// Does `post`'s model have the level 2 crank, or the level 1 mill's
        /// quern (2026-10-03: the miller's `Mill` clip is authored with the
        /// peg at his front-right, so he stands square to the building's
        /// front on the exact `Worker_Stand`, as the sawyer does at the
        /// crank)? Cached per building
        /// and model revision (asked every frame he works).
        bool Cranks(Building post)
        {
            if (post == null) return false;
            if (!ReferenceEquals(post, crankPost) || crankRev != post.ModelRevision)
            {
                crankPost = post;
                crankRev = post.ModelRevision;
                crankHere = post.GetComponentInChildren<MillCrankWheels>(true) != null
                    || post.GetComponentInChildren<MillQuern>(true) != null;
            }
            return crankHere;
        }

        Building crankPost;
        int crankRev;
        bool crankHere;

        static VillagerActing.Mode ModeAt(string planId)
        {
            string post = BuildPlans.PositionAt(planId);
            switch (post)
            {
                // Each a v15 clip (2026-10-01); a rig without the state shows
                // the old code pose (`VillagerActing.CodePose`).
                case "sawyer": return VillagerActing.Mode.Saw;
                case "smith": return VillagerActing.Mode.Smith;
                case "farmhand": return VillagerActing.Mode.Farm;
                case "cook": return VillagerActing.Mode.Cook;
                case "miller": return VillagerActing.Mode.Mill;
                case "quarryman": return VillagerActing.Mode.Quarry;
                case "fletcher": return VillagerActing.Mode.Fletcher;
                // Gutting the catch on the prep board -- the fishing hut,
                // 2026-09-27.
                case "fisher": return VillagerActing.Mode.Fisher;
                default: return VillagerActing.Mode.Hammer;
            }
        }

        /// Where a load from the direct-rate gather loop goes: the stack
        /// that resource belongs on. A builder's carry to a SITE is a ledger
        /// haul now (`TickHaul` walks straight to `HaulOf(r).toAt`, which the
        /// books already aimed at "the oldest site that still wants this" --
        /// `OutpostLedger.SiteWanting` -- when the trip was booked), so this
        /// is only ever asked for a Gather row's own pile.
        ///
        /// **The store, not the fire, once a storage building stands**
        /// (2026-09-24): `StoreSpot` decides, so the hut takes every armful.
        /// Asked every step of the carry home, so a hut raised while he is
        /// walking takes the load he is already carrying.
        Vector3 Dropoff(OutpostHand r, string resource) => StoreSpot(resource, out _);

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
        /// the camp's claim table now.
        ///
        /// **Where his next trip will send him, or nowhere (2026-10-03,
        /// honest bodies).** Only asked of a gatherer between ledger trips
        /// (`TickErrand`). The prop is `NearestNode` -- the same pick
        /// `HaulPickupSpot` makes for the trip itself (nearest to the camp
        /// centre, this island, reachable, no `Reach` bound since the
        /// ledger's trip has none) -- so the swing between trips is at the
        /// rock the next armful comes off. False when there is nothing of
        /// the kind to work: the caller rests at home instead of the old
        /// potter to a random spot by the fire, swinging at the grass.
        ///
        /// **Measured from the CAMP, not from the man.** It is the camp that
        /// works outward, and asking from where each hand happens to be
        /// standing would send somebody who has just walked home back to the
        /// same place the pile came from.
        bool FindSomethingToWorkAt(OutpostHand r, out Vector3 spot)
        {
            spot = home;
            workNode = null;
            string what = WhatFor(r);

            // Wheat: the camp hands out the bed the ledger will cut next,
            // nearest the fire outward, one hand a bed (`Outpost.ClaimBed`).
            if (what == Res.Food && camp.ClaimBed(this, out _, out Vector3 bedAt))
            {
                spot = Stand(bedAt);
                return true;
            }

            if (string.IsNullOrEmpty(what) || what == Res.Timber) return false;
            var near = NearestNode(what);
            if (near == null) return false;
            workNode = near;
            spot = Stand(near.transform.position, near.StandOff);
            return true;
        }

        /// The prop a gatherer is swinging at between trips
        /// (`FindSomethingToWorkAt`), so the swing stops when it is worked
        /// out from under him. Null at a wheat bed or at nothing.
        ResourceNode workNode;

        /// Beside the thing, not inside it — and on a bearing of this hand's
        /// own, so three cutters sent to the same trunk ring it instead of
        /// standing in one another.
        ///
        /// `off` is how far from the thing's pivot: a pace for a trunk, just
        /// outside the footprint for a stone deposit (`ResourceNode.StandOff`,
        /// 2026-09-24) -- on the camp's side of it, which is where he came from
        /// and where he carries the armful back to.
        Vector3 Stand(Vector3 at, float off = 1.1f)
        {
            Vector3 away = at - camp.CampCentre;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            away.Normalize();

            float spread = agent != null
                ? (Mathf.Abs(agent.DisplayName.GetHashCode() % 140) - 70f) : 0f;
            away = Quaternion.Euler(0f, spread, 0f) * away;

            Vector3 p = at - away * off;
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
        /// On a ladder chain (2026-09-27, `LadderClimb`): while climbing,
        /// the climb has the body.
        readonly LadderClimb climb = new LadderClimb();

        /// The work-pad gate's memory for the current walk target
        /// (`WorkerPad.Gate`, 2026-09-28).
        Vector3 padFor = new Vector3(1e9f, 0f, 1e9f);
        bool padStartedOn, padPassed;

        /// **A guard calling the walk "reached" where he stands
        /// (2026-09-30).** On a pad's gate leg that is the GATE, never the
        /// errand: the gate is spent and the next step walks on to the real
        /// target. The stall, wall and slope guards used to return true
        /// here, which told the caller he was at the store / bay / rack
        /// while he still stood by the mill.
        bool Reached(bool gated)
        {
            if (gated) padPassed = true;
            return !gated;
        }

        bool Walk(Vector3 to, float dt)
        {
            if (climb.Active) { climb.Tick(camp, transform, dt); return false; }
            Vector3 here = transform.position;
            // **A raised work pad's rear gate (2026-09-28, `WorkerPad`).**
            // On to or off the lumber mill's pad by `Worker_Approach` only:
            // the bench and racks box the rest of it in. `to` becomes the
            // gate for this step; reaching the gate is never arriving -- the
            // next step walks on to the real target down the lane.
            // Once per trip (`padFor`): a new target re-arms the gate and
            // notes whether he set off from the pad; reaching the gate
            // spends it, and he goes straight on from there.
            if ((to - padFor).sqrMagnitude > 0.01f)
            {
                padFor = to;
                padStartedOn = WorkerPad.OnPad(here);
                padPassed = false;
            }
            bool gated = false;
            if (!padPassed && WorkerPad.Gate(here, to, padStartedOn, out Vector3 via))
            {
                if (FlatDistance(here, via) < 0.35f) padPassed = true;
                else { gated = true; to = via; }
            }
            // **Spent once it stops asking (2026-10-01).** `Gate` goes quiet
            // within `Arrive` of the gate, so the line above never saw him
            // arrive: a step on (down the lane toward the route's first
            // corner) put him back in the lane past `Arrive`, the gate
            // called him back, and he shuffled on the spot for good. A trip
            // that began on the pad has passed its gate the first time the
            // gate has nothing to say.
            else if (!padPassed && padStartedOn) padPassed = true;
            Vector3 d = to - here;
            d.y = 0f;
            float dist = d.magnitude;
            // **Somebody already stands there (2026-10-01, `Spacing`):** a
            // man walking to a spot another body is holding stops beside
            // it rather than shouldering into him -- except at his own
            // work spot, where the other one yields.
            if (dist >= 0.35f && dist < 2f * BodyRadius + 0.15f && SpotHeldByOther(to))
            {
                stride.Stop();
                ClearRoute();
                ResetStall(to, 0f);
                return !gated;
            }
            if (dist < 0.35f)
            {
                // Arriving without a step (spawned or loaded on his spot)
                // still stands him at the right height: on a work pad, on it
                // (2026-09-28), rather than 0.16 m inside the platform.
                if (camp != null)
                {
                    float y = WorkerPad.Foot(here, camp.GroundAt(here));
                    if (Mathf.Abs(y - here.y) > 0.02f && y > here.y) { here.y = y; transform.position = here; }
                }
                stride.Stop();
                acting?.Commanded(0f);
                ClearRoute();
                ResetStall(to, 0f);
                return !gated;
            }

            // **The stall guard (2026-09-27).** Kevin's phone: a hand stood
            // motionless against a wall tower's ladder, between the runs, for
            // good -- the wall guard below pins a body in the corner two
            // runs make and has nothing to slide along, and nothing ever
            // gave up. Every walk now watches its own progress: no metre
            // gained on the SAME target for `StallSeconds` and he steps out
            // of wherever he is wedged, re-plans, and says why on his sheet
            // (`bodyBlocked` -> `StallReason`); close enough and clear of any
            // wall, the errand counts as reached where he stands.
            if (TickStall(here, to, dist, RouteLeft(here, to, dist), dt)) { stride.Stop(); ClearRoute(); return Reached(gated); }
            if (escapeLeft > 0f) { StepEscape(here, dt); return false; }

            // Where to head THIS frame: the next corner of the route if there
            // is one, otherwise the target itself — which is exactly the
            // straight line this used to be, and is what a failed plan falls
            // back to.
            Vector3 aim = NextCorner(here, to, dist, dt);
            if (stallNote != null && row != null && string.IsNullOrEmpty(row.bodyBlocked))
                row.bodyBlocked = stallNote;
            // The leg up (or down) a ladder chain: the route kept both of
            // its ends as corners (`CampPath.Route`).
            if (climb.TryBegin(camp, transform, here, aim, dt)) return false;

            Vector3 leg = aim - here;
            leg.y = 0f;
            float legLen = leg.magnitude;
            if (legLen < 0.0001f) return false;

            // A player's road (2026-09-27): ×`CampRoads.SpeedMultiplier` on a
            // road cell -- the same rule the route prices and the invisible
            // walker meters (`Outpost.WalkedMetres`).
            // **Turn, then walk (2026-10-01, `Stride`).** Kevin: they slid
            // about and did not turn their bodies. The body turns toward the
            // leg at a body's rate and moves only along its own forward, at
            // its walk's own speed (feet planted), easing off and turning on
            // the spot at a sharp corner, and braking into the stop.
            float steer = Time.frameCount - passSteerFrame <= 1 ? passSteer : 0f;
            Vector3 step = stride.Step(transform, leg, Cruise * CampRoads.SpeedAt(camp, here), dist - 0.35f, dt, steer);
            acting?.Commanded(stride.Speed);
            Vector3 next = here + step;

            // **The wall guard (2026-09-24).** Kevin: *"villagers ... walk
            // through the walls that I've built."* The route keeps a hand
            // on the right side of a wall, but a route is cell centres and
            // a body is not: a corner retired 1.4 m early, a straight hop
            // under `NextCorner`'s 6 m, the frames before a plan comes back
            // -- each of those walked a straight line, and a straight line
            // does not know a palisade is there. So every step is checked
            // against the walls themselves, as lines. Blocked, he slides
            // along the wall (which is where the gate or the end of it is);
            // pinned with no slide, he stands and the route is re-asked.
            //
            // **And buildings (2026-10-01, `CampPath.Solids`).** Kevin:
            // villagers walked straight through buildings. The same guard,
            // against each building's benches, racks and posts as boxes:
            // slide along the face, never through; a box his own errand's
            // spot is inside of never blocks him.
            if (CampPath.Obstructs(camp, here, next, to, CampPath.Walker.Hand, out Vector3 along))
            {
                Vector3 slide = along * Vector3.Dot(step, along);
                Vector3 alt = here + slide;
                if (slide.sqrMagnitude < 1e-8f
                    || CampPath.Obstructs(camp, here, alt, to, CampPath.Walker.Hand, out _))
                {
                    // Re-ask in half a second rather than every frame: a
                    // pinned man asking every frame is a search a frame.
                    routeAge = Mathf.Max(routeAge, RePlanSeconds - 0.5f);
                    // A target just the near side of the wall -- a pile or a
                    // stand spot laid against the palisade -- is as reached
                    // as it is going to get; one across it is not.
                    if (dist < 1.2f && !CampPath.Crosses(camp, here, to, CampPath.Walker.Hand))
                    { stride.Stop(); ClearRoute(); return Reached(gated); }
                    stride.Stop();
                    Face(leg, dt);
                    return false;
                }
                next = alt;
            }

            // **The slope guard (2026-09-27).** Kevin: *"villagers and goats
            // can just walk straight up the sides of the mountains."* The
            // route keeps off steep cells, but the straight line (a failed
            // or pending plan, a target off the grid), every hop under
            // `NextCorner`'s 6 m, and the last leg from the snapped cell up
            // to a tree on a slope never asked. So every step does now, with
            // the same numbers as the grid (`Walkability`). Refused: he
            // stands, re-asks, and a target he cannot climb to counts as
            // reached from the foot (`SlopeArrive`) or after `SlopeGiveUp`
            // seconds of standing -- an errand must never stall on a hill.
            // Somebody already on ground too steep for him (dropped there)
            // may always walk off it downhill.
            //
            // **Asked about the terrain, not the boards (2026-09-30).**
            // Kevin: the sawyer "glitches uncontrollably" at the lumber
            // mill. The backstop reads `from.y` as the ground under him,
            // but on a `WorkerPad` his feet are on the platform, which
            // stands at the building's HIGHEST corner + 0.16 m (`Raise`) --
            // on a sloping plot 0.7 m+ over the terrain. Every step on the
            // pad measured that as a cliff and was refused, so he could
            // never walk off it, and the slope give-up just below called
            // every errand "reached" from the pad: store, bay, rack all
            // arrived on the spot, trip after trip, poses flipping in place.
            //
            // **Far away is never "reached" (2026-10-03, honest bodies).**
            // The give-up after `SlopeGiveUp` seconds used to return reached
            // at ANY distance: a body refused by a slope 30 m from the store
            // fired `BodyArrived` and the goods moved from 30 m away. Now
            // only the foot of the climb (`SlopeArrive`) counts. Further out,
            // a refusal that lasts is a stuck walker: the route is thrown
            // away and re-planned from where he stands, his sheet says so
            // (`stallNote` -> `bodyBlocked` -> the Stuck alert), and the
            // stall guard above (`TickStall`) steps him out of the wedge and
            // re-plans every `StallSeconds`, exactly as for a man pinned
            // by a wall. His order and his trip are left alone -- he just
            // does not arrive. The pad fix above (`Grounded(here)`) is
            // untouched, so a sawyer on the lumber mill's boards still
            // steps off them.
            if (!Walkability.MayStep(camp, Grounded(here), next, Walkability.Feet.Man))
            {
                routeAge = Mathf.Max(routeAge, RePlanSeconds - 0.5f);
                slopeStuck += dt;
                if (dist < SlopeArrive)
                { slopeStuck = 0f; stride.Stop(); ClearRoute(); return Reached(gated); }
                if (slopeStuck > SlopeGiveUp)
                {
                    slopeStuck = 0f;
                    stallNote = "stuck — the ground's too steep to get there";
                    hasRoute = false;
                    route.Clear();
                    routeAt = 0;
                    if (routeAge > 0f) routeAge = 0f;   // re-planned next step
                }
                stride.Stop();
                Face(leg, dt);
                return false;
            }
            slopeStuck = 0f;

            // Terrain, or a raised pad's boards (`WorkerPad`; a one-step
            // 0.16 m rise at the mill, and the terrain everywhere else).
            next.y = WorkerPad.Foot(next, camp.GroundAt(next));
            transform.position = next;
            return false;
        }

        // --- the stall guard -------------------------------------------------------

        /// Seconds without a metre of progress toward the same target before
        /// a walker counts as stuck.
        const float StallSeconds = 5f;
        /// Stuck this close to the target, with no wall between, is arrived.
        const float StallArrive = 2.5f;
        /// After this many escapes that did not help, a target within
        /// `StallGiveUpArrive` (no wall between) is called reached anyway.
        const int StallEscapesBeforeGiveUp = 3;
        const float StallGiveUpArrive = 5f;
        const float EscapeSeconds = 0.8f;

        Vector3 stallFor;
        float stallBest, stallFor_t;
        /// Which yardstick `stallBest` was measured on (`RouteLeft`).
        int routePlans, stallRuler = -2;
        bool stallRebase;
        float stallLastLeft;
        int stallEscapes;
        float escapeLeft;
        Vector3 escapeDir;
        /// Shown on the hand's sheet while he is stuck (`bodyBlocked`).
        string stallNote;

        void ResetStall(Vector3 to, float dist)
        {
            stallFor = to;
            stallBest = dist;
            stallLastLeft = dist;
            stallFor_t = 0f;
            stallEscapes = 0;
            escapeLeft = 0f;
            stallNote = null;
        }

        /// **Metres left to walk** on the planned route (to its next corner,
        /// then corner to corner to the target), or the straight line when
        /// there is no route. What the stall guard measures progress on
        /// (2026-10-01): Kevin's phone showed "Yara · stuck" -- her route to
        /// the watchtower went round the camp first, AWAY from it, so the
        /// straight-line distance did not shrink by a metre in 5 s and the
        /// guard called a walking woman wedged (three escapes, "stuck").
        /// At the slower, planted-feet walks (a tired 0.29 m/s covers 1.45 m
        /// in those 5 s) it fired on nearly every detour.
        float RouteLeft(Vector3 here, Vector3 to, float dist)
        {
            float mx = to.x - routeFor.x, mz = to.z - routeFor.z;
            bool onRoute = hasRoute && routeAt < route.Count && mx * mx + mz * mz <= 1f;
            // A new plan (or the straight line instead of one) is a new
            // yardstick: re-base the best on it, keep the clock running --
            // a wedged man re-planning still trips the guard in time.
            int ruler = onRoute ? routePlans : -1;
            if (ruler != stallRuler) { stallRuler = ruler; stallRebase = true; }
            if (!onRoute) return dist;
            float left = FlatDistance(here, route[routeAt]);
            for (int i = routeAt + 1; i < route.Count; i++) left += FlatDistance(route[i - 1], route[i]);
            return Mathf.Max(left, dist);
        }

        /// True when the errand should count as reached where he stands.
        /// `left` = metres of route left (`RouteLeft`): progress is walked
        /// route, not straight-line closing.
        bool TickStall(Vector3 here, Vector3 to, float dist, float left, float dt)
        {
            float mx = to.x - stallFor.x, mz = to.z - stallFor.z;
            if (mx * mx + mz * mz > 1f) { ResetStall(to, left); return false; }
            // A metre, or what a third of his walk covers in the window
            // (a tired walk: 0.5 m), whichever is less.
            float need = Mathf.Min(1f, 0.35f * Cruise * StallSeconds);
            // Re-based on a new yardstick, the progress already made since
            // the best carries over (a re-plan every second must not move
            // the goalposts and starve the guard of progress).
            if (stallRebase) { stallRebase = false; stallBest = left + (stallBest - stallLastLeft); }
            stallLastLeft = left;
            if (left < stallBest - need)
            {
                stallBest = left;
                stallFor_t = 0f;
                stallEscapes = 0;
                stallNote = null;
                return false;
            }
            if (escapeLeft > 0f) return false;
            stallFor_t += dt;
            if (stallFor_t < StallSeconds) return false;
            stallFor_t = 0f;

            bool walled = CampPath.Crosses(camp, here, to, CampPath.Walker.Hand);
            // Walled off with no way round already says so ("needs a gate",
            // `NextCorner`) and waits for one; stepping about would not help.
            if (walled && !hasRoute && routeAge < 0f) return false;
            if (!walled && (dist < StallArrive
                || (stallEscapes >= StallEscapesBeforeGiveUp && dist < StallGiveUpArrive)))
            {
                ResetStall(to, 0f);
                return true;
            }

            // Wedged: step out, then ask for a fresh route.
            stallEscapes++;
            stallBest = left;
            stallNote = "stuck — can't get through to where he's going";
            if (PickEscape(here, to, out escapeDir)) escapeLeft = EscapeSeconds;
            hasRoute = false;
            route.Clear();
            routeAt = 0;
            if (routeAge > 0f) routeAge = 0f;   // !hasRoute + age >= 0: re-planned next step
            return false;
        }

        /// The free direction out of a wedge: of eight, the one whose stride
        /// is not refused by a wall or the slope and lands furthest from any
        /// wall line, ties toward the target.
        bool PickEscape(Vector3 here, Vector3 to, out Vector3 dir)
        {
            dir = Vector3.zero;
            Vector3 toT = to - here; toT.y = 0f;
            if (toT.sqrMagnitude > 1e-6f) toT.Normalize();
            float best = float.MinValue;
            var walls = camp.Walls;
            for (int k = 0; k < 8; k++)
            {
                float t = k * Mathf.PI * 0.25f;
                var d = new Vector3(Mathf.Sin(t), 0f, Mathf.Cos(t));
                Vector3 q = here + d * 1.2f;
                if (CampPath.Obstructs(camp, here, q, to, CampPath.Walker.Hand, out _)) continue;
                if (!Walkability.MayStep(camp, Grounded(here), q, Walkability.Feet.Man)) continue;   // terrain, not pad (see `Walk`)
                float clear = 3f;
                if (walls != null)
                    for (int i = 0; i < walls.Count; i++)
                    {
                        var w = walls[i];
                        if (w == null || w.Breached) continue;
                        clear = Mathf.Min(clear, w.FlatDistanceTo(q));
                    }
                float score = clear + 0.25f * Vector3.Dot(d, toT);
                if (score > best) { best = score; dir = d; }
            }
            return best > float.MinValue;
        }

        void StepEscape(Vector3 here, float dt)
        {
            escapeLeft -= dt;
            Vector3 next = here + stride.Step(transform, escapeDir, Cruise, 9f, dt);
            acting?.Commanded(stride.Speed);
            if (CampPath.Obstructs(camp, here, next, stallFor, CampPath.Walker.Hand, out _)
                || !Walkability.MayStep(camp, Grounded(here), next, Walkability.Feet.Man))
            { escapeLeft = 0f; return; }
            next.y = WorkerPad.Foot(next, camp.GroundAt(next));
            transform.position = next;
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

        /// Extra seconds before re-asking after "no route, and straight is
        /// through a wall". On top of `RePlanSeconds`, so about four seconds
        /// between asks: a gate the player puts in is found that quickly.
        const float NoRouteBackoff = 3f;

        /// **Give up on a walled-off errand (2026-09-28).** Kevin: a hand
        /// with no route and a wall across the straight line used to wait
        /// and re-ask forever when no gate exists at all -- stood there
        /// "working" on the books while the sheet said "needs a gate" for
        /// good. After this many failed asks in a row for the SAME
        /// destination, the trip he was walking is dropped (since
        /// 2026-10-02 his ORDER is kept -- the player's assignment is
        /// permanent until the player changes it); `StallReason`
        /// still shows "walled off" while he stands there and while a fresh
        /// trip is asking for the same blocked spot again.
        const int WalledAsksBeforeGiveUp = 3;
        int walledAsks;
        /// **Same give-up, a different cause (2026-09-28).** A route that
        /// fails for any reason OTHER than "straight line crosses a wall"
        /// (a target the planner can't reach at all, a stale/disagreeing
        /// grid) used to leave `routeAge` at zero and ask again next frame
        /// forever, with no backoff and no `bodyBlocked` message -- the
        /// straight-line branch just below already backs off and gives up;
        /// this counter does the same for its `else`.
        int lostAsks;

        /// Metres from a target a hand stops at when the ground up to it is
        /// too steep: the tree on the bank is worked from its foot.
        /// (`CampPath.ReachCells` is the same distance for target picks.)
        const float SlopeArrive = 4f;
        /// Seconds a hand stands refused by the slope before calling the
        /// errand reached where he is. Only a grid/step disagreement or a
        /// target that slipped the reachability filter gets here.
        const float SlopeGiveUp = 2f;
        float slopeStuck;

        void ClearRoute()
        {
            route.Clear();
            routeAt = 0;
            hasRoute = false;
            routeAge = 0f;
            walledAsks = 0;
            lostAsks = 0;
            if (row != null) row.bodyBlocked = null;
        }

        Vector3 NextCorner(Vector3 here, Vector3 to, float dist, float dt)
        {
            // Close in, or a hop not worth a search: go straight. Most steps
            // a camp ever takes are this one -- UNLESS a wall is in the way
            // (2026-09-24): a tree four metres the far side of the palisade
            // was a straight walk through it. Then it is planned like any
            // other walk, and goes round by the gate.
            bool straightCrosses = CampPath.Crosses(camp, here, to, CampPath.Walker.Hand);
            // A building in the way (2026-10-01) is planned round too.
            if (dist < 6f && !straightCrosses && !CampPath.SolidBetween(camp, here, to, to))
            { ClearRoute(); return to; }

            routeAge += dt;

            // A walker told "no route" waits out `NoRouteBackoff` (a
            // negative age) before asking again -- unless the errand itself
            // changed, which is a new question.
            bool moved = Vector3.SqrMagnitude(new Vector3(to.x - routeFor.x, 0f, to.z - routeFor.z))
                       > RePlanMoved * RePlanMoved;
            bool stale = moved
                || (!hasRoute
                    ? routeAge >= 0f
                    : routeAt >= route.Count || routeAge >= RePlanSeconds);

            if (stale && CampPath.Budget())
            {
                var map = CampPath.For(camp);
                if (moved) { walledAsks = 0; lostAsks = 0; }
                routeAge = 0f;
                routeFor = to;
                routeAt = 0;
                routePlans++;
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
                if (!hasRoute)
                {
                    route.Clear();
                    // **No way round, and straight is through a wall**: a
                    // ring with no gate, or the far side of one. He waits
                    // where he is -- and asks less often, because a failed
                    // search in a closed ring has just flooded the whole
                    // ring (up to `MaxExpansions`), and a camp of hands
                    // doing that every 1.2 s is a phone's frame budget.
                    if (straightCrosses)
                    {
                        routeAge = -NoRouteBackoff;
                        walledAsks++;
                        if (walledAsks >= WalledAsksBeforeGiveUp && row != null)
                        {
                            // **The ERRAND is dropped, never the job
                            // (2026-10-02).** Kevin: *"you assign someone
                            // somewhere, thats what they do."* This used to
                            // set the order to Idle -- which silently undid
                            // the player's assignment of every runner whose
                            // barrow trip once failed to route (runners are
                            // Work hands walking to arbitrary racks, piles
                            // and shore spots). The job stays; the stuck
                            // trip goes, and `bodyBlocked` puts him on the
                            // Stuck alert so the player can see why.
                            // **And the trip he was walking (2026-09-30).**
                            // Kevin's save: a closed palisade with the store
                            // inside and three hands outside. The order went
                            // to Idle but the planned meal trip stayed on the
                            // row -- a driven hand's trip is only ever walked
                            // by this body, and `EatStep` starts no new meal
                            // while `eating` is set -- so every hand starved
                            // beside 30 grilled fish. Dropped the ledger's
                            // own way (`DropCarriedLoadNow`: a planned load
                            // is cancelled, one in his arms goes on the
                            // ground here). The next trip to the same walled
                            // spot waits out this same backoff (`routeFor`
                            // has not moved) -- never a search a frame.
                            camp.Ledger?.DropCarriedLoadNow(row);
                            row.bodyBlocked = "walled off — no way round, needs a gate";
                            walledAsks = 0;
                        }
                    }
                    else
                    {
                        // **No wall, still no route (2026-09-28).** The
                        // straight line is clear but `CampPath` couldn't
                        // find a plan anyway (a target outside its reach
                        // filter, a stale grid) -- back off the same as the
                        // walled case rather than re-ask every frame
                        // forever. The straight-line fallback below still
                        // carries him toward the target while he waits out
                        // the backoff. **His order is kept (2026-10-02)**:
                        // it used to go to Idle here, which wiped runners'
                        // and workers' assignments; now he is only flagged
                        // stuck (`bodyBlocked`, the Stuck alert).
                        routeAge = -NoRouteBackoff;
                        lostAsks++;
                        if (lostAsks >= WalledAsksBeforeGiveUp && row != null)
                        {
                            row.bodyBlocked = "can't reach that — no way there";
                            lostAsks = 0;
                        }
                    }
                }
                else { walledAsks = 0; lostAsks = 0; }
            }

            // No route (yet, or at all): the old straight line -- but never
            // through a wall. Standing still for the frames a plan takes, or
            // until a gate goes in, is the honest answer there. **And say
            // so** (2026-09-24): the sheet reads `bodyBlocked`, because the
            // books think he is working while his feet are at the fire.
            // Covers both give-up causes (2026-09-28): a wall with no gate,
            // or a route that fails for any other reason.
            if (row != null)
                row.bodyBlocked = !hasRoute && routeAge < 0f
                    ? (straightCrosses ? "walled off — no way round, needs a gate" : "can't reach that — no way there")
                    : null;
            if (!hasRoute || routeAt >= route.Count) return straightCrosses ? here : to;

            // Retire corners we are already on top of, and never let the last
            // one stand in for the target. **Not round a wall's end early**
            // (2026-09-24): a corner is retired at 1.4 m only if the line
            // from here to the one after it is clear of the walls; otherwise
            // he walks on to the corner itself (within 0.3 m it goes anyway,
            // and the step guard in `Walk` has the last word).
            while (routeAt < route.Count - 1)
            {
                Vector3 c = route[routeAt];
                float dx = c.x - here.x, dz = c.z - here.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > CornerReach * CornerReach) break;
                Vector3 after = routeAt + 1 >= route.Count - 1 ? to : route[routeAt + 1];
                if (d2 > 0.09f && (CampPath.Crosses(camp, here, after, CampPath.Walker.Hand)
                                   || CampPath.SolidBetween(camp, here, after, to))) break;
                routeAt++;
            }

            return routeAt >= route.Count - 1 ? to : route[routeAt];
        }

        // --- the watchtower platform (2026-09-27) --------------------------------

        /// Where a lookout is with respect to his tower's ladder.
        enum TowerState : byte { Ground, Up, Top, Down }
        TowerState tower;
        Building towerOn;
        /// `towerOn.ModelRevision` when he last stood on it (a level 2 swap
        /// moves his corner).
        int towerRev = -1;
        /// The tower's own ladder climb: separate from `climb` (the cliff
        /// chains `Walk` starts on its own), so `Walk` never ticks it.
        readonly LadderClimb towerClimb = new LadderClimb();

        /// Is this man on a watchtower (on its deck, or on its ladder)?
        public bool OnTower => tower != TowerState.Ground;

        /// The building this Work row is drawn at (`PreferWorkplace` first).
        Building WorkPostOf(OutpostHand r)
            => preferred != null && preferred.Id == r.target ? preferred : camp.WorkplaceOf(r);

        /// Metres between two waiting runners: clear of two `BodyRadius`.
        const float RunnerWaitGap = 0.95f;

        /// **Each runner waits on his own spot (2026-10-02).** Kevin's
        /// phone: *"they're both just glitching in the storage hut"* -- both
        /// runners of one store hut waited on the hut's ONE work spot, and a
        /// man's own work spot is the one place `SpotHeldByOther` never
        /// yields, so both walked onto it; standing there both were anchored
        /// (`SpacingRole` 1), `SpaceBodies` pushed the later one off, he
        /// walked straight back, and so on every frame. The first runner
        /// (in the hand list's order) keeps the door; the next stand a gap
        /// to its right, its left, two gaps right..., along the hut's front
        /// -- or straight out from it where that ground is blocked.
        Vector3 RunnerWaitSpot(OutpostHand r, Building post, Vector3 door)
        {
            int k = 0;
            var hands = camp != null && camp.Ledger != null ? camp.Ledger.hands : null;
            if (hands != null)
                foreach (var o in hands)
                {
                    if (o == r) break;
                    if (OutpostLedger.IsRunner(o) && camp.WorkplaceOf(o) == post) k++;
                }
            if (k == 0) return door;
            Vector3 outward = door - post.transform.position;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-4f) outward = Vector3.forward;
            outward.Normalize();
            Vector3 along = new Vector3(outward.z, 0f, -outward.x);
            float side = (k & 1) == 1 ? 1f : -1f;
            float step = RunnerWaitGap * ((k + 1) / 2);
            Vector3 a = door + along * (side * step);
            if (StandsClear(door, a)) return a;
            Vector3 b = door + outward * (RunnerWaitGap * k);
            if (StandsClear(door, b)) return b;
            return door;
        }

        /// Open ground a man may stand on, reached from `from` in a line.
        bool StandsClear(Vector3 from, Vector3 to)
        {
            if (camp == null) return false;
            to.y = from.y;
            if (CampPath.Obstructs(camp, from, to, FarGoal, CampPath.Walker.Hand, out _)) return false;
            if (!Walkability.MayStep(camp, Grounded(from), to, Walkability.Feet.Man)) return false;
            return true;
        }

        /// The watchtower this row should be standing on, or null.
        Building LookoutTower(OutpostHand r)
        {
            if (r == null || r.order != OutpostOrder.Work || r.target != OutpostLedger.WatchtowerId
                || r.Hauling) return null;
            var b = WorkPostOf(r);
            return b != null && b.Id == OutpostLedger.WatchtowerId ? b : null;
        }

        /// Something else has the body (the Hand, a throw): he is not on the
        /// tower any more, whatever he was doing on it.
        void OffTower()
        {
            towerClimb.Cancel();
            tower = TowerState.Ground;
            towerOn = null;
        }

        /// **The lookout's walk to his ladder, then up it.** True when this
        /// frame is handled; false for a tower with no ladder marks or no
        /// clear foot (the old stand-at-the-door shift runs instead).
        bool TowerFootWalk(Building post, Vector3 door, float dt)
        {
            if (!camp.TowerLadderFoot(post, out Vector3 foot)) return false;
            acting?.Set(VillagerActing.Mode.None);
            if (!Walk(foot, dt)) { phase = Phase.Going; return true; }
            phase = Phase.Working;
            // The stall guard can call a spot "reached" from a couple of
            // metres off: never start a climb from across a wall or a hedge.
            if (FlatDistance(transform.position, foot) > 1.5f)
            {
                Face(post.transform.position - transform.position, dt);
                return true;
            }
            var shape = camp.TowerClimbShape(post, foot);
            if (shape == null) return false;
            towerOn = post;
            towerRev = post.ModelRevision;
            tower = TowerState.Up;
            towerClimb.Begin(camp, transform, shape, true, dt);
            return true;
        }

        /// **Up, on, or down the tower.** True while the tower has the body.
        ///
        /// - Ground: nothing to do here -- unless the body was PUT on his
        ///   tower's deck (a save loading, the camp coming into view:
        ///   `Outpost.ArrangeHands` places a lookout there), in which case he
        ///   is simply up.
        /// - Up / Down: `LadderClimb` walks the tower's shape
        ///   (`Outpost.TowerClimbShape`). A lookout relieved mid-climb finishes
        ///   going up, then comes straight back down.
        /// - Top: stands on the deck looking out, away from the camp, and
        ///   sweeps the horizon slowly. Relieved, re-posted to another tower,
        ///   or given a trip: climbs down first.
        bool TickTower(OutpostHand r, float dt)
        {
            Building want = LookoutTower(r);
            if (tower == TowerState.Ground)
            {
                if (want == null || !Outpost.TowerMarks(want, out _, out _, out Vector3 deck)) return false;
                if ((transform.position - deck).sqrMagnitude > 0.8f * 0.8f) return false;
                tower = TowerState.Top;
                towerOn = want;
            }
            // The tower went from under him (burnt, pulled down): on his feet
            // where he was.
            if (towerOn == null)
            {
                OffTower();
                Vector3 p = transform.position;
                p.y = Ground(p);
                transform.position = p;
                return false;
            }
            acting?.Set(VillagerActing.Mode.None);
            switch (tower)
            {
                case TowerState.Up:
                {
                    phase = Phase.Going;
                    if (!towerClimb.Tick(camp, transform, dt)) return true;
                    // Finished -- or dropped because something moved him.
                    if (!Outpost.TowerMarks(towerOn, out _, out _, out Vector3 deck))
                    {
                        OffTower();
                        return false;
                    }
                    if ((transform.position - deck).sqrMagnitude > 1f)
                    {
                        // The tower was upgraded under him mid-climb (a new
                        // deck, a new corner): he steps to it.
                        if (towerRev == towerOn.ModelRevision) { OffTower(); return false; }
                        transform.position = deck;
                    }
                    towerRev = towerOn.ModelRevision;
                    tower = TowerState.Top;
                    return true;
                }
                case TowerState.Down:
                {
                    phase = Phase.Going;
                    if (!towerClimb.Tick(camp, transform, dt)) return true;
                    OffTower();
                    Vector3 p = transform.position;
                    float g = Ground(p);
                    if (p.y > g + 0.3f) { p.y = g; transform.position = p; }
                    phase = Phase.Resting;
                    wait = 0f;
                    return true;
                }
                default:
                {
                    if (!Outpost.TowerMarks(towerOn, out _, out _, out Vector3 deck))
                    {
                        towerOn = null;
                        return TickTower(r, dt);
                    }
                    if (!ReferenceEquals(want, towerOn))
                    {
                        var shape = camp.TowerClimbShape(towerOn, WorkSpot(camp, towerOn));
                        if (shape == null)
                        {
                            towerOn = null;
                            return TickTower(r, dt);
                        }
                        tower = TowerState.Down;
                        phase = Phase.Going;
                        towerClimb.Begin(camp, transform, shape, false, dt);
                        return true;
                    }
                    // **Upgraded under him (2026-10-01)**: the level 2 deck
                    // puts his corner ~1.1 m further out; he steps to it
                    // rather than reading as "moved off".
                    if (towerRev != towerOn.ModelRevision)
                    {
                        towerRev = towerOn.ModelRevision;
                        transform.position = deck;
                    }
                    // Moved off it by something that is not the Hand (a
                    // teleport home): he is wherever that put him.
                    if ((transform.position - deck).sqrMagnitude > 1f) { OffTower(); return false; }
                    phase = Phase.Working;
                    transform.position = deck;
                    // **The lookout shoots first (bows, 2026-09-30)**: with
                    // the camp's bow and a raider in range he stops sweeping
                    // the horizon and looses at him (`CampWorker.Archery`).
                    if (TickTowerArcher(r, deck, dt)) return true;
                    // The v15 `Lookout` clip (2026-10-01): scanning the
                    // horizon, pointing out a sail. Over the top of the
                    // `Set(None)` at the head of this method, same frame.
                    acting?.Set(VillagerActing.Mode.Lookout);
                    // On watch in his back corner, facing out over the
                    // corner post along the deck diagonal. The clip does
                    // the scanning (its head and shoulders sweep the
                    // horizon), so the body holds still: no sweep here.
                    if (!Outpost.LookoutCorner(towerOn, out _, out _, out Vector3 outward))
                    {
                        outward = deck - camp.CampCentre;
                        outward.y = 0f;
                        if (outward.sqrMagnitude < 0.01f) outward = towerOn.transform.forward;
                    }
                    Face(outward, dt);
                    return true;
                }
            }
        }

        /// Turn toward a direction, smoothed. Every facing in this file goes
        /// through here, which is what keeps a hand from snapping round when
        /// the thing they are looking at changes.
        void Face(Vector3 dir, float dt)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            // Smoothed, and never faster than a body turns on the spot
            // (`VillagerGaits.TurnInPlace`): a snap round reads as a glide.
            Quaternion want = Quaternion.LookRotation(dir.normalized, Vector3.up);
            Quaternion eased = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-8f * dt));
            transform.rotation = Quaternion.RotateTowards(transform.rotation, eased, VillagerGaits.TurnInPlace * dt);
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
