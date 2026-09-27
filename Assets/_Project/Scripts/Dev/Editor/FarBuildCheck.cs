using System.Text;
using SeaSick.UI;
using SeaSick.World;
using UnityEngine;

/// **Far build check (2026-09-27): a hut sited well past the old 40 m
/// ring.**
///
/// Kevin: *"I want to do away with the 40 m building radius for the
/// campfire... The whole island should be built if you want it to."* This
/// drives the REAL siting path -- `CampSiting.Begin`/`MoveTo`/`Confirm`, the
/// same calls a tap makes -- at a spot at least `MinDistance` from the fire,
/// and checks the three things that decision promised:
///
/// 1. the spot is VALID (`CanPlace` + the one rule kept, reachability --
///    `Outpost.TooFarFromTown` no longer refuses on distance at all);
/// 2. the walk LABEL is showing (`CampSiting.WalkLine`: "N m from stores ...
///    round trip"), which is the "reason to stay close" now that the ring
///    itself is gone;
/// 3. the hands can actually ROUTE there afterwards (`CampPath.HasRoute`),
///    which is the thing the label's promise would be a lie without.
///
/// Companion to `WallTowerCheck` (which covers the wall/tower exemption that
/// this decision made moot); this is the ordinary-building path.
///
/// PLAY mode, a watched camp whose island has `MinDistance` of open,
/// walkable ground somewhere within six 20 m rings of the fire (any real
/// island does). One `unity cmd eval` call:
///
///   `unity cmd eval --json --code 'return FarBuildCheck.Run();'`
///
/// **Not run as part of this change** -- see the task that added it.
public static class FarBuildCheck
{
    /// Metres from the fire the probe insists on. Three times the old
    /// `Outpost.TownRadius` (40), so a fix that only nudges a limit back
    /// a little would still be caught failing.
    public const float MinDistance = 120f;

    public static string Run()
    {
        var sb = new StringBuilder("FarBuildCheck.Run\n");
        if (!Application.isPlaying) return sb.Append("FAIL: needs play mode").ToString();

        Outpost camp = null;
        foreach (var o in Outpost.All)
            if (o != null && o.Watched && o.HasCamp && o.Ledger != null) { camp = o; break; }
        if (camp == null) return sb.Append("FAIL: no watched camp with a fire").ToString();

        Vector3 c = camp.CampCentre;
        var map = CampPath.For(camp);

        // --- find a real spot at least MinDistance out -----------------------
        //
        // An island is not a disc: walk outward in 20 m rings, 16 bearings
        // each, and take the first spot the ground map itself already calls
        // walkable and reachable from the fire -- the same answer `CanPlace`
        // is about to be asked for.
        Vector3 spot = default;
        bool found = false;
        float gotDist = 0f;
        for (int ring = 0; ring < 6 && !found; ring++)
        {
            float r = MinDistance + ring * 20f;
            for (int k = 0; k < 16; k++)
            {
                float t = k * Mathf.PI * 2f / 16f;
                Vector3 p = c + new Vector3(Mathf.Sin(t), 0f, Mathf.Cos(t)) * r;
                if (map != null && map.OnMap(p) && map.WalkableAt(p, CampPath.Walker.Hand)
                    && map.Reachable(p))
                {
                    spot = p; gotDist = r; found = true; break;
                }
            }
        }
        if (!found)
            return sb.Append($"FAIL: no walkable+reachable ground found >= {MinDistance:F0} m "
                + "out on 6 rings of 16 bearings each").ToString();
        sb.AppendLine($"spot: {gotDist:F0} m out at {spot:F0}, CampPath already calls it "
            + "walkable and reachable");

        // --- site a hut through the real path, same as a tap -----------------
        CampSiting.Begin(camp, BuildPlans.Hut, camp.transform);
        if (!CampSiting.Placing) return sb.Append("FAIL: CampSiting did not start").ToString();

        CampSiting.MoveTo(spot);
        float actualDist = Flat(CampSiting.GhostAt, c);
        sb.AppendLine($"ghost at {CampSiting.GhostAt:F0}, {actualDist:F0} m from the fire, "
            + $"valid {CampSiting.CanConfirm}, refusal \"{CampSiting.Refusal}\"");
        sb.AppendLine($"walk label: \"{CampSiting.WalkLine}\"");

        bool labelShowsWalk = !string.IsNullOrEmpty(CampSiting.WalkLine)
            && CampSiting.WalkLine.Contains("from stores")
            && CampSiting.WalkLine.Contains("round trip");

        if (!CampSiting.CanConfirm)
        {
            string why = CampSiting.Refusal;
            CampSiting.End();
            return sb.Append($"FAIL: refused {actualDist:F0} m out: {why}").ToString();
        }

        CampSiting.Confirm();
        if (CampSiting.Placing)
        {
            string why = CampSiting.Refusal;
            CampSiting.End();
            return sb.Append($"FAIL: the checkmark itself was refused: {why}").ToString();
        }

        // --- force-complete and report the hands can actually get there ------
        PendingBuild row = null;
        foreach (var r in camp.Ledger.sites)
            if (r != null && r.planId == BuildPlans.Hut.id && Flat(r.At, spot) < 0.5f) row = r;
        if (row != null) { Pay(row); camp.CatchUp(); }

        bool built = camp.CountOf(BuildPlans.Hut.id) > 0;
        sb.AppendLine($"hut: row {(row != null ? "queued" : "MISSING")}, built {(built ? "YES" : "NO")}");

        bool handRoute = map != null && map.HasRoute(c, spot, CampPath.Walker.Hand);
        sb.AppendLine($"hands can route there from the fire: {handRoute}");

        bool pass = actualDist >= MinDistance && labelShowsWalk && built && handRoute;
        sb.AppendLine(pass ? "PASS" : "FAIL (see lines above)");
        return sb.ToString();
    }

    /// Every phase of a row paid, as `Outpost.MakeCamp` / `WallTowerCheck` do.
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
