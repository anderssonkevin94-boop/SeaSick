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

        /// What the open sea is doing right now, metres of Hs. This is the
        /// number every other system should reason about.
        public float CurrentHs { get; private set; }

        /// 0..1, drives sky, spray, camera storm response (SkyDirector).
        /// Expressed in Hs so it survives any re-spacing of the anchors.
        public float Storminess01 => LogInverseLerp(skyHsStart, skyHsFull, CurrentHs);
        public float Severity01 => severity;
        public Vector2 WindDirection => blend != null ? blend.WindDir : Vector2.right;
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
                    // Said out loud because "the sea looked wrong at the start"
                    // is otherwise indistinguishable from the ease still
                    // running, and the two want opposite fixes.
                    Debug.Log($"SeaStateController: warm start at Hs {HsAt(severity):F1} m, severity {severity:F2} ({CurrentStateName})");
                }
            }

            CurrentHs = HsAt(severity);

            // Throttled rebuild, and only when the state actually moved.
            if (OceanTime.Now - lastRebuildTime < 1.0 / rebuildHz) return;
            if (Mathf.Abs(severity - lastRebuildSeverity) < 0.002f) return;
            lastRebuildTime = OceanTime.Now;
            lastRebuildSeverity = severity;

            if (rough == null)
            {
                // Pre-wiring fallback, so a scene that has not had the fourth
                // asset pushed into it still runs the sea it always ran.
                if (severity <= 0.5f) blend.LerpFrom(calm, normal, severity * 2f);
                else blend.LerpFrom(normal, stormy, (severity - 0.5f) * 2f);
            }
            else if (severity <= SevNormal)
                blend.LerpFrom(calm, normal, severity / SevNormal);
            else if (severity <= SevRough)
                blend.LerpFrom(normal, rough, (severity - SevNormal) / (SevRough - SevNormal));
            else
                blend.LerpFrom(rough, stormy, (severity - SevRough) / (1f - SevRough));

            ocean.SetSettings(blend);
        }

        Vector2 FollowXZ()
        {
            Transform t = follow != null ? follow
                : (Camera.main != null ? Camera.main.transform : transform);
            return new Vector2(t.position.x, t.position.z);
        }
    }
}
