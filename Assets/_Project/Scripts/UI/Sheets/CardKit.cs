using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The small Midnight cards' parts (2026-09-27, menu rework #7 / #10
    /// and the Campfire card).** Manifest, Campfire, Wall, Ladder, Road and
    /// Chart are all the same shape as `LookoutSheet`: a glyph header with a
    /// pill, a row of chips, a "now" card, a tile grid or two, and a thumb
    /// row pinned at the bottom. These are the pieces, on `.st` + Hand.uss +
    /// Lookout.uss (`WatchTiles.Root`); `.ck-*` rules live in Lookout.uss.
    /// Nothing here decides anything; it only draws.
    internal static class CardKit
    {
        /// The page root (bottom thumb row goes straight into it) and the
        /// column above it that takes the rest of the band. No scroll: a card
        /// that does not fit pages its grids instead.
        public static VisualElement Page(out VisualElement col)
        {
            var root = WatchTiles.Root("st-page");
            StationPage.FitToParent(root);
            col = new VisualElement();
            col.AddToClassList("ck-col");
            root.Add(col);
            return root;
        }

        /// Glyph tile · title / sub · pill · ✕, with a `StationPage.Glyph`.
        public static WatchTiles.Head Head(string glyph, string title) =>
            new WatchTiles.Head(new StationPage.Glyph(glyph, MidnightLandHud.Ice, "lk-glyph"), title);

        public static VisualElement Chips(VisualElement into)
        {
            var chips = WatchTiles.Box("hs-chips");
            chips.style.marginTop = 0;
            into.Add(chips);
            return chips;
        }

        /// "EYEBROW  em" -- the label over a grid.
        public static Label Eye(VisualElement into, string key, string em = null)
        {
            var eye = WatchTiles.Box("hs-eye-row");
            var k = StationPage.Text(key, "st-eyebrow");
            eye.Add(k);
            if (!string.IsNullOrEmpty(em)) eye.Add(StationPage.Text(em, "hs-eye-em"));
            into.Add(eye);
            return k;
        }

        public static VisualElement Grid(VisualElement into)
        {
            var g = WatchTiles.Box("hs-grid");
            g.pickingMode = PickingMode.Position;
            into.Add(g);
            return g;
        }

        // --- the "now" card -------------------------------------------------------

        public sealed class Now
        {
            public readonly VisualElement Root, Ico;
            public readonly Label T, S;
            int tone = -1;

            public Now(VisualElement into, VisualElement icon)
            {
                Root = StationPage.Card();
                Root.AddToClassList("hs-now");
                Root.AddToClassList("lk-card");
                Root.AddToClassList("ck-card");
                var top = WatchTiles.Box("hs-now-top");
                Ico = WatchTiles.Box("hs-now-ico");
                if (icon != null) Ico.Add(icon);
                top.Add(Ico);
                var words = WatchTiles.Box("hs-now-words");
                T = StationPage.Text("", "hs-now-t");
                S = StationPage.Text("", "hs-now-s");
                words.Add(T);
                words.Add(S);
                top.Add(words);
                Root.Add(top);
                into?.Add(Root);
            }

            public void Set(string t, string s)
            {
                WatchTiles.Set(T, t);
                WatchTiles.Set(S, s);
                WatchTiles.Show(S, !string.IsNullOrEmpty(s));
            }

            /// 0 good, 1 warn, 2 bad, -1 plain.
            public void Tone(int t)
            {
                if (t == tone) return;
                tone = t;
                Root.EnableInClassList("lk-card--good", t == 0);
                Root.EnableInClassList("lk-card--warn", t == 1);
                Root.EnableInClassList("lk-card--bad", t == 2);
            }
        }

        public static VisualElement ItemIcon(string res) => StationPage.Icon(res, "hs-now-ico-img");

        public static VisualElement GlyphIcon(string kind) =>
            new StationPage.Glyph(kind, MidnightLandHud.Ice, "hs-now-ico-img");

        // --- tiles --------------------------------------------------------------------

        /// An item tile (icon · name / sub) or a villager tile (initial ·
        /// name / sub). A Button, 50 units tall, a third of the row.
        public sealed class Tile
        {
            public readonly Button Root;
            public readonly VisualElement Ico;
            public readonly Label Initial, N, S;
            public string Key;

            public Tile(Action<Tile> tap, bool person)
            {
                Root = new Button(() => tap?.Invoke(this)) { text = "" };
                Root.AddToClassList("hs-tile");
                if (person)
                {
                    Ico = WatchTiles.Box("ck-av");
                    Initial = StationPage.Text("", "ck-av-t");
                    Ico.Add(Initial);
                }
                else Ico = StationPage.Icon(null, "hs-tile-ico");
                Root.Add(Ico);
                var words = WatchTiles.Box("hs-tile-words");
                N = StationPage.Text("", "hs-tile-n");
                S = StationPage.Text("", "hs-tile-s");
                words.Add(N);
                words.Add(S);
                Root.Add(words);
            }

            public Tile Col3(int index)
            {
                Root.EnableInClassList("hs-tile--col3", index % 3 == 2);
                return this;
            }

            public void Set(string name, string sub)
            {
                WatchTiles.Set(N, name);
                WatchTiles.Set(S, sub);
            }

            public void SetItem(string res) => StationPage.SetIcon(Ico, res);

            public void SetPerson(string who) => WatchTiles.Set(Initial, SheetBits.Initial(who));

            public void State(bool on, bool locked = false)
            {
                Root.EnableInClassList("hs-tile--on", on);
                Root.EnableInClassList("hs-tile--lock", locked);
            }
        }

        // --- pills and buttons -----------------------------------------------------------

        /// A secondary header-row pill: a 44-unit tap target.
        public static Button Pill(VisualElement row, string text, Action tap, string tone = null)
        {
            var b = new Button(() => tap?.Invoke()) { text = text };
            b.AddToClassList("ck-pill");
            if (tone != null) b.AddToClassList("ck-pill--" + tone);
            row.Add(b);
            return b;
        }

        public static void PillTone(Button b, string tone)
        {
            foreach (var t in new[] { "ice", "wait", "good", "bad" })
                b.EnableInClassList("ck-pill--" + t, t == tone);
        }

        /// A pill that carries a sentence (a blocker, a two-tap question)
        /// takes its own full-width row and wraps (`.ck-pill--line`,
        /// Lookout.uss): UI text is never cut with an ellipsis (2026-10-02).
        public static void PillLine(Button b, bool on)
        {
            if (b != null) b.EnableInClassList("ck-pill--line", on);
        }

        public static VisualElement Acts(VisualElement root)
        {
            var acts = WatchTiles.Box("hs-acts");
            acts.pickingMode = PickingMode.Position;
            root.Add(acts);
            return acts;
        }

        /// kind: 0 plain, 1 primary (ice), 2 stop (ember).
        public static Button Act(VisualElement acts, string text, Action tap, int kind = 0)
        {
            var b = new Button(() => tap?.Invoke()) { text = text };
            b.AddToClassList("st-btn");
            b.AddToClassList("hs-act");
            if (acts.childCount == 0) b.AddToClassList("hs-act--first");
            if (kind == 1) b.AddToClassList("lk-act--pri");
            if (kind == 2) b.AddToClassList("hs-act--stop");
            acts.Add(b);
            return b;
        }

        public static void Primary(Button b, bool on) => b?.EnableInClassList("lk-act--pri", on);

        /// **Tap, then tap again.** First press arms the button (it says
        /// `armed`) for 3.5 s; a second press inside that runs `confirmed`.
        /// Tear down refunds nothing, so it never goes on one tap.
        public sealed class Confirm
        {
            public readonly Button Button;
            readonly string idle, armed;
            readonly Action confirmed;
            float until = -99f;

            public Confirm(VisualElement acts, string idle, string armed, Action confirmed)
            {
                this.idle = idle;
                this.armed = armed;
                this.confirmed = confirmed;
                Button = Act(acts, idle, Press, 2);
                Button.schedule.Execute(Tick).Every(250);
            }

            void Press()
            {
                if (Time.unscaledTime < until) { until = -99f; Tick(); confirmed?.Invoke(); return; }
                until = Time.unscaledTime + 3.5f;
                Tick();
            }

            void Tick()
            {
                string want = Time.unscaledTime < until ? armed : idle;
                if (Button.text != want) Button.text = want;
            }
        }

        // --- a bar ----------------------------------------------------------------------

        public sealed class Bar
        {
            public readonly VisualElement Root, Fill;

            public Bar(VisualElement into)
            {
                Root = WatchTiles.Box("st-bar");
                Root.AddToClassList("ck-bar");
                Fill = WatchTiles.Box("st-bar-fill");
                Root.Add(Fill);
                into.Add(Root);
            }

            public void Set(float t01, Color? colour = null)
            {
                Fill.style.width = Length.Percent(Mathf.Clamp01(t01) * 100f);
                Fill.style.backgroundColor = colour.HasValue ? new StyleColor(colour.Value) : new StyleColor(StyleKeyword.Null);
            }
        }

        public static readonly Color Ember = new Color32(246, 146, 128, 255);
        public static readonly Color Amber = new Color32(240, 195, 106, 255);
        public static readonly Color Ice = new Color32(164, 210, 232, 255);
    }
}
