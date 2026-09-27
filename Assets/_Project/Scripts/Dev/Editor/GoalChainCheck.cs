using System.Text;
using SeaSick.World;
using SeaSick.World.Economy;

/// **Goal chain check (2026-09-27): the camp overview's "what am I waiting
/// on" tree, against a fake ledger shaped like Kevin's Island_2.**
///
/// EDIT mode, no scene: builds bare `OutpostLedger`s and asks
/// `GoalChain.NextCampfire` / `GoalChain.ForCost`. One call:
///
///   `unity cmd eval --json --code 'return GoalChainCheck.Run();'`
///
/// Returns "GoalChainCheck PASS (n gates)" or "FAIL" with every failed gate.
///
/// 1. Island_2 as Kevin had it (30 boards, 6 stone, 0 hide, 0 spear, a forge
///    standing with no smith, 12 game on the island): the fire II chain is
///    boards ✓, stone ✓, hide 0/4 blocking → hunting (needs a spear, no
///    hunter) → spear 0/1 → forge "built · no smith"; 2 of 3 ready; the
///    first step is "make a spear" at the forge; nothing deeper than 3.
/// 2. The same camp with a spear in the pile: hunting asks for a hunter and
///    that is the first step (assign a hand to game).
/// 3. Every line met: the goal can complete and the first step is Raise.
/// 4. Iron at fire II with a smith and no ore on the island: the chain ends
///    at "ore is on the far islands".
public static class GoalChainCheck
{
    static StringBuilder log;
    static int gates, fails;

    public static string Run()
    {
        log = new StringBuilder();
        gates = fails = 0;

        // --- 1. Island_2 ---
        var l = Island2();
        var c = GoalChain.NextCampfire(l);
        Dump("island_2", c);
        Gate(c.HasGoal && c.title == "Campfire level II", $"goal is fire II (got '{c.title}')");
        Gate(c.ready == 2 && c.total == 3, $"2 of 3 ready (got {c.ready} of {c.total})");
        Gate(!c.CanComplete, "cannot complete yet");
        int hide = Find(c, r => r.depth == 0 && r.res == Res.Hide);
        Gate(hide >= 0, "a top-level hide row");
        if (hide >= 0)
        {
            var h = c.rows[hide];
            Gate(h.have == 0 && h.need == 4 && h.qty == "0 / 4", $"hide 0 / 4 (got {h.qty})");
            Gate(h.state == GoalState.Blocked && h.Blocking, "hide is blocking");
            var hunt = At(c, hide + 1);
            Gate(hunt != null && hunt.depth == 1 && hunt.kind == GoalRowKind.Hunting, "hunting under hide");
            Gate(hunt != null && hunt.state == GoalState.Blocked, "hunting is blocked");
            Gate(hunt != null && hunt.how.Contains("12 game") && hunt.how.Contains("needs a spear"),
                $"hunting says 12 game and needs a spear (got '{hunt?.how}')");
            Gate(hunt != null && hunt.qty == "no hunter", $"hunting says no hunter (got '{hunt?.qty}')");
            var spear = At(c, hide + 2);
            Gate(spear != null && spear.depth == 2 && spear.res == Res.Spear && spear.need == 1 && spear.have == 0,
                "spear 0 / 1 under hunting");
            Gate(spear != null && spear.state != GoalState.Ok, "spear is not met");
            Gate(spear != null && spear.how.StartsWith("forge"), $"spear is made at the forge (got '{spear?.how}')");
            var forge = At(c, hide + 3);
            Gate(forge != null && forge.depth == 3 && forge.kind == GoalRowKind.Station && forge.planId == "Blacksmith",
                "forge under spear");
            Gate(forge != null && forge.how.Contains("no smith") && forge.qty == "assign",
                $"forge: built, no smith, assign (got '{forge?.how}' / '{forge?.qty}')");
        }
        foreach (var r in c.rows)
            if (r.res == Res.Boards || r.res == Res.Stone)
                if (r.depth == 0) Gate(r.state == GoalState.Ok, $"{r.res} is one green row");
        Gate(Find(c, r => r.depth > 0 && (r.res == Res.Boards || r.res == Res.Stone)) < 0,
            "met lines collapse: nothing under boards or stone");
        Gate(Find(c, r => r.depth >= GoalChain.MaxDepth) < 0, "no row deeper than MaxDepth - 1");
        var s = c.firstStep;
        Gate(s != null && s.action == GoalAction.OpenStation && s.planId == "Blacksmith",
            $"first step opens the forge (got {s?.action} {s?.planId})");
        Gate(s != null && s.title == "make a spear", $"first step: make a spear (got '{s?.title}')");
        Gate(s != null && s.detail.Contains("needs a smith"), $"first step says needs a smith (got '{s?.detail}')");
        long key = c.Key;
        Gate(GoalChain.NextCampfire(l).Key == key, "deterministic: same books, same key");

        // --- 2. a spear in the pile ---
        l = Island2();
        l.Store(Res.Spear, true).whole = 1;
        c = GoalChain.NextCampfire(l);
        Dump("with a spear", c);
        int hunt2 = Find(c, r => r.kind == GoalRowKind.Hunting);
        Gate(hunt2 >= 0 && c.rows[hunt2].how.Contains("needs a hunter"), "hunting now needs a hunter");
        Gate(Find(c, r => r.res == Res.Spear) < 0, "no spear row once one is held");
        Gate(c.firstStep != null && c.firstStep.action == GoalAction.AssignHand && c.firstStep.res == Res.Game,
            $"first step: post a hunter (got {c.firstStep?.action} {c.firstStep?.res})");

        // --- 3. everything met ---
        l = Island2();
        l.Store(Res.Hide, true).whole = 4;
        c = GoalChain.NextCampfire(l);
        Gate(c.ready == 3 && c.CanComplete, "3 of 3 ready and payable");
        Gate(c.firstStep != null && c.firstStep.action == GoalAction.Complete, "first step: raise");
        Gate(c.rows.Count == 3, $"three green rows and nothing else (got {c.rows.Count})");

        // --- 4. ore is far away ---
        l = Island2();
        l.campfireLevel = 2;
        l.hands.Add(new OutpostHand { name = "Smith", order = OutpostOrder.Work, target = "Blacksmith" });
        c = GoalChain.ForCost(l, "Two bars", "for the test", Cost.Of(Cost.I(Res.Iron, 2)));
        Dump("iron", c);
        Gate(Find(c, r => r.res == Res.Ore && r.depth > 0) >= 0, "an ore row under the forge");
        Gate(Find(c, r => r.how != null && r.how.Contains("far islands")) >= 0, "ore is on the far islands");

        string head = fails == 0 ? $"GoalChainCheck PASS ({gates} gates)" : $"GoalChainCheck FAIL ({fails} of {gates})";
        return head + "\n" + log;
    }

