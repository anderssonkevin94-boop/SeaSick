using UnityEngine;

namespace SeaSick.Ocean
{
    public struct GerstnerWave
    {
        public Vector2 direction;
        public float wavelength;
        public float steepness;
        public float phase;
    }

    /// The sea surface, built from a wave *spectrum* rather than a handful of
    /// hand-placed waves. Components are spread across a band of wavelengths
    /// with energy peaking at the wind-driven peak, each with a random phase
    /// and its own direction — long swell runs tight to the wind, short chop
    /// fans out widely, exactly as real seas do. A slow-drifting sea state
    /// takes the whole thing from glassy calm to rough and back.
    ///
    /// Still the single source of truth: mesh, buoyancy, surf and roughness
    /// all sample this.
    public class WaveField : MonoBehaviour
    {
        public static WaveField Instance { get; private set; }

        const float Gravity = 9.81f;

        [Header("Spectrum")]
        [SerializeField] int waveCount = 10;
        [Tooltip("Drives the peak wavelength — bigger wind, longer swell.")]
        [SerializeField] float windSpeed = 11f;
        [SerializeField] Vector2 windDirection = new Vector2(1f, 0.22f);
        [SerializeField] float minWavelength = 9f;
        [SerializeField] float maxWavelength = 190f;
        [Tooltip("Total steepness shared across the spectrum. The look dial.")]
        [SerializeField] float totalSteepness = 0.34f;
        [Tooltip("Width of the energy peak in log-wavelength space.")]
        [SerializeField] float spectrumWidth = 0.62f;
        [SerializeField] int seed = 1337;

        [Header("Sea state (drifts over minutes)")]
        [SerializeField] float calmFloor = 0.14f;
        [SerializeField] float roughCeiling = 1.15f;
        [SerializeField] float changeSpeed = 0.011f;

        GerstnerWave[] waves;

        /// 0.14 ≈ glassy, 1.15 ≈ rough. Everything scales off this.
        public float SeaState01 { get; private set; } = 1f;
        public Vector2 WindDirection => windDirection.normalized;

        void OnEnable() { Instance = this; }
        void OnDisable() { if (Instance == this) Instance = null; }

        void Awake() { BuildSpectrum(); }

        void Update()
        {
            float n = Mathf.PerlinNoise(Time.time * changeSpeed, 3.71f);
            SeaState01 = Mathf.Lerp(SeaState01, Mathf.Lerp(calmFloor, roughCeiling, n),
                1f - Mathf.Exp(-0.5f * Time.deltaTime));
        }

        void BuildSpectrum()
        {
            waves = new GerstnerWave[Mathf.Max(1, waveCount)];
            var rnd = new System.Random(seed);

            // Peak wavelength of a wind-driven sea, roughly 0.6 v² (metres).
            float peakL = Mathf.Clamp(0.62f * windSpeed * windSpeed,
                minWavelength * 2f, maxWavelength * 0.85f);
            float baseAngle = Mathf.Atan2(windDirection.x, windDirection.y);

            var energies = new float[waves.Length];
            float energySum = 0f;

            for (int i = 0; i < waves.Length; i++)
            {
                float t = (i + 0.5f) / waves.Length;
                float wavelength = Mathf.Exp(Mathf.Lerp(
                    Mathf.Log(minWavelength), Mathf.Log(maxWavelength), t));

                // Energy peaks at the spectral peak and falls off either side.
                float logRatio = Mathf.Log(wavelength / peakL);
                float energy = Mathf.Exp(-(logRatio * logRatio) / (2f * spectrumWidth * spectrumWidth));
                energies[i] = energy;
                energySum += energy;

                // Directional spreading: short waves fan out, long swell stays
                // tight to the wind. This is why a real sea never looks like
                // one marching set of rollers.
                float spread = Mathf.Clamp(peakL / wavelength, 0.3f, 3.2f);
                float offset = ((float)rnd.NextDouble() * 2f - 1f) * 0.5f * spread;
                offset = Mathf.Clamp(offset, -1.35f, 1.35f);
                float angle = baseAngle + offset;

                waves[i] = new GerstnerWave
                {
                    direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)),
                    wavelength = wavelength,
                    phase = (float)rnd.NextDouble() * Mathf.PI * 2f,
                };
            }

