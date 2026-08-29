using UnityEngine;

namespace SeaSick.Ocean
{
    /// Weather: Calm / Normal / Rough / Stormy as spectrum-parameter assets,
    /// blended by lerping the PARAMETERS and rebuilding h0 on a throttle —
    /// never by cross-fading two simulations (that ghosts). The blend target
    /// follows the ship's position through the storm region (sail west and the
    /// weather itself turns), plus a slow global wander so the sea near home
    /// breathes. The seed never changes, so rebuilds move amplitudes only and
    /// the phase field stays continuous.
    ///
    /// EVERYTHING HERE IS AUTHORED IN METRES OF Hs, not in severity. Severity
    /// is a blend coordinate, and it is not a unit: the whole ocean shipped
    /// flat once because `setDepth 0.32` looked like "the storm eases by a
    /// third" and actually took 57 % off the waves. Every knob below that
    /// describes how big the sea is says so in metres, or as a fraction of
    /// metres, and is converted through SeverityForHs at the last moment.
    [DefaultExecutionOrder(-95)]
    public class SeaStateController : MonoBehaviour
    {
        public static SeaStateController Instance { get; private set; }

        [SerializeField] OceanSpectrumSettings calm;
        [SerializeField] OceanSpectrumSettings normal;
        [Tooltip("The middle of the ocean's range, and the reason it can now be gradual. Without it the blend ran Normal (Hs 3.5) straight into Stormy (Hs 65) — an 18x gap — so every sea worth sailing lived in a 0.07-wide sliver of severity and there was nowhere to sit between a pond and the apocalypse. It also fixes the SHAPE of the ramp: the two-anchor version smeared wavelength 180 -> 470 m linearly, which is not a sea, it is a straight line through a hole.")]
        [SerializeField] OceanSpectrumSettings rough;
        [SerializeField] OceanSpectrumSettings stormy;
        [Tooltip("Whose position drives the weather target (the player ship). Falls back to the main camera.")]
        [SerializeField] Transform follow;

        /// Where each authored state sits on the 0..1 severity axis. Spaced so
        /// severity is roughly LOGARITHMIC in wave height (the log-rate per
        /// unit severity runs 2.5 / 4.6 / 5.1 across the three segments,
        /// against 20:1 for the old two-anchor blend), because equal steps in
        /// severity should look like equal steps to the eye.
        public const float SevNormal = 0.40f;
        public const float SevRough = 0.70f;

        [Header("Weather, in metres of Hs")]
        [Tooltip("The calmest the open sea ever gets, metres of Hs. Below about 1 m it reads as a mirror rather than water.")]
        [SerializeField] float shelfCalmHs = 1.6f;
        [Tooltip("The top of the open sea's own slow breathing, metres of Hs — a lively day on the home shelf with no storm anywhere near it.")]
        [SerializeField] float shelfLivelyHs = 4.5f;
        [Tooltip("Seconds for the sea to move most of the way to a new target state. The ease runs in LOG Hs, so a climb from 3 m to 65 m spends its time evenly in ratio rather than dumping 50 m in the last few seconds.")]
        [SerializeField] float blendTime = 45f;
        [Tooltip("Spectrum rebuild throttle while blending, Hz.")]
        [SerializeField] float rebuildHz = 4f;

