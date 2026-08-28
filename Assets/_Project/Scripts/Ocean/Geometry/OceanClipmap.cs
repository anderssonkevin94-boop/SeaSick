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
    /// Cascade weights fade the high-frequency cascades out before the mesh's
    /// vertex density stops resolving them (a boiling horizon is the
    /// alternative), but they are computed per VERTEX from its distance to the
    /// camera, not per ring. Per ring they cannot be: rings double in cell
    /// size, so a weight curve sampled once per ring is sampled at eight
    /// points that jump 2x each, and the chop went 0.78 -> 0.00 between two
    /// neighbouring rings. That is a hard, camera-locked circle drawn on the
    /// water at ~128 m, and a second at ~512 m where the mid band did the
    /// same. Ramped per metre the same schedule reads as waves getting
    /// smaller. See CascadeFade below for why this costs no resolution.
    public class OceanClipmap : MonoBehaviour
    {
        [SerializeField] Material material;
        [Tooltip("Optional: rings follow this instead of the main camera (probes use it).")]
        [SerializeField] Transform followOverride;

        [Tooltip("Cells across the centre block; also cells across each ring annulus.")]
        [SerializeField] int cellsAcross = 128;

        /// Vertical half-extent of every ring's bounds, metres. Bounds only
        /// drive culling, so generous is free and mean is a bug: at storm
        /// amplitudes a 120 m box (the old value, +-60 m) starts culling rings
        /// that are still on screen, and the sea flickers out in patches.
        const float BoundsHeight = 800f;

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

            PublishCascadeFade(c0, cellsAcross, patches);

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

                rings.Add(go.transform);
                cellSizes.Add(cell);
            }
        }

        // ---- CascadeFade ---------------------------------------------------
        // THE RULE LIVES IN TWO PLACES AND MUST MATCH: here, and
        // `CascadeWeightsAt` in Ocean.shader / OceanDebug.shader. Only the
        // three lines of curve are duplicated -- every parameter is computed
        // once, here, and pushed as a global, so the two cannot drift apart on
        // a retune. The same contract RegionField.cs / RegionField.hlsl have.
        //
        // A cascade may be drawn as long as the mesh under it has enough
        // vertices for the cascade's waves. The mesh's cell size at a given
        // distance is not a free choice -- ring r spans 14.5*2^r to 32*2^r
        // metres with cells of 0.5*2^r, so the cell a vertex sits on is
        // between d/64 and d/29. Taking the OUTER end, `cell = d / 64`, is
        // what makes this safe: the region a ring actually owns starts where
        // the finer ring stops, at d = 16*2^r, and there the rule asks for
        // 4*d/64 = 2^r metres of wavelength, which is exactly two vertices per
        // wave on that ring's cells. So nothing below the ring's own Nyquist
        // is ever requested in the water that ring is responsible for --
        // the fade is smooth in distance AND no coarser than the old per-ring
        // one where it counts. The only place it asks for more is the sliver
        // at each ring's inner edge, which the finer ring is drawn over.
        //
        // It also makes the OVERLAP consistent, which the per-ring rule never
        // was: two rings covering the same water now compute the same weight
        // there instead of differing by a factor of two.
        const float VertsPerWave = 4f;

        static Vector4 fadeLamMin, fadeLamMax, fadeCell;

        static void PublishCascadeFade(float innerCell, int cellsAcross, float[] patches)
        {
            // Wavelength range each cascade covers. Cascade 0 (64..patch0),
            // 1 (16..64), 2 (min..16) by design. patch0 is 2048 m so the storm
            // swell's 400-900 m rollers have somewhere to live; 512 m held
            // exactly one of them.
            fadeLamMin = new Vector4(patches[1] * 0.5f, patches[2] * 0.5f, 1f, 0f);
            fadeLamMax = new Vector4(patches[0], patches[1] * 0.5f, patches[2] * 0.5f, 0f);
            // x: metres of resolvable wavelength gained per metre of distance.
            // y: the floor, from the innermost cells -- inside the centre block
            // there is no more detail to be had however close you stand.
            fadeCell = new Vector4(VertsPerWave * 2f / Mathf.Max(cellsAcross, 1),
                                   VertsPerWave * innerCell, 0f, 0f);
            Shader.SetGlobalVector("_Ocean_FadeLamMin", fadeLamMin);
            Shader.SetGlobalVector("_Ocean_FadeLamMax", fadeLamMax);
            Shader.SetGlobalVector("_Ocean_CascadeFade", fadeCell);
        }

        /// What a vertex this far from the camera gets, per cascade. The C#
        /// twin of `CascadeWeightsAt` in the shaders; probes read the schedule
        /// through this rather than restating it.
        public static Vector4 WeightsAt(float distance)
        {
            float lamRes = Mathf.Max(fadeCell.y, distance * fadeCell.x);
            var w = Vector4.zero;
            for (int c = 0; c < 3; c++)
            {
                float t = Mathf.Clamp01((lamRes - fadeLamMin[c])
                    / Mathf.Max(fadeLamMax[c] - fadeLamMin[c], 1e-4f));
                // smoothstep, not the old 1 - t*t: it leaves the curve flat at
                // both ends, so the cascade neither snaps away from full
                // strength nor arrives at zero still falling. A ramp with a
                // corner in it is a fainter version of the same line.
                w[c] = 1f - t * t * (3f - 2f * t);
            }
            return w;
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
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(bound * 2f, BoundsHeight, bound * 2f));
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
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(half * 2f, BoundsHeight, half * 2f));
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
