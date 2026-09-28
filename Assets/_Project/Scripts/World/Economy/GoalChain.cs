using System.Collections.Generic;

namespace SeaSick.World.Economy
{
    /// How a row of the goal chain reads: done, stuck, or on its way / yours
    /// to do. The sheet colours them green, red and amber.
    public enum GoalState
    {
        /// Met. A met branch is one green row and nothing under it.
        Ok,
        /// Stuck, and something under it (or nothing on this island) is why.
        Blocked,
        /// Moving (a hand is on it), or one tap from moving.
        Pending,
    }

    /// What a row is ABOUT, so the sheet can pick a picture and a verb.
    public enum GoalRowKind
    {
        /// So many of one resource: have / need.
        Resource,
        /// The hunt that brings hide and meat home.
        Hunting,
        /// Hands taking a raw good out of the ground here.
        Gathering,
        /// A station that makes the row above it.
        Station,
        /// A gate that is not a resource: the fire's level, a building that
        /// must stand before it can go up a level.
        Gate,
    }

    /// The one thing the first-step card's Go button does.
    public enum GoalAction
    {
        /// Nothing a tap can do here (sail elsewhere, wait for the fire).
        None,
        /// Pay the goal: raise the fire / take the building up a level.
        Complete,
        /// Open the station's own sheet (worker slot, order, recipe).
        OpenStation,
        /// Give a hand a job: `res` to gather (Game = hunt), or `planId` to work at.
        AssignHand,
        /// Put a station on the build list.
        Build,
    }

    /// A step the player can take, with what the Go button needs to do it.
    public class GoalStep
    {
        public GoalAction action;
        /// "make a spear"
        public string title;
        /// "forge · 1 board + 1 stone · needs a smith"
        public string detail;
        public string planId;
        public string res;
    }

    /// What a whole chain is the goal OF -- so the Go button knows what
    /// "pay it" means.
    public enum GoalKind
    {
        Campfire,
        /// One building's next level (`raisedIndex`).
        Upgrade,
        /// The next copy of a plan (sited from the build list).
        Build,
        /// Any named price.
        Cost,
    }

    /// One line of the chain.
    public class GoalRow
    {
        public int depth;
        public GoalState state;
        public GoalRowKind kind;
        /// `Res.*` id for the icon (ItemIconSet), or null.
        public string icon;
        public string name;
        /// "forge · 1 board + 1 stone"
        public string how;
        /// The right-hand figure: "0 / 4", "no hunter", "assign".
        public string qty;
        public string res;
        public string planId;
        public int have, need;
        /// The step THIS row asks for, if a tap can make it move.
        public GoalStep step;
        /// Red border: this row is what the goal is waiting on.
        public bool Blocking => state != GoalState.Ok;
    }

    /// **The goal chain, 2026-09-27 (Kevin's Melvor-style redesign, concept
    /// C).** Given a camp's books and a goal -- the fire's next level, a
    /// building's next level, or any price (a ship section) -- this answers
    /// "what am I waiting on, and what do I do first?" as a short tree:
    ///
    /// `resource have/need` → how it is got (a recipe at a station, the
    /// ground here, the hunt, or "not on this island") → the station's own
    /// state (standing? manned? a tool? an order?) → the first thing a tap
    /// can move.
    ///
    /// **Rules** (the GDD's production chains, read, never restated):
    /// - a top-level line is met when `SpendableOf` covers it -- the same
    ///   figure `CanRaiseCampfire` / `CanUpgradeAt` pay from;
    /// - a met line is ONE green row with nothing under it;
    /// - depth is capped at `MaxDepth` rows (0..3) and a resource already
    ///   being explained higher up is not explained again (no cycles);
    /// - a made thing's inputs appear only when SHORT of one batch; met
    ///   inputs are named in the row's "how" instead, so the tree stays a
    ///   screen tall;
    /// - hide (a `Drop`) and game are explained by the hunt: game on the
    ///   island, a spear in the pile (`Techs.HuntingSpears`, hard gate), a
    ///   hunter out; a gathered raw by the standing stock and the hands on
    ///   it; a raw this island never had is "on the far islands";
    /// - the FIRST STEP is the first leaf (deepest non-green row with a step)
    ///   of the first blocked branch, top to bottom; when every line is met
    ///   it is the goal itself (Raise).
    ///
    /// Pure: reads an `OutpostLedger`, allocates a few small lists, touches
    /// no scene. Cheap enough to recompute on the sheet's 1 s tick.
    public class GoalChain
    {
        public const int MaxDepth = 4;

