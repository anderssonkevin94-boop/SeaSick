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
    ///     `OutputCap`, checked after every 0.1-day tick (five quanta).
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
    /// (k) Gathering is trips (Kevin, 2026-09-23): a timber gatherer 20 m
    ///     out books 2 logs a trip in 2 x 20 x 1.15 / 2.6 + 1 + 2 x 5 s; the
    ///     same camp ticked 0.1 d at a time, 1 d at once and raggedly lands
    ///     on the same books; two gatherers never push the store past its
    ///     ceiling and every log is accounted; with the store full a gatherer
    ///     says so and helps build / hauls for the stations, then resumes.
    /// (l) Clearing a plot is seconds of builder time: 2 trees = 10 s,
    ///     a rock = 8 s (the rest of an 18 s tick goes on building).
    /// (m) Tempo, Kevin's phone playtest 2026-09-24: one log -> 3 boards in
    ///     45 s (15 s a plank), all 3 onto the rack together; a rack short of
    ///     room takes what fits and the rest wait on the bench, blocking it;
    ///     a sawyer's store fetch lands within one 3.6 s quantum of its
    ///     walked time (1 s to pick up); a hungry hand's line says he is
    ///     working slowly and at what pace; a busy camp ticked for 30 days in
    ///     0.02-, 0.1- and 1-day calls keeps the same books (and is timed).
    ///     DIAGNOSTIC, never a failure: the same camp STEPPED (not ticked) in
    ///     0.02-, 0.1- and 1-day steps -- how far the books drift with the
    ///     step size, which is why there is no coarse catch-up path.
    /// (n) Ship <-> store transfers (Kevin, 2026-09-24: *"unload things from
    ///     my ship to the island and vice versa ... physically carried"*),
    ///     against a fake `ICargoSide` with its gangway 30 m from the store:
    ///     no order while she is away; 7 timber store -> ship lands 7 aboard
    ///     and 0 lost in 0.02-, 1-day and ragged ticks; a trip books the
    ///     walked store <-> gangway distance; 5 stone ship -> store lands 5;
    ///     a full store and a full hold each wait, say why, and resume; a
    ///     cancel mid-trip lands only what was in arms; she casting off
    ///     mid-trip sends a store -> ship armful back to the store (order
    ///     paused, count restored) and lets a ship -> store armful finish;
    ///     a re-ordered hand's armful lands, a builder walks it on.
    /// (o) Station spots (Kevin, 2026-09-30): the forge's smelter and forge
    ///     advance together, each exactly as far as alone (one worker, rate
    ///     not divided); a grill pauses "waiting for fish" and resumes when
    ///     fish reaches the store; a sawmill pauses "store full of boards"
    ///     without loading its timber and resumes when the store has room.
    ///     (b)'s old "full rack blocks the bench" is now "the spot pauses".
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
            // Spots (2026-09-30): with the rack full and the store full of
            // bricks the spot AUTO-PAUSES ("store full of brick") instead of
            // loading a batch it could not put down; a batch that finished
            // into a nearly-full rack may still wait on the bench.
            var cspot = cs.Spots[0];
            Gate(sb, ref fails, "full-rack-pauses-spot",
                cs.RackFull && !cspot.BenchBusy && cspot.pauseReason != null
                && cspot.pauseReason.StartsWith("store full of") && cspot.Selected,
                $"rack {cs.RackTotal}/{cs.OutputCap}, bench {cs.benchState}, spot '{cspot.pauseReason ?? "running"}', "
                + $"stall '{c.StallReason(c.hands[0])}'");

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
                // A 15 m trip (~14 s) starts and lands inside one 0.05-day
                // check, so "seen in arms" is not enough: a trip counter
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

            // --- (j) trips are WALKED (2026-09-27, docs/DELIVERY-ON-ARRIVAL.md)
            {
                float day = TimeOfDay.WorkDaySeconds, qs = OutpostLedger.QuantumDays * day;
                float v = OutpostLedger.WalkMetresPerSecond;
                // One quantum: the builder is at the store (0 m), stoops 1 s,
                // PICKS UP (the store drops now) and is carrying the rest.
                var near = Site(true, 100f);
                double nn = near.lastTicked;
                near.Tick(nn + qs + 1e-3); nn += qs;
                var h0 = near.hands[0];
                float wantLeft = 100f - v * (qs - OutpostLedger.HandleSeconds);
                Gate(sb, ref fails, "trip-picked-at-pickup-walked-after",
                    h0.Hauling && h0.haulPicked && h0.Leg == TripLeg.ToDrop && Mathf.Abs(h0.legLeft - wantLeft) < 0.05f
                    && near.sites[0].done == 0,
                    $"after {qs:0.0} s: leg {h0.Leg}, picked {h0.haulPicked}, {h0.legLeft:0.00} m left (want {wantLeft:0.00}), "
                    + $"store timber {near.StoreCountOf(Res.Timber)}, site {near.sites[0].done}/5");
                // The site counts the logs on ARRIVAL: 1 s + 100 m / 2.6 m/s.
                double arrive = OutpostLedger.HandleSeconds + 100.0 / v, seen = -1;
                for (int i = 2; i <= 40 && seen < 0; i++)
                {
                    near.Tick(i * qs + 1e-3);
                    if (near.sites[0].done > 0) seen = i * qs;
                }
                Gate(sb, ref fails, "site-counts-on-arrival",
                    seen >= arrive - 1e-3 && seen <= arrive + qs + 1e-3,
                    $"first logs counted at {seen:0.0} s; the walk lands at {arrive:0.0} s (quantum {qs:0.0} s)");
                var far = Site(true, 200f);
                near = Site(true, 100f);
                nn = near.lastTicked;
                double nf = far.lastTicked;
                double tn = StockedAt(near, nn), tf = StockedAt(far, nf);
                double ratio = tf / System.Math.Max(1e-6, tn);
                Gate(sb, ref fails, "twice-as-far-twice-as-long", ratio > 1.7 && ratio < 2.1,
                    $"stocked at {tn:0.00} d (100 m) vs {tf:0.00} d (200 m), x{ratio:0.00}");
                // A cut trip: the island gives nothing up until the cutting
                // ends -- 20 m out is 7.7 s, so after one quantum he is still
                // walking and every tree is standing.
                var cut = Site(false, 7.07f);
                double nc = cut.lastTicked;
                cut.Tick(nc + qs + 1e-3);
                var ch = cut.hands[0];
                float wantWork = OutpostLedger.HandleSeconds + ch.haulCount * Playtest.CutSecondsPerLog;
                Gate(sb, ref fails, "cut-nothing-leaves-the-island-before-pickup",
                    ch.Hauling && ch.haulFrom == HaulPlace.Field && !ch.haulPicked && ch.Leg == TripLeg.ToPickup
                    && Mathf.Abs(cut.Stock(Res.Timber).standing - 40f) < 1e-3f && Mathf.Abs(ch.workLeft - wantWork) < 1e-3f,
                    $"leg {ch.Leg}, standing {cut.Stock(Res.Timber).standing:0.#}/40, cutting {ch.workLeft:0.0} s booked (want {wantWork:0.0})");
            }

            // --- (k) gathering is trips (Kevin, 2026-09-23) -----------------
            {
                // (1) one gatherer, trees 20 m from the store: walk 7.7 s,
                // cut 1 + 2 x 5 s, walk back 7.7 s -- the first armful is
                // counted at ~26 s; at 36 s he is cutting the second (both
                // logs still standing: nothing leaves the island early).
                var gt = Gatherers(1, 20);
                var gh = gt.hands[0];
                double ng = gt.lastTicked;
                float leg = 20f / OutpostLedger.WalkMetresPerSecond;
                float cutS = OutpostLedger.HandleSeconds + 2f * Playtest.CutSecondsPerLog;
                float firstIn = 2f * leg + cutS;
                Advance(gt, ref ng, 0.2);                    // 36 s
                bool secondPicked = 36f >= firstIn + leg + cutS;
                Gate(sb, ref fails, "gather-trip-2-logs-walked",
                    gt.StoreCountOf(Res.Timber) == 2 && gt.CarriedOf(Res.Timber) == (secondPicked ? 2 : 0)
                    && Mathf.Abs(gt.Stock(Res.Timber).standing - (secondPicked ? 36f : 38f)) < 1e-3f,
                    $"first armful lands at {firstIn:0.0} s; at 36 s store {gt.StoreCountOf(Res.Timber)}, "
                    + $"arms {gt.CarriedOf(Res.Timber)}, standing {gt.Stock(Res.Timber).standing:0.#}, leg {gh.Leg}");

                // A gatherer leaving mid-trip (`RemoveHand`) before he has
                // cut anything takes nothing and leaves nothing: the trees
                // are still standing. Nothing lost, nothing made.
                var lv = Gatherers(1, 20);
                double nl = lv.lastTicked;
                Advance(lv, ref nl, 0.1);
                var lh = lv.hands[0];
                bool midTrip = lh.Hauling && lh.haulFrom == HaulPlace.Field;
                bool picked = lh.haulPicked;
                int store0 = lv.StoreCountOf(Res.Timber) + (picked ? lh.haulCount : 0);
                lv.RemoveHand(lh);
                Gate(sb, ref fails, "gather-leaving-hand-drops-nothing",
                    midTrip && !lh.Hauling && lv.StoreCountOf(Res.Timber) == store0 && Mathf.Abs(GatherAll(lv) - 40f) < 1e-3f,
                    $"mid-trip {midTrip} (picked {picked}), store {lv.StoreCountOf(Res.Timber)}, {GatherAll(lv):0.##} of 40 accounted, felled {lv.timberTaken:0.#}");

                // (2) D2: ten 0.1-day ticks vs one 1-day tick vs ragged.
                foreach (double span in new[] { 1.0, 1.7 })
                {
                    var d1 = Gatherers(1, 20); var d2 = Gatherers(1, 20); var d3 = Gatherers(1, 20);
                    double m1 = d1.lastTicked, m2 = d2.lastTicked, m3 = d3.lastTicked;
                    int k = Mathf.RoundToInt((float)(span / 0.1));
                    for (int i = 0; i < k; i++) Advance(d1, ref m1, 0.1);
                    Advance(d2, ref m2, span);
                    double left = span;
                    double[] rag = { 0.03, 0.17, 0.01, 0.42 };
                    for (int i = 0; left > 1e-9; i++) { double st = System.Math.Min(left, rag[i % rag.Length]); Advance(d3, ref m3, st); left -= st; }
                    Gate(sb, ref fails, $"gather-d2-{span:0.0}d", SameGather(d1, d2) && SameGather(d1, d3),
                        $"{GatherState(d1)} | {GatherState(d2)} | {GatherState(d3)}");
                }

                // (3) two gatherers, ceiling 10: never over, every log accounted.
                var two = Gatherers(2, 10);
                double nt = two.lastTicked;
                bool ceilG = true, consG = true;
                string ceilGWhy = "", consGWhy = "";
                for (int i = 0; i < 60; i++)
                {
                    Advance(two, ref nt, 0.05);
                    if (two.StoreCountOf(Res.Timber) > two.ceilingPer && ceilG)
                    { ceilG = false; ceilGWhy = $"tick {i}: store {two.StoreCountOf(Res.Timber)} > {two.ceilingPer}"; }
                    float all = GatherAll(two);
                    if (Mathf.Abs(all - 40f) > 1e-3f && consG) { consG = false; consGWhy = $"tick {i}: {all:0.###} of 40"; }
                }
                string full0 = two.StallReason(two.hands[0]) ?? "";
                Gate(sb, ref fails, "gather-ceiling-two-gatherers",
                    ceilG && consG && two.StoreCountOf(Res.Timber) == 10 && two.CarriedOf(Res.Timber) == 0
                    && full0.Contains("store is full of timber"),
                    ceilG && consG ? $"store {two.StoreCountOf(Res.Timber)}/10, arms {two.CarriedOf(Res.Timber)}, "
                                     + $"40 logs accounted every tick, stall '{full0}'"
                                   : ceilGWhy + consGWhy);

                // (4a) store full + a site queued: says so, helps build, resumes.
                var bs = Gatherers(1, 10);
                bs.Store(Res.Timber).whole = 10;
                bs.sites.Add(new PendingBuild { planId = BuildPlans.Hut.id, x = 5f, z = 5f, needed = 4, phased = true });
                var bh = bs.hands[0];
                string whyB = bs.StallReason(bh) ?? "";
                double nb = bs.lastTicked;
                Advance(bs, ref nb, OutpostLedger.QuantumDays);   // ONE quantum: after it room is back and he resumes
                bool helpedB = bh.haulSerial > 0 && (bs.sites[0].done > 0 || bs.HaulOf(bh).to == HaulPlace.Site)
                               && bs.Stock(Res.Timber).standing >= 40f - 1e-3f;
                for (int i = 0; i < 40 && !(bs.sites[0].Complete && bs.StoreCountOf(Res.Timber) == 10); i++) Advance(bs, ref nb, 0.1);
                Gate(sb, ref fails, "gather-full-store-helps-build",
                    whyB.Contains("store is full of timber, helping build") && helpedB
                    && bs.sites[0].Complete && bs.StoreCountOf(Res.Timber) == 10,
                    $"stall '{whyB}', first quantum a site trip {helpedB}, site complete {bs.sites[0].Complete}, "
                    + $"store back to {bs.StoreCountOf(Res.Timber)}/10 at {nb / TimeOfDay.WorkDaySeconds:0.0} d");

                // (4b) store full + a manned station wanting it: hauls for it, resumes.
                var q = Quarry(10, 10, 0);
                q.SetSourceMetres(Res.Stone, 20f);
                q.AddStanding(Res.Stone, 40f).regrowPerDay = 0f;
                q.hands.Add(new OutpostHand { name = "Gatherer", order = OutpostOrder.Gather, target = Res.Stone });
                q.PlaceOrder(BuildPlans.Quarry.id, "brick", OutpostLedger.RepeatOrder);
                var qh = q.hands[1];
                string whyQ = q.StallReason(qh) ?? "";
                double nq = q.lastTicked;
                Advance(q, ref nq, OutpostLedger.QuantumDays);     // ONE quantum (was 0.1 d = the old quantum)
                var qv = q.HaulOf(qh);
                bool haulQ = qh.haulSerial > 0 && q.Stock(Res.Stone).standing >= 40f - 1e-3f
                             && (!qv.active || (qv.from == HaulPlace.Store && qv.to == HaulPlace.Station));
                Advance(q, ref nq, 2.0);
                Gate(sb, ref fails, "gather-full-store-hauls",
                    whyQ.Contains("store is full of stone, hauling for the stations") && haulQ
                    && q.Stock(Res.Stone).standing < 40f - 1e-3f,
                    $"stall '{whyQ}', first quantum a station haul {haulQ}, "
                    + $"field {q.Stock(Res.Stone).standing:0.#}/40 after 2 d (resumed)");
            }

            // --- (l) clearing a plot is seconds of builder time --------------
            foreach (bool rock in new[] { false, true })
            {
                var c2 = Site(true);
                var p = c2.sites[0];
                p.done = 5; p.stoneDone = 3;                   // stocked: only clear + build left
                if (rock) p.clearRocks = 1; else p.clearTrees = 2;
                p.clearSited = true;
                c2.hands.RemoveAt(1);                         // one builder
                double nc2 = c2.lastTicked;
                string clearRes = rock ? Res.Stone : Res.Timber;
                int before = c2.StoreCountOf(clearRes);
                Advance(c2, ref nc2, 0.1);                    // 18 s
                bool clearedFirst = p.Cleared;
                int landed = c2.StoreCountOf(clearRes) + c2.CarriedOf(clearRes) - before;
                Advance(c2, ref nc2, 0.1);
                float clearSec = rock ? Playtest.ClearSecondsPerRock : 2f * Playtest.ClearSecondsPerTree;
                // (2026-09-27) What comes off the plot is CARRIED to the
                // store, and he walks back to the plot: clearing costs its
                // seconds plus those walks, and the hammering starts after.
                Gate(sb, ref fails, rock ? "clear-rock-8s-carried" : "clear-2-trees-10s-carried",
                    clearedFirst && landed == 2 && c2.StoreCountOf(clearRes) - before == 2 && p.built > 0f,
                    $"{(rock ? "1 rock" : "2 trees")} ({clearSec:0} s of cutting) cleared in the first 18 s {clearedFirst}, "
                    + $"{landed} {clearRes} carried to the store, then {p.built * TimeOfDay.WorkDaySeconds:0.0} s of hammering");
            }

            Tempo(sb, ref fails);
            Transfers(sb, ref fails);
            Deposits(sb, ref fails);
            Delivery(sb, ref fails);
            Fishing(sb, ref fails);
            SpotGates(sb, ref fails);

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
                $"complete {p.Complete} at {now / TimeOfDay.WorkDaySeconds:0.0} d, {p.done}/5 {p.stoneDone}/3, {p.Progress01:P0}");
            Gate(sb, ref fails, $"site-trips-{tag}", Trips(l) <= 4,
                $"{Trips(l)} trips for 5 timber (armful 2) + 3 stone (armful 3); 4 is the least");
        }

        /// Days (from 0) at which the site first reads Stocked, stepping 0.02 d.
        static double StockedAt(OutpostLedger l, double now)
        {
            var p = l.sites[0];
            for (int i = 0; i < 1000 && !p.Stocked; i++) Advance(l, ref now, 0.02);
            return now / TimeOfDay.WorkDaySeconds;
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

        /// `n` timber gatherers, the store at the fire square (0,0), trees
        /// 20 m out, 40 logs standing and no regrowth (so it conserves).
        static OutpostLedger Gatherers(int n, int ceiling)
        {
            var l = new OutpostLedger { ceilingPer = ceiling, stationsMigrated = true };
            l.SetCentre(Vector3.zero);
            l.SetSourceMetres(Res.Timber, 20f);
            for (int i = 0; i < n; i++)
                l.hands.Add(new OutpostHand { name = "Gatherer" + i, order = OutpostOrder.Gather, target = Res.Timber });
            l.Store(Res.Food, true).whole = 1000;
            l.Store(Res.Timber, true).whole = 0;
            l.AddStanding(Res.Timber, 40f).regrowPerDay = 0f;
            l.lastTicked = 0.0;
            return l;
        }

        /// Timber in the store (whole and part), in arms and still standing.
        static float GatherAll(OutpostLedger l)
        {
            var st = l.Store(Res.Timber);
            return (st != null ? st.whole + st.part : 0f) + l.CarriedOf(Res.Timber) + l.Stock(Res.Timber).standing;
        }

        static bool SameGather(OutpostLedger a, OutpostLedger b)
            => a.StoreCountOf(Res.Timber) == b.StoreCountOf(Res.Timber)
            && a.CarriedOf(Res.Timber) == b.CarriedOf(Res.Timber)
            && Mathf.Abs(a.Stock(Res.Timber).standing - b.Stock(Res.Timber).standing) < 1e-3f
            && Mathf.Abs(a.hands[0].haulLeft - b.hands[0].haulLeft) < 1e-4f
            && Mathf.Abs(a.timberTaken - b.timberTaken) < 1e-3f;

        static string GatherState(OutpostLedger l)
            => $"store {l.StoreCountOf(Res.Timber)} arms {l.CarriedOf(Res.Timber)} standing {l.Stock(Res.Timber).standing:0.#} "
             + $"taken {l.timberTaken:0.#} left {l.hands[0].haulLeft * TimeOfDay.WorkDaySeconds:0.0}s";

        /// Whole quanta a tick of `days` advances (what `Tick` books).
        static int QuantaIn(double days)
            => (int)((days * TimeOfDay.WorkDaySeconds + 1e-3) / (OutpostLedger.QuantumDays * (double)TimeOfDay.WorkDaySeconds));

        // --- (m) tempo, Kevin's phone playtest 2026-09-24 -------------------
        static void Tempo(StringBuilder sb, ref int fails)
        {
            float day = TimeOfDay.WorkDaySeconds;
            double q = OutpostLedger.QuantumDays * (double)day;          // seconds a quantum
            var boards = Economy.Recipes.Named("boards");
            float jobSec = day * Mathf.Max(1, boards.yield) / boards.ratePerDay;

            // (1) One log on the bench: nothing at the last quantum before
            // 45 s, 3 boards (together, on the rack) at the first after.
            {
                var l = Sawmill(20, 0);
                var s = l.StationOf(BuildPlans.Sawmill.id);
                s.Bay(Res.Timber, true).whole = 1;
                // The bench runs only while he stands at it (2026-09-27):
                // this gate is the bench timer, so he starts there.
                l.hands[0].wHas = true; l.hands[0].wx = 15f; l.hands[0].wz = 0f;
                l.PlaceOrder(BuildPlans.Sawmill.id, "boards", 3);
                int before = Mathf.FloorToInt((float)(jobSec / q - 1e-4));
                l.Tick(before * q + 1e-3);
                int atBefore = l.CountOf(Res.Boards);
                float prog = s.benchProgress;
                l.Tick((before + 1) * q + 1e-3);
                // The count-3 order is done, so in the same quantum the
                // sawyer picks the rack up to carry it home: count the 3 on
                // the rack OR in his arms.
                int made = l.CountOf(Res.Boards) + l.CarriedOf(Res.Boards);
                Gate(sb, ref fails, "boards-3-a-log-15s",
                    boards.yield == 3 && Mathf.Abs(jobSec / boards.yield - 15f) < 1e-3f
                    && atBefore == 0 && made == 3 && s.benchState == BenchState.Empty && !s.HasOrder,
                    $"1 timber -> {boards.yield} boards, {jobSec / boards.yield:0.#} s a plank ({jobSec:0} s a job); "
                    + $"at {before * q:0.0} s {atBefore} boards (bench {prog:P0}), at {(before + 1) * q:0.0} s "
                    + $"{made} (rack {s.RackCount(Res.Boards)}, arms {l.CarriedOf(Res.Boards)}), bench {s.benchState}, "
                    + $"count-3 order done {!s.HasOrder}");
            }

            // (2) Rack 11/12 and the store full of boards: 1 goes on, 2 wait
            // on the bench (Finished), which blocks -- nothing lost.
            {
                var l = Sawmill(5, 0);
                l.Store(Res.Boards, true).whole = 5;
                var s = l.StationOf(BuildPlans.Sawmill.id);
                s.Rack(Res.Boards, true).whole = 11;
                s.Bay(Res.Timber, true).whole = 1;
                l.PlaceOrder(BuildPlans.Sawmill.id, "boards", OutpostLedger.RepeatOrder);
                double now = 0.0;
                Advance(l, ref now, 2.0 * jobSec / day);
                string why = l.StallReason(l.hands[0]) ?? "";
                Gate(sb, ref fails, "boards-short-rack-holds-rest",
                    s.RackTotal == 12 && s.benchState == BenchState.Finished && s.benchOut == 2
                    && l.CountOf(Res.Boards) == 5 + 11 + 3 && why.Contains("rack and store are full of boards"),
                    $"rack {s.RackTotal}/12, bench {s.benchState} holding {s.benchOut}, boards counted {l.CountOf(Res.Boards)} of 19, stall '{why}'");
            }

            // (3) A sawyer fetching from the store 15 m off: the load lands in
            // the first quantum that reaches the walked time
            // (2 x 15 x 1.15 / 2.6 + HandleSeconds), not up to 18 s later.
            {
                var l = Sawmill(20, 5);
                var s = l.StationOf(BuildPlans.Sawmill.id);
                l.PlaceOrder(BuildPlans.Sawmill.id, "boards", OutpostLedger.RepeatOrder);
                var h = l.hands[0];
                // Walked (2026-09-27): he starts at the store, stoops 1 s,
                // carries 15 m.
                float tripSec = OutpostLedger.HandleSeconds + 15f / OutpostLedger.WalkMetresPerSecond;
                double landedAt = -1;
                bool fetched = false;
                for (int i = 1; i <= 40 && landedAt < 0; i++)
                {
                    l.Tick(i * q + 1e-3);
                    if (h.Hauling && h.haulTo == HaulPlace.Station) fetched = true;
                    if (fetched && !h.Hauling && s.benchState != BenchState.Empty) landedAt = i * q;
                }
                Gate(sb, ref fails, "sawyer-fetch-lands-within-a-quantum",
                    fetched && OutpostLedger.HandleSeconds <= 1f
                    && landedAt >= tripSec - 1e-3 && landedAt <= tripSec + q + 1e-3,
                    $"store->sawmill 15 m, 2 logs: trip {tripSec:0.00} s (1 s to pick up), log on the bench at "
                    + $"{landedAt:0.0} s (quantum {q:0.0} s)");
            }

            // (4) Hunger shows: a sawyer at mood 0.175 (pace 0.35) says so;
            // a content one says nothing.
            {
                var l = Sawmill(20, 0);
                var s = l.StationOf(BuildPlans.Sawmill.id);
                s.Bay(Res.Timber, true).whole = 1;
                l.PlaceOrder(BuildPlans.Sawmill.id, "boards", OutpostLedger.RepeatOrder);
                var h = l.hands[0];
                string content = l.StallReason(h);
                h.mood = 0.175f;
                l.Store(Res.Food).whole = 0;
                string starving = l.StallReason(h) ?? "";
                float pace = l.WorkFactorOf(h);
                l.Store(Res.Food).whole = 1000;           // fed again, mood still low
                h.mood = 0.4f;
                string recovering = l.StallReason(h) ?? "";
                Gate(sb, ref fails, "slow-work-says-why",
                    content == null && Mathf.Abs(pace - OutpostLedger.StarvingWorkFloor) < 1e-4f
                    && starving == "working slowly — hungry (35% pace)"
                    && recovering == "working slowly — low spirits (80% pace)",
                    $"content '{content ?? "(nothing)"}', mood 0.175 unfed '{starving}', mood 0.4 fed '{recovering}'");
            }

            // (5) D2 on a busy camp over 30 days: two gatherers, a sawyer, a
            // hauler, a builder with a hut, food running short -- ticked in
            // 0.02-, 0.1- and 1-day calls and one 30-day call. Same books.
            {
                var a = Busy(); var b = Busy(); var c = Busy();
                double na = 0, nb = 0, nc = 0;
                for (int i = 0; i < 1500; i++) Advance(a, ref na, 0.02);
                for (int i = 0; i < 300; i++) Advance(b, ref nb, 0.1);
                for (int i = 0; i < 30; i++) Advance(c, ref nc, 1.0);
                var once = Busy(); double no = 0;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Advance(once, ref no, 30.0);
                watch.Stop();
                Gate(sb, ref fails, "d2-30-days-busy-camp",
                    SameBusy(a, b) && SameBusy(a, c) && SameBusy(a, once),
                    $"{BusyState(a)} | {BusyState(b)} | {BusyState(c)} | one call {BusyState(once)}; "
                    + $"the one 30-day call ran {QuantaIn(30.0)} steps in {watch.Elapsed.TotalMilliseconds:0.0} ms");
            }

            // (6) DIAGNOSTIC, not a gate: the same busy camp STEPPED (not
            // ticked) 30 days in 0.02-, 0.1- and 1-day steps. `Tick` only ever
            // steps `QuantumDays`, so this is what a coarse catch-up path for
            // unwatched camps WOULD do. It is reported, never counted: the
            // step size is not neutral (hands take turns inside a step, mood
            // is read at the step start and eating settles at its end, the
            // haul-or-build choice is per step), which is why no coarse path
            // exists.
            {
                var f = Busy(); var m = Busy(); var g = Busy();
                for (int i = 0; i < 1500; i++) f.StepForTest(0.02f);
                for (int i = 0; i < 300; i++) m.StepForTest(0.1f);
                for (int i = 0; i < 30; i++) g.StepForTest(1f);
                bool same = SameBusy(f, m) && SameBusy(f, g);
                sb.Append("  DIAG mixed-step-30d (not counted) -- ")
                  .Append(same ? "step size made no difference: " : "books DRIFT with step size: ")
                  .AppendLine($"0.02 d {BusyState(f)} | 0.1 d {BusyState(m)} | 1 d {BusyState(g)}");
            }
        }

        /// **The fisher at the water (2026-09-30).** A fishing hut 15 m off
        /// the store with its shore spot 6 m beyond it: catches land in the
        /// box one by one, the box never passes its capacity, and the box
        /// goes to the store whole (`Res.FishArmful`). Then a camp with no
        /// food in the store and fish in the box: the box counts as food,
        /// `BestMeal` sees it, and a hungry hand eats.
        static void Fishing(StringBuilder sb, ref int fails)
        {
            string hut = BuildPlans.FishingHut.id;
            var l = new OutpostLedger { ceilingPer = 1000, stationsMigrated = true, campfireLevel = 1 };
            l.SetCentre(Vector3.zero);
            l.raised.Add(new BuiltBuilding { planId = hut, x = 15f });
            l.built.Add(hut);
            l.hands.Add(new OutpostHand { name = "Fisher", order = OutpostOrder.Work, target = hut });
            l.Store(Res.Food, true).whole = 1000;
            l.lastTicked = 0.0;
            l.EnsureStations();
            int si = l.StationIndex(hut, 0);
            l.SetShore(si, true, new Vector3(21f, 0f, 0f), new Vector3(23f, 0f, 0f), "", new Vector3(15f, 0f, 0f), 0);
            bool ordered = l.PlaceOrder(hut, "fish", OutpostLedger.RepeatOrder);
            var st = l.StationAt(si);
            double now = l.lastTicked;
            int cap = st != null ? st.OutputCap : 0, maxBox = 0;
            // The smallest landing of fish in the store (a step's rise):
            // the box goes whole, so never below its capacity.
            int minLanding = int.MaxValue, prevStore = 0;
            bool fromShore = false;
            for (int i = 0; i < 120 && st != null; i++)
            {
                Advance(l, ref now, 0.05);
                maxBox = Mathf.Max(maxBox, st.RackTotal);
                var f = l.hands[0];
                if (f.Hauling && f.haulFrom == HaulPlace.Shore) fromShore = true;
                int inStore = l.StoreCountOf(Res.Fish);
                if (inStore > prevStore) minLanding = Mathf.Min(minLanding, inStore - prevStore);
                prevStore = inStore;
            }
            int made = l.CountOf(Res.Fish) + l.CarriedOf(Res.Fish);
            int stored = l.StoreCountOf(Res.Fish);
            Gate(sb, ref fails, "fish-caught-at-shore", ordered && fromShore && made >= 1,
                $"order {ordered}, catch trips from the shore {fromShore}, {made} fish in 6 days");
            Gate(sb, ref fails, "fish-box-capacity", st != null && maxBox <= cap,
                $"box peaked at {maxBox} of {cap}");
            Gate(sb, ref fails, "fish-box-whole-to-store",
                Res.Armful(Res.Fish) >= cap && (stored == 0 || minLanding >= cap),
                $"armful {Res.Armful(Res.Fish)}, {stored} in the store, smallest landing "
                + (minLanding == int.MaxValue ? "none" : minLanding.ToString()));

            var m = new OutpostLedger { ceilingPer = 1000, stationsMigrated = true, campfireLevel = 1 };
            m.SetCentre(Vector3.zero);
            m.raised.Add(new BuiltBuilding { planId = hut, x = 15f });
            m.built.Add(hut);
            m.hands.Add(new OutpostHand { name = "Hungry", order = OutpostOrder.Idle, full = 0f });
            m.lastTicked = 0.0;
            m.EnsureStations();
            var box = m.StationOf(hut);
            if (box != null) box.Rack(Res.Fish, true).whole = 3;
            float fill = m.FoodFill();
            string meal = m.BestMeal();
            // Supper (2026-10-02): nobody walks to the box any more; the
            // fish is served at the fire, straight off the box's rack.
            m.ServeSupperForTest(0);
            var hungry = m.hands[0];
            Gate(sb, ref fails, "fish-box-is-food",
                fill > 0f && meal == Res.Fish && hungry.lastMeal == Res.Fish && hungry.full > 0f,
                $"fill {fill:0.##} with an empty store, best meal {meal ?? "none"}, ate {hungry.lastMeal}, full {hungry.full:0.00}");
        }

        // --- (o) station spots, Kevin 2026-09-30 --------------------------------

        /// **Melvor-style spots.** (1) The forge's Smelter (iron) and Forge
        /// (spear) run AT ONCE under one smith, each at its full rate: their
        /// benches advance exactly as far as each alone would. (2) A grill
        /// with grilled fish selected runs its last fish, auto-pauses
        /// "waiting for fish", and resumes by itself when fish reaches the
        /// store. (3) A sawmill whose rack and store are full of boards
        /// pauses "store full of boards" without loading its timber, and
        /// resumes when the store has room.
        static void SpotGates(StringBuilder sb, ref int fails)
        {
            string forge = BuildPlans.Blacksmith.id;
            int smelter = StationSpots.IndexOf(forge, "Smelter"), anvil = StationSpots.IndexOf(forge, "Forge");

            // (1) two spots at once
            float Both(bool iron, bool spear, out float spearProg, out bool bothWorking)
            {
                var l = new OutpostLedger { ceilingPer = 1000, stationsMigrated = true, campfireLevel = 2 };
                l.SetCentre(Vector3.zero);
                l.raised.Add(new BuiltBuilding { planId = forge, x = 15f });
                l.built.Add(forge);
                l.hands.Add(new OutpostHand { name = "Smith", order = OutpostOrder.Work, target = forge,
                    wHas = true, wx = 15f, wz = 0f });
                l.Store(Res.Food, true).whole = 1000;
                l.lastTicked = 0.0;
                l.EnsureStations();
                var st = l.StationOf(forge);
                st.Bay(Res.Ore, true).whole = 2;
                st.Bay(Res.Boards, true).whole = 1;
                st.Bay(Res.Stone, true).whole = 1;
                if (iron) l.SelectRecipe(st, smelter, "iron", out _);
                if (spear) l.SelectRecipe(st, anvil, "spear", out _);
                double now = 0.0;
                bothWorking = false;
                for (int i = 0; i < 4; i++)
                {
                    Advance(l, ref now, 0.05);
                    if (st.Spots[smelter].BenchBusy && st.Spots[anvil].BenchBusy) bothWorking = true;
                }
                spearProg = st.Spots[anvil].progress01;
                return st.Spots[smelter].progress01;
            }
            float ironBoth = Both(true, true, out float spearBoth, out bool sawBoth);
            float ironAlone = Both(true, false, out _, out _);
            Both(false, true, out float spearAlone, out _);
            Gate(sb, ref fails, "spots-run-at-once-full-rate",
                smelter >= 0 && anvil >= 0 && sawBoth && ironBoth > 0.1f && spearBoth > 0.1f
                && Mathf.Abs(ironBoth - ironAlone) < 1e-4f && Mathf.Abs(spearBoth - spearAlone) < 1e-4f,
                $"after 0.2 d: smelter {ironBoth:P0} (alone {ironAlone:P0}), forge {spearBoth:P0} (alone {spearAlone:P0}), "
                + $"both working together {sawBoth}");

            // (2) waiting for an input, then resuming
            {
                string kitchen = BuildPlans.Kitchen.id;
                var l = new OutpostLedger { ceilingPer = 1000, stationsMigrated = true, campfireLevel = 1 };
                l.SetCentre(Vector3.zero);
                l.raised.Add(new BuiltBuilding { planId = kitchen, x = 15f });
                l.built.Add(kitchen);
                l.hands.Add(new OutpostHand { name = "Cook", order = OutpostOrder.Work, target = kitchen,
                    wHas = true, wx = 15f, wz = 0f });
                l.Store(Res.Food, true).whole = 1000;
                l.lastTicked = 0.0;
                l.EnsureStations();
                var st = l.StationOf(kitchen);
                int grill = StationSpots.IndexOf(kitchen, "Grill");
                st.Bay(Res.Fish, true).whole = 1;
                bool picked = l.SelectRecipe(st, grill, "grilled-fish", out string refusal);
                bool wrongSpot = !l.SelectRecipe(st, StationSpots.IndexOf(kitchen, "Cauldron"), "grilled-fish", out string wrongWhy);
                double now = 0.0;
                for (int i = 0; i < 20; i++) Advance(l, ref now, 0.05);
                var sp = st.Spots[grill];
                // (Counted by the fish used, not the dishes: the cook may eat one.)
                int fishLeft = st.BayCount(Res.Fish);
                string paused = sp.pauseReason ?? "";
                bool stillSelected = sp.Selected;
                l.Store(Res.Fish, true).whole = 3;
                bool resumed = false;
                for (int i = 0; i < 40; i++)
                {
                    Advance(l, ref now, 0.05);
                    if (sp.Running && sp.BenchBusy) resumed = true;
                }
                int fishUsed = 3 - l.StoreCountOf(Res.Fish) - st.BayCount(Res.Fish) - l.CarriedOf(Res.Fish);
                Gate(sb, ref fails, "spot-pauses-on-input-and-resumes",
                    picked && wrongSpot && fishLeft == 0 && stillSelected && paused.StartsWith("waiting for")
                    && resumed && fishUsed > 0,
                    $"select {picked} ({refusal ?? "ok"}), cauldron refused '{wrongWhy}', bay fish {fishLeft} then "
                    + $"'{paused}', fish in the store -> resumed {resumed}, {fishUsed} more used");
            }

            // (3) the store full: paused without loading, resumes with room
            {
                var l = Sawmill(5, 0);
                l.Store(Res.Boards, true).whole = 5;
                var st = l.StationOf(BuildPlans.Sawmill.id);
                st.Rack(Res.Boards, true).whole = st.OutputCap;
                st.Bay(Res.Timber, true).whole = 3;
                l.hands[0].wHas = true; l.hands[0].wx = 15f; l.hands[0].wz = 0f;
                l.SelectRecipe(st, 0, "boards", out _);
                double now = 0.0;
                for (int i = 0; i < 10; i++) Advance(l, ref now, 0.05);
                var sp = st.Spots[0];
                string paused = sp.pauseReason ?? "";
                int timberWhilePaused = st.BayCount(Res.Timber);
                bool idleBench = sp.benchState == BenchState.Empty;
                l.ceilingPer = 100;
                for (int i = 0; i < 20; i++) Advance(l, ref now, 0.05);
                int timberAfter = st.BayCount(Res.Timber);
                Gate(sb, ref fails, "spot-pauses-on-full-store-and-resumes",
                    paused.StartsWith("store full of") && timberWhilePaused == 3 && idleBench && timberAfter < 3,
                    $"'{paused}', timber kept {timberWhilePaused}/3, bench empty {idleBench}; store room -> timber {timberAfter}");
            }
        }

        /// A sawmill 15 m from the store (the fire square at 0,0), one
        /// sawyer, fed, no timber standing -- the test sets the rest.
        static OutpostLedger Sawmill(int ceiling, int storeTimber)
        {
            var l = new OutpostLedger { ceilingPer = ceiling, stationsMigrated = true };
            l.SetCentre(Vector3.zero);
            l.raised.Add(new BuiltBuilding { planId = BuildPlans.Sawmill.id, x = 15f });
            l.built.Add(BuildPlans.Sawmill.id);
            l.hands.Add(new OutpostHand { name = "Sawyer", order = OutpostOrder.Work, target = BuildPlans.Sawmill.id });
            l.Store(Res.Food, true).whole = 1000;
            l.Store(Res.Timber, true).whole = storeTimber;
            l.lastTicked = 0.0;
            l.EnsureStations();
            return l;
        }

        static OutpostLedger Busy()
        {
            var l = Sawmill(20, 4);
            l.SetSourceMetres(Res.Timber, 20f);
            l.SetSourceMetres(Res.Stone, 20f);
            l.AddStanding(Res.Timber, 60f, 0.02f);
            l.AddStanding(Res.Stone, 30f).regrowPerDay = 0f;
            l.Store(Res.Food).whole = 120;          // runs short: hunger in the mix
            for (int i = 0; i < 2; i++)
                l.hands.Add(new OutpostHand { name = "Cutter" + i, order = OutpostOrder.Gather, target = Res.Timber });
            l.hands.Add(new OutpostHand { name = "Hauler", order = OutpostOrder.Idle });
            l.hands.Add(new OutpostHand { name = "Builder", order = OutpostOrder.Build });
            l.sites.Add(new PendingBuild
            {
                planId = BuildPlans.Hut.id, x = 8f, z = -6f, needed = 6, stoneNeeded = 2, phased = true,
            });
            l.PlaceOrder(BuildPlans.Sawmill.id, "boards", OutpostLedger.RepeatOrder);
            return l;
        }

        static bool SameBusy(OutpostLedger a, OutpostLedger b)
        {
            foreach (var r in new[] { Res.Timber, Res.Boards, Res.Stone, Res.Food })
                if (Mathf.Abs(a.CountOf(r) + a.CarriedOf(r) - b.CountOf(r) - b.CarriedOf(r)) > 1) return false;
            return a.sites.Count == b.sites.Count && Mathf.Abs(a.Wood.standing - b.Wood.standing) < 1f
                && Mathf.Abs(a.hands[1].mood - b.hands[1].mood) < 1e-3f;
        }

        static string BusyState(OutpostLedger l)
            => $"timber {l.CountOf(Res.Timber) + l.CarriedOf(Res.Timber)} boards {l.CountOf(Res.Boards) + l.CarriedOf(Res.Boards)} "
             + $"stone {l.CountOf(Res.Stone) + l.CarriedOf(Res.Stone)} food {l.CountOf(Res.Food)} sites {l.sites.Count} "
             + $"standing {l.Wood.standing:0.#} mood {l.hands[1].mood:0.00}";

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
            now += days * TimeOfDay.WorkDaySeconds;
            l.Tick(now + 1e-3);
        }

        static int Bricks(OutpostLedger l) => l.CountOf(Res.Brick) + l.CarriedOf(Res.Brick);

        /// Stone anywhere + bricks anywhere (one stone per brick).
        static int StoneIn(OutpostLedger l)
        {
            int n = l.CountOf(Res.Stone) + l.CarriedOf(Res.Stone) + Bricks(l);
            foreach (var s in l.Stations)
                foreach (var sp in s.Spots)          // every spot's bench (2026-09-30)
                {
                    if (!sp.BenchBusy) continue;
                    var r = sp.BenchRecipe;
                    if (r == null) continue;
                    foreach (var line in r.takes) if (line.res == Res.Stone) n += line.n;
                }
            return n;
        }

        // --- (n) ship <-> store transfers ------------------------------------------

        /// The ship's end for the self-test: a hold with a capacity, a
        /// gangway 30 m east of the store, and a switch for "she is here".
        sealed class FakeCargo : ICargoSide
        {
            public bool present = true;
            public int capacity = 100;
            public Vector3 gangway = new Vector3(30f, 0f, 0f);
            public readonly System.Collections.Generic.Dictionary<string, int> held =
                new System.Collections.Generic.Dictionary<string, int>();

            public int Total { get { int n = 0; foreach (var kv in held) n += kv.Value; return n; } }
            public bool Present => present;
            public int Room => Mathf.Max(0, capacity - Total);
            public int HeldOf(string res) => res != null && held.TryGetValue(res, out int n) ? n : 0;
            public int Take(string res, int n)
            {
                int got = Mathf.Min(HeldOf(res), Mathf.Max(0, n));
                if (got > 0) held[res] = HeldOf(res) - got;
                return got;
            }
            public int Give(string res, int n)
            {
                n = Mathf.Min(Mathf.Max(0, n), Room);
                if (n > 0) held[res] = HeldOf(res) + n;
                return n;
            }
            public bool GangwayAt(out Vector3 at) { at = gangway; return true; }
        }

        /// A fed camp with its store at 0,0, `porters` idle hands, and the
        /// fake ship alongside.
        static OutpostLedger Porters(int ceiling, int porters, out FakeCargo ship)
        {
            var l = new OutpostLedger { ceilingPer = ceiling, stationsMigrated = true };
            l.SetCentre(Vector3.zero);
            for (int i = 0; i < porters; i++)
                l.hands.Add(new OutpostHand { name = "Porter" + i, order = OutpostOrder.Idle });
            l.Store(Res.Food, true).whole = 1000;
            l.lastTicked = 0.0;
            ship = new FakeCargo();
            l.cargo = ship;
            return l;
        }

        static int Ashore(OutpostLedger l, string res) => l.StoreCountOf(res);

        static void Transfers(StringBuilder sb, ref int fails)
        {
            string T = Res.Timber, S = Res.Stone;

            // Refused while she is away.
            {
                var l = Porters(20, 1, out var ship);
                l.Store(T, true).whole = 10;
                ship.present = false;
                bool refused = !l.OrderTransfer(T, 5, true);
                Gate(sb, ref fails, "transfer-refused-when-away", refused && !l.TransferPending(T, true),
                    refused ? "no order without the ship" : "an order was placed with her away");
            }

            // 7 timber store -> ship, across tick sizes: 7 aboard, 0 lost.
            {
                double[] ragged = { 0.03, 0.31, 0.02, 0.4, 0.05, 1.19 };
                string detail = "";
                bool ok = true;
                for (int run = 0; run < 3; run++)
                {
                    var l = Porters(20, 1, out var ship);
                    l.Store(T, true).whole = 10;
                    bool placed = l.OrderTransfer(T, 7, true);
                    double now = l.lastTicked;
                    if (run == 0) for (int i = 0; i < 100; i++) Advance(l, ref now, 0.02);
                    else if (run == 1) for (int i = 0; i < 2; i++) Advance(l, ref now, 1.0);
                    else foreach (var st in ragged) Advance(l, ref now, st);
                    bool good = placed && ship.HeldOf(T) == 7 && Ashore(l, T) == 3
                                && l.CarriedOf(T) == 0 && !l.TransferPending(T, true)
                                && l.transferredAboard == 7;
                    ok &= good;
                    detail += $"[{(run == 0 ? "0.02d" : run == 1 ? "1d" : "ragged")}: aboard {ship.HeldOf(T)} "
                              + $"ashore {Ashore(l, T)} arms {l.CarriedOf(T)} pending {l.TransferPending(T, true)}] ";
                }
                Gate(sb, ref fails, "transfer-7-timber-aboard-d2", ok, detail);
            }

            // A trip is the walked distance store <-> gangway.
            {
                var l = Porters(20, 1, out var ship);
                l.Store(T, true).whole = 10;
                l.OrderTransfer(T, 7, true);
                double now = l.lastTicked;
                Advance(l, ref now, 0.02);
                var h = l.hands[0];
                // One quantum: stoop 1 s at the store, pick up, walk toward
                // the gangway 30 m off -- the rest of the quantum's metres.
                float qs = OutpostLedger.QuantumDays * TimeOfDay.WorkDaySeconds;
                float wantLeft = 30f - OutpostLedger.WalkMetresPerSecond * (qs - OutpostLedger.HandleSeconds);
                bool ok = h.Hauling && h.haulTo == HaulPlace.Ship && h.haulCount == 2 && h.haulPicked
                          && Mathf.Abs(h.legLeft - wantLeft) < 0.05f && Mathf.Abs(h.haulToX - 30f) < 1e-3f;
                Gate(sb, ref fails, "transfer-trip-is-walked", ok,
                    $"armful {h.haulCount} to {h.haulTo}, picked {h.haulPicked}, {h.legLeft:0.00} m left (want {wantLeft:0.00}), "
                    + $"drop at x {h.haulToX:0.#}");
            }

            // 5 stone ship -> store.
            {
                var l = Porters(20, 1, out var ship);
                ship.held[S] = 5;
                bool placed = l.OrderTransfer(S, 5, false);
                double now = l.lastTicked;
                Advance(l, ref now, 2.0);
                Gate(sb, ref fails, "transfer-5-stone-ashore",
                    placed && Ashore(l, S) == 5 && ship.HeldOf(S) == 0 && l.CarriedOf(S) == 0
                    && l.transferredAshore == 5 && !l.TransferPending(S, false),
                    $"ashore {Ashore(l, S)} aboard {ship.HeldOf(S)} arms {l.CarriedOf(S)} landed {l.transferredAshore}");
            }

            // A full store waits, says so, and resumes.
            {
                var l = Porters(4, 1, out var ship);
                ship.held[S] = 10;
                l.OrderTransfer(S, OutpostLedger.TransferAll, false);
                double now = l.lastTicked;
                Advance(l, ref now, 2.0);
                string why = l.TransferStall(S, false);
                string line = l.TransferSummary();
                bool waited = Ashore(l, S) == 4 && ship.HeldOf(S) == 6 && l.CarriedOf(S) == 0
                              && why == OutpostLedger.StallStoreFull && l.TransferPending(S, false)
                              && line.Contains(OutpostLedger.StallStoreFull);
                l.ceilingPer = 10;
                Advance(l, ref now, 2.0);
                bool resumed = Ashore(l, S) == 10 && ship.HeldOf(S) == 0 && l.CarriedOf(S) == 0
                               && !l.TransferPending(S, false);
                Gate(sb, ref fails, "transfer-store-full-waits", waited && resumed,
                    $"waited {waited} (stall '{why}', line '{line}'), after room: ashore {Ashore(l, S)} aboard {ship.HeldOf(S)}");
            }

            // A full hold waits, says so, and resumes.
            {
                var l = Porters(20, 1, out var ship);
                ship.capacity = 3;
                l.Store(T, true).whole = 10;
                l.OrderTransfer(T, OutpostLedger.TransferAll, true);
                double now = l.lastTicked;
                Advance(l, ref now, 2.0);
                string why = l.TransferStall(T, true);
                bool waited = ship.HeldOf(T) == 3 && Ashore(l, T) == 7 && l.CarriedOf(T) == 0
                              && why == OutpostLedger.StallHoldFull && l.TransferPending(T, true);
                ship.capacity = 10;
                // Three days, not two (2026-10-02): seven more timber is four
                // walked trips at the off-screen 0.75 m/s, and with supper's
                // shorter work day (evening from 21:00) two days left the
                // last armful on the plank -- timing, not the wait/resume
                // this gate is about.
                Advance(l, ref now, 3.0);
                bool resumed = ship.HeldOf(T) == 10 && Ashore(l, T) == 0 && l.CarriedOf(T) == 0
                               && !l.TransferPending(T, true);
                Gate(sb, ref fails, "transfer-hold-full-waits", waited && resumed,
                    $"waited {waited} (stall '{why}'), after room: aboard {ship.HeldOf(T)} ashore {Ashore(l, T)}");
            }

            // Cancel mid-trip: what is in arms lands, nothing more starts.
            {
                var l = Porters(20, 1, out var ship);
                l.Store(T, true).whole = 10;
                l.OrderTransfer(T, 10, true);
                double now = l.lastTicked;
                Advance(l, ref now, 0.2);   // 36 s: one armful down, the next in arms
                int landed = ship.HeldOf(T), inArms = l.CarryingTransfer(T, true);
                l.CancelTransfers();
                Advance(l, ref now, 2.0);
                bool ok = inArms > 0 && ship.HeldOf(T) == landed + inArms
                          && ship.HeldOf(T) + Ashore(l, T) == 10 && l.CarriedOf(T) == 0
                          && !l.TransferPending(T, true);
                Gate(sb, ref fails, "transfer-cancel-mid-trip-conserves", ok,
                    $"at cancel {landed} aboard + {inArms} in arms; after: aboard {ship.HeldOf(T)} ashore {Ashore(l, T)} arms {l.CarriedOf(T)}");
            }

            // She casts off mid-trip (store -> ship): the armful goes home,
            // the order pauses with its count restored, and resumes.
            {
                var l = Porters(20, 1, out var ship);
                l.Store(T, true).whole = 10;
                l.OrderTransfer(T, 10, true);
                double now = l.lastTicked;
                Advance(l, ref now, 0.2);
                int inArms = l.CarryingTransfer(T, true);
                ship.present = false;
                Advance(l, ref now, 1.0);
                int aboard = ship.HeldOf(T);
                int leftWhileAway = l.TransferLeft(T, true);
                bool paused = inArms > 0 && aboard + Ashore(l, T) == 10 && l.CarriedOf(T) == 0
                              && l.TransferPending(T, true) && leftWhileAway == 10 - aboard
                              && l.TransferStall(T, true) == OutpostLedger.StallAway;
                ship.present = true;
                Advance(l, ref now, 2.0);
                bool resumed = ship.HeldOf(T) == 10 && Ashore(l, T) == 0 && l.CarriedOf(T) == 0;
                Gate(sb, ref fails, "transfer-ship-leaves-to-ship-conserves", paused && resumed,
                    $"{inArms} in arms when she left; then aboard {aboard} + ashore {Ashore(l, T)}, left {leftWhileAway}; "
                    + $"back alongside: aboard {ship.HeldOf(T)}");
            }

            // She casts off mid-trip (ship -> store). Before he has picked
            // the armful up at the gangway, nothing has left her (the order
            // keeps its count); once it is in his arms, it lands in the store.
            {
                var l = Porters(20, 1, out var ship);
                ship.held[S] = 9;
                l.OrderTransfer(S, 9, false);
                double now = l.lastTicked;
                Advance(l, ref now, 0.02);                 // still walking out to her
                bool planned = l.hands[0].Hauling && !l.hands[0].haulPicked;
                ship.present = false;
                Advance(l, ref now, 2.0);
                bool before = planned && ship.HeldOf(S) == 9 && Ashore(l, S) == 0 && l.CarriedOf(S) == 0
                              && l.TransferPending(S, false);

                var m = Porters(20, 1, out var ship2);
                ship2.held[S] = 9;
                m.OrderTransfer(S, 9, false);
                double nm = m.lastTicked;
                for (int i = 0; i < 20 && !m.hands[0].haulPicked; i++) Advance(m, ref nm, 0.02);
                int inArms = m.CarriedOf(S);
                ship2.present = false;
                Advance(m, ref nm, 2.0);
                bool after = inArms > 0 && Ashore(m, S) == inArms && ship2.HeldOf(S) == 9 - inArms
                             && m.CarriedOf(S) == 0 && m.TransferPending(S, false);
                Gate(sb, ref fails, "transfer-ship-leaves-to-store-conserves", before && after,
                    $"left before pickup: aboard {ship.HeldOf(S)} ashore {Ashore(l, S)}; "
                    + $"left with {inArms} in arms: ashore {Ashore(m, S)} aboard {ship2.HeldOf(S)} arms {m.CarriedOf(S)}");
            }

            // Re-ordered mid-trip: to a farm (not a transfer carrier) the
            // armful lands where it was going at once; to Build he walks it
            // on and keeps carrying (a builder with nothing to build).
            {
                var l = Porters(20, 1, out var ship);
                l.Store(T, true).whole = 10;
                l.OrderTransfer(T, 4, true);
                double now = l.lastTicked;
                Advance(l, ref now, 0.02);
                int inArms = l.CarryingTransfer(T, true);
                l.hands[0].order = OutpostOrder.Work;
                l.hands[0].target = BuildPlans.Farm.id;
                // (2026-09-27) Re-ordered, he still WALKS it there: nothing
                // lands untouched, then it lands aboard.
                Advance(l, ref now, 0.02);
                bool walking = l.CarriedOf(T) == inArms && ship.HeldOf(T) == 0;
                Advance(l, ref now, 0.2);
                bool landedNow = inArms > 0 && walking && ship.HeldOf(T) == inArms && l.CarriedOf(T) == 0
                                 && ship.HeldOf(T) + Ashore(l, T) == 10;

                var b = Porters(20, 1, out var ship2);
                b.Store(T, true).whole = 10;
                b.OrderTransfer(T, 4, true);
                double nb = b.lastTicked;
                Advance(b, ref nb, 0.02);
                b.hands[0].order = OutpostOrder.Build;
                Advance(b, ref nb, 0.02);
                bool stillCarrying = b.CarryingTransfer(T, true) > 0 && ship2.HeldOf(T) == 0;
                Advance(b, ref nb, 2.0);
                bool builderDone = ship2.HeldOf(T) == 4 && Ashore(b, T) == 6 && b.CarriedOf(T) == 0;
                Gate(sb, ref fails, "transfer-reordered-mid-trip-conserves", landedNow && stillCarrying && builderDone,
                    $"to farm: {inArms} in arms, walked on, -> aboard {ship.HeldOf(T)}; to build: still carrying {stillCarrying}, "
                    + $"then aboard {ship2.HeldOf(T)} ashore {Ashore(b, T)}");
            }
        }

        // --- (m) stone deposits, 2026-09-24 ------------------------------------
        // The seam sized once to the kit rocks on the island
        // (`SizeStoneToDeposits`), never lowered; a trip's uncut units still in
        // the rock; a store's booked-out units still on the pile.
        static void Deposits(StringBuilder sb, ref int fails)
        {
            // Kevin's Island_2 camp: 0.9 standing of a 10.4 seam, rocks worth 27.
            var k = new OutpostLedger();
            var ks = k.Stock(Res.Stone, true);
            ks.standing = 0.9f; ks.standingMax = 10.4f;
            bool changed = k.SizeStoneToDeposits(27f);
            Gate(sb, ref fails, "deposits-size-near-empty-seam",
                changed && ks.standing == 27f && ks.standingMax == 27f && k.stoneDepositsV == OutpostLedger.StoneDepositsVersion,
                $"0.9/10.4 -> {ks.standing:0.#}/{ks.standingMax:0.#}, v{k.stoneDepositsV}");

            bool again = k.SizeStoneToDeposits(40f);
            Gate(sb, ref fails, "deposits-size-once", !again && ks.standing == 27f && ks.standingMax == 27f,
                $"second call {(again ? "changed" : "left")} it at {ks.standing:0.#}/{ks.standingMax:0.#}");

            var copy = JsonUtility.FromJson<OutpostLedger>(JsonUtility.ToJson(k));
            float cs = copy.Stock(Res.Stone).standing;
            bool afterLoad = copy.SizeStoneToDeposits(40f);
            Gate(sb, ref fails, "deposits-size-once-across-a-save",
                copy.stoneDepositsV == OutpostLedger.StoneDepositsVersion && !afterLoad && copy.Stock(Res.Stone).standing == cs,
                $"loaded v{copy.stoneDepositsV}, re-size {(afterLoad ? "changed" : "left")} it at {copy.Stock(Res.Stone).standing:0.#}");

            var rich = new OutpostLedger();
            var rs = rich.Stock(Res.Stone, true);
            rs.standing = 50f; rs.standingMax = 60f;
            bool richChanged = rich.SizeStoneToDeposits(27f);
            Gate(sb, ref fails, "deposits-never-lower-stone",
                !richChanged && rs.standing == 50f && rs.standingMax == 60f && rich.stoneDepositsV == OutpostLedger.StoneDepositsVersion,
                $"50/60 with rocks worth 27 -> {rs.standing:0.#}/{rs.standingMax:0.#}");

            var early = new OutpostLedger();
            var es = early.Stock(Res.Stone, true);
            es.standing = es.standingMax = 10.4f;
            bool none = early.SizeStoneToDeposits(0f);
            bool later = early.SizeStoneToDeposits(33f);
            Gate(sb, ref fails, "deposits-wait-for-rocks",
                !none && later && es.standing == 33f && es.standingMax == 33f,
                $"no rocks yet: {(none ? "sized" : "waited")}; then 10.4 -> {es.standing:0.#}/{es.standingMax:0.#}");

            var bare = new OutpostLedger();
            bool noStock = bare.SizeStoneToDeposits(20f);
            Gate(sb, ref fails, "deposits-no-seam-no-stone", !noStock && bare.Stock(Res.Stone) == null && bare.stoneDepositsV == 0,
                "a camp with no Stone stock gets none from rocks");

            // Since 2026-09-27 the island and the store give a load up only
            // at the PICKUP, so the "booked but not lifted" corrections are
            // gone: the stock already is what stands / what is on the pile.
            var g = new OutpostLedger();
            g.AddStanding(Res.Stone, 5f).regrowPerDay = 0f;
            g.hands.Add(new OutpostHand { name = "Gatherer", order = OutpostOrder.Gather, target = Res.Stone,
                haulRes = Res.Stone, haulCount = 3, haulFrom = HaulPlace.Field, haulTo = HaulPlace.Store,
                tripLeg = (int)TripLeg.ToPickup });
            Gate(sb, ref fails, "deposits-uncut-still-in-the-rock",
                g.UncutFromField(Res.Stone) == 0 && Mathf.Abs(g.Stock(Res.Stone).standing - 5f) < 1e-4f,
                $"walking out for 3: standing {g.Stock(Res.Stone).standing:0.#} (untouched), uncut correction {g.UncutFromField(Res.Stone)}");

            var b = new OutpostLedger();
            b.Store(Res.Stone, true).whole = 6;
            b.hands.Add(new OutpostHand { name = "Builder", order = OutpostOrder.Build,
                haulRes = Res.Stone, haulCount = 4, haulFrom = HaulPlace.Store, haulTo = HaulPlace.Site,
                tripLeg = (int)TripLeg.ToPickup });
            Gate(sb, ref fails, "deposits-planned-stays-on-pile", b.OnStorePile(Res.Stone) == 6 && b.StoreCountOf(Res.Stone) == 6,
                $"planned, not lifted: {b.OnStorePile(Res.Stone)} on the pile (store {b.StoreCountOf(Res.Stone)})");
        }

        // --- (o) delivery on arrival (2026-09-27, docs/DELIVERY-ON-ARRIVAL.md) ---

        /// **Nothing is counted before its drop-off; a watched camp and an
        /// unwatched one land on the same order of goods.**
        ///
        /// (1) A busy camp (cutters, a sawyer, a hauler, a builder) stepped a
        ///     quantum at a time for 3 days: in no step does the store GROW
        ///     (summed over resources) by more than the units dropped off at
        ///     it in that step (`deliveredEvents`), and the island's timber
        ///     never shrinks by more than the units picked up.
        /// (2) Watched vs unwatched: two copies of a two-cutter camp for one
        ///     day. One is ticked alone (the invisible walkers); the other has
        ///     its hands DRIVEN by a stand-in body that walks each leg at
        ///     2.6 m/s in 0.1 s frames and reports `BodyAt` / `BodyArrived` /
        ///     `BodyWorked`, ticked every frame. Timber in store within 20 %
        ///     (or 4 logs) of each other.
        static void Delivery(StringBuilder sb, ref int fails)
        {
            float day = TimeOfDay.WorkDaySeconds, qs = OutpostLedger.QuantumDays * day;
            {
                var l = Busy();
                string[] res = { Res.Timber, Res.Boards, Res.Stone, Res.Food };
                bool ok = true;
                string why = "";
                int steps = Mathf.RoundToInt(3f / OutpostLedger.QuantumDays);
                for (int i = 1; i <= steps && ok; i++)
                {
                    int grew = 0;
                    int[] before = new int[res.Length];
                    for (int k = 0; k < res.Length; k++) before[k] = l.StoreCountOf(res[k]);
                    int ev = l.deliveredEvents;
                    l.Tick(i * qs + 1e-3);
                    for (int k = 0; k < res.Length; k++) grew += Mathf.Max(0, l.StoreCountOf(res[k]) - before[k]);
                    int dropped = l.deliveredEvents - ev;
                    if (grew > dropped) { ok = false; why = $"step {i}: store grew {grew}, dropped off {dropped}"; }
                }
                Gate(sb, ref fails, "store-grows-only-at-dropoff", ok,
                    ok ? $"{steps} steps, {l.deliveredEvents} units dropped off, no unit counted early" : why);
            }
            {
                var a = Gatherers(2, 40);
                var b = Gatherers(2, 40);
                double t = 0;
                for (int i = 1; i <= Mathf.RoundToInt(1f / OutpostLedger.QuantumDays); i++) a.Tick(i * qs + 1e-3);
                const float frame = 0.1f;
                int frames = Mathf.RoundToInt(day / frame);
                var pos = new Vector3[b.hands.Count];
                for (int f = 1; f <= frames; f++)
                {
                    for (int k = 0; k < b.hands.Count; k++)
                    {
                        var h = b.hands[k];
                        b.BodyAt(h, pos[k]);
                        var v = b.HaulOf(h);
                        if (!v.active) continue;
                        if (v.leg == TripLeg.ToPickup || v.leg == TripLeg.ToDrop)
                        {
                            Vector3 goal = v.leg == TripLeg.ToPickup ? v.fromAt : v.toAt;
                            Vector3 d = goal - pos[k]; d.y = 0f;
                            float stepM = OutpostLedger.WalkMetresPerSecond * frame;
                            if (d.magnitude <= stepM) { pos[k] = goal; b.BodyAt(h, pos[k]); b.BodyArrived(h); }
                            else pos[k] += d.normalized * stepM;
                        }
                        else if (v.leg == TripLeg.AtPickup) b.BodyWorked(h, frame);
                    }
                    t = f * frame;
                    b.Tick(t + 1e-3);
                }
                int ta = a.StoreCountOf(Res.Timber), tb = b.StoreCountOf(Res.Timber);
                bool close = Mathf.Abs(ta - tb) <= Mathf.Max(4, 0.2f * Mathf.Max(ta, tb)) && ta > 0 && tb > 0;
                Gate(sb, ref fails, "watched-vs-unwatched-one-day", close,
                    $"one day, two cutters 20 m out: unwatched {ta} logs in store (standing {a.Stock(Res.Timber).standing:0}), "
                    + $"watched (driven bodies) {tb} (standing {b.Stock(Res.Timber).standing:0})");
            }
        }

        static void Gate(StringBuilder sb, ref int fails, string name, bool ok, string detail)
        {
            if (!ok) fails++;
            sb.Append(ok ? "  PASS " : "  FAIL ").Append(name).Append(" -- ").AppendLine(detail);
        }
    }
}
