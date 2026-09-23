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
