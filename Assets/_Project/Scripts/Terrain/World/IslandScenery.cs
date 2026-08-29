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
        /// A tree is 5-9 m. That is the ruler, and it only works if it is
        /// honest -- these must NOT scale with the island.
        const float TreeMinH = 5.2f, TreeMaxH = 8.6f;

        public static GameObject Build(Transform parent, Vector3 centre, float meanR,
            System.Func<float, float, float> height, TerrainSettings terrain,
            System.Func<float, float> radiusAt, int seed)
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
            float treeLine = Mathf.Max(18f, peak * 0.62f);
            float sand = terrain != null ? terrain.sandHeight : 3.2f;

            // Jittered grid, so the spacing reads as a wood rather than as a
            // scatter with clumps and bald patches.
            float step = Mathf.Clamp(meanR * 0.035f, 9f, 22f);
            int trees = 0, rocks = 0;
            const int MaxTrees = 520, MaxRocks = 140;

            for (float z = -meanR; z <= meanR && trees < MaxTrees; z += step)
            {
                for (float x = -meanR; x <= meanR && trees < MaxTrees; x += step)
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

                    if (h > treeLine || slope > 0.62f)
                    {
                        // Above the trees, or too steep for them: scree.
                        if (rocks < MaxRocks && rng.NextDouble() < 0.16)
                        {
                            AddBoulder(verts, norms, cols, tris, new Vector3(wx, h, wz), rng);
                            rocks++;
                        }
                        continue;
                    }
                    // Thin the wood out near the tree line so it has an edge
                    // instead of stopping on a contour like a mown lawn.
                    float t = Mathf.InverseLerp(treeLine, treeLine * 0.72f, h);
                    if (rng.NextDouble() > Mathf.Clamp01(0.25f + t * 0.75f)) continue;

                    AddTree(verts, norms, cols, tris, new Vector3(wx, h, wz), rng);
                    trees++;
                }
            }

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

        static void AddTree(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 at, System.Random rng)
        {
            float h = Mathf.Lerp(TreeMinH, TreeMaxH, (float)rng.NextDouble());
            float trunkH = h * 0.34f;
            float trunkR = h * 0.035f;
            float canopyR = h * 0.24f;
            float lean = (float)(rng.NextDouble() - 0.5) * 0.12f;
            var leanV = new Vector3(lean, 0f, (float)(rng.NextDouble() - 0.5) * 0.12f);

            var trunk = new Color32(92, 64, 40, 255);
            byte g = (byte)(96 + rng.Next(0, 46));
            var leaf = new Color32((byte)(30 + rng.Next(0, 22)), g, (byte)(38 + rng.Next(0, 18)), 255);

            Prism(v, n, c, t, at, trunkR, trunkH, leanV, trunk);
            // Two stacked cones read as a conifer from any angle and cost 12
            // triangles; a sphere canopy costs 500 and reads as a lollipop.
            Cone(v, n, c, t, at + new Vector3(0f, trunkH, 0f) + leanV * 0.5f,
                canopyR, h * 0.42f, leanV, leaf);
            Cone(v, n, c, t, at + new Vector3(0f, trunkH + h * 0.26f, 0f) + leanV * 0.8f,
                canopyR * 0.68f, h * 0.40f, leanV, leaf);
        }

        static void AddBoulder(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 at, System.Random rng)
        {
            float s = 0.9f + (float)rng.NextDouble() * 2.4f;
            byte grey = (byte)(96 + rng.Next(0, 40));
            var col = new Color32(grey, (byte)(grey + 4), (byte)(grey + 10), 255);
            Cone(v, n, c, t, at + new Vector3(0f, -s * 0.25f, 0f), s, s * 1.5f,
                new Vector3((float)(rng.NextDouble() - 0.5) * 0.5f, 0f,
                            (float)(rng.NextDouble() - 0.5) * 0.5f), col, 5);
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
