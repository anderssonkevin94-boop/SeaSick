using System.Text;
using UnityEngine;

namespace SeaSick.World
{
    /// **Plain-C# gate for the station stock (2026-09-23).** No scene, no
    /// MonoBehaviour: builds bare ledgers with a quarry and ticks them.
    /// Run from the Unity CLI: `unity cmd eval --json --code
    /// 'SeaSick.World.StationStockSelfTest.Run()'` -- returns true when every
    /// gate passes and logs the report either way.
    ///
    /// (a) D2: ten 1-day ticks and one 10-day tick make the same bricks (±1).
    /// (b) No bay row ever holds more than `InputCap`, no rack more than
    ///     `OutputCap`, checked after every 0.1-day tick.
    /// (c) Conservation: stone put in = stone anywhere (store, bay, bench,
    ///     arms) + bricks anywhere, at every tick.
    /// (d) A Repeat order runs past any count until stopped; after the stop
    ///     at most the job already on the bench finishes.
    /// (e) A hand removed mid-haul (`RemoveHand`) puts its armful down.
    /// (f) Demolishing the first of two quarries spills IT, the survivor
    ///     keeps its own stock and becomes ordinal 0; the dead row is emptied
    ///     and `IsLive` says so.
    /// (g) `Take` draws store, racks, finished benches -- never a bay;
    ///     `SpendableOf` agrees, `CountOf` still shows the bay.
    /// (h) The store's ceiling holds while a gatherer and a hauler both
    ///     fill it, and every unit is accounted.
    public static class StationStockSelfTest
    {
        public static bool Run()
        {
            var sb = new StringBuilder("StationStockSelfTest\n");
            int fails = 0;

            // --- (a) D2 ------------------------------------------------------
            var a = Quarry(40, 1000, 0);
            var b = Quarry(40, 1000, 0);
            var lumpy = Quarry(40, 1000, 0);
            bool placed = a.PlaceOrder(BuildPlans.Quarry.id, "brick", 10)
                          & b.PlaceOrder(BuildPlans.Quarry.id, "brick", 10)
                          & lumpy.PlaceOrder(BuildPlans.Quarry.id, "brick", 10);
            Gate(sb, ref fails, "order-placed", placed, placed ? "count-10 brick orders" : "PlaceOrder refused");
            double nowA = a.lastTicked, nowB = b.lastTicked, nowL = lumpy.lastTicked;
            for (int i = 0; i < 10; i++) Advance(a, ref nowA, 1.0);
            Advance(b, ref nowB, 10.0);
            // Uneven ticks, a few smaller than one quantum.
            double[] steps = { 0.03, 0.31, 0.02, 2.4, 0.05, 1.19, 3.0, 0.37, 2.63 };
            foreach (var st in steps) Advance(lumpy, ref nowL, st);
            int bricksA = Bricks(a), bricksB = Bricks(b), bricksL = Bricks(lumpy);
            Gate(sb, ref fails, "d2-same-bricks",
                Mathf.Abs(bricksA - bricksB) <= 1 && Mathf.Abs(bricksA - bricksL) <= 1,
                $"10x1 day = {bricksA}, 1x10 days = {bricksB}, uneven = {bricksL}");
            Gate(sb, ref fails, "count-10-made-10", bricksA == 10 && bricksB == 10,
                $"{bricksA} / {bricksB} bricks for a count of 10");
            Gate(sb, ref fails, "count-order-clears", !a.StationOf(BuildPlans.Quarry.id).HasOrder,
                "order still active after its count was made");
            Gate(sb, ref fails, "conserved-a", StoneIn(a) == 40, $"stone+brick = {StoneIn(a)} of 40");

            // --- (b) + (c): capacities and conservation, tick by tick ---------
            // Store ceiling 10, so the bricks back up: store full -> rack
            // fills to 12 -> the bench blocks. One idle hauler fills the bay.
            var c = Quarry(60, 10, 1);
            c.PlaceOrder(BuildPlans.Quarry.id, "brick", OutpostLedger.RepeatOrder);
            double nowC = c.lastTicked;
            bool capOk = true, consOk = true;
            string capWhy = "", consWhy = "";
            for (int i = 0; i < 300; i++)
            {
                Advance(c, ref nowC, 0.1);
                foreach (var s in c.Stations)
                {
                    foreach (var row in s.bay)
                        if (row != null && row.whole > s.InputCap && capOk)
                        { capOk = false; capWhy = $"tick {i}: bay {row.resource} {row.whole} > {s.InputCap}"; }
                    if (s.RackTotal > s.OutputCap && capOk)
                    { capOk = false; capWhy = $"tick {i}: rack {s.RackTotal} > {s.OutputCap}"; }
                }
                int held = StoneIn(c);
                if (held != 60 && consOk) { consOk = false; consWhy = $"tick {i}: stone+brick = {held} of 60"; }
            }
            var cs = c.StationOf(BuildPlans.Quarry.id);
            Gate(sb, ref fails, "bay-rack-within-capacity", capOk, capOk ? $"rack {cs.RackTotal}/{cs.OutputCap}" : capWhy);
            Gate(sb, ref fails, "nothing-created-or-destroyed", consOk, consOk ? "60 stone in, 60 accounted" : consWhy);
            Gate(sb, ref fails, "full-rack-blocks-bench", cs.RackFull && cs.benchState == BenchState.Finished,
                $"rack {cs.RackTotal}/{cs.OutputCap}, bench {cs.benchState}, stall '{c.StallReason(c.hands[0])}'");

            // --- (d) repeat until stopped -------------------------------------
            var d = Quarry(200, 1000, 1);
            d.PlaceOrder(BuildPlans.Quarry.id, "brick", OutpostLedger.RepeatOrder);
            double nowD = d.lastTicked;
            Advance(d, ref nowD, 20.0);
            int during = Bricks(d);
            var ds = d.StationOf(BuildPlans.Quarry.id);
            Gate(sb, ref fails, "repeat-runs-past-a-count", during > 10 && ds.HasOrder,
                $"{during} bricks in 20 days, order active {ds.HasOrder}");
            d.StopOrder(BuildPlans.Quarry.id);
            int atStop = Bricks(d);
            Advance(d, ref nowD, 10.0);
            int after = Bricks(d);
            Gate(sb, ref fails, "stop-stops", after <= atStop + 1 && !ds.HasOrder
                                              && ds.benchState == BenchState.Empty,
                $"{atStop} at stop, {after} ten days later, bench {ds.benchState}");
            Gate(sb, ref fails, "conserved-d", StoneIn(d) == 200, $"stone+brick = {StoneIn(d)} of 200");

            // --- (e) a hand leaving mid-haul puts the armful down -------------
            var e = Quarry(20, 1000, 1);
            e.PlaceOrder(BuildPlans.Quarry.id, "brick", OutpostLedger.RepeatOrder);
            double nowE = e.lastTicked;
            var hauler = e.hands[1];
            for (int i = 0; i < 40 && !hauler.Hauling; i++) Advance(e, ref nowE, 0.02);
            bool wasHauling = hauler.Hauling;
            int beforeLeave = StoneIn(e);
            e.RemoveHand(hauler);
            int afterLeave = StoneIn(e);
            Gate(sb, ref fails, "leaving-hand-drops-nothing",
                wasHauling && !hauler.Hauling && afterLeave == beforeLeave && afterLeave == 20,
                $"hauling {wasHauling}, stone+brick {beforeLeave} -> {afterLeave} of 20");

            // --- (f) demolishing one of two same-plan stations -----------------
            var f = Quarry(0, 1000, 0);
            f.built.Add(BuildPlans.Quarry.id);
            f.raised.Add(new BuiltBuilding { planId = BuildPlans.Quarry.id, x = 0f });
            f.raised.Add(new BuiltBuilding { planId = BuildPlans.Quarry.id, x = 10f });
            f.EnsureStations();
            var f0 = f.StationOf(BuildPlans.Quarry.id, 0);
            var f1 = f.StationOf(BuildPlans.Quarry.id, 1);
            f0.Rack(Res.Brick, true).whole = 3;
            f1.Rack(Res.Brick, true).whole = 7;
            f1.Bay(Res.Stone, true).whole = 2;
            f.DemolishBuilt(BuildPlans.Quarry.id, 0);      // the FIRST one comes down
            var fs = f.StationForRaised(0);
            Gate(sb, ref fails, "demolish-keeps-survivor-stock",
                f.Stations.Count == 1 && fs == f1 && f1.ordinal == 0 && f1.RackCount(Res.Brick) == 7
                && f1.BayCount(Res.Stone) == 2 && f.StoreCountOf(Res.Brick) == 3
                && !f.IsLive(f0) && f0.removed && f0.RackTotal == 0 && f.IsLive(f1),
                $"stations {f.Stations.Count}, survivor rack {f1.RackCount(Res.Brick)} bay {f1.BayCount(Res.Stone)}, "
                + $"store brick {f.StoreCountOf(Res.Brick)}, dead row live {f.IsLive(f0)}");

            // --- (g) Take and cost gates never spend a bay ---------------------
            var g = Quarry(2, 1000, 0);
            var gs = g.StationOf(BuildPlans.Quarry.id);
            gs.Bay(Res.Stone, true).whole = 5;
            gs.Rack(Res.Brick, true).whole = 1;
            int spendable = g.SpendableOf(Res.Stone), shown = g.CountOf(Res.Stone);
            int took = g.Take(Res.Stone, 10);
            int tookBrick = g.Take(Res.Brick, 5);
            Gate(sb, ref fails, "take-skips-bays",
                took == 2 && gs.BayCount(Res.Stone) == 5 && spendable == 2 && shown == 7 && tookBrick == 1,
                $"took {took} of 10 (bay kept {gs.BayCount(Res.Stone)}), spendable {spendable}, shown {shown}, brick {tookBrick}");

            // --- (h) the store ceiling holds with loads walking in -------------
            // An unmanned quarry's bay goes home while a gatherer fills the
            // same pile: nothing over the ceiling, nothing lost.
            var h = new OutpostLedger { ceilingPer = 10, stationsMigrated = true, campfireLevel = 2 };
            h.built.Add(BuildPlans.Quarry.id);
            h.hands.Add(new OutpostHand { name = "Hauler", order = OutpostOrder.Idle });
            for (int i = 0; i < 3; i++)
                h.hands.Add(new OutpostHand { name = "Gatherer" + i, order = OutpostOrder.Gather, target = Res.Stone });
            h.Store(Res.Food, true).whole = 1000;
            h.Store(Res.Stone, true).whole = 7;
            var seam = h.AddStanding(Res.Stone, 200f);
            seam.regrowPerDay = 0f;
            h.lastTicked = 0.0;
            h.EnsureStations();
            h.StationOf(BuildPlans.Quarry.id).Bay(Res.Stone, true).whole = 6;
            double nowH = h.lastTicked;
            bool ceilOk = true, consH = true, hauled = false;
            string ceilWhy = "", consHWhy = "";
            for (int i = 0; i < 200; i++)
            {
                Advance(h, ref nowH, 0.05);
                if (h.CarriedOf(Res.Stone) > 0) hauled = true;
                if (h.StoreCountOf(Res.Stone) > h.ceilingPer && ceilOk)
                { ceilOk = false; ceilWhy = $"tick {i}: store {h.StoreCountOf(Res.Stone)} > {h.ceilingPer}"; }
                var st = h.Store(Res.Stone);
                float all = (st != null ? st.whole + st.part : 0f) + h.CountOf(Res.Stone) - h.StoreCountOf(Res.Stone)
                            + h.CarriedOf(Res.Stone) + h.Stock(Res.Stone).standing;
                if (Mathf.Abs(all - 213f) > 0.01f && consH) { consH = false; consHWhy = $"tick {i}: {all:0.###} of 213"; }
            }
            Gate(sb, ref fails, "store-ceiling-holds", ceilOk && hauled,
                ceilOk ? $"store stone {h.StoreCountOf(Res.Stone)}/{h.ceilingPer}, bay {h.StationOf(BuildPlans.Quarry.id).BayCount(Res.Stone)}, hauled {hauled}" : ceilWhy);
            Gate(sb, ref fails, "store-ceiling-conserves", consH, consH ? "213 stone accounted every tick" : consHWhy);

            sb.AppendLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
            if (fails == 0) Debug.Log(sb.ToString()); else Debug.LogError(sb.ToString());
            return fails == 0;
        }

