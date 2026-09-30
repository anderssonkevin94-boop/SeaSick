using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **Bakes the node turn and the ×100 into the mesh for the kits exported
    /// the other way round (2026-09-30).**
    ///
    /// Astra's worker tools and four of the sea discovery models (the salvage
    /// cluster and the three reefs) come out of Blender with the -90° X turn
    /// and a ×100 scale on the FBX node instead of in the vertices -- the
    /// resource and food kits have it the other way and import clean. No
    /// ModelImporter combination fixes both: baking the axis conversion
    /// leaves the tools lying along -Z, not baking leaves the raw mesh
    /// Z-up, and the reefs came in 100× too big. Runtime code loads these as
    /// bare meshes (`ToolKit`, `SeaKit`), so the mesh itself must be in the
    /// game frame, in metres.
    ///
    /// So: import without the axis bake or file scale, then fold each mesh
    /// filter's whole node transform into its vertices, divide out the ×100,
    /// and reset every node to identity. The kits' own importers
    /// (`ToolKitImport`, `SeaKitImport`) still check the resulting bounds.
    public class KitAxisFix : AssetPostprocessor
    {
        const float Unit = 0.01f;

        static bool Wants(string path)
        {
            if (path.StartsWith("Assets/_Project/Resources/Kits/Tools/")) return true;
            if (!path.StartsWith("Assets/_Project/Resources/Kits/Sea/")) return false;
            return path.Contains("SalvageCluster") || path.Contains("/Reef");
        }

        void OnPreprocessModel()
        {
            if (!Wants(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.bakeAxisConversion = false;
            mi.useFileScale = false;
            mi.globalScale = 1f;
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!Wants(assetPath)) return;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var m = Matrix4x4.Scale(Vector3.one * Unit) * mf.transform.localToWorldMatrix;
                var rot = mf.transform.rotation;

                var v = mesh.vertices;
                for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
                mesh.vertices = v;

                var n = mesh.normals;
                if (n != null && n.Length == v.Length)
                {
                    for (int i = 0; i < n.Length; i++) n[i] = (rot * n[i]).normalized;
                    mesh.normals = n;
                }
                var t = mesh.tangents;
                if (t != null && t.Length == v.Length)
                {
                    for (int i = 0; i < t.Length; i++)
                    {
                        var d = rot * new Vector3(t[i].x, t[i].y, t[i].z);
                        t[i] = new Vector4(d.x, d.y, d.z, t[i].w);
                    }
                    mesh.tangents = t;
                }
                mesh.RecalculateBounds();
            }
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                tr.localPosition = Vector3.zero;
                tr.localRotation = Quaternion.identity;
                tr.localScale = Vector3.one;
            }
        }
    }
}
