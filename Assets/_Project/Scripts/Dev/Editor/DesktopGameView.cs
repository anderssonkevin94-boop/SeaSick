using UnityEditor;

/// Put the Game view into the shape the game runs in on a computer:
/// 1920x1080, `ChaseCamera.LandscapeAspect`.
///
/// The twin of `PortraitGameView`, and the pair is the point. Landscape became
/// first-class on 2026-09-11 without the phone being retired, so there are two
/// shapes the HUD has to be right in and checking one proves nothing about the
/// other: the tab rail starts at 0.14 of the height here and 0.30 upright, and
/// every panel that hangs off the rail moves with it.
///
/// Run this, then `HudOverlapProbe`; then run `PortraitGameView` and do it
/// again.
public static class DesktopGameView
{
    public static void Execute() => GameViewSize.Set("SeaSick Desktop", 1920, 1080);
}
