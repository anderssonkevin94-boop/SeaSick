using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The farm's page -- `StationSheet`'s frame, farm rows (concept A2,
    /// 2026-09-27).** The same full-height page and kit (`StationPage`):
    ///
    /// 1. **header** -- ☰, "Farm", "Level N · island", a pill (working /
    ///    growing / no worker);
    /// 2. **the farmhand** -- the same worker card every station has;
    /// 3. **the beds** -- this farm's beds, ripe or cut, straight off the
    ///    field (`Outpost.FarmBeds`), and when the next one stands again
    ///    (`Outpost.NextBedDays`);
    /// 4. **food out** -- `OutpostLedger.FoodPerHandPerDay` a hand, into the
    ///    store, and how long the store lasts at the ration they are on;
    /// 5. **why it's stopped** -- the farmhand's `StallReason`;
    /// 6. **upgrade** -- only once the farm has a second level
    ///    (`Techs.MaxLevel("Farm")` is 1 today).
    ///
    /// No recipe cards and no order: a field is not told what to make.
    public class FarmSheet : ISheetFramed
    {
        readonly Outpost outpost;
        readonly Building building;
        readonly string planId;
        readonly BuildPlan plan;
        readonly bool hasUpgrade;
        int raisedIndex = -1;

        public FarmSheet(Outpost o, Building b)
        {
            outpost = o;
            building = b;
            planId = b != null ? b.Id : BuildPlans.Farm.id;
            plan = BuildPlans.Named(planId);
            hasUpgrade = Techs.MaxLevel(planId) > 1;
        }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        public string Title => StationPage.Cap(plan.label);
        public Color Accent => SheetTheme.Moss;
        public bool WantsTallSheet => true;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public VisualElement BuildActions() => null;

        public Vector3 AnchorWorld => building != null
            ? building.transform.position
            : (outpost != null ? outpost.CampCentre : Vector3.zero);

        public bool StillValid => outpost != null && outpost.Ledger != null && building != null;

        StationPage.Header header;

        public VisualElement BuildHeader()
        {
            header = new StationPage.Header(Title, true, () => StationPage.OpenLedgerFor(outpost));
            return header.Root;
        }

        // --- built once -------------------------------------------------------

        VisualElement root;
        StationPage.WorkerCard worker;
        StationPage.UpgradeCard upgrade;
        VisualElement bedRow;
        Label bedsLine;
        Label yieldLine;
        Label keptLine;
        Label stallLine;
        readonly List<bool> beds = new List<bool>();

        public VisualElement Build()
        {
            root = StationPage.Root("st-page");
            StationPage.FitToParent(root);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("st-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            root.Add(scroll);
            var col = new VisualElement();
            col.AddToClassList("st-content");
            scroll.Add(col);

            // 2. the farmhand
            var ws = Section(col, null);
            ws.style.marginTop = 0f;
            worker = new StationPage.WorkerCard(outpost, planId, () => Refresh());
            ws.Add(worker.Root);

            // 3. the beds
            var bs = Section(col, "BEDS");
            var bedCard = StationPage.Card();
            var bedCol = new VisualElement(); bedCol.AddToClassList("st-col");
            bedRow = new VisualElement(); bedRow.AddToClassList("st-beds");
            bedRow.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < Mathf.Max(1, plan.beds); i++)
            {
                var p = new VisualElement();
                p.AddToClassList("st-bed");
                p.pickingMode = PickingMode.Ignore;
                bedRow.Add(p);
            }
            bedCol.Add(bedRow);
            bedsLine = StationPage.Text("", "st-line");
            bedCol.Add(bedsLine);
            bedCard.Add(bedCol);
            bs.Add(bedCard);

            // 4. food out
            var fs = Section(col, "FOOD OUT");
            var foodCard = StationPage.Card();
            var icon = StationPage.Icon(Res.Food, "st-flow-icon");
            icon.style.marginRight = 14f;
            icon.style.marginBottom = 0f;
            foodCard.Add(icon);
            var foodCol = new VisualElement(); foodCol.AddToClassList("st-col");
            yieldLine = StationPage.Text("", "st-line");
            keptLine = StationPage.Text("", "st-line");
            keptLine.AddToClassList("st-muted");
            foodCol.Add(yieldLine);
            foodCol.Add(keptLine);
            foodCard.Add(foodCol);
            fs.Add(foodCard);

            // 5. why it's stopped
            stallLine = StationPage.Text("", "st-stall");
            fs.Add(stallLine);

            // 6. upgrade
            if (hasUpgrade)
            {
                upgrade = new StationPage.UpgradeCard(DoUpgrade);
                col.Add(upgrade.Root);
            }

            Refresh();
            return root;
        }

        static VisualElement Section(VisualElement col, string eyebrow)
        {
            var s = new VisualElement();
            s.AddToClassList("st-section");
            if (eyebrow != null) s.Add(StationPage.Text(eyebrow, "st-eyebrow"));
            col.Add(s);
            return s;
        }

        void ResolveRaisedIndex()
        {
            raisedIndex = -1;
            if (outpost == null || building == null) return;
            var built = outpost.Built;
            for (int i = 0; i < built.Count; i++)
                if (built[i] == building) { raisedIndex = i; break; }
        }

        public void Refresh()
        {
            var l = L;
            if (outpost == null || l == null || worker == null) return;
            outpost.CatchUp();
            ResolveRaisedIndex();
            int level = outpost.LevelOfBuilding(building);

            // Farmhands are dealt round the farms (2026-09-27, the same deal
            // as the stations -- `OutpostLedger.OrdinalOfHand`): this page
            // counts the ones at THIS plot. One farm = every farmhand.
            int mine = outpost.OrdinalOf(building);
            OutpostHand first = null;
            int hands = 0;
            foreach (var h in l.hands)
            {
                if (h == null || h.order != OutpostOrder.Work || h.target != planId) continue;
                if (mine >= 0 && l.OrdinalOfHand(h) != mine) continue;
                if (first == null) first = h;
                hands++;
            }
            worker.Update(l, first, Mathf.Max(0, hands - 1));

            // 3. the beds
            int found = outpost.FarmBeds(building, beds);
            int ripe = 0;
            for (int i = 0; i < bedRow.childCount; i++)
            {
                bool known = i < found;
                bool up = known && beds[i];
                if (up) ripe++;
                bedRow[i].EnableInClassList("st-bed--ripe", up);
                bedRow[i].EnableInClassList("st-bed--cut", known && !up);
            }
            if (found == 0) bedsLine.text = "the beds are not in sight";
            else
            {
                string next = ripe >= found ? "all standing" : "next in " + Days(outpost.NextBedDays);
                bedsLine.text = $"{ripe} of {found} ripe · {next}";
            }

            // 4. food out -- the ledger's per-hand figure, times the hands.
            float per = OutpostLedger.FoodPerHandPerDay * Techs.RateMul(planId, level);
            yieldLine.text = hands <= 1
                ? $"{per:0.#} food a day · to the store"
                : $"{per * hands:0.#} food a day with {hands} hands";
            float stored = l.CountOf(Res.Food) + (l.Store(Res.Food)?.part ?? 0f);
            float days = SheetBits.FoodDays(l);
            keptLine.text = days < 0f
                ? $"{stored:0.#} stored · {SheetBits.FoodDaysLine(l)}"
                : $"{stored:0.#} stored · lasts {days:0.#} days";

            // 5. why it's stopped
            string why = StationSheet.StallText(l, first, false);
            stallLine.text = why;
            stallLine.style.display = string.IsNullOrEmpty(why) ? DisplayStyle.None : DisplayStyle.Flex;

            // the header
            if (header != null)
            {
                header.SetSub($"Level {level} · {StationPage.IslandName(outpost)}");
                if (first == null) header.SetPill("no worker", StationPage.PillBad);
                else if (!string.IsNullOrEmpty(why)) header.SetPill("waiting", StationPage.PillWait);
                else if (ripe > 0) header.SetPill("working", StationPage.PillGood);
                else header.SetPill("growing", StationPage.PillWait);
                header.Refresh();
            }

            upgrade?.Update(l, raisedIndex, planId, level);
        }

        void DoUpgrade()
        {
            var l = L;
            if (l == null) return;
            ResolveRaisedIndex();
            if (l.UpgradeAt(raisedIndex, planId))
            {
                outpost?.Retint(building);
                Refresh();
            }
        }

        /// "~0.5 day", "~3 days", "any moment" -- never "Infinity".
        static string Days(float d)
        {
            if (float.IsInfinity(d) || float.IsNaN(d)) return "— (not regrowing)";
            if (d < 0.05f) return "any moment";
            return d < 1f ? $"~{d:0.#} day" : $"~{d:0.#} days";
        }
    }
}
