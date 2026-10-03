using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using SeaSick.World;

namespace SeaSick.Dev
{
    /// **The approved storage-slot containers (Kevin, 2026-10-03),
    /// `art-staging/storage-slots-preview/export` (KIT MODE, see its
    /// CONTRACT.md):** the level 1 store hut, the fire cache, and the one
    /// shared fill kit their slots draw from.
    ///
    /// - **Copies** the three approved FBXs in from the export folder (only
    ///   when the bytes differ, so a re-run does not reimport for nothing):
    ///   `StorageHutL1.fbx` + `FireCache.fbx` -> `Art/StorageL1/Models/`,
    ///   `StorageFillKit.fbx` -> `Resources/Kits/StorageL1/` (so
    ///   `StorageSlotView`'s `Resources.Load("Kits/StorageL1/StorageFillKit")`
    ///   finds it in a build). The `*_AllSteps.fbx` review files are never
    ///   copied: 163k tris a hut.
    /// - **Import settings as `KitchenL1Import`** (the export used kitchen
    ///   V6's exporter kwargs verbatim): file scale, no rig, no animation,
    ///   authored normals, not readable, `preserveHierarchy` so the
    ///   `StorageHutL1` / `FireCache` roots the view keys on survive. Every
    ///   transform is MEASURED, never assumed: the ×100 Blender root / bone
    ///   trap (memory astra-rig-scale-trap) shows up here as an anchor a
    ///   hundred times off its CONTRACT position and throws.
    /// - **Materials remapped BY STEM** (Blender's `.001`/`.002` copies) onto
    ///   `SeaSick/Environment Toon Textured`, the kitchen's recipe:
    ///   `SS_StorageL1_Wood` (wood tile x GameColor), `SS_StorageL1_Hemp`
    ///   (rope tile), `SS_StorageL1_Canvas_Endgrain_Stone` (GameColor only),
    ///   both tiles reused from `Art/WallL1/Textures`. The food children's
    ///   `SS_Food_GameColor*` go to the food kit's own shared
    ///   `Art/Kits/Shared/GameColor.mat` (`FoodKitImport`), so a potato in a
    ///   sack is the same material as a potato in a barrow.
    /// - **Wrappers** (GrainMill pattern: created on the first run, GUID kept
    ///   after): `Resources/Settlement/storage_l1.prefab` (=
    ///   `BuildingFactory.StorageL1Prefab`; `ModelFor` swaps it in for the
    ///   store hut the moment it exists) and
    ///   `Resources/Settlement/firecache_l1.prefab` (=
    ///   `BuildingFactory.FireCachePrefab`, put beside every campfire). The
    ///   frame is measured from the anchors and every anchor + the pickup is
    ///   checked against `export-verification.json`'s positions (Blender
    ///   (x, y, z) -> wrapper (-x, z, -y)); the hut's bounds against the
    ///   Storage plan's 6.46 x 5.14 m footprint and 3.84 m ridge.
    /// - **The hut's `Marker_Pickup` is renamed `Input_Pickup`**, the name
    ///   `CampWorker.StoreSpot` already sends every store drop-off and runner
    ///   pickup to (and `CampPath` gives a lane). The cache's keeps its FBX
    ///   name: nothing walks to a campfire's store spot yet (the pre-hut
    ///   drop-off is still `CampWorker.PileAt`'s ring).
    /// - **The kit is checked against the anchors**: every top-level step is
    ///   in the same frame relative to the kit root as an anchor is relative
    ///   to its building root (so the view's identity placement under the
    ///   anchor reproduces the authored slot), every resource in
    ///   `StorageSlots`' table resolves a step 1..N in the hut and the
    ///   cache, and the anchor counts per family equal `StorageSlots`' rows.
    ///
    /// Run order once the editor is free (each step fits the eval timeout
    /// better than `Run` in one go): `StorageL1Import.CopyOnly()`,
    /// `ImportOnly()`, `WrapOnly()`, then `BuildingSolidsBake.Run()` (the
    /// `storage_l1` row) and `StorageShot.Run("<dir>")`. `Run()` does all
    /// three import steps. Idempotent.
    public static class StorageL1Import
    {
        const string ArtRoot = "Assets/_Project/Art/StorageL1";
        const string ModelDir = ArtRoot + "/Models";
        const string MaterialDir = ArtRoot + "/Materials";
        const string HutPath = ModelDir + "/StorageHutL1.fbx";
        const string CachePath = ModelDir + "/FireCache.fbx";
        const string KitDir = "Assets/_Project/Resources/Kits/StorageL1";
        const string KitPath = KitDir + "/StorageFillKit.fbx";
        const string HutWrapper = "Assets/_Project/Resources/" + BuildingFactory.StorageL1Prefab + ".prefab";
        const string CacheWrapper = "Assets/_Project/Resources/" + BuildingFactory.FireCachePrefab + ".prefab";
        const string TextureDir = "Assets/_Project/Art/WallL1/Textures";
        const string FoodMaterialPath = "Assets/_Project/Art/Kits/Shared/GameColor.mat";
        const string ShaderName = "SeaSick/Environment Toon Textured";

