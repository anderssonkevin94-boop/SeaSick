using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SeaSick.Ocean;

/// The storm-sea values that are SCENE-serialised and therefore unreachable
/// from C# defaults. Run after changing any of them, then re-run the probes.
///
/// farScale 1.5 -> 1.0 is the important one. It was a post-hoc amplitude
/// multiplier on the deep west, which is the wrong instrument now that the
/// west has a storm SPECTRUM of its own: multiplying an authored 45 m sea by
/// 1.5 asks for 67 m of wave, which needs 150 m of water under it before the
/// depth limit will allow it, and the open ocean floor is -120. The regional
/// story is carried by the weather target (SeaStateController ramps to the
/// stormy spectrum out west) instead of by a gain. The calm shelf near home
/// keeps nearScale 0.35 -- that one is still doing real work.
public static class TuneStormSea
{
    public static string Execute()
    {
        var rf = Object.FindAnyObjectByType<RegionField>();
        if (rf == null) return "no RegionField in the open scene";

        var so = new SerializedObject(rf);
        so.FindProperty("farScale").floatValue = 1.0f;
        // Written explicitly rather than left to the C# initialiser: saving a
        // scene freezes every field including ones just added, and this
        // project has lost three tuning runs to exactly that.
        so.FindProperty("breakFraction").floatValue = 0.55f;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rf);
        EditorSceneManager.MarkSceneDirty(rf.gameObject.scene);
        EditorSceneManager.SaveScene(rf.gameObject.scene);

        return "RegionField: farScale -> " + so.FindProperty("farScale").floatValue
             + ", breakFraction -> " + so.FindProperty("breakFraction").floatValue
             + " (scene saved)";
    }
}
