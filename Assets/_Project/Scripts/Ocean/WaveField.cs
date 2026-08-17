using UnityEngine;

namespace SeaSick.Ocean
{
    [System.Serializable]
    public struct GerstnerWave
    {
        public Vector2 direction;
        public float wavelength;
        [Range(0f, 1f)] public float steepness;
    }

    /// Analytic Gerstner wave field. The single source of truth for water shape:
    /// the ocean mesh, ship buoyancy, and (later) the sickness roughness metric
    /// all sample this same function.
    public class WaveField : MonoBehaviour
    {
        public static WaveField Instance { get; private set; }

        const float Gravity = 9.81f;

        [SerializeField]
        GerstnerWave[] waves =
        {
            new GerstnerWave { direction = new Vector2(1f, 0.30f),  wavelength = 48f, steepness = 0.20f },
            new GerstnerWave { direction = new Vector2(0.7f, -0.4f), wavelength = 24f, steepness = 0.15f },
            new GerstnerWave { direction = new Vector2(-0.2f, 1f),   wavelength = 13f, steepness = 0.10f },
            new GerstnerWave { direction = new Vector2(0.5f, 0.8f),  wavelength = 8f,  steepness = 0.07f },
        };

        // --- Swell front: a band of heavy water sweeping across the world ---
        // Spatial, not global: you can see it coming, sail away from it, or
        // hide behind an island until it passes.
        Vector2 swellDir;
        Vector2 swellOrigin;
        float swellStart = -999f;
        float swellSpeed;
        float swellHalfWidth;
        float swellSteepness;
        float swellWavelength;
        bool swellActive;

        public bool SwellActive => swellActive;
        public Vector2 SwellDirection => swellDir;
        public float SwellSpeed => swellSpeed;
        public float SwellHalfWidth => swellHalfWidth;

        public void LaunchSwell(Vector2 origin, Vector2 direction, float speed,
            float halfWidth, float steepness, float wavelength)
        {
            swellOrigin = origin;
            swellDir = direction.normalized;
            swellSpeed = speed;
            swellHalfWidth = halfWidth;
            swellSteepness = steepness;
            swellWavelength = wavelength;
            swellStart = Time.time;
            swellActive = true;
        }

        public void ClearSwell() { swellActive = false; }

        /// Centre line of the front right now.
        public Vector2 SwellCenter(float time) =>
            swellOrigin + swellDir * (swellSpeed * (time - swellStart));

        /// 0 = calm water, 1 = the heart of the swell. Signed distance across
        /// the band, smoothed so the front builds and passes rather than snaps.
        public float SwellIntensity(Vector2 pos, float time)
        {
            if (!swellActive) return 0f;
            float along = Vector2.Dot(pos - SwellCenter(time), swellDir);
            float t = 1f - Mathf.Clamp01(Mathf.Abs(along) / swellHalfWidth);
            return Mathf.SmoothStep(0f, 1f, t);
        }

        /// Metres until the front reaches this point (negative once it's past).
        /// Positive dot = the point lies ahead of the front along its travel.
        public float SwellApproachDistance(Vector2 pos, float time)
        {
            if (!swellActive) return float.MaxValue;
            return Vector2.Dot(pos - SwellCenter(time), swellDir) - swellHalfWidth;
        }

        void OnEnable() { Instance = this; }
        void OnDisable() { if (Instance == this) Instance = null; }

        /// Full Gerstner displacement (dx, height, dz) for a rest-position point.
        public Vector3 Displace(Vector2 restPos, float time)
        {
            Vector3 d = Vector3.zero;
            for (int i = 0; i < waves.Length; i++)
            {
                var w = waves[i];
                Vector2 dir = w.direction.normalized;
                float k = 2f * Mathf.PI / w.wavelength;
                float amplitude = w.steepness / k;
                float omega = Mathf.Sqrt(Gravity * k);
                float phase = k * Vector2.Dot(dir, restPos) - omega * time;
                float cos = Mathf.Cos(phase);
                d.x += dir.x * amplitude * cos;
                d.z += dir.y * amplitude * cos;
                d.y += amplitude * Mathf.Sin(phase);
            }

            float env = SwellIntensity(restPos, time);
            if (env > 0.001f)
            {
                float k = 2f * Mathf.PI / swellWavelength;
                float amplitude = (swellSteepness * env) / k;
                float omega = Mathf.Sqrt(Gravity * k);
                float phase = k * Vector2.Dot(swellDir, restPos) - omega * time;
                float cos = Mathf.Cos(phase);
                d.x += swellDir.x * amplitude * cos;
                d.z += swellDir.y * amplitude * cos;
                d.y += amplitude * Mathf.Sin(phase);
            }
            return d;
        }


        /// Water surface height at a fixed horizontal world position.
        /// Iterates to undo the horizontal part of the Gerstner displacement.
        public float SampleHeight(Vector2 worldPos, float time)
        {
            Vector2 p = worldPos;
            for (int i = 0; i < 3; i++)
            {
                Vector3 d = Displace(p, time);
                p = worldPos - new Vector2(d.x, d.z);
            }
            return Displace(p, time).y;
        }
    }
}