        /// The approved export, relative to the project folder.
        const string SourceRel = "art-staging/storage-slots-preview/export";

        const string Wood = "SS_StorageL1_Wood";
        const string Hemp = "SS_StorageL1_Hemp";
        const string Plain = "SS_StorageL1_Canvas_Endgrain_Stone";
        const string Food = "SS_Food_GameColor";

        /// The Storage plan's plot and ridge (`BuildPlans.Storage`); the hut
        /// must fit. Its structure is 6.46 x 5.02 x 3.70 m.
        const float FootX = 6.46f, FootZ = 5.14f, Ridge = 3.84f;
        const float Tolerance = 0.02f;

        static Vector3 B(float x, float y, float z) => new Vector3(-x, z, -y);

        /// `export-verification.json` -> `StorageHutL1.slot_positions` and
        /// `Marker_Pickup`, Blender metres (2026-10-03 18:18 export).
        static readonly Dictionary<string, Vector3> HutMarks = new Dictionary<string, Vector3>
        {
            { "Stock_BoardBearers_01", B(-0.715f, 0.475f, 0.2f) },
            { "Stock_BoardBearers_02", B(0.0f, 0.475f, 0.2f) },
            { "Stock_BoardBearers_03", B(0.715f, 0.475f, 0.2f) },
            { "Stock_DishShelf_01", B(-2.66f, -2.175f, 0.6f) },
            { "Stock_DishShelf_02", B(-1.94f, -2.175f, 0.6f) },
            { "Stock_DishShelf_03", B(-2.66f, -1.675f, 1.0f) },
            { "Stock_DishShelf_04", B(-1.94f, -1.675f, 1.0f) },
            { "Stock_GearRack_01", B(1.82f, -1.925f, 0.21f) },
            { "Stock_GearRack_02", B(2.14f, -1.925f, 0.21f) },
            { "Stock_GearRack_03", B(2.46f, -1.925f, 0.21f) },
            { "Stock_GearRack_04", B(2.78f, -1.925f, 0.21f) },
            { "Stock_HangBeam_01", B(-1.12f, -1.45f, 3.4f) },
            { "Stock_HangBeam_02", B(-0.67f, -1.45f, 3.4f) },
            { "Stock_HangBeam_03", B(-0.22f, -1.45f, 3.4f) },
            { "Stock_HangBeam_04", B(0.22f, -1.45f, 3.4f) },
            { "Stock_HangBeam_05", B(0.67f, -1.45f, 3.4f) },
            { "Stock_HangBeam_06", B(1.12f, -1.45f, 3.4f) },
            { "Stock_LogCradle_01", B(-2.675f, 0.0f, 0.2f) },
            { "Stock_LogCradle_02", B(-1.925f, 0.0f, 0.2f) },
            { "Stock_LogCradle_03", B(-2.675f, 0.0f, 0.79f) },
            { "Stock_LogCradle_04", B(-1.925f, 0.0f, 0.79f) },
            { "Stock_Sacks_01", B(-1.112f, 1.9f, 0.145f) },
            { "Stock_Sacks_02", B(-0.668f, 1.9f, 0.145f) },
            { "Stock_Sacks_03", B(-0.222f, 1.9f, 0.145f) },
            { "Stock_Sacks_04", B(0.222f, 1.9f, 0.145f) },
            { "Stock_Sacks_05", B(0.668f, 1.9f, 0.145f) },
            { "Stock_Sacks_06", B(1.112f, 1.9f, 0.145f) },
            { "Stock_StoneCrib_01", B(2.3f, -0.705f, 0.08f) },
            { "Stock_StoneCrib_02", B(2.3f, 0.0f, 0.08f) },
            { "Stock_StoneCrib_03", B(2.3f, 0.705f, 0.08f) },
            { "Marker_Pickup", B(0.0f, -2.2f, 0.0f) },
        };

