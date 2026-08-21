using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Pushes the storm-feel tuning onto Sea.unity. These fields are all
/// scene-serialised (ShipMotor.acceleration 2.6, ChaseCamera.stormDrop 9 and
/// stormPullIn 6 are baked into the .unity file), so editing the C# defaults
/// does nothing at all — the serialization trap, for the umpteenth time.
///
/// Both changes answer the same field report: in a storm the camera came in
/// "super close and super low" and the ship "drops from 20 to 2".
///
///  - ChaseCamera: the scene frames at distance 20 / height 13, NOT the code
///    defaults of 25/19. Subtracting the old stormDrop 9 put the lens 4 m over
///    a ship being thrown around by 20 m waves. Halving the storm response
///    keeps the sea looming without burying the camera in it.
///  - ShipMotor.acceleration stays at the scene's 2.6. 3.6 was measured and
///    reverted (see below): more propulsion just drove the bow under.
public static class TuneStormFeel
{
    public static string Execute()
    {
        var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Sea.unity");
        var report = new System.Text.StringBuilder();

        var motor = Object.FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        if (motor != null)
        {
            var so = new SerializedObject(motor);
            // Left at the scene's tuned 2.6. Raising it to 3.6 was tried and
            // reverted: she charges harder, meanSpeed in a severity-1.0 head
            // sea went 7.8 -> 9.5, and the bow started burying (deckOverMax
            // -0.24 m -> +0.80 m). The stall was never an acceleration
            // problem — BuoyantBody.plowSpeedFloor is what fixes it.
            report.AppendLine(Set(so, "acceleration", 2.6f));
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        else report.AppendLine("WARNING: no ShipMotor");

        var cam = Object.FindAnyObjectByType<SeaSick.CameraRig.ChaseCamera>();
        if (cam != null)
        {
            var so = new SerializedObject(cam);
            report.AppendLine(Set(so, "stormDrop", 4f));
            report.AppendLine(Set(so, "stormPullIn", 3f));
            so.ApplyModifiedPropertiesWithoutUndo();
            var d = new SerializedObject(cam);
            report.AppendLine(string.Format(
                "  storm framing is now {0:F0} m astern, {1:F0} m up (was 14 / 4)",
                d.FindProperty("distance").floatValue - 3f,
                d.FindProperty("height").floatValue - 4f));
        }
        else report.AppendLine("WARNING: no ChaseCamera");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        report.AppendLine("Sea.unity saved");
        return report.ToString();
    }

    static string Set(SerializedObject so, string field, float v)
    {
        var p = so.FindProperty(field);
        if (p == null) return string.Format("  MISSING {0}", field);
        float was = p.floatValue;
        p.floatValue = v;
        return string.Format("  {0}: {1} -> {2}", field, was, v);
    }
}
