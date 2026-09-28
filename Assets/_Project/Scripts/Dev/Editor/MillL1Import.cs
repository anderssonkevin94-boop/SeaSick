using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using SeaSick.World;

namespace SeaSick.Dev
{
    /// **The approved level 1 lumber mill (2026-09-28), `art-staging/lumber-mill-c-v1`
    /// concept C "Timber Fan".**
    ///
    /// Puts the full state kit (`Art/MillL1/Models/lumber-mill-state-kit.fbx`,
    /// structure + six logs + twelve boards + every bench variant; the
    /// `-empty` export is NOT installed beside it, it would double the
    /// structure) behind the existing `Resources/Settlement/sawmill.prefab`.
    /// The wrapper keeps its path and GUID, so `BuildPlans.Sawmill` (and the
    /// flour `Mill`, which still wears this model as placeholder art) and
    /// every saved camp pick the new art up with no code change.
    ///
    /// - Same material path as the level 1 wall (`WallL1Import`): the kit
    ///   lives OUTSIDE `Art/AstraPlaytest`, so `AstraPlaytestImport`'s
    ///   blanket remap never sees it, and its three materials are remapped BY
    ///   NAME onto `SeaSick/Environment Toon Textured` (colour map * the
    ///   FBX's `GameColor` vertex colours * `_BaseColor`), reusing the wall's
    ///   two 512 px tiles. The FBX's names carry Blender suffixes
    ///   (`SS_LumberL1_Wood.006`), so they are enumerated after import and
    ///   matched on their stem.
    /// - The frame is measured from the kit's own markers, not assumed: up
    ///   is the normal of the ground marks, signed toward the 0.16 m
    ///   `Worker_Stand`; front (+Z) is `Bench_Anchor` -> `Entrance_Anchor`; the
    ///   input side must land on +X (Astra's convention, and the one
    ///   `CampWorker.InputSpot` falls back on). Every marker is then checked
    ///   against the README's coordinates, and the bounds against the
    ///   7.56 x 5.85 m footprint and 3.84 m ridge. Any miss throws.
    /// - The inventory/state objects stay separate, parented as authored
    ///   (`StationStockView` scopes its slots to `Input_Container` /
    ///   `Output_Container` / `Bench_Anchor`), and are saved HIDDEN: an empty
    ///   mill is what a blueprint or a mill with no ledger row shows.
    /// - A `Worker_Pad` (`WorkerPad`) is measured off `Mill_WorkPlatform`'s
    ///   top faces, so the sawyer stands on the boards and walks on and off
    ///   by `Worker_Approach`.
    ///
    /// Idempotent: run it again after re-exporting the FBX.
    public static class MillL1Import
    {
        const string Root = "Assets/_Project/Art/MillL1";
        const string ModelPath = Root + "/Models/lumber-mill-state-kit.fbx";
        const string MaterialDir = Root + "/Materials";
        const string TextureDir = "Assets/_Project/Art/WallL1/Textures";
        const string Wrapper = "Assets/_Project/Resources/Settlement/sawmill.prefab";
        const string ShaderName = "SeaSick/Environment Toon Textured";

        const float FootX = 7.56f, FootZ = 5.85f, Ridge = 3.84f;
        const float MarkTolerance = 0.02f;

        const string Wood = "SS_LumberL1_Wood";
        const string Hemp = "SS_LumberL1_Hemp";
        const string Plain = "SS_LumberL1_Canvas_Endgrain_Stone";

        /// README marker coordinates, Blender (x, y, z) -> wrapper
        /// (-x, z, -y): front -Y becomes +Z, and the input side (Blender -X)
        /// becomes +X.
        static readonly Dictionary<string, Vector3> Marks = new Dictionary<string, Vector3>
        {
            { "Worker_Stand", new Vector3(0.04f, 0.16f, -0.66f) },
            { "Worker_Approach", new Vector3(0.50f, 0f, -1.80f) },
            { "Input_Pickup", new Vector3(2.30f, 0f, 1.68f) },
            { "Output_Dropoff", new Vector3(-2.20f, 0f, 1.58f) },
            { "Entrance_Anchor", new Vector3(0f, 0f, 1.52f) },
            { "Bench_Anchor", new Vector3(0f, 0f, -0.04f) },
        };

