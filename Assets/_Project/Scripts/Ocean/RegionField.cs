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
        };

        /// islands: xy = centre, z = radius. shore: terrain heights on the
        /// shore grid (length shoreN*shoreN; ignored when shoreN == 0).
        /// MUST match RegionField.hlsl.
        public float Evaluate(float2 p, NativeArray<float4> islands, NativeArray<float> shore)
        {
            float d = math.distance(p, home);
            float t = math.smoothstep(calmRadius, wildRadius, d);
            float env = math.lerp(nearScale, farScale, t);
            for (int i = 0; i < islandCount; i++)
            {
                float s = math.distance(p, islands[i].xy) - islands[i].z;
                env *= math.smoothstep(0f, shoreFalloff, s);
            }
            float wet = 1f;
            float cap = float.MaxValue;
            if (shoreN > 0)
            {
                float3 swd = ShoreWetDepth(p, shore);
                env *= swd.x;
                wet = swd.y;
                // Depth limit: no wave taller than a fraction of the water
                // under it. This is the shoaling/breaking rule, and it is what
                // makes seabed clipping structurally impossible at ANY wave
                // size rather than something to re-tune every time the sea
                // grows. It also reads as gameplay for free -- the sea visibly
                // lies down as you come in off the deep, and gets up again as
                // the bottom falls away.
                if (waveHs > 0.01f)
                    cap = math.max(0f, breakFraction * swd.z / waveHs);
            }
            // Never dead flat. Sheltered water still has chop; only land is
            // glass. Twin of RegionEnvelope's max() in RegionField.hlsl.
            // The cap is applied last so it beats the chop floor: in half a
            // metre of water there is no chop to have.
            return math.min(math.max(env, chopFloor * wet), cap);
        }

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

        // Pulled from the renderer each frame rather than pushed, so the data
        // flows one way and nothing has to remember to call a setter.
        float waveHs;

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
            Params.Evaluate(new float2(p.x, p.y), islands, shore);

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
            Shader.SetGlobalVectorArray("_Ocean_Islands", new Vector4[MaxIslands]);
            Shader.SetGlobalTexture("_Ocean_ShoreTex", Texture2D.blackTexture);
        }
    }
}
