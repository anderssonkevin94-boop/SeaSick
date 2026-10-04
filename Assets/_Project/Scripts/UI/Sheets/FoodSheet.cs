using System;
using System.Collections.Generic;
using System.Text;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **Food, in one place (island UI phase 4, 2026-09-30).** Kevin (top bar,
    /// 2026-09-29): *"food is good that it shows how many days i have food
    /// for. give it a up or down arrow showing which way the food will go at
    /// its current rate. pressing on it should show what food is consumed,
    /// and how much is left."* The Larder sheet (rations, saved dishes, the
    /// store) was a second home for the same topic; rule 3, "one home per
    /// topic", folded it in here and `LarderSheet` is gone.
    ///
    /// Top to bottom: the headline (days of food and the arrow with the net
    /// fill a day, and when the store is empty at that pace); EATEN (who eats
    /// how much, the next meal, what they ate last); COMING IN (each food's
    /// net pace a day: plots, fishers, hunters, the kitchen); RATIONS (Full /
    /// Half / None); DISHES (everything edible the camp holds, with a Save /
    /// Unsave switch on each -- a saved dish is kept for the ship and never
    /// eaten). Stock counts the store AND station boxes, as `FoodFill` does
    /// since 2026-09-30 (fish in the fishing hut's box is camp food).
    ///
    /// **Where food is short it carries its fix** (rule 2), at most two, on
    /// the row that names the problem: the next meal is raw or missing
    /// (Cook at the kitchen / Gather food), or nothing is coming in (Assign
    /// a farmhand / Assign a fisher / Build a farm plot / Build a fishing
    /// hut). The thumb row repeats the first of them as the one main action,
    /// and is hidden when food is fine.
    ///
    /// All figures per SKY day ("Day N" on the bar); "fill" is the larder's
    /// unit, one fill feeds one hand for a day.
    public sealed class FoodSheet : ISheetFramed
    {
        readonly Outpost outpost;

        public FoodSheet(Outpost camp)
        {
            outpost = camp;
            // Made once: `Refresh` runs all the time and must not allocate a
            // closure per row per pass.
            runEat = () => RunFix(fixEat);
            runIn = () => RunFix(fixIn);
            toggles = new Action[FoodBook.Edibles.Length];
            for (int i = 0; i < toggles.Length; i++)
            {
                string res = FoodBook.Edibles[i].res;
                toggles[i] = () =>
                {
                    var l = L;
                    if (l == null) return;
                    l.SetDishSaved(res, !l.DishSaved(res));
                    Refresh();
                };
            }
        }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        // --- frame --------------------------------------------------------------

        public string Title => "Food";
        public Vector3 AnchorWorld => outpost != null ? outpost.CampCentre : Vector3.zero;
        public bool StillValid => outpost != null && outpost.Ledger != null;
        public Color Accent => SheetTheme.Moss;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public bool WantsTallSheet => true;

        Label subtitle;

        public VisualElement BuildHeader() =>
            CampPages.Header("Food", out subtitle, () => CampPages.OpenLedger(WorkersSheet.OpenLedger, outpost));

        // The one main action: the first fix the sheet found, else none.
        Button mainBtn;
        VisualElement actionsRow;
        ShortFix.Fix fixMain;

        public VisualElement BuildActions()
        {
            mainBtn = SheetKit.Btn(fixMain.Valid ? fixMain.label : "", () => RunFix(fixMain), true);
            actionsRow = SheetKit.Actions(mainBtn);
            // Once the host has put the row in its strip, hide the strip at
            // once when food needs nothing (else it shows empty until the
            // first refresh).
            actionsRow.RegisterCallback<AttachToPanelEvent>(_ => BindMain());
            return actionsRow;
        }

        // --- body ----------------------------------------------------------------

        VisualElement rootEl;
        Label big, small, eatHead, inHead, rationHead, dishHead, rationNote, emptyDishes, note;
        ReadoutUi.Lines eaten, coming, dishes;
        VisualElement rationSeg;
        int rationKey = -1;
        readonly Action runEat, runIn;
        readonly Action[] toggles;
        ShortFix.Fix fixEat, fixIn;

        static readonly string[] RationOptions = { "Full", "Half", "None" };

        public VisualElement Build()
        {
            rootEl = ReadoutUi.Root(out var col);
            col.Add(ReadoutUi.Headline(out big, out small));

            eatHead = ReadoutUi.Eyebrow(col);
            eaten = new ReadoutUi.Lines(col);

            inHead = ReadoutUi.Eyebrow(col);
            coming = new ReadoutUi.Lines(col);

            rationHead = ReadoutUi.Eyebrow(col);
            rationHead.text = "RATIONS";
            rationSeg = SheetKit.Segmented(RationOptions, 0, i =>
            {
                var l = L;
                if (l == null) return;
                l.rations = (Rations)i;
                Refresh();
            });
            rationSeg.AddToClassList("fd-rations");
            col.Add(rationSeg);
            rationNote = CampPages.Classed(new Label(), "cp-note");
            col.Add(rationNote);

            dishHead = ReadoutUi.Eyebrow(col);
            dishes = new ReadoutUi.Lines(col);
            emptyDishes = CampPages.Classed(new Label("The store holds no food."), "cp-note");
            col.Add(emptyDishes);
            note = CampPages.Classed(new Label("Paces are a whole day's average, nights included."), "cp-note");
            col.Add(note);

            Refresh();
            return rootEl;
        }

        void RunFix(ShortFix.Fix fix)
        {
            if (fix.Valid && fix.Run(outpost)) Refresh();
        }

        readonly Dictionary<string, int> lastMeals = new Dictionary<string, int>();
        readonly StringBuilder sb = new StringBuilder();

        public void Refresh()
        {
            var l = L;
            if (l == null || rootEl == null) return;
            outpost.CatchUp();

            float eat = CampReadouts.FoodEatenPerDay(l);
            float inflow = CampReadouts.FoodInPerDay(l);
            float trend = inflow - eat;
            float days = CampReadouts.FoodDays(l);
            float fillNow = l.FoodFill();

            // --- headline ------------------------------------------------------
            if (l.hands.Count == 0) { ReadoutUi.SetText(big, "--"); }
            else if (days < 0f) { ReadoutUi.SetText(big, "--"); }
            else ReadoutUi.SetText(big, (days >= 10f ? days.ToString("0") : days.ToString("0.#"))
                                      + (Mathf.Abs(days - 1f) < 0.05f ? " day" : " days"));

            string arrow = trend > 0.05f ? "up" : trend < -0.05f ? "down" : "";
            string line;
            if (l.hands.Count == 0) line = "nobody to feed";
            else if (eat <= 1e-4f) line = l.rations == Rations.None ? "rations off: nobody eats" : "nobody is eating";
            else if (arrow.Length == 0) line = "steady";
            else
            {
                line = $"{CampReadouts.Signed(trend)} fill a day";
                if (trend < 0f) line += $" · empty in {fillNow / -trend:0.#} days";
            }
            ReadoutUi.SetText(small, line);
            ReadoutUi.Tone(small, trend > 0.05f ? "ok" : trend < -0.05f ? "bad" : "");
            if (subtitle != null)
                ReadoutUi.SetText(subtitle, $"{fillNow:0.#} fill in store · {l.hands.Count} to feed");

            FindFixes(l, eat, trend, days);
            FillEaten(l, eat);
            FillComing(l, inflow);
            FillRations(l);
            FillDishes(l, eat, fillNow);
            BindMain();
        }

        // --- the fixes -------------------------------------------------------------
        //
        // Two, at most: `fixEat` for the meal problem (raw or nothing to eat),
        // `fixIn` for "not enough is coming in". Structs, no closures, so the
        // 0.25 s refresh allocates nothing for them.

        Building FirstBuilt(string planId)
        {
            foreach (var b in outpost.Built)
                if (b != null && b.Id == planId) return b;
            return null;
        }

        static bool Manned(OutpostLedger l, string planId) => l.HandsOn(OutpostOrder.Work, planId) > 0;

        void FindFixes(OutpostLedger l, float eat, float trend, float days)
        {
            fixEat = default;
            fixIn = default;
            if (l.hands.Count == 0 || eat <= 1e-4f) return;

            // Short: hungry now, under three days left, or falling toward it.
            bool needFood = l.Hungry || days < 3f || (trend < -0.05f && days < 6f);

            // The next meal is raw (a kitchen makes it a dish) or there is none.
            string next = l.BestMeal();
            if (next == null)
            {
                fixEat = ShortFix.For(outpost, Res.Food);
                fixEat.label = "Gather food";
            }
            else if (FoodBook.Edible(next) is EdibleDef e && e.raw)
            {
                var kitchen = FirstBuilt(BuildPlans.Kitchen.id);
                fixEat = kitchen != null
                    ? new ShortFix.Fix { kind = ShortFix.Kind.Station, station = kitchen, label = "Cook at the kitchen" }
                    : new ShortFix.Fix { kind = ShortFix.Kind.Build, planId = BuildPlans.Kitchen.id, label = "Build a kitchen" };
            }

            if (!needFood) return;
            var farm = FirstBuilt(BuildPlans.Farm.id);
            var hut = FirstBuilt(BuildPlans.FishingHut.id);
            if (farm != null && !Manned(l, BuildPlans.Farm.id))
                fixIn = new ShortFix.Fix { kind = ShortFix.Kind.Station, station = farm, label = "Assign a farmhand" };
            else if (hut != null && !Manned(l, BuildPlans.FishingHut.id))
                fixIn = new ShortFix.Fix { kind = ShortFix.Kind.Station, station = hut, label = "Assign a fisher" };
            else if (farm == null)
                fixIn = new ShortFix.Fix { kind = ShortFix.Kind.Build, planId = BuildPlans.Farm.id, label = "Build a farm plot" };
            else if (hut == null)
                fixIn = new ShortFix.Fix { kind = ShortFix.Kind.Build, planId = BuildPlans.FishingHut.id, label = "Build a fishing hut" };
        }

        /// The thumb row: the first fix found; the whole row is hidden when
        /// food needs nothing.
        void BindMain()
        {
            fixMain = fixEat.Valid ? fixEat : fixIn;
            if (mainBtn == null) return;
            var host = actionsRow != null ? actionsRow.parent : null;   // SheetHost's strip
            var want = fixMain.Valid ? DisplayStyle.Flex : DisplayStyle.None;
            if (host != null && host.style.display != want) host.style.display = want;
            if (fixMain.Valid && mainBtn.text != fixMain.label) mainBtn.text = fixMain.label;
        }

        // --- eaten ---------------------------------------------------------------

        void FillEaten(OutpostLedger l, float eat)
        {
            ReadoutUi.SetText(eatHead, $"EATEN · {eat:0.#} FILL A DAY");
            eaten.Begin();
            int eaters = CampReadouts.Eaters(l);
            int down = l.hands.Count - eaters;
            if (l.rations == Rations.None)
                eaten.Add("0", "bad", $"{eaters} hands on no rations", "they starve: set rations below");
            else if (eaters > 0)
            {
                string each = l.rations == Rations.Half ? "½ fill a day (half rations)" : "1 fill a day";
                eaten.Add(CampReadouts.Signed(-eat), "bad", $"{eaters} hands × {each}",
                    l.rations == Rations.Half ? "half rations lower their mood" : null);
            }
            if (down > 0) eaten.Add("", "", $"{down} down, not eating", null);

            if (eaters > 0 && l.rations != Rations.None)
            {
                string next = l.BestMeal();
                if (next == null)
                    eaten.Add("", "bad", "Next meal: nothing left to eat",
                        l.FoodFill(true) > l.FoodFill() ? "only saved dishes left: unsave one below" : null,
                        fixEat.Valid ? fixEat.label : null, fixEat.Valid ? runEat : null);
                else
                {
                    var e = FoodBook.Edible(next);
                    string bonus = FoodBook.BonusLine(next);
                    eaten.Add("", "", "Next meal: " + CampReadouts.Label(next)
                                      + (e != null && e.raw ? " (raw)" : ""),
                        e != null && e.raw ? "raw only when no cooked dish is left"
                            : string.IsNullOrEmpty(bonus) ? null : bonus,
                        fixEat.Valid ? fixEat.label : null, fixEat.Valid ? runEat : null);
                }
            }

            lastMeals.Clear();
            foreach (var h in l.hands)
            {
                if (h == null || string.IsNullOrEmpty(h.lastMeal)) continue;
                lastMeals.TryGetValue(h.lastMeal, out int n);
                lastMeals[h.lastMeal] = n + 1;
            }
            if (lastMeals.Count > 0)
            {
                sb.Clear();
                foreach (var kv in lastMeals)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(kv.Value).Append(' ').Append(CampReadouts.Label(kv.Key));
                }
                eaten.Add("", "", "Last ate: " + sb, null);
            }
            eaten.End();
        }

        // --- coming in -----------------------------------------------------------------

        void FillComing(OutpostLedger l, float inflow)
        {
            ReadoutUi.SetText(inHead, $"COMING IN · {CampReadouts.Signed(inflow)} FILL A DAY");
            coming.Begin();
            bool unmannedFarm = false;
            foreach (var res in CampReadouts.FoodLines)
            {
                float net = CampReadouts.NetPerDay(l, res);
                CampReadouts.PlotUnitsPerDay(l, res, out int plots, out bool unmanned);
                unmannedFarm |= unmanned;
                if (Mathf.Abs(net) < 0.05f) continue;
                float per = FoodBook.Fill(res);
                bool saved = l.DishSaved(res);
                string text = CampReadouts.Cap(CampReadouts.Label(res)) + " · " + Source(res, net, plots);
                string hint = per <= 0f ? "no fill until cooked"
                    : saved ? "saved for the ship, not counted"
                    : $"{CampReadouts.Signed(net * per)} fill a day";
                coming.Add(CampReadouts.Signed(net) + "/d", net > 0f ? "ok" : "bad", text, hint);
            }
            if (unmannedFarm)
                coming.Add("", "warm", "Farm plots with nobody to tend them", "put a farmhand on the farm");
            // The fix sits on the row that says the problem: the first thing
            // that is not there yet, or a bare "nothing coming in".
            if (coming.Count == 0)
                coming.Add("", "bad", "Nothing coming in",
                    "a farm plot, a fisher at the fishing hut, or a hunter; cook at the kitchen",
                    fixIn.Valid ? fixIn.label : null, fixIn.Valid ? runIn : null);
            else if (fixIn.Valid)
                coming.Add("", "warm", "Not enough coming in", "food is running short",
                    fixIn.label, runIn);
            coming.End();
        }

        static string Source(string res, float net, int plots)
        {
            if (net < 0f) return "used for cooking";
            if (plots > 0) return plots == 1 ? "1 farm plot" : $"{plots} farm plots";
            if (res == Res.Fish) return "fishers";
            if (res == Res.Meat) return "hunters";
            if (res == Res.Food) return "foraging";
            if (FoodBook.IsDish(res)) return "kitchen";
            return "coming in";
        }

        // --- rations -----------------------------------------------------------------

        void FillRations(OutpostLedger l)
        {
            int sel = (int)l.rations;
            if (sel == rationKey) return;
            rationKey = sel;
            SheetKit.SetSegmented(rationSeg, sel);
            ReadoutUi.SetText(rationNote, l.rations == Rations.Full ? "Everyone eats a full fill a day."
                : l.rations == Rations.Half ? "Half a fill a day: the food lasts twice as long, and moods drop."
                : "Nobody eats. They starve.");
        }

        // --- dishes ----------------------------------------------------------------------

        /// Whole units of `res` on station output racks (the fishing hut's
        /// box, the kitchen's rack): camp food, as `FoodFill` counts it.
        static int RackUnits(OutpostLedger l, string res)
        {
            var st = l.Stations;
            int n = 0;
            for (int i = 0; i < st.Count; i++) if (st[i] != null) n += st[i].RackCount(res);
            return n;
        }

        void FillDishes(OutpostLedger l, float eat, float fillNow)
        {
            ReadoutUi.SetText(dishHead, $"DISHES · {fillNow:0.#} FILL");
            dishes.Begin();
            for (int i = 0; i < FoodBook.Edibles.Length; i++)
            {
                var e = FoodBook.Edibles[i];
                int store = l.StoreCountOf(e.res);
                int box = RackUnits(l, e.res);
                int count = store + box;
                if (count <= 0) continue;
                float per = FoodBook.Fill(e.res);
                bool saved = l.DishSaved(e.res);
                string val = saved ? "saved" : eat > 1e-4f ? $"{count * per / eat:0.#} d" : "";
                string hint = saved
                    ? "saved for the ship, not eaten"
                    : $"{per:0.##} fill each" + (e.raw ? " · raw" : "")
                      + (box > 0 ? $" · {box} in station boxes" : "")
                      + (eat > 1e-4f ? " · days it feeds everyone alone" : "");
                dishes.Add(val, saved ? "warm" : "ok", $"{CampReadouts.Cap(CampReadouts.Label(e.res))} × {count}", hint,
                    saved ? "Unsave" : "Save", toggles[i]);
            }

            // The kitchen's fill-less stock, on one line: it is food, not yet.
            sb.Clear();
            foreach (var res in CampReadouts.FoodLines)
            {
                if (FoodBook.IsEdible(res)) continue;
                int n = l.StoreCountOf(res);
                if (n <= 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(n).Append(' ').Append(CampReadouts.Label(res));
            }
            int shown = dishes.Count;
            if (sb.Length > 0) dishes.Add("", "", "For the kitchen: " + sb, "not eaten until cooked");
            dishes.End();
            ReadoutUi.Show(emptyDishes, shown == 0);
        }
    }
}
