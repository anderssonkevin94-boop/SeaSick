using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace SeaSick.Dev
{
    /// Imports only the standalone September art drop. Never rewrites the live game scene.
    public static class SettlementAssetImport
    {
        const string Kit = "Assets/_Project/Art/SettlementKitV1";
        const string Crew = "Assets/_Project/Art/CrewWeatheredV2";
        const float Scale = 1.7f / 1.93f;
        [Serializable] public class Manifest { public Entry[] assets; }
        [Serializable] public class Entry { public string id; public int triangles; public Marker[] markers; }
        [Serializable] public class Marker { public string name; public string role; public float[] position_z_up; }

        [MenuItem("SeaSick/Art/Import settlement and weathered sailor")]
        public static void Execute()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit play mode before importing.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Folder(Kit + "/Prefabs"); Folder(Crew + "/Prefabs");
            var shader = Shader.Find("SeaSick/Crew Vertex Color");
            if (!shader || ShaderUtil.ShaderHasError(shader)) throw new Exception("Vertex colour shader missing or invalid.");
            string matPath = Kit + "/VertexPalette.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, matPath); }
            material.SetColor("_BaseColor", Color.white);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText("docs/art-direction/settlement-kit/v1/manifest.json"));
            var report = new StringBuilder("Unity settlement import validation\nScale: 1.7/1.93 applied uniformly to prefab model children.\n");
            // Source GLBs contain 40 zero-area flame-tip triangles per campfire.
            // Unity removes precisely these; see unity-source-geometry-audit.json.
            foreach (var entry in manifest.assets) Import(Kit, entry.id, entry.triangles - (entry.id.StartsWith("campfire_", StringComparison.Ordinal) ? 40 : 0), entry.markers, material, report);
            Import(Crew, "WeatheredSailor", 2210, Array.Empty<Marker>(), material, report);
            File.WriteAllText("docs/art-direction/settlement-kit/v1/unity-import-validation.txt", report.ToString());
            CreateReview(manifest);
            Debug.Log("[SettlementImport] PASS: 16 prefabs, vertex colours, triangle counts and marker counts verified.");
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void Import(string folder, string id, int expectedTriangles, Marker[] markers, Material material, StringBuilder report)
        {
            string path = folder + "/Models/" + id + ".fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!importer) throw new Exception("Missing FBX " + path);
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false; importer.importLights = false;
            importer.importBlendShapes = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.isReadable = false;
            importer.SaveAndReimport();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject(id);
            SceneManager.MoveGameObjectToScene(root, scene);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                model.transform.SetParent(root.transform, false);
                model.transform.localScale = Vector3.one * Scale;
                int triangles = 0;
                foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = filter.sharedMesh;
                    triangles += mesh.triangles.Length / 3;
                    if (mesh.colors.Length != mesh.vertexCount) throw new Exception(id + ": vertex colours missing");
                    if (mesh.subMeshCount != 1) throw new Exception(id + ": palette was not consolidated");
                    filter.GetComponent<MeshRenderer>().sharedMaterial = material;
                }
                if (triangles != expectedTriangles) throw new Exception(id + ": triangles " + triangles + " expected " + expectedTriangles);
                var transforms = model.GetComponentsInChildren<Transform>(true);
                foreach (var marker in markers)
                {
                    var matches = transforms.Where(t => t.name == marker.name || t.name.StartsWith(marker.name + ".", StringComparison.Ordinal)).ToArray();
                    if (matches.Length != 1) throw new Exception(id + ": missing/ambiguous marker " + marker.name);
                    var p = matches[0].position / Scale;
                    var authored = marker.position_z_up;
                    var expected = new Vector3(-authored[0], authored[2], -authored[1]);
                    if (Vector3.Distance(p, expected) > .002f) throw new Exception(id + ": marker axis/scale mismatch " + marker.name);
                    report.AppendLine("  " + marker.role + " " + marker.name + " authored metres " + p.ToString("F3"));
                }
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                if (id == "WeatheredSailor" && Mathf.Abs(bounds.size.y - 1.7f) > .035f) throw new Exception("Sailor scale incorrect: " + bounds.size);
                PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/" + id + ".prefab");
                report.AppendLine(id + ": PASS " + triangles + " triangles; " + renderers.Length + " meshes; " + markers.Length + " markers; game bounds " + bounds.size.ToString("F3"));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static void CreateReview(Manifest manifest)
        {
            var original = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.5f, .56f, .65f);
                RenderSettings.fog = false;
                string[] order = { "campfire_01", "campfire_02", "campfire_03", "campfire_04", "campfire_05", "hut_01", "hut_02", "hut_03", "sawmill", "storage", "farm_01", "farm_02", "farm_03", "blacksmith", "kitchen" };
                for (int i = 0; i < order.Length; i++)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + "/Prefabs/" + order[i] + ".prefab");
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                    go.transform.position = new Vector3((i % 5 - 2) * 12, 0, (i / 5) * 13);
                    var sailor = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Crew + "/Prefabs/WeatheredSailor.prefab"), scene);
                    sailor.transform.position = go.transform.position + new Vector3(-3, 0, -4);
                }
                var sun = new GameObject("Review Sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.3f;
                sun.transform.rotation = Quaternion.Euler(48, -35, 0);
                var camera = new GameObject("Review Camera").AddComponent<Camera>();
                camera.transform.position = new Vector3(40, 50, 80);
                camera.transform.LookAt(new Vector3(0, 0, 13));
                camera.scene = scene;
                camera.orthographic = true; camera.orthographicSize = 25;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.15f, .23f, .28f);
                camera.nearClipPlane = .1f; camera.farClipPlane = 300;
                EditorSceneManager.SaveScene(scene, "Assets/_Project/Scenes/SettlementAssetReview.unity");
                // Render away from the still-open game scene so its objects cannot
                // appear in this offscreen gallery; the saved review stays at origin.
                foreach (var sceneRoot in scene.GetRootGameObjects())
                    sceneRoot.transform.position += new Vector3(10000, 0, 10000);
                var rt = new RenderTexture(1920, 1080, 24);
                var previous = RenderTexture.active;
                var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                try
                {
                    camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                    texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
                    File.WriteAllBytes("docs/art-direction/settlement-kit/v1/unity-import-review.png", texture.EncodeToPNG());
                }
                finally { camera.targetTexture = null; RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(rt); }
            }
            finally { SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
