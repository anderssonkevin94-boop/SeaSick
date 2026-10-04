using System.Text;
using UnityEngine;

namespace SeaSick.World
{
    /// **Gate for the flour auto-pause (Kevin, 2026-10-04).** Plain C#, no
    /// scene:
    ///
    ///   tools/selftest-outside-editor/run.sh SeaSick.World.FlourPauseSelfTest.Run
    ///   unity cmd eval --json --code 'return SeaSick.World.FlourPauseSelfTest.Run();'
    ///
    /// A camp with a Mill (flour selected, one miller) and a Kitchen, fire
    /// II, 100 wheat in the store, no runners. (a) 6 flour + Kitchen I, 4
    /// wheat in the bay: held -- no batch loads, no more wheat is fetched
    /// into the bay, the spot and the
    /// miller say "mill paused · nothing bakes flour yet", also over one
    /// 5-day tick (the time-away catch-up goes through the same door);
    /// (b) 5 flour: runs; (c) Kitchen II standing (bread available) with 583
    /// flour: runs; (d) a batch already on the bench when the hold starts
    /// finishes and the next one is held; (e) resumes by itself when the
    /// store drops below 6, and when the Kitchen is raised to II; (f) the
    /// miller's assignment never changes. Returns "PASS n/n" or the failures.
    public static class FlourPauseSelfTest
    {
        public static string Run()
        {
            float hour = OutpostLedger.ActiveHour;
            bool hourKnown = OutpostLedger.ActiveHourKnown;
            // Awake at scale 1 from second 0, as `StationStockSelfTest` runs.
            Life.CampLifeTuning.OverrideForTest(true);
            try { return Gates(); }
            finally
            {
                Life.CampLifeTuning.OverrideForTest(false);
                OutpostLedger.ActiveHour = hour;
                OutpostLedger.ActiveHourKnown = hourKnown;
            }
        }

        static string Gates()
        {
            var sb = new StringBuilder();
            int pass = 0, total = 0;
            void Check(string name, bool ok, string detail = "")
            {
                total++;
                if (ok) { pass++; return; }
                sb.Append("FAIL ").Append(name).Append(detail.Length > 0 ? ": " + detail : "").Append('\n');
            }
            string mill = BuildPlans.Mill.id;

            // ---- (a) 6 flour, Kitchen I: held --------------------------------
            {
                var l = Camp(6, 1, out var st, out var sp, out var miller);
                // Two batches' wheat already in the bay: only the hold itself
                // (`TryLoadSpot`) can keep the bench empty.
                st.Bay(Res.Wheat, true).whole = 4;
                Check("(a) nothing can use flour with a Kitchen I", !l.FlourHasUse());
                Check("(a) HoldOf(flour) names the hold",
                      l.HoldOf(Economy.Recipes.Named("flour")) == OutpostLedger.FlourHoldWords,
                      "got '" + l.HoldOf(Economy.Recipes.Named("flour")) + "'");
                double now = 0.0;
                bool everLoaded = false;
                for (int i = 0; i < 20; i++) { Advance(l, ref now, 0.05); if (sp.BenchBusy) everLoaded = true; }
                Check("(a) no batch loads in 1 day", !everLoaded && sp.benchState == BenchState.Empty,
                      "bench " + sp.benchState);
                Check("(a) flour stays 6", Flour(l) == 6, "flour " + Flour(l));
                Check("(a) no wheat used, none fetched into the held bay",
                      st.BayCount(Res.Wheat) == 4 && l.StoreCountOf(Res.Wheat) == 100 && l.CarriedOf(Res.Wheat) == 0,
                      $"bay {st.BayCount(Res.Wheat)}, store {l.StoreCountOf(Res.Wheat)}, carried {l.CarriedOf(Res.Wheat)}");
                Check("(a) the spot's pause reason is the hold",
                      sp.pauseReason == OutpostLedger.FlourHoldWords, "got '" + sp.pauseReason + "'");
                Check("(a) the spot is still selected", sp.Selected && sp.recipeId == "flour");
                Check("(a) the miller's line is the hold",
                      l.StallReason(miller) == OutpostLedger.FlourHoldWords, "got '" + l.StallReason(miller) + "'");
                // One coarse 5-day tick: the catch-up's door is the same.
                Advance(l, ref now, 5.0);
                Check("(a) one 5-day tick: still held", Flour(l) == 6 && sp.benchState == BenchState.Empty,
                      $"flour {Flour(l)}, bench {sp.benchState}");
                Check("(f) the miller stays posted", Posted(l, miller, st), Assignment(miller));

                // ---- (e) resumes by itself when the store drops below 6 -------
                l.Store(Res.Flour, true).whole = 5;
                bool resumed = false;
                for (int i = 0; i < 10 && !resumed; i++) { Advance(l, ref now, 0.05); if (sp.BenchBusy) resumed = true; }
                Check("(e) flour 6 -> 5: the mill loads again by itself", resumed, "bench " + sp.benchState);
                Check("(f) the miller stays posted after resuming", Posted(l, miller, st), Assignment(miller));
            }

            // ---- (b) 5 flour: runs -------------------------------------------
            {
                var l = Camp(5, 1, out var st, out var sp, out var miller);
                Check("(b) 5 flour: no hold", l.HoldOf(Economy.Recipes.Named("flour")) == null);
                double now = 0.0;
                bool loaded = false;
                for (int i = 0; i < 10 && !loaded; i++) { Advance(l, ref now, 0.05); if (sp.BenchBusy) loaded = true; }
                Check("(b) 5 flour: a batch loads", loaded, $"bench {sp.benchState}, reason '{sp.pauseReason}'");
            }

            // ---- (c) Kitchen II standing, bread available: runs ----------------
            {
                var l = Camp(583, 2, out var st, out var sp, out var miller);
                Check("(c) Kitchen II + fire II: flour has a use", l.FlourHasUse());
                Check("(c) bread is available", l.RecipeAvailable(Economy.Recipes.Named("bread"), out string why), why ?? "");
                double now = 0.0;
                bool loaded = false;
                for (int i = 0; i < 10 && !loaded; i++) { Advance(l, ref now, 0.05); if (sp.BenchBusy) loaded = true; }
                Check("(c) 583 flour + Kitchen II: a batch loads", loaded, $"bench {sp.benchState}, reason '{sp.pauseReason}'");
            }

            // ---- (d) a running batch is not cancelled --------------------------
            {
                var l = Camp(0, 1, out var st, out var sp, out var miller);
                double now = 0.0;
                for (int i = 0; i < 20 && !sp.BenchBusy; i++) Advance(l, ref now, 0.01);
                bool wasBusy = sp.BenchBusy;
                int before = FlourAll(l, st);
                l.Store(Res.Flour, true).whole = 583;   // the hold starts mid-batch
                int start = FlourAll(l, st);
                bool finished = false;
                for (int i = 0; i < 40; i++)
                {
                    Advance(l, ref now, 0.02);
                    if (!sp.BenchBusy) { finished = true; break; }
                }
                int after = FlourAll(l, st);
                Check("(d) a batch was on the bench", wasBusy, "bench " + sp.benchState);
                Check("(d) the running batch finishes (+1 flour)", finished && after == start + 1,
                      $"finished {finished}, flour {before} -> {start} (set) -> {after}");
                bool reloaded = false;
                for (int i = 0; i < 20; i++) { Advance(l, ref now, 0.05); if (sp.BenchBusy) reloaded = true; }
                Check("(d) the next batch is held", !reloaded && sp.pauseReason == OutpostLedger.FlourHoldWords,
                      $"reloaded {reloaded}, reason '{sp.pauseReason}'");
                Check("(f) the miller stays posted through the hold", Posted(l, miller, st), Assignment(miller));

                // ---- (e) resumes when the Kitchen is raised to II -----------------
                foreach (var b in l.raised) if (b != null && b.planId == BuildPlans.Kitchen.id) b.level = 2;
                bool resumed = false;
                for (int i = 0; i < 10 && !resumed; i++) { Advance(l, ref now, 0.05); if (sp.BenchBusy) resumed = true; }
                Check("(e) Kitchen I -> II: the mill loads again by itself", resumed,
                      $"bench {sp.benchState}, reason '{sp.pauseReason}'");
            }

            // ---- the hold is the mill's only: other recipes are untouched -------
            {
                var l = Camp(583, 1, out _, out _, out _);
                Check("only a recipe that makes flour is held",
                      l.HoldOf(Economy.Recipes.Named("boards")) == null && l.HoldOf(Economy.Recipes.Named("grilled-fish")) == null);
            }

            string head = pass == total ? "PASS" : "FAIL";
            return $"{head} {pass}/{total}" + (sb.Length > 0 ? "\n" + sb : "");
        }