        public string title;
        public string why;
        public GoalKind kind;
        /// Upgrade / Build: the plan. Upgrade: the building's `raised` row.
        public string planId;
        public int raisedIndex = -1;
        /// True when the player pinned this goal (not the fire's default).
        public bool Pinned;
        public int ready, total;
        /// False when there is no goal to show (the fire is at its top).
        public bool HasGoal;
        /// Every line met and every gate open: the Go button pays.
        public bool CanComplete;
        public readonly List<GoalRow> rows = new List<GoalRow>();
        public GoalStep firstStep;

        /// A stable number that moves when anything the sheet draws moves --
        /// so a view rebuilds on a real change and not every tick.
        public long Key
        {
            get
            {
                long k = (title?.GetHashCode() ?? 0) * 31L + ready * 7L + total;
                foreach (var r in rows)
                    k = k * 1000003L + ((r.name?.GetHashCode() ?? 0) ^ (r.how?.GetHashCode() ?? 0) * 17
                                         ^ (r.qty?.GetHashCode() ?? 0) * 131) + (int)r.state * 3 + r.depth;
                if (firstStep != null) k = k * 31L + (firstStep.title?.GetHashCode() ?? 0) + (firstStep.detail?.GetHashCode() ?? 0);
                return k;
            }
        }

        // --- goals -------------------------------------------------------------

        /// The fire's next level -- the camp's standing goal.
        public static GoalChain NextCampfire(OutpostLedger l)
        {
            var g = new GoalChain();
            if (l == null) return g;
            var next = l.NextCampfire;
            if (next == null)
            {
                g.title = $"Campfire level {RecipeGraph.Roman(l.CampfireLevel)}";
                g.why = "The fire is as high as it goes.";
                return g;
            }
            g.HasGoal = true;
            g.title = $"Campfire level {RecipeGraph.Roman(next.level)}";
            g.why = Sentence(next.blurb);
            new Walker(l, g).Lines(next.cost);
            g.CanComplete = l.CanRaiseCampfire(out _);
            g.Finish(GoalAction.Complete, $"raise the fire to {RecipeGraph.Roman(next.level)}",
                $"pays {Cost.Describe(next.cost)} from the store");
            return g;
        }

        /// The camp's goal: the pinned one (`OutpostLedger.ActiveGoal`) if
        /// the player set one, else the fire's next level.
        public static GoalChain ForCamp(OutpostLedger l)
        {
            var p = l != null ? l.ActiveGoal : null;
            if (p == null) return NextCampfire(l);
            GoalChain g = p.kind == PinnedGoal.Upgrade
                ? UpgradeAt(l, l.RaisedIndexOf(p.planId, p.ordinal), p.planId)
                : p.kind == PinnedGoal.Stock
                ? SpearStock(l, p.target)
                : NextCopy(l, p.planId);
            g.Pinned = true;
            return g;
        }

        /// **Phase 11, "the camp goal"**: keep `target` spears (stone or
        /// iron, either counts) in the store, suggested after a raid left
        /// hands hiding for lack of one (`OutpostLedger.PinSpearGoal`).
        /// No production-chain drill-down like the other goals get -- a
        /// spear is already explained in full the moment the player opens
        /// Build › the smithy; this just tracks the count the raid asked for.
        public static GoalChain SpearStock(OutpostLedger l, int target)
        {
            var g = new GoalChain { kind = GoalKind.Cost, title = "Forge spears before the next raid" };
            if (l == null || target <= 0) return g;
            g.HasGoal = true;
            g.why = "Hands went unarmed in the last raid.";
            int have = l.SpearStockCount();
            g.total = target;
            g.ready = System.Math.Min(have, target);
            g.rows.Add(new GoalRow
            {
                depth = 0, kind = GoalRowKind.Resource, res = Res.Spear, icon = Res.Spear,
                name = "Spears in store", have = have, need = target, qty = $"{have} / {target}",
                state = have >= target ? GoalState.Ok : GoalState.Blocked,
                how = "stone or iron, either counts",
            });
            g.CanComplete = have >= target;
            g.Finish(GoalAction.None, "spears in the store", "stone or iron, either counts");
            return g;
        }

