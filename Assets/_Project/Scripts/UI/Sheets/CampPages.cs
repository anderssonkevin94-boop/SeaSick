using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **Shared bits of the Ledger drawer's CAMP pages, 2026-09-27**
    /// (`BuildSheet`, `PeopleSheet`): the stylesheet, the header (☰, title,
    /// subtitle, ✕) in the Overview/Station look, and the building glyph.
    /// Nothing here decides anything; it only draws.
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

        /// ☰ · title / subtitle · ✕. `menu` opens the Ledger drawer.
        public static VisualElement Header(string titleText, out Label subtitle, Action menu)
        {
            var head = new VisualElement();
            head.AddToClassList(SheetTheme.Head);
            head.AddToClassList("cp-head");
            Styled(head);

            var burger = new Button(() => menu?.Invoke()) { text = "", tooltip = "Ledger" };
            burger.AddToClassList("cp-head-btn");
            burger.Add(new LedgerDrawer.Glyph(LedgerDrawer.Glyph.Kind.Menu));
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

        /// The drawer, through whichever hook is set (the page's own, else
        /// the station page's), so ☰ is never a dead button.
        public static void OpenLedger(Action own, SeaSick.World.Outpost camp)
        {
            if (own != null) { own(); return; }
            StationPage.OpenLedgerFor(camp);
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