        /// How far an axis must have turned before a rebuild is worth doing.
        ///
        /// THIS IS A STEP SIZE, NOT A DEADBAND. A rebuild applies the WHOLE
        /// accumulated offset at once, so whatever this number is, it is the
        /// smallest jolt the sea can give you -- the gate does not damp the
        /// turn, it batches it up and delivers it in one piece.
        ///
        /// It was 1.5 degrees, and that is the bug behind "I'm sitting on a
        /// wave and suddenly it drops out from under me". The swell wanders
        /// +/-150 degrees on a 1600 s period -- about 0.4 deg/s -- so a 1.5 deg
        /// gate fired roughly every four seconds and rotated the 62 m rollers
        /// a degree and a half INSTANTLY. Rotating the directional spread
        /// reassigns energy across k, so the surface under the hull does not
        /// slide, it changes shape: metres of height, in one frame, on a sea
        /// whose measured margin to the rail was under two metres. The picture
        /// stays smooth throughout, which is exactly why it reads as the ocean
        /// skipping rather than the game stuttering.
        ///
        /// At 0.1 deg the throttle above does the rate limiting instead, which
        /// is what it was always for, and the step becomes whatever the axes
        /// genuinely drifted in a quarter second -- 0.1 deg for the swell,
        /// about 0.5 for the faster wind. A rebuild is one compute dispatch
        /// over n^2 x 3, so paying it at the full 4 Hz instead of sporadically
        /// costs nothing measurable.
        ///
        /// Lower is smoother right up until it stops mattering: past the point
        /// where every throttle tick rebuilds anyway, this does nothing at all
        /// and `rebuildHz` becomes the only lever. That lever is SERIALIZED
        /// into Sea.unity at 4, so raising it needs a SerializedObject push,
        /// not an edit here.
        const float AxisRebuildDeg = 0.1f;
        [Tooltip("Seconds for the slow open-sea wander. A second, shorter octave rides on it at an incommensurate period so the breathing never settles into a rhythm the player can learn.")]
        [SerializeField] float wanderPeriod = 620f;
        [Tooltip("On the first frame, jump straight to the weather the ship's position asks for instead of easing into it over blendTime. Pressing play in storm water otherwise buys you the better part of a minute watching the sea grow -- and every probe and playtest that warps somewhere starts in the wrong sea.")]
        [SerializeField] bool warmStart = true;

        [Header("Sets and lulls")]
        [Tooltip("How deeply the sea breathes in groups, AS A FRACTION OF Hs — 0.16 really is a 16 % swing in wave height, because this is applied in metres and converted to severity afterwards. The old version of this knob was a fraction of SEVERITY and 0.32 of it took 57 % off the waves; that is the bug this whole file is arranged to make impossible.")]
        [Range(0f, 0.4f)] [SerializeField] float setDepth = 0.16f;
        [Tooltip("Seconds per set cycle. Long swell arrives in groups on this sort of period; short enough to feel while sailing, long enough not to read as a pulsing effect. A second octave rides on it at an incommensurate period.")]
        [SerializeField] float setPeriod = 95f;

        [Header("Storm cells")]
        [Tooltip("The most a squall brings on the home shelf, metres of Hs, and the bottom of the storm ceiling. Distance sets the CEILING; the drifting cells decide where under it the water actually is. That is how GDD 5's \"distance is the difficulty curve\" survives a sea that also has weather: out west a cell can reach full mountainous, on the shelf the same cell tops out here.")]
        [SerializeField] float squallHs = 9f;
        [Tooltip("How much of the lull is squeezed out of a full storm, 0..1. At 0.88 the deep west runs about 45-65 m instead of dropping to 26 -- the storm eases and gathers without ever stopping being a storm, which is the failure 05a30d0 shipped.")]
        [Range(0f, 1f)] [SerializeField] float stormSteady = 0.88f;
        [Tooltip("Raises the cell field to this power. Above 1, high values are rare, so a squall is an event rather than the normal condition: at 1.6 the shelf sits near 4 m and reaches 9 only occasionally.")]
        [Range(0.5f, 4f)] [SerializeField] float cellShape = 1.6f;