        /// The same for `FireCache`.
        static readonly Dictionary<string, Vector3> CacheMarks = new Dictionary<string, Vector3>
        {
            { "Stock_BoardBearers_01", B(0.48f, 0.55f, 0.215f) },
            { "Stock_DishShelf_01", B(-0.15f, -0.6f, 0.415f) },
            { "Stock_GearRack_01", B(1.22f, 0.55f, 0.115f) },
            { "Stock_HangBeam_01", B(-0.26f, 1.64f, 1.58f) },
            { "Stock_HangBeam_02", B(0.26f, 1.64f, 1.58f) },
            { "Stock_LogCradle_01", B(-1.1f, 0.55f, 0.155f) },
            { "Stock_LogCradle_02", B(-0.38f, 0.55f, 0.155f) },
            { "Stock_Sacks_01", B(-1.303f, -0.6f, 0.16f) },
            { "Stock_Sacks_02", B(-0.858f, -0.6f, 0.16f) },
            { "Stock_StoneCrib_01", B(0.92f, -0.6f, 0.055f) },
            { "Marker_Pickup", B(0.0f, -1.5f, 0.0f) },
        };

        /// CONTRACT: kit steps per prefix (167 in all).
        static readonly Dictionary<string, int> KitSteps = new Dictionary<string, int>
        {
            { "LogCradle", 5 }, { "BoardBearers", 6 }, { "StoneCrib", 9 }, { "Sacks", 19 },
            { "HangBeam", 12 }, { "DishShelf", 40 }, { "GearRack", 28 },
            { "FireLogCradle", 5 }, { "FireBoardBearers", 6 }, { "FireStoneCrib", 9 }, { "FireGearRack", 28 },
        };

        // --- entry points --------------------------------------------------------

        /// Everything: copy, import + remap, wrap + check. Returns the report.
        [MenuItem("SeaSick/Art/Import level 1 storage slots (hut, fire cache, fill kit)")]
        public static string Run() => Run(null);

        public static string Run(string sourceDir)
        {
            var log = new StringBuilder();
            try
            {
                Copy(sourceDir, log);
                Import(log);
                Wrap(log);
                AssetDatabase.SaveAssets();
            }
            catch (System.Exception e) { log.AppendLine("FAILED: " + e.Message); Debug.LogException(e); }
            Debug.Log("[StorageL1] " + log);
            return log.ToString();
        }

        /// Step 1 alone: the three FBXs into the project.
        public static string CopyOnly(string sourceDir = null)
        {
            var log = new StringBuilder();
            try { Copy(sourceDir, log); }
            catch (System.Exception e) { log.AppendLine("FAILED: " + e.Message); }
            return log.ToString();
        }

