using SeaSick.UI;
using UnityEngine;

/// Open the settings drawer onto its widest tool, so `HudOverlapProbe` gets a
/// census of the screen in the state that used to be worst.
///
/// The foam tuner is the one to pick: it took a fixed 440x560 box out of
/// `Screen.width − 452, 12`, which at the shipping portrait aspect is exactly
/// where the minimap, the wind row and the ship panel live. If the drawer's
/// body clears the HUD with that open, it clears it with anything open.
///
/// Play mode only — it drives runtime state, not the scene.
public static class OpenSettingsForCheck
{
    public static void Execute()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("OpenSettingsForCheck: not in play mode");
            return;
        }

        var tools = DevTools.All;
        if (tools.Count == 0)
        {
            Debug.LogError("OpenSettingsForCheck: no tools registered — "
                + "either no tuners are in this scene, or IDevTool registration is not running.");
            return;
        }

        IDevTool pick = null;
        foreach (var t in tools)
            if (t != null && t.ToolName.Contains("Foam")) { pick = t; break; }
        if (pick == null) pick = tools[0];

        SettingsPanel.Show(pick);
        Debug.Log($"OpenSettingsForCheck: drawer open on '{pick.ToolName}'. "
                + $"{tools.Count} tools registered: {Names(tools)}");
    }

    static string Names(System.Collections.Generic.IReadOnlyList<IDevTool> tools)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in tools)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(t == null ? "<null>" : t.ToolName);
        }
        return sb.ToString();
    }
}
