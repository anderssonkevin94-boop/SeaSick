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
}
