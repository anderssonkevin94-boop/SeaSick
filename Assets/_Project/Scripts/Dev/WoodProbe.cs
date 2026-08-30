using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SeaSick.World;

/// How thick is the wood where an outpost would go?
///
/// Read off the BAKED MESH, not off the scatter loop that wrote it. The
/// scenery is several hundred trees welded into one mesh, and the only
/// honest question about a keep-out is what the shipped geometry contains --
/// a check that re-ran the placement rules would agree with itself no matter
/// what the mesh held. (That is not hypothetical: the deck probe in this
/// same session reported six crew standing perfectly on planking they were
/// floating 7 cm above, because it asked the deck the same question the
/// placement code had.)
///
/// Trees are found by their trunk colour. IslandScenery builds a trunk as a
/// four-sided prism in one flat brown, then two canopy cones in greens, then
/// moves on -- so a run of consecutive trunk-brown vertices is exactly one
/// tree, and its base is the lowest of them.
public class WoodProbe : MonoBehaviour
{
    /// The best buildable site on the home island, from TuneIslands.Flats:
    /// the centre of the largest circle that fits inside its largest
    /// contiguous piece of under-10-degree ground.
    static readonly Vector2 Site = new Vector2(-522f, -518f);

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("WoodProbe: not in play mode"); return; }
        var sb = new StringBuilder();

        Transform scenery = null; Island isle = null; float best = float.MaxValue;
        foreach (var i in Island.All)
        {
            float d = Vector2.Distance(new Vector2(i.transform.position.x, i.transform.position.z), Site);
            if (d >= best) continue;
            foreach (var t in i.GetComponentsInChildren<Transform>())
                if (t.name == "Scenery") { scenery = t; isle = i; best = d; }
        }
        if (scenery == null) { Report("no Scenery mesh near the site\n"); return; }

        var mesh = scenery.GetComponent<MeshFilter>().sharedMesh;
        var verts = mesh.vertices;
        var cols = mesh.colors32;
        sb.AppendLine($"island '{isle.name}' at {isle.transform.position.x:F0},{isle.transform.position.z:F0}"
            + $" -- site is {best:F0} m from its centre");
        sb.AppendLine($"scenery mesh: {verts.Length} verts, {mesh.triangles.Length / 3} tris");

        // The trunk brown IslandScenery writes, exactly.
        var trunks = new List<Vector3>();
        int runStart = -1;
        for (int i = 0; i <= verts.Length; i++)
        {
            bool brown = i < verts.Length && cols[i].r == 92 && cols[i].g == 64 && cols[i].b == 40;
            if (brown && runStart < 0) runStart = i;
            else if (!brown && runStart >= 0)
            {
                Vector3 lo = verts[runStart];
                double sx = 0, sz = 0; int n = i - runStart;
                for (int k = runStart; k < i; k++)
                {
                    if (verts[k].y < lo.y) lo = verts[k];
                    sx += verts[k].x; sz += verts[k].z;
                }
                trunks.Add(scenery.TransformPoint(new Vector3((float)(sx / n), lo.y, (float)(sz / n))));
                runStart = -1;
            }
        }
        sb.AppendLine($"{trunks.Count} trees standing in it");

        // Density in rings around the site. Reported per hectare, because
        // "how many trees" means nothing without the area they are spread
        // over and the rings have wildly different areas.
        float[] edges = { 0f, 10f, 20f, 35f, 50f, 65f, 100f, 150f };
        float nearest = float.MaxValue;
        sb.AppendLine();
        sb.AppendLine($"around the site ({Site.x:F0}, {Site.y:F0}):");
        for (int r = 0; r < edges.Length - 1; r++)
        {
            int n = 0;
            foreach (var t in trunks)
            {
                float d = Vector2.Distance(new Vector2(t.x, t.z), Site);
                if (d < nearest) nearest = d;
                if (d >= edges[r] && d < edges[r + 1]) n++;
            }
            float ha = Mathf.PI * (edges[r + 1] * edges[r + 1] - edges[r] * edges[r]) / 10000f;
            sb.AppendLine($"   {edges[r],3:F0}-{edges[r + 1],3:F0} m: {n,4} trees   "
                + $"{n / ha,6:F0} per hectare   ({ha:F2} ha of ground)");
        }
        sb.AppendLine($"   nearest tree to the site: {nearest:F1} m");

        // What a cleared compound has to displace, in the units the charter
        // uses: a 40 m walled yard and the band outside it.
        int inWall = 0, inGlacis = 0;
        foreach (var t in trunks)
        {
            float d = Vector2.Distance(new Vector2(t.x, t.z), Site);
            if (d < 20f) inWall++;
            else if (d < 35f) inGlacis++;
        }
        // Is this site clear because a wood happens to stop short of it, or
        // because nothing could ever grow there? IslandScenery refuses any
        // spot under sandHeight + 1.2 m, and the flattest ground on an island
        // tends to be the LOWEST ground -- so "the best buildable site" and
        // "ground the scatter already skips" may be the same places, and a
        // keep-out would then be solving a problem that is not there.
        float siteY = Island.TerrainHeight != null ? Island.TerrainHeight(Site.x, Site.y) : -999f;
        sb.AppendLine();
        sb.AppendLine($"ground at the site: {siteY:F1} m "
            + "(the scatter plants nothing under sandHeight + 1.2 = 4.4 m)");

        // How thick the wood actually is, wherever it IS. Nearest-neighbour
        // spacing is the number that decides whether it reads as woodland or
        // as an orchard -- IslandScenery aims for 7-12 m so canopies touch.
        var gaps = new List<float>();
        for (int i = 0; i < trunks.Count; i++)
        {
            float d2 = float.MaxValue;
            for (int j = 0; j < trunks.Count; j++)
            {
                if (i == j) continue;
                float dx = trunks[i].x - trunks[j].x, dz = trunks[i].z - trunks[j].z;
                float d = dx * dx + dz * dz;
                if (d < d2) d2 = d;
            }
            gaps.Add(Mathf.Sqrt(d2));
        }
        gaps.Sort();
        if (gaps.Count > 0)
            sb.AppendLine($"tree spacing, nearest neighbour: p10 {gaps[gaps.Count / 10]:F1} m   "
                + $"p50 {gaps[gaps.Count / 2]:F1} m   p90 {gaps[gaps.Count * 9 / 10]:F1} m   "
                + $"(authored intent is 7-12 m, so canopies touch)");

        // The other candidate: IslandScenery plants nothing past
        // `radiusAt(bearing) * 0.94`, and an island is not a disc. A radial
        // outline measured from the centre stops at the FIRST water it
        // crosses, so a bay bitten into one side cuts the radius on that
        // bearing and everything beyond the bay -- real, walkable, buildable
        // land -- falls outside the scatter. This is the same shape of error
        // as the minimap drawing a circle for a crescent.
        Vector3 c = isle.transform.position;
        float bearing = Mathf.Atan2(Site.x - c.x, Site.y - c.z);
        float rHere = isle.RadiusAt(bearing);
        float dSite = Vector2.Distance(new Vector2(c.x, c.z), Site);
        sb.AppendLine($"island radial outline on the site's bearing: {rHere:F0} m, "
            + $"scatter stops at {rHere * 0.94f:F0} m; the site is {dSite:F0} m out -- "
            + (dSite > rHere * 0.94f ? "OUTSIDE the scatter" : "inside it"));

        // How much of the island's land the radial disc actually covers.
        int inDisc = 0, outDisc = 0;
        for (int a = 0; a < 720; a++)
        {
            float ang = a / 720f * Mathf.PI * 2f;
            for (float d = 10f; d < 700f; d += 10f)
            {
                float wx = c.x + Mathf.Sin(ang) * d, wz = c.z + Mathf.Cos(ang) * d;
                if (Island.TerrainHeight == null) break;
                if (Island.TerrainHeight(wx, wz) <= 0.5f) continue;
                if (d <= isle.RadiusAt(ang) * 0.94f) inDisc++; else outDisc++;
            }
        }
        if (inDisc + outDisc > 0)
            sb.AppendLine($"land inside the scatter's reach: {100f * inDisc / (inDisc + outDisc):F0}% "
                + $"-- {100f * outDisc / (inDisc + outDisc):F0}% of this island's dry land can never be dressed");

        // Where the wood STOPS, measured off the mesh rather than recomputed
        // from IslandScenery's rule. `treeLine = max(18, peak * 0.62)` has a
        // world constant in it, and on an island whose summit is low that
        // constant -- not the island -- is what decides where the trees end.
        var treeY = new List<float>();
        foreach (var t in trunks) treeY.Add(t.y);
        treeY.Sort();
        if (treeY.Count > 0)
            sb.AppendLine($"tree base elevation: p50 {treeY[treeY.Count / 2]:F1} m   "
                + $"p90 {treeY[treeY.Count * 9 / 10]:F1} m   highest {treeY[treeY.Count - 1]:F1} m"
                + $"  <- the tree line as it actually shipped");
        sb.AppendLine($"the site stands at {siteY:F1} m, so it is "
            + (treeY.Count > 0 && siteY > treeY[treeY.Count - 1] ? "ABOVE" : "below")
            + " the highest tree on this island");

        // One island is an anecdote. Every island the populator built says
        // whether that 18 m floor is a quirk of home or the rule.
        sb.AppendLine();
        sb.AppendLine("every island, from its own baked mesh:");
        int floored = 0, counted = 0;
        foreach (var other in Island.All)
        {
            Transform sc = null;
            foreach (var t in other.GetComponentsInChildren<Transform>())
                if (t.name == "Scenery") sc = t;
            if (sc == null) continue;
            var m2 = sc.GetComponent<MeshFilter>().sharedMesh;
            var v2 = m2.vertices; var c2 = m2.colors32;
            // The tree LINE is the highest ground a tree stands on, so it is
            // the highest of the per-tree BASES. Taking the highest brown
            // vertex outright measures the top of a trunk instead and reads
            // about 4.5 m too high -- which is how this first reported the
            // home island's line at 22.4 m against the 17.9 m its own bases
            // give.
            float hiTree = -999f; int n2 = 0; int start = -1;
            for (int i = 0; i <= v2.Length; i++)
            {
                bool brown = i < v2.Length && c2[i].r == 92 && c2[i].g == 64 && c2[i].b == 40;
                if (brown && start < 0) start = i;
                else if (!brown && start >= 0)
                {
                    float baseY = float.MaxValue;
                    for (int k = start; k < i; k++) if (v2[k].y < baseY) baseY = v2[k].y;
                    if (baseY > hiTree) hiTree = baseY;
                    n2++; start = -1;
                }
            }
            if (n2 == 0) continue;
            counted++;
            bool isFloor = hiTree < 18.5f;
            if (isFloor) floored++;
            sb.AppendLine($"   {other.name,-14} {n2,4} trees, highest stands at {hiTree,6:F1} m"
                + (isFloor ? "   <- the 18 m floor, not this island" : ""));
        }
        if (counted > 0)
            sb.AppendLine($"   {floored} of {counted} islands have their tree line set by the "
                + "world constant rather than by their own summit");

        sb.AppendLine();
        sb.AppendLine($"a 40 m walled yard here stands on {inWall} trees; "
            + $"a 15 m cleared band outside the wall adds {inGlacis} more");
        Report(sb.ToString());
    }

    static void Report(string s)
    {
        System.IO.File.WriteAllText("/tmp/seasick-wood.txt", s);
        Debug.Log("WoodProbe\n" + s);
    }
}
