using UnityEngine;

namespace SeaSick.Ocean
{
    /// CPU-displaced ocean tile that follows a target (the ship), snapped to the
    /// grid cell size so vertices never swim. Prototype-grade: good enough to
    /// judge sailing feel; optimize (jobs/shader) once the feel is locked.
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(WaveField))]
    public class OceanRenderer : MonoBehaviour
    {
        [SerializeField] int gridQuads = 110;
        [SerializeField] float extent = 460f;
        [SerializeField] Transform followTarget;

        Mesh mesh;
        Vector3[] baseVerts;
        Vector3[] verts;
        WaveField field;

        public Transform FollowTarget { get => followTarget; set => followTarget = value; }

        void Start()
        {
            field = GetComponent<WaveField>();
            BuildGrid();
        }

        void BuildGrid()
        {
            int n = gridQuads + 1;
            baseVerts = new Vector3[n * n];
            verts = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            float half = extent * 0.5f;
            float step = extent / gridQuads;
            for (int z = 0, i = 0; z < n; z++)
                for (int x = 0; x < n; x++, i++)
                {
                    baseVerts[i] = new Vector3(x * step - half, 0f, z * step - half);
                    uvs[i] = new Vector2((float)x / gridQuads, (float)z / gridQuads);
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
            mesh.MarkDynamic();
            mesh.vertices = baseVerts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            // Generous fixed bounds: vertices move every frame, culling must not flicker.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(extent, 40f, extent));
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        void LateUpdate()
        {
            if (followTarget != null)
            {
                float cell = extent / gridQuads;
                Vector3 p = followTarget.position;
                transform.position = new Vector3(
                    Mathf.Round(p.x / cell) * cell, 0f, Mathf.Round(p.z / cell) * cell);
            }

            float t = Time.time;
            Vector3 origin = transform.position;
            for (int i = 0; i < baseVerts.Length; i++)
            {
                Vector3 b = baseVerts[i];
                Vector3 d = field.Displace(new Vector2(origin.x + b.x, origin.z + b.z), t);
                verts[i] = new Vector3(b.x + d.x, d.y, b.z + d.z);
            }
            mesh.vertices = verts;
            mesh.RecalculateNormals();
        }
    }
}
