using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **Bakes each kit building's walk-blocking parts into boxes
    /// (2026-10-01).** Kevin: *"buildings don't have a collider and villagers
    /// walk straight through them. give them hitboxes that make sense while
    /// still allowing workers to get to and from their stations / collecting
    /// / drop off points."*
    ///
    /// For each prefab in `Table`, the named solid parts (benches, racks,
    /// hearths, posts, a hut's walls -- never stock, state objects, roofs or
    /// a walking pad) are rasterised from their triangles, below
    /// `BlockHeight` only, onto a 0.2 m grid in the building's own frame;
    /// each connected patch becomes one box. So the mill's two rear posts are
    /// two posts with the gate between them, not one bar across it. The
    /// result is written as code to `World/BuildingSolids.Baked.cs`
    /// (a diffable table keyed by the plan's prefab path) -- NOT onto the
    /// prefabs, so an importer re-run (`MillL1Import`, `KitchenL1Import`, ...)
    /// can never drop it; re-run this after a model changes shape.
    ///
    /// Also checks every access marker (`Worker_Stand`, `Input_Pickup`, ...)
    /// against the boxes and prints how far outside each one is.
    ///
    /// Run: `SeaSick.Dev.BuildingSolidsBake.Run()` (or the menu item).
    public static class BuildingSolidsBake
    {
        const string OutPath = "Assets/_Project/Scripts/World/BuildingSolids.Baked.cs";
        /// Geometry above this (metres over the model's base) never blocks a
        /// walker: roofs, tarps, signs, beams.
        const float BlockHeight = 1.3f;
        const float CoarseCell = 0.25f;
        /// The grid this prefab is baked on (`FineCells` get half).
        static float Cell = CoarseCell;
        /// Prefabs whose worker reaches his stand through a gap narrower
        /// than two coarse cells: the level 2 sawmill's 0.47 m between the
        /// grindstone and the sign post (2026-10-01).
        static readonly HashSet<string> FineCells = new HashSet<string> { "Settlement/sawmill_l2" };

        /// Prefab (Resources path, as `BuildPlan.prefab`) -> the part-name
        /// stems that block. A stem ending in `*` is a prefix.
        static readonly (string prefab, string[] parts)[] Table =
        {
            ("Settlement/sawmill", new[] { "Mill_Workbench", "Mill_InputCradle", "Mill_OutputRack", "Mill_Pillars" }),
            ("Settlement/kitchen_astra", new[] { "Counter_Fitted_Base", "Continuous_Countertop", "Sculpted_Hearth",
                "Hearth_Back", "Grill_Firebox", "Grill_Stone_Slab", "Serving_Terrace_Supports", "Serving_Front_Terrace",
                "Canopy_Frame" }),
            ("Settlement/blacksmith_astra", new[] { "Anvil", "Bellows", "Forge_Hearth", "Quench_Tub", "Ore_Bin",
                "Tool_Rack", "Workbench", "Frame" }),
            ("Settlement/fletcher_astra", new[] { "Workbench", "Shaving_Horse", "Arrow_Stand", "Timber_Cradle",
                "Bow_Rack", "Frame" }),
            ("Settlement/quarry", new[] { "Cutting_Bench", "Input_Bay", "Lifting_Frame", "Masonry", "Output_Pallet" }),
            ("Settlement/fishinghut_astra", new[] { "Work_Bench", "Net_Rack", "Output_Crate", "Windbreak", "Gear", "Frame" }),
            ("Settlement/storage_astra", new[] { "Front_Racks", "Platform", "Shelving", "Frame" }),
            ("Settlement/hut_astra", new[] { "Lower_Walls" }),
            ("Settlement/farm_astra", new[] { "Bed_*_Soil", "Tool_Frame", "Water_Butt", "Seed_Box", "Harvest_Basket" }),
            ("Settlement/campfire_astra", new[] { "Hearth", "Fuel_Cradle", "Cooking_Frame", "Seat" }),
            ("Settlement/watchtower_astra", new[] { "TowerL1_Posts", "TowerL1_Walls" }),
            // Level 2 models (2026-10-01), swapped in by `BuildingFactory.ShowLevel`.
            // The level 2 tower's ground parts are level 1's within 5 cm
            // (README: same enclosure faces, legs 0.48 m at +-0.95): it takes
            // level 1's box (`SameAs`), so its ladder foot stays outside it.
            ("Settlement/watchtower_l2", new[] { "TowerL2_Posts", "TowerL2_Walls" }),
            ("Settlement/sawmill_l2", new[] { "Mill2_SawTable", "Mill2_CrankStand", "Mill_InputCradle", "Mill_OutputRack",
                "Mill2_Frame", "Mill2_Piers", "Mill2_BackWall", "Mill2_SignPost" }),
            // Not the grindstone: a small prop on the sawyer's way in (the
            // 0.47 m gap past it is narrower than a baked box can show).
        };

        /// A prefab that takes another's boxes rather than its own bake (its
        /// markers are still checked against them).
        static readonly Dictionary<string, string> SameAs = new Dictionary<string, string>
            { { "Settlement/watchtower_l2", "Settlement/watchtower_astra" } };

        /// Shells: one box over the whole low footprint, not the parts.
        static readonly HashSet<string> Shells = new HashSet<string>
            { "Settlement/hut_astra", "Settlement/watchtower_astra", "Settlement/storage_astra", "Settlement/watchtower_l2" };

        static readonly string[] AccessStems =
            { "Input_Pickup", "Output_Dropoff", "Worker_Stand", "Worker_Approach", "Entry", "Entrance_Anchor" };

        [MenuItem("SeaSick/Dev/Bake Building Solids")]
        public static string Run()
        {
            var code = new StringBuilder();
            var report = new StringBuilder();
            code.Append("// GENERATED by SeaSick.Dev.BuildingSolidsBake.Run() -- do not edit by hand.\n");
            code.Append("// Boxes in each model's own frame: centre x, centre z, half x, half z.\n");
            code.Append("using System.Collections.Generic;\n\nnamespace SeaSick.World\n{\n");
            code.Append("    public static partial class BuildingSolids\n    {\n");
            code.Append("        static readonly Dictionary<string, float[]> Baked = new Dictionary<string, float[]>\n        {\n");

            var done = new Dictionary<string, List<Vector4>>();
            foreach (var (prefab, parts) in Table)
            {
                var asset = Resources.Load<GameObject>(prefab);
                if (asset == null) { report.Append($"MISSING {prefab}\n"); continue; }
                // Exactly as `BuildingFactory.Dress` puts it on: under the
                // building root at zero, identity rotation, own scale kept.
                var holder = new GameObject("SolidsBakeHolder");
                var model = Object.Instantiate(asset, holder.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                try
                {
                    Cell = FineCells.Contains(prefab) ? CoarseCell * 0.5f : CoarseCell;
                    var boxes = BakeOne(holder.transform, model, parts, Shells.Contains(prefab), report);
                    Cell = CoarseCell;
                    if (SameAs.TryGetValue(prefab, out var same) && done.TryGetValue(same, out var theirs))
                    {
                        report.Append($"{prefab}: takes {same}'s boxes (own bake: {boxes.Count})\n");
                        boxes = theirs;
                    }
                    done[prefab] = boxes;
                    code.Append($"            {{ \"{prefab}\", new float[] {{ ");
                    foreach (var b in boxes)
                        code.Append(F(b.x)).Append(", ").Append(F(b.y)).Append(", ")
                            .Append(F(b.z)).Append(", ").Append(F(b.w)).Append(",  ");
                    code.Append("} },\n");
                    report.Append($"{prefab}: {boxes.Count} boxes\n");
                    foreach (var b in boxes)
                        report.Append($"   box c=({b.x:0.00},{b.y:0.00}) h=({b.z:0.00},{b.w:0.00})\n");
                    // Every access marker's clearance from the nearest box.
                    foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    {
                        string stem = Stem(t.name);
                        if (System.Array.IndexOf(AccessStems, stem) < 0) continue;
                        Vector3 p = holder.transform.InverseTransformPoint(t.position);
                        float best = float.MaxValue;
                        foreach (var b in boxes) best = Mathf.Min(best, SignedDist(p.x, p.z, b));
                        report.Append($"   {stem} ({p.x:0.00},{p.z:0.00}) clear {best:0.00} m{(best < 0.2f ? "  <-- INSIDE CLEARANCE" : "")}\n");
                    }
                }
                finally { Object.DestroyImmediate(holder); }
            }

            code.Append("        };\n    }\n}\n");
            System.IO.File.WriteAllText(OutPath, code.ToString());
            AssetDatabase.ImportAsset(OutPath);
            System.IO.File.WriteAllText("Temp/solids-bake.txt", report.ToString());
            Debug.Log("[BuildingSolidsBake]\n" + report);
            return report.ToString();
        }

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture) + "f";

        static string Stem(string name)
        {
            int dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        static bool Matches(string name, string[] parts)
        {
            string stem = Stem(name);
            foreach (var p in parts)
            {
                int star = p.IndexOf('*');
                if (star < 0) { if (stem == p) return true; continue; }
                string pre = p.Substring(0, star), post = p.Substring(star + 1);
                if (stem.Length >= pre.Length + post.Length && stem.StartsWith(pre) && stem.EndsWith(post)) return true;
            }
            return false;
        }

        static float SignedDist(float x, float z, Vector4 b)
        {
            float dx = Mathf.Abs(x - b.x) - b.z, dz = Mathf.Abs(z - b.y) - b.w;
            if (dx <= 0f && dz <= 0f) return Mathf.Max(dx, dz);
            return Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dz, 0f) * Mathf.Max(dz, 0f));
        }

        /// One prefab: rasterise the named parts' low triangles, flood the
        /// filled cells into patches, one box per patch.
        static List<Vector4> BakeOne(Transform frame, GameObject model, string[] parts, bool fill, StringBuilder report)
        {
            var filled = new HashSet<long>();
            int used = 0;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !Matches(mf.name, parts)) continue;
                var r = mf.GetComponent<Renderer>();
                if (r == null || !mf.gameObject.activeSelf) continue;
                used++;
                var mesh = mf.sharedMesh;
                var verts = mesh.vertices;
                var tris = mesh.triangles;
                var m = frame.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var w = new Vector3[verts.Length];
                for (int i = 0; i < verts.Length; i++) w[i] = m.MultiplyPoint3x4(verts[i]);
                for (int t = 0; t < tris.Length; t += 3)
                    Raster(w[tris[t]], w[tris[t + 1]], w[tris[t + 2]], filled);
            }
            if (used == 0) report.Append("   (no parts matched)\n");

            var boxes = new List<Vector4>();
            if (fill)
            {
                // A shell (hut, tower, store): the whole low footprint.
                int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
                foreach (var k in filled)
                {
                    int x = (int)(k >> 32), z = (int)(k & 0xffffffff);
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z);
                }
                if (filled.Count > 0) boxes.Add(Rect(minX, minZ, maxX, maxZ));
                return boxes;
            }

            // Greedy rectangle cover of the filled cells: row by row, each
            // uncovered cell grows right as far as it can, then down while
            // the whole span below is filled. Exact -- the gaps between
            // parts stay gaps.
            var keys = new List<long>(filled);
            keys.Sort((a, b) =>
            {
                int za = (int)(a & 0xffffffff), zb = (int)(b & 0xffffffff);
                if (za != zb) return za.CompareTo(zb);
                return ((int)(a >> 32)).CompareTo((int)(b >> 32));
            });
            var covered = new HashSet<long>();
            foreach (var k in keys)
            {
                if (covered.Contains(k)) continue;
                int x0 = (int)(k >> 32), z0 = (int)(k & 0xffffffff);
                int x1 = x0;
                while (filled.Contains(Key(x1 + 1, z0)) && !covered.Contains(Key(x1 + 1, z0))) x1++;
                int z1 = z0;
                while (true)
                {
                    bool ok = true;
                    for (int x = x0; x <= x1 && ok; x++)
                        ok = filled.Contains(Key(x, z1 + 1)) && !covered.Contains(Key(x, z1 + 1));
                    if (!ok) break;
                    z1++;
                }
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++) covered.Add(Key(x, z));
                boxes.Add(Rect(x0, z0, x1, z1));
            }
            return boxes;
        }

        /// Cell index i covers [i*Cell, (i+1)*Cell).
        static Vector4 Rect(int x0, int z0, int x1, int z1)
        {
            float a = x0 * Cell, b = (x1 + 1) * Cell, c = z0 * Cell, d = (z1 + 1) * Cell;
            return new Vector4(0.5f * (a + b), 0.5f * (c + d), 0.5f * (b - a), 0.5f * (d - c));
        }

        static long Key(int x, int z) => ((long)x << 32) | (uint)z;

        static void Mark(Vector3 p, HashSet<long> filled)
        {
            if (p.y > BlockHeight) return;
            filled.Add(Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell)));
        }

        /// Sample the triangle densely enough (under half a cell) that its
        /// low part's footprint has no holes; only points below
        /// `BlockHeight` count.
        static void Raster(Vector3 a, Vector3 b, Vector3 c, HashSet<long> filled)
        {
            if (Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > BlockHeight) return;
            float span = Mathf.Max((a - b).magnitude, Mathf.Max((b - c).magnitude, (c - a).magnitude));
            int n = Mathf.Clamp(Mathf.CeilToInt(span / (Cell * 0.4f)), 1, 80);
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n - i; j++)
                {
                    float u = i / (float)n, v = j / (float)n;
                    Mark(a + (b - a) * u + (c - a) * v, filled);
                }
        }
    }
}
