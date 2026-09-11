using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SeaSick.Terrain;

/// Puts the distant-land field in the scene and opens the camera far plane
/// enough to see it. Idempotent — re-run it after changing anything.
///
/// **The far plane is the actual reason nothing distant renders**, and it was
/// worth measuring before building anything: it is 600 m. The streamed terrain
/// reaches 1024 m and the ocean clipmap reaches 8192 m, so BOTH were already
/// being drawn further than the camera would show, and the scene's fog ending
/// at 1500 m never even came into play. Land at 700 m was not fogged out or
/// unloaded; it was clipped.
///
/// 600 m is also a known trap in this project rather than an oversight — the
/// island overview camera raises its own far plane while it is up, because a
/// camera far enough back to frame a 535 m island renders a flat grey
/// rectangle. This makes the sailing camera agree with the overview one.
public static class SetupHorizon
{
    const string MatPath = "Assets/_Project/Materials/Horizon.mat";
    const string ShaderName = "SeaSick/Horizon";
    /// The scene's linear fog ends at 1500 m and the ocean displacement fades
    /// out at 2600 m, so an 8600 m far plane was mostly wasted depth
    /// precision — but neither of those numbers is what the far plane has to
    /// contain. `HorizonField`'s own `outerRadius`, set below, is 8000 m: real
    /// visible geometry, not haze, and clipping it early would cut a sharp
    /// edge out of the horizon rather than a soft fade. 8000 m x 1.1 (~10 %
    /// margin) is 8800 m, which is what this is — barely different from the
    /// old value, because the horizon mesh, not the fog or the displacement
    /// falloff, was already the thing setting the floor.
    const float FarClip = 8800f;

    public static string Execute()
    {
        var sb = new StringBuilder("=== SetupHorizon ===\n");

        var settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>(
            "Assets/_Project/Settings/Terrain/TerrainSettings.asset");
        if (settings == null) return "no TerrainSettings asset";

        // **viewRadius is deliberately NOT touched here, and that is a
        // correction.** It was raised 8 -> 12 to push real terrain out to
        // 1536 m, on the theory that the coarse field was being asked to draw
        // land at 1.1 km and looked like a grey wall doing it. The grey wall
        // was real; the diagnosis was wrong. It was the field being centred on
        // Camera.main while the picture was taken from a second camera 1.7 km
        // away — the shot camera was standing inside the annulus looking at
        // the far side of it. Fixing the centring removed the wall at
        // viewRadius 8.
        //
        // And 12 is not free: `TerrainPerfProbe` measured the worst chunk
        // crossing at 9.25 ms against this project's own 4 ms gate, up from
        // 1.63 ms, because the ring of chunks to replan grows with the square
        // of the radius. Steady-state drawing barely moved (38,013 visible
        // verts against 39,435) — the whole cost was streaming spikes. Paying
        // that for a problem that turned out to be a one-line bug would have
        // been a bad trade made invisibly.
        sb.AppendLine($"viewRadius left at {settings.viewRadius} "
                      + $"({settings.viewRadius * settings.chunkSize:F0} m of real terrain) "
                      + "— see the note in this file before raising it");

        // --- material ------------------------------------------------------
        var shader = Shader.Find(ShaderName);
        if (shader == null) return "shader not found: " + ShaderName;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(shader);
            System.IO.Directory.CreateDirectory("Assets/_Project/Materials");
            AssetDatabase.CreateAsset(mat, MatPath);
            sb.AppendLine("created " + MatPath);
        }
        else if (mat.shader != shader) mat.shader = shader;
        mat.SetFloat("_HazeDistance", 1700f);
        mat.SetFloat("_MaxHaze", 0.95f);
        mat.SetFloat("_BaseHaze", 0.35f);
        mat.SetFloat("_BaseHazeHeight", 110f);
        mat.SetColor("_LowColour", new Color(0.24f, 0.29f, 0.27f));
        mat.SetColor("_HighColour", new Color(0.38f, 0.41f, 0.46f));
        EditorUtility.SetDirty(mat);

        // --- the field itself ----------------------------------------------
        var go = GameObject.Find("HorizonField");
        if (go == null)
        {
            go = new GameObject("HorizonField");
            sb.AppendLine("created HorizonField");
        }
        var field = go.GetComponent<HorizonField>();
        if (field == null) field = go.AddComponent<HorizonField>();

        var so = new SerializedObject(field);
        so.FindProperty("settings").objectReferenceValue = settings;
        so.FindProperty("material").objectReferenceValue = mat;
        // Inner edge past the streamed chunks (viewRadius x chunkSize), so the
        // two never contest the same pixels.
        float streamed = settings.viewRadius * settings.chunkSize;
        so.FindProperty("innerRadius").floatValue = streamed + 80f;
        so.FindProperty("outerRadius").floatValue = 8000f;
        so.FindProperty("rings").intValue = 64;
        so.FindProperty("segments").intValue = 192;
        so.FindProperty("innerSink").floatValue = 30f;
        so.FindProperty("sinkFadeEnd").floatValue = 2400f;
        so.FindProperty("seaSink").floatValue = 6f;
        so.FindProperty("rebuildStep").floatValue = 128f;
        so.ApplyModifiedPropertiesWithoutUndo();
        sb.AppendLine($"field: {streamed + 80f:F0} m -> 8000 m, 64 x 192 "
                      + $"({64 * 192} verts), streamed terrain ends at {streamed:F0} m");

        // --- scenery beyond the ground ---------------------------------------
        var cullGo = GameObject.Find("DistantSceneryCull");
        if (cullGo == null)
        {
            cullGo = new GameObject("DistantSceneryCull");
            sb.AppendLine("created DistantSceneryCull");
        }
        var cull = cullGo.GetComponent<DistantSceneryCull>();
        if (cull == null) cull = cullGo.AddComponent<DistantSceneryCull>();
        var cso = new SerializedObject(cull);
        cso.FindProperty("settings").objectReferenceValue = settings;
        cso.FindProperty("margin").floatValue = 120f;
        cso.ApplyModifiedPropertiesWithoutUndo();
        sb.AppendLine($"scenery culled past {settings.viewRadius * settings.chunkSize + 120f:F0} m "
                      + "(props are spawned to 3 km but ground only reaches the streamed edge)");

        // --- the far plane ---------------------------------------------------
        var cam = Camera.main;
        if (cam == null) { sb.AppendLine("NO MAIN CAMERA — far plane not raised"); }
        else
        {
            sb.AppendLine($"far clip {cam.farClipPlane:F0} -> {FarClip:F0} m");
            cam.farClipPlane = FarClip;
            EditorUtility.SetDirty(cam);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        Debug.Log(sb.ToString());
        return sb.ToString();
    }
}