        static OutpostLedger Quarry(int stone, int ceiling, int idleHaulers)
        {
            var l = new OutpostLedger();
            l.ceilingPer = ceiling;
            l.campfireLevel = 2;            // brick is a fire-II recipe
            l.stationsMigrated = true;      // no free order: the test places them
            l.built.Add(BuildPlans.Quarry.id);
            l.hands.Add(new OutpostHand { name = "Quarryman", order = OutpostOrder.Work, target = BuildPlans.Quarry.id });
            for (int i = 0; i < idleHaulers; i++)
                l.hands.Add(new OutpostHand { name = "Hauler" + i, order = OutpostOrder.Idle });
            l.Store(Res.Stone, true).whole = stone;
            l.Store(Res.Tools, true).whole = 8;     // brick wears tools
            l.Store(Res.Food, true).whole = 1000;   // fed hands work at full strength
            l.lastTicked = 0.0;
            l.EnsureStations();
            return l;
        }

        /// Tick through `Tick`, the same door the game uses, so the quantum
        /// is the game's. The epsilon keeps a float-short last quantum from
        /// being carried to the next call.
        static void Advance(OutpostLedger l, ref double now, double days)
        {
            now += days * TimeOfDay.DayLength;
            l.Tick(now + 1e-3);
        }

        static int Bricks(OutpostLedger l) => l.CountOf(Res.Brick) + l.CarriedOf(Res.Brick);

        /// Stone anywhere + bricks anywhere (one stone per brick).
        static int StoneIn(OutpostLedger l)
        {
            int n = l.CountOf(Res.Stone) + l.CarriedOf(Res.Stone) + Bricks(l);
            foreach (var s in l.Stations)
            {
                if (s.benchState != BenchState.Loaded && s.benchState != BenchState.Working) continue;
                var r = s.BenchRecipe;
                if (r == null) continue;
                foreach (var line in r.takes) if (line.res == Res.Stone) n += line.n;
            }
            return n;
        }

        static void Gate(StringBuilder sb, ref int fails, string name, bool ok, string detail)
        {
            if (!ok) fails++;
            sb.Append(ok ? "  PASS " : "  FAIL ").Append(name).Append(" -- ").AppendLine(detail);
        }
    }
}
