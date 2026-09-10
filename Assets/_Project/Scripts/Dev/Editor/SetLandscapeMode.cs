using UnityEditor;
using UnityEngine;

/// Make landscape the shape the game runs in, without retiring the phone.
///
/// Kevin, 2026-09-11: "please change the game to computer mode, right now its
/// in portrait mode." He chose the reversible version -- landscape
/// first-class, the portrait path still alive -- so nothing here retires the
/// phone target and CLAUDE.md keeps its constraint.
///
/// **Only the platform settings live here now.** The other two halves of
/// "computer mode" turned out not to need a script at all, and that is the
/// interesting part:
///
///   THE CAMERA. `ChaseCamera.narrowestAspect` was a `[SerializeField]`, and
///   Unity had written 0.4615 into Sea.unity when the component was added --
///   so changing the default in code would have done nothing, silently. The
///   first version of this file pushed a new value through `SerializedObject`.
///   The better answer was to notice it is not per-scene tuning at all: there
///   is one aspect the game is composed for, so it is a `const` and the stale
///   scene line is ignored. A value that cannot drift needs no script to
///   un-drift it.
///
///   THE HUD. `HudLayout` places everything off `Screen.safeArea` and asks the
///   window what shape it is, so it re-lays itself out on resize. There is no
///   mode to set.
///
/// Run through `RunProbe.Landscape()`, not directly: this file names
/// `UnityEditor.PlayerSettings`, and handing it straight to `execute_script`
/// compiles it into a fresh assembly without the project's reference set,
/// which fails with no readable diagnostic.
public static class SetLandscapeMode
{
    public static void Execute()
    {
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.useAnimatedAutorotation = true;

        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.resizableWindow = true;   // so the HUD's re-layout is exercisable

        AssetDatabase.SaveAssets();

        // Read back off PlayerSettings rather than trusting the writes above.
        Debug.Log("SetLandscapeMode: orientation "
            + $"{PlayerSettings.defaultInterfaceOrientation}, "
            + $"standalone {PlayerSettings.defaultScreenWidth}x{PlayerSettings.defaultScreenHeight}, "
            + $"autorotate-to-portrait {PlayerSettings.allowedAutorotateToPortrait}, "
            + $"resizable {PlayerSettings.resizableWindow}.\n"
            + "Camera and HUD need no setting: ChaseCamera.narrowestAspect is a const at "
            + $"{SeaSick.CameraRig.ChaseCamera.LandscapeAspect:F3} and HudLayout reads the "
            + "window's own shape.");
    }
}
