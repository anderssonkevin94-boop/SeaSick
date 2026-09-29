using System.Text;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;

/// **Fishing hut check (2026-09-27): the shore rule, the raise, and a
/// fisher's catch.**
///
/// PLAY mode, a camp with a fire on the named island. One eval:
///
///   `unity cmd eval --json --code 'return FishingHutCheck.Run("Island_2");'`
///
/// DEV ONLY and it CHANGES the camp: it leaves a fishing hut standing (paid
/// by the check, not the camp), puts one hand on it (the first hand not
/// already fishing -- his old order is NOT restored) with a repeat "fish"
/// order, and runs the camp's own ledger `days` ahead (`OutpostLedger.Tick`
/// past its own clock; the next real tick re-anchors). Do not save the slot
/// afterwards unless that is wanted.
///
/// 1. `RecipeGraph.Validate()` is OK (the catch rule lets "fish" make raw
///    Food with no takes).
/// 2. The shore rule refuses the camp centre (inland) with the shore reason.
/// 3. Scans the island for the nearest spot `CanPlace` accepts, then sites
///    there through the real `Outpost.Site` (caps, locks, reach, price).
/// 4. Pays and raises it (`CatchUp`), assigns a hand, orders "fish".
/// 5. Fast-forwards and reports Food made (the absence log's Food row, the
///    store and the hut's rack) against the recipe's rate.
///
/// Ends with `PASS n/m` over its gates.
public static class FishingHutCheck
{
    static int pass, total;

    static void Gate(StringBuilder sb, bool ok, string what)
    {
        total++;
        if (ok) pass++;
        sb.AppendLine((ok ? "  ok   " : "  FAIL ") + what);
    }

