using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard
{
    /// **The Interior page's 2D cutaway** (docs/SHIPYARD-SECTIONS-UI.md step
    /// 2, replacing the old +/- berths list -- Kevin 2026-09-25: "the system
    /// for balancing berths/storage/cannon capacity needs to be more
    /// intuitive... one should never feel like they're missing out on
    /// features because of poor UI and player feedback"). A simple side-view
    /// silhouette of THIS section, its interior divided into compartment
    /// cells (the section's own space budget), each cell a crate (cargo) or
    /// a bunk pair -- tap to swap. Gun slots sit on the deck line as small
    /// port/starboard markers, tap to fit/remove from the dry dock --
    /// labelled with the exact same `EquipmentSlotView.label` string the
    /// Guns page shows (docs/SHIPYARD-UX-AUDIT.md "For the two changes in
    /// flight": one slot-naming scheme, not two.
    ///
    /// Pure display + tap targets: every number it draws is handed in
    /// already computed (`SectionSpaceView`/`EquipmentSlotView`, the same
    /// backend views the old list used) -- this file owns none of the
    /// berths/hold math, only how it reads as a picture. Rebuilt fresh every
    /// `ShipyardSectionSheet.Fill()` call, same convention as the rest of
    /// that file (`YardIcon`, the gun rows). `YardIcon` (ShipyardScreen.cs,
    /// another agent's file) has no crate/bunk glyphs, so this file draws
    /// its own tiny icons rather than extend a file it does not own.
    public sealed class SectionCutaway : VisualElement
    {
        /// One cell of the compartment row. `berthDelta` is what tapping it
        /// asks `WithBerths` for -- the sheet works out the number (+2 for a
        /// cargo cell, -2 or -1 for a bunk cell, backend clamps the rest),
        /// this file just renders it and fires the tap.
        public readonly struct Cell
        {
            public readonly bool isBunk;
            public readonly int bunkCount; // 1 or 2, meaningful only when isBunk
            public readonly int berthDelta;
            public Cell(bool isBunk, int bunkCount, int berthDelta)
            { this.isBunk = isBunk; this.bunkCount = bunkCount; this.berthDelta = berthDelta; }
        }

        /// One gun slot marker, already resolved the same way the Guns page
        /// resolves its rows (occupied/usable/dry-dock stock) -- `label` is
        /// carried through unchanged so a tooltip/legend never re-word it.
        public sealed class GunMarker
        {
            public string slotId;
            public string side; // "port" or "starboard"
            public string label;
            public bool occupied;
            public bool enabled;
            public string status;
        }

        readonly VisualElement hull;
        readonly VisualElement cellRow;
        readonly VisualElement gunLayer;

        public SectionCutaway()
        {
            AddToClassList("yard-cutaway");

            hull = new VisualElement();
            hull.AddToClassList("yard-cutaway-hull");
            Add(hull);

            gunLayer = new VisualElement();
            gunLayer.AddToClassList("yard-cutaway-guns");
            hull.Add(gunLayer);

            cellRow = new VisualElement();
            cellRow.AddToClassList("yard-cutaway-cells");
            hull.Add(cellRow);
        }

        /// Rebuilds the whole picture: the hull silhouette (stern/middle/bow,
        /// raised or not -- drawn once per call via `generateVisualContent`,
        /// same trick `YardIcon` uses), the gun markers, and the cell row.
        public void Set(bool isStern, bool isBow, bool raised,
            IReadOnlyList<Cell> cells, Action<Cell> onTapCell,
            IReadOnlyList<GunMarker> guns, Action<GunMarker> onTapGun)
        {
            if (paintedHull != null) hull.generateVisualContent -= paintedHull;
            paintedHull = ctx => PaintHull(ctx, isStern, isBow, raised);
            hull.generateVisualContent += paintedHull;
            hull.MarkDirtyRepaint();

            gunLayer.Clear();
            if (guns != null)
            {
                for (int i = 0; i < guns.Count; i++)
                {
                    var g = guns[i];
                    float t = guns.Count <= 1 ? 0.5f : i / (float)(guns.Count - 1);
                    var marker = BuildGunMarker(g, onTapGun);
                    marker.style.left = Length.Percent(Mathf.Lerp(12f, 84f, t));
                    // Port drawn above the deck line, starboard below it --
                    // a side-view cutaway cannot show both sides apart in
                    // depth, so vertical offset stands in for which side.
                    marker.style.top = Length.Percent(g.side == "port" ? 2f : 30f);
                    gunLayer.Add(marker);
                }
            }

            cellRow.Clear();
            if (cells != null)
                foreach (var c in cells) cellRow.Add(BuildCell(c, onTapCell));
        }

        Action<MeshGenerationContext> paintedHull;

        /// The Button is now just the hit box (docs/SHIPYARD-UX-AUDIT.md
        /// item 5: gun markers were 32pt, below the GDD's 44pt thumb-target
        /// floor) -- the drawn circle is a separate, still-32px child so
        /// the visual doesn't have to grow along with the tap target.
        VisualElement BuildGunMarker(GunMarker g, Action<GunMarker> onTap)
        {
            var b = new Button(() => onTap?.Invoke(g));
            b.AddToClassList("yard-cutaway-gun");
            b.SetEnabled(g.enabled);
            b.tooltip = string.IsNullOrEmpty(g.label) ? (g.status ?? "") : g.label + " — " + g.status;
            var dot = new Label(g.side == "port" ? "P" : "S");
            dot.AddToClassList("yard-cutaway-gun-dot");
            dot.EnableInClassList("yard-cutaway-gun-dot--fitted", g.occupied);
            dot.pickingMode = PickingMode.Ignore;
            b.Add(dot);
            return b;
        }

        VisualElement BuildCell(Cell c, Action<Cell> onTap)
        {
            var b = new Button(() => onTap?.Invoke(c));
            b.AddToClassList("yard-cutaway-cell");
            b.EnableInClassList("yard-cutaway-cell--bunk", c.isBunk);
            if (c.isBunk)
            {
                var icons = new VisualElement(); icons.AddToClassList("yard-cutaway-cell-icons");
                icons.Add(new CutawayIcon("bunk"));
                if (c.bunkCount > 1) icons.Add(new CutawayIcon("bunk"));
                b.Add(icons);
                b.tooltip = c.bunkCount > 1 ? "2 bunks — tap to load cargo here instead" : "1 bunk — tap to load cargo here instead";
            }
            else
            {
                b.Add(new CutawayIcon("crate"));
                b.tooltip = "Cargo — tap to fit bunks here instead";
            }
            return b;
        }

        /// Deliberately schematic, not to scale: a keel curve, a deck line
        /// (stepped up in the middle when `raised`), a pointed bow and a
        /// flat-ish stern transom, a flat vertical interface on whichever
        /// side(s) join a neighbour. "Simple, readable silhouette" is the
        /// brief -- Kevin can see at a glance which end of the ship this is.
        static void PaintHull(MeshGenerationContext ctx, bool isStern, bool isBow, bool raised)
        {
            var rect = ctx.visualElement.contentRect;
            float w = rect.width, h = rect.height;
            if (w <= 1 || h <= 1) return;
            var p = ctx.painter2D;
            p.strokeColor = new Color32(171, 209, 226, 255);
            p.fillColor = new Color32(31, 57, 72, 255);
            p.lineWidth = 2f;

            float deckY = h * 0.28f;
            float keelY = h * 0.86f;
            float aftX = isStern ? w * 0.10f : 0f;
            float fwdX = isBow ? w * 0.94f : w;

            p.BeginPath();
            p.MoveTo(new Vector2(aftX, deckY));
            if (isBow)
            {
                p.LineTo(new Vector2(w * 0.80f, deckY));
                p.LineTo(new Vector2(fwdX, h * 0.5f)); // the prow tip
                p.LineTo(new Vector2(w * 0.80f, keelY));
            }
            else
            {
                p.LineTo(new Vector2(fwdX, deckY));
                p.LineTo(new Vector2(fwdX, keelY));
            }
            // Keel: a shallow curve back to the aft edge.
            p.BezierCurveTo(new Vector2(w * 0.55f, h * 0.98f), new Vector2(w * 0.35f, h * 0.98f), new Vector2(aftX, keelY));
            if (isStern) p.LineTo(new Vector2(aftX, deckY));
            p.ClosePath();
            p.Fill();
            p.Stroke();

            if (raised)
            {
                float rx0 = w * 0.30f, rx1 = w * 0.70f, ry = deckY - h * 0.16f;
                p.BeginPath();
                p.MoveTo(new Vector2(rx0, deckY));
                p.LineTo(new Vector2(rx0, ry));
                p.LineTo(new Vector2(rx1, ry));
                p.LineTo(new Vector2(rx1, deckY));
                p.Stroke();
            }

            // Deck line, full width -- the line gun markers sit near.
            p.BeginPath();
            p.MoveTo(new Vector2(aftX, deckY));
            p.LineTo(new Vector2(isBow ? w * 0.80f : fwdX, deckY));
            p.Stroke();
        }
    }

    /// A tiny self-drawn glyph for the cutaway's cells only (crate, bunk) --
    /// deliberately separate from `ShipyardScreen.cs`'s `YardIcon` (another
    /// agent's file, no crate/bunk kind, not touched here), same
    /// `generateVisualContent`/`Painter2D` technique.
    sealed class CutawayIcon : VisualElement
    {
        public CutawayIcon(string kind)
        {
            AddToClassList("yard-cutaway-icon"); pickingMode = PickingMode.Ignore;
            generateVisualContent += ctx =>
            {
                var p = ctx.painter2D; float s = Mathf.Min(contentRect.width, contentRect.height) / 24f;
                p.strokeColor = new Color32(225, 240, 246, 255); p.lineWidth = 1.6f * s;
                void Rect(float x, float y, float w, float h) { p.BeginPath(); p.MoveTo(new Vector2(x, y) * s); p.LineTo(new Vector2(x + w, y) * s); p.LineTo(new Vector2(x + w, y + h) * s); p.LineTo(new Vector2(x, y + h) * s); p.ClosePath(); p.Stroke(); }
                void Line(float x0, float y0, float x1, float y1) { p.BeginPath(); p.MoveTo(new Vector2(x0, y0) * s); p.LineTo(new Vector2(x1, y1) * s); p.Stroke(); }
                if (kind == "crate")
                {
                    Rect(4, 4, 16, 16);
                    Line(4, 12, 20, 12); Line(12, 4, 12, 20);
                }
                else // bunk: a simple bed frame -- headboard + a mattress line
                {
                    Rect(3, 9, 18, 9);
                    Line(3, 9, 3, 20); Line(21, 9, 21, 20);
                    Line(3, 13, 21, 13);
                }
            };
        }
    }
}
