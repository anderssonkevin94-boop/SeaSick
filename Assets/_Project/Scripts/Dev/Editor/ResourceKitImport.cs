using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **Astra's resource kit v1, Kevin approved 2026-09-30**
    /// (`art-staging/resource-kit-v1`): Timber, Boards, Stone, Ore and Brick,
    /// each as `_Unit` (one unit of a changing pile, bottom-centre origin),
    /// `_CarryUnit` (one carried item, centre-grip origin), and the fixed
    /// `_Carry` / `_Stack` review compositions (imported, not used by the
    /// runtime, which composes its own counts from the two unit meshes).
    ///
    /// - The 20 FBXs sit in `Resources/Kits/Resources/` under their own names
    ///   so runtime code can `Resources.Load` them (`ResourceKit`). True
    ///   metres (`useFileScale`, Blender's cm unit), Y-up with the axis
    ///   conversion baked into the mesh (the same settings `FoodKitImport`
    ///   uses), flat authored normals, no animation, colliders, cameras or
    ///   lights, meshes not readable (nothing reads them at runtime).
    /// - Every material is remapped to the one shared
    ///   `Art/Kits/Shared/GameColor.mat` (`SeaSick/Environment Toon`, colour
    ///   from the FBX's `GameColor` vertex colours), so every unit renderer
    ///   carries that material into a build and the piles batch.
    /// - Each mesh is measured against `manifest.json` (Blender x, y, z ->
    ///   Unity x, z, y), its vertex colours and its pivot checked (Unit and
    ///   Stack sit on y = 0, CarryUnit and Carry are centred on the grip), so
    ///   a wrong axis, scale or a dropped `GameColor` throws instead of
    ///   shipping a giant log.
    ///
    /// Idempotent: run it again after re-exporting.
    public static class ResourceKitImport
    {
        const string FbxDir = "Assets/_Project/Resources/Kits/Resources";
        const string MaterialPath = "Assets/_Project/Art/Kits/Shared/GameColor.mat";

        /// `manifest.json` dimensions in Blender axes: x, y (the long axis of
        /// stock logs and boards), z (up).
        static readonly (string name, float x, float y, float z, bool bottomPivot)[] Meshes =
        {
            ("Timber_Unit", 0.2447f, 1.6040f, 0.2368f, true),
            ("Timber_CarryUnit", 0.1774f, 1.1629f, 0.1717f, false),
            ("Timber_Carry", 0.3574f, 1.1629f, 0.1717f, false),
            ("Timber_Stack", 0.7547f, 1.6040f, 0.6528f, true),
            ("Boards_Unit", 0.25f, 1.60f, 0.075f, true),
            ("Boards_CarryUnit", 0.46f, 0.13f, 0.035f, false),
            ("Boards_Carry", 0.46f, 0.13f, 0.125f, false),
            ("Boards_Stack", 0.80f, 1.65f, 0.318f, true),
            ("Stone_Unit", 0.4906f, 0.4348f, 0.31f, true),
            ("Stone_CarryUnit", 0.1962f, 0.1739f, 0.2f, false),
            ("Stone_Carry", 0.3662f, 0.1739f, 0.2f, false),
            ("Stone_Stack", 1.1511f, 1.1497f, 0.75f, true),
            ("Ore_Unit", 0.4906f, 0.4348f, 0.31f, true),
            ("Ore_CarryUnit", 0.1962f, 0.1739f, 0.2f, false),
            ("Ore_Carry", 0.3662f, 0.1739f, 0.2f, false),
            ("Ore_Stack", 1.1511f, 1.1497f, 0.75f, true),
            ("Brick_Unit", 0.36f, 0.18f, 0.12f, true),
            ("Brick_CarryUnit", 0.17f, 0.10f, 0.085f, false),
            ("Brick_Carry", 0.17f, 0.10f, 0.265f, false),
            ("Brick_Stack", 1.19f, 0.375f, 0.501f, true),
        };

        const float Tolerance = 0.02f;

        /// `unity cmd eval --json --code 'SeaSick.Dev.ResourceKitImport.Run()'`
        public static string Run()
        {
            var log = new StringBuilder();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null) throw new System.Exception("missing shared material " + MaterialPath);

            foreach (var m in Meshes)
            {
                string path = $"{FbxDir}/{m.name}.fbx";
                ImportModel(path, mat, log);
                Check(path, m.x, m.y, m.z, m.bottomPivot, mat, log);
            }
            AssetDatabase.SaveAssets();
            log.Insert(0, $"[ResourceKit] {Meshes.Length} FBXs imported, all on {mat.name}\n");
            Debug.Log(log.ToString());
            return log.ToString();
        }

        [MenuItem("SeaSick/Art/Import resource kit (Astra v1)")]
        static void Menu() => Run();

        static void ImportModel(string path, Material mat, StringBuilder log)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) throw new System.Exception("no model at " + path);
            mi.globalScale = 1f;
            mi.useFileScale = true;                          // true metres
            mi.bakeAxisConversion = true;                    // Blender Z-up -> Unity Y-up, into the mesh
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
            // Drop earlier remaps so the FBX's own material names show, then
            // map every one of them onto the shared GameColor material.
            foreach (var key in mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).ToList())
                mi.RemoveRemap(key);
            mi.SaveAndReimport();

            var names = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>()
                .Select(m => m.name).Distinct().ToList();
            if (names.Count == 0) throw new System.Exception(path + ": no embedded materials to remap");
            foreach (var n in names)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), mat);
            mi.SaveAndReimport();
        }

        /// One mesh, on the shared material with vertex colours, its Blender
        /// bounds (x, y, z) landing as Unity (x, z, y), and its pivot where
        /// the README puts it.
        static void Check(string path, float bx, float by, float bz, bool bottomPivot,
            Material mat, StringBuilder log)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try
            {
                var filters = go.GetComponentsInChildren<MeshFilter>(true);
                if (filters.Length != 1 || filters[0].sharedMesh == null)
                    throw new System.Exception($"{path}: want exactly one mesh, found {filters.Length}");
                var mf = filters[0];
                var mesh = mf.sharedMesh;
                var rend = mf.GetComponent<MeshRenderer>();
                if (rend == null || rend.sharedMaterials.Any(m => m != mat))
                    throw new System.Exception($"{path}: renderer is not on the shared GameColor material");
                if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color))
                    throw new System.Exception($"{path}: mesh has no vertex colours (GameColor lost)");

                var m = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Bounds b = default; bool any = false;
                foreach (var v in mesh.vertices)
                {
                    var p = m.MultiplyPoint3x4(v);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
                var s = b.size;
                log.AppendLine($"{System.IO.Path.GetFileName(path)}: size {s.x:F3} x {s.y:F3} x {s.z:F3} (want {bx:F3} x {bz:F3} x {by:F3}), "
                    + $"y {b.min.y:F3}..{b.max.y:F3}, centre xz {b.center.x:F3},{b.center.z:F3}, {mesh.triangles.Length / 3} tris");
                if (Mathf.Abs(s.x - bx) > Tolerance || Mathf.Abs(s.y - bz) > Tolerance || Mathf.Abs(s.z - by) > Tolerance)
                    throw new System.Exception($"{path}: size {s:F3}, manifest says {bx:F3} x {bz:F3} x {by:F3} (x, up, depth): axes or scale are off");
                // The pivots the runtime relies on are the two unit meshes'; the
                // fixed Carry / Stack compositions are only reported.
                bool strict = path.EndsWith("_Unit.fbx") || path.EndsWith("_CarryUnit.fbx");
                string pivot = null;
                if (bottomPivot && Mathf.Abs(b.min.y) > Tolerance)
                    pivot = $"base at y {b.min.y:F3}, README says bottom-centre origin";
                else if (!bottomPivot && Mathf.Abs(b.center.y) > Tolerance)
                    pivot = $"centre at y {b.center.y:F3}, README says centre-grip origin";
                else if (Mathf.Abs(b.center.x) > 0.05f || Mathf.Abs(b.center.z) > 0.05f)
                    pivot = $"centre off the origin by {b.center.x:F3}, {b.center.z:F3}";
                if (pivot != null)
                {
                    if (strict) throw new System.Exception(path + ": " + pivot);
                    log.AppendLine("  note: " + pivot + " (fixed composition, not used by the runtime)");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
