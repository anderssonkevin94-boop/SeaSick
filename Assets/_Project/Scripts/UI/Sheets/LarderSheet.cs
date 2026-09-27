using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The food view (menu-audit #4, 2026-09-27).** The "Out of food" and
    /// "Food · under a day" chips used to open the People roster, which has
    /// no food on it at all. This is the card they open instead: what is
    /// cooked and raw in the store (with its fill value), what the kitchen
    /// and the farm are making right now, how many days that buys the camp,
    /// and the rations switch -- moved here from the old `FireSheet` orders
    /// page, which is being retired plan by plan.
    ///
    /// **It reads and it calls; it never decides.** Every number comes off
    /// `OutpostLedger` (`FoodBook`, `DaysOfFood`, `DishSaved`); every button
    /// is a verb the ledger already had (`SetDishSaved`, `rations`).
    public class LarderSheet : ISheetFramed
    {
        readonly Outpost outpost;

        public LarderSheet(Outpost outpost)
        {
            this.outpost = outpost;
        }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        // --- frame -------------------------------------------------------------

        public string Title => "Larder";
        public Color Accent => SheetTheme.Moss;
        public bool WantsTallSheet => true;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public VisualElement BuildActions() => null;

        public bool StillValid => outpost != null && outpost.Ledger != null;

        public Vector3 AnchorWorld => outpost != null ? outpost.CampCentre : Vector3.zero;

        StationPage.Header header;

        public VisualElement BuildHeader()
        {
            header = new StationPage.Header(Title, false, () => StationPage.OpenLedgerFor(outpost));
            return header.Root;
        }

        // --- pieces kept between refreshes --------------------------------------

        VisualElement root;
        Label daysBig, daysSmall;
        VisualElement rationsHolder;
        int rationsKey = -99;
        VisualElement dishesHolder;
        Label dishesEmpty;
        VisualElement kitchenHolder, farmHolder;

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

            // --- days of food + rations -------------------------------------
            var top = new VisualElement(); top.AddToClassList("st-section");
            top.style.marginTop = 0f;
            var daysCard = StationPage.Card();
            daysCard.style.flexDirection = FlexDirection.Column;
            daysCard.Add(StationPage.Text("DAYS OF FOOD", "st-eyebrow"));
            var nums = new VisualElement(); nums.style.flexDirection = FlexDirection.Row;
            nums.style.alignItems = Align.FlexEnd;
            daysBig = StationPage.Text("", "st-worker-name");
            daysBig.style.fontSize = 28f;
            daysSmall = StationPage.Text("", "st-worker-sub");
            daysSmall.style.marginLeft = 6f;
            nums.Add(daysBig); nums.Add(daysSmall);
            daysCard.Add(nums);
            top.Add(daysCard);

            top.Add(StationPage.Text("RATIONS", "st-eyebrow"));
            rationsHolder = new VisualElement();
            top.Add(rationsHolder);
            col.Add(top);

            // --- dishes & raw food -------------------------------------------
            var stock = new VisualElement(); stock.AddToClassList("st-section");
            stock.Add(StationPage.Text("IN THE STORE", "st-eyebrow"));
            dishesHolder = new VisualElement();
            stock.Add(dishesHolder);
            dishesEmpty = StationPage.Text("nothing in the store", "st-line");
            stock.Add(dishesEmpty);
            col.Add(stock);

            // --- kitchen & farm -------------------------------------------------
            var making = new VisualElement(); making.AddToClassList("st-section");
            making.Add(StationPage.Text("MAKING", "st-eyebrow"));
            kitchenHolder = new VisualElement();
            making.Add(kitchenHolder);
            farmHolder = new VisualElement();
            making.Add(farmHolder);
            col.Add(making);

            Refresh();
            return root;
        }

        // --- refresh -------------------------------------------------------------

        public void Refresh()
        {
            var l = L;
            if (outpost == null || l == null || root == null) return;
            outpost.CatchUp();

            float days = SheetBits.FoodDays(l);
            if (l.hands.Count == 0) { daysBig.text = "--"; daysSmall.text = "nobody to feed"; }
            else if (days < 0f) { daysBig.text = "0"; daysSmall.text = "they eat nothing"; }
            else { daysBig.text = days >= 10f ? $"{days:0}" : $"{days:0.#}"; daysSmall.text = "days"; }

            FillRations(l);
            FillDishes(l);
            FillKitchen(l);
            FillFarm(l);
        }

        // --- rations ---------------------------------------------------------------

        static readonly string[] RationOptions = { "full", "half", "none" };

        void FillRations(OutpostLedger l)
        {
            int sel = (int)l.rations;
            if (sel == rationsKey) return;
            rationsKey = sel;
            SheetBits.Swap(rationsHolder, SheetKit.Segmented(RationOptions, sel, i =>
            {
                l.rations = (Rations)i;
                rationsKey = i;
                Refresh();
            }));
        }

        // --- dishes & raw food -------------------------------------------------------

        void FillDishes(OutpostLedger l)
        {
            dishesHolder.Clear();
            int shown = 0;
            foreach (var e in FoodBook.Edibles)
            {
                int count = l.StoreCountOf(e.res);
                if (count <= 0) continue;
                shown++;
                float fill = count * FoodBook.Fill(e.res);
                bool saved = l.DishSaved(e.res);
                string label = StationPage.Cap(ResDefs.Label(e.res));

                var row = StationPage.Card();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 6f;
                row.Add(StationPage.Icon(e.res, "st-small-icon"));
                var words = new VisualElement(); words.style.flexGrow = 1f;
                words.style.marginLeft = 8f;
                words.Add(StationPage.Text(label, "st-line"));
                var sub = StationPage.Text($"{count} kept · {fill:0.#} fill" + (e.raw ? " · raw" : ""), "st-line");
                sub.AddToClassList("st-muted");
                words.Add(sub);
                row.Add(words);

                var saveBtn = new Button(() =>
                {
                    l.SetDishSaved(e.res, !l.DishSaved(e.res));
                    Refresh();
                }) { text = saved ? "Saved" : "Save" };
                saveBtn.AddToClassList("st-seg-btn");
                saveBtn.EnableInClassList("st-seg-btn--on", saved);
                saveBtn.style.minHeight = 36f;
                saveBtn.style.minWidth = 72f;
                row.Add(saveBtn);

                dishesHolder.Add(row);
            }
            dishesEmpty.style.display = shown == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // --- kitchen -----------------------------------------------------------------

        void FillKitchen(OutpostLedger l)
        {
            kitchenHolder.Clear();
            var building = FirstBuilt(BuildPlans.Kitchen.id);
            if (building == null)
            {
                kitchenHolder.Add(StationPage.Text("kitchen not built yet", "st-line"));
                return;
            }
            string sub = "idle";
            foreach (var st in l.Stations)
            {
                if (st == null || st.planId != BuildPlans.Kitchen.id) continue;
                var order = l.OrderAt(BuildPlans.Kitchen.id, st.ordinal);
                if (order.Active && order.recipe != null) sub = "making " + order.recipe.label;
                break;
            }
            var row = MakingRow("Kitchen", sub, () => Sheets.Open(new StationSheet(outpost, building)));
            kitchenHolder.Add(row);
        }

        // --- farm --------------------------------------------------------------------

        void FillFarm(OutpostLedger l)
        {
            farmHolder.Clear();
            var building = FirstBuilt(BuildPlans.Farm.id);
            if (building == null)
            {
                farmHolder.Add(StationPage.Text("farm not built yet", "st-line"));
                return;
            }
            int ripe = 0, growing = 0;
            if (l.plots != null)
                foreach (var p in l.plots)
                {
                    if (p == null) continue;
                    if (p.state == PlotState.Ripe) ripe++;
                    else if (p.state == PlotState.Growing) growing++;
                }
            string sub = $"{ripe} ripe · {growing} growing";
            var row = MakingRow("Farm", sub, () => Sheets.Open(new FarmSheet(outpost, building)));
            farmHolder.Add(row);
        }

        // --- shared --------------------------------------------------------------------

        Building FirstBuilt(string planId)
        {
            if (outpost == null) return null;
            foreach (var b in outpost.Built)
                if (b != null && b.Id == planId) return b;
            return null;
        }

        static VisualElement MakingRow(string name, string sub, System.Action onTap)
        {
            var btn = new Button(() => onTap?.Invoke()) { text = "" };
            btn.AddToClassList("st-worker-person");
            btn.style.marginTop = 6f;
            var words = new VisualElement(); words.AddToClassList("st-worker-words");
            words.pickingMode = PickingMode.Ignore;
            words.Add(StationPage.Text(name, "st-worker-name"));
            words.Add(StationPage.Text(sub, "st-worker-sub"));
            btn.Add(words);
            var chevron = StationPage.Text("›", "st-worker-chevron");
            chevron.pickingMode = PickingMode.Ignore;
            btn.Add(chevron);
            return btn;
        }
    }
}
