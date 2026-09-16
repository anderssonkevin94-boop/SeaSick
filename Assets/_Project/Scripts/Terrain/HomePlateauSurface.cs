using Unity.Mathematics;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// Connected Blender-authored ground. The same triangles drive rendering,
    /// collision and Burst height queries; the streamed meadow stays underneath.
    public static class HomePlateauSurface
    {
        public static bool Enabled(in TerrainParams p) => p.storybookLandforms != 0 && p.homeIsle != 0;

        public static bool TryHeight(float2 world, in TerrainParams p, out float height)
        {
            height = p.seaLevel + p.seabedDepth;
            if (!Enabled(p)) return false;
            float2 delta = world - p.homeIsleCentre;
            float2 q = new float2(math.dot(delta, new float2(-p.homeIsleCoveDir.y, p.homeIsleCoveDir.x)),
                math.dot(delta, -p.homeIsleCoveDir)) * (110f / p.homeIsleRadius);
            int2 cell = (int2)math.floor((q + 160f) / 8f);
            if (math.any(cell < 0) || math.any(cell >= 40)) return false;
            int id = cell.y * 40 + cell.x;
            bool found = false;
            for (int i = HomePlateauData.Start(id); i < HomePlateauData.End(id); i++)
            {
                int3 t = HomePlateauData.Triangle(HomePlateauData.Candidate(i));
                float3 a = HomePlateauData.Vertex(t.x), b = HomePlateauData.Vertex(t.y), c = HomePlateauData.Vertex(t.z);
                float2 u = b.xz - a.xz, v = c.xz - a.xz, w = q - a.xz;
                float det = u.x * v.y - u.y * v.x;
                if (math.abs(det) < .000001f) continue;
                float s = (w.x * v.y - w.y * v.x) / det;
                float r = (u.x * w.y - u.y * w.x) / det;
                if (s < -.00001f || r < -.00001f || s + r > 1.00001f) continue;
                height = math.max(height, p.seaLevel + a.y + s * (b.y - a.y) + r * (c.y - a.y));
                found = true;
            }
            return found;
        }

        [System.Serializable] class GroundData
        {
            public Vector3[] vertices, normals;
            public Color[] colors;
            public int[] triangles;
        }

        public static GameObject Create(Transform parent, in TerrainParams p, Material source)
        {
            if (!Enabled(p)) return null;
            var asset = Resources.Load<TextAsset>("Flora/HomeIslandTerrain");
            if (!asset) { Debug.LogError("Authored island terrain export is missing"); return null; }
            var data = JsonUtility.FromJson<GroundData>(asset.text);
            var go = new GameObject("Home Plateau — Connected Ground");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(p.homeIsleCentre.x, p.seaLevel, p.homeIsleCentre.y);
            go.transform.rotation = Quaternion.LookRotation(new Vector3(-p.homeIsleCoveDir.x, 0, -p.homeIsleCoveDir.y));
            go.transform.localScale = new Vector3(p.homeIsleRadius / 110f, 1, p.homeIsleRadius / 110f);
            var mesh = new Mesh { name = "Blender connected plateau (exact height surface)" };
            mesh.vertices = data.vertices; mesh.normals = data.normals; mesh.colors = data.colors; mesh.triangles = data.triangles; mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mat = new Material(source) { name = "Authored plateau face colours" };
            mat.SetFloat("_CrispTerrain", 0);
            mat.SetFloat("_DetailStrength", 0); mat.SetFloat("_NormalStrength", 0); mat.SetFloat("_StriationStrength", 0);
            mat.SetFloat("_AuthoredFormLighting", 1f);
            mat.SetFloat("_PaintedSurface", 1f);
            mat.SetFloat("_PaintStudy", 0f);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        public static void Release(GameObject go)
        {
            if (!go) return;
            Object.Destroy(go.GetComponent<MeshFilter>().sharedMesh);
            Object.Destroy(go.GetComponent<MeshRenderer>().sharedMaterial);
            Object.Destroy(go);
        }
    }
}
