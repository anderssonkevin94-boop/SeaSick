using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SeaSick.Ocean2;

/// Builds (or rebuilds) OceanLab.unity — the new ocean stack's home until the
/// cutover — and the OceanQuality tier assets. The lab has a free camera, a low
/// sun for backlit-crest work later, and an Ocean root the milestones add
/// components to. Idempotent: run it again after adding components and it
/// leaves existing objects alone unless asked to reset.
public static class SetupOceanLab
{
    const string ScenePath = "Assets/_Project/Scenes/OceanLab.unity";
    const string SettingsDir = "Assets/_Project/Settings/Resources/Ocean";

    public static string Execute()
    {
        EnsureQualityAssets();

        Scene scene;
        if (File.Exists(ScenePath))
        {
            scene = EditorSceneManager.OpenScene(ScenePath);
        }
        else
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        var cam = Object.FindFirstObjectByType<Camera>();
        if (cam == null)
        {
            var camGo = new GameObject("LabCamera");
            cam = camGo.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 12f, -30f);
            cam.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
            cam.farClipPlane = 6000f; // lab needs the 5 km tiling test; game cams stay ~600
            camGo.tag = "MainCamera";
        }

        if (Object.FindFirstObjectByType<Light>() == null)
        {
            var lightGo = new GameObject("Sun");
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(35f, -140f, 0f);
        }

        if (GameObject.Find("Ocean") == null)
            new GameObject("Ocean");

        EditorSceneManager.SaveScene(scene, ScenePath);
        return $"OceanLab ready at {ScenePath}; quality assets in {SettingsDir}";
    }

    static void EnsureQualityAssets()
    {
        Directory.CreateDirectory(SettingsDir);

        var mobile = AssetDatabase.LoadAssetAtPath<OceanQuality>($"{SettingsDir}/OceanQuality_Mobile.asset");
        if (mobile == null)
        {
            mobile = ScriptableObject.CreateInstance<OceanQuality>();
            mobile.fftSize = 128;
            mobile.clipmapRings = 5;
            mobile.rippleSimResolution = 256;
            mobile.rippleSimExtent = 60f;
            mobile.displacementFadeDistance = 350f;
            AssetDatabase.CreateAsset(mobile, $"{SettingsDir}/OceanQuality_Mobile.asset");
        }

        var pc = AssetDatabase.LoadAssetAtPath<OceanQuality>($"{SettingsDir}/OceanQuality_PC.asset");
        if (pc == null)
        {
            pc = ScriptableObject.CreateInstance<OceanQuality>();
            AssetDatabase.CreateAsset(pc, $"{SettingsDir}/OceanQuality_PC.asset");
        }

        AssetDatabase.SaveAssets();
    }
}
