using SeaSick.Ship;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Pushes the paddle boat's top speed from SetupPaddleBoat.MaxSpeed onto the
/// ShipMotor in Sea.unity, without re-running the whole cutover.
///
/// It has to go through SerializedObject: the scene serialised maxSpeed when
/// the component was added and that value beats the C# field initialiser
/// forever. Reads the same constant SetupPaddleBoat derives from Scale, so
/// there is still exactly one number.
public static class ApplyShipSpeed
{
    const string ScenePath = "Assets/_Project/Scenes/Sea.unity";

    public static string Execute()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);

        var motor = Object.FindFirstObjectByType<ShipMotor>();
        if (motor == null) return "no ShipMotor in Sea.unity";

        var so = new SerializedObject(motor);
        var prop = so.FindProperty("maxSpeed");
        float was = prop.floatValue;
        prop.floatValue = SetupPaddleBoat.MaxSpeed;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Read it back. A push that silently did nothing is the whole reason
        // this project distrusts serialised tuning values.
        var check = new SerializedObject(motor);
        float now = check.FindProperty("maxSpeed").floatValue;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        return $"ShipMotor.maxSpeed {was:F2} -> {now:F2} m/s (wanted {SetupPaddleBoat.MaxSpeed:F2})";
    }
}
