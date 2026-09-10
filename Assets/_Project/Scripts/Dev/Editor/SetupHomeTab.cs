using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SeaSick.UI;

/// Puts the Home tab on the HUD object in Sea.unity.
///
/// It hangs off whatever already carries `StatusHUD`, so the permanent HUD
/// stays one object in the hierarchy rather than gaining a stray GameObject
/// per control. Wiring goes through `SerializedObject` because a serialized
/// value beats a C# field initialiser forever — the trap that has bitten this
/// project on `WeatherField`, `SpeedJuice` and the storm sky material.
public static class SetupHomeTab
{
    public static string Execute()
    {
        var log = new System.Text.StringBuilder();

        var hud = Object.FindAnyObjectByType<StatusHUD>(FindObjectsInactive.Include);
        GameObject host = hud != null ? hud.gameObject : GameObject.Find("HUD");
        if (host == null)
        {
            host = new GameObject("HUD");
            log.AppendLine("no StatusHUD and no 'HUD' object — created one");
        }

        var tab = host.GetComponent<HomeTab>();
        if (tab == null)
        {
            tab = host.AddComponent<HomeTab>();
            log.AppendLine($"added HomeTab to '{host.name}'");
        }
        else log.AppendLine($"HomeTab already on '{host.name}'");

        var so = new SerializedObject(tab);
        so.FindProperty("armSeconds").floatValue = 3.5f;
        so.FindProperty("refusalSeconds").floatValue = 3f;
        so.ApplyModifiedPropertiesWithoutUndo();

        var anchor = Object.FindAnyObjectByType<SeaSick.Ship.AnchorController>(
            FindObjectsInactive.Include);
        log.AppendLine(anchor == null
            ? "*** no AnchorController in the scene — the tab will find nothing ***"
            : $"AnchorController on '{anchor.gameObject.name}': BerthAtHome available");

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        log.AppendLine("scene saved");
        var s = log.ToString();
        Debug.Log("SetupHomeTab:\n" + s);
        return s;
    }
}
