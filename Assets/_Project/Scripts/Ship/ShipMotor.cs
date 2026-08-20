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
        [SerializeField] float verticalResponse = 10f; // how quickly the hull follows wave height
        // The hull may lag the surface by at most this, whatever the sea does.
        //
        // Softening the hull's height with a plain first-order lag was fine for
        // years and then put the whole deck under water. A lag's error is
        // proportional to how fast the surface is MOVING, and that went up an
        // order of magnitude at once: the western deep got three times taller
        // and she crosses it at 25 m/s, so the water under her now moves at up
        // to 19 m/s where it used to manage about one. MEASURED with the plain
        // lag: she sat between 2.23m under the sea and 3.39m above it, RMS
        // 1.60m, against 1.3m of freeboard to the waist. A lag also delays and
        // attenuates every frequency differently, so she traced a genuinely
        // different curve from the water rather than a delayed copy of it —
        // which is why it read as the boat following a different set of waves,
        // and why it was worst in the choppiest water.
        //
        // A cap in metres rather than a fraction of the wave height, because
        // what it has to stay inside is the BOAT's freeboard, not the sea.
        [Tooltip("Most the hull may ever sit off the true surface, in metres.")]
        [SerializeField] float maxSeatError = 0.3f;
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
        /// 0 empty, **1 = the marked line** (a full hold), and above that is
        /// deck cargo. Deliberately NOT clamped: overloading is allowed, and
        /// everything it costs you gets worse faster past 1.
        public float CargoLoad { get; set; }

        [Header("Load & freeboard")]
        [Tooltip("How deep the hull sits at the marked line. The whole ship goes down, so the deck, crew and guns go with it.")]
        [SerializeField] float sinkAtMarkedLine = 0.50f;
        [Tooltip("Extra sink per unit of load ABOVE the line. Steeper than below it — that is the gamble.")]
        [SerializeField] float sinkPerOverload = 0.72f;
        [Tooltip("How deep a completely swamped bilge pushes her down on its own.")]
        [SerializeField] float sinkAtFullBilge = 0.42f;
        // MEASURED, not guessed. At 2.35 (the top of the bulwark) the sea's
        // closest approach was 1.97m BELOW the rail in a working sea, so green
        // water could never happen at any load — the deck simply rides too high.
        // This is the effective waist where she takes it aboard: the low point
        // by the scuppers, not the cap rail.
        [Tooltip("Effective height above the ship's origin at which water comes aboard.")]
        [SerializeField] float railHeight = 1.30f;
        [Tooltip("Half-beam at the rail. Bigger means heel dips the lee rail further.")]
        [SerializeField] float railHalfBeam = 3.9f;

        /// How far below her light waterline she is sitting, from cargo and
        /// from water already aboard. The spiral lives here: water makes her
        /// sit lower, sitting lower ships more water.
        public float SinkDepth { get; private set; }

        /// Metres of green water over the rail this frame, 0 when dry. The
        /// wave crest is sampled at the hull, so this rises with the sea, with
        /// the load, and with how far the hull is lagging the surface.
        public float RailImmersion { get; private set; }

        /// Set by Bilge each frame. Kept as a plain field so ShipMotor doesn't
        /// have to know the component exists.
        public float BilgeLoad01 { get; set; }
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

        [Header("Head seas")]
        [Tooltip("Most speed a dead-on head sea can take, in the very worst water. Calm water never costs anything at any heading.")]
        [SerializeField, Range(0f, 0.8f)] float headSeaPenalty = 0.45f;

        /// Degrees between the bow and where the seas are coming FROM.
        /// 0 = punching straight into them, 180 = running with them.
        public float SeaAngleDeg { get; private set; } = 90f;
        /// How heavy the water is here: 0 glassy, 1 biblical.
        public float SeaSeverity01 { get; private set; }
        /// How much of the sea the bow is taking head-on, 0 (beam or running) to 1.
        public float HeadSea01 { get; private set; }
        /// Drive available from the current heading. **The only reason one
        /// course is slower than another** — and in calm water it is always 1,
        /// so exploring never costs you anything.
        public float SeaResistance01 { get; private set; } = 1f;

        public string SeaStateName =>
            SeaSeverity01 < 0.2f ? "calm"
            : SeaSeverity01 < 0.45f ? "lively"
            : SeaSeverity01 < 0.7f ? "heavy"
            : SeaSeverity01 < 0.9f ? "wild"
            : "mountainous";

        /// Heading to steer to actually REACH a point. There is no no-go zone
        /// any more, so this is simply the bearing: every course is sailable,
        /// and heavy water makes some of them slower rather than forbidden.
        public float CourseFor(Vector3 target, Vector2 wind)
        {
            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 1f) return heading;
            return Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
        }

        /// Used by grounding: cancel the component of momentum driving the
        /// hull into the rock, keeping whatever slides along the shore.
        /// Lay her over hard and let her come back up.
        ///
        /// Separate from `AddRecoilRoll` on purpose. That feeds a damped spring
        /// whose peak the rotation smoothing then eats — measured, a broadside
        /// heels her ~5° and a mountain sea managed 7°, which is not what being
        /// hit by a mountain looks like. A knockdown is applied AFTER the roll
        /// clamp and decays on its own, so it can put her rail under.
        public void Knockdown(float degrees, float seconds = 2.6f)
        {
            knockdownRoll = degrees;
            knockdownDecay = Mathf.Max(0.2f, seconds);
            knockdownLeft = knockdownDecay;
        }

        float knockdownRoll;
        float knockdownDecay = 1f;
        float knockdownLeft;

        /// How far she is currently laid over by a knockdown, in degrees.
        public float KnockdownRoll { get; private set; }

        /// Take a fraction of her way off in one go — a wall of water hitting
        /// her, or anything else that stops a ship rather than slowing one.
        public void ScrubWay(float fraction01)
        {
            velocity *= Mathf.Clamp01(1f - fraction01);
            speed = velocity.magnitude;
        }

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

        /// Resistance from the water itself. Zero cost in calm water at any
        /// heading; in the worst seas, punching dead into them costs
        /// `headSeaPenalty`. Beam-on and running are always free — and running
        /// down a big sea is actively FASTER, because the surf term pays out.
        /// The whole model, in one place, so raiders sail exactly the sea the
        /// player does. Returns 1 (free) through 1-penalty (dead into the worst
        /// water there is).
        public static float SeaResistanceAt(Vector2 pos, Vector3 forward, float penalty,
            out float severity, out float headSea, out float angleDeg)
        {
            severity = 0f; headSea = 0f; angleDeg = 90f;
            var field = Ocean.WaveField.Instance;
            if (field == null) return 1f;

            float t = Time.time;
            severity = field.SeaSeverity01(pos, t);
            Vector2 run = field.SeaRunDirection(pos, t);
            Vector3 seasFrom = new Vector3(-run.x, 0f, -run.y);
            angleDeg = Vector3.Angle(new Vector3(forward.x, 0f, forward.z), seasFrom);
            headSea = Mathf.Clamp01(Mathf.Cos(angleDeg * Mathf.Deg2Rad));
            return 1f - penalty * headSea * severity;
        }

        void SampleSeaResistance(Vector3 forward)
        {
            SeaResistance01 = SeaResistanceAt(
                new Vector2(transform.position.x, transform.position.z), forward, headSeaPenalty,
                out float sev, out float head, out float angle);
            SeaSeverity01 = sev;
            HeadSea01 = head;
            SeaAngleDeg = angle;
        }

        /// How much sea is standing over the rail right now.
        ///
        /// Measured by sampling the water AT the rails, against where those
        /// rails actually are once the hull has finished moving. That distinction
        /// matters: a hull that follows the waves quasi-statically never ships
        /// water at all, because the rail that dips is always the one over the
        /// lower water. Green water comes from everything that does NOT follow
        /// the surface — the vertical lag, heel in a turn, a gust laying her
        /// over, the kick of a broadside — and every one of those is already
        /// baked into this transform.
        ///
        /// Cargo then simply hands the sea a head start: every centimetre she
        /// has settled is a centimetre of rail she no longer has.
        void SampleGreenWater()
        {
            var field = Ocean.WaveField.Instance;
            if (field == null || Anchored) { RailImmersion = 0f; return; }

            float t = Time.time;
            float worst = 0f;
            for (int i = 0; i < RailPoints.Length; i++)
            {
                Vector3 rail = transform.TransformPoint(
                    new Vector3(RailPoints[i].x * railHalfBeam, railHeight, RailPoints[i].y));
                // Same solver the buoyancy uses. SampleHeightFast does one
                // inverse-displacement iteration to SampleHeight's three, and
                // the two diverge on steep crests — the hull and the test for
                // water coming over it must agree about where the water is.
                float h = field.SampleHeight(new Vector2(rail.x, rail.z), t);
                float over = h - rail.y;
                if (over > worst) worst = over;
            }
            RailImmersion = worst;
        }

        /// Along both rails: x in units of half-beam, z in metres. Four points
        /// with the accurate solver costs about what five did with the fast one.
        static readonly Vector2[] RailPoints =
        {
            new Vector2(-1f,  3.5f), new Vector2(1f,  3.5f),   // forward
            new Vector2(-1f, -2.0f), new Vector2(1f, -2.0f),   // aft
        };

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

            // Eases off along a sine rather than a straight line, so she goes
            // over hard and rights herself gradually — a ship recovering, not
            // a value counting down.
            if (knockdownLeft > 0f)
            {
                knockdownLeft = Mathf.Max(0f, knockdownLeft - dt);
                float t = knockdownLeft / knockdownDecay;
                KnockdownRoll = knockdownRoll * Mathf.Sin(t * Mathf.PI * 0.5f);
            }
            else KnockdownRoll = 0f;

            // Damped spring: acceleration back toward level, minus drag.
            recoilRollVel += (-recoilStiffness * recoilRoll - recoilDamping * recoilRollVel) * dt;
            recoilRoll += recoilRollVel * dt;

            // --- Steering & speed ---
            // A laden ship is not mainly a SLOW ship — she still runs before
            // the wind. What she loses is willingness: slower to gather way,
            // slower to shed it, and she carries through a turn instead of
            // biting. Speed and turn rate take only light penalties so that
            // sailing loaded stays fun; the weight is felt in the momentum
            // terms further down.
            float load = Mathf.Max(0f, CargoLoad);
            float over = Mathf.Max(0f, load - 1f);      // deck cargo only
            float laden = Mathf.Min(load, 1f);          // up to the marked line

            float effMaxSpeed = maxSpeed * (1f - 0.10f * laden - 0.14f * over);
            if (hull != null) effMaxSpeed *= hull.SpeedMultiplier;
            float speedFactor = Mathf.Clamp01(speed / maxSpeed);
            float turnRate = Mathf.Lerp(minTurnRate, maxTurnRate, speedFactor)
                * (1f - 0.15f * laden - 0.20f * over);

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

            // The sea, not the wind, is the only thing that argues with a
            // heading now — and only when it is big. Every course is equally
            // fast in calm water, so exploring is never punished; driving into
            // mountains is slow because they are mountains.
            SampleSeaResistance(forward);

            float sailPower = Mathf.Lerp(steerageWay, 1f, Mathf.Clamp01(SailSetting));
            float targetSpeed = effMaxSpeed * sailPower * SeaResistance01;

            // Oars don't care about the wind. They never beat a good point of
            // sail, but they'll always get you off a lee shore or home.
            if (Rowing && !Anchored)
                targetSpeed = Mathf.Max(targetSpeed,
                    rowSpeed * (1f - 0.22f * laden - 0.30f * over) * OarPower01);

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
            // Mass. Everything she does, she does more reluctantly — building
            // way, losing it, and answering the helm.
            float heaviness = 1f / (1f + 0.55f * laden + 1.05f * over);
            forwardWay = Mathf.MoveTowards(forwardWay, targetSpeed,
                (Anchored ? acceleration * 2.5f : pull * heaviness) * dt);
            forwardWay = Mathf.Clamp(forwardWay, -2f, effMaxSpeed * surfOvershoot);

            // A heavy hull slides. The keel bleeds sideslip more slowly, so the
            // stern keeps going where it was already going and the ship crabs
            // through a turn — the single clearest signal that she is loaded.
            sideWay *= Mathf.Exp(-(Anchored ? keelGrip * 3f : keelGrip * heaviness) * dt);
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

                // After the clamp, deliberately: a knockdown is allowed to put
                // her further over than anything the sea can do on its own.
                rollDeg = Mathf.Clamp(rollDeg + KnockdownRoll, -78f, 78f);

                // Freeboard. Cargo and water aboard both push her down, and the
                // whole ship goes with it — deck, crew, guns — so a loaded
                // ship is legible from the chase camera without any UI.
                SinkDepth = sinkAtMarkedLine * laden
                    + sinkPerOverload * over
                    + sinkAtFullBilge * Mathf.Clamp01(BilgeLoad01);

                // Ease toward the surface for softness in calm water, then
                // CLAMP, so she can never be swallowed however fast the sea is
                // moving. hCenter is already the mean of three float points
                // spread along the hull, which is the smoothing that stops her
                // twitching on small chop — the lag was doing that job a second
                // time, in the one place that guarantees hull and water
                // disagree.
                float seat = hCenter - SinkDepth;
                float eased = Mathf.Lerp(pos.y, seat, 1f - Mathf.Exp(-verticalResponse * dt));
                pos.y = Mathf.Clamp(eased, seat - maxSeatError, seat + maxSeatError);
                Quaternion targetRot = Quaternion.Euler(pitchDeg, heading, rollDeg);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, targetRot, 1f - Mathf.Exp(-angularResponse * dt));
            }
            else
            {
                transform.rotation = Quaternion.Euler(0f, heading, 0f);
            }

            transform.position = pos;

            SampleGreenWater();

            // --- Visual pivots ---
            if (rudderPivot != null)
                rudderPivot.localRotation = Quaternion.Euler(0f, -effectiveRudder * rudderVisualAngle, 0f);
            if (mastPivot != null)
            {
                Vector3 windWorld = new Vector3(wind.x, 0f, wind.y);
                float windYaw = Vector3.SignedAngle(forward, windWorld, Vector3.up);
                // No no-go zone means no luffing. The canvas still shakes, but
                // now it shakes when she's driving into a heavy sea — which is
                // the thing that actually costs you speed.
                float strain = HeadSea01 * SeaSeverity01;
                float trim = Mathf.Clamp(windYaw * 0.5f, -70f, 70f)
                             + Mathf.Sin(Time.time * 16f) * 10f * strain;
                float blend = 1f - Mathf.Exp(-(strain > 0.35f ? 7f : 2f) * dt);
                mastPivot.localRotation = Quaternion.Slerp(
                    mastPivot.localRotation, Quaternion.Euler(0f, trim, 0f), blend);
            }
        }
    }
}
