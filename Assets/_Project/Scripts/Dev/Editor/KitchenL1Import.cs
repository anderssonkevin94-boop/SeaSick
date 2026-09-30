using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **The approved level 1 kitchen (V6, 2026-10-01),
    /// `art-staging/kitchen-grill-lvl1-v6`: the cook line in front of the
    /// cook** -- a cauldron hearth and a stone grill side by side, straight
    /// ahead of `Worker_Stand`, prep board at his right hand.
    ///
    /// Puts the full state kit (`Art/KitchenL1/Models/kitchen-state-kit.fbx`;
    /// the `-empty` export is NOT installed beside it, it would double the
    /// structure) behind the existing `Resources/Settlement/kitchen_astra.prefab`.
    /// The wrapper keeps its path and GUID, so `BuildPlans.Kitchen` and
    /// every saved camp pick the new art up with no code change.
    ///
    /// - Same material path as the level 1 mill (`MillL1Import`): outside
    ///   `Art/AstraPlaytest` (so `AstraPlaytestImport`'s blanket remap never
    ///   sees it, and that importer builds no kitchen), three materials
    ///   remapped BY STEM onto `SeaSick/Environment Toon Textured`, reusing
    ///   the wall's two 512 px tiles.
    /// - The frame is measured from the kit's markers: up is the normal of
    ///   the ground marks; front (+Z) is `Output_Anchor` -> `Output_Dropoff`
    ///   (both on Blender x = -1.33, so exactly the kit's -Y). Every marker is
    ///   then checked against the README (Blender (x, y, z) -> wrapper
    ///   (-x, z, -y)), and the bounds against the 6.26 x 6.12 m footprint and
    ///   4.18 m height. Any miss throws.
    /// - Stock and state objects stay separate and are saved HIDDEN
    ///   (`StationStockView` shows the ledger's truth): `Input_Food_01..04`,
    ///   `Output_Meal_01..06`, `Work_Preparing` / `Work_Cooking` (with its
    ///   `Cauldron_Steam` + `Cauldron_Glow` children) / `Work_Finished`,
    ///   `Spoon_Tool`, and `Grill_Cooking` (`Grill_Fish`, `Grill_Meat`,
    ///   `Grill_Glow`). The view drives `Grill_Cooking` from the Grill spot
    ///   and `Work_Cooking` from the Cauldron spot.
    /// - Ground-level cook: no `Worker_Pad`. `CampWorker` faces him at
    ///   `Cook_Line_Anchor` (between the two fires).
    ///
    /// Idempotent: run it again after re-exporting the FBX.
    public static class KitchenL1Import
    {
        const string Root = "Assets/_Project/Art/KitchenL1";
        const string ModelPath = Root + "/Models/kitchen-state-kit.fbx";
        const string MaterialDir = Root + "/Materials";
        const string TextureDir = "Assets/_Project/Art/WallL1/Textures";
        const string Wrapper = "Assets/_Project/Resources/Settlement/kitchen_astra.prefab";
        const string ShaderName = "SeaSick/Environment Toon Textured";

        const float FootX = 6.26f, FootZ = 6.12f, Ridge = 4.18f;
        const float MarkTolerance = 0.02f;

        const string Wood = "SS_KitchenL1_Wood";
        const string Hemp = "SS_KitchenL1_Hemp";
        const string Plain = "SS_KitchenL1_Clay_Canvas_Food";

        /// README marker coordinates, Blender (x, y, z) -> wrapper (-x, z, -y).
        static readonly Dictionary<string, Vector3> Marks = new Dictionary<string, Vector3>
        {
            { "Worker_Stand", B(0.25f, 0.32f, 0f) },
            { "Worker_Approach", B(0.05f, 1.20f, 0f) },
            { "Prep_Anchor", B(-0.475f, 0.24f, 1.075f) },
            { "Cook_Anchor", B(-0.15f, -0.40f, 1.13f) },
            { "Steam_Anchor", B(-0.15f, -0.40f, 1.27f) },
            { "Fire_Anchor", B(-0.15f, -0.44f, 0.28f) },
            { "Grill_Anchor", B(0.70f, -0.40f, 0.90f) },
            { "Grill_Fire_Anchor", B(0.70f, -0.54f, 0.20f) },
            { "Cook_Line_Anchor", B(0.275f, -0.40f, 0.95f) },
            { "Input_Anchor", B(-1.54f, 0.57f, 1.03f) },
            { "Input_Pickup", B(-2.54f, 0.65f, 0f) },
            { "Output_Anchor", B(-1.33f, -0.76f, 0.90f) },
            { "Output_Dropoff", B(-1.33f, -1.80f, 0f) },
        };

        static Vector3 B(float x, float y, float z) => new Vector3(-x, z, -y);

        /// Stock and state objects: saved hidden, the view toggles them.
        static readonly string[] States =
            { "Work_Preparing", "Work_Cooking", "Work_Finished", "Spoon_Tool", "Grill_Cooking" };

        /// Every name runtime code or the contract relies on, by stem.
        static IEnumerable<string> Required()
        {
            for (int i = 1; i <= 4; i++) yield return $"Input_Food_{i:00}";
            for (int i = 1; i <= 6; i++) yield return $"Output_Meal_{i:00}";
            foreach (var s in States) yield return s;
            yield return "Cauldron_Steam";
            yield return "Cauldron_Glow";
            yield return "Grill_Fish";
            yield return "Grill_Meat";
            yield return "Grill_Glow";
            foreach (var m in Marks.Keys) yield return m;
        }

        [MenuItem("SeaSick/Art/Import level 1 kitchen (V6 cook line)")]
        public static string Execute()
        {
            var log = new StringBuilder();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var mats = Materials(log);
            Import(mats, log);
            var frame = MeasureFrame(log);
            Wrap(frame, log);
            AssetDatabase.SaveAssets();
            Debug.Log("[KitchenL1] " + log);
            return log.ToString();
        }

        /// The two halves of `Execute`, for callers with a short timeout
        /// (the FBX reimport alone can take several seconds).
        public static string ImportOnly()
        {
            var log = new StringBuilder();
            Import(Materials(log), log);
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        public static string WrapOnly()
        {
            var log = new StringBuilder();
            Wrap(MeasureFrame(log), log);
            AssetDatabase.SaveAssets();
            Debug.Log("[KitchenL1] " + log);
            return log.ToString();
        }

        static string Stem(string name) => Regex.Replace(name, @"\.\d+$", "");

        // --- materials ---------------------------------------------------------

        static Dictionary<string, Material> Materials(StringBuilder log)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new System.Exception("Missing shader " + ShaderName);
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
                // Clay, canvas, stone and food: GameColor only.
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

        static Quaternion MeasureFrame(StringBuilder log)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            try
            {
                go.transform.position = Vector3.zero;
                var t = go.transform;
                Vector3 pick = Find(t, "Input_Pickup").position;
                Vector3 drop = Find(t, "Output_Dropoff").position;
                Vector3 gate = Find(t, "Worker_Approach").position;
                Vector3 line = Find(t, "Cook_Line_Anchor").position;
                Vector3 outAnchor = Find(t, "Output_Anchor").position;

                Vector3 up = Vector3.Cross(drop - pick, gate - pick).normalized;
                if (Vector3.Dot(up, line - gate) < 0f) up = -up;
                Vector3 front = Vector3.ProjectOnPlane(drop - outAnchor, up).normalized;
                if (front.sqrMagnitude < 0.5f) throw new System.Exception("output anchor and dropoff coincide: no front");
                var q = Quaternion.Inverse(Quaternion.LookRotation(front, up));
                log.AppendLine($"frame: up {up:F3} front {front:F3}");
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
                    throw new System.Exception("kitchen wrapper root is not at unit scale/identity");
                for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);

                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
                model.name = "Kitchen_L1";
                model.transform.SetParent(root, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = frame * model.transform.localRotation;

                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }

                foreach (var n in Required()) Find(root, n);
                if (root.GetComponentsInChildren<Transform>(true).Any(t => Stem(t.name) == "Grill_Stand"))
                    throw new System.Exception("Grill_Stand is back: V6 dropped it");

                foreach (var kv in Marks)
                {
                    Vector3 at = root.InverseTransformPoint(Find(root, kv.Key).position);
                    log.AppendLine($"{kv.Key}: {at:F3} (want {kv.Value:F2})");
                    if ((at - kv.Value).magnitude > MarkTolerance)
                        throw new System.Exception($"{kv.Key} at {at:F3}, README says {kv.Value:F2}: frame or scale is off");
                }

                if (Find(root, "Cauldron_Steam").parent != Find(root, "Work_Cooking"))
                    throw new System.Exception("Cauldron_Steam is not under Work_Cooking");
                foreach (var g in new[] { "Grill_Fish", "Grill_Meat", "Grill_Glow" })
                    if (Find(root, g).parent != Find(root, "Grill_Cooking"))
                        throw new System.Exception(g + " is not under Grill_Cooking");

                var b = Bounds(root, _ => true);
                log.AppendLine($"bounds (all variants) {b.min:F2}..{b.max:F2}");
                if (Mathf.Abs(b.min.x) > FootX * 0.5f || Mathf.Abs(b.max.x) > FootX * 0.5f
                    || Mathf.Abs(b.min.z) > FootZ * 0.5f || Mathf.Abs(b.max.z) > FootZ * 0.5f)
                    throw new System.Exception($"bounds {b.min:F2}..{b.max:F2} leave the {FootX} x {FootZ} footprint");
                if (b.max.y > Ridge) throw new System.Exception($"top {b.max.y:F2} m is over the {Ridge} m height");
                if (b.min.y < -0.2f || b.min.y > 0.05f)
                    throw new System.Exception($"lowest point {b.min.y:F2} m: the root is not on the ground");

                int all = Tris(root, _ => true);
                int empty = Tris(root, t => !IsStockOrState(t));
                log.AppendLine($"tris: empty {empty} (want 2402), all states {all} (want 4077)"
                    + (empty != 2402 || all != 4077 ? "  !! MISMATCH" : ""));

                // Saved EMPTY and idle: StationStockView shows the ledger's truth.
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (IsSlotName(Stem(t.name)) || States.Contains(Stem(t.name)))
                        t.gameObject.SetActive(false);

                PrefabUtility.SaveAsPrefabAsset(contents, Wrapper);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            if (AssetDatabase.AssetPathToGUID(Wrapper) != guid)
                throw new System.Exception("kitchen wrapper GUID changed");
            log.AppendLine("wrapper saved, GUID kept " + guid);
        }

        static bool IsSlotName(string s) => Regex.IsMatch(s, @"^(Input_Food|Output_Meal)_\d+$");

        static bool IsStockOrState(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
            {
                string s = Stem(p.name);
                if (IsSlotName(s) || States.Contains(s)) return true;
            }
            return false;
        }

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
