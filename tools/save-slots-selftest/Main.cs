// HEADLESS ONLY: entry point for tools/save-slots-selftest.sh.
//
// Exercises SeaSick.Save.SaveSlotLogic -- the UnityEngine-free half of the
// save-slot system -- against synthetic in-memory "filesystems" (plain
// dictionaries), so every gate here runs with no Unity, no real disk I/O,
// and no JSON at all.
using System;
using System.Collections.Generic;
using System.Linq;
using SeaSick.Save;

public static class SaveSlotsSelfTestMain
{
    static int fails;

    public static int Main(string[] args)
    {
        GateListOrder();
        GateIsManualIsAuto();
        GateAutoRotationFillsEmptyFirst();
        GateAutoRotationRecyclesOldest();
        GateMigrationGate();
        GateMostRecentPicksNewest();
        GateMostRecentTieBreakIsSlotOrder();
        GateMostRecentNoneExist();

        Console.WriteLine(fails == 0 ? "ALL GREEN" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }

    static void Gate(string name, bool ok, string detail = "")
    {
        if (ok) { Console.WriteLine("PASS  " + name); return; }
        fails++;
        Console.WriteLine("FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : "  -- " + detail));
    }

    static void GateListOrder()
    {
        var order = SaveSlotLogic.ListOrder().ToList();
        Gate("list-order-is-m1-m5-then-a1-a3",
            order.SequenceEqual(new[] { "m1", "m2", "m3", "m4", "m5", "a1", "a2", "a3" }),
            string.Join(",", order));
    }

    static void GateIsManualIsAuto()
    {
        Gate("m3-is-manual-not-auto", SaveSlotLogic.IsManual("m3") && !SaveSlotLogic.IsAuto("m3"));
        Gate("a2-is-auto-not-manual", SaveSlotLogic.IsAuto("a2") && !SaveSlotLogic.IsManual("a2"));
        Gate("junk-id-is-neither", !SaveSlotLogic.IsKnownSlot("m6") && !SaveSlotLogic.IsKnownSlot("a4") && !SaveSlotLogic.IsKnownSlot(""));
    }

    static void GateAutoRotationFillsEmptyFirst()
    {
        var exists = new Dictionary<string, bool> { { "a1", true }, { "a2", false }, { "a3", true } };
        var when = new Dictionary<string, DateTime>
        {
            { "a1", new DateTime(2026, 1, 1) },
            { "a3", new DateTime(2026, 1, 2) },
        };
        string pick = SaveSlotLogic.PickAutoSlotToWrite(id => exists[id], id => when.TryGetValue(id, out var t) ? t : default);
        Gate("rotation-fills-the-empty-slot-before-recycling-anything", pick == "a2", pick);
    }

    static void GateAutoRotationRecyclesOldest()
    {
        var when = new Dictionary<string, DateTime>
        {
            { "a1", new DateTime(2026, 1, 3) },
            { "a2", new DateTime(2026, 1, 1) }, // oldest -- must win
            { "a3", new DateTime(2026, 1, 2) },
        };
        string pick = SaveSlotLogic.PickAutoSlotToWrite(_ => true, id => when[id]);
        Gate("rotation-recycles-the-oldest-slot-once-all-three-exist", pick == "a2", pick);

        // Same three timestamps, run twice: deterministic, not order-dependent.
        string pickAgain = SaveSlotLogic.PickAutoSlotToWrite(_ => true, id => when[id]);
        Gate("rotation-pick-is-deterministic", pick == pickAgain);
    }

    static void GateMigrationGate()
    {
        Gate("migrates-when-m1-empty-and-legacy-exists", SaveSlotLogic.ShouldMigrate(false, true));
        Gate("never-migrates-once-m1-has-anything", !SaveSlotLogic.ShouldMigrate(true, true));
        Gate("nothing-to-migrate-if-legacy-never-existed", !SaveSlotLogic.ShouldMigrate(false, false));
        Gate("migration-is-idempotent-on-a-second-look",
            SaveSlotLogic.ShouldMigrate(false, true) && !SaveSlotLogic.ShouldMigrate(true, true));
    }

    static void GateMostRecentPicksNewest()
    {
        var exists = new HashSet<string> { "m2", "a1", "m5" };
        var when = new Dictionary<string, DateTime>
        {
            { "m2", new DateTime(2026, 9, 20) },
            { "a1", new DateTime(2026, 9, 25) }, // newest -- must win, even though it's an autosave
            { "m5", new DateTime(2026, 9, 22) },
        };
        string best = SaveSlotLogic.PickMostRecent(SaveSlotLogic.ListOrder(), exists.Contains, id => when[id]);
        Gate("most-recent-wins-regardless-of-manual-vs-auto", best == "a1", best);
    }

    static void GateMostRecentTieBreakIsSlotOrder()
    {
        var exists = new HashSet<string> { "m4", "m1" };
        var sameTime = new DateTime(2026, 9, 25, 12, 0, 0);
        string best = SaveSlotLogic.PickMostRecent(SaveSlotLogic.ListOrder(), exists.Contains, _ => sameTime);
        // ListOrder visits m1 before m4; PickMostRecent only replaces on a
        // strictly later time, so the first one visited (m1) keeps the tie.
        Gate("a-tie-is-broken-by-slot-order", best == "m1", best);
    }

    static void GateMostRecentNoneExist()
    {
        string best = SaveSlotLogic.PickMostRecent(SaveSlotLogic.ListOrder(), _ => false, _ => default);
        Gate("no-slots-means-no-most-recent", best == null, best ?? "(null)");
    }
}
