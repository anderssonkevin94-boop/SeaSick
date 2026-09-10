using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// Force the Game view to an exact pixel size.
///
/// **This is the single most repeated measurement trap in the project.**
/// Anything laid out from `Screen.width` and `Screen.height` — which is the
/// whole IMGUI HUD — reads the Game VIEW, and no amount of setting
/// `cam.aspect` moves it. `FramingProbe` and `TerrainPerfProbe` force
/// `cam.aspect` for their own projections, but that only fixes the CAMERA.
/// The only way to judge the HUD at a given shape is to make the window that
/// shape.
///
/// It cost real time before it was written down: measured 2026-08-30 by
/// projecting real ground points through the live camera, the home village sat
/// at viewport x 0.18 — comfortably in frame — at the editor's 1.41, and at
/// x −0.26, off the left edge, at the phone's aspect.
///
/// Since 2026-09-11 there are TWO shapes that matter, not one: the game runs
/// landscape on a computer and portrait on a phone, and the HUD lays itself
/// out differently in each. `DesktopGameView` and `PortraitGameView` are the
/// two entry points; use both.
public static class GameViewSize
{
    /// Add (or reuse) a fixed-resolution Game view size and select it.
    public static void Set(string name, int width, int height)
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
                Debug.LogError($"GameViewSize: Unity's GameViewSizes types moved. "
                    + $"Set the Game view to {width}x{height} by hand before judging any HUD.");
                return;
            }

            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)
                                 ?.GetValue(null, null);
            if (sizes == null) { Debug.LogError("GameViewSize: no GameViewSizes instance"); return; }

            var group = sizesType
                .GetMethod("GetGroup", BindingFlags.Public | BindingFlags.Instance)
                ?.Invoke(sizes, new object[] { CurrentGroupType(sizesType, groupTypeEnum, sizes) });
            if (group == null) { Debug.LogError("GameViewSize: no size group"); return; }

            var groupType = group.GetType();
            int index = FindByName(groupType, group, name);

            if (index < 0)
            {
                var ctor = sizeType.GetConstructor(new[] { sizeKindEnum, typeof(int), typeof(int), typeof(string) });
                if (ctor == null) { Debug.LogError("GameViewSize: GameViewSize constructor moved"); return; }
                // GameViewSizeType.FixedResolution == 1
                var size = ctor.Invoke(new[] { Enum.ToObject(sizeKindEnum, 1), width, height, (object)name });
                groupType.GetMethod("AddCustomSize")?.Invoke(group, new[] { size });
                index = FindByName(groupType, group, name);
            }

            if (index < 0) { Debug.LogError("GameViewSize: could not add the size"); return; }

            var gameViewType = editorAsm.GetType("UnityEditor.GameView");
            var window = EditorWindow.GetWindow(gameViewType, false, null, false);
            gameViewType.GetMethod("SizeSelectionCallback",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(window, new object[] { index, null });
            window.Repaint();

            Debug.Log($"GameViewSize: Game view set to {name} {width}x{height} "
                    + $"(aspect {(float)width / height:F3}).");
        }
        catch (Exception e)
        {
            Debug.LogError($"GameViewSize failed: {e.Message}"
                + $"\nSet the Game view to {width}x{height} by hand — a HUD judged in the "
                + "wrong shape is a HUD nobody will see.");
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
