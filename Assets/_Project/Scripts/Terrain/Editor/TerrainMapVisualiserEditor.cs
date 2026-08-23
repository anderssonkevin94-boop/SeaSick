using UnityEditor;
using UnityEngine;

namespace SeaSick.Terrain
{
    [CustomEditor(typeof(TerrainMapVisualiser))]
    public class TerrainMapVisualiserEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var v = (TerrainMapVisualiser)target;
            GUILayout.Space(6);
            if (GUILayout.Button("Regenerate")) v.Regenerate();
            if (GUILayout.Button("Export PNG…"))
            {
                string path = EditorUtility.SaveFilePanel("Export terrain map", "", "terrain-map.png", "png");
                if (!string.IsNullOrEmpty(path)) v.ExportPng(path);
            }
            if (v.Texture != null)
            {
                GUILayout.Label($"range [{v.LastMin:F3}, {v.LastMax:F3}]");
                var r = GUILayoutUtility.GetAspectRect(1f);
                EditorGUI.DrawPreviewTexture(r, v.Texture);
            }
        }
    }
}