        [Header("Two axes: the wind sea and the swell")]
        // THE SEA USED TO HAVE EXACTLY ONE DEGREE OF FREEDOM. Wind speed,
        // fetch, both swell heights, BOTH SWELL DIRECTIONS, choppiness,
        // detailGain and dispersion depth were all a single lerp along
        // severity between four anchors, so two seas at the same Hs were
        // byte-identical and there was no way to have an old south-westerly
        // swell under a fresh north-easterly wind: swell direction was welded
        // to wind direction by the blend. No amount of retuning the anchors
        // fixes that, because it is not a tuning problem.
        //
        // So the anchors now set the sea's SIZE and its base directions, and
        // two wanders on top of them turn the wind sea and the swell
        // SEPARATELY. What the player reads is the RELATIONSHIP between them:
        // wind with swell is long ordered rollers, wind against swell is
        // short, steep and dangerous, and an old swell under a new cross wind
        // is confused pyramidal water. One sea state, three different days.
        //
        // Nothing here changes how BIG the sea is. That is deliberate and it
        // is a hard constraint, not a preference: NewtonIterations is 7
        // against a 0.4 ms budget with no headroom, so this layer may only
        // ever move character. The swell split below is variance-preserving
        // for exactly that reason.
        [Tooltip("Seconds for the wind to back and veer through its range. The local wind is the fast axis -- it is weather happening HERE. A day is 180 s, so 300 s is a wind that changes over a day and a half.")]
        [SerializeField] float windTurnPeriod = 300f;
        [Tooltip("Degrees the wind wanders either side of the direction the sea state authored.")]
        [Range(0f, 180f)] [SerializeField] float windTurnRange = 150f;
        [Tooltip("Seconds for the swell to turn. LONG, and that is the whole point: swell is made by weather that was somewhere else, hours ago, and it keeps coming after that weather has gone. A swell that turned with the wind would just be the wind again.")]
        [SerializeField] float swellTurnPeriod = 1600f;
        [Tooltip("Degrees the swell wanders either side of the direction the sea state authored.")]
        [Range(0f, 180f)] [SerializeField] float swellTurnRange = 150f;
        [Tooltip("Metres over which each axis also varies in PLACE. Sailing twenty kilometres should find a different wind, not just a later one.")]
        [SerializeField] float windTurnMetres = 6000f;
        [SerializeField] float swellTurnMetres = 30000f;
        [Tooltip("How far either way the crossing angle between the two swell trains swings AROUND the angle the sea state authored. Around it, never through zero: the authored 45 degrees is the sea that has been measured and gated, and a crossing near zero is two trains lying on each other, which is the parallel corduroy a single narrow train gives at any height. 30 degrees puts the storm's trains between 15 and 75 degrees apart -- always a crossing sea, never a sheet.")]
        [Range(0f, 60f)] [SerializeField] float crossSwingDeg = 30f;
        [Tooltip("How far the JONSWAP peak enhancement swings either side of the value the sea state authored -- the wind sea's AGE. A young sea under a rising wind is peaky and organised; an old one that has been blowing for days is broad and mixed. Small, and relative to the asset rather than absolute, because gamma moves the spectrum's total energy a little and the depth cap is written against the DECLARED Hs.")]
        [Range(0f, 1.5f)] [SerializeField] float gammaSwing = 0.5f;

        [Header("Sky coupling")]
        [Tooltip("Hs at which the sky starts to turn, metres.")]
        [SerializeField] float skyHsStart = 8f;
        [Tooltip("Hs at which the sky is fully stormy, metres. The ramp between the two is geometric, so the sky darkens with the RATIO of wave height rather than snapping at the top of the range.")]
        [SerializeField] float skyHsFull = 55f;

        OceanSpectrumSettings blend;
        float severity;          // 0 = calm, blend coordinate only — NOT a height
        float baseHs;            // the slow weather in metres, before sets ride on it
        bool warmed;             // the first-frame jump has happened
        bool justWarmed;         // log it once severity has been computed
        int nameBand;            // hysteresis state for CurrentStateName
        float lastRebuildSeverity = -1f;
        double lastRebuildTime = -999.0;
        float lastWindOffset = -999f, lastSwellOffset = -999f;

        /// What the open sea is doing right now, metres of Hs. This is the
        /// number every other system should reason about.
        public float CurrentHs { get; private set; }

        /// 0..1, drives sky, spray, camera storm response (SkyDirector).
        /// Expressed in Hs so it survives any re-spacing of the anchors.
        public float Storminess01 => LogInverseLerp(skyHsStart, skyHsFull, CurrentHs);
        public float Severity01 => severity;
        public Vector2 WindDirection => blend != null ? blend.WindDir : Vector2.right;
        /// The wind sea's and the swell's headings, and the angle between
        /// them, which is the number that describes the DAY: near 0 the wind
        /// runs with the swell and the sea is long and ordered, near 180 it
        /// runs against it and the sea is short and steep, and in between it
        /// is confused. Probes and the HUD read these rather than
        /// re-deriving them.
        public float WindDirectionDeg => blend != null ? blend.windDirectionDeg : 0f;
        public float SwellDirectionDeg => blend != null ? blend.swellDirectionDeg : 0f;
        public float WindAgainstSwellDeg => blend != null
            ? Mathf.Abs(Mathf.DeltaAngle(blend.windDirectionDeg, blend.swellDirectionDeg)) : 0f;
        public float SwellCrossingDeg => blend != null
            ? Mathf.Abs(Mathf.DeltaAngle(blend.swellDirectionDeg, blend.swell2DirectionDeg)) : 0f;
        public string CurrentStateName => NameForHs(CurrentHs, ref nameBand);

