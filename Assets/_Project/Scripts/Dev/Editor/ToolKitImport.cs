using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeaSick.Dev
{
    /// **Astra's worker tools v1, Kevin approved 2026-09-30**
    /// (`art-staging/worker-tools-v1`).
    ///
    /// Sets up the seven FBXs under `Resources/Kits/Tools` (Axe, Hammer, Saw,
    /// Hoe, StirPaddle, SpearStone, SpearIron) for `World/ToolKit`, which
    /// hangs each mesh at IDENTITY in the tool frame (origin at the fist,
    /// +Y up the haft, +Z the working face, X the swing axis) under a
    /// unit-scale parent.
    ///
    /// - **True metres, axis baked into the mesh.** The exports are Blender
    ///   Z-up vertices in metres under a `Lcl Rotation -90 / Lcl Scaling 100`
    ///   node with a 1 cm unit (game (x,y,z) = Blender (x,-z,y)). With
    ///   `bakeAxisConversion` the rotation is applied to the vertices, so the
    ///   MESH itself is in the game frame and the runtime never reads the
    ///   node's transform. Each tool's mesh bounds are then CHECKED against
    ///   the game-frame ranges of its source vertices (grip at the origin,
    ///   reach and face as `PoseTool` expects); if the first setting misses,
    ///   the others (`bake` off, file scale off) are tried and the first one
    ///   that lands is kept. Any tool that fits none throws.
    /// - **One shared material.** Every FBX material is remapped onto
    ///   `Art/Kits/Shared/GameColor.mat` (`SeaSick/Environment Toon`, the
    ///   FBX's `GameColor` vertex colours), so all seven batch as one. Flat
    ///   authored normals are kept, nothing is welded.
    /// - No animation, colliders, cameras or lights. Read-only meshes.
    ///
    /// Idempotent: run it again after re-exporting an FBX.
    /// `unity cmd eval --json --code 'SeaSick.Dev.ToolKitImport.Run()'`
    public static class ToolKitImport
    {
        const string Dir = "Assets/_Project/Resources/Kits/Tools";
        const string MaterialPath = "Assets/_Project/Art/Kits/Shared/GameColor.mat";
        const float Tol = 0.012f;

        /// Mesh bounds in the tool frame, from the source vertices in
        /// `art-staging/worker-tools-v1` (game y = Blender z, game z = -Blender
        /// y): { half-width x, y min, y max, z min, z max }. Every tool is
        /// symmetric about X, so the one axis the Unity import may mirror
        /// (Blender -Y-forward -> Unity +Z-forward negates X) is harmless.
        static readonly (string name, float x, float y0, float y1, float z0, float z1)[] Tools =
        {
            ("Axe",        0.031f, -0.050f, 0.670f, -0.062f, 0.130f),
            ("Hammer",     0.040f, -0.040f, 0.341f, -0.090f, 0.100f),
            ("Saw",        0.018f, -0.105f, 0.580f, -0.130f, 0.055f),
            ("Hoe",        0.095f, -0.080f, 1.065f, -0.025f, 0.140f),
            ("StirPaddle", 0.060f, -0.060f, 0.755f, -0.021f, 0.021f),
            ("SpearStone", 0.052f, -0.650f, 1.405f, -0.030f, 0.030f),
            ("SpearIron",  0.042f, -0.650f, 1.450f, -0.030f, 0.030f),
            ("Pickaxe",    0.032f, -0.050f, 0.645f, -0.175f, 0.230f),
        };

        [MenuItem("SeaSick/Art/Import worker tools (Astra v1)")]
        public static string Run()
        {
            var log = new StringBuilder();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var shared = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (shared == null) throw new System.Exception("missing shared material " + MaterialPath);

            foreach (var t in Tools) Import(t, shared, log);

            AssetDatabase.SaveAssets();
            log.Insert(0, $"worker tools: {Tools.Length} imported onto {shared.name} ({shared.shader.name})\n");
            Debug.Log("[ToolKit] " + log);
            return log.ToString();
        }

        static void Import((string name, float x, float y0, float y1, float z0, float z1) t,
            Material shared, StringBuilder log)
        {
            string path = Dir + "/" + t.name + ".fbx";
            if (!System.IO.File.Exists(path)) throw new System.Exception("missing " + path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) throw new System.Exception("no model importer at " + path);

            // (bakeAxisConversion, useFileScale), preferred first.
            var tries = new[] { (true, true), (false, true), (true, false), (false, false) };
            string misses = "";
            foreach (var (bake, fileScale) in tries)
            {
                Configure(mi, bake, fileScale);
                mi.SaveAndReimport();
                Remap(mi, path, shared);
                mi.SaveAndReimport();

                string why = Check(path, t, out string report);
                if (why == null)
                {
                    log.AppendLine($"{t.name}: bake={bake} fileScale={fileScale}; {report}");
                    return;
                }
                misses += $"\n  bake={bake} fileScale={fileScale}: {why}";
            }
            throw new System.Exception($"{t.name}: mesh bounds fit no import setting{misses}");
        }

        static void Configure(ModelImporter mi, bool bake, bool fileScale)
        {
            mi.globalScale = 1f;
            mi.useFileScale = fileScale;
            mi.bakeAxisConversion = bake;
            mi.importNormals = ModelImporterNormals.Import;   // flat authored normals
            mi.importTangents = ModelImporterTangents.None;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.addCollider = false;
            mi.generateSecondaryUV = false;
            mi.weldVertices = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
            mi.preserveHierarchy = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        /// Every material the FBX carries (Blender names it
        /// `SS_WorkerTools_GameColor`) goes to the shared GameColor material.
        static void Remap(ModelImporter mi, string path, Material shared)
        {
            foreach (var key in mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).ToList())
                mi.RemoveRemap(key);
            mi.SaveAndReimport();
            var names = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>()
                .Select(m => m.name).Distinct().ToList();
            if (names.Count == 0) throw new System.Exception(path + ": no embedded materials to map");
            foreach (var n in names)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), shared);
        }

        /// Null if the FBX is one mesh whose bounds are the tool frame's,
        /// wearing only the shared material; otherwise what is wrong.
        static string Check(string path, (string name, float x, float y0, float y1, float z0, float z1) t,
            out string report)
        {
            report = "";
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) return "no prefab";
            var mfs = go.GetComponentsInChildren<MeshFilter>(true);
            if (mfs.Length != 1) return mfs.Length + " meshes (want 1)";
            if (go.GetComponentsInChildren<Camera>(true).Length + go.GetComponentsInChildren<Light>(true).Length
                + go.GetComponentsInChildren<Collider>(true).Length > 0) return "camera, light or collider present";
            var mesh = mfs[0].sharedMesh;
            if (mesh == null) return "no mesh";
            var mr = mfs[0].GetComponent<MeshRenderer>();
            if (mr == null) return "no MeshRenderer";
            foreach (var m in mr.sharedMaterials)
                if (m == null || AssetDatabase.GetAssetPath(m) != MaterialPath)
                    return "material not remapped (" + (m ? m.name : "null") + ")";
            if (!mesh.HasVertexAttribute(VertexAttribute.Color)) return "mesh lost its vertex colours";

            Bounds b = mesh.bounds;
            report = $"{mesh.triangles.Length / 3} tris, x +-{b.extents.x:F3}, y {b.min.y:F3}..{b.max.y:F3}, "
                + $"z {b.min.z:F3}..{b.max.z:F3}; node rot {mfs[0].transform.localEulerAngles.ToString("F0")} "
                + $"scale {mfs[0].transform.localScale.x:F2}";
            if (Mathf.Abs(b.extents.x - t.x) > Tol || Mathf.Abs(b.min.y - t.y0) > Tol || Mathf.Abs(b.max.y - t.y1) > Tol
                || Mathf.Abs(b.min.z - t.z0) > Tol || Mathf.Abs(b.max.z - t.z1) > Tol)
                return $"bounds off the tool frame (want x +-{t.x:F3} y {t.y0:F3}..{t.y1:F3} z {t.z0:F3}..{t.z1:F3}): {report}";
            return null;
        }
    }
}
