using System.Text;
using SeaSick.UI;
using SeaSick.World;
using UnityEngine;

/// **Wall tower check (2026-09-27): a watchtower sited on the wall.**
///
/// PLAY mode, a watched camp. One `unity cmd eval` call:
///
///   `WallTowerCheck.Run(farWall, capturePath)`
///
/// 1. Finds a wall node: a lattice point inside (else the end of) the first
///    standing plain segment. With none -- or with `farWall` -- it queues an
///    8 m run 62 m from the fire (past `Outpost.TownRadius`, and past the
///    46 m this used to test at -- Kevin's phone bug was past 40 m but
///    still snapped there) through `Outpost.SiteWall` and force-completes
///    it (DEV ONLY: writes the books).
/// 2. Sites a watchtower through the REAL siting path: `CampSiting.Begin`,
///    the drawing moved 1.4 m off the node (`CampSiting.MoveTo`, what a tap
///    does), then `CampSiting.Confirm` (the ✓). Reports whether it snapped,
///    the label, and any refusal (the one-of-each rule and a locked plan
///    refuse here exactly as in play). **Also asks the camera** whether the
///    node is inside `IslandCam.ClampPivot`'s reach -- `MoveTo` alone would
///    pass even if the phone's thumb could never have panned or dragged
///    there, which is exactly the shape of bug this project already hit
///    once (`CanPlace` was fine in isolation; the phone still could not
///    reach the spot to ask it). `IslandCam.ExtraReachCentre/Radius`,
///    widened by `CampSiting.Begin`, is what is being asked here.
/// 3. Force-completes the tower row and reports: the tower on the node,
///    its distance from the fire (may exceed 40 m), the post at the node
///    hidden, the node's cell blocked for raiders AND hands, and the
///    lookout's door walkable and reachable from the fire.
///
/// Optional screenshot to `capturePath` with the island camera over the
/// node (written at the end of the frame).
///
/// e.g. `unity cmd eval --json --code 'return WallTowerCheck.Run(false, "/tmp/walltower.png");'`
public static class WallTowerCheck
{
    /// Metres from the fire the queued run goes when `farWall` is asked
    /// for, or when no wall stands to test against at all. Past
    /// `Outpost.TownRadius` (40) by enough margin that a fix which only
    /// nudges the reach a few metres would still be caught failing.
    const float FarWallDistance = 62f;