            if (energySum <= 0f) energySum = 1f;
            for (int i = 0; i < waves.Length; i++)
                waves[i].steepness = totalSteepness * energies[i] / energySum;
        }

        // --- Swell front: a band of heavy water sweeping across the world ---
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

        public Vector2 SwellCenter(float time) =>
            swellOrigin + swellDir * (swellSpeed * (time - swellStart));

        public float SwellIntensity(Vector2 pos, float time)
        {
            if (!swellActive) return 0f;
            float along = Vector2.Dot(pos - SwellCenter(time), swellDir);
            float t = 1f - Mathf.Clamp01(Mathf.Abs(along) / swellHalfWidth);
            return Mathf.SmoothStep(0f, 1f, t);
        }

        /// Metres until the front reaches this point (negative once it's past).
        public float SwellApproachDistance(Vector2 pos, float time)
        {
            if (!swellActive) return float.MaxValue;
            return Vector2.Dot(pos - SwellCenter(time), swellDir) - swellHalfWidth;
        }

        /// Per-frame constants. Recomputing k, omega and amplitude inside the
        /// per-vertex loop meant a division and a square root per wave per
        /// vertex — tens of thousands of them a frame. Hoisting them here is
        /// the difference between a playable ocean and a slideshow.
        struct WaveConstants
        {
            public float kx, kz;      // direction * wavenumber
            public float amp;
            public float phaseOffset; // wave phase minus omega*time
            public float dirX, dirZ;
        }

        WaveConstants[] constants;
        float cachedTime = float.NaN;
        float cachedSea = float.NaN;

        void EnsureConstants(float time)
        {
            if (cachedTime == time && cachedSea == SeaState01 && constants != null) return;
            if (constants == null || constants.Length != waves.Length)
                constants = new WaveConstants[waves.Length];

            cachedTime = time;
            cachedSea = SeaState01;
            for (int i = 0; i < waves.Length; i++)
            {
                var w = waves[i];
                float k = 2f * Mathf.PI / w.wavelength;
                float omega = Mathf.Sqrt(Gravity * k);
                constants[i] = new WaveConstants
                {
                    kx = w.direction.x * k,
                    kz = w.direction.y * k,
                    amp = (w.steepness * cachedSea) / k,
                    phaseOffset = w.phase - omega * time,
                    dirX = w.direction.x,
                    dirZ = w.direction.y,
                };
            }
        }

        /// Full Gerstner displacement (dx, height, dz) for a rest-position point.
        public Vector3 Displace(Vector2 restPos, float time)
        {
            EnsureConstants(time);
            float x = restPos.x, z = restPos.y;
            float dx = 0f, dy = 0f, dz = 0f;
            for (int i = 0; i < constants.Length; i++)
            {
                var c = constants[i];
                float ph = c.kx * x + c.kz * z + c.phaseOffset;
                float cos = Mathf.Cos(ph);
                float ampCos = c.amp * cos;
                dx += c.dirX * ampCos;
                dz += c.dirZ * ampCos;
                dy += c.amp * Mathf.Sin(ph);
            }
            var d = new Vector3(dx, dy, dz);

            float env = SwellIntensity(restPos, time);
            if (env > 0.001f)
            {
                float k = 2f * Mathf.PI / swellWavelength;
                float amplitude = (swellSteepness * env) / k;
                float omega = Mathf.Sqrt(Gravity * k);
                float ph = k * Vector2.Dot(swellDir, restPos) - omega * time;
                float cos = Mathf.Cos(ph);
                d.x += swellDir.x * amplitude * cos;
                d.z += swellDir.y * amplitude * cos;
                d.y += amplitude * Mathf.Sin(ph);
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

        /// One-iteration version for decoration (foam, flotsam, gust ripples)
        /// where a few centimetres of error will never be noticed.
        public float SampleHeightFast(Vector2 worldPos, float time)
        {
            Vector3 d = Displace(worldPos, time);
            return Displace(worldPos - new Vector2(d.x, d.z), time).y;
        }
    }
}
