using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// Trees and boulders, in bulk, so the eye can tell how big an island is.
    ///
    /// The land had almost nothing on it: props are RESOURCE NODES, capped at
    /// 26 an island, and 26 trees on a 400 m island is a golf course. That is
    /// most of why the islands read as small no matter how tall they were
    /// made -- scale is not something a landform can state on its own. The eye
    /// works it out from things it already knows the size of, and there was
    /// nothing out there to know.
    ///
    /// So this is scenery and nothing else: no components, no harvesting, no
    /// interaction. Raising the resource count instead would have changed the
    /// economy to fix a visual problem.
    ///
    /// Everything an island gets is baked into ONE mesh with vertex colours --
    /// one renderer, one draw call, a few thousand triangles. Six hundred
    /// GameObjects each carrying a trunk and two canopy spheres, which is what
    /// the prop factory builds, would be eighteen hundred renderers per
    /// island.
    public static class IslandScenery
    {
        /// Tree height comes off the charter in WorldScale, not from a
        /// number typed here -- which is how it ended up at 5.2-8.6 m and
        /// then at 11-26 m, each time chosen against whatever was on screen
        /// rather than against the crew, the buildings and the giants it has
        /// to be seen beside. These must NOT scale with the island.
        ///
        /// The first pass built them 5.2-8.6 m, which measured correctly and
        /// looked wrong, and the reason is worth keeping: the ship is 24.3 m
        /// overall, so the tallest tree on an island stood barely a third of
        /// her length. Conifers beside a vessel that size are as tall as she
        /// is long or taller, so the undersized trees did not read as small
        /// trees -- they read as a small ISLAND, a model of a place rather
        /// than a place. Scale cues only work in the direction of the truth.
        static float TreeMinH => SeaSick.World.WorldScale.TreeMin;
        static float TreeMaxH => SeaSick.World.WorldScale.TreeMax;

        /// Bias on the height roll. Below 1 puts most trees in the upper half
        /// of the range, which is what a stand of mature conifers looks like;
        /// an even spread reads as a nursery.
        const float HeightBias = 0.75f;

        /// `keepOut` is where nothing may stand -- the village clearing.
        ///
        /// **It suppresses the geometry, never the draws.** This walks ONE
        /// `System.Random` through the grid in order, so a `continue` here
        /// removes rolls and reshuffles every tree after it: nudge a clearing
        /// five metres and the whole wood moves. Every candidate therefore
        /// takes exactly the same numbers out of the stream whether it is
        /// built or not, and the trees outside a clearing stand exactly where
        /// they stood before there was one.
        public static GameObject Build(Transform parent, Vector3 centre, float meanR,
            System.Func<float, float, float> height, TerrainSettings terrain,
            System.Func<float, float> radiusAt, int seed, TerrainParams prm,
            System.Func<float, float, bool> keepOut = null)
        {
            var rng = new System.Random(seed);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var cols = new List<Color32>();
            var tris = new List<int>();

            // Highest ground on the island, so the tree line is a fraction of
            // THIS island rather than a world constant: a 300 m massif gets
            // bare rock up top, a 40 m one is wooded to the summit.
            float peak = 0f;
            for (int a = 0; a < 32; a++)
            {
                float ang = a / 32f * Mathf.PI * 2f;
                for (int k = 1; k <= 4; k++)
                {
                    float d = meanR * k / 5f;
                    peak = Mathf.Max(peak, height(centre.x + Mathf.Sin(ang) * d,
                                                  centre.z + Mathf.Cos(ang) * d));
                }
            }
            float sand = terrain != null ? terrain.sandHeight : 3.2f;

            // **There is no tree line.** It used to be
            // `max(18, peak * 0.62)` — a horizontal contour, which is the
            // single most artificial thing about these islands and which
            // appears nowhere in the reference boards. There, green climbs
            // nearly to the summit up gentle spurs and bare stone reaches
            // the water on steep faces: cover is a function of SLOPE and of
            // what the ground is made of, and altitude barely enters into
            // it. Only the very top thins, for exposure, and that is a
            // summit rather than a line.
            //
            // The two island-scale character fields are read ONCE, at the
            // centre: they are slow enough that one landmass sits inside one
            // value of each, which is the whole point of them (sample per
            // cell and a sandbank blends into a crag across one beach).
            var c2 = new Unity.Mathematics.float2(centre.x, centre.z);
            float verdancy = TerrainHeight.Verdancy01(c2, prm);
            float rockiness = TerrainHeight.Rock01(c2, prm);

            // Jittered grid, so the spacing reads as a wood rather than as a
            // scatter with clumps and bald patches.
            //
            // Spacing has to be read against tree HEIGHT, not chosen on its
            // own: at 12 m apart and 8 m tall the gaps were wider than the
            // trees, which is an orchard on a lawn. A 20 m conifer here
            // carries a canopy about 10 m across, so 7-12 m spacing puts the
            // canopies in contact and the stand closes up into woodland.
            // The scatter box has to cover the island's REACH, not its mean
            // radius. Islands are lobed -- the mean is an average over
            // bearings, so a headland running out past it fell outside the
            // loop altogether and came back bare, which read as a wood
            // planted in a band across the middle of the island.
            float maxR = meanR;
            if (radiusAt != null)
                for (int a = 0; a < 48; a++)
                    maxR = Mathf.Max(maxR, radiusAt(a / 48f * Mathf.PI * 2f));
            maxR = Mathf.Min(maxR, meanR * 3f);   // guard against a bad sector

            float step = Mathf.Clamp(meanR * 0.02f, 7f, 12f);
            int trees = 0, rocks = 0;
            const int MaxTrees = 3000, MaxRocks = 900;

            /// Below this peak an island has no exposed summit to thin.
            const float ExposedAbove = 60f;

            // Thin UNIFORMLY to the budget rather than filling until it runs
            // out. The scatter walks the grid in order, so a hard cap dresses
            // one band of the island and leaves the rest bare -- which is
            // exactly how it looked: a dense wood across the middle and a
            // clean green slope beside it. Estimating the eligible cells up
            // front and keeping that fraction spreads the same number of
            // trees over the whole island.
            // **The budget has to know what the rule will accept.**
            //
            // `keep` thins uniformly to the tree cap. It was calibrated
            // against the OLD acceptance rate, and the new cover rule
            // multiplies by verdancy, slope and exposure on top of it — so
            // it double-counted and the wood collapsed by about five times,
            // worst on the big islands where `keep` was already small: a
            // 314 m island came back with 152 trees on it. The expected
            // acceptance now includes verdancy, and the budget itself scales
            // with it, so a bare island is bare because it is BARE and not
            // because it is large.
            float cells = Mathf.PI * maxR * maxR / (step * step);
            float budget = MaxTrees * Mathf.Lerp(0.3f, 1f, verdancy);
            float expected = cells * 0.45f * verdancy;
            float keep = Mathf.Clamp01(budget / Mathf.Max(1f, expected));

            for (float z = -maxR; z <= maxR && trees < MaxTrees; z += step)
            {
                for (float x = -maxR; x <= maxR && trees < MaxTrees; x += step)
                {
                    float jx = (float)(rng.NextDouble() - 0.5) * step * 0.9f;
                    float jz = (float)(rng.NextDouble() - 0.5) * step * 0.9f;
                    float wx = centre.x + x + jx, wz = centre.z + z + jz;

                    float ang = Mathf.Atan2(wx - centre.x, wz - centre.z);
                    float dist = Mathf.Sqrt((wx - centre.x) * (wx - centre.x) + (wz - centre.z) * (wz - centre.z));
                    if (radiusAt != null && dist > radiusAt(ang) * 0.94f) continue;

                    float h = height(wx, wz);
                    if (h < sand + 1.2f) continue;              // not on the beach
                    float sx = (height(wx + 3f, wz) - height(wx - 3f, wz)) / 6f;
                    float sz = (height(wx, wz + 3f) - height(wx, wz - 3f)) / 6f;
                    float slope = Mathf.Sqrt(sx * sx + sz * sz);

                    // Rock that has broken through the soil is stone, and
                    // nothing roots in it.
                    float proud = TerrainHeight.RockBreak(
                        new Unity.Mathematics.float2(wx, wz), rockiness, prm);

                    float slopeTerm = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(prm.vegSlopeSoft, prm.vegSlopeHard, slope));
                    float rockTerm = proud > 0.4f ? 1f - prm.vegRockSuppress : 1f;
                    // Exposure is a MOUNTAIN phenomenon and has to be gated
                    // on the island being one. Keyed to `peak` alone it broke
                    // completely on flat land: a 5 m sandbank put its
                    // exposure band at 4.0-5.1 m, which is the whole island
                    // above the beach, so every tree on it was refused and a
                    // 10,000-cell island came back with zero.
                    float exposure = peak < ExposedAbove ? 1f
                        : 1f - Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(peak * 0.80f, peak * 1.02f, h));
                    float chance = verdancy * slopeTerm * rockTerm * exposure * keep;

                    if (rng.NextDouble() > chance)
                    {
                        // Scree where a tree could not hold: steep ground, or
                        // rock that has already surfaced.
                        bool stony = slope > prm.vegSlopeSoft || proud > 0.4f;
                        // Thinned by `keep` like the trees are. Un-thinned, a
                        // big island spent its whole rock budget on the first
                        // band the scan reached and came back with 300
                        // boulders in a stripe and bare ground beyond it —
                        // the same failure the tree budget already carries a
                        // comment about.
                        if (rocks < MaxRocks && stony && rng.NextDouble() < 0.16 * keep)
                        {
                            AddBoulder(verts, norms, cols, tris, new Vector3(wx, h, wz), rng,
                                keepOut == null || !keepOut(wx, wz));
                            rocks++;
                        }
                        continue;
                    }

                    AddTree(verts, norms, cols, tris, new Vector3(wx, h, wz), rng,
                        keepOut == null || !keepOut(wx, wz));
                    // Counted whether or not it was placed, so the budget and
                    // the loop's exit are the same with a clearing as without.
                    trees++;
                }
            }

            Debug.Log($"IslandScenery: r{meanR:F0} peak {peak:F0} verdancy {verdancy:F2} "
                + $"rockiness {rockiness:F2} keep {keep:F3} cells {cells:F0} step {step:F1} "
                + $"-> {trees} trees, {rocks} rocks");

            if (verts.Count == 0) return null;

            var go = new GameObject("Scenery");
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            var mesh = new Mesh { name = "SceneryMesh" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = SceneryMaterial();
            return go;
        }

        static Material scenery;

        /// The terrain's own shader, so scenery is lit exactly like the ground
        /// it stands on -- but with the rock striation off, which is meant for
        /// cliff faces and looks like a fault on a canopy.
        static Material SceneryMaterial()
        {
            if (scenery != null) return scenery;
            var sh = Shader.Find("SeaSick/Terrain Vertex Color");
            scenery = new Material(sh) { name = "Scenery" };
            if (scenery.HasProperty("_StriationStrength")) scenery.SetFloat("_StriationStrength", 0f);
            if (scenery.HasProperty("_DetailScale")) scenery.SetFloat("_DetailScale", 1.1f);
            if (scenery.HasProperty("_NormalStrength")) scenery.SetFloat("_NormalStrength", 0.25f);
            if (scenery.HasProperty("_DetailStrength")) scenery.SetFloat("_DetailStrength", 0.3f);
            return scenery;
        }

        /// `place` false still draws every random number this tree would
        /// have used and then writes nothing -- see the note on `keepOut`.
        static void AddTree(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 at, System.Random rng, bool place = true)
        {
            float h = Mathf.Lerp(TreeMinH, TreeMaxH, Mathf.Pow((float)rng.NextDouble(), HeightBias));
            float trunkH = h * 0.34f;
            float trunkR = h * 0.035f;
            // Crown width varies independently of height: a wood of
            // identically-proportioned cones reads as one tree stamped out
            // repeatedly, however much the heights differ.
            float canopyR = h * Mathf.Lerp(0.19f, 0.29f, (float)rng.NextDouble());
            float lean = (float)(rng.NextDouble() - 0.5) * 0.12f;
            var leanV = new Vector3(lean, 0f, (float)(rng.NextDouble() - 0.5) * 0.12f);

            var trunk = new Color32(92, 64, 40, 255);
            byte g = (byte)(74 + rng.Next(0, 58));
            var leaf = new Color32((byte)(24 + rng.Next(0, 26)), g, (byte)(34 + rng.Next(0, 26)), 255);

            if (!place) return;

            Prism(v, n, c, t, at, trunkR, trunkH, leanV, trunk);
            // Two stacked cones read as a conifer from any angle and cost 12
            // triangles; a sphere canopy costs 500 and reads as a lollipop.
            Cone(v, n, c, t, at + new Vector3(0f, trunkH, 0f) + leanV * 0.5f,
                canopyR, h * 0.42f, leanV, leaf);
            Cone(v, n, c, t, at + new Vector3(0f, trunkH + h * 0.26f, 0f) + leanV * 0.8f,
                canopyR * 0.68f, h * 0.40f, leanV, leaf);
        }

        static void AddBoulder(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 at, System.Random rng, bool place = true)
        {
            float s = Mathf.Lerp(SeaSick.World.WorldScale.BoulderMin,
                                 SeaSick.World.WorldScale.BoulderMax, (float)rng.NextDouble());
            byte grey = (byte)(96 + rng.Next(0, 40));
            var col = new Color32(grey, (byte)(grey + 4), (byte)(grey + 10), 255);
            var lean = new Vector3((float)(rng.NextDouble() - 0.5) * 0.5f, 0f,
                                   (float)(rng.NextDouble() - 0.5) * 0.5f);
            if (!place) return;
            Cone(v, n, c, t, at + new Vector3(0f, -s * 0.25f, 0f), s, s * 1.5f, lean, col, 5);
        }

        /// Four-sided prism for a trunk. Flat-shaded: normals per face.
        static void Prism(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 baseAt, float r, float h, Vector3 lean, Color32 col)
        {
            Vector3 top = baseAt + new Vector3(0f, h, 0f) + lean;
            for (int i = 0; i < 4; i++)
            {
                float a0 = i / 4f * Mathf.PI * 2f, a1 = (i + 1) / 4f * Mathf.PI * 2f;
                Vector3 p0 = baseAt + new Vector3(Mathf.Sin(a0) * r, 0f, Mathf.Cos(a0) * r);
                Vector3 p1 = baseAt + new Vector3(Mathf.Sin(a1) * r, 0f, Mathf.Cos(a1) * r);
                Vector3 p2 = top + new Vector3(Mathf.Sin(a1) * r * 0.7f, 0f, Mathf.Cos(a1) * r * 0.7f);
                Vector3 p3 = top + new Vector3(Mathf.Sin(a0) * r * 0.7f, 0f, Mathf.Cos(a0) * r * 0.7f);
                Quad(v, n, c, t, p0, p1, p2, p3, col);
            }
        }

        /// Cone with `sides` faces, used for canopies and boulders alike.
        static void Cone(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 baseAt, float r, float h, Vector3 lean, Color32 col, int sides = 6)
        {
            Vector3 apex = baseAt + new Vector3(0f, h, 0f) + lean;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                Vector3 p0 = baseAt + new Vector3(Mathf.Sin(a0) * r, 0f, Mathf.Cos(a0) * r);
                Vector3 p1 = baseAt + new Vector3(Mathf.Sin(a1) * r, 0f, Mathf.Cos(a1) * r);
                Tri(v, n, c, t, p0, p1, apex, col);
            }
        }

        static void Tri(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 a, Vector3 b, Vector3 d, Color32 col)
        {
            int i0 = v.Count;
            Vector3 nrm = Vector3.Normalize(Vector3.Cross(b - a, d - a));
            v.Add(a); v.Add(b); v.Add(d);
            n.Add(nrm); n.Add(nrm); n.Add(nrm);
            c.Add(col); c.Add(col); c.Add(col);
            t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1);
        }

        static void Quad(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color32 col)
        {
            Tri(v, n, c, t, a, b, d, col);
            Tri(v, n, c, t, a, d, e, col);
        }
    }
}