        /// Step 2 alone: materials, import settings, remaps (the slow part).
        public static string ImportOnly()
        {
            var log = new StringBuilder();
            try { Import(log); AssetDatabase.SaveAssets(); }
            catch (System.Exception e) { log.AppendLine("FAILED: " + e.Message); }
            return log.ToString();
        }

        /// Step 3 alone: frames, wrappers, every check, the report.
        public static string WrapOnly()
        {
            var log = new StringBuilder();
            try { Wrap(log); AssetDatabase.SaveAssets(); }
            catch (System.Exception e) { log.AppendLine("FAILED: " + e.Message); }
            Debug.Log("[StorageL1] " + log);
            return log.ToString();
        }

        static string Stem(string name) => Regex.Replace(name, @"\.\d+$", "");

        // --- copy ------------------------------------------------------------------

        static void Copy(string sourceDir, StringBuilder log)
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            string src = string.IsNullOrEmpty(sourceDir) ? Path.Combine(project, SourceRel) : sourceDir;
            if (!Directory.Exists(src)) throw new System.Exception("no export folder " + src);
            EnsureFolder(ModelDir);
            EnsureFolder(MaterialDir);
            EnsureFolder(KitDir);
            bool any = false;
            foreach (var (file, dst) in new[]
                { ("StorageHutL1.fbx", HutPath), ("FireCache.fbx", CachePath), ("StorageFillKit.fbx", KitPath) })
            {
                string from = Path.Combine(src, file);
                if (!File.Exists(from)) throw new System.Exception("missing " + from);
                string to = Path.Combine(project, dst);
                if (File.Exists(to) && File.ReadAllBytes(to).SequenceEqual(File.ReadAllBytes(from)))
                {
                    log.AppendLine($"copy: {file} unchanged");
                    continue;
                }
                File.Copy(from, to, true);
                any = true;
                log.AppendLine($"copy: {file} -> {dst} ({new FileInfo(to).Length / 1024} KB)");
            }
            if (any) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // --- materials -------------------------------------------------------------

