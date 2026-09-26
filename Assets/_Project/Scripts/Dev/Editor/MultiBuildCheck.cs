using System.Collections.Generic;
using System.Text;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;

/// **Multiple buildings check (2026-09-27): caps by fire level, the price of
/// a copy, and each building's own level.**
///
/// PLAY mode, a watched camp with a fire. One `unity cmd eval` call:
///
///   `unity cmd eval --json --code 'return MultiBuildCheck.Run();'`
///
/// DEV ONLY and it CHANGES the camp: it fakes the fire's level (restored at
/// the end), leaves two sawmills standing (paid by the check, not by the
/// camp) with the second at level 2, and re-stands the camp from a JSON copy
/// of its own ledger twice (`Outpost.Adopt`, what a load does). Do not save
/// the slot afterwards unless that is wanted.
///
/// 1. Caps: at fire I then II, queues shelters (and sawmills) through the
///    real `Outpost.Site` until refused, reports how many were allowed, the
///    refusal text ("a 3rd shelter needs campfire II") and the build list's
///    row (`BuildRowText`), then cancels what it queued.
/// 2. Fakes fire III, raises sawmills until two stand, upgrades ONLY the
///    second (`UpgradeAt`), reports each copy's level and bench rate.
/// 3. Save -> reload: `Adopt` of a JSON round trip; each sawmill keeps its
///    own level and tint.
/// 4. Old-save migration: the same JSON with every row's level zeroed and
///    the legacy plan-wide level at 2 -- both sawmills load at 2, nothing
///    downgraded. The per-building state is then restored.
/// 5. Prices of copies 1-3 of a shelter and a sawmill (100/125/150%).
///
/// Ends with `PASS n/m` over its gates.
public static class MultiBuildCheck
{
    static int pass, total;

    static void Gate(StringBuilder sb, bool ok, string what)
    {
        total++;
        if (ok) pass++;
        sb.AppendLine((ok ? "  ok   " : "  FAIL ") + what);
    }

