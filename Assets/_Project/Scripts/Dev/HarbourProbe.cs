using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SeaSick.World;
using SeaSick.Terrain;

/// Where the home island's dock goes, reported from the SAME finder the
/// populator builds from.
///
/// Runs in play mode against the real Island_Home rather than working out
/// which island is home for itself. The first version of this hill-climbed
/// to its own idea of the island centre and landed 500 m from the one the
/// populator had already found -- two answers to a question with one right
/// answer, which is the failure this project keeps paying for.
public class HarbourProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("HarbourProbe: not in play mode"); return; }
        var sb = new StringBuilder();

        Island home = null;
        foreach (var i in Island.All) if (i.IsHome) home = i;
        if (home == null) { Report("no home island\n"); return; }
        if (Island.TerrainHeight == null) { Report("no terrain height function\n"); return; }

        var H = Island.TerrainHeight;
        Vector3 centre = home.transform.position;
        sb.AppendLine($"'{home.name}' centre ({centre.x:F0}, {centre.z:F0}), mean radius {home.Radius:F0} m");

        var all = new List<HarbourSite.Site>();
        var best = HarbourSite.Find(centre, HarbourSite.SearchRadiusFor(home.Radius), (x, z) => H(x, z), all);
        sb.AppendLine($"{all.Count} shore cells passed all four tests (depth, approach, backshore, land)");
        if (!best.found) { Report(sb + "no site met them\n"); return; }

        all.Sort((a, b) => b.score.CompareTo(a.score));
        sb.AppendLine();
        sb.AppendLine("best candidates (score = pier + backshore + flat behind + shelter):");
        sb.AppendLine("   score   pier  depth  backslope  flat-behind  shelter   at");
        var shown = new List<HarbourSite.Site>();
        foreach (var c in all)
        {
            bool near = false;
            foreach (var d in shown)
                if ((d.root - c.root).sqrMagnitude < 120f * 120f) { near = true; break; }
            if (near) continue;                       // one row per stretch of coast
            shown.Add(c);
            sb.AppendLine($"   {c.score,5:F2}  {c.pierLength,4:F0}m {c.berthDepth,5:F1}m "
                + $"{Mathf.Atan(c.backSlope) * Mathf.Rad2Deg,7:F0} deg {c.flatBehind,10:F2} ha "
                + $"{c.shelter * 100f,7:F0}%   ({c.root.x:F0}, {c.root.z:F0})");
            if (shown.Count >= 6) break;
        }

        sb.AppendLine();
        sb.AppendLine("CHOSEN:");
        sb.AppendLine($"   root  ({best.root.x:F0}, {best.root.z:F0}) standing {best.root.y:F1} m above the water");
        sb.AppendLine($"   head  ({best.head.x:F0}, {best.head.z:F0})");
        sb.AppendLine($"   berth ({best.berth.x:F0}, {best.berth.z:F0})");
        sb.AppendLine($"   pier {best.pierLength:F0} m = {best.pierLength / WorldScale.ShipLength:F1} ship lengths, "
            + $"{best.berthDepth:F1} m under her at the head");
        sb.AppendLine($"   backshore {Mathf.Atan(best.backSlope) * Mathf.Rad2Deg:F0} deg, "
            + $"{best.flatBehind:F2} ha buildable within {HarbourSite.Hinterland} m, "
            + $"shelter {best.shelter * 100f:F0}%");

        sb.AppendLine("   depth from the root, seaward:");
        for (float d = 0f; d <= best.pierLength + HarbourSite.ApproachRun; d += 20f)
            sb.AppendLine($"      {d,4:F0} m: {H(best.root.x + best.seaward.x * d, best.root.z + best.seaward.y * d),6:F1} m");

        // How far she has to come from the spawn, and whether anything is in
        // the way on the straight line -- a berth she cannot reach is not one.
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        if (motor != null)
        {
            Vector3 from = motor.transform.position;
            float run = Vector3.Distance(new Vector3(from.x, 0f, from.z), best.berth);
            float shallowest = -999f;
            for (float t = 0.05f; t < 1f; t += 0.01f)
            {
                Vector3 p = Vector3.Lerp(new Vector3(from.x, 0f, from.z), best.berth, t);
                shallowest = Mathf.Max(shallowest, H(p.x, p.z));
            }
            sb.AppendLine($"   from the ship's spawn: {run:F0} m, shallowest water on the straight "
                + $"line {shallowest:F1} m " + (shallowest > -HarbourSite.ApproachDepth
                    ? "-- SHE WOULD GROUND, the approach needs a dogleg" : "-- clear"));
        }
        // ---- what actually got BUILT -------------------------------------
        // Everything above is the site finder describing its own intentions.
        // These read the dock in the scene: its mesh, its piles, and the
        // water under the ship-sized box where she is supposed to lie.
        var dock = Dock.Home;
        sb.AppendLine();
        if (dock == null) { sb.AppendLine("NO DOCK BUILT"); Report(sb.ToString()); return; }

        // Whose island is this dock actually on? The search radius used to be
        // a constant and put the home dock on the neighbour, which every
        // other number then reported perfectly correctly about the wrong
        // island.
        var owner = Island.Nearest(dock.Root);
        sb.AppendLine($"   the dock stands on '{(owner != null ? owner.name : "nothing")}'"
            + (owner == home ? " -- home" : " -- NOT THE HOME ISLAND"));

        var mf = dock.GetComponent<MeshFilter>();
        var verts = mf.sharedMesh.vertices;
        float deckTop = -999f, lowest = 999f;
        foreach (var lv in verts)
        {
            Vector3 w = dock.transform.TransformPoint(lv);
            if (w.y > deckTop) deckTop = w.y;
            if (w.y < lowest) lowest = w.y;
        }
        sb.AppendLine("DOCK AS BUILT");
        sb.AppendLine($"   {verts.Length} verts, {mf.sharedMesh.triangles.Length / 3} tris");
        sb.AppendLine($"   highest point {deckTop:F2} m above sea level "
            + $"(bollards stand on decking at {dock.DeckY:F2})");
        sb.AppendLine($"   deepest pile foot {lowest:F1} m");

        // Which way the faces are lit. A mesh can have correct winding and
        // inverted normals at the same time -- it draws, and every surface
        // renders ambient-only near-black. Nothing else in the scene would
        // have said so, and a screenshot only says "dark", which on a
        // weathered timber pier under a northern sky is not obviously wrong.
        var norms = mf.sharedMesh.normals;
        int topV = 0;
        for (int i = 0; i < verts.Length; i++) if (verts[i].y > verts[topV].y) topV = i;
        int upN = 0, downN = 0;
        foreach (var nn in norms) { if (nn.y > 0.7f) upN++; else if (nn.y < -0.7f) downN++; }
        sb.AppendLine($"   highest vertex normal {norms[topV]:F2} "
            + (norms[topV].y > 0.5f ? "-- facing the sky" : "-- INVERTED, the decking is lit from below")
            + $"   ({upN} up / {downN} down)");

        // Is the walkway clear of the ground it crosses, or buried in the
        // beach? Sampled along the centreline from root to head.
        float worstBury = -999f; float buryAt = 0f;
        for (float d = 0f; d <= best.pierLength + HarbourSite.BerthOffset; d += 1f)
        {
            float px = best.root.x + best.seaward.x * d, pz = best.root.z + best.seaward.y * d;
            float ground = H(px, pz);
            float under = dock.DeckY - 0.35f;
            if (d < 7f) continue;                    // the ramp is meant to touch
            if (ground - under > worstBury) { worstBury = ground - under; buryAt = d; }
        }
        sb.AppendLine($"   ground under the decking: worst is {worstBury:+0.00;-0.00} m at {buryAt:F0} m out "
            + (worstBury > 0f ? "-- BURIED, the walkway runs through the beach" : "-- clear"));

        // Does she fit? Her whole box, at the berth, in the water, without
        // touching the pier.
        float shallow = 999f;
        for (float t2 = -0.5f; t2 <= 0.5f; t2 += 0.05f)
            for (float b2 = -0.5f; b2 <= 0.5f; b2 += 0.25f)
            {
                float px = dock.Berth.x + best.seaward.x * t2 * WorldScale.ShipLength
                         + -best.seaward.y * b2 * 8.44f;
                float pz = dock.Berth.z + best.seaward.y * t2 * WorldScale.ShipLength
                         + best.seaward.x * b2 * 8.44f;
                shallow = Mathf.Min(shallow, -H(px, pz));
            }
        sb.AppendLine($"   water under her whole 24.2 x 8.4 m box: {shallow:F1} m at its shallowest "
            + (shallow < 2.0f ? "-- SHE TOUCHES" : "-- afloat"));

        float sideGap = HarbourSite.BerthOffset - DockBuilder.HeadWidth * 0.5f - 8.44f * 0.5f;
        sb.AppendLine($"   daylight between her side and the pier head: {sideGap:F2} m "
            + (sideGap < 0.3f ? "-- SHE IS INSIDE THE PIER" : ""));

        // ---- and where SHE actually is ----------------------------------
        sb.AppendLine();
        if (motor == null) { Report(sb.ToString()); return; }
        Vector3 at = motor.transform.position;
        float off = dock.DistanceFrom(at);
        float headErr = Mathf.Abs(Mathf.DeltaAngle(motor.transform.eulerAngles.y,
            dock.Heading.eulerAngles.y));
        sb.AppendLine("THE SHIP");
        sb.AppendLine($"   {off:F1} m off her berth, heading {headErr:F0} deg from parallel");

        // Aground? Her own box against the seabed, in her CURRENT attitude,
        // not the berth's.
        Vector3 fwd = motor.transform.forward, rgt = motor.transform.right;
        float clear = 999f;
        for (float t3 = -0.5f; t3 <= 0.5f; t3 += 0.05f)
            for (float b3 = -0.5f; b3 <= 0.5f; b3 += 0.25f)
            {
                Vector3 p = at + fwd * (t3 * WorldScale.ShipLength) + rgt * (b3 * 8.44f);
                clear = Mathf.Min(clear, -H(p.x, p.z));
            }
        sb.AppendLine($"   {clear:F1} m of water under her, shallowest "
            + (clear < 1.5f ? "-- AGROUND" : "-- afloat"));

        var anchor = motor.GetComponent<SeaSick.Ship.AnchorController>();
        if (anchor != null)
            sb.AppendLine($"   state {anchor.CurrentState}, "
                + (anchor.CurrentDock != null ? "at the dock" : "not at a dock"));

        // ---- can you actually SEE anything from up there? ----------------
        //
        // The whole point of the overview is watching people move between
        // buildings, and "is the island in frame" does not answer that. A
        // 1.7 m crew member has an on-screen height in PIXELS and that is the
        // number to report, because it is the one that decides whether the
        // view works.
        var village = home.GetComponent<Settlement>();
        sb.AppendLine();
        sb.AppendLine("THE SETTLEMENT");
        if (village == null) sb.AppendLine("   none measured on this island");
        else
            sb.AppendLine($"   {village.AreaHectares:F2} ha of joined-up buildable ground, "
                + $"core {village.Core:F0} m, framed at {village.ViewRadius:F0} m (extent {village.Extent:F0} m -- tendrils), compound "
                + $"{village.Inscribed * 2f:F0} m across, centred "
                + $"({village.Centre.x:F0}, {village.Centre.z:F0}) at {village.Centre.y:F0} m");

        var chase = FindAnyObjectByType<SeaSick.CameraRig.ChaseCamera>();
        if (chase != null)
        {
            var ccam = chase.GetComponent<Camera>();
            float fov = ccam != null ? ccam.fieldOfView : 60f;
            // The framing distance is the rig's SPAN -- how far it sits from
            // the point it is framing -- not the distance to the village.
            // The frame is centred between the village and the boat, so
            // measuring to the village alone under-reports the zoom: it said
            // 112 m of ground where the rig was drawing 165.
            float dist = chase.CurrentSpan > 0.01f
                ? chase.CurrentSpan
                : Vector3.Distance(chase.transform.position,
                    village != null ? village.Centre : home.transform.position);
            float frac = chase.PersonScreenFraction(dist, fov);
            float px = frac * Screen.height;
            float onPhone = frac * 2340f;
            sb.AppendLine();
            sb.AppendLine("THE VIEW");
            sb.AppendLine($"   overview {(chase.Overview.HasValue ? "ON" : "off")}, "
                + $"{chase.transform.position.y:F0} m up, {dist:F0} m from what it is framing, "
                + $"far clip {(ccam != null ? ccam.farClipPlane : 0f):F0} m, screen {Screen.width}x{Screen.height}");
            sb.AppendLine($"   a crew member is {frac * 100f:F2}% of screen height = "
                + $"{px:F0} px in this window, {onPhone:F0} px on a 2340-tall phone"
                + (frac < 0.005f ? "   -- TOO SMALL TO READ" : ""));
            sb.AppendLine($"   on a phone: hut {onPhone * WorldScale.Hut / WorldScale.Person:F0} px,  "
                + $"longhouse {onPhone * WorldScale.Longhouse / WorldScale.Person:F0} px,  "
                + $"watchtower {onPhone * WorldScale.WatchTower / WorldScale.Person:F0} px,  "
                + $"the ship {onPhone * WorldScale.ShipLength / WorldScale.Person:F0} px long");
            sb.AppendLine($"   ground in frame: about {2f * dist * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad):F0} m tall");
            sb.AppendLine($"   tilt {Mathf.Asin(Mathf.Clamp01(-ccam.transform.forward.y)) * Mathf.Rad2Deg:F0} deg above the horizon");

            // Against the framing Kevin flew to by hand. Azimuth is the one
            // that went wrong: my derivation sat the camera INLAND looking
            // seaward, 136 degrees from his, and every other number in this
            // report was correct while the picture was of the wrong side.
            //
            // Gated against the DOCK's own statement of the shot, not against
            // world numbers. Kevin's framing was stored here as a bearing of
            // -14 degrees and a point at (-95, 82), and those describe one
            // pier on one island: the authored home island put the harbour in
            // a cove on the far side, the camera followed the pier exactly as
            // it is meant to, and this gate reported "off his framing by 172
            // degrees". The picture was his; the yardstick was not. `Dock`
            // already holds the rule -- 41 m inland of the root, 22 m to
            // starboard, 5 degrees off the pier's seaward bearing -- in ONE
            // place, so the only honest question left is whether the camera
            // is obeying it.
            Vector3 rel = chase.transform.position - (dock != null ? dock.Root : home.transform.position);
            if (chase.Overview.HasValue && dock != null)
            {
                Vector3 f3 = chase.Overview.Value.from; f3.y = 0f;
                Vector3 c3 = chase.Overview.Value.centre;
                Vector3 wantFrom = dock.ViewFrom, wantCentre = dock.ViewCentre;

                float azErr = Vector3.Angle(f3.normalized, wantFrom);
                float cErr = Vector2.Distance(new Vector2(c3.x, c3.z),
                                              new Vector2(wantCentre.x, wantCentre.z));
                Vector3 sea3 = dock.Seaward, stb3 = new Vector3(sea3.z, 0f, -sea3.x);
                Vector3 d4 = c3 - dock.Root;
                sb.AppendLine($"   the dock asks for: centre ({wantCentre.x:F0}, {wantCentre.z:F0}), "
                    + $"camera bearing {Mathf.Atan2(wantFrom.x, wantFrom.z) * Mathf.Rad2Deg:F0} deg");
                sb.AppendLine($"   the camera gives: centre ({c3.x:F0}, {c3.z:F0}) "
                    + $"= {-Vector3.Dot(d4, sea3):F0} m inland of the root (his 41), "
                    + $"{Vector3.Dot(d4, stb3):F0} m to starboard (his 22)");
                sb.AppendLine($"   span {chase.CurrentSpan:F0} m (his 254), fov {fov:F0} (his 36)");
                sb.AppendLine($"   off the dock's own framing by {azErr:F0} deg and {cErr:F0} m"
                    + (azErr < 8f && cErr < 15f && Mathf.Abs(chase.CurrentSpan - 254f) < 25f
                       && Mathf.Abs(fov - 36f) < 3f ? "   -- matches" : "   -- DOES NOT MATCH"));

                // How much of home is actually in the shot. The docked frame
                // is a long narrow wedge running up the beach -- 42 m either
                // side of its centre in portrait -- and the island is 160 m
                // across, so "can I see the whole island from the dock" has a
                // number and it is not a matter of opinion.
                sb.AppendLine($"   the frame holds {Dock.ViewHalfWidth * 2f:F0} m of ground ACROSS "
                    + $"(portrait, measured) against an island {home.Radius * 2f:F0} m wide"
                    + $"  ->  about {Mathf.Min(100f, Dock.ViewHalfWidth * 200f / Mathf.Max(1f, home.Radius * 2f)):F0}% of its width");
            }

            // "Can I see my ship" is a frustum question, not an opinion.
            // Tested against the real camera's planes, on her actual hull
            // corners rather than her centre -- a 24 m boat can have her
            // middle in frame and her bow out of it.
            if (motor != null)
            {
                var planes = GeometryUtility.CalculateFrustumPlanes(ccam);
                Vector3 f2 = motor.transform.forward, r2 = motor.transform.right;
                int seen = 0, corners = 0;
                for (float t4 = -0.5f; t4 <= 0.5f; t4 += 1f)
                    for (float b4 = -0.5f; b4 <= 0.5f; b4 += 1f)
                    {
                        Vector3 p = motor.transform.position
                                  + f2 * (t4 * WorldScale.ShipLength) + r2 * (b4 * 8.44f);
                        corners++;
                        if (GeometryUtility.TestPlanesAABB(planes, new Bounds(p, Vector3.one * 2f))) seen++;
                    }
                bool dockSeen = GeometryUtility.TestPlanesAABB(planes,
                    new Bounds(dock.Head, Vector3.one * 8f));
                sb.AppendLine($"   the ship: {seen} of {corners} hull corners in frame"
                    + (seen == corners ? " -- fully in shot" : seen == 0 ? " -- NOT VISIBLE" : " -- clipped"));
                sb.AppendLine($"   the pier head: {(dockSeen ? "in frame" : "NOT VISIBLE")}");
            }

            // And does the ground on show actually suit a village?
            if (village != null)
            {
                int cap = village.Capacity();
                sb.AppendLine($"   the settlement holds about {cap} buildings on 15 m plots "
                    + $"(village radius {village.VillageRadius():F0} m)"
                    + (cap < 15 ? "   -- FEWER THAN THE 15-20 ASKED FOR" : ""));
            }
        }

        Report(sb.ToString());
    }

    static void Report(string s)
    {
        System.IO.File.WriteAllText("/tmp/seasick-harbour.txt", s);
        Debug.Log("HarbourProbe\n" + s);
    }
}
