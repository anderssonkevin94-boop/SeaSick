using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Ocean;

namespace SeaSick.Terrain
{
    /// Feeds the ocean a world-anchored grid of terrain heights around the
    /// target so waves shoal and die at the new shores (RegionField.SetShore;
    /// the ocean shader and the Burst samplers read the same grid). The grid
    /// is texel-aligned to the world, so re-centring as the ship moves changes
    /// which texels exist, never their values — no pop. Built in a Burst job
    /// over a few frames.
    public class TerrainShoreField : MonoBehaviour
    {
        public TerrainSettings settings;
        public Transform target;
        [Tooltip("Metres covered by the grid (centred on the target).")]
        public float extent = 4096f;
        [Tooltip("Texels per edge. 256 over 4 km = 16 m texels; shoaling bands are wider than that.")]
        public int resolution = 256;
        [Tooltip("Rebuild when the target has moved this far from the grid centre, metres.")]
        public float recentreDistance = 512f;

        [BurstCompile]
        struct ShoreJob : IJobParallelFor
        {
            public int n;
            public float2 origin;
            public float texel;
            public TerrainParams prm;
            [ReadOnly] public NativeArray<float> lut;
            [WriteOnly] public NativeArray<float> heights;

            public void Execute(int index)
            {
                int x = index % n, y = index / n;
                float2 p = origin + (new float2(x, y) + 0.5f) * texel;
                heights[index] = TerrainHeight.Height(p, prm, lut);
            }
        }

        NativeArray<float> heights, lut;
        JobHandle handle;
        bool building;
        float2 builtCentre = new float2(float.NaN, float.NaN);
        float2 pendingOrigin;
        public float2 Origin { get; private set; }
        public int BuildCount { get; private set; }
        public bool Ready => BuildCount > 0;

        void OnEnable()
        {
            if (target == null && Camera.main != null) target = Camera.main.transform;
        }

        void OnDisable()
        {
            if (building) handle.Complete();
            building = false;
            if (heights.IsCreated) heights.Dispose();
            if (lut.IsCreated) lut.Dispose();
            if (RegionField.Instance != null) RegionField.Instance.ClearShore();
        }

        public void MarkDirty() { builtCentre = new float2(float.NaN, float.NaN); }

        void Update()
        {
            if (settings == null || target == null || RegionField.Instance == null) return;

            if (building)
            {
                if (!handle.IsCompleted) return;
                handle.Complete();
                building = false;
                Origin = pendingOrigin;
                RegionField.Instance.SetShore(heights, Origin, extent, resolution);
                BuildCount++;
            }

            float2 c = new float2(target.position.x, target.position.z);
            float texel = extent / resolution;
            // Snap the origin to the texel lattice so the grid is world-anchored.
            float2 origin = math.floor((c - extent * 0.5f) / texel) * texel;
            if (!math.any(math.isnan(builtCentre)) && math.distance(c, builtCentre) < recentreDistance) return;

            if (!heights.IsCreated) heights = new NativeArray<float>(resolution * resolution, Allocator.Persistent);
            if (!lut.IsCreated) lut = TerrainCurveLut.Bake(settings.terraceCurve, Allocator.Persistent);
            handle = new ShoreJob
            {
                n = resolution, origin = origin, texel = texel, prm = TerrainParams.From(settings), lut = lut, heights = heights,
            }.Schedule(heights.Length, 256);
            JobHandle.ScheduleBatchedJobs();
            building = true;
            pendingOrigin = origin;
            builtCentre = c;
        }
    }
}