    public static string Run()
    {
        pass = 0; total = 0;
        var sb = new StringBuilder("MultiBuildCheck.Run\n");
        if (!Application.isPlaying) return sb.Append("FAIL: needs play mode").ToString();

        Outpost camp = null;
        foreach (var o in Outpost.All)
            if (o != null && o.Watched && o.HasCamp && o.Ledger != null) { camp = o; break; }
        if (camp == null) return sb.Append("FAIL: no watched camp with a fire").ToString();

        int fireWas = camp.Ledger.campfireLevel;
        string saw = BuildPlans.Sawmill.id;

        // --- 1. caps at fire I and II --------------------------------------------
        foreach (int L in new[] { 1, 2 })
        {
            camp.Ledger.campfireLevel = L;
            sb.AppendLine($"fire {RecipeGraph.Roman(L)}:");
            CapProbe(sb, camp, BuildPlans.Hut, L);
            CapProbe(sb, camp, BuildPlans.Sawmill, L);
        }

        // --- 2. two sawmills at a faked fire III, upgrade only #2 ----------------
        camp.Ledger.campfireLevel = 3;
        sb.AppendLine("fire III (faked): raising sawmills to two");
        for (int guard = 0; guard < 4 && camp.Ledger.CountBuilt(saw) < 2; guard++)
        {
            var row = SiteSomewhere(camp, BuildPlans.Sawmill, out string why);
            if (row == null) { sb.AppendLine($"  could not site a sawmill: {why}"); break; }
            Pay(row);
            camp.CatchUp();
        }
        Gate(sb, camp.Ledger.CountBuilt(saw) == 2, $"two sawmills stand (have {camp.Ledger.CountBuilt(saw)})");
        if (camp.Ledger.CountBuilt(saw) < 2) return Finish(sb, camp, fireWas);

        var l = camp.Ledger;
        foreach (var c in Techs.Upgrade(saw, 2).cost)
            l.Store(c.res, true).whole += c.n;
        int second = l.RaisedIndexOf(saw, 1);
        bool up = l.UpgradeAt(second, saw);
        camp.Retint(BuildingAt(camp, second));
        Gate(sb, up, "UpgradeAt(sawmill #2) succeeded");
        ReportSaws(sb, camp, "after upgrade");
        Gate(sb, l.LevelOf(saw, 0) == 1 && l.LevelOf(saw, 1) == 2, "only #2 went up (1, 2)");

        // --- 3. save -> reload ------------------------------------------------------
        string json = JsonUtility.ToJson(camp.Ledger);
        camp.Adopt(JsonUtility.FromJson<OutpostLedger>(json), camp.CampCentre, true);
        ReportSaws(sb, camp, "after save->reload");
        l = camp.Ledger;
        Gate(sb, l.LevelOf(saw, 0) == 1 && l.LevelOf(saw, 1) == 2, "levels survive the reload (1, 2)");
        Gate(sb, camp.LevelOfBuilding(BuildingAt(camp, l.RaisedIndexOf(saw, 1))) == 2
                 && camp.LevelOfBuilding(BuildingAt(camp, l.RaisedIndexOf(saw, 0))) == 1,
            "each building on the ground reads its own level");

        // --- 4. an old save: per-plan level 2, rows with no level ----------------
        var legacy = JsonUtility.FromJson<OutpostLedger>(json);
        foreach (var r in legacy.raised) if (r != null) r.level = 0;
        legacy.levels = new List<OutpostLedger.PlanLevel>
            { new OutpostLedger.PlanLevel { planId = saw, level = 2 } };
        camp.Adopt(JsonUtility.FromJson<OutpostLedger>(JsonUtility.ToJson(legacy)), camp.CampCentre, true);
        ReportSaws(sb, camp, "old save (plan-wide level 2)");
        l = camp.Ledger;
        Gate(sb, l.LevelOf(saw, 0) == 2 && l.LevelOf(saw, 1) == 2, "old save: every sawmill at 2, none downgraded");
        bool migrated = true;
        foreach (var r in l.raised) if (r != null && r.level <= 0) migrated = false;
        Gate(sb, migrated, "Adopt wrote every row's own level");
        camp.Adopt(JsonUtility.FromJson<OutpostLedger>(json), camp.CampCentre, true);   // back to 1, 2

        // --- 5. prices ------------------------------------------------------------------
        foreach (var plan in new[] { BuildPlans.Hut, BuildPlans.Sawmill })
        {
            var line = new StringBuilder($"price {plan.label}:");
            bool ok = true;
            for (int n = 0; n < 3; n++)
            {
                var p = BuildPlans.PriceForCopy(plan, n);
                float mul = 1f + 0.25f * n;
                int wantT = Mathf.CeilToInt(plan.cost * mul - 1e-4f);
                int wantS = Mathf.CeilToInt(plan.stoneCost * mul - 1e-4f);
                ok &= p.cost == wantT && p.stoneCost == wantS;
                line.Append($"  copy {n + 1}: {p.cost} timber {p.stoneCost} stone");
            }
            sb.AppendLine(line.ToString());
            Gate(sb, ok, $"{plan.label} copies cost 100/125/150% (rounded up)");
        }

        return Finish(sb, camp, fireWas);
    }

    static string Finish(StringBuilder sb, Outpost camp, int fireWas)
    {
        if (camp.Ledger != null) camp.Ledger.campfireLevel = fireWas;
        sb.AppendLine($"fire restored to {Mathf.Max(1, fireWas)}");
        sb.Append(pass == total ? "PASS " : "FAIL ").Append(pass).Append('/').Append(total);
        return sb.ToString();
    }

