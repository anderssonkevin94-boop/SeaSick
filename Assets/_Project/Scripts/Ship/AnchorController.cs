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
        [SerializeField] float berthSpeed = 1.6f;
        [SerializeField] float approachSpeedLimit = 6.5f; // must slow down to anchor

        public enum State { Underway, Dropping, Anchored, Ashore, Weighing }
        public State CurrentState { get; private set; } = State.Underway;
        public Island CurrentIsland { get; private set; }

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

            // With an enemy alongside, space is for the lock, not the anchor.
            // You are far more likely to want to hold them in view than to try
            // to dock in the middle of a fight.
            if (combatLock == null) combatLock = GetComponent<SeaSick.Combat.CombatLock>();
            if (combatLock != null && combatLock.WantsSpace) return;

            switch (CurrentState)
            {
                case State.Underway:
                {
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

        void Update()
        {
            float dt = Time.deltaTime;

            SpacebarCommand();

            switch (CurrentState)
            {
                case State.Dropping:
                    timer -= dt;
                    if (timer <= 0f) CurrentState = State.Anchored;
                    break;

                case State.Weighing:
                    timer -= dt;
                    if (timer <= 0f)
                    {
                        motor.Anchored = false;
                        CurrentIsland = null;
                        CurrentState = State.Underway;
                    }
                    break;

                case State.Ashore:
                    Repair(dt);
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
                return;
            }

            Vector3 c = CurrentIsland.transform.position;
            Vector3 out2 = transform.position - c;
            out2.y = 0f;
            if (out2.sqrMagnitude < 0.01f) return;
            float bearing = Mathf.Atan2(out2.x, out2.z);
            float shore = CurrentIsland.RadiusAt(bearing);

            Vector3 berth = c + out2.normalized * (shore + berthDistance);
            Vector3 pos = transform.position;
            berth.y = pos.y;
            transform.position = Vector3.Lerp(pos, berth, 1f - Mathf.Exp(-berthSpeed * dt));

            if (gangway != null) gangway.Extend(CurrentIsland);
        }

        /// While the crew are ashore, pull the camera back to frame them —
        /// otherwise they wander out of shot and the player misses the work.
        void UpdateCameraFocus()
        {
            if (chaseCam == null) return;
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

            for (int i = 0; i < crew.Length; i++)
            {
                if (crew[i] == null) continue;
                Vector3 spread = transform.right * ((i - (crew.Length - 1) * 0.5f) * 2.2f);
                crew[i].GoAshore(landing + spread, CurrentIsland, hold, gangway, voyage);
            }
            CurrentState = State.Ashore;
        }

        void RecallCrew()
        {
            foreach (var c in crew) if (c != null) c.ReturnAboard();
        }

        void WeighAnchor()
        {
            timer = weighTime;
            if (timer > 0f) { CurrentState = State.Weighing; return; }
            motor.Anchored = false;
            CurrentIsland = null;
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

            var style = new GUIStyle(buttonStyle);
            if (voyage.TakeDeckCargo) style.normal = style.active;
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
