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
        [SerializeField] int waveCount = 14;
        [SerializeField] Vector2 windDirection = new Vector2(1f, 0.22f);
        [SerializeField] float minWavelength = 9f;
        [SerializeField] float maxWavelength = 190f;

        // How big the sea is, in metres of amplitude, at sea state 1 before the
        // region scale. This replaced a `totalSteepness` dial, which conflated
        // two things that need separating: how TALL the sea is and how SHARP
        // it is. Amplitude is the honest size control; steepness now falls out
        // of the shape below.
        [Tooltip("Total base amplitude in metres at sea state 1.")]
        [SerializeField] float baseAmplitude = 3.9f;

        // "The larger a wave, the wider it needs to be."
        //
        // In Gerstner that is one identity: amplitude = steepness x wavelength
        // / 2pi. So if amplitude rises in PROPORTION to wavelength — ampPower 1
        // — every wave in the set carries the same steepness, and a tall crest
        // can only ever be a long one.
        //
        // The old spectrum did the opposite: it weighted steepness by a
        // log-normal energy curve around a 45m peak, which decoupled amplitude
        // from wavelength entirely. MEASURED before the change: the tallest
        // base wave was 71m and the 170m wave was the SMALLEST in the set at
        // 0.17m, so mid-length waves came out proportionally the tallest —
        // exactly the narrow spike the eye picks out.
        [Tooltip("Amplitude ~ wavelength^this. 1 = every wave equally steep.")]
        [Range(0.4f, 1.6f)] [SerializeField] float ampPower = 1f;
        [Tooltip("Per-wave amplitude scatter, so the spectrum is not a ramp.")]
        [Range(0f, 0.6f)] [SerializeField] float ampJitter = 0.22f;

        // Directional spread, shortest wave .. longest wave.
        //
        // The old spreading was peakL/wavelength, which pinned the LONG waves —
        // the ones that carry the sea's shape — to within about +/-9 degrees of
        // the wind. Measured: the four longest base waves ran at -1, 4, 11 and
        // -1 degrees off it. A sea built that way is a set of rows by
        // construction, and only the storm's cross trains ever broke it up
        // (row-ness 0.76 inside the storm against 0.35 just outside it).
        //
        // There is no longer any reason for it. Wind stopped being physics in
        // 3fd9309 and is weather flavour now; nothing in the gameplay reads the
        // spectrum's directions. So the arc opens right out.
        [Tooltip("Half-arc in degrees for the shortest waves.")]
        [SerializeField] float shortSpreadDegrees = 125f;
        [Tooltip("Half-arc in degrees for the longest waves. This is the " +
                 "row-ness dial: small values put the big waves back in rows.")]
        [SerializeField] float longSpreadDegrees = 80f;

        // Gerstner's sharp crest comes from the HORIZONTAL part of the
        // displacement pinching the tops together. Scaling it down rounds the
        // crests without touching wave height at all, which is the one control
        // that separates "sharp" from "big".
        [Tooltip("Horizontal displacement scale. 1 = full Gerstner cusping, " +
                 "lower = rounder tops at the same wave height.")]
        [Range(0.15f, 1f)] [SerializeField] float choppiness = 0.72f;

        // A Gerstner surface folds through itself once the HORIZONTAL
        // steepness sums past 1 — and only the horizontal part, which is what
        // makes this safe. Vertical height is not constrained by it at all.
        //
        // So choppiness is also the safety valve: hold `chop x total steepness`
        // under the limit and the sea can be made as tall as the design wants
        // without ever turning inside out. Computed against the WORST case
        // (farthest region, current sea state) and uploaded as one uniform, so
        // the CPU and the shader cannot disagree about it.
        [Tooltip("Ceiling on chop x total steepness. Under 1 or the sea folds.")]
        [Range(0.3f, 0.95f)] [SerializeField] float foldLimit = 0.85f;

        float sumSteepness = 1f;

        /// The choppiness actually in force, after the anti-fold clamp.
        public float EffectiveChop { get; private set; } = 1f;

        [Header("Storm sea (the western deep)")]
        // A storm sea is not a bigger wind sea — it is several trains
        // DISAGREEING. Where they add you get a pyramid peak, where they
        // cancel you get a hole, and that interference is what stops the
        // ocean looking like a corrugated roof.
        //
        // The base spectrum can't provide it: its directional spreading is
        // peakL/wavelength, which clamps the LONG waves — the ones that give
        // the sea its shape — to about ±9° of the wind. So they march in rows
        // by construction. These cross trains are the fix.
        [Tooltip("Extra wave trains crossing the wind sea. 0 disables the storm.")]
        [SerializeField] int stormWaveCount = 6;
        // These used to be pinned to a 58-124 degree band off the wind,
        // because their job was to CROSS a base spectrum that ran in rows. The
        // base spectrum no longer runs in rows, so a band of six big waves all
        // travelling within 60 degrees of one bearing just becomes the new
        // dominant axis: measured row-ness 1.87 in the storm against 1.09 on
        // the home shelf, from the same spectrum plus these. They now spread
        // like everything else, and cross each other by covering the fan
        // rather than by being aimed across it.
        [Tooltip("Half-arc in degrees for the storm trains.")]
        [SerializeField] float stormSpreadDegrees = 140f;
        [Tooltip("Wavelength band for the storm trains — long, so they build real peaks.")]
        [SerializeField] Vector2 stormWavelengths = new Vector2(120f, 340f);
        // Specified in metres for the same reason the base spectrum is: with
        // steepness as the dial, the sea's SIZE depended on the wavelength
        // lottery. Changing the number of random draws in the base loop
        // silently shrank the storm from 12.7m of amplitude to 9.9m — a 22%
        // smaller storm, from a change that had nothing to do with the storm.
        // This is the "section of the sea that is a large epic ocean" dial.
        //
        // Storm waves are gated by StormAmount01, which is zero on the home
        // shelf, so raising this makes the western deep enormous WITHOUT
        // touching the calm water the village fishes in. At 12.7m the storm
        // waves ran amp/L around 0.011 — a 0.6 degree face, which is a gentle
        // swell no matter how tall the numbers say it is, and read as a flat
        // sheet on screen even with the fog opened right up to see it.
        [Tooltip("Total amplitude of the storm trains at full storm, in metres.")]
        [SerializeField] float stormAmplitude = 40f;

        [Header("Where the storm lives")]
        [Tooltip("Direction the storm lies in, from home. Default is due west.")]
        [SerializeField] Vector2 stormBearing = new Vector2(-1f, 0f);
        [Tooltip("Metres in that direction before the storm starts to build.")]
        [SerializeField] float stormNear = 450f;
        [Tooltip("Metres before it is in full fury.")]
        [SerializeField] float stormFar = 1250f;

        int stormStart;   // index of the first cross-train wave
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

        const int MaxGpuWaves = 24;
        static readonly int WavesId = Shader.PropertyToID("_SS_Waves");
        static readonly int SeaRegionId = Shader.PropertyToID("_SS_SeaRegion");
        static readonly int SeaRegionScaleId = Shader.PropertyToID("_SS_SeaRegionScale");
        static readonly int StormId = Shader.PropertyToID("_SS_Storm");
        static readonly int StormStartId = Shader.PropertyToID("_SS_StormStart");
        static readonly int ChopId = Shader.PropertyToID("_SS_Chop");
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

            Vector2 sdir = stormBearing.sqrMagnitude < 0.0001f
                ? Vector2.left : stormBearing.normalized;
            Shader.SetGlobalVector(StormId, new Vector4(sdir.x, sdir.y, stormNear, stormFar));
            Shader.SetGlobalFloat(StormStartId, stormWaveCount > 0 ? stormStart : 9999);
            Shader.SetGlobalFloat(ChopId, EffectiveChop);
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

        /// Hands out direction strata in mirrored pairs, biggest wave first.
        ///
        /// Dividing the arc into strata and using each exactly once beats
        /// random draws, which cluster and leave holes. But coverage alone is
        /// not enough: amplitude rises with wavelength, so the few longest
        /// waves decide what the sea looks like, and a plain shuffle that puts
        /// three of the four longest on one side simply rebuilds rows on a new
        /// bearing. Pairing the biggest waves into mirrored strata makes the
        /// dominant components cancel each other's bearing by construction,
        /// while WHICH pair each takes stays random so the fan never repeats.
        ///
        /// Expects `count` waves ordered smallest to largest.
        static int[] BalancedStrata(int count, System.Random rnd)
        {
            var strata = new int[count];
            int halves = count / 2;
            var pairs = new int[Mathf.Max(1, halves)];
            for (int i = 0; i < halves; i++) pairs[i] = i;
            for (int i = halves - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (pairs[i], pairs[j]) = (pairs[j], pairs[i]);
            }
            int cursor = 0;
            for (int a = count - 1; a >= 0; a -= 2)
            {
                if (a - 1 < 0) { strata[a] = count / 2; break; }   // odd one out: centre
                int pair = pairs[cursor++ % Mathf.Max(1, halves)];
                strata[a] = pair;
                strata[a - 1] = count - 1 - pair;
            }
            return strata;
        }

        void BuildSpectrum()
        {
            int baseCount = Mathf.Max(1, waveCount);
            int storm = Mathf.Max(0, stormWaveCount);
            waves = new GerstnerWave[baseCount + storm];
            stormStart = baseCount;
            var rnd = new System.Random(seed);
            float baseAngle = Mathf.Atan2(windDirection.x, windDirection.y);

            // Wavelength rises with index, so the array is already ordered
            // smallest to largest — exactly what BalancedStrata expects.
            var strata = BalancedStrata(baseCount, rnd);

            var shape = new float[baseCount];
            float shapeSum = 0f;

            for (int i = 0; i < baseCount; i++)
            {
                float t = (i + 0.5f) / baseCount;
                float wavelength = Mathf.Exp(Mathf.Lerp(
                    Mathf.Log(minWavelength), Mathf.Log(maxWavelength), t));

                // Amplitude rises with wavelength: bigger IS wider, by
                // construction rather than by tuning.
                float jitter = 1f + ((float)rnd.NextDouble() * 2f - 1f) * ampJitter;
                shape[i] = Mathf.Pow(wavelength, ampPower) * Mathf.Max(0.1f, jitter);
                shapeSum += shape[i];

                float arc = Mathf.Lerp(shortSpreadDegrees, longSpreadDegrees, t) * Mathf.Deg2Rad;
                float frac = (strata[i] + 0.5f) / baseCount * 2f - 1f;          // -1 .. +1
                frac += ((float)rnd.NextDouble() * 2f - 1f) / baseCount;         // within the stratum
                float angle = baseAngle + Mathf.Clamp(frac, -1f, 1f) * arc;

                waves[i] = new GerstnerWave
                {
                    direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)),
                    wavelength = wavelength,
                    phase = (float)rnd.NextDouble() * Mathf.PI * 2f,
                };
            }

            // Steepness is derived, never dialled: amp = steepness x L / 2pi,
            // so steepness = amp x k. With ampPower 1 this comes out the same
            // for every wave in the set.
            if (shapeSum <= 0f) shapeSum = 1f;
            for (int i = 0; i < baseCount; i++)
            {
                float amp = baseAmplitude * shape[i] / shapeSum;
                waves[i].steepness = amp * (2f * Mathf.PI / waves[i].wavelength);
            }

            // The storm trains: long, big, and spread right around the fan so
            // that where they add you get a standing pyramid and where they
            // cancel you get a hole. That interference is what stops a heavy
            // sea reading as a corrugated roof — but it comes from the trains
            // disagreeing with EACH OTHER, which a wide spread gives, not from
            // aiming them all across one bearing.
            var stormL = new float[storm];
            for (int i = 0; i < storm; i++)
                stormL[i] = Mathf.Lerp(stormWavelengths.x, stormWavelengths.y,
                    (float)rnd.NextDouble());
            // BalancedStrata pairs off the biggest first, so it needs them in
            // size order; the draws above are not.
            System.Array.Sort(stormL);
            var stormStrata = BalancedStrata(storm, rnd);

            float stormShapeSum = 0f;
            for (int i = 0; i < storm; i++)
            {
                float frac = (stormStrata[i] + 0.5f) / storm * 2f - 1f;          // -1 .. +1
                frac += ((float)rnd.NextDouble() * 2f - 1f) / storm;              // within the stratum
                float angle = baseAngle
                            + Mathf.Clamp(frac, -1f, 1f) * stormSpreadDegrees * Mathf.Deg2Rad;

                waves[baseCount + i] = new GerstnerWave
                {
                    direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)),
                    wavelength = stormL[i],
                    phase = (float)rnd.NextDouble() * Mathf.PI * 2f,
                };
                stormShapeSum += Mathf.Pow(stormL[i], ampPower);
            }

            // Same rule as the base spectrum: bigger is wider, and the total
            // size is fixed in metres so the wavelength draw cannot change it.
            if (stormShapeSum <= 0f) stormShapeSum = 1f;
            for (int i = 0; i < storm; i++)
            {
                int w = baseCount + i;
                float amp = stormAmplitude * Mathf.Pow(waves[w].wavelength, ampPower) / stormShapeSum;
                waves[w].steepness = amp * (2f * Mathf.PI / waves[w].wavelength);
            }

            // Worst case for the fold clamp: every wave present at full storm.
            sumSteepness = 0f;
            for (int i = 0; i < waves.Length; i++) sumSteepness += waves[i].steepness;
        }

        /// How deep into the storm this spot is: 0 in home waters, 1 out in the
        /// western deep. Measured along the storm bearing rather than by an
        /// angle, so it is one dot product and one smoothstep — trivial to keep
        /// identical to StormAmount() in Ocean.shader, which it must be.
        public float StormAmount01(Vector2 p)
        {
            if (stormWaveCount <= 0) return 0f;
            Vector2 dir = stormBearing.sqrMagnitude < 0.0001f ? Vector2.left : stormBearing.normalized;
            float along = Vector2.Dot(p - HomeXZ, dir);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(stormNear, stormFar, along));
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

            // Displace multiplies the whole thing by RegionScale and by the sea
            // state, so both scale the horizontal steepness that decides
            // folding. Clamp against the deepest water, whether or not we are
            // standing in it — one global value keeps CPU and GPU in step.
            // The swell front is a wave too, and it was missing from this sum
            // — so a front standing over the ship could push the surface past
            // the fold limit the clamp believed it was holding.
            float total = sumSteepness + (swellActive ? Mathf.Abs(swellSteepness) : 0f);
            float worst = total * Mathf.Max(nearScale, farScale) * Mathf.Max(0.01f, cachedSea);
            EffectiveChop = Mathf.Min(choppiness, foldLimit / Mathf.Max(0.0001f, worst));
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
        /// The Gerstner Jacobian at a point: 1 is undisturbed water, 0 means
        /// the horizontal displacement has folded the surface over itself,
        /// which is where a wave genuinely breaks. Mirrors BreakingAmount() in
        /// Ocean.shader so the foam threshold can be set from measurement
        /// rather than from a guess.
        public float JacobianAt(Vector2 p)
        {
            EnsureConstants(Time.time);
            float shore = ShoreAttenuation(p) * RegionScale(p);
            if (shore <= 0.001f) return 1f;
            float storm = StormAmount01(p);

            float jxx = 0f, jzz = 0f, jxz = 0f;
            for (int i = 0; i < constants.Length; i++)
            {
                var c = constants[i];
                float amp = i >= stormStart ? c.amp * storm : c.amp;
                if (amp == 0f) continue;
                float k = Mathf.Sqrt(c.kx * c.kx + c.kz * c.kz);
                float sn = Mathf.Sin(c.kx * p.x + c.kz * p.y + c.phaseOffset)
                           * amp * k * shore * EffectiveChop;
                jxx -= c.dirX * c.dirX * sn;
                jzz -= c.dirZ * c.dirZ * sn;
                jxz -= c.dirX * c.dirZ * sn;
            }
            return (1f + jxx) * (1f + jzz) - jxz * jxz;
        }

        public Vector3 Displace(Vector2 restPos, float time)
        {
            EnsureConstants(time);
            float shore = ShoreAttenuation(restPos) * RegionScale(restPos);
            if (shore <= 0.001f) return Vector3.zero;
            float x = restPos.x, z = restPos.y;
            float storm = StormAmount01(restPos);
            float dx = 0f, dy = 0f, dz = 0f;
            for (int i = 0; i < constants.Length; i++)
            {
                var c = constants[i];
                float amp = i >= stormStart ? c.amp * storm : c.amp;
                if (amp == 0f) continue;
                float ph = c.kx * x + c.kz * z + c.phaseOffset;
                float cos = Mathf.Cos(ph);
                float ampCos = amp * cos * EffectiveChop;
                dx += c.dirX * ampCos;
                dz += c.dirZ * ampCos;
                dy += amp * Mathf.Sin(ph);
            }
            var d = new Vector3(dx, dy, dz);

            float env = SwellIntensity(restPos, time);
            if (env > 0.001f)
            {
                float k = 2f * Mathf.PI / swellWavelength;
                float amplitude = (swellSteepness * env) / k;
                float omega = Mathf.Sqrt(Gravity * k);
                float ph = k * Vector2.Dot(swellDir, restPos) - omega * time;
                float cos = Mathf.Cos(ph) * EffectiveChop;
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