    /// Queue `plan` until refused, report, cancel what was queued.
    static void CapProbe(StringBuilder sb, Outpost camp, BuildPlan plan, int fire)
    {
        var l = camp.Ledger;
        var queued = new List<PendingBuild>();
        string why = null;
        for (int guard = 0; guard < 8; guard++)
        {
            var row = SiteSomewhere(camp, plan, out why);
            if (row == null) break;
            queued.Add(row);
        }
        int held = l.CopiesHeld(plan.id);
        int cap = l.CopyLimit(plan.id);
        string rowText = l.BuildRowText(plan, out bool enabled);
        sb.AppendLine($"  {plan.label}: held {held} of {cap} (queued {queued.Count} here) -- refused: \"{why}\"");
        sb.AppendLine($"  build row: \"{rowText}\" enabled={enabled}");
        int expectCap = Techs.MaxCopies(plan.id, fire);
        // `held` may exceed the cap in a camp saved before caps (uncapped
        // shelters): siting still refuses, which is the gate.
        Gate(sb, cap == expectCap && held >= cap, $"{plan.label} capped at {expectCap} at fire {RecipeGraph.Roman(fire)}");
        int unlock = Techs.FireLevelForCopies(plan.id, held + 1);
        if (unlock > fire)
        {
            string want = $"a {OutpostLedger.Nth(held + 1)} {plan.label} needs campfire {RecipeGraph.Roman(unlock)}";
            Gate(sb, why == want, $"refusal names the unlock (\"{want}\")");
        }
        Gate(sb, !enabled, "build row is disabled at the cap");
        foreach (var r in queued) camp.CancelPending(r);
    }

    /// Site `plan` at the first spot round the fire that takes it. Stops at
    /// once on a refusal that is not about the ground (the cap, a lock).
    static PendingBuild SiteSomewhere(Outpost camp, BuildPlan plan, out string why)
    {
        why = "no spot";
        var l = camp.Ledger;
        if (!l.CanAddCopy(plan.id, out string capWhy)) { why = capWhy; return null; }
        for (float r = 9f; r <= 36f; r += 3f)
            for (int k = 0; k < 20; k++)
            {
                float t = k * Mathf.PI * 2f / 20f + r * 0.37f;
                Vector3 at = camp.CampCentre + new Vector3(Mathf.Sin(t), 0f, Mathf.Cos(t)) * r;
                int before = l.sites.Count;
                if (camp.Site(plan, at, out why) >= 0 && l.sites.Count > before)
                    return l.sites[l.sites.Count - 1];
            }
        return null;
    }

    static void ReportSaws(StringBuilder sb, Outpost camp, string when)
    {
        var l = camp.Ledger;
        string saw = BuildPlans.Sawmill.id;
        var line = new StringBuilder($"{when}:");
        for (int k = 0; k < l.CountBuilt(saw); k++)
        {
            int lv = l.LevelOf(saw, k);
            var st = l.StationOf(saw, k);
            var rec = st != null ? (st.BenchRecipe ?? l.RecipeAt(saw)) : l.RecipeAt(saw);
            float rate = rec != null ? rec.ratePerDay * Techs.RateMul(saw, lv) : 0f;
            var b = BuildingAt(camp, l.RaisedIndexOf(saw, k));
            line.Append($"  #{k + 1} level {lv} (ground {camp.LevelOfBuilding(b)}) {rate:0.##}/hand/day");
        }
        sb.AppendLine(line.ToString());
    }

    static Building BuildingAt(Outpost camp, int raisedIndex)
    {
        var built = camp.Built;
        return raisedIndex >= 0 && raisedIndex < built.Count ? built[raisedIndex] : null;
    }

    static void Pay(PendingBuild row)
    {
        row.done = row.needed; row.donePart = 0f;
        row.stoneDone = row.stoneNeeded; row.stoneDonePart = 0f;
        row.brickDone = row.brickNeeded; row.brickDonePart = 0f;
        row.clearDone = row.ClearTotal;
        row.built = row.LabourNeeded;
    }
}
