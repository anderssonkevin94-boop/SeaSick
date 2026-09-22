using UnityEditor;
using UnityEngine;
using SeaSick.Ocean;

/// Pushes RegionField's island-selection values onto the scene component and
/// READS THEM BACK.
///
/// A NEW [SerializeField] keeps whatever initialiser it was born with, and a
/// later edit to that initialiser never reaches a component the editor already
/// has loaded. Measured here: the source said 1500 and the live component said
/// 3000 for three runs, so the selection radius the probe was reporting on was
/// not the one in the file. Hand-editing the .unity YAML does not help either
/// until something forces a reimport.
///
/// Run in EDIT mode so it persists into Sea.unity.
public static class PushRegionField
{
    public const float SelectRadius = 1500f;
    public const float RebindDistance = 250f;

    public static void Execute()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("PushRegionField: run in EDIT mode, or it will not persist.");
            return;
        }
        var rf = Object.FindFirstObjectByType<RegionField>(FindObjectsInactive.Include);
        if (rf == null) { Debug.LogError("PushRegionField: no RegionField in the open scene"); return; }

        var so = new SerializedObject(rf);
        so.FindProperty("islandSelectRadius").floatValue = SelectRadius;
        so.FindProperty("islandRebindDistance").floatValue = RebindDistance;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rf);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rf.gameObject.scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(rf.gameObject.scene);

        // Read back off the OBJECT THE GAME WILL USE, not off the property we
        // just wrote. A write that reports success and a value that never moved
        // is the exact failure this script exists for.
        var check = new SerializedObject(rf);
        Debug.Log($"PushRegionField: islandSelectRadius = "
            + $"{check.FindProperty("islandSelectRadius").floatValue:F0} m, "
            + $"rebind = {check.FindProperty("islandRebindDistance").floatValue:F0} m "
            + $"(live property reads {rf.IslandSelectRadius:F0})");
    }
}
