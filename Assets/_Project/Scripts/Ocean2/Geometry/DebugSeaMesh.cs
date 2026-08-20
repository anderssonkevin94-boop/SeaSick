using UnityEngine;

namespace SeaSick.Ocean2
{
    /// Lab-only flat grid displaced by the cascade textures — the stand-in
    /// surface until the real clipmap lands at M3. Not used in gameplay.
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class DebugSeaMesh : MonoBehaviour
    {
        [SerializeField] float extent = 256f;
        [SerializeField] float cellSize = 1f;

        void Start()
        {
            int cells = Mathf.CeilToInt(extent / cellSize);
            int verts1D = cells + 1;
            var verts = new Vector3[verts1D * verts1D];
            var idx = new int[cells * cells * 6];

            for (int z = 0; z < verts1D; z++)
                for (int x = 0; x < verts1D; x++)
                    verts[z * verts1D + x] = new Vector3(
                        x * cellSize - extent * 0.5f, 0f, z * cellSize - extent * 0.5f);

            int t = 0;
            for (int z = 0; z < cells; z++)
                for (int x = 0; x < cells; x++)
                {
                    int v = z * verts1D + x;
                    idx[t++] = v; idx[t++] = v + verts1D; idx[t++] = v + 1;
                    idx[t++] = v + 1; idx[t++] = v + verts1D; idx[t++] = v + verts1D + 1;
                }

            var mesh = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = verts,
                triangles = idx,
                // Displacement happens in the vertex shader and is invisible to
                // culling; generous bounds or the sea vanishes at frame edges.
                bounds = new Bounds(Vector3.zero, new Vector3(extent, 100f, extent)),
            };
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }
    }
}
