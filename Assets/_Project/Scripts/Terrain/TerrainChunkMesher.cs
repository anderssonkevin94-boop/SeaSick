using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// Builds the mesh for one chunk of terrain. Vertices live on a GLOBAL
    /// integer lattice (vertex index * spacing) so two chunks that share an
    /// edge compute bit-identical positions; heights are sampled one ring
    /// beyond the chunk so edge normals see the neighbour's slope and match
    /// across the seam. Main-thread for now (step 5 moves this into a job);
    /// the sampling is already job-shaped — flat NativeArrays, no managed
    /// access inside the loops.
    public static class TerrainChunkMesher
    {
        public struct ChunkDesc
        {
            public int2 coord;       // chunk grid coordinate
            public float size;       // metres per chunk edge
            public int resolution;   // vertices per edge (LOD 0)
            public int lodStep;      // 1 = full density, 2 = every other vertex, ...
        }

        /// Heights on the (n+2)² bordered grid for a chunk. Index (i+1, j+1) is
        /// local vertex (i, j); the ring outside is only used for normals.
        public static NativeArray<float> SampleHeights(in ChunkDesc d, in TerrainParams prm, in NativeArray<float> lut, Allocator alloc)
        {
            int n = VertsPerEdge(d);
            int bn = n + 2;
            var h = new NativeArray<float>(bn * bn, alloc, NativeArrayOptions.UninitializedMemory);
            for (int j = 0; j < bn; j++)
                for (int i = 0; i < bn; i++)
                    h[j * bn + i] = TerrainHeight.Height(VertexWorldXZ(d, i - 1, j - 1), prm, lut);
            return h;
        }

        public static int VertsPerEdge(in ChunkDesc d) => (d.resolution - 1) / d.lodStep + 1;
        public static float Spacing(in ChunkDesc d) => d.size / (d.resolution - 1) * d.lodStep;

        /// World XZ of local vertex (i, j) — via the global integer lattice.
        public static float2 VertexWorldXZ(in ChunkDesc d, int i, int j)
        {
            int cells = d.resolution - 1;
            float spacing = d.size / cells;
            long gx = (long)d.coord.x * cells + (long)i * d.lodStep;
            long gz = (long)d.coord.y * cells + (long)j * d.lodStep;
            return new float2(gx * spacing, gz * spacing);
        }

        public static float2 ChunkOrigin(in ChunkDesc d) => new float2(d.coord.x, d.coord.y) * d.size;

        /// Fills a Mesh (positions relative to the chunk origin, normals,
        /// vertex colours, world-space UVs) from the bordered height grid.
        public static void Build(Mesh mesh, in ChunkDesc d, in NativeArray<float> heights, in TerrainParams prm, TerrainSettings colours)
        {
            int n = VertsPerEdge(d);
            int bn = n + 2;
            float spacing = Spacing(d);
            float2 origin = ChunkOrigin(d);

            var verts = new Vector3[n * n];
            var normals = new Vector3[n * n];
            var cols = new Color32[n * n];
            var uvs = new Vector2[n * n];
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    int bi = (j + 1) * bn + (i + 1);
                    float h = heights[bi];
                    float2 w = VertexWorldXZ(d, i, j);
                    verts[j * n + i] = new Vector3(w.x - origin.x, h, w.y - origin.y);
                    // Central differences on the bordered grid — valid on the edge too.
                    float dx = (heights[bi + 1] - heights[bi - 1]) / (2f * spacing);
                    float dz = (heights[bi + bn] - heights[bi - bn]) / (2f * spacing);
                    var nrm = new Vector3(-dx, 1f, -dz).normalized;
                    normals[j * n + i] = nrm;
                    cols[j * n + i] = VertexColour(h - prm.seaLevel, nrm.y, colours);
                    uvs[j * n + i] = new Vector2(w.x, w.y) * 0.05f;
                }
            }

            int quads = (n - 1) * (n - 1);
            var tris = new int[quads * 6];
            int t = 0;
            for (int j = 0; j < n - 1; j++)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    int a = j * n + i, b = a + 1, c = a + n, e = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = e;
                }
            }

            mesh.Clear();
            mesh.indexFormat = n * n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.colors32 = cols;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        /// Sand in the beach band, grass above, rock where steep, snow high.
        static Color32 VertexColour(float hAboveSea, float up, TerrainSettings s)
        {
            Color sand = new Color(0.86f, 0.78f, 0.55f), grass = new Color(0.30f, 0.55f, 0.22f),
                  rock = new Color(0.42f, 0.38f, 0.34f), snow = new Color(0.95f, 0.95f, 0.97f),
                  seabed = new Color(0.45f, 0.5f, 0.4f);
            Color c;
            if (hAboveSea < 0f) c = Color.Lerp(sand, seabed, math.saturate(-hAboveSea / 6f));
            else if (hAboveSea < s.beachHeight) c = sand;
            else c = Color.Lerp(sand, grass, math.saturate((hAboveSea - s.beachHeight) / 3f));
            float snowT = math.saturate((hAboveSea - s.snowHeight) / 8f);
            c = Color.Lerp(c, snow, snowT);
            float slopeT = math.saturate((0.8f - up) / 0.25f); // up < 0.8 (~37°) starts rock
            c = Color.Lerp(c, rock, slopeT);
            return c;
        }
    }
}
