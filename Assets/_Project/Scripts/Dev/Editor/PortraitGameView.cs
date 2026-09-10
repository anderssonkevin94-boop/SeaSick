using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// Put the Game view into the shape the game actually ships in: 1080x2340,
/// portrait, `ChaseCamera.PortraitAspect`.
///
/// **This is the single most repeated measurement trap in the project.** The
/// editor Game view is landscape (~1.41) and SeaSick is a portrait one-handed
/// phone game. Measured on 2026-08-30 by projecting real ground points through
/// the live camera: the home village sat at viewport x 0.18 — comfortably in
/// frame — at 1.41, and at x −0.26, off the left edge, at the shipping aspect.
/// A shot, a HUD or a framing certified in the editor window is a picture no
/// player will ever see.
///
/// `FramingProbe` and `TerrainPerfProbe` already force `cam.aspect` for their
/// own projections, but that only fixes the CAMERA. Anything laid out from
/// `Screen.width` and `Screen.height` — which is the whole IMGUI HUD — reads
/// the Game VIEW, and no amount of setting `cam.aspect` moves it. The only way
/// to judge the HUD at the phone's shape is to make the window that shape,
/// which is what this does.
///
/// Run it before `HudOverlapProbe`, before any HUD screenshot, and before
/// deciding that a panel fits.
public static class PortraitGameView
{
    const int Width = 1080;
    const int Height = 2340;
    const string SizeName = "SeaSick Portrait";

    public static void Execute()
    {
        try
        {
            var editorAsm = typeof(Editor).Assembly;
            var sizesType = editorAsm.GetType("UnityEditor.GameViewSizes");
            var groupTypeEnum = editorAsm.GetType("UnityEditor.GameViewSizeGroupType");
            var sizeType = editorAsm.GetType("UnityEditor.GameViewSize");
            var sizeKindEnum = editorAsm.GetType("UnityEditor.GameViewSizeType");
            if (sizesType == null || sizeType == null || sizeKindEnum == null)
            {
                Debug.LogError("PortraitGameView: Unity's GameViewSizes types moved. "
                    + "Set the Game view to 1080x2340 by hand before judging any HUD.");
                return;
            }

            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)
                                 ?.GetValue(null, null);
            if (sizes == null) { Debug.LogError("PortraitGameView: no GameViewSizes instance"); return; }

            // The group the current build target uses, so the size lands in
            // the list the Game view is actually showing.
            var currentGroup = sizesType
                .GetMethod("GetGroup", BindingFlags.Public | BindingFlags.Instance)
                ?.Invoke(sizes, new object[] { CurrentGroupType(sizesType, groupTypeEnum, sizes) });
            if (currentGroup == null) { Debug.LogError("PortraitGameView: no size group"); return; }

            var groupType = currentGroup.GetType();
            int index = FindByName(groupType, currentGroup, SizeName);

            if (index < 0)
            {
                var ctor = sizeType.GetConstructor(new[] { sizeKindEnum, typeof(int), typeof(int), typeof(string) });
                if (ctor == null) { Debug.LogError("PortraitGameView: GameViewSize constructor moved"); return; }
                // GameViewSizeType.FixedResolution == 1
                var size = ctor.Invoke(new[] { Enum.ToObject(sizeKindEnum, 1), Width, Height, (object)SizeName });
                groupType.GetMethod("AddCustomSize")?.Invoke(currentGroup, new[] { size });
                index = FindByName(groupType, currentGroup, SizeName);
            }

            if (index < 0) { Debug.LogError("PortraitGameView: could not add the size"); return; }

            var gameViewType = editorAsm.GetType("UnityEditor.GameView");
            var window = EditorWindow.GetWindow(gameViewType, false, null, false);
            gameViewType.GetMethod("SizeSelectionCallback",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(window, new object[] { index, null });
            window.Repaint();

            Debug.Log($"PortraitGameView: Game view set to {SizeName} {Width}x{Height} "
                    + $"(aspect {(float)Width / Height:F3}). This is the shape the HUD must be judged in.");
        }
        catch (Exception e)
        {
            Debug.LogError("PortraitGameView failed: " + e.Message
                + "\nSet the Game view to 1080x2340 by hand — a HUD judged in a landscape "
                + "window is a HUD nobody will see.");
        }
    }

    static object CurrentGroupType(Type sizesType, Type groupTypeEnum, object sizes)
    {
        var prop = sizesType.GetProperty("currentGroupType", BindingFlags.Public | BindingFlags.Instance);
        if (prop != null) return Enum.ToObject(groupTypeEnum, (int)prop.GetValue(sizes, null));
        return Enum.ToObject(groupTypeEnum, 0);   // Standalone
    }

    static int FindByName(Type groupType, object group, string name)
    {
        int total = (int)(groupType.GetMethod("GetTotalCount")?.Invoke(group, null) ?? 0);
        var getSize = groupType.GetMethod("GetGameViewSize");
        for (int i = 0; i < total; i++)
        {
            var s = getSize?.Invoke(group, new object[] { i });
            var n = s?.GetType().GetProperty("baseText")?.GetValue(s, null) as string;
            if (n == name) return i;
        }
        return -1;
    }
}
