using SeaSick.Ocean;
using UnityEngine;

namespace SeaSick.Ship
{
    /// Kinematic sailing model: heading/speed integration on the water plane,
    /// with the hull seated on the WaveField via three float points. Fully
    /// deterministic and tunable — no rigidbody surprises on mobile.
    public class ShipMotor : MonoBehaviour
    {
        [Header("Sailing")]
        [SerializeField] float maxSpeed = 12f;         // m/s at full wind
        [SerializeField] float acceleration = 2.2f;    // m/s^2
        [SerializeField] float minTurnRate = 6f;       // deg/s when drifting
        [SerializeField] float maxTurnRate = 26f;      // deg/s at full speed
        [SerializeField] Vector2 windDirection = new Vector2(1f, 0.35f); // where the wind blows toward
        [SerializeField, Range(0f, 1f)] float upwindPenalty = 0.65f;
        [SerializeField, Range(0f, 0.5f)] float steerageWay = 0.15f; // min drive with sails fully reefed

        [Header("Seating on the water")]
        [SerializeField] float verticalResponse = 4f;  // how quickly the hull follows wave height
        [SerializeField] float angularResponse = 2.5f; // how quickly pitch/roll follow the surface
        [SerializeField] float turnHeel = 5f;          // extra roll (deg) at full rudder + speed
        [SerializeField] Vector3 bowPoint = new Vector3(0f, 0f, 7.5f);
        [SerializeField] Vector3 portPoint = new Vector3(-2.2f, 0f, -6f);
        [SerializeField] Vector3 starboardPoint = new Vector3(2.2f, 0f, -6f);

        [Header("Visual pivots")]
        [SerializeField] Transform rudderPivot;
        [SerializeField] Transform mastPivot;
        [SerializeField] float rudderVisualAngle = 35f;

        /// -1 (full port) .. 1 (full starboard). Set by HelmInput.
        public float Rudder { get; set; }
        /// 0 (fully reefed) .. 1 (full canvas). The trim decision: more sail is
        /// faster but rougher on the crew; easing sail in chop is a skill.
        public float SailSetting { get; set; } = 1f;
        /// 0 empty .. 1 full hold. Loaded ships are slower and turn heavier.
        public float CargoLoad01 { get; set; }
        /// Mutinous crew won't give you full canvas (set by MutinyController).
        public float SailCap { get; set; } = 1f;
        /// When set, the crew has seized the helm: rudder input is ignored and
        /// the ship steers itself toward this point (mutiny stage 3+).
        public Vector3? AutopilotTarget { get; set; }
        public float CurrentSpeed => speed;
        public float Heading => heading;
        /// Actual wind at the ship this frame (WindField if present, else static).
        public Vector2 WindDirection { get; private set; } = new Vector2(0.95f, 0.33f);
        public float WindStrength { get; private set; } = 1f;
        public float GustFactor01 { get; private set; }
        public float MaxSpeed => maxSpeed;

        float heading;   // degrees, 0 = +Z
        float speed;

        void Start()
        {
            heading = transform.eulerAngles.y;
            if (rudderPivot == null) rudderPivot = transform.Find("RudderPivot");
            if (mastPivot == null) mastPivot = transform.Find("MastPivot");
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // --- Steering & speed ---
            float load = Mathf.Clamp01(CargoLoad01);
            float effMaxSpeed = maxSpeed * (1f - 0.18f * load);
            float speedFactor = Mathf.Clamp01(speed / maxSpeed);
            float turnRate = Mathf.Lerp(minTurnRate, maxTurnRate, speedFactor) * (1f - 0.25f * load);

            float effectiveRudder = Rudder;
            if (AutopilotTarget.HasValue)
            {
                Vector3 to = AutopilotTarget.Value - transform.position;
                float desiredYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                effectiveRudder = Mathf.Clamp(Mathf.DeltaAngle(heading, desiredYaw) / 20f, -1f, 1f);
            }
            heading += effectiveRudder * turnRate * dt;

            Vector3 forward = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            Vector2 posXZ = new Vector2(transform.position.x, transform.position.z);
            var windField = WindField.Instance;
            if (windField != null)
            {
                WindDirection = windField.BaseDir;
                WindStrength = windField.SampleStrength(posXZ);
                GustFactor01 = windField.GustFactor(posXZ);
            }
            else
            {
                WindDirection = windDirection.normalized;
                WindStrength = 1f;
                GustFactor01 = 0f;
            }
            Vector2 wind = WindDirection;
            float windAlignment = Vector2.Dot(new Vector2(forward.x, forward.z), wind); // -1..1
            float windFactor = Mathf.Lerp(1f - upwindPenalty, 1f, (windAlignment + 1f) * 0.5f);
            float sailPower = Mathf.Lerp(steerageWay, 1f, Mathf.Clamp01(Mathf.Min(SailSetting, SailCap)));
            float targetSpeed = effMaxSpeed * windFactor * sailPower * WindStrength;
            speed = Mathf.MoveTowards(speed, targetSpeed, acceleration * dt);

            Vector3 pos = transform.position;
            pos += forward * (speed * dt);

            // --- Seat the hull on the waves ---
            var field = WaveField.Instance;
            if (field != null)
            {
                float t = Time.time;
                Quaternion yawOnly = Quaternion.Euler(0f, heading, 0f);
                Vector3 bowW = pos + yawOnly * bowPoint;
                Vector3 portW = pos + yawOnly * portPoint;
                Vector3 starW = pos + yawOnly * starboardPoint;

                float hBow = field.SampleHeight(new Vector2(bowW.x, bowW.z), t);
                float hPort = field.SampleHeight(new Vector2(portW.x, portW.z), t);
                float hStar = field.SampleHeight(new Vector2(starW.x, starW.z), t);

                float hStern = (hPort + hStar) * 0.5f;
                float hCenter = (hBow + hStern) * 0.5f;

                float pitchDeg = Mathf.Atan2(hStern - hBow, bowPoint.z - portPoint.z) * Mathf.Rad2Deg;
                float rollDeg = Mathf.Atan2(hStar - hPort, starboardPoint.x - portPoint.x) * Mathf.Rad2Deg;
                rollDeg += effectiveRudder * speedFactor * turnHeel;
                // Gusts shove the rig: extra heel away from the wind.
                float windSide = Mathf.Sign(Vector3.Cross(forward, new Vector3(wind.x, 0f, wind.y)).y);
                rollDeg += GustFactor01 * sailPower * 4f * windSide;

                pos.y = Mathf.Lerp(pos.y, hCenter, 1f - Mathf.Exp(-verticalResponse * dt));
                Quaternion targetRot = Quaternion.Euler(pitchDeg, heading, rollDeg);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, targetRot, 1f - Mathf.Exp(-angularResponse * dt));
            }
            else
            {
                transform.rotation = Quaternion.Euler(0f, heading, 0f);
            }

            transform.position = pos;

            // --- Visual pivots ---
            if (rudderPivot != null)
                rudderPivot.localRotation = Quaternion.Euler(0f, -effectiveRudder * rudderVisualAngle, 0f);
            if (mastPivot != null)
            {
                // Trim the yards roughly square to the wind, clamped to the rig's limits.
                Vector3 windWorld = new Vector3(wind.x, 0f, wind.y);
                float windYaw = Vector3.SignedAngle(forward, windWorld, Vector3.up);
                float trim = Mathf.Clamp(windYaw * 0.5f, -40f, 40f);
                mastPivot.localRotation = Quaternion.Slerp(
                    mastPivot.localRotation, Quaternion.Euler(0f, trim, 0f),
                    1f - Mathf.Exp(-2f * dt));
            }
        }
    }
}
