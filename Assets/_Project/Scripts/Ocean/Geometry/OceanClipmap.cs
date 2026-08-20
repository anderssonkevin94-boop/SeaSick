using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ocean
{
    /// Camera-locked concentric ring geometry. A dense centre block plus
    /// annulus rings of doubling cell size, each ring's anchor snapped to twice
    /// its own cell size so vertices land on the same world positions every
    /// frame — the surface never crawls under a moving camera. Ring boundaries
    /// overlap by two coarse cells and carry small downward skirts to hide the
    /// 2:1 T-junctions; the outermost ring adds a wide horizon flange.
    /// Per-ring cascade weights fade high-frequency cascades out before the
    /// ring's vertex density stops resolving them (a boiling horizon is the
    /// alternative).
    public class OceanClipmap : MonoBehaviour
    {
        [SerializeField] Material material;
        [Tooltip("Optional: rings follow this instead of the main camera (probes use it).")]
        [SerializeField] Transform followOverride;

        [Tooltip("Cells across the centre block; also cells across each ring annulus.")]
        [SerializeField] int cellsAcross = 128;

        readonly List<Transform> rings = new List<Transform>();
        readonly List<float> cellSizes = new List<float>();

        public Transform FollowOverride { get => followOverride; set => followOverride = value; }
        public Material Material { get => material; set => material = value; }
        public int RingCount => rings.Count;

        void Start()
        {
            Build();
        }

        public void Build()
        {
            foreach (var r in rings) if (r != null) Destroy(r.gameObject);
            rings.Clear();
            cellSizes.Clear();

            var q = OceanQuality.Active;
            float c0 = q != null ? q.innerCellSize : 0.5f;
            int ringCount = q != null ? q.clipmapRings : 7;
            float[] patches = q != null ? q.patchSizes : new[] { 512f, 128f, 32f };

            // Wavelength range per cascade, for the per-ring weights.
            // Cascade bands: c0 (64..512), c1 (16..64), c2 (min..16) by design.
            var lamMin = new float[] { patches[1] * 0.5f, patches[2] * 0.5f, 1f };
            var lamMax = new float[] { patches[0], patches[1] * 0.5f, patches[2] * 0.5f };

            for (int r = 0; r < ringCount; r++)
            {
                float cell = c0 * (1 << r);
                var mesh = r == 0
                    ? BuildCentreBlock(cellsAcross, cell)
                    : BuildRing(cellsAcross, cell, r == ringCount - 1);

                var go = new GameObject($"Ring{r}");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // Cascade weight: fraction of the cascade's wavelength range
                // this ring's vertex density resolves (4 verts per wavelength).
                float lamResolvable = 4f * cell;
                var weights = Vector4.zero;
                for (int c = 0; c < 3; c++)
                {
                    float t = Mathf.InverseLerp(lamMin[c], lamMax[c], lamResolvable);
                    weights[c] = 1f - Mathf.Clamp01(t) * Mathf.Clamp01(t);
                }
                var mpb = new MaterialPropertyBlock();
                mpb.SetVector("_Ocean_CascadeWeights", weights);
                mr.SetPropertyBlock(mpb);

                rings.Add(go.transform);
                cellSizes.Add(cell);
            }
        }

        void LateUpdate()
        {
            Transform follow = followOverride != null ? followOverride
                : (Camera.main != null ? Camera.main.transform : null);
            if (follow == null) return;
            Vector3 p = follow.position;
            for (int r = 0; r < rings.Count; r++)
            {
                float snap = cellSizes[r] * 2f;
                rings[r].position = new Vector3(
                    Mathf.Floor(p.x / snap) * snap, 0f,
                    Mathf.Floor(p.z / snap) * snap);
            }
        }

        static Mesh BuildCentreBlock(int cells, float cell)
        {
            // Full grid, extended one cell outward as overlap under ring 1.
            int c = cells + 4;
            float half = c * cell * 0.5f;
            return BuildGrid(c, c, cell, -half, -half, null);
        }

        static Mesh BuildRing(int cells, float cell, bool outermost)
        {
            // Annulus: outer extent = cells*cell, hole = half that minus a
            // two-cell overlap margin (the finer ring's snap step is one of
            // this ring's cells; two cells absorb any anchor mismatch).
            int c = cells;
            float outerHalf = c * cell * 0.5f;
            float holeHalf = outerHalf * 0.5f - 3f * cell;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            int n1 = c + 1;
            var index = new int[n1 * n1];
            for (int i = 0; i < index.Length; i++) index[i] = -1;

            for (int z = 0; z <= c; z++)
                for (int x = 0; x <= c; x++)
                {
                    float px = -outerHalf + x * cell;
                    float pz = -outerHalf + z * cell;
                    // Keep vertices on or outside the hole boundary.
                    if (Mathf.Abs(px) < holeHalf - 0.01f && Mathf.Abs(pz) < holeHalf - 0.01f)
                        continue;
                    index[z * n1 + x] = verts.Count;
                    verts.Add(new Vector3(px, 0f, pz));
                }

            for (int z = 0; z < c; z++)
                for (int x = 0; x < c; x++)
                {
                    int a = index[z * n1 + x];
                    int b = index[z * n1 + x + 1];
                    int d = index[(z + 1) * n1 + x];
                    int e = index[(z + 1) * n1 + x + 1];
                    if (a < 0 || b < 0 || d < 0 || e < 0) continue;
                    // Skip quads fully inside the hole (all four on boundary
                    // ring are kept; interior omissions handled above).
                    Vector3 centre = (verts[a] + verts[e]) * 0.5f;
                    if (Mathf.Abs(centre.x) < holeHalf && Mathf.Abs(centre.z) < holeHalf)
                        continue;
                    tris.Add(a); tris.Add(d); tris.Add(b);
                    tris.Add(b); tris.Add(d); tris.Add(e);
                }

            // Skirts: a short flange dropped below the inner edge hides the
            // T-junction against the finer ring; the outermost ring instead
            // gets a huge outward flange at y=0 that carries the horizon
            // (displacement fades to zero long before it, so it stays flat).
            AddEdgeSkirt(verts, tris, holeHalf, -cell * 1.5f, true);
            if (outermost)
                AddEdgeSkirt(verts, tris, outerHalf, 0f, false, 50f);
            else
                AddEdgeSkirt(verts, tris, outerHalf, -cell * 1.5f, false);

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            float bound = outermost ? outerHalf * 51f : outerHalf;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(bound * 2f, 120f, bound * 2f));
            return mesh;
        }

        static Mesh BuildGrid(int cx, int cz, float cell, float ox, float oz, List<Vector3> extra)
        {
            var verts = new Vector3[(cx + 1) * (cz + 1)];
            var tris = new int[cx * cz * 6];
            for (int z = 0; z <= cz; z++)
                for (int x = 0; x <= cx; x++)
                    verts[z * (cx + 1) + x] = new Vector3(ox + x * cell, 0f, oz + z * cell);
            int t = 0;
            for (int z = 0; z < cz; z++)
                for (int x = 0; x < cx; x++)
                {
                    int v = z * (cx + 1) + x;
                    tris[t++] = v; tris[t++] = v + cx + 1; tris[t++] = v + 1;
                    tris[t++] = v + 1; tris[t++] = v + cx + 1; tris[t++] = v + cx + 2;
                }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.triangles = tris;
            float half = Mathf.Max(cx, cz) * cell * 0.5f;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(half * 2f, 120f, half * 2f));
            return mesh;
        }

        /// Adds a quad strip around the square |x|,|z| = half: either dropped
        /// by `drop` (crack cover) or extended outward by `stretch` x half
        /// (horizon flange).
        static void AddEdgeSkirt(List<Vector3> verts, List<int> tris, float half,
            float drop, bool inner, float stretch = 0f)
        {
            int segs = 32;
            float step = half * 2f / segs;
            for (int side = 0; side < 4; side++)
            {
                for (int i = 0; i < segs; i++)
                {
                    Vector3 a = EdgePoint(side, half, -half + i * step);
                    Vector3 b = EdgePoint(side, half, -half + (i + 1) * step);
                    Vector3 a2, b2;
                    if (stretch > 0f)
                    {
                        a2 = new Vector3(a.x * stretch, 0f, a.z * stretch);
                        b2 = new Vector3(b.x * stretch, 0f, b.z * stretch);
                    }
                    else
                    {
                        a2 = a + Vector3.up * drop;
                        b2 = b + Vector3.up * drop;
                    }
                    int i0 = verts.Count;
                    verts.Add(a); verts.Add(b); verts.Add(a2); verts.Add(b2);
                    bool flip = inner ^ (side is 1 or 2);
                    if (flip)
                    {
                        tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 1);
                        tris.Add(i0 + 1); tris.Add(i0 + 2); tris.Add(i0 + 3);
                    }
                    else
                    {
                        tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
                        tris.Add(i0 + 1); tris.Add(i0 + 3); tris.Add(i0 + 2);
                    }
                }
            }
        }

        static Vector3 EdgePoint(int side, float half, float t) => side switch
        {
            0 => new Vector3(t, 0f, half),
            1 => new Vector3(t, 0f, -half),
            2 => new Vector3(half, 0f, t),
            _ => new Vector3(-half, 0f, t),
        };
    }
}