        public Transform Follow { get => follow; set => follow = value; }

        // ---- Hs <-> severity -------------------------------------------------
        // These two MUST agree with what LerpFrom actually produces, which
        // lerps nominalHs LINEARLY inside each segment. The perceptual
        // evenness comes from where the anchors sit, not from bending the
        // curve here: the depth limit caps against the blended nominalHs, so a
        // mapping that flattered the numbers would quietly mis-cap the sea.

        float HsCalm => calm != null ? calm.nominalHs : 1.3f;
        float HsNormal => normal != null ? normal.nominalHs : 3.5f;
        float HsRough => rough != null ? rough.nominalHs : 14f;
        float HsStorm => stormy != null ? stormy.nominalHs : 65f;

        /// Metres of Hs the blend produces at this severity.
        public float HsAt(float s)
        {
            s = Mathf.Clamp01(s);
            if (rough == null)   // pre-wiring fallback: the old three-anchor blend
                return s <= 0.5f ? Mathf.Lerp(HsCalm, HsNormal, s * 2f)
                                 : Mathf.Lerp(HsNormal, HsStorm, (s - 0.5f) * 2f);
            if (s <= SevNormal) return Mathf.Lerp(HsCalm, HsNormal, s / SevNormal);
            if (s <= SevRough) return Mathf.Lerp(HsNormal, HsRough, (s - SevNormal) / (SevRough - SevNormal));
            return Mathf.Lerp(HsRough, HsStorm, (s - SevRough) / (1f - SevRough));
        }

        /// The exact inverse of HsAt. Everything that wants to say "make the
        /// sea four metres" goes through here.
        public float SeverityForHs(float hs)
        {
            if (rough == null)
                return hs <= HsNormal
                    ? 0.5f * Mathf.InverseLerp(HsCalm, HsNormal, hs)
                    : 0.5f + 0.5f * Mathf.InverseLerp(HsNormal, HsStorm, hs);
            if (hs <= HsNormal) return SevNormal * Mathf.InverseLerp(HsCalm, HsNormal, hs);
            if (hs <= HsRough) return SevNormal + (SevRough - SevNormal) * Mathf.InverseLerp(HsNormal, HsRough, hs);
            return SevRough + (1f - SevRough) * Mathf.InverseLerp(HsRough, HsStorm, hs);
        }

        /// Geometric interpolation. Wave height is read as a ratio, not a
        /// difference — 3 m to 6 m is the same step to the eye as 30 m to
        /// 60 m — so every gradient in this file runs in log Hs. A linear one
        /// spends most of its distance in seas that all look the same and then
        /// delivers the entire storm in the last stretch.
        public static float LogLerp(float a, float b, float t)
        {
            a = Mathf.Max(a, 0.01f); b = Mathf.Max(b, 0.01f);
            return a * Mathf.Pow(b / a, Mathf.Clamp01(t));
        }

        public static float LogInverseLerp(float a, float b, float x)
        {
            a = Mathf.Max(a, 0.01f); b = Mathf.Max(b, a * 1.001f);
            return Mathf.Clamp01(Mathf.Log(Mathf.Max(x, 0.01f) / a) / Mathf.Log(b / a));
        }

        // Upper bound of each band, metres of Hs, and the name below it.
        static readonly float[] BandHs = { 2.0f, 5.5f, 16f, 32f, 50f };
        static readonly string[] BandNames = { "calm", "lively", "rough", "heavy", "wild", "mountainous" };

