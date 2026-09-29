using System.Collections.Generic;
using System.Text;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **Where the food goes and where it comes from (top bar, 2026-09-29).**
    /// Kevin: *"food is good that it shows how many days i have food for.
    /// give it a up or down arrow showing which way the food will go at its
    /// current rate. pressing on it should show what food is consumed, and
    /// how much is left."*
    ///
    /// Headline: days of food (`CampReadouts.FoodDays`, sky days) and the
    /// arrow with the net fill a day (`FoodTrendPerDay`), and -- when it is
    /// falling -- when the store is empty at that pace. EATEN: who eats how
    /// much, what the next meal is and what they ate last. LEFT: every
    /// edible in the store with how long it feeds the camp on its own,
    /// saved dishes marked. COMING IN: each food's net pace a day (plots,
    /// fishers, hunters, the kitchen -- cooking shows as its dish rising and
    /// its raw food falling). The Larder button is where the rations and the
    /// save switches are.
    ///
    /// All figures per SKY day ("Day N" on the bar); "fill" is the larder's
    /// unit, one fill feeds one hand for a day.
    public sealed class FoodSheet : ISheetFramed
    {
        readonly Outpost outpost;

        public FoodSheet(Outpost camp) { outpost = camp; }

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
            CampPages.Header("Food", out subtitle, () => CampPages.OpenLedger(PeopleSheet.OpenLedger, outpost));

        public VisualElement BuildActions() =>
            SheetKit.Actions(SheetKit.Btn("Larder · rations", () =>
            {
                if (outpost != null) Sheets.Open(new LarderSheet(outpost));
            }, true));

        // --- body ----------------------------------------------------------------

        VisualElement rootEl;
        Label big, small, eatHead, leftHead, inHead, emptyLeft, emptyIn, note;
        ReadoutUi.Lines eaten, left, coming;

        public VisualElement Build()
        {
            rootEl = ReadoutUi.Root(out var col);
            col.Add(ReadoutUi.Headline(out big, out small));

            eatHead = ReadoutUi.Eyebrow(col);
            eaten = new ReadoutUi.Lines(col);

            leftHead = ReadoutUi.Eyebrow(col);
            left = new ReadoutUi.Lines(col);
            emptyLeft = CampPages.Classed(new Label("The store holds no food."), "cp-note");
            col.Add(emptyLeft);

            inHead = ReadoutUi.Eyebrow(col);
            coming = new ReadoutUi.Lines(col);
            emptyIn = CampPages.Classed(new Label(
                "Nothing coming in. Put a farmhand on the farm, a fisher at the fishing hut, or a hunter out; cook at the kitchen."),
                "cp-note");
            col.Add(emptyIn);
            note = CampPages.Classed(new Label("Paces are a whole day's average, nights included."), "cp-note");
            col.Add(note);

            Refresh();
            return rootEl;
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

            string arrow = trend > 0.05f ? "▲" : trend < -0.05f ? "▼" : "";
            string line;
            if (l.hands.Count == 0) line = "nobody to feed";
            else if (eat <= 1e-4f) line = l.rations == Rations.None ? "rations off: nobody eats" : "nobody is eating";
            else if (arrow.Length == 0) line = "steady";
            else
            {
                line = $"{arrow} {CampReadouts.Signed(trend)} fill a day";
                if (trend < 0f) line += $" · empty in {fillNow / -trend:0.#} days";
            }
            ReadoutUi.SetText(small, line);
            ReadoutUi.Tone(small, trend > 0.05f ? "ok" : trend < -0.05f ? "bad" : "");
            if (subtitle != null)
                ReadoutUi.SetText(subtitle, $"{fillNow:0.#} fill in store · {l.hands.Count} to feed");

            FillEaten(l, eat);
            FillLeft(l, eat);
            FillComing(l, inflow);
        }

        // --- eaten ---------------------------------------------------------------

        void FillEaten(OutpostLedger l, float eat)
        {
            ReadoutUi.SetText(eatHead, $"EATEN · {eat:0.#} FILL A DAY");
            eaten.Begin();
            int eaters = CampReadouts.Eaters(l);
            int down = l.hands.Count - eaters;
            if (l.rations == Rations.None)
                eaten.Add("0", "bad", $"{eaters} hands on no rations", "they starve: set rations in the larder");
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
                        l.FoodFill(true) > l.FoodFill() ? "only saved dishes left: unsave one in the larder" : null);
                else
                {
                    var e = FoodBook.Edible(next);
                    string bonus = FoodBook.BonusLine(next);
                    eaten.Add("", "", "Next meal: " + CampReadouts.Label(next)
                                      + (e != null && e.raw ? " (raw)" : ""),
                        e != null && e.raw ? "raw only when no cooked dish is left; cook at the kitchen"
                            : string.IsNullOrEmpty(bonus) ? null : bonus);
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

        // --- left ------------------------------------------------------------------

        void FillLeft(OutpostLedger l, float eat)
        {
            ReadoutUi.SetText(leftHead, $"LEFT · {l.FoodFill():0.#} FILL");
            left.Begin();
            foreach (var e in FoodBook.Edibles)
            {
                int count = l.StoreCountOf(e.res);
                if (count <= 0) continue;
                float per = FoodBook.Fill(e.res);
                bool saved = l.DishSaved(e.res);
                string val = saved ? "saved" : eat > 1e-4f ? $"{count * per / eat:0.#} d" : "";
                string hint = saved
                    ? "saved for the ship, not eaten"
                    : $"{per:0.##} fill each" + (e.raw ? " · raw" : "")
                      + (eat > 1e-4f ? " · days it feeds everyone alone" : "");
                left.Add(val, saved ? "warm" : "ok", $"{CampReadouts.Cap(CampReadouts.Label(e.res))} × {count}", hint);
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
            int shown = left.Count;
            if (sb.Length > 0) left.Add("", "", "For the kitchen: " + sb, "not eaten until cooked");
            left.End();
            ReadoutUi.Show(emptyLeft, shown == 0);
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
            coming.End();
            ReadoutUi.Show(emptyIn, coming.Count == 0);
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
    }
}
