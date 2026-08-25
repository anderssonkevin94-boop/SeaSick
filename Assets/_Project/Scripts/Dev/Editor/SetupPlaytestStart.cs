using SeaSick.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Puts the PlaytestStart object into Sea.unity so pressing play drops the
/// ship in the western storm instead of at home. Idempotent: re-run it after
/// changing the component's defaults and it pushes them through
/// SerializedObject, because a value already serialised into the scene beats
/// the C# field initialiser forever.
public static class SetupPlaytestStart
{
    const string ScenePath = "Assets/_Project/Scenes/Sea.unity";
    const string ObjectName = "PlaytestStart";

    /// The search window and the depth bar. WestProbe measured the western
    /// coastline: an island chain sits across the approach from 800 m to
    /// 2200 m, and the open water starts around 2400 m. The component searches
    /// rather than trusting a distance, so these are limits, not a position.
    const float MinDistance = 1300f;
    const float MaxDistance = 6000f;
    const float RequiredDepth = 130f;
    const float ClearRadius = 800f;

    public static string Execute()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);

        var go = GameObject.Find(ObjectName);
        if (go == null) go = new GameObject(ObjectName);
        var comp = go.GetComponent<PlaytestStart>();
        if (comp == null) comp = go.AddComponent<PlaytestStart>();

        var so = new SerializedObject(comp);
        so.FindProperty("active").boolValue = true;
        so.FindProperty("bearing").vector2Value = new Vector2(-1f, 0f);
        so.FindProperty("minDistance").floatValue = MinDistance;
        so.FindProperty("maxDistance").floatValue = MaxDistance;
        so.FindProperty("requiredDepth").floatValue = RequiredDepth;
        so.FindProperty("clearRadius").floatValue = ClearRadius;
        so.FindProperty("maxLateral").floatValue = 1600f;
        so.FindProperty("faceOutward").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        return ObjectName + ": active, searching west from " + MinDistance + " m for "
             + RequiredDepth + " m of water clear for " + ClearRadius + " m, bow-on";
    }
}
