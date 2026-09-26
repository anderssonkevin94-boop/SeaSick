using System.Collections.Generic;
using System.Text;
using SeaSick.UI;
using SeaSick.World;
using UnityEngine;

/// **Cliff ladder check (2026-09-27): a ladder chain up a cliff.**
///
/// (Named `CliffLadderCheck` because `LadderCheck` is already the SHIP
/// ladder probe, in this same global namespace.)
///
/// PLAY mode, a camp with a fire. One `unity cmd eval` call:
///
///   `CliffLadderCheck.Run(islandName, capturePath)`
///
/// `islandName` null/"" = the first watched camp; otherwise the camp whose
/// island's GameObject name contains it.
///
/// 1. Finds a cliff near the camp: a foot on the fire's ground and a top
///    2-8 m away and 6-15 m higher, both standable, preferring a top the
///    hands CANNOT reach yet, that `Outpost.CanPlaceLadder` accepts.
/// 2. Before: can a hand reach a target on the plateau (a few metres past
///    the top), and how long is the route?
/// 3. Sites the chain through the REAL siting path (`CampSiting.Begin` with
///    the ladder plan, `LadderSiting.SetEnds` as the two taps would,
///    `CampSiting.Confirm` = ✓), then force-completes the row (DEV ONLY:
///    writes the books, the chain stays standing and is saved).
/// 4. After: flights/landings/rise/climb time, the link on the map, the
///    hand's route and length again, whether a RAIDER routes over it, that
///    a GOAT cannot (animals never ask the camp's map, and the straight
///    line up the face fails the goat's own step test), and a simulated
///    climb (`LadderClimb` on a throwaway transform) reaching the top.
///
/// Optional screenshot to `capturePath` with the island camera on the chain
/// (written at the end of the frame).
///
/// e.g. `unity cmd eval --json --code 'return CliffLadderCheck.Run("", "/tmp/ladder.png");'`
public static class CliffLadderCheck
{
    public static string Run(string islandName = null, string capturePath = null)
    {
        var sb = new StringBuilder("CliffLadderCheck.Run\n");
        if (!Application.isPlaying) return sb.Append("FAIL: needs play mode").ToString();

        Outpost camp = null;
        foreach (var o in Outpost.All)
        {
            if (o == null || !o.HasCamp || o.Ledger == null || !o.Sited) continue;
            if (string.IsNullOrEmpty(islandName) ? o.Watched
                : (o.Island != null && o.Island.name.Contains(islandName)) || o.name.Contains(islandName))
            { camp = o; break; }
        }
        if (camp == null) return sb.Append($"FAIL: no camp with a fire{(string.IsNullOrEmpty(islandName) ? " (watched)" : " on '" + islandName + "'")}").ToString();
        var map = CampPath.For(camp);
        if (map == null || !map.OnMap(camp.CampCentre)) return sb.Append("FAIL: camp has no path map").ToString();
        Vector3 fire = camp.CampCentre;
        fire.y = camp.GroundAt(fire);
        sb.AppendLine($"camp: {camp.name} fire {fire:F0}, ladders standing {camp.Ladders.Count}, links {map.LinkCount}");

        // --- 1. a cliff --------------------------------------------------------
        if (!FindCliff(camp, map, out Vector3 foot, out Vector3 top, out bool topCutOff, sb))
            return sb.Append("FAIL: no cliff (6-15 m rise within 8 m) near the camp that a ladder may stand on").ToString();
        Vector3 run = top - foot; run.y = 0f; run.Normalize();
        Vector3 target = top + run * 4f;
        target.y = camp.GroundAt(target);
        if (!Walkability.Standable(camp, target.x, target.z, Walkability.Feet.Man)) { target = top; }
        sb.AppendLine($"cliff: foot {foot:F1} top {top:F1} rise {top.y - foot.y:F1} m run {LadderLayout.Flat(foot, top):F1} m, "
            + $"top cut off before: {topCutOff}; plateau target {target:F1}");

        // --- 2. before -----------------------------------------------------------
        var route = new List<Vector3>();
        bool reachBefore = map.Reachable(target);
        bool routeBefore = map.Route(fire, target, CampPath.Walker.Hand, route);
        float lenBefore = routeBefore ? CampPath.RouteMetres(fire, route) : -1f;
        sb.AppendLine($"before: hand Reachable {reachBefore}, Route {routeBefore} ({lenBefore:F0} m, {route.Count} corners)");

        // --- 3. site through the siting mode, force-complete ----------------------
        int rowsBefore = camp.Ledger.sites.Count;
        CampSiting.Begin(camp, BuildPlans.Ladder, camp.transform);
        if (!CampSiting.Placing || !LadderSiting.Active)
            return sb.Append("FAIL: CampSiting did not start the ladder tool").ToString();
        LadderSiting.SetEnds(foot, top);
        sb.AppendLine($"siting: valid {CampSiting.CanConfirm}, refusal \"{LadderSiting.Refusal}\", price \"{LadderSiting.PriceLine}\"");
        CampSiting.Confirm();
        if (CampSiting.Placing)
        {
            string why = LadderSiting.Refusal;
            CampSiting.End();
            return sb.Append($"FAIL: ✓ refused: {why}").ToString();
        }
        PendingBuild row = null;
        foreach (var r in camp.Ledger.sites)
            if (Outpost.IsLadderRow(r) && LadderLayout.Flat(r.postA, foot) < 0.3f) row = r;
        sb.AppendLine($"row: {(row != null ? $"queued at the foot {row.At:F1}, {row.needed} timber, labour {row.LabourNeeded:F2} hand-days" : "MISSING")} (sites {rowsBefore}->{camp.Ledger.sites.Count})");
        if (row == null) return sb.Append("FAIL: no ladder row queued").ToString();
        Pay(row);
        camp.CatchUp();
        Ladder ladder = null;
        foreach (var l in camp.Ladders)
            if (l != null && LadderLayout.Flat(l.Foot, foot) < 0.3f) ladder = l;
        if (ladder == null) return sb.Append("FAIL: row paid but no chain stands").ToString();
        bool inBooks = camp.Ledger.builtLadders != null && camp.Ledger.builtLadders.Contains(ladder.Row);
        sb.AppendLine($"chain: {ladder.Flights} flights, {ladder.Landings} landings, rise {ladder.Rise:F1} m, "
            + $"climb {ladder.ClimbSeconds:F1} s, in ledger.builtLadders {inBooks}");

        // --- 4. after ------------------------------------------------------------
        bool link = map.LinkBetween(ladder.Foot, ladder.Top);
        sb.AppendLine($"link: foot->top {link}, links on map {map.LinkCount}");

        bool reachAfter = map.Reachable(target);
        bool routeAfter = map.Route(fire, target, CampPath.Walker.Hand, route);
        float lenAfter = routeAfter ? CampPath.RouteMetres(fire, route) : -1f;
        bool viaLadder = routeAfter && UsesLadder(route, ladder);
        sb.AppendLine($"after: hand Reachable {reachAfter}, Route {routeAfter} ({lenAfter:F0} m, {route.Count} corners), "
            + $"route keeps foot+top as corners {viaLadder}");

        // A raider from below the cliff, well out from the foot.
        Vector3 below = ladder.Foot - run * 6f;
        below.y = camp.GroundAt(below);
        bool raiderHas = map.HasRoute(below, target, CampPath.Walker.Raider);
        bool raiderRoute = map.Route(below, target, CampPath.Walker.Raider, route);
        bool raiderVia = raiderRoute && UsesLadder(route, ladder);
        sb.AppendLine($"raider: HasRoute {raiderHas}, Route {raiderRoute}, over the ladder {raiderVia}");

        // A goat never asks CampPath (Animal.Step: field + walls + Walkability,
        // Feet.Goat). The straight line up the chain's face, in 1 m steps:
        int refused = 0, steps = 0;
        Vector3 p = ladder.Foot;
        int n = Mathf.CeilToInt(LadderLayout.Flat(ladder.Foot, ladder.Top));
        for (int i = 1; i <= n; i++)
        {
            Vector3 q = Vector3.Lerp(ladder.Foot, ladder.Top, i / (float)n);
            q.y = camp.GroundAt(q);
            steps++;
            if (!Walkability.MayStep(camp, p, q, Walkability.Feet.Goat)) refused++;
            p = q;
        }
        sb.AppendLine($"goat: cannot use the chain (animals step on their own field, Animal.Step, and never "
            + $"route on CampPath or its links); the bare face straight up refuses {refused}/{steps} goat steps");

        // A simulated climb on a throwaway body.
        var body = new GameObject("CliffLadderCheck_Body").transform;
        body.position = ladder.Foot;
        var climb = new LadderClimb();
        bool began = climb.TryBegin(camp, body, ladder.Foot, ladder.Top, 0.05f);
        float t = 0f, maxY = body.position.y;
        while (began && climb.Active && t < 120f)
        {
            climb.Tick(camp, body, 0.05f);
            t += 0.05f;
            maxY = Mathf.Max(maxY, body.position.y);
        }
        float endErr = LadderLayout.Flat(body.position, ladder.Top);
        Object.Destroy(body.gameObject);
        bool climbed = began && !climb.Active && endErr < 0.5f;
        sb.AppendLine($"climb: began {began}, took {t:F1} s (shape says {ladder.ClimbSeconds:F1}), ends {endErr:F2} m from the top, peak y {maxY:F1}");

        bool pass = link && reachAfter && routeAfter && viaLadder && raiderHas && climbed && inBooks
                    && (topCutOff ? !reachBefore : lenAfter < lenBefore || !routeBefore);
        sb.AppendLine(pass ? "PASS" : "FAIL (see lines above)");

        if (!string.IsNullOrEmpty(capturePath))
        {
            var cam = Object.FindFirstObjectByType<SeaSick.CameraRig.IslandCam>();
            if (cam != null) cam.LookAt(Vector3.Lerp(ladder.Foot, ladder.Top, 0.5f), 25f);
            ScreenCapture.CaptureScreenshot(capturePath);
            sb.AppendLine($"capture -> {capturePath} (end of frame)");
        }
        return sb.ToString();
    }

