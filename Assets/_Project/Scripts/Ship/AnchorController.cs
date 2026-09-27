using SeaSick.Crew;
using SeaSick.UI;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;
using SheetsHud = global::SeaSick.UI.Sheets.Sheets;

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
        [Tooltip("Water under the hull, metres, within which she counts as off a shore and may anchor. The whole shoreline, not a radius from the island's centre.")]
        [SerializeField] float landingDepth = 12f;
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

        // --- T-berth (2026-09-26) ---------------------------------------
        // Where she is actually easing to and turning to, for THIS
        // berthing -- decided once, in `ComeAlongside`/`BerthAtHome`, from
        // her live beam and her heading on the approach. `Dock.Berth`/
        // `Heading` stay the deterministic default for everyone who has
        // neither (a probe, a save's display point); `MoorAlongside` reads
        // these instead, because the point of asking the approach at all is
        // to hold the answer steady while the spring hauls her in, not to
        // re-ask it every frame and risk it flipping under her.
        Vector3 berthPos;
        Quaternion berthHeading = Quaternion.identity;
        Shipyard yard;

        /// Her beam right now: the live hull's, if a `Shipyard` says so;
        /// otherwise the same default `HarbourSite`/`Pier` assume when they
        /// site a berth with no ship built yet.
        float BerthBeam => yard != null && yard.Node != null
            ? yard.Node.beam : SeaSick.World.WorldScale.ShipBeam;

        /// Lying at her own pier. **This is what "home" means to the voyage
        /// now**: an arrival is a berth you took, not a radius you drifted
        /// across. The old test could not be used once she started the game
        /// tied up -- she is already well outside the home island's centre
        /// at the end of a 46 m pier, so a distance check called the voyage
        /// finished on the first frame.
        public bool AtHomeDock => CurrentDock != null && CurrentDock.IsHome && !landingPending
            && (CurrentState == State.Anchored || CurrentState == State.Ashore);

        /// Has the spawn-time berthing had its frame? A load has to wait for
        /// it, or `BerthAtHome` fires a frame after the ship was put back
        /// where the save left her.
        public bool StartedDocked => startedDocked || !startAtHomeDock;

        /// Anchor her off this island from outside, as a load does after it
        /// has stood her where the save left her. Under way only; the timed
        /// drop and the survey run exactly as they would for the button.
        public bool MoorAt(Island isle)
        {
            if (isle == null || CurrentState != State.Underway) return false;
            DropAnchor(isle);
            return true;
        }

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
        /// The sheet HUD's door onto the repair toggle the prompt button flips.
        public bool Repairing => repairing;
        public void ToggleRepair() => repairing = !repairing;
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
        readonly HudLabel partyText = new HudLabel();

        void Start()
        {
            motor = GetComponent<ShipMotor>();
            hull = GetComponent<HullIntegrity>();
            hold = GetComponent<ShipHold>();
            gangway = GetComponent<Gangway>();
            crew = GetComponentsInChildren<CrewAgent>(true);
            yard = GetComponent<Shipyard>();

            // Build on demand rather than trusting the scene: Unity does not
            // guarantee script order, and a component that only exists if
            // somebody remembered to add it in the editor is a feature that
            // works on one machine. Siting mode (the ghost and its ✓ ✕ ↻),
            // the loader's coroutine host and the camp toasts all ride on the
            // ship and carry no serialised state.
            if (CampSiting.Instance == null) gameObject.AddComponent<CampSiting>();
            if (GetComponent<CampLoading>() == null) gameObject.AddComponent<CampLoading>();
            if (GetComponent<CampToasts>() == null) gameObject.AddComponent<CampToasts>();
            islandCam = GetComponent<SeaSick.CameraRig.IslandCam>();
            if (islandCam == null) islandCam = gameObject.AddComponent<SeaSick.CameraRig.IslandCam>();
            voyage = FindFirstObjectByType<VoyageManager>();
            chaseCam = FindFirstObjectByType<SeaSick.CameraRig.ChaseCamera>();
        }

        /// Space runs whatever the state's own primary button would run, so
        /// the whole land / cast off / recall cycle is one key. It is only ever
        /// a shortcut to a command already offered on screen — if the button is
        /// not there, or is disabled, the key does nothing.
        void SpacebarCommand()
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
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
            // Kevin, 2026-09-22: no island is home any more -- the starting
            // island surveys and wakes its hands like any other.
            if (isle != null)
            {
                Outpost.BeginSurvey(isle, this);
                // Wake the hands who live here. A camp you are standing in
                // front of should have people in it; one three kilometres
                // astern should cost nothing at all.
                var here = Outpost.Of(isle);
                if (here != null) { here.CatchUp(); here.ShowHands(true); }
            }
        }

        SeaSick.CameraRig.IslandCam islandCam;

        /// What the view is doing, for the HUD. Null when the overview is not up.
        public string ViewReadout =>
            islandCam != null && chaseCam != null && chaseCam.Overview.HasValue
                ? islandCam.Readout : null;

        /// **Off a shore, by the water under her -- not by a radius.**
        ///
        /// This used to be `RadiusToward + 30 m` from the island's CENTRE:
        /// the same 46-sector radial profile `HullIntegrity` retired for
        /// being up to 934 m out on a lobed island. Landing therefore
        /// needed her run onto the sand on some bearings and was offered
        /// in deep water on others, and Kevin could not land without
        /// beaching. Now she is "in range" wherever the shore depth field
        /// says there is less than `landingDepth` of water under the hull:
        /// the same grid the ocean shoals on and the hull grounds on, so
        /// the prompt appears where the beach is. `HasBeachToward` still
        /// refuses a cliff. The old radius stays only as the fallback for a
        /// scene with no field.
        Island IslandInRange()
        {
            var isle = Island.Nearest(transform.position);
            if (isle == null) return null;
            float depth = WaterDepthUnder(transform.position);
            if (!float.IsNaN(depth)) return depth <= landingDepth ? isle : null;
            float reach = isle.RadiusToward(transform.position) + 30f;
            return Island.FlatDistance(transform.position, isle.transform.position) <= reach
                ? isle : null;
        }

        /// Metres of water under `p` at mean level, from the shore grid where
        /// it reaches and the exact field elsewhere; NaN with neither.
        static float WaterDepthUnder(Vector3 p)
        {
            var rf = Ocean.RegionField.Instance;
            if (rf != null && rf.ShoreN > 0)
            {
                var shore = rf.Shore;
                if (shore.IsCreated && shore.Length >= rf.ShoreN * rf.ShoreN)
                {
                    float d = rf.Params.ShoreWetDepth(
                        new Unity.Mathematics.float2(p.x, p.z), shore).z;
                    if (d < 1e8f) return d;
                }
            }
            if (Island.TerrainHeight != null) return -Island.TerrainHeight(p.x, p.z);
            return float.NaN;
        }

        /// You can only put a boat ashore on a beach — cliff faces drop sheer
        /// into the water, so the approach bearing matters.
        bool CanLandHere(Island isle) => isle != null && isle.HasBeachToward(transform.position);

        /// The nearest berth, home's or a camp pier's, if she is close
        /// enough to take it.
        public Dock DockInRange()
        {
            var d = Dock.Nearest(transform.position);
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
            // Decide the berth ONCE, from what is known right now: her live
            // beam, and her heading on the approach (or, coming through
            // `BerthAtHome`, whatever heading was just set for the
            // deterministic teleport -- `HeadingFor` then just confirms it).
            Vector3 approach = transform.forward; approach.y = 0f;
            berthPos = d.BerthFor(BerthBeam);
            berthHeading = d.HeadingFor(approach);

            CurrentDock = d;
            CurrentIsland = Island.Nearest(d.Berth);
            // A camp's pier is a landing like any beach: the ground gets
            // surveyed and the hands who live here wake up.
            // Kevin, 2026-09-22: no island is home any more -- the starting
            // island wakes its hands too.
            if (CurrentIsland != null)
            {
                Outpost.BeginSurvey(CurrentIsland, this);
                var here = Outpost.Of(CurrentIsland);
                if (here != null) { here.CatchUp(); here.ShowHands(true); }
            }
            motor.Anchored = true;
            CurrentState = State.Anchored;
            SeaSick.Save.SaveGame.Autosave(d.IsHome ? "alongside at home"
                : "alongside at " + (CurrentIsland != null ? CurrentIsland.name : "a pier"));
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

            // No approach to judge here -- she is being SET DOWN, not
            // sailed in -- so this is `Dock`'s deterministic default
            // heading, and `BerthFor` takes her live beam if a Shipyard can
            // give one.
            Vector3 p = d.BerthFor(BerthBeam);
            var heading = d.HeadingFor(Vector3.zero);
            // The berth is a place on the WATER, and the water moves. Taking
            // her current Y would set her down at whatever height the trough
            // she was sitting in happened to be — which at sea is metres.
            p.y = Ocean.OceanSampler.Ready
                ? Ocean.OceanSampler.SampleImmediate(p).height
                : transform.position.y;

            var rb = GetComponent<Rigidbody>();
            transform.SetPositionAndRotation(p, heading);
            if (rb != null)
            {
                rb.position = p;
                rb.rotation = heading;
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
            //
            // **Not under a load** (2026-09-26): a Continue/Load reload starts
            // the restore in `GameBoot.Awake`, before this ever runs, and the
            // restore puts her where the save left her. Berthing her at the
            // harbour first was one of the "four locations" Kevin watched
            // her flick through -- origin, harbour, saved spot, berth. Count
            // the spawn berth as had so `SaveGame.Restore` stops waiting on it.
            if (startAtHomeDock && !startedDocked && SeaSick.Save.SaveGame.Restoring)
                startedDocked = true;
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

            // **The outpost may not have existed when she arrived.** The
            // survey runs for seconds after the anchor is down -- measured at
            // 7.9 s on Island_1 -- and `SurveyWhatIsNear` only runs while she
            // is UNDER WAY, so on a first visit nothing was left to wake the
            // camp's hands and `Watched` stayed false for as long as she lay
            // there. Anybody stationed then was switched off and never drawn.
            // That is the same fault Kevin reported, one door further in.
            if (CurrentState == State.Anchored || CurrentState == State.Ashore)
            {
                // Kevin, 2026-09-22: no island is home any more -- the
                // starting island's camp gets watched too.
                var camp = CurrentIsland != null
                    ? Outpost.Of(CurrentIsland) : null;
                if (camp != null && !camp.Watched) { camp.CatchUp(); camp.ShowHands(true); }
            }

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
                // `berthPos`/`berthHeading` -- decided once in
                // `ComeAlongside`/`BerthAtHome` from her live beam and her
                // approach -- not `CurrentDock.Berth`/`Heading` (the
                // deterministic default): re-asking the dock every frame
                // would answer a beam that may have changed since (a
                // refit) and a heading that never looked at how she
                // actually arrived.
                Vector3 target = berthPos;
                target.y = motor.AnchorPoint.y;
                motor.AnchorPoint = Vector3.Lerp(motor.AnchorPoint, target,
                    1f - Mathf.Exp(-berthSpeed * dt));
                motor.MooringHeading = berthHeading.eulerAngles.y;
                // At a camp's pier the plank crosses from her side onto the
                // pier HEAD -- a short hop, not the length of the pier --
                // and the shore party walks the pier the rest of the way
                // (see `SendAshore`). At home it stays inboard as before:
                // the home pier has its own arrival and the plank was never
                // part of it.
                if (gangway != null)
                {
                    if (CurrentDock.IsHome) gangway.Withdraw();
                    else gangway.ExtendTo(CurrentDock.Head);
                }
                return;
            }
            motor.MooringHeading = null;

            // **Off a beach she lies where she stopped.** This used to walk
            // the anchor point to `RadiusAt(bearing) + 11 m` from the
            // island's centre -- a berth on the radial profile, which is
            // metres to hundreds of metres off the real waterline, so the
            // spring dragged her onto the sand or out to sea after the
            // player had already chosen where to stop. `ShipMotor.Anchored`
            // took the anchor point where she was when it was set; that is
            // the berth.
            if (gangway != null) gangway.Extend(CurrentIsland);
        }

        /// **The island whose siting ring the overview has already been
        /// seated on.** The latch, not a measurement: see `UpdateCameraFocus`.
        Island ringFramedAt;

        /// How much wider than the siting ring to frame it. 1.15 leaves the
        /// ring at 87 % of the narrow axis — clear of the edges, without
        /// backing off so far that the shore stops reading.
        const float ringMargin = 1.15f;

        /// Metres round the ship a campless landing frames (2026-09-23).
        /// Close enough to see the crew come down the gangway; the player
        /// pans inland from there to choose the town centre.
        const float LandingFrameRadius = 30f;

        /// While the crew are ashore, pull the camera back to frame them —
        /// otherwise they wander out of shot and the player misses the work.
        void UpdateCameraFocus()
        {
            if (chaseCam == null) return;

            // Lying at the dock is the one moment the player is not steering,
            // so it is the one moment the camera can leave the water and show
            // them what they came home to.
            // The composed shot is home's; a camp's pier takes the camp's
            // bird's-eye below, like any landing there.
            //
            // **The BERTH decides this, not a live distance.** Kevin,
            // 2026-09-22 on the phone: arriving flipped the view back and
            // forth between two framings about nine times before it settled.
            // This test was the flip. `DockInRange`/`TryComeAlongside` accept
            // the berth at `DistanceFrom <= dockRange`, so the instant
            // `CurrentDock` is set she is sitting ON the 55 m boundary this
            // line then re-tests every frame -- and she is a rigidbody on an
            // FFT ocean being hauled in on a spring (`MoorAlongside` eases
            // `motor.AnchorPoint`, the hull lags it, the swell moves her
            // metres). So the boolean chattered for the second or two it took
            // the spring to win, and each chatter swapped the overview between
            // the pier's composed shot and the seaward shot below it -- two
            // compositions the code's own note puts 136 degrees apart. No
            // blend can hide that, because both branches hand the rig a
            // finished seat and aim.
            //
            // There is nothing for the distance to protect: `CurrentDock` is
            // written in exactly one place (`ComeAlongside`, which already
            // made the range test) and cleared in exactly one
            // (`GetUnderway`), and while it is set `MoorAlongside` is walking
            // her onto the berth rather than off it. The distance could only
            // ever fall, so re-asking it could only ever produce a false
            // negative -- which is what it did. Dropping it makes the flip
            // impossible by construction: the view now changes only when the
            // ship takes a berth or lets go of one, which are events, not
            // measurements.
            bool atDock = CurrentDock != null && CurrentDock.IsHome
                && (CurrentState == State.Anchored || CurrentState == State.Ashore);
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

                var homeShot = new SeaSick.CameraRig.ChaseCamera.IslandShot
                {
                    centre = CurrentDock.ViewCentre,
                    radius = reach,
                    from = CurrentDock.ViewFrom,
                };
                if (islandCam != null)
                {
                    islandCam.Focus(CurrentIsland);
                    homeShot = islandCam.Apply(homeShot);
                }
                chaseCam.Overview = homeShot;
            }
            else if (CurrentIsland != null
                     && (CurrentState == State.Anchored || CurrentState == State.Ashore))
            {
                // Kevin, 2026-09-22: no island is home any more -- anchored
                // off the starting island frames like any camp; only the pier
                // keeps its composed shot above.
                // **The bird's-eye at any island.** Home gets the shot Kevin
                // flew to, composed against its pier; a camp has no pier, so
                // the vantage is taken from where the ship actually is —
                // seaward of the ground, looking inland past her. That is the
                // same composition as home (the water in the near edge with
                // her on it, the land beyond) without a second set of authored
                // constants to drift out of step.
                var outpost = Outpost.Of(CurrentIsland);

                // **A campless island is framed on the SITING RING, not on the
                // island.**
                //
                // Kevin on the phone, 2026-09-22: "when landing on an island
                // the camera pans to a spot in the middle of the island
                // instead of the radius where I can place the initial
                // campfire." He is describing two rules that were never made
                // to agree. This shot aimed at `ClearingCentre` (the survey's
                // clearing, inland) or at the island's own centre; the only
                // ground that will take the first campfire is
                // `CampSiting`'s ring, and with no camp standing that ring is
                // drawn around the SHIP -- which is at the shore, a whole
                // island radius away from where the camera was looking. On a
                // portrait screen the ring was frequently not in the frame at
                // all, so the player was shown a place they could not build
                // and no sight of the place they could.
                //
                // The centre is asked of `CampSiting` rather than restated
                // here, so the circle that is drawn and the circle that is
                // framed cannot drift apart.
                bool camp = outpost != null && (outpost.HasCamp || outpost.Building);
                Vector3 ringAt = SeaSick.UI.CampSiting.RingCentre(outpost, transform);
                float ringR = SeaSick.UI.CampSiting.RingRadius;

                // **2026-09-23, Kevin on the phone: "when debarking it zooms
                // out to reveal essentially the entire island. this is too far
                // zoomed back."** The ring above is gone as a RULE (the town
                // centre can go anywhere since today), so framing all 80 m of
                // it -- ~400 m of ground on a portrait screen -- framed a
                // constraint that no longer exists. A campless landing now
                // looks at the ship and a little ground round her, and the
                // player pans inland to choose; a camp is framed on the TOWN
                // (`CampCentre`, not the survey's `ClearingCentre`, which the
                // town no longer has to be anywhere near) at its build reach.
                ringR = LandingFrameRadius;
                Vector3 aim = camp
                    ? (outpost != null && outpost.HasCampCentre
                        ? outpost.CampCentre
                        : CurrentIsland.transform.position)
                    : ringAt;

                // The vantage is taken from the ISLAND, not from the aim
                // point: with the ring framed the aim IS the ship, and
                // `ship - ship` is a zero direction with no composition in it
                // at all. Measured from the island's centre it is the same
                // "water in the near edge, land beyond" look as before.
                Vector3 seaward = transform.position - CurrentIsland.transform.position;
                seaward.y = 0f;

                // What has to fit. With no camp that is the siting ring; once
                // one stands it is the settlement's own ViewRadius when the
                // ground has been surveyed -- the same number home frames by
                // -- and the island otherwise.
                var settle = CurrentIsland.GetComponent<Settlement>();
                float reach = !camp
                    ? ringR
                    : outpost != null && outpost.HasCampCentre
                        ? Outpost.TownRadius
                        : (settle != null ? settle.ViewRadius
                                          : Mathf.Max(70f, CurrentIsland.Radius));

                // The zoom that holds the whole ring, on the NARROW axis of
                // whatever shape the window is. On a phone that is the width,
                // and it is the only fit that works: the overview's coverage
                // is metres up the FRAME, and portrait is 0.46 as wide as it
                // is tall, so a coverage picked to hold 160 m vertically holds
                // 74 m across. See ChaseCamera.OverviewGroundForRing.
                float ringGround = !camp
                    ? chaseCam.OverviewGroundForRing(ringR, ringMargin)
                    : 0f;

                var shot = new SeaSick.CameraRig.ChaseCamera.IslandShot
                {
                    centre = aim,
                    radius = reach,
                    from = seaward,
                    ground = ringGround,
                    // Nothing may quietly widen or slide this one: the legibility
                    // clamp would pull the coverage back in (it stops at 309 m of
                    // ground and the ring needs 399), and the ship-slide would
                    // drag the frame off the very circle it is centred on -- she
                    // IS the centre.
                    free = !camp,
                };
                if (islandCam != null)
                {
                    islandCam.Focus(CurrentIsland);

                    // **ONE SHOT, ON AN EVENT.** Landing at a campless island
                    // seats the player's own view on the ring once; after that
                    // the view is theirs to pan and zoom, so this must not run
                    // every frame or a drag would snap back under the thumb.
                    // The latch is the same single-owner shape as the berth
                    // test above: the composition changes when she LANDS, when
                    // the ring's owner changes (a fire is sited -> `camp`), and
                    // at no other time. No per-frame distance or difference
                    // test decides it.
                    if (!camp && ringFramedAt != CurrentIsland)
                    {
                        islandCam.LookAtGround(aim, ringGround);
                        ringFramedAt = CurrentIsland;
                    }
                    else if (camp && ringFramedAt == CurrentIsland)
                    {
                        // A fire now stands where the ring was. Hand the view
                        // to the camp, which is exactly what `CampSiting`'s
                        // own commit does -- said here as well so the same
                        // thing happens when a camp arrives by any other road
                        // (a build finishing, a save adopting one) and the
                        // view is never left latched on the ship for ever.
                        islandCam.LookAt(outpost.CampCentre,
                            SeaSick.CameraRig.IslandCam.BlueprintHeight);
                        ringFramedAt = null;
                    }

                    shot = islandCam.Apply(shot);
                }
                chaseCam.Overview = shot;
            }
            else
            {
                chaseCam.Overview = null;
                ringFramedAt = null;   // she left; the next landing frames afresh
                if (islandCam != null) islandCam.Focus(null);
            }

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
            // Kevin, 2026-09-22: no island is home any more -- the starting
            // island surveys and wakes its hands like any other.
            if (isle != null)
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
            SeaSick.Save.SaveGame.Autosave("anchored off " + (isle != null ? isle.name : "nothing"));
        }

        // --- Gather party (2026-09-27) --------------------------------------
        //
        // Kevin: "yes go ahead with the gather party." A few hands go ashore
        // for ONE raw good and carry it into the hold; see `GatherParty`.
        // The ship is `Ashore` while any of them is out, so there is no
        // cast-off until they are back -- the prompt says so and offers the
        // recall in its place.

        /// Where a party (or the plain shore party) steps onto the land.
        public Vector3 PartyLanding()
        {
            if (CurrentIsland == null) return transform.position;
            return CurrentDock != null
                ? CurrentDock.Landing
                : (gangway != null && gangway.Ready
                    ? gangway.LandingPoint
                    : CurrentIsland.ShorePoint(0, 1, transform.position));
        }

        /// Can a gather party go from here right now?
        public bool CanSendParty(out string why)
        {
            why = "";
            if (CurrentIsland == null) { why = "not at an island"; return false; }
            if (CurrentState != State.Anchored) { why = $"not lying at anchor ({CurrentState})"; return false; }
            if (landingPending) { why = "still coming alongside"; return false; }
            if (voyage != null && voyage.AtHome) { why = "at home"; return false; }
            return true;
        }

        /// The party's hands walk down the plank; she is `Ashore` until the
        /// last of them is back (`AllAboard`).
        public void PutPartyAshore(GatherParty p, System.Collections.Generic.List<CrewAgent> who, Vector3 landing)
        {
            if (p == null || who == null || who.Count == 0) return;
            for (int i = 0; i < who.Count; i++)
            {
                if (!Ours(who[i])) continue;
                Vector3 spread = transform.right * ((i - (who.Count - 1) * 0.5f) * 2.2f);
                who[i].GoAshoreInParty(p, landing + spread, CurrentIsland, hold, gangway, voyage);
            }
            CurrentState = State.Ashore;
        }

        GatherParty party;
        GatherParty Party => party != null ? party : (party = GatherParty.For(this));

        void DrawGatherParty(ref Prompts.Stack stack, float bh, GUIStyle buttonStyle)
        {
            if (CurrentIsland == null || CampSiting.Placing) return;
            var r = stack.Next(bh);
            UIBlocker.Block(r);
            if (GUI.Button(r, "⛏  Send gather party", buttonStyle))
                SeaSick.UI.Sheets.GatherPartySheet.Open(this, Party);
        }

        void SendAshore()
        {
            if (CurrentIsland == null) return;
            // Land them at the foot of the plank, then they find their own work.
            //
            // At a pier the plank only reaches the HEAD now (she lies beyond
            // it, not beside it) -- `gangway.LandingPoint` is the head, not
            // somewhere to start cutting wood. `CrewAgent.PathToShore` routes
            // every trip through `gangway.LandingPoint` before the final
            // point regardless, so handing it the pier's ROOT instead walks
            // them off the ship, onto the head, down the pier, and only then
            // to work -- exactly "step onto the head, walk the pier to root"
            // -- with no pathing changes needed on the crew's side.
            Vector3 landing = CurrentDock != null
                ? CurrentDock.Landing
                : (gangway != null && gangway.Ready
                    ? gangway.LandingPoint
                    : CurrentIsland.ShorePoint(0, 1, transform.position));

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
            if (party != null && party.Out) party.Recall("recalled");
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
            // Kevin, 2026-09-22: no island is home any more -- the starting
            // island's hands get stowed too.
            if (CurrentIsland == null) return;
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
        /// **Make camp**, the one verb an island has before it has a fire.
        ///
        /// A row on the anchor's own stack, the row above leaving, because
        /// until the fire is sited nothing else can offer it: the sheet HUD
        /// only takes an island that has a fire or the drawing of one
        /// (`SheetBootstrap.FireFor`), and there is no campfire in the world
        /// to tap. A second `Prompts` bidder would lose the slot to this one
        /// anyway, so it lives in the stack that already owns it. The tap
        /// starts siting exactly as the old camp bar's button did; the ghost
        /// then carries its own ✓ ✕ ↻ (`CampSiting.OnGUI`).
        ///
        /// Before the survey has decided, or where it found no ground, a line
        /// says so instead of a button that could not work.
        void DrawMakeCamp(ref Prompts.Stack stack, int u, float bh,
            GUIStyle buttonStyle, GUIStyle infoStyle)
        {
            if (CurrentIsland == null || CampSiting.Placing) return;
            var camp = Outpost.Of(CurrentIsland);
            if (camp == null)
            {
                GUI.Label(stack.Next(u * 1.8f), Outpost.Surveying(CurrentIsland)
                    ? "looking over the ground…"
                    : "no ground here will take a camp", infoStyle);
                return;
            }
            if (camp.HasCamp || camp.Building) return;
            var r = stack.Next(bh);
            UIBlocker.Block(r);
            if (GUI.Button(r, MakeCampLabel, buttonStyle))
                CampSiting.Begin(camp, BuildPlans.Campfire,
                    motor != null ? motor.transform : null);
        }

        /// Built once: every part of it is a constant.
        static readonly string MakeCampLabel =
            $"🔥  Make camp — {BuildPlans.Campfire.cost} logs";

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
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (SeaSick.UI.Sheets.MidnightLandHud.Active) return;
            // The gather party's sheet owns the bottom of the screen while
            // it is up.
            if (SeaSick.UI.Sheets.GatherPartySheet.IsOpen) return;
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
            bool sheetHud = SheetsHud.SuppressLegacy;

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

                    if (!sheetHud) DrawMakeCamp(ref stack, u, bh, buttonStyle, infoStyle);
                    if (!sheetHud) DrawGatherParty(ref stack, bh, buttonStyle);

                    // While the sheet HUD is up, the ship's own sheet carries
                    // the shore party, the deck cargo and the repairs. Only
                    // "cast off" stays here, because leaving is the one
                    // decision that is about the VOYAGE rather than the camp
                    // -- and because a player who wants to go should never
                    // have to find an object to tap first.
                    if (!sheetHud && CurrentIsland != null && CurrentIsland.HasResources)
                    {
                        var secondary = stack.Next(bh);
                        UIBlocker.Block(secondary);
                        if (GUI.Button(secondary, "send crew ashore", buttonStyle)) SendAshore();
                    }

                    // Deck cargo is the ship's sheet's once there is a camp;
                    // before that it is a row here.
                    if (!sheetHud) DrawDeckCargoToggle(ref stack, u, buttonStyle, infoStyle);
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

                    // A gather party out: the recall is the party's, and the
                    // line under it says why there is no cast-off.
                    if (party != null && party.Out)
                    {
                        if (GUI.Button(primary, party.Recalling
                                ? "⛏  coming back aboard…"
                                : "⛏  Recall party   (space)", buttonStyle)) RecallCrew();
                        int out_ = party.Ashore;
                        if (partyText.Changed(HudLabel.Key(party.DeliveredUnits, out_,
                                party.Recalling ? 1 : 0, party.Target)))
                            partyText.Set($"{party.StatusLine}\n{out_} hand{(out_ == 1 ? "" : "s")} ashore — recall first to cast off");
                        GUI.Label(stack.Next(u * 3.2f), partyText.Content, infoStyle);
                        break;
                    }

                    if (GUI.Button(primary, "recall crew aboard   (space)", buttonStyle)) RecallCrew();

                    if (!sheetHud) DrawMakeCamp(ref stack, u, bh, buttonStyle, infoStyle);

                    bool canRepair = !sheetHud && hull != null && hull.NeedsRepair && voyage != null
                        && voyage.AmountOf("Timber") > 0;
                    if (canRepair || (repairing && !sheetHud))
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

                    if (!sheetHud) DrawDeckCargoToggle(ref stack, u, buttonStyle, infoStyle);

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
