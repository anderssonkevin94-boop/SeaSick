using UnityEngine;

namespace SeaSick.Ocean
{
    /// Weather: Calm / Normal / Stormy as spectrum-parameter assets, blended
    /// by lerping the PARAMETERS and rebuilding h0 on a throttle — never by
    /// cross-fading two simulations (that ghosts). The blend target follows
    /// the ship's position through the storm region (sail west and the
    /// weather itself turns), plus a slow global wander so the sea near home
    /// breathes between calm and normal. The seed never changes, so rebuilds
    /// move amplitudes only and the phase field stays continuous.
    [DefaultExecutionOrder(-95)]
    public class SeaStateController : MonoBehaviour
    {
        public static SeaStateController Instance { get; private set; }

        [SerializeField] OceanSpectrumSettings calm;
        [SerializeField] OceanSpectrumSettings normal;
        [SerializeField] OceanSpectrumSettings stormy;
        [Tooltip("Whose position drives the weather target (the player ship). Falls back to the main camera.")]
        [SerializeField] Transform follow;
        [Tooltip("Seconds for the sea to move most of the way to a new target state.")]
        [SerializeField] float blendTime = 45f;
        [Tooltip("Spectrum rebuild throttle while blending, Hz.")]
        [SerializeField] float rebuildHz = 4f;
        [Tooltip("The calmest the open sea ever gets. Below about 0.2 it reads as a mirror rather than water.")]
        [Range(0f, 0.5f)] [SerializeField] float calmFloor = 0.22f;
        [Tooltip("How far the open-sea wander swings between calm and normal, 0..1.")]
        [SerializeField] float wanderAmount = 0.5f;
        [SerializeField] float wanderPeriod = 240f;
        [Tooltip("On the first frame, jump straight to the weather the ship's position asks for instead of easing into it over blendTime. Pressing play in storm water otherwise buys you the better part of a minute watching the sea grow -- and every probe and playtest that warps somewhere starts in the wrong sea.")]
        [SerializeField] bool warmStart = true;

        [Header("Sets and lulls")]
        [Tooltip("How deeply the storm breathes, as a fraction of SEVERITY -- and severity is NOT wave height. The blend runs normal->stormy across severity 0.5..1.0, and normal is Hs 3.5 against stormy's 65, so the mapping is brutally nonlinear at the top: severity 1.00 is Hs 65, 0.90 is Hs 53, 0.70 is Hs 28. A setDepth of 0.32 therefore does NOT take 32% off the waves, it takes 57% off them, and the first version of this shipped exactly that -- the storm spent most of its life at 'heavy' instead of 'mountainous' and the whole ocean read as flat. 0.10 is about a 19% swing in height, which is what a set actually looks like, and it also keeps the state name from flickering between heavy and storm.")]
        [Range(0f, 0.6f)] [SerializeField] float setDepth = 0.10f;
        [Tooltip("Seconds per set cycle. Long swell arrives in groups on this sort of period; short enough to feel while sailing, long enough not to read as a pulsing effect.")]
        [SerializeField] float setPeriod = 70f;

        OceanSpectrumSettings blend;
        float severity;          // 0 = calm, 0.5 = normal, 1 = stormy
        float baseSeverity;      // the slow weather, before sets ride on it
        bool warmed;             // the first-frame jump has happened
        bool justWarmed;         // log it once severity has been computed
        float lastRebuildSeverity = -1f;
        double lastRebuildTime = -999.0;

        /// 0..1, drives sky, spray, camera storm response (SkyDirector).
        public float Storminess01 => Mathf.Clamp01((severity - 0.5f) * 2f);
        public float Severity01 => severity;
        public Vector2 WindDirection => blend != null ? blend.WindDir : Vector2.right;
        public string CurrentStateName =>
            severity < 0.25f ? "calm" : severity < 0.55f ? "lively" :
            severity < 0.8f ? "heavy" : "storm";

        public Transform Follow { get => follow; set => follow = value; }

        /// Severity the gameplay math sees at a position: the global weather
        /// scaled by the local envelope (calm shelf stays calm in a storm).
        public float SeaSeverityAt(Vector2 p)
        {
            float env = RegionField.Instance != null
                ? Mathf.Clamp01(RegionField.Instance.Evaluate(p)) : 1f;
            return Mathf.Clamp01(severity * env);
        }

