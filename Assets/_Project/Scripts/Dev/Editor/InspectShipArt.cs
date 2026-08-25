using System.Text;
using UnityEditor;
using UnityEngine;

/// What is actually inside the ship art FBXs — sub-assets, meshes, vertex and
/// triangle counts. The cannon FBX imports as an empty root, so before
/// wiring it into the battery we need to know whether it holds a mesh at all.
public static class InspectShipArt
{
    public static string Execute()
    {
        string[] paths =
        {
            "Assets/_Project/Art/Ship/player_ship_cannon.fbx",
            "Assets/_Project/Art/Ship/player_ship_hull.fbx",
            "Assets/_Project/Art/Ship/player_ship_rudder.fbx",
            "Assets/_Project/Art/Ship/player_ship_sail.fbx",
        };
        var sb = new StringBuilder();
        for (int i = 0; i < paths.Length; i++)
        {
            sb.AppendLine(paths[i]);
            var all = AssetDatabase.LoadAllAssetsAtPath(paths[i]);
            if (all == null || all.Length == 0) { sb.AppendLine("  (nothing imported)"); continue; }
            for (int k = 0; k < all.Length; k++)
            {
                Object o = all[k];
                if (o == null) continue;
                string extra = "";
                Mesh m = o as Mesh;
                if (m != null)
                    extra = string.Format("  verts {0}, tris {1}, bounds {2}",
                        m.vertexCount, m.triangles.Length / 3, m.bounds.size);
                sb.AppendLine("  " + o.GetType().Name + "  '" + o.name + "'" + extra);
            }
            sb.AppendLine();
        }
        System.IO.File.WriteAllText("/tmp/seasick-shipart.txt", sb.ToString());
        Debug.Log("InspectShipArt:\n" + sb);
        return "wrote /tmp/seasick-shipart.txt";
    }
}
