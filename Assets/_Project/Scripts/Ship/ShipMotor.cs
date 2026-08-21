using SeaSick.Ocean;
using UnityEngine;

namespace SeaSick.Ship
{
    /// Physical sailing model: the hull is a Rigidbody riding BuoyantBody
    /// probe forces (heave, pitch, roll, surf and broach all emerge from the
    /// water), while this class supplies what a hull can't feel — propulsion,
    /// keel grip, steering, soft attitude limits, and the gameplay-facing
    /// derived state every other system reads. The public surface is kept
    /// byte-compatible with the old kinematic motor so Bilge, SpeedJuice,
    /// combat, crew and UI compile untouched.
    [RequireComponent(typeof(Rigidbody), typeof(BuoyantBody), typeof(BuoyancyProbeSet))]
    public class ShipMotor : MonoBehaviour
    {
        [Header("Sailing")]
        [SerializeField] float maxSpeed = 18f;
        [SerializeField] float acceleration = 2.6f;
        [SerializeField] float keelGrip = 2.2f;        // /s decay of sideways slip
        [SerializeField] float surfOvershoot = 1.25f;
        [SerializeField] float overspeedDragScale = 0.35f;
        [SerializeField] float waveDrift = 1f;
        [SerializeField] float minTurnRate = 15f;
        [SerializeField] float maxTurnRate = 34f;
        [SerializeField, Range(0f, 0.5f)] float steerageWay = 0.12f;
        [Tooltip("Surf strength: multiple of gravity's pull along the surface slope. The old kinematic surfPower 22 corresponds to ~2.2 here.")]
        [SerializeField] float surfGain = 2.2f;
        [SerializeField] float surfResponse = 2.2f;

        [Header("Attitude limits (soft)")]
        [SerializeField] float pitchLimit = 16f;
        [SerializeField] float rollLimit = 20f;
        [SerializeField] float limitSpring = 6f;   // torque per deg past the limit, x inertia
        [SerializeField] float turnHeel = 5f;

        [Header("Load & freeboard")]
        [SerializeField] float sinkAtMarkedLine = 0.50f;
        [SerializeField] float sinkPerOverload = 0.72f;
        [SerializeField] float sinkAtFullBilge = 0.42f;

        [Header("Oars")]
        [SerializeField] float rowSpeed = 9f;

        [Header("Head seas")]
        [SerializeField, Range(0f, 0.8f)] float headSeaPenalty = 0.45f;

        [Header("Crew")]
        [SerializeField] float sailTrimRate = 0.55f;

        [Header("Gun recoil / knockdown")]
        [SerializeField] float knockdownTorquePerDeg = 900f;

        [Header("Visual pivots")]
        [SerializeField] Transform rudderPivot;
        [SerializeField] Transform mastPivot;
        [SerializeField] float rudderVisualAngle = 35f;

        // ------- public API (kept compatible with the kinematic motor) -------
        public float Rudder { get; set; }
        public float SailSetting { get; private set; } = 1f;
        public float SailOrder { get; set; } = 1f;
        public bool Trimming => !Mathf.Approximately(SailSetting, SailOrder);
        public float CargoLoad { get; set; }
        public float SinkDepth { get; private set; }
        public float RailImmersion { get; private set; }
        public float BilgeLoad01 { get; set; }

        public bool Anchored
        {
            get => anchored;
            set
            {
                if (value && !anchored) anchorPoint = transform.position;
                anchored = value;
            }
        }
        bool anchored;
        Vector3 anchorPoint;

        /// Where the anchor spring pulls. Berthing walks this point toward the
        /// beach instead of writing the transform — physics does the moving.
        public Vector3 AnchorPoint { get => anchorPoint; set => anchorPoint = value; }

        public bool Rowing { get; set; }
        public float RowSpeed => rowSpeed;
        public float OarPower01 => roster != null ? roster.Labour01 : 1f;
        public Vector3? AutopilotTarget { get; set; }
        public float CurrentSpeed { get; private set; }
        public float Heading => transform.eulerAngles.y;
        public Vector2 WindDirection { get; private set; } = new Vector2(0.95f, 0.33f);
        public float WindStrength { get; private set; } = 1f;
        public float GustFactor01 { get; private set; }
        public float MaxSpeed => maxSpeed;
        /// The whole propulsion budget: x mass is the most force the sail can
        /// ever apply, and any water force above that wins outright.
        public float Acceleration => acceleration;
        public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;
        public float SurfAccel { get; private set; }
        public float SurfBoost01 => Mathf.Clamp01(SurfAccel / 3.5f);
        public float LateralWaveAccel { get; private set; }
        public float SeaAngleDeg { get; private set; } = 90f;
        public float SeaSeverity01 { get; private set; }
        public float HeadSea01 { get; private set; }
        public float SeaResistance01 { get; private set; } = 1f;
        public Vector3 WaterVelocity { get; private set; }
        public float KnockdownRoll { get; private set; }

