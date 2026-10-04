using System.Collections.Generic;
using System.Text;
using SeaSick.World.Economy;
using UnityEngine;

namespace SeaSick.World
{
    /// **Plain-C# gate for the stall/locked wording (2026-10-04, Kevin:
    /// 'it's set to fine boards but I don't have a saw blade').** No scene:
    /// a fake camp (`Camp`) for the wording itself, one bare ledger for the
    /// real path (`OutpostLedger.StallReason` of a sawyer on fine boards).
    /// Run from the Unity CLI: `unity cmd eval --json --code
    /// 'SeaSick.World.MissingWordsSelfTest.Run()'` -- true when every gate
    /// passes, the report logged either way.
    ///
    /// Gates (given a recipe + stock -> the exact line):
    ///  - Kevin's case: no saw blade AND no boards -> both named, the tool
    ///    first, each with where to get it;
    ///  - the tool present, one board short -> "more boards";
    ///  - a lock before a tool before an input; a thing nobody can make
    ///    (maker not built, worked-out ground) before one that is just short;
    ///  - nothing missing -> null; only a gatherable standing -> named but
    ///    not blocking;
    ///  - the real ledger's reason for a sawyer on fine boards names both.
    public static class MissingWordsSelfTest
    {
        sealed class Camp : MissingWords.IView
        {
            public int fire = 2, level = 1;
            public readonly HashSet<string> tools = new HashSet<string>();
            public readonly Dictionary<string, int> have = new Dictionary<string, int>();
            public readonly HashSet<string> ground = new HashSet<string>();
            public readonly HashSet<string> built = new HashSet<string>();
            public int FireLevel => fire;
            public int StationLevel => level;
            public bool HasTool(string res) => tools.Contains(res);
            public int Have(string res) => have.TryGetValue(res, out int n) ? n : 0;
            public bool GatherLeft(string res) => ground.Contains(res);
            public bool Built(string planId) => built.Contains(planId);
        }

