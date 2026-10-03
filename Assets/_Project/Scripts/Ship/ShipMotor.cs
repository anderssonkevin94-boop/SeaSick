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
        [Tooltip("Turn rate at rest, deg/s. SUPERSEDED at runtime (2026-09-24): the rate at rest is now HandlingTuning.turnRateAtRest01 x the peak. Kept so scenes still load.")]
        [SerializeField] float minTurnRate = 15f;
        [Tooltip("Turn rate at top speed on a TurnCircleLengths (2.2 L) radius, deg/s. Still the base: at runtime it is re-based onto HandlingTuning.turnCircleLengths and shaped by HandlingTuning.TurnRate01.")]
        [SerializeField] float maxTurnRate = 34f;
        [Tooltip("How fast she BUILDS a turn. Was a rad/s^2 rate limit; now a MULTIPLIER on the HandlingTuning yaw lags: tau x (4 / this). 4 is the knob as-is; the ladder sets 4 x sqrt(24.2/L), so a big ship still takes longer to start.")]
        [SerializeField] float yawResponse = 4f;
        [Tooltip("Top speed astern as a fraction of ahead. Every hull backs badly -- the rudder is at the wrong end of her and the hull is shaped for one direction.")]
        [SerializeField, Range(0.1f, 0.8f)] float asternFraction = 0.35f;
        [Tooltip("Surf strength: multiple of gravity's pull along the surface slope. The old kinematic surfPower 22 corresponds to ~2.2 here.")]
        [SerializeField] float surfGain = 2.2f;
        [SerializeField] float surfResponse = 2.2f;
        [Tooltip("How much of the overspeed brake is lifted while she is on a face. The brake exists so she cannot run away; applying it DURING the ride meant a well-worked wave gave back everything it gave the moment the face flattened, and there was nothing left to have earned.")]
        [SerializeField, Range(0f, 1f)] float surfDragRelief = 0.25f;

        [Header("Broaching")]
        [Tooltip("Face steepness (tangent of the face angle) at which she STARTS to lose the water. 0.20 is about 11 degrees -- the storm sea's median face is 12.9, so ordinary running water only just reaches it.")]
        [SerializeField] float broachOnsetSlope = 0.20f;
        [Tooltip("Steepness at which she is fully broaching. 0.50 is about 27 degrees, which the measured swell faces reach at their worst and the wind sea passes in a squall.")]
        [SerializeField] float broachFullSlope = 0.50f;
        [Tooltip("How much of the rudder she loses at a full broach. Never 1: the captain always keeps the tiller -- she answers late and small, she does not stop answering.")]
        [SerializeField, Range(0f, 0.85f)] float broachRudderLoss = 0.55f;
        [Tooltip("How much of the keel she loses at a full broach. The stern is lifted clear, so there is less of her in the water to grip with, and she starts to skate.")]
        [SerializeField, Range(0f, 0.85f)] float broachGripLoss = 0.45f;
        [Tooltip("Yaw rate the face wants to give her at 90 degrees off the fall line, deg/s. Set against her RUDDER (about 26 deg/s for the tuned hull, 12 once the broach has taken its cut) so that a few degrees off is trivially caught and forty is not.")]
        [SerializeField] float broachYawRate = 22f;
        [Tooltip("Roll acceleration that goes with it, rad/s^2. She lies over as the sea takes her -- the tell arrives before the heading has moved far enough to read.")]
        [SerializeField] float broachHeel = 0.6f;
        [Tooltip("How fast a broach builds, per second. Quick: it is supposed to arrive.")]
        [SerializeField] float broachOnsetRate = 1.6f;
        [Tooltip("How fast it lets go. Slower than it builds -- she is still unsteady after the face has gone under her.")]
        [SerializeField] float broachRecoverRate = 0.8f;

        [Header("Easing")]
        [Tooltip("Most speed easing gives up, as a fraction, when she is taking a sea her own size square on the bow.")]
        [SerializeField, Range(0f, 0.7f)] float easeDepth = 0.40f;
        [Tooltip("Sea height as a fraction of her length at which easing costs its full depth. 0.35 matches the head-sea overwhelm rule above, so one ratio describes 'a sea big enough to matter to THIS hull' in both places.")]
        [SerializeField] float easeFullAtHullFraction = 0.35f;

        [Header("Attitude limits (soft)")]
        [SerializeField] float pitchLimit = 16f;
        [SerializeField] float rollLimit = 20f;
        [SerializeField] float limitSpring = 6f;   // torque per deg past the limit, x inertia
        // SUPERSEDED at runtime (2026-09-24) by HandlingTuning.turnHeelDegrees,
        // an ANGLE against her roll stiffness rather than a torque (this 5
        // measured 24.6 deg steady on the brig). Kept so scenes still load.
#pragma warning disable 0414
        [SerializeField] float turnHeel = 5f;
