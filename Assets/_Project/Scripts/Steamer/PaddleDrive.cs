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
    /// price of the single wheel. (2026-10-02: the phone needs her pointable
    /// from a standstill, so the response assists give a modest PIVOT --
    /// `HandlingTuning.pivotTurnDegPerSec`, the wheel drawn kicking over --
    /// where the physics alone gives nothing. The strip forces are unchanged.)
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

        [Header("Response assists (0 = pure physics)")]
        [Tooltip("Master dial for everything in this block. 0 is the pure strip-theory boat as tuned before 2026-09-23; 1 is the phone feel (answers inside ~1.5 s, ~70% of the ordered speed in ~3 s, quick yaw bite, leans into turns). Every assist fades out as the physics catches up, so top speed, the circle she settles into and what the sea does to her are the physics' own.")]
        [SerializeField, Range(0f, 1.5f)] float responsiveness = 1f;
        [Tooltip("Shaft spin-up, seconds, at responsiveness 1 (lerped from spinUpSeconds). A lighter shaft: the wheel is seen to churn the moment the telegraph moves. Floored so the explicit shaft step cannot ring.")]
        [SerializeField] float quickSpinUpSeconds = 0.6f;
        [Tooltip("Extra ahead/astern push, m/s^2 (x mass, so any scale), while she is well short of the ordered speed. Fades to nothing by surgeAssistFadeEnd of it: the start is snappy, the top speed is untouched.")]
        [SerializeField] float surgeAssistAccel = 1.4f;
        [Tooltip("Fraction of the ordered speed below which the surge assist is at full strength.")]
        [SerializeField, Range(0f, 1f)] float surgeAssistFadeStart = 0.35f;
        [Tooltip("Fraction of the ordered speed at which the surge assist is gone. Below 1 on purpose: a head sea that knocks her back less than this is felt in full; only a big stall gets a hand.")]
        [SerializeField, Range(0f, 1f)] float surgeAssistFadeEnd = 0.85f;
        [Tooltip("Extra braking, m/s^2 (x mass), when she is going faster than ordered (telegraph eased or stopped). Full once she is 25% of top speed over the order.")]
        [SerializeField] float brakeAssistAccel = 0.6f;
        // SUPERSEDED at runtime (2026-09-24) by the yaw servo below and
        // HandlingTuning (turnCircleLengths, yawTauBuild/Release,
        // turnHeelDegrees). Kept so anything serialized against them loads.
#pragma warning disable 0414
        [Tooltip("SUPERSEDED by HandlingTuning.turnCircleLengths. Was: turning circle, in waterline lengths, the old fade-out yaw assist steered toward.")]
        [SerializeField] float assistTurnDiameterL = 3.5f;
        [Tooltip("SUPERSEDED by yawTrackSeconds. Was: yaw assist gain, 1/s.")]
        [SerializeField] float yawAssistGain = 2.2f;
        [Tooltip("SUPERSEDED by yawServoMaxAccel. Was: ceiling on the yaw assist, rad/s^2.")]
        [SerializeField] float yawAssistMaxAccel = 0.5f;
        [Tooltip("SUPERSEDED by HandlingTuning.turnHeelDegrees (+ crossflowHeelPerAccel). Was: heel into the turn at the assist's full-helm rate, degrees.")]
        [SerializeField] float heelIntoTurnDeg = 8f;
