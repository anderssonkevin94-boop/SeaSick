using System.Collections.Generic;
using System.Text;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;

namespace SeaSick.Dev.EditorTools
{
    /// **Food-collapse trace (diagnostic, 2026-10-02).** Loads ONE camp's
    /// ledger out of a save JSON in EDIT mode (no play, no scene, no bodies,
    /// straight-line walks) and plays it forward the way `AwayProgress` does:
    /// `Tick` against a clock walked in chunks, the absence record closed.
    /// Logs per sky day: food fill, units by kind, eaten, produced, every
    /// hand's order/target/draft/pout/mood/supperHunger, wild stocks, plots.
    ///
    /// Chunked across evals (the CLI bridge times out at ~5 s):
    ///   FoodCollapseTrace.Load(path, camp, policy, stride);
    ///   FoodCollapseTrace.Run(10);  // sky days, repeat
    ///   FoodCollapseTrace.Done();   // restores the clock, returns the log path
    /// Policies (counterfactuals, all in memory, the save is never written):
    ///   "none"      as saved
    ///   "staff"     Pip on Farm + Tam on Kitchen from the start
    ///   "autostaff" emulated fix: when food is under FedDays, a free hand is
    ///               put on the Farm (and one on the Kitchen) instead of foraging
    ///   "watched"   chunk = 1 quantum, as live play ticks
    ///   "death"     Hollis dies at load (grief pouts)
    ///   "starved"   start from 0 food, mood 0, supperHunger 1 (with "watched")
    /// Policies combine with '+', e.g. "starved+watched+staff".
    public static class FoodCollapseTrace
    {
        static OutpostLedger L;
        static double now, clock0, cal0;
        static int skyDay;
        static float eaten0;
        static float fill0;
        static string policy = "none";
        static int stride = 1;
        static StringBuilder log;
        static string outPath;

        public static string Load(string path, int camp, string pol = "none", int str = 1, string outFile = null)
        {
            var json = System.IO.File.ReadAllText(path);
            var data = JsonUtility.FromJson<SeaSick.Save.SaveData>(json);
            L = data.outposts[camp].ledger;
            L.MigratePending();
            if (L.sites == null) L.sites = new List<PendingBuild>();
            L.sites.RemoveAll(r => r == null || string.IsNullOrEmpty(r.planId));
            L.away = new OutpostLedger.Absence();
            L.raiders = 0;
            L.router = null;
            foreach (var h in L.hands) if (h != null) { h.walkingIn = false; h.driven = false; }
            clock0 = TimeOfDay.Seconds; cal0 = TimeOfDay.CalendarDays;
            TimeOfDay.SetClock(data.timeSeconds, data.CalendarDays);
            now = L.lastTicked;
            policy = pol ?? "none";
            stride = Mathf.Max(1, str);
            outPath = outFile ?? "/tmp/foodtrace.txt";
            log = new StringBuilder();
            if (Has("staff")) { Assign("Pip", BuildPlans.Farm.id); Assign("Tam", BuildPlans.Kitchen.id); }
            if (Has("lean"))
            {
                // Only the old ship's biscuit goes: the camp lives on what it grows.
                var m = L.Store(Res.Meals); if (m != null) { m.whole = 0; m.part = 0f; }
                foreach (var st in L.stations) if (st != null) foreach (var r in st.rack) if (r != null && r.resource == Res.Meals) { r.whole = 0; r.part = 0f; }
                L.foodMigrated = true;
            }
            if (Has("death"))
            {
                // A death at the camp: the survivors grieve (2026-10-02).
                OutpostHand dead = null;
                foreach (var h in L.hands) if (h != null && h.name == "Hollis") dead = h;
                if (dead != null) { L.Die(dead, "test"); Line("  Hollis died (in memory)"); }
            }
            if (Has("spear")) { L.Store(Res.Spear, true).whole = 3; Line("  +3 spears in memory"); }
            if (Has("huts")) { for (int i = 0; i < 5; i++) L.built.Add("Hut"); Line("  +5 huts in memory: beds " + L.HousingCapacity); }
            if (Has("cook")) Line("  cook order baked-potato repeat: " + L.PlaceOrder(BuildPlans.Kitchen.id, "baked-potato", OutpostLedger.RepeatOrder));
            if (Has("starved"))
            {
                foreach (var s in L.stores) if (s != null && FoodBook.Fill(s.resource) > 0f) { s.whole = 0; s.part = 0f; }
                foreach (var st in L.stations)
                    if (st != null)
                    {
                        foreach (var r in st.rack) if (r != null && FoodBook.Fill(r.resource) > 0f) { r.whole = 0; r.part = 0f; }
                    }
                foreach (var h in L.hands) if (h != null) { h.mood = 0f; h.full = 0f; h.supperHunger = 1f; }
                // MigrateFood would turn the old Food pile into biscuit: mark it done.
                L.foodMigrated = true;
            }
            skyDay = 0;
            eaten0 = L.foodEaten;
            fill0 = L.FoodFill();
            Line($"LOAD camp {camp} policy={policy} stride={stride} hands={L.hands.Count} t={data.timeSeconds:0} cal={data.CalendarDays:0.00} "
                 + $"workDay={TimeOfDay.WorkDaySeconds} skyDay={TimeOfDay.DayLength} scale={TimeOfDay.Scale}");
            Snapshot("start");
            Flush();
            return Tail(6);
        }

