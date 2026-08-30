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

        void Start()
        {
            motor = GetComponent<ShipMotor>();
            hull = GetComponent<HullIntegrity>();
            hold = GetComponent<ShipHold>();
            gangway = GetComponent<Gangway>();
            crew = GetComponentsInChildren<CrewAgent>(true);
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
                var d = Dock.Home;
                Vector3 p = d.Berth;
                p.y = transform.position.y;
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
            }

            SpacebarCommand();

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
                            if (c != null && !c.IsAboard) { at += c.transform.position; n++; }
                        if (n > 0) StockTheWood(at / n);
                    }
                    if (AllAboard()) { CurrentState = State.Anchored; repairing = false; }
                    break;
            }

            MoorAlongside(dt);

            // Finish the landing once the plank is actually down.
            if (landingPending && CurrentState == State.Anchored
                && gangway != null && gangway.Ready)
            {
                landingPending = false;
                SendAshore();
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
            else chaseCam.Overview = null;

            if (CurrentState != State.Ashore) { chaseCam.PointOfInterest = null; return; }

            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var c in crew)
                if (c != null && !c.IsAboard) { sum += c.transform.position; n++; }

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

        bool AllAboard()
        {
            foreach (var c in crew)
                if (c != null && !c.IsAboard) return false;
            return true;
        }

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
                if (crew[i] == null) continue;
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
            foreach (var c in crew) if (c != null) c.ReturnAboard();
        }

        /// Let go and get her underway, from outside. The home panel's
        /// "set sail" runs this so one button both closes the tally and
        /// casts off -- splitting it in two is the same mistake `Land`
        /// already fixed in the other direction.
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
        void DrawDeckCargoToggle(Rect anchorRect, int u, float w,
            GUIStyle buttonStyle, GUIStyle infoStyle)
        {
            if (voyage == null) return;

            var r = new Rect(anchorRect.x, anchorRect.y + u * 2.3f, anchorRect.width, u * 1.8f);
            UIBlocker.Block(r);

            var style = voyage.TakeDeckCargo ? UITheme.ButtonPressed : buttonStyle;
            string label = voyage.TakeDeckCargo
                ? $"◉  deck cargo — to {voyage.MaxHold}"
                : $"◎  deck cargo — stop at {voyage.HoldCapacity}";
            if (GUI.Button(r, label, style)) voyage.TakeDeckCargo = !voyage.TakeDeckCargo;

            if (voyage.TakeDeckCargo)
                GUI.Label(new Rect(0f, r.yMax, w, u * 1.6f),
                    "she'll swim low and take water", infoStyle);
        }

        void OnGUI()
        {
            // Two panels offering to cast off in the same corner of the
            // screen is a choice nobody wants to make -- the same rule the
            // dock prompt already applies against the beach one.
            if (voyage != null && voyage.AtHome) return;

            buttonStyle = UITheme.Button;
            infoStyle = UITheme.Small2Centered;

            int u = UITheme.Unit;
            float w = Screen.width;
            float h = Screen.height;
            float bw = Mathf.Min(w * 0.68f, u * 20f);
            float bh = u * 2.7f;
            float bx = (w - bw) * 0.5f;
            float by = h - u * 4.2f - bh;
            var primary = new Rect(bx, by, bw, bh);
            var secondary = new Rect(bx, by - bh - u * 0.5f, bw, bh);

            switch (CurrentState)
            {
                case State.Underway:
                {
                    // The dock's own prompt, which replaces the beach one
                    // rather than sitting beside it -- two ways to stop in
                    // the same thirty metres is a choice nobody wants to make.
                    var d = DockInRange();
                    if (d != null)
                    {
                        bool slow = motor.CurrentSpeed <= approachSpeedLimit;
                        UIBlocker.Block(primary);
                        GUI.enabled = slow;
                        if (GUI.Button(primary, slow
                                ? "⚓  Come alongside   (space)"
                                : "slow down to come alongside  (S)", buttonStyle))
                            ComeAlongside(d);
                        GUI.enabled = true;
                        return;
                    }

                    var isle = IslandInRange();
                    if (isle == null) return;
                    bool beach = CanLandHere(isle);
                    bool slowEnough = motor.CurrentSpeed <= approachSpeedLimit;
                    string label = !beach
                        ? "sheer cliff — find a beach"
                        : slowEnough
                            ? (isle.HasResources
                                ? $"⚓  Land here — {isle.ResourceName}   (space)"
                                : "⚓  Land here — rest   (space)")
                            : "slow down to land  (S)";
                    UIBlocker.Block(primary);
                    GUI.enabled = slowEnough && beach;
                    if (GUI.Button(primary, label, buttonStyle)) Land(isle);
                    GUI.enabled = true;
                    break;
                }

                case State.Dropping:
                    GUI.Label(new Rect(0f, by, w, bh),
$"dropping anchor…  {timer:F1}s", infoStyle);
                    break;

                case State.Weighing:
                    GUI.Label(new Rect(0f, by, w, bh), $"weighing anchor…  {timer:F1}s", infoStyle);
                    break;

                case State.Anchored:
                {
                    if (landingPending)
                    {
                        GUI.Label(new Rect(0f, by, w, bh), "coming alongside…", infoStyle);
                        break;
                    }
                    // Crew are back aboard: cast off, or put them ashore again.
                    UIBlocker.Block(primary);
                    UIBlocker.Block(secondary);
                    if (GUI.Button(primary, "⚓  Cast off   (space)", buttonStyle)) WeighAnchor();
                    if (CurrentIsland != null && CurrentIsland.HasResources
                        && GUI.Button(secondary, "send crew ashore", buttonStyle)) SendAshore();
                    DrawDeckCargoToggle(secondary, u, w, buttonStyle, infoStyle);
                    break;
                }

                case State.Ashore:
                {
                    string status = CurrentIsland != null && CurrentIsland.HasResources
                        ? $"harvesting {CurrentIsland.ResourceName} — {CurrentIsland.Remaining:F0} left"
                        : "the crew rests on solid ground";
                    if (repairing) status += "   ·   repairing hull";
                    GUI.Label(new Rect(0f, secondary.y - u * 2f, w, u * 1.8f), status, infoStyle);

                    DrawDeckCargoToggle(secondary, u, w, buttonStyle, infoStyle);

                    bool canRepair = hull != null && hull.NeedsRepair && voyage != null
                        && voyage.AmountOf("Timber") > 0;
                    if (canRepair || repairing)
                    {
                        UIBlocker.Block(primary);
                        UIBlocker.Block(secondary);
                        string repairLabel = repairing
                            ? $"stop repairs — hull {hull.Integrity01:P0}"
                            : $"repair hull ({hull.Integrity01:P0}) — uses timber";
                        if (GUI.Button(primary, repairLabel, buttonStyle)) repairing = !repairing;
                        if (GUI.Button(secondary, "recall crew aboard   (space)", buttonStyle)) RecallCrew();
                    }
                    else
                    {
                        UIBlocker.Block(primary);
                        if (GUI.Button(primary, "recall crew aboard   (space)", buttonStyle)) RecallCrew();
                    }
                    break;
                }
            }
        }
    }
}
