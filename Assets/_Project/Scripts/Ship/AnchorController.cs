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

        /// Where her centre lies at this berth (flat), and which way she
        /// points there -- this berthing's answer, for the catwalk and probes.
        public Vector3 BerthPosition => berthPos;
        public Quaternion BerthHeading => berthHeading;

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

        void Start()
        {
            // Converted once: `SeaActions.Offer` is called every frame and
            // must not allocate a delegate each time.
            tapLand = TapLand;
            tapAlongside = TapAlongside;

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
                    // **A bigger sea action owns the key (2026-09-30).** The
                    // sea card shows ONE offer, the highest priority
                    // (`SeaActions`); a castaway outranks landing, so space
                    // must not run her ashore while the card is asking for a
                    // rescue. Last frame's winner: this frame's offers are
                    // still coming in.
                    if (SeaSick.UI.Sheets.SeaActions.HasOffer
                        && SeaSick.UI.Sheets.SeaActions.Current.priority > SeaSick.UI.Sheets.SeaActions.PriorityLand)
                        break;
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
                    CallThemBack();
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
        /// **No flicker at the shelf (2026-10-03, Kevin: "I can't always land
        /// at a new island").** `landingDepth` (12 m) is also the depth of
        /// the shelf round every island (seabed -12 m, +-0.25 m of detail),
        /// so over the shelf the test flipped in and out on ~25 m patches and
        /// the "Land here" card blinked -- a tap could fall on a frame it had
        /// gone. Now: the SHALLOWEST water under bow, middle or stern (the
        /// points `HullIntegrity.HoldOffTheLand` uses, so a long hull nosed
        /// into a steep coast counts from her bow), and once in range she
        /// stays in range until `landingDepthLeave` deeper. The card turns on
        /// where it did (or a little sooner) and stays on.
        const float landingDepthLeave = 1f;
        Island inRangeIsle;

        Island IslandInRange()
        {
            var isle = Island.Nearest(transform.position);
            if (isle == null) { inRangeIsle = null; return null; }
            float depth = ShallowestUnderHull();
            if (!float.IsNaN(depth))
            {
                float limit = landingDepth + (isle == inRangeIsle ? landingDepthLeave : 0f);
                inRangeIsle = depth <= limit ? isle : null;
                return inRangeIsle;
            }
            inRangeIsle = null;
            float reach = isle.RadiusToward(transform.position) + 30f;
            return Island.FlatDistance(transform.position, isle.transform.position) <= reach
                ? isle : null;
        }

        /// The shallowest water under bow, middle and stern (NaN only when
        /// none of the three has a reading).
        float ShallowestUnderHull()
        {
            float half = motor != null ? motor.HullLength * 0.4f : 0f;
            Vector3 fwd = transform.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 c = transform.position;
            float best = float.NaN;
            for (int k = -1; k <= 1; k++)
            {
                float d = WaterDepthUnder(c + fwd * (half * k));
                if (!float.IsNaN(d) && (float.IsNaN(best) || d < best)) best = d;
            }
            return best;
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
        ///
        /// **Held (2026-10-04):** a beach found at this island stays offered
        /// within `Island.BeachHoldRadius` of where it was found or for
        /// `Island.BeachHoldSeconds` (`Island.BeachHold`): at a narrow sand
        /// sliver the raw answer was true on a ~1 m column and the card
        /// blinked "Land here" / "Sheer cliff". Cliff -> beach is immediate.
        bool CanLandHere(Island isle)
        {
            if (isle == null) return false;
            if (isle != beachHeldIsle) { beachHeldIsle = isle; beachHeldTime = -1f; }
            Vector3 at = transform.position;
            return Island.BeachHold(isle.HasBeachToward(at), at, Time.time, ref beachHeldAt, ref beachHeldTime);
        }
        Island beachHeldIsle;
        Vector3 beachHeldAt;
        float beachHeldTime = -1f;

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
            berthHeading = d.HeadingFor(approach);
            berthPos = BerthPointAt(d, berthHeading);

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
            if (Dock.Home == null) { why = "no home dock yet"; return false; }
            return BerthAt(Dock.Home, Vector3.zero, out why);
        }

        /// **Where her centre lies at `d` when she points `heading`**: far
        /// enough off the head for the catwalk (2026-10-01) to reach from the
        /// pier's edge to her rail -- `Gangway.PierBerthOffset`, measured off
        /// the live hull -- or the dock's own beam-and-fender default when
        /// there is no gangway to ask.
        Vector3 BerthPointAt(Dock d, Quaternion heading) => gangway != null
            ? d.BerthAt(gangway.PierBerthOffset(d, heading))
            : d.BerthFor(BerthBeam);

        /// Set her down tied up at `d`, from wherever she is -- `BerthAtHome`
        /// for home, and a load for a camp pier she was saved lying at.
        /// `approachFlat` picks which of the two T-berth headings (zero =
        /// the dock's default).
        public bool BerthAt(Dock d, Vector3 approachFlat, out string why)
        {
            why = null;
            if (d == null) { why = "no dock"; return false; }
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
            var heading = d.HeadingFor(approachFlat);
            Vector3 p = BerthPointAt(d, heading);
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
            // Set down square to the berth: the approach she "had" is the
            // heading just given her, so `ComeAlongside` keeps it.
            ComeAlongside(d);

            // Ring down stop, or she arrives at her own pier under full
            // ahead. HelmInput re-asserts the throttle every frame, so the
            // ORDER has to move — zeroing ThrottleOrder here would last
            // exactly until HelmInput's next Update.
            var helm = GetComponent<HelmInput>();
            if (helm != null) helm.AllStop();
            return true;
        }

        Terrain.TerrainWorldPopulator populator;
        bool WorldBuilt()
        {
            if (populator == null) populator = FindFirstObjectByType<Terrain.TerrainWorldPopulator>();
            return populator != null && populator.Done && Island.TerrainHeight != null;
        }

        /// **The new-game start: open water with land in sight.** Rings out
        /// from the world origin for the first spot that is deep water all
        /// round (no scraping a shoal on the first frame) and has an island
        /// 80-400 m off, and faces her at it: the first thing on screen is a
        /// choice of where to go, not an empty horizon.
        void StartAtSea()
        {
            var h = Island.TerrainHeight;
            Vector3 best = Vector3.zero; Island aim = null; bool found = false;
            for (float r = 0f; r <= 1500f && !found; r += 30f)
            for (int k = 0; k < 16 && !found; k++)
            {
                float a = k * Mathf.PI / 8f;
                var p = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                if (h(p.x, p.z) > -6f) continue;
                bool open = true;
                for (int j = 0; j < 8 && open; j++)
                {
                    float b = j * Mathf.PI / 4f;
                    if (h(p.x + Mathf.Sin(b) * 35f, p.z + Mathf.Cos(b) * 35f) > -3f) open = false;
                }
                if (!open) continue;
                Island near = null; float gap = float.MaxValue;
                foreach (var isle in Island.All)
                {
                    if (isle == null) continue;
                    float g = Island.FlatDistance(isle.transform.position, p) - isle.MaxRadius;
                    if (g < gap) { gap = g; near = isle; }
                }
                if (near == null || gap < 80f || gap > 400f) continue;
                best = p; aim = near; found = true;
            }
            if (!found) { Debug.LogWarning("AnchorController: no open-water start found; staying put"); return; }
            var to = aim.transform.position - best;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            SeaSick.Save.SaveGame.Warp(motor, best, yaw);
            Debug.Log($"AnchorController: new game starts at sea ({best.x:F0},{best.z:F0}) facing {aim.name}");
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
            // **A new game has no home** (Kevin, 2026-09-29: "you choose
            // yourself which island to settle"). Once the world is built and
            // there is still no home berth, she starts at sea instead.
            if (startAtHomeDock && !startedDocked && Dock.Home == null && WorldBuilt())
            {
                startedDocked = true;
                StartAtSea();
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

                case State.Anchored:
                    // **Repairs run wherever she lies stopped (2026-09-30,
                    // island UI phase 6).** They only ran `Ashore`, the state
                    // the retired "send crew ashore" row put her in -- so the
                    // Ship sheet's "Repair hull" pill (the one repair button
                    // left) flipped a flag that did nothing at anchor or at a
                    // pier. The hands mend her from the deck.
                    Repair(dt);
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
                    // Repairs carry on at anchor now (see `Anchored` above),
                    // so the crew coming back no longer stops them.
                    if (AllAboard()) CurrentState = State.Anchored;
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
            // Crew go ashore through the landing party (`LandingPartySheet`,
            // the thumb bar's "Landing party"); the old "send crew ashore"
            // row was retired 2026-09-30 (island UI phase 6) -- the party's
            // Gather is crew working ashore, with a say in who and how much.
            if (landingPending && CurrentState == State.Anchored
                && gangway != null && gangway.Ready)
            {
                landingPending = false;
            }

            OfferSeaAction();
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
                motor.HoldStation = false;
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
                // **Locked to the berth (2026-10-01).** Once the spring has
                // eased her in, `ShipMotor` holds her flat position and her
                // heading with a servo; heave, roll and pitch stay free. The
                // catwalk below needs her side to stay where it was laid.
                motor.HoldStation = true;
                // **The catwalk** (2026-10-01): from the pier's sea edge onto
                // her pier-side rail, and the shore party walks the pier the
                // rest of the way (see `PartyLanding`). **Home too** -- home
                // has been a pier the player built at one of their camps
                // since 2026-09-25 (`Dock.SetHome`), the same pier the same
                // hands walk at every other camp; "the home pier has its own
                // arrival" dated from the world-built harbour, and lying at
                // a pier with no way ashore read as the bug it was.
                // It comes down once she is locked, not while she is still
                // being hauled in.
                if (gangway != null)
                {
                    if (motor.StationLock01 > 0.99f) gangway.ExtendToPier(CurrentDock, berthHeading);
                    else gangway.Withdraw();
                }
                return;
            }
            motor.MooringHeading = null;
            motor.HoldStation = false;

            // **Off a beach she lies where she stopped.** This used to walk
            // the anchor point to `RadiusAt(bearing) + 11 m` from the
            // island's centre -- a berth on the radial profile, which is
            // metres to hundreds of metres off the real waterline, so the
            // spring dragged her onto the sand or out to sea after the
            // player had already chosen where to stop. `ShipMotor.Anchored`
            // took the anchor point where she was when it was set; that is
            // the berth.
            // The plank runs to the beach `DropAnchor` found (the one the
            // "Land here" card offered), not to the outline on the line to
            // the island's centre -- off a headland or in a bay that was the
            // cliff. No terrain / no beach: the old radial aim.
            if (gangway != null)
            {
                if (landingStepSet) gangway.ExtendTo(landingStep);
                else gangway.Extend(CurrentIsland);
            }
        }

        bool landingStepSet;
        Vector3 landingStep;

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
            // A landing party out is not aboard, whoever `crew` (cached at
            // Start) knows about: a hand boarded later (a save's villagers,
            // a castaway) is missing from it, and the ship went back to
            // Anchored -- Cast off offered -- the moment he walked off
            // (2026-09-30 screenshot pass).
            if (Party.Out) return false;
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
            // **Ashore on the beach the card offered (2026-10-04).** Found
            // once, here: from where she lies, else from where the held
            // beach was last found (a sliver she has drifted a metre off).
            landingStepSet = isle != null && (isle.TryLandingStep(transform.position, out landingStep)
                || (isle == beachHeldIsle && beachHeldTime >= 0f && isle.TryLandingStep(beachHeldAt, out landingStep)));

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
                    : landingStepSet ? landingStep
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
            party = p;
            // Everyone is under her now; hands boarded since Start join the
            // list the recall and `AllAboard` walk.
            crew = GetComponentsInChildren<CrewAgent>(true);
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

        /// **A raider in range (2026-09-30).** The combat lock's dial and the
        /// helm take the bottom of the screen then: the thumb bar's "Landing
        /// party" stands down until the fight is over (the party runs for
        /// the ship on its own when raiders come near), and Cast off / Call
        /// them back stay. Asked once a frame however often it is read.
        public bool CombatNear
        {
            get
            {
                if (combatNearFrame == Time.frameCount) return combatNear;
                combatNearFrame = Time.frameCount;
                if (combatLock == null) combatLock = GetComponent<SeaSick.Combat.CombatLock>();
                return combatNear = combatLock != null && combatLock.WantsSpace;
            }
        }
        bool combatNear;
        int combatNearFrame = -1;

        // "Landing party" and the old "send crew ashore" rows were IMGUI
        // here until 2026-09-30 (island UI phase 6). The landing party is the
        // thumb bar's primary off a fresh island now (`ThumbBar`, anchored
        // mode, opening `LandingPartySheet`); "send crew ashore" (the whole
        // crew, unasked, for timber) is retired -- the party's Gather is
        // crew working ashore.

        /// True from the landing press until the plank is down: she is still
        /// coming alongside, and Cast off waits (`CastOff` refuses it too).
        public bool LandingPending => landingPending;

        /// "Call them back" -- the thumb bar's recall while a landing party
        /// (or any of her hands) is ashore; what space does in that state.
        public void CallThemBack()
        {
            if (CurrentState != State.Ashore) return;
            // Already turned for home: a second order would only re-route
            // hands already walking back (the bar's hint says "Coming back
            // aboard…" meanwhile).
            if (Party.Out && party.Recalling) return;
            RecallCrew();
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
            bool partyOut = Party.Out;
            if (partyOut) party.Recall("called back");
            // Parked camp hands are not ours to recall -- they live there now.
            // A landing party's hands were just turned for home by the party
            // itself; a second order here would overwrite theirs.
            foreach (var c in crew)
                if (Ours(c) && !(partyOut && c.Party == party)) c.ReturnAboard();
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
            // **Death/rescue phase 3.** A pending tombstone at the camp the
            // player is anchored at blocks casting off -- covers the
            // button, the space-bar shortcut and `CastOff()` all at once,
            // the one place all three converge.
            if (SeaSick.World.Life.GraveGate.Blocking) return;

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
            motor.Anchored = false;     // also lets go of the pier lock
            motor.HoldStation = false;
            motor.MooringHeading = null;
            CurrentIsland = null;
            CurrentDock = null;
            landingPending = false;
            CurrentState = State.Underway;
        }

        // --- UI -------------------------------------------------------------
        //
        // **No IMGUI drawn here (2026-09-30, island UI phase 6).** Kevin
        // approved mockup "8c · Anchored off a fresh island". What this
        // controller used to draw as a stack of IMGUI pills over the helm
        // now lives in two UI Toolkit places:
        //
        // * **Under way** -- "Land here", "Come alongside", "slow down",
        //   "sheer cliff" and the (dead, 0 s) drop/weigh countdowns are
        //   OFFERS to the sea action card (`SeaActions`, drawn by `SeaHud`),
        //   made every frame from `Update` (`OfferSeaAction`).
        // * **Stopped off a fresh island** -- Landing party · Ship · Cast off
        //   (Call them back while anyone is ashore) are the thumb bar's
        //   anchored mode (`ThumbBar`), with the Next card's "Make camp"
        //   above it; "coming alongside…", "looking over the ground…" and
        //   the party's progress are the bar's hint line.
        //
        // Retired with the IMGUI: "send crew ashore" (the landing party's
        // Gather), the deck cargo row (the Backpack's Ship tab), the ashore
        // "repair hull" row (the Ship sheet's Repair pill, which now works at
        // anchor too) and the IMGUI "Make camp" fallback (the Next card).
        // At a camp the thumb bar's Camp · Build · Ship is unchanged and Cast
        // off stays in the Ship sheet; at home the home panel owns leaving.

        /// The frame this controller last offered the sea card something.
        int offeredFrame = -10;

        /// **The only IMGUI left: a bid, never a draw.** The shared bottom
        /// prompt slot (`Prompts`) went to the anchor whenever it had
        /// something to say, which kept `Bilge`'s "over the side" pill away
        /// from the landing controls. Those controls are UI Toolkit now,
        /// but they sit where that pill would, so the anchor still takes the
        /// slot -- and draws nothing in it -- while it offers the sea card
        /// or the anchored thumb bar is up. The Hand still outranks it.
        /// Bid on every event (see `Prompts`).
        void OnGUI()
        {
            if (Time.frameCount - offeredFrame <= 1 || SeaSick.UI.Sheets.ThumbBar.AnchoredActive)
                Prompts.Claim(Prompts.Rank.Anchor);
        }

        /// Keyboard hints ("space", "S") only on a desk-shaped window
        /// (`HudLayout.Wide`); a phone gets the touch wording alone (Kevin,
        /// 2026-09-27: dev leftovers on the phone).
        static bool Desk => SeaSick.UI.HudLayout.Wide;
        static string KeyHint(string hint) => Desk ? hint : "";

        // The sea card's taps, converted to delegates once (`Start`). Each
        // re-checks everything (`TryLand` / `TryComeAlongside` refuse unless
        // she is under way, near enough and slow enough), so a second press
        // -- the card's own click racing the space bar -- is a no-op.
        System.Action tapLand, tapAlongside;
        void TapLand() => TryLand(out _);
        void TapAlongside() => TryComeAlongside();

        // The offer's words, rebuilt only when what they SAY changes (a new
        // island or pier, a speed or beach verdict flipping, the window's
        // shape): the card is offered every frame and must not allocate.
        Object offerAt;
        int offerKey = int.MinValue;
        string offerEyebrow = "", offerTitle = "", offerDetail = "";
        // The island a pier in range stands on, found once per pier (`Island.Nearest` walks every island).
        Dock offerDockOf; Island offerDockIsle;

        const string TitleLand = "Land here";
        const string TitleCliff = "Sheer cliff";
        const string TitleAlongside = "Come alongside";
        const string TitleDropping = "Dropping anchor…";
        const string TitleWeighing = "Weighing anchor…";

        bool OfferStale(Object at, int key)
        {
            if (at == offerAt && key == offerKey) return false;
            offerAt = at;
            offerKey = key;
            return true;
        }

        string SlowText => $"Slow to under {approachSpeedLimit:0.#} m/s" + KeyHint("  ·  S");

        static string Upper(Object o) => o != null ? o.name.ToUpperInvariant() : "";

        /// **The anchor's offers to the sea action card**, once a frame from
        /// `Update`, at `SeaActions.PriorityLand` (200): what the IMGUI
        /// prompt's under-way rows said, as one card above the thumb. Never
        /// a disabled button: too fast or a cliff is an information card
        /// (`enabled: false`) that says what is needed instead.
        void OfferSeaAction()
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            // The home panel owns the screen and the leaving while it is up.
            if (voyage != null && voyage.AtHome) return;
            const int P = SeaSick.UI.Sheets.SeaActions.PriorityLand;
            // Stamped on every offer below; `OnGUI` holds the prompt slot
            // while it is fresh.
            int frame = Time.frameCount;

            switch (CurrentState)
            {
                // Both timers are 0 s today (anchoring is immediate), so
                // these are for a tuning that brings them back: a progress
                // bar on the card, not a ticking IMGUI label.
                case State.Dropping:
                    if (OfferStale(CurrentIsland, 1)) { offerEyebrow = Upper(CurrentIsland); offerDetail = "Letting go"; }
                    offeredFrame = frame;
                    SeaSick.UI.Sheets.SeaActions.Offer(P, offerEyebrow, TitleDropping, offerDetail, null, false,
                        dropTime > 0f ? Mathf.Clamp01(1f - timer / dropTime) : 1f);
                    return;
                case State.Weighing:
                    if (OfferStale(CurrentIsland, 2)) { offerEyebrow = Upper(CurrentIsland); offerDetail = "Getting under way"; }
                    offeredFrame = frame;
                    SeaSick.UI.Sheets.SeaActions.Offer(P, offerEyebrow, TitleWeighing, offerDetail, null, false,
                        weighTime > 0f ? Mathf.Clamp01(1f - timer / weighTime) : 1f);
                    return;
                case State.Underway:
                    break;
                default:
                    return;
            }

            bool slow = motor != null && motor.CurrentSpeed <= approachSpeedLimit;
            int desk = Desk ? 8 : 0;

            // The pier's own offer replaces the beach one rather than sitting
            // beside it -- two ways to stop in the same thirty metres is a
            // choice nobody wants to make. A dock beats a beach, as on space.
            var dock = DockInRange();
            if (dock != null)
            {
                // Any pier on the home island is the home berth to the player, not just the
                // dock `Dock.Home` names; other piers read the place name, not "ISLAND_6".
                // Home is part of the cache key: on a freshly loaded save the first cast-off
                // offer was built before the home island was known and kept "ISLAND 6 · PIER".
                if (dock != offerDockOf) { offerDockOf = dock; offerDockIsle = Island.Nearest(dock.Berth); }
                var at = offerDockIsle;
                bool home = dock.IsHome || (at != null && at.IsHome);
                if (OfferStale(dock, 3 | (slow ? 4 : 0) | desk | (home ? 16 : 0)))
                {
                    offerEyebrow = home ? "HOME BERTH"
                        : (at != null ? SeaSick.UI.Sheets.ChartData.PrettyName(at).ToUpperInvariant() + " · PIER" : "PIER");
                    offerTitle = TitleAlongside;
                    offerDetail = slow ? "Tie up at the pier" + KeyHint("  ·  space") : SlowText;
                }
                offeredFrame = frame;
                SeaSick.UI.Sheets.SeaActions.Offer(P, offerEyebrow, offerTitle, offerDetail,
                    slow ? tapAlongside : null, slow);
                return;
            }

            var isle = IslandInRange();
            if (isle == null) return;
            bool beach = CanLandHere(isle);
            bool res = isle.HasResources;
            // **Sand past the walk (2026-10-04):** the cliff card says where
            // it is, "Sand 60 m astern". Metres in 10 m buckets and four
            // sides, so the key (and the string) changes only when the words do.
            int sandM = 0, sandSide = 0;
            Vector3 sandAt = default;
            bool sand = !beach && isle.SandBeyondReach(transform.position, out sandAt);
            if (sand) Island.SandPointer(transform.position, transform.forward, sandAt, out sandM, out sandSide);
            int sandKey = sand ? ((sandM / 10) << 2 | sandSide) + 1 : 0;
            if (OfferStale(isle, 16 | (slow ? 4 : 0) | desk | (beach ? 32 : 0) | (res ? 64 : 0) | sandKey << 8))
            {
                offerEyebrow = Upper(isle);
                offerTitle = beach ? TitleLand : TitleCliff;
                offerDetail = !beach ? (sand ? Island.SandPointerText(sandM, sandSide) : "Find a beach to land")
                    : !slow ? SlowText
                    : (res && !string.IsNullOrEmpty(isle.ResourceName) ? isle.ResourceName : "Rest ashore")
                      + KeyHint("  ·  space");
            }
            bool can = beach && slow;
            offeredFrame = frame;
            SeaSick.UI.Sheets.SeaActions.Offer(P, offerEyebrow, offerTitle, offerDetail, can ? tapLand : null, can);
        }
    }
}
