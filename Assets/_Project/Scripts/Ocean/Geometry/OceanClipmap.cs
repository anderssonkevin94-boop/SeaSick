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
    ///
    /// Every ring (and the centre block) is actually FOUR quadrant meshes, not
    /// one square. A camera-centred square always has the camera inside its
    /// AABB, from every direction, so it never culls no matter which way the
    /// camera looks -- the outermost ring's own bounds is `outerHalf * 51`
    /// because of the horizon flange, and that whole box passed culling in
    /// every view direction every frame. Split at the ring's own centre into
    /// NE/SE/NW/SW quadrants, each with its own tight bounds, and the half of
    /// the sea behind or beside the camera is a normal frustum-culled
    /// renderer again. The horizon flange stays one piece (see
    /// BuildHorizonFlange) -- it exists to always be on screen, so tightening
    /// its bounds buys nothing, and quartering a thin 51x skirt is not worth
    /// the complexity.
    ///
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

        /// FALLBACK ONLY. The tier decides this now (`OceanQuality.cellsAcross`,
        /// 128 PC / 64 mobile) because it used to be serialized here instead
        /// and both tiers built at 128 -- the phone paid for 5 rings of PC-
        /// density geometry (~75k verts / 141k tris) it could not resolve.
        /// This field is read only when `OceanQuality.Active` is null (no
        /// scene, an editor tool building a ring in isolation).
        [Tooltip("Cells across the centre block; also cells across each ring " +
                 "annulus. Used only when no OceanQuality asset resolves -- " +
                 "otherwise the active tier's cellsAcross wins.")]
        [SerializeField] int cellsAcross = 128;

        /// Vertical half-extent of every ring's bounds, metres. Bounds only
        /// drive culling, so generous is free and mean is a bug: at storm
        /// amplitudes a 120 m box (the old value, +-60 m) starts culling rings
        /// that are still on screen, and the sea flickers out in patches.
        const float BoundsHeight = 800f;

        readonly List<Transform> rings = new List<Transform>();
        readonly List<float> cellSizes = new List<float>();

        /// The material every ring is actually drawn with at runtime: a COPY of
        /// the authored `material`, made once in Start and destroyed in
        /// OnDestroy.
        ///
        /// Why a copy at all. The ocean shader's see-through half now lives
        /// behind a `_REFRACTION` keyword, and the keyword has to be set per
        /// TIER, from OceanQuality.Active — a runtime decision. The authored
        /// material is a shared asset: setting a keyword on it in play mode
        /// writes through to the .mat on disk in the editor, which is how a
        /// "PC" asset silently ends up saved with the phone's keyword state
        /// (and the reverse), and there is exactly one OceanSurface.mat handed
        /// out from a scene field, so there is nowhere else for that damage to
        /// land. An instance keeps the decision where it belongs — in the
        /// running game — and guarantees the project's own rule that runtime
        /// material edits do not survive leaving play mode.
        ///
        /// It is also why the shader uses `multi_compile` and not
        /// `shader_feature`: shader_feature strips variants against the
        /// keywords SAVED ON THE ASSET at build time, so the variant this
        /// instance wants might not exist in the player at all. multi_compile
        /// keeps both and this line picks one.
        Material instance;

        public Transform FollowOverride { get => followOverride; set => followOverride = value; }

        /// The material the rings are drawn with — the runtime instance once
        /// Start has run, the authored asset before that. Anything that writes
        /// shader properties at runtime (WaterClarityTuner, SeaFoamTuner, the
        /// look probes) must hit the instance or its edits go to a material
        /// nothing on screen is using. The dev tuners find it by scanning
        /// renderers for `sharedMaterial` with shader "SeaSick/Ocean", which
        /// lands on the instance by construction.
        public Material Material
        {
            get => instance != null ? instance : material;
            set { material = value; if (instance != null) ApplyQualityKeywords(instance); }
        }
        public int RingCount => rings.Count;

        void Start()
        {
            // ONE instance for the whole clipmap, made BEFORE the rings so
            // every ring is handed the same material and they still batch.
            if (material != null)
                instance = new Material(material) { name = material.name + " (clipmap)" };
            ApplyQualityKeywords(instance);
            Build();
        }

        void OnDestroy()
        {
            if (instance != null) Destroy(instance);
            instance = null;
        }

        /// Tier-driven shader keywords for the ocean material. Kept in one
        /// place so the rule is not restated per ring.
        static void ApplyQualityKeywords(Material m)
        {
            if (m == null) return;
            var q = OceanQuality.Active;
            bool refraction = q != null ? q.refraction : true;
            if (refraction) m.EnableKeyword("_REFRACTION");
            else m.DisableKeyword("_REFRACTION");
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
            // Tier decides the mesh density (item 1); `cellsAcross` above is
            // only what's left when nothing else will.
            int cells = q != null ? q.cellsAcross : cellsAcross;

            PublishCascadeFade(c0, cells, patches);

            // `instance` in play mode; the authored asset when an editor setup
            // script or probe rebuilds the rings outside of it, where there is
            // no instance and nothing to keep a keyword decision out of. One
            // lookup, shared by every quadrant of every ring, so they still
            // batch. Never `mr.material` below: that would mint a SECOND copy
            // per renderer and break both batching and the dev tuners, which
            // expect one ocean material to write to.
            var mat = instance != null ? instance : material;

            for (int r = 0; r < ringCount; r++)
            {
                float cell = c0 * (1 << r);
                bool outermost = r == ringCount - 1;
                var go = new GameObject($"Ring{r}");
                go.transform.SetParent(transform, false);

                // Four quadrant meshes, not one square: a camera-centred
                // square has the camera inside its AABB from every direction,
                // so it never culls. Each quadrant gets its own tight bounds
                // instead, and the parent (this transform) is what LateUpdate
                // snaps, so all four still move together as one ring.
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        var mesh = r == 0
                            ? BuildCentreBlockQuadrant(cells, cell, sx, sz)
                            : BuildRingQuadrant(cells, cell, outermost, sx, sz);
                        string name = (sx > 0 ? "E" : "W") + (sz > 0 ? "N" : "S");
                        AddQuadrantChild(go.transform, mesh, mat, name);
                    }

                // The horizon flange stays one piece -- see BuildHorizonFlange.
                if (outermost)
                    AddQuadrantChild(go.transform, BuildHorizonFlange(cells, cell), mat, "Flange");

                rings.Add(go.transform);
                cellSizes.Add(cell);
            }
        }

        static void AddQuadrantChild(Transform parent, Mesh mesh, Material mat, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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

        /// One quadrant (sx,sz each ±1) of the centre block: the full grid was
        /// `cells+4` cells square (the +4 is overlap under ring 1); this is a
        /// quarter of it, split at the shared centre column/row, with its own
        /// tight bounds instead of the old camera-centred square.
        static Mesh BuildCentreBlockQuadrant(int cells, float cell, int sx, int sz)
        {
            int c = cells + 4;
            float half = c * cell * 0.5f;
            int h = c / 2;
            int x0 = sx > 0 ? h : 0, x1 = sx > 0 ? c : h;
            int z0 = sz > 0 ? h : 0, z1 = sz > 0 ? c : h;
            int nx = x1 - x0, nz = z1 - z0;

            var verts = new Vector3[(nx + 1) * (nz + 1)];
            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                    verts[z * (nx + 1) + x] = new Vector3(-half + (x0 + x) * cell, 0f, -half + (z0 + z) * cell);
            var tris = new int[nx * nz * 6];
            int t = 0;
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int v = z * (nx + 1) + x;
                    tris[t++] = v; tris[t++] = v + nx + 1; tris[t++] = v + 1;
                    tris[t++] = v + 1; tris[t++] = v + nx + 1; tris[t++] = v + nx + 2;
                }

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.bounds = QuadrantBounds(-half, x0, x1, z0, z1, cell);
            return mesh;
        }

        /// One quadrant (sx,sz each ±1) of ring `r`'s annulus, split at its
        /// own centre the same way as the centre block. Every vertex, the
        /// hole cut and the overlap margin are exactly what the old single-
        /// mesh BuildRing computed for that quarter of the square — only the
        /// loop bounds and the two skirt calls (the seam between quadrants
        /// needs none; both sides land on the same world positions) differ.
        static Mesh BuildRingQuadrant(int cells, float cell, bool outermost, int sx, int sz)
        {
            int c = cells;
            float outerHalf = c * cell * 0.5f;
            float holeHalf = outerHalf * 0.5f - 3f * cell;
            int h = c / 2;
            int x0 = sx > 0 ? h : 0, x1 = sx > 0 ? c : h;
            int z0 = sz > 0 ? h : 0, z1 = sz > 0 ? c : h;
            int nx = x1 - x0, nz = z1 - z0;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            int n1x = nx + 1, n1z = nz + 1;
            var index = new int[n1x * n1z];
            for (int i = 0; i < index.Length; i++) index[i] = -1;

            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                {
                    float px = -outerHalf + (x0 + x) * cell;
                    float pz = -outerHalf + (z0 + z) * cell;
                    if (Mathf.Abs(px) < holeHalf - 0.01f && Mathf.Abs(pz) < holeHalf - 0.01f)
                        continue;
                    index[z * n1x + x] = verts.Count;
                    verts.Add(new Vector3(px, 0f, pz));
                }

            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int a = index[z * n1x + x];
                    int b = index[z * n1x + x + 1];
                    int d = index[(z + 1) * n1x + x];
                    int e = index[(z + 1) * n1x + x + 1];
                    if (a < 0 || b < 0 || d < 0 || e < 0) continue;
                    Vector3 centre = (verts[a] + verts[e]) * 0.5f;
                    if (Mathf.Abs(centre.x) < holeHalf && Mathf.Abs(centre.z) < holeHalf)
                        continue;
                    tris.Add(a); tris.Add(d); tris.Add(b);
                    tris.Add(b); tris.Add(d); tris.Add(e);
                }

            // Skirts: only THIS quadrant's two outward edges (the hole edge
            // and, unless outermost, the outer edge) get a crack-hider. The
            // two inward edges are the seam against the neighbouring
            // quadrant of this SAME ring — both share exact world positions
            // there already, so a skirt would hide nothing.
            AddEdgeSkirtQuadrant(verts, tris, holeHalf, cell, -cell * 1.5f, true, sx, sz);
            if (!outermost)
                AddEdgeSkirtQuadrant(verts, tris, outerHalf, cell, -cell * 1.5f, false, sx, sz);

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = QuadrantBounds(-outerHalf, x0, x1, z0, z1, cell);
            return mesh;
        }

        /// The outermost ring's horizon flange, kept as ONE piece rather than
        /// four quadrants. It exists purely to always be on screen out to the
        /// horizon (displacement fades to zero long before it, so it stays
        /// flat) — its bounds already can't be tightened, so quartering a
        /// thin 51x-stretched skirt would only add seam bookkeeping for no
        /// culling win.
        static Mesh BuildHorizonFlange(int cells, float cell)
        {
            float outerHalf = cells * cell * 0.5f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            AddEdgeSkirtFull(verts, tris, outerHalf, cell, 0f, false, 50f);
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            float bound = outerHalf * 51f;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(bound * 2f, BoundsHeight, bound * 2f));
            return mesh;
        }

        /// Tight AABB for one quadrant's own footprint (a quarter of the old
        /// camera-centred square), in the ring/block-local space the
        /// BuildCentreBlockQuadrant/BuildRingQuadrant loops above work in.
        /// This is the entire point of splitting: the camera sits at this
        /// ring's centre (up to the LateUpdate snap), so exactly one quadrant
        /// is behind it in any given view direction and a tight box actually
        /// lets the frustum drop it.
        /// How far a vertex may be displaced SIDEWAYS by the sea, in metres.
        /// The mesh is culled against its undisplaced footprint, and at Hs 55
        /// with choppiness the vertex shader moves a vertex tens of metres:
        /// a footprint-tight box let a quadrant beside the camera be culled
        /// while its displaced surface was still in view -- measured as a
        /// hole in the storm sea with the neighbours' skirts hanging in it
        /// as curtains (CrestProbe, Hs 55, 2026-09-12). The margin costs the
        /// inner rings their culling (their half-width is below it), which
        /// is the right trade: a hole is a bug, four small rings are not.
        const float HorizontalDisplacementMargin = 100f;

        static Bounds QuadrantBounds(float lo, int x0, int x1, int z0, int z1, float cell)
        {
            float qx0 = lo + x0 * cell, qx1 = lo + x1 * cell;
            float qz0 = lo + z0 * cell, qz1 = lo + z1 * cell;
            float m = HorizontalDisplacementMargin;
            var b = new Bounds();
            b.SetMinMax(
                new Vector3(Mathf.Min(qx0, qx1) - m, -BoundsHeight * 0.5f, Mathf.Min(qz0, qz1) - m),
                new Vector3(Mathf.Max(qx0, qx1) + m, BoundsHeight * 0.5f, Mathf.Max(qz0, qz1) + m));
            return b;
        }

        /// One quadrant's worth of edge skirt: the outward-facing half of the
        /// "z" side plus the outward-facing half of the "x" side. `segs` is
        /// picked so a full ring's two halves together sample the same cell
        /// spacing as the ring's own mesh (item 3) — a fixed 32 regardless of
        /// span used to put 1.81 cells under every segment at cellsAcross 128,
        /// so the skirt's top edge didn't land on the ring's own vertices and
        /// the crack it exists to hide reopened under large displacement.
        static void AddEdgeSkirtQuadrant(List<Vector3> verts, List<int> tris, float half, float cell,
            float drop, bool inner, int sx, int sz)
        {
            int segs = Mathf.Max(1, Mathf.RoundToInt(half / cell));
            // tFrom < tTo always, same as the full-loop version: AddSkirtSide's
            // winding depends on which end of the segment is "a", so walking
            // this quadrant's half-side backwards (t descending, which the
            // negative-sign quadrants would do if this just used 0 and
            // sx*half/sz*half directly) would flip every triangle on that
            // side and turn it into a hole seen from the one direction it's
            // supposed to face.
            float zt0 = sx > 0 ? 0f : -half, zt1 = sx > 0 ? half : 0f;
            AddSkirtSide(verts, tris, sz > 0 ? 0 : 1, half, zt0, zt1, segs, drop, inner, 0f);
            float xt0 = sz > 0 ? 0f : -half, xt1 = sz > 0 ? half : 0f;
            AddSkirtSide(verts, tris, sx > 0 ? 2 : 3, half, xt0, xt1, segs, drop, inner, 0f);
        }

        /// The full four-side loop, for the one skirt that isn't split into
        /// quadrants (the horizon flange). Same per-cell segment count as the
        /// quadrant version above, just walked all the way around.
        static void AddEdgeSkirtFull(List<Vector3> verts, List<int> tris, float half, float cell,
            float drop, bool inner, float stretch = 0f)
        {
            int segs = Mathf.Max(1, Mathf.RoundToInt(2f * half / cell));
            for (int side = 0; side < 4; side++)
                AddSkirtSide(verts, tris, side, half, -half, half, segs, drop, inner, stretch);
        }

        /// One side's worth of skirt quads, from `tFrom` to `tTo` along that
        /// side, in `segs` equal steps. Either dropped by `drop` (crack cover)
        /// or extended outward by `stretch` x half (horizon flange).
        static void AddSkirtSide(List<Vector3> verts, List<int> tris, int side, float half,
            float tFrom, float tTo, int segs, float drop, bool inner, float stretch)
        {
            float step = (tTo - tFrom) / segs;
            for (int i = 0; i < segs; i++)
            {
                Vector3 a = EdgePoint(side, half, tFrom + i * step);
                Vector3 b = EdgePoint(side, half, tFrom + (i + 1) * step);
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

        static Vector3 EdgePoint(int side, float half, float t) => side switch
        {
            0 => new Vector3(t, 0f, half),
            1 => new Vector3(t, 0f, -half),
            2 => new Vector3(half, 0f, t),
            _ => new Vector3(-half, 0f, t),
        };
    }
}
