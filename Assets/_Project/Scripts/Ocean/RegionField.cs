using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SeaSick.Ocean
{
    /// Blittable copy of the regional envelope for Burst jobs. The same
    /// formula exists in RegionField.hlsl; the two are held identical by the
    /// DivergenceProbe, which runs the HLSL side end-to-end against this one.
    public struct RegionFieldParams
    {
        public float2 home;
        public float calmRadius, wildRadius, nearScale, farScale, shoreFalloff;
        public int islandCount;
        // Shore grid: world-anchored terrain heights, bilinear, 1 outside the rect.
        public float2 shoreOrigin;
        public float shoreInvSize;   // 1 / (metres covered by the grid)
        public int shoreN;           // texels per edge, 0 = no grid
        public float shoalDepthFull; // depth (m) at and below which waves are untouched
        public float shoalDepthZero; // depth (m) at and above which waves are gone (shoreline)
        public float chopFloor;      // envelope never falls below this in water
        public float breakFraction;  // a wave may not exceed this x the water depth
        public float waveHs;         // the open-sea Hs the spectrum is producing, m
        // Drifting roughness patches (WeatherField). Tile is sampled with WRAP.
        public float weatherInvTile; // 1 / metres the tile covers
        public float2 weatherOffset; // drift, in tile units
        public int weatherN;         // texels per edge, 0 = no field
        public float patchLo;        // the most a patch takes off the sea
        public float3 patchCoupling; // how much each cascade feels a patch

        public static RegionFieldParams Neutral => new RegionFieldParams
        {
            home = float2.zero,
            calmRadius = 0f,
            wildRadius = 1f,
            nearScale = 1f,
            farScale = 1f,
            shoreFalloff = 1f,
            islandCount = 0,
            shoreN = 0,
            shoalDepthFull = 8f,
            shoalDepthZero = 0.5f,
            chopFloor = 0f,
            breakFraction = 0f,
            waveHs = 0f,
            weatherInvTile = 0f,
            weatherOffset = float2.zero,
            weatherN = 0,
            patchLo = 1f,
            patchCoupling = float3.zero,
        };

        /// islands: xy = centre, z = radius. shore: terrain heights on the
        /// shore grid (length shoreN*shoreN; ignored when shoreN == 0).
        /// MUST match RegionField.hlsl.
        /// How strongly each cascade's waves feel the sea bed.
        /// MUST match SS_BottomCoupling in RegionField.hlsl.
        ///
        /// SHOALING IS A WAVELENGTH EFFECT, and treating it as a single scalar
        /// was wrong. A wave feels the bottom when kd is small -- when its
        /// wavelength is comparable to the depth. In 10 m of water a 300 m
        /// swell is fully shallow-water, a 30 m wave feels it partly, and a
        /// 3 m ripple does not feel it at all. Scaling every cascade by one
        /// factor made inshore water a 300 m swell shrunk to a couple of
        /// metres: amp/L about 0.005, which is precisely the "flat sheet"
        /// failure this project already shipped once. The water near the
        /// islands was not too SMALL, it was too FLAT.
        ///
        /// Cascade 0 stays exactly 1.0, so everything already measured and
        /// gated against the long swell is untouched.
        ///
        /// Bands: c0 >= 64 m, c1 16-64 m, c2 0.5-16 m.
        public static float BottomCoupling(int cascade) =>
            cascade == 0 ? 1f : cascade == 1 ? 0.40f : 0.12f;

        /// How strongly each cascade feels an island's RADIAL falloff.
        /// MUST match SS_IslandCoupling in RegionField.hlsl.
        ///
        /// This term is not the depth terms and does not do their job. It is a
        /// disc — a centre, a radius and 60 m of smoothstep — that knows
        /// nothing about the seabed, the wavelength, or which side of the
        /// island you are on. On the generated world an island's `MaxRadius`
        /// is its outer bound and not its beach: measured on Island_1, the
        /// land stops 330 m from the centre and the disc goes on to 1130,
        /// so it was flattening an 800 m ANNULUS OF 180 m DEEP WATER to the
        /// chop floor. That is the pond, and it is 800 m out from any shore.
        ///
        /// Swell does not stop at a circle. It wraps round an island and rolls
        /// onto the beach; what dies in an island's lee is the WIND SEA, the
        /// short stuff, because a lee is a wind shadow. So the disc now reads
        /// as shelter from short waves: nothing on the swell, about half on
        /// the mid band, all of it on the chop.
        ///
        /// Cascade 0 losing this term costs no safety, because the terms that
        /// actually keep the swell off the seabed are the depth ones and they
        /// are untouched: measured at storm on the same island, the swell in
        /// 11.6 m of water is on the depth cap (0.098) and in 3.6 m it is on
        /// the cap again (0.030), with the island falloff nowhere near
        /// binding. What changes is only the deep water offshore.
        ///
        /// Bands: c0 >= 64 m, c1 16-64 m, c2 0.5-16 m.
        public static float IslandCoupling(int cascade) =>
            cascade == 0 ? 0f : cascade == 1 ? 0.55f : 1f;

        /// Per-cascade envelope. Distance from home is the only
        /// wavelength-independent term; the depth terms and the island falloff
        /// both differ between cascades, and in opposite directions — shoaling
        /// is a LONG-wave effect and shelter is a SHORT-wave one.
        /// MUST match RegionEnvelopeCascades in RegionField.hlsl.
        public float3 EvaluateCascades(float2 p, NativeArray<float4> islands,
                                       NativeArray<float> shore, NativeArray<float> weather)
        {
            float d = math.distance(p, home);
            float t = math.smoothstep(calmRadius, wildRadius, d);
            float baseEnv = math.lerp(nearScale, farScale, t);
            // Kept out of baseEnv: each cascade feels it differently. See
            // IslandCoupling.
            float isle = 1f;
            for (int i = 0; i < islandCount; i++)
            {
                float s = math.distance(p, islands[i].xy) - islands[i].z;
                isle *= math.smoothstep(0f, shoreFalloff, s);
            }

            float shoal = 1f, wet = 1f, depth = 1e9f;
            if (shoreN > 0)
            {
                float3 swd = ShoreWetDepth(p, shore);
                shoal = swd.x; wet = swd.y; depth = swd.z;
            }
            // Never dead flat. Sheltered water still has chop; only land is
            // glass. Twin of the max() in RegionField.hlsl.
            float floorTerm = chopFloor * wet;

            // Drifting roughness patches. Hoisted out of the cascade loop --
            // it does not depend on the cascade, and this runs inside the
            // sampler's Newton loop eight times per query against a batch
            // budget that is nearly spent.
            //
            // Patches only ever REDUCE. An envelope above 1 asks for waves the
            // depth limit and the seabed cannot hold, and steeper water costs
            // another Newton step there is no budget for.
            float patch = 1f;
            if (weatherN > 0)
                patch = math.lerp(patchLo, 1f, WeatherAt(p, weather));

            float3 result = default;
            for (int c = 0; c < 3; c++)
            {
                float bw = BottomCoupling(c);
                // Weak on the swell, strong on the chop: a roughness patch is
                // short-wave energy and the long swell rolls straight through
                // it. That is what the sea does, and it also keeps patches
                // clear of everything that has been measured -- cascade 0
                // carries nearly all of Hs.
                float env = baseEnv * math.lerp(1f, isle, IslandCoupling(c))
                                    * math.lerp(1f, shoal, bw)
                                    * math.lerp(1f, patch, patchCoupling[c]);
                // Depth limit: no wave taller than a fraction of the water
                // under it -- the shoaling/breaking rule, which makes seabed
                // clipping structurally impossible at ANY wave size rather
                // than something to re-tune every time the sea grows. Capped
                // against the height THIS cascade carries, not the whole sea:
                // 5 m of chop in 10 m of water is not breaking just because a
                // 70 m swell would be.
                //
                // The cap is applied last so it beats the chop floor: in half
                // a metre of water there is no chop to have.
                float cap = float.MaxValue;
                if (shoreN > 0 && waveHs > 0.01f)
                    cap = math.max(0f, breakFraction * depth / (waveHs * bw));
                result[c] = math.min(math.max(env, floorTerm), cap);
            }
            return result;
        }

        /// Cascade 0's envelope: the long swell, and what every gameplay reader
        /// means by "how big is the sea here". BottomCoupling(0) is exactly 1,
        /// so the depth terms reach it in full; IslandCoupling(0) is exactly 0,
        /// so the radial disc does not reach it at all.
        public float Evaluate(float2 p, NativeArray<float4> islands,
                              NativeArray<float> shore, NativeArray<float> weather)
            => EvaluateCascades(p, islands, shore, weather).x;

        /// Wrapped bilinear over the weather tile. MUST match WeatherAt in
        /// RegionField.hlsl, which is hardware Repeat filtering: texel centres
        /// at (i + 0.5)/N, and a POSITIVE modulo, because world positions west
        /// of home make these coordinates negative and C#'s % does not.
        public float WeatherAt(float2 p, NativeArray<float> weather)
        {
            if (weatherN <= 0 || weather.Length < weatherN * weatherN) return 1f;
            int n = weatherN;
            float2 uv = p * weatherInvTile + weatherOffset;
            float2 f = uv * n - 0.5f;
            int2 i0 = (int2)math.floor(f);
            float2 w = f - i0;
            int x0 = WrapIndex(i0.x, n), y0 = WrapIndex(i0.y, n);
            int x1 = WrapIndex(i0.x + 1, n), y1 = WrapIndex(i0.y + 1, n);
            float a = weather[y0 * n + x0], b = weather[y0 * n + x1];
            float c = weather[y1 * n + x0], d = weather[y1 * n + x1];
            return math.lerp(math.lerp(a, b, w.x), math.lerp(c, d, w.x), w.y);
        }

        static int WrapIndex(int a, int n) { int r = a % n; return r < 0 ? r + n : r; }

        /// Shoal factor, "is there water here at all", and the depth itself,
        /// from one lookup. The chop floor needs the second (the sea keeps
        /// texture in sheltered water but must be perfectly gone over land)
        /// and the depth limit needs the third. Outside the grid the sea is
        /// untouched and nominally bottomless.
        public float3 ShoreWetDepth(float2 p, NativeArray<float> shore)
        {
            float2 uv = (p - shoreOrigin) * shoreInvSize;
            if (uv.x < 0f || uv.y < 0f || uv.x > 1f || uv.y > 1f)
                return new float3(1f, 1f, 1e9f);
            float2 f = uv * shoreN - 0.5f;
            int2 i0 = math.clamp((int2)math.floor(f), 0, shoreN - 1);
            int2 i1 = math.min(i0 + 1, shoreN - 1);
            float2 w = math.saturate(f - i0);
            float a = shore[i0.y * shoreN + i0.x], b = shore[i0.y * shoreN + i1.x];
            float c = shore[i1.y * shoreN + i0.x], e = shore[i1.y * shoreN + i1.x];
            float h = math.lerp(math.lerp(a, b, w.x), math.lerp(c, e, w.x), w.y);
            return new float3(math.smoothstep(shoalDepthZero, shoalDepthFull, -h),
                              math.smoothstep(0f, 0.5f, -h), -h);
        }

        /// 1 in deep water, 0 at the shoreline and over land. Bilinear over the
        /// grid with clamped edges; outside the rect the sea is untouched.
        public float ShoreFactor(float2 p, NativeArray<float> shore)
        {
            float2 uv = (p - shoreOrigin) * shoreInvSize;
            if (uv.x < 0f || uv.y < 0f || uv.x > 1f || uv.y > 1f) return 1f;
            float2 f = uv * shoreN - 0.5f;
            int2 i0 = math.clamp((int2)math.floor(f), 0, shoreN - 1);
            int2 i1 = math.min(i0 + 1, shoreN - 1);
            float2 w = math.saturate(f - i0);
            float a = shore[i0.y * shoreN + i0.x], b = shore[i0.y * shoreN + i1.x];
            float c = shore[i1.y * shoreN + i0.x], e = shore[i1.y * shoreN + i1.x];
            float h = math.lerp(math.lerp(a, b, w.x), math.lerp(c, e, w.x), w.y);
            return math.smoothstep(shoalDepthZero, shoalDepthFull, -h);
        }
    }

    /// The regional sea: a calm shelf around home, wilder water further out,
    /// and waves dying against island shores. Applied as a displacement
    /// multiplier after the (spatially homogeneous) FFT — in the shader on
    /// the GPU and post-sample in the Burst jobs, from this single parameter
    /// source. The westward storm weight additionally drives which weather
    /// state SeaStateController targets (M6).
    public class RegionField : MonoBehaviour
    {
        public const int MaxIslands = 24;

        public static RegionField Instance { get; private set; }

        [SerializeField] float calmRadius = 260f;
        [SerializeField] float wildRadius = 1150f;
        [SerializeField] float nearScale = 0.35f;
        [SerializeField] float farScale = 1.5f;
        [SerializeField] float shoreFalloff = 60f;

        [Header("Storm region (drives weather target, not the envelope)")]
        [SerializeField] Vector2 stormBearing = new Vector2(-1f, 0f);
        [SerializeField] float stormNear = 450f;
        [SerializeField] float stormFar = 1250f;

        [Tooltip("Bind home to VoyageManager.HomePoint and islands from the generated world. Probes that set their own geography disable this implicitly.")]
        [SerializeField] bool autoBindWorld = true;
        bool manualBound;
        bool worldBound;

        [Header("Shore grid (fed by the terrain)")]
        [Tooltip("Depth at and below which waves are untouched.")]
        [SerializeField] float shoalDepthFull = 8f;
        [Tooltip("Depth at and above which waves are gone — the shoreline.")]
        [SerializeField] float shoalDepthZero = 0.5f;
        [Tooltip("The envelope never falls below this in water, so sheltered anchorages keep some chop instead of turning to glass. Land is still perfectly flat.")]
        [Range(0f, 0.4f)] [SerializeField] float chopFloor = 0.12f;
        [Tooltip("A wave may not be taller than this fraction of the water under it. The real breaking index is about 0.78; below that leaves margin for the hull and the camera.")]
        [Range(0.1f, 0.8f)] [SerializeField] float breakFraction = 0.55f;

        // Pulled from the renderer and the weather field each frame rather
        // than pushed, so the data flows one way and nothing has to remember
        // to call a setter.
        float waveHs;
        float weatherInvTile; float2 weatherOffset; int weatherN;
        float patchLo = 1f; float3 patchCoupling;
        NativeArray<float> weather;    // borrowed from WeatherField, never owned
        Texture2D weatherTex;

        Vector2 home;
        readonly Vector4[] islandsGpu = new Vector4[MaxIslands];
        NativeArray<float4> islands;
        int islandCount;

        NativeArray<float> shore;      // heights, shoreN² (a 1-element dummy when absent)
        Texture2D shoreTex;
        float2 shoreOrigin; float shoreSize; int shoreN;

        public NativeArray<float4> Islands => islands;
        public NativeArray<float> Shore => shore;
        public int ShoreN => shoreN;
        public NativeArray<float> Weather => weather;

        /// The terrain hands over a world-anchored grid of heights (length n*n,
        /// row-major, x fastest) covering [origin, origin + size). Copied; the
        /// caller keeps ownership of its array.
        public void SetShore(NativeArray<float> heights, float2 origin, float size, int n)
        {
            if (!shore.IsCreated || shore.Length != n * n)
            {
                if (shore.IsCreated) shore.Dispose();
                shore = new NativeArray<float>(n * n, Allocator.Persistent);
            }
            shore.CopyFrom(heights);
            shoreOrigin = origin; shoreSize = size; shoreN = n;
            if (shoreTex == null || shoreTex.width != n)
            {
                if (shoreTex != null) Destroy(shoreTex);
                shoreTex = new Texture2D(n, n, TextureFormat.RFloat, false, true)
                {
                    name = "OceanShore", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                };
            }
            shoreTex.SetPixelData(heights, 0);
            shoreTex.Apply(false);
        }

        public void ClearShore() { shoreN = 0; }

        public RegionFieldParams Params => new RegionFieldParams
        {
            home = new float2(home.x, home.y),
            calmRadius = calmRadius,
            wildRadius = wildRadius,
            nearScale = nearScale,
            farScale = farScale,
            shoreFalloff = shoreFalloff,
            islandCount = islandCount,
            shoreOrigin = shoreOrigin,
            shoreInvSize = shoreSize > 0f ? 1f / shoreSize : 0f,
            shoreN = shoreN,
            shoalDepthFull = shoalDepthFull,
            shoalDepthZero = shoalDepthZero,
            chopFloor = chopFloor,
            breakFraction = breakFraction,
            waveHs = waveHs,
            weatherInvTile = weatherInvTile,
            weatherOffset = weatherOffset,
            weatherN = weatherN,
            patchLo = patchLo,
            patchCoupling = patchCoupling,
        };

        void OnEnable()
        {
            Instance = this;
            islands = new NativeArray<float4>(MaxIslands, Allocator.Persistent);
            shore = new NativeArray<float>(1, Allocator.Persistent);
            shoreN = 0;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (islands.IsCreated) islands.Dispose();
            if (shore.IsCreated) shore.Dispose();
            if (shoreTex != null) Destroy(shoreTex);
        }

        public void SetHome(Vector2 xz)
        {
            home = xz;
            manualBound = true;
        }

        /// Runtime configuration (setup scripts and probes).
        public void SetProfile(float calmR, float wildR, float nearS, float farS, float shoreF)
        {
            calmRadius = calmR; wildRadius = wildR;
            nearScale = nearS; farScale = farS; shoreFalloff = shoreF;
        }

        public void ClearIslands() => islandCount = 0;

        public void AddIsland(Vector2 centre, float radius)
        {
            if (islandCount >= MaxIslands) return;
            islands[islandCount] = new float4(centre.x, centre.y, radius, 0f);
            islandsGpu[islandCount] = new Vector4(centre.x, centre.y, radius, 0f);
            islandCount++;
        }

        public float Evaluate(Vector2 p) =>
            Params.Evaluate(new float2(p.x, p.y), islands, shore, weather);

        /// 0 at/inside stormNear along the storm bearing, 1 beyond stormFar.
        public float StormWeight(Vector2 p)
        {
            float along = Vector2.Dot(p - home, stormBearing.normalized);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(stormNear, stormFar, along));
        }

        void LateUpdate()
        {
            // How tall the open sea currently is, so the depth limit knows what
            // it is capping. Declared by the sea-state asset and checked
            // against the measured Hs by WaveSizeProbe.
            var ocean = OceanRenderer.Instance;
            waveHs = ocean != null && ocean.Settings != null ? ocean.Settings.nominalHs : 0f;

            // Drifting roughness patches, if there is a weather field in the
            // scene. Borrowed, never owned: WeatherField bakes the tile once
            // and this only carries it to the shader and the jobs.
            var wf = WeatherField.Instance;
            if (wf != null && wf.Tile.IsCreated)
            {
                weather = wf.Tile;
                weatherTex = wf.TileTexture;
                weatherN = wf.TileTexels;
                weatherInvTile = wf.InvTileMetres;
                weatherOffset = wf.PatchOffset;
                patchLo = wf.PatchLo;
                patchCoupling = wf.PatchCoupling;
            }
            else
            {
                weatherN = 0; patchLo = 1f; patchCoupling = float3.zero;
            }

            // The world is generated at runtime, so home and islands can only
            // be picked up once they exist. One-shot; probes that set their own
            // geography (SetHome/AddIsland) win and this never fires.
            if (autoBindWorld && !manualBound && !worldBound)
            {
                var voyage = FindFirstObjectByType<Voyage.VoyageManager>();
                var isles = World.Island.All;
                if (voyage != null && voyage.HomePoint != null && isles.Count > 0)
                {
                    home = new Vector2(voyage.HomePoint.position.x, voyage.HomePoint.position.z);
                    islandCount = 0;
                    foreach (var isle in isles)
                    {
                        if (isle == null || islandCount >= MaxIslands) continue;
                        var p = isle.transform.position;
                        islands[islandCount] = new float4(p.x, p.z, isle.MaxRadius, 0f);
                        islandsGpu[islandCount] = new Vector4(p.x, p.z, isle.MaxRadius, 0f);
                        islandCount++;
                    }
                    worldBound = true;
                }
            }
            Publish();
        }

        public void Publish()
        {
            Shader.SetGlobalVector("_Ocean_Region",
                new Vector4(home.x, home.y, calmRadius, wildRadius));
            Shader.SetGlobalVector("_Ocean_RegionScale",
                new Vector4(nearScale, farScale, shoreFalloff, islandCount));
            Shader.SetGlobalVectorArray("_Ocean_Islands", islandsGpu);
            Shader.SetGlobalVector("_Ocean_ShoreRect",
                new Vector4(shoreOrigin.x, shoreOrigin.y, shoreSize > 0f ? 1f / shoreSize : 0f, shoreN));
            Shader.SetGlobalVector("_Ocean_Shoal",
                new Vector4(shoalDepthZero, shoalDepthFull, chopFloor, 0f));
            Shader.SetGlobalVector("_Ocean_DepthLimit",
                new Vector4(breakFraction, waveHs, 0f, 0f));
            Shader.SetGlobalVector("_Ocean_Weather",
                new Vector4(weatherInvTile, weatherOffset.x, weatherOffset.y, weatherN));
            Shader.SetGlobalVector("_Ocean_WeatherPatch",
                new Vector4(patchLo, patchCoupling.x, patchCoupling.y, patchCoupling.z));
            Shader.SetGlobalTexture("_Ocean_WeatherTex",
                weatherTex != null ? (Texture)weatherTex : Texture2D.whiteTexture);
            // ALWAYS bind, even with no shore grid. A fragment shader tolerates
            // an unbound texture it never samples (the _Ocean_ShoreRect.w guard
            // sees to that), but a COMPUTE dispatch does not: Metal validates
            // every declared resource up front and silently skips the whole
            // dispatch, writing nothing. That is what killed DivergenceProbe --
            // its verify kernel returned zeros for a fortnight and the failure
            // read as an ocean parity drift. Costs one global set per frame.
            Shader.SetGlobalTexture("_Ocean_ShoreTex",
                shoreTex != null ? (Texture)shoreTex : Texture2D.blackTexture);
        }

        /// When no RegionField exists (early lab scenes), the shader still
        /// needs sane globals: a neutral envelope of 1 everywhere.
        public static void PublishNeutralIfAbsent()
        {
            if (Instance != null) return;
            Shader.SetGlobalVector("_Ocean_Region", new Vector4(0f, 0f, 0f, 1f));
            Shader.SetGlobalVector("_Ocean_RegionScale", new Vector4(1f, 1f, 1f, 0f));
            Shader.SetGlobalVector("_Ocean_ShoreRect", Vector4.zero);
            // The rest of the contract, for the same compute-dispatch reason:
            // every resource RegionField.hlsl declares must be bound or a
            // kernel including it does nothing at all.
            Shader.SetGlobalVector("_Ocean_Shoal", Vector4.zero);
            Shader.SetGlobalVector("_Ocean_DepthLimit", Vector4.zero);
            Shader.SetGlobalVector("_Ocean_Weather", Vector4.zero);
            Shader.SetGlobalVector("_Ocean_WeatherPatch", new Vector4(1f, 0f, 0f, 0f));
            Shader.SetGlobalTexture("_Ocean_WeatherTex", Texture2D.whiteTexture);
            Shader.SetGlobalVectorArray("_Ocean_Islands", new Vector4[MaxIslands]);
            Shader.SetGlobalTexture("_Ocean_ShoreTex", Texture2D.blackTexture);
        }
    }
}
