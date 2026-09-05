using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeaSick.Terrain
{
    /// The land you can see but cannot reach yet: a single coarse mesh of the
    /// height field from where the streamed chunks stop out to the horizon,
    /// drawn as haze rather than as ground.
    ///
    /// **Why it is a separate mesh and not a bigger view radius.** Chunks are
    /// 128 m and `viewRadius` is 8, so real terrain exists to 1024 m. Reaching
    /// 8 km that way is 15,876 chunks against the present 289 — colliders,
    /// props, LOD churn and meshing jobs for scenery no one will ever stand
    /// on. This carries none of that: no colliders, no props, no LOD, one mesh,
    /// one job, rebuilt only when the camera has moved.
    ///
    /// **The radii are not free choices.** The ocean clipmap is 128 cells
    /// across over 8 doubling rings from a 0.5 m inner cell, so the sea's own
    /// mesh ends at 128 x 64 = 8192 m. Land drawn beyond that would hang over
    /// open skybox with no water under it, so `outerRadius` matches the sea
    /// and the camera's far plane is set just past both.
    ///
    /// **The grid is polar and geometric on purpose.** Rings step by a
    /// constant ratio and segments are constant, so a cell is roughly square
    /// at every distance: 36 m at the inner edge, 260 m at the horizon. A
    /// cartesian grid fine enough for the near edge would be 50,000 times the
    /// vertices at the far one, all of them spent on a silhouette.
    public class HorizonField : MonoBehaviour
    {
        [SerializeField] TerrainSettings settings;
        [Tooltip("Whose position the field centres on. Defaults to the main camera.")]
        [SerializeField] Transform target;
        [SerializeField] Material material;

        [Header("Extent")]
        [Tooltip("Where the field starts, metres. Must be past the streamed chunks (viewRadius x chunkSize) or it fights real terrain for the same pixels.")]
        [SerializeField] float innerRadius = 1100f;
        [Tooltip("Where it ends. Matched to the ocean clipmap's own reach — land drawn past the water has nothing under it.")]
        [SerializeField] float outerRadius = 8000f;
        [SerializeField] int rings = 64;
        [SerializeField] int segments = 192;

        [Header("Seam")]
        [Tooltip("Metres the field is pushed DOWN at its inner edge, easing to zero by `sinkFadeEnd`. The coarse grid samples a 300 m island at 36 m and will read high in places; sinking the overlap band means real terrain always wins the depth test instead of a crude ridge poking through a real one.")]
        [SerializeField] float innerSink = 30f;
        [SerializeField] float sinkFadeEnd = 2400f;
        [Tooltip("How far below sea level the water parts of the field are put, so the ocean covers them and never z-fights.")]
        [SerializeField] float seaSink = 6f;

        [Header("Rebuild")]
        [Tooltip("Metres the target must move before the field is re-centred and re-sampled.")]
        [SerializeField] float rebuildStep = 128f;

        Mesh mesh;
        MeshRenderer mr;
        NativeArray<float2> plan;        // local XZ, fixed for the life of the mesh
        NativeArray<float3> verts;
        NativeArray<float> lut;
        NativeArray<float> tint;
        TerrainParams prm;
        JobHandle handle;
        bool building, hasBuilt;
        Vector3 builtAt;
        Vector3[] managedVerts;
        Color[] managedColours;

        void OnEnable()
        {
            if (target == null && Camera.main != null) target = Camera.main.transform;
            if (settings == null)
            {
                Debug.LogError("HorizonField: no TerrainSettings");
                enabled = false;
                return;
            }
            Build();
        }

        void OnDisable()
        {
            handle.Complete();
            building = false;
            if (plan.IsCreated) plan.Dispose();
            if (verts.IsCreated) verts.Dispose();
            if (tint.IsCreated) tint.Dispose();
            if (lut.IsCreated) lut.Dispose();
            if (mesh != null) { Destroy(mesh); mesh = null; }
            hasBuilt = false;
        }

        void Build()
        {
            rings = Mathf.Max(4, rings);
            segments = Mathf.Max(8, segments);
            int n = rings * segments;

            prm = TerrainParams.From(settings);
            if (lut.IsCreated) lut.Dispose();
            lut = TerrainCurveLut.Bake(settings.profileCurve, Allocator.Persistent);

            if (plan.IsCreated) plan.Dispose();
            if (verts.IsCreated) verts.Dispose();
            if (tint.IsCreated) tint.Dispose();
            plan = new NativeArray<float2>(n, Allocator.Persistent);
            verts = new NativeArray<float3>(n, Allocator.Persistent);
            tint = new NativeArray<float>(n, Allocator.Persistent);

            float ratio = Mathf.Pow(outerRadius / innerRadius, 1f / (rings - 1));
            for (int i = 0; i < rings; i++)
            {
                float r = innerRadius * Mathf.Pow(ratio, i);
                for (int j = 0; j < segments; j++)
                {
                    float a = 2f * Mathf.PI * j / segments;
                    plan[i * segments + j] = new float2(r * Mathf.Cos(a), r * Mathf.Sin(a));
                }
            }

            var tris = new int[(rings - 1) * segments * 6];
            int t = 0;
            for (int i = 0; i < rings - 1; i++)
            {
                for (int j = 0; j < segments; j++)
                {
                    int j2 = (j + 1) % segments;
                    int a = i * segments + j, b = i * segments + j2;
                    int c = (i + 1) * segments + j, d = (i + 1) * segments + j2;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            }

            managedVerts = new Vector3[n];
            managedColours = new Color[n];

            mesh = new Mesh { name = "HorizonField", indexFormat = IndexFormat.UInt32 };
            for (int i = 0; i < n; i++) managedVerts[i] = new Vector3(plan[i].x, 0f, plan[i].y);
            mesh.SetVertices(managedVerts);
            mesh.SetTriangles(tris, 0);
            // Never cull it: the bounds of a ring centred on the camera are
            // meaningless to the culler and a wrong guess pops the horizon out.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (outerRadius * 4f));

            var mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            mr = GetComponent<MeshRenderer>();
            if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
            if (material != null) mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // The mesh is created flat and only becomes terrain when the first
            // job lands. Showing it in between draws a disc at sea level
            // across the whole view — which is exactly what the first probe
            // run photographed and what made a correct field look broken.
            hasBuilt = false;
            mr.enabled = false;
            Schedule(target != null ? target.position : Vector3.zero);
        }

        void LateUpdate()
        {
            if (settings == null) return;
            if (target == null && Camera.main != null) target = Camera.main.transform;
            if (target == null) return;

            // The haze colour is whatever the sky is doing, so dusk, night and
            // storm all come through without a second set of authored colours.
            Shader.SetGlobalColor("_HorizonHaze", RenderSettings.fogColor);

            if (building && handle.IsCompleted)
            {
                handle.Complete();
                building = false;
                Apply();
            }

            if (building) return;
            Vector3 p = target.position;
            if (hasBuilt && new Vector2(p.x - builtAt.x, p.z - builtAt.z).sqrMagnitude
                < rebuildStep * rebuildStep) return;
            Schedule(p);
        }

        void Schedule(Vector3 centre)
        {
            builtAt = new Vector3(centre.x, 0f, centre.z);
            var job = new SampleJob
            {
                plan = plan, verts = verts, tint = tint,
                centre = new float2(builtAt.x, builtAt.z),
                prm = prm, lut = lut,
                seaLevel = settings.seaLevel, seaSink = seaSink,
                innerRadius = innerRadius, innerSink = innerSink, sinkFadeEnd = sinkFadeEnd,
                reliefHeight = Mathf.Max(1f, settings.reliefHeight),
            };
            handle = job.Schedule(plan.Length, 128);
            building = true;
        }

        void Apply()
        {
            for (int i = 0; i < verts.Length; i++)
            {
                managedVerts[i] = verts[i];
                float h = tint[i];
                managedColours[i] = new Color(h, 0f, 0f, 1f);
            }
            mesh.SetVertices(managedVerts);
            mesh.SetColors(managedColours);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (outerRadius * 4f));
            transform.position = builtAt;
            hasBuilt = true;
            if (mr != null) mr.enabled = true;
        }

        /// What the field actually contains, in metres. A picture cannot tell
        /// a tall hill from a broken vertex; this can.
        /// True once the field holds sampled heights rather than the flat disc
        /// it is created as.
        public bool Ready => hasBuilt;

        /// What the field centres on. It is an ANNULUS with a hole in the
        /// middle, so this has to be the camera the picture is taken from —
        /// centre it on one camera and photograph it from another standing
        /// 1.7 km away, and that camera is inside the ring looking at the far
        /// wall of it. (Which is exactly what three rounds of "why is there a
        /// grey mountain" turned out to be.)
        public Transform Target
        {
            get => target;
            set { target = value; hasBuilt = false; if (isActiveAndEnabled) Rebuild(); }
        }

        /// Force a re-centre and re-sample now.
        public void Rebuild()
        {
            if (!plan.IsCreated || target == null) return;
            handle.Complete();
            building = false;
            if (mr != null) mr.enabled = false;
            hasBuilt = false;
            Schedule(target.position);
        }

        public string Describe()
        {
            if (!hasBuilt || !verts.IsCreated) return "HorizonField: not built";
            float lo = float.MaxValue, hi = float.MinValue;
            int bad = 0;
            float rMin = float.MaxValue, rMax = 0f;
            for (int i = 0; i < verts.Length; i++)
            {
                float y = verts[i].y;
                if (!float.IsFinite(y) || Mathf.Abs(y) > 1e5f) { bad++; continue; }
                if (y < lo) lo = y;
                if (y > hi) hi = y;
                float r = math.length(new float2(verts[i].x, verts[i].z));
                if (r < rMin) rMin = r;
                if (r > rMax) rMax = r;
            }
            return $"HorizonField: {verts.Length} verts, radius {rMin:F0}-{rMax:F0} m, "
                   + $"y {lo:F1} to {hi:F1} m, {bad} non-finite, "
                   + $"centre ({builtAt.x:F0},{builtAt.z:F0}), "
                   + $"transform ({transform.position.x:F0},{transform.position.y:F0},{transform.position.z:F0}), "
                   + $"inner {innerRadius:F0} outer {outerRadius:F0}";
        }

        [BurstCompile]
        struct SampleJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> plan;
            [WriteOnly] public NativeArray<float3> verts;
            [WriteOnly] public NativeArray<float> tint;
            public float2 centre;
            public TerrainParams prm;
            [ReadOnly] public NativeArray<float> lut;
            public float seaLevel, seaSink, innerRadius, innerSink, sinkFadeEnd, reliefHeight;

            public void Execute(int i)
            {
                float2 local = plan[i];
                float h = TerrainHeight.Height(local + centre, prm, lut);

                // Water goes under the sea so the ocean covers it outright —
                // cheaper and more robust than trying to cut the mesh, and it
                // cannot z-fight.
                float y = h <= seaLevel ? seaLevel - seaSink : h;

                // Ease the whole field down near the seam. The coarse grid
                // reads a small island high, and a crude ridge punching
                // through a real one is the one artefact that would give this
                // away as geometry rather than distance.
                float r = math.length(local);
                float sink = innerSink * (1f - math.saturate(
                    (r - innerRadius) / math.max(1f, sinkFadeEnd - innerRadius)));
                y -= sink;

                verts[i] = new float3(local.x, y, local.y);
                tint[i] = math.saturate(h / reliefHeight);
            }
        }
    }
}
