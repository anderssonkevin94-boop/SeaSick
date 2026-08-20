using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;

/// The cutover's scene surgery on Sea.unity: removes the old Ocean object
/// (whose components' classes no longer exist), builds the new Ocean root
/// (renderer, clipmap, physics driver, region field, weather, ripple-sim
/// stub), strips missing-script stubs everywhere, and gives the player ship
/// its Rigidbody + buoyancy probes. Idempotent.
public static class SetupOceanScene
{
    const string ScenePath = "Assets/_Project/Scenes/Sea.unity";
    const string ShaderDir = "Assets/_Project/Art/Shaders/Ocean";
    const string SettingsDir = "Assets/_Project/Settings/Resources/Ocean";

    public static string Execute()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var report = new System.Text.StringBuilder();

        // --- strip every missing-script stub in the scene ---
        int removed = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        report.AppendLine($"missing scripts removed: {removed}");

        // --- rebuild the Ocean root ---
        var oldOcean = GameObject.Find("Ocean");
        if (oldOcean != null && oldOcean.GetComponent<OceanRenderer>() == null)
        {
            Object.DestroyImmediate(oldOcean);
            report.AppendLine("old Ocean object deleted");
        }

        var ocean = GameObject.Find("Ocean");
        if (ocean == null) ocean = new GameObject("Ocean");

        var renderer = Ensure<OceanRenderer>(ocean);
        var so = new SerializedObject(renderer);
        so.FindProperty("initialSpectrumShader").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<ComputeShader>($"{ShaderDir}/InitialSpectrum.compute");
        so.FindProperty("timeEvolveShader").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<ComputeShader>($"{ShaderDir}/TimeEvolve.compute");
        so.FindProperty("fftShader").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<ComputeShader>($"{ShaderDir}/FFT.compute");
        so.FindProperty("foamShader").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<ComputeShader>($"{ShaderDir}/FoamAccumulate.compute");
        so.FindProperty("settings").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<OceanSpectrumSettings>($"{SettingsDir}/SeaState_Normal.asset");
        so.ApplyModifiedPropertiesWithoutUndo();

        Ensure<OceanPhysicsDriver>(ocean);
        Ensure<RegionField>(ocean);
        var sim = Ensure<DynamicWaterSim>(ocean);
        var simSo = new SerializedObject(sim);
        simSo.FindProperty("rippleShader").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<ComputeShader>($"{ShaderDir}/RippleSim.compute");
        simSo.ApplyModifiedPropertiesWithoutUndo();

        var ship = FindShip();
        var ctrl = Ensure<SeaStateController>(ocean);
        var cso = new SerializedObject(ctrl);
        cso.FindProperty("calm").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<OceanSpectrumSettings>($"{SettingsDir}/SeaState_Calm.asset");
        cso.FindProperty("normal").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<OceanSpectrumSettings>($"{SettingsDir}/SeaState_Normal.asset");
        cso.FindProperty("stormy").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<OceanSpectrumSettings>($"{SettingsDir}/SeaState_Stormy.asset");
        if (ship != null)
            cso.FindProperty("follow").objectReferenceValue = ship.transform;
        cso.ApplyModifiedPropertiesWithoutUndo();

        var clip = ocean.GetComponentInChildren<OceanClipmap>();
        if (clip == null)
        {
            var clipGo = new GameObject("Clipmap");
            clipGo.transform.SetParent(ocean.transform, false);
            clip = clipGo.AddComponent<OceanClipmap>();
        }
        var mso = new SerializedObject(clip);
        mso.FindProperty("material").objectReferenceValue = EnsureSurfaceMaterial();
        mso.ApplyModifiedPropertiesWithoutUndo();

        // --- ship physics ---
        if (ship != null)
        {
            var rb = Ensure<Rigidbody>(ship.gameObject);
            rb.mass = 4000f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.useGravity = true;

            var probes = Ensure<BuoyancyProbeSet>(ship.gameObject);
            if (probes.Count == 0)
            {
                // Hull footprint from the old float points: stem +9, transom
                // -8.5, beam +-2.4, rail half-beam 3.9, rail at 1.3.
                probes.SetProbes(BuoyancyProbeSet.SloopLayout(17.5f, 4.8f, -1.2f, 1.3f));
            }
            SceneDefaults.ResetToCodeDefaults(Ensure<BuoyantBody>(ship.gameObject));
            EditorUtility.SetDirty(ship.gameObject);
            report.AppendLine("ship physics: rigidbody + probe set + buoyant body");
        }
        else report.AppendLine("WARNING: no ShipMotor found");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        report.AppendLine("Sea.unity saved");
        return report.ToString();
    }

    static ShipMotor FindShip() => Object.FindFirstObjectByType<ShipMotor>();

    static T Ensure<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = go.AddComponent<T>();
        return c;
    }

    static Material EnsureSurfaceMaterial()
    {
        const string path = "Assets/_Project/Materials/OceanSurface.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            // Debug shading until the real Ocean.shader lands at the shading
            // milestone; the material asset stays, only its shader changes.
            m = new Material(Shader.Find("SeaSick/OceanDebug"));
            AssetDatabase.CreateAsset(m, path);
            AssetDatabase.SaveAssets();
        }
        return m;
    }
}
