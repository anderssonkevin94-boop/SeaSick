using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard
{
    /// **The side strip: the ship in elevation, and the section picker.**
    ///
    /// A port of the mockup's SVG (`draw()` in index.html, 340x150 design
    /// box) to Painter2D: paddle wheel at the stern, one block per section
    /// weighted stern 1.15 / middle 1 / bow 1.25, stacked decks above the
    /// main deck, one brass gun port per deck level above the hold, the
    /// chimney, the water line. Labels, the tap areas and the '+' chips are
    /// real elements placed in PERCENT of the design box, so the strip only
    /// re-lays itself out when its width changes (height = width x 150/340).
    ///
    /// Talks back through `onSectionTap(key)` and `onPlusTap(index)`. During
    /// a drag the host calls `ShowDropHints` and registers `SectionHitArea`s
    /// with the `DragController`, which marks the hovered one via
    /// `SetDropHover`.
    public sealed class ShipStripView : VisualElement
    {
        const float W = 340f, H = 150f, Deck = 88f, Keel = 120f, WL = 106f, Lv = 20f;
        const float X0 = 34f, X1 = 322f;

        public event Action<string> onSectionTap;
        public event Action<int> onPlusTap;

        YardSectionVm[] sections = Array.Empty<YardSectionVm>();
        YardPlusChipVm[] chips = Array.Empty<YardPlusChipVm>();
        readonly float[] sx = new float[16], sw = new float[16];
        readonly VisualElement layer, canvas;
        readonly Label note;
        readonly Dictionary<string, VisualElement> hitByKey = new Dictionary<string, VisualElement>();
        bool dropHints;
        string dropHover;

        public ShipStripView()
        {
            AddToClassList("ys-strip");
            canvas = new VisualElement { pickingMode = PickingMode.Ignore };
            canvas.AddToClassList("ys-strip-layer");
            canvas.generateVisualContent += Draw;
            Add(canvas);
            layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.AddToClassList("ys-strip-layer");
            Add(layer);
            note = new Label { pickingMode = PickingMode.Ignore };
            note.AddToClassList("ys-strip-note");
            Add(note);
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float want = Mathf.Round(contentRect.width * H / W);
                if (contentRect.width > 1f && Mathf.Abs(resolvedStyle.height - want) > 0.5f) style.height = want;
            });
        }

        public void Bind(YardSectionVm[] secs, YardPlusChipVm[] plus, string stripNote)
        {
            sections = secs ?? Array.Empty<YardSectionVm>();
            chips = plus ?? Array.Empty<YardPlusChipVm>();
            restNote = stripNote;
            note.text = stripNote ?? "";
            note.style.display = string.IsNullOrEmpty(stripNote) ? DisplayStyle.None : DisplayStyle.Flex;
            LayoutSections();
            RebuildOverlay();
            canvas.MarkDirtyRepaint();
        }

        /// The tap area over one section: register it with the drag
        /// controller as a drop target, or read its `worldBound`.
        public VisualElement SectionHitArea(string key) =>
            key != null && hitByKey.TryGetValue(key, out var e) ? e : null;

        /// Every section's tap area in panel coordinates, stern first.
        public IEnumerable<(string key, Rect rect)> SectionRects()
        {
            foreach (var s in sections)
                if (hitByKey.TryGetValue(s.key ?? "", out var e)) yield return (s.key, e.worldBound);
        }

        string restNote;

        /// Dashed green boxes on every section while a module is dragged,
        /// with `dragNote` across the top ("drop on a section to move it there").
        public void ShowDropHints(bool on, string dragNote = null)
        {
            if (dropHints == on) return;
            dropHints = on; if (!on) dropHover = null;
            string n = on ? dragNote : restNote;
            note.text = n ?? "";
            note.style.display = string.IsNullOrEmpty(n) ? DisplayStyle.None : DisplayStyle.Flex;
            canvas.MarkDirtyRepaint();
        }

        public void SetDropHover(string key)
        {
            if (dropHover == key) return;
            dropHover = key; canvas.MarkDirtyRepaint();
        }

        // ------------------------------------------------------------------

        static float Weight(YardSectionKind k) =>
            k == YardSectionKind.Stern ? 1.15f : k == YardSectionKind.Bow ? 1.25f : 1f;

        void LayoutSections()
        {
            float total = 0f;
            int n = Mathf.Min(sections.Length, sx.Length);
            for (int i = 0; i < n; i++) total += Weight(sections[i].kind);
            float u = total > 0f ? (X1 - X0) / total : 0f, x = X0;
            for (int i = 0; i < n; i++) { sx[i] = x; sw[i] = Weight(sections[i].kind) * u; x += sw[i]; }
        }

        static Length Pct(float v, float of) => Length.Percent(v / of * 100f);

        void RebuildOverlay()
        {
            layer.Clear(); hitByKey.Clear();
            int n = Mathf.Min(sections.Length, sx.Length);
            for (int i = 0; i < n; i++)
            {
                var s = sections[i];
                string key = s.key;
                var hit = new VisualElement();
                hit.AddToClassList("ys-strip-hit");
                hit.style.left = Pct(sx[i], W); hit.style.width = Pct(sw[i], W);
                hit.RegisterCallback<ClickEvent>(_ => onSectionTap?.Invoke(key));
                layer.Add(hit);
                if (key != null) hitByKey[key] = hit;

                var lab = new Label(s.label ?? "") { pickingMode = PickingMode.Ignore };
                lab.AddToClassList("ys-strip-label");
                if (s.selected || s.kind == YardSectionKind.New) lab.AddToClassList("ys-strip-label--on");
                lab.style.left = Pct(sx[i], W); lab.style.width = Pct(sw[i], W);
                layer.Add(lab);

                if (s.raisePreview)
                {
                    int extra = Mathf.Max(0, s.decksUsed - 2);
                    var r = new Label(string.IsNullOrEmpty(s.raiseLabel) ? "+ Upper" : s.raiseLabel) { pickingMode = PickingMode.Ignore };
                    r.AddToClassList("ys-strip-raise");
                    r.style.left = Pct(sx[i], W); r.style.width = Pct(sw[i], W);
                    r.style.top = Pct(Deck - (extra + 1) * Lv, H); r.style.height = Pct(Lv, H);
                    layer.Add(r);
                }
            }
            foreach (var c in chips)
            {
                if (c.index <= 0 || c.index >= n) continue;
                int index = c.index;
                var chip = new VisualElement();
                chip.AddToClassList("ys-plus");
                if (c.locked) { chip.AddToClassList("ys-plus--locked"); chip.tooltip = c.lockReason; }
                chip.style.left = Pct(sx[index], W); chip.style.top = Pct(Deck + 18f, H);
                var dot = new Label("+") { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("ys-plus-dot");
                chip.Add(dot);
                chip.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); onPlusTap?.Invoke(index); });
                layer.Add(chip);
            }
        }

        // ------------------------------------------------------------------
        // Painting, in design-box units
        // ------------------------------------------------------------------

        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            float k = canvas.contentRect.width / W;
            if (k <= 0f) return;
            Vector2 V(float x, float y) => new Vector2(x * k, y * k);
            void Box(float x, float y, float w, float h, Color? fill, Color? stroke, float lw)
            {
                p.BeginPath(); p.MoveTo(V(x, y)); p.LineTo(V(x + w, y)); p.LineTo(V(x + w, y + h)); p.LineTo(V(x, y + h)); p.ClosePath();
                if (fill.HasValue) { p.fillColor = fill.Value; p.Fill(); }
                if (stroke.HasValue) { p.strokeColor = stroke.Value; p.lineWidth = lw * k; p.Stroke(); }
            }
            void Seg(float x, float y, float a, float b, Color c, float lw)
            { p.strokeColor = c; p.lineWidth = lw * k; p.BeginPath(); p.MoveTo(V(x, y)); p.LineTo(V(a, b)); p.Stroke(); }
            void Dashed(float x, float y, float a, float b, Color c, float lw, float dash, float gap)
            {
                var from = new Vector2(x, y); var to = new Vector2(a, b);
                float len = Vector2.Distance(from, to); var dir = len > 0f ? (to - from) / len : Vector2.zero;
                for (float t = 0f; t < len; t += dash + gap)
                {
                    var q0 = from + dir * t; var q1 = from + dir * Mathf.Min(len, t + dash);
                    Seg(q0.x, q0.y, q1.x, q1.y, c, lw);
                }
            }
            void DashedBox(float x, float y, float w, float h, Color? fill, Color c, float lw)
            {
                if (fill.HasValue) Box(x, y, w, h, fill, null, 0f);
                Dashed(x, y, x + w, y, c, lw, 5f, 3f); Dashed(x + w, y, x + w, y + h, c, lw, 5f, 3f);
                Dashed(x + w, y + h, x, y + h, c, lw, 5f, 3f); Dashed(x, y + h, x, y, c, lw, 5f, 3f);
            }
            Color A(Color c, float a) { c.a = a; return c; }

            // sky and sea behind everything
            Box(0, 0, W, H * 0.64f, YardPalette.Sky, null, 0f);
            Box(0, H * 0.64f, W, H * 0.36f, new Color32(14, 34, 51, 255), null, 0f);

            int n = Mathf.Min(sections.Length, sx.Length);
            if (n == 0) return;

            // selection and drop highlights
            for (int i = 0; i < n; i++)
            {
                if (sections[i].selected && !dropHints)
                {
                    p.BeginPath(); RoundRect(p, V(sx[i] + 1, 12), V(sw[i] - 2, Keel - 6), 8f * k);
                    p.fillColor = A(YardPalette.Ice, 0.13f); p.Fill();
                    p.strokeColor = YardPalette.Ice; p.lineWidth = 2f * k; p.Stroke();
                }
                if (dropHints && sections[i].kind != YardSectionKind.New)
                {
                    bool hot = sections[i].key == dropHover;
                    DashedBox(sx[i] + 2, 14, sw[i] - 4, Keel - 10, A(YardPalette.Moss, hot ? 0.28f : 0.10f), YardPalette.Moss, hot ? 2.4f : 1.6f);
                }
            }

            // the stern wheel
            float s0 = sx[0], wy = Deck + 8f;
            p.BeginPath(); p.Arc(V(s0 - 6, wy), 19f * k, 0f, 360f);
            p.fillColor = YardPalette.WoodDk; p.Fill();
            p.strokeColor = YardPalette.Wood2; p.lineWidth = 2.5f * k; p.Stroke();
            for (int a = 0; a < 8; a++)
            {
                float r = a * Mathf.PI / 4f;
                Seg(s0 - 6, wy, s0 - 6 + 19f * Mathf.Cos(r), wy + 19f * Mathf.Sin(r), YardPalette.Wood2, 2f);
            }

            // the hull
            float bowX = sx[n - 1], bowW = sw[n - 1], bx = bowX + bowW;
            p.BeginPath();
            p.MoveTo(V(s0, Deck)); p.LineTo(V(s0 + 4, Keel - 6));
            p.QuadraticCurveTo(V(s0 + 6, Keel), V(s0 + 16, Keel));
            p.LineTo(V(bowX + bowW * 0.35f, Keel));
            p.QuadraticCurveTo(V(bx - 6, Keel - 2), V(bx, Deck - 8));
            p.LineTo(V(bx - 4, Deck)); p.ClosePath();
            p.fillColor = YardPalette.Wood; p.Fill();
            p.strokeColor = YardPalette.WoodDk; p.lineWidth = 2f * k; p.Stroke();
            Seg(s0 + 2, Deck + 5, bx - 3, Deck + 5, YardPalette.Wood2, 3f);

            // stacked decks, gun ports, dividers
            float topY = Deck;
            for (int i = 0; i < n; i++)
            {
                var s = sections[i];
                bool isNew = s.kind == YardSectionKind.New;
                int extra = Mathf.Max(0, s.decksUsed - 2);
                float inL = s.kind == YardSectionKind.Stern ? 3f : 0f, inR = s.kind == YardSectionKind.Bow ? 10f : 0f;
                for (int l = 1; l <= extra; l++)
                {
                    float y = Deck - l * Lv;
                    Box(sx[i] + inL, y, sw[i] - inL - inR, Lv, YardPalette.Wood2, YardPalette.WoodDk, 1.6f);
                    topY = Mathf.Min(topY, y);
                }
                if (s.raisePreview)
                    DashedBox(sx[i] + 1, Deck - (extra + 1) * Lv, sw[i] - 2, Lv, A(YardPalette.Ice, 0.12f), YardPalette.Ice, 2f);
                if (isNew)
                    DashedBox(sx[i] + 1, Deck - 2, sw[i] - 2, Keel - Deck + 2, A(YardPalette.Ice, 0.15f), YardPalette.Ice, 2f);

                float px = sx[i] + sw[i] * (s.kind == YardSectionKind.Bow ? 0.35f : 0.5f) - 5f;
                if (!isNew && s.decksUsed >= 2) Port(px, Deck + 14);
                for (int l = 1; l <= extra; l++) Port(px, Deck - l * Lv + 7);

                if (i > 0) Dashed(sx[i], Deck + 6, sx[i], Keel - 2, YardPalette.WoodDk, 1.5f, 3f, 3f);
            }
            void Port(float x, float y)
            {
                Box(x, y, 10, 8, new Color32(28, 20, 13, 255), YardPalette.Brass, 1.4f);
                Box(x + 3, y + 2, 4, 4, YardPalette.Iron, null, 0f);
            }

            // rail and chimney
            Seg(s0 + 2, Deck - 5, bx - 8, Deck - 5, YardPalette.Wood2, 1.5f);
            float cx = sx[0] + sw[0] * 0.55f;
            Box(cx - 5, topY - 30, 10, Deck - topY + 30, YardPalette.Iron, new Color32(21, 24, 27, 255), 1.5f);
            Box(cx - 6, topY - 32, 12, 4, YardPalette.Brass, null, 0f);

            // water over the lower hull, and the wave line
            Box(0, WL, W, H - WL, A(YardPalette.Sea, 0.72f), null, 0f);
            p.strokeColor = YardPalette.Foam; p.lineWidth = 1.5f * k; p.BeginPath(); p.MoveTo(V(0, WL));
            for (int i = 0; i < 18; i++) p.QuadraticCurveTo(V(i * 20 + 10, WL - 2), V(i * 20 + 20, WL));
            p.Stroke();
        }

        static void RoundRect(Painter2D p, Vector2 pos, Vector2 size, float r)
        {
            r = Mathf.Min(r, size.x * 0.5f, size.y * 0.5f);
            float x = pos.x, y = pos.y, w = size.x, h = size.y;
            p.MoveTo(new Vector2(x + r, y));
            p.LineTo(new Vector2(x + w - r, y)); p.ArcTo(new Vector2(x + w, y), new Vector2(x + w, y + r), r);
            p.LineTo(new Vector2(x + w, y + h - r)); p.ArcTo(new Vector2(x + w, y + h), new Vector2(x + w - r, y + h), r);
            p.LineTo(new Vector2(x + r, y + h)); p.ArcTo(new Vector2(x, y + h), new Vector2(x, y + h - r), r);
            p.LineTo(new Vector2(x, y + r)); p.ArcTo(new Vector2(x, y), new Vector2(x + r, y), r);
            p.ClosePath();
        }
    }
}
