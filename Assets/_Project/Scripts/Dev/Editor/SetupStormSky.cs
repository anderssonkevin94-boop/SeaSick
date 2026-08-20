using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// Wires the storm sky into Sea.unity: the sky material, the render settings
/// it needs, SkyDirector, and StormSpray on the ship.
///
/// Idempotent, and it RESETS the components to their C# defaults every run.
/// That matters here more than anywhere: Unity serialises a component's field
/// values into the .unity file the moment it is added, and those beat the C#
/// initialisers forever after — re-tuning in code otherwise does nothing at
/// all. Run this again after changing any default and the scene picks it up.
public static class SetupStormSky
{
    const string SkyMatPath = "Assets/_Project/Art/Sky.mat";

    public static void Execute()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // --- The sky material -------------------------------------------
        var shader = Shader.Find("SeaSick/Sky");
        if (shader == null) { Debug.LogError("SetupStormSky: SeaSick/Sky not found"); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
        if (mat == null)
        {
            mat = new Material(shader) { name = "Sky" };
            AssetDatabase.CreateAsset(mat, SkyMatPath);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
            EditorUtility.SetDirty(mat);
        }

        // --- Render settings ---------------------------------------------
        RenderSettings.skybox = mat;
        // Ambient from a procedural skybox needs DynamicGI.UpdateEnvironment
        // every frame, which costs milliseconds on a phone. SkyDirector sets
        // the three colours itself instead.
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;

        // --- SkyDirector ---------------------------------------------------
        var world = GameObject.Find("World");
        var skyGo = GameObject.Find("Sky");
        if (skyGo == null)
        {
            skyGo = new GameObject("Sky");
            if (world != null) skyGo.transform.SetParent(world.transform, false);
        }

        var dir = skyGo.GetComponent<SeaSick.World.SkyDirector>();
        if (dir == null) dir = skyGo.AddComponent<SeaSick.World.SkyDirector>();
        SceneDefaults.ResetToCodeDefaults(dir);

        var motor = Object.FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        Light sun = null;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { sun = l; break; }

        var so = new SerializedObject(dir);
        so.FindProperty("ship").objectReferenceValue = motor != null ? motor.transform : null;
        so.FindProperty("sun").objectReferenceValue = sun;
        so.ApplyModifiedPropertiesWithoutUndo();

        // --- StormSpray ------------------------------------------------------
        if (motor != null)
        {
            var spray = motor.GetComponent<SeaSick.Ocean.StormSpray>();
            if (spray == null) spray = motor.gameObject.AddComponent<SeaSick.Ocean.StormSpray>();
            SceneDefaults.ResetToCodeDefaults(spray);
        }

        // --- ChaseCamera picked up new fields --------------------------------
        var cam = Object.FindAnyObjectByType<SeaSick.CameraRig.ChaseCamera>();
        if (cam != null)
        {
            // Only the new sea-response fields — the framing values on this one
            // were hand-tuned in the scene and must survive.
            var cso = new SerializedObject(cam);
            SetFloat(cso, "heaveFollow", 1.5f);
            SetFloat(cso, "heaveShare", 1f);
            SetFloat(cso, "stormDrop", 9f);
            SetFloat(cso, "stormPullIn", 6f);
            SetFloat(cso, "stormResponse", 1.2f);
            cso.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        Debug.Log($"STORMSKY SETUP: skybox={mat.shader.name} ambient={RenderSettings.ambientMode} " +
                  $"fog={RenderSettings.fogStartDistance:F0}-{RenderSettings.fogEndDistance:F0} " +
                  $"sky={skyGo.name} ship={(motor != null ? motor.name : "MISSING")} " +
                  $"sun={(sun != null ? sun.name : "MISSING")} " +
                  $"camera={(cam != null ? cam.name : "MISSING")}");
    }

    static void SetFloat(SerializedObject so, string path, float v)
    {
        var p = so.FindProperty(path);
        if (p != null) p.floatValue = v;
        else Debug.LogWarning($"SetupStormSky: no property '{path}'");
    }

}
