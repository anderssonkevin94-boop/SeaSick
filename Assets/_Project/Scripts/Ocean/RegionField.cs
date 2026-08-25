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
            if (shoreN > 0)
            {
                float2 sw = ShoreAndWet(p, shore);
                env *= sw.x;
                wet = sw.y;
            }
            // Never dead flat. Sheltered water still has chop; only land is
            // glass. Twin of RegionEnvelope's max() in RegionField.hlsl.
            return math.max(env, chopFloor * wet);
        }

        /// Shoal factor AND "is there water here at all", from one lookup.
        /// The chop floor needs the second: the sea keeps texture in sheltered
        /// water but must still be perfectly gone over land.
        public float2 ShoreAndWet(float2 p, NativeArray<float> shore)
        {
            float2 uv = (p - shoreOrigin) * shoreInvSize;
            if (uv.x < 0f || uv.y < 0f || uv.x > 1f || uv.y > 1f) return new float2(1f, 1f);
            float2 f = uv * shoreN - 0.5f;
            int2 i0 = math.clamp((int2)math.floor(f), 0, shoreN - 1);
            int2 i1 = math.min(i0 + 1, shoreN - 1);
            float2 w = math.saturate(f - i0);
            float a = shore[i0.y * shoreN + i0.x], b = shore[i0.y * shoreN + i1.x];
            float c = shore[i1.y * shoreN + i0.x], e = shore[i1.y * shoreN + i1.x];
            float h = math.lerp(math.lerp(a, b, w.x), math.lerp(c, e, w.x), w.y);
            return new float2(math.smoothstep(shoalDepthZero, shoalDepthFull, -h),
                              math.smoothstep(0f, 0.5f, -h));
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
            if (shoreTex != null) Shader.SetGlobalTexture("_Ocean_ShoreTex", shoreTex);
        }

        /// When no RegionField exists (early lab scenes), the shader still
        /// needs sane globals: a neutral envelope of 1 everywhere.
        public static void PublishNeutralIfAbsent()
        {
            if (Instance != null) return;
            Shader.SetGlobalVector("_Ocean_Region", new Vector4(0f, 0f, 0f, 1f));
            Shader.SetGlobalVector("_Ocean_RegionScale", new Vector4(1f, 1f, 1f, 0f));
            Shader.SetGlobalVector("_Ocean_ShoreRect", Vector4.zero);
        }
    }
}
