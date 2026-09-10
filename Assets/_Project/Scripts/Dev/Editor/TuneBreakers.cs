using SeaSick.Ship;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Pushes the Breakers defaults back onto the component in the scene.
///
/// **A changed `= 0.035f` in the source does not reach a component that is
/// already in a scene.** Unity restores the serialized value over the new
/// initializer, silently, so the code says one thing and the game does
/// another -- the same trap that had `groundingDraft` reporting 1.0 through
/// recompiles and fresh play sessions. Anything already attached has to be
/// written through SerializedObject.
public static class TuneBreakers
{
    public static void Execute()
    {
        var b = Object.FindAnyObjectByType<Breakers>();
        if (b == null) { Debug.LogError("TuneBreakers: no Breakers in the scene"); return; }

        var so = new SerializedObject(b);
        so.FindProperty("onsetRatio").floatValue = 0.35f;
        so.FindProperty("fullRatio").floatValue = 0.55f;
        so.FindProperty("minSeaHs").floatValue = 0.8f;
        so.FindProperty("shoreSet").floatValue = 1.8f;
        so.FindProperty("batterPerSecond").floatValue = 0.02f;
        so.FindProperty("batterFullAtHullFraction").floatValue = 0.25f;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(b);
        EditorSceneManager.MarkSceneDirty(b.gameObject.scene);
        EditorSceneManager.SaveScene(b.gameObject.scene);
        Debug.Log("TuneBreakers: written and scene saved");
    }
}
