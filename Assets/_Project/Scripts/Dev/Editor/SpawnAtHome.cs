using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using SeaSick.Dev;

/// Puts the ship back at her home mooring on play.
///
/// PlaytestStart exists to skip the sail west to the storm water -- it
/// searches out along a bearing for 130 m of open sea and drops her there,
/// which is right when the ocean is what you are working on and wrong when
/// the islands are. Turning it off returns her to wherever the scene puts
/// her, which is home.
///
/// The flag is SERIALISED in Sea.unity, so editing the C# default does
/// nothing at all -- it has to be pushed and the scene saved.
public static class SpawnAtHome
{
    public static string Execute() { return Set(false); }
    public static string OffshoreAgain() { return Set(true); }

    static string Set(bool active)
    {
        if (Application.isPlaying) return "stop play mode first — this saves the scene";
        var ps = Object.FindAnyObjectByType<PlaytestStart>(FindObjectsInactive.Include);
        if (ps == null) return "no PlaytestStart in the open scene";

        var so = new SerializedObject(ps);
        var prop = so.FindProperty("active");
        bool was = prop.boolValue;
        prop.boolValue = active;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ps);
        EditorSceneManager.MarkSceneDirty(ps.gameObject.scene);
        EditorSceneManager.SaveScene(ps.gameObject.scene);

        var check = new SerializedObject(ps).FindProperty("active").boolValue;
        return "PlaytestStart.active " + was + " -> " + check
            + (check ? "  (she spawns offshore in the storm water)"
                     : "  (she spawns at home, where the scene puts her)");
    }
}
