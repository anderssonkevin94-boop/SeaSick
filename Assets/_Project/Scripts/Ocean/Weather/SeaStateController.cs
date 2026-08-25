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

        OceanSpectrumSettings blend;
        float severity;          // 0 = calm, 0.5 = normal, 1 = stormy
        bool warmed;             // the first-frame jump has happened
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
            severity = 0.4f;
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
                    severity = target;
                    warmed = true;
                    // Said out loud because "the sea looked wrong at the start"
                    // is otherwise indistinguishable from the ease still
                    // running, and the two want opposite fixes.
                    Debug.Log($"SeaStateController: warm start at severity {severity:F2} ({CurrentStateName})");
                }
                else
                {
                    severity = Mathf.Lerp(severity, target,
                        1f - Mathf.Exp(-Time.deltaTime * 3f / Mathf.Max(blendTime, 1f)));
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