        static bool Has(string p) => System.Array.IndexOf(policy.Split('+'), p) >= 0;

        static void Assign(string name, string plan)
        {
            foreach (var h in L.hands)
                if (h != null && h.name == name)
                { h.order = OutpostOrder.Work; h.target = plan; h.autoFood = false; h.playerIdle = false; }
        }

        static int CountOn(string plan)
        {
            int n = 0;
            foreach (var h in L.hands) if (h != null && h.order == OutpostOrder.Work && h.target == plan) n++;
            return n;
        }

        /// The emulated fix: food under `FedDays` -> a free hand on the Farm
        /// first, then the Kitchen; never a hand the player gave other work.
        static void AutoStaff()
        {
            float day = L.hands.Count * OutpostLedger.EatPerHandPerDay;
            if (L.FoodFill() >= day * OutpostLedger.FedDays) return;
            foreach (var plan in new[] { BuildPlans.Farm.id, BuildPlans.Kitchen.id })
            {
                if (!L.built.Contains(plan) || CountOn(plan) > 0) continue;
                foreach (var h in L.hands)
                {
                    if (h == null || h.Busy || h.Hauling) continue;
                    bool free = h.order == OutpostOrder.Idle || h.order == OutpostOrder.Build
                        || (h.autoFood && h.order == OutpostOrder.Gather);
                    if (!free) continue;
                    h.order = OutpostOrder.Work; h.target = plan; h.autoFood = false; h.playerIdle = false;
                    Line($"  autostaff: {h.name} -> {plan}");
                    break;
                }
            }
        }

        /// Advance `skyDays` sky days, logging each.
        public static string Run(int skyDays)
        {
            if (L == null) return "not loaded";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            OutpostLedger.CatchUpStride = stride;
            bool watched = Has("watched");
            double workDay = TimeOfDay.WorkDaySeconds;
            double chunk = watched ? OutpostLedger.QuantumDays * workDay * stride : 0.25 * workDay;
            double skyLen = TimeOfDay.DayLength;
            int start = skyDay;
            try
            {
                while (skyDay < start + skyDays && sw.ElapsedMilliseconds < 3500)
                {
                    double end = now + skyLen;
                    while (now < end - 1e-6)
                    {
                        double next = System.Math.Min(end, now + chunk);
                        TimeOfDay.Scrub(next);
                        if (Has("autostaff")) AutoStaff();
                        L.Tick(next);
                        // (Pouts run inside Step since 2026-10-02: grief only.)
                        now = next;
                    }
                    skyDay++;
                    Snapshot("day " + skyDay);
                }
            }
            finally { OutpostLedger.CatchUpStride = 1; }
            Line($"  ({sw.ElapsedMilliseconds} ms)");
            Flush();
            return Tail(skyDay - start + 1);
        }

