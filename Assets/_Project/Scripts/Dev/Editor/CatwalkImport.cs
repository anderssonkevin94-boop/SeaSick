using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **The pier catwalk, level 1** (2026-10-01, `art-staging/catwalk-lvl1-v1`):
    /// the gangway laid from a pier's sea edge onto the ship's rail.
    ///
    /// Copies `catwalk.fbx` to `Art/Catwalk/Models`, imports it like the pier
    /// kit (scale 1 with the file's unit conversion, readable, the one
    /// `Catwalk_VertexColor` material remapped onto
    /// `Art/AstraPlaytest/Astra_Building.mat` -- EnvironmentToon reading the
    /// per-corner `Col` vertex colours, same as `Art/AstraPlaytest/Pier`),
    /// and writes `Resources/Ship/Catwalk.prefab`: a `CatwalkKit` root with
    /// the three meshes (`Catwalk`, `Catwalk_ShipHook`, `Catwalk_PierChocks`)
    /// and the six contract empties. `Ship/Gangway` takes it apart at runtime
    /// (span about the hinge, hook at the stretched end, chocks on the pier).
    ///
    /// Every marker and mesh origin is checked against the README to 1 cm,
    /// the triangle count against 1,200, and the posts' 0.9 m top; any miss
    /// throws and nothing is written. Idempotent.
    public static class CatwalkImport
    {
        const string Source = "art-staging/catwalk-lvl1-v1/catwalk.fbx";
        const string Root = "Assets/_Project/Art/Catwalk";
        const string ModelPath = Root + "/Models/catwalk.fbx";
        const string MaterialPath = "Assets/_Project/Art/AstraPlaytest/Astra_Building.mat";
        const string PrefabDir = "Assets/_Project/Resources/Ship";
        const string PrefabPath = PrefabDir + "/Catwalk.prefab";
        const float Tol = 0.01f;

        static readonly Dictionary<string, Vector3> Marks = new Dictionary<string, Vector3>
        {
            { "Pier_Hinge", new Vector3(0f, 0f, 0f) },
            { "Ship_End", new Vector3(0f, 0f, 3f) },
            { "Rail_Seat", new Vector3(0f, -0.09f, 3f) },
            { "Pier_Seat", new Vector3(0f, -0.20f, -0.20f) },
            { "Walk_Start", new Vector3(0f, 0f, 0.25f) },
            { "Walk_End", new Vector3(0f, 0f, 2.40f) },
            { "Catwalk", new Vector3(0f, 0f, 0f) },
            { "Catwalk_ShipHook", new Vector3(0f, 0f, 3f) },
            { "Catwalk_PierChocks", new Vector3(0f, 0f, 0f) },
        };

        public static readonly List<string> Log = new List<string>();

        [MenuItem("SeaSick/Art/Import pier catwalk (level 1)")]
        public static string Execute()
        {
            Log.Clear();
            try
            {
                string project = Path.GetDirectoryName(Application.dataPath);
                Directory.CreateDirectory(Path.Combine(project, Root, "Models"));
                Directory.CreateDirectory(Path.Combine(project, PrefabDir));
                File.Copy(Path.Combine(project, Source), Path.Combine(project, ModelPath), true);
                AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                if (mat == null) throw new Exception("missing " + MaterialPath);

                var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
                if (importer == null) throw new Exception("no model importer for " + ModelPath);
                importer.globalScale = 1f; importer.useFileScale = true;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importCameras = false; importer.importLights = false; importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.isReadable = true; importer.optimizeGameObjects = false;
                importer.addCollider = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                importer.SaveAndReimport();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null)
                            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), mat);
                importer.SaveAndReimport();
                model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

                // Build the wrapper.
                var kit = new GameObject("CatwalkKit");
                try
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    // Lift the FBX's top-level objects straight under the kit.
                    var kids = new List<Transform>();
                    foreach (Transform c in inst.transform) kids.Add(c);
                    foreach (var c in kids) c.SetParent(kit.transform, true);
                    UnityEngine.Object.DestroyImmediate(inst);

                    Check(kit.transform);
                    PrefabUtility.SaveAsPrefabAsset(kit, PrefabPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(kit); }
                AssetDatabase.SaveAssets();
                Log.Add("SUCCESS: " + PrefabPath);
            }
            catch (Exception e)
            {
                Log.Add("FAILED: " + e.Message);
                Debug.LogException(e);
            }
            string all = string.Join("\n", Log);
            Debug.Log("[CatwalkImport] " + all);
            return all;
        }

        static void Check(Transform kit)
        {
            foreach (var kv in Marks)
            {
                var t = kit.Find(kv.Key);
                if (t == null) throw new Exception("missing " + kv.Key);
                if ((t.localPosition - kv.Value).magnitude > Tol)
                    throw new Exception($"{kv.Key} at {t.localPosition:F3}, contract {kv.Value:F3}");
                if (Quaternion.Angle(t.localRotation, Quaternion.identity) > 0.5f)
                    throw new Exception($"{kv.Key} is rotated {t.localRotation.eulerAngles:F1}");
                if ((t.localScale - Vector3.one).magnitude > 0.001f)
                    throw new Exception($"{kv.Key} is scaled {t.localScale:F3}");
            }
            int tris = 0;
            float top = float.MinValue;
            foreach (var mf in kit.GetComponentsInChildren<MeshFilter>(true))
            {
                tris += mf.sharedMesh.triangles.Length / 3;
                if (mf.name == "Catwalk") top = mf.GetComponent<Renderer>().bounds.max.y;
                var r = mf.GetComponent<Renderer>();
                foreach (var m in r.sharedMaterials)
                    if (m == null || !m.name.StartsWith("Astra_Building"))
                        throw new Exception($"{mf.name} material {(m != null ? m.name : "null")} not remapped");
                Log.Add($"{mf.name}: {mf.sharedMesh.triangles.Length / 3} tris, bounds {r.bounds.min:F2}..{r.bounds.max:F2}");
            }
            if (tris > 1200) throw new Exception($"{tris} triangles, contract 1200");
            if (Mathf.Abs(top - 0.9f) > 0.05f) throw new Exception($"span top {top:F2}, posts should top out at 0.9 (axis?)");
            Log.Add($"markers OK, {tris} tris, span top {top:F2}");
        }
    }
}
