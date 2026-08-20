using UnityEditor;
using UnityEngine;

/// Switch the editor's quality tier (0 = Mobile, 1 = PC) so the ocean stack
/// can be rehearsed at phone settings. Restart play mode after switching —
/// OceanQuality resolves once at OnEnable.
public static class SetQuality
{
    public static string Execute()
    {
        int level = QualitySettings.GetQualityLevel() == 0 ? 1 : 0;
        QualitySettings.SetQualityLevel(level, true);
        SeaSick.Ocean.OceanQuality.Override(null);
        return "quality level -> " + level + " (" + QualitySettings.names[level] + ")";
    }
}