        /// A building's next level (the lowest standing copy, as
        /// `OutpostLedger.Upgrade(planId)` takes up).
        public static GoalChain NextUpgrade(OutpostLedger l, string planId) =>
            UpgradeAt(l, l != null && !string.IsNullOrEmpty(planId) ? LowestRaised(l, planId) : -1, planId);

        static int LowestRaised(OutpostLedger l, string planId)
        {
            int pick = -1, lo = int.MaxValue;
            if (l.raised != null)
                for (int i = 0; i < l.raised.Count; i++)
                    if (l.raised[i] != null && l.raised[i].planId == planId)
                    {
                        int lv = l.LevelAtRaised(i);
                        if (lv < lo) { lo = lv; pick = i; }
                    }
            return pick;
        }

        /// THIS building's next level: the one on `raised[raisedIndex]`
        /// (2026-09-27, "they have their own levels, always").
        public static GoalChain UpgradeAt(OutpostLedger l, int raisedIndex, string planId)
        {
            var g = new GoalChain { kind = GoalKind.Upgrade, planId = planId, raisedIndex = raisedIndex };
            if (l == null || string.IsNullOrEmpty(planId)) return g;
            string label = Cap(BuildPlans.Named(planId).label ?? planId);
            var step = l.NextUpgradeAt(raisedIndex, planId);
            if (step == null)
            {
                g.title = $"{label} at its top";
                g.why = "Nothing further to build it up to.";
                return g;
            }
            g.HasGoal = true;
            g.title = $"{label} level {step.toLevel}";
            g.why = step.storeBonus > 0 ? $"Keeps {step.storeBonus} more of each thing."
                : step.housesBonus > 0 ? $"Sleeps {step.housesBonus} more."
                : $"Works {step.rateMul:0.#}× as fast.";
            var w = new Walker(l, g);
            if (l.CountBuilt(planId) <= 0)
                w.Gate(GoalRowKind.Station, planId, label, "not built", "build",
                    new GoalStep { action = GoalAction.Build, planId = planId, title = $"build a {BuildPlans.Named(planId).label}",
                        detail = "from the build list" });
            if (l.CampfireLevel < step.campfireLevel)
                w.Gate(GoalRowKind.Gate, null, $"Campfire {RecipeGraph.Roman(step.campfireLevel)}",
                    $"the fire is at {RecipeGraph.Roman(l.CampfireLevel)}", "raise",
                    new GoalStep { action = GoalAction.None, title = $"raise the fire to {RecipeGraph.Roman(step.campfireLevel)}",
                        detail = "the fire's own goal" });
            w.Lines(step.cost);
            g.CanComplete = l.CanUpgradeAt(raisedIndex, planId, out _);
            g.Finish(GoalAction.Complete, $"take the {BuildPlans.Named(planId).label} to level {step.toLevel}",
                $"pays {Cost.Describe(step.cost)} from the store", planId);
            return g;
        }

        /// The next copy of a plan, at its copy price
        /// (`OutpostLedger.PriceOfNext`) and inside its cap (`CanAddCopy`).
        /// Done = sited from the build list; the builders haul the price.
        public static GoalChain NextCopy(OutpostLedger l, string planId)
        {
            var g = new GoalChain { kind = GoalKind.Build, planId = planId };
            if (l == null || string.IsNullOrEmpty(planId)) return g;
            var plan = BuildPlans.Named(planId);
            string label = plan.label ?? planId;
            int held = l.CopiesHeld(planId);
            g.HasGoal = true;
            g.title = held > 0 ? $"A {OutpostLedger.Nth(held + 1)} {label}" : $"Build {Article(label)}";
            g.why = Sentence(plan.blurb);
            var w = new Walker(l, g);
            bool gateOpen = true;
            if (!l.PlanUnlocked(planId))
            {
                gateOpen = false;
                w.Gate(GoalRowKind.Gate, null, $"Campfire {RecipeGraph.Roman(Techs.PlanLevel(planId))}",
                    l.PlanLockReason(planId), "raise",
                    new GoalStep { action = GoalAction.None, title = "raise the fire first", detail = "the fire's own goal" });
            }
            else if (!l.CanAddCopy(planId, out string why))
            {
                gateOpen = false;
                w.Gate(GoalRowKind.Gate, null, Cap(why), "the fire opens more", "cap",
                    new GoalStep { action = GoalAction.None, title = "raise the fire first", detail = why });
            }
            var priced = l.PriceOfNext(plan);
            var cost = new List<Ingredient>(3);
            if (priced.cost > 0) cost.Add(new Ingredient(Res.Timber, priced.cost));
            if (priced.stoneCost > 0) cost.Add(new Ingredient(Res.Stone, priced.stoneCost));
            if (priced.brickCost > 0) cost.Add(new Ingredient(Res.Brick, priced.brickCost));
            var arr = cost.ToArray();
            w.Lines(arr);
            g.CanComplete = gateOpen && Cost.Affordable(arr, l.SpendableOf);
            g.Finish(GoalAction.Build, $"site the {label}", $"from the build list · {Cost.Describe(arr)}", planId);
            return g;
        }

