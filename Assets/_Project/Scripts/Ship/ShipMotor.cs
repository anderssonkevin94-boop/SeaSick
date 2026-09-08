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
        [Tooltip("How fast she BUILDS a turn, rad/s per second. A big ship does not just turn slower, she takes longer to start.")]
        [SerializeField] float yawResponse = 4f;
        [Tooltip("Top speed astern as a fraction of ahead. Paddle wheels back badly -- the blades are shaped for one direction and the hull is not.")]
        [SerializeField, Range(0.1f, 0.8f)] float asternFraction = 0.35f;
        [Tooltip("Surf strength: multiple of gravity's pull along the surface slope. The old kinematic surfPower 22 corresponds to ~2.2 here.")]
        [SerializeField] float surfGain = 2.2f;
        [SerializeField] float surfResponse = 2.2f;

        [Header("Attitude limits (soft)")]
        [SerializeField] float pitchLimit = 16f;
        [SerializeField] float rollLimit = 20f;
        [SerializeField] float limitSpring = 6f;   // torque per deg past the limit, x inertia
        [SerializeField] float turnHeel = 5f;

        [Header("Load & freeboard")]

        [Header("Oars")]
        [SerializeField] float rowSpeed = 9f;

        [Header("Head seas")]
        [SerializeField, Range(0f, 0.8f)] float headSeaPenalty = 0.45f;

        [Header("Crew")]
        [SerializeField] float sailTrimRate = 0.55f;

        [Header("Propulsion source")]
        [Tooltip("Off for the paddle boat: she carries no sail, so the wind must not push her or drift her. The head-sea penalty stays either way — that is the WAVES, and a paddle steamer still loses way charging a sea.")]
        [SerializeField] bool windDriven = true;

        [Header("Gun recoil / knockdown")]
        [SerializeField] float knockdownTorquePerDeg = 900f;

        [Header("Visual pivots")]
        [SerializeField] Transform rudderPivot;
        [SerializeField] Transform mastPivot;

        /// Re-fit the drive to a different hull, at runtime -- the ladder
        /// swaps hulls mid-game and every number here scales with her.
        ///
        /// Top speed is deliberately NOT physics: displacement hull speed for
        /// a 46 m waterline is about 16 knots and for a 9 m boat about 7, a
        /// range of barely two. The ladder scales the steamer's authored
        /// 15 m/s by sqrt(LOA/24.2) instead, which keeps her the reference and
        /// still moves in the right direction. Same rule as WorldScale: a
        /// number may be generous, but nothing on screen may contradict it.
        public void ConfigureForHull(float loa, float railY, bool underSail,
                                     Transform sail)
        {
            HullLength = Mathf.Max(1f, loa);
            baseMaxSpeed = 15f * Mathf.Sqrt(HullLength / TunedLoa);
            windDriven = underSail;
            mastPivot = sail;

            // --- how she HANDLES, which was the same for every hull ----------
            //
            // Turn rate and acceleration were fixed constants, so a 46 m
            // first-rate came about as briskly as a 9 m fishing boat.
            //
            // **Turn rate is DERIVED, not scaled.** The first attempt scaled
            // the authored 34 deg/s by 24.2/L and gave the skiff 91 deg/s —
            // a full circle in four seconds, which is not a boat. A hull's
            // turning circle is a few times her own length, so the honest
            // quantity is the RADIUS: at speed V on a circle of radius kL she
            // turns at V/(kL). That couples her helm to her size and her speed
            // the way a real hull does, and it cannot produce a nonsense number
            // because it is a geometry, not a multiplier.
            //
            // k = 2.2 puts her turning circle at about 4.4 lengths across,
            // which is what a real sailing hull does. Measured across the
            // ladder that is 26 deg/s for the skiff down to 12 for the
            // three-decker — a spread you feel on the tiller without either
            // end being absurd.
            float L = Mathf.Max(1f, loa);
            float turnRadius = TurnCircleLengths * L;
            baseMaxTurn = baseMaxSpeed / turnRadius * Mathf.Rad2Deg;
            // With no way on she has no steerage; the rate at rest is a
            // fraction of the rate at speed, and ShipMotor already lerps
            // between them with speed.
            baseMinTurn = baseMaxTurn * 0.35f;

            // **Acceleration so that TIME to top speed grows with length.**
            // a goes as 1/sqrt(L) against the steamer, which makes V/a — the
            // number actually felt — proportional to L: about 2 s for the
            // skiff, 6 for the brig, 11 for the three-decker.
            accelScale = Mathf.Clamp(Mathf.Sqrt(TunedLoa / L), 0.6f, 2.0f);
            // She takes longer to BUILD a turn as well as to hold a smaller one.
            hullScale = accelScale;
            ApplyFit(1f, 1f, 1f);
        }

        /// Turning-circle radius in ship lengths. 2.2 means a circle 4.4
        /// lengths across, which is a real hull's.
        const float TurnCircleLengths = 2.2f;

        /// The hull every constant in this file was authored and tuned
        /// against: the paddle steamer, 24.2 m overall. One public copy,
        /// because the yard and the chase camera both need to know what the
        /// numbers were anchored to and a second private 24.2 is exactly how
        /// the steamer once ended up at a sixth of her displacement.
        public const float TunedLoa = 24.2f;

        /// Her length overall. Anything that has to FRAME or scale to the ship
        /// reads it here rather than reaching into the ladder, because a ship
        /// that never went through the yard still has a length. Defaults to
        /// the hull the numbers were tuned on.
        public float HullLength { get; private set; } = TunedLoa;
        float baseMinTurn = 15f, baseMaxTurn = 34f, accelScale = 1f;

        float baseMaxSpeed = 15f, hullScale = 1f;
        const float BaseAccel = 2.6f, BaseYawResponse = 4f;

        /// Multipliers from the FITTINGS, applied on top of the hull.
        ///
        /// Kept as a separate call so the two things stay separable: the hull
        /// decides what she can never escape being, the fittings decide what
        /// has been done about it. A bigger rudder does not make her shorter.
        public void ApplyFit(float speedMul, float turnMul, float accelMul)
        {
            maxSpeed = baseMaxSpeed * speedMul;
            rowSpeed = Mathf.Min(2.5f, maxSpeed * 0.35f);
            minTurnRate = baseMinTurn * turnMul;
            maxTurnRate = baseMaxTurn * turnMul;
            acceleration = BaseAccel * accelScale * accelMul;
            yawResponse = BaseYawResponse * hullScale;
        }

        /// What the helm actually has, for the panel to report. `MaxSpeed`
        /// already exists further down and is the same field.
        public float MaxTurnRate => maxTurnRate;
        public float AccelerationNow => acceleration;
        [SerializeField] float rudderVisualAngle = 35f;

        // ------- public API (kept compatible with the kinematic motor) -------
        /// Where the sails want to lie, and how fast they get there. Public so
        /// every mast can be trimmed from one calculation.
        /// How far below her marks she is riding, metres. Set by the yard
        /// from the loading; a MEASUREMENT now rather than three constants.
        public float SinkDepth { get; set; }

        public float SailTrimDeg { get; private set; }
        public float SailTrimBlend { get; private set; }
        public bool UnderSail => windDriven;

        public float Rudder { get; set; }
        /// Engine order and what the engine has actually reached, -1 (full
        /// astern) through 0 (stopped) to 1 (full ahead).
        ///
        /// This was SailSetting/SailOrder, furled-half-full, which never
        /// matched the boat: she is a paddle steamer with no sail and
        /// windDriven has been off in the scene all along -- PaddleDrive was
        /// already reading the "sail" as a throttle to decide how fast to
        /// turn the wheels. Renaming it is the honest half; astern is the
        /// half that adds something, because a sail cannot back up and a
        /// paddle wheel can, which is what makes coming off a beach possible.
        public float Throttle { get; private set; }
        public float ThrottleOrder { get; set; }
        public bool ThrottleMoving => !Mathf.Approximately(Throttle, ThrottleOrder);
        public float CargoLoad { get; set; }
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

        /// Which way she lies while moored, in degrees, or null to let her
        /// swing. Anchoring off a beach leaves her free to lie however the
        /// water puts her, which is right; lying against a PIER does not —
        /// a boat at a dock is parallel to it or she is fouling it. The
        /// anchor spring only ever pulled position, so heading needs its own.
        public float? MooringHeading { get; set; }

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
        /// False on the paddle boat: wind still exists in the world and still
        /// drives the sea state, it just does not act on this hull.
        public bool WindDriven => windDriven;
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

        /// How big the water under her actually is, metres of Hs.
        public float SeaHs { get; private set; }

        // Named from METRES, not from severity: severity is a blend
        // coordinate whose relation to wave height depends on where the
        // authored states sit, so a threshold in it silently changes meaning
        // every time a sea state is retuned. SeaStateController owns the
        // bands and the hysteresis; this holds its own band so the ship's
        // local water can be named independently of the global weather.
        int nameBand;
        public string SeaStateName => SeaStateController.NameForHs(SeaHs, ref nameBand);

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
            ThrottleOrder = Mathf.Clamp(ThrottleOrder, -1f, 1f);
            if (roster == null) { Throttle = ThrottleOrder; return; }
            Throttle = Mathf.MoveTowards(
                Throttle, ThrottleOrder, sailTrimRate * roster.Labour01 * dt);
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
            SeaHs = ctrl != null ? ctrl.SeaHsAt(posXZ) : 0f;
            HeadSea01 = head;
            SeaAngleDeg = angle;
            // The static rule keys the penalty on SEVERITY, which is a
            // weather coordinate: the same number for a 9 m skiff and a 46 m
            // first-rate in the same water. But whether a head sea is
            // trouble is a ratio of the sea to the HULL — Hs 5 m is spray on
            // the first-rate's bow and a wall the length of the skiff. So a
            // second rule keyed on that ratio, and she takes the worse of
            // the two: full penalty from a head sea a third of her length,
            // which for the Long boat means the sea she was measured
            // charging at 7.9 m/s and spearing (rails 4.65 freeboards under
            // at the worst, inside a wave 13.9% of the run). A sea she
            // cannot climb is a sea she must not be able to CHARGE — the
            // honest fix is her speed, because every fix tried on the
            // vertical axis either lost the race (forces) or parked her
            // inside the crests (position clamps). Measured with the rule:
            // way 7.9 -> 5.6 m/s and the worst burial nearly halved,
            // 4.65 -> 2.68 freeboards.
            float overwhelm = Mathf.Clamp01(SeaHs / Mathf.Max(1f, 0.35f * HullLength));
            SeaResistance01 = Mathf.Min(SeaResistance01,
                1f - headSeaPenalty * HeadSea01 * overwhelm);

            // Freeboard: cargo and bilge water push the whole ship down.
            float load = Mathf.Max(0f, CargoLoad);
            float over = Mathf.Max(0f, load - 1f);
            float laden = Mathf.Min(load, 1f);
            // SinkDepth is no longer AUTHORED and no longer shoves the hull
            // down. She is heavier, so she floats lower — `ShipLoad` puts real
            // mass on the rigidbody and `BuoyantBody` settles her until she
            // displaces her own weight. What is left here is the reporting
            // number, which the yard sets from the loading, and a seat offset
            // of zero because nothing needs faking any more.
            buoyant.SeatOffset = 0f;

            // Green water: rails are buoyancy probes; their submersion is
            // already measured every physics step.
            RailImmersion = Anchored ? 0f : Mathf.Max(0f, buoyant.MaxRailImmersion);

            // Drift: the water mass itself moving (Stokes drift scaled by sea).
            // A hull with no sail still sits in moving water, but this term is
            // the wind's push on the ship and the paddle boat is exempt.
            WaterVelocity = windDriven
                ? new Vector3(WindDirection.x, 0f, WindDirection.y) * (waveDrift * SeaSeverity01)
                : Vector3.zero;
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
            if (windDriven)
            {
                // Computed whether or not there is a pivot here to apply it to.
                // A ship with three masts has three sails, each of which turns
                // about its OWN mast — one Transform field cannot express that,
                // so `SailRig` reads these two numbers and drives them all.
                Vector3 windWorld = new Vector3(WindDirection.x, 0f, WindDirection.y);
                float windYaw = Vector3.SignedAngle(Flat(transform.forward), windWorld, Vector3.up);
                float strain = HeadSea01 * SeaSeverity01;
                SailTrimDeg = Mathf.Clamp(windYaw * 0.5f, -70f, 70f)
                              + Mathf.Sin(Time.time * 16f) * 10f * strain;
                SailTrimBlend = 1f - Mathf.Exp(-(strain > 0.35f ? 7f : 2f) * dt);
                if (mastPivot != null)
                    mastPivot.localRotation = Quaternion.Slerp(
                        mastPivot.localRotation,
                        Quaternion.Euler(0f, SailTrimDeg, 0f), SailTrimBlend);
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
            av.y = Mathf.MoveTowards(av.y, targetYawRate, yawResponse * dt);
            rb.angularVelocity = av;

            // Turn heel: rudder + speed lays her over into the turn.
            rb.AddTorque(forward * (-effectiveRudder * speedFactor * turnHeel
                * 0.15f * RollInertia()), ForceMode.Force);

            // --- propulsion toward the engine's target speed ---
            //
            // No steerage-way floor any more. A furled sail still let her
            // ghost along at steerageWay of full, which is right for canvas
            // and wrong for an engine: stop has to mean stop, or she cannot
            // be held off a beach.
            float demand = Mathf.Clamp(Throttle, -1f, 1f);
            float power = demand >= 0f ? demand : demand * asternFraction;
            float targetSpeed = effMaxSpeed * power * SeaResistance01;
            if (Rowing && !Anchored && demand >= 0f)
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

                // Spring her head round to the berth's heading, damped on her
                // actual yaw rate. Critically damped-ish rather than snapped:
                // she is a floating body and a mooring line pulls, it does not
                // teleport.
                if (MooringHeading.HasValue)
                {
                    float err = Mathf.DeltaAngle(transform.eulerAngles.y, MooringHeading.Value);
                    rb.AddTorque(Vector3.up * ((err * Mathf.Deg2Rad * 0.9f
                        - rb.angularVelocity.y * 1.6f) * RollInertia()), ForceMode.Force);
                }
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
