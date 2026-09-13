using SeaSick.Crew;
using SeaSick.UI;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.Ship
{
    /// Anchoring, shore parties, and harvesting — the player's answer to both
    /// seasickness and an incoming swell. Dropping anchor in heavy water takes
    /// much longer, so the decision to run for shelter has to be made early.
    public class AnchorController : MonoBehaviour
    {
        // Anchoring is immediate — the interesting decision is whether to stop
        // at all, not watching a progress timer tick down.
        [SerializeField] float dropTime = 0f;
        [SerializeField] float weighTime = 0f;
        [Tooltip("How far off the shoreline the ship lies when moored.")]
        [SerializeField] float berthDistance = 11f;
        [Tooltip("How close to her berth she has to be before the dock will take her. About two ship lengths.")]
        [SerializeField] float dockRange = 55f;
        [Tooltip("How far from the shore party real scenery trees are stood up as harvestable.")]
        [SerializeField] float woodReach = 130f;
        [Tooltip("Start the voyage tied up at home, rather than adrift off the beach.")]
        [SerializeField] bool startAtHomeDock = true;
        bool startedDocked;
        [SerializeField] float berthSpeed = 1.6f;
        [SerializeField] float approachSpeedLimit = 6.5f; // must slow down to anchor

        public enum State { Underway, Dropping, Anchored, Ashore, Weighing }
        public State CurrentState { get; private set; } = State.Underway;
        public Island CurrentIsland { get; private set; }

        /// The dock she is lying at, or null if she is anchored off a beach.
        public Dock CurrentDock { get; private set; }

        /// Lying at her own pier. **This is what "home" means to the voyage
        /// now**: an arrival is a berth you took, not a radius you drifted
        /// across. The old test could not be used once she started the game
        /// tied up -- she is already well outside the home island's centre
        /// at the end of a 46 m pier, so a distance check called the voyage
        /// finished on the first frame.
        public bool AtHomeDock => CurrentDock != null && !landingPending
            && (CurrentState == State.Anchored || CurrentState == State.Ashore);

        ShipMotor motor;
        HullIntegrity hull;
        ShipHold hold;
        Gangway gangway;
        CrewAgent[] crew;
        VoyageManager voyage;
        SeaSick.CameraRig.ChaseCamera chaseCam;

        float timer;
        float gatherFraction;
        float repairDebt;
        bool repairing;
        GUIStyle buttonStyle, infoStyle;

        // Every readout on this prompt, cached.
        //
        // IMGUI calls OnGUI once per EVENT — Layout, Repaint, one per mouse
        // move — so the interpolations below ran several times a frame to
        // produce the same sentence. See StatusHUD for the measurement. Each
        // of these rebuilds only when what it SAYS changes: the anchor timers
        // on the tenth of a second they show, the hull on the whole percent,
        // the harvest on the whole unit.
        readonly HudLabel landText = new HudLabel();
        readonly HudLabel timerText = new HudLabel();
        readonly HudLabel repairText = new HudLabel();
        readonly HudLabel statusText = new HudLabel();
        readonly HudLabel deckCargoText = new HudLabel();

        void Start()
        {
            motor = GetComponent<ShipMotor>();
            hull = GetComponent<HullIntegrity>();
            hold = GetComponent<ShipHold>();
            gangway = GetComponent<Gangway>();
            crew = GetComponentsInChildren<CrewAgent>(true);

            // Build on demand rather than trusting the scene: Unity does not
            // guarantee script order, and a sheet that only exists if somebody
            // remembered to add it in the editor is a feature that works on
            // one machine.
            if (GetComponentInChildren<SeaSick.UI.CampSheet>(true) == null)
                gameObject.AddComponent<SeaSick.UI.CampSheet>();
            voyage = FindFirstObjectByType<VoyageManager>();
            chaseCam = FindFirstObjectByType<SeaSick.CameraRig.ChaseCamera>();
        }

        /// Space runs whatever the state's own primary button would run, so
        /// the whole land / cast off / recall cycle is one key. It is only ever
        /// a shortcut to a command already offered on screen — if the button is
        /// not there, or is disabled, the key does nothing.
        void SpacebarCommand()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || !kb.spaceKey.wasPressedThisFrame) return;
            // The home panel owns both the screen and the spacebar while it
            // is up; its "set sail" is the cast-off.
            if (voyage != null && voyage.AtHome) return;

            // With an enemy alongside, space is for the lock, not the anchor.
            // You are far more likely to want to hold them in view than to try
            // to dock in the middle of a fight.
            if (combatLock == null) combatLock = GetComponent<SeaSick.Combat.CombatLock>();
            if (combatLock != null && combatLock.WantsSpace) return;

            switch (CurrentState)
            {
                case State.Underway:
                {
                    // A dock beats a beach. If you have brought her within
                    // reach of her berth, coming alongside is what you meant;
                    // running her up the sand thirty metres away is not.
                    var d = DockInRange();
                    if (d != null && motor.CurrentSpeed <= approachSpeedLimit)
                    {
                        ComeAlongside(d);
                        break;
                    }
                    var isle = IslandInRange();
                    if (isle != null && CanLandHere(isle)
                        && motor.CurrentSpeed <= approachSpeedLimit)
                        Land(isle);
                    break;
                }

                case State.Anchored:
                    // Mid-landing the ship is still coming alongside and the
                    // button is replaced by a status line; don't let the key
                    // cast off out from under it.
                    if (!landingPending) WeighAnchor();
                    break;

                case State.Ashore:
                    // Recall, even when the repair button is holding the
                    // primary slot — getting the crew back is the command that
                    // moves the voyage on.
                    RecallCrew();
                    break;
            }
        }

        /// Kick off the ground survey for whatever island she is standing in
        /// toward. Cheap to call every frame: `BeginSurvey` ignores anything
        /// already surveyed or already running, and the range test is a
        /// distance against the nearest island.
        void SurveyWhatIsNear()
        {
            var isle = IslandInRange();
            if (isle != null && !isle.IsHome)
            {
                Outpost.BeginSurvey(isle, this);
                // Wake the hands who live here. A camp you are standing in
                // front of should have people in it; one three kilometres
                // astern should cost nothing at all.
                var here = Outpost.Of(isle);
                if (here != null) { here.CatchUp(); here.ShowHands(true); }
            }
        }

        Island IslandInRange()
        {
            var isle = Island.Nearest(transform.position);
            if (isle == null) return null;
            float reach = isle.RadiusToward(transform.position) + 30f;
            return Island.FlatDistance(transform.position, isle.transform.position) <= reach
                ? isle : null;
        }

        /// You can only put a boat ashore on a beach — cliff faces drop sheer
        /// into the water, so the approach bearing matters.
        bool CanLandHere(Island isle) => isle != null && isle.HasBeachToward(transform.position);

        /// Her own berth, if she is close enough to take it.
        public Dock DockInRange()
        {
            var d = Dock.Home;
            return d != null && d.DistanceFrom(transform.position) <= dockRange ? d : null;
        }

        /// Take her berth, from outside — what the button and the spacebar
        /// both do. False if she is not near enough or is still carrying too
        /// much way to be taken alongside.
        public bool TryComeAlongside()
        {
            if (CurrentState != State.Underway) return false;
            var d = DockInRange();
            if (d == null || motor.CurrentSpeed > approachSpeedLimit) return false;
            ComeAlongside(d);
            return true;
        }

        void ComeAlongside(Dock d)
        {
            CurrentDock = d;
            CurrentIsland = Island.Nearest(d.Berth);
            motor.Anchored = true;
            CurrentState = State.Anchored;
        }

        /// Put her on her home berth, tied up, from wherever she happens to be.
        ///
        /// ONE place knows how to do this: the spawn path calls it on the
        /// first frame the dock exists, and the Home tab calls it mid-voyage.
        /// Two copies of "how to be berthed" is exactly the divergence
        /// `GetUnderway` had to be written to end — a second copy of a
        /// let-go/tie-up routine has already cost this project a week of
        /// broken landings.
        ///
        /// Returns false with a reason when it would break something rather
        /// than doing it badly. Today that means crew ashore: the plank is
        /// the only way they get back aboard, so moving the hull would leave
        /// them standing on a beach a voyage from home.
        public bool BerthAtHome(out string why)
        {
            why = null;
            var d = Dock.Home;
            if (d == null) { why = "no home dock yet"; return false; }
            if (CurrentState == State.Ashore || landingPending)
            { why = "crew are ashore"; return false; }

            // Let go of wherever she is FIRST. Arriving somewhere new with
            // `CurrentDock` still pointing at the last place is the same
            // fault that made every landing fail, just pointing the other way.
            GetUnderway();

            Vector3 p = d.Berth;
            // The berth is a place on the WATER, and the water moves. Taking
            // her current Y would set her down at whatever height the trough
            // she was sitting in happened to be — which at sea is metres.
            p.y = Ocean.OceanSampler.Ready
                ? Ocean.OceanSampler.SampleImmediate(p).height
                : transform.position.y;

            var rb = GetComponent<Rigidbody>();
            transform.SetPositionAndRotation(p, d.Heading);
            if (rb != null)
            {
                rb.position = p;
                rb.rotation = d.Heading;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            motor.AnchorPoint = p;
            ComeAlongside(d);

            // Ring down stop, or she arrives at her own pier under full
            // ahead. The telegraph re-asserts itself every frame, so the
            // ORDER has to move — zeroing ThrottleOrder here would last
            // exactly until HelmInput's next Update.
            var helm = GetComponent<HelmInput>();
            if (helm != null) helm.AllStop();
            return true;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // Home is where the dock is, so that is where a voyage starts.
            // Done here rather than in a spawner because the dock does not
            // exist until the populator has found the island and built it,
            // which is a frame after anything in Awake could ask.
            if (startAtHomeDock && !startedDocked && Dock.Home != null)
            {
                startedDocked = true;
                BerthAtHome(out _);
            }

            SpacebarCommand();

            // Look at the ground while she is still standing in, not when the
            // anchor bites.
            //
            // The survey is about 800 ms of work on the biggest island. Spread
            // over frames at 4 ms a band that is some three seconds, and the
            // camera only takes 0.7 s to rise -- so starting it at anchor means
            // the answer arrives after the player has already looked at the
            // place. Landing range is the earliest honest moment to ask: she is
            // close enough that this island is the one she means.
            if (CurrentState == State.Underway) SurveyWhatIsNear();

            switch (CurrentState)
            {
                case State.Dropping:
                    timer -= dt;
                    if (timer <= 0f) CurrentState = State.Anchored;
                    break;

                case State.Weighing:
                    timer -= dt;
                    if (timer <= 0f) GetUnderway();
                    break;

                case State.Ashore:
                    Repair(dt);
                    // Top up as they work inland and fell what they were
                    // given; the wood is capped at a few dozen live nodes.
                    restockIn -= dt;
                    if (restockIn <= 0f)
                    {
                        restockIn = 3f;
                        Vector3 at = Vector3.zero; int n = 0;
                        foreach (var c in crew)
                            if (Ours(c) && !c.IsAboard) { at += c.transform.position; n++; }
                        if (n > 0) StockTheWood(at / n);
                    }
                    if (AllAboard()) { CurrentState = State.Anchored; repairing = false; }
                    break;
            }

            MoorAlongside(dt);

            // Finish the landing once the plank is actually down.
            //
            // **The plank going down no longer empties the ship.** Landing put
            // the whole crew ashore automatically, which was right while
            // gathering wood was the only thing you could do on a beach — and
            // is wrong now that the sheet asks WHO goes and a camp is
            // somewhere they might stay. Emptying the deck before the player
            // has been asked takes the decision away and then offers it.
            //
            // The feature is not gone: "send crew ashore" is still one tap in
            // the prompt stack, and that button was always the way to send
            // them back out after a recall. It is an ACTION now rather than a
            // consequence of arriving.
            if (landingPending && CurrentState == State.Anchored
                && gangway != null && gangway.Ready)
            {
                landingPending = false;
            }

            UpdateCameraFocus();
        }

        /// Once anchored, ease the ship in until it's lying alongside the beach
        /// and run the plank out. Without this the crew had a long wade ashore.
        void MoorAlongside(float dt)
        {
            bool moored = CurrentState == State.Anchored || CurrentState == State.Ashore;
            if (!moored || CurrentIsland == null)
            {
                if (gangway != null) gangway.Withdraw();
                motor.MooringHeading = null;
                return;
            }

            // At a pier the berth is a PLACE with a HEADING, not an offset
            // from an island centre. The radial mooring below is right for
            // running her onto a beach and wrong here: it would walk her off
            // the berth toward whatever bearing she happens to lie on and
            // leave her athwart the pier.
            if (CurrentDock != null)
            {
                Vector3 target = CurrentDock.Berth;
                target.y = motor.AnchorPoint.y;
                motor.AnchorPoint = Vector3.Lerp(motor.AnchorPoint, target,
                    1f - Mathf.Exp(-berthSpeed * dt));
                motor.MooringHeading = CurrentDock.Heading.eulerAngles.y;
                // The plank reaches the pier, not the island — until it knows
                // how to do that, it stays inboard rather than stabbing at a
                // beach thirty metres away.
                if (gangway != null) gangway.Withdraw();
                return;
            }
            motor.MooringHeading = null;

            Vector3 c = CurrentIsland.transform.position;
            Vector3 out2 = transform.position - c;
            out2.y = 0f;
            if (out2.sqrMagnitude < 0.01f) return;
            float bearing = Mathf.Atan2(out2.x, out2.z);
            float shore = CurrentIsland.RadiusAt(bearing);

            Vector3 berth = c + out2.normalized * (shore + berthDistance);
            berth.y = motor.AnchorPoint.y;
            // Walk the anchor spring's target rather than the transform: the
            // rigidbody does the moving, so berthing can't fight the physics.
            motor.AnchorPoint = Vector3.Lerp(motor.AnchorPoint, berth,
                1f - Mathf.Exp(-berthSpeed * dt));

            if (gangway != null) gangway.Extend(CurrentIsland);
        }

        /// While the crew are ashore, pull the camera back to frame them —
        /// otherwise they wander out of shot and the player misses the work.
        void UpdateCameraFocus()
        {
            if (chaseCam == null) return;

            // Lying at the dock is the one moment the player is not steering,
            // so it is the one moment the camera can leave the water and show
            // them what they came home to.
            bool atDock = CurrentDock != null
                && (CurrentState == State.Anchored || CurrentState == State.Ashore)
                && CurrentDock.DistanceFrom(transform.position) < dockRange;
            if (atDock && CurrentIsland != null)
            {
                // The shot Kevin flew to, expressed in the DOCK's own frame
                // so it is the same shot at any pier rather than one
                // island's coordinates.
                //
                // Fitted from his readout, and the fit is what makes it
                // meaningful: the camera sits essentially straight out to sea
                // from the pier -- 4.8 degrees off its seaward bearing --
                // looking back down the pier at the land, with the frame
                // centred 41 m inland of the pier root and 22 m to starboard.
                // So the pier runs INTO the shot from the near edge with the
                // ship on it, and the village lies beyond.
                //
                // My own derivation put the camera INLAND looking seaward,
                // which is 136 degrees away and puts the pier behind the
                // lens. Both readings are "the village in front, the sea
                // behind"; only one of them is the picture he wanted, and no
                // amount of reasoning was going to pick it.
                // The framing itself lives on the Dock now: the village is
                // sited against the same two numbers, so a second copy of
                // them here would be a gate that stops gating the moment one
                // is retuned.
                var village = CurrentIsland.GetComponent<Settlement>();

                // Only the fallback for a dock with no settlement measured;
                // the zoom itself is ChaseCamera's, in metres of ground.
                float reach = village != null ? village.ViewRadius : 120f;

                chaseCam.Overview = new SeaSick.CameraRig.ChaseCamera.IslandShot
                {
                    centre = CurrentDock.ViewCentre,
                    radius = reach,
                    from = CurrentDock.ViewFrom,
                };
            }
            else if (CurrentIsland != null && !CurrentIsland.IsHome
                     && (CurrentState == State.Anchored || CurrentState == State.Ashore))
            {
                // **The bird's-eye at any island.** Home gets the shot Kevin
                // flew to, composed against its pier; a camp has no pier, so
                // the vantage is taken from where the ship actually is —
                // seaward of the ground, looking inland past her. That is the
                // same composition as home (the water in the near edge with
                // her on it, the land beyond) without a second set of authored
                // constants to drift out of step.
                var outpost = Outpost.Of(CurrentIsland);
                Vector3 aim = outpost != null && outpost.Sited
                    ? outpost.ClearingCentre
                    : CurrentIsland.transform.position;

                Vector3 seaward = transform.position - aim;
                seaward.y = 0f;

                // What has to fit. The settlement's own ViewRadius when the
                // ground has been surveyed -- the same number home frames by --
                // and the island otherwise.
                var settle = CurrentIsland.GetComponent<Settlement>();
                float reach = settle != null ? settle.ViewRadius
                                             : Mathf.Max(70f, CurrentIsland.Radius);

                chaseCam.Overview = new SeaSick.CameraRig.ChaseCamera.IslandShot
                {
                    centre = aim,
                    radius = reach,
                    from = seaward,
                };
            }
            else chaseCam.Overview = null;

            if (CurrentState != State.Ashore) { chaseCam.PointOfInterest = null; return; }

            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var c in crew)
                if (Ours(c) && !c.IsAboard) { sum += c.transform.position; n++; }

            chaseCam.PointOfInterest = n > 0
                ? sum / n
                : (CurrentIsland != null ? CurrentIsland.transform.position : (Vector3?)null);
        }

        void Repair(float dt)
        {
            if (!repairing || hull == null || voyage == null) return;
            if (!hull.NeedsRepair) { repairing = false; return; }

            float timberOnHand = voyage.AmountOf("Timber") - repairDebt;
            float used = hull.RepairStep(dt, timberOnHand);
            if (used <= 0f) { repairing = false; return; }

            repairDebt += used;
            while (repairDebt >= 1f)
            {
                if (!voyage.TryConsume("Timber", 1)) { repairing = false; break; }
                repairDebt -= 1f;
            }
        }

        // Harvesting is no longer a rate drained from the island — each crew
        // member walks to a tree, works it, and carries the log back
        // themselves (see CrewAgent).

        /// Is the whole SHIP'S COMPANY back aboard?
        ///
        /// **Only hands that still belong to this ship count.** A hand left at
        /// a camp is parked under the island and switched off, and its state
        /// machine is frozen wherever it stopped — so a cached array that
        /// still holds it answers "not aboard" for ever, and she can never
        /// weigh anchor again. Measured: two hands left at a camp and the
        /// ship was stuck `Ashore` permanently.
        ///
        /// Asking the transform rather than re-caching, because this is called
        /// every frame in `Ashore` and a hand can change hands mid-frame.
        bool AllAboard()
        {
            foreach (var c in crew)
            {
                if (c == null) continue;
                if (!Ours(c)) continue;
                if (!c.IsAboard) return false;
            }
            return true;
        }

        /// Still one of ours: alive, switched on, and under this ship.
        bool Ours(CrewAgent c)
            => c != null && c.gameObject.activeInHierarchy
               && (c.transform.IsChildOf(transform) || c.IsAshore);

        // --- Player actions -------------------------------------------------

        bool landingPending;
        SeaSick.Combat.CombatLock combatLock;

        /// One press to put a shore party on an island: anchor, warp in
        /// alongside, run the plank out and send the crew down it. Splitting
        /// this into "anchor" then "go ashore" was two taps for one intention.
        /// Put a shore party on an island, from outside -- what the button
        /// and the spacebar both do. False with the reason left in `why`, so
        /// a probe can tell "no prompt was offered" from "the prompt was
        /// there and refused", which look identical from the deck.
        public bool TryLand(out string why)
        {
            if (CurrentState != State.Underway) { why = $"not underway ({CurrentState})"; return false; }
            var isle = IslandInRange();
            if (isle == null) { why = "no island in range"; return false; }
            if (!CanLandHere(isle)) { why = $"{isle.name}: sheer cliff on this bearing"; return false; }
            if (motor.CurrentSpeed > approachSpeedLimit)
            { why = $"too fast ({motor.CurrentSpeed:F1} > {approachSpeedLimit})"; return false; }
            Land(isle);
            why = isle.name;
            return true;
        }

        void Land(Island isle)
        {
            DropAnchor(isle);
            landingPending = true;
        }

        void DropAnchor(Island isle)
        {
            CurrentIsland = isle;

            // Survey the ground the first time she anchors here.
            //
            // Lazy on purpose: the survey rasterises the island and searches
            // it, and paying that for thirty islands at world build would
            // charge every launch for places most players never land on.
            //
            // And spread over frames, not taken in one: about 800 ms of work
            // on the biggest island, which is fifty frames, not one.
            //
            // Usually a no-op by now -- `SurveyWhatIsNear` started this while
            // she was still standing in. Kept because anchoring is the moment
            // the answer is definitely needed, and a ship that arrives by some
            // other path (a dev warp, a respawn) never passed through the
            // approach. Null is a real answer: some ground will not take a
            // settlement.
            if (isle != null && !isle.IsHome)
            {
                Outpost.BeginSurvey(isle, this);
                // Wake the hands who live here. A camp you are standing in
                // front of should have people in it; one three kilometres
                // astern should cost nothing at all.
                var here = Outpost.Of(isle);
                if (here != null) { here.CatchUp(); here.ShowHands(true); }
            }
            motor.Anchored = true;
            timer = dropTime;
            CurrentState = timer > 0f ? State.Dropping : State.Anchored;
        }

        void SendAshore()
        {
            if (CurrentIsland == null) return;
            // Land them at the foot of the plank, then they find their own work.
            Vector3 landing = gangway != null && gangway.Ready
                ? gangway.LandingPoint
                : CurrentIsland.ShorePoint(0, 1, transform.position);

            // Stand harvest nodes on the real trees around the landing, so
            // the crew cut the wood that is actually drawn rather than the
            // handful of prop trees. 1.9% of an island's trees used to be
            // cuttable; see SceneryWood.
            StockTheWood(landing);

            for (int i = 0; i < crew.Length; i++)
            {
                if (!Ours(crew[i])) continue;   // a camp's own hands stay put
                Vector3 spread = transform.right * ((i - (crew.Length - 1) * 0.5f) * 2.2f);
                crew[i].GoAshore(landing + spread, CurrentIsland, hold, gangway, voyage);
            }
            CurrentState = State.Ashore;
        }

        float restockIn;

        /// Materialise harvest nodes on the scenery trees around a point.
        void StockTheWood(Vector3 near)
        {
            if (CurrentIsland == null) return;
            var wood = CurrentIsland.GetComponentInChildren<SeaSick.Terrain.SceneryWood>();
            if (wood != null) wood.Populate(near, woodReach);
        }

        void RecallCrew()
        {
            // Parked camp hands are not ours to recall -- they live there now.
            foreach (var c in crew) if (Ours(c)) c.ReturnAboard();
        }

        /// Let go and get her underway, from outside. The home panel's
        /// "set sail" runs this so one button both closes the tally and
        /// casts off -- splitting it in two is the same mistake `Land`
        /// already fixed in the other direction.
        /// Put the camp's own hands away again. They keep working -- the
        /// ledger is what produces -- but nothing needs drawing on an island
        /// the ship has left.
        void StowCampHands()
        {
            if (CurrentIsland == null || CurrentIsland.IsHome) return;
            var here = Outpost.Of(CurrentIsland);
            if (here == null) return;
            here.CatchUp();          // settle the books before we stop looking
            here.ShowHands(false);
        }

        public void CastOff()
        {
            if (CurrentState == State.Anchored && !landingPending) WeighAnchor();
        }

        void WeighAnchor()
        {
            timer = weighTime;
            if (timer > 0f) { CurrentState = State.Weighing; return; }
            GetUnderway();
        }

        /// Everything she has to let go of, in ONE place.
        ///
        /// It was two, and they had drifted. `weighTime` is 0, so weighing
        /// takes the instant path every single time and the timed branch in
        /// `Update` — the one that also cleared `CurrentDock` and the forced
        /// mooring heading — was dead code. So **casting off from the pier
        /// left `CurrentDock` pointing at home for the rest of the voyage**,
        /// and `MoorAlongside` takes its pier branch whenever that is set:
        /// the plank stayed inboard at every island the crew ever landed on,
        /// `landingPending` never resolved, nobody went ashore, and the
        /// anchor spring was quietly easing her back toward the home berth
        /// from a thousand metres away. Every voyage now starts tied up, so
        /// this was on the only path there is.
        void GetUnderway()
        {
            // Before `CurrentIsland` is cleared -- this is the last moment
            // anything knows which camp she is leaving.
            StowCampHands();
            motor.Anchored = false;
            motor.MooringHeading = null;
            CurrentIsland = null;
            CurrentDock = null;
            landingPending = false;
            CurrentState = State.Underway;
        }

        // --- UI -------------------------------------------------------------

        /// The greed switch. Off, the crew fill her to the marked line and
        /// stop. On, they keep piling it on deck. Deliberately only reachable
        /// while anchored: overloading is a decision you make in harbour, and
        /// then have to live with all the way home.
        ///
        /// It takes a row off the prompt stack rather than offsetting itself
        /// from the button above it. It used to sit at `secondary.y + 2.3u`
        /// with a height of 1.8u, and the button below it started at
        /// `secondary.y + 3.2u` — so the toggle covered the top 0.9u of "cast
        /// off", on a screen where the next thing you do is cast off.
        void DrawDeckCargoToggle(ref Prompts.Stack stack, int u,
            GUIStyle buttonStyle, GUIStyle infoStyle)
        {
            if (voyage == null) return;

            var r = stack.Next(u * 1.8f);
            UIBlocker.Block(r);

            var style = voyage.TakeDeckCargo ? UITheme.ButtonPressed : buttonStyle;
            if (deckCargoText.Changed(HudLabel.Key(voyage.TakeDeckCargo ? 1 : 0,
                                                   voyage.MaxHold, voyage.HoldCapacity)))
                deckCargoText.Set(voyage.TakeDeckCargo
                    ? $"◉  deck cargo — to {voyage.MaxHold}"
                    : $"◎  deck cargo — stop at {voyage.HoldCapacity}");
            if (GUI.Button(r, deckCargoText.Content, style)) voyage.TakeDeckCargo = !voyage.TakeDeckCargo;

            if (voyage.TakeDeckCargo)
                GUI.Label(stack.Next(u * 1.6f),
                    "she'll swim low and take water", infoStyle);
        }

        /// The one contextual prompt, bottom centre, through `Prompts`.
        ///
        /// It used to place its own buttons at `h − 4.2u − bh` and hope. At
        /// the shipping portrait aspect that put "come alongside" 34 px inside
        /// `CombatLock`'s "space · lock on" — two live buttons overlapping, one
        /// of which puts the ship somewhere. Position is no longer this
        /// class's to choose: it bids a priority and draws in the rows it is
        /// handed.
        void OnGUI()
        {
            // Two panels offering to cast off in the same corner of the
            // screen is a choice nobody wants to make -- the same rule the
            // dock prompt already applies against the beach one.
            if (voyage != null && voyage.AtHome) return;

            // What is in reach is worked out BEFORE bidding. Underway with
            // open water all round this controller has nothing to say, and a
            // claim it never draws in would silently mute the combat lock and
            // the jettison button behind it.
            Dock dock = null;
            Island isle = null;
            if (CurrentState == State.Underway)
            {
                dock = DockInRange();
                if (dock == null)
                {
                    isle = IslandInRange();
                    if (isle == null) return;
                }
            }

            // Bid on EVERY event, not only Repaint: a caller that bids on
            // repaint alone owns the slot on repaint frames and has lost it by
            // the mouse-up that would have pressed its own button.
            if (!Prompts.Claim(Prompts.Rank.Anchor)) return;

            buttonStyle = UITheme.Button;
            infoStyle = UITheme.Small2Centered;

            int u = HudLayout.Unit;
            float bh = u * 2.7f;
            // Rows stack upward, so the first row asked for is the one nearest
            // the thumb. The action you take most often gets it.
            var stack = Prompts.Begin();

            switch (CurrentState)
            {
                case State.Underway:
                {
                    var primary = stack.Next(bh);
                    // The dock's own prompt, which replaces the beach one
                    // rather than sitting beside it -- two ways to stop in
                    // the same thirty metres is a choice nobody wants to make.
                    if (dock != null)
                    {
                        bool slow = motor.CurrentSpeed <= approachSpeedLimit;
                        UIBlocker.Block(primary);
                        GUI.enabled = slow;
                        if (GUI.Button(primary, slow
                                ? "⚓  Come alongside   (space)"
                                : "slow down to come alongside  (S)", buttonStyle))
                            ComeAlongside(dock);
                        GUI.enabled = true;
                        return;
                    }

                    bool beach = CanLandHere(isle);
                    bool slowEnough = motor.CurrentSpeed <= approachSpeedLimit;
                    // Three of the four readings are literals; only the named
                    // resource has to be built, and only when the island under
                    // the bow changes.
                    bool res = isle.HasResources;
                    if (landText.Changed(HudLabel.Key(beach ? 1 : 0, slowEnough ? 1 : 0,
                            res ? 1 : 0, res && isle.ResourceName != null
                                         ? isle.ResourceName.GetHashCode() : 0)))
                        landText.Set(!beach
                            ? "sheer cliff — find a beach"
                            : slowEnough
                                ? (res
                                    ? $"⚓  Land here — {isle.ResourceName}   (space)"
                                    : "⚓  Land here — rest   (space)")
                                : "slow down to land  (S)");
                    UIBlocker.Block(primary);
                    GUI.enabled = slowEnough && beach;
                    if (GUI.Button(primary, landText.Content, buttonStyle)) Land(isle);
                    GUI.enabled = true;
                    break;
                }

                // One tenth of a second is what these show, so that — not the
                // float — is the key: ten strings a second instead of one per
                // event.
                case State.Dropping:
                    if (timerText.Changed(HudLabel.Key(0, Mathf.RoundToInt(timer * 10f))))
                        timerText.Set($"dropping anchor…  {timer:F1}s");
                    GUI.Label(stack.Next(bh), timerText.Content, infoStyle);
                    break;

                case State.Weighing:
                    if (timerText.Changed(HudLabel.Key(1, Mathf.RoundToInt(timer * 10f))))
                        timerText.Set($"weighing anchor…  {timer:F1}s");
                    GUI.Label(stack.Next(bh), timerText.Content, infoStyle);
                    break;

                case State.Anchored:
                {
                    if (landingPending)
                    {
                        GUI.Label(stack.Next(bh), "coming alongside…", infoStyle);
                        break;
                    }
                    // Crew are back aboard: cast off, or put them ashore again.
                    var primary = stack.Next(bh);
                    UIBlocker.Block(primary);
                    if (GUI.Button(primary, "⚓  Cast off   (space)", buttonStyle)) WeighAnchor();

                    if (CurrentIsland != null && CurrentIsland.HasResources)
                    {
                        var secondary = stack.Next(bh);
                        UIBlocker.Block(secondary);
                        if (GUI.Button(secondary, "send crew ashore", buttonStyle)) SendAshore();
                    }

                    // The camp's own controls live in `CampSheet`, which owns
                    // the lower third while she is lying at an island. Two
                    // places offering to make the same camp is the duplication
                    // the prompt slot exists to prevent.
                    DrawDeckCargoToggle(ref stack, u, buttonStyle, infoStyle);
                    break;
                }

                case State.Ashore:
                {
                    // Recall is always the bottom row, in every state that
                    // offers it. A button that moves depending on whether the
                    // hull happens to need timber is a button you have to read
                    // before pressing.
                    var primary = stack.Next(bh);
                    UIBlocker.Block(primary);
                    if (GUI.Button(primary, "recall crew aboard   (space)", buttonStyle)) RecallCrew();

                    bool canRepair = hull != null && hull.NeedsRepair && voyage != null
                        && voyage.AmountOf("Timber") > 0;
                    if (canRepair || repairing)
                    {
                        var secondary = stack.Next(bh);
                        UIBlocker.Block(secondary);
                        // Whole percent is what P0 prints; the float under it
                        // moves every frame a plank goes on.
                        if (repairText.Changed(HudLabel.Key(repairing ? 1 : 0,
                                Mathf.RoundToInt(hull.Integrity01 * 100f))))
                            repairText.Set(repairing
                                ? $"stop repairs — hull {hull.Integrity01:P0}"
                                : $"repair hull ({hull.Integrity01:P0}) — uses timber");
                        if (GUI.Button(secondary, repairText.Content, buttonStyle)) repairing = !repairing;
                    }

                    DrawDeckCargoToggle(ref stack, u, buttonStyle, infoStyle);

                    // What is left reads as a whole unit, so it only needs a
                    // new string when a unit actually comes out of the ground
                    // — not on every event while the crew work.
                    bool harvesting = CurrentIsland != null && CurrentIsland.HasResources;
                    if (statusText.Changed(HudLabel.Key(harvesting ? 1 : 0, repairing ? 1 : 0,
                            harvesting ? Mathf.RoundToInt(CurrentIsland.Remaining) : 0,
                            harvesting && CurrentIsland.ResourceName != null
                                ? CurrentIsland.ResourceName.GetHashCode() : 0)))
                    {
                        string status = harvesting
                            ? $"harvesting {CurrentIsland.ResourceName} — {CurrentIsland.Remaining:F0} left"
                            : "the crew rests on solid ground";
                        if (repairing) status += "   ·   repairing hull";
                        statusText.Set(status);
                    }
                    GUI.Label(stack.Next(u * 1.8f), statusText.Content, infoStyle);
                    break;
                }
            }
        }
    }
}
