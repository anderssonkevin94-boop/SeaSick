using System.Collections.Generic;
using SeaSick.World.Economy;

namespace SeaSick.World
{
    /// **Gate for "the Storehouse is merged into the store hut" (Kevin
    /// 2026-10-04).** Plain C#, no scene:
    ///
    ///   tools/selftest-outside-editor/run.sh SeaSick.World.StorehouseMergeSelfTest.Run
    ///   unity cmd eval --json --code 'return SeaSick.World.StorehouseMergeSelfTest.Run();'
    ///
    /// (a) store hut runner posts by level and by copy; (b) the perks come
    /// from the highest-level store hut, and the barrow / jog read them;
    /// (c) the tables: Storage L3, no Storehouse plan, step or cap; (d) the
    /// load repair takes a Storehouse down, refunds the old tables' amounts,
    /// puts its runners on free store hut posts else Idle (not the reserve),
    /// is idempotent and touches nothing else. Returns "PASS n/n" or the
    /// failures.
    public static class StorehouseMergeSelfTest
    {
        public static string Run()
        {
            var sb = new System.Text.StringBuilder();
            int pass = 0, total = 0;
            void Check(string name, bool ok, string detail = "")
            {
                total++;
                if (ok) { pass++; return; }
                sb.Append("FAIL ").Append(name).Append(detail.Length > 0 ? ": " + detail : "").Append('\n');
            }
            string S = OutpostLedger.StorageId;

            // ---- (a) posts per level and per copy ---------------------------
            for (int lv = 1; lv <= 3; lv++)
            {
                var l = Camp((S, lv));
                Check($"store hut L{lv}: {2 * lv} runner posts", l.StationCapacity(S, 0) == 2 * lv,
                      "got " + l.StationCapacity(S, 0));
                Check($"store hut L{lv}: RunnerSlots {2 * lv}", l.RunnerSlots() == 2 * lv, "got " + l.RunnerSlots());
            }
            var two = Camp((S, 1), (S, 3));
            Check("two huts L1 + L3: 2 + 6 posts", two.RunnerSlots() == 8, "got " + two.RunnerSlots());
            Check("two huts: each copy its own posts",
                  two.StationCapacity(S, 0) == 2 && two.StationCapacity(S, 1) == 6);
            Check("no store hut: no posts", Camp().RunnerSlots() == 0);
            Check("only the store hut is a runner post",
                  OutpostLedger.IsRunnerPost(S) && !OutpostLedger.IsRunnerPost(OutpostLedger.LegacyStorehouseId));

            // ---- (b) perks from the highest level ---------------------------
            Check("perks L1: none", OutpostLedger.PerksAt(1) == (0, 1f));
            Check("perks L2: +1 armful, x1.1", OutpostLedger.PerksAt(2) == (1, 1.1f));
            Check("perks L3: +2 armfuls, x1.2", OutpostLedger.PerksAt(3) == (2, 1.2f));
            Check("no store hut: no perk", Camp().RunnerPerks() == (0, 1f));
            Check("L1 + L3 huts: the L3 perk", two.RunnerPerks() == (2, 1.2f));
            Check("L3 + L1 huts (order): the L3 perk", Camp((S, 3), (S, 1)).RunnerPerks() == (2, 1.2f));
            Check("L2 + L2 huts: one L2 perk, not two", Camp((S, 2), (S, 2)).RunnerPerks() == (1, 1.1f));
            var r3 = Camp((S, 3));
            var runner = new OutpostHand { name = "Run", order = OutpostOrder.Work, target = S };
            r3.hands.Add(runner);
            Check("L3 camp: a runner's barrow is base +2 armfuls (logs 10)",
                  r3.CarryArmful(runner, Res.Timber) == Res.BarrowArmful(Res.Timber, 2)
                  && Res.BarrowArmful(Res.Timber, 2) == 10, "got " + r3.CarryArmful(runner, Res.Timber));
            Check("L1 camp: today's barrow (logs 6)",
                  Camp((S, 1)).CarryArmful(runner, Res.Timber) == 6);
            Check("L3 camp: jog x1.2", System.Math.Abs(r3.RunnerJogSpeed() - VillagerGaits.BarrowAt(1.2f)) < 1e-5f);
            Check("upgrade words L2", OutpostLedger.RunnerUpgradeWords(S, 2) == "+2 runner posts · barrow +1 armful · jog 10% faster",
                  OutpostLedger.RunnerUpgradeWords(S, 2));
            Check("upgrade words L3", OutpostLedger.RunnerUpgradeWords(S, 3) == "+2 runner posts · barrow +2 armfuls · jog 20% faster",
                  OutpostLedger.RunnerUpgradeWords(S, 3));

            // ---- (c) the tables ---------------------------------------------
            Check("store hut tops out at level 3", Techs.MaxLevel(S) == 3);
            var l3 = Techs.Upgrade(S, 3);
            Check("store hut L3: 14 brick + 8 fine boards at fire II",
                  l3 != null && l3.campfireLevel == 2 && Has(l3.baseCost, Res.Brick, 14) && Has(l3.baseCost, Res.FineBoards, 8)
                  && l3.baseCost.Length == 2);
            var l2 = Techs.Upgrade(S, 2);
            Check("store hut L2 keeps its price (6 brick + 2 fine boards)",
                  l2 != null && Has(l2.baseCost, Res.Brick, 6) && Has(l2.baseCost, Res.FineBoards, 2));
            Check("no Storehouse upgrade steps", Techs.Upgrade("Storehouse", 2) == null && Techs.Upgrade("Storehouse", 3) == null);
            bool capped = false;
            foreach (var c in Techs.Caps) if (c.planId == "Storehouse") capped = true;
            Check("no Storehouse cap row", !capped);
            Check("no Storehouse plan", string.IsNullOrEmpty(BuildPlans.Named("Storehouse").id));
            bool listed = false;
            foreach (var p in BuildPlans.AtACamp) if (p.id == "Storehouse") listed = true;
            Check("not on the camp build list", !listed);

            // ---- (d) the load repair ----------------------------------------
            // Kevin's shape: a fire, a store hut L1 with one runner, a hut, a
            // wall, a Storehouse L2 behind it with three runners -- one
            // mid-haul with a picked load.
            var m = Camp((BuildPlans.Campfire.id, 1), (S, 1), (BuildPlans.Hut.id, 2),
                         (OutpostLedger.LegacyStorehouseId, 2));
            m.builtWalls.Add(new BuiltWall { ax = 1, az = 2, bx = 9, bz = 2, hp = 50, maxHp = 50, level = 1 });
            m.Store(Res.Timber, true).whole = 5;
            m.Store(Res.Fish, true).whole = 7;
            var ann = new OutpostHand { name = "Ann", order = OutpostOrder.Work, target = S };
            var bo = new OutpostHand { name = "Bo", order = OutpostOrder.Work, target = "Storehouse", workPin = 1 };
            var cy = new OutpostHand { name = "Cy", order = OutpostOrder.Work, target = "Storehouse" };
            var di = new OutpostHand { name = "Di", order = OutpostOrder.Work, target = "Storehouse",
                                       haulRes = Res.Boards, haulCount = 6, haulPicked = true, haulTo = HaulPlace.Store };
            var ed = new OutpostHand { name = "Ed", order = OutpostOrder.Gather, target = Res.Timber };
            m.hands.AddRange(new[] { ann, bo, cy, di, ed });
            m.postsLost.Add(new PostLost { name = "Fy", planId = "Storehouse" });
            var before = Snapshot(m);

            string log = m.MergeStorehouses();
            Check("repair logs one line", log != null && log.Contains("Storehouse merged"), log ?? "null");
            Check("Storehouse gone from built", !m.built.Contains("Storehouse"));
            Check("Storehouse gone from raised", !m.raised.Exists(r => r.planId == "Storehouse"));
            Check("refund timber 5 + 25", m.StoreCountOf(Res.Timber) == 30, "got " + m.StoreCountOf(Res.Timber));
            Check("refund stone 4", m.StoreCountOf(Res.Stone) == 4, "got " + m.StoreCountOf(Res.Stone));
            Check("refund brick 8 (L2)", m.StoreCountOf(Res.Brick) == 8, "got " + m.StoreCountOf(Res.Brick));
            Check("refund fine boards 4 (L2)", m.StoreCountOf(Res.FineBoards) == 4, "got " + m.StoreCountOf(Res.FineBoards));
            Check("Bo onto the store hut's free post, still a runner",
                  bo.order == OutpostOrder.Work && bo.target == S && OutpostLedger.IsRunner(bo) && bo.workPin == 1);
            Check("store hut L1 full at 2: Ann + Bo", m.WorkersAt(S, 0) == 2, "got " + m.WorkersAt(S, 0));
            Check("Ann untouched", ann.order == OutpostOrder.Work && ann.target == S && m.OrdinalOfHand(ann) == 0);
            Check("Cy no post: Idle, not the reserve",
                  cy.order == OutpostOrder.Idle && cy.target == "" && !cy.playerIdle && cy.workPin == 0);
            Check("Di no post: Idle, his picked boards in the store",
                  di.order == OutpostOrder.Idle && !di.Hauling && m.StoreCountOf(Res.Boards) == 6,
                  "boards " + m.StoreCountOf(Res.Boards));
            Check("Ed (a gatherer) untouched", ed.order == OutpostOrder.Gather && ed.target == Res.Timber);
            Check("no post-lost note names a Storehouse", !m.postsLost.Exists(p => p.planId == "Storehouse"));
            Check("everything else exactly as it was", Snapshot(m) == before, "\n" + before + "\nvs\n" + Snapshot(m));
            Check("fish untouched", m.StoreCountOf(Res.Fish) == 7);

            var after = Snapshot(m) + Hands(m) + Stores(m);
            Check("second run: nothing to do", m.MergeStorehouses() == null);
            Check("second run: idempotent", Snapshot(m) + Hands(m) + Stores(m) == after);

            // Two copies, one an old save's level (row 0 + the plan-wide row),
            // and a blueprint with goods on it.
            var k = Camp((S, 2), (OutpostLedger.LegacyStorehouseId, 3), (OutpostLedger.LegacyStorehouseId, 0));
            k.levels.Add(new OutpostLedger.PlanLevel { planId = "Storehouse", level = 2 });
            k.sites.Add(new PendingBuild { planId = "Storehouse", needed = 25, done = 7, stoneNeeded = 4, stoneDone = 1 });
            k.sites.Add(new PendingBuild { planId = BuildPlans.Hut.id, needed = 10, done = 3 });
            var builder = new OutpostHand { name = "Gus", order = OutpostOrder.Build };
            k.hands.Add(builder);
            k.MergeStorehouses();
            // L3 copy 1: 25 + 4 + 8/4 + 14/8; legacy L2 copy 2 (125 %): 32 + 5 + 8/4; site: 7 + 1.
            Check("two copies + site: timber 25 + 32 + 7", k.StoreCountOf(Res.Timber) == 64, "got " + k.StoreCountOf(Res.Timber));
            Check("two copies + site: stone 4 + 5 + 1", k.StoreCountOf(Res.Stone) == 10, "got " + k.StoreCountOf(Res.Stone));
            Check("two copies: brick 22 + 8", k.StoreCountOf(Res.Brick) == 30, "got " + k.StoreCountOf(Res.Brick));
            Check("two copies: fine boards 12 + 4", k.StoreCountOf(Res.FineBoards) == 16, "got " + k.StoreCountOf(Res.FineBoards));
            Check("the plan-wide Storehouse level row goes", !k.levels.Exists(x => x.planId == "Storehouse"));
            Check("the Storehouse blueprint goes, the hut's stays",
                  k.sites.Count == 1 && k.sites[0].planId == BuildPlans.Hut.id && k.sites[0].done == 3);
            Check("a builder with a hut still to build keeps building", builder.order == OutpostOrder.Build);
            Check("the store hut keeps its level", k.LevelOf(S, 0) == 2);

            Check("a camp with no Storehouse: no-op", Camp((S, 1)).MergeStorehouses() == null);

            string head = $"PASS {pass}/{total}";
            return pass == total ? head : $"FAIL {total - pass} of {total}\n{sb}";
        }