        /// Every name runtime code or the contract relies on, by stem.
        static IEnumerable<string> Required()
        {
            yield return "Input_Container";
            for (int i = 1; i <= 6; i++) yield return $"Input_Log_{i:00}";
            yield return "Output_Container";
            for (int i = 1; i <= 12; i++) yield return $"Output_Plank_{i:00}";
            yield return "Bench_Loaded";
            yield return "Bench_Cutting";
            yield return "Bench_Finished";
            for (int i = 1; i <= 3; i++) yield return $"Bench_Result_{i:00}";
            yield return "Mallet_Tool";
            yield return "Mill_WorkPlatform";
            foreach (var m in Marks.Keys) yield return m;
        }

        [MenuItem("SeaSick/Art/Import level 1 lumber mill (Timber Fan)")]
        public static string Execute()
        {
            var log = new StringBuilder();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var mats = Materials(log);
            Import(mats, log);
            var frame = MeasureFrame(log);
            Wrap(frame, log);
            AssetDatabase.SaveAssets();
            Debug.Log("[MillL1] " + log);
            return log.ToString();
        }

        static string Stem(string name) => Regex.Replace(name, @"\.\d+$", "");

        // --- materials ---------------------------------------------------------

        static Dictionary<string, Material> Materials(StringBuilder log)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new System.Exception("Missing shader " + ShaderName);
            // The wall's tiles, already imported by `WallL1Import` (sRGB,
            // repeat, mips, 512, compressed) -- shared, not copied.
            var wood = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + "/wood-tile-512.png");
            var rope = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + "/rope-tile-512.png");
            if (wood == null || rope == null)
                throw new System.Exception("wall tiles missing under " + TextureDir + " (run WallL1Import first)");
            if (!AssetDatabase.IsValidFolder(MaterialDir))
                AssetDatabase.CreateFolder(Root, "Materials");
            var set = new Dictionary<string, Material>
            {
                { Wood, Mat(Wood, shader, wood) },
                { Hemp, Mat(Hemp, shader, rope) },
                // Canvas, cut ends, stone and the painted sign: GameColor only.
                { Plain, Mat(Plain, shader, null) },
            };
            log.AppendLine("materials: " + string.Join(", ", set.Keys) + " on " + ShaderName);
            return set;
        }

        static Material Mat(string name, Shader shader, Texture2D map)
        {
            string path = MaterialDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_BaseMap", map);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Ambient", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // --- the model -----------------------------------------------------------

        static void Import(Dictionary<string, Material> mats, StringBuilder log)
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var mi = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (mi == null) throw new System.Exception("no model at " + ModelPath);
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importNormals = ModelImporterNormals.Import;  // flat authored normals
            mi.importTangents = ModelImporterTangents.None;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.addCollider = false;
            mi.generateSecondaryUV = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
            mi.preserveHierarchy = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            // Drop any earlier remaps so the FBX's own (suffixed) names show.
            foreach (var key in mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).ToList())
                mi.RemoveRemap(key);
            mi.SaveAndReimport();

            var fbxNames = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>()
                .Select(m => m.name).Distinct().ToList();
            if (fbxNames.Count == 0) throw new System.Exception(ModelPath + ": no embedded materials to map");
            var seen = new HashSet<string>();
            foreach (var n in fbxNames)
            {
                string stem = Stem(n);
                if (!mats.TryGetValue(stem, out var target))
                    throw new System.Exception($"{ModelPath}: FBX material '{n}' matches none of {string.Join(", ", mats.Keys)}");
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), target);
                seen.Add(stem);
                log.AppendLine($"remap {n} -> {target.name}");
            }
            foreach (var k in mats.Keys)
                if (!seen.Contains(k)) throw new System.Exception($"{ModelPath}: no FBX material with stem {k}");
            mi.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m == null || !mats.ContainsValue(m))
                        throw new System.Exception($"{ModelPath}: {r.name} has an unmapped material {(m ? m.name : "null")}");
        }

        // --- the frame -------------------------------------------------------------

        static Transform Find(Transform root, string stem)
        {
            var hits = root.GetComponentsInChildren<Transform>(true).Where(t => Stem(t.name) == stem).ToList();
            if (hits.Count == 0) throw new System.Exception("kit has no " + stem);
            if (hits.Count > 1) throw new System.Exception($"kit has {hits.Count} objects named {stem}");
            return hits[0];
        }

        /// The rotation taking the imported model's axes to the wrapper
        /// frame, from the markers alone.
        static Quaternion MeasureFrame(StringBuilder log)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            try
            {
                // Native rotation kept: `Wrap` applies `frame` on top of it.
                go.transform.position = Vector3.zero;
                var t = go.transform;
                Vector3 pick = Find(t, "Input_Pickup").position;
                Vector3 drop = Find(t, "Output_Dropoff").position;
                Vector3 gate = Find(t, "Worker_Approach").position;
                Vector3 stand = Find(t, "Worker_Stand").position;
                Vector3 bench = Find(t, "Bench_Anchor").position;
                Vector3 door = Find(t, "Entrance_Anchor").position;

                // Up: the plane of three ground marks, toward the raised stand.
                Vector3 up = Vector3.Cross(drop - pick, gate - pick).normalized;
                if (Vector3.Dot(up, stand - bench) < 0f) up = -up;
                // Front along bench -> entrance: both sit on the kit's centre
                // line (Blender x = 0). The rear gate is 0.5 m off it, so a
                // gate -> entrance front turned the whole mill ~8.6 degrees.
                Vector3 front = Vector3.ProjectOnPlane(door - bench, up).normalized;
                if (front.sqrMagnitude < 0.5f) throw new System.Exception("bench and entrance coincide: no front");
                var q = Quaternion.Inverse(Quaternion.LookRotation(front, up));
                float inputX = (q * (pick - door)).x;
                log.AppendLine($"frame: up {up:F3} front {front:F3} -> input side at wrapper x {inputX:F2}");
                if (inputX <= 0f)
                    throw new System.Exception("input side lands on -X: the FBX is mirrored against the wrapper frame");
                return q;
            }
            finally { Object.DestroyImmediate(go); }
        }

        // --- the wrapper -----------------------------------------------------------

        static void Wrap(Quaternion frame, StringBuilder log)
        {
            string guid = AssetDatabase.AssetPathToGUID(Wrapper);
            if (string.IsNullOrEmpty(guid)) throw new System.Exception("missing wrapper " + Wrapper);

            var contents = PrefabUtility.LoadPrefabContents(Wrapper);
            try
            {
                var root = contents.transform;
                if (root.localScale != Vector3.one || root.localRotation != Quaternion.identity)
                    throw new System.Exception("sawmill wrapper root is not at unit scale/identity");
                // The old visual goes; the root and its own components stay.
                for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);

                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
                model.name = "LumberMill_L1";
                model.transform.SetParent(root, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = frame * model.transform.localRotation;

                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }

                // Every name, exactly once.
                foreach (var n in Required()) Find(root, n);

                // Markers where the README puts them.
                foreach (var kv in Marks)
                {
                    Vector3 at = root.InverseTransformPoint(Find(root, kv.Key).position);
                    log.AppendLine($"{kv.Key}: {at:F3} (want {kv.Value:F2})");
                    if ((at - kv.Value).magnitude > MarkTolerance)
                        throw new System.Exception($"{kv.Key} at {at:F3}, README says {kv.Value:F2}: frame or scale is off");
                }

                // Slot groups: what StationStockView will find.
                int logs = Slots(Find(root, "Input_Container"), "Input_");
                int planks = Slots(Find(root, "Output_Container"), "Output_");
                int results = Slots(Find(root, "Bench_Finished"), "Bench_Result_");
                log.AppendLine($"slots: {logs} logs, {planks} planks, {results} bench results");
                if (logs != 6 || planks != 12 || results != 3)
                    throw new System.Exception($"slot counts {logs}/{planks}/{results}, want 6/12/3");
                if (Find(root, "Bench_Finished").parent != Find(root, "Bench_Anchor"))
                    throw new System.Exception("Bench_Finished is not under Bench_Anchor");

                // Bounds of every variant inside the plan's footprint/ridge,
                // centred on the root.
                var b = Bounds(root, _ => true);
                log.AppendLine($"bounds (all variants) {b.min:F2}..{b.max:F2}");
                if (Mathf.Abs(b.min.x) > FootX * 0.5f || Mathf.Abs(b.max.x) > FootX * 0.5f
                    || Mathf.Abs(b.min.z) > FootZ * 0.5f || Mathf.Abs(b.max.z) > FootZ * 0.5f)
                    throw new System.Exception($"bounds {b.min:F2}..{b.max:F2} leave the {FootX} x {FootZ} footprint");
                if (b.max.y > Ridge) throw new System.Exception($"top {b.max.y:F2} m is over the {Ridge} m ridge");
                if (b.min.y < -0.2f || b.min.y > 0.05f)
                    throw new System.Exception($"lowest point {b.min.y:F2} m: the root is not on the ground");

                int all = Tris(root, _ => true);
                int empty = Tris(root, t => !IsStock(t) && !IsBenchVariant(t));
                int full = Tris(root, t => !IsBenchVariant(t) || Under(t, "Bench_Loaded"));
                log.AppendLine($"tris: empty {empty} (want 1706), full+loaded {full} (want 2238), all variants {all} (want 2434)"
                    + (empty != 1706 || full != 2238 || all != 2434 ? "  !! MISMATCH" : ""));

                Pad(root, log);

                // Saved as an EMPTY mill: stock and bench states hidden
                // (`StationStockView` shows the ledger's truth once raised).
                // The three result boards stay active under their hidden
                // parent; the view counts them itself.
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    string s = Stem(t.name);
                    if (IsSlotName(s) || s == "Bench_Loaded" || s == "Bench_Cutting" || s == "Bench_Finished" || s == "Mallet_Tool")
                        t.gameObject.SetActive(false);
                }

                PrefabUtility.SaveAsPrefabAsset(contents, Wrapper);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            if (AssetDatabase.AssetPathToGUID(Wrapper) != guid)
                throw new System.Exception("sawmill wrapper GUID changed");
            log.AppendLine("wrapper saved, GUID kept " + guid);
        }

        /// The raised pad's walking top, off `Mill_WorkPlatform`'s highest
        /// faces (the 0.08 m front step is below them and left out), as a
        /// `Worker_Pad` child the runtime grounds bodies on.
        static void Pad(Transform root, StringBuilder log)
        {
            var mf = Find(root, "Mill_WorkPlatform").GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) throw new System.Exception("Mill_WorkPlatform has no mesh");
            var pts = mf.sharedMesh.vertices.Select(v => root.InverseTransformPoint(mf.transform.TransformPoint(v))).ToList();
            float top = pts.Max(p => p.y);
            var topPts = pts.Where(p => p.y > top - 0.03f).ToList();
            float x0 = topPts.Min(p => p.x), x1 = topPts.Max(p => p.x);
            float z0 = topPts.Min(p => p.z), z1 = topPts.Max(p => p.z);

            var stand = Find(root, "Worker_Stand");
            var gate = Find(root, "Worker_Approach");
            Vector3 s = root.InverseTransformPoint(stand.position);
            log.AppendLine($"pad: top {top:F3} x {x0:F2}..{x1:F2} z {z0:F2}..{z1:F2}");
            if (Mathf.Abs(top - s.y) > MarkTolerance)
                throw new System.Exception($"pad top {top:F3} but Worker_Stand at {s.y:F3}");
            if (s.x < x0 || s.x > x1 || s.z < z0 || s.z > z1)
                throw new System.Exception("Worker_Stand is off the pad");

            var go = new GameObject("Worker_Pad");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3((x0 + x1) * 0.5f, top, (z0 + z1) * 0.5f);
            go.transform.localRotation = Quaternion.identity;
            go.AddComponent<WorkerPad>().Configure(new Vector2(x1 - x0, z1 - z0), stand, gate);
        }

        static bool IsSlotName(string s)
            => Regex.IsMatch(s, @"^(Input_Log|Output_Plank)_\d+$");

        static bool IsStock(Transform t)
        {
            for (var p = t; p != null; p = p.parent) if (IsSlotName(Stem(p.name))) return true;
            return false;
        }

        static bool IsBenchVariant(Transform t)
            => Under(t, "Bench_Loaded") || Under(t, "Bench_Cutting") || Under(t, "Bench_Finished") || Under(t, "Mallet_Tool");

        static bool Under(Transform t, string stem)
        {
            for (var p = t; p != null; p = p.parent) if (Stem(p.name) == stem) return true;
            return false;
        }

        static int Slots(Transform group, string prefix)
            => group.GetComponentsInChildren<Transform>(true)
                .Count(t => t != group && Regex.IsMatch(Stem(t.name), "^" + prefix + @".*\d+$"));

        static Bounds Bounds(Transform root, System.Func<Transform, bool> keep)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!keep(mf.transform) || mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = root.InverseTransformPoint(mf.transform.TransformPoint(v));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static int Tris(Transform root, System.Func<Transform, bool> keep)
            => root.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && keep(f.transform))
                .Sum(f => f.sharedMesh.triangles.Length / 3);
    }
}
