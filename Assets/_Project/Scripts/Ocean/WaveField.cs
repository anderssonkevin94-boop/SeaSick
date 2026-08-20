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
        // Shorter peak wavelength relative to a 21m hull: on 75m swell the ship
        // barely moved and sat ON the sea like a toy. Around 45m it rides
        // through the waves instead of over them.
        [Tooltip("Drives the peak wavelength — bigger wind, longer swell.")]
        [SerializeField] float windSpeed = 8.5f;
        [SerializeField] Vector2 windDirection = new Vector2(1f, 0.22f);
        [SerializeField] float minWavelength = 9f;
        [SerializeField] float maxWavelength = 190f;
        [Tooltip("Total steepness shared across the spectrum. The look dial.")]
        [SerializeField] float totalSteepness = 0.46f;
        [Tooltip("Width of the energy peak in log-wavelength space.")]
        [SerializeField] float spectrumWidth = 0.62f;
        [SerializeField] int seed = 1337;

        [Header("Sea state (drifts over minutes)")]
        [SerializeField] float calmFloor = 0.14f;
        [SerializeField] float roughCeiling = 1.15f;
        [SerializeField] float changeSpeed = 0.011f;

        [Header("Shore")]
        [Tooltip("Metres over which waves die down as they approach land.")]
        [SerializeField] float shoreFalloff = 60f;

        GerstnerWave[] waves;

        /// 0.14 ≈ glassy, 1.15 ≈ rough. Everything scales off this.
        public float SeaState01 { get; private set; } = 1f;

        [Header("Regional sea state")]
        // Distance IS the difficulty, made physical. A calm shelf around home
        // where the tribe has always fished, then worse and worse water the
        // further out you go, until the folklore turns out to be true.
        // The drifting SeaState01 multiplies on top, so the outer waters are
        // not ALWAYS mountainous — a calm window out there is a real chance.
        [Tooltip("Out to here the water is home-shelf calm.")]
        [SerializeField] float calmRadius = 260f;
        [Tooltip("Past here it is as big as it gets.")]
        [SerializeField] float wildRadius = 1150f;
        [Tooltip("Amplitude multiplier on the home shelf.")]
        [SerializeField] float nearScale = 0.35f;
        [Tooltip("Amplitude multiplier out in the deep.")]
        [SerializeField] float farScale = 1.5f;

        Transform homePoint;
        bool homeSearched;

        Vector2 HomeXZ
        {
            get
            {
                if (!homeSearched)
                {
                    homeSearched = true;
                    var voyage = FindAnyObjectByType<Voyage.VoyageManager>();
                    if (voyage != null) homePoint = voyage.HomePoint;
                }
                return homePoint != null
                    ? new Vector2(homePoint.position.x, homePoint.position.z)
                    : Vector2.zero;
            }
        }

        /// How big the sea is allowed to get here, purely from how far out you
        /// are. **Must stay identical to RegionScale() in Ocean.shader** or the
        /// ship floats on water that isn't the water you can see.
        public float RegionScale(Vector2 p)
        {
            Vector2 home = HomeXZ;
            float dist = Vector2.Distance(p, home);
            float t = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(calmRadius, wildRadius, dist));
            return Mathf.Lerp(nearScale, farScale, t);
        }
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

        void LateUpdate() { PushToGpu(Time.time); }

        const int MaxGpuWaves = 16;
        static readonly int WavesId = Shader.PropertyToID("_SS_Waves");
        static readonly int SeaRegionId = Shader.PropertyToID("_SS_SeaRegion");
        static readonly int SeaRegionScaleId = Shader.PropertyToID("_SS_SeaRegionScale");
        static readonly int WaveCountId = Shader.PropertyToID("_SS_WaveCount");
        static readonly int SwellId = Shader.PropertyToID("_SS_Swell");
        static readonly int SwellFrontId = Shader.PropertyToID("_SS_SwellFront");
        static readonly int SwellExtraId = Shader.PropertyToID("_SS_SwellExtra");
        readonly Vector4[] gpuWaves = new Vector4[MaxGpuWaves];

        const int MaxGpuIslands = 24;
        static readonly int IslandsId = Shader.PropertyToID("_SS_Islands");
        static readonly int IslandCountId = Shader.PropertyToID("_SS_IslandCount");
        readonly Vector4[] gpuIslands = new Vector4[MaxGpuIslands];

        /// Hand the same constants the CPU uses to the vertex shader, so the
        /// visible surface and the surface the ship floats on are identical.
        void PushToGpu(float time)
        {
            EnsureConstants(time);
            int count = Mathf.Min(constants.Length, MaxGpuWaves);
            for (int i = 0; i < count; i++)
            {
                var c = constants[i];
                gpuWaves[i] = new Vector4(c.kx, c.kz, c.amp, c.phaseOffset);
            }
            for (int i = count; i < MaxGpuWaves; i++) gpuWaves[i] = Vector4.zero;

            Shader.SetGlobalVectorArray(WavesId, gpuWaves);
            Vector2 home = HomeXZ;
            Shader.SetGlobalVector(SeaRegionId,
                new Vector4(home.x, home.y, calmRadius, wildRadius));
            Shader.SetGlobalVector(SeaRegionScaleId,
                new Vector4(nearScale, farScale, 0f, 0f));
            Shader.SetGlobalInt(WaveCountId, count);

            // Shore falloff data, so the shader kills the same waves the
            // physics does and the sea stops cutting through the beaches.
            var isles = World.Island.All;
            int islandCount = Mathf.Min(isles.Count, MaxGpuIslands);
            for (int i = 0; i < islandCount; i++)
            {
                var isle = isles[i];
                if (isle == null) { gpuIslands[i] = Vector4.zero; continue; }
                Vector3 c = isle.transform.position;
                float inner = isle.MaxRadius * 1.02f;
                gpuIslands[i] = new Vector4(c.x, c.z, inner, inner + shoreFalloff);
            }
            for (int i = islandCount; i < MaxGpuIslands; i++) gpuIslands[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(IslandsId, gpuIslands);
            Shader.SetGlobalInt(IslandCountId, islandCount);

            if (swellActive)
            {
                float k = 2f * Mathf.PI / swellWavelength;
                float omega = Mathf.Sqrt(Gravity * k);
                Shader.SetGlobalVector(SwellId, new Vector4(
                    swellDir.x * k, swellDir.y * k, swellSteepness / k, -omega * time));
                Vector2 centre = SwellCenter(time);
                Shader.SetGlobalVector(SwellFrontId,
                    new Vector4(centre.x, centre.y, swellDir.x, swellDir.y));
                Shader.SetGlobalVector(SwellExtraId, new Vector4(swellHalfWidth, 1f, 0f, 0f));
            }
            else
            {
                Shader.SetGlobalVector(SwellExtraId, Vector4.zero);
            }
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

        /// How heavy the sea is here, 0 glassy .. 1 biblical. Combines the
        /// drifting sea state with any swell front standing over this spot.
        /// This is the number a heading now argues with — the ONLY one.
        public float SeaSeverity01(Vector2 pos, float time)
        {
            // Scaled by region so what slows the ship is the water actually
            // standing around it, not a global average.
            float baseSea = Mathf.InverseLerp(0.25f, 1.15f, SeaState01 * RegionScale(pos));
            float swell = swellActive ? SwellIntensity(pos, time) : 0f;
            return Mathf.Clamp01(baseSea + swell * 0.85f);
        }

        /// Which way the dominant seas are RUNNING here. A swell front standing
        /// over the ship overrules the ordinary wind sea, because that is what
        /// the hull is actually climbing.
        public Vector2 SeaRunDirection(Vector2 pos, float time)
        {
            if (swellActive && SwellIntensity(pos, time) > 0.4f) return swellDir.normalized;
            return WindDirection;
        }

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
            // Other components sample the water from their own Awake/LateUpdate,
            // and script order isn't guaranteed — build on demand rather than
            // relying on ours having run first.
            if (waves == null) BuildSpectrum();
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

        /// Waves shoal and die as they reach land. Without this the open-sea
        /// swell drives straight through the islands and clips the beaches.
        /// The ocean shader applies the identical falloff.
        public float ShoreAttenuation(Vector2 p)
        {
            float atten = 1f;
            var isles = World.Island.All;
            for (int i = 0; i < isles.Count; i++)
            {
                var isle = isles[i];
                if (isle == null) continue;
                Vector3 c = isle.transform.position;
                float dx = p.x - c.x, dz = p.y - c.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                float inner = isle.MaxRadius * 1.02f;
                float outer = inner + shoreFalloff;
                if (dist >= outer) continue;
                atten = Mathf.Min(atten, Mathf.SmoothStep(0f, 1f, (dist - inner) / (outer - inner)));
                if (atten <= 0f) return 0f;
            }
            return atten;
        }

        /// Full Gerstner displacement (dx, height, dz) for a rest-position point.
        public Vector3 Displace(Vector2 restPos, float time)
        {
            EnsureConstants(time);
            float shore = ShoreAttenuation(restPos) * RegionScale(restPos);
            if (shore <= 0.001f) return Vector3.zero;
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
            return d * shore;
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