        /// A Mill 12 m east of the store with flour selected and one miller,
        /// a Kitchen at `kitchenLevel` 12 m west (nobody on it), fire II, 100
        /// wheat and `flour` flour in the store, no runners.
        static OutpostLedger Camp(int flour, int kitchenLevel, out StationStock st, out SpotState sp, out OutpostHand miller)
        {
            string mill = BuildPlans.Mill.id, kitchen = BuildPlans.Kitchen.id;
            var l = new OutpostLedger { ceilingPer = 1000, stationsMigrated = true, campfireLevel = 2 };
            l.SetCentre(Vector3.zero);
            l.raised.Add(new BuiltBuilding { planId = mill, x = 12f, level = 1 });
            l.built.Add(mill);
            l.raised.Add(new BuiltBuilding { planId = kitchen, x = -12f, level = kitchenLevel });
            l.built.Add(kitchen);
            miller = new OutpostHand { name = "Miller", order = OutpostOrder.Work, target = mill };
            l.hands.Add(miller);
            l.Store(Res.Food, true).whole = 1000;
            l.Store(Res.Wheat, true).whole = 100;
            l.Store(Res.Flour, true).whole = flour;
            l.lastTicked = 0.0;
            l.EnsureStations();
            st = l.StationOf(mill);
            st.EnsureSpotRows();
            l.SelectRecipe(st, 0, "flour", out _);
            sp = st.Spots[0];
            return l;
        }

        static int Flour(OutpostLedger l) => l.StoreCountOf(Res.Flour);

        /// Flour in the store, on the mill's rack and in arms.
        static int FlourAll(OutpostLedger l, StationStock st) =>
            l.StoreCountOf(Res.Flour) + (st.Rack(Res.Flour)?.whole ?? 0) + l.CarriedOf(Res.Flour);

        static bool Posted(OutpostLedger l, OutpostHand h, StationStock st) =>
            l.hands.Contains(h) && h.order == OutpostOrder.Work && h.target == BuildPlans.Mill.id
            && l.StationOfHand(h) == st;

        static string Assignment(OutpostHand h) => $"order {h.order}, target {h.target}";

        static void Advance(OutpostLedger l, ref double now, double days)
        {
            now += days * TimeOfDay.WorkDaySeconds;
            l.Tick(now + 1e-3);
        }
    }
}