        static void Snapshot(string tag)
        {
            float fill = L.FoodFill();
            float eaten = L.foodEaten - eaten0;
            float produced = fill - fill0 + eaten;
            eaten0 = L.foodEaten; fill0 = fill;
            var kinds = new SortedDictionary<string, float>();
            foreach (var s in L.stores)
                if (s != null && (FoodBook.Fill(s.resource) > 0f || s.resource == Res.Meat) && s.whole + s.part > 0.01f)
                    kinds[s.resource] = (kinds.TryGetValue(s.resource, out var v) ? v : 0f) + s.whole + s.part;
            foreach (var st in L.stations)
            {
                if (st == null) continue;
                foreach (var r in st.rack)
                    if (r != null && FoodBook.Fill(r.resource) > 0f && r.whole + r.part > 0.01f)
                        kinds["rack:" + r.resource] = (kinds.TryGetValue("rack:" + r.resource, out var v) ? v : 0f) + r.whole + r.part;
                foreach (var r in st.bay)
                    if (r != null && FoodBook.Fill(r.resource) > 0f && r.whole + r.part > 0.01f)
                        kinds["bay:" + r.resource] = (kinds.TryGetValue("bay:" + r.resource, out var v) ? v : 0f) + r.whole + r.part;
            }
            var sb = new StringBuilder();
            sb.Append($"{tag,-7} n={L.hands.Count} fill={fill,6:0.00} ate={eaten,5:0.00} made={produced,6:0.00} |");
            foreach (var k in kinds) sb.Append($" {k.Key}={k.Value:0.#}");
            var food = L.Stock(Res.Food); var game = L.Stock(Res.Game);
            sb.Append($" | wild food={(food != null ? food.standing : -1):0.0}/{(food != null ? food.standingMax : 0):0.#}");
            sb.Append($" game={(game != null ? game.standing : -1):0.0}");
            int e = 0, g = 0, r2 = 0;
            if (L.plots != null) foreach (var p in L.plots) { if (p.state == PlotState.Empty) e++; else if (p.state == PlotState.Growing) g++; else r2++; }
            sb.Append($" plots E{e}/G{g}/R{r2}");
            var k2 = L.StationOf(BuildPlans.Kitchen.id);
            if (k2 != null)
            {
                var ko = L.OrderAt(L.StationIndex(BuildPlans.Kitchen.id));
                sb.Append($" kit={k2.benchState}:{ko.recipe}{(ko.repeat ? "*" : "")}");
            }
            sb.Append("\n    ");
            foreach (var h in L.hands)
            {
                if (h == null) continue;
                string ord = h.order == OutpostOrder.Idle ? "Idle" : h.order + ":" + h.target;
                if (h.pouting) ord += $" pout{h.poutLeft:0}s";
                if (h.griefPending) ord += " grief";
                sb.Append($"{h.name}[{ord}{(h.autoFood ? " auto" : "")}{(h.pouting ? " POUT" : "")}{(h.Hauling ? " h:" + h.haulRes : "")}"
                          + $" m{h.mood:0.00} f{h.full:0.00} sh{h.supperHunger:0.00}] ");
            }
            Line(sb.ToString());
        }

        static void Line(string s) { log.AppendLine(s); }
        static void Flush() { System.IO.File.WriteAllText(outPath, log.ToString()); }
        static string Tail(int n)
        {
            var lines = log.ToString().TrimEnd().Split('\n');
            int from = Mathf.Max(0, lines.Length - n * 2 - 1);
            return string.Join("\n", lines, from, lines.Length - from);
        }

        public static string Done()
        {
            TimeOfDay.SetClock(clock0, cal0);
            OutpostLedger.ActiveHourKnown = false;
            OutpostLedger.CatchUpStride = 1;
            L = null;
            return outPath;
        }
    }
}