        /// Names a sea by its height, with 6 % hysteresis so a set or a lull
        /// cannot make the label flicker across a boundary. The caller owns
        /// the band state, so the global weather and the ship's local water
        /// can be named independently without duplicating the thresholds.
        public static string NameForHs(float hs, ref int band)
        {
            band = Mathf.Clamp(band, 0, BandNames.Length - 1);
            while (band < BandHs.Length && hs > BandHs[band] * 1.06f) band++;
            while (band > 0 && hs < BandHs[band - 1] * 0.94f) band--;
            return BandNames[band];
        }

        // ---- Local water -----------------------------------------------------

        /// How big the sea actually is at a position, metres of Hs: the global
        /// weather scaled by the local envelope.
        ///
        /// This used to scale SEVERITY by the envelope, which was wrong in the
        /// direction that matters most. Half the severity of a 65 m storm is a
        /// 7 m sea; half its ENVELOPE is a 32 m one. Every gameplay reader —
        /// sickness roughness, the sky lift, the nav tape — was being told the
        /// water beside an island in a storm was nearly flat.
        public float SeaHsAt(Vector2 p)
        {
            float env = RegionField.Instance != null
                ? Mathf.Clamp01(RegionField.Instance.Evaluate(p)) : 1f;
            return CurrentHs * env;
        }

        /// The same thing as a 0..1 blend coordinate, for callers that want a
        /// scalar rather than metres.
        public float SeaSeverityAt(Vector2 p) => SeverityForHs(SeaHsAt(p));

        /// What the SKY would read at a point that is not where the ship is.
        ///
        /// Same mapping as Storminess01, deliberately in one place: the sky
        /// rose asks this eight times a frame along eight bearings, and a
        /// hand-copied `LogInverseLerp(8, 55, ...)` in SkyDirector would be a
        /// duplicated constant that stops agreeing with this one the first
        /// time the anchors move — which is exactly how DivergenceProbe spent
        /// a fortnight certifying a sea that no longer existed.
        public float SkyStorminessAt(Vector2 p) =>
            LogInverseLerp(skyHsStart, skyHsFull, SeaHsAt(p));

        /// Probes pin the weather with this; wander and region stop moving it.
        public void ForceSeverity(float s)
        {
            forced = true;
            severity = Mathf.Clamp01(s);
            CurrentHs = HsAt(severity);
            baseHs = CurrentHs;
        }

        /// The same pin, said in metres.
        public void ForceHs(float hs) => ForceSeverity(SeverityForHs(hs));

        public void ReleaseForce() => forced = false;
        bool forced;

        void OnEnable()
        {
            Instance = this;
            blend = ScriptableObject.CreateInstance<OceanSpectrumSettings>();
            if (normal != null) blend.CopyFrom(normal);
            baseHs = HsNormal;
            severity = SeverityForHs(baseHs);
            CurrentHs = baseHs;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (blend != null) Destroy(blend);
        }

        /// Two Perlin octaves at incommensurate periods, stretched to actually
        /// reach 0 and 1. Perlin alone sits near its midpoint most of the time,
        /// and a single period is a rhythm the player learns.
        static float Wave01(double t, float slow, float fastRatio, float fastWeight, float seed)
        {
            float a = Mathf.PerlinNoise1D((float)(t / Mathf.Max(slow, 1f)) + seed);
            float b = Mathf.PerlinNoise1D((float)(t / Mathf.Max(slow * fastRatio, 1f)) + seed + 57.13f);
            float v = Mathf.Lerp(a, b, fastWeight);
            return Mathf.Clamp01((v - 0.5f) * 1.9f + 0.5f);
        }

