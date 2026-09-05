using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeaSick.Terrain
{
    /// Builds the mesh for one chunk of terrain, entirely in Burst jobs that
    /// write straight into Mesh.MeshData. Vertices live on a GLOBAL integer
    /// lattice (vertex index * spacing) so two chunks that share an edge
    /// compute bit-identical positions; heights are sampled one ring beyond
    /// the chunk so edge normals see the neighbour's slope and match across
    /// the seam. LOD is a vertex stride; chunks of different LOD meet with a
    /// skirt (edge vertices duplicated and dropped by skirtDepth) so they
    /// never depend on each other and never need a rebuild when a neighbour
    /// changes LOD.
    ///
    /// Vertex layout: first n*n are the grid (index j*n+i), then 4*n skirt
    /// vertices (west, east, south, north edges in that order).
    public static class TerrainChunkMesher
    {
        public struct ChunkDesc
        {
            public int2 coord;       // chunk grid coordinate
            public float size;       // metres per chunk edge
            public int resolution;   // vertices per edge (LOD 0)
            public int lodStep;      // 1 = full density, 2 = every other vertex, ...
        }

        /// Blittable look parameters for the vertex colour bake.
        public struct ColourParams
        {
            public float seaLevel, beachHeight, snowHeight, sandHeight;
            public float rockShowsAt, cliffRockStart, cliffRockFull;
            public static ColourParams From(TerrainSettings s) =>
                new ColourParams { seaLevel = s.seaLevel, beachHeight = s.beachHeight,
                                   snowHeight = s.snowHeight, sandHeight = s.sandHeight,
                                   rockShowsAt = math.max(0.05f, s.rockShowsAt),
                                   cliffRockStart = s.cliffRockStart,
                                   cliffRockFull = math.min(s.cliffRockFull, s.cliffRockStart - 0.01f) };
        }

        public struct Vertex
        {
            public float3 position;
            public float3 normal;
            public Color32 colour;
            public float2 uv;
        }

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        };

        public static int VertsPerEdge(in ChunkDesc d) => (d.resolution - 1) / d.lodStep + 1;
        public static float Spacing(in ChunkDesc d) => d.size / (d.resolution - 1) * d.lodStep;
        public static int VertexCount(in ChunkDesc d) { int n = VertsPerEdge(d); return n * n + 4 * n; }
        public static int IndexCount(in ChunkDesc d) { int n = VertsPerEdge(d); return (n - 1) * (n - 1) * 6 + 4 * (n - 1) * 6; }

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

        /// Heights on the (n+2)² bordered grid. Index (i+1, j+1) is local vertex (i, j).
        [BurstCompile]
        public struct HeightJob : IJobParallelFor
        {
            public ChunkDesc desc;
            public TerrainParams prm;
            [ReadOnly] public NativeArray<float> lut;
            [WriteOnly] public NativeArray<float> heights;
            [WriteOnly] public NativeArray<float> rock;
            public int borderedN;

            public void Execute(int index)
            {
                int x = index % borderedN - 1, y = index / borderedN - 1;
                // Evaluate, not Height: `Height` IS `Evaluate(...).height`, so
                // taking the whole sample costs nothing and carries the metres
                // of proud rock out with it. Recomputing it in the mesh job
                // would have meant a second pass over the entire pipeline.
                var s = TerrainHeight.Evaluate(VertexWorldXZ(desc, x, y), prm, lut);
                heights[index] = s.height;
                rock[index] = s.rock;
            }
        }

        /// Fills the MeshData vertex and index buffers; bounds out as (minY, maxY).
        [BurstCompile]
        public struct MeshJob : IJob
        {
            public ChunkDesc desc;
            public ColourParams colours;
            public float skirtDepth;
            [ReadOnly] public NativeArray<float> heights;
            [ReadOnly] public NativeArray<float> rock;
            public Mesh.MeshData mesh;
            [WriteOnly] public NativeArray<float> yRange;

            public void Execute()
            {
                int n = VertsPerEdge(desc);
                int bn = n + 2;
                float spacing = Spacing(desc);
                float2 origin = ChunkOrigin(desc);
                var verts = mesh.GetVertexData<Vertex>();
                var tris = mesh.GetIndexData<ushort>();

                float minY = float.MaxValue, maxY = float.MinValue;
                for (int j = 0; j < n; j++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        int bi = (j + 1) * bn + (i + 1);
                        float h = heights[bi];
                        float2 w = VertexWorldXZ(desc, i, j);
                        float dx = (heights[bi + 1] - heights[bi - 1]) / (2f * spacing);
                        float dz = (heights[bi + bn] - heights[bi - bn]) / (2f * spacing);
                        float3 nrm = math.normalize(new float3(-dx, 1f, -dz));
                        Vertex v;
                        v.position = new float3(w.x - origin.x, h, w.y - origin.y);
                        v.normal = nrm;
                        v.colour = VertexColour(h - colours.seaLevel, nrm.y, rock[bi], colours);
                        v.uv = w * 0.05f;
                        verts[j * n + i] = v;
                        minY = math.min(minY, h); maxY = math.max(maxY, h);
                    }
                }

                // Skirts: copies of the edge vertices dropped by skirtDepth.
                int sw = n * n, se = sw + n, ss = se + n, sn = ss + n;
                for (int k = 0; k < n; k++)
                {
                    verts[sw + k] = Dropped(verts[k * n + 0]);
                    verts[se + k] = Dropped(verts[k * n + (n - 1)]);
                    verts[ss + k] = Dropped(verts[0 * n + k]);
                    verts[sn + k] = Dropped(verts[(n - 1) * n + k]);
                }
                yRange[0] = minY - skirtDepth; yRange[1] = maxY;

                int t = 0;
                for (int j = 0; j < n - 1; j++)
                {
                    for (int i = 0; i < n - 1; i++)
                    {
                        int a = j * n + i, b = a + 1, c = a + n, e = c + 1;
                        tris[t++] = (ushort)a; tris[t++] = (ushort)c; tris[t++] = (ushort)b;
                        tris[t++] = (ushort)b; tris[t++] = (ushort)c; tris[t++] = (ushort)e;
                    }
                }
                // Skirt quads, wound to face outward (Unity: clockwise = front).
                for (int k = 0; k < n - 1; k++)
                {
                    // West (x = 0), seen from -X: +Z runs left.
                    int e0 = k * n, e1 = (k + 1) * n, s0 = sw + k, s1 = sw + k + 1;
                    Quad(tris, ref t, e1, e0, s1, s0);
                    // East (x = max), seen from +X: +Z runs right.
                    e0 = k * n + (n - 1); e1 = (k + 1) * n + (n - 1); s0 = se + k; s1 = se + k + 1;
                    Quad(tris, ref t, e0, e1, s0, s1);
                    // South (z = 0), seen from -Z: +X runs right.
                    e0 = k; e1 = k + 1; s0 = ss + k; s1 = ss + k + 1;
                    Quad(tris, ref t, e0, e1, s0, s1);
                    // North (z = max), seen from +Z: +X runs left.
                    e0 = (n - 1) * n + k; e1 = (n - 1) * n + k + 1; s0 = sn + k; s1 = sn + k + 1;
                    Quad(tris, ref t, e1, e0, s1, s0);
                }
            }

            /// topLeft/topRight are edge vertices, bottomLeft/bottomRight the
            /// dropped copies, as seen from outside the chunk.
            static void Quad(NativeArray<ushort> tris, ref int t, int topLeft, int topRight, int bottomLeft, int bottomRight)
            {
                tris[t++] = (ushort)topLeft; tris[t++] = (ushort)topRight; tris[t++] = (ushort)bottomLeft;
                tris[t++] = (ushort)topRight; tris[t++] = (ushort)bottomRight; tris[t++] = (ushort)bottomLeft;
            }

            /// A skirt vertex: the edge vertex dropped, and DARKENED.
            ///
            /// It used to be an exact copy, colour included, and that is what
            /// made it read as a fault instead of as shadow. A skirt is only
            /// ever seen where it was not meant to be seen -- an outward-facing
            /// chunk edge at the boundary of the loaded region, or an LOD
            /// transition that opened wider than it should. At a shoreline the
            /// edge vertex is beach sand, so what hung there was a 14 m curtain
            /// of bright cream, fanning into triangles wherever the edge was
            /// uneven. Exactly what the tooltip on `skirtDepth` warned about.
            ///
            /// Darkening does not hide a crack -- the skirt still fills it --
            /// it changes what an exposed one LOOKS like: an undercut in
            /// shadow, which is what the underside of ground should look like,
            /// rather than a beach standing on its end. Cheaper and safer than
            /// trimming skirtDepth, which was measured at 14 m against a worst
            /// crack of 11.9 m and has little room in it.
            Vertex Dropped(Vertex v)
            {
                v.position.y -= skirtDepth;
                v.colour = new Color32((byte)(v.colour.r * 0.30f),
                                       (byte)(v.colour.g * 0.30f),
                                       (byte)(v.colour.b * 0.32f),
                                       v.colour.a);
                return v;
            }
        }

        /// Sand in the beach band, grass above, snow high — and rock where
        /// rock actually IS.
        ///
        /// `proud` is metres of rock standing above the soil, straight out of
        /// the height pipeline. It used to be inferred entirely from the
        /// gradient (`up < 0.8`, i.e. 37 degrees, which is ordinary
        /// hillside), and that is what put broad brown smears across every
        /// green flank: the shader was guessing at rock because there was no
        /// rock in the geometry to ask about. There is now, so the material
        /// is a fact rather than an inference.
        ///
        /// The slope term stays, moved out to genuine cliffs: a sheer face
        /// is stone whether or not a crag happens to have broken out on it,
        /// because soil does not stay on one.
        static Color32 VertexColour(float hAboveSea, float up, float proud, in ColourParams s)
        {
            float3 sand = new float3(0.86f, 0.78f, 0.55f), grass = new float3(0.30f, 0.55f, 0.22f),
                   rock = new float3(0.42f, 0.38f, 0.34f), snow = new float3(0.95f, 0.95f, 0.97f),
                   seabed = new float3(0.45f, 0.5f, 0.4f);
            float3 c;
            if (hAboveSea < 0f) c = math.lerp(sand, seabed, math.saturate(-hAboveSea / 6f));
            // Sand stops at the BERM, not at the blend band. Painting it all
            // the way to beachHeight put a yellow stripe up the hillside
            // behind every beach, which is half of why the shore read as a
            // mountainside with sand on it rather than as a beach.
            else if (hAboveSea < s.sandHeight) c = sand;
            else c = math.lerp(sand, grass, math.saturate((hAboveSea - s.sandHeight) / 2.5f));
            c = math.lerp(c, snow, math.saturate((hAboveSea - s.snowHeight) / 8f));
            float wonHere = math.saturate(proud / s.rockShowsAt);
            float cliff = math.saturate((s.cliffRockStart - up)
                                        / math.max(0.01f, s.cliffRockStart - s.cliffRockFull));
            c = math.lerp(c, rock, math.max(wonHere, cliff));
            return new Color32((byte)(c.x * 255f), (byte)(c.y * 255f), (byte)(c.z * 255f), 255);
        }

        /// Sizes a writable MeshData for this chunk (main thread, before scheduling).
        public static void Prepare(Mesh.MeshData md, in ChunkDesc d)
        {
            md.SetVertexBufferParams(VertexCount(d), Layout);
            md.SetIndexBufferParams(IndexCount(d), IndexFormat.UInt16);
        }

        /// Schedules height sampling then mesh assembly. Caller owns heights
        /// and yRange (dispose after Complete) and the MeshDataArray.
        public static JobHandle Schedule(in ChunkDesc d, in TerrainParams prm, NativeArray<float> lut, in ColourParams colours,
            float skirtDepth, NativeArray<float> heights, NativeArray<float> rock,
            Mesh.MeshData md, NativeArray<float> yRange, JobHandle deps = default)
        {
            int bn = VertsPerEdge(d) + 2;
            var hj = new HeightJob { desc = d, prm = prm, lut = lut, heights = heights, rock = rock, borderedN = bn }
                .Schedule(bn * bn, 64, deps);
            return new MeshJob { desc = d, colours = colours, skirtDepth = skirtDepth, heights = heights, rock = rock, mesh = md, yRange = yRange }
                .Schedule(hj);
        }

        /// Finishes a prepared MeshData into the Mesh (main thread).
        public static void Apply(Mesh.MeshDataArray mda, Mesh mesh, in ChunkDesc d, NativeArray<float> yRange)
        {
            var md = mda[0];
            md.subMeshCount = 1;
            md.SetSubMesh(0, new SubMeshDescriptor(0, IndexCount(d)), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            Mesh.ApplyAndDisposeWritableMeshData(mda, mesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            float lo = yRange[0], hi = yRange[1];
            mesh.bounds = new Bounds(new Vector3(d.size * 0.5f, (lo + hi) * 0.5f, d.size * 0.5f), new Vector3(d.size, hi - lo + 0.01f, d.size));
        }

        /// Synchronous build for edit-mode tools and probes.
        public static void BuildSync(Mesh mesh, in ChunkDesc d, in TerrainParams prm, NativeArray<float> lut, in ColourParams colours, float skirtDepth)
        {
            int bn = VertsPerEdge(d) + 2;
            var heights = new NativeArray<float>(bn * bn, Allocator.TempJob);
            var rock = new NativeArray<float>(bn * bn, Allocator.TempJob);
            var yRange = new NativeArray<float>(2, Allocator.TempJob);
            var mda = Mesh.AllocateWritableMeshData(1);
            Prepare(mda[0], d);
            Schedule(d, prm, lut, colours, skirtDepth, heights, rock, mda[0], yRange).Complete();
            Apply(mda, mesh, d, yRange);
            heights.Dispose(); rock.Dispose(); yRange.Dispose();
        }
    }
}
