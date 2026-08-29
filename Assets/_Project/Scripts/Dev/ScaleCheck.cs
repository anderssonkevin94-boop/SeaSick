using System.Text;
using UnityEngine;
using SeaSick.World;

/// Are the things on the land actually the size they claim to be?
///
/// Scale is the one thing in this project that cannot be judged from a
/// screenshot, because every cue in the frame is one of the things under
/// suspicion. So measure the real metres: the ship from her renderers, the
/// trees from the scenery mesh's height above the ground it stands on, and
/// the terrain from the height field.
public class ScaleCheck : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("ScaleCheck: not in play mode"); return; }
        var sb = new StringBuilder();

        // --- the ship, from her actual renderers ---
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        if (motor != null)
        {
            var rends = motor.GetComponentsInChildren<Renderer>();
            bool any = false; Bounds b = new Bounds();
            foreach (var r in rends)
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (any)
                sb.AppendLine($"ship renderer bounds: {b.size.x:F1} x {b.size.y:F1} x {b.size.z:F1} m "
                    + $"(hull is authored 20.9 x 8.4 m, 24.2 m overall)");
        }

        // --- the crew, who are the unit everything else is judged in ---
        //
        // Measured off their renderers, not read off the builder: they are
        // parented into the ship's hierarchy, and a scale anywhere above them
        // would silently multiply through.
        if (motor != null)
        {
            foreach (var t in motor.GetComponentsInChildren<Transform>())
            {
                if (t.name != "Helmsman" && t.GetComponent<SeaSick.Crew.CrewAgent>() == null) continue;
                var rs = t.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                sb.AppendLine($"crew '{t.name}': {b.size.y:F2} m tall  "
                    + $"(lossy scale {t.lossyScale.x:F2}) -- she is {24.2f / Mathf.Max(b.size.y, 0.01f):F1} of them long");
                break;
            }
            int hands = motor.GetComponentsInChildren<SeaSick.Crew.CrewAgent>(true).Length;
            sb.AppendLine($"crew aboard: {hands}");
        }

        // --- the trees, from the scenery mesh ---
        Island target = null; float best = float.MaxValue;
        Vector3 ship = motor != null ? motor.transform.position : Vector3.zero;
        foreach (var isl in Island.All)
        {
            float d = Vector3.Distance(isl.transform.position, ship);
            if (d < best) { best = d; target = isl; }
        }
        if (target == null || Island.TerrainHeight == null)
        {
            System.IO.File.WriteAllText("/tmp/seasick-scale.txt", sb + "no island or no height function\n");
            return;
        }

        Transform scenery = null;
        foreach (var t in target.GetComponentsInChildren<Transform>())
            if (t.name == "Scenery") { scenery = t; break; }

        if (scenery == null) sb.AppendLine("no Scenery mesh on the nearest island");
        else
        {
            var mf = scenery.GetComponent<MeshFilter>();
            var verts = mf.sharedMesh.vertices;
            sb.AppendLine($"scenery mesh: {verts.Length} vertices, {mf.sharedMesh.triangles.Length / 3} triangles, "
                + $"scale {scenery.lossyScale:F3}");

            // Height above the ground directly under each vertex. The tallest
            // of these IS the tree height, since the mesh is built in world
            // space standing on the terrain.
            var heights = new System.Collections.Generic.List<float>();
            // EVERY vertex, not every third: the cones are built as
            // triangle fans with the apex last, so a stride of 3 samples only
            // base vertices and reports a tree's height as its shoulder.
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 w = scenery.TransformPoint(verts[i]);
                float ground = Island.TerrainHeight(w.x, w.z);
                heights.Add(w.y - ground);
            }
            heights.Sort();
            int n = heights.Count;
            sb.AppendLine($"vertex height above ground: p50 {heights[n / 2]:F2} m   "
                + $"p90 {heights[n * 9 / 10]:F2} m   p99 {heights[n - n / 100]:F2} m   max {heights[n - 1]:F2} m");
            sb.AppendLine("   (trees are authored 11-26 m against her 24.3 m, so the max should be about 26)");
        }

        // --- the props, which are a SEPARATE set of trees ---
        int props = 0; float tallestProp = 0f;
        foreach (var rn in target.GetComponentsInChildren<ResourceNode>())
        {
            props++;
            var rs = rn.GetComponentsInChildren<Renderer>();
            foreach (var r in rs)
                tallestProp = Mathf.Max(tallestProp, r.bounds.max.y - Island.TerrainHeight(r.bounds.center.x, r.bounds.center.z));
        }
        sb.AppendLine($"resource props on this island: {props}, tallest stands {tallestProp:F1} m above ground");

        System.IO.File.WriteAllText("/tmp/seasick-scale.txt", sb.ToString());
        Debug.Log("ScaleCheck\n" + sb);
    }
}
