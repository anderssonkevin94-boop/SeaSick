using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// Prints the LIVE values of the ocean quality assets and the terrain draw
/// settings. Reading the .asset file on disk is not the same question: Unity
/// holds inspector edits in memory until something saves them.
public static class ReadQuality
{
    public static string Execute()
    {
        var sb = new StringBuilder("=== ReadQuality ===\n");
        foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject OceanQuality"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var o = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (o == null) continue;
            sb.AppendLine($"-- {System.IO.Path.GetFileName(path)}");
            foreach (var f in o.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f.IsNotSerialized) continue;
                var v = f.GetValue(o);
                if (v is System.Array arr)
                {
                    var parts = new System.Collections.Generic.List<string>();
                    foreach (var e in arr) parts.Add(e.ToString());
                    sb.AppendLine($"   {f.Name} = [{string.Join(", ", parts)}]");
                }
                else sb.AppendLine($"   {f.Name} = {v}");
            }
        }
        // The asset on disk having the right numbers is NOT the same question
        // as the game seeing them. `OceanQuality.Active` goes through
        // Resources.Load<OceanQuality>, which returns null -- silently -- if
        // the asset's script link is broken, and every consumer then falls
        // back to its own hard-coded default: 7 rings, {512,128,32}, 500 m
        // fade. That failure looks exactly like "I changed it and nothing
        // happened", so the tool now reports the RUNTIME path too.
        sb.AppendLine();
        sb.AppendLine($"-- runtime: QualitySettings level {QualitySettings.GetQualityLevel()} "
                      + $"(\"{QualitySettings.names[QualitySettings.GetQualityLevel()]}\") "
                      // Ask OceanQuality which tier it wants rather than
                      // re-deriving it here. This line carried its own copy of
                      // the rule and therefore reproduced the inversion it
                      // existed to catch.
                      + "-> " + SeaSick.Ocean.OceanQuality.TierAsset());
        var act = SeaSick.Ocean.OceanQuality.Active;
        if (act == null)
        {
            sb.AppendLine("   OceanQuality.Active = NULL -- Resources.Load failed.");
            sb.AppendLine("   Every consumer is on its own fallback, so NOTHING you set in");
            sb.AppendLine("   these assets is reaching the game.");
        }
        else
        {
            sb.AppendLine($"   OceanQuality.Active = {act.name}");
            sb.AppendLine($"   patchSizes[0] = {act.patchSizes[0]}   clipmapRings = {act.clipmapRings}"
                          + $"   innerCellSize = {act.innerCellSize}");
            sb.AppendLine($"   displacementFadeDistance = {act.displacementFadeDistance}");
            // What the clipmap will actually reach with those numbers.
            float cell = act.innerCellSize * (1 << (act.clipmapRings - 1));
            sb.AppendLine($"   -> outermost ring cell {cell:F1} m; a wave needs ~4 cells per");
            sb.AppendLine($"      wavelength to be drawn at all, so nothing under ~{cell * 4f:F0} m");
            sb.AppendLine($"      of wavelength is rendered out there, however well it is simulated.");
        }

        Debug.Log(sb.ToString());
        return sb.ToString();
    }
}