        static Dictionary<string, Material> Materials(StringBuilder log)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new System.Exception("Missing shader " + ShaderName);
            var wood = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + "/wood-tile-512.png");
            var rope = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + "/rope-tile-512.png");
            if (wood == null || rope == null)
                throw new System.Exception("wall tiles missing under " + TextureDir + " (run WallL1Import first)");
            var food = AssetDatabase.LoadAssetAtPath<Material>(FoodMaterialPath);
            if (food == null) throw new System.Exception("missing " + FoodMaterialPath + " (run FoodKitImport first)");
            EnsureFolder(MaterialDir);
            var set = new Dictionary<string, Material>
            {
                { Wood, Mat(Wood, shader, wood) },
                { Hemp, Mat(Hemp, shader, rope) },
                // Canvas, end grain and stone: GameColor only.
                { Plain, Mat(Plain, shader, null) },
                // The food kit's own shared material, untouched here.
                { Food, food },
            };
            log.AppendLine($"materials: {Wood}, {Hemp}, {Plain} on {ShaderName}; food -> {food.name} ({food.shader.name})");
            return set;
        }

        /// `KitchenL1Import.Mat`, verbatim.
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

        // --- import ----------------------------------------------------------------

        static void Import(StringBuilder log)
        {
            var mats = Materials(log);
            foreach (var path in new[] { HutPath, CachePath, KitPath })
                ImportModel(path, mats, log);
        }

        static void ImportModel(string path, Dictionary<string, Material> mats, StringBuilder log)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) throw new System.Exception("no model at " + path + " (run CopyOnly first)");
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

            var fbxNames = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>()
                .Select(m => m.name).Distinct().ToList();
            if (fbxNames.Count == 0) throw new System.Exception(path + ": no embedded materials to map");
            foreach (var n in fbxNames)
            {
                if (!mats.TryGetValue(Stem(n), out var target))
                    throw new System.Exception($"{path}: FBX material '{n}' matches none of {string.Join(", ", mats.Keys)}");
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), target);
            }
            mi.SaveAndReimport();
            log.AppendLine($"import {Path.GetFileName(path)}: {fbxNames.Count} FBX materials remapped ({string.Join(", ", fbxNames)})");

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var allowed = new HashSet<Material>(mats.Values);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m == null || !allowed.Contains(m))
                        throw new System.Exception($"{path}: {r.name} has an unmapped material {(m ? m.name : "null")}");
        }

        // --- frames and wrappers -----------------------------------------------------

        static Transform Find(Transform root, string stem)
        {
            var hits = root.GetComponentsInChildren<Transform>(true).Where(t => Stem(t.name) == stem).ToList();
            if (hits.Count == 0) throw new System.Exception("model has no " + stem);
            if (hits.Count > 1) throw new System.Exception($"model has {hits.Count} objects named {stem}");
            return hits[0];
        }

        /// The rotation taking the model's measured directions onto the
        /// CONTRACT's: `a1`/`a2` measured, `e1`/`e2` expected (not parallel).
        /// Also the ×100 guard: the measured span must equal the expected.
        static Quaternion Frame(Vector3 a1, Vector3 a2, Vector3 e1, Vector3 e2, string what, StringBuilder log)
        {
            float ratio = a1.magnitude / e1.magnitude;
            if (Mathf.Abs(ratio - 1f) > 0.02f)
                throw new System.Exception($"{what}: anchors {ratio:F3}x their CONTRACT spacing -- a file-scale problem (the x100 trap?)");
            var q = Quaternion.LookRotation(e1.normalized, e2) * Quaternion.Inverse(Quaternion.LookRotation(a1.normalized, a2));
            log.AppendLine($"{what} frame: {q.eulerAngles:F1} (identity = the importer already matches the CONTRACT)");
            return q;
        }

        static Quaternion MeasureFrame(string modelPath, bool hut, StringBuilder log)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var t = go.transform;
                var marks = hut ? HutMarks : CacheMarks;
                Vector3 P(string n) => Find(t, n).position;
                // Hut: along the hang beam (x) and up the log cradle's two
                // tiers (pure up). Cache: between the tripod hooks (x) and
                // hook -> first sack (up and back), not parallel.
                string a = "Stock_HangBeam_01", b = hut ? "Stock_HangBeam_06" : "Stock_HangBeam_02";
                string c = hut ? "Stock_LogCradle_01" : "Stock_Sacks_01", d = hut ? "Stock_LogCradle_03" : "Stock_HangBeam_01";
                return Frame(P(b) - P(a), P(d) - P(c), marks[b] - marks[a], marks[d] - marks[c],
                    hut ? "hut" : "cache", log);
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void Wrap(StringBuilder log)
        {
            var kit = KitIndex(log);
            WrapOne(HutPath, HutWrapper, "StorageHutL1", true, kit, log);
            WrapOne(CachePath, CacheWrapper, "FireCache", false, kit, log);
            CheckResolution(kit, log);
        }

        static void WrapOne(string modelPath, string wrapper, string rootName, bool hut,
            KitInfo kit, StringBuilder log)
        {
            var frame = MeasureFrame(modelPath, hut, log);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(wrapper) == null)
            {
                var fresh = new GameObject(Path.GetFileNameWithoutExtension(wrapper));
                PrefabUtility.SaveAsPrefabAsset(fresh, wrapper);
                Object.DestroyImmediate(fresh);
                log.AppendLine("created " + wrapper);
            }
            string guid = AssetDatabase.AssetPathToGUID(wrapper);
            var marks = hut ? HutMarks : CacheMarks;

            var contents = PrefabUtility.LoadPrefabContents(wrapper);
            try
            {
                var root = contents.transform;
                if (root.localScale != Vector3.one || root.localRotation != Quaternion.identity)
                    throw new System.Exception(wrapper + " root is not at unit scale/identity");
                for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);

                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
                // Named for the FBX root `StorageSlotView.SiteOf` keys on
                // (the FBX's own inner root keeps the name too).
                model.name = rootName;
                model.transform.SetParent(root, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = frame * model.transform.localRotation;

                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }

                // Every anchor and the pickup where the CONTRACT says.
                float worst = 0f;
                foreach (var kv in marks)
                {
                    Vector3 at = root.InverseTransformPoint(Find(root, kv.Key).position);
                    float miss = (at - kv.Value).magnitude;
                    worst = Mathf.Max(worst, miss);
                    if (miss > Tolerance)
                        throw new System.Exception($"{rootName} {kv.Key} at {at:F3}, CONTRACT says {kv.Value:F3}: frame or scale is off");
                }
                log.AppendLine($"{rootName}: {marks.Count} marks within {worst:F3} m of the CONTRACT");

                // Anchors: EMPTY (kit mode), counted per family against the table.
                var counts = new int[StorageSlots.FamilyCount];
                var anchors = new List<Transform>();
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    string s = Stem(t.name);
                    if (!s.StartsWith("Stock_", System.StringComparison.Ordinal)) continue;
                    int us = s.LastIndexOf('_');
                    if (us <= 6 || !StorageSlots.TryFamilyFromWord(s.Substring(6, us - 6), out var fam))
                        throw new System.Exception($"{rootName}: '{t.name}' is not Stock_<Family>_NN");
                    if (t.childCount != 0)
                        throw new System.Exception($"{rootName}: anchor {t.name} has {t.childCount} children (kit mode wants it empty)");
                    counts[(int)fam]++;
                    anchors.Add(t);
                }
                var want = StorageSlots.SlotsOf(hut ? BuildPlans.Storage.id : BuildPlans.Campfire.id, 1);
                var line = new StringBuilder($"{rootName} anchors:");
                bool bad = false;
                for (int f = 0; f < StorageSlots.FamilyCount; f++)
                {
                    line.Append($" {(StoreFamily)f} {counts[f]}/{want[f]}");
                    if (counts[f] != want[f]) { bad = true; line.Append(" MISMATCH"); }
                }
                log.AppendLine(line.ToString());
                if (bad) throw new System.Exception($"{rootName}: anchor counts differ from StorageSlots' row");

                // The kit's steps go under an anchor at identity. A step is
                // authored at the slot's origin, unrotated, so with the kit
                // root at identity its world frame IS "Blender slot space
                // as Unity shows it". The anchor, in the finished wrapper
                // (frame applied), must have that same frame, or every
                // step lands turned or scaled (the x100 trap again).
                foreach (var a in anchors)
                {
                    var rel = Quaternion.Inverse(root.rotation) * a.rotation;
                    float ang = Quaternion.Angle(rel, kit.stepRot);
                    Vector3 sc = Div(a.lossyScale, root.lossyScale);
                    if (ang > 1f || (sc - kit.stepScale).magnitude > 0.01f * kit.stepScale.magnitude)
                        throw new System.Exception($"{rootName} {a.name}: frame {rel.eulerAngles:F1} x{sc:F3} vs kit steps {kit.stepRot.eulerAngles:F1} x{kit.stepScale:F3} -- steps would land turned or scaled");
                }
                log.AppendLine($"{rootName}: all {anchors.Count} anchors share the kit steps' frame ({kit.stepRot.eulerAngles:F0}, x{kit.stepScale.x:F2})");

                if (hut)
                {
                    // The name `CampWorker.StoreSpot` walks to.
                    Find(root, "Marker_Pickup").name = "Input_Pickup";
                    log.AppendLine("StorageHutL1: Marker_Pickup -> Input_Pickup (store drop-off + runner pickup)");
                }

                var b = Bounds(root);
                log.AppendLine($"{rootName} bounds {b.min:F2}..{b.max:F2}, {Tris(root)} tris, {root.GetComponentsInChildren<MeshRenderer>(true).Length} renderers");
                if (hut)
                {
                    if (b.min.x < -FootX * 0.5f - Tolerance || b.max.x > FootX * 0.5f + Tolerance
                        || b.min.z < -FootZ * 0.5f - Tolerance || b.max.z > FootZ * 0.5f + Tolerance)
                        throw new System.Exception($"bounds {b.min:F2}..{b.max:F2} leave the {FootX} x {FootZ} footprint");
                    if (b.max.y > Ridge) throw new System.Exception($"top {b.max.y:F2} m is over the {Ridge} m ridge");
                }
                else
                {
                    // Must match the walk box `BuildingFactory.FireCacheBox`.
                    var k = BuildingFactory.FireCacheBox;
                    if (b.min.x < k.x - k.z - 0.05f || b.max.x > k.x + k.z + 0.05f
                        || b.min.z < k.y - k.w - 0.05f || b.max.z > k.y + k.w + 0.05f)
                        log.AppendLine($"  !! cache bounds leave BuildingFactory.FireCacheBox {k}: update the box");
                }
                if (b.min.y < -0.2f || b.min.y > 0.05f)
                    throw new System.Exception($"{rootName}: lowest point {b.min.y:F2} m, the root is not on the ground");

                PrefabUtility.SaveAsPrefabAsset(contents, wrapper);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            if (AssetDatabase.AssetPathToGUID(wrapper) != guid)
                throw new System.Exception(wrapper + " GUID changed");
            log.AppendLine($"{wrapper} saved, GUID {guid}");
        }

        static Vector3 Div(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);

        // --- the fill kit ------------------------------------------------------------

        sealed class KitInfo
        {
            /// prefix -> per-resource name ("" = generic) -> step numbers.
            public readonly Dictionary<string, Dictionary<string, SortedSet<int>>> sets =
                new Dictionary<string, Dictionary<string, SortedSet<int>>>();
            public Quaternion stepRot = Quaternion.identity;
            public Vector3 stepScale = Vector3.one;
        }

        /// Reads `StorageFillKit` the way `StorageSlotView.LoadKit` does
        /// (top-level children `<Prefix>__Fill_[<Res>_]<k>`), counts steps
        /// per prefix against the CONTRACT, and records the steps' common
        /// frame relative to the kit root (all steps must share one).
        static KitInfo KitIndex(StringBuilder log)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(KitPath);
            if (asset == null) throw new System.Exception("no kit at " + KitPath);
            var info = new KitInfo();
            var go = Object.Instantiate(asset);
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var root = go.transform;
                bool first = true;
                int steps = 0, food = 0;
                var odd = new List<string>();
                for (int i = 0; i < root.childCount; i++)
                {
                    var c = root.GetChild(i);
                    string stem = Stem(c.name);
                    var m = Regex.Match(stem, @"^(\w+?)__Fill_(?:(\w+)_)?(\d+)$");
                    if (!m.Success) { odd.Add(c.name); continue; }
                    string prefix = m.Groups[1].Value, res = m.Groups[2].Success ? m.Groups[2].Value : "";
                    int k = int.Parse(m.Groups[3].Value);
                    if (!info.sets.TryGetValue(prefix, out var byRes)) info.sets[prefix] = byRes = new Dictionary<string, SortedSet<int>>();
                    if (!byRes.TryGetValue(res, out var ks)) byRes[res] = ks = new SortedSet<int>();
                    ks.Add(k);
                    steps++;
                    food += c.GetComponentsInChildren<Transform>(true).Count(t => Stem(t.name).StartsWith("Food_"));
                    // World frame with the kit root at identity (see WrapOne).
                    var rel = c.rotation;
                    var sc = c.lossyScale;
                    if (first) { info.stepRot = rel; info.stepScale = sc; first = false; }
                    else if (Quaternion.Angle(rel, info.stepRot) > 1f || (sc - info.stepScale).magnitude > 0.01f * sc.magnitude)
                        throw new System.Exception($"kit step {c.name} frame {rel.eulerAngles:F1} x{sc:F3} differs from the others' {info.stepRot.eulerAngles:F1} x{info.stepScale:F3}");
                }
                log.AppendLine($"kit: {steps} steps (CONTRACT 167), {food} food children, frame {info.stepRot.eulerAngles:F0} x{info.stepScale:F2}"
                    + (odd.Count > 0 ? $"; {odd.Count} other top-level objects: {string.Join(", ", odd.Take(8))}" : ""));
                foreach (var kv in KitSteps)
                {
                    int n = info.sets.TryGetValue(kv.Key, out var byRes) ? byRes.Values.Sum(s => s.Count) : 0;
                    log.AppendLine($"  {kv.Key}: {n}/{kv.Value}" + (n != kv.Value ? "  !! MISMATCH" : ""));
                }
                foreach (var p in info.sets.Keys)
                    if (!KitSteps.ContainsKey(p)) log.AppendLine($"  {p}: unexpected prefix");
            }
            finally { Object.DestroyImmediate(go); }
            return info;
        }

        /// Every resource the store table knows, through the view's own rule
        /// (CONTRACT "Runtime": `Fill_<Res>_k`, else `Fill_k`; the fire cache
        /// tries `Fire<Family>` first): the steps 1..N it would show, and
        /// any step number with nothing to show.
        static void CheckResolution(KitInfo kit, StringBuilder log)
        {
            var missing = new List<string>();
            for (int i = 0; i < StorageSlots.ResourceCount; i++)
            {
                string res = StorageSlots.ResourceAt(i);
                var fam = StorageSlots.FamilyOf(res);
                string plain = LongName(fam);
                foreach (var prefix in new[] { plain, "Fire" + plain })
                {
                    string use = kit.sets.ContainsKey(prefix) ? prefix : plain;
                    if (!kit.sets.TryGetValue(use, out var byRes)) { missing.Add($"{res}@{prefix}: no set"); continue; }
                    byRes.TryGetValue(res, out var own);
                    byRes.TryGetValue("", out var gen);
                    int n = Mathf.Max(own != null && own.Count > 0 ? own.Max : 0, gen != null ? gen.Count : 0);
                    if (n == 0) { missing.Add($"{res}@{use}: no steps"); continue; }
                    for (int k = 1; k <= n; k++)
                        if (!(own != null && own.Contains(k)) && !(gen != null && gen.Contains(k)))
                            missing.Add($"{res}@{use}: step {k}");
                }
            }
            log.AppendLine(missing.Count == 0
                ? $"kit resolution: all {StorageSlots.ResourceCount} store resources show a step at every fill level, hut and cache"
                : "kit resolution MISSING: " + string.Join("; ", missing));
        }

        /// The kit's prefix for a family (the CONTRACT's long names).
        static string LongName(StoreFamily f)
        {
            switch (f)
            {
                case StoreFamily.Logs: return "LogCradle";
                case StoreFamily.Stone: return "StoneCrib";
                case StoreFamily.Boards: return "BoardBearers";
                case StoreFamily.Sacks: return "Sacks";
                case StoreFamily.Hang: return "HangBeam";
                case StoreFamily.Dishes: return "DishShelf";
                case StoreFamily.Gear: return "GearRack";
            }
            return "?";
        }

        // --- measuring -----------------------------------------------------------------

        static Bounds Bounds(Transform root)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = root.InverseTransformPoint(mf.transform.TransformPoint(v));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static int Tris(Transform root)
            => root.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null)
                .Sum(f => f.sharedMesh.triangles.Length / 3);
    }
}
