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
    /// card.
    ///
    /// **No longer a dead end (2026-09-30).** A plan that is unlocked with
    /// a copy to spare gets a primary "Build" button -- short of material
    /// is fine, the builders fetch the rest, as on the Build screen -- that
    /// starts placement through `BuildSheet.StartPlacement`, the very call
    /// a Build card makes. Otherwise the sheet says why it cannot be built
    /// (locked, or at its cap) and "Set as goal" is what is left. Both
    /// buttons are built once and re-texted / shown / hidden on refresh.
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

        Button pinBtn, buildBtn;

        public VisualElement BuildActions()
        {
            buildBtn = SheetKit.Btn("Build", StartBuild, true);
            buildBtn.style.display = DisplayStyle.None;
            pinBtn = SheetKit.Btn("Set as goal", TogglePin);
            buildKind = -1;
            pinKind = -1;
            if (lockNote != null) Refresh();   // the body is built first; sync the new buttons
            return SheetKit.Actions(buildBtn, pinBtn);
        }

        VisualElement blurb, lockNote, tiles;
        Label copiesNote, shortNote;

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

            shortNote = SheetKit.Text("", false, true, 12f);
            shortNote.style.display = DisplayStyle.None;
            root.Add(shortNote);

            Refresh();
            return root;
        }

        long tilesKey = long.MinValue;
        int pinKind = -1, buildKind = -1;

        public void Refresh()
        {
            var l = L;
            if (l == null || string.IsNullOrEmpty(planId)) return;
            var plan = Plan;

            bool unlocked = l.PlanUnlocked(planId);
            int needLevel = Techs.PlanLevel(planId);
            // The drawn plans (palisade, ladder, road) have no copy cap.
            string capWhy = null;
            bool canBuild = unlocked && (BuildSheet.IsDrawn(planId) || l.CanAddCopy(planId, out capWhy));
            string whyNot = !unlocked
                ? $"Needs campfire {RecipeGraph.Roman(needLevel)} (this camp is {RecipeGraph.Roman(l.CampfireLevel)})."
                : canBuild ? null : Cap(capWhy) + ".";
            lockNote.style.display = whyNot == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (whyNot != null) SheetKit.SetNote(lockNote, whyNot);

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

            // Short of material: still placeable, the builders fetch the rest.
            bool isShort = canBuild && !BuildSheet.IsDrawn(planId)
                           && (haveT < priced.cost || haveS < priced.stoneCost || haveB < priced.brickCost);
            string sn = isShort ? "Short of material: place it anyway and the builders fetch the rest." : "";
            if (shortNote.text != sn) shortNote.text = sn;
            shortNote.style.display = isShort ? DisplayStyle.Flex : DisplayStyle.None;

            int bk = canBuild ? 1 : 0;
            if (buildBtn != null && bk != buildKind)
            {
                buildKind = bk;
                buildBtn.style.display = canBuild ? DisplayStyle.Flex : DisplayStyle.None;
            }

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

        /// Start placement -- exactly the call a Build card makes -- and fold
        /// the sheet away so the ground is clear.
        void StartBuild()
        {
            if (outpost == null || string.IsNullOrEmpty(planId)) return;
            BuildSheet.StartPlacement(outpost, Plan);
        }

        void TogglePin()
        {
            if (outpost == null || string.IsNullOrEmpty(planId)) return;
            if (GoalPin.IsBuildPinned(outpost, planId)) GoalPin.Clear(outpost);
            else GoalPin.SetBuild(outpost, planId);
            pinKind = -1;
            Refresh();
        }

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static string TimberOf(BuildPlan p) => string.IsNullOrEmpty(p.resource) ? Res.Timber : p.resource;

        static VisualElement MaterialChip(string res, int have, int need) =>
            SheetKit.Chip(ResDefs.Label(res), $"{have}/{need}", have >= need ? SheetTheme.Moss : SheetTheme.Ember);
    }
}
