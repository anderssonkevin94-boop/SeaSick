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
        [Tooltip("How far the open-sea wander swings between calm and normal, 0..1.")]
        [SerializeField] float wanderAmount = 0.5f;
        [SerializeField] float wanderPeriod = 240f;

        OceanSpectrumSettings blend;
        float severity;          // 0 = calm, 0.5 = normal, 1 = stormy
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
                float baseline = 0.15f + wanderAmount * 0.5f * wander;
                float target = Mathf.Max(baseline, Mathf.Lerp(baseline, 1f, storm));
                severity = Mathf.Lerp(severity, target,
                    1f - Mathf.Exp(-Time.deltaTime * 3f / Mathf.Max(blendTime, 1f)));
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
