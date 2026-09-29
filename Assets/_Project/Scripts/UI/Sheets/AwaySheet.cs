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
        public string Title => "Welcome back";
        public Color Accent => SheetTheme.Moss;
        public bool WantsTallSheet => true;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public VisualElement BuildActions() => null;
        public bool StillValid => true;
        public Vector3 AnchorWorld => Outpost.Home != null ? Outpost.Home.CampCentre : Vector3.zero;

        WatchTiles.Head header;
        VisualElement root, col;
        bool filled;

        public VisualElement BuildHeader()
        {
            // CardKit's head (glyph + title/sub + close, no dead menu
            // button): the old `StationPage.Header` always drew a "☰"
            // square that opened nothing here (no ledger to jump to), and
            // ate the width the title needed to fit on one line.
            header = CardKit.Head("clock", Title);
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
                + $"  ({r.Timing})",
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
            name.style.flexShrink = 1f;
            top.Add(name);
            if (c.camp != null)
            {
                var open = StationPage.Text("Open ›", "st-pill-text");
                open.style.marginLeft = 10f;
                var chip = new VisualElement();
                chip.AddToClassList("st-pill");
                chip.style.flexShrink = 0f;
                chip.style.marginLeft = 10f;
                chip.style.marginRight = 0f;
                chip.Add(open);
                top.Add(chip);
            }
            card.Add(top);

            // Only the rows with something to say -- a row nobody can read
            // ("Hungry: never" on every quiet camp) is noise, not status.
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
                card.Add(Line("Eaten", $"{c.hands} hand{(c.hands == 1 ? "" : "s")}"
                    + (c.eaten > 0f ? $" · {c.eaten:0.#} fill" : " · nothing to eat")));
            if (c.hungryDays > 0.01f)
                card.Add(Line("Hungry", AwayProgress.Span(c.hungryDays * TimeOfDay.DayLength)));
            if (c.unhappy > 0) card.Add(Line("Unhappy", $"{c.unhappy} of {c.hands}"));
            if (c.stall != null)
                card.Add(Line("Stalled", AwayProgress.Span(c.stalledDays * TimeOfDay.WorkDaySeconds) + " · " + c.stall));

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
            k.style.flexShrink = 0f;
            k.style.marginRight = 8f;
            var v = StationPage.Text(value, "st-line");
            v.style.flexGrow = 1f;
            v.style.flexShrink = 1f;
            v.style.whiteSpace = WhiteSpace.Normal;
            row.Add(k); row.Add(v);
            return row;
        }
    }
}