#pragma warning restore 0414

        [Header("Yaw servo (HandlingTuning drives it; responsiveness 0 turns it off)")]
        [Tooltip("Seconds: how tightly her actual yaw rate is held to the lagged reference. Small = the HandlingTuning lags are exactly what she does; the strip physics (rudder, cross-flow, yaw dampers, the sea) become disturbances it corrects. 0.12 s at 50 Hz is a loop gain of 0.17 per step, well inside stable.")]
        [SerializeField, Range(0.05f, 1f)] float yawTrackSeconds = 0.12f;
        [Tooltip("Seconds of low-pass on the estimate of what everything ELSE is doing to her yaw (hull damping, cross-flow, rudder, sea). The servo cancels that estimate, so the steady turn lands on the commanded rate instead of short of it by the hull's own damping (~1/s here, which would be ~15% short on tracking alone).")]
        [SerializeField, Range(0.05f, 2f)] float yawDisturbanceSeconds = 0.25f;
        [Tooltip("Ceiling on the servo's yaw acceleration, rad/s^2. Stops a collision or a glitch becoming a spin; at the fastest build lag (0.1 s) it is what limits turn-in.")]
        [SerializeField] float yawServoMaxAccel = 2.5f;
        [Tooltip("Rudder inflow, as a fraction of top speed, at which the at-rest turn share (HandlingTuning.turnRateAtRest01) is fully available. Below it the share fades to nothing: engine stopped and no way on, the stern wheel's rudder has no water and the helm does nothing -- her one honest limit. Ring any ahead and the race gives it back.")]
        [SerializeField, Range(0.01f, 1f)] float steerageInflow01 = 0.2f;
        [Tooltip("Degrees of OUTBOARD heel the hull's own cross-flow gives per m/s^2 of turn (way x yaw rate). Added back into the heel moment so HandlingTuning.turnHeelDegrees reads as the NET lean into the turn. ~1: the old tuning measured ~4 deg out at ~4 m/s^2. An estimate -- re-measure if the net lean looks off.")]
        [SerializeField, Range(0f, 3f)] float crossflowHeelPerAccel = 1f;
        [Tooltip("Seconds: every assist force is eased through this first-order lag so a stepped order or helm never becomes a step in acceleration (jerk is what makes the player sick).")]
        [SerializeField] float assistEaseSeconds = 0.25f;

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
        // Drawn wheel rate, rad/s, at a full pivot with the engine stopped.
        const float PivotWheelRate = 1.2f;

        HullFormData data;
        HullFormBody body;
        ShipMotor motor;
        Rigidbody rb;
        Transform wheel;
        float omega, dip, thrust, visualAngle;
        // Eased assist outputs: surge N, yaw N m, roll N m.
        float surgeAssistN, yawAssistNm, heelAssistNm;
        // Yaw servo: the lagged reference rate, the estimate of everything
        // else's yaw acceleration, and last step's rate and servo push (the
        // pair the estimate is measured from). rad/s, rad/s^2.
        float yawRef, yawDist, lastYawRate, lastServoAcc;
        bool servoPrimed;
        // Heading hold (HelmTuning.headingHold), gated by the helm's say
        // (`ShipMotor.HoldAllowed`): AI hulls have no HelmInput, no hold.
        HeadingHold hold;
        // Share of the helm the pivot floor is paying for (0..1): the drawn
        // wheel ticks over with it, the kick that turns her on the spot.
        float pivot01;

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
        /// The top speed the drive is solved for THIS step: `TopSpeed` x
        /// `HandlingTuning.topSpeedScale`. `TopSpeed` stays the authored
        /// value because the bootstrap fits the motor's MaxSpeed to it, and
        /// MaxSpeed applies the same scale itself.
        public float TopSpeedNow => topSpeed * Mathf.Clamp(HandlingTuning.topSpeedScale, 0.3f, 2f);
        /// The assist master dial (0 = pure physics). Settable live.
        ///
        /// `Derive()` blends `spinUpSeconds`/`quickSpinUpSeconds` by this
        /// value into `shaftJ`, so a live change that only wrote the field
        /// left the shaft inertia stale until the next `FixedUpdate` called
        /// `Derive()` anyway (every step, so in practice one frame late) —
        /// re-run it here instead of waiting, it is cheap (a handful of
        /// float ops, no allocation).
        public float Responsiveness
        {
            get => responsiveness;
            set
            {
                float v = Mathf.Clamp(value, 0f, 1.5f);
                if (Mathf.Approximately(v, responsiveness)) return;
                responsiveness = v;
                if (Configured) Derive();
            }
        }
        /// Assist surge force this step, newtons, and assist yaw torque, N m.
        public float SurgeAssistN => surgeAssistN;
        public float YawAssistNm => yawAssistNm;

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
            surgeAssistN = yawAssistNm = heelAssistNm = 0f;
            servoPrimed = false;
            hold.Reset();
            probesWritten = false;
            if (!Configured) return;
            // Visual-only water rig reads this hull and the drawn rotor; forces are unchanged.
            var waterEffects = GetComponent<ShipWaterEffects>();
            if (waterEffects == null) waterEffects = gameObject.AddComponent<ShipWaterEffects>();
            waterEffects.Configure(data, wheel);

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
            float v = Mathf.Max(0.5f, TopSpeedNow);
            reff = Mathf.Max(0.2f, data.wheelRadius - 0.5f * data.wheelDesignDip);
            omegaMax = v / Mathf.Max(0.05f, 1f - slipAtTop) / reff;
            // HandlingTuning.accelScale is the engine's torque limit, i.e. her
            // pull from rest -- floored at 1.15x what holding the top speed
            // needs, so the slider's low end makes her sluggish but never
            // leaves her short of the speed the telegraph promises. At the
            // default the floor (~1.3 m/s^2) is far under bollardAccel (2.6).
            tauMax = m * reff * Mathf.Max(Mathf.Max(0.1f, bollardAccel * HandlingTuning.accelScale),
                                          1.15f * SurgeDecel(v));
            // The governor's time constant is spinUp x band; keep it above a
            // step and a bit so the lighter shaft cannot ring at 50 Hz.
            float spin = Mathf.Lerp(Mathf.Max(0.05f, spinUpSeconds),
                                    Mathf.Max(0.05f, quickSpinUpSeconds), Mathf.Clamp01(responsiveness));
            spin = Mathf.Max(spin, 1.1f * Time.fixedDeltaTime / Mathf.Max(0.02f, governorBand));
            shaftJ = spin * tauMax / omegaMax;
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

            // HandlingTuning, read every step. The setter early-outs on an
            // unchanged value, so this is a compare when nobody touches it.
            Responsiveness = HandlingTuning.paddleResponsiveness;
            // Telegraph at stop: the coast knob scales everything that takes
            // her way off -- the hull's surge drag (here), the stopped wheel
            // dragging through the water and the brake assist (below).
            bool coasting = Mathf.Abs(motor.ThrottleOrder) < HandlingTuning.CoastOrder;
            float coastK = coasting ? Mathf.Max(0f, HandlingTuning.coastDownScale) : 1f;
            body.SurgeDragScale = coastK;

            // She publishes even before the sea is ready, so the gauges read
            // a settled zero rather than whatever the servo last left there.
            motor.PublishExternal(Mathf.Max(0f, body.FkSurgeAccel), body.FkSwayAccel, 0f);
            // The hull's drag reference, exactly as the motor hands it to
            // BuoyantBody. Zero for a hull with no canvas today; carried so a
            // current, when there is one, moves her too.
            body.AmbientFlow = motor.Anchored ? Vector3.zero : motor.WaterVelocity;

            // Any skipped step breaks the yaw servo's step-to-step estimate;
            // it re-primes from her actual rate on the next live one.
            if (!body.Ready || !probesWritten) { servoPrimed = false; return; }
            for (int i = 0; i < handles.Length; i++)
                if (handles[i] == null || handles[i].sampledFrame == 0) { servoPrimed = false; return; }

            Derive();
            float dt = Time.fixedDeltaTime;
            Vector3 fwd = transform.forward;
            bool anchored = motor.Anchored;

            float way = Vector3.Dot(rb.linearVelocity - body.AmbientFlow, fwd);
            float helm = Helm();
            float topNow = TopSpeedNow;
            // Speed bleed in a turn (HandlingTuning.turnSpeedBleed at the peak
            // yaw rate), taken off the governor's order: the wheel is seen and
            // heard to labour as she digs in. On top of what the hull's own
            // crabbing drag already costs her.
            float yaw01 = Mathf.Clamp01(Mathf.Abs(Vector3.Dot(rb.angularVelocity, transform.up))
                                        / PeakYawRate(topNow));
            float bleed = 1f - Mathf.Clamp01(HandlingTuning.turnSpeedBleed) * yaw01 * yaw01;
            // The order carries the same overdrive ceiling every other hull
            // answers to (`ShipMotor.Overdrive`, 1.35 by default) rather than
            // being clamped flat at 1 -- burn is meant to push her past her
            // ordinary top speed, not do nothing. Astern has no overdrive of
            // its own (there is no burn notch behind the stop mark), so only
            // the ahead side gets the ceiling.
            float overdriveCeiling = Mathf.Max(1f, motor.Overdrive);
            float order = anchored ? 0f : Mathf.Clamp(motor.Throttle, -1f, overdriveCeiling);
            if (order < 0f) order *= asternFraction;

            float upY = Mathf.Max(transform.up.y, 0.5f);
            Vector3 axle = data.wheelAxle;
            float wheelBottom = axle.y - data.wheelRadius;
            var sim = DynamicWaterSim.Instance;

            float level = 0.5f * ((handles[0].sample.height - probeWorld[0].y)
                                + (handles[1].sample.height - probeWorld[1].y)) / upY;
            dip = level - wheelBottom;

            // Ahead, the ceiling on the order (above) is the ceiling here too
            // -- burn commands revs past omegaMax on purpose. Astern order is
            // already <= 0 and pre-scaled by asternFraction, so it never asks
            // for more than omegaMax the other way.
            float cmd = Mathf.Clamp(order * omegaMax, -omegaMax, omegaMax * overdriveCeiling) * bleed;

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
            // Headroom above the commanded ceiling so the governor has room
            // to close on `cmd` without clipping it outright; 1.15x was
            // enough when `cmd` never asked for more than omegaMax, burn
            // needs the same margin on top of its own higher ceiling.
            float omegaCeiling = Mathf.Max(1.15f, overdriveCeiling * 1.05f);
            omega = Mathf.Clamp(omega, -omegaCeiling * omegaMax, omegaCeiling * omegaMax);

            if (!anchored && f > 0f)
            {
                // A stopped (or windmilling) wheel dragged through the water
                // is most of what stops her; with the telegraph at stop that
                // drag is the coast knob's. The shaft still feels the true T.
                if (coasting && T * way < 0f) T *= coastK;
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

            ApplyAssists(dt, way, order * bleed, helm, anchored ? 0f : DipFactor(dip), at, coastK, anchored);
        }

        /// rad/s: the rate a HandlingTuning.turnCircleLengths radius gives at
        /// top speed. The turn-rate curve's 1.0.
        float PeakYawRate(float top) =>
            Mathf.Max(1e-3f, Mathf.Max(0.1f, top)
                / (Mathf.Max(0.3f, HandlingTuning.turnCircleLengths) * Mathf.Max(1f, data.lwl)));

        // --- response assists ------------------------------------------------
        //
        // Not physics: a helmsman's hand on the scale, so a thumb on a phone
        // sees her answer. Every term is mass- or inertia-scaled (so the 0.42
        // hull behaves like the drawing), eased (no jerk), gated by the wheel
        // being in the water and the hull being in the sea (a wheel racing in
        // the air gets no help, a hull thrown clear gets none), and FADES as
        // the physics arrives, so the speed she holds and the circle she
        // settles into are still the strip model's.
        void ApplyAssists(float dt, float way, float order, float helm, float wheelF, Vector3 thrustAt,
                          float coastK, bool anchored)
        {
            float r01 = Mathf.Max(0f, responsiveness);
            float sub = Mathf.Clamp01(body.Submersion * 2f);
            float ease = 1f - Mathf.Exp(-dt / Mathf.Max(0.02f, assistEaseSeconds));
            float topNow = TopSpeedNow;

            // Surge: toward the speed the order asks for (already bled for
            // the turn by the caller). Astern orders are already scaled by
            // asternFraction, which is about her astern top.
            float wantF = 0f;
            float vCmd = order * topNow;
            float e = vCmd - way;
            if (r01 > 0f && Mathf.Abs(e) > 0.01f)
            {
                bool gaining = Mathf.Abs(vCmd) > 0.05f && Mathf.Sign(e) == Mathf.Sign(vCmd)
                               && way * Mathf.Sign(vCmd) < Mathf.Abs(vCmd);
                float w = 0f;
                if (gaining)
                {
                    float frac = way * Mathf.Sign(vCmd) / Mathf.Abs(vCmd);
                    // HandlingTuning.accelScale: the push, like the engine's torque limit.
                    w = surgeAssistAccel * Mathf.Max(0f, HandlingTuning.accelScale)
                        * (1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(surgeAssistFadeStart, Mathf.Max(surgeAssistFadeStart + 0.01f, surgeAssistFadeEnd), frac)));
                }
                else
                {
                    // Let waves give speed: a wave-surfed hull running a bit
                    // hotter than ordered is the reward (see HelmInput's way
                    // gauge), not something to fight. Only brake once she is
                    // more than ~10% of the ORDERED speed over it; an order
                    // of stop (vCmd == 0) has no tolerance to give, so any
                    // way at all still gets braked down. At stop the coast
                    // knob scales it.
                    float tol = 0.10f * Mathf.Abs(vCmd);
                    if (Mathf.Abs(e) > tol)
                        w = brakeAssistAccel * coastK * Mathf.SmoothStep(0f, 1f,
                            Mathf.Clamp01(Mathf.Abs(e) / (0.25f * Mathf.Max(1f, topNow))));
                }
                wantF = Mathf.Sign(e) * w * r01 * rb.mass * wheelF * sub;
            }
            surgeAssistN = Mathf.Lerp(surgeAssistN, wantF, ease);

            // --- yaw servo (HandlingTuning) -------------------------------
            //
            // The strip physics alone gives her whatever turn the rudder and
            // the cross-flow balance out at, with no handle on how it FEELS.
            // So the helm commands a yaw RATE, shaped like every other hull's:
            // peak = top / (turnCircleLengths x L), TurnRate01 of it at this
            // speed, and a first-order lag toward it -- yawTauBuild while the
            // command pulls away from zero, yawTauRelease while it eases.
            // The servo holds her actual rate to that reference (feed-forward
            // on the reference's own slope, plus a yawTrackSeconds correction),
            // and cancels a low-passed estimate of everything else's yaw
            // acceleration, so the hull's ~1/s yaw damping does not leave the
            // steady turn short. Astern (rudder inflow reversed) the helm
            // reverses, as it does on the blade.
            float yawRate = Vector3.Dot(rb.angularVelocity, transform.up);
            float wServo = Mathf.Clamp01(r01) * sub;
            float servoAcc = 0f;
            float peak = PeakYawRate(topNow);
            pivot01 = 0f;
            if (wServo <= 0f || anchored)
            {
                // Off (pure physics, thrown clear, or moored -- the mooring
                // spring owns her heading): follow her, so switching back on
                // picks up from where she is, not from a stale reference.
                yawRef = yawRate;
                yawDist = 0f;
                hold.Reset();
            }
            else
            {
                float inflow = Mathf.Clamp(RudderInflow, -topNow, topNow);
                // Dead still, ahead: a hair of sternway from the sea must not
                // flip which way the pivot goes under a held helm. Engine
                // stopped (2026-10-03, the DREDGE stick pivots at rest): the
                // sea's drift is ±1.5 m/s either way in a swell, so the helm
                // alone says which way -- reading the drift flipped the pivot
                // every second and she rocked on the spot instead of turning.
                float dir = Mathf.Abs(order) < 0.05f ? 1f
                    : inflow < -0.05f ? -1f : (inflow > 0.05f ? 1f : (way < -0.05f ? -1f : 1f));
                // The at-rest share needs water on the blade: none with the
                // engine stopped and no way on, all of it once the race runs.
                float steer = Mathf.Clamp01(Mathf.Abs(inflow) / (steerageInflow01 * Mathf.Max(0.5f, topNow)));
                float speed01 = Mathf.Abs(way) / Mathf.Max(0.1f, topNow);
                float avail = peak * HandlingTuning.TurnRate01(speed01, HandlingTuning.turnRateAtRest01 * steer);
                // The pivot (2026-10-02): with little or no water on the
                // blade she can still be pointed, the wheel kicked against
                // the helm. A floor, so it only shows where the race and her
                // way give less.
                float pivot = HandlingTuning.PivotRate(speed01);
                if (pivot > avail)
                {
                    pivot01 = Mathf.Abs(helm) * (pivot - avail) / pivot;
                    avail = pivot;
                }
                float rCmd = helm * dir * avail;

                // Heading hold: once the helm is centred and the turn has
                // died it owns the rate command outright (a stray few
                // hundredths of blade from a thumb resting on the stick do
                // not drift her). The capture waits on the REFERENCE, which
                // the release brake takes to zero cleanly; her actual rate
                // carries the sea.
                bool holdOk = motor.HoldAllowed && !motor.AutopilotTarget.HasValue;
                float holdRate = hold.Step(holdOk, helm, motor.Heading, yawRef, dt);
                if (hold.Active) { rCmd = holdRate; pivot01 = 0f; }

                if (!servoPrimed) { yawRef = yawRate; yawDist = 0f; servoPrimed = true; }
                else
                {
                    // What the rest of the world did to her yaw last step.
                    float other = (yawRate - lastYawRate) / Mathf.Max(1e-4f, dt) - lastServoAcc;
                    yawDist += (other - yawDist) * (1f - Mathf.Exp(-dt / Mathf.Max(0.02f, yawDisturbanceSeconds)));
                }
                float prevRef = yawRef;
                // Lag + release brake (HandlingTuning.YawStep): she bites at
                // yawTauBuild and, when the helm eases, is braked to the new
                // rate instead of carrying the swing on.
                yawRef = HandlingTuning.YawStep(yawRef, rCmd, dt, 1f);
                float acc = (yawRef - prevRef) / Mathf.Max(1e-4f, dt)
                          + (yawRef - yawRate) / Mathf.Max(0.02f, yawTrackSeconds)
                          - yawDist;
                servoAcc = Mathf.Clamp(acc, -yawServoMaxAccel, yawServoMaxAccel) * wServo;
            }
            lastYawRate = yawRate;
            lastServoAcc = servoAcc;
            // Not eased through assistEaseSeconds: it is inside a feedback
            // loop, and its reference is already a smooth lag.
            yawAssistNm = servoAcc * rb.inertiaTensor.y;

            // Heel INTO the turn: HandlingTuning.turnHeelDegrees at the peak
            // yaw rate x top speed, against her own roll stiffness, following
            // her ACTUAL yaw rate (so the turn, not the thumb). Plus the
            // estimated outboard lean her cross-flow gives at this lateral
            // acceleration, so the knob is the net lean. +yaw is to
            // starboard; starboard down is -Z roll.
            float wantHeel = 0f;
            if (r01 > 0f)
            {
                float yaw01 = Mathf.Clamp(yawRate / peak, -1f, 1f);
                float speed01 = Mathf.Clamp01(Mathf.Abs(way) / Mathf.Max(0.1f, topNow));
                float leanDeg = HandlingTuning.turnHeelDegrees * yaw01 * speed01
                              + crossflowHeelPerAccel * way * yawRate;
                wantHeel = -leanDeg * Mathf.Deg2Rad * body.RollStiffness * Mathf.Clamp01(r01) * sub;
            }
            heelAssistNm = Mathf.Lerp(heelAssistNm, wantHeel, ease);

            if (surgeAssistN != 0f)
                rb.AddForceAtPosition(transform.forward * surgeAssistN, thrustAt, ForceMode.Force);
            if (yawAssistNm != 0f || heelAssistNm != 0f)
                rb.AddTorque(transform.up * yawAssistNm + transform.forward * heelAssistNm, ForceMode.Force);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || wheel == null) return;
            float cap = Mathf.Max(0.1f, visualRateCap);
            // Positive about +X carries the top of the wheel toward the bow
            // and the bottom floats aft, which is ahead.
            float rate = cap * (float)System.Math.Tanh(omega / cap);
            // The pivot's kick: a slow churn ahead while she turns on the
            // spot with the engine stopped, so the turn has a visible cause.
            if (pivot01 > 0f && rate > -0.05f)
                rate = Mathf.Max(rate, PivotWheelRate * pivot01);
            visualAngle = Mathf.Repeat(visualAngle + rate * Mathf.Rad2Deg * dt, 360f);
            wheel.localRotation = Quaternion.Euler(visualAngle, 0f, 0f);
        }
    }
}
