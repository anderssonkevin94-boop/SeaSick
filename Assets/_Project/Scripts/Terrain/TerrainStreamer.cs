using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// Keeps a square of chunks loaded around a target transform, building
    /// them on worker threads (Burst HeightJob → MeshJob → Physics.BakeMesh)
    /// and applying finished ones on the main thread. One pooled GameObject
    /// per chunk. LOD is a vertex stride picked from the Chebyshev chunk
    /// distance (1 / 2 / 4); a chunk whose LOD target changes is rebuilt.
    /// Colliders only within colliderRadius. Tuning lives on TerrainSettings.
    public class TerrainStreamer : MonoBehaviour
    {
        public TerrainSettings settings;
        public Material material;
        [Tooltip("Chunks stream around this transform (the ship). Defaults to the main camera.")]
        public Transform target;
        [Tooltip("Chunks are unloaded only beyond viewRadius + this, so crossing a border doesn't churn.")]
        public int unloadHysteresis = 1;

        class Chunk
        {
            public int2 coord;
            public int lod;            // lodStep currently on the mesh (0 = nothing yet)
            public int targetLod;
            public bool building;
            public bool wantCollider;
            public bool baked;         // collider data baked for the current mesh
            public GameObject go;
            public MeshFilter filter;
            public MeshRenderer renderer;
            public MeshCollider collider;
            public Mesh mesh;
        }

        struct InFlight
        {
            public Chunk chunk;
            public TerrainChunkMesher.ChunkDesc desc;
            public JobHandle handle;
            public NativeArray<float> heights;
            public NativeArray<float> rock;
            public NativeArray<float> yRange;
            public Mesh.MeshDataArray mda;
        }

        struct Baking
        {
            public Chunk chunk;
            public JobHandle handle;
        }

        [Unity.Burst.BurstCompile]
        struct BakeJob : IJob
        {
            public int meshId;
            public void Execute() => Physics.BakeMesh(meshId, false);
        }

        readonly Dictionary<int2, Chunk> loaded = new Dictionary<int2, Chunk>();
        readonly Stack<Chunk> pool = new Stack<Chunk>();
        readonly List<int2> queue = new List<int2>();        // farthest first; pop from the end
        const int MaxAppliesPerFrame = 2;
        readonly List<InFlight> inFlight = new List<InFlight>();
        readonly List<Baking> baking = new List<Baking>();
        int2 lastCentre = new int2(int.MinValue, int.MinValue);
        static int2 sortCentre;
        static readonly System.Comparison<int2> FarthestFirst =
            (a, b) => math.lengthsq(b - sortCentre).CompareTo(math.lengthsq(a - sortCentre));
        TerrainParams prm;
        TerrainChunkMesher.ColourParams colours;
        NativeArray<float> lut;
        bool paramsDirty = true;

        public int LoadedCount => loaded.Count;
        public int PendingCount => queue.Count + inFlight.Count + baking.Count + releaseQueue.Count;
        public int2 CentreChunk => lastCentre;
        /// Main-thread milliseconds the streamer spent in the last Update.
        public float LastMainThreadMs { get; private set; }
        /// Worst per-phase main-thread ms since ResetStats (profiling aid for probes).
        public float WorstReplanMs, WorstApplyMs, WorstBakeMs, WorstScheduleMs, WorstSetColliderMs;
        public float WorstReleaseMs, WorstLoopMs, WorstSortMs; public int WorstReplanGCs, WorstReplanDropped, WorstReplanQueued;
        public void ResetStats() { WorstReplanMs = WorstApplyMs = WorstBakeMs = WorstScheduleMs = WorstSetColliderMs = WorstReleaseMs = WorstLoopMs = WorstSortMs = 0f; WorstReplanGCs = 0; }
        static readonly System.Diagnostics.Stopwatch phase = new System.Diagnostics.Stopwatch();
        static float Lap() { float ms = (float)phase.Elapsed.TotalMilliseconds; phase.Restart(); return ms; }
        public int TotalBuilt { get; private set; }
        // Reused rather than `Stopwatch.StartNew()`'d fresh every Update/
        // Replan call -- same reasoning as `phase` above, just for the two
        // callers that used to allocate their own.
        readonly System.Diagnostics.Stopwatch updateWatch = new System.Diagnostics.Stopwatch();
        readonly System.Diagnostics.Stopwatch replanWatch = new System.Diagnostics.Stopwatch();

        public bool IsLoaded(int2 c) => loaded.TryGetValue(c, out var ch) && ch.lod != 0 && !ch.building;
        public Mesh MeshAt(int2 c) => IsLoaded(c) ? loaded[c].mesh : null;
        public int LodAt(int2 c) => loaded.TryGetValue(c, out var ch) ? ch.lod : 0;
        public bool HasCollider(int2 c) => loaded.TryGetValue(c, out var ch) && ch.collider.enabled && ch.collider.sharedMesh != null;
        public IEnumerable<int2> LoadedCoords => loaded.Keys;

        public int ViewRadius => settings != null ? settings.viewRadius : 0;
        public int ColliderRadius => settings != null ? settings.colliderRadius : 0;

        /// Call after editing settings at runtime.
        public void MarkDirty() { paramsDirty = true; lastCentre = new int2(int.MinValue, int.MinValue); }

        public int2 ChunkCoordOf(Vector3 worldPos) =>
            (int2)math.floor(new float2(worldPos.x, worldPos.z) / settings.chunkSize);

        public int LodFor(int2 coord, int2 centre)
        {
            int2 d = math.abs(coord - centre);
            int cheb = math.max(d.x, d.y);
            return cheb <= settings.lod0Radius ? 1 : cheb <= settings.lod1Radius ? 2 : 4;
        }

        void OnEnable()
        {
            if (target == null && Camera.main != null) target = Camera.main.transform;
        }

        void OnDisable()
        {
            foreach (var f in inFlight) { f.handle.Complete(); f.heights.Dispose(); f.rock.Dispose(); f.yRange.Dispose(); f.mda.Dispose(); }
            inFlight.Clear();
            foreach (var b in baking) b.handle.Complete();
            baking.Clear();
            foreach (var ch in loaded.Values) Release(ch);
            loaded.Clear();
            releaseQueue.Clear();
            queue.Clear();
            if (lut.IsCreated) lut.Dispose();
            paramsDirty = true;
        }

        void RefreshParams()
        {
            prm = TerrainParams.From(settings);
            colours = TerrainChunkMesher.ColourParams.From(settings);
            if (lut.IsCreated) lut.Dispose();
            lut = TerrainCurveLut.Bake(settings.profileCurve, Allocator.Persistent);
            paramsDirty = false;
            // Dry-run the whole create → drop → release → pool cycle once. The
            // first real release costs ~10 ms of one-off lazy initialisation
            // (measured by CrossingProbe: 9.4 ms on the first, 0.0 on the next
            // 36), so pay it here on the load frame instead of mid-sail.
            var warm = new int2(int.MaxValue, int.MaxValue);
            var ch = pool.Count > 0 ? pool.Pop() : Create();
            loaded[warm] = ch;
            foreach (var kv in loaded) { }
            drop.Add(warm); releaseQueue.Add(loaded[warm]); loaded.Remove(warm); drop.Clear();
            ReleaseSome();
            queue.Add(warm); queue.Sort(FarthestFirst); queue.Clear();
        }

        void Update()
        {
            if (settings == null || target == null) return;
            updateWatch.Restart();

            if (paramsDirty)
            {
                if (inFlight.Count > 0 || baking.Count > 0) { CompleteAll(); }
                RefreshParams();
                foreach (var ch in loaded.Values) Release(ch);
                loaded.Clear();
                queue.Clear();
            }

            int2 centre = ChunkCoordOf(target.position);
            phase.Restart();
            if (!centre.Equals(lastCentre))
            {
                lastCentre = centre;
                int gc0 = System.GC.CollectionCount(0);
                Replan(centre);
                float ms = Lap();
                if (ms > WorstReplanMs)
                {
                    WorstReplanMs = ms;
                    WorstReplanGCs = System.GC.CollectionCount(0) - gc0;
                    WorstReleaseMs = lastReleaseMs; WorstLoopMs = lastLoopMs; WorstSortMs = lastSortMs; WorstScanMs = lastScanMs;
                    WorstReplanDropped = lastDropped; WorstReplanQueued = queue.Count;
                }
            }

            ReleaseSome();
            PollFinished();
            ScheduleMore();
            WorstScheduleMs = math.max(WorstScheduleMs, Lap());
            JobHandle.ScheduleBatchedJobs();

            LastMainThreadMs = (float)updateWatch.Elapsed.TotalMilliseconds;
        }

        float lastReleaseMs, lastLoopMs, lastSortMs, lastScanMs; int lastDropped;
        public float WorstScanMs;
        readonly List<int2> drop = new List<int2>();
        readonly List<Chunk> releaseQueue = new List<Chunk>();
        const int MaxReleasesPerFrame = 4;

        void ReleaseSome()
        {
            int n = math.min(MaxReleasesPerFrame, releaseQueue.Count);
            for (int i = 0; i < n; i++) Release(releaseQueue[releaseQueue.Count - 1 - i]);
            releaseQueue.RemoveRange(releaseQueue.Count - n, n);
        }

        void Replan(int2 centre)
        {
            replanWatch.Restart();
            int dropR = settings.viewRadius + unloadHysteresis;
            drop.Clear();
            foreach (var kv in loaded)
            {
                int2 d = math.abs(kv.Key - centre);
                if (math.max(d.x, d.y) > dropR && !kv.Value.building) drop.Add(kv.Key);
            }
            lastScanMs = (float)replanWatch.Elapsed.TotalMilliseconds; replanWatch.Restart();
            // Releases are amortised over the next frames (see ReleaseSome).
            foreach (var c in drop) { releaseQueue.Add(loaded[c]); loaded.Remove(c); }
            lastDropped = drop.Count;
            lastReleaseMs = (float)replanWatch.Elapsed.TotalMilliseconds; replanWatch.Restart();

            // Existing chunks: update LOD target and collider ring. Missing ones
            // are only queued — their objects are created when scheduled, so a
            // fresh view never allocates hundreds of objects in one frame.
            queue.Clear();
            int r = settings.viewRadius;
            for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    int2 c = centre + new int2(x, z);
                    int2 d = math.abs(c - centre);
                    bool wantColl = math.max(d.x, d.y) <= settings.colliderRadius;
                    int lod = LodFor(c, centre);
                    if (loaded.TryGetValue(c, out var ch))
                    {
                        ch.targetLod = lod;
                        ch.wantCollider = wantColl;
                        if (!wantColl) SetCollider(ch, false);
                        else if (ch.lod == ch.targetLod && !ch.building) EnableColliderAsync(ch);
                        if (ch.lod != lod && !ch.building) queue.Add(c);
                    }
                    else queue.Add(c);
                }
            lastLoopMs = (float)replanWatch.Elapsed.TotalMilliseconds; replanWatch.Restart();
            // Chunks already loaded keep rendering at their old LOD until the rebuild lands.
            sortCentre = centre;
            queue.Sort(FarthestFirst);
            lastSortMs = (float)replanWatch.Elapsed.TotalMilliseconds;
        }

        void ScheduleMore()
        {
            while (inFlight.Count < settings.jobsInFlight && queue.Count > 0)
            {
                int2 c = queue[queue.Count - 1];
                queue.RemoveAt(queue.Count - 1);
                if (!loaded.TryGetValue(c, out var ch))
                {
                    ch = pool.Count > 0 ? pool.Pop() : Create();
                    ch.coord = c; ch.lod = 0; ch.building = false;
                    ch.go.transform.position = new Vector3(c.x * settings.chunkSize, 0f, c.y * settings.chunkSize);
                    int2 d = math.abs(c - lastCentre);
                    ch.wantCollider = math.max(d.x, d.y) <= settings.colliderRadius;
                    ch.targetLod = LodFor(c, lastCentre);
                    loaded[c] = ch;
                }
                if (ch.building || ch.lod == ch.targetLod) continue;
                var desc = new TerrainChunkMesher.ChunkDesc
                {
                    coord = ch.coord, size = settings.chunkSize, resolution = settings.chunkResolution, lodStep = ch.targetLod,
                };
                int bn = TerrainChunkMesher.VertsPerEdge(desc) + 2;
                var f = new InFlight
                {
                    chunk = ch, desc = desc,
                    heights = new NativeArray<float>(bn * bn, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                    rock = new NativeArray<float>(bn * bn, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                    yRange = new NativeArray<float>(2, Allocator.Persistent),
                    mda = Mesh.AllocateWritableMeshData(1),
                };
                TerrainChunkMesher.Prepare(f.mda[0], desc);
                f.handle = TerrainChunkMesher.Schedule(desc, prm, lut, colours, settings.skirtDepth, f.heights, f.rock, f.mda[0], f.yRange);
                ch.building = true;
                inFlight.Add(f);
            }
        }

        void PollFinished()
        {
            int applies = 0;
            for (int i = inFlight.Count - 1; i >= 0 && applies < MaxAppliesPerFrame; i--)
            {
                var f = inFlight[i];
                if (!f.handle.IsCompleted) continue;
                applies++;
                f.handle.Complete();
                Lap();
                TerrainChunkMesher.Apply(f.mda, f.chunk.mesh, f.desc, f.yRange);
                WorstApplyMs = math.max(WorstApplyMs, Lap());
                f.heights.Dispose(); f.rock.Dispose(); f.yRange.Dispose();
                f.chunk.lod = f.desc.lodStep;
                f.chunk.building = false;
                f.chunk.renderer.enabled = true;
                TotalBuilt++;
                inFlight.RemoveAt(i);

                // The collider is only valid once the bake for this exact mesh lands.
                f.chunk.collider.enabled = false;
                f.chunk.collider.sharedMesh = null;
                f.chunk.baked = false;
                if (f.chunk.wantCollider) EnableColliderAsync(f.chunk);
                else if (f.chunk.lod != f.chunk.targetLod) queue.Add(f.chunk.coord);
            }
            for (int i = baking.Count - 1; i >= 0; i--)
            {
                var b = baking[i];
                if (!b.handle.IsCompleted) continue;
                Lap();
                b.handle.Complete();
                WorstBakeMs = math.max(WorstBakeMs, Lap());
                b.chunk.building = false;
                b.chunk.baked = true;
                if (b.chunk.wantCollider) SetCollider(b.chunk, true);
                WorstSetColliderMs = math.max(WorstSetColliderMs, Lap());
                if (b.chunk.lod != b.chunk.targetLod) queue.Add(b.chunk.coord);
                baking.RemoveAt(i);
            }
        }

        /// Bakes on a worker thread before the collider ever sees the mesh, so
        /// PhysX never cooks on the main thread (a 65² chunk costs ~20 ms there).
        void EnableColliderAsync(Chunk ch)
        {
            if (ch.baked) { SetCollider(ch, true); return; }
            if (ch.building) return;
            ch.building = true; // hold it until the bake lands
            baking.Add(new Baking { chunk = ch, handle = new BakeJob { meshId = ch.mesh.GetInstanceID() }.Schedule() });
        }

        void CompleteAll()
        {
            foreach (var f in inFlight) { f.handle.Complete(); f.heights.Dispose(); f.rock.Dispose(); f.yRange.Dispose(); f.mda.Dispose(); f.chunk.building = false; }
            inFlight.Clear();
            foreach (var b in baking) { b.handle.Complete(); b.chunk.building = false; }
            baking.Clear();
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
            ch.renderer.enabled = false;
            return ch;
        }

        static void SetCollider(Chunk ch, bool on)
        {
            if (on)
            {
                if (ch.collider.sharedMesh != ch.mesh) ch.collider.sharedMesh = ch.mesh; // uses the baked data
                ch.collider.enabled = true;
            }
            else if (ch.collider.enabled || ch.collider.sharedMesh != null)
            {
                ch.collider.enabled = false;
                ch.collider.sharedMesh = null;
            }
        }

        void Release(Chunk ch)
        {
            ch.renderer.enabled = false;
            SetCollider(ch, false);
            ch.lod = 0;
            ch.baked = false;
            pool.Push(ch);
        }
    }
}
