using UnityEditor;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// "Regenerate" on the settings asset refreshes every visualiser (and,
    /// later, every streamer) in the open scene, so values can be iterated
    /// without leaving the asset inspector.
    [CustomEditor(typeof(TerrainSettings))]
    public class TerrainSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            GUILayout.Space(6);
            if (GUILayout.Button("Regenerate scene previews"))
                foreach (var v in Object.FindObjectsByType<TerrainMapVisualiser>(FindObjectsSortMode.None))
                    v.Regenerate();
        }
    }
}
