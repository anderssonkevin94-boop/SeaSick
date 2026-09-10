/// Put the Game view into the shape the game runs in on a phone:
/// 1080x2340, `ChaseCamera.PortraitAspect`.
///
/// The twin of `DesktopGameView`. Landscape became first-class on 2026-09-11
/// ("computer mode") and the phone stayed supported, so this is no longer the
/// only shape that matters — it is one of two, and the HUD lays itself out
/// differently in each: the tab rail starts at 0.30 of the height here and
/// 0.14 on a desk, and every panel hanging off the rail moves with it.
///
/// Run this, then `HudOverlapProbe`; then run `DesktopGameView` and do it
/// again. The reflection that does the work, and why setting `cam.aspect` is
/// not a substitute, is in `GameViewSize`.
public static class PortraitGameView
{
    public static void Execute() => GameViewSize.Set("SeaSick Portrait", 1080, 2340);
}