        public string SeaStateName =>
            SeaSeverity01 < 0.2f ? "calm"
            : SeaSeverity01 < 0.45f ? "lively"
            : SeaSeverity01 < 0.7f ? "heavy"
            : SeaSeverity01 < 0.9f ? "wild"
            : "mountainous";

        public float DriftAngleDeg =>
            CurrentSpeed < 0.5f ? 0f : Vector3.SignedAngle(
                Flat(transform.forward), Flat(rb.linearVelocity), Vector3.up);

        public float CourseFor(Vector3 target, Vector2 wind)
        {
            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 1f) return Heading;
            return Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
        }

        /// A wall of water taking her way off in one go.
        public void ScrubWay(float fraction01)
        {
            Vector3 fwd = Flat(transform.forward);
            float along = Vector3.Dot(rb.linearVelocity, fwd);
            rb.linearVelocity -= fwd * (along * Mathf.Clamp01(fraction01));
        }

        public void KillVelocityAlong(Vector3 outwardNormal)
        {
            float into = Vector3.Dot(rb.linearVelocity, outwardNormal);
            if (into < 0f) rb.linearVelocity -= outwardNormal * into;
        }

        /// Lay her over hard and let the sea bring her back: a roll torque
        /// profile over `seconds`, allowed past the normal soft roll limit.
        public void Knockdown(float degrees, float seconds = 2.6f)
        {
            knockdownDeg = degrees;
            knockdownLeft = knockdownTotal = Mathf.Max(0.2f, seconds);
        }
        float knockdownDeg, knockdownLeft, knockdownTotal = 1f;

        /// Broadside recoil: an instant roll-rate kick; the water damps it out
        /// over the next second or two, overshooting once — which is the feel.
        public void AddRecoilRoll(float degreesPerSecond)
        {
            Vector3 axis = Flat(transform.forward).normalized;
            rb.AddTorque(axis * (RollInertia() * degreesPerSecond * Mathf.Deg2Rad),
                ForceMode.Impulse);
        }

        /// The whole heading-vs-sea model in one place, so raiders sail the
        /// same water the player does. Kept static with the old signature.
        public static float SeaResistanceAt(Vector2 pos, Vector3 forward, float penalty,
            out float severity, out float headSea, out float angleDeg)
        {
            severity = 0f; headSea = 0f; angleDeg = 90f;
            var ctrl = SeaStateController.Instance;
            if (ctrl == null) return 1f;
            severity = ctrl.SeaSeverityAt(pos);
            Vector2 run = ctrl.WindDirection;
            Vector3 seasFrom = new Vector3(-run.x, 0f, -run.y);
            angleDeg = Vector3.Angle(new Vector3(forward.x, 0f, forward.z), seasFrom);
            headSea = Mathf.Clamp01(Mathf.Cos(angleDeg * Mathf.Deg2Rad));
            return 1f - penalty * headSea * severity;
        }

        // ------------------------- internals -------------------------
        Rigidbody rb;
        BuoyantBody buoyant;
        HullIntegrity hull;
        Crew.CrewRoster roster;
        Vector3 smoothedWaveForce;
        float gustPhase;

