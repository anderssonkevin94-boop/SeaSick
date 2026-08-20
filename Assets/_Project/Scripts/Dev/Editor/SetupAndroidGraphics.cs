using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// Locks Android to Vulkan only. The FFT ocean is compute-shader based and
/// OpenGL ES 3.0 has no compute; rather than maintain a second ocean for
/// pre-2018 devices we drop the GLES fallback entirely (decision 2026-08-20).
/// iOS is already Metal-only.
public static class SetupAndroidGraphics
{
    public static string Execute()
    {
        var before = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
            new[] { GraphicsDeviceType.Vulkan });
        AssetDatabase.SaveAssets();
        var after = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
        return $"Android graphics APIs: [{string.Join(", ", before)}] -> [{string.Join(", ", after)}]";
    }
}