        /// The weather a position asks for at a time, metres of Hs, before the
        /// ease and before sets. Update calls it and so does LivingSeaTrace --
        /// one implementation, so the probe measures the shipped rule rather
        /// than a hand-copy of it that stops matching and stops gating.
        public float TargetHsAt(Vector2 p, double t)
        {
            float storm = RegionField.Instance != null
                ? RegionField.Instance.StormWeight(p) : 0f;

            // The open sea's own slow breathing, in metres. This is the quiet
            // level -- the bottom a lull can reach, not the sea itself.
            float w = Wave01(t, wanderPeriod, 0.376f, 0.35f, 13.7f);
            float floorHs = LogLerp(shelfCalmHs, shelfLivelyHs, w);

            var wf = WeatherField.Instance;
            if (wf == null)
            {
                // No weather field (lab scenes): the plain distance ramp.
                // Toward the storm GEOMETRICALLY, because the eye reads wave
                // height as a ratio. A linear ramp spends two thirds of a
                // crossing looking like nothing has changed and then delivers
                // 40 m in the last stretch, which is the "abrupt" complaint.
                return LogLerp(floorHs, HsStorm, storm);
            }

            // DISTANCE SETS THE CEILING, NOT THE SEA. Out west a cell can
            // reach full mountainous; on the home shelf the same cell tops out
            // at a squall. Difficulty is still a function of how far you sail,
            // but the water is no longer a function of where you are standing.
            float ceilingHs = LogLerp(squallHs, HsStorm, storm);
            floorHs = Mathf.Min(floorHs, ceilingHs);

            // Where under the ceiling the drifting cells put it. Raised to a
            // power so high values are rare and a squall is an event rather
            // than the normal condition, and the lull is squeezed out of a
            // real storm so it eases and gathers without ever stopping being
            // a storm.
            float cell = Mathf.Clamp01(wf.Cell01(new Unity.Mathematics.float2(p.x, p.y), t));
            float lull = (1f - stormSteady * storm) * (1f - Mathf.Pow(cell, cellShape));
            return LogLerp(ceilingHs, floorHs, lull);
        }

        /// A 0..1 wander that varies in TIME and in PLACE, and is a pure
        /// function of both so OceanTime.Scrub still makes probes repeatable.
        /// Offset well away from the origin: Mathf.PerlinNoise is zero on the
        /// integer lattice and symmetric about it, which is the mirroring
        /// NoiseProbe exists to catch, and the origin is exactly where the
        /// home island sits.
        static float Field01(Vector2 p, double t, float period, float metres, float seed)
        {
            float u = (float)(t / Mathf.Max(period, 1f)) + p.x / Mathf.Max(metres, 1f) + seed + 137.5f;
            float v = p.y / Mathf.Max(metres, 1f) + seed * 0.37f + 219.7f;
            float a = Mathf.PerlinNoise(u, v);
            float b = Mathf.PerlinNoise(u * 2.37f + 11.3f, v * 2.37f + 5.1f);
            // Perlin sits near its midpoint most of the time; stretch it so
            // the wind actually reaches the ends of its range.
            return Mathf.Clamp01((Mathf.Lerp(a, b, 0.3f) - 0.5f) * 1.9f + 0.5f);
        }

        /// The wind-sea axis, 0..1: local weather, the fast one.
        public float WindAxis(Vector2 p, double t) =>
            Field01(p, t, windTurnPeriod, windTurnMetres, 3.1f);

        /// The swell axis, 0..1: distant weather, slow and with memory. A
        /// different period, a different seed AND a five times larger spatial
        /// scale, so it cannot track the wind by accident.
        public float SwellAxis(Vector2 p, double t) =>
            Field01(p, t, swellTurnPeriod, swellTurnMetres, 61.7f);

