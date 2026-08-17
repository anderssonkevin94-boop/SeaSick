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
        [SerializeField] float dropTime = 3.5f;
        [SerializeField] float weighTime = 2.8f;
        [SerializeField] float swellAnchorPenalty = 2.6f;
        [SerializeField] float gatherRatePerCrew = 1.1f; // resource units/sec each
        [SerializeField] float approachSpeedLimit = 6.5f; // must slow down to anchor

        public enum State { Underway, Dropping, Anchored, Ashore, Weighing }
        public State CurrentState { get; private set; } = State.Underway;
        public Island CurrentIsland { get; private set; }

        ShipMotor motor;
        CrewAgent[] crew;
        VoyageManager voyage;
        WaveField waves;

        float timer;
        float gatherFraction;
        GUIStyle buttonStyle, infoStyle;

        void Start()
        {
            motor = GetComponent<ShipMotor>();
            crew = GetComponentsInChildren<CrewAgent>(true);
            voyage = FindFirstObjectByType<VoyageManager>();
            waves = FindFirstObjectByType<WaveField>();
        }

        Island IslandInRange()
        {
            var isle = Island.Nearest(transform.position);
            if (isle == null) return null;
            return Island.FlatDistance(transform.position, isle.transform.position) <= isle.AnchorRadius
                ? isle : null;
        }

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
                    if (AllAboard()) CurrentState = State.Anchored;
                    break;
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
            timer = dropTime * (SwellHere() > 0.25f ? swellAnchorPenalty : 1f);
            motor.Anchored = true;
            CurrentState = State.Dropping;
        }

        void SendAshore()
        {
            if (CurrentIsland == null) return;
            for (int i = 0; i < crew.Length; i++)
                if (crew[i] != null) crew[i].GoAshore(CurrentIsland.ShorePoint(i, crew.Length));
            CurrentState = State.Ashore;
        }

        void RecallCrew()
        {
            foreach (var c in crew) if (c != null) c.ReturnAboard();
        }

        void WeighAnchor()
        {
            timer = weighTime;
            CurrentState = State.Weighing;
        }

        // --- UI -------------------------------------------------------------

        void OnGUI()
        {
            if (buttonStyle == null)
            {
                buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold };
                infoStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 15, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }

            float w = Screen.width;
            float h = Screen.height;
            float bw = Mathf.Min(w * 0.62f, 340f);
            float bh = 52f;
            float bx = (w - bw) * 0.5f;
            float by = h * 0.78f;
            var primary = new Rect(bx, by, bw, bh);
            var secondary = new Rect(bx, by + bh + 8f, bw, bh);

            switch (CurrentState)
            {
                case State.Underway:
                {
                    var isle = IslandInRange();
                    if (isle == null) return;
                    bool slowEnough = motor.CurrentSpeed <= approachSpeedLimit;
                    string label = slowEnough
                        ? $"⚓  Drop anchor — {isle.ResourceName}"
                        : "slow down to anchor  (S)";
                    UIBlocker.Block(primary);
                    GUI.enabled = slowEnough;
                    if (GUI.Button(primary, label, buttonStyle)) DropAnchor(isle);
                    GUI.enabled = true;
                    break;
                }

                case State.Dropping:
                    GUI.Label(new Rect(0f, by, w, 30f),
                        SwellHere() > 0.25f
                            ? $"dropping anchor in heavy water… {timer:F1}s"
                            : $"dropping anchor… {timer:F1}s", infoStyle);
                    break;

                case State.Weighing:
                    GUI.Label(new Rect(0f, by, w, 30f), $"weighing anchor… {timer:F1}s", infoStyle);
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
                    GUI.Label(new Rect(0f, by - 26f, w, 24f), status, infoStyle);
                    UIBlocker.Block(primary);
                    if (GUI.Button(primary, "recall crew aboard", buttonStyle)) RecallCrew();
                    break;
                }
            }
        }
    }
}
