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

        public static RegionFieldParams Neutral => new RegionFieldParams
        {
            home = float2.zero,
            calmRadius = 0f,
            wildRadius = 1f,
            nearScale = 1f,
            farScale = 1f,
            shoreFalloff = 1f,
            islandCount = 0,
        };

        /// islands: xy = centre, z = radius. MUST match RegionField.hlsl.
        public float Evaluate(float2 p, NativeArray<float4> islands)
        {
            float d = math.distance(p, home);
            float t = math.smoothstep(calmRadius, wildRadius, d);
            float env = math.lerp(nearScale, farScale, t);
            for (int i = 0; i < islandCount; i++)
            {
                float shore = math.distance(p, islands[i].xy) - islands[i].z;
                env *= math.smoothstep(0f, shoreFalloff, shore);
            }
            return env;
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

        Vector2 home;
        readonly Vector4[] islandsGpu = new Vector4[MaxIslands];
        NativeArray<float4> islands;
        int islandCount;

        public NativeArray<float4> Islands => islands;

        public RegionFieldParams Params => new RegionFieldParams
        {
            home = new float2(home.x, home.y),
            calmRadius = calmRadius,
            wildRadius = wildRadius,
            nearScale = nearScale,
            farScale = farScale,
            shoreFalloff = shoreFalloff,
            islandCount = islandCount,
        };

        void OnEnable()
        {
            Instance = this;
            islands = new NativeArray<float4>(MaxIslands, Allocator.Persistent);
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (islands.IsCreated) islands.Dispose();
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
            Params.Evaluate(new float2(p.x, p.y), islands);

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
        }

        /// When no RegionField exists (early lab scenes), the shader still
        /// needs sane globals: a neutral envelope of 1 everywhere.
        public static void PublishNeutralIfAbsent()
        {
            if (Instance != null) return;
            Shader.SetGlobalVector("_Ocean_Region", new Vector4(0f, 0f, 0f, 1f));
            Shader.SetGlobalVector("_Ocean_RegionScale", new Vector4(1f, 1f, 1f, 0f));
        }
    }
}
