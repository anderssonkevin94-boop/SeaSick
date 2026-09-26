using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard
{
    /// **The Interior page's 2D cutaway** (docs/SHIPYARD-SECTIONS-UI.md step
    /// 2 -- Kevin 2026-09-25: "the system for balancing berths/storage/cannon
    /// capacity needs to be more intuitive... one should never feel like
    /// they're missing out on features because of poor UI and player
    /// feedback"). A side view of THIS section: its compartments drawn as a
    /// grid INSIDE the hull (bunks on top, cargo below -- a raised section's
    /// top row is its between-deck), gun slots as markers standing on the
    /// top deck at their real fore/aft spot, each station's port/starboard
    /// pair side by side (P left, S right).
    ///
    /// v2 (2026-09-26 redesign): the v1 row of cells was a flex-wrap row
    /// laid over a fixed 150px hull, so cells overflowed the hull on the
    /// left, wrapped BELOW the keel, and gun markers overlapped them. Now
    /// every piece is placed by `Layout()` from ONE set of numbers (the
    /// element's measured width): the grid's columns/rows are chosen to fit
    /// the hull interior at >= 44pt cells, the hull and the element's own
    /// height are then grown to hold exactly that grid, and the gun lane
    /// sits above the deck, so nothing can overlap anything by construction.
    ///
    /// Pure display + tap targets: every number is handed in already
    /// computed (`SectionSpaceView`/`EquipmentSlotView` via the sheet); this
    /// file owns none of the berths/hold math, only how it reads.
    public sealed class SectionCutaway : VisualElement
    {
        /// One compartment. `berthDelta` is what tapping it asks
        /// `WithBerths` for (+2 for a cargo cell, -2/-1 for a bunk cell).
        public readonly struct Cell
        {
            public readonly bool isBunk;
            public readonly int bunkCount; // 1 or 2, meaningful only when isBunk
            public readonly int berthDelta;
            public Cell(bool isBunk, int bunkCount, int berthDelta)
            { this.isBunk = isBunk; this.bunkCount = bunkCount; this.berthDelta = berthDelta; }
        }

        /// One gun slot marker, resolved the same way the Guns page resolves
        /// its rows. `along` is 0 (aft end of the section) .. 1 (fore end),
        /// from the slot's real position; `label` is the Guns page's own
        /// string, carried unchanged.
        public sealed class GunMarker
        {
            public string slotId;
            public string side; // "port" or "starboard"
            public string label;
            public bool occupied;
            public bool enabled;
            public string status;
            public float along = 0.5f;
        }

        // ---- geometry constants (panel units) ----
        const float Lane = 44f;          // one gun lane == one 44pt hit row
        const float CellMin = 44f, CellMax = 60f, CellH = 44f, Gap = 4f;
        const float PadTop = 6f, PadKeel = 11f, RowPadTween = 5f;
        const float KeyW = 78f;

        /// Last measured width, shared by every instance: the sheet rebuilds
        /// this element on every refresh tick, so laying out from the last
        /// real width avoids a one-frame jump from a guess each time.
        static float lastWidth = 340f;

        bool isStern, isBow, raised;
        IReadOnlyList<Cell> cells; Action<Cell> onTapCell;
        IReadOnlyList<GunMarker> guns; Action<GunMarker> onTapGun;
        int flashIndex = -1;
        float laidOutWidth = -1f;

        // Geometry of the current layout, read by the painter.
        float W, yTop, yMain, yKeel, H;
        readonly List<Vector2> stems = new List<Vector2>(); // (x, yFrom) pairs down to yTop
        readonly List<Rect> tweenCells = new List<Rect>();

        public SectionCutaway()
        {
            AddToClassList("yard-cutaway");
            style.flexShrink = 0f;
            style.position = Position.Relative;
            generateVisualContent += Paint;
            RegisterCallback<GeometryChangedEvent>(e =>
            {
                float w = e.newRect.width;
                if (w > 1f && Mathf.Abs(w - laidOutWidth) > 0.5f) { lastWidth = w; Layout(w); }
            });
        }

        /// `flashIndex`: the cell (index into `cells`) that the last tap just
        /// changed, drawn highlighted for a beat (-1 for none).
        public void Set(bool isStern, bool isBow, bool raised,
            IReadOnlyList<Cell> cells, Action<Cell> onTapCell,
            IReadOnlyList<GunMarker> guns, Action<GunMarker> onTapGun, int flashIndex = -1)
        {
            this.isStern = isStern; this.isBow = isBow; this.raised = raised;
            this.cells = cells ?? Array.Empty<Cell>(); this.onTapCell = onTapCell;
            this.guns = guns ?? Array.Empty<GunMarker>(); this.onTapGun = onTapGun;
            this.flashIndex = flashIndex;
            Layout(lastWidth);
        }

        // ---- layout -----------------------------------------------------------

        /// Interior x-range for cells: clear of the stern transom and of the
        /// bow's raked stem, a margin at a plain joint.
        static void InteriorX(float w, bool isStern, bool isBow, out float xL, out float xR)
        {
            xL = isStern ? 22f : 10f;
            xR = isBow ? w - 70f : w - 10f;
        }

        /// The whole vertical layout from one width and the cell count --
        /// shared by `Layout` and `EstimateHeight`, so the sheet's page
        /// planning measures exactly what will be drawn.
        static void Frame(float w, int n, bool raised, bool isStern, bool isBow, bool hasGuns,
            out int rows, out float cw, out float yTop, out float yMain, out float holdTop, out float yKeel, out float h)
        {
            yTop = hasGuns ? Lane + 6f : 8f;
            InteriorX(w, isStern, isBow, out float xL, out float xR);
            float iw = Mathf.Max(CellMin, xR - xL);
            int maxCols = Mathf.Max(1, Mathf.FloorToInt((iw + Gap) / (CellMin + Gap)));
            rows = n == 0 ? 1 : Mathf.CeilToInt(n / (float)maxCols);
            if (raised) rows = Mathf.Max(2, rows); // top row = the between-deck
            int perRowMax = n == 0 ? 0 : Mathf.CeilToInt(n / (float)rows);
            cw = perRowMax == 0 ? CellMin : Mathf.Clamp((iw - Gap * (perRowMax - 1)) / perRowMax, CellMin, CellMax);
            int holdRows = raised ? rows - 1 : rows;
            yMain = raised ? yTop + RowPadTween * 2f + CellH : yTop;
            holdTop = yMain + PadTop;
            yKeel = holdTop + holdRows * CellH + (holdRows - 1) * Gap + PadKeel;
            h = yKeel + 7f; // keel sag 4.5 + stroke
        }

        /// Height the cutaway will take at the last measured width (plus its
        /// bottom margin), for `ShipyardSectionSheet.PlanPages`.
        public static float EstimateHeight(int cells, bool raised, bool isStern, bool isBow, bool hasGuns)
        {
            Frame(lastWidth, cells, raised, isStern, isBow, hasGuns, out _, out _, out _, out _, out _, out _, out float h);
            return h + 6f;
        }

        void Layout(float w)
        {
            Clear();
            stems.Clear(); tweenCells.Clear();
            laidOutWidth = w;
            W = w;

            int n = cells.Count;
            bool hasGuns = guns.Count > 0;
            Frame(w, n, raised, isStern, isBow, hasGuns, out int rows, out float cw, out yTop, out yMain, out float holdTop, out yKeel, out H);
            style.height = H;
            InteriorX(w, isStern, isBow, out float xL, out float xR);
            float iw = Mathf.Max(CellMin, xR - xL);

            // ---- cells, row-major top to bottom (bunks arrive first, so they
            // fill the between-deck / top row; cargo settles in the hold).
            int idx = 0;
            for (int r = 0; r < rows && idx < n; r++)
            {
                int inRow = n / rows + (r < n % rows ? 1 : 0);
                float rowW = inRow * cw + (inRow - 1) * Gap;
                float x0 = xL + (iw - rowW) * 0.5f;
                float y = raised && r == 0 ? yTop + RowPadTween : holdTop + (raised ? r - 1 : r) * (CellH + Gap);
                for (int c = 0; c < inRow && idx < n; c++, idx++)
                {
                    var rect = new Rect(x0 + c * (cw + Gap), y, cw, CellH);
                    if (raised && r == 0) tweenCells.Add(rect);
                    Add(BuildCell(cells[idx], idx == flashIndex, rect));
                }
            }

            // ---- guns: ONE lane above the deck. Each gun station is a
            // port/starboard pair side by side (P left, S right) at the
            // station's real fore/aft spot; stations are nudged apart so no
            // two 44pt hit boxes overlap. Stems are drawn down to the deck.
            if (hasGuns) PlaceGuns(isStern ? 22f : 4f, isBow ? w - 30f : w - 4f);

            MarkDirtyRepaint();
        }

        void PlaceGuns(float deckL, float deckR)
        {
            // Group into stations: guns within 3% of the section's length
            // of each other stand at the same spot (a port/starboard pair).
            var sorted = new List<GunMarker>(guns);
            sorted.Sort((a, b) => a.along != b.along ? a.along.CompareTo(b.along) : string.CompareOrdinal(a.side == "port" ? "0" : "1", b.side == "port" ? "0" : "1"));
            var stations = new List<List<GunMarker>>();
            foreach (var g in sorted)
            {
                var last = stations.Count > 0 ? stations[stations.Count - 1] : null;
                if (last != null && Mathf.Abs(last[0].along - g.along) < 0.03f && last.Count < 2 && last[0].side != g.side) last.Add(g);
                else stations.Add(new List<GunMarker> { g });
            }
            int n = stations.Count;
            var xs = new float[n]; var half = new float[n];
            for (int i = 0; i < n; i++)
            {
                half[i] = stations[i].Count * Lane * 0.5f;
                float lo = deckL + half[i], hi = deckR - half[i];
                xs[i] = Mathf.Lerp(lo, hi, Mathf.Clamp01(stations[i][0].along));
            }
            for (int i = 1; i < n; i++) xs[i] = Mathf.Max(xs[i], xs[i - 1] + half[i - 1] + half[i]);
            if (n > 0 && xs[n - 1] > deckR - half[n - 1])
            {
                xs[n - 1] = deckR - half[n - 1];
                for (int i = n - 2; i >= 0; i--) xs[i] = Mathf.Min(xs[i], xs[i + 1] - half[i + 1] - half[i]);
            }

            for (int i = 0; i < n; i++)
            {
                var st = stations[i];
                for (int k = 0; k < st.Count; k++)
                {
                    float cx = st.Count == 1 ? xs[i] : xs[i] + (k == 0 ? -Lane * 0.5f : Lane * 0.5f);
                    var m = BuildGunMarker(st[k], onTapGun);
                    m.style.left = cx - Lane * 0.5f; m.style.top = 0f;
                    Add(m);
                    stems.Add(new Vector2(cx, Lane - 4f));
                }
            }

            // "P port · S starboard" key where the lane has room for it.
            float freeL = n > 0 ? xs[0] - half[0] - deckL : deckR - deckL;
            float freeR = n > 0 ? deckR - (xs[n - 1] + half[n - 1]) : 0f;
            if (Mathf.Max(freeL, freeR) >= KeyW)
            {
                var cap = new Label("P port\nS starboard");
                cap.AddToClassList("yard-cutaway-lane");
                cap.pickingMode = PickingMode.Ignore;
                cap.style.position = Position.Absolute;
                cap.style.top = 0f; cap.style.height = Lane; cap.style.width = KeyW;
                bool left = freeL >= freeR;
                cap.style.left = left ? deckL : deckR - KeyW;
                cap.style.unityTextAlign = left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
                Add(cap);
            }
        }

        // ---- pieces -------------------------------------------------------------

        VisualElement BuildGunMarker(GunMarker g, Action<GunMarker> onTap)
        {
            var b = new Button(() => onTap?.Invoke(g));
            b.AddToClassList("yard-cutaway-gun");
            b.SetEnabled(g.enabled);
            b.tooltip = string.IsNullOrEmpty(g.label) ? (g.status ?? "") : g.label + " — " + g.status;
            b.style.position = Position.Absolute;
            var dot = new VisualElement();
            dot.AddToClassList("yard-cutaway-gun-dot");
            dot.EnableInClassList("yard-cutaway-gun-dot--fitted", g.occupied);
            dot.EnableInClassList("yard-cutaway-gun-dot--blocked", !g.occupied && !g.enabled);
            dot.pickingMode = PickingMode.Ignore;
            dot.Add(new CutawayIcon(g.occupied ? "cannon" : g.enabled ? "plus" : "none",
                g.occupied ? new Color32(22, 43, 57, 255) : new Color32(225, 240, 246, 255)));
            b.Add(dot);
            var letter = new Label(g.side == "port" ? "P" : "S");
            letter.AddToClassList("yard-cutaway-gun-side");
            letter.pickingMode = PickingMode.Ignore;
            b.Add(letter);
            return b;
        }

        VisualElement BuildCell(Cell c, bool flash, Rect r)
        {
            var b = new Button(() => onTapCell?.Invoke(c));
            b.AddToClassList("yard-cutaway-cell");
            b.EnableInClassList("yard-cutaway-cell--bunk", c.isBunk);
            b.EnableInClassList("yard-cutaway-cell--cargo", !c.isBunk);
            b.EnableInClassList("yard-cutaway-cell--flash", flash);
            b.style.position = Position.Absolute;
            b.style.left = r.x; b.style.top = r.y; b.style.width = r.width; b.style.height = r.height;
            if (c.isBunk)
            {
                b.Add(new CutawayIcon(c.bunkCount > 1 ? "bunk2" : "bunk1", new Color32(225, 240, 246, 255)));
                b.tooltip = c.bunkCount > 1 ? "2 bunks — tap to load cargo here instead" : "1 bunk — tap to load cargo here instead";
            }
            else
            {
                b.Add(new CutawayIcon("crate", new Color32(246, 220, 170, 255)));
                b.tooltip = "Cargo — tap to fit bunks here instead";
            }
            return b;
        }

        // ---- painting -----------------------------------------------------------

        static readonly Color Stroke = new Color32(171, 209, 226, 255);
        static readonly Color HullFill = new Color32(35, 64, 80, 255);
        static readonly Color TweenFill = new Color32(46, 80, 99, 255);
        static readonly Color Faint = new Color32(171, 209, 226, 110);

        void Paint(MeshGenerationContext ctx)
        {
            if (W <= 1f) return;
            var p = ctx.painter2D;
            float w = W;
            float sheer = isBow ? 8f : 0f;

            // Silhouette. Stern: a transom raked outward at the top. Bow: the
            // stem sweeps up and forward to a slightly raised prow. A joint
            // to a neighbouring section is a straight cut (dashed below).
            p.fillColor = HullFill; p.strokeColor = Stroke; p.lineWidth = 2f;
            p.BeginPath();
            p.MoveTo(new Vector2(isStern ? 2f : 0f, yTop));
            if (isBow)
            {
                p.LineTo(new Vector2(w - 34f, yTop));
                p.BezierCurveTo(new Vector2(w - 20f, yTop - 2f), new Vector2(w - 8f, yTop - sheer), new Vector2(w - 2f, yTop - sheer));
                p.BezierCurveTo(new Vector2(w - 10f, yKeel - 12f), new Vector2(w - 34f, yKeel), new Vector2(w - 84f, yKeel));
            }
            else
            {
                p.LineTo(new Vector2(w, yTop));
                p.LineTo(new Vector2(w, yKeel - 6f));
                p.QuadraticCurveTo(new Vector2(w, yKeel), new Vector2(w - 12f, yKeel));
            }
            p.QuadraticCurveTo(new Vector2(w * 0.5f, yKeel + 9f), new Vector2(isStern ? 30f : 12f, yKeel));
            if (isStern) p.QuadraticCurveTo(new Vector2(16f, yKeel), new Vector2(14f, yKeel - 10f));
            else p.QuadraticCurveTo(new Vector2(0f, yKeel), new Vector2(0f, yKeel - 6f));
            p.ClosePath();
            p.Fill();

            // Between-deck band tint (raised only).
            if (raised)
            {
                p.fillColor = TweenFill;
                float xl = isStern ? 6f : 1f, xr = isBow ? w - 16f : w - 1f;
                p.BeginPath();
                p.MoveTo(new Vector2(xl, yTop + 1f)); p.LineTo(new Vector2(xr, yTop + 1f));
                p.LineTo(new Vector2(xr, yMain)); p.LineTo(new Vector2(xl, yMain)); p.ClosePath();
                p.Fill();
            }

            // Outline: the silhouette again, stroked -- except joint edges,
            // which are dashed so a middle section reads as "continues here".
            p.BeginPath();
            p.MoveTo(new Vector2(isStern ? 2f : 0f, yTop));
            if (isBow)
            {
                p.LineTo(new Vector2(w - 34f, yTop));
                p.BezierCurveTo(new Vector2(w - 20f, yTop - 2f), new Vector2(w - 8f, yTop - sheer), new Vector2(w - 2f, yTop - sheer));
                p.BezierCurveTo(new Vector2(w - 10f, yKeel - 12f), new Vector2(w - 34f, yKeel), new Vector2(w - 84f, yKeel));
            }
            else
            {
                p.LineTo(new Vector2(w, yTop));
                p.MoveTo(new Vector2(w - 12f, yKeel));
            }
            p.QuadraticCurveTo(new Vector2(w * 0.5f, yKeel + 9f), new Vector2(isStern ? 30f : 12f, yKeel));
            if (isStern)
            {
                p.QuadraticCurveTo(new Vector2(16f, yKeel), new Vector2(14f, yKeel - 10f));
                p.LineTo(new Vector2(2f, yTop));
            }
            p.Stroke();
            if (!isBow) Dashed(p, new Vector2(w - 1f, yTop), new Vector2(w - 1f, yKeel), Stroke);
            if (!isStern) Dashed(p, new Vector2(1f, yTop), new Vector2(1f, yKeel), Stroke);
            if (!isBow)
            {
                p.BeginPath(); p.MoveTo(new Vector2(w - 12f, yKeel)); p.QuadraticCurveTo(new Vector2(w, yKeel), new Vector2(w, yKeel - 6f)); p.Stroke();
            }
            if (!isStern)
            {
                p.BeginPath(); p.MoveTo(new Vector2(12f, yKeel)); p.QuadraticCurveTo(new Vector2(0f, yKeel), new Vector2(0f, yKeel - 6f)); p.Stroke();
            }

            // Top deck: heavier, it is what the guns stand on.
            p.lineWidth = 3f; p.strokeColor = Stroke;
            p.BeginPath(); p.MoveTo(new Vector2(isStern ? 2f : 0f, yTop)); p.LineTo(new Vector2(isBow ? w - 34f : w, yTop)); p.Stroke();
            // Main deck under a raised top deck.
            if (raised)
            {
                p.lineWidth = 2f;
                p.BeginPath(); p.MoveTo(new Vector2(isStern ? 8f : 0f, yMain)); p.LineTo(new Vector2(isBow ? w - 14f : w, yMain)); p.Stroke();
            }

            // Stems: marker to deck.
            p.lineWidth = 2f; p.strokeColor = Faint;
            foreach (var s in stems)
            {
                p.BeginPath(); p.MoveTo(new Vector2(s.x, s.y)); p.LineTo(new Vector2(s.x, yTop)); p.Stroke();
            }
            // Gun ports: a small notch on the deck under each stem.
            p.fillColor = Stroke;
            foreach (var s in stems)
            {
                p.BeginPath();
                p.MoveTo(new Vector2(s.x - 5f, yTop - 1.5f)); p.LineTo(new Vector2(s.x + 5f, yTop - 1.5f));
                p.LineTo(new Vector2(s.x + 5f, yTop + 4f)); p.LineTo(new Vector2(s.x - 5f, yTop + 4f)); p.ClosePath();
                p.Fill();
            }
        }

        static void Dashed(Painter2D p, Vector2 a, Vector2 b, Color c)
        {
            p.strokeColor = c; p.lineWidth = 1.5f;
            float len = Vector2.Distance(a, b); if (len < 1f) return;
            Vector2 d = (b - a) / len;
            for (float t = 0f; t < len; t += 8f)
            {
                p.BeginPath(); p.MoveTo(a + d * t); p.LineTo(a + d * Mathf.Min(len, t + 4f)); p.Stroke();
            }
            p.lineWidth = 2f;
        }
    }

    /// Self-drawn glyphs for the cutaway (crate, bunks, cannon, plus) --
    /// separate from `ShipyardScreen.cs`'s `YardIcon`, same
    /// `generateVisualContent`/`Painter2D` technique, drawn on a 24-unit
    /// grid scaled to the element. Public so the sheet's totals row and
    /// legend use the exact same pictures as the cells.
    public sealed class CutawayIcon : VisualElement
    {
        public CutawayIcon(string kind, Color color)
        {
            AddToClassList("yard-cutaway-icon"); pickingMode = PickingMode.Ignore;
            generateVisualContent += ctx =>
            {
                var r = contentRect;
                float s = Mathf.Min(r.width, r.height) / 24f;
                if (s <= 0f) return;
                var o = new Vector2((r.width - 24f * s) * 0.5f, (r.height - 24f * s) * 0.5f);
                var p = ctx.painter2D;
                p.strokeColor = color; p.fillColor = color; p.lineWidth = 1.7f * s;
                p.lineJoin = LineJoin.Round; p.lineCap = LineCap.Round;
                Vector2 P(float x, float y) => o + new Vector2(x, y) * s;
                void Line(float x0, float y0, float x1, float y1) { p.BeginPath(); p.MoveTo(P(x0, y0)); p.LineTo(P(x1, y1)); p.Stroke(); }
                void Box(float x, float y, float w, float h, bool fill = false)
                {
                    p.BeginPath(); p.MoveTo(P(x, y)); p.LineTo(P(x + w, y)); p.LineTo(P(x + w, y + h)); p.LineTo(P(x, y + h)); p.ClosePath();
                    if (fill) p.Fill(); else p.Stroke();
                }
                switch (kind)
                {
                    case "crate":
                        // A slatted crate: frame, three planks, a diagonal brace.
                        Box(3f, 5f, 18f, 15f);
                        Line(3f, 10f, 21f, 10f); Line(3f, 15f, 21f, 15f);
                        Line(5f, 18.5f, 19f, 6.5f);
                        break;
                    case "bunk1":
                        // One bed, side on: tall headboard left, short
                        // footboard right, a pillow and a blanket.
                        Line(3f, 7f, 3f, 20f); Line(21f, 12f, 21f, 20f);
                        Box(3f, 14f, 18f, 3f, true);
                        Box(5f, 10.5f, 4.5f, 3f, true);
                        Box(10.5f, 11.5f, 10.5f, 2.5f, true);
                        break;
                    case "bunk2":
                        // Bunk bed, side on: one tall post at the head end,
                        // two beds each with pillow + blanket, short foot.
                        Line(3f, 2f, 3f, 22f); Line(21f, 7f, 21f, 12f); Line(21f, 16f, 21f, 22f);
                        Box(3f, 9f, 18f, 2.6f, true);
                        Box(5f, 6f, 4.2f, 2.6f, true);
                        Box(10f, 6.8f, 11f, 2.2f, true);
                        Box(3f, 18.5f, 18f, 2.6f, true);
                        Box(5f, 15.5f, 4.2f, 2.6f, true);
                        Box(10f, 16.3f, 11f, 2.2f, true);
                        break;
                    case "cannon":
                        // Barrel on a carriage with a wheel.
                        p.BeginPath(); p.MoveTo(P(3f, 9f)); p.LineTo(P(19f, 7f)); p.LineTo(P(19f, 12f)); p.LineTo(P(3f, 13.5f)); p.ClosePath(); p.Fill();
                        Box(4f, 14f, 12f, 3f, true);
                        p.BeginPath(); p.Arc(P(8f, 18.5f), 3.2f * s, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree)); p.Fill();
                        break;
                    case "plus":
                        Line(12f, 6f, 12f, 18f); Line(6f, 12f, 18f, 12f);
                        break;
                    case "none":
                        Line(8f, 8f, 16f, 16f); Line(16f, 8f, 8f, 16f);
                        break;
                }
            };
        }
    }
}
