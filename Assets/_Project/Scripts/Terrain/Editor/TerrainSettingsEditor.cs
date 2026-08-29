using UnityEditor;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// "Regenerate" on the settings asset refreshes every visualiser (and,
    /// later, every streamer) in the open scene, so values can be iterated
    /// without leaving the asset inspector. Also offers curve presets.
    [CustomEditor(typeof(TerrainSettings))]
    public class TerrainSettingsEditor : Editor
    {
        int steps = 4;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var s = (TerrainSettings)target;
            GUILayout.Space(6);
            if (GUILayout.Button("Regenerate scene previews")) RegenerateAll();

            GUILayout.Space(6);
            GUILayout.Label("Terrace curve presets (keep current endpoints)", EditorStyles.boldLabel);
            steps = EditorGUILayout.IntSlider("Plateaus", steps, 2, 8);
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Stepped")) { Undo.RecordObject(s, "Stepped curve"); s.profileCurve = Stepped(s.profileCurve, steps); EditorUtility.SetDirty(s); RegenerateAll(); }
                if (GUILayout.Button("Linear")) { Undo.RecordObject(s, "Linear curve"); s.profileCurve = AnimationCurve.Linear(0f, s.profileCurve.Evaluate(0f), 1f, s.profileCurve.Evaluate(1f)); EditorUtility.SetDirty(s); RegenerateAll(); }
            }
        }

        static void RegenerateAll()
        {
            foreach (var v in Object.FindObjectsByType<TerrainMapVisualiser>(FindObjectsSortMode.None))
                v.Regenerate();
        }

        /// Plateaus of equal noise width between the curve's endpoints, with
        /// short steep risers between them so the LUT stays monotonic.
        public static AnimationCurve Stepped(AnimationCurve from, int plateaus)
        {
            float lo = from.Evaluate(0f), hi = from.Evaluate(1f);
            var c = new AnimationCurve();
            float riser = 0.15f / plateaus;
            for (int i = 0; i < plateaus; i++)
            {
                float h = Mathf.Lerp(lo, hi, i / (float)(plateaus - 1));
                float t0 = i / (float)plateaus, t1 = (i + 1f) / plateaus - riser;
                c.AddKey(new Keyframe(t0, h, 0f, 0f));
                c.AddKey(new Keyframe(t1, h, 0f, 0f));
            }
            for (int i = 0; i < c.length; i++) AnimationUtility.SetKeyBroken(c, i, true);
            return c;
        }
    }
}
