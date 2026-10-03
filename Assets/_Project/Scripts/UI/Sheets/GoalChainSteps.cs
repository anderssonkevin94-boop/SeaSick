using System;
using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;

namespace SeaSick.UI.Sheets
{
    /// How one piece of the Next card's progress line reads.
    public enum NextTone
    {
        /// A plain state ("being built · 40%"): ice.
        Info,
        /// Short of it: amber.
        Short,
        /// Met: moss, with a painted check after it.
        Met,
    }

    /// **What the Next card shows** -- filled by `GoalChainSteps` (the
    /// chain), or by `NextCard` itself (a pinned goal, an alert). Reused
    /// between refreshes; `Key` moves only when something drawn moves, so
    /// the card re-texts on a change and not every tick.
    public sealed class NextModel
    {
        public const int MaxParts = 4;

        public string label = "", title = "", reason = "";
        public readonly string[] partText = new string[MaxParts];
        public readonly NextTone[] partTone = new NextTone[MaxParts];
        public int parts;
        /// The whole card's tap.
        public Action tap;
        /// Set when the card is showing a `CampAlerts` alert (so the alert
        /// strip does not show the same one twice).
        public string alertText;
        public bool raid;

        public void Clear()
        {
            label = title = reason = "";
            parts = 0;
            tap = null;
            alertText = null;
            raid = false;
        }

        public void Part(string text, NextTone tone)
        {
            if (parts >= MaxParts || string.IsNullOrEmpty(text)) return;
            partText[parts] = text;
            partTone[parts] = tone;
            parts++;
        }

        public long Key
        {
            get
            {
                long k = (label?.GetHashCode() ?? 0) * 31L + (title?.GetHashCode() ?? 0);
                k = k * 1000003L + (reason?.GetHashCode() ?? 0);
                for (int i = 0; i < parts; i++)
                    k = k * 1000003L + (partText[i]?.GetHashCode() ?? 0) * 3 + (int)partTone[i];
                return k * 31L + parts;
            }
        }
    }

    /// **The goal chain, island UI restructure phase 2 (2026-09-30).**
    ///
    /// The first island's path to the fire's second level, as ONE list of
    /// step records. Derived from the save every refresh -- never timed or
    /// scripted: the first step whose `done` test fails (and is not
    /// `skip`ped) is the current one, so an old save lands on the right
    /// step and a player who went ahead of the list simply skips steps.
    ///
    /// Done tests that ride on a stock that later gets spent (boards, a
    /// spear, hide) also read the milestones after them, so spending the
    /// boards on a spear does not send the card back to "Make boards".
    /// The fire at II (the chain's own end) marks every step done.
    public static class GoalChainSteps
    {
        public sealed class Step
        {
            public string id;
            public string title;
            public string reason;
            /// Not applicable here (its building is locked at this fire).
            public Func<Outpost, OutpostLedger, bool> skip;
            public Func<Outpost, OutpostLedger, bool> done;
            /// Progress line, tap, and any title/reason override for this
            /// step's current state. Title and reason are set before it runs.
            public Action<Outpost, OutpostLedger, NextModel> fill;
        }

        // --- the milestones the done tests share ---

        static bool FireRaised(OutpostLedger l) => l.CampfireLevel >= 2;
        static bool Built(OutpostLedger l, string planId) => l.CountBuilt(planId) > 0;
        static bool HuntSent(OutpostLedger l)
        {
            if (l.StoreCountOf(Res.Hide) > 0) return true;
            foreach (var h in l.hands)
                if (h != null && h.order == OutpostOrder.Gather && h.target == Res.Game) return true;
            return false;
        }
        static bool SpearMade(OutpostLedger l) => l.SpearInHand() != null || HuntSent(l);
        static bool BoardsMade(OutpostLedger l) => l.StoreCountOf(Res.Boards) > 0 || SpearMade(l);

        static string Saw => BuildPlans.Sawmill.id;
        static string Forge => BuildPlans.Blacksmith.id;

