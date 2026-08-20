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

        var ocean = GameObject.Find("Ocean");
        if (ocean == null) ocean = new GameObject("Ocean");
        WireOcean(ocean);

        EditorSceneManager.SaveScene(scene, ScenePath);
        return $"OceanLab ready at {ScenePath}; quality assets in {SettingsDir}";
    }

    static void WireOcean(GameObject ocean)
    {
        var renderer = ocean.GetComponent<OceanRenderer>();
        if (renderer == null) renderer = ocean.AddComponent<OceanRenderer>();

        var so = new SerializedObject(renderer);
        so.FindProperty("initialSpectrumShader").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_Project/Art/Shaders/Ocean/InitialSpectrum.compute");
        so.FindProperty("timeEvolveShader").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_Project/Art/Shaders/Ocean/TimeEvolve.compute");
        so.FindProperty("fftShader").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_Project/Art/Shaders/Ocean/FFT.compute");
        so.FindProperty("settings").objectReferenceValue = EnsureTestSeaState();
        so.ApplyModifiedPropertiesWithoutUndo();

        var meshGo = GameObject.Find("DebugSea");
        if (meshGo == null)
        {
            meshGo = new GameObject("DebugSea");
            meshGo.transform.SetParent(ocean.transform, false);
            meshGo.AddComponent<MeshFilter>();
            var mr = meshGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = EnsureDebugMaterial();
            meshGo.AddComponent<DebugSeaMesh>();
        }
    }

    static OceanSpectrumSettings EnsureTestSeaState()
    {
        const string path = SettingsDir + "/SeaState_Test.asset";
        var s = AssetDatabase.LoadAssetAtPath<OceanSpectrumSettings>(path);
        if (s == null)
        {
            s = ScriptableObject.CreateInstance<OceanSpectrumSettings>();
            s.windSpeed = 12f; s.fetchKm = 100f; // the "Normal" validation triple
            AssetDatabase.CreateAsset(s, path);
            AssetDatabase.SaveAssets();
        }
        return s;
    }

    static Material EnsureDebugMaterial()
    {
        const string path = "Assets/_Project/Materials/OceanDebug.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var shader = Shader.Find("SeaSick/OceanDebug");
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
            AssetDatabase.SaveAssets();
        }
        return m;
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
