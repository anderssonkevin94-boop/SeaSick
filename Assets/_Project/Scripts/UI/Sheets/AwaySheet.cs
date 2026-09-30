using System.Collections.Generic;
using SeaSick.Save;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **"Welcome back" (time away, 2026-09-27; Melvor-style report,
    /// 2026-09-30).** What every camp did while the app was closed
    /// (`AwayProgress.Last`), one block per camp with the busiest first, and
    /// inside a block only the sections that have something in them, in this
    /// order: Events (raids, deaths, new hands, anyone downed or back up --
    /// the worst first, tinted), Made (+N products), Gathered (+N raw),
    /// Spent (-N), Eaten (-N plus a hunger note) and Built ("2 x road").
    /// Item rows wear the item's icon when it has one and never an empty
    /// box. "Open ›" on a camp opens its overview; the one action in the
    /// thumb row is "Back to camp". On a resume it opens while the catch-up
    /// runs and shows the progress until the report is in. Reads only;
    /// decides nothing. The work timing is dev text: editor only.
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
        VisualElement col;
        bool filled;

        public VisualElement BuildHeader()
        {
            // CardKit's head (glyph + title/sub + close, no dead menu
            // button). Its status pill has nothing to say here: hidden, or it
            // shows as an empty dark-green circle beside the title
            // (Kevin's phone screenshot, 2026-09-30).
            header = CardKit.Head("clock", Title);
            header.SetPill("", StationPage.PillGood);
            return header.Root;
        }

        public VisualElement Build()
        {
            var root = CardKit.Page(out var page);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.flexShrink = 1f;
            scroll.style.minHeight = 0f;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            page.Add(scroll);
            col = new VisualElement();
            col.AddToClassList("aw-col");
            scroll.Add(col);

            // The one action, in the thumb row.
            var acts = CardKit.Acts(root);
            CardKit.Act(acts, "Back to camp", () => Sheets.Close(), 1);

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
            if (r == null) { col.Add(Note("Nothing happened.")); return; }

            header?.SetSub("Away " + AwayProgress.Span(r.awaySeconds) + (r.capped ? " · capped at 12 h" : ""));

            // Busiest camp first: events weigh most, then how much was moved.
            var camps = new List<AwayProgress.CampReport>(r.camps);
            camps.Sort((a, b) => Weight(b).CompareTo(Weight(a)));
            int shown = 0;
            foreach (var c in camps)
            {
                if (Weight(c) <= 0) continue;
                col.Add(CampBlock(c));
                shown++;
            }
            if (shown == 0) col.Add(Note("Quiet while you were away: nothing was made, gathered or built."));

            col.Add(Note((r.capped ? "Time away counts up to 12 h. " : "") + "Raids wait for you."
                + (Application.isEditor ? $"  ({r.Timing})" : "")));
        }

        static int Weight(AwayProgress.CampReport c) =>
            c.events.Count * 100 + c.made.Count + c.gathered.Count + c.spent.Count + c.ate.Count
            + c.built.Count + (c.hungryDays > 0.01f ? 1 : 0) + (c.stall != null ? 1 : 0);

        static Label Note(string text)
        {
            var l = StationPage.Text(text, "st-line");
            l.AddToClassList("st-muted");
            l.style.marginTop = 10f;
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        // --- one camp ------------------------------------------------------------

        VisualElement CampBlock(AwayProgress.CampReport c)
        {
            var card = StationPage.Card();
            card.style.flexDirection = FlexDirection.Column;
            card.style.alignItems = Align.Stretch;
            card.style.marginTop = 10f;

            // Name and "Open ›": the whole row is the tap (a 44+ tall target).
            var top = new VisualElement();
            top.AddToClassList("aw-top");
            var name = StationPage.Text(StationPage.Cap(c.name), "st-worker-name");
            name.style.flexGrow = 1f;
            name.style.flexShrink = 1f;
            top.Add(name);
            if (c.camp != null)
            {
                var chip = new VisualElement();
                chip.AddToClassList("st-pill");
                chip.style.flexShrink = 0f;
                chip.style.marginRight = 0f;
                chip.pickingMode = PickingMode.Ignore;
                chip.Add(StationPage.Text("Open ›", "st-pill-text"));
                top.Add(chip);
                var camp = c.camp;
                top.RegisterCallback<ClickEvent>(_ =>
                {
                    if (camp != null && camp.Ledger != null) Sheets.Open(new CampSheet(camp));
                });
            }
            card.Add(top);

            // 1. Events, the worst first.
            if (c.events.Count > 0)
            {
                Eyebrow(card, "EVENTS");
                foreach (var e in c.events)
                {
                    var row = new VisualElement();
                    row.AddToClassList("aw-ev");
                    row.AddToClassList(e.tone >= 2 ? "aw-ev--bad" : e.tone == 1 ? "aw-ev--warn" : "aw-ev--good");
                    var t = StationPage.Text(e.text, "aw-ev-t");
                    row.Add(t);
                    card.Add(row);
                }
            }

            // 2-5. What moved.
            Items(card, "MADE", c.made, "+", "aw-num--good");
            Items(card, "GATHERED", c.gathered, "+", "aw-num--good");
            Items(card, "SPENT", c.spent, "−", "aw-num--out");
            bool hungry = c.hungryDays > 0.01f;
            if (c.ate.Count > 0 || hungry)
            {
                Items(card, "EATEN", c.ate, "−", "aw-num--out");
                if (hungry)
                {
                    if (c.ate.Count == 0) Eyebrow(card, "EATEN");
                    var h = StationPage.Text("Someone went hungry for " + AwayProgress.Span(c.hungryDays * TimeOfDay.DayLength) + ".", "aw-ev-t");
                    h.AddToClassList("aw-hungry");
                    h.style.marginTop = 6f;
                    h.style.whiteSpace = WhiteSpace.Normal;
                    card.Add(h);
                }
            }

            // 6. Built, grouped.
            if (c.built.Count > 0)
            {
                Eyebrow(card, "BUILT");
                var order = new List<string>();
                var count = new Dictionary<string, int>();
                foreach (var b in c.built)
                {
                    if (!count.ContainsKey(b)) { count[b] = 0; order.Add(b); }
                    count[b]++;
                }
                foreach (var b in order)
                {
                    var row = new VisualElement();
                    row.AddToClassList("aw-row");
                    row.Add(StationPage.Text(count[b] > 1 ? $"{count[b]} × {b}" : StationPage.Cap(b), "aw-name"));
                    card.Add(row);
                }
            }

            if (c.stall != null)
            {
                var s = Note("Held up for " + AwayProgress.Span(c.stalledDays * TimeOfDay.WorkDaySeconds) + ": " + c.stall);
                card.Add(s);
            }
            return card;
        }

        static void Eyebrow(VisualElement into, string key)
        {
            var e = StationPage.Text(key, "st-eyebrow");
            e.AddToClassList("aw-eye");
            into.Add(e);
        }

        static void Items(VisualElement into, string key, List<AwaySummary.Line> lines, string sign, string numClass)
        {
            if (lines == null || lines.Count == 0) return;
            Eyebrow(into, key);
            foreach (var l in lines)
            {
                var row = new VisualElement();
                row.AddToClassList("aw-row");
                // An item with no icon is its name alone, never an empty box.
                if (ItemIconSet.Get(l.res) != null) row.Add(StationPage.Icon(l.res, "aw-ico"));
                row.Add(StationPage.Text(StationPage.Cap(ResDefs.Label(l.res)), "aw-name"));
                var n = StationPage.Text(sign + l.n, "aw-num");
                n.AddToClassList(numClass);
                row.Add(n);
                into.Add(row);
            }
        }
    }
}