    /// Kevin's Island_2 on 2026-09-27: 30 boards, 6 stone, no hide, no
    /// spear, a forge standing with nobody at it, 12 animals, no stone left.
    static OutpostLedger Island2()
    {
        var l = new OutpostLedger();
        l.Store(Res.Boards, true).whole = 30;
        l.Store(Res.Stone, true).whole = 6;
        l.Store(Res.Timber, true).whole = 9;
        var game = l.Stock(Res.Game, true);
        game.standing = 12f; game.standingMax = 12f;
        var stone = l.Stock(Res.Stone, true);
        stone.standing = 0f; stone.standingMax = 20f;
        var wood = l.Stock(Res.Timber, true);
        wood.standing = 200f; wood.standingMax = 200f;
        l.built.Add("Campfire");
        l.built.Add("Sawmill");
        l.built.Add("Blacksmith");
        l.hands.Add(new OutpostHand { name = "Hollis", order = OutpostOrder.Work, target = "Sawmill" });
        l.hands.Add(new OutpostHand { name = "Finch", order = OutpostOrder.Build });
        return l;
    }

    static GoalRow At(GoalChain c, int i) => i >= 0 && i < c.rows.Count ? c.rows[i] : null;

    static int Find(GoalChain c, System.Predicate<GoalRow> p)
    {
        for (int i = 0; i < c.rows.Count; i++) if (p(c.rows[i])) return i;
        return -1;
    }

    static void Gate(bool ok, string what)
    {
        gates++;
        if (!ok) { fails++; log.Append("  FAIL ").AppendLine(what); }
    }

    static void Dump(string name, GoalChain c)
    {
        log.Append("[").Append(name).Append("] ").Append(c.title).Append(" · ")
           .Append(c.ready).Append(" of ").Append(c.total).AppendLine(" ready");
        foreach (var r in c.rows)
            log.Append("   ").Append(new string(' ', r.depth * 2))
               .Append(r.state == GoalState.Ok ? "✓ " : r.state == GoalState.Blocked ? "! " : "… ")
               .Append(r.name).Append(" — ").Append(r.how).Append("  [").Append(r.qty).AppendLine("]");
        if (c.firstStep != null)
            log.Append("   first step: ").Append(c.firstStep.title).Append(" (").Append(c.firstStep.detail).AppendLine(")");
    }
}
