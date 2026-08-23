using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// Keeps a square of chunks loaded around a target transform. One pooled
    /// GameObject per chunk (MeshFilter/Renderer/Collider); chunks outside
    /// viewRadius (+ hysteresis) are returned to the pool, missing chunks are
    /// built nearest-first under a per-frame budget. Colliders only exist
    /// within colliderRadius chunks of the target. All chunks are LOD 0 until
    /// step 5 adds LOD with seam stitching.
    public class TerrainStreamer : MonoBehaviour
    {
        public TerrainSettings settings;
        public Material material;
        [Tooltip("Chunks stream around this transform (the ship). Defaults to the main camera.")]
        public Transform target;
        [Tooltip("Chunks loaded in every direction from the target's chunk.")]
        public int viewRadius = 6;
        [Tooltip("Chunks are unloaded only beyond viewRadius + this, so crossing a border doesn't churn.")]
        public int unloadHysteresis = 1;
        [Tooltip("Mesh colliders only within this many chunks of the target.")]
        public int colliderRadius = 1;
        [Tooltip("Maximum chunks built per frame (main thread, until step 5).")]
        public int buildsPerFrame = 2;

        class Chunk
        {
            public int2 coord;
            public GameObject go;
            public MeshFilter filter;
            public MeshRenderer renderer;
            public MeshCollider collider;
            public Mesh mesh;
        }

        readonly Dictionary<int2, Chunk> loaded = new Dictionary<int2, Chunk>();
        readonly Stack<Chunk> pool = new Stack<Chunk>();
        readonly List<int2> wanted = new List<int2>();
        int2 lastCentre = new int2(int.MinValue, int.MinValue);
        TerrainParams prm;
        NativeArray<float> lut;
        bool paramsDirty = true;

        public int LoadedCount => loaded.Count;
        public int PendingCount => wanted.Count;
        public int2 CentreChunk => lastCentre;
        /// Main-thread milliseconds spent building chunks in the last frame (for the step-5 baseline).
        public float LastBuildMs { get; private set; }
        public int TotalBuilt { get; private set; }

        public bool IsLoaded(int2 c) => loaded.ContainsKey(c);
        public Mesh MeshAt(int2 c) => loaded.TryGetValue(c, out var ch) ? ch.mesh : null;
        public bool HasCollider(int2 c) => loaded.TryGetValue(c, out var ch) && ch.collider.enabled;
        public IEnumerable<int2> LoadedCoords => loaded.Keys;

        /// Call after editing settings at runtime.
        public void MarkDirty() { paramsDirty = true; lastCentre = new int2(int.MinValue, int.MinValue); }

        void OnEnable()
        {
            if (target == null && Camera.main != null) target = Camera.main.transform;
        }

        void OnDisable()
        {
            foreach (var ch in loaded.Values) Release(ch);
            loaded.Clear();
            wanted.Clear();
            if (lut.IsCreated) lut.Dispose();
            paramsDirty = true;
        }

        void RefreshParams()
        {
            prm = TerrainParams.From(settings);
            if (lut.IsCreated) lut.Dispose();
            lut = TerrainCurveLut.Bake(settings.terraceCurve, Allocator.Persistent);
            paramsDirty = false;
        }

        public int2 ChunkCoordOf(Vector3 worldPos) =>
            (int2)math.floor(new float2(worldPos.x, worldPos.z) / settings.chunkSize);

        void Update()
        {
            if (settings == null || target == null) return;
            if (paramsDirty)
            {
                RefreshParams();
                foreach (var ch in loaded.Values) Release(ch);
                loaded.Clear();
            }

            int2 centre = ChunkCoordOf(target.position);
            if (!centre.Equals(lastCentre))
            {
                lastCentre = centre;
                Replan(centre);
            }

            LastBuildMs = 0f;
            int budget = buildsPerFrame;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (budget-- > 0 && wanted.Count > 0)
            {
                int2 c = wanted[wanted.Count - 1];
                wanted.RemoveAt(wanted.Count - 1);
                if (loaded.ContainsKey(c)) continue;
                Build(c, centre);
            }
            LastBuildMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        void Replan(int2 centre)
        {
            // Unload beyond the hysteresis band.
            int dropR = viewRadius + unloadHysteresis;
            var drop = new List<int2>();
            foreach (var kv in loaded)
            {
                int2 d = math.abs(kv.Key - centre);
                if (math.max(d.x, d.y) > dropR) drop.Add(kv.Key);
            }
            foreach (var c in drop) { Release(loaded[c]); loaded.Remove(c); }

            // Collider ring follows the target.
            foreach (var kv in loaded)
            {
                int2 d = math.abs(kv.Key - centre);
                SetCollider(kv.Value, math.max(d.x, d.y) <= colliderRadius);
            }

            // Queue the missing ones, farthest first so the nearest pops first.
            wanted.Clear();
            for (int z = -viewRadius; z <= viewRadius; z++)
                for (int x = -viewRadius; x <= viewRadius; x++)
                {
                    int2 c = centre + new int2(x, z);
                    if (!loaded.ContainsKey(c)) wanted.Add(c);
                }
            wanted.Sort((a, b) => math.lengthsq(b - centre).CompareTo(math.lengthsq(a - centre)));
        }

        void Build(int2 coord, int2 centre)
        {
            var d = new TerrainChunkMesher.ChunkDesc
            {
                coord = coord, size = settings.chunkSize, resolution = settings.chunkResolution, lodStep = 1,
            };
            Chunk ch = pool.Count > 0 ? pool.Pop() : Create();
            ch.coord = coord;
            var h = TerrainChunkMesher.SampleHeights(d, prm, lut, Allocator.Temp);
            TerrainChunkMesher.Build(ch.mesh, d, h, prm, settings);
            h.Dispose();
            float2 o = TerrainChunkMesher.ChunkOrigin(d);
            ch.go.name = "Chunk " + coord.x + "," + coord.y;
            ch.go.transform.position = new Vector3(o.x, 0f, o.y);
            ch.go.SetActive(true);
            int2 dd = math.abs(coord - centre);
            SetCollider(ch, math.max(dd.x, dd.y) <= colliderRadius);
            loaded[coord] = ch;
            TotalBuilt++;
        }

        Chunk Create()
        {
            var go = new GameObject("Chunk");
            go.transform.SetParent(transform, false);
            var ch = new Chunk
            {
                go = go,
                mesh = new Mesh { name = "ChunkMesh" },
                filter = go.AddComponent<MeshFilter>(),
                renderer = go.AddComponent<MeshRenderer>(),
                collider = go.AddComponent<MeshCollider>(),
            };
            ch.mesh.MarkDynamic();
            ch.filter.sharedMesh = ch.mesh;
            ch.renderer.sharedMaterial = material;
            ch.collider.enabled = false;
            return ch;
        }

        void SetCollider(Chunk ch, bool on)
        {
            if (ch.collider.enabled == on) return;
            ch.collider.enabled = on;
            // Re-assigning forces the collider to rebake the updated mesh.
            ch.collider.sharedMesh = on ? ch.mesh : null;
        }

        void Release(Chunk ch)
        {
            ch.go.SetActive(false);
            ch.collider.enabled = false;
            ch.collider.sharedMesh = null;
            pool.Push(ch);
        }
    }
}