#pragma warning restore 0414

        [Header("Load & freeboard")]

        [Header("Burn (overdrive)")]
        [Tooltip("How far past full ahead the burn notch reaches. The telegraph's top tier: more thrust and a higher ceiling, at a cost the hold pays. 1.35 is a third again on top of her rated speed.")]
        [SerializeField, Range(1f, 2f)] float overdrive = 1.35f;

        [Header("Oars")]
        [SerializeField] float rowSpeed = 9f;

        [Header("Head seas")]
        [SerializeField, Range(0f, 0.8f)] float headSeaPenalty = 0.45f;

        [Header("Crew")]
        [SerializeField] float sailTrimRate = 0.55f;

        [Header("Propulsion source")]
        [Tooltip("Off for a hull with no canvas: the wind must then not push her or drift her. The head-sea penalty stays either way -- that is the WAVES, and any hull loses way charging a sea.")]
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

        /// The length every constant in this file was authored and tuned
        /// against: 24.2 m.
        ///
        /// **It outlives the hull it came from.** This was the paddle
        /// steamer's length overall, and she has been removed from the game --
        /// but her dimensions were never the point. Top speed, acceleration,
        /// turn rate, buoyancy and wave response are all expressed as RATIOS
        /// to this length, so it is the origin the whole ladder is measured
        /// from rather than a fact about any ship. Delete it as a leftover and
        /// all twenty rungs silently change speed.
        ///
        /// One public copy, because the yard and the chase camera both need to
        /// know what the numbers were anchored to and a second private 24.2 is
        /// exactly how a hull once ended up at a sixth of her displacement.
        public const float TunedLoa = 24.2f;

        /// Her length overall. Anything that has to FRAME or scale to the ship
        /// reads it here rather than reaching into the ladder, because a ship
        /// that never went through the yard still has a length. Defaults to
        /// the hull the numbers were tuned on.
        public float HullLength { get; private set; } = TunedLoa;
        float baseMinTurn = 15f, baseMaxTurn = 34f, accelScale = 1f;

        float baseMaxSpeed = 15f, hullScale = 1f;
        const float BaseAccel = 2.6f, BaseYawResponse = 4f;
        /// (2 pi / 4.6 s)^2: roll stiffness per unit roll inertia for a hull
        /// the yard never fitted (BuoyantBody.RollStiffness still 0). 4.6 s
        /// is the brig's measured roll period.
        const float FallbackRollOmega2 = 1.866f;

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
        /// matched the boat it was written for. The engine vocabulary stayed
        /// when the paddle steamer went, and it still earns its place: astern
        /// is what makes coming off a beach possible, and a square rig backs
        /// its topsails to do exactly that.
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
                if (!value) { holdStation = false; stationLock01 = 0f; }
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

        /// **Tied up at a pier: hold the berth, ride the sea** (2026-10-01,
        /// the catwalk). The anchor spring lets her wander a metre or two
        /// and swing, which is right off a beach and wrong with a catwalk
        /// run out from the pier to her rail. While this is set and she has
        /// settled on `AnchorPoint`/`MooringHeading`, the spring hands over
        /// to a station-keeping servo on her horizontal centre of mass and
        /// her yaw only; heave, roll and pitch stay entirely the buoyancy's.
        /// `AnchorController` sets it at a pier and clears it on cast-off.
        public bool HoldStation
        {
            get => holdStation;
            set { holdStation = value; if (!value) stationLock01 = 0f; }
        }
        bool holdStation;
        float stationLock01;
        /// 0 while she is still easing in on the spring, 1 once the servo
        /// has her. Ramps, so the hand-over never kicks the hull.
        public float StationLock01 => stationLock01;
        /// Probe-only A/B switch (`Dev/MooringTrace`): true keeps the old
        /// anchor spring at a pier. Never set by the game.
        public static bool ProbeDisableStationLock;

        public bool Rowing { get; set; }
        public float RowSpeed => rowSpeed;
        public float OarPower01 => roster != null
            ? Mathf.Max(HandlingTuning.captainAloneThrottle, roster.Labour01) : 1f;
        /// The most the telegraph can order, either way, in units of full ahead.
        /// **Captain alone can sail** (2026-09-30): with no hands aboard it is
        /// `HandlingTuning.captainAloneThrottle` (she sails slowly and steers);
        /// with any crew it is the burn ceiling, as it always was.
        public float ThrottleCeiling => roster != null && roster.CrewCount == 0
            ? Mathf.Min(overdrive, HandlingTuning.captainAloneThrottle) : overdrive;
        /// True when nobody but the captain is aboard (the HUD hint reads this).
        public bool SailingAlone => roster != null && roster.CrewCount == 0;
        public Vector3? AutopilotTarget { get; set; }
        /// **The helm's say on the heading hold** (`HeadingHold`): true while
        /// nobody is turning her. `HelmInput` writes it every Update and
        /// clears it when disabled; AI hulls have no helm, so it stays false
        /// and they are never held. Read by this motor and `PaddleDrive`.
        public bool HoldAllowed { get; set; }
        public float CurrentSpeed { get; private set; }
        public float Heading => transform.eulerAngles.y;
        public Vector2 WindDirection { get; private set; } = new Vector2(0.95f, 0.33f);
        public float WindStrength { get; private set; } = 1f;
        public float GustFactor01 { get; private set; }
        /// Top speed as she will actually make it: the fitted `maxSpeed` x the
        /// lab's `HandlingTuning.topSpeedScale`, so every gauge scaled to it
        /// moves with the slider.
        public float MaxSpeed => maxSpeed * HandlingTuning.topSpeedScale;
        /// The ceiling `ThrottleOrder` is clamped to. Above 1 is the burn tier.
        public float Overdrive => overdrive;
        /// True while the telegraph is past full ahead. The HUD reads this.
        public bool Burning => ThrottleOrder > 1.001f;
        /// The boost's engage surge envelope, 0..1 (1 at the engage, decaying
        /// over `BoostTuning.surgeSeconds`, 0 when not boosting). Written by
        /// `HelmInput` every Update; `PaddleDrive` (the steamer) and this
        /// motor's own drive add `BoostTuning.surgeAccel` x it, below the
        /// ordered speed only. AI hulls have no helm, so it stays 0.
        public float BoostSurge01 { get; set; }
        /// False on a hull with no canvas: wind still exists in the world and
        /// still drives the sea state, it just does not act on this hull.
        public bool WindDriven => windDriven;
        /// The whole propulsion budget: x mass is the most force the sail can
        /// ever apply, and any water force above that wins outright.
        public float Acceleration => acceleration;
        public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;
        /// True when something else is driving and steering her (the paddle
        /// steamer's wheels and rudder are real forces on the rigidbody). The
        /// motor then stops being a propulsion servo and a yaw assignment --
        /// either of which would simply overwrite what the wheels did -- and
        /// stays what every other system needs it to be: the helm's inputs,
        /// the sea-state readout, the anchor and the knockdown.
        public bool ExternalDrive { get; set; }

        /// The derived wave state an external drive measured for itself, so
        /// the gauges, SpeedJuice and the crew keep reading it from the one
        /// place they always have.
        public void PublishExternal(float surfAccel, float lateralAccel, float broach01)
        {
            SurfAccel = surfAccel;
            LateralWaveAccel = lateralAccel;
            Broach01 = Mathf.Clamp01(broach01);
        }

        public float SurfAccel { get; private set; }
        public float SurfBoost01 => Mathf.Clamp01(SurfAccel / 3.5f);
        /// How far over her top speed the water is allowed to carry her. The
        /// way gauge scales itself to this rather than carrying its own copy:
        /// a gauge with a hand-written ceiling stops agreeing with the hull
        /// the first time this is retuned, and then it is an instrument that
        /// lies.
        public float SurfOvershoot => surfOvershoot;
        public float LateralWaveAccel { get; private set; }
        public float SeaAngleDeg { get; private set; } = 90f;
        /// Where the SWELL is coming from, relative to the bow. The wind sea
        /// and the swell are separate trains and the nav tape marks both.
        public float SwellAngleDeg { get; private set; } = 90f;

        /// World bearings the two trains come FROM, in the same convention as
        /// everything else that draws on the compass: 0 is +Z, 90 is +X.
        ///
        /// `SeaAngleDeg` and `SwellAngleDeg` come from `Vector3.Angle` and are
        /// therefore UNSIGNED — 0..180 with no side to them. The nav tape has
        /// been reconstructing a bearing from one of them, which put the sea
        /// mark on the starboard bow whichever side the seas were actually on;
        /// half the time the instrument was pointing at the wrong half of the
        /// compass. A signed quantity cannot be recovered from an unsigned
        /// one, so the bearing has to be carried rather than derived.
        public float SeasFromDeg { get; private set; }
        public float SwellFromDeg { get; private set; }

        static float FromBearing(Vector2 run) =>
            Mathf.Atan2(-run.x, -run.y) * Mathf.Rad2Deg;
        public float SeaSeverity01 { get; private set; }
        public float HeadSea01 { get; private set; }
        public float SeaResistance01 { get; private set; } = 1f;

        /// How far gone she is toward a broach, 0..1. Rises when she is
        /// running down a steep face fast: the stern lifts, the rudder goes
        /// soft, and the sea starts to slew her beam-on. The one number that
        /// makes the FASTEST heading also the one that needs hands.
        public float Broach01 { get; private set; }
        /// Seconds she has held a surf run. A face is worth working, and this
        /// is the only thing that remembers she worked it.
        public float SurfRunSeconds { get; private set; }
        /// How far over her own top speed the water has carried her, 0..1
        /// across the overspeed allowance. `SurfBoost01` says she is on a
        /// face; this says the face gave her something.
        public float Overspeed01 { get; private set; }

        /// Pace her to the sea: hold back deliberately so she rides instead of
        /// slamming. Costs time, buys smoothness — the one verb at the helm
        /// besides the tiller and the telegraph.
        public bool Easing { get; set; }
        /// What easing is actually costing her right now, as a fraction of the
        /// speed she would otherwise be making. Zero in calm water, because
        /// there is then nothing to ease for.
        public float EaseCost01 { get; private set; }
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

        // --- capsize safety net for ExternalDrive hulls (2026-09-29) ---
        // Kevin's steamer/modular hull flipped at a beach: the soft limits
        // below are skipped for ExternalDrive, HullFormBody only damps roll,
        // and its buoyancy is small-angle (upY floored at 0.5), so past
        // ~60-70 deg nothing brought her back. Two layers, both tunable in
        // HandlingTuning (FeelLab): a soft righting spring + damper past
        // `capsizeSoftRollDeg` (nothing inside it, so turn heel and wave roll
        // are untouched), and, if she is still over past
        // `capsizeRecoverRollDeg` for `capsizeRecoverSeconds`, a scripted
        // righting: spin zeroed, rotation eased upright over
        // `capsizeRightingSeconds`, lifted out of anything solid she is in.
        SeaSick.Steamer.HullFormBody hullForm;
        float capsizedFor;
        bool righting;
        float rightingT;
        Quaternion rightingFrom, rightingTo;
        static readonly Collider[] RightingOverlaps = new Collider[16];

        /// True while the capsize recovery is easing her upright.
        public bool Righting => righting;

        /// Heel about her own keel, degrees, signed like `eulerAngles.z`
        /// (+ = port side down), full +-180 range -- the euler reading wraps
        /// and lies once she is past 90 or pitched hard.
        float HeelDegrees()
        {
            Vector3 r = transform.right, u = transform.up;
            return Mathf.Atan2(r.y, u.y) * Mathf.Rad2Deg;
        }

        void ExternalRollGuard(float dt)
        {
            if (rb == null || rb.isKinematic) { capsizedFor = 0f; righting = false; return; }

            if (righting)
            {
                rightingT += dt / Mathf.Max(0.05f, HandlingTuning.capsizeRightingSeconds);
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(rightingT));
                Quaternion q = Quaternion.Slerp(rightingFrom, rightingTo, e);
                rb.angularVelocity = Vector3.zero;
                rb.MoveRotation(q);
                LiftOutOfPenetration(q);
                if (rightingT >= 1f) { righting = false; capsizedFor = 0f; }
                return;
            }

            float roll = HeelDegrees();
            float absRoll = Mathf.Abs(roll);
            Vector3 axis = transform.forward;

            // --- soft limit: a spring + damper only past the limit ---
            // A knockdown is allowed to lay her over, as on the sail hulls.
            float limit = HandlingTuning.capsizeSoftRollDeg;
            if (knockdownLeft > 0f) limit = Mathf.Max(limit, 78f);
            if (absRoll > limit)
            {
                if (hullForm == null) hullForm = GetComponent<SeaSick.Steamer.HullFormBody>();
                float stiffness = hullForm != null && hullForm.RollStiffness > 0f
                    ? hullForm.RollStiffness
                    : rb.mass * Physics.gravity.magnitude * 0.5f; // GM 0.5 m fallback
                float k = stiffness * Mathf.Max(0f, HandlingTuning.capsizeSoftRollStiffness);
                float excessRad = (absRoll - limit) * Mathf.Deg2Rad;
                float inertia = Mathf.Max(1f, RollInertia());
                float c = 2f * Mathf.Max(0f, HandlingTuning.capsizeSoftRollDamping)
                          * Mathf.Sqrt(Mathf.Max(k, stiffness) * inertia)
                          * Mathf.Clamp01((absRoll - limit) / 10f);
                float rollRate = Vector3.Dot(rb.angularVelocity, axis);
                rb.AddTorque(axis * (-Mathf.Sign(roll) * k * excessRad - c * rollRate), ForceMode.Force);
            }

            // --- recovery: over for long enough -> right her ---
            // Not during a knockdown: that is meant to lay her over. If it
            // leaves her capsized, the clock starts when it ends.
            bool over = knockdownLeft <= 0f
                && (absRoll > HandlingTuning.capsizeRecoverRollDeg || transform.up.y < 0.25f);
            capsizedFor = over ? capsizedFor + dt : 0f;
            if (capsizedFor >= Mathf.Max(0.1f, HandlingTuning.capsizeRecoverSeconds))
            {
                righting = true;
                rightingT = 0f;
                rightingFrom = rb.rotation;
                // Keep her heading; the keel's flat direction, or her deck's
                // if she is standing on her bow/stern.
                Vector3 f = transform.forward; f.y = 0f;
                if (f.sqrMagnitude < 0.04f) { f = -transform.up; f.y = 0f; }
                rightingTo = Quaternion.LookRotation(Flat(f), Vector3.up);
                rb.angularVelocity = Vector3.zero;
            }
        }

        /// Push her out of any solid, non-excluded collider her hull box is
        /// inside (a pier, another hull). Land is excluded from her colliders
        /// on purpose (`HullIntegrity.ExcludeLand`) and stays so: the shore
        /// wall, not a lift onto the sand, is what grounds her.
        void LiftOutOfPenetration(Quaternion rotation)
        {
            var box = GetComponent<BoxCollider>();
            if (box == null || !box.enabled || box.isTrigger) return;
            Vector3 pos = rb.position;
            Vector3 scale = transform.lossyScale;
            Vector3 half = Vector3.Scale(box.size, scale) * 0.5f;
            half = new Vector3(Mathf.Abs(half.x), Mathf.Abs(half.y), Mathf.Abs(half.z));
            Vector3 centre = pos + rotation * Vector3.Scale(box.center, scale);
            int n = Physics.OverlapBoxNonAlloc(centre, half, RightingOverlaps, rotation, ~0,
                QueryTriggerInteraction.Ignore);
            Vector3 push = Vector3.zero;
            int excluded = box.excludeLayers.value;
            for (int i = 0; i < n; i++)
            {
                var other = RightingOverlaps[i];
                if (other == null || other.isTrigger) continue;
                if (other.attachedRigidbody == rb || other.transform.IsChildOf(transform)) continue;
                if ((excluded & (1 << other.gameObject.layer)) != 0) continue;
                if (Physics.ComputePenetration(box, pos, rotation, other,
                        other.transform.position, other.transform.rotation, out Vector3 dir, out float dist))
                    push += dir * dist;
            }
            if (push.sqrMagnitude > 1e-6f)
            {
                // Out, and never down into the sea.
                if (push.y < 0f) push.y = 0f;
                rb.position = pos + Vector3.ClampMagnitude(push, 3f);
            }
        }

        /// Broadside recoil: an instant roll-rate kick; the water damps it out
        /// over the next second or two, overshooting once — which is the feel.
        public void AddRecoilRoll(float degreesPerSecond)
        {
            Vector3 axis = Flat(transform.forward).normalized;
            rb.AddTorque(axis * (RollInertia() * degreesPerSecond * Mathf.Deg2Rad),
                ForceMode.Impulse);
        }

        /// The whole heading-vs-sea model in one place, so raiders sail the
        /// same water the player does.
        ///
        /// **There are TWO trains, not one.** The ocean has had two axes since
        /// the 08-28 pass — a fast wind sea and a slow swell that turns on its
        /// own hours-long clock — and this function read only the wind, which
        /// meant the most interesting fact the sea knows about today never
        /// reached the helm. With one train the model is a single cosine and
        /// the optimal play is always "turn away from it"; with two crossing
        /// trains there is no heading that is clean, and the day's crossing
        /// angle becomes a thing to plan a route around.
        ///
        /// Three terms, and she takes the worst:
        /// - the wind sea on the bow, at full penalty — short and steep, it is
        ///   what stops a hull dead;
        /// - the swell on the bow, at `SwellShare` — longer, so she climbs it
        ///   rather than slamming it, but it carries nearly all of the height;
        /// - **confusion**, which is what makes a crossing sea its own weather:
        ///   the product of the two, so it can only deepen a heading that is
        ///   already taking water from both and never invents a penalty on a
        ///   clean run. Pyramidal peaks have no face to work.
        ///
        /// The floor exists so a bad day is slow and never a wall — the same
        /// rule as everywhere else in this project: you lose options, not the
        /// ship.
        public static float SeaResistanceAt(Vector2 pos, Vector3 forward, float penalty,
            out float severity, out float headSea, out float angleDeg,
            out float swellAngleDeg)
        {
            severity = 0f; headSea = 0f; angleDeg = 90f; swellAngleDeg = 90f;
            var ctrl = SeaStateController.Instance;
            if (ctrl == null) return 1f;
            severity = ctrl.SeaSeverityAt(pos);

            Vector3 flat = new Vector3(forward.x, 0f, forward.z);
            angleDeg = AngleOffBow(flat, ctrl.WindDirection);
            swellAngleDeg = AngleOffBow(flat, ctrl.SwellDirection);

            float headWind = Mathf.Clamp01(Mathf.Cos(angleDeg * Mathf.Deg2Rad));
            float headSwell = Mathf.Clamp01(Mathf.Cos(swellAngleDeg * Mathf.Deg2Rad));
            // Whichever train is actually on the bow is the one the rest of
            // the game should react to — sail strain, the nav tape's shading,
            // the spray thresholds.
            headSea = Mathf.Max(headWind, headSwell);

            float r = Mathf.Min(1f - penalty * headWind * severity,
                                1f - penalty * SwellShare * headSwell * severity);
            r -= penalty * ConfusionShare * headWind * headSwell * severity;
            return Mathf.Clamp(r, SeaResistanceFloor, 1f);
        }

        /// Kept so anything still calling the six-argument form compiles. The
        /// swell angle is the only thing it cannot see.
        public static float SeaResistanceAt(Vector2 pos, Vector3 forward, float penalty,
            out float severity, out float headSea, out float angleDeg) =>
            SeaResistanceAt(pos, forward, penalty, out severity, out headSea,
                out angleDeg, out _);

        /// Degrees between the bow and where a train is coming FROM. The
        /// direction vectors say which way the seas RUN, so the bearing they
        /// arrive on is its negative — the sign error here is silent and puts
        /// the penalty on exactly the wrong half of the compass.
        static float AngleOffBow(Vector3 flatForward, Vector2 run) =>
            Vector3.Angle(flatForward, new Vector3(-run.x, 0f, -run.y));

        /// How much of the bow penalty a swell carries against a wind sea of
        /// the same height. Below 1 because a 500 m roller is climbed and a
        /// 30 m one is hit.
        const float SwellShare = 0.80f;
        /// The extra cost of taking both trains at once. This is the whole
        /// point of a crossing sea: it is worse than either train alone and
        /// there is no heading that escapes it.
        const float ConfusionShare = 0.45f;
        /// She is never stopped by water, only slowed.
        const float SeaResistanceFloor = 0.30f;

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
            // The pier lock's physics step (2026-10-01): its own component so
            // it can run after every other force on the hull.
            if (GetComponent<StationKeeper>() == null) gameObject.AddComponent<StationKeeper>();
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

        /// How far gone she is toward a broach.
        ///
        /// Three things have to be true at once, and the product of them is
        /// what keeps this from firing in water where it would just be noise:
        /// the face has to be STEEP, she has to be RUNNING down it rather
        /// than across or up it, and she has to have WAY on. A hull slower
        /// than the wave is overtaken and lifted; the danger is being carried,
        /// which is the same condition that makes surfing worth doing. That
        /// is deliberate — the reward and the risk are the same piece of
        /// water, which is the only reason either is interesting.
        void UpdateBroach(Vector3 downSlope, bool onFace, Vector3 forward, float dt)
        {
            float want = 0f;
            if (onFace && !Anchored)
            {
                Vector3 dir = downSlope.normalized;
                float running = Vector3.Dot(dir, forward);
                if (running > 0f)
                {
                    float steep = Mathf.InverseLerp(broachOnsetSlope, broachFullSlope,
                        downSlope.magnitude);
                    float way = Mathf.InverseLerp(0.45f, 0.90f,
                        CurrentSpeed / Mathf.Max(1f, maxSpeed));
                    want = steep * way * running * buoyant.Submersion;
                }
            }
            float rate = want > Broach01 ? broachOnsetRate : broachRecoverRate;
            Broach01 = Mathf.MoveTowards(Broach01, Mathf.Clamp01(want), rate * dt);
        }

        void TrimSails(float dt)
        {
            // Ahead clamps at the OVERDRIVE ceiling, not at 1: the burn notch
            // is an order above full ahead, and the ramp walks to it the same
            // way it walks to any other.
            // 2026-09-30 **Captain alone can sail**: with 0 hands `Labour01` was
            // 0, so the ramp never moved and a crewless ship drifted. The
            // ceiling is the captain floor with nobody aboard, and the ramp
            // never runs slower than the floor either.
            float ceiling = ThrottleCeiling;
            ThrottleOrder = Mathf.Clamp(ThrottleOrder, -Mathf.Min(1f, ceiling), ceiling);
            if (roster == null) { Throttle = ThrottleOrder; return; }
            Throttle = Mathf.MoveTowards(
                Throttle, ThrottleOrder, sailTrimRate * OarPower01 * dt);
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
                out float sev, out float head, out float angle, out float swellAngle);
            SeaSeverity01 = sev;
            SeaHs = ctrl != null ? ctrl.SeaHsAt(posXZ) : 0f;
            HeadSea01 = head;
            SeaAngleDeg = angle;
            SwellAngleDeg = swellAngle;
            if (ctrl != null)
            {
                SeasFromDeg = FromBearing(ctrl.WindDirection);
                SwellFromDeg = FromBearing(ctrl.SwellDirection);
            }
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
            // the wind's push on the ship, so a hull under no canvas is exempt.
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

        // --- the pier lock (2026-10-01) -----------------------------------
        /// Within this far of the berth, and this square to it, she counts as
        /// eased in and the lock starts to take her. Generous on purpose: the
        /// spring alone holds her ~1 m off in a 2.5 m sea (measured on
        /// Kevin's save), and the servo's own speed cap walks her the rest.
        const float StationEngageDistance = 3f;
        const float StationEngageDegrees = 15f;
        /// The spring-to-servo hand-over, seconds.
        const float StationEngageSeconds = 1.5f;
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

        // Heading hold for the ladder (sail) hulls (2026-10-02, see
        // `HeadingHold`); the steamer's lives in `PaddleDrive`. Gated by
        // `HoldAllowed`, which only the player's helm ever sets.
        HeadingHold yawHold;

        void FixedUpdate()
        {
            if (rb == null) return;
            float dt = Time.fixedDeltaTime;
            float mass = rb.mass;

            float load = Mathf.Max(0f, CargoLoad);
            float over = Mathf.Max(0f, load - 1f);
            float laden = Mathf.Min(load, 1f);
            float heaviness = 1f / (1f + 0.55f * laden + 1.05f * over);

            // MaxSpeed carries HandlingTuning.topSpeedScale.
            float topSpeed = Mathf.Max(0.1f, MaxSpeed);
            float effMaxSpeed = topSpeed * (1f - 0.10f * laden - 0.14f * over);
            if (hull != null) effMaxSpeed *= hull.SpeedMultiplier;

            Vector3 forward = Flat(transform.forward);
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            Vector3 through = rb.linearVelocity - WaterVelocity;
            float forwardWay = Vector3.Dot(through, forward);
            float sideWay = Vector3.Dot(through, right);
            float speedFactor = Mathf.Clamp01(CurrentSpeed / topSpeed);

            // An externally driven hull (the steamer) makes her own thrust,
            // yaw, grip and surf out of real forces. Everything from here to
            // the anchor is a servo that would overwrite them -- `av.y` is an
            // ASSIGNMENT -- so it is skipped whole, and the three instruments
            // only this block feeds read zero rather than their last value.
            // A jump rather than a wrapping `if` so the block keeps its indent.
            if (ExternalDrive)
            {
                Overspeed01 = 0f;
                SurfRunSeconds = 0f;
                EaseCost01 = 0f;
                goto ExternalDriveAnchor;
            }

            // --- steering: yaw is a control axis, pitch/roll stay physical ---
            // --- the face she is on, sampled ONCE ---------------------------
            //
            // The surf force further down already wanted this; the broach
            // wants it BEFORE the rudder is applied, and a second
            // SampleImmediate would spend a call out of a budget of about
            // eight a frame. `downSlope` points downhill and its magnitude is
            // the tangent of the face angle.
            Vector3 downSlope = Vector3.zero;
            bool onFace = false;
            if (!Anchored && Ocean.OceanSampler.Ready)
            {
                // Through the HULL's filter, the same one her buoyancy probes
                // get. This read the raw surface at one point, which was
                // harmless while the short band was gentle; the art waves put
                // a 9 degree face under her every 4.5 s in every sea state,
                // and one point on a 32 m wave is not what 26 m of hull is
                // standing on. Measured in the everyday sea, beam-on: broach
                // 0.38 and a 24 degree lurch from a mechanic that is meant
                // to need a storm and a following sea, and a surge of
                // +-1.4 m/s^2 along the keel at the wave period.
                Vector3 n = Ocean.OceanSampler.SampleImmediate(transform.position,
                    Ocean.OceanPhysicsDriver.HullFilterNow).normal;
                if (n.y > 0.2f)
                {
                    downSlope = new Vector3(n.x, 0f, n.z) / n.y;
                    onFace = true;
                }
            }
            UpdateBroach(downSlope, onFace, forward, dt);

            float effectiveRudder = EffectiveRudder();
            // A broaching hull has lost her grip on the water: the stern is
            // lifted clear and running, the rudder is in aerated water going
            // the same way she is, and neither bites. She still answers — the
            // captain never loses the tiller — but she answers late and small,
            // which is what makes the recovery a thing you can do WELL.
            float helm = 1f - broachRudderLoss * Broach01;
            // --- how hard she CAN turn (HandlingTuning, read every step) ----
            //
            // `maxTurnRate` is the ladder's rate for a TurnCircleLengths
            // (2.2 L) radius at top speed, fittings included; re-based onto
            // the knob's radius it is the PEAK. The curve then gives
            // turnRateAtRest01 of it at rest, all of it by half speed and
            // 0.8 of it flat out (`minTurnRate` is no longer read).
            float peakTurn = maxTurnRate * (TurnCircleLengths
                / Mathf.Max(0.3f, HandlingTuning.turnCircleLengths));
            float peakTurnRad = Mathf.Max(1e-4f, peakTurn * Mathf.Deg2Rad);
            float turnRate = peakTurn
                * HandlingTuning.TurnRate01(speedFactor, HandlingTuning.turnRateAtRest01)
                * (1f - 0.15f * laden - 0.20f * over) * helm;
            // The pivot floor (2026-10-02), as on the steamer: she can be
            // pointed from a standstill, whatever the canvas is doing.
            turnRate = Mathf.Max(turnRate,
                HandlingTuning.PivotRate(speedFactor) * Mathf.Rad2Deg * helm);
            // --- the broach: a DIVERGENT yaw the rudder has to hold off -----
            //
            // Square to the face she is stable; a few degrees off and the sea
            // carries the lifted stern further round, which puts her further
            // off, which lifts more stern. That runaway is the whole mechanic,
            // and it is why the term goes as sin(error) about the fall line:
            // zero straight down the face, largest halfway to beam-on, and
            // always in the direction that makes it worse. Past 90 degrees it
            // falls away again, so a broach that is allowed to run does not
            // spin her — it lays her beam-on and leaves her there, which is
            // what a broach actually is.
            //
            // **It enters as a yaw RATE, not a torque, and that is load-
            // bearing.** Steering here is an assignment: `av.y` is moved
            // toward the rudder's target at a fixed rate, so a competing
            // torque either always beats that rate limiter or always loses to
            // it, and neither outcome has a lever in it. As a bias on the
            // TARGET the two are commensurable — the rudder can cancel a
            // small one outright, a big one outruns the helm, and the way out
            // of a big one is to stop feeding it: throttle back or ease her,
            // and the `way` term drops the broach on its own. Slowing down in
            // a following sea is the real answer, so it should be the one the
            // mechanic teaches.
            float broachBias = 0f;
            if (Broach01 > 0f && onFace)
            {
                float err = Vector3.SignedAngle(forward, downSlope.normalized, Vector3.up);
                float swing = -Mathf.Sin(err * Mathf.Deg2Rad) * Broach01;
                broachBias = swing * broachYawRate * Mathf.Deg2Rad;
                // And she lies over as the sea takes her: the visible tell,
                // before the heading has moved far enough to read.
                rb.AddTorque(forward * (swing * broachHeel * RollInertia()),
                    ForceMode.Force);
            }

            Vector3 av = rb.angularVelocity;
            float targetYawRate = effectiveRudder * turnRate * Mathf.Deg2Rad + broachBias;
            // Heading hold: once the helm is centred and the turn has died
            // it owns the rudder's share of the command; the broach still
            // pushes on top, so a following sea is still felt and fought.
            bool holdOk = !Anchored && !AutopilotTarget.HasValue && HoldAllowed;
            float holdRate = yawHold.Step(holdOk, effectiveRudder, Heading, av.y, dt);
            if (yawHold.Active) targetYawRate = holdRate + broachBias;
            // First-order lag toward the commanded rate (was a 4 rad/s^2
            // rate limit: full rate in 0.08 s, dead in 0.04 s). tau is the
            // BUILD lag while the command is pulling away from zero and the
            // RELEASE lag while it is easing, centred or reversed -- so she
            // bites, then carries a little when the helm comes off.
            // `yawResponse` (4 x hull scale on the ladder) scales the lag, so
            // the long hulls still start slower; the broach's rudder loss
            // (`helm`) slows it further, as it slowed the old limiter.
            // 2026-10-02: plus HandlingTuning.yawReleaseBrake while easing
            // (`YawStep`), stretched by the same factor as the lag.
            float yawTauScale = (BaseYawResponse / Mathf.Max(0.1f, yawResponse))
                / Mathf.Max(0.15f, helm);
            av.y = HandlingTuning.YawStep(av.y, targetYawRate, dt, yawTauScale);
            rb.angularVelocity = av;
            float yaw01 = Mathf.Clamp(av.y / peakTurnRad, -1f, 1f);

            // Turn heel: an ANGLE into the turn, turnHeelDegrees at the peak
            // yaw rate x top speed, against her roll stiffness -- so it
            // follows the actual yaw (lagged, never a snap) and still passes
            // through the soft roll limit below. The stiffness is the yard's
            // m g GM; a hull the yard never fitted falls back to the brig's
            // measured 4.6 s roll period.
            float rollK = buoyant.RollStiffness > 0f
                ? buoyant.RollStiffness : RollInertia() * FallbackRollOmega2;
            float heelRad = HandlingTuning.turnHeelDegrees * Mathf.Deg2Rad
                * yaw01 * speedFactor;
            rb.AddTorque(forward * (-heelRad * rollK), ForceMode.Force);

            // --- propulsion toward the engine's target speed ---
            //
            // No steerage-way floor any more. A furled sail still let her
            // ghost along at steerageWay of full, which is right for canvas
            // and wrong for an engine: stop has to mean stop, or she cannot
            // be held off a beach.
            // Burn is a MULTIPLIER on her rated speed rather than more of the
            // same demand: past full ahead there is no more sail to set, so
            // the extra has to lift the target and the hard ceiling with it or
            // the overspeed brake quietly eats the whole tier.
            float order = Mathf.Clamp(Throttle, -1f, overdrive);
            float burnMul = order > 1f ? order : 1f;
            float demand = Mathf.Clamp(order, -1f, 1f);
            float power = demand >= 0f ? demand : demand * asternFraction;
            float targetSpeed = effMaxSpeed * power * SeaResistance01 * burnMul;
            // A hard turn costs way: the rudder is a brake and the hull is
            // crabbing. HandlingTuning.turnSpeedBleed at the peak yaw rate.
            targetSpeed *= 1f - Mathf.Clamp01(HandlingTuning.turnSpeedBleed) * yaw01 * yaw01;
            // Pace her to the sea. A hull driven flat out into a head sea
            // does not go faster, she goes wetter -- she launches off a crest
            // and lands on her forefoot. Easing gives up way on purpose so
            // she rides, and it only ever costs anything when there is a sea
            // to ease for, which is what keeps it a decision rather than a
            // handbrake somebody leaves on.
            EaseCost01 = 0f;
            if (Easing && !Anchored)
            {
                float bite = HeadSea01 * Mathf.Clamp01(SeaHs
                    / Mathf.Max(1f, easeFullAtHullFraction * HullLength));
                EaseCost01 = easeDepth * bite;
                targetSpeed *= 1f - EaseCost01;
            }
            if (Rowing && !Anchored && demand >= 0f)
                targetSpeed = Mathf.Max(targetSpeed,
                    rowSpeed * (1f - 0.22f * laden - 0.30f * over) * OarPower01);
            if (Anchored) targetSpeed = 0f;

            // Above her target the engine stops pushing and starts holding
            // her back -- except on a face, where the brake was quietly
            // undoing the ride. A hull that has been carried up to speed by
            // the water KEEPS it for a while: that is what surfing is, and
            // the old constant drag meant every run bled away the instant the
            // face flattened, so working a wave well was worth nothing you
            // could still feel two seconds later.
            float overspeedDrag = Mathf.Lerp(overspeedDragScale,
                overspeedDragScale * surfDragRelief, SurfBoost01);
            // HandlingTuning: accelScale on the PUSH only, coastDownScale on
            // the hold-back while the telegraph is at stop (this is her whole
            // coast-down: 124 m from full measured on 09-18), so the two
            // sliders never touch each other's end of the curve.
            bool coasting = Mathf.Abs(ThrottleOrder) < HandlingTuning.CoastOrder;
            float pull = forwardWay > targetSpeed
                ? acceleration * overspeedDrag
                    * (coasting ? Mathf.Max(0f, HandlingTuning.coastDownScale) : 1f)
                : acceleration * burnMul * Mathf.Max(0f, HandlingTuning.accelScale)
                  // Boost engage surge: more pull toward the SAME target, so
                  // it only shortens the climb; the target and the hard
                  // ceiling below are untouched.
                  + Mathf.Max(0f, BoostTuning.surgeAccel) * Mathf.Max(0f, BoostTuning.punch)
                    * Mathf.Clamp01(BoostSurge01);
            if (Anchored) pull = acceleration * 2.5f;
            float dv = Mathf.Clamp(targetSpeed - forwardWay,
                -pull * heaviness * dt, pull * heaviness * dt);
            rb.AddForce(forward * (dv / dt * mass), ForceMode.Force);

            // Hard ceiling so a surf run can't build without limit.
            float ceiling = effMaxSpeed * surfOvershoot * burnMul;
            if (forwardWay > ceiling)
                rb.AddForce(forward * ((ceiling - forwardWay) / dt * mass * 0.5f), ForceMode.Force);

            // What the ride actually bought her, for the instruments. Measured
            // against her OWN top speed so it means the same thing on every
            // rung of the ladder.
            Overspeed01 = Mathf.Clamp01((forwardWay - effMaxSpeed)
                / Mathf.Max(0.01f, effMaxSpeed * (surfOvershoot - 1f)));
            SurfRunSeconds = Overspeed01 > 0.05f || SurfBoost01 > 0.35f
                ? SurfRunSeconds + dt : 0f;

            // --- keel grip: bleed sideways slip so she carves, not skates ---
            float grip = Anchored ? keelGrip * 3f
                : keelGrip * heaviness * (1f - broachGripLoss * Broach01);
            rb.AddForce(-right * (sideWay * grip * mass), ForceMode.Force);

            // --- surf: gravity pulling the hull along the surface slope ---
            // Derived from the sampled surface normal, NOT from the buoyancy
            // drag forces (those read as a brake at speed, and amplifying them
            // measured as a 4 kN anti-propulsion force). Running down a face
            // pulls her forward; climbing costs; a beam sea shoves sideways.
            Vector3 surfForce = Vector3.zero;
            if (onFace)
            {
                Vector3 accel = downSlope * surfGain * 9.81f;
                accel = Vector3.ClampMagnitude(accel, 4.5f);
                surfForce = accel * mass * buoyant.Submersion;
                rb.AddForce(surfForce, ForceMode.Force);
            }
            smoothedWaveForce = Vector3.Lerp(smoothedWaveForce, surfForce,
                1f - Mathf.Exp(-surfResponse * dt));
            SurfAccel = Vector3.Dot(smoothedWaveForce, forward) / mass;
            LateralWaveAccel = Vector3.Dot(smoothedWaveForce, right) / mass;

            ExternalDriveAnchor:
            // --- anchor: spring back to the drop point, heave stays free ---
            if (Anchored)
            {
                Vector3 toAnchor = Flat3(anchorPoint - transform.position);
                float headErr = MooringHeading.HasValue
                    ? Mathf.DeltaAngle(transform.eulerAngles.y, MooringHeading.Value) : 0f;

                // The pier lock takes over once she has eased in (see
                // `HoldStation`); the spring is what brings her there.
                if (holdStation && MooringHeading.HasValue && !ProbeDisableStationLock)
                {
                    bool settled = toAnchor.magnitude < StationEngageDistance
                        && Mathf.Abs(headErr) < StationEngageDegrees;
                    if (settled || stationLock01 > 0f)
                        stationLock01 = Mathf.MoveTowards(stationLock01, 1f, dt / StationEngageSeconds);
                }
                float springShare = 1f - stationLock01;

                if (springShare > 0f)
                {
                    rb.AddForce((toAnchor * (mass * 0.4f) - Flat3(rb.linearVelocity) * (mass * 0.8f))
                        * springShare, ForceMode.Force);

                    // Spring her head round to the berth's heading, damped on her
                    // actual yaw rate. Critically damped-ish rather than snapped:
                    // she is a floating body and a mooring line pulls, it does not
                    // teleport.
                    if (MooringHeading.HasValue)
                        rb.AddTorque(Vector3.up * ((headErr * Mathf.Deg2Rad * 0.9f
                            - rb.angularVelocity.y * 1.6f) * RollInertia() * springShare), ForceMode.Force);
                }
                // The lock itself is `StationKeeper`, which runs after every
                // other force on the hull this step has been queued.
            }

            // Her attitude is bounded by her own flare and GM, not by a spring
            // that would fight the strip buoyancy at 16 degrees of pitch.
            if (ExternalDrive) { ExternalRollGuard(dt); goto ExternalDriveKnockdown; }

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

            ExternalDriveKnockdown:
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
