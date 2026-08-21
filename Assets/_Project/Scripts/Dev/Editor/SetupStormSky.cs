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

        // --- The ocean's own storm palette ------------------------------
        //
        // A material asset snapshots a shader property's default at the moment
        // the property is created, and changing that default later does NOT
        // reach materials that already exist. Verified by reading the material
        // back at runtime: _StormDeep still returned (0.020, 0.045, 0.055) long
        // after the shader said (0.058, 0.086, 0.098), and the near water was
        // rendering black in a steep sea because of it. Same trap the scene
        // plays with serialized fields; push the values explicitly.
        foreach (var guid in AssetDatabase.FindAssets("t:Material"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var om = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (om == null || om.shader == null || om.shader.name != "SeaSick/Ocean") continue;

            om.SetColor("_StormDeep", new Color(0.200f, 0.235f, 0.245f));
            om.SetColor("_StormShallow", new Color(0.260f, 0.300f, 0.310f));
            om.SetColor("_StormCrest", new Color(0.86f, 0.88f, 0.89f));
            om.SetFloat("_SkyReflect", 0.45f);
            om.SetFloat("_SkySoft", 0.65f);
            EditorUtility.SetDirty(om);
            Debug.Log($"STORMSKY: ocean palette pushed to {path} — " +
                      $"_StormDeep now {om.GetColor("_StormDeep")}");
        }

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

        // --- Hull seating ----------------------------------------------------
        if (motor != null)
        {
            // (The old kinematic seating pushes are gone with the fields —
            // the rigidbody hull has no verticalResponse/maxSeatError.)
        }

        // --- SpeedJuice gained a field ---------------------------------------
        if (motor != null)
        {
            var juice = motor.GetComponent<SeaSick.Ship.SpeedJuice>();
            if (juice != null)
            {
                // Only the new field. sprayFullRate and wakeFullRate were tuned
                // by hand in the scene (55 and 30 against 130 and 85 in code)
                // and a blanket reset would quietly throw that away.
                var jso = new SerializedObject(juice);
                SetFloat(jso, "seaThresholdScale", 3.5f);
                jso.ApplyModifiedPropertiesWithoutUndo();
            }
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
            // Halved 2026-08-21: the scene frames at height 13, so a drop of 9
            // put the lens 4 m above a ship in 20 m seas. See TuneStormFeel.
            SetFloat(cso, "stormDrop", 4f);
            SetFloat(cso, "stormPullIn", 3f);
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
