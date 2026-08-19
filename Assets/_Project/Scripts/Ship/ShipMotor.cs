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
        // Stokes drift: waves carry a float along with them. These are target
        // SPEEDS in m/s and they, not maxWaveAccel, set how hard a drifting
        // ship gets pushed — raising the accel cap alone barely moved it.
        // Drift must never exceed what the ship can make head-to-wind, or an
        // upwind leg becomes impossible during a swell.
        [SerializeField] float waveDrift = 1f;
        [SerializeField] float swellDrift = 2.5f;
        [Tooltip("A hull resists being shoved sideways far more than forward.")]
        [SerializeField, Range(0f, 1f)] float lateralWaveScale = 0.4f;
        [Tooltip("Ceiling on wave acceleration so a swell can't fling the ship.")]
        // Ceiling on wave acceleration. Measured with sails genuinely furled:
        // 2.2 carries a drifting ship at ~2.9 m/s in a swell, 4.5 at ~5 m/s.
        [SerializeField] float maxWaveAccel = 4.5f;
        [SerializeField] float driftResponse = 1f;
        [SerializeField] float minTurnRate = 15f;      // deg/s when barely moving — always escapable
        [SerializeField] float maxTurnRate = 34f;      // deg/s at full speed
        [SerializeField] Vector2 windDirection = new Vector2(1f, 0.35f); // where the wind blows toward
        [SerializeField, Range(0f, 0.5f)] float steerageWay = 0.12f; // min drive with sails furled

        [Header("Seating on the water")]
        [SerializeField] float verticalResponse = 4f;  // how quickly the hull follows wave height
        [SerializeField] float angularResponse = 2.5f; // how quickly pitch/roll follow the surface
        [SerializeField] float turnHeel = 5f;          // extra roll (deg) at full rudder + speed
        // Six points down the hull rather than three. With only a bow and two
        // stern points the ship stayed rigid; sampling along the length lets
        // the bow ride up while midships is still in a trough.
        static readonly Vector3[] FloatPoints =
        {
            new Vector3( 0.0f, 0f,  9.0f),   // stem
            new Vector3(-2.0f, 0f,  4.0f),   // fore port
            new Vector3( 2.0f, 0f,  4.0f),   // fore starboard
            new Vector3(-2.4f, 0f, -4.0f),   // aft port
            new Vector3( 2.4f, 0f, -4.0f),   // aft starboard
            new Vector3( 0.0f, 0f, -8.5f),   // transom
        };

        [Header("Visual pivots")]
        [SerializeField] Transform rudderPivot;
        [SerializeField] Transform mastPivot;
        [SerializeField] float rudderVisualAngle = 35f;

        /// -1 (full port) .. 1 (full starboard). Set by HelmInput.
        public float Rudder { get; set; }
        /// 0 (fully reefed) .. 1 (full canvas). What is ACTUALLY set on the
        /// yard right now. The captain does not touch the canvas himself: he
        /// calls for it with SailOrder and the crew get there when they can.
        public float SailSetting { get; private set; } = 1f;
        /// What the captain has called for. Free to set at any time — a crew
        /// too sick to work simply never arrives at it, and the ship carries
        /// whatever it was already carrying.
        public float SailOrder { get; set; } = 1f;
        /// True while the hands are still working the sheets toward the order.
        public bool Trimming => !Mathf.Approximately(SailSetting, SailOrder);
        /// 0 empty .. 1 full hold. Loaded ships are slower and turn heavier.
        public float CargoLoad01 { get; set; }
        /// Anchored: no thrust, no steering, but the hull still rides the waves.
        public bool Anchored { get; set; }

        [Header("Oars")]
        [Tooltip("Speed under oars alone — wind-independent, always available.")]
        [SerializeField] float rowSpeed = 9f;
        /// Crew on the oars. Slower than a good point of sail, but it works in
        /// any direction, which means there is always a way home.
        public bool Rowing { get; set; }
        public float RowSpeed => rowSpeed;
        /// Oars are pure labour — no wind to borrow. A broken crew takes the
        /// guaranteed way home away with them, which is exactly when it
        /// should stop being guaranteed.
        public float OarPower01 => roster != null ? roster.Labour01 : 1f;
        /// When set, rudder input is ignored and the ship steers itself toward
        /// this point, tacking upwind if it has to. Nothing drives it since
        /// mutiny was removed — kept as the hook for a "set course" order.
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
        /// Sideways wave force on the hull. Positive = struck on the starboard
        /// beam. Drives which side throws spray when a sea hits.
        public float LateralWaveAccel { get; private set; }

        /// Degrees between the bow and the direction the wind blows FROM.
        /// 0 = pointing straight into the wind, 180 = dead downwind.
        public float WindAngleDeg { get; private set; } = 90f;
        /// How badly the sails are shaking: 1 in irons, 0 once they draw.
        public float Luff01 { get; private set; }
        public bool InIrons => WindAngleDeg < NoGoDegrees;
        /// Drive available from the current heading, before sail setting.
        public float PolarEfficiency { get; private set; } = 1f;

        /// Where the slow zone ends. The wind is a GRADIENT, not a gate: you
        /// can always sail anywhere, it's just slower dead upwind. Catching a
        /// good angle should feel like a reward, not an escape from a trap.
        public const float NoGoDegrees = 30f;

        public string PointOfSailName =>
            WindAngleDeg < 30f ? "into the wind"
            : WindAngleDeg < 60f ? "close hauled"
            : WindAngleDeg < 105f ? "beam reach"
            : WindAngleDeg < 150f ? "broad reach"
            : "running";

        static float bestUpwindAngle = -1f;

        /// The heading that makes the most ground to windward — fast enough to
        /// matter, close enough to the wind to gain. Shared by the autopilot
        /// and the navigation tape so both give the same advice.
        public static float BestUpwindAngle
        {
            get
            {
                if (bestUpwindAngle < 0f)
                {
                    float best = -999f;
                    for (float a = 20f; a <= 90f; a += 1f)
                    {
                        float vmg = SailPolar(a) * Mathf.Cos(a * Mathf.Deg2Rad);
                        if (vmg > best) { best = vmg; bestUpwindAngle = a; }
                    }
                }
                return bestUpwindAngle;
            }
        }

        int autopilotTack = 1;
        bool beating;

        /// Heading to steer to actually REACH a point. Sails straight at it
        /// when it can, and beats to windward in tacks when it can't. Without
        /// this an autopilot aimed at an upwind target parks itself in the
        /// no-go zone and never arrives — which is what stranded mutinies.
        public float CourseFor(Vector3 target, Vector2 wind)
        {
            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 1f) return heading;

            float targetBearing = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            float windFrom = Mathf.Atan2(-wind.x, -wind.y) * Mathf.Rad2Deg;
            float offWind = Mathf.Abs(Mathf.DeltaAngle(targetBearing, windFrom));

            // Hysteresis: start beating once the target is inside the no-go
            // zone, and only stop once it's comfortably clear. Without the gap
            // the autopilot flickers between beating and steering direct.
            if (!beating && offWind < NoGoDegrees + 3f) beating = true;
            else if (beating && offWind > NoGoDegrees + 14f) beating = false;

            if (!beating) return targetBearing;

            // Cross-track measured against the UPWIND CORRIDOR through the
            // target — not against the ship-to-target line, which is trivially
            // always perpendicular to its own normal and therefore always zero.
            Vector3 upwindAxis = new Vector3(-wind.x, 0f, -wind.y).normalized;
            Vector3 across = new Vector3(upwindAxis.z, 0f, -upwindAxis.x); // right of upwind
            float crossTrack = Vector3.Dot(transform.position - target, across);
            float limit = Mathf.Clamp(toTarget.magnitude * 0.32f, 35f, 220f);
            if (Mathf.Abs(crossTrack) > limit) autopilotTack = crossTrack > 0f ? -1 : 1;

            return windFrom + BestUpwindAngle * autopilotTack;
        }

        /// Sailing polar. Upwind is genuinely slow and worth tacking out of,
        /// but never a trap: even head-to-wind you keep enough drive to steer
        /// out and get home. Peak is on a beam-to-broad reach.
        public static float SailPolar(float thetaDeg)
        {
            // Floor is high enough that heading straight into the wind is
            // merely slow, never a wall. The payoff for a good angle is the
            // top of the curve, not escape from the bottom.
            if (thetaDeg < 25f) return 0.55f;
            if (thetaDeg < 45f) return Mathf.Lerp(0.55f, 0.80f, (thetaDeg - 25f) / 20f);
            if (thetaDeg < 70f) return Mathf.Lerp(0.80f, 0.95f, (thetaDeg - 45f) / 25f);
            if (thetaDeg < 115f) return Mathf.Lerp(0.95f, 1.00f, (thetaDeg - 70f) / 45f);
            if (thetaDeg < 150f) return Mathf.Lerp(1.00f, 0.95f, (thetaDeg - 115f) / 35f);
            return Mathf.Lerp(0.95f, 0.86f, (thetaDeg - 150f) / 30f);
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

        // Firing a broadside shoves the hull. Modelled as a damped spring
        // rather than a lerp back to level, because a boat does not return to
        // upright — it rolls past and comes back, once or twice, and that
        // second little roll is the whole feel of it.
        [Header("Gun recoil")]
        [SerializeField] float recoilStiffness = 34f;   // ~1.1s period
        [SerializeField] float recoilDamping = 3.4f;    // underdamped on purpose
        float recoilRoll, recoilRollVel;

        /// Positive heels to port — the side away from starboard guns, which
        /// is where the reaction from a starboard broadside actually throws it.
        public void AddRecoilRoll(float degreesPerSecond) => recoilRollVel += degreesPerSecond;

        float heading;   // degrees, 0 = +Z
        float speed;     // |velocity|, for HUD/camera/turn-rate
        Vector3 velocity; // world-space; decoupled from heading so the hull can slide
        Vector3 waveAccel;             // smoothed 2D push from the surface slope
        Vector3 waterVelocity;         // the water mass's own motion (drift)
        Vector3 targetWaterVelocity;

        /// How fast the water under the ship is itself moving.
        public Vector3 WaterVelocity => waterVelocity;

        HullIntegrity hull;

        void Start()
        {
            hull = GetComponent<HullIntegrity>();
            heading = transform.eulerAngles.y;
            if (rudderPivot == null) rudderPivot = transform.Find("RudderPivot");
            if (mastPivot == null) mastPivot = transform.Find("MastPivot");
        }

        [Header("Crew")]
        [Tooltip("Sail units per second a FULL, healthy crew can trim. Scales with how much crew is actually working.")]
        [SerializeField] float sailTrimRate = 0.55f;

        Crew.CrewRoster roster;

        /// Canvas is set by hands, not by the captain's thumb. The order goes
        /// up instantly; the sail follows at whatever pace the crew can manage,
        /// and at zero able crew it simply stays where it is. That is the whole
        /// failure mode: you never lose the ship, you lose the ability to
        /// change your mind about it.
        void TrimSails(float dt)
        {
            if (roster == null) roster = GetComponent<Crew.CrewRoster>();
            if (roster == null) { SailSetting = SailOrder; return; }   // raiders, tests
            SailSetting = Mathf.MoveTowards(
                SailSetting, SailOrder, sailTrimRate * roster.Labour01 * dt);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            TrimSails(dt);

            // Damped spring: acceleration back toward level, minus drag.
            recoilRollVel += (-recoilStiffness * recoilRoll - recoilDamping * recoilRollVel) * dt;
            recoilRoll += recoilRollVel * dt;

            // --- Steering & speed ---
            float load = Mathf.Clamp01(CargoLoad01);
            float effMaxSpeed = maxSpeed * (1f - 0.18f * load);
            if (hull != null) effMaxSpeed *= hull.SpeedMultiplier;
            float speedFactor = Mathf.Clamp01(speed / maxSpeed);
            float turnRate = Mathf.Lerp(minTurnRate, maxTurnRate, speedFactor) * (1f - 0.25f * load);

            // Wind is sampled BEFORE steering: the autopilot needs to know
            // where the wind is to work out whether it has to tack.
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

            float effectiveRudder = Anchored ? 0f : Rudder;
            if (!Anchored && AutopilotTarget.HasValue)
            {
                float desiredYaw = CourseFor(AutopilotTarget.Value, wind);
                effectiveRudder = Mathf.Clamp(Mathf.DeltaAngle(heading, desiredYaw) / 20f, -1f, 1f);
            }
            heading += effectiveRudder * turnRate * dt;

            Vector3 forward = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;

            // Points of sail: measure the bow against where the wind comes FROM.
            Vector3 windFromWorld = new Vector3(-wind.x, 0f, -wind.y);
            WindAngleDeg = Vector3.Angle(new Vector3(forward.x, 0f, forward.z), windFromWorld);
            PolarEfficiency = SailPolar(WindAngleDeg);
            Luff01 = Mathf.Clamp01(1f - (WindAngleDeg - 12f) / 23f);

            float sailPower = Mathf.Lerp(steerageWay, 1f, Mathf.Clamp01(SailSetting));
            float targetSpeed = effMaxSpeed * PolarEfficiency * sailPower * WindStrength;

            // Oars don't care about the wind. They never beat a good point of
            // sail, but they'll always get you off a lee shore or home.
            if (Rowing && !Anchored)
                targetSpeed = Mathf.Max(targetSpeed, rowSpeed * (1f - 0.18f * load) * OarPower01);

            // Sea-of-Thieves-style carve: thrust builds along the hull, but
            // momentum keeps its own direction. Turning converts forward way
            // into sideways slip, which the keel bleeds off over ~half a
            // second — so the stern slides out and you move THROUGH the water.
            if (Anchored) targetSpeed = 0f;

            // --- Wave riding ---
            // Sample the surface just ahead of the bow: running downhill pulls
            // the hull forward, climbing a face bleeds momentum. This is what
            // turns the sea into terrain you steer across rather than through.
            // Wave force as a proper 2D world vector, not just a push along the
            // hull. Water runs downhill, so the surface gradient shoves the
            // boat whichever way it faces — a beam-on swell moves you sideways
            // and a boat lying still still gets carried about.
            Vector3 rawWaveAccel = Vector3.zero;
            var waveField = Ocean.WaveField.Instance;
            if (waveField != null && !Anchored)
            {
                Vector3 p = transform.position;
                float t = Time.time;
                Vector2 here = new Vector2(p.x, p.z);
                float h0 = waveField.SampleHeight(here, t);
                float e = surfSampleDistance;
                float slopeX = (waveField.SampleHeight(here + new Vector2(e, 0f), t) - h0) / e;
                float slopeZ = (waveField.SampleHeight(here + new Vector2(0f, e), t) - h0) / e;
                rawWaveAccel = new Vector3(-slopeX, 0f, -slopeZ) * surfPower;

                // Stokes drift is the water mass itself moving. It is NOT a
                // force on the hull: a keel resists moving THROUGH water, not
                // being carried WITH it. So it's tracked separately and added
                // at integration, where neither keel grip nor sail drag can
                // eat it (which is exactly what was swallowing it before).
                Vector3 driftVel = Vector3.zero;
                Vector2 windWave = waveField.WindDirection;
                driftVel += new Vector3(windWave.x, 0f, windWave.y) * (waveDrift * waveField.SeaState01);
                if (waveField.SwellActive)
                {
                    float intensity = waveField.SwellIntensity(here, t);
                    Vector2 sd = waveField.SwellDirection;
                    driftVel += new Vector3(sd.x, 0f, sd.y) * (swellDrift * intensity);
                }
                // Ocean currents — the water itself is going somewhere, and it
                // takes the ship with it whichever way the bow points.
                var currents = Ocean.CurrentField.Instance;
                if (currents != null)
                {
                    Vector2 c = currents.Sample(here);
                    driftVel += new Vector3(c.x, 0f, c.y);
                }

                targetWaterVelocity = driftVel;
            }

            // The hull only yields so much to a beam sea: keep the along-hull
            // component and damp the across-hull one, then cap the whole thing
            // so a big swell shoves the ship without hurling it.
            {
                Vector3 r = new Vector3(forward.z, 0f, -forward.x);
                float along = Vector3.Dot(rawWaveAccel, forward);
                float lateral = Vector3.Dot(rawWaveAccel, r) * lateralWaveScale;
                rawWaveAccel = forward * along + r * lateral;
                rawWaveAccel = Vector3.ClampMagnitude(rawWaveAccel, maxWaveAccel);
            }

            // Smoothed so the push swells and fades like a real wave rather
            // than jittering frame to frame.
            waveAccel = Vector3.Lerp(waveAccel, rawWaveAccel, 1f - Mathf.Exp(-surfResponse * dt));
            SurfAccel = Vector3.Dot(waveAccel, forward);
            // Signed: positive means the sea is shoving the starboard side.
            LateralWaveAccel = Vector3.Dot(waveAccel, new Vector3(forward.z, 0f, -forward.x));

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
            forwardWay = Mathf.Clamp(forwardWay, -2f, effMaxSpeed * surfOvershoot);

            sideWay *= Mathf.Exp(-(Anchored ? keelGrip * 3f : keelGrip) * dt);
            velocity = forward * forwardWay + right * sideWay;

            // Wave push goes on last, in world space, so the sail-drag solve
            // above can't cancel it in the same frame. The keel bleeds off the
            // sideways part over the following frames, which is what turns a
            // beam sea into leeway rather than a shove you can ignore.
            if (!Anchored) velocity += waveAccel * dt;
            velocity = Vector3.ClampMagnitude(velocity, effMaxSpeed * surfOvershoot + 6f);

            // The anchor holds against the drift; otherwise the water carries
            // the hull bodily along with it.
            waterVelocity = Vector3.Lerp(waterVelocity,
                Anchored ? Vector3.zero : targetWaterVelocity,
                1f - Mathf.Exp(-driftResponse * dt));

            Vector3 groundVelocity = velocity + waterVelocity;
            speed = groundVelocity.magnitude;

            Vector3 pos = transform.position;
            pos += groundVelocity * dt;

            // --- Seat the hull on the waves ---
            var field = WaveField.Instance;
            if (field != null)
            {
                float t = Time.time;
                Quaternion yawOnly = Quaternion.Euler(0f, heading, 0f);

                // Sample every float point, then fit the hull to them: mean
                // height for heave, fore/aft difference for pitch, port/
                // starboard difference for roll.
                float sum = 0f, fore = 0f, aft = 0f, port = 0f, star = 0f;
                int foreN = 0, aftN = 0, portN = 0, starN = 0;
                for (int i = 0; i < FloatPoints.Length; i++)
                {
                    Vector3 w = pos + yawOnly * FloatPoints[i];
                    float h = field.SampleHeight(new Vector2(w.x, w.z), t);
                    sum += h;
                    if (FloatPoints[i].z > 0.5f) { fore += h; foreN++; }
                    else if (FloatPoints[i].z < -0.5f) { aft += h; aftN++; }
                    if (FloatPoints[i].x < -0.5f) { port += h; portN++; }
                    else if (FloatPoints[i].x > 0.5f) { star += h; starN++; }
                }

                float hCenter = sum / FloatPoints.Length;
                float hFore = foreN > 0 ? fore / foreN : hCenter;
                float hAft = aftN > 0 ? aft / aftN : hCenter;
                float hPort = portN > 0 ? port / portN : hCenter;
                float hStar = starN > 0 ? star / starN : hCenter;

                float pitchDeg = Mathf.Atan2(hAft - hFore, 13f) * Mathf.Rad2Deg;
                float rollDeg = Mathf.Atan2(hStar - hPort, 4.4f) * Mathf.Rad2Deg;
                rollDeg += effectiveRudder * speedFactor * turnHeel;
                // Gusts shove the rig: extra heel away from the wind.
                float windSide = Mathf.Sign(Vector3.Cross(forward, new Vector3(wind.x, 0f, wind.y)).y);
                rollDeg += GustFactor01 * sailPower * 4f * windSide;

                // Beam-on to a big swell the raw fit reached nearly 30 degrees
                // of heel, which reads as capsizing rather than working the sea.
                pitchDeg = Mathf.Clamp(pitchDeg, -16f, 16f);
                rollDeg = Mathf.Clamp(rollDeg, -20f, 20f);

                // Added after the swell clamp so a broadside is always felt,
                // even when the sea already has the hull hard over.
                rollDeg = Mathf.Clamp(rollDeg + recoilRoll, -26f, 26f);

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