        /// Any price with a name -- a ship section, a rung. `why` is one line.
        public static GoalChain ForCost(OutpostLedger l, string title, string why, Ingredient[] cost)
        {
            var g = new GoalChain { title = title, why = why, HasGoal = true, kind = GoalKind.Cost };
            if (l == null) return g;
            new Walker(l, g).Lines(cost);
            g.CanComplete = Cost.Affordable(cost, l.SpendableOf);
            g.Finish(GoalAction.Complete, "pay for it", $"pays {Cost.Describe(cost)} from the store");
            return g;
        }

        /// Pick the first step (see the class doc) and fill the progress.
        void Finish(GoalAction done, string doneTitle, string doneDetail, string planId = null)
        {
            if (CanComplete)
            {
                firstStep = new GoalStep { action = done, title = doneTitle, detail = doneDetail, planId = planId };
                return;
            }
            // First leaf with a step, top-level branch by branch.
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r.state == GoalState.Ok || r.step == null) continue;
                bool leaf = i + 1 >= rows.Count || rows[i + 1].depth <= r.depth;
                // A row whose children have no step of their own still
                // offers its own (the hunter, when the spear row says "far
                // islands"): take it if nothing deeper in its branch can.
                if (!leaf && BranchHasStep(i)) continue;
                firstStep = r.step;
                return;
            }
            firstStep = null;
        }

        bool BranchHasStep(int i)
        {
            int d = rows[i].depth;
            for (int j = i + 1; j < rows.Count && rows[j].depth > d; j++)
                if (rows[j].state != GoalState.Ok && rows[j].step != null) return true;
            return false;
        }

        // --- the walk ------------------------------------------------------------

        /// One walk over one ledger. Keeps the set of resources already on
        /// the path so a chain can never loop back on itself.
        sealed class Walker
        {
            readonly OutpostLedger l;
            readonly GoalChain g;
            readonly HashSet<string> onPath = new HashSet<string>();

            public Walker(OutpostLedger l, GoalChain g) { this.l = l; this.g = g; }

            public void Gate(GoalRowKind kind, string planId, string name, string how, string qty, GoalStep step)
            {
                g.rows.Add(new GoalRow { depth = 0, state = GoalState.Blocked, kind = kind, planId = planId,
                    name = name, how = how, qty = qty, step = step, icon = null });
            }

            public void Lines(Ingredient[] cost)
            {
                if (cost == null) return;
                foreach (var line in cost)
                {
                    if (line.n <= 0) continue;
                    g.total++;
                    int have = l.SpendableOf(line.res);
                    if (have >= line.n) g.ready++;
                    Resource(line.res, have, line.n, 0);
                }
            }

            /// A resource row and, when short, what it takes to get more.
            void Resource(string res, int have, int need, int depth)
            {
                bool ok = have >= need;
                var row = new GoalRow
                {
                    depth = depth, kind = GoalRowKind.Resource, res = res, icon = res,
                    name = Cap(ResDefs.Label(res)), have = have, need = need,
                    qty = $"{have} / {need}",
                    state = ok ? GoalState.Ok : GoalState.Blocked,
                    how = HowGot(res),
                };
                g.rows.Add(row);
                if (ok || depth + 1 >= MaxDepth || !onPath.Add(res)) return;
                int start = g.rows.Count;
                Explain(res, row, depth + 1);
                onPath.Remove(res);
                // A short row whose only explanation is "hands are on it" is
                // on its way, not stuck: amber rather than red.
                row.state = BranchState(start, depth + 1, row.state);
            }

            /// Red if anything under it is red, else amber.
            GoalState BranchState(int start, int childDepth, GoalState fallback)
            {
                bool any = false;
                for (int i = start; i < g.rows.Count; i++)
                {
                    if (g.rows[i].depth != childDepth) continue;
                    any = true;
                    if (g.rows[i].state == GoalState.Blocked) return GoalState.Blocked;
                }
                return any ? GoalState.Pending : fallback;
            }

            void Explain(string res, GoalRow parent, int depth)
            {
                ResDefs.TryGet(res, out var def);
                switch (def.source)
                {
                    case ResSource.Drop:
                    case ResSource.Hunted:
                        Hunt(res, parent, depth);
                        return;
                    case ResSource.Gathered:
                        Ground(res, parent, depth);
                        return;
                    default:
                        Made(res, parent, depth);
                        return;
                }
            }

            // --- the hunt ---

            void Hunt(string res, GoalRow parent, int depth)
            {
                var stock = l.Stock(Res.Game);
                int game = stock != null ? (int)stock.standing : 0;
                int hunters = l.HandsOn(OutpostOrder.Gather, Res.Game);
                string spear = l.SpearInHand();
                var row = new GoalRow
                {
                    depth = depth, kind = GoalRowKind.Hunting, res = Res.Game, icon = Res.Food, name = "Hunting",
                    how = game > 0 ? $"{game} game on the island" : "no game left on the island",
                    qty = hunters > 0 ? (hunters == 1 ? "1 hunter" : $"{hunters} hunters") : "no hunter",
                };
                g.rows.Add(row);
                if (game < 1)
                {
                    row.state = GoalState.Blocked;
                    row.how = "no game left on this island · hunt elsewhere";
                    return;
                }
                if (spear == null)
                {
                    row.how += " · needs a spear";
                    row.state = GoalState.Blocked;
                    // The hunter can be posted now; the spear is the real wait.
                    row.step = hunters == 0 ? AssignStep(Res.Game, "post a hunter", "he waits at the fire until a spear is made") : null;
                    if (depth + 1 < MaxDepth && !onPath.Contains(Res.Spear))
                        Resource(Res.Spear, l.SpendableOf(Res.Spear), 1, depth + 1);
                    return;
                }
                if (hunters == 0)
                {
                    row.how += " · needs a hunter";
                    row.state = GoalState.Blocked;
                    row.step = AssignStep(Res.Game, "post a hunter", $"{ResDefs.Label(spear)} in the pile · {game} game on the island");
                    return;
                }
                string stall = FirstStall(OutpostOrder.Gather, Res.Game);
                row.state = stall != null ? GoalState.Blocked : GoalState.Pending;
                if (stall != null) row.how += " · " + stall;
                else row.how += res == Res.Hide ? " · 1 hide per animal" : "";
            }

            // --- the ground ---

            void Ground(string res, GoalRow parent, int depth)
            {
                var stock = l.Stock(res);
                string label = ResDefs.Label(res);
                int gatherers = l.HandsOn(OutpostOrder.Gather, res);
                var row = new GoalRow
                {
                    depth = depth, kind = GoalRowKind.Gathering, res = res, icon = res,
                    name = "Gather " + label,
                    qty = gatherers > 0 ? $"{gatherers} on it" : "nobody",
                };
                g.rows.Add(row);
                if (stock == null || (stock.standing < 1f && stock.standingMax < 1f))
                {
                    row.state = GoalState.Blocked;
                    row.how = res == Res.Ore
                        ? "not on this island · ore is on the far islands"
                        : "not on this island · " + (ResDefs.TryGet(res, out var d) ? d.blurb : "found elsewhere");
                    row.qty = "far";
                    return;
                }
                if (stock.standing < 1f)
                {
                    row.state = GoalState.Blocked;
                    row.how = "none left on the island · sail for more";
                    row.qty = "0 left";
                    return;
                }
                row.how = $"{(int)stock.standing} left on the island";
                if (gatherers == 0)
                {
                    row.state = GoalState.Pending;
                    row.step = AssignStep(res, $"send a hand for {label}", row.how);
                    return;
                }
                string stall = FirstStall(OutpostOrder.Gather, res);
                row.state = stall != null ? GoalState.Blocked : GoalState.Pending;
                if (stall != null) { row.how += " · " + stall; row.step = AssignStep(res, $"sort out the {label} gatherer", stall); }
            }

            // --- a station ---

            void Made(string res, GoalRow parent, int depth)
            {
                var r = PickRecipe(res);
                if (r == null)
                {
                    parent.how = "nothing here makes it";
                    return;
                }
                var plan = BuildPlans.Named(r.station);
                string stationLabel = plan.label ?? r.station;
                parent.how = $"{stationLabel} · {Cost.Describe(r.takes)}"
                             + (r.tool != null ? $" · {ResDefs.Label(r.tool)}" : "");

                var row = new GoalRow
                {
                    depth = depth, kind = GoalRowKind.Station, planId = r.station, icon = null,
                    name = Cap(stationLabel),
                };
                g.rows.Add(row);
                string makeTitle = $"make {Article(ResDefs.Label(res))}";

                if (l.CampfireLevel < r.campfireLevel)
                {
                    row.state = GoalState.Blocked;
                    row.how = $"{ResDefs.Label(res)} needs the fire at {RecipeGraph.Roman(r.campfireLevel)}";
                    row.qty = "locked";
                    return;
                }
                if (l.CountBuilt(r.station) <= 0)
                {
                    if (l.Queued(r.station))
                    {
                        row.state = GoalState.Pending;
                        row.how = "being built";
                        row.qty = "building";
                        return;
                    }
                    row.state = l.PlanUnlocked(r.station) ? GoalState.Pending : GoalState.Blocked;
                    row.how = l.PlanUnlocked(r.station) ? "not built" : "not built · " + l.PlanLockReason(r.station);
                    row.qty = "build";
                    if (l.PlanUnlocked(r.station))
                        row.step = new GoalStep { action = GoalAction.Build, planId = r.station,
                            title = $"build {Article(stationLabel)}", detail = $"then it can {makeTitle}" };
                    return;
                }
                if (l.LevelOf(r.station) < r.stationLevel)
                {
                    row.state = GoalState.Blocked;
                    row.how = $"needs the {stationLabel} at level {r.stationLevel}";
                    row.qty = "upgrade";
                    row.step = new GoalStep { action = GoalAction.OpenStation, planId = r.station,
                        title = $"upgrade the {stationLabel}", detail = row.how };
                    return;
                }

                string position = plan.position ?? "worker";
                int workers = l.HandsOn(OutpostOrder.Work, r.station);
                var missingIn = new List<Ingredient>();
                foreach (var line in r.takes)
                    if (l.CountOf(line.res) < line.n) missingIn.Add(new Ingredient(line.res, line.n));
                bool toolShort = r.tool != null && l.CountOf(r.tool) <= 0 && !HasToolHeld(r.tool);
                bool ordered = OrderedToMake(r);

                if (workers == 0)
                {
                    row.state = GoalState.Pending;
                    row.how = $"built · no {position}";
                    row.qty = "assign";
                    row.step = new GoalStep { action = GoalAction.OpenStation, planId = r.station, res = res,
                        title = makeTitle, detail = $"{stationLabel} · {Cost.Describe(r.takes)} · needs a {position}" };
                }
                else if (!ordered)
                {
                    row.state = GoalState.Pending;
                    row.how = $"{position} at work · not told to {makeTitle}";
                    row.qty = "order";
                    row.step = new GoalStep { action = GoalAction.OpenStation, planId = r.station, res = res,
                        title = makeTitle, detail = $"order it at the {stationLabel}" };
                }
                else
                {
                    string stall = FirstStall(OutpostOrder.Work, r.station);
                    row.state = stall != null && missingIn.Count == 0 && !toolShort ? GoalState.Blocked : GoalState.Pending;
                    row.how = stall ?? $"making {ResDefs.Label(res)}";
                    row.qty = stall != null ? "stopped" : "working";
                    if (stall != null)
                        row.step = new GoalStep { action = GoalAction.OpenStation, planId = r.station, res = res,
                            title = $"see the {stationLabel}", detail = stall };
                }

                // What the station is short of, under it.
                if (depth + 1 >= MaxDepth) return;
                if (toolShort && !onPath.Contains(r.tool))
                    Resource(r.tool, l.SpendableOf(r.tool), 1, depth + 1);
                foreach (var m in missingIn)
                    if (!onPath.Contains(m.res))
                        Resource(m.res, l.CountOf(m.res), m.n, depth + 1);
                if (missingIn.Count > 0 || toolShort)
                {
                    // The station itself is fine only if its inputs are.
                    bool childRed = false;
                    int i = g.rows.IndexOf(row);
                    for (int j = i + 1; j < g.rows.Count && g.rows[j].depth > depth; j++)
                        if (g.rows[j].depth == depth + 1 && g.rows[j].state == GoalState.Blocked) childRed = true;
                    if (childRed) row.state = GoalState.Blocked;
                }
            }

            /// The recipe to explain: the first one this camp can work now,
            /// else the first one at all (whose row then says why not).
            Recipe PickRecipe(string res)
            {
                var all = Recipes.Making(res);
                Recipe first = null;
                foreach (var r in all)
                {
                    if (first == null) first = r;
                    if (r.campfireLevel <= l.CampfireLevel) return r;
                }
                return first;
            }

            bool HasToolHeld(string tool)
            {
                // A worn tool at 0.4 still cuts: the store's part counts.
                var s = l.Store(tool);
                return s != null && s.whole + s.part > 0f;
            }

            /// Some station of this plan has an order for this recipe -- or
            /// the station list has not been built yet (a fresh ledger), in
            /// which case the choice-is-an-order rule makes the pick count.
            bool OrderedToMake(Recipe r)
            {
                var list = l.stations;
                bool anyStation = false;
                if (list != null)
                    foreach (var s in list)
                    {
                        if (s == null || s.removed || s.planId != r.station) continue;
                        anyStation = true;
                        if (s.HasOrder && s.orderRecipe == r.id) return true;
                    }
                if (anyStation) return false;
                var chosen = l.RecipeAt(r.station);
                return chosen != null && chosen.id == r.id && l.choices != null && l.choices.Exists(c => c != null && c.planId == r.station);
            }

            string FirstStall(OutpostOrder order, string target)
            {
                foreach (var h in l.hands)
                {
                    if (h == null || h.order != order || h.target != target) continue;
                    string why = l.StallReason(h);
                    if (why == null) return null;   // one hand working is working
                }
                foreach (var h in l.hands)
                    if (h != null && h.order == order && h.target == target) return l.StallReason(h);
                return null;
            }

            GoalStep AssignStep(string res, string title, string detail) =>
                new GoalStep { action = GoalAction.AssignHand, res = res, title = title, detail = detail };

            /// "sawmill · Hollis", "in store", "from hunting · 1 per animal".
            string HowGot(string res)
            {
                ResDefs.TryGet(res, out var def);
                if (def.source == ResSource.Drop) return "from hunting · 1 per animal";
                if (def.source == ResSource.Hunted) return "hunted on the island";
                if (def.source == ResSource.Gathered)
                {
                    var stock = l.Stock(res);
                    if (stock == null || (stock.standing < 1f && stock.standingMax < 1f)) return "in store · not on this island";
                    if (stock.standing < 1f) return "in store · none left on the island";
                    return $"in store · {(int)stock.standing} on the island";
                }
                var r = PickRecipe(res);
                if (r == null) return "in store";
                string who = null;
                foreach (var h in l.hands)
                    if (h != null && h.order == OutpostOrder.Work && h.target == r.station) { who = h.name; break; }
                string st = BuildPlans.Named(r.station).label ?? r.station;
                return who != null ? $"{st} · {who}" : st;
            }
        }

        // --- words --------------------------------------------------------------

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static string Sentence(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Cap(s);
            return s.EndsWith(".") ? s : s + ".";
        }

        static string Article(string noun)
        {
            if (string.IsNullOrEmpty(noun)) return noun;
            // Mass nouns read without one: "make boards", "make iron".
            if (noun.EndsWith("s") || noun == "iron" || noun == "brick" || noun == "food") return noun;
            return ("aeiou".IndexOf(noun[0]) >= 0 ? "an " : "a ") + noun;
        }
    }
}