        /// A bare ledger with these buildings standing, in order, each at its
        /// own spot (level 0 = an old save's row).
        static OutpostLedger Camp(params (string planId, int level)[] bs)
        {
            var l = new OutpostLedger();
            int i = 0;
            foreach (var (id, lv) in bs)
            {
                l.built.Add(id);
                l.raised.Add(new BuiltBuilding { planId = id, x = 10f * i, z = 3f * i, yaw = 15f * i, level = lv });
                i++;
            }
            return l;
        }

        static bool Has(Ingredient[] cost, string res, int n)
        {
            if (cost == null) return false;
            foreach (var c in cost) if (c.res == res && c.n == n) return true;
            return false;
        }

        /// Everything the repair must NOT touch: the other buildings' rows,
        /// the walls, the queue, the ground stocks, the non-refund stores.
        static string Snapshot(OutpostLedger l)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var b in l.built) if (b != "Storehouse") sb.Append(b).Append(';');
            sb.Append('|');
            foreach (var r in l.raised)
                if (r.planId != "Storehouse") sb.Append($"{r.planId}@{r.x},{r.z},{r.yaw},{r.length},L{r.level};");
            sb.Append('|');
            foreach (var w in l.builtWalls) sb.Append($"{w.ax},{w.az},{w.bx},{w.bz},{w.hp},{w.level};");
            sb.Append('|');
            foreach (var p in l.sites) if (p.planId != "Storehouse") sb.Append($"{p.planId}@{p.x},{p.z}:{p.done};");
            sb.Append('|');
            foreach (var s in l.stocks) sb.Append($"{s.resource}:{s.standing};");
            sb.Append('|').Append(l.Store(Res.Fish)?.whole ?? 0);
            return sb.ToString();
        }

        static string Hands(OutpostLedger l)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var h in l.hands) sb.Append($"{h.name}:{h.order}:{h.target}:{h.workPin}:{h.haulCount};");
            return sb.ToString();
        }

        static string Stores(OutpostLedger l)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in l.stores) sb.Append($"{s.resource}:{s.whole};");
            return sb.ToString();
        }
    }
}
