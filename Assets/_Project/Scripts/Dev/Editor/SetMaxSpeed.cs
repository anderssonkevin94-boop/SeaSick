using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// One-off: pushes ShipMotor.maxSpeed onto Sea.unity and nothing else.
/// The field is scene-serialised (it sat at 21, not the C# default 18), so
/// editing the default would not have reached it.
///
/// Experimental knob, deliberately kept out of TuneStormFeel so it does not
/// read as a tuning decision. To put her back: change Speed to 21 and re-run.
public static class SetMaxSpeed
{
    const float Speed = 21f;

    public static string Execute()
    {
        var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Sea.unity");
        var motor = Object.FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        if (motor == null) return "WARNING: no ShipMotor in Sea.unity";

        var so = new SerializedObject(motor);
        var p = so.FindProperty("maxSpeed");
        if (p == null) return "WARNING: no maxSpeed field";
        float was = p.floatValue;
        p.floatValue = Speed;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return string.Format("maxSpeed: {0} -> {1}   (surf ceiling is maxSpeed x surfOvershoot, so {2:F0} m/s)\nSea.unity saved",
            was, Speed, Speed * 1.25f);
    }
}
