using SeaSick.Ship;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// The scene serialized the first values these fields ever had, and serialized
/// values beat C# field initializers forever. Push them explicitly.
public static class ApplyFreeboardTuning
{
    public static void Execute()
    {
        var motor = Object.FindAnyObjectByType<ShipMotor>();
        if (motor == null) { Debug.LogError("ApplyFreeboardTuning: no ShipMotor"); return; }

        Set(motor, ("sinkAtMarkedLine", 0.50f), ("sinkPerOverload", 0.72f),
                   ("sinkAtFullBilge", 0.42f), ("railHeight", 1.30f),
                   ("railHalfBeam", 3.9f));

        var bilge = motor.GetComponent<Bilge>();
        if (bilge != null)
            Set(bilge, ("ingressPerMetre", 0.28f), ("maxEffectiveImmersion", 0.45f));

        EditorSceneManager.MarkSceneDirty(motor.gameObject.scene);
        EditorSceneManager.SaveScene(motor.gameObject.scene);
        Debug.Log("ApplyFreeboardTuning: scene saved");
    }

    static void Set(Object target, params (string name, float value)[] fields)
    {
        var so = new SerializedObject(target);
        foreach (var (name, value) in fields)
        {
            var prop = so.FindProperty(name);
            if (prop == null) { Debug.LogError($"  MISSING {target.GetType().Name}.{name}"); continue; }
            float was = prop.floatValue;
            prop.floatValue = value;
            Debug.Log($"  {target.GetType().Name}.{name}: {was} -> {value}");
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}
