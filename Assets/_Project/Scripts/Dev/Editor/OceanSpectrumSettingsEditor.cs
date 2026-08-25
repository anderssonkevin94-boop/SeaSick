using UnityEditor;
using UnityEngine;
using SeaSick.Ocean;

/// The sea-state assets, with artist knobs on top of the physical fields.
///
/// Select any SeaState_* asset and the inspector leads with height /
/// wavelength / chaos / texture, derived onto the real spectrum by StormShape.
/// The physical fields stay visible underneath and stay editable — this is a
/// convenience layer, not a replacement, and anything the knobs cannot express
/// (wind direction, fetch, foam) is still reached the normal way.
///
/// Edits push straight to a running ocean, so dragging a slider in play mode
/// changes the water under the boat.
[CustomEditor(typeof(OceanSpectrumSettings))]
public class OceanSpectrumSettingsEditor : Editor
{
    bool showPhysical = true;

    public override void OnInspectorGUI()
    {
        var s = (OceanSpectrumSettings)target;
        var knobs = StormShape.Read(s);

        EditorGUILayout.LabelField("Shape", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();

        knobs.heightHs = EditorGUILayout.Slider(new GUIContent("Wave height (Hs)",
            "Significant wave height: the mean of the biggest third. Individual waves reach about 1.6x it."),
            knobs.heightHs, 0.2f, 90f);
        knobs.wavelength = EditorGUILayout.Slider(new GUIContent("Wavelength (m)",
            "Crest to crest. THIS is what decides whether a wave is climbable or a wall — height never was."),
            knobs.wavelength, 20f, 900f);
        knobs.chaos = EditorGUILayout.Slider(new GUIContent("Chaos",
            "0 = one clean train of parallel rollers (reads as a hillside). 1 = two broad trains crossing at 75 degrees. Moves energy BETWEEN the trains, so it cannot change the wave height."),
            knobs.chaos, 0f, 1f);
        knobs.texture = EditorGUILayout.Slider(new GUIContent("Surface texture",
            "Over-drives the spectrum's short-wave tail. 1 = physically honest, and visually flat, because at storm wind speeds almost no energy lands at the scales the eye reads as rough."),
            knobs.texture, 1f, 25f);
        knobs.choppiness = EditorGUILayout.Slider(new GUIContent("Crest sharpness",
            "Horizontal displacement: sharpens crests, rounds troughs. Past about 1.3 the surface folds, which is where foam comes from."),
            knobs.choppiness, 0f, 2f);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(s, "Tune sea state");
            StormShape.Apply(s, knobs);
            EditorUtility.SetDirty(s);
            PushLive(s);
        }

        EditorGUILayout.Space(4);
        float slope = StormShape.FaceSlopeDeg(knobs);
        string verdict =
            slope < 6f ? "flat" :
            slope < 12f ? "gentle — reads as a hillside" :
            slope <= 22f ? "GOOD (15-20° target)" : "past breaking";
        EditorGUILayout.LabelField($"face ~{slope:F1}°  ({verdict})",
            $"{StormShape.FaceBoatLengths(knobs, 24.2f):F1} boat lengths, "
            + $"needs {StormShape.DepthNeeded(knobs, 0.55f):F0} m of water");

        if (GUILayout.Button("Open Storm Tuner (top-down view)")) StormTuner.Open();
        EditorGUILayout.HelpBox(
            "Estimates, for while you drag. WaveSizeProbe is the truth, and "
            + "DivergenceProbe is the gate after any big change in steepness.",
            MessageType.None);

        EditorGUILayout.Space(6);
        showPhysical = EditorGUILayout.Foldout(showPhysical, "Physical spectrum", true);
        if (showPhysical)
        {
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck()) PushLive(s);
            EditorGUI.indentLevel--;
        }
    }

    /// SetSettings holds the asset BY REFERENCE, so a running ocean only needs
    /// telling that the spectrum moved. Nothing is copied and nothing can fall
    /// out of step.
    static void PushLive(OceanSpectrumSettings s)
    {
        if (!Application.isPlaying) return;
        var ocean = OceanRenderer.Instance;
        if (ocean == null) return;
        // Only if this asset is the one in force — otherwise SeaStateController
        // is blending and the edit belongs to a state that is not on screen.
        if (ocean.Settings == s) ocean.MarkSpectrumDirty();
    }
}