    public static string Run(bool farWall = false, string capturePath = null)
    {
        var sb = new StringBuilder("WallTowerCheck.Run\n");
        if (!Application.isPlaying) return sb.Append("FAIL: needs play mode").ToString();

        Outpost camp = null;
        foreach (var o in Outpost.All)
            if (o != null && o.Watched && o.HasCamp && o.Ledger != null) { camp = o; break; }
        if (camp == null) return sb.Append("FAIL: no watched camp with a fire").ToString();

        // --- 1. a wall node ---------------------------------------------------
        Vector3 node = default;
        bool haveNode = false;
        if (!farWall)
            foreach (var w in camp.Walls)
            {
                if (w == null || w.IsGate) continue;
                Vector3 mid = camp.SnapPost(w.Midpoint);
                bool interior = w.FlatDistanceTo(mid) < 0.1f
                                && (mid - w.A).sqrMagnitude > 0.1f && (mid - w.B).sqrMagnitude > 0.1f;
                node = interior ? mid : w.A;
                haveNode = true;
                sb.AppendLine($"wall: standing {w.A:F0}->{w.B:F0}, node {(interior ? "inside" : "at end")} {node:F0}");
                break;
            }
        if (!haveNode)
        {
            float r = farWall ? FarWallDistance : 20f;
            for (int k = 0; k < 16 && !haveNode; k++)
            {
                float t = k * Mathf.PI * 2f / 16f;
                var dir = new Vector3(Mathf.Sin(t), 0f, Mathf.Cos(t));
                Vector3 a = camp.SnapPost(camp.CampCentre + dir * r);
                Vector3 b = camp.SnapPost(a + Vector3.Cross(Vector3.up, dir) * 8f);
                var row = camp.SiteWall(a, b, out string why);
                if (row == null) continue;
                Pay(row);
                camp.CatchUp();
                if (camp.WallOn(camp.SnapPost(a), camp.SnapPost(b)) == null)
                { sb.AppendLine($"wall: queued at {a:F0} but did not raise"); continue; }
                node = camp.SnapPost(0.5f * (a + b));
                if (!camp.FindWallNode(node, 0.3f, out node)) node = camp.SnapPost(a);
                haveNode = true;
                sb.AppendLine($"wall: queued+raised {a:F0}->{b:F0} ({r:F0} m out), node {node:F0}");
            }
        }
        if (!haveNode) return sb.Append("FAIL: no wall and none could be sited").ToString();

        // --- 2. site the tower through the siting mode ------------------------
        CampSiting.Begin(camp, BuildPlans.Watchtower, camp.transform);
        if (!CampSiting.Placing) return sb.Append("FAIL: CampSiting did not start (no instance?)").ToString();

        // **The camera's own reach (2026-09-27 fix).** `CampSiting.Begin`
        // widens `IslandCam.ExtraReachCentre/Radius` to the camp's walls'
        // extent; ask `ClampPivot` whether the far node itself is still
        // admissible, which is the thing `MoveTo` below cannot tell us --
        // it moves the ghost directly and never touches the camera at all.
        float fromFireNow = Flat(node, camp.CampCentre);
        var islandCam = Object.FindFirstObjectByType<SeaSick.CameraRig.IslandCam>();
        bool camReach = islandCam == null // no island camera in this scene: not this check's problem
            || (islandCam.ClampPivot(node) - node).sqrMagnitude < 0.25f;
        sb.AppendLine($"camera reach: node is {fromFireNow:F0} m from the fire, "
            + $"ExtraReachRadius {SeaSick.CameraRig.IslandCam.ExtraReachRadius:F0} m, "
            + $"ClampPivot admits it: {(camReach ? "YES" : "NO -- the thumb could not have panned here")}");

        CampSiting.MoveTo(node + new Vector3(1.2f, 0f, 0.7f));
        bool snapped = CampSiting.OnWall;
        Vector3 ghostAt = CampSiting.GhostAt;
        sb.AppendLine($"siting: snapped {(snapped ? "YES" : "no")} ghost {ghostAt:F1} on node {Flat(ghostAt, node) < 0.2f}, "
            + $"valid {CampSiting.CanConfirm}, label \"{CampSiting.ClearLine}\", refusal \"{CampSiting.Refusal}\"");
        CampSiting.Confirm();
        if (CampSiting.Placing)
        {
            string why = CampSiting.Refusal;
            CampSiting.End();
            return sb.Append($"FAIL: ✓ refused: {why}").ToString();
        }

        // --- 3. force-complete and report -------------------------------------
        PendingBuild towerRow = null;
        foreach (var r in camp.Ledger.sites)
            if (r != null && r.planId == BuildPlans.Watchtower.id && Flat(r.At, node) < 0.2f) towerRow = r;
        if (towerRow != null) { Pay(towerRow); camp.CatchUp(); }
        var tower = camp.WallTowerAt(node);
        sb.AppendLine($"tower: row {(towerRow != null ? "queued" : "MISSING")}, built on node {(tower != null ? "YES" : "NO")}");
        if (tower == null) return sb.Append("FAIL: no wall tower stands on the node").ToString();

        // The gun's Start would do this next frame; do it now to report.
        camp.TouchWallTowers();
        var chain = WallChain.Of(camp.transform);
        if (chain != null) chain.Refresh();

        float fromFire = Flat(tower.transform.position, camp.CampCentre);
        sb.AppendLine($"distance from fire {fromFire:F1} m (town reach {Outpost.TownRadius:F0} m){(fromFire > Outpost.TownRadius ? " -- PAST the reach" : "")}");
        bool postHidden = chain == null || !chain.HasPostAt(node);
        sb.AppendLine($"post at node hidden: {postHidden}{(chain == null ? " (no chain: extruded fallback)" : "")}");

        int runs = 0;
        foreach (var w in camp.Walls)
            if (w != null && (Flat(w.A, node) < 0.2f || Flat(w.B, node) < 0.2f)) runs++;
        sb.AppendLine($"runs ending at the tower: {runs}");

        var map = CampPath.For(camp);
        bool raiderBlocked = map != null && !map.WalkableAt(node, CampPath.Walker.Raider);
        bool handBlocked = map != null && !map.WalkableAt(node, CampPath.Walker.Hand);
        sb.AppendLine($"CampPath node cell blocked: raider {raiderBlocked}, hand {handBlocked}");

        Vector3 door = CampWorker.WorkSpot(camp, tower);
        bool doorOpen = map != null && map.WalkableAt(door, CampPath.Walker.Hand);
        bool doorReach = map != null && map.HasRoute(camp.CampCentre, door, CampPath.Walker.Hand);
        bool doorInside = !CampPath.Crosses(camp, door, camp.CampCentre, CampPath.Walker.Hand);
        sb.AppendLine($"lookout door {door:F1}: walkable {doorOpen}, route from fire {doorReach}, "
            + $"straight line to fire crosses no wall {doorInside}");

        bool pass = snapped && camReach && postHidden && raiderBlocked && doorReach && runs >= 1;
        sb.AppendLine(pass ? "PASS" : "FAIL (see lines above)");

        if (!string.IsNullOrEmpty(capturePath))
        {
            var cam = Object.FindFirstObjectByType<SeaSick.CameraRig.IslandCam>();
            if (cam != null) cam.LookAt(node, 25f);
            ScreenCapture.CaptureScreenshot(capturePath);
            sb.AppendLine($"capture -> {capturePath} (end of frame)");
        }
        return sb.ToString();
    }

    /// Every phase of a row paid, as `Outpost.MakeCamp` does for the fire.
    static void Pay(PendingBuild row)
    {
        row.done = row.needed; row.donePart = 0f;
        row.stoneDone = row.stoneNeeded; row.stoneDonePart = 0f;
        row.brickDone = row.brickNeeded; row.brickDonePart = 0f;
        row.clearDone = row.ClearTotal;
        row.built = row.LabourNeeded;
    }

    static float Flat(Vector3 a, Vector3 b)
        => Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));
}
