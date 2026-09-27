using SeaSick.Save;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **"While you were away" (time away, 2026-09-27).** What every camp did
    /// while the app was closed (`AwayProgress.Last`): how long, whether the
    /// 12 h cap was hit, and per camp what was built, brought in, eaten,
    /// who went hungry or unhappy and what stalled. Tap a camp -> its
    /// overview. On a resume it opens while the catch-up runs and shows the
    /// progress until the report is in. Reads only; decides nothing.
    public class AwaySheet : ISheetFramed
    {
        public string Title => "While you were away";
        public Color Accent => SheetTheme.Moss;
        public bool WantsTallSheet => true;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public VisualElement BuildActions() => null;
        public bool StillValid => true;
        public Vector3 AnchorWorld => Outpost.Home != null ? Outpost.Home.CampCentre : Vector3.zero;

        StationPage.Header header;
        VisualElement root, col;
        bool filled;

        public VisualElement BuildHeader()
        {
            header = new StationPage.Header(Title, false, null);
            return header.Root;
        }

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
            col = new VisualElement();
            col.AddToClassList("st-content");
            scroll.Add(col);
            filled = false;
            Refresh();
            return root;
        }

        public void Refresh()
        {
            if (col == null) return;
            if (AwayProgress.Running)
            {
                col.Clear();
                filled = false;
                header?.SetSub("catching up…");
                col.Add(StationPage.Text($"Your camps kept working… {Mathf.RoundToInt(AwayProgress.Progress01 * 100f)}%", "st-line"));
                return;
            }
            if (filled) return;
            filled = true;
            col.Clear();
            var r = AwayProgress.Last;
            if (r == null) { col.Add(StationPage.Text("Nothing happened.", "st-line")); return; }

            header?.SetSub(AwayProgress.Span(r.awaySeconds) + (r.capped ? " · capped at 12 h" : ""));

            foreach (var c in r.camps) col.Add(CampCard(c));

            var foot = StationPage.Text(
                (r.capped ? "Time away counts up to 12 h. " : "") + "Raids wait for you."
                + $"  ({r.wallSeconds:0.0} s to catch up{(r.stride > 1 ? $", step ×{r.stride}" : "")}{(r.dropped ? ", cut short" : "")})",
                "st-line");
            foot.AddToClassList("st-muted");
            foot.style.marginTop = 10f;
            foot.style.whiteSpace = WhiteSpace.Normal;
            col.Add(foot);
        }

        VisualElement CampCard(AwayProgress.CampReport c)
        {
            var card = StationPage.Card();
            card.style.flexDirection = FlexDirection.Column;
            card.style.marginTop = 8f;

            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems = Align.Center;
            var name = StationPage.Text(StationPage.Cap(c.name), "st-worker-name");
            name.style.flexGrow = 1f;
            top.Add(name);
            if (c.camp != null) top.Add(StationPage.Text("Open ›", "st-line"));
            card.Add(top);

            if (c.built.Count > 0) card.Add(Line("Built", string.Join(", ", c.built)));
            if (c.born.Count > 0) card.Add(Line("New hands", string.Join(", ", c.born)));
            if (c.got.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int k = 0; k < c.got.Count && k < 6; k++)
                    sb.Append(k > 0 ? " · " : "").Append($"{c.got[k].Value:0} {ResDefs.Label(c.got[k].Key)}");
                if (c.got.Count > 6) sb.Append($" · +{c.got.Count - 6} more");
                card.Add(Line("Brought in", sb.ToString()));
            }
            if (c.hands > 0)
                card.Add(Line($"Eaten by {c.hands} hands", c.eaten > 0f ? $"{c.eaten:0.#} fill" : "nothing"));
            card.Add(Line("Hungry", c.hungryDays > 0.01f
                ? AwayProgress.Span(c.hungryDays * TimeOfDay.DayLength) : "never"));
            if (c.unhappy > 0) card.Add(Line("Unhappy", $"{c.unhappy} of {c.hands}"));
            if (c.stall != null)
                card.Add(Line("Stalled", AwayProgress.Span(c.stalledDays * TimeOfDay.DayLength) + " · " + c.stall));

            if (c.camp != null)
            {
                var camp = c.camp;
                card.RegisterCallback<ClickEvent>(_ =>
                {
                    if (camp != null && camp.Ledger != null) Sheets.Open(new CampOverviewSheet(camp));
                });
            }
            return card;
        }

        static VisualElement Line(string key, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 4f;
            var k = StationPage.Text(key, "st-line");
            k.AddToClassList("st-muted");
            k.style.minWidth = 110f;
            var v = StationPage.Text(value, "st-line");
            v.style.flexGrow = 1f;
            v.style.flexShrink = 1f;
            v.style.whiteSpace = WhiteSpace.Normal;
            row.Add(k); row.Add(v);
            return row;
        }
    }
}