        /// Turns the wind sea and the swell separately on the blended state.
        /// Called after LerpFrom, so the anchors still author the sea's size
        /// and its base headings and this only ever moves the RELATIONSHIP.
        /// Public so LivingSeaTrace and TwoAxisTrace measure the shipped rule
        /// instead of a hand-copy of it that stops matching and stops gating.
        public void ApplyAxes(OceanSpectrumSettings s, Vector2 p, double t)
        {
            if (s == null) return;
            // Read the authored crossing angle BEFORE turning the main train,
            // or it is lost.
            float authoredCross = Mathf.DeltaAngle(s.swellDirectionDeg, s.swell2DirectionDeg);

            s.windDirectionDeg += (WindAxis(p, t) * 2f - 1f) * windTurnRange;
            s.swellDirectionDeg += (SwellAxis(p, t) * 2f - 1f) * swellTurnRange;

            // The crossing angle wanders AROUND the authored one, and this is
            // the third time the same rule has had to be learnt on this pass:
            // swing about what the asset authored, never replace it. Swinging
            // about ZERO instead looks reasonable and is not, because Perlin
            // spends most of its time near its middle -- so the two trains
            // averaged 28 degrees apart against an authored 45, the sea got
            // directionally NARROWER, and a long-crested sea reads high on a
            // one-dimensional transect. Measured: WaveSizeProbe's storm Hs
            // went 60 -> 77 m against a declared 65, which is the number the
            // depth cap is written against. Total variance never moved; the
            // sea had just stopped being spread out.
            float u = Field01(p, t, swellTurnPeriod * 0.61f, swellTurnMetres, 23.3f) * 2f - 1f;
            s.swell2DirectionDeg = s.swellDirectionDeg + authoredCross + u * crossSwingDeg;

            // THE ENERGY SPLIT BETWEEN THE TWO TRAINS IS DELIBERATELY LEFT
            // ALONE, and it was tried twice. It is variance-preserving on
            // paper -- independent trains add in variance, so holding the sum
            // of the squares holds Hs exactly, and TwoAxisTrace measured the
            // drift at 0.0000% -- and it still failed both ways round:
            //
            //   Toward the SECOND train. That train is the SHORT one (170 m
            //   against the main train's 470 in the storm), so energy moved
            //   into it is energy moved into steeper water. The authored storm
            //   puts 12.3% of the swell's variance there, which at 24 m over
            //   170 m is H/lambda 0.14, already sitting on the 1/7 breaking
            //   limit. Letting the split reach 45% took WaveSizeProbe's median
            //   face angle 15.1 -> 18.0 deg, p99 40.9 -> 49.6, worst face 56.6
            //   -> 78.0, and halved the median wavelength from 340 m to 185.
            //   The SWELL-ONLY face angle barely moved, which is the tell: the
            //   mountain was not steeper, energy had slid down into the short
            //   train. NewtonIterations is 7 with no headroom; that is not
            //   affordable.
            //
            //   Toward the FIRST train. Safe for steepness (median face came
            //   back to 14.5 deg) and wrong for a different reason: 98% in one
            //   train IS the single narrow train, which is parallel corduroy at
            //   any height, and a long-crested sea reads high on a transect --
            //   measured Hs went 60 -> 77 m against a declared 65, which is the
            //   number the depth cap is written against.
            //
            // The crossing ANGLE below already changes the sea's character far
            // more than the split ever did, and it is bounded by what has been
            // gated. So the split stays where the asset put it.

            // The wind sea's age. Peaky and organised under a rising wind,
            // broad and mixed after days of it. Relative to what the asset
            // authored and deliberately small: gamma reshapes the spectrum,
            // and the sea has no steepness budget to spend.
            s.gamma = Mathf.Max(1f, s.gamma + (WindAxis(p, t + 311.0) * 2f - 1f) * gammaSwing);
        }

        /// Sets and lulls as a multiplier on Hs at a time.
        public float SetFactor(double t) =>
            1f + setDepth * (Wave01(t, setPeriod, 0.43f, 0.40f, 41.3f) * 2f - 1f);

