using SeaSick.UI.Sheets;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard
{
    /// The Midnight colours the slot screen draws with in code (Painter2D
    /// cannot read USS variables). Same values as the mockup's `:root` and
    /// Station.uss, so a colour change is one edit here plus ShipyardSlots.uss.
    public static class YardPalette
    {
        public static readonly Color Page = new Color32(15, 27, 37, 255);
        public static readonly Color Card = new Color32(19, 34, 46, 255);
        public static readonly Color Card2 = new Color32(26, 48, 64, 255);
        public static readonly Color Tile = new Color32(14, 26, 36, 255);
        public static readonly Color Line = new Color32(30, 51, 68, 255);
        public static readonly Color Line2 = new Color32(44, 74, 94, 255);
        public static readonly Color Ink = new Color32(232, 242, 246, 255);
        public static readonly Color Muted = new Color32(166, 186, 198, 255);
        public static readonly Color Faint = new Color32(110, 132, 148, 255);
        public static readonly Color Ice = new Color32(164, 210, 232, 255);
        public static readonly Color Moss = new Color32(159, 224, 194, 255);
        public static readonly Color Amber = new Color32(240, 195, 106, 255);
        public static readonly Color Ember = new Color32(246, 146, 128, 255);
        public static readonly Color Brass = new Color32(201, 147, 57, 255);
        public static readonly Color Wood = new Color32(107, 75, 46, 255);
        public static readonly Color Wood2 = new Color32(138, 100, 64, 255);
        public static readonly Color WoodDk = new Color32(63, 44, 27, 255);
        public static readonly Color Iron = new Color32(43, 47, 51, 255);
        public static readonly Color Sky = new Color32(20, 42, 56, 255);
        public static readonly Color Sea = new Color32(18, 58, 82, 255);
        public static readonly Color Foam = new Color32(111, 182, 216, 255);

        static StyleSheet uss;
        static bool tried;

        /// `Resources/UI/ShipyardSlots.uss`, added to `root` once. Every
        /// selector in it sits under `.ys`, so it reaches nothing else.
        public static void Apply(VisualElement root)
        {
            if (!tried)
            {
                tried = true;
                uss = Resources.Load<StyleSheet>("UI/ShipyardSlots");
                if (uss == null) Debug.LogWarning("[ShipyardSlots] Resources/UI/ShipyardSlots.uss is missing.");
            }
            if (uss != null && !root.styleSheets.Contains(uss)) root.styleSheets.Add(uss);
            root.AddToClassList("ys");
        }
    }

    /// **A module's picture.** The approved PNG from `ItemIconSet` when one
    /// exists under the key, otherwise a small vector glyph drawn here after
    /// the mockup's SVG symbols (32x32 design grid), so a module never draws
    /// a blank square. Keys: cannon, bunk, crate, pump, lookout, bench,
    /// undo, close.
    public sealed class YardGlyph : VisualElement
    {
        string key;
        bool hasTex;
        public float Alpha = 1f;

        public YardGlyph(string key, float size = 24f)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("ys-glyph");
            style.width = size; style.height = size; style.flexShrink = 0;
            generateVisualContent += Draw;
            SetKey(key);
        }

        public void SetKey(string k)
        {
            key = k;
            var tex = string.IsNullOrEmpty(k) ? null : ItemIconSet.Get(k);
            hasTex = tex != null;
            style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            if (hasTex || string.IsNullOrEmpty(key)) return;
            var p = ctx.painter2D;
            float s = Mathf.Min(contentRect.width, contentRect.height) / 32f;
            Vector2 V(float x, float y) => new Vector2(x * s, y * s);
            Color A(Color c) { c.a *= Alpha; return c; }
            void Rect(float x, float y, float w, float h, Color c)
            {
                p.fillColor = A(c); p.BeginPath();
                p.MoveTo(V(x, y)); p.LineTo(V(x + w, y)); p.LineTo(V(x + w, y + h)); p.LineTo(V(x, y + h));
                p.ClosePath(); p.Fill();
            }
            void Circle(float x, float y, float r, Color c)
            { p.fillColor = A(c); p.BeginPath(); p.Arc(V(x, y), r * s, 0f, 360f); p.Fill(); }
            void Line(float x, float y, float a, float b, Color c, float w)
            { p.strokeColor = A(c); p.lineWidth = w * s; p.lineCap = LineCap.Round; p.BeginPath(); p.MoveTo(V(x, y)); p.LineTo(V(a, b)); p.Stroke(); }

            var wood = YardPalette.Wood2; var woodDk = YardPalette.Wood; var crate = new Color32(160, 115, 63, 255);
            switch (key)
            {
                case "cannon":
                    Rect(6, 11, 21, 8, YardPalette.Iron);
                    Rect(24, 10, 4, 10, new Color32(68, 73, 78, 255));
                    Rect(4, 17, 16, 6, wood);
                    Circle(8, 24, 3.5f, new Color32(90, 64, 41, 255));
                    Circle(17, 24, 3.5f, new Color32(90, 64, 41, 255));
                    break;
                case "bunk":
                    Rect(4, 6, 3, 21, wood); Rect(25, 6, 3, 21, wood);
                    Rect(7, 10, 18, 4, new Color32(201, 183, 154, 255));
                    Rect(7, 20, 18, 4, new Color32(201, 183, 154, 255));
                    Rect(7, 9, 5, 3, new Color32(238, 238, 238, 255));
                    Rect(7, 19, 5, 3, new Color32(238, 238, 238, 255));
                    break;
                case "crate":
                    Rect(5, 7, 22, 19, crate);
                    Line(5, 7, 27, 26, woodDk, 2.5f); Line(27, 7, 5, 26, woodDk, 2.5f);
                    Line(5, 7, 27, 7, woodDk, 2.5f); Line(27, 7, 27, 26, woodDk, 2.5f);
                    Line(27, 26, 5, 26, woodDk, 2.5f); Line(5, 26, 5, 7, woodDk, 2.5f);
                    break;
                case "pump":
                    Rect(12, 10, 8, 17, new Color32(124, 133, 140, 255));
                    Rect(6, 7, 20, 3, wood); Rect(15, 4, 2, 6, wood);
                    Line(20, 15, 25, 15, YardPalette.Foam, 2.5f); Line(25, 15, 25, 20, YardPalette.Foam, 2.5f);
                    Circle(25, 24, 2, YardPalette.Foam);
                    break;
                case "lookout":
                    Rect(15, 10, 2.5f, 18, wood);
                    p.fillColor = A(crate); p.BeginPath(); p.MoveTo(V(9, 10)); p.LineTo(V(23, 10)); p.LineTo(V(21, 15)); p.LineTo(V(11, 15)); p.ClosePath(); p.Fill();
                    Circle(16, 6, 3, YardPalette.Amber);
                    Line(4, 5, 9, 7, YardPalette.Amber, 1.6f); Line(28, 5, 23, 7, YardPalette.Amber, 1.6f);
                    break;
                case "bench":
                    Rect(4, 13, 24, 4, crate); Rect(6, 17, 3, 10, woodDk); Rect(23, 17, 3, 10, woodDk);
                    Rect(14, 5, 3, 8, wood); Rect(11, 4, 9, 4, new Color32(124, 133, 140, 255));
                    break;
                case "undo":
                    Line(12, 9, 6, 15, YardPalette.Ice, 3.4f); Line(6, 15, 12, 21, YardPalette.Ice, 3.4f);
                    Line(6, 15, 19, 15, YardPalette.Ice, 3.4f);
                    p.strokeColor = A(YardPalette.Ice); p.lineWidth = 3.4f * s; p.BeginPath();
                    p.Arc(V(19, 20.5f), 5.5f * s, -90f, 90f); p.Stroke();
                    Line(19, 26, 16, 26, YardPalette.Ice, 3.4f);
                    break;
                case "close":
                    Line(9, 9, 23, 23, YardPalette.Muted, 3.2f); Line(23, 9, 9, 23, YardPalette.Muted, 3.2f);
                    break;
                default:
                    Rect(7, 7, 18, 18, YardPalette.Line2);
                    break;
            }
        }
    }
}
