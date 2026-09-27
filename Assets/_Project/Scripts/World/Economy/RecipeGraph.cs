using System.Collections.Generic;
using System.Text;
using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.World.Economy
{
    /// **The guarantee.** Walks every resource, recipe, plan, fire level,
    /// upgrade and ship rung and refuses the lot if any of them cannot be
    /// reached from a fresh camp with only the ground to gather from. Run
    /// it from the editor (`RecipeGraph.Validate()` through `unity cmd
    /// eval`) or let the dev-build hook below shout on load.
    ///
    /// What it holds the data to:
    /// - every id anything names is in `ResDefs`;
    /// - a raw resource has no recipe; anything not raw has at least one;
    /// - no recipe takes an Item (items are made OF raw and treated, and
    ///   are used, not re-made) and no cycle exists;
    /// - fire level N+1's price, every plan it opens, every recipe and every
    ///   upgrade at level N+1, are all reachable with what level N can make;
    /// - every ship rung's price names known resources.
    /// Warnings, not failures: a fire level that asks for nothing new.
    public static class RecipeGraph
    {
        public class Report
        {
            public readonly List<string> errors = new List<string>();
            public readonly List<string> warnings = new List<string>();
            public bool Ok => errors.Count == 0;

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append(Ok ? "RecipeGraph OK" : $"RecipeGraph FAIL ({errors.Count} errors)");
                foreach (var e in errors) sb.Append("\n  ERROR ").Append(e);
                foreach (var w in warnings) sb.Append("\n  warn  ").Append(w);
                return sb.ToString();
            }
        }

        public static Report Validate()
        {
            var rep = new Report();
            var known = new HashSet<string>();
            foreach (var d in ResDefs.All)
            {
                if (!known.Add(d.id)) rep.errors.Add($"resource '{d.id}' defined twice");
                if (d.campfireLevel < 1) rep.errors.Add($"resource '{d.id}' has campfireLevel {d.campfireLevel}");
            }
            foreach (var k in Res.Gatherable)
                if (!known.Contains(k)) rep.errors.Add($"gatherable '{k}' is not in ResDefs");

            // --- recipes name real things, respect the tiers ---
            var stations = new HashSet<string>();
            foreach (var p in BuildPlans.AtACamp) stations.Add(p.id);
            foreach (var p in BuildPlans.Fortifications) stations.Add(p.id);
            // The home storehouse is in `All`, not `AtACamp`: buildable, just
            // not at an outpost. It upgrades like the rest.
            foreach (var p in BuildPlans.All) stations.Add(p.id);
            var madeBy = new Dictionary<string, List<Recipe>>();
            var ids = new HashSet<string>();
            foreach (var r in Recipes.All)
            {
                if (!ids.Add(r.id)) rep.errors.Add($"recipe '{r.id}' defined twice");
                if (!known.Contains(r.makes)) rep.errors.Add($"recipe '{r.id}' makes unknown '{r.makes}'");
                else if (ResDefs.IsRaw(r.makes) && !IsCatch(r)) rep.errors.Add($"recipe '{r.id}' makes raw '{r.makes}'; raw is gathered, never made");
                if (!stations.Contains(r.station)) rep.errors.Add($"recipe '{r.id}' is at unknown station '{r.station}'");
                if (r.yield < 1) rep.errors.Add($"recipe '{r.id}' yields {r.yield}");
                if (r.ratePerDay <= 0f) rep.errors.Add($"recipe '{r.id}' has no rate");
                foreach (var line in r.takes)
                {
                    if (!known.Contains(line.res)) rep.errors.Add($"recipe '{r.id}' takes unknown '{line.res}'");
                    else if (ResDefs.Tier(line.res) == ResTier.Item)
                        rep.errors.Add($"recipe '{r.id}' takes item '{line.res}'; items are made of raw and treated only");
                    if (line.n < 1) rep.errors.Add($"recipe '{r.id}' takes {line.n} {line.res}");
                }
                if (r.tool != null)
                {
                    if (!known.Contains(r.tool)) rep.errors.Add($"recipe '{r.id}' needs unknown tool '{r.tool}'");
                    else if (ResDefs.Tier(r.tool) != ResTier.Item) rep.errors.Add($"recipe '{r.id}' tool '{r.tool}' is not an item");
                    if (r.toolWear < 0f) rep.errors.Add($"recipe '{r.id}' has negative tool wear");
                }
                if (r.campfireLevel > Techs.MaxCampfireLevel)
                    rep.errors.Add($"recipe '{r.id}' needs fire level {r.campfireLevel}, above the top ({Techs.MaxCampfireLevel})");
                if (ResDefs.TryGet(r.makes, out var md) && r.campfireLevel < md.campfireLevel)
                    rep.errors.Add($"recipe '{r.id}' is offered at fire {r.campfireLevel} but '{r.makes}' is a fire-{md.campfireLevel} resource");
                // A catch is gathering at a station, not making: it does not
                // turn a raw good into a made one, so it stays out of the
                // made-by table the tier rules below read.
                if (IsCatch(r) && ResDefs.IsRaw(r.makes)) continue;
                if (!madeBy.TryGetValue(r.makes, out var list)) madeBy[r.makes] = list = new List<Recipe>();
                list.Add(r);
            }
            foreach (var d in ResDefs.All)
            {
                bool made = madeBy.ContainsKey(d.id);
                if (d.tier == ResTier.Raw && made) rep.errors.Add($"raw '{d.id}' has a recipe");
                if (d.tier != ResTier.Raw && !made) rep.errors.Add($"'{d.id}' is {d.tier} but nothing makes it");
                if (d.tier == ResTier.Raw && d.source == ResSource.Made) rep.errors.Add($"raw '{d.id}' says it is Made");
                if (d.tier != ResTier.Raw && d.source != ResSource.Made) rep.errors.Add($"'{d.id}' is {d.tier} but its source is {d.source}");
            }

            // --- cycles: a recipe's inputs must never depend on its output ---
            foreach (var r in Recipes.All)
                if (Reaches(r.makes, r.makes, madeBy, new HashSet<string>(), true))
                    rep.errors.Add($"'{r.makes}' is in a cycle: something it takes is made from it");

            // --- the ladder: each level is reachable from the one before ---
            var have = new HashSet<string>();
            foreach (var d in ResDefs.All)
                if (d.tier == ResTier.Raw && d.source != ResSource.Drop) have.Add(d.id);
            var plans = new HashSet<string>();
            foreach (var p in BuildPlans.AtACamp) if (Techs.PlanLevel(p.id) <= 1) plans.Add(p.id);
            foreach (var p in BuildPlans.Fortifications) if (Techs.PlanLevel(p.id) <= 1) plans.Add(p.id);
            foreach (var p in BuildPlans.All) if (Techs.PlanLevel(p.id) <= 1) plans.Add(p.id);
            var levels = new Dictionary<string, int>();

            for (int L = 1; L <= Techs.MaxCampfireLevel; L++)
            {
                var cl = Techs.CampfireAt(L);
                if (cl == null) { rep.errors.Add($"no CampfireLevel {L} in Techs.Campfire"); break; }
                foreach (var p in cl.unlocksPlans)
                {
                    if (BuildPlans.Named(p).id != p) rep.errors.Add($"fire {L} unlocks unknown plan '{p}'");
                    plans.Add(p);
                }
                // Grow what this level can hold until nothing new appears.
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    // Hunting drops: need a spear in the pile.
                    bool canHunt = false;
                    foreach (var s in Techs.HuntingSpears) if (have.Contains(s)) canHunt = true;
                    if (canHunt) foreach (var d in Techs.HuntDrops) if (have.Add(d.res)) grew = true;
                    // Upgrades payable now.
                    foreach (var u in Techs.Upgrades)
                    {
                        if (u.campfireLevel > L || !plans.Contains(u.planId)) continue;
                        int cur = levels.TryGetValue(u.planId, out var lv) ? lv : 1;
                        if (u.toLevel != cur + 1) continue;
                        if (!AllIn(u.cost, have)) continue;
                        levels[u.planId] = u.toLevel; grew = true;
                    }
                    // Recipes workable now.
                    foreach (var r in Recipes.All)
                    {
                        if (r.campfireLevel > L || !plans.Contains(r.station)) continue;
                        int cur = levels.TryGetValue(r.station, out var lv) ? lv : 1;
                        if (r.stationLevel > cur) continue;
                        if (r.tool != null && !have.Contains(r.tool)) continue;
                        if (!AllIn(r.takes, have)) continue;
                        if (have.Add(r.makes)) grew = true;
                    }
                }
                // Everything the table says exists at this level must now be in hand.
                foreach (var d in ResDefs.All)
                    if (d.campfireLevel == L && !have.Contains(d.id))
                        rep.errors.Add($"'{d.id}' is a fire-{L} resource but cannot be made at fire {L}");
                foreach (var r in Recipes.All)
                    if (r.campfireLevel == L && plans.Contains(r.station) && !have.Contains(r.makes))
                        rep.errors.Add($"recipe '{r.id}' unlocks at fire {L} but its inputs cannot all be had there");
                foreach (var u in Techs.Upgrades)
                    if (u.campfireLevel == L && (!levels.TryGetValue(u.planId, out var lv) || lv < u.toLevel))
                        rep.errors.Add($"upgrade {u.planId}→{u.toLevel} unlocks at fire {L} but cannot be paid there ({Cost.Describe(u.cost)})");
                foreach (var u in Techs.Upgrades)
                    if (u.campfireLevel == L && BuildPlans.Named(u.planId).id != u.planId)
                        rep.errors.Add($"upgrade names unknown plan '{u.planId}'");

                var next = Techs.NextCampfire(L);
                if (next == null) continue;
                foreach (var line in next.cost)
                {
                    if (!known.Contains(line.res)) rep.errors.Add($"fire {next.level} costs unknown '{line.res}'");
                    else if (!have.Contains(line.res))
                        rep.errors.Add($"fire {next.level} costs {line.n} {line.res}, which fire {L} cannot make");
                }
                if (next.cost.Length == 0) rep.warnings.Add($"fire {next.level} is free");
                else
                {
                    bool varied = false;
                    foreach (var line in next.cost) if (ResDefs.Tier(line.res) != ResTier.Raw) varied = true;
                    if (!varied) rep.warnings.Add($"fire {next.level} costs only raw goods; a level should ask for something made");
                }
            }
            for (int L = Techs.MaxCampfireLevel + 1; L < 100; L++)
                if (Techs.CampfireAt(L) != null) rep.errors.Add($"CampfireLevel {L} is above MaxCampfireLevel");

            // --- copy caps (2026-09-27): real plans, never shrinking ---
            // Columns past `MaxCampfireLevel` are allowed on purpose: the
            // table carries fire III/IV ahead of the levels themselves.
            var capSeen = new HashSet<string>();
            foreach (var c in Techs.Caps)
            {
                if (c == null || string.IsNullOrEmpty(c.planId)) { rep.errors.Add("a BuildingCap row with no plan"); continue; }
                if (!capSeen.Add(c.planId)) rep.errors.Add($"two BuildingCap rows for '{c.planId}'");
                if (string.IsNullOrEmpty(BuildPlans.Named(c.planId).id)) rep.errors.Add($"BuildingCap names unknown plan '{c.planId}'");
                if (c.copies == null || c.copies.Length == 0) { rep.errors.Add($"BuildingCap '{c.planId}' has no columns"); continue; }
                for (int i = 0; i < c.copies.Length; i++)
                {
                    if (c.copies[i] < 1) rep.errors.Add($"BuildingCap '{c.planId}' allows {c.copies[i]} at fire {i + 1}; the floor is 1");
                    if (i > 0 && c.copies[i] < c.copies[i - 1]) rep.errors.Add($"BuildingCap '{c.planId}' shrinks at fire {i + 1}");
                }
            }

            // --- plans and rungs only name registered goods ---
            foreach (var p in BuildPlans.AtACamp)
            {
                if (!string.IsNullOrEmpty(p.resource) && !known.Contains(p.resource)) rep.errors.Add($"plan '{p.id}' priced in unknown '{p.resource}'");
                if (!string.IsNullOrEmpty(p.takes) && !known.Contains(p.takes)) rep.errors.Add($"plan '{p.id}' takes unknown '{p.takes}'");
                if (!string.IsNullOrEmpty(p.makes) && !known.Contains(p.makes)) rep.errors.Add($"plan '{p.id}' makes unknown '{p.makes}'");
                if (!string.IsNullOrEmpty(p.makes) && !string.IsNullOrEmpty(p.takes) && !Recipes.StationHasRecipes(p.id))
                    rep.warnings.Add($"plan '{p.id}' converts {p.takes}→{p.makes} on the old plan fields with no Recipe; add one");
            }
            foreach (var k in ShipPrices.Priced)
                if (!known.Contains(k)) rep.errors.Add($"ShipPrices.Priced names unknown '{k}'");
            for (int i = 1; i < 40; i++)
            {
                var price = ShipPrices.ForRung(i);
                if (!price.Has) break;
                if (!known.Contains(price.a)) rep.errors.Add($"rung {i} priced in unknown '{price.a}'");
                if (price.HasSecond && !known.Contains(price.b)) rep.errors.Add($"rung {i} priced in unknown '{price.b}'");
            }
            return rep;
        }

        /// **A catch (2026-09-27, the fishing hut):** a station recipe that
        /// takes nothing and no tool. The one shape allowed to produce a Raw
        /// good -- the sea is its input, the way the field is a farm's --
        /// and the only shape that may take nothing at all: a made good
        /// (Treated/Item) still needs a real recipe with inputs.
        public static bool IsCatch(Recipe r) =>
            r != null && (r.takes == null || r.takes.Length == 0) && r.tool == null;

        static bool AllIn(Ingredient[] cost, HashSet<string> have)
        {
            foreach (var line in cost) if (!have.Contains(line.res)) return false;
            return true;
        }

        /// Does making `res` (through any recipe) depend on `target`?
        static bool Reaches(string res, string target, Dictionary<string, List<Recipe>> madeBy,
            HashSet<string> seen, bool first)
        {
            if (!first && res == target) return true;
            if (!seen.Add(res)) return false;
            if (!madeBy.TryGetValue(res, out var list)) return false;
            foreach (var r in list)
            {
                foreach (var line in r.takes)
                    if (Reaches(line.res, target, madeBy, seen, false)) return true;
                if (r.tool != null && Reaches(r.tool, target, madeBy, seen, false)) return true;
            }
            return false;
        }

        /// The whole tree as Markdown, for `docs/PRODUCTION-CHAINS.md`, so
        /// the design doc is generated from the data and cannot drift.
        public static string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Production chains");
            sb.AppendLine();
            sb.AppendLine("Generated from `Scripts/World/Economy/` by `RecipeGraph.ToMarkdown()`. Do not edit by hand.");
            sb.AppendLine();
            sb.AppendLine("## Resources");
            sb.AppendLine();
            sb.AppendLine("| id | label | tier | source | fire | what |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var d in ResDefs.All)
                sb.AppendLine($"| {d.id} | {d.label} | {d.tier} | {d.source} | {Roman(d.campfireLevel)} | {d.blurb} |");
            sb.AppendLine();
            sb.AppendLine("## Fire levels");
            sb.AppendLine();
            foreach (var c in Techs.Campfire)
            {
                sb.AppendLine($"- **{Roman(c.level)} {c.name}** — costs {Cost.Describe(c.cost)}"
                    + (c.unlocksPlans.Length > 0 ? $"; opens {string.Join(", ", c.unlocksPlans)}" : "")
                    + $". {c.blurb}");
            }
            sb.AppendLine();
            sb.AppendLine("## Recipes");
            sb.AppendLine();
            sb.AppendLine("| station | makes | takes | per hand per day | fire | station level | tool (wear) |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var r in Recipes.All)
                sb.AppendLine($"| {r.station} | {r.yield} {r.label} | {Cost.Describe(r.takes)} | {r.ratePerDay} | {Roman(r.campfireLevel)} | {r.stationLevel} | "
                    + (r.tool != null ? $"{ResDefs.Label(r.tool)} ({r.toolWear})" : "") + " |");
            sb.AppendLine();
            sb.AppendLine("## Building upgrades");
            sb.AppendLine();
            sb.AppendLine("| building | to level | fire | costs | rate × | store + | beds + |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var u in Techs.Upgrades)
                sb.AppendLine($"| {u.planId} | {u.toLevel} | {Roman(u.campfireLevel)} | {Cost.Describe(u.cost)} | {u.rateMul} | {u.storeBonus} | {u.housesBonus} |");
            sb.AppendLine();
            sb.AppendLine("## Hunting");
            sb.AppendLine();
            sb.AppendLine($"A hunter needs one of: {string.Join(", ", System.Array.ConvertAll(Techs.HuntingSpears, ResDefs.Label))} (best first). "
                + $"Wear per animal: stone spear {Techs.SpearWear(Res.Spear):0.00}, iron spear {Techs.SpearWear(Res.IronSpear):0.00}. "
                + $"Each animal also drops {Cost.Describe(Techs.HuntDrops)}.");
            return sb.ToString();
        }

        public static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => n.ToString() };

        /// Write the doc. Editor-side helper for the probe and the docs step.
        public static void WriteMarkdown(string path)
        {
            System.IO.File.WriteAllText(path, ToMarkdown());
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// Shouts on load in the editor and in dev builds, so a broken table
        /// is found the first time anybody presses Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void CheckOnLoad()
        {
            var rep = Validate();
            if (!rep.Ok) Debug.LogError(rep.ToString());
            else if (rep.warnings.Count > 0) Debug.LogWarning(rep.ToString());
        }
#endif
    }
}
