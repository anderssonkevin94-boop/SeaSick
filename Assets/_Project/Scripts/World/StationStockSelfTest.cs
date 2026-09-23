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
    /// (i) A build site (5 timber + 3 stone, 2 builders, store or ground):
    ///     delivered + in arms never above the cost, shown counts are the
    ///     whole delivered units, 0 % until stocked and cleared, every unit
    ///     conserved, at most 4 trips, D2 across tick sizes, and an old
    ///     save's over-delivery goes back to the store.
    /// (j) Trip time is walked distance (2026-09-23): every ledger here has
    ///     an explicit centre (the store), quarry and site position, so each
    ///     trip's time is known; a trip books exactly `TripDays`, and a site
    ///     twice as far from the store takes ~twice as long to stock.
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
            f.raised.Clear();
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
            h.SetCentre(Vector3.zero);
            h.SetSourceMetres(Res.Stone, 20f);
            h.built.Add(BuildPlans.Quarry.id);
            h.raised.Add(new BuiltBuilding { planId = BuildPlans.Quarry.id, x = 15f });
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
                // A 15 m trip (~14 s) starts and lands inside one 18 s
                // quantum, so "seen in arms" is not enough: a trip counter
                // or the bay going down says it happened.
                if (h.CarriedOf(Res.Stone) > 0 || h.hands[0].haulSerial > 0) hauled = true;
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

            // --- (i) a build site: stock exactly the cost, THEN build ----------
            // Kevin's phone playtest, 2026-09-23. A site wanting 5 timber +
            // 3 stone, two builders, the store stocked / the store empty
            // (they cut and quarry it themselves), stepped fine and coarse.
            foreach (bool fromStore in new[] { true, false })
                foreach (double stepDays in new[] { 0.02, 0.1, 0.5 })
                    SiteRun(sb, ref fails, fromStore, stepDays);

            // D2: the same site, ten 0.1-day ticks vs one 1-day tick vs
            // ragged ticks, compared mid-stocking and at the end.
            foreach (double span in new[] { 0.3, 1.0, 4.0 })
            {
                var s1 = Site(false); var s2 = Site(false); var s3 = Site(false);
                double n1 = s1.lastTicked, n2 = s2.lastTicked, n3 = s3.lastTicked;
                int k = Mathf.RoundToInt((float)(span / 0.1));
                for (int i = 0; i < k; i++) Advance(s1, ref n1, 0.1);
                Advance(s2, ref n2, span);
                double left = span;
                double[] rag = { 0.03, 0.17, 0.01, 0.42 };
                for (int i = 0; left > 1e-9; i++) { double st = System.Math.Min(left, rag[i % rag.Length]); Advance(s3, ref n3, st); left -= st; }
                Gate(sb, ref fails, $"site-d2-{span:0.0}d", SameSite(s1, s2) && SameSite(s1, s3),
                    $"{SiteState(s1)} | {SiteState(s2)} | {SiteState(s3)}");
            }

            // An old save's over-delivered row (8 of 5 logs, 0.6 of a log in
            // `donePart`, 4 of 3 stone): the surplus goes back to the store,
            // nothing lost or made.
            {
                var o = Site(true);
                var row = o.sites[0];
                o.Store(Res.Timber).whole = 0; o.Store(Res.Stone).whole = 0;
                row.done = 8; row.donePart = 0.6f; row.stoneDone = 4;
                float t0 = TimberAll(o), st0 = StoneAll(o);
                double no = o.lastTicked;
                Advance(o, ref no, 0.1);
                Gate(sb, ref fails, "old-surplus-back-to-store",
                    row.done == 5 && row.stoneDone == 3 && row.donePart == 0f
                    && Mathf.Abs(TimberAll(o) - t0) < 1e-3f && Mathf.Abs(StoneAll(o) - st0) < 1e-3f,
                    $"site {row.done}/5 timber {row.stoneDone}/3 stone part {row.donePart}, "
                    + $"timber {t0:0.##}->{TimberAll(o):0.##}, stone {st0:0.##}->{StoneAll(o):0.##}");
            }

            // --- (j) trip time is distance ----------------------------------
            {
                var near = Site(true, 100f);
                var far = Site(true, 200f);
                double nn = near.lastTicked, nf = far.lastTicked;
                Advance(near, ref nn, 0.1);
                Advance(far, ref nf, 0.1);
                float dn = near.hands[0].haulDays, df = far.hands[0].haulDays;
                float want = (2f * 100f * OutpostLedger.PathFactor / OutpostLedger.WalkMetresPerSecond
                              + OutpostLedger.HandleSeconds) / TimeOfDay.DayLength;
                Gate(sb, ref fails, "trip-days-is-distance",
                    near.hands[0].Hauling && Mathf.Abs(dn - want) < 1e-4f,
                    $"100 m store->site trip {dn * TimeOfDay.DayLength:0.0} s, expected {want * TimeOfDay.DayLength:0.0} s "
                    + $"(2 x 100 m x {OutpostLedger.PathFactor} / {OutpostLedger.WalkMetresPerSecond} m/s + {OutpostLedger.HandleSeconds} s)");
                // A cut trip: walk out, 5 s a log, walk back.
                var cut = Site(false, 7.07f);
                double nc = cut.lastTicked;
                Advance(cut, ref nc, 0.1);
                var ch = cut.hands[0];
                float wantCut = (2f * 20f * OutpostLedger.PathFactor / OutpostLedger.WalkMetresPerSecond
                                 + OutpostLedger.HandleSeconds + ch.haulCount * Playtest.CutSecondsPerLog) / TimeOfDay.DayLength;
                Gate(sb, ref fails, "cut-trip-5s-a-log",
                    ch.Hauling && ch.haulFrom == HaulPlace.Field && ch.haulRes == Res.Timber
                    && Mathf.Abs(ch.haulDays - wantCut) < 1e-4f,
                    $"{ch.haulCount} logs cut 20 m out: {ch.haulDays * TimeOfDay.DayLength:0.0} s, expected {wantCut * TimeOfDay.DayLength:0.0} s");
                double tn = StockedAt(near, nn), tf = StockedAt(far, nf);
                double ratio = tf / System.Math.Max(1e-6, tn);
                Gate(sb, ref fails, "twice-as-far-twice-as-long", df > dn * 1.9f && ratio > 1.7 && ratio < 2.1,
                    $"trip {dn * TimeOfDay.DayLength:0.0} s vs {df * TimeOfDay.DayLength:0.0} s; stocked at {tn:0.00} d (100 m) vs {tf:0.00} d (200 m), x{ratio:0.00} "
                    + "(observed on the 0.1-day quantum)");
            }

            sb.AppendLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
            if (fails == 0) Debug.Log(sb.ToString()); else Debug.LogError(sb.ToString());
            return fails == 0;
        }

        /// A site wanting 5 timber + 3 stone and two builders. `fromStore`:
        /// 10 of each in the store and nothing standing; else an empty store
        /// and ground to cut and quarry (no regrowth, so it conserves).
        static OutpostLedger Site(bool fromStore, float siteMetres = 7.07f)
        {
            var l = new OutpostLedger { ceilingPer = 20, stationsMigrated = true };
            l.SetCentre(Vector3.zero);                 // the store is the fire square at 0,0
            l.SetSourceMetres(Res.Timber, 20f);        // trees and rock 20 m out
            l.SetSourceMetres(Res.Stone, 20f);
            l.hands.Add(new OutpostHand { name = "Bo", order = OutpostOrder.Build });
            l.hands.Add(new OutpostHand { name = "Sten", order = OutpostOrder.Build });
            l.Store(Res.Food, true).whole = 1000;
            l.Store(Res.Timber, true).whole = fromStore ? 10 : 0;
            l.Store(Res.Stone, true).whole = fromStore ? 10 : 0;
            l.AddStanding(Res.Timber, fromStore ? 0f : 40f).regrowPerDay = 0f;
            l.AddStanding(Res.Stone, fromStore ? 0f : 40f).regrowPerDay = 0f;
            l.sites.Add(new PendingBuild
            {
                planId = BuildPlans.Hut.id, x = siteMetres * 0.7071f, z = siteMetres * 0.7071f,
                needed = 5, stoneNeeded = 3, phased = true,
            });
            l.lastTicked = 0.0;
            return l;
        }

        static float TimberAll(OutpostLedger l)
        {
            var st = l.Store(Res.Timber);
            return (st != null ? st.whole + st.part : 0f) + l.CarriedOf(Res.Timber)
                + l.Stock(Res.Timber).standing + l.sites[0].done + l.sites[0].donePart;
        }

        static float StoneAll(OutpostLedger l)
        {
            var st = l.Store(Res.Stone);
            return (st != null ? st.whole + st.part : 0f) + l.CarriedOf(Res.Stone)
                + l.Stock(Res.Stone).standing + l.sites[0].stoneDone + l.sites[0].stoneDonePart;
        }

        static int Trips(OutpostLedger l)
        {
            int n = 0;
            foreach (var h in l.hands) n += h.haulSerial;
            return n;
        }

        static void SiteRun(StringBuilder sb, ref int fails, bool fromStore, double stepDays)
        {
            var l = Site(fromStore);
            var p = l.sites[0];
            float t0 = TimberAll(l), s0 = StoneAll(l);
            double now = l.lastTicked;
            string tag = $"{(fromStore ? "store" : "cut")}-{stepDays:0.00}d";
            string over = null, shown = null, early = null, cons = null;
            bool sawBuild = false;
            int ticks = Mathf.CeilToInt((float)(8.0 / stepDays));
            for (int i = 0; i < ticks && !p.Complete; i++)
            {
                Advance(l, ref now, stepDays);
                int tIn = p.done + l.CarriedOf(Res.Timber), sIn = p.stoneDone + l.CarriedOf(Res.Stone);
                if (over == null && (p.done > p.needed || p.stoneDone > p.stoneNeeded || tIn > p.needed || sIn > p.stoneNeeded))
                    over = $"tick {i}: timber {p.done}+{l.CarriedOf(Res.Timber)} carried of {p.needed}, stone {p.stoneDone}+{l.CarriedOf(Res.Stone)} of {p.stoneNeeded}";
                string line = p.PhaseLine;
                bool countsOk = p.donePart == 0f && p.stoneDonePart == 0f
                    && Mathf.Abs(p.Fill01 - (p.done + p.stoneDone) / 8f) < 1e-5f
                    && (p.Stocked || (line.Contains($"{p.done}/{p.needed} logs") && line.Contains($"{p.stoneDone}/{p.stoneNeeded} stone")));
                if (shown == null && !countsOk)
                    shown = $"tick {i}: '{line}' done {p.done}+{p.donePart} stone {p.stoneDone}+{p.stoneDonePart} fill {p.Fill01:0.###}";
                if (early == null && !(p.Stocked && p.Cleared) && (p.built > 0f || p.Build01 > 0f || p.Progress01 > 0f))
                    early = $"tick {i}: built {p.built:0.###} build01 {p.Build01:0.##} progress {p.Progress01:0.##} at {p.done}/5 {p.stoneDone}/3";
                if (p.Stocked && p.built > 0f) sawBuild = true;
                if (cons == null && (Mathf.Abs(TimberAll(l) - t0) > 1e-3f || Mathf.Abs(StoneAll(l) - s0) > 1e-3f))
                    cons = $"tick {i}: timber {TimberAll(l):0.###} of {t0}, stone {StoneAll(l):0.###} of {s0}";
            }
            Gate(sb, ref fails, $"site-never-over-{tag}", over == null, over ?? "delivered + in arms never above the cost");
            Gate(sb, ref fails, $"site-counts-true-{tag}", shown == null, shown ?? $"'{p.PhaseLine}', whole units, no fractions");
            Gate(sb, ref fails, $"site-build-waits-{tag}", early == null, early ?? "0 % until 5/5 + 3/3");
            Gate(sb, ref fails, $"site-conserves-{tag}", cons == null, cons ?? $"timber {t0}, stone {s0} accounted every tick");
            Gate(sb, ref fails, $"site-completes-{tag}", p.Complete && sawBuild && p.done == 5 && p.stoneDone == 3
                                                      && Mathf.Approximately(p.Progress01, 1f) && l.CarriedOf(Res.Timber) == 0,
                $"complete {p.Complete} at {now / TimeOfDay.DayLength:0.0} d, {p.done}/5 {p.stoneDone}/3, {p.Progress01:P0}");
            Gate(sb, ref fails, $"site-trips-{tag}", Trips(l) <= 4,
                $"{Trips(l)} trips for 5 timber (armful 2) + 3 stone (armful 3); 4 is the least");
        }

        /// Days (from 0) at which the site first reads Stocked, stepping 0.02 d.
        static double StockedAt(OutpostLedger l, double now)
        {
            var p = l.sites[0];
            for (int i = 0; i < 1000 && !p.Stocked; i++) Advance(l, ref now, 0.02);
            return now / TimeOfDay.DayLength;
        }

        static bool SameSite(OutpostLedger a, OutpostLedger b)
        {
            var p = a.sites[0]; var q = b.sites[0];
            return p.done == q.done && p.stoneDone == q.stoneDone && Mathf.Abs(p.built - q.built) < 1e-3f
                && a.CarriedOf(Res.Timber) == b.CarriedOf(Res.Timber) && a.CarriedOf(Res.Stone) == b.CarriedOf(Res.Stone)
                && Mathf.Abs(a.Stock(Res.Timber).standing - b.Stock(Res.Timber).standing) < 1e-3f
                && Mathf.Abs(a.Stock(Res.Stone).standing - b.Stock(Res.Stone).standing) < 1e-3f;
        }

        static string SiteState(OutpostLedger l)
        {
            var p = l.sites[0];
            return $"{p.done}/5 {p.stoneDone}/3 arms {l.CarriedOf(Res.Timber)}t{l.CarriedOf(Res.Stone)}s built {p.built:0.###}";
        }

        static OutpostLedger Quarry(int stone, int ceiling, int idleHaulers)
        {
            var l = new OutpostLedger();
            l.SetCentre(Vector3.zero);                  // store at the fire square
            l.raised.Add(new BuiltBuilding { planId = BuildPlans.Quarry.id, x = 15f });  // quarry 15 m off
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