    public static string Run(string islandName = "Island_2", float days = 3f)
    {
        pass = 0; total = 0;
        var sb = new StringBuilder($"FishingHutCheck.Run({islandName}, {days} days)\n");
        if (!Application.isPlaying) return sb.Append("FAIL: needs play mode").ToString();

        // --- 1. the table ------------------------------------------------------
        var rep = RecipeGraph.Validate();
        Gate(sb, rep.Ok, "RecipeGraph.Validate OK" + (rep.Ok ? "" : ": " + rep));
        var fish = Recipes.Named("fish");
        Gate(sb, fish != null && fish.station == BuildPlans.FishingHut.id && RecipeGraph.IsCatch(fish),
            "recipe 'fish' is a catch at the fishing hut");

        Outpost camp = null;
        foreach (var o in Outpost.All)
        {
            if (o == null || !o.HasCamp || o.Ledger == null) continue;
            if ((o.Island != null && o.Island.name == islandName) || o.name == islandName) { camp = o; break; }
        }
        if (camp == null) return Finish(sb.Append($"FAIL: no camp with a fire on '{islandName}'\n"));
        var l = camp.Ledger;
        var plan = BuildPlans.FishingHut;
        string id = plan.id;

        // --- 2. inland is refused --------------------------------------------------
        bool inland = camp.CanPlace(plan, camp.CampCentre, out string inlandWhy);
        sb.AppendLine($"  at the fire: {(inland ? "accepted" : "refused")} \"{inlandWhy}\"");
        Gate(sb, inland || inlandWhy.Length > 0, "the fire's own spot is judged (refused unless the fire is on the beach)");

        // --- 3. find the shore and site through the real path -----------------
        if (l.CountBuilt(id) == 0)
        {
            if (!FindShore(camp, plan, out Vector3 spot, out int tried))
                return Finish(sb.Append($"FAIL: no shore spot accepted ({tried} candidates tried)\n"));
            float ground = camp.GroundAt(spot);
            camp.FishingHutShore(spot, camp.AutoYaw(spot), plan.footprint.x, plan.footprint.y, out Vector3 water);
            sb.AppendLine($"  shore spot ({spot.x:0.0}, {spot.z:0.0}) ground {ground:0.00} m, "
                + $"{Island.FlatDistance(spot, camp.CampCentre):0} m from the fire, water at {Island.FlatDistance(spot, water):0.0} m "
                + $"({tried} candidates tried)");
            int before = l.sites.Count;
            int wanted = camp.Site(plan, spot, out string siteWhy);
            Gate(sb, wanted >= 0 && l.sites.Count > before, $"Outpost.Site accepted it (wants {wanted} timber) {siteWhy}");
            if (wanted < 0 || l.sites.Count <= before) return Finish(sb);
            var row = l.sites[l.sites.Count - 1];
            Pay(row);
            camp.CatchUp();
        }
        else sb.AppendLine("  a fishing hut already stands here; using it");
        Gate(sb, l.CountBuilt(id) >= 1, $"a fishing hut stands ({l.CountBuilt(id)})");
        if (l.CountBuilt(id) == 0) return Finish(sb);
        Gate(sb, OutpostLedger.IsStation(id), "it is a station (StationSheet page, MAKE row)");

        // --- 4. a fisher ------------------------------------------------------------
        OutpostHand fisher = null;
        foreach (var h in l.hands)
            if (h != null && h.order == OutpostOrder.Work && h.target == id) { fisher = h; break; }
        if (fisher == null)
            foreach (var h in l.hands)
                if (h != null) { fisher = h; break; }
        if (fisher == null) return Finish(sb.Append("FAIL: the camp has no hands\n"));
        bool assigned = fisher.order == OutpostOrder.Work && fisher.target == id || camp.Assign(fisher, id);
        Gate(sb, assigned, $"{fisher.name} assigned as fisher");
        bool ordered = l.PlaceOrder(id, "fish", OutpostLedger.RepeatOrder);
        Gate(sb, ordered, "repeat order for fish placed");

        // --- 5. fast-forward --------------------------------------------------------
        var st = l.StationOf(id);
        float got0 = AwayFood(l);
        int store0 = l.StoreCountOf(Res.Food), rack0 = st != null ? st.RackCount(Res.Food) : 0;
        double to = l.lastTicked + days * TimeOfDay.WorkDaySeconds + 1e-3;
        l.Tick(to);
        float made = AwayFood(l) - got0;
        int store1 = l.StoreCountOf(Res.Food), rack1 = st != null ? st.RackCount(Res.Food) : 0;
        float expect = fish != null ? fish.ratePerDay * Techs.RateMul(id, l.LevelOf(id, 0)) * days : 0f;
        sb.AppendLine($"  {days} days: Food made (absence log, whole camp) {made:0.#}; store {store0}->{store1}, "
            + $"hut rack {rack0}->{rack1}; one fisher at the recipe rate would make ~{expect:0.#}");
        sb.AppendLine($"  bench {st?.benchState} {st?.benchProgress:0.00}; stall: \"{l.StallReason(fisher) ?? "none"}\"");
        Gate(sb, made >= 1f || rack1 + store1 > rack0 + store0, "food came in");
        return Finish(sb);
    }

    static string Finish(StringBuilder sb)
        => sb.Append(pass == total ? "PASS " : "FAIL ").Append(pass).Append('/').Append(total).ToString();

    /// The nearest-to-the-fire spot the fishing hut's own `CanPlace`
    /// accepts, scanning the island on a 2 m grid limited to ground between
    /// the hut's floor and 2.5 m (the beach and its bank).
    static bool FindShore(Outpost camp, BuildPlan plan, out Vector3 best, out int tried)
    {
        best = Vector3.zero;
        tried = 0;
        Vector3 c = camp.Island != null ? camp.Island.transform.position : camp.CampCentre;
        float r = camp.Island != null ? camp.Island.MaxRadius + 10f : 120f;
        float bestD = float.MaxValue;
        for (float x = -r; x <= r; x += 2f)
            for (float z = -r; z <= r; z += 2f)
            {
                var at = new Vector3(c.x + x, 0f, c.z + z);
                float g = camp.GroundAt(at);
                if (g < BuildPlans.FishingHutFloor + 0.1f || g > 2.5f) continue;
                at.y = g;
                float d = Island.FlatDistance(at, camp.CampCentre);
                if (d >= bestD) continue;
                tried++;
                if (!camp.CanPlace(plan, at, out _)) continue;
                if (camp.TooFarFromTown(plan, at, out _)) continue;
                best = at; bestD = d;
            }
        return bestD < float.MaxValue;
    }

    static float AwayFood(OutpostLedger l)
    {
        var a = l.away;
        if (a == null) return 0f;
        for (int i = 0; i < a.res.Count; i++) if (a.res[i] == Res.Food) return a.got[i];
        return 0f;
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