    /// Scan the fire's ground out to 70 m for a foot with a top 2-8 m away
    /// and 6-15 m up that the camp would accept. A cut-off top wins.
    static bool FindCliff(Outpost camp, CampPath map, out Vector3 foot, out Vector3 top, out bool cutOff,
        StringBuilder sb)
    {
        foot = top = default;
        cutOff = false;
        Vector3 c = camp.CampCentre;
        bool haveAny = false;
        Vector3 anyFoot = default, anyTop = default;
        int tried = 0;
        for (float r = 8f; r <= 70f; r += 2f)
        {
            int around = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / 2f));
            for (int k = 0; k < around; k++)
            {
                float a = k * Mathf.PI * 2f / around;
                Vector3 f = new Vector3(c.x + Mathf.Sin(a) * r, 0f, c.z + Mathf.Cos(a) * r);
                f.y = camp.GroundAt(f);
                if (f.y <= CampPath.SeaLevelY + 0.3f) continue;
                if (!Walkability.Standable(camp, f.x, f.z, Walkability.Feet.Man)) continue;
                if (!map.Reachable(f)) continue;
                for (int d = 0; d < 16; d++)
                {
                    float b = d * Mathf.PI / 8f;
                    Vector3 dir = new Vector3(Mathf.Sin(b), 0f, Mathf.Cos(b));
                    for (float run = 3f; run <= 8f; run += 1.5f)
                    {
                        Vector3 t = f + dir * run;
                        t.y = camp.GroundAt(t);
                        float rise = t.y - f.y;
                        if (rise < 6f || rise > 15f) continue;
                        if (!Walkability.Standable(camp, t.x, t.z, Walkability.Feet.Man)) continue;
                        bool cut = !map.Reachable(t);
                        if (!cut && haveAny) continue;
                        if (++tried > 400) break;
                        Vector3 ff = f, tt = t;
                        if (!camp.CanPlaceLadder(ref ff, ref tt, out _)) continue;
                        if (cut)
                        {
                            foot = ff; top = tt; cutOff = true;
                            sb.AppendLine($"search: {tried} candidates asked, cut-off top found {r:F0} m from the fire");
                            return true;
                        }
                        if (!haveAny) { haveAny = true; anyFoot = ff; anyTop = tt; }
                    }
                }
            }
        }
        sb.AppendLine($"search: {tried} candidates asked, no cut-off top{(haveAny ? "; using a shortcut cliff" : "")}");
        if (!haveAny) return false;
        foot = anyFoot; top = anyTop;
        return true;
    }

    /// Does the route keep corners by both ends of the chain?
    static bool UsesLadder(List<Vector3> route, Ladder l)
    {
        bool nearFoot = false, nearTop = false;
        foreach (var c in route)
        {
            if (LadderLayout.Flat(c, l.Foot) < 3f && Mathf.Abs(c.y - l.Foot.y) < 1.6f) nearFoot = true;
            if (LadderLayout.Flat(c, l.Top) < 3f && Mathf.Abs(c.y - l.Top.y) < 1.6f) nearTop = true;
        }
        return nearFoot && nearTop;
    }

    /// Every phase of a row paid, as `WallTowerCheck.Pay` does.
    static void Pay(PendingBuild row)
    {
        row.done = row.needed; row.donePart = 0f;
        row.stoneDone = row.stoneNeeded; row.stoneDonePart = 0f;
        row.brickDone = row.brickNeeded; row.brickDonePart = 0f;
        row.clearDone = row.ClearTotal;
        row.built = row.LabourNeeded;
    }
}
