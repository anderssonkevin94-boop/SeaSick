using System.Text;
using SeaSick.World;
using UnityEngine;

/// **Warmth check (2026-09-27): who is warm, who is cold.**
///
/// Kevin's "1" of "1 and 4" -- GDD's Islands item 12 shipped the walk
/// label ("4") and left "an aura or morale incentive for building close"
/// as his call, separately; this is that call, and its own small check.
///
/// EDIT mode, no scene, no play: a bare `OutpostLedger` (no `Outpost`, no
/// MonoBehaviour at all -- the same "plain C#" shape `StationStockSelfTest`
/// already uses), the fire keyed to the origin, two Huts -- one 20 m out
/// (inside `OutpostLedger.WarmHutRadius`, warm) and one 50 m out (past it,
/// cold) -- and 8 hands. Reports each hand's name and whether
/// `OutpostLedger.IsHandWarm` calls it warm, plus the warm bed count
/// against what a single warm Hut (`BuildPlan.houses`) should give.
///
/// `unity cmd eval --json --code 'return WarmthCheck.Run();'`
///
/// **Not run as part of this change** -- see the task that added it.
public static class WarmthCheck
{
    public static string Run()
    {
        var sb = new StringBuilder("WarmthCheck.Run\n");

        var l = new OutpostLedger();
        l.SetKey(Vector3.zero); // the fire, at the origin

        // One Hut well inside WarmHutRadius (30 m), one well past it.
        l.built.Add(BuildPlans.Hut.id);
        l.raised.Add(new BuiltBuilding { planId = BuildPlans.Hut.id, x = 20f, z = 0f, level = 1 });
        l.built.Add(BuildPlans.Hut.id);
        l.raised.Add(new BuiltBuilding { planId = BuildPlans.Hut.id, x = 50f, z = 0f, level = 1 });

        for (int i = 0; i < 8; i++)
            l.hands.Add(new OutpostHand { name = "Hand " + i });

        int warmBeds = l.WarmBedCapacity;
        sb.AppendLine($"warm bed capacity: {warmBeds} (one warm Hut of two, houses={BuildPlans.Hut.houses})");

        int warmCount = 0;
        for (int i = 0; i < l.hands.Count; i++)
        {
            bool warm = l.IsHandWarm(i);
            if (warm) warmCount++;
            sb.AppendLine($"  {l.hands[i].name}: {(warm ? "warm" : "cold hut")}");
        }

        // Deterministic fill: the first `warmBeds` hands (by list order) are
        // warm, no more and no fewer, and that count matches the one warm
        // Hut's own bed count -- nothing from the cold Hut leaks in.
        bool pass = warmBeds == BuildPlans.Hut.houses && warmCount == warmBeds;
        sb.AppendLine(pass ? "PASS" : "FAIL (see counts above)");
        return sb.ToString();
    }
}
