using SeaSick.Ocean;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Pushes the sets-and-lulls values onto the SeaStateController in Sea.unity.
///
/// This exists because of the trap this project keeps re-learning: a scene
/// component holds the value it was deserialised with, and changing the C#
/// field initialiser afterwards NEVER reaches it. The field does not even have
/// to be present in the .unity file on disk -- once the scene has been loaded
/// with a given default, that is the value, and editing the source changes
/// nothing at all.
///
/// It cost a full diagnosis cycle here. setDepth was lowered 0.32 -> 0.10 in
/// source, the log line proved the NEW code was running, and severity still sat
/// pinned at 0.85 exactly, because setDepth itself was still 0.32 underneath.
///
/// SETS ARE NOT A PERCENTAGE OF WAVE HEIGHT. Severity maps to Hs through
/// LerpFrom(normal, stormy, (severity - 0.5) * 2), and normal is Hs 3.5 against
/// stormy's 65, so the top of the range is brutally nonlinear:
///
///     severity 1.00 -> Hs 65      0.90 -> Hs 53
///     severity 0.85 -> Hs 46      0.70 -> Hs 28
///
/// So setDepth 0.32 does not take a third off the waves, it takes 57% off them.
/// That is what "we have lost what made the general ocean good, now it's just
/// very very flat" was: the storm spending most of its life at HEAVY instead of
/// MOUNTAINOUS. Re-run this after touching either value.
public static class TuneStormSets
{
    const string ScenePath = "Assets/_Project/Scenes/Sea.unity";

    const float SetDepth = 0.10f;   // ~19% swing in Hs: a set, not a collapse
    const float SetPeriod = 70f;

    public static string Execute()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);

        var ctrl = Object.FindAnyObjectByType<SeaStateController>();
        if (ctrl == null) return "no SeaStateController in " + ScenePath;

        var so = new SerializedObject(ctrl);
        var depth = so.FindProperty("setDepth");
        var period = so.FindProperty("setPeriod");
        if (depth == null || period == null) return "setDepth/setPeriod not found on SeaStateController";
        float wasDepth = depth.floatValue, wasPeriod = period.floatValue;
        depth.floatValue = SetDepth;
        period.floatValue = SetPeriod;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Read back. A push that silently did nothing is the entire reason this
        // file exists.
        var check = new SerializedObject(ctrl);
        float nowDepth = check.FindProperty("setDepth").floatValue;
        float nowPeriod = check.FindProperty("setPeriod").floatValue;

        EditorUtility.SetDirty(ctrl);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);

        return $"setDepth {wasDepth:F2} -> {nowDepth:F2}, setPeriod {wasPeriod:F0} -> {nowPeriod:F0}"
             + $"  (lull floor now about Hs {Mathf.Lerp(3.5f, 65f, ((1f - SetDepth) - 0.5f) * 2f):F0} m"
             + $" against {65f:F0} at the top)";
    }
}