        /// The chain, in order. Ids are this list's own (for the GDD and a
        /// probe), not plan ids.
        public static readonly Step[] All =
        {
            new Step
            {
                id = "camp", title = "Make camp",
                reason = "Light a fire ashore. It keeps ten of anything.",
                done = (o, l) => o.HasCamp,
                fill = FillCamp,
            },
            BuildStep("hut", BuildPlans.Hut.id, "Build a shelter",
                "Beds, so hands stay happy and new ones can join."),
            new Step
            {
                id = "food", title = "Grow food",
                reason = "Build a farm plot or a fishing hut and give it a hand.",
                skip = (o, l) => !l.PlanUnlocked(BuildPlans.Farm.id) && !l.PlanUnlocked(BuildPlans.FishingHut.id),
                done = (o, l) => l.MannedCopies(BuildPlans.Farm.id) > 0 || l.MannedCopies(BuildPlans.FishingHut.id) > 0,
                fill = FillFood,
            },
            BuildStep("store", BuildPlans.Storage.id, "Build a store hut",
                "The fire cache holds only a little. A store hut adds racks, sacks and bays.",
                (o, l) => Built(l, BuildPlans.Storehouse.id)),
            BuildStep("sawmill", Saw, "Build a sawmill", "Logs into boards. Boards raise the fire."),
            new Step
            {
                id = "sawyer", title = "Give the sawmill a worker",
                reason = "A sawyer turns logs into boards. Pick a hand at the saw.",
                skip = (o, l) => !l.PlanUnlocked(Saw),
                done = (o, l) => l.MannedCopies(Saw) > 0 || BoardsMade(l),
                fill = (o, l, m) => m.tap = () => OpenStation(o, Saw),
            },
            new Step
            {
                id = "boards", title = "Make boards",
                reason = "Set the sawmill to boards and keep logs coming.",
                skip = (o, l) => !l.PlanUnlocked(Saw),
                done = (o, l) => BoardsMade(l),
                fill = (o, l, m) => m.tap = () => OpenStation(o, Saw),
            },
            BuildStep("forge", Forge, "Build a forge", "Spears for hunting and, later, defence.",
                (o, l) => SpearMade(l)),
            new Step
            {
                id = "spear", title = "Make a stone spear",
                reason = "One board and one stone at the forge. No spear, no hunt.",
                skip = (o, l) => !l.PlanUnlocked(Forge),
                done = (o, l) => SpearMade(l),
                fill = (o, l, m) =>
                {
                    if (l.MannedCopies(Forge) == 0) m.Part("no smith yet", NextTone.Short);
                    m.tap = () => OpenStation(o, Forge);
                },
            },
            new Step
            {
                id = "hunt", title = "Send a hunter",
                reason = "Hide for the fire. Meat for the kitchen.",
                done = (o, l) => HuntSent(l),
                fill = (o, l, m) => m.tap = () => Sheets.Open(new GatherSheet(o, Res.Game)),
            },
            new Step
            {
                id = "fire2", title = "Raise the fire to II",
                reason = "Opens the quarry and every building's second level.",
                done = (o, l) => FireRaised(l),
                fill = FillRaise,
            },
        };

        /// Fill `m` with the current step. False when the chain is done (or
        /// there is no camp to read), and `m` is left cleared.
        public static bool Current(Outpost camp, NextModel m)
        {
            m.Clear();
            if (camp == null) return false;
            var l = camp.Ledger;
            if (l == null)
            {
                // A surveyed island with no ledger yet: only the fire applies.
                if (camp.HasCamp) return false;
                Fill(All[0], camp, null, m, 1, 1);
                return true;
            }
            if (FireRaised(l)) return false;

            int total = 0, index = 0;
            Step current = null;
            foreach (var s in All)
            {
                if (s.skip != null && s.skip(camp, l)) continue;
                total++;
                if (current != null || s.done(camp, l)) continue;
                current = s;
                index = total;
            }
            if (current == null) return false;
            Fill(current, camp, l, m, index, total);
            return true;
        }

        static void Fill(Step s, Outpost camp, OutpostLedger l, NextModel m, int index, int total)
        {
            m.label = $"NEXT · STEP {index} OF {total}";
            m.title = s.title;
            m.reason = s.reason;
            s.fill?.Invoke(camp, l, m);
        }

        // --- the step kinds ---

        /// "Build a X": not placed = its cost against the store, tap opens
        /// Build with this card carrying GOAL; a blueprint down = in progress
        /// ("being built · 40%"), tap opens that site (the building-sheet
        /// focus brings the camera to it). Locked at this fire = skipped.
        static Step BuildStep(string id, string planId, string title, string reason,
                              Func<Outpost, OutpostLedger, bool> alsoDone = null) => new Step
        {
            id = id, title = title, reason = reason,
            skip = (o, l) => !l.PlanUnlocked(planId),
            done = (o, l) => Built(l, planId) || (alsoDone != null && alsoDone(o, l)),
            fill = (o, l, m) => FillBuild(o, l, m, planId),
        };

        static void FillBuild(Outpost o, OutpostLedger l, NextModel m, string planId)
        {
            var row = FirstSite(l, planId);
            if (row != null) { InProgress(o, row, m, planId); return; }
            Price(l, BuildPlans.Named(planId), m);
            m.tap = () => BuildSheet.Open(o, planId);
        }

