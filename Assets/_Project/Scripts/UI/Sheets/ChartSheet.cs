using System.Collections.Generic;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The seam to the chart instrument, until `Sheets` grows one.**
    ///
    /// The round instrument on the HUD opens this sheet, and the instrument
    /// is the other agent's file. Rather than have either of us reach into
    /// the other's, the instrument asks this for a factory. When
    /// `Sheets.RegisterChart` / `Sheets.TryOpenChart` land, this becomes one
    /// line of forwarding and then goes away.
    public static class ChartHook
    {
        public static System.Func<ISheet> Factory;

        /// Open the chart, or do nothing when nothing has registered one.
        /// True when a sheet was opened.
        public static bool TryOpen()
        {
            var make = Factory;
            if (make == null) return false;
            var sheet = make();
            if (sheet == null) return false;
            Sheets.Open(sheet);
            return true;
        }
    }

    /// **The whole archipelago, as much of it as you have earned.**
    ///
    /// Not a minimap made bigger. The minimap is an instrument — where am I,
    /// what is around me, right now. This is a document: what you have found,
    /// what your camps are doing while you are not standing in them, and
    /// where you are going next. It is drawn from `ChartData` and it decides
    /// nothing; the one thing it writes is the course, and that is a single
    /// call on the way out.
    ///
    /// **Everything in the body is one element.** A chart is a picture, not
    /// a list of rows, so the sea, the land, the wake, the raiders and the
    /// ship are a single `generateVisualContent` pass over `Painter2D`. The
    /// only children are the things Painter2D cannot draw — words — and they
    /// are absolutely positioned over it and re-placed whenever the fit
    /// changes.
    public class ChartSheet : ISheetFramed
    {
        // --- the frame (2026-09-22, "one sheet, tabs") ----------------------
        //
        // **One section, so no tab strip.** The chart IS the sheet; there is
        // nothing to tab between. It takes the standard frame for the two
        // things the frame is for: the title stays put while the chart is
        // dragged, and the three verbs stay under the thumb instead of
        // scrolling away under a map that is taller than the card.
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public Color Accent => SheetTheme.Sea;

        /// **Midnight header, 2026-09-27** (audit #10): a drawn compass
        /// glyph instead of the old parchment "N" badge, title fixed at
        /// "The chart" and the day/islands/camps sentence moved to the
        /// subtitle -- the same shape every other restyled sheet uses.
        public VisualElement BuildHeader()
        {
            var icon = new StationPage.Glyph("chart", MidnightLandHud.Ice, "cp-glyph");
            var head = CampPages.IconHeader("The chart", icon, out var sub);
            subLabel = sub;
            return head;
        }

        /// The chart's three verbs, pinned at the bottom of the frame. The
        /// host calls this after `Build`, so `chart` is already up and the
        /// track toggle's repaint has something to repaint.
        public VisualElement BuildActions()
        {
            trackBtn = SheetKit.Btn("track · last day", () =>
            {
                showTrack = !showTrack;
                // The button says what it will do next, not what it is: on a
                // 400 px card there is no room for a pill and a label both.
                trackBtn.text = showTrack ? "track · last day" : "track · off";
                chart.MarkDirtyRepaint();
            }, false, true);

            // A readout that is also the way out of a selection: pressing the
            // name drops it, which is the only "never mind" the sheet has.
            pickBtn = SheetKit.Btn("—", () => { selected = null; wordsKey = long.MinValue; Refresh(); });
            courseBtn = SheetKit.Btn("Set course", OnCourse, true);

            var row = SheetKit.Actions(trackBtn, pickBtn, courseBtn);
            Refresh();
            return row;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Register() => ChartHook.Factory = () => new ChartSheet();

        public string Title => "The chart";

        /// The ring belongs on the ship: the chart is about where she is, and
        /// there is nothing else in the world it is a sheet FOR.
        public Vector3 AnchorWorld
        {
            get
            {
                var m = SheetBits.Motor;
                return m != null ? m.transform.position : Vector3.zero;
            }
        }

        /// A chart cannot stop existing. Every other sheet in the game is
        /// about a thing that can burn down or sail away; this one is about
        /// the sea.
        public bool StillValid => true;

        // --- the pieces kept between refreshes ---------------------------------

        VisualElement chart;      // the drawing, and the parent of every word on it
        VisualElement words;      // labels that move with the fit
        Label subLabel;
        Button trackBtn;
        Button pickBtn;
        Button courseBtn;

        bool showTrack = true;
        Island selected;

        // The fit: world XZ -> element pixels. Held rather than recomputed per
        // draw so the picture does not breathe every quarter second as the
        // ship crawls across it (see `Fit`).
        Vector2 mid;
        float scale = 1f;
        float vw, vh;
        Rect fitted;              // the world rect the current fit covers

        long wordsKey = long.MinValue;
        long headKey = long.MinValue;

        // --- build ---------------------------------------------------------------

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            chart = new VisualElement();
            chart.style.width = Length.Percent(100f);
            chart.style.height = 220f;
            chart.style.marginTop = 6f;
            chart.style.marginBottom = 8f;
            chart.style.overflow = Overflow.Hidden;
            chart.pickingMode = PickingMode.Position;
            chart.generateVisualContent += Paint;
            chart.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            chart.RegisterCallback<PointerDownEvent>(OnPointer);
            root.Add(chart);

            words = new VisualElement();
            words.style.position = Position.Absolute;
            words.style.left = 0; words.style.top = 0;
            words.style.right = 0; words.style.bottom = 0;
            words.pickingMode = PickingMode.Ignore;
            chart.Add(words);

            root.Add(SheetKit.Text("Tap an island for its name. Tap a flame to set course.",
                false, true, 12f));


            Refresh();
            return root;
        }

        void OnGeometry(GeometryChangedEvent evt)
        {
            vw = evt.newRect.width;
            // A chart is a wide thing. On the docked phone card the body is
            // wider than it is allowed to be tall, and on the desk column it
            // is 400 px — both want the same ratio and a floor under it.
            float want = Mathf.Clamp(vw / 1.5f, 180f, 320f);
            if (!Mathf.Approximately(chart.style.height.value.value, want))
                chart.style.height = want;
            vh = evt.newRect.height;
            fitted = default;         // the pixels moved; refit unconditionally
            Fit();
            PlaceWords(true);
            chart.MarkDirtyRepaint();
        }

        // --- refresh ---------------------------------------------------------------

        public void Refresh()
        {
            var isles = ChartData.Islands();

            int seenCount = isles.Count;
            int camps = 0;
            foreach (var i in isles) if (i.outpost != null) camps++;

            long hk = seenCount * 1000003L + camps * 31L + TimeOfDay.Day;
            if (hk != headKey)
            {
                headKey = hk;
                if (subLabel != null)
                    subLabel.text = "Day " + TimeOfDay.Day + " · " + Cap(Words(seenCount))
                        + (seenCount == 1 ? " island seen, " : " islands seen, ")
                        + Words(camps) + (camps == 1 ? " camp" : " camps");
            }

            Fit();
            PlaceWords(false);
            Footer(isles);
            chart.MarkDirtyRepaint();
        }

        void Footer(IReadOnlyList<ChartIsland> isles)
        {
            // The picked button is a readout, not an action: it says what
            // "Set course" would do. With nothing picked it offers the same
            // answer `ChartData` gives everything else — the nearest pier.
            string label = "—";
            bool canSet = false;
            if (selected != null)
            {
                foreach (var i in isles)
                {
                    if (i.island != selected) continue;
                    label = (i.seen == Seen.Landed ? i.name : "unseen land")
                          + " · " + Km(Vector2.Distance(ChartData.ShipPos, i.centre));
                    canSet = i.outpost != null;
                    break;
                }
            }
            else if (ChartData.TryCourse(out _, out string where, out float d))
            {
                label = where + " · " + Km(d);
            }
            if (pickBtn != null) pickBtn.text = label;

            if (courseBtn == null) return;
            // **One button, and it never says two things at once.** With a
            // camp picked it sets the course; with nothing picked and a
            // course already set it is the way to drop it; otherwise it is
            // simply not a decision that can be made yet.
            courseBtn.text = canSet ? "Set course" : "Clear course";
            courseBtn.SetEnabled(canSet || ChartData.HasCourse);
        }

        void OnCourse()
        {
            if (FindSelected(out var pick) && pick.outpost != null)
            {
                ChartData.SetCourse(pick.outpost);
                // Setting a course is the decision the chart exists for, so
                // the chart folds away the moment it is made.
                Sheets.Close();
                return;
            }
            if (ChartData.HasCourse) { ChartData.ClearCourse(); Refresh(); }
        }

        bool FindSelected(out ChartIsland found)
        {
            found = default;
            if (selected == null) return false;
            foreach (var i in ChartData.Islands())
                if (i.island == selected) { found = i; return true; }
            return false;
        }

        // --- the fit -----------------------------------------------------------------

        /// **World bounds to pixels, with hysteresis.**
        ///
        /// Refitting every refresh is correct and unreadable: the ship moves,
        /// the bounds move with her, and the whole chart slides a pixel four
        /// times a second. The fit is therefore kept until it stops working —
        /// something has left the drawn rect, or the drawn rect has grown so
        /// much larger than it needs to be that the islands are specks.
        void Fit()
        {
            if (vw <= 1f || vh <= 1f) return;

            var isles = ChartData.Islands();
            Vector2 lo = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 hi = new Vector2(float.MinValue, float.MinValue);

            foreach (var i in isles)
            {
                float r = i.island != null ? i.island.MaxRadius : i.meanRadius;
                Grow(ref lo, ref hi, i.centre - Vector2.one * r);
                Grow(ref lo, ref hi, i.centre + Vector2.one * r);
            }
            // The ship is always on her own chart, so the bounds can never be
            // empty and there is no "nothing to draw" branch below.
            var ship = ChartData.ShipPos;
            Grow(ref lo, ref hi, ship - Vector2.one * 120f);
            Grow(ref lo, ref hi, ship + Vector2.one * 120f);

            // A tenth of the span of padding all round, so a coastline never
            // runs into the frame and a name has somewhere to sit.
            Vector2 span = hi - lo;
            Vector2 pad = new Vector2(Mathf.Max(80f, span.x * 0.10f), Mathf.Max(80f, span.y * 0.10f));
            lo -= pad; hi += pad;
            var want = new Rect(lo, hi - lo);

            bool keep = fitted.width > 1f
                        && fitted.Contains(new Vector2(want.xMin, want.yMin))
                        && fitted.Contains(new Vector2(want.xMax, want.yMax))
                        && want.width * want.height > fitted.width * fitted.height * 0.55f;
            if (keep) return;

            fitted = want;
            mid = want.center;
            scale = Mathf.Min(vw / Mathf.Max(1f, want.width), vh / Mathf.Max(1f, want.height));
            wordsKey = long.MinValue;     // every label has to move
        }

        static void Grow(ref Vector2 lo, ref Vector2 hi, Vector2 p)
        {
            lo.x = Mathf.Min(lo.x, p.x); lo.y = Mathf.Min(lo.y, p.y);
            hi.x = Mathf.Max(hi.x, p.x); hi.y = Mathf.Max(hi.y, p.y);
        }

        /// World XZ to element pixels. **North is up and screen y grows down**,
        /// so z is negated — the same flip `MiniMap.MapPoint` makes, and the
        /// same one that puts a chart upside down when it is forgotten.
        Vector2 P(Vector2 world) => new Vector2(
            vw * 0.5f + (world.x - mid.x) * scale,
            vh * 0.5f - (world.y - mid.y) * scale);

        // --- the words ------------------------------------------------------------------

        /// Names, ledger lines and flames. Rebuilt only when the set of
        /// things to say has changed (or the fit moved), because a rebuilt
        /// label is a label that loses the press happening on it — the same
        /// rule every other sheet in this folder keeps.
        void PlaceWords(bool force)
        {
            if (words == null || vw <= 1f) return;
            var isles = ChartData.Islands();

            long key = isles.Count;
            foreach (var i in isles)
            {
                key = key * 31 + (i.island != null ? i.island.GetInstanceID() : 0);
                key = key * 31 + (int)i.seen;
                key = key * 31 + (int)i.flame;
                key = key * 31 + (i.ledgerLine != null ? i.ledgerLine.GetHashCode() : 0);
                key = key * 31 + (i.island == selected ? 7 : 0);
            }
            if (!force && key == wordsKey) return;
            wordsKey = key;

            words.Clear();
            foreach (var isle in isles)
            {
                Vector2 c = P(isle.centre);
                float r = (isle.island != null ? isle.island.MaxRadius : isle.meanRadius) * scale;

                if (isle.seen == Seen.Landed && !string.IsNullOrEmpty(isle.name))
                    Word(isle.name, c + new Vector2(0f, -r - 16f), SheetTheme.Ink, 11f, true);
                else if (isle.seen == Seen.Glimpsed)
                    Word("unseen", c + new Vector2(0f, r + 4f), SheetTheme.InkDim, 10f, false);

                if (isle.outpost == null) continue;

                var camp = isle.outpost.CampCentre;
                Vector2 f = P(new Vector2(camp.x, camp.z));
                words.Add(Flame(isle, f));
                if (!string.IsNullOrEmpty(isle.ledgerLine))
                    Word((isle.seen == Seen.Landed ? isle.name.ToUpperInvariant() + " · " : "")
                         + isle.ledgerLine,
                         f + new Vector2(0f, 14f), FlameInk(isle.flame), 10f, true);
            }

            // The two marks that belong to the frame rather than to the sea.
            Word("N", new Vector2(vw - 16f, 6f), SheetTheme.Ink, 11f, true);
            Word(ScaleLabel(), new Vector2(14f + ScaleBarPx() * 0.5f, vh - 26f),
                SheetTheme.Ink, 9f, true);
        }

        /// A label centred on a point in the drawing. Absolute, inert, and
        /// clipped by the chart's own overflow so a name on an island at the
        /// edge cannot run out over the parchment.
        void Word(string text, Vector2 at, Color col, float size, bool bold)
        {
            var l = new Label(text ?? "");
            l.style.position = Position.Absolute;
            l.style.left = at.x - 110f;
            l.style.top = at.y - size * 0.75f;
            l.style.width = 220f;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.fontSize = size;
            l.style.color = col;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.pickingMode = PickingMode.Ignore;
            words.Add(l);
        }

        /// The camp mark: a flame you can press. Pressing it selects the camp
        /// — which is the same thing as arming "Set course" on it, and is the
        /// shortest path the sheet has from "that one" to "go there".
        VisualElement Flame(ChartIsland isle, Vector2 at)
        {
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.left = at.x - 12f;
            box.style.top = at.y - 12f;
            box.style.width = 24f;
            box.style.height = 24f;
            box.style.alignItems = Align.Center;
            box.style.justifyContent = Justify.Center;
            box.pickingMode = PickingMode.Position;

            var g = new Label("🔥");
            g.style.fontSize = 15f;
            g.pickingMode = PickingMode.Ignore;
            // Hungry is the same flame, burnt down: dim it rather than
            // recolour it, so the three states read as one mark in three
            // conditions instead of three different marks.
            g.style.opacity = isle.flame == FlameState.Hungry ? 0.45f : 1f;
            box.Add(g);

            if (isle.flame == FlameState.Raided)
            {
                var x = new Label("✕");
                x.style.position = Position.Absolute;
                x.style.fontSize = 17f;
                x.style.color = SheetTheme.Ember;
                x.style.unityFontStyleAndWeight = FontStyle.Bold;
                x.pickingMode = PickingMode.Ignore;
                box.Add(x);
            }

            if (isle.island == selected)
            {
                box.style.borderTopWidth = box.style.borderBottomWidth =
                    box.style.borderLeftWidth = box.style.borderRightWidth = 1.5f;
                box.style.borderTopColor = box.style.borderBottomColor =
                    box.style.borderLeftColor = box.style.borderRightColor = SheetTheme.Brass;
                box.style.borderTopLeftRadius = box.style.borderTopRightRadius =
                    box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = 12f;
            }

            var pick = isle.island;
            box.RegisterCallback<PointerDownEvent>(evt =>
            {
                selected = pick;
                evt.StopPropagation();
                wordsKey = long.MinValue;
                // **Next frame, not this one.** `Refresh` rebuilds `words`,
                // and `words` is this element's parent — tearing down the
                // element that is in the middle of handling a press is how a
                // pointer capture ends up owned by a dead element.
                // Scheduled on `chart`, which outlives the rebuild: an item
                // scheduled on the box itself is unscheduled the moment the
                // box is detached, which is exactly what is about to happen.
                chart.schedule.Execute(Refresh).ExecuteLater(0);
            });
            return box;
        }

        static Color FlameInk(FlameState f) => f switch
        {
            FlameState.Raided => SheetTheme.Ember,
            FlameState.Hungry => SheetTheme.Timber,
            _ => SheetTheme.Ink,
        };

        // --- the press ---------------------------------------------------------------------

        void OnPointer(PointerDownEvent evt)
        {
            Vector2 at = new Vector2(evt.localPosition.x, evt.localPosition.y);
            Island best = null;
            float bestD = float.MaxValue;
            foreach (var i in ChartData.Islands())
            {
                Vector2 c = P(i.centre);
                float r = (i.island != null ? i.island.MaxRadius : i.meanRadius) * scale;
                float d = Vector2.Distance(at, c);
                // Inside the island, or within a fingertip of it: at chart
                // scale an islet is four pixels across and would otherwise be
                // untappable on a phone.
                if (d > Mathf.Max(r, 14f)) continue;
                if (d >= bestD) continue;
                bestD = d; best = i.island;
            }
            selected = best;
            wordsKey = long.MinValue;
            Refresh();
        }

        // --- the drawing ------------------------------------------------------------------

        static readonly Color Sea = new Color(0.737f, 0.851f, 0.894f);      // #bcd9e4
        static readonly Color Grid = new Color(31f / 255f, 45f / 255f, 56f / 255f, 0.16f);
        const int OutlineSamples = 48;

        // Reused across every repaint: a chart redraws on a pointer move and
        // four times a second besides, and the whole reason the sheet HUD
        // replaced the IMGUI panels is that the old one allocated per frame.
        static readonly List<Vector2> poly = new List<Vector2>(OutlineSamples);
        static readonly List<float> cuts = new List<float>(16);

        void Paint(MeshGenerationContext mgc)
        {
            if (vw <= 1f || vh <= 1f) return;
            var p = mgc.painter2D;

            // 1. the sea, and its frame
            p.fillColor = Sea;
            Rectangle(p, 0f, 0f, vw, vh);
            p.Fill();
            p.strokeColor = SheetTheme.Ink;
            p.lineWidth = 1.5f;
            Rectangle(p, 0.75f, 0.75f, vw - 1.5f, vh - 1.5f);
            p.Stroke();

            // 2. a grid on round metres, so it means something
            float step = GridStep();
            p.strokeColor = Grid;
            p.lineWidth = 1f;
            p.BeginPath();
            for (float x = Mathf.Ceil(fitted.xMin / step) * step; x < fitted.xMax; x += step)
            {
                float px = P(new Vector2(x, 0f)).x;
                p.MoveTo(new Vector2(px, 0f)); p.LineTo(new Vector2(px, vh));
            }
            for (float z = Mathf.Ceil(fitted.yMin / step) * step; z < fitted.yMax; z += step)
            {
                float py = P(new Vector2(0f, z)).y;
                p.MoveTo(new Vector2(0f, py)); p.LineTo(new Vector2(vw, py));
            }
            p.Stroke();

            // 3. the land
            foreach (var isle in ChartData.Islands()) Land(p, isle);

            // 4. the wake
            if (showTrack)
            {
                var track = ChartData.Track();
                if (track.Count > 1)
                {
                    p.strokeColor = SheetTheme.Ink;
                    p.lineWidth = 1.6f;
                    p.BeginPath();
                    for (int i = 1; i < track.Count; i++)
                        Dash(p, P(track[i - 1]), P(track[i]), 2f, 5f);
                    p.Stroke();
                }
            }

            // 5. the raiders
            foreach (var r in ChartData.Raiders()) Raider(p, r);

            // 6. the ship
            Arrow(p, P(ChartData.ShipPos), ChartData.ShipHeadingDeg, 8f, SheetTheme.Ink);

            // 7. north, and how far a finger is
            p.strokeColor = SheetTheme.Ink;
            p.lineWidth = 1.5f;
            p.BeginPath();
            p.MoveTo(new Vector2(vw - 16f, 44f)); p.LineTo(new Vector2(vw - 16f, 22f));
            p.MoveTo(new Vector2(14f, vh - 16f)); p.LineTo(new Vector2(14f + ScaleBarPx(), vh - 16f));
            p.Stroke();
            p.fillColor = SheetTheme.Ink;
            p.BeginPath();
            p.MoveTo(new Vector2(vw - 16f, 20f));
            p.LineTo(new Vector2(vw - 20f, 28f));
            p.LineTo(new Vector2(vw - 12f, 28f));
            p.ClosePath();
            p.Fill();
        }

        void Land(Painter2D p, ChartIsland isle)
        {
            if (isle.island == null) return;
            var outline = ChartData.OutlineOf(isle.island);
            poly.Clear();
            for (int i = 0; i < OutlineSamples; i++)
            {
                float deg = i * 360f / OutlineSamples;
                float rad = deg * Mathf.Deg2Rad;
                float r = outline(deg);
                poly.Add(P(isle.centre + new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * r));
            }

            bool landed = isle.seen == Seen.Landed;
            var tint = isle.tint;

            p.fillColor = landed ? new Color(tint.r, tint.g, tint.b, 0.95f)
                                 : new Color(tint.r, tint.g, tint.b, 0.22f);
            p.BeginPath();
            p.MoveTo(poly[0]);
            for (int i = 1; i < poly.Count; i++) p.LineTo(poly[i]);
            p.ClosePath();
            p.Fill();

            if (!landed)
            {
                // **Hatched, not greyed.** A glimpse is a shape you saw and
                // nothing more; hatching says "not surveyed" the way a chart
                // has always said it, and it survives being four pixels wide
                // where a pale fill just disappears.
                p.strokeColor = new Color(31f / 255f, 45f / 255f, 56f / 255f, 0.45f);
                p.lineWidth = 1f;
                p.BeginPath();
                Hatch(p, poly, 6f);
                p.Stroke();
            }

            p.strokeColor = SheetTheme.Ink;
            p.lineWidth = isle.island == selected ? 2.4f : 1.4f;
            if (isle.island == selected) p.strokeColor = SheetTheme.Brass;
            p.BeginPath();
            if (landed)
            {
                p.MoveTo(poly[0]);
                for (int i = 1; i < poly.Count; i++) p.LineTo(poly[i]);
                p.ClosePath();
            }
            else
            {
                for (int i = 0; i < poly.Count; i++)
                    Dash(p, poly[i], poly[(i + 1) % poly.Count], 3f, 3f);
            }
            p.Stroke();
        }

        void Raider(Painter2D p, ChartRaider r)
        {
            // The patrol ring first and faintest: it is the water to stay out
            // of, and it must never cover the coast it is drawn over.
            p.strokeColor = new Color(0.95f, 0.30f, 0.25f, 0.60f);
            p.lineWidth = 1.4f;
            p.BeginPath();
            DashRing(p, P(r.patrolCentre), r.patrolRadius * scale, 4f, 4f);
            p.Stroke();

            if (r.hasTarget)
            {
                p.strokeColor = new Color(0.95f, 0.30f, 0.25f, 0.85f);
                p.lineWidth = 1.4f;
                p.BeginPath();
                Dash(p, P(r.pos), P(r.target), 3f, 4f);
                p.Stroke();
            }

            Arrow(p, P(r.pos), r.headingDeg, 6f, new Color(0.90f, 0.28f, 0.22f));
        }

        // --- painter helpers ---------------------------------------------------------------

        static void Rectangle(Painter2D p, float x, float y, float w, float h)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
        }

        /// A triangle pointing at a compass bearing. Bearing, not screen
        /// angle: 0 is up the page because up the page is north.
        static void Arrow(Painter2D p, Vector2 at, float bearingDeg, float size, Color col)
        {
            float a = bearingDeg * Mathf.Deg2Rad;
            Vector2 fwd = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
            Vector2 side = new Vector2(-fwd.y, fwd.x);
            p.fillColor = col;
            p.BeginPath();
            p.MoveTo(at + fwd * size);
            p.LineTo(at - fwd * size * 0.7f + side * size * 0.6f);
            p.LineTo(at - fwd * size * 0.25f);
            p.LineTo(at - fwd * size * 0.7f - side * size * 0.6f);
            p.ClosePath();
            p.Fill();
        }

        /// **Painter2D has no dash pattern**, so a dotted line is drawn as
        /// the dots. Appended to whatever path is already open, so a whole
        /// polyline is one `Stroke`.
        static void Dash(Painter2D p, Vector2 a, Vector2 b, float on, float off)
        {
            float len = Vector2.Distance(a, b);
            if (len < 0.01f) return;
            Vector2 d = (b - a) / len;
            // A pathological fit can hand this a segment kilometres long in
            // PIXELS; a dashed line of ten thousand dots is a frozen editor,
            // and at that length nobody can see the dashes anyway.
            if (len > 4000f) { p.MoveTo(a); p.LineTo(b); return; }
            float t = 0f;
            while (t < len)
            {
                float e = Mathf.Min(t + on, len);
                p.MoveTo(a + d * t);
                p.LineTo(a + d * e);
                t = e + off;
            }
        }

        static void DashRing(Painter2D p, Vector2 c, float r, float on, float off)
        {
            if (r < 2f) return;
            int steps = Mathf.Clamp(Mathf.RoundToInt(r * 0.8f), 24, 120);
            Vector2 prev = c + new Vector2(r, 0f);
            for (int i = 1; i <= steps; i++)
            {
                float a = i * Mathf.PI * 2f / steps;
                Vector2 next = c + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                Dash(p, prev, next, on, off);
                prev = next;
            }
        }

        /// Diagonal fill, clipped to the polygon by scanline rather than by a
        /// clip rect — Painter2D has no clipping, and a hatch that runs past
        /// an island's coast reads as a reef.
        static void Hatch(Painter2D p, List<Vector2> poly, float gap)
        {
            if (poly.Count < 3) return;
            Vector2 dir = new Vector2(0.7071f, 0.7071f);
            Vector2 nrm = new Vector2(-0.7071f, 0.7071f);

            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in poly)
            {
                float t = Vector2.Dot(v, nrm);
                lo = Mathf.Min(lo, t); hi = Mathf.Max(hi, t);
            }
            // A glimpsed island wider than the whole chart means the fit has
            // gone wrong; hatching it would be thousands of scanlines against
            // forty-eight edges each, per repaint.
            if (hi - lo > 1200f) return;

            for (float t = Mathf.Ceil(lo / gap) * gap; t <= hi; t += gap)
            {
                cuts.Clear();
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                    float da = Vector2.Dot(a, nrm) - t;
                    float db = Vector2.Dot(b, nrm) - t;
                    if ((da < 0f) == (db < 0f)) continue;
                    float u = da / (da - db);
                    cuts.Add(Vector2.Dot(Vector2.Lerp(a, b, u), dir));
                }
                if (cuts.Count < 2) continue;
                cuts.Sort();
                // Origin of the scanline: the point on it closest to zero
                // along `nrm`, so a distance along `dir` is a point again.
                Vector2 o = nrm * t;
                for (int i = 0; i + 1 < cuts.Count; i += 2)
                {
                    p.MoveTo(o + dir * cuts[i]);
                    p.LineTo(o + dir * cuts[i + 1]);
                }
            }
        }

        // --- scales and words ----------------------------------------------------------------

        /// A round number of metres that puts five or six lines across the
        /// chart — 100, 250, 500, 1000, and so on up.
        float GridStep()
        {
            float want = Mathf.Max(fitted.width, fitted.height) / 6f;
            float pow = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Mathf.Max(1f, want))));
            float n = want / pow;
            float mul = n < 1.5f ? 1f : n < 3.5f ? 2.5f : n < 7.5f ? 5f : 10f;
            return Mathf.Max(50f, pow * mul);
        }

        /// The bar is 500 m unless 500 m is longer than the chart, in which
        /// case it says what it actually is.
        float ScaleBarMetres() => fitted.width > 1400f ? 500f
                                : fitted.width > 700f ? 250f : 100f;

        float ScaleBarPx() => Mathf.Clamp(ScaleBarMetres() * scale, 24f, vw * 0.4f);

        string ScaleLabel() => ScaleBarMetres().ToString("0") + " m";

        static string Km(float metres) =>
            metres < 950f ? Mathf.RoundToInt(metres) + " m"
                          : (metres / 1000f).ToString("0.#") + " km";

        static readonly string[] Numbers =
            { "no", "one", "two", "three", "four", "five", "six",
              "seven", "eight", "nine", "ten", "eleven", "twelve" };

        /// A chart's title is a sentence, and a sentence counts in words.
        static string Words(int n) =>
            n >= 0 && n < Numbers.Length ? Numbers[n] : n.ToString();

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
