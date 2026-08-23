using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// Builds a small grid of chunks around a world position, on the main
    /// thread, as child meshes. Step-3 lab tool: look at one island in 3D and
    /// eyeball seams before the streamer exists. One child per chunk is the
    /// same granularity the streamer will use.
    [ExecuteAlways]
    public class TerrainChunkPreview : MonoBehaviour
    {
        public TerrainSettings settings;
        public Material material;
        [Tooltip("World XZ the preview is centred on.")]
        public Vector2 centre = new Vector2(-1600f, 600f);
        [Range(1, 9), Tooltip("Chunks per side around the centre.")]
        public int chunksPerSide = 7;
        [Range(1, 8)] public int lodStep = 1;
        public bool colliders = true;

        public int VertexCount { get; private set; }

        public void Rebuild()
        {
            Clear();
            if (settings == null) return;
            var prm = TerrainParams.From(settings);
            var lut = TerrainCurveLut.Bake(settings.terraceCurve, Allocator.Temp);
            int2 c0 = (int2)math.floor(new float2(centre.x, centre.y) / settings.chunkSize);
            int half = chunksPerSide / 2;
            VertexCount = 0;
            for (int z = -half; z <= half; z++)
                for (int x = -half; x <= half; x++)
                {
                    var d = new TerrainChunkMesher.ChunkDesc
                    {
                        coord = c0 + new int2(x, z), size = settings.chunkSize,
                        resolution = settings.chunkResolution, lodStep = lodStep,
                    };
                    var h = TerrainChunkMesher.SampleHeights(d, prm, lut, Allocator.Temp);
                    var mesh = new Mesh { name = "Chunk " + d.coord.x + "," + d.coord.y };
                    TerrainChunkMesher.Build(mesh, d, h, prm, settings);
                    h.Dispose();
                    VertexCount += mesh.vertexCount;

                    var go = new GameObject(mesh.name) { hideFlags = HideFlags.DontSave };
                    go.transform.SetParent(transform, false);
                    float2 o = TerrainChunkMesher.ChunkOrigin(d);
                    go.transform.position = new Vector3(o.x, 0f, o.y);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = material;
                    if (colliders) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                }
            lut.Dispose();
        }

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var ch = transform.GetChild(i).gameObject;
                var mf = ch.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) DestroyImmediate(mf.sharedMesh);
                DestroyImmediate(ch);
            }
        }

        void OnDisable() { Clear(); }
    }
}
