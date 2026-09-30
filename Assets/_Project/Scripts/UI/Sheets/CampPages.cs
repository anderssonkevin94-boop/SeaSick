using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **Shared bits of the Camp pages, 2026-09-27** (`BuildSheet`,
    /// `WorkersSheet`, the readout sheets): the stylesheet, the header (☰,
    /// title, subtitle, ✕) in the Overview/Station look, the building glyph
    /// and (since 2026-09-30, when the ledger drawer went) the painted ☰ /
    /// gear / ✕ pictograms. **The ☰ opens the Camp sheet** (`CampSheet`, the
    /// one camp hub). Nothing here decides anything; it only draws.
    internal static class CampPages
    {
        static StyleSheet style;
        static StyleSheet Style => style != null ? style : (style = Resources.Load<StyleSheet>("UI/CampPages"));

        public static void Styled(VisualElement e)
        {
            if (Style != null) e.styleSheets.Add(Style);
            else Debug.LogWarning("[CampPages] Resources/UI/CampPages.uss is missing -- the page is unstyled.");
        }

        public static T Classed<T>(T e, string cls) where T : VisualElement
        {
            e.AddToClassList(cls);
            return e;
        }

        /// ☰ · title / subtitle · ✕. The ☰ opens the Camp sheet
        /// (`OpenLedger`; `menu` is kept for the callers' signatures).
        public static VisualElement Header(string titleText, out Label subtitle, Action menu)
        {
            var head = new VisualElement();
            head.AddToClassList(SheetTheme.Head);
            head.AddToClassList("cp-head");
            Styled(head);

            var burger = new Button(() => menu?.Invoke()) { text = "", tooltip = "Camp" };
            burger.AddToClassList("cp-head-btn");
            burger.Add(new Glyph(Glyph.Kind.Menu));
            head.Add(burger);

            var words = Classed(new VisualElement(), "cp-head-words");
            words.Add(Classed(new Label(titleText), "cp-title"));
            subtitle = Classed(new Label(), "cp-subtitle");
            words.Add(subtitle);
            head.Add(words);

            var x = new Button(() => Sheets.Close());
            x.AddToClassList(SheetTheme.Close);
            x.tooltip = "Close";
            x.text = "";
            x.style.alignItems = Align.Center;
            x.style.justifyContent = Justify.Center;
            x.Add(new LandIcon("close"));
            head.Add(x);
            return head;
        }

        /// **A plain icon badge · title / subtitle · ✕ (2026-09-27).** The
        /// same shape as `Header` above, but the leading badge is a drawn
        /// glyph rather than a ☰ button -- for the small world-object sheets
        /// (Wall, Ladder, Road, Chart) that have no ledger to open and were
        /// using "☰" as decoration, which reads as a dead menu control.
        public static VisualElement IconHeader(string titleText, VisualElement icon, out Label subtitle)
        {
            var head = new VisualElement();
            head.AddToClassList(SheetTheme.Head);
            head.AddToClassList("cp-head");
            Styled(head);

            var badge = new VisualElement { pickingMode = PickingMode.Ignore };
            badge.AddToClassList("cp-head-btn");
            if (icon != null) badge.Add(icon);
            head.Add(badge);

            var words = Classed(new VisualElement(), "cp-head-words");
            words.Add(Classed(new Label(titleText), "cp-title"));
            subtitle = Classed(new Label(), "cp-subtitle");
            words.Add(subtitle);
            head.Add(words);

            var x = new Button(() => Sheets.Close());
            x.AddToClassList(SheetTheme.Close);
            x.tooltip = "Close";
            x.text = "";
            x.style.alignItems = Align.Center;
            x.style.justifyContent = Justify.Center;
            x.Add(new LandIcon("close"));
            head.Add(x);
            return head;
        }

        /// **The ☰ chokepoint (2026-09-30, island UI phase 4).** Every sheet
        /// header's ☰ / back button lands here and opens the Camp sheet.
        /// `own` is the old per-sheet drawer hook, now unused; it stays so
        /// the callers (`CampPages.Header(..., () => OpenLedger(hook, camp))`)
        /// did not have to change.
        public static void OpenLedger(Action own, SeaSick.World.Outpost camp)
        {
            if (camp == null || camp.Ledger == null) return;
            Sheets.Open(new CampSheet(camp));
        }

        /// The ☰, gear and ✕ pictograms, painted (no font glyphs). Moved
        /// here from the deleted `LedgerDrawer` (2026-09-30); styled by the
        /// `ledger-glyph` rule in Ledger.uss.
        internal sealed class Glyph : VisualElement
        {
            public enum Kind { Menu, Gear, Close }
            readonly Kind kind;

            public Glyph(Kind kind)
            {
                this.kind = kind;
                pickingMode = PickingMode.Ignore;
                AddToClassList("ledger-glyph");
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;
                float s = Mathf.Min(contentRect.width, contentRect.height) / 24f;
                if (s <= 0f) return;
                Vector2 V(float x, float y) => new Vector2(x * s, y * s);
                void Line(float x, float y, float a, float b)
                { p.BeginPath(); p.MoveTo(V(x, y)); p.LineTo(V(a, b)); p.Stroke(); }
                p.strokeColor = MidnightLandHud.Pearl;
                p.lineCap = LineCap.Round;
                switch (kind)
                {
                    case Kind.Menu:
                        p.lineWidth = 2.4f * s;
                        Line(4, 6, 20, 6); Line(4, 12, 20, 12); Line(4, 18, 20, 18);
                        break;
                    case Kind.Close:
                        p.lineWidth = 2.6f * s;
                        Line(6, 6, 18, 18); Line(18, 6, 6, 18);
                        break;
                    default:
                        p.lineWidth = 2.2f * s;
                        p.BeginPath(); p.Arc(V(12, 12), 3f * s, 0, 360); p.Stroke();
                        Line(12, 2, 12, 5); Line(12, 19, 12, 22); Line(2, 12, 5, 12); Line(19, 12, 22, 12);
                        Line(4.9f, 4.9f, 7f, 7f); Line(17f, 17f, 19.1f, 19.1f);
                        Line(4.9f, 19.1f, 7f, 17f); Line(17f, 7f, 19.1f, 4.9f);
                        break;
                }
            }
        }

        /// **A building, drawn** (roof, walls, door), in ice -- the mockup's
        /// house glyph. Painted, so no font glyph is needed.
        internal sealed class HouseGlyph : VisualElement
        {
            public Color colour = MidnightLandHud.Ice;

            public HouseGlyph()
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("cp-glyph");
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;
                float s = Mathf.Min(contentRect.width, contentRect.height) / 24f;
                if (s <= 0f) return;
                Vector2 V(float x, float y) => new Vector2(x * s, y * s);
                p.strokeColor = colour;
                p.lineWidth = 1.8f * s;
                p.lineCap = LineCap.Round;
                p.lineJoin = LineJoin.Round;
                p.BeginPath(); p.MoveTo(V(3, 11)); p.LineTo(V(12, 4)); p.LineTo(V(21, 11)); p.Stroke();
                p.BeginPath(); p.MoveTo(V(5, 10)); p.LineTo(V(5, 20)); p.LineTo(V(19, 20)); p.LineTo(V(19, 10)); p.Stroke();
                p.BeginPath(); p.MoveTo(V(10, 20)); p.LineTo(V(10, 15)); p.LineTo(V(14, 15)); p.LineTo(V(14, 20)); p.Stroke();
            }
        }
    }
}