        void Start()
        {
            rb = GetComponent<Rigidbody>();
            buoyant = GetComponent<BuoyantBody>();
            hull = GetComponent<HullIntegrity>();
            roster = GetComponent<Crew.CrewRoster>();
            if (rudderPivot == null) rudderPivot = transform.Find("RudderPivot");
            if (mastPivot == null) mastPivot = transform.Find("MastPivot");
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        float RollInertia()
        {
            Vector3 axis = transform.InverseTransformDirection(Flat(transform.forward));
            Vector3 it = rb.inertiaTensor;
            return Mathf.Abs(axis.x * it.x) + Mathf.Abs(axis.y * it.y) + Mathf.Abs(axis.z * it.z);
        }

        void TrimSails(float dt)
        {
            if (roster == null) { SailSetting = SailOrder; return; }
            SailSetting = Mathf.MoveTowards(
                SailSetting, SailOrder, sailTrimRate * roster.Labour01 * dt);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            TrimSails(dt);

            // Weather-facing state.
            var ctrl = SeaStateController.Instance;
            Vector2 posXZ = new Vector2(transform.position.x, transform.position.z);
            if (ctrl != null)
            {
                WindDirection = ctrl.WindDirection;
                WindStrength = 0.7f + 0.5f * ctrl.Severity01;
                gustPhase += dt;
                GustFactor01 = Mathf.PerlinNoise(gustPhase * 0.15f, 3.7f) * ctrl.Storminess01;
            }
            SeaResistance01 = SeaResistanceAt(posXZ, transform.forward, headSeaPenalty,
                out float sev, out float head, out float angle);
            SeaSeverity01 = sev;
            HeadSea01 = head;
            SeaAngleDeg = angle;

            // Freeboard: cargo and bilge water push the whole ship down.
            float load = Mathf.Max(0f, CargoLoad);
            float over = Mathf.Max(0f, load - 1f);
            float laden = Mathf.Min(load, 1f);
            SinkDepth = sinkAtMarkedLine * laden
                + sinkPerOverload * over
                + sinkAtFullBilge * Mathf.Clamp01(BilgeLoad01);
            buoyant.SeatOffset = SinkDepth;

            // Green water: rails are buoyancy probes; their submersion is
            // already measured every physics step.
            RailImmersion = Anchored ? 0f : Mathf.Max(0f, buoyant.MaxRailImmersion);

            // Drift: the water mass itself moving (Stokes drift scaled by sea).
            WaterVelocity = new Vector3(WindDirection.x, 0f, WindDirection.y)
                * (waveDrift * SeaSeverity01);
            buoyant.AmbientFlow = Anchored ? Vector3.zero : WaterVelocity;

            CurrentSpeed = Flat3(rb.linearVelocity).magnitude;

            // Knockdown bookkeeping for the HUD.
            float roll = Signed(transform.eulerAngles.z);
            KnockdownRoll = knockdownLeft > 0f
                ? Mathf.Max(0f, Mathf.Abs(roll) - rollLimit) : 0f;

            // Visual pivots.
            float effectiveRudder = EffectiveRudder();
            if (rudderPivot != null)
                rudderPivot.localRotation = Quaternion.Euler(0f, -effectiveRudder * rudderVisualAngle, 0f);
            if (mastPivot != null)
            {
                Vector3 windWorld = new Vector3(WindDirection.x, 0f, WindDirection.y);
                float windYaw = Vector3.SignedAngle(Flat(transform.forward), windWorld, Vector3.up);
                float strain = HeadSea01 * SeaSeverity01;
                float trim = Mathf.Clamp(windYaw * 0.5f, -70f, 70f)
                             + Mathf.Sin(Time.time * 16f) * 10f * strain;
                float blend = 1f - Mathf.Exp(-(strain > 0.35f ? 7f : 2f) * dt);
                mastPivot.localRotation = Quaternion.Slerp(
                    mastPivot.localRotation, Quaternion.Euler(0f, trim, 0f), blend);
            }
        }

        static Vector3 Flat3(Vector3 v) => new Vector3(v.x, 0f, v.z);
        static float Signed(float a) => a > 180f ? a - 360f : a;

        float EffectiveRudder()
        {
            if (Anchored) return 0f;
            if (AutopilotTarget.HasValue)
            {
                float desiredYaw = CourseFor(AutopilotTarget.Value, WindDirection);
                return Mathf.Clamp(Mathf.DeltaAngle(Heading, desiredYaw) / 20f, -1f, 1f);
            }
            return Rudder;
        }

        void FixedUpdate()
        {
            if (rb == null) return;
            float dt = Time.fixedDeltaTime;
            float mass = rb.mass;

            float load = Mathf.Max(0f, CargoLoad);
            float over = Mathf.Max(0f, load - 1f);
            float laden = Mathf.Min(load, 1f);
            float heaviness = 1f / (1f + 0.55f * laden + 1.05f * over);

            float effMaxSpeed = maxSpeed * (1f - 0.10f * laden - 0.14f * over);
            if (hull != null) effMaxSpeed *= hull.SpeedMultiplier;

            Vector3 forward = Flat(transform.forward);
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            Vector3 through = rb.linearVelocity - WaterVelocity;
            float forwardWay = Vector3.Dot(through, forward);
            float sideWay = Vector3.Dot(through, right);
            float speedFactor = Mathf.Clamp01(CurrentSpeed / maxSpeed);

            // --- steering: yaw is a control axis, pitch/roll stay physical ---
            float effectiveRudder = EffectiveRudder();
            float turnRate = Mathf.Lerp(minTurnRate, maxTurnRate, speedFactor)
                * (1f - 0.15f * laden - 0.20f * over);
            Vector3 av = rb.angularVelocity;
            float targetYawRate = effectiveRudder * turnRate * Mathf.Deg2Rad;
            av.y = Mathf.MoveTowards(av.y, targetYawRate, 4f * dt);
            rb.angularVelocity = av;

            // Turn heel: rudder + speed lays her over into the turn.
            rb.AddTorque(forward * (-effectiveRudder * speedFactor * turnHeel
                * 0.15f * RollInertia()), ForceMode.Force);

            // --- propulsion toward the sail-set target speed ---
            float sailPower = Mathf.Lerp(steerageWay, 1f, Mathf.Clamp01(SailSetting));
            float targetSpeed = effMaxSpeed * sailPower * SeaResistance01;
            if (Rowing && !Anchored)
                targetSpeed = Mathf.Max(targetSpeed,
                    rowSpeed * (1f - 0.22f * laden - 0.30f * over) * OarPower01);
            if (Anchored) targetSpeed = 0f;

            float pull = forwardWay > targetSpeed
                ? acceleration * overspeedDragScale
                : acceleration;
            if (Anchored) pull = acceleration * 2.5f;
            float dv = Mathf.Clamp(targetSpeed - forwardWay,
                -pull * heaviness * dt, pull * heaviness * dt);
            rb.AddForce(forward * (dv / dt * mass), ForceMode.Force);

            // Hard ceiling so a surf run can't build without limit.
            float ceiling = effMaxSpeed * surfOvershoot;
            if (forwardWay > ceiling)
                rb.AddForce(forward * ((ceiling - forwardWay) / dt * mass * 0.5f), ForceMode.Force);

            // --- keel grip: bleed sideways slip so she carves, not skates ---
            float grip = Anchored ? keelGrip * 3f : keelGrip * heaviness;
            rb.AddForce(-right * (sideWay * grip * mass), ForceMode.Force);

            // --- surf: gravity pulling the hull along the surface slope ---
            // Derived from the sampled surface normal, NOT from the buoyancy
            // drag forces (those read as a brake at speed, and amplifying them
            // measured as a 4 kN anti-propulsion force). Running down a face
            // pulls her forward; climbing costs; a beam sea shoves sideways.
            Vector3 surfForce = Vector3.zero;
            if (!Anchored && Ocean.OceanSampler.Ready)
            {
                Vector3 n = Ocean.OceanSampler.SampleImmediate(transform.position).normal;
                if (n.y > 0.2f)
                {
                    Vector3 downSlope = new Vector3(n.x, 0f, n.z) / n.y;
                    Vector3 accel = downSlope * surfGain * 9.81f;
                    accel = Vector3.ClampMagnitude(accel, 4.5f);
                    surfForce = accel * mass * buoyant.Submersion;
                    rb.AddForce(surfForce, ForceMode.Force);
                }
            }
            smoothedWaveForce = Vector3.Lerp(smoothedWaveForce, surfForce,
                1f - Mathf.Exp(-surfResponse * dt));
            SurfAccel = Vector3.Dot(smoothedWaveForce, forward) / mass;
            LateralWaveAccel = Vector3.Dot(smoothedWaveForce, right) / mass;

            // --- anchor: spring back to the drop point, heave stays free ---
            if (Anchored)
            {
                Vector3 toAnchor = Flat3(anchorPoint - transform.position);
                rb.AddForce(toAnchor * (mass * 0.4f) - Flat3(rb.linearVelocity) * (mass * 0.8f),
                    ForceMode.Force);
            }

            // --- soft attitude limits (replace the old hard clamps) ---
            float pitch = Signed(transform.eulerAngles.x);
            float roll = Signed(transform.eulerAngles.z);
            float rollCap = knockdownLeft > 0f ? 78f : rollLimit;
            if (Mathf.Abs(pitch) > pitchLimit)
                rb.AddTorque(right * (-(pitch - Mathf.Sign(pitch) * pitchLimit)
                    * limitSpring * Mathf.Deg2Rad * RollInertia()), ForceMode.Force);
            if (Mathf.Abs(roll) > rollCap)
                rb.AddTorque(forward * (-(roll - Mathf.Sign(roll) * rollCap)
                    * limitSpring * Mathf.Deg2Rad * RollInertia()), ForceMode.Force);

            // --- knockdown: a decaying roll torque that beats the limits ---
            if (knockdownLeft > 0f)
            {
                knockdownLeft = Mathf.Max(0f, knockdownLeft - dt);
                float tt = knockdownLeft / knockdownTotal;
                rb.AddTorque(forward * (knockdownDeg * knockdownTorquePerDeg
                    * Mathf.Sin(tt * Mathf.PI)), ForceMode.Force);
            }
        }
    }
}
