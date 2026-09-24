using System.IO;
using SeaSick.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeaSick.Ship.Modular
{
    /// Editor setup for the isolated modular-ship test scene.
    ///
    ///   Menu:  SeaSick/Modular/Create Test Scene
    ///   Batch: Unity -batchmode -quit -projectPath <p>
    ///          -executeMethod SeaSick.Ship.Modular.ModularShipTestSceneSetup.CreateTestScene
    ///
    /// The scene is NOT added to EditorBuildSettings: it never ships and
    /// never replaces the playtest scene.
    public static class ModularShipTestSceneSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/Tests/ModularShipTest.unity";
        public const string MeshFolder = "Assets/_Project/Resources/ShipModules/Meshes";

        [MenuItem("SeaSick/Modular/Create Test Scene")]
        public static void CreateTestScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ConfigureImporters();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.68f, 0.78f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 500f;
            camGo.transform.SetPositionAndRotation(new Vector3(-12f, 6f, -8f), Quaternion.LookRotation(new Vector3(12f, -5f, 13f)));

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // A flat reference plane BELOW the keel (keel = -1.92 u = -0.96 m).
            // Not a waterline: buoyancy/draft are out of scope for milestone 1.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground (below keel, not a waterline)";
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.transform.position = new Vector3(0f, -1.1f, 6f);
            ground.transform.localScale = new Vector3(6f, 1f, 6f);

            var bench = new GameObject("ModularShipBench").AddComponent<ModularShipBench>();
            var so = new SerializedObject(bench);
            var camProp = so.FindProperty("cam");
            if (camProp != null) { camProp.objectReferenceValue = cam; so.ApplyModifiedPropertiesWithoutUndo(); }

            EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[ModularShip] test scene saved to {ScenePath} (not added to build settings).");
        }

        /// ModelImporter settings for every FBX under Resources/ShipModules/Meshes.
        /// Same scale settings as Editor/AstraPlaytestImport.cs (globalScale 1,
        /// useFileScale); no materials (ModularShipView assigns the shared
        /// vertex-colour material), no cameras/lights/animation, authored
        /// normals kept (the kit is flat shaded). Axis conversion is left at
        /// the importer default, as for every other Astra FBX.
        [MenuItem("SeaSick/Modular/Configure Mesh Importers")]
        public static void ConfigureImporters()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { MeshFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) continue;
                if (ModularShipModelImport.Apply(mi)) { mi.SaveAndReimport(); n++; }
            }
            Debug.Log($"[ModularShip] configured {n} mesh importer(s) under {MeshFolder}.");
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