        /// Probes pin the weather with this; wander and region stop moving it.
        public void ForceSeverity(float s)
        {
            forced = true;
            severity = Mathf.Clamp01(s);
        }

        public void ReleaseForce() => forced = false;
        bool forced;

        void OnEnable()
        {
            Instance = this;
            blend = ScriptableObject.CreateInstance<OceanSpectrumSettings>();
            if (normal != null) blend.CopyFrom(normal);
            severity = baseSeverity = 0.4f;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (blend != null) Destroy(blend);
        }

        void Update()
        {
            if (calm == null || normal == null || stormy == null) return;
            var ocean = OceanRenderer.Instance;
            if (ocean == null) return;

            if (!forced)
            {
                Vector2 pos = FollowXZ();
                float storm = RegionField.Instance != null
                    ? RegionField.Instance.StormWeight(pos) : 0f;
                // Open-sea wander breathes between calm and normal on OceanTime.
                float wander = Mathf.PerlinNoise1D((float)(OceanTime.Now / wanderPeriod) + 13.7f);
                // Floored so the open sea never goes to glass either. The
                // wander used to bottom out around 0.14, which reads as a
                // mirror; the water should always have some texture.
                float baseline = Mathf.Max(calmFloor, 0.15f + wanderAmount * 0.5f * wander);
                float target = Mathf.Max(baseline, Mathf.Lerp(baseline, 1f, storm));
                // Wait for RegionField before snapping: with no region there is
                // no storm weight, so a frame-one jump would land on the calm
                // baseline and then have to ease up anyway -- which is the
                // thing this exists to avoid.
                if (warmStart && !warmed && RegionField.Instance != null)
                {
                    baseSeverity = target;
                    warmed = true;
                    justWarmed = true;   // logged below, once severity is real
                }
                else
                {
                    baseSeverity = Mathf.Lerp(baseSeverity, target,
                        1f - Mathf.Exp(-Time.deltaTime * 3f / Mathf.Max(blendTime, 1f)));
                }

                // Sets and lulls ride ON TOP of the smoothed weather rather
                // than being folded into its target, deliberately. blendTime's
                // 15 s time constant is there to stop the weather flickering,
                // and it would eat most of a 70 s modulation before it ever
                // reached the water -- the sea would breathe on paper and look
                // constant.
                //
                // Scaled by storm weight, so the calm shelf near home does not
                // develop a pulse it has no reason to have. The wander already
                // gives the open sea its slow breathing.
                float sets = Mathf.PerlinNoise1D((float)(OceanTime.Now / Mathf.Max(setPeriod, 1f)) + 41.3f);
                sets = Mathf.Clamp01((sets - 0.5f) * 2f + 0.5f);   // Perlin rarely reaches its ends
                float breathe = setDepth * storm;
                severity = Mathf.Clamp01(baseSeverity * (1f - breathe * (1f - sets)));
                // Never let a lull drop the storm out of its own weather band:
                // the point is a sea that eases and gathers, not one that stops
                // being a storm every ninety seconds.
                if (storm > 0.5f) severity = Mathf.Max(severity, baseSeverity * 0.85f);

                if (justWarmed)
                {
                    justWarmed = false;
                    // Said out loud because "the sea looked wrong at the start"
                    // is otherwise indistinguishable from the ease still
                    // running, and the two want opposite fixes. Logged HERE
                    // rather than at the jump, because CurrentStateName reads
                    // `severity` and the jump only sets `baseSeverity` -- the
                    // first version cheerfully reported "severity 1.00
                    // (lively)".
                    Debug.Log($"SeaStateController: warm start at severity {severity:F2} ({CurrentStateName})");
                }
            }

            // Throttled rebuild, and only when the state actually moved.
            if (OceanTime.Now - lastRebuildTime < 1.0 / rebuildHz) return;
            if (Mathf.Abs(severity - lastRebuildSeverity) < 0.002f) return;
            lastRebuildTime = OceanTime.Now;
            lastRebuildSeverity = severity;

            if (severity <= 0.5f) blend.LerpFrom(calm, normal, severity * 2f);
            else blend.LerpFrom(normal, stormy, (severity - 0.5f) * 2f);
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
