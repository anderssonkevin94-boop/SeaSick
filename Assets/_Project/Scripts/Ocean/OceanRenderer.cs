using UnityEngine;

namespace SeaSick.Ocean
{
    /// A flat grid that follows the ship. It is built once and never touched
    /// again — all displacement happens in the SeaSick/Ocean vertex shader
    /// from the constants WaveField uploads each frame.
    ///
    /// This used to rebuild ~5,300 vertices on the CPU every frame at a cost
    /// of about 8.5 ms. Now the per-frame CPU work is one transform update.
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(WaveField))]
    public class OceanRenderer : MonoBehaviour
    {
        [Tooltip("Grid resolution. Cheap now that displacement is on the GPU.")]
        [SerializeField] int gridQuads = 150;
        [SerializeField] float extent = 620f;
        [SerializeField] Transform followTarget;

        Mesh mesh;

        public Transform FollowTarget { get => followTarget; set => followTarget = value; }

        void Start() { BuildGrid(); }

        void BuildGrid()
        {
            int n = gridQuads + 1;
            var verts = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            var normals = new Vector3[n * n];
            float half = extent * 0.5f;
            float step = extent / gridQuads;

            for (int z = 0, i = 0; z < n; z++)
                for (int x = 0; x < n; x++, i++)
                {
                    verts[i] = new Vector3(x * step - half, 0f, z * step - half);
                    uvs[i] = new Vector2((float)x / gridQuads, (float)z / gridQuads);
                    normals[i] = Vector3.up;
                }

            var tris = new int[gridQuads * gridQuads * 6];
            for (int z = 0, t = 0; z < gridQuads; z++)
                for (int x = 0; x < gridQuads; x++)
                {
                    int i = z * n + x;
                    tris[t++] = i; tris[t++] = i + n; tris[t++] = i + 1;
                    tris[t++] = i + 1; tris[t++] = i + n; tris[t++] = i + n + 1;
                }

            mesh = new Mesh { name = "OceanTile" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = tris;
            // Vertices move in the shader, so bounds must be generous enough
            // that culling never clips the tile.
            // Displacement happens in the vertex shader, so Unity culls
            // against these bounds and never sees how far the surface actually
            // moved. 80m of total height (40m either way) was ample for a 5m
            // swell and would cull the whole ocean out of frame now the western
            // deep is measured in tens of metres. Bounds are free — be generous.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(extent, 600f, extent));
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        void LateUpdate()
        {
            if (followTarget == null) return;
            // Snap to the grid cell so the surface never crawls under the ship.
            float cell = extent / gridQuads;
            Vector3 p = followTarget.position;
            transform.position = new Vector3(
                Mathf.Round(p.x / cell) * cell, 0f, Mathf.Round(p.z / cell) * cell);
        }
    }
}
