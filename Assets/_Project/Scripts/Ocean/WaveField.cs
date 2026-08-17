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

        // Transient rogue swell (rogue wave sets, milestone: sailing feel)
        Vector2 swellDir;
        float swellStart = -999f;
        float swellDuration;
        float swellSteepness;
        float swellWavelength;

        /// Direction the swell travels toward. Envelope ramps in and out so the
        /// set builds on the horizon, peaks, and passes.
        public void TriggerSwell(Vector2 direction, float duration, float steepness, float wavelength)
        {
            swellDir = direction.normalized;
            swellStart = Time.time;
            swellDuration = duration;
            swellSteepness = steepness;
            swellWavelength = wavelength;
        }

        public float SwellEnvelope
        {
            get
            {
                float t01 = (Time.time - swellStart) / swellDuration;
                if (t01 < 0f || t01 > 1f) return 0f;
                return Mathf.Sin(t01 * Mathf.PI);
            }
        }

        public Vector2 SwellDirection => swellDir;

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

            float env = SwellEnvelopeAt(time);
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

        float SwellEnvelopeAt(float time)
        {
            float t01 = (time - swellStart) / swellDuration;
            if (t01 < 0f || t01 > 1f) return 0f;
            return Mathf.Sin(t01 * Mathf.PI);
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
