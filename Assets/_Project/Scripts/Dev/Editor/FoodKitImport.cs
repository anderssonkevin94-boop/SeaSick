using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **Astra's food ingredients kit v1, Kevin approved 2026-09-30**
    /// (`art-staging/food-ingredients-v1`): Apple, Carrot, Fish, Meat, Onion,
    /// Potato, Wheat, each as a metre-scale `<Name>_Unit.fbx` (one food, base
    /// origin) and a `<Name>_Display.fbx` (the same food arranged on the lid
    /// of an approved closed `Cargo_Box_Small`), plus a 256 px icon.
    ///
    /// - The FBXs sit in `Resources/Kits/Food/` under their own names so
    ///   runtime code can `Resources.Load` them (`CampPiles` draws its food
    ///   piles from the `_Unit` meshes; carried food is loaded the same way).
    ///   True metres (`useFileScale`, Blender's cm unit), Y-up with the axis
    ///   conversion baked into the mesh, flat authored normals, no animation,
    ///   colliders, cameras or lights.
    /// - Every material, the food's `SS_Food_GameColor` and the crate's four
    ///   `SS_Cargo_*`, is remapped to the one shared
    ///   `Art/Kits/Shared/GameColor.mat` (`SeaSick/Environment Toon`, colour
    ///   from the FBX's `GameColor` vertex colours), so a Unit's renderer
    ///   carries that material into a build with it.
    /// - `_Unit` meshes are made readable: `CampPiles` re-centres each once
    ///   into its own pile-ready copy at first use.
    /// - The icons get the same importer settings as the existing item PNGs
    ///   (read off `food.png`) and are registered in
    ///   `Resources/UI/ItemIcons.asset` under their `Res` ids, preserving
    ///   every other entry.
    ///
    /// The measured heights are checked against the README's Blender bounds,
    /// so a wrong axis or scale throws instead of shipping a giant potato.
    /// Idempotent: run it again after re-exporting.
    public static class FoodKitImport
    {
        const string FbxDir = "Assets/_Project/Resources/Kits/Food";
        const string IconDir = "Assets/_Project/Art/UI/Icons/Items";
        const string IconRef = IconDir + "/food.png";
        const string MaterialPath = "Assets/_Project/Art/Kits/Shared/GameColor.mat";
        const string IconSetPath = "Assets/_Project/Resources/UI/ItemIcons.asset";

        /// Name -> the unit's height in metres (Blender Z extent, README).
        static readonly (string name, float top)[] Foods =
        {
            ("Apple", 0.246f), ("Carrot", 0.420f), ("Fish", 0.112f), ("Meat", 0.065f),
            ("Onion", 0.320f), ("Potato", 0.170f), ("Wheat", 0.426f),
        };

        const float Tolerance = 0.012f;

        /// `unity cmd eval --json --code 'SeaSick.Dev.FoodKitImport.Run()'`
        public static string Run()
        {
            var log = new StringBuilder();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null) throw new System.Exception("missing shared material " + MaterialPath);

            foreach (var (name, top) in Foods)
            {
                ImportModel($"{FbxDir}/{name}_Unit.fbx", true, mat, log);
                ImportModel($"{FbxDir}/{name}_Display.fbx", false, mat, log);
                CheckUnit($"{FbxDir}/{name}_Unit.fbx", top, mat, log);
            }
            var tex = ImportIcons(log);
            Register(tex, log);
            AssetDatabase.SaveAssets();
            Debug.Log("[FoodKit] " + log);
            return log.ToString();
        }

        [MenuItem("SeaSick/Art/Import food kit (Astra v1)")]
        static void Menu() => Run();

        // --- models -------------------------------------------------------------

        static void ImportModel(string path, bool readable, Material mat, StringBuilder log)
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
            mi.isReadable = readable;
            mi.preserveHierarchy = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var key in mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).ToList())
                mi.RemoveRemap(key);
            mi.SaveAndReimport();

            var names = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>()
                .Select(m => m.name).Distinct().ToList();
            if (names.Count == 0) throw new System.Exception(path + ": no embedded materials to remap");
            foreach (var n in names)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), mat);
            mi.SaveAndReimport();
            log.AppendLine($"{System.IO.Path.GetFileName(path)}: {names.Count} material(s) [{string.Join(", ", names)}] -> {mat.name}");
        }

        /// Height, base and vertex colours of a `_Unit`, against the README.
        static void CheckUnit(string path, float top, Material mat, StringBuilder log)
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
                log.AppendLine($"{System.IO.Path.GetFileName(path)}: bounds {b.min:F3}..{b.max:F3}, {mesh.triangles.Length / 3} tris");
                if (Mathf.Abs(b.max.y - top) > Tolerance || Mathf.Abs(b.min.y) > Tolerance)
                    throw new System.Exception($"{path}: y {b.min.y:F3}..{b.max.y:F3} m, README says 0..{top:F3}: axes or scale are off");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // --- icons ----------------------------------------------------------------

        /// Same importer settings as the existing item icons (read off
        /// `food.png`): default texture type, alpha as transparency, no
        /// mips, clamped, the same compression, max size and platform rows.
        static Dictionary<string, Texture2D> ImportIcons(StringBuilder log)
        {
            var reference = AssetImporter.GetAtPath(IconRef) as TextureImporter;
            if (reference == null) throw new System.Exception("missing reference icon " + IconRef);
            var settings = new TextureImporterSettings();
            reference.ReadTextureSettings(settings);

            var result = new Dictionary<string, Texture2D>();
            foreach (var (name, _) in Foods)
            {
                string path = $"{IconDir}/{name}.png";
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) throw new System.Exception("no icon at " + path);
                ti.textureType = reference.textureType;
                ti.SetTextureSettings(settings);
                ti.sRGBTexture = reference.sRGBTexture;
                ti.alphaSource = reference.alphaSource;
                ti.alphaIsTransparency = reference.alphaIsTransparency;
                ti.mipmapEnabled = reference.mipmapEnabled;
                ti.wrapMode = reference.wrapMode;
                ti.filterMode = reference.filterMode;
                ti.npotScale = reference.npotScale;
                ti.isReadable = reference.isReadable;
                ti.textureCompression = reference.textureCompression;
                ti.compressionQuality = reference.compressionQuality;
                ti.maxTextureSize = reference.maxTextureSize;
                ti.SetPlatformTextureSettings(reference.GetDefaultPlatformTextureSettings());
                foreach (var platform in new[] { "Standalone", "iOS" })
                {
                    var ps = reference.GetPlatformTextureSettings(platform);
                    ti.SetPlatformTextureSettings(ps);
                }
                ti.SaveAndReimport();
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) throw new System.Exception("icon did not import: " + path);
                result[name] = tex;
                log.AppendLine($"icon {name}: {tex.width}x{tex.height}, type {ti.textureType}, max {ti.maxTextureSize}, compression {ti.textureCompression}");
            }
            return result;
        }

        /// Adds (or replaces) `ids[i]`/`icons[i]` pairs in the registry, the
        /// `Res` id being the icon's file name; every other entry stays.
        static void Register(Dictionary<string, Texture2D> icons, StringBuilder log)
        {
            var set = AssetDatabase.LoadAssetAtPath<Object>(IconSetPath);
            if (set == null) throw new System.Exception("missing " + IconSetPath);
            var so = new SerializedObject(set);
            var ids = so.FindProperty("ids");
            var tex = so.FindProperty("icons");
            if (ids == null || tex == null) throw new System.Exception("ItemIconSet has no ids/icons arrays");
            // Keep the two arrays the same length before adding.
            while (tex.arraySize < ids.arraySize) tex.InsertArrayElementAtIndex(tex.arraySize);
            while (ids.arraySize < tex.arraySize) ids.InsertArrayElementAtIndex(ids.arraySize);

            int added = 0, replaced = 0;
            foreach (var kv in icons)
            {
                int at = -1;
                for (int i = 0; i < ids.arraySize; i++)
                    if (ids.GetArrayElementAtIndex(i).stringValue == kv.Key) { at = i; break; }
                if (at < 0)
                {
                    at = ids.arraySize;
                    ids.InsertArrayElementAtIndex(at);
                    tex.InsertArrayElementAtIndex(at);
                    added++;
                }
                else replaced++;
                ids.GetArrayElementAtIndex(at).stringValue = kv.Key;
                tex.GetArrayElementAtIndex(at).objectReferenceValue = kv.Value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(set);
            log.AppendLine($"ItemIcons.asset: {added} added, {replaced} replaced, {ids.arraySize} entries total ({string.Join(", ", icons.Keys)})");
        }
    }
}
