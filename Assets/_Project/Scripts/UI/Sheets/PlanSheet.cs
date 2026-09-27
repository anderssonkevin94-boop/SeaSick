using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **A locked or unbuilt plan's card (2026-09-27, candidate #9).**
    ///
    /// Today, tapping a locked "campfire II" row in the Ledger's MAKE group
    /// opened the whole Camp Overview -- whatever goal happened to be
    /// pinned, never an answer to "why is this locked." And an unbuilt
    /// plan's row opened the build catalogue with no focus on that card.
    /// This is the missing destination: one plan, what it costs, why it is
    /// out of reach if it is, and the same "Set as goal" door
    /// `BuildSheet`'s own cards use (`GoalPin.SetBuild`).
    ///
    /// **It reads and it calls; it never decides.** Prices are
    /// `OutpostLedger.PriceOfNext`, the lock is `PlanUnlocked` /
    /// `Techs.PlanLevel`, exactly what `BuildSheet` reads for the same
    /// card. Nothing here sites a building -- that stays `BuildSheet`'s
    /// job (Camp › Build), reached from here only via "Set as goal" ->
    /// the goal bar/overview's own Go.
    public sealed class PlanSheet : ISheetFramed
    {
        readonly Outpost outpost;
        readonly string planId;

        public PlanSheet(Outpost o, string planId)
        {
            outpost = o;
            this.planId = planId;
        }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;
        BuildPlan Plan => BuildPlans.Named(planId);

        // --- ISheet / ISheetFramed -------------------------------------------

        public string Title => string.IsNullOrEmpty(Plan.label) ? "plan" : Plan.label;
        public Vector3 AnchorWorld => outpost != null ? outpost.CampCentre : Vector3.zero;
        public bool StillValid => outpost != null && outpost.Ledger != null && !string.IsNullOrEmpty(planId);
        public Color Accent => SheetTheme.Timber;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }

        public VisualElement BuildHeader() =>
            SheetKit.Header("not built", Title, SheetTheme.Timber, "⚒", () => Sheets.Close());

        Button pinBtn;

        public VisualElement BuildActions()
        {
            pinBtn = SheetKit.Btn("Set as goal", TogglePin, true);
            return SheetKit.Actions(pinBtn);
        }

        VisualElement blurb, lockNote, tiles;
        Label copiesNote;

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            blurb = SheetKit.Text(Plan.blurb ?? "", false, true, 13f);
            root.Add(blurb);

            lockNote = SheetKit.Note("");
            lockNote.style.display = DisplayStyle.None;
            root.Add(lockNote);

            tiles = SheetBits.Holder();
            root.Add(tiles);

            copiesNote = SheetKit.Text("", false, true, 12f);
            root.Add(copiesNote);

            Refresh();
            return root;
        }

        long tilesKey = long.MinValue;
        int pinKind = -1;

        public void Refresh()
        {
            var l = L;
            if (l == null || string.IsNullOrEmpty(planId)) return;
            var plan = Plan;

            bool unlocked = l.PlanUnlocked(planId);
            int needLevel = Techs.PlanLevel(planId);
            lockNote.style.display = unlocked ? DisplayStyle.None : DisplayStyle.Flex;
            if (!unlocked)
                SheetKit.SetNote(lockNote, $"Needs campfire {RecipeGraph.Roman(needLevel)} (this camp is {RecipeGraph.Roman(l.CampfireLevel)}).");

            var priced = unlocked ? l.PriceOfNext(plan) : plan;
            int haveT = l.SpendableOf(TimberOf(plan));
            int haveS = l.SpendableOf(Res.Stone);
            int haveB = l.SpendableOf(Res.Brick);
            long key = (((long)haveT * 31 + priced.cost) * 31 + haveS * 31 + priced.stoneCost)
                       * 31 + (haveB * 31 + priced.brickCost) * 7 + (unlocked ? 1 : 0);
            if (key != tilesKey)
            {
                tilesKey = key;
                var row = new List<VisualElement>(3)
                {
                    MaterialChip(TimberOf(plan), haveT, priced.cost),
                };
                if (priced.stoneCost > 0) row.Add(MaterialChip(Res.Stone, haveS, priced.stoneCost));
                if (priced.brickCost > 0) row.Add(MaterialChip(Res.Brick, haveB, priced.brickCost));
                SheetBits.Swap(tiles, SheetKit.Row(row.ToArray()));
            }

            int top = Techs.MaxCopies(planId, Techs.CapTableLevels);
            if (top > 1)
            {
                int held = l.CopiesHeld(planId);
                int cap = l.CopyLimit(planId);
                copiesNote.text = $"×{held} of {cap} today.";
            }
            else copiesNote.text = "";

            bool pinned = GoalPin.IsBuildPinned(outpost, planId);
            int kind = (pinned ? 1 : 0) * 2 + (unlocked ? 1 : 0);
            // `Build()` calls `Refresh()` before `SheetHost.FillTab` gets to
            // `BuildActions()` (Build fills the body first, actions second),
            // so the first Refresh runs with `pinBtn` still null. Guard it
            // here rather than reorder FillTab for every sheet's sake.
            if (pinBtn != null && kind != pinKind)
            {
                pinKind = kind;
                pinBtn.text = pinned ? "Goal ✓" : "Set as goal";
                pinBtn.SetEnabled(unlocked);
            }
        }

        void TogglePin()
        {
            if (outpost == null || string.IsNullOrEmpty(planId)) return;
            if (GoalPin.IsBuildPinned(outpost, planId)) GoalPin.Clear(outpost);
            else GoalPin.SetBuild(outpost, planId);
            pinKind = -1;
            Refresh();
        }

        static string TimberOf(BuildPlan p) => string.IsNullOrEmpty(p.resource) ? Res.Timber : p.resource;

        static VisualElement MaterialChip(string res, int have, int need) =>
            SheetKit.Chip(ResDefs.Label(res), $"{have}/{need}", have >= need ? SheetTheme.Moss : SheetTheme.Ember);
    }
}
