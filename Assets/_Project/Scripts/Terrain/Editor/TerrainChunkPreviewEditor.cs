using UnityEditor;
using UnityEngine;

namespace SeaSick.Terrain
{
    [CustomEditor(typeof(TerrainChunkPreview))]
    public class TerrainChunkPreviewEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var p = (TerrainChunkPreview)target;
            GUILayout.Space(6);
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild")) p.Rebuild();
                if (GUILayout.Button("Clear")) p.Clear();
            }
            GUILayout.Label(p.VertexCount + " vertices");
        }
    }
}
