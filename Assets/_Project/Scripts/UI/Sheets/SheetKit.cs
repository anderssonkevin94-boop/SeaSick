using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The parts a sheet is made of.**
    ///
    /// Every card in the game is composed out of these, so the look is a
    /// property of ONE file rather than of however many sheets get written.
    /// Kevin retunes the sheet HUD by editing `Sheets.uss` and `SheetTheme`;
    /// nothing in a card's own code carries a colour or a corner radius.
    ///
    /// Each builder returns a plain `VisualElement`, never a bespoke type, so
    /// a card can hold on to one and mutate it in `Refresh` — which is the
    /// only way a sheet is allowed to update. `Store` and `Bar` both hand back
    /// an element with the fill reachable as `ElementAt(...)`, and the helpers
    /// `SetBar` / `SetStore` below do that lookup so a card does not have to
    /// know the internal shape.
    public static class SheetKit
    {
        // ------------------------------------------------------------------
        // Header
        // ------------------------------------------------------------------

        /// The top of every card: a coloured disc with a glyph in it, the
        /// small-caps eyebrow over the serif title, and the X.
        ///
        /// The close button is passed in rather than wired to `Sheets.Close`
        /// here, because a card that has a sub-state open may want the X to
        /// back out of that first.
        public static VisualElement Header(string eyebrow, string title, Color badge,
                                           string glyph, Action close)
        {
            var head = new VisualElement();
            head.AddToClassList(SheetTheme.Head);

            var disc = new VisualElement();
            disc.AddToClassList(SheetTheme.Badge);
            disc.style.backgroundColor = badge;
            var g = new Label(string.IsNullOrEmpty(glyph) ? "" : glyph);
            g.AddToClassList("sheet-badge-glyph");
            disc.Add(g);
            if (MidnightLandHud.Active)
            {
                disc.Clear(); disc.style.backgroundColor = Color.clear;
                disc.Add(new LandIcon(string.Equals(title, "sawmill", StringComparison.OrdinalIgnoreCase) ? "saw" : "build"));
            }
            head.Add(disc);

            var words = new VisualElement();
            words.AddToClassList(SheetTheme.Col);
            words.style.flexGrow = 1f;
            words.style.marginLeft = 12f;
            if (!string.IsNullOrEmpty(eyebrow)) words.Add(Eyebrow(eyebrow));
            var t = new Label(title ?? "");
            t.AddToClassList(SheetTheme.Title);
            words.Add(t);
            head.Add(words);

            var x = new Button(() => close?.Invoke()) { text = "✕" };
            x.AddToClassList(SheetTheme.Close);
            if (MidnightLandHud.Active)
            {
                x.text = ""; x.tooltip = "Close";
                x.style.alignItems = Align.Center; x.style.justifyContent = Justify.Center;
                x.Add(new LandIcon("close"));
            }
            head.Add(x);
            return head;
        }

        // ------------------------------------------------------------------
        // Buttons
        // ------------------------------------------------------------------

        /// A sheet button. `primary` is the brass one — at most one per card,
        /// because "one decision" is the rule the whole HUD is built on.
        /// `quiet` is the small outlined one on a row ("change").
        public static Button Btn(string text, Action onClick,
                                 bool primary = false, bool quiet = false)
        {
            var b = new Button(() => onClick?.Invoke()) { text = text ?? "" };
            b.AddToClassList(SheetTheme.Btn);
            if (primary) b.AddToClassList(SheetTheme.BtnPrimary);
            if (quiet) b.AddToClassList(SheetTheme.BtnQuiet);
            return b;
        }

        // ------------------------------------------------------------------
        // Crew tokens
        // ------------------------------------------------------------------

        /// One hand, as a disc with their initial and a small badge for what
        /// they are doing. The ring is the mood and nothing else: ember means
        /// go and find out why, moss means leave them alone.
        public static VisualElement Token(string initial, bool angry, string jobGlyph,
                                          Action onClick = null)
        {
            var tok = new VisualElement();
            tok.AddToClassList(SheetTheme.Token);
            tok.AddToClassList(angry ? SheetTheme.TokenAngry : SheetTheme.TokenContent);
            tok.style.borderTopColor = tok.style.borderBottomColor =
                tok.style.borderLeftColor = tok.style.borderRightColor =
                    angry ? SheetTheme.Ember : SheetTheme.Moss;

            var letter = new Label(string.IsNullOrEmpty(initial) ? "?" : initial.Substring(0, 1).ToUpperInvariant());
            letter.AddToClassList(SheetTheme.TokenLetter);
            tok.Add(letter);

            if (!string.IsNullOrEmpty(jobGlyph))
            {
                var job = new Label(jobGlyph);
                job.AddToClassList(SheetTheme.TokenJob);
                tok.Add(job);
            }

            if (onClick != null)
            {
                tok.RegisterCallback<ClickEvent>(_ => onClick());
                tok.AddToClassList("sheet-clickable");
            }
            else tok.pickingMode = PickingMode.Ignore;
            return tok;
        }

        // ------------------------------------------------------------------
        // Stores
        // ------------------------------------------------------------------

        /// One of the three numbers across the top of a camp sheet: TIMBER /
        /// 23 / "of 30" / a bar. `big` and `small` are separate because they
        /// are different weights, and because the unit is the part the eye
        /// should skip once it has learnt it.
        /// **Three of these sit side by side in a 420 px card, so every part of
        /// one has to be a BLOCK in a column.** The first version let the
        /// eyebrow, the number and the unit find their own places in a flex
        /// row and they landed on top of each other — "TIMBER" printed through
        /// the "23", and the unit sat up at eyebrow height. Each row here is
        /// therefore explicit: label, then one baseline row of number + unit,
        /// then the bar, and whatever the caller appends after that.
        public static VisualElement Store(string label, string big, string small,
                                          float frac01, Color col)
        {
            var box = new VisualElement();
            box.AddToClassList(SheetTheme.Store);
            box.style.flexDirection = FlexDirection.Column;

            var eb = Eyebrow(label);
            eb.style.marginBottom = 1f;
            box.Add(eb);

            // A row of its own, with nothing stretching it: the number sets
            // the height and the unit sits on its baseline.
            var nums = new VisualElement();
            nums.style.flexDirection = FlexDirection.Row;
            nums.style.alignItems = Align.FlexEnd;
            nums.style.flexShrink = 0f;
            var b = new Label(big ?? "");
            b.AddToClassList("sheet-store-big");
            nums.Add(b);
            var s = new Label(small ?? "");
            s.AddToClassList("sheet-store-small");
            if (MidnightLandHud.Active)
            {
                nums.style.flexDirection = FlexDirection.Column;
                nums.style.alignItems = Align.FlexStart;
                s.style.whiteSpace = WhiteSpace.Normal;
                s.style.marginLeft = 0;
                s.style.fontSize = 10f;
            }
            nums.Add(s);
            box.Add(nums);

            box.Add(Bar(frac01, col));
            return box;
        }

        /// Re-point a `Store` built above without rebuilding it. Safe on a
        /// null or on anything that is not a store.
        public static void SetStore(VisualElement store, string big, string small, float frac01)
        {
            if (store == null || store.childCount < 3) return;
            var nums = store[1];
            if (nums.childCount > 0 && nums[0] is Label b) b.text = big ?? "";
            if (nums.childCount > 1 && nums[1] is Label s) s.text = small ?? "";
            SetBar(store[2], frac01);
        }

        // ------------------------------------------------------------------
        // Bars
        // ------------------------------------------------------------------

        public static VisualElement Bar(float frac01, Color col, float height = 6f)
        {
            var track = new VisualElement();
            track.AddToClassList(SheetTheme.Bar);
            track.style.height = height;
            var fill = new VisualElement();
            fill.AddToClassList(SheetTheme.BarFill);
            fill.style.backgroundColor = col;
            fill.style.width = Length.Percent(Mathf.Clamp01(frac01) * 100f);
            track.Add(fill);
            return track;
        }

        public static void SetBar(VisualElement bar, float frac01, Color? col = null)
        {
            if (bar == null || bar.childCount == 0) return;
            var fill = bar[0];
            fill.style.width = Length.Percent(Mathf.Clamp01(frac01) * 100f);
            if (col.HasValue) fill.style.backgroundColor = col.Value;
        }

        // ------------------------------------------------------------------
        // Structure
        // ------------------------------------------------------------------

        /// The hairline between two sections of a card.
        public static VisualElement Rule()
        {
            var r = new VisualElement();
            r.AddToClassList(SheetTheme.Rule);
            r.pickingMode = PickingMode.Ignore;
            return r;
        }

        public static VisualElement Row(params VisualElement[] children)
        {
            var e = new VisualElement();
            e.AddToClassList(SheetTheme.Row);
            Fill(e, children, true);
            return e;
        }

        public static VisualElement Col(params VisualElement[] children)
        {
            var e = new VisualElement();
            e.AddToClassList(SheetTheme.Col);
            Fill(e, children, false);
            return e;
        }

        /// **The gap is set here, not in USS.** It used to be a
        /// `.sheet-row > * { margin-right }` rule, which also reached every
        /// element a CARD happened to put in a row — including the parts of a
        /// `Store` — and spacing you cannot see in the file that built the
        /// element is spacing that surprises you. Set on the children and on
        /// nothing else, and never on the last one, so a row does not carry a
        /// trailing gap that throws a centred layout off.
        static void Fill(VisualElement parent, VisualElement[] children, bool row)
        {
            if (children == null) return;
            VisualElement last = null;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] == null) continue;
                parent.Add(children[i]);
                if (last != null)
                {
                    if (row) last.style.marginRight = 10f;
                    else last.style.marginBottom = 8f;
                }
                last = children[i];
            }
        }

        // ------------------------------------------------------------------
        // Words
        // ------------------------------------------------------------------

        public static Label Text(string s, bool bold = false, bool muted = false, float size = 14f)
        {
            var l = new Label(s ?? "");
            l.AddToClassList("sheet-text");
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (muted) l.AddToClassList(SheetTheme.Muted);
            l.style.fontSize = size;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        /// Small caps, letter-spaced, dim: the label over a number or a list.
        public static Label Eyebrow(string s)
        {
            var l = new Label((s ?? "").ToUpperInvariant());
            l.AddToClassList(SheetTheme.Eyebrow);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        // ------------------------------------------------------------------
        // Compound controls
        // ------------------------------------------------------------------

        /// A pill toggle group — brass on the one that is on. Used where a
        /// card has a mode rather than a decision, so the modes do not each
        /// become a button competing with the card's real action.
        public static VisualElement Segmented(string[] options, int selected, Action<int> pick)
        {
            var bar = new VisualElement();
            bar.AddToClassList(SheetTheme.Seg);
            if (options == null) return bar;
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                var b = new Button(() => pick?.Invoke(index)) { text = options[i] };
                b.AddToClassList(SheetTheme.SegBtn);
                if (i == selected) b.AddToClassList(SheetTheme.SegOn);
                bar.Add(b);
            }
            return bar;
        }

        /// Re-mark which segment is on, without rebuilding the group.
        public static void SetSegmented(VisualElement seg, int selected)
        {
            if (seg == null) return;
            for (int i = 0; i < seg.childCount; i++)
                seg[i].EnableInClassList(SheetTheme.SegOn, i == selected);
        }

        /// **The tab strip at the top of the standard frame.**
        ///
        /// Not `Segmented`, and the difference is what each one is for: a
        /// segmented group is a SETTING with three values (full / half /
        /// none), pill-shaped and small; a tab strip is which part of the
        /// sheet you are LOOKING at, and it has to be hit with a thumb. Every
        /// tab is therefore at least 44 px tall — the one number this project
        /// treats as a floor on anything a finger aims at — and the live one
        /// is marked in the sheet's own accent rather than in brass, so the
        /// fire's tabs read ember and the ship's read sea.
        ///
        /// Labels are sentence-case and short: "camp", "hands · 3", "build".
        public static VisualElement Tabs(string[] labels, int selected,
                                         Action<int> pick, Color accent)
        {
            var bar = new VisualElement();
            bar.AddToClassList(SheetTheme.Tabs);
            if (labels == null) return bar;
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var b = new Button(() => pick?.Invoke(index)) { text = labels[i] ?? "" };
                b.AddToClassList(SheetTheme.Tab);
                // The underline is a child rather than a border, because a
                // border on the button itself would move the label by two
                // pixels every time the tab changed.
                var mark = new VisualElement();
                mark.AddToClassList(SheetTheme.TabMark);
                mark.style.backgroundColor = accent;
                mark.pickingMode = PickingMode.Ignore;
                b.Add(mark);
                if (i == selected) b.AddToClassList(SheetTheme.TabOn);
                bar.Add(b);
            }
            return bar;
        }

        /// **The strip the frame actually shows.** Five 44 px tabs is as much
        /// as a thumb can aim at across a phone, so a sheet with more pages
        /// than that gets the compact form -- arrows, the live page's name,
        /// and a dot per page -- rather than six tabs nobody can hit. Either
        /// way the swipe on the body is the primary control and this is the
        /// map of where you are in it.
        public const int MaxTabs = 5;

        public static VisualElement Strip(string[] labels, int selected,
                                          Action<int> pick, Color accent)
        {
            if (labels != null && labels.Length > MaxTabs)
                return Compact(labels, selected, pick, accent);
            return Tabs(labels, selected, pick, accent);
        }

        public static void SetStrip(VisualElement strip, int selected, string[] labels)
        {
            if (strip == null) return;
            if (strip.ClassListContains(SheetTheme.TabsCompact)) SetCompact(strip, selected, labels);
            else SetTabs(strip, selected, labels);
        }

        /// The live page index, carried on the strip itself so the arrows can
        /// read it without a captured copy going stale.
        class StripAt { public int at; }

        static VisualElement Compact(string[] labels, int selected,
                                     Action<int> pick, Color accent)
        {
            var bar = new VisualElement();
            bar.AddToClassList(SheetTheme.Tabs);
            bar.AddToClassList(SheetTheme.TabsCompact);
            var state = new StripAt { at = selected };
            bar.userData = state;
            if (labels == null || labels.Length == 0) return bar;

            var back = new Button(() => pick?.Invoke(state.at - 1)) { text = "‹" };
            back.AddToClassList(SheetTheme.TabArrow);
            back.name = "back";
            bar.Add(back);

            var now = new Label(labels[Mathf.Clamp(selected, 0, labels.Length - 1)]);
            now.AddToClassList(SheetTheme.TabNow);
            now.name = "now";
            bar.Add(now);

            var dots = new VisualElement();
            dots.AddToClassList(SheetTheme.Dots);
            dots.name = "dots";
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var d = new VisualElement();
                d.AddToClassList(SheetTheme.Dot);
                if (i == selected) d.style.backgroundColor = accent;
                d.RegisterCallback<ClickEvent>(_ => pick?.Invoke(index));
                dots.Add(d);
            }
            dots.userData = accent;
            bar.Add(dots);

            var fwd = new Button(() => pick?.Invoke(state.at + 1)) { text = "›" };
            fwd.AddToClassList(SheetTheme.TabArrow);
            fwd.name = "fwd";
            bar.Add(fwd);

            SetCompact(bar, selected, labels);
            return bar;
        }

        static void SetCompact(VisualElement bar, int selected, string[] labels)
        {
            if (bar == null) return;
            int count = labels != null ? labels.Length : 0;
            if (bar.userData is StripAt st) st.at = selected;

            var now = bar.Q<Label>("now");
            if (now != null && labels != null && count > 0)
                now.text = labels[Mathf.Clamp(selected, 0, count - 1)] ?? "";

            var dots = bar.Q<VisualElement>("dots");
            if (dots != null)
            {
                var accent = dots.userData is Color c ? c : SheetTheme.Brass;
                for (int i = 0; i < dots.childCount; i++)
                    dots[i].style.backgroundColor = i == selected
                        ? accent
                        : new Color(31f / 255f, 45f / 255f, 56f / 255f, 0.25f);
            }

            var back = bar.Q<Button>("back");
            if (back != null) back.SetEnabled(selected > 0);
            var fwd = bar.Q<Button>("fwd");
            if (fwd != null) fwd.SetEnabled(selected < count - 1);
        }

        // ------------------------------------------------------------------
        // Paginated lists
        // ------------------------------------------------------------------

        /// **The height of one row in a paginated list.** Fixed, because the
        /// page count is divided out of it (`SheetHost.RowsThatFit`): a row
        /// that grows to fit its text is a row that pushes the last one off
        /// a page that no longer scrolls.
        public const float RowPx = 40f;

        /// A row of that exact height. Same children as `Row`, same gaps.
        public static VisualElement ListRow(params VisualElement[] children)
        {
            var e = Row(children);
            e.AddToClassList(SheetTheme.ListRow);
            return e;
        }

        /// "hands", "hands 2/3" -- the label a paged section wears in the
        /// strip. One page keeps its plain name.
        public static string PageLabel(string name, int part, int parts) =>
            parts <= 1 ? name : $"{name} {part + 1}/{parts}";

        /// How many pages `count` rows need at `perPage` a page. Never zero:
        /// an empty list still has one page, and that page says so.
        public static int PageCount(int count, int perPage) =>
            Mathf.Max(1, Mathf.CeilToInt(count / (float)Mathf.Max(1, perPage)));

        /// Re-mark which tab is live, and re-label them (the count on
        /// "hands · 3" moves with the camp), without rebuilding the strip.
        public static void SetTabs(VisualElement tabs, int selected, string[] labels = null)
        {
            if (tabs == null) return;
            for (int i = 0; i < tabs.childCount; i++)
            {
                tabs[i].EnableInClassList(SheetTheme.TabOn, i == selected);
                if (labels != null && i < labels.Length && tabs[i] is Button b)
                    b.text = labels[i] ?? "";
            }
        }

        /// **One fact, boxed.** A store's number, an order's state, a count —
        /// the compact form a tab's body uses where a full `Store` tile would
        /// eat a third of the frame. Label over value, and nothing else.
        public static VisualElement Chip(string label, string value, Color tint)
        {
            var box = new VisualElement();
            box.AddToClassList(SheetTheme.Chip);
            box.pickingMode = PickingMode.Ignore;
            var stripe = new VisualElement();
            stripe.AddToClassList(SheetTheme.ChipStripe);
            stripe.style.backgroundColor = tint;
            stripe.pickingMode = PickingMode.Ignore;
            box.Add(stripe);
            var col = new VisualElement();
            col.style.flexDirection = FlexDirection.Column;
            col.style.flexShrink = 1f;
            var eb = Eyebrow(label);
            eb.style.marginBottom = 0f;
            col.Add(eb);
            var v = new Label(value ?? "");
            v.AddToClassList(SheetTheme.ChipValue);
            v.pickingMode = PickingMode.Ignore;
            col.Add(v);
            box.Add(col);
            return box;
        }

        /// Re-point a `Chip` without rebuilding it.
        public static void SetChip(VisualElement chip, string value)
        {
            if (chip == null || chip.childCount < 2) return;
            var col = chip[1];
            if (col.childCount > 1 && col[1] is Label v) v.text = value ?? "";
        }

        /// The row pinned at the bottom of the frame. It wraps, because a tab
        /// with three verbs on a 1056 px phone frame and on a 616 px desk
        /// column is the same three verbs.
        public static VisualElement Actions(params VisualElement[] children)
        {
            var e = new VisualElement();
            e.AddToClassList(SheetTheme.Actions);
            if (children == null) return e;
            foreach (var c in children)
            {
                if (c == null) continue;
                c.style.flexGrow = 1f;
                c.style.flexShrink = 1f;
                c.style.marginRight = 8f;
                e.Add(c);
            }
            if (e.childCount > 0) e[e.childCount - 1].style.marginRight = 0f;
            return e;
        }

        // ------------------------------------------------------------------
        // Heights, for pagination
        // ------------------------------------------------------------------
        //
        // What each piece of a card costs in panel units, so a sheet can work
        // out how many of them fit the band BEFORE it builds them. They are
        // the sums of `Sheets.uss` (a quiet button is 26 px plus the 4 px it
        // is given below it, a note is 9 + 12 + 9 + a line), rounded UP:
        // guessing high loses a row at the bottom of a page, guessing low
        // puts a row off the edge of a card that no longer scrolls.

        public const float EyebrowPx = 18f;
        public const float TextPx = 18f;
        public const float QuietPx = 30f;
        public const float ButtonPx = 42f;
        public const float SegPx = 36f;
        public const float NotePx = 46f;
        public const float RulePx = 18f;
        public const float StorePx = 74f;
        public const float BarPx = 14f;
        public const float BigPx = 38f;

        /// The inset box that carries a card's one status sentence — what is
        /// going up, who is on it, how long. One line, never a log.
        public static VisualElement Note(string text)
        {
            var box = new VisualElement();
            box.AddToClassList(SheetTheme.Note);
            var glyph = new Label("✎");
            glyph.AddToClassList("sheet-note-glyph");
            box.Add(glyph);
            var l = new Label(text ?? "");
            l.AddToClassList("sheet-note-text");
            box.Add(l);
            box.pickingMode = PickingMode.Ignore;
            return box;
        }

        public static void SetNote(VisualElement note, string text)
        {
            if (note == null || note.childCount < 2) return;
            if (note[1] is Label l) l.text = text ?? "";
        }
    }

    /// **A list of rows cut into pages that fit the band.**
    ///
    /// The one piece of machinery Kevin's *"you can swipe left to right for
    /// new windows"* actually needs: a section hands over its rows and what
    /// each one costs, this works out where the page breaks fall, and the
    /// sheet then builds one page at a time. Nothing is dropped and nothing
    /// scrolls -- a row that will not fit page 1 is the first row of page 2.
    ///
    /// Rows are handed over as FACTORIES rather than as elements, because a
    /// page is rebuilt on every swipe and building all of them up front to
    /// throw most away is the cost this whole redesign exists to avoid.
    public class SheetPager
    {
        readonly System.Collections.Generic.List<float> heights =
            new System.Collections.Generic.List<float>();
        readonly System.Collections.Generic.List<System.Func<VisualElement>> rows =
            new System.Collections.Generic.List<System.Func<VisualElement>>();
        readonly System.Collections.Generic.List<int> starts =
            new System.Collections.Generic.List<int>();

        public void Clear() { heights.Clear(); rows.Clear(); starts.Clear(); }

        public void Add(float px, System.Func<VisualElement> make)
        {
            if (make == null) return;
            heights.Add(px);
            rows.Add(make);
        }

        public int RowCount => rows.Count;

        /// Cut the rows into pages of at most `band` units each. A single row
        /// taller than the band still gets its own page -- it is clipped
        /// rather than lost, and a clipped row is a bug in the row's height,
        /// which is a thing a reader can find.
        public void Lay(float band)
        {
            starts.Clear();
            if (rows.Count == 0) { starts.Add(0); return; }
            float used = 0f;
            starts.Add(0);
            for (int i = 0; i < rows.Count; i++)
            {
                float h = heights[i];
                if (i > starts[starts.Count - 1] && used + h > band)
                {
                    starts.Add(i);
                    used = 0f;
                }
                used += h;
            }
        }

        /// How many pages the last `Lay` produced. Always at least one.
        public int Pages => Mathf.Max(1, starts.Count);

        /// One page, as a column of its rows.
        public VisualElement Build(int page)
        {
            var col = new VisualElement();
            col.style.flexDirection = FlexDirection.Column;
            if (starts.Count == 0) return col;
            page = Mathf.Clamp(page, 0, starts.Count - 1);
            int from = starts[page];
            int to = page + 1 < starts.Count ? starts[page + 1] : rows.Count;
            for (int i = from; i < to; i++)
            {
                var e = rows[i]();
                if (e != null) col.Add(e);
            }
            return col;
        }
    }
}