        static void FillCamp(Outpost o, OutpostLedger l, NextModel m)
        {
            var row = l != null ? FirstSite(l, BuildPlans.Campfire.id) : null;
            if (row != null) { InProgress(o, row, m, BuildPlans.Campfire.id); return; }
            m.Part($"{BuildPlans.Campfire.cost} logs", NextTone.Info);
            m.tap = () =>
            {
                if (o == null || o.HasCamp || CampSiting.Placing) return;
                // As the anchor prompt's old "Make camp" row did.
                CampSiting.Begin(o, BuildPlans.Campfire, SheetBits.ShipTransform);
            };
        }

        static void FillFood(Outpost o, OutpostLedger l, NextModel m)
        {
            string farm = BuildPlans.Farm.id, fish = BuildPlans.FishingHut.id;
            float days = SheetBits.FoodDays(l);
            string clock = l.hands.Count == 0 || days < 0f ? ""
                : days < 1f ? " The biscuit runs out in under a day."
                : $" The biscuit runs out in {Mathf.FloorToInt(days)} day{(Mathf.FloorToInt(days) == 1 ? "" : "s")}.";

            // Standing but nobody at it: the hand is the step.
            string standing = Built(l, farm) ? farm : Built(l, fish) ? fish : null;
            if (standing != null)
            {
                m.title = standing == farm ? "Give the farm a hand" : "Give the fishing hut a hand";
                m.reason = "Nobody works it yet." + clock;
                m.tap = () => OpenStation(o, standing);
                return;
            }
            m.reason += clock;
            var row = FirstSite(l, farm) ?? FirstSite(l, fish);
            if (row != null) { InProgress(o, row, m, row.planId); return; }
            string pick = l.PlanUnlocked(farm) ? farm : fish;
            Price(l, BuildPlans.Named(pick), m);
            m.tap = () => BuildSheet.Open(o, pick);
        }

        static void FillRaise(Outpost o, OutpostLedger l, NextModel m)
        {
            var next = l.NextCampfire;
            if (next != null && next.cost != null)
                foreach (var line in next.cost)
                    Have(m, line.n, l.SpendableOf(line.res), line.res);
            m.tap = () => Sheets.Open(new CampfireSheet(o));
        }

        // --- helpers ---

        static PendingBuild FirstSite(OutpostLedger l, string planId)
        {
            if (l == null || l.sites == null) return null;
            foreach (var r in l.sites)
                if (r != null && r.planId == planId) return r;
            return null;
        }

        /// A blueprint is down: its phase in the words the site sheet uses
        /// (stocking is counts, only the hammering is a percentage -- Kevin,
        /// 2026-09-23), tap opens the site.
        static void InProgress(Outpost o, PendingBuild row, NextModel m, string planId)
        {
            string line = row.Stocked && row.Cleared
                ? $"being built · {Mathf.RoundToInt(row.Progress01 * 100f)}%"
                : "blueprint · " + row.PhaseLine;
            m.Part(line, NextTone.Info);
            m.tap = () => OpenSite(o, row, planId);
        }

        /// "20 timber (you have 14)" amber, "4 stone" moss with a check.
        static void Price(OutpostLedger l, BuildPlan plan, NextModel m)
        {
            var p = l.PriceOfNext(plan);
            string timber = string.IsNullOrEmpty(p.resource) ? Res.Timber : p.resource;
            if (p.cost > 0) Have(m, p.cost, l.SpendableOf(timber), timber);
            if (p.stoneCost > 0) Have(m, p.stoneCost, l.SpendableOf(Res.Stone), Res.Stone);
            if (p.brickCost > 0) Have(m, p.brickCost, l.SpendableOf(Res.Brick), Res.Brick);
        }

        static void Have(NextModel m, int need, int have, string res)
        {
            string name = CampReadouts.Label(res);
            if (have >= need) m.Part($"{need} {name}", NextTone.Met);
            else m.Part($"{need} {name} (you have {Mathf.Max(0, have)})", NextTone.Short);
        }

        /// The first standing copy of `planId`: its own sheet (the station
        /// page, or the farm's), which the building-sheet focus brings into
        /// view. Build with it highlighted when none stands.
        internal static void OpenStation(Outpost o, string planId)
        {
            if (o == null) return;
            foreach (var b in o.Built)
                if (b != null && b.Id == planId)
                {
                    Sheets.Open(Sheets.TryCreateFor(b) ?? new StationSheet(o, b));
                    return;
                }
            BuildSheet.Open(o, planId);
        }

        static void OpenSite(Outpost o, PendingBuild row, string planId)
        {
            if (o == null) return;
            foreach (var s in UnityEngine.Object.FindObjectsByType<BuildSite>(FindObjectsSortMode.None))
                if (s != null && s.Row == row && SheetBits.OutpostOf(s) == o)
                {
                    Sheets.Open(new SiteSheet(o, s));
                    return;
                }
            BuildSheet.Open(o, planId);
        }
    }
}
