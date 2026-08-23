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

        var cam = Object.FindFirstObjectByType<Camera>();
        if (cam == null)
        {
            var go = new GameObject("LabCamera");
            cam = go.AddComponent<Camera>();
            go.tag = "MainCamera";
            cam.orthographic = true;
            cam.farClipPlane = 1000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.1f, 0.14f);
        }
        cam.transform.position = new Vector3(vis.centre.x, 200f, vis.centre.y);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.orthographicSize = vis.extent;

        vis.Regenerate();
        EditorSceneManager.SaveScene(SceneManager(), ScenePath);
        AssetDatabase.SaveAssets();
        return "TerrainLab ready; range [" + vis.LastMin + ", " + vis.LastMax + "]";
    }

    static UnityEngine.SceneManagement.Scene SceneManager() => EditorSceneManager.GetActiveScene();
}
