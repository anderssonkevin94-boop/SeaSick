using UnityEngine;
using SeaSick.Voyage;

/// A grab of the home panel as the player sees it.
///
/// In the EDITOR assembly on purpose: `ScreenCapture` lives in a module this
/// project does not give the runtime assembly, which is also why every other
/// shot here renders a camera to a RenderTexture instead. A camera cannot be
/// used for this one — the panel is IMGUI and IMGUI never reaches a target
/// texture, so the screen is the only place it exists.
public static class HomePanelShot
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("HomePanelShot: not in play mode"); return; }
        var voyage = Object.FindFirstObjectByType<VoyageManager>();
        if (voyage == null) { Debug.LogError("HomePanelShot: no VoyageManager"); return; }
        if (!voyage.AtHome)
        {
            Debug.LogError("HomePanelShot: she is not home — run RunProbe.Loop first, "
                + "the panel only exists while she is lying at the pier");
            return;
        }
        ScreenCapture.CaptureScreenshot("/tmp/home-panel.png");
        Debug.Log($"HomePanelShot: writing /tmp/home-panel.png at {Screen.width}x{Screen.height}"
            + " — the editor Game view aspect, not the phone's");
    }
}
