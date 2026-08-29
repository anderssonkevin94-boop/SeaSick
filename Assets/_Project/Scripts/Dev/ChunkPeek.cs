using System.Text;
using UnityEngine;
using SeaSick.World;

/// What the terrain chunks around a point ACTUALLY contain, versus what the
/// height function says should be there.
///
/// Written because a look sheet came back as open water with trees hanging
/// in the air over it: the height field said 88 m of island, the props were
/// placed on that height and floated at it, and no ground was drawn. That is
/// a disagreement between the FIELD and the MESH, and no amount of staring
/// at a screenshot resolves which side is wrong.
public class ChunkPeek : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("ChunkPeek: not in play mode"); return; }
        var sb = new StringBuilder();

        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        Vector3 ship = motor != null ? motor.transform.position : Vector3.zero;
        sb.AppendLine("ship at " + ship.ToString("F0"));

        Island target = null; float best = float.MaxValue;
        foreach (var isl in Island.All)
        {
            float d = Vector3.Distance(isl.transform.position, ship);
            if (d < best) { best = d; target = isl; }
        }
        if (target == null) { System.IO.File.WriteAllText("/tmp/seasick-peek.txt", "no islands\n"); return; }
        Vector3 c = target.transform.position;
        sb.AppendLine("island centre " + c.ToString("F0") + " radius " + target.Radius.ToString("F0"));

        // What the FIELD says along the line from the island centre out
        // toward the ship -- the exact bearing the look camera shoots down.
        Vector3 dir = ship - c; dir.y = 0f; dir.Normalize();
        sb.AppendLine("--- height field along the shot bearing ---");
        for (float d = 0f; d <= 900f; d += 60f)
        {
            Vector3 p = c + dir * d;
            float h = Island.TerrainHeight != null ? Island.TerrainHeight(p.x, p.z) : float.NaN;
            sb.AppendLine("  d=" + d.ToString("F0").PadLeft(4) + " m   field h=" + h.ToString("F1").PadLeft(7)
                + "   " + (h > 0f ? "LAND" : "water"));
        }

        // What the MESHES say. Chunks are named "Chunk" and positioned on the
        // chunk grid, so find the ones covering that same line.
        sb.AppendLine("--- chunk meshes near the island ---");
        var filters = FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
        int chunks = 0, empty = 0, drawn = 0;
        float highestVert = -9999f;
        foreach (var f in filters)
        {
            if (f.gameObject.name != "Chunk") continue;
            chunks++;
            var m = f.sharedMesh;
            var r = f.GetComponent<MeshRenderer>();
            if (m == null || m.vertexCount == 0) { empty++; continue; }
            if (r != null && r.enabled) drawn++;
            float top = f.transform.position.y + m.bounds.max.y;
            if (top > highestVert) highestVert = top;
            // Report the handful nearest the island centre in detail.
            if (Vector3.Distance(f.transform.position, c) < 260f)
                sb.AppendLine("  chunk at " + f.transform.position.ToString("F0")
                    + " verts=" + m.vertexCount
                    + " boundsY=[" + m.bounds.min.y.ToString("F1") + ", " + m.bounds.max.y.ToString("F1") + "]"
                    + " renderer=" + (r != null ? (r.enabled ? "on" : "OFF") : "none")
                    + " mat=" + (r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "NULL"));
        }
        sb.AppendLine("chunks=" + chunks + " empty=" + empty + " withRendererOn=" + drawn
            + " highest vertex in the whole world=" + highestVert.ToString("F1") + " m");

        System.IO.File.WriteAllText("/tmp/seasick-peek.txt", sb.ToString());
        Debug.Log("ChunkPeek\n" + sb);
    }
}
