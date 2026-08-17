using SeaSick.Crew;
using SeaSick.Ocean;
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
        [SerializeField] float swellAnchorPenalty = 1f;
        [SerializeField] float gatherRatePerCrew = 1.1f; // resource units/sec each
        [SerializeField] float approachSpeedLimit = 6.5f; // must slow down to anchor

        public enum State { Underway, Dropping, Anchored, Ashore, Weighing }
        public State CurrentState { get; private set; } = State.Underway;
        public Island CurrentIsland { get; private set; }

        ShipMotor motor;
        HullIntegrity hull;
        CrewAgent[] crew;
        VoyageManager voyage;
        WaveField waves;
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
            crew = GetComponentsInChildren<CrewAgent>(true);
            voyage = FindFirstObjectByType<VoyageManager>();
            waves = FindFirstObjectByType<WaveField>();
            chaseCam = FindFirstObjectByType<SeaSick.CameraRig.ChaseCamera>();
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

        float SwellHere() => waves != null
            ? waves.SwellIntensity(new Vector2(transform.position.x, transform.position.z), Time.time)
            : 0f;

        void Update()
        {
            float dt = Time.deltaTime;

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
                    Harvest(dt);
                    Repair(dt);
                    if (AllAboard()) { CurrentState = State.Anchored; repairing = false; }
                    break;
            }

            UpdateCameraFocus();
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

        void Harvest(float dt)
        {
            if (CurrentIsland == null || voyage == null) return;
            int ashore = 0;
            foreach (var c in crew) if (c != null && c.IsAshore) ashore++;
            if (ashore == 0 || !CurrentIsland.HasResources) return;
            if (voyage.HoldFull) return;

            gatherFraction += CurrentIsland.Extract(gatherRatePerCrew * ashore * dt);
            while (gatherFraction >= 1f)
            {
                gatherFraction -= 1f;
                voyage.AddLoot(1, CurrentIsland.ResourceName);
            }
        }

        bool AllAboard()
        {
            foreach (var c in crew)
                if (c != null && !c.IsAboard) return false;
            return true;
        }

        // --- Player actions -------------------------------------------------

        void DropAnchor(Island isle)
        {
            CurrentIsland = isle;
            motor.Anchored = true;
            timer = dropTime * (SwellHere() > 0.25f ? swellAnchorPenalty : 1f);
            CurrentState = timer > 0f ? State.Dropping : State.Anchored;
        }

        void SendAshore()
        {
            if (CurrentIsland == null) return;
            for (int i = 0; i < crew.Length; i++)
                if (crew[i] != null)
                    crew[i].GoAshore(CurrentIsland.ShorePoint(i, crew.Length, transform.position));
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
                            ? $"⚓  Drop anchor — {isle.ResourceName}"
                            : "slow down to anchor  (S)";
                    UIBlocker.Block(primary);
                    GUI.enabled = slowEnough && beach;
                    if (GUI.Button(primary, label, buttonStyle)) DropAnchor(isle);
                    GUI.enabled = true;
                    break;
                }

                case State.Dropping:
                    GUI.Label(new Rect(0f, by, w, bh),
                        SwellHere() > 0.25f
                            ? $"dropping anchor in heavy water…  {timer:F1}s"
                            : $"dropping anchor…  {timer:F1}s", infoStyle);
                    break;

                case State.Weighing:
                    GUI.Label(new Rect(0f, by, w, bh), $"weighing anchor…  {timer:F1}s", infoStyle);
                    break;

                case State.Anchored:
                {
                    string res = CurrentIsland != null && CurrentIsland.HasResources
                        ? $"go ashore — harvest {CurrentIsland.ResourceName}"
                        : "go ashore — rest";
                    UIBlocker.Block(primary);
                    UIBlocker.Block(secondary);
                    if (GUI.Button(primary, res, buttonStyle)) SendAshore();
                    if (GUI.Button(secondary, "⚓  Weigh anchor", buttonStyle)) WeighAnchor();
                    break;
                }

                case State.Ashore:
                {
                    string status = CurrentIsland != null && CurrentIsland.HasResources
                        ? $"harvesting {CurrentIsland.ResourceName} — {CurrentIsland.Remaining:F0} left"
                        : "the crew rests on solid ground";
                    if (repairing) status += "   ·   repairing hull";
                    GUI.Label(new Rect(0f, secondary.y - u * 2f, w, u * 1.8f), status, infoStyle);

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
                        if (GUI.Button(secondary, "recall crew aboard", buttonStyle)) RecallCrew();
                    }
                    else
                    {
                        UIBlocker.Block(primary);
                        if (GUI.Button(primary, "recall crew aboard", buttonStyle)) RecallCrew();
                    }
                    break;
                }
            }
        }
    }
}