        void Update()
        {
            if (calm == null || normal == null || stormy == null) return;
            var ocean = OceanRenderer.Instance;
            if (ocean == null) return;

            if (!forced)
            {
                Vector2 pos = FollowXZ();
                float targetHs = TargetHsAt(pos, OceanTime.Now);

                // Wait for RegionField before snapping: with no region there is
                // no storm weight, so a frame-one jump would land on the calm
                // baseline and then have to ease up anyway -- which is the
                // thing this exists to avoid.
                if (warmStart && !warmed && RegionField.Instance != null)
                {
                    baseHs = targetHs;
                    warmed = true;
                    justWarmed = true;   // logged below, once severity is real
                }
                else
                {
                    // Exponential ease, run in log Hs so it is even in ratio.
                    baseHs = LogLerp(baseHs, targetHs,
                        1f - Mathf.Exp(-Time.deltaTime * 3f / Mathf.Max(blendTime, 1f)));
                }

                // Sets and lulls ride ON TOP of the smoothed weather rather
                // than being folded into its target, deliberately. blendTime's
                // time constant is there to stop the weather flickering, and it
                // would eat most of a 95 s modulation before it ever reached
                // the water -- the sea would breathe on paper and look constant.
                //
                // No longer gated to storm water. As a fraction of Hs it is
                // self-scaling: 16 % of a 3.5 m shelf sea is 56 cm, which is
                // exactly the subtle, always-there variation the open sea wants,
                // and 16 % of the storm is 10 m without ever dropping it out of
                // its own weather band.
                severity = SeverityForHs(baseHs * SetFactor(OceanTime.Now));

                if (justWarmed)
                {
                    justWarmed = false;
                    // Publish the warm-started height BEFORE naming it. The
                    // assignment below this block is what normally sets
                    // CurrentHs, so a name read here would come from last
                    // frame's value -- at warm start that is still OnEnable's
                    // HsNormal, and the line reported a 43 m sea as "lively".
                    // Doing it through CurrentHs rather than a second
                    // NameForHs call keeps the hysteresis band advancing
                    // exactly once per frame.
                    CurrentHs = HsAt(severity);
                    // Said out loud because "the sea looked wrong at the start"
                    // is otherwise indistinguishable from the ease still
                    // running, and the two want opposite fixes.
                    Debug.Log($"SeaStateController: warm start at Hs {CurrentHs:F1} m, severity {severity:F2} ({CurrentStateName})");
                }
            }

            CurrentHs = HsAt(severity);

            // Throttled rebuild, and only when the state actually moved --
            // where "the state" now includes WHICH WAY the two seas are
            // running, not only how big they are. Without the second test the
            // wind and the swell would be frozen for as long as Hs happened to
            // sit still, which on a steady day is the whole voyage.
            // Compare the OFFSETS the axes are asking for, never the angles
            // on `blend` -- those already carry the last rebuild's offset, so
            // testing them would add this turn's offset to the last one's and
            // measure a drift that is not happening.
            Vector2 axisP = FollowXZ();
            float windOffset = (WindAxis(axisP, OceanTime.Now) * 2f - 1f) * windTurnRange;
            float swellOffset = (SwellAxis(axisP, OceanTime.Now) * 2f - 1f) * swellTurnRange;
            bool axesMoved = Mathf.Abs(windOffset - lastWindOffset) > AxisRebuildDeg
                          || Mathf.Abs(swellOffset - lastSwellOffset) > AxisRebuildDeg;
            if (OceanTime.Now - lastRebuildTime < 1.0 / rebuildHz) return;
            if (Mathf.Abs(severity - lastRebuildSeverity) < 0.002f && !axesMoved) return;
            lastRebuildTime = OceanTime.Now;
            lastRebuildSeverity = severity;

            BlendAt(severity, blend);

            // The anchors have authored the size and the base headings; now
            // turn the two seas separately on top of them.
            ApplyAxes(blend, axisP, OceanTime.Now);
            lastWindOffset = windOffset;
            lastSwellOffset = swellOffset;

            ocean.SetSettings(blend);
        }

        /// The anchor blend at a severity, with no axes applied -- the sea as
        /// the four assets author it, and exactly what the sea WAS before the
        /// two axes existed. Update calls it, and so does TwoAxisTrace, which
        /// is how the probe can show the before and the after in one run
        /// without a hand-copy of the anchor chain going stale.
        public void BlendAt(float s, OceanSpectrumSettings into)
        {
            if (into == null || calm == null || normal == null || stormy == null) return;
            if (rough == null)
            {
                // Pre-wiring fallback, so a scene that has not had the fourth
                // asset pushed into it still runs the sea it always ran.
                if (s <= 0.5f) into.LerpFrom(calm, normal, s * 2f);
                else into.LerpFrom(normal, stormy, (s - 0.5f) * 2f);
            }
            else if (s <= SevNormal)
                into.LerpFrom(calm, normal, s / SevNormal);
            else if (s <= SevRough)
                into.LerpFrom(normal, rough, (s - SevNormal) / (SevRough - SevNormal));
            else
                into.LerpFrom(rough, stormy, (s - SevRough) / (1f - SevRough));
        }

        Vector2 FollowXZ()
        {
            Transform t = follow != null ? follow
                : (Camera.main != null ? Camera.main.transform : transform);
            return new Vector2(t.position.x, t.position.z);
        }
    }
}
