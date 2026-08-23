using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SeaSick.Terrain;

/// Builds (or reopens) TerrainLab.unity: a top-down camera over a
/// TerrainMapVisualiser quad, plus the TerrainSettings asset. Idempotent.
public static class SetupTerrainLab
{
    const string ScenePath = "Assets/_Project/Scenes/TerrainLab.unity";
    const string SettingsPath = "Assets/_Project/Settings/Terrain/TerrainSettings.asset";
    const string MaterialPath = "Assets/_Project/Materials/TerrainVertexColor.mat";

    public static string Execute()
    {
        var settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>(SettingsPath);
        if (settings == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            settings = ScriptableObject.CreateInstance<TerrainSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }

        if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
        else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var vis = Object.FindFirstObjectByType<TerrainMapVisualiser>();
        if (vis == null)
        {
            vis = new GameObject("TerrainMap").AddComponent<TerrainMapVisualiser>();
            vis.settings = settings;
        }

        // Material for the chunk meshes.
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            mat = new Material(Shader.Find("SeaSick/Terrain Vertex Color"));
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }

        var preview = Object.FindFirstObjectByType<TerrainChunkPreview>();
        if (preview == null)
        {
            preview = new GameObject("TerrainChunks").AddComponent<TerrainChunkPreview>();
            preview.settings = settings;
        }
        preview.material = mat;
        preview.chunksPerSide = 7;

        var sun = Object.FindFirstObjectByType<Light>();
        if (sun == null)
        {
            sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            sun.shadows = LightShadows.Soft;
        }

        // Perspective camera looking at the preview island; the map quad at
        // y=0 doubles as a stand-in water plane.
        var cam = Object.FindFirstObjectByType<Camera>();
        if (cam == null)
        {
            var go = new GameObject("LabCamera");
            cam = go.AddComponent<Camera>();
            go.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.7f, 0.9f);
        }
        cam.orthographic = false;
        cam.fieldOfView = 50f;
        cam.farClipPlane = 6000f;
        cam.transform.position = new Vector3(preview.centre.x - 500f, 260f, preview.centre.y - 650f);
        cam.transform.LookAt(new Vector3(preview.centre.x, 10f, preview.centre.y));
        vis.stage = TerrainMapStage.FinalHeight;

        preview.Rebuild();
        vis.Regenerate();
        EditorSceneManager.SaveScene(SceneManager(), ScenePath);
        AssetDatabase.SaveAssets();
        return "TerrainLab ready; map range [" + vis.LastMin + ", " + vis.LastMax + "], chunk verts=" + preview.VertexCount;
    }

    static UnityEngine.SceneManagement.Scene SceneManager() => EditorSceneManager.GetActiveScene();
}