        public static bool Run()
        {
            var sb = new StringBuilder("MissingWordsSelfTest\n");
            int fails = 0;
            var fine = Recipes.Named("fine-boards");
            var brick = Recipes.Named("brick");
            var arrows = Recipes.Named("arrows");
            var stew = Recipes.Named("veg-stew");
            string saw = BuildPlans.Sawmill.id;

            // (1) Kevin's own case: no blade, no boards.
            var c = new Camp();
            c.built.Add(saw); c.built.Add(BuildPlans.Blacksmith.id);
            string line = MissingWords.Line(c, saw, fine, out bool blocking);
            Gate(sb, ref fails, "kevins-case-blade-and-boards",
                line == "fine boards need a saw blade (make one at the forge) and boards (switch to Boards here)" && blocking,
                $"'{line}'");

            // (2) blade in the pile, one of two boards: "more boards".
            c.tools.Add(Res.SawBlade); c.have[Res.Boards] = 1;
            line = MissingWords.Line(c, saw, fine, out blocking);
            Gate(sb, ref fails, "one-board-short-says-more",
                line == "fine boards need more boards (switch to Boards here)" && blocking, $"'{line}'");

            // (3) nothing missing.
            c.have[Res.Boards] = 2;
            line = MissingWords.Line(c, saw, fine, out blocking);
            Gate(sb, ref fails, "nothing-missing-is-null", line == null && !blocking, $"'{line}'");

            // (4) lock, then tool, then input; the forge not built reads "build".
            c = new Camp { fire = 1 };
            c.built.Add(saw);
            line = MissingWords.Line(c, saw, fine, out blocking);
            Gate(sb, ref fails, "lock-then-tool-then-input-forge-unbuilt",
                line == "fine boards need the fire at II (raise it at the campfire), a saw blade (build a forge to make one) and boards (switch to Boards here)",
                $"'{line}'");

            // (5) a thing nobody can supply before one that is just short.
            c = new Camp();
            c.built.Add(BuildPlans.Quarry.id); c.built.Add(BuildPlans.Blacksmith.id);
            line = MissingWords.Line(c, BuildPlans.Quarry.id, brick, out blocking);
            Gate(sb, ref fails, "tool-then-worked-out-stone",
                line == "brick needs tools (make some at the forge) and stone (none left on this island)" && blocking,
                $"'{line}'");

            // (6) only a gatherable standing: named, not a stall.
            c = new Camp();
            c.built.Add(BuildPlans.Fletcher.id); c.ground.Add(Res.Timber);
            line = MissingWords.Line(c, BuildPlans.Fletcher.id, arrows, out blocking);
            Gate(sb, ref fails, "gatherable-standing-is-named-not-blocking",
                line == "arrows need timber (gather on the island)" && !blocking, $"'{line}' blocking {blocking}");

            // (7) grown inputs: the farm decides the words and the rank.
            c = new Camp { fire = 2, level = 2 };
            c.built.Add(BuildPlans.Kitchen.id); c.built.Add(BuildPlans.Farm.id);
            c.have[Res.Carrot] = 1;
            line = MissingWords.Line(c, BuildPlans.Kitchen.id, stew, out blocking);
            Gate(sb, ref fails, "grown-inputs-name-the-farm",
                line == "vegetable stew needs a potato (grow it at the farm) and an onion (grow it at the farm)" && blocking,
                $"'{line}'");

            // (8) the real ledger: a sawyer on fine boards, no blade, no boards.
            {
                var l = new OutpostLedger { ceilingPer = 1000, stationsMigrated = true, campfireLevel = 2 };
                l.SetCentre(Vector3.zero);
                l.raised.Add(new BuiltBuilding { planId = saw, x = 15f });
                l.built.Add(saw);
                var hand = new OutpostHand { name = "Yara", order = OutpostOrder.Work, target = saw,
                    wHas = true, wx = 15f, wz = 0f };
                l.hands.Add(hand);
                l.Store(Res.Food, true).whole = 1000;
                l.lastTicked = 0.0;
                l.EnsureStations();
                var st = l.StationOf(saw);
                bool picked = l.SelectRecipe(st, 0, "fine-boards", out string refusal);
                string why = l.StallReason(hand) ?? "";
                Gate(sb, ref fails, "ledger-sawyer-names-blade-and-boards",
                    picked && why.Contains("a saw blade (") && why.Contains("and boards (switch to Boards here)")
                    && why.IndexOf("saw blade", System.StringComparison.Ordinal) < why.IndexOf("boards (", System.StringComparison.Ordinal),
                    $"select {picked} ({refusal ?? "ok"}); stall '{why}'");
            }

            // (9) Kevin's real save: a WORN blade (whole 0, part 0.90) is a blade.
            {
                var l = new OutpostLedger { ceilingPer = 1000, stationsMigrated = true, campfireLevel = 2 };
                l.SetCentre(Vector3.zero);
                l.raised.Add(new BuiltBuilding { planId = saw, x = 15f });
                l.built.Add(saw);
                var hand = new OutpostHand { name = "Yara", order = OutpostOrder.Work, target = saw,
                    wHas = true, wx = 15f, wz = 0f };
                l.hands.Add(hand);
                l.lastTicked = 0.0;
                l.EnsureStations();
                var blade = l.Store(Res.SawBlade, true); blade.whole = 0; blade.part = 0.9f;
                var st = l.StationOf(saw);
                l.SelectRecipe(st, 0, "fine-boards", out _);
                string why = l.StallReason(hand) ?? "";
                Gate(sb, ref fails, "worn-blade-counts-as-one-with-its-life",
                    l.ShownStoreCount(Res.SawBlade) == 1 && l.StoreCountText(Res.SawBlade) == "1 (90%)"
                    && l.LifeLeft(Res.SawBlade) == "90% left" && l.Holds(Res.SawBlade)
                    && !why.Contains("saw blade") && why.Contains("boards (switch to Boards here)"),
                    $"shown {l.ShownStoreCount(Res.SawBlade)} '{l.StoreCountText(Res.SawBlade)}' '{l.LifeLeft(Res.SawBlade)}'; stall '{why}'");
            }

            // (10) a forge with BOTH spots starved: worst line + "+1 more", both listed.
            {
                string forge = BuildPlans.Blacksmith.id;
                var l = new OutpostLedger { ceilingPer = 1000, stationsMigrated = true, campfireLevel = 2 };
                l.SetCentre(Vector3.zero);
                l.raised.Add(new BuiltBuilding { planId = forge, x = 15f });
                l.built.Add(forge);
                var hand = new OutpostHand { name = "Smith", order = OutpostOrder.Work, target = forge,
                    wHas = true, wx = 15f, wz = 0f };
                l.hands.Add(hand);
                l.lastTicked = 0.0;
                l.EnsureStations();
                var st = l.StationOf(forge);
                l.SelectRecipe(st, StationSpots.IndexOf(forge, "Smelter"), "iron", out _);
                l.SelectRecipe(st, StationSpots.IndexOf(forge, "Forge"), "spear", out _);
                var all = new List<string>();
                string why = l.StallReasonAll(hand, all) ?? "";
                Gate(sb, ref fails, "two-starved-spots-both-listed",
                    all.Count == 2 && why.EndsWith(" · +1 more") && all[0].Contains("iron needs") && all[1].Contains("spear needs"),
                    $"reason '{why}'; lines {all.Count}: '{(all.Count > 0 ? all[0] : "")}' / '{(all.Count > 1 ? all[1] : "")}'");
            }

            sb.AppendLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
            Debug.Log(sb.ToString());
            return fails == 0;
        }

        static void Gate(StringBuilder sb, ref int fails, string name, bool ok, string detail)
        {
            sb.AppendLine($"  {(ok ? "PASS" : "FAIL")} {name}: {detail}");
            if (!ok) fails++;
        }
    }
}
