#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SeaSick.Ship;

/// The two scene-serialised values behind "she floats above the water and the
/// wheels spin like a food processor". Targeted rather than a full
/// SetupPaddleBoat re-run, which would also push maxSpeed, the camera and the
/// freeboard back to the constants in that file.
public static class TuneWaterline
{
    [MenuItem("SeaSick/Ship/Tune Waterline")]
    public static void Run()
    {
        var motor = Object.FindAnyObjectByType<ShipMotor>();
        if (motor == null) { Debug.LogError("TuneWaterline: no ShipMotor in the open scene"); return; }

        var sb = new System.Text.StringBuilder("TuneWaterline:\n  ");
        sb.AppendLine(SetupPaddleBoat.PushProbes(motor.gameObject));

        var drive = motor.GetComponent<PaddleDrive>();
        if (drive != null)
        {
            var dso = new SerializedObject(drive);
            dso.FindProperty("maxVisualRate").floatValue = 3.6f;
            dso.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(motor.gameObject.scene);
        EditorSceneManager.SaveScene(motor.gameObject.scene);

        // Read back off fresh SerializedObjects.
        var pset = motor.GetComponent<SeaSick.Ocean.BuoyancyProbeSet>();
        if (pset != null)
        {
            var check = new SerializedObject(pset);
            var arr = check.FindProperty("probes");
            float lowest = 999f, railY = -999f;
            for (int i = 0; i < arr.arraySize; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                float y = el.FindPropertyRelative("localPosition").vector3Value.y;
                bool rail = el.FindPropertyRelative("isRail").boolValue;
                if (rail) railY = Mathf.Max(railY, y); else lowest = Mathf.Min(lowest, y);
            }
            sb.AppendLine($"  read back: lowest hull probe y {lowest:F2}, rail probe y {railY:F2}");
        }
        if (drive != null)
            sb.AppendLine($"  read back: maxVisualRate {new SerializedObject(drive).FindProperty("maxVisualRate").floatValue}"
                        + $"  ({new SerializedObject(drive).FindProperty("maxVisualRate").floatValue * 60f / (2f * Mathf.PI):F0} rpm)");
        Debug.Log(sb.ToString());
    }
}
#endif
