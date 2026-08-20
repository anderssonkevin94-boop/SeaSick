using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Pushes WaveField's spectrum defaults from code into Sea.unity and reports
/// what actually landed. Run after changing any spectrum default.
public static class ApplyWaveShape
{
    public static void Execute()
    {
        var field = Object.FindAnyObjectByType<SeaSick.Ocean.WaveField>();
        if (field == null) { Debug.LogError("ApplyWaveShape: no WaveField in the scene"); return; }

        var all = Object.FindObjectsByType<SeaSick.Ocean.WaveField>(FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (all.Length > 1)
            Debug.LogWarning($"ApplyWaveShape: {all.Length} WaveFields in the scene — " +
                             "there must be exactly one, they all write the same globals.");

        SceneDefaults.ResetToCodeDefaults(field);

        var so = new SerializedObject(field);
        string Read(string n)
        {
            var p = so.FindProperty(n);
            if (p == null) return "<missing>";
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float: return p.floatValue.ToString("F3");
                case SerializedPropertyType.Integer: return p.intValue.ToString();
                case SerializedPropertyType.Vector2: return p.vector2Value.ToString();
                default: return p.propertyType.ToString();
            }
        }

        Debug.Log($"WAVESHAPE: waveCount {Read("waveCount")}  baseAmplitude {Read("baseAmplitude")}m  " +
                  $"ampPower {Read("ampPower")}  ampJitter {Read("ampJitter")}  " +
                  $"spread {Read("shortSpreadDegrees")}->{Read("longSpreadDegrees")} deg  " +
                  $"choppiness {Read("choppiness")}  " +
                  $"L {Read("minWavelength")}..{Read("maxWavelength")}m  " +
                  $"storm {Read("stormWaveCount")} x steep {Read("stormSteepness")}");

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveOpenScenes();
    }
}
