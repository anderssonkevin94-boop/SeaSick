using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The crafting menu.**
    ///
    /// Kevin, 2026-09-23: *"make sure the ui makes clear what is needed. im
    /// thinking a crafting menu in the appropriate buildings."* Tap a raised
    /// station and this is what it has to say: what it can make (`Make`) and
    /// what raising it further would buy (`Upgrade`) -- both read straight
    /// out of the ledger's own tables (`SeaSick.World.Economy.Recipes` /
    /// `Techs`), so a new recipe or a re-priced upgrade shows up here without
    /// this file changing.
    ///
    /// **It reads and it calls; it never decides.** Every have/need number
    /// comes off `OutpostLedger`; the only arithmetic here is the rate
    /// note's multiplication, the same sum `OutpostLedger.Step` does per
    /// hand per day.
    ///
    /// A building with nothing to make and nowhere to go (a pier) has no
    /// sheet at all -- `SheetBootstrap` returns null for it rather than
    /// opening an empty card.
    public class StationSheet : ISheetFramed
    {
        const int TabMake = 0;
        const int TabUpgrade = 1;

        readonly Outpost outpost;
        readonly Building building;
        readonly string planId;
        readonly BuildPlan plan;
        readonly bool hasMake;
        readonly bool hasUpgrade;

        int tab = -1;

        public StationSheet(Outpost o, Building b)
        {
            outpost = o;
            building = b;
            planId = b != null ? b.Id : null;
            plan = BuildPlans.Named(planId);
            hasMake = Recipes.StationHasRecipes(planId);
            hasUpgrade = Techs.MaxLevel(planId) > 1;
        }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        // --- the frame -------------------------------------------------------

        public Color Accent => SheetTheme.Timber;

        /// Two tabs only when there is a decision on each; one section is a
        /// single page with no strip -- `WallSheet`'s rule.
        public string[] TabLabels => hasMake && hasUpgrade ? TwoTabs : null;
        static readonly string[] TwoTabs = { "Make", "Upgrade" };

        /// **`Tab` stays -1 until the host sets it**, exactly as
        /// `ISheetFramed` asks: -1 means "no preference", and that is what
        /// lets the host restore the tab this sheet TYPE was left on last
        /// session (`Sheets.RecallTab`). `Live` is what the body actually
        /// reads, and clamps a remembered tab that does not exist on a
        /// station with only one section.
        public int Tab => tab;
        public void SetTab(int index) { tab = index; }

        int Live => hasMake && hasUpgrade ? Mathf.Clamp(tab < 0 ? 0 : tab, TabMake, TabUpgrade)
            : (hasMake ? TabMake : TabUpgrade);

        public string Title => plan.label;

        public Vector3 AnchorWorld => building != null
            ? building.transform.position
            : (outpost != null ? outpost.CampCentre : Vector3.zero);

        public bool StillValid => outpost != null && outpost.Ledger != null && building != null;

        public VisualElement BuildHeader()
        {
            var l = L;
            int level = l != null ? l.LevelOf(planId) : 1;
            string where = string.IsNullOrEmpty(plan.position) ? plan.label : plan.position;
            return SheetKit.Header($"level {level} · {where}", Title, SheetTheme.Timber, "⚒",
                () => Sheets.Close());
        }

        Button upgradeBtn;

        /// **The upgrade tab's alone.** Make's rows are their own actions --
        /// tap one to choose it -- so a pinned row under them would be a
        /// second place to look for the same decision.
        public VisualElement BuildActions()
        {
            if (Live != TabUpgrade) return null;
            var l = L;
            if (l == null || l.NextUpgrade(planId) == null) return null;
            int next = l.LevelOf(planId) + 1;
            upgradeBtn = SheetKit.Btn($"Raise to level {next}", DoUpgrade, true);
            upgradeBtn.SetEnabled(l.CanUpgrade(planId, out _));
            return SheetKit.Actions(upgradeBtn);
        }

        // --- the pieces kept between refreshes --------------------------------

        VisualElement makeHolder;
        VisualElement rateNote;
        long makeKey = long.MinValue;

        Label upgradeLevel;
        VisualElement upgradeHolder;
        long upgradeKey = long.MinValue;

        public VisualElement Build()
        {
            makeHolder = null; rateNote = null; upgradeHolder = null; upgradeLevel = null;
            makeKey = long.MinValue; upgradeKey = long.MinValue;

            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            if (Live == TabMake) BuildMake(root);
            else BuildUpgrade(root);

            Refresh();
            return root;
        }

        public void Refresh()
        {
            var l = L;
            if (outpost == null || l == null) return;
            outpost.CatchUp();

            if (Live == TabMake)
            {
                long k = MakeKey(l);
                if (k != makeKey) { makeKey = k; FillMake(l); }
            }
            else
            {
                UpgradeTab(l);
            }

            if (upgradeBtn != null && Live == TabUpgrade)
                upgradeBtn.SetEnabled(l.CanUpgrade(planId, out _));
        }

        // --- tab: make ---------------------------------------------------------

        void BuildMake(VisualElement root)
        {
            root.Add(SheetKit.Eyebrow("make"));
            makeHolder = SheetBits.Holder();
            root.Add(makeHolder);
            rateNote = SheetBits.Holder();
            root.Add(rateNote);
        }

        /// What has to change before the list is worth redrawing: the chosen
        /// recipe, the station's own level, the fire level (locks move with
        /// it), every ingredient and tool the list can name, and the hands on
        /// it -- the rate note's own inputs.
        long MakeKey(OutpostLedger l)
        {
            var chosen = l.RecipeAt(planId);
            long key = (chosen != null && chosen.id != null ? chosen.id.GetHashCode() : 0);
            key = key * 31 + l.LevelOf(planId);
            key = key * 31 + l.CampfireLevel;
            int hands = 0;
            foreach (var h in l.hands)
                if (h != null && h.order == OutpostOrder.Work && h.target == planId) hands++;
            key = key * 31 + hands;
            foreach (var r in Recipes.At(planId))
            {
                foreach (var t in r.takes) key = key * 31 + l.CountOf(t.res);
                if (r.tool != null) key = key * 31 + (l.CountOf(r.tool) > 0 ? 1 : 0);
            }
            return key;
        }

        void FillMake(OutpostLedger l)
        {
            if (makeHolder == null) return;
            makeHolder.Clear();
            foreach (var r in Recipes.At(planId))
                makeHolder.Add(RecipeRow(r, l));

            var chosen = l.RecipeAt(planId);
            if (chosen == null)
            {
                SheetBits.Swap(rateNote, null);
                return;
            }
            int hands = 0;
            foreach (var h in l.hands)
                if (h != null && h.order == OutpostOrder.Work && h.target == planId) hands++;
            float rate = chosen.ratePerDay * Techs.RateMul(planId, l.LevelOf(planId)) * hands;
            string s = hands == 1 ? "1 hand" : $"{hands} hands";
            SheetBits.Swap(rateNote,
                SheetKit.Note($"+{rate:0.#} {ResDefs.Label(chosen.makes)}/day with {s}"));
        }

        /// One recipe: its label, the ingredient line coloured have/need, the
        /// tool line when it wants one, and a state chip or a lock line on
        /// the right. Locked rows (fire level, station level, tool) are
        /// greyed and do nothing but say why; an unlocked row chooses the
        /// recipe when tapped.
        VisualElement RecipeRow(Recipe r, OutpostLedger l)
        {
            bool available = l.RecipeAvailable(r, out string lockWhy);
            var chosen = l.RecipeAt(planId);
            bool isChosen = chosen != null && chosen.id == r.id;

            var left = new VisualElement();
            left.style.flexDirection = FlexDirection.Column;
            left.style.flexGrow = 1f;
            left.Add(SheetKit.Text(r.label, true, false, 14f));

            var ingRow = new VisualElement();
            ingRow.style.flexDirection = FlexDirection.Row;
            ingRow.style.flexWrap = Wrap.Wrap;
            for (int i = 0; i < r.takes.Length; i++)
            {
                if (i > 0) ingRow.Add(SheetKit.Text(", ", false, true, 12f));
                ingRow.Add(IngredientLine(l, r.takes[i].res, r.takes[i].n));
            }
            ingRow.Add(SheetKit.Text($" → {r.yield} {r.label}", false, true, 12f));
            left.Add(ingRow);

            if (r.tool != null)
            {
                bool hasTool = l.CountOf(r.tool) > 0;
                var toolLine = SheetKit.Text($"needs a {ResDefs.Label(r.tool)} (wears)", false, false, 11f);
                toolLine.style.color = hasTool ? SheetTheme.Moss : SheetTheme.Ember;
                left.Add(toolLine);
            }

            VisualElement right = null;
            if (!available)
            {
                right = SheetKit.Text(lockWhy, false, true, 11f);
            }
            else if (isChosen)
            {
                var missing = Cost.Missing(r.takes, res => l.CountOf(res));
                right = missing.Count > 0
                    ? SheetKit.Chip("waiting", ResDefs.Label(missing[0].res), SheetTheme.Ember)
                    : SheetKit.Chip("state", "making", SheetTheme.Moss);
            }

            var row = SheetKit.Row(left, right);
            row.style.paddingTop = 6f;
            row.style.paddingBottom = 6f;
            if (!available)
            {
                row.style.opacity = 0.5f;
            }
            else
            {
                row.AddToClassList("sheet-clickable");
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    l.ChooseRecipe(planId, r.id);
                    Dirty();
                });
            }
            return row;
        }

        void Dirty()
        {
            makeKey = long.MinValue;
            Refresh();
        }

        // --- tab: upgrade --------------------------------------------------------

        void BuildUpgrade(VisualElement root)
        {
            upgradeLevel = SheetKit.Text("", true, false, 16f);
            root.Add(upgradeLevel);
            upgradeHolder = SheetBits.Holder();
            root.Add(upgradeHolder);
        }

        void UpgradeTab(OutpostLedger l)
        {
            int level = l.LevelOf(planId);
            int max = Techs.MaxLevel(planId);
            if (upgradeLevel != null) upgradeLevel.text = $"level {level} of {max}";

            var next = l.NextUpgrade(planId);
            long key = level * 1000003L;
            if (next != null)
                foreach (var c in next.cost) key = key * 31 + l.CountOf(c.res);
            if (key == upgradeKey) return;
            upgradeKey = key;

            var col = new VisualElement();
            col.style.flexDirection = FlexDirection.Column;
            if (next == null)
            {
                col.Add(SheetKit.Note("as good as it gets"));
            }
            else
            {
                var costRow = new VisualElement();
                costRow.style.flexDirection = FlexDirection.Row;
                costRow.style.flexWrap = Wrap.Wrap;
                for (int i = 0; i < next.cost.Length; i++)
                {
                    if (i > 0) costRow.Add(SheetKit.Text(", ", false, true, 12f));
                    costRow.Add(IngredientLine(l, next.cost[i].res, next.cost[i].n));
                }
                col.Add(costRow);
                string effect = EffectLine(next);
                if (effect.Length > 0) col.Add(SheetKit.Text(effect, false, true, 12f));
                if (!l.CanUpgrade(planId, out string why))
                    col.Add(SheetKit.Note(why));
            }
            SheetBits.Swap(upgradeHolder, col);
        }

        static string EffectLine(UpgradeStep step)
        {
            var parts = new List<string>(2);
            if (step.storeBonus != 0) parts.Add($"+{step.storeBonus} stores");
            if (step.housesBonus != 0) parts.Add(step.housesBonus == 1 ? "+1 bed" : $"+{step.housesBonus} beds");
            if (Mathf.Abs(step.rateMul - 1f) > 0.001f) parts.Add($"×{step.rateMul:0.#} rate");
            return string.Join(", ", parts);
        }

        void DoUpgrade()
        {
            var l = L;
            if (l == null) return;
            if (l.Upgrade(planId))
            {
                upgradeKey = long.MinValue;
                Refresh();
            }
        }

        // --- shared with FireSheet's own fire-level block -----------------------

        /// "boards 0/1" -- one ingredient, coloured green when the pile
        /// covers it and red when it does not. The one place the have/need
        /// arithmetic for a single line is written, so the fire's own next-
        /// level cost (`FireSheet`) cannot say something different about the
        /// same pile.
        internal static VisualElement IngredientLine(OutpostLedger l, string res, int need)
        {
            int have = l != null ? l.CountOf(res) : 0;
            var lab = SheetKit.Text($"{ResDefs.Label(res)} {have}/{need}", false, false, 12f);
            lab.style.color = have >= need ? SheetTheme.Moss : SheetTheme.Ember;
            return lab;
        }
    }
}
