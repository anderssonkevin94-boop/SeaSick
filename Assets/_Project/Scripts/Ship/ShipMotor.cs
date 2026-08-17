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
        [SerializeField] float maxSpeed = 18f;         // m/s at full wind
        [SerializeField] float acceleration = 2.6f;    // m/s^2
        [SerializeField] float keelGrip = 2.2f;        // /s decay of sideways slip; lower = driftier

        [Header("Wave riding")]
        [Tooltip("How hard gravity pulls the hull down a wave face. This is what makes the ocean terrain.")]
        [SerializeField] float surfPower = 22f;
        [SerializeField] float surfSampleDistance = 16f;
        [Tooltip("How quickly the surf pull follows the slope. Lower = smoother.")]
        [SerializeField] float surfResponse = 2.2f;
        [Tooltip("Surfing can carry you past normal top speed by this factor.")]
        [SerializeField] float surfOvershoot = 1.25f;
        [SerializeField] float overspeedDragScale = 0.35f;
        [SerializeField] float minTurnRate = 15f;      // deg/s when barely moving — always escapable
        [SerializeField] float maxTurnRate = 34f;      // deg/s at full speed
        [SerializeField] Vector2 windDirection = new Vector2(1f, 0.35f); // where the wind blows toward
        [SerializeField, Range(0f, 0.5f)] float steerageWay = 0.12f; // min drive with sails furled

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
        /// Anchored: no thrust, no steering, but the hull still rides the waves.
        public bool Anchored { get; set; }
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
        public Vector3 Velocity => velocity;
        /// Positive when running down a wave face, negative climbing one.
        /// Drives camera punch, spray, and audio — the feel of catching a wave.
        public float SurfAccel { get; private set; }
        public float SurfBoost01 => Mathf.Clamp01(SurfAccel / 3.5f);

        /// Degrees between the bow and the direction the wind blows FROM.
        /// 0 = pointing straight into the wind, 180 = dead downwind.
        public float WindAngleDeg { get; private set; } = 90f;
        /// How badly the sails are shaking: 1 in irons, 0 once they draw.
        public float Luff01 { get; private set; }
        public bool InIrons => WindAngleDeg < NoGoDegrees;
        /// Drive available from the current heading, before sail setting.
        public float PolarEfficiency { get; private set; } = 1f;

        public const float NoGoDegrees = 35f;

        public string PointOfSailName =>
            WindAngleDeg < 20f ? "in irons"
            : WindAngleDeg < NoGoDegrees ? "pinching"
            : WindAngleDeg < 60f ? "close hauled"
            : WindAngleDeg < 105f ? "beam reach"
            : WindAngleDeg < 150f ? "broad reach"
            : "running";

        /// Sailing polar. Upwind is genuinely slow and worth tacking out of,
        /// but never a trap: even head-to-wind you keep enough drive to steer
        /// out and get home. Peak is on a beam-to-broad reach.
        public static float SailPolar(float thetaDeg)
        {
            if (thetaDeg < 20f) return 0.22f;
            if (thetaDeg < 35f) return Mathf.Lerp(0.22f, 0.55f, (thetaDeg - 20f) / 15f);
            if (thetaDeg < 50f) return Mathf.Lerp(0.55f, 0.85f, (thetaDeg - 35f) / 15f);
            if (thetaDeg < 75f) return Mathf.Lerp(0.85f, 0.97f, (thetaDeg - 50f) / 25f);
            if (thetaDeg < 110f) return Mathf.Lerp(0.97f, 1.00f, (thetaDeg - 75f) / 35f);
            if (thetaDeg < 150f) return Mathf.Lerp(1.00f, 0.92f, (thetaDeg - 110f) / 40f);
            return Mathf.Lerp(0.92f, 0.80f, (thetaDeg - 150f) / 30f);
        }

        /// Used by grounding: cancel the component of momentum driving the
        /// hull into the rock, keeping whatever slides along the shore.
        public void KillVelocityAlong(Vector3 outwardNormal)
        {
            float into = Vector3.Dot(velocity, outwardNormal);
            if (into < 0f) velocity -= outwardNormal * into;
            speed = velocity.magnitude;
        }
        /// Signed angle between where the hull points and where it's actually
        /// moving — the visible drift. ~0 in a straight line, spikes in turns.
        public float DriftAngleDeg =>
            speed < 0.5f ? 0f : Vector3.SignedAngle(
                Quaternion.Euler(0f, heading, 0f) * Vector3.forward, velocity, Vector3.up);

        float heading;   // degrees, 0 = +Z
        float speed;     // |velocity|, for HUD/camera/turn-rate
        Vector3 velocity; // world-space; decoupled from heading so the hull can slide

        HullIntegrity hull;

        void Start()
        {
            hull = GetComponent<HullIntegrity>();
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
            if (hull != null) effMaxSpeed *= hull.SpeedMultiplier;
            float speedFactor = Mathf.Clamp01(speed / maxSpeed);
            float turnRate = Mathf.Lerp(minTurnRate, maxTurnRate, speedFactor) * (1f - 0.25f * load);

            float effectiveRudder = Anchored ? 0f : Rudder;
            if (!Anchored && AutopilotTarget.HasValue)
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

            // Points of sail: measure the bow against where the wind comes FROM.
            Vector3 windFromWorld = new Vector3(-wind.x, 0f, -wind.y);
            WindAngleDeg = Vector3.Angle(new Vector3(forward.x, 0f, forward.z), windFromWorld);
            PolarEfficiency = SailPolar(WindAngleDeg);
            Luff01 = Mathf.Clamp01(1f - (WindAngleDeg - 12f) / 23f);

            float sailPower = Mathf.Lerp(steerageWay, 1f, Mathf.Clamp01(Mathf.Min(SailSetting, SailCap)));
            float targetSpeed = effMaxSpeed * PolarEfficiency * sailPower * WindStrength;

            // Sea-of-Thieves-style carve: thrust builds along the hull, but
            // momentum keeps its own direction. Turning converts forward way
            // into sideways slip, which the keel bleeds off over ~half a
            // second — so the stern slides out and you move THROUGH the water.
            if (Anchored) targetSpeed = 0f;

            // --- Wave riding ---
            // Sample the surface just ahead of the bow: running downhill pulls
            // the hull forward, climbing a face bleeds momentum. This is what
            // turns the sea into terrain you steer across rather than through.
            float rawSurf = 0f;
            var waveField = Ocean.WaveField.Instance;
            if (waveField != null && !Anchored)
            {
                Vector3 p = transform.position;
                Vector2 here = new Vector2(p.x, p.z);
                Vector2 ahead = here + new Vector2(forward.x, forward.z) * surfSampleDistance;
                float slope = (waveField.SampleHeight(ahead, Time.time)
                             - waveField.SampleHeight(here, Time.time)) / surfSampleDistance;
                rawSurf = -slope * surfPower;
            }
            // Smoothed so the pull swells and fades like a real wave rather
            // than jittering frame to frame.
            SurfAccel = Mathf.Lerp(SurfAccel, rawSurf, 1f - Mathf.Exp(-surfResponse * dt));

            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            float forwardWay = Vector3.Dot(velocity, forward);
            float sideWay = Vector3.Dot(velocity, right);

            // Drag toward the wind-driven target is gentler above it, so a
            // surf boost carries for a while instead of evaporating.
            float pull = forwardWay > targetSpeed
                ? acceleration * overspeedDragScale
                : acceleration;
            forwardWay = Mathf.MoveTowards(forwardWay, targetSpeed,
                (Anchored ? acceleration * 2.5f : pull) * dt);
            forwardWay += SurfAccel * dt;
            forwardWay = Mathf.Clamp(forwardWay, -2f, effMaxSpeed * surfOvershoot);

            sideWay *= Mathf.Exp(-(Anchored ? keelGrip * 3f : keelGrip) * dt);
            velocity = forward * forwardWay + right * sideWay;
            speed = velocity.magnitude;

            Vector3 pos = transform.position;
            pos += velocity * dt;

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
                Vector3 windWorld = new Vector3(wind.x, 0f, wind.y);
                float windYaw = Vector3.SignedAngle(forward, windWorld, Vector3.up);
                float trim;
                float blend;
                if (Luff01 > 0.35f)
                {
                    // Luffing: the yards weathervane and the canvas shakes —
                    // the clearest possible signal that you're pinching.
                    trim = Mathf.Clamp(windYaw, -80f, 80f)
                           + Mathf.Sin(Time.time * 16f) * 14f * Luff01;
                    blend = 1f - Mathf.Exp(-7f * dt);
                }
                else
                {
                    trim = Mathf.Clamp(windYaw * 0.5f, -70f, 70f);
                    blend = 1f - Mathf.Exp(-2f * dt);
                }
                mastPivot.localRotation = Quaternion.Slerp(
                    mastPivot.localRotation, Quaternion.Euler(0f, trim, 0f), blend);
            }
        }
    }
}
