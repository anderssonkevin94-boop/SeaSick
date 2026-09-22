using SeaSick.Ocean;
using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Steamer
{
    /// One stern wheel and a rudder, as real forces on the rigidbody.
    ///
    /// `ShipMotor` drives a hull by servo: it ASSIGNS a yaw rate and pulls the
    /// speed toward a target, which is right for a ladder of twenty hulls that
    /// all have to feel authored and wrong for a ship whose whole character is
    /// her machinery. Here nothing is assigned. The wheel is a shaft with
    /// inertia, a torque-limited engine and a governor; thrust is what the
    /// floats do to the water they are actually standing in; and the top speed
    /// is not a number she is held to but the speed at which that thrust meets
    /// `HullFormBody`'s surge resistance. A wheel rolled clear of the sea
    /// loses its load and races, and one buried to the axle lugs -- neither of
    /// which is a special case below.
    ///
    /// STEERING IS THE ONE THING THE STERN WHEEL CHANGES. Two side wheels
    /// could be turned against each other, and with the telegraph at stop she
    /// pivoted in her own length on the differential alone. One wheel on the
    /// centreline has no differential to give. What it has instead is the
    /// reason stern-wheelers put their rudders where they do: the wheel drags
    /// a race of water past the blades, and the rudder stands in it. So the
    /// rudder here is not given the speed the hull is making through the
    /// water, it is given that PLUS the wheel's slip -- which is largest
    /// exactly when she has no way on. Ring her ahead with the helm over and
    /// she comes round on the spot; drift with the engine stopped and the
    /// helm does nothing, which is true of the real thing and is the honest
    /// price of the single wheel.
    ///
    /// The motor stays on the ship as the helm's inputs and the instruments'
    /// outputs (`ExternalDrive`), so HelmInput, the HUD, SpeedJuice, the crew
    /// and the anchor are untouched. Contract: docs/steamer-spec.md section 5.
    ///
    /// Runs at -70: after the probe feeders (-100), the ocean driver that
    /// stamps the samples (-90) and the hull (-80), so every number read here
    /// belongs to THIS physics step.
    [DefaultExecutionOrder(-70)]
    public class PaddleDrive : MonoBehaviour
    {
        /// The authored top speed, for whoever has to fit the motor's gauges
        /// before this component exists on the ship.
        public const float DefaultTopSpeed = 15.5f;

        [Header("Engine")]
        [Tooltip("Speed through the water, m/s, at which full-ahead thrust meets the hull's surge resistance. The thrust constant is SOLVED from this, so retuning the hull's resistance does not move it.")]
        [SerializeField] float topSpeed = DefaultTopSpeed;
        [Tooltip("Wheel slip at top speed: the floats' rim speed is this fraction faster than the water going past. Sets how fast the wheel turns, which is what the eye and the ear get.")]
        [SerializeField, Range(0.05f, 0.6f)] float slipAtTop = 0.25f;
        [Tooltip("Acceleration from rest with the wheel at full torque, m/s^2. This is the engine's torque limit expressed as the thing it is felt as.")]
        [SerializeField] float bollardAccel = 2.6f;
        [Tooltip("Seconds for an unloaded wheel to reach full revolutions. Sets the shaft inertia: long enough that a wheel lifted clear by a pitch is heard to race, short enough that the telegraph is not mush.")]
        [SerializeField] float spinUpSeconds = 1.4f;
        [Tooltip("Governor droop: the fraction of full revolutions below the order at which the engine reaches full torque. Small is a stiff governor. Floored at 0.02 because the shaft is stepped explicitly at 50 Hz and a stiffer one rings.")]
        [SerializeField, Range(0.02f, 0.2f)] float governorBand = 0.04f;

        [Header("Steering")]
        [Tooltip("Rudder force multiplier on top of the flat-plate lift it is derived from.")]
        [SerializeField] float rudderGain = 1.35f;
        [SerializeField] float rudderMaxDeg = 35f;
        [Tooltip("How much of the rudder's drawn depth below the centre of mass its force acts at. 1 is the geometry. Counter-intuitively this wants to be LARGE on her: the blade leans her INTO the turn and the hull's cross-flow leans her OUT of it, and on a hull this broad the second is the bigger of the two, so the blade's lever is what is left to pay for it. Dropping this to 0.35 to 'lean her less' measured 8.1 degrees of heel where 0.6 measured 5.6. THAT BALANCE HAS SINCE FLIPPED: with the rudder cut to a real blade's area (SteamerBootstrap.RudderAreaFraction) it makes ~13 kN, not 225, so the hull's cross-flow now wins and she heels about 4 degrees OUTBOARD in a hard turn -- which is what a launch does. This lever is kept high so the blade takes the edge off it rather than because it is fighting for the other side.")]
        [SerializeField, Range(0f, 1f)] float rudderHeelLever = 0.9f;
        [Tooltip("How much of the WHEEL'S SLIP the rudder feels on top of the water already going past it. This is the whole of her low-speed handling: at rest the slip IS the rim speed, so a turn from standstill is bought with the telegraph, not the helm. 0.80 (was 0.55): with the blade cut to a real rudder's area her circle became almost speed-independent in metres, and the race is the only term that gives the slow end back its bite, so a camp approach at slow ahead turns in about the same water as a run at full.")]
        [SerializeField, Range(0f, 1f)] float raceGain = 0.80f;
        /// Full astern as a fraction of full ahead revs. Feathering floats
        /// bite as well backwards as forwards, so without this she measured
        /// as fast astern as ahead, stern-first into her own counter. Astern
        /// is for coming off a beach and for stopping.
        [SerializeField, Range(0.1f, 1f)] float asternFraction = 0.45f;

        [Header("Visuals")]
        [Tooltip("Ceiling on how fast the wheel is DRAWN turning, rad/s. At full ahead the real rate is well past 10 rad/s and eight floats strobe against the frame rate and read as standing still or running backwards. The drawn rate follows the true one at low speed and saturates here.")]
        [SerializeField] float visualRateCap = 3.2f;
        [Tooltip("Radius of the mark the wheel leaves in the ripple sim, metres.")]
        [SerializeField] float wakeRadius = 3.2f;
        [Tooltip("Foam and trough a wheel at full bollard thrust stamps per second.")]
        [SerializeField] float wakeFoam = 1.1f;
        [SerializeField] float wakeDisplace = 0.36f;

        const float Rho = 1025f;
        // Flat-plate lift slope for the rudder, per the contract.
        const float RudderLift = 2.4f;
        // Fore and aft of the axle, metres: two points so the dip is the mean
        // over the part of the sea the wheel is actually in, not one spike.
        const float ProbeSpread = 0.8f;

        HullFormData data;
        HullFormBody body;
        ShipMotor motor;
        Rigidbody rb;
        Transform wheel;
        float omega, dip, thrust, visualAngle;

        readonly OceanProbeRegistry.Handle[] handles = new OceanProbeRegistry.Handle[2];
        readonly Vector3[] probeWorld = new Vector3[2];
        bool probesWritten;

        // Derived every step from the tunables, so a probe can move any of
        // them live and the top speed still comes out where it was asked for.
        float reff, omegaMax, tauMax, shaftJ, governorKp, thrustK;

        // ------------------------------ read surface ------------------------------
        /// True shaft rate, rad/s, positive driving her ahead. Honest: the
        /// drawn wheel is geared down, this is not.
        public float WheelRate => omega;
        /// How deep the lowest float is, hull-local metres. Design is
        /// `wheelDesignDip`; negative is a wheel in the air.
        public float WheelDip => dip;
        /// Signed thrust along the keel this step, newtons.
        public float WheelThrustN => thrust;
        /// Water speed the rudder is actually working in, m/s: her way through
        /// the water plus the wheel's race. This, not her speed, is what the
        /// helm has to bite on.
        public float RudderInflow { get; private set; }
        /// Signed athwart force at the rudder this step, newtons; positive is
        /// the stern being pushed to port (bow to starboard).
        public float RudderForceN { get; private set; }
        public float TopSpeed => topSpeed;

        /// Set BEFORE `Configure`, which derives the shaft and thrust
        /// constants from it. A scaled hull keeps Froude number, so her top
        /// speed goes as √k of the design's.
        public void SetTopSpeed(float v) { topSpeed = Mathf.Max(1f, v); }
        /// Full revolutions, rad/s -- what an engine-note or a gauge scales by.
        public float MaxRate => omegaMax;
        public bool Configured => data != null && body != null && rb != null;

        public void Configure(HullFormData hullData, HullFormBody hullBody, Transform sternWheel)
        {
            data = hullData;
            body = hullBody;
            wheel = sternWheel;
            motor = GetComponent<ShipMotor>();
            rb = GetComponent<Rigidbody>();

            var feeder = GetComponent<PaddleProbeFeeder>();
            if (feeder == null) feeder = gameObject.AddComponent<PaddleProbeFeeder>();
            feeder.Bind(this);

            omega = 0f;
            probesWritten = false;
            if (!Configured) return;

            Derive();
            // The one way this model can silently fail its own gate: an
            // engine too weak to hold the thrust top speed needs never gets
            // there, and the symptom is just "she is a bit slow".
            float topTorque = rb.mass * SurgeDecel(topSpeed) * reff;
            if (topTorque > tauMax)
                Debug.LogWarning($"[Steamer] PaddleDrive: holding {topSpeed:F1} m/s needs "
                    + $"{topTorque / 1000f:F0} kNm at the wheel and the engine has {tauMax / 1000f:F0}. "
                    + "Raise bollardAccel or lower the hull's surge resistance.");
        }

        // The static registry outlives a play session (domain reload is off),
        // so a handle left in it is sampled for ever by a ship that no longer
        // exists. Register and unregister strictly in pairs.
        void OnEnable()
        {
            for (int i = 0; i < handles.Length; i++)
                handles[i] = OceanProbeRegistry.Register(transform.position);
            probesWritten = false;
        }

        void OnDisable()
        {
            for (int i = 0; i < handles.Length; i++)
            {
                OceanProbeRegistry.Unregister(handles[i]);
                handles[i] = null;
            }
        }

        /// Called by `PaddleProbeFeeder` at -100, ahead of the ocean driver.
        public void WriteProbePositions()
        {
            if (!Configured || handles[0] == null) return;
            Vector3 axle = data.wheelAxle;
            for (int i = 0; i < 2; i++)
            {
                float z = axle.z + (i == 0 ? ProbeSpread : -ProbeSpread);
                // y = 0: the design waterline, the same datum the hull's own
                // strips sample at, so a level is a level on both.
                probeWorld[i] = transform.TransformPoint(new Vector3(axle.x, 0f, z));
                handles[i].position = probeWorld[i];
            }
            probesWritten = true;
        }

        float SurgeDecel(float v) =>
            body.SurgeLinear * v + body.SurgeQuadratic * v * v;

        void Derive()
        {
            float m = rb.mass;
            float v = Mathf.Max(0.5f, topSpeed);
            reff = Mathf.Max(0.2f, data.wheelRadius - 0.5f * data.wheelDesignDip);
            omegaMax = v / Mathf.Max(0.05f, 1f - slipAtTop) / reff;
            tauMax = m * Mathf.Max(0.1f, bollardAccel) * reff;
            shaftJ = Mathf.Max(0.05f, spinUpSeconds) * tauMax / omegaMax;
            governorKp = tauMax / (governorBand * omegaMax);

            // K solves T = m (a1 v + a2 v^2) at top speed -- at the revs the
            // shaft actually HOLDS there, not at the ordered ones. The
            // governor is proportional, so under load it sits a little below
            // its order (droop); solving at the nominal slip would leave her
            // that same few percent short of the speed she was asked for.
            float topThrust = m * SurgeDecel(v);
            float droop = Mathf.Min(topThrust * reff, tauMax) / governorKp;
            float slipSpeed = Mathf.Max(0.25f, (omegaMax - droop) * reff - v);
            thrustK = topThrust / (slipSpeed * slipSpeed);
        }

        /// Load on the floats against how deep they are. Nothing in the air,
        /// full at the design dip, and past 1.8x of it the floats are coming
        /// in flat and lifting water instead of pushing it, down to 0.6 with
        /// the sea at the axle.
        float DipFactor(float d)
        {
            float design = Mathf.Max(0.05f, data.wheelDesignDip);
            float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / design));
            float deep = 1.8f * design;
            if (d > deep && data.wheelRadius > deep)
                f *= Mathf.Lerp(1f, 0.6f, Mathf.InverseLerp(deep, data.wheelRadius, d));
            return f;
        }

        float Helm()
        {
            if (motor.Anchored) return 0f;
            // The motor's own autopilot rule (its EffectiveRudder is private):
            // full helm at 20 degrees off, proportional inside that.
            if (motor.AutopilotTarget.HasValue)
            {
                float want = motor.CourseFor(motor.AutopilotTarget.Value, motor.WindDirection);
                return Mathf.Clamp(Mathf.DeltaAngle(motor.Heading, want) / 20f, -1f, 1f);
            }
            return Mathf.Clamp(motor.Rudder, -1f, 1f);
        }

        void FixedUpdate()
        {
            thrust = 0f;
            RudderForceN = 0f;
            RudderInflow = 0f;
            if (!Configured || motor == null) return;

            // She publishes even before the sea is ready, so the gauges read
            // a settled zero rather than whatever the servo last left there.
            motor.PublishExternal(Mathf.Max(0f, body.FkSurgeAccel), body.FkSwayAccel, 0f);
            // The hull's drag reference, exactly as the motor hands it to
            // BuoyantBody. Zero for a hull with no canvas today; carried so a
            // current, when there is one, moves her too.
            body.AmbientFlow = motor.Anchored ? Vector3.zero : motor.WaterVelocity;

            if (!body.Ready || !probesWritten) return;
            for (int i = 0; i < handles.Length; i++)
                if (handles[i] == null || handles[i].sampledFrame == 0) return;

            Derive();
            float dt = Time.fixedDeltaTime;
            Vector3 fwd = transform.forward;
            bool anchored = motor.Anchored;

            float way = Vector3.Dot(rb.linearVelocity - body.AmbientFlow, fwd);
            float helm = Helm();
            float order = anchored ? 0f : Mathf.Clamp(motor.Throttle, -1f, 1f);
            if (order < 0f) order *= asternFraction;

            float upY = Mathf.Max(transform.up.y, 0.5f);
            Vector3 axle = data.wheelAxle;
            float wheelBottom = axle.y - data.wheelRadius;
            var sim = DynamicWaterSim.Instance;

            float level = 0.5f * ((handles[0].sample.height - probeWorld[0].y)
                                + (handles[1].sample.height - probeWorld[1].y)) / upY;
            dip = level - wheelBottom;

            float cmd = Mathf.Clamp(order * omegaMax, -omegaMax, omegaMax);

            // Thrust acts where the floats are: at the effective radius BELOW
            // the axle, which is why hard ahead lifts her head.
            Vector3 at = transform.TransformPoint(
                new Vector3(axle.x, axle.y - reff, axle.z));
            float uWheel = Vector3.Dot(
                rb.GetPointVelocity(at) - body.WaterVelocityNear(at), fwd);

            float f = anchored ? 0f : DipFactor(dip);
            float du = omega * reff - uWheel;
            float T = thrustK * du * Mathf.Abs(du) * f;

            // Shaft: torque-limited engine under a proportional governor,
            // loaded by the water. The load can take the slip to zero in a
            // step and no further -- unbounded, a wheel dropped into solid
            // water at full revs overshoots through zero slip and the
            // explicit step rings.
            float engine = Mathf.Clamp(governorKp * (cmd - omega), -tauMax, tauMax);
            float maxLoadStep = Mathf.Abs(du) / reff;
            float loadStep = Mathf.Clamp(T * reff / shaftJ * dt, -maxLoadStep, maxLoadStep);
            omega += engine / shaftJ * dt - loadStep;
            omega = Mathf.Clamp(omega, -1.15f * omegaMax, 1.15f * omegaMax);

            if (!anchored && f > 0f)
            {
                thrust = T;
                rb.AddForceAtPosition(fwd * T, at, ForceMode.Force);

                // A working wheel tears the sea up behind it. Scaled by thrust
                // against the most the engine can ever make, so a wheel
                // ticking over leaves a mark and one at full bollard pull
                // leaves a race.
                if (sim != null)
                {
                    float k = Mathf.Clamp01(Mathf.Abs(T) * reff / tauMax);
                    if (k > 0.02f)
                    {
                        Vector3 p = transform.TransformPoint(new Vector3(axle.x, 0f,
                            axle.z - Mathf.Sign(T) * 0.5f * data.wheelRadius));
                        sim.Stamp(new Vector2(p.x, p.z), wakeRadius,
                            wakeFoam * dt * k, wakeDisplace * dt * k);
                    }
                }
            }

            // --- rudder ------------------------------------------------------
            //
            // Flat-plate lift, but on the water the BLADE is in, not on her
            // speed made good: the wheel is the aftmost thing on her and the
            // rudder stands in what it is pulling through. With no way on the
            // race is the whole of the inflow, which is why she answers her
            // helm at a standstill only while the engine is turning. Positive
            // helm pushes the stern to port.
            if (!anchored && data.rudderArea > 0f)
            {
                // The blade's force heels her INTO the turn because it acts
                // below the centre of mass. Some of that lean is the charm of
                // her; `rudderHeelLever` is how much of it she keeps.
                Vector3 rudderAt = data.rudder;
                rudderAt.y = Mathf.Lerp(data.com.y, data.rudder.y, rudderHeelLever);
                Vector3 at2 = transform.TransformPoint(rudderAt);
                float u = Vector3.Dot(rb.GetPointVelocity(at2) - body.WaterVelocityNear(at2), fwd);
                float race = raceGain * (omega * reff - u) * DipFactor(dip);
                RudderInflow = u + race;
                float delta = helm * rudderMaxDeg * Mathf.Deg2Rad;
                float force = 0.5f * Rho * data.rudderArea * RudderLift
                    * Mathf.Sin(delta) * Mathf.Cos(delta)
                    * RudderInflow * Mathf.Abs(RudderInflow)
                    * rudderGain * Mathf.Clamp01(body.Submersion * 2f);
                RudderForceN = force;
                rb.AddForceAtPosition(-transform.right * force, at2, ForceMode.Force);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || wheel == null) return;
            float cap = Mathf.Max(0.1f, visualRateCap);
            // Positive about +X carries the top of the wheel toward the bow
            // and the bottom floats aft, which is ahead.
            float rate = cap * (float)System.Math.Tanh(omega / cap);
            visualAngle = Mathf.Repeat(visualAngle + rate * Mathf.Rad2Deg * dt, 360f);
            wheel.localRotation = Quaternion.Euler(visualAngle, 0f, 0f);
        }
    }
}
