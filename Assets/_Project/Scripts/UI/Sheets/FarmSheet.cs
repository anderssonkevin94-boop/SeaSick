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
        Label bedsLine;
        VisualElement plotGrid, picker, cropRow;
        Label pickerTitle, pickerInfo;
        Button repeatBtn;
        int pickedPlot = -1;
        long plotKey = long.MinValue;

        static Button FarmBtn(string text, System.Action act)
        {
            var b = new Button(act) { text = text };
            b.AddToClassList("st-seg-btn");
            b.style.minHeight = StationSheet.TouchPx;
            b.style.marginRight = 6f;
            b.style.marginTop = 6f;
            return b;
        }

        FarmPlot Picked()
        {
            var l = L;
            if (l == null || pickedPlot < 0) return null;
            var list = l.PlotsOf(Mathf.Max(0, outpost.OrdinalOf(building)));
            return pickedPlot < list.Count ? list[pickedPlot] : null;
        }

        static string Clock(float secs)
        {
            int s = Mathf.CeilToInt(secs);
            return $"{s / 60}:{s % 60:00}";
        }

        void FillPlots(OutpostLedger l)
        {
            var list = l.PlotsOf(Mathf.Max(0, outpost.OrdinalOf(building)));
            long key = list.Count * 131L + pickedPlot;
            foreach (var p in list)
                key = key * 31 + (p.crop?.GetHashCode() ?? 0) + (int)p.state * 7 + (p.repeat ? 1 : 0)
                      + Mathf.CeilToInt(p.SecondsLeft) * 13;
            int ripe = 0, growing = 0;
            float soonest = float.MaxValue;
            foreach (var p in list)
            {
                if (p.state == PlotState.Ripe) ripe++;
                if (p.state == PlotState.Growing) { growing++; soonest = Mathf.Min(soonest, p.SecondsLeft); }
            }
            bedsLine.text = list.Count == 0 ? "no plots yet"
                : $"{ripe} ripe · {growing} growing" + (soonest < float.MaxValue ? $" · next in {Clock(soonest)}" : "");
            lastRipe = ripe;
            if (key == plotKey) return;
            plotKey = key;

            plotGrid.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                int idx = i;
                var p = list[i];
                var tile = new Button(() => { pickedPlot = pickedPlot == idx ? -1 : idx; plotKey = long.MinValue; Refresh(); });
                tile.AddToClassList("st-seg-btn");
                // **Three tiles a row, each wrapping its own two lines**
                // (Kevin, 2026-09-28, the plots ran together as
                // "PotatoPotatoPotato..."): `.st-seg-btn` is a segmented-row
                // button -- flex-basis 0, flex-grow 1, fixed 56 px height,
                // no wrapping -- which overrode the 31 % width and put every
                // plot on one squeezed line. Undo exactly those here.
                tile.style.flexGrow = 0f;
                tile.style.flexShrink = 0f;
                tile.style.flexBasis = Length.Percent(31f);
                tile.style.width = Length.Percent(31f);
                tile.style.height = StyleKeyword.Auto;
                tile.style.minHeight = 64f;
                tile.style.whiteSpace = WhiteSpace.Normal;
                tile.style.unityTextAlign = TextAnchor.MiddleCenter;
                tile.style.fontSize = 16f;
                tile.style.paddingTop = 6f;
                tile.style.paddingBottom = 6f;
                tile.style.marginLeft = 0f;
                tile.style.marginRight = Length.Percent(2f);
                tile.style.marginBottom = 6f;
                tile.style.flexDirection = FlexDirection.Column;
                if (idx == pickedPlot) tile.AddToClassList("st-seg-btn--on");
                string name = string.IsNullOrEmpty(p.crop) ? "Empty" : StationPage.Cap(ResDefs.Label(p.crop));
                string state = p.state == PlotState.Ripe ? $"ripe · {FoodBook.Crop(p.crop)?.yield ?? 0}"
                    : p.state == PlotState.Growing ? Clock(p.SecondsLeft)
                    : string.IsNullOrEmpty(p.crop) ? "tap to plant" : "to plant";
                // No emoji: the sheet font (Nunito) has none, and the phone drew
                // them as a stray "]". An empty plot gets a plain +.
                tile.text = (string.IsNullOrEmpty(p.crop) ? "+ " : "") + $"{name}\n{state}"
                    + (p.repeat && !string.IsNullOrEmpty(p.crop) ? " ↻" : "");
                plotGrid.Add(tile);
            }

            var pk = Picked();
            picker.style.display = pk != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (pk == null) return;
            pickerTitle.text = $"Plot {pickedPlot + 1} · " + (string.IsNullOrEmpty(pk.crop) ? "empty" : ResDefs.Label(pk.crop));
            cropRow.Clear();
            foreach (var c in FoodBook.Crops)
            {
                string crop = c.res;
                bool ok = l.CropUnlocked(pk, crop, out string why);
                var b = FarmBtn(ok ? $"{c.icon} {StationPage.Cap(ResDefs.Label(crop))}" : $"{c.icon} 🔒 {why}",
                    () => { L?.SetPlotCrop(Picked(), crop, Picked()?.repeat ?? true); plotKey = long.MinValue; Refresh(); });
                b.SetEnabled(ok);
                if (pk.crop == crop) b.AddToClassList("st-seg-btn--on");
                cropRow.Add(b);
            }
            var def = FoodBook.Crop(pk.crop);
            pickerInfo.text = def == null ? "pick a crop; the farmhand plants it"
                : $"{def.growSeconds / 60f:0.#} min · {def.yield} per harvest · {def.PerHour:0} an hour";
            repeatBtn.text = pk.repeat ? "↻ Replant on repeat: on" : "↻ Replant on repeat: off";
        }

        int lastRipe;
        Label yieldLine;
        Label keptLine;
        Label stallLine;

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

            // 3. the plots (food rework, 2026-09-27): one tile per plot with
            // its own timer; tap a plot to pick its crop underneath.
            var bs = Section(col, "PLOTS");
            plotGrid = new VisualElement();
            plotGrid.style.flexDirection = FlexDirection.Row;
            plotGrid.style.flexWrap = Wrap.Wrap;
            bs.Add(plotGrid);
            bedsLine = StationPage.Text("", "st-line");
            bs.Add(bedsLine);
            picker = StationPage.Card();
            picker.style.flexDirection = FlexDirection.Column;
            picker.style.marginTop = 8f;
            pickerTitle = StationPage.Text("", "st-line");
            picker.Add(pickerTitle);
            cropRow = new VisualElement();
            cropRow.style.flexDirection = FlexDirection.Row;
            cropRow.style.flexWrap = Wrap.Wrap;
            picker.Add(cropRow);
            pickerInfo = StationPage.Text("", "st-line");
            pickerInfo.AddToClassList("st-muted");
            picker.Add(pickerInfo);
            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            repeatBtn = FarmBtn("↻ Replant on repeat", () => { var p = Picked(); if (p != null) L?.SetPlotCrop(p, p.crop, !p.repeat); Refresh(); });
            repeatBtn.style.flexGrow = 1f;
            actions.Add(repeatBtn);
            actions.Add(FarmBtn("Clear plot", () => { var p = Picked(); if (p != null) L?.SetPlotCrop(p, "", p.repeat); Refresh(); }));
            picker.Add(actions);
            bs.Add(picker);

            // 4. food out
            var fs = Section(col, "FOOD OUT");
            var foodCard = StationPage.Card();
            var icon = StationPage.Icon(Res.Potato, "st-flow-icon");
            icon.style.marginRight = 14f;
            icon.style.marginBottom = 0f;
            foodCard.Add(icon);
            var foodCol = new VisualElement(); foodCol.AddToClassList("st-col");
            yieldLine = StationPage.Text("", "st-line");
            keptLine = StationPage.Text("", "st-line");
            keptLine.AddToClassList("st-muted");
            // Wrap inside the card rather than run off its right edge
            // (Kevin's 2026-09-28 screenshot: "carried in by the farm…").
            foodCol.style.flexGrow = 1f;
            foodCol.style.flexShrink = 1f;
            foodCol.style.minWidth = 0f;
            yieldLine.style.whiteSpace = WhiteSpace.Normal;
            keptLine.style.whiteSpace = WhiteSpace.Normal;
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

            // 3. the plots
            FillPlots(l);
            int ripe = lastRipe;

            // 4. food out: crops in the store, and how long the camp eats.
            int crops = 0;
            foreach (var c in FoodBook.Crops) crops += l.StoreCountOf(c.res);
            yieldLine.text = $"{crops} crops in store · carried in by the farmhand";
            float days = SheetBits.FoodDays(l);
            keptLine.text = days < 0f
                ? SheetBits.FoodDaysLine(l)
                : $"{l.FoodFill():0.#} meals of food · lasts {days:0.#} days";

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
