using UnityEditor;
using UnityEngine;
using SeaSick.Ocean;

/// Pushes the current sea-state tuning onto the three weather assets (assets
/// snapshot values at creation; changing code defaults never reaches them).
public static class TuneSeaStates
{
    const string Dir = "Assets/_Project/Settings/Resources/Ocean";

    public static string Execute()
    {
        Apply("Calm", s => { s.choppiness = 0.75f; s.foamThreshold = 0.58f; s.foamInjection = 0.15f; s.foamHalflife = 3f; });
        Apply("Normal", s => { s.choppiness = 1.0f; s.foamThreshold = 0.63f; s.foamInjection = 0.4f; s.foamHalflife = 4f; });
        Apply("Stormy", s => { s.choppiness = 1.25f; s.foamThreshold = 0.68f; s.foamInjection = 0.9f; s.foamHalflife = 4.5f; });
        AssetDatabase.SaveAssets();
        return "sea state assets tuned (choppiness + foam thresholds)";
    }

    static void Apply(string name, System.Action<OceanSpectrumSettings> act)
    {
        var s = AssetDatabase.LoadAssetAtPath<OceanSpectrumSettings>($"{Dir}/SeaState_{name}.asset");
        if (s == null) { Debug.LogWarning($"missing SeaState_{name}"); return; }
        act(s);
        EditorUtility.SetDirty(s);
    }
}
