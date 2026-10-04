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

        WatchTiles.Head head;

        /// **Midnight card, 2026-09-27** (audit #10): compass glyph, "The
        /// chart", the day / islands / camps line as the subtitle. The body
        /// (`Build`) is the map in a rounded frame, the hint, a Track
        /// segmented control and a pick tile; the thumb row is Close · Set
        /// course (or Clear course).
        public VisualElement BuildHeader()
        {
            head = CardKit.Head("chart", "The chart");
            head.SetPill(null, StationPage.PillGood);
            headKey = long.MinValue;
            return head.Root;
        }

        public VisualElement BuildActions() => null;

        public bool WantsTallSheet => true;

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

        VisualElement trackOn, trackOff;
        CardKit.Tile pickTile;
        VisualElement colEl, hintEl, segEl;     // for sizing the map to the room left
        VisualElement campGrid;                 // one row per camp, under the pick tile
        readonly Dictionary<Island, CardKit.Tile> campTiles = new Dictionary<Island, CardKit.Tile>();
        long campKey = long.MinValue;

        public VisualElement Build()
        {
            // **Build can run again on the same instance** (SheetHost rebuilds
            // the open sheet when the window changes shape). Everything that
            // remembers "I already made that" belongs to the OLD element tree,
            // so every incremental-update key and element reference is reset
            // here: a kept `campKey` made CampRows skip creating the rows
            // (an empty camp list after a shape switch), a kept `wordsKey`
            // would have skipped the labels on the new chart.
            campKey = long.MinValue;
            wordsKey = long.MinValue;
            layoutKey = long.MinValue;
            headKey = long.MinValue;
            layoutDirty = true;
            campTiles.Clear();
            laid.Clear();
            fitted = default;
            vw = vh = 0f;
            campGrid = null; pickTile = null; courseBtn = null;
            trackOn = trackOff = null;
            colEl = hintEl = segEl = null;

            var root = CardKit.Page(out var col);
            colEl = col;
            col.RegisterCallback<GeometryChangedEvent>(_ => AdaptMapHeight());

            chart = new VisualElement();
            chart.AddToClassList("ck-map");
            chart.style.width = Length.Percent(100f);
            chart.style.height = 220f;
            chart.style.overflow = Overflow.Hidden;
            chart.pickingMode = PickingMode.Position;
            chart.generateVisualContent += Paint;
            chart.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            chart.RegisterCallback<PointerDownEvent>(OnPointer);
            col.Add(chart);

            words = new VisualElement();
            words.style.position = Position.Absolute;
            words.style.left = 0; words.style.top = 0;
            words.style.right = 0; words.style.bottom = 0;
            words.pickingMode = PickingMode.Ignore;
            chart.Add(words);

            var hint = StationPage.Text("Tap an island for its name. Tap a flame, then Set course.", "ck-note");
            col.Add(hint);
            hintEl = hint;

            // Track: last day / off, Station.uss's segmented control.
            var seg = WatchTiles.Box("st-seg");
            seg.AddToClassList("ck-seg");
            seg.pickingMode = PickingMode.Position;
            var on = new Button(() => SetTrack(true)) { text = "Track: last day" };
            on.AddToClassList("st-seg-btn");
            on.AddToClassList("st-seg-btn--first");
            var off = new Button(() => SetTrack(false)) { text = "Track off" };
            off.AddToClassList("st-seg-btn");
            seg.Add(on);
            seg.Add(off);
            trackOn = on; trackOff = off;
            col.Add(seg);
            segEl = seg;
            SetTrack(showTrack);

            // The list under the map: what "Set course" would do (a tap
            // drops the pick), then one row per camp with its ledger. It
            // scrolls, so a short desk card keeps the map and the thumb row.
            var list = new ScrollView(ScrollViewMode.Vertical);
            list.style.flexGrow = 1f;
            list.style.flexShrink = 1f;
            list.style.minHeight = 0f;
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            list.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            list.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            col.Add(list);

            var grid = CardKit.Grid(list);
            grid.style.marginTop = 8f;
            pickTile = new CardKit.Tile(_ => { selected = null; wordsKey = long.MinValue; Refresh(); }, false);
            pickTile.Root.AddToClassList("ck-tile--wide");
            var compass = new StationPage.Glyph("chart", MidnightLandHud.Ice, "ck-glyph-fill");
            compass.style.width = Length.Percent(100f);
            compass.style.height = Length.Percent(100f);
            pickTile.Ico.Add(compass);
            grid.Add(pickTile.Root);

            campGrid = CardKit.Grid(list);

            ApplyShape(HudLayout.Wide);

            var acts = CardKit.Acts(root);
            CardKit.Act(acts, "Close", () => Sheets.Close());
            courseBtn = CardKit.Act(acts, "Set course", OnCourse, 1);

            Refresh();
            return root;
        }

        void SetTrack(bool show)
        {
            showTrack = show;
            trackOn?.EnableInClassList("st-seg-btn--on", show);
            trackOff?.EnableInClassList("st-seg-btn--on", !show);
            chart?.MarkDirtyRepaint();
        }

        void OnGeometry(GeometryChangedEvent evt)
        {
            vw = evt.newRect.width;
            AdaptMapHeight();
            vh = evt.newRect.height;
            fitted = default;         // the pixels moved; refit unconditionally
            Fit();
            layoutDirty = true;
            PlaceWords(true);
            chart.MarkDirtyRepaint();
        }

        /// A chart is a wide thing. On the docked phone card the body is
        /// wider than it is allowed to be tall, and on the desk column it
        /// is 400 px — both want the same ratio and a floor under it.
        ///
        /// **Desk (2026-10-04, round 2).** The card is short and the column
        /// is third-of-the-screen wide, so the old 260 px cap left a map of
        /// ~220 px and a list of ~90 px. On the wide shape the hint line is
        /// dropped (the pick tile already says "Tap a flame on the chart"),
        /// the Track toggle shrinks to a 44 pt row, and the map takes what is
        /// left after two full list rows, never under `DeskMapMin`. The
        /// phone keeps its own numbers.
        void AdaptMapHeight()
        {
            if (chart == null || vw <= 1f) return;
            bool wide = HudLayout.Wide;
            ApplyShape(wide);
            float colH = colEl != null ? colEl.layout.height : 0f;
            float want;
            if (wide)
            {
                want = DeskMapMin;
                if (colH > 1f)
                {
                    float segH = segEl != null && segEl.layout.height > 1f ? segEl.layout.height + 8f : DeskSegPx + 18f;
                    float tileH = pickTile != null && pickTile.Root.layout.height > 1f ? pickTile.Root.layout.height + 6f : 80f;
                    float room = colH - 8f - segH - (8f + tileH * 2f);
                    want = Mathf.Clamp(room, DeskMapMin, 360f);
                }
            }
            else
            {
                want = Mathf.Clamp(vw / 1.5f, 170f, 260f);
                if (colH > 1f)
                {
                    float hintH = hintEl != null && hintEl.layout.height > 1f ? hintEl.layout.height + 6f : 46f;
                    float segH = segEl != null && segEl.layout.height > 1f ? segEl.layout.height + 8f : 72f;
                    float room = colH - 8f - hintH - segH - 120f;
                    want = Mathf.Min(want, Mathf.Max(150f, room));
                }
            }
            if (!Mathf.Approximately(chart.style.height.value.value, want))
                chart.style.height = want;
        }

        const float DeskMapMin = 300f;
        const float DeskSegPx = 44f;

        /// The pieces that differ between the phone and the desk shape.
        /// Idempotent, so it is safe to call on every geometry change.
        void ApplyShape(bool wide)
        {
            if (hintEl != null)
                hintEl.style.display = wide ? DisplayStyle.None : DisplayStyle.Flex;
            if (segEl != null)
            {
                float pad = wide ? 3f : 5f;
                segEl.style.paddingTop = segEl.style.paddingBottom = pad;
                segEl.style.paddingLeft = segEl.style.paddingRight = pad;
                StyleLength h = wide ? new StyleLength(DeskSegPx) : new StyleLength(StyleKeyword.Null);
                if (trackOn != null) trackOn.style.height = h;
                if (trackOff != null) trackOff.style.height = h;
            }
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
                if (head != null)
                    head.SetSub("Day " + TimeOfDay.Day + " · " + Cap(Words(seenCount))
                        + (seenCount == 1 ? " island seen, " : " islands seen, ")
                        + Words(camps) + (camps == 1 ? " camp" : " camps"));
            }

            Fit();
            PlaceWords(false);
            CampRows(isles);
            Footer(isles);
            chart.MarkDirtyRepaint();
        }

        /// **One row per camp, under the map.** The ledger line used to be
        /// printed on the map, where it ran across rings, flames and other
        /// islands; here it has the width of the card and wraps. Rows are
        /// kept and updated in place (a rebuilt row loses a press), and are
        /// only rebuilt when the set of camps changes. A row is a 44 pt+
        /// button that picks the camp, the same as tapping its flame.
        void CampRows(IReadOnlyList<ChartIsland> isles)
        {
            if (campGrid == null) return;
            long k = 17;
            foreach (var i in isles)
                if (i.outpost != null && i.island != null) k = k * 31 + i.island.GetInstanceID();
            if (k != campKey)
            {
                campKey = k;
                campGrid.Clear();
                campTiles.Clear();
                for (int pass = 0; pass < 2; pass++)       // home first
                    foreach (var i in isles)
                    {
                        if (i.outpost == null || i.island == null) continue;
                        if ((pass == 0) != i.island.IsHome) continue;
                        var pick = i.island;
                        var tile = new CardKit.Tile(_ =>
                        {
                            selected = pick;
                            wordsKey = long.MinValue;
                            Refresh();
                        }, false);
                        tile.Root.AddToClassList("ck-tile--wide");
                        var fire = new StationPage.Glyph("fire", new Color32(242, 196, 109, 255), "sheet-glyph");
                        fire.style.width = Length.Percent(100f);
                        fire.style.height = Length.Percent(100f);
                        tile.Ico.Add(fire);
                        campGrid.Add(tile.Root);
                        campTiles[pick] = tile;
                    }
            }
            foreach (var i in isles)
            {
                if (i.island == null || !campTiles.TryGetValue(i.island, out var tile)) continue;
                string name = string.IsNullOrEmpty(i.name) ? ChartData.PlaceName(i.island) : i.name;
                tile.Set(name, i.ledgerLine);
                tile.State(i.island == selected);
                tile.Root.EnableInClassList("ck-tile--short", i.flame != FlameState.Fed);
                tile.Root.EnableInClassList("ck-tile--ok", i.flame == FlameState.Fed);
                tile.Ico.style.opacity = i.flame == FlameState.Hungry ? 0.45f : 1f;
            }
        }

        void Footer(IReadOnlyList<ChartIsland> isles)
        {
            // The picked button is a readout, not an action: it says what
            // "Set course" would do. With nothing picked it offers the same
            // answer `ChartData` gives everything else — the nearest pier.
            string label = null, sub = null;
            bool canSet = false;
            if (selected != null)
            {
                foreach (var i in isles)
                {
                    if (i.island != selected) continue;
                    label = i.seen == Seen.Landed ? ChartData.PlaceName(i.island) : "unseen";
                    sub = Km(Vector2.Distance(ChartData.ShipPos, i.centre))
                        + (i.outpost != null ? " · tap to drop" : " · no camp · tap to drop");
                    canSet = i.outpost != null;
                    break;
                }
            }
            else if (ChartData.TryCourse(out _, out string where, out float d))
            {
                label = where;
                sub = Km(d) + " · the course";
            }
            if (pickTile != null)
            {
                pickTile.Set(label ?? "Nothing picked", sub ?? "Tap a flame on the chart");
                pickTile.State(selected != null);
            }

            if (courseBtn == null) return;
            // **One button, and it never says two things at once.** With a
            // camp picked it sets the course; with nothing picked and a
            // course already set it is the way to drop it; otherwise it is
            // simply not a decision that can be made yet.
            string t = canSet ? "Set course" : ChartData.HasCourse ? "Clear course" : "Set course";
            if (courseBtn.text != t) courseBtn.text = t;
            courseBtn.SetEnabled(canSet || ChartData.HasCourse);
            CardKit.Primary(courseBtn, canSet);
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
            layoutDirty = true;
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

        /// Island names and flames. Rebuilt only when the set of things to
        /// say has changed (or the fit moved), because a rebuilt label is a
        /// label that loses the press happening on it — the same rule every
        /// other sheet in this folder keeps.
        ///
        /// **What is on the map (2026-10-04):** a name beside each landed
        /// island and a flame on each camp, nothing else. The camp ledger
        /// lives in the rows under the map (`CampRows`); "unseen" is not
        /// printed (a tap on an unseen island says it in the pick tile).
        ///
        /// **Stable between picks (round 2).** Where the names go is decided
        /// from the islands and the fit alone (`layoutKey`, which leaves the
        /// pick out), and kept in `laid`. A pick only rebuilds the elements
        /// (the flame's highlight) from the kept placements, so a tap never
        /// makes a name jump.
        void PlaceWords(bool force)
        {
            if (words == null || vw <= 1f) return;
            var isles = ChartData.Islands();

            long lk = isles.Count;
            long key = isles.Count;
            foreach (var i in isles)
            {
                long part = i.island != null ? i.island.GetInstanceID() : 0;
                part = part * 31 + (int)i.seen;
                part = part * 31 + (i.outpost != null ? 1 : 0);
                lk = lk * 31 + part;
                key = key * 31 + part;
                key = key * 31 + (int)i.flame;
                key = key * 31 + (i.island == selected ? 7 : 0);
            }
            if (!force && key == wordsKey) return;
            wordsKey = key;

            if (layoutDirty || lk != layoutKey) { LayOutNames(isles); layoutKey = lk; layoutDirty = false; }

            words.Clear();
            Word("N", new Vector2(vw - 16f, 6f), SheetTheme.Ink, 11f, true);
            Word(ScaleLabel(), new Vector2(14f + ScaleBarPx() * 0.5f, vh - 26f), SheetTheme.Ink, 9f, true);
            foreach (var l in laid) Word(l.text, l.at, SheetTheme.Ink, NameSize, true);
            foreach (var isle in isles)
            {
                if (isle.outpost == null) continue;
                var camp = isle.outpost.CampCentre;
                words.Add(Flame(isle, P(new Vector2(camp.x, camp.z))));
            }
        }

        struct NameWord { public string text; public int shape; public bool camp; }
        struct Placed { public string text; public Vector2 at; }
        struct Ring { public Vector2 c; public float r; }

        const float NameSize = 11f;
        /// A name stays this far inside the map's frame, px.
        const float FrameMargin = 6f;
        /// The gap kept between a name and anything else, px.
        const float NameGap = 2f;

        readonly List<NameWord> names = new List<NameWord>();
        readonly List<Rect> shapes = new List<Rect>();                 // every island's projected bounds
        readonly List<List<Vector2>> polys = new List<List<Vector2>>();  // and its projected outline
        readonly List<Rect> taken = new List<Rect>();                  // labels and marks already placed
        readonly List<Ring> rings = new List<Ring>();                  // patrol rings: only the stroke is solid
        readonly List<Placed> laid = new List<Placed>();               // the kept answer
        long layoutKey = long.MinValue;
        bool layoutDirty = true;

        void Take(Vector2 centre, float w, float h) =>
            taken.Add(new Rect(centre.x - w * 0.5f, centre.y - h * 0.5f, w, h));

        /// Decide where every name goes. Pure in its inputs (the islands, the
        /// fit, the ship and raiders as they are now), so the same inputs give
        /// the same placement.
        void LayOutNames(IReadOnlyList<ChartIsland> isles)
        {
            taken.Clear();
            shapes.Clear();
            polys.Clear();
            names.Clear();
            rings.Clear();
            laid.Clear();

            // The marks that belong to the frame rather than to the sea.
            // Fixed: they are placed first and every name steers round them.
            Take(new Vector2(vw - 16f, 6f), 14f, 16f);
            Take(new Vector2(vw - 16f, 33f), 14f, 24f);                 // the arrow
            var scalePos = new Vector2(14f + ScaleBarPx() * 0.5f, vh - 26f);
            Take(scalePos, ScaleLabel().Length * 9f * 0.7f + 8f, 9f * 1.4f);
            Take(new Vector2(14f + ScaleBarPx() * 0.5f, vh - 16f), ScaleBarPx() + 6f, 8f);   // the bar
            Take(P(ChartData.ShipPos), 18f, 18f);
            foreach (var r in ChartData.Raiders())
            {
                Take(P(r.pos), 14f, 14f);
                float rr = r.patrolRadius * scale;
                if (rr >= 2f) rings.Add(new Ring { c = P(r.patrolCentre), r = rr });
            }

            foreach (var isle in isles)
            {
                var poly = OutlinePoly(isle);
                polys.Add(poly);
                shapes.Add(BoundsOf(poly));
                if (isle.seen == Seen.Landed && !string.IsNullOrEmpty(isle.name))
                    names.Add(new NameWord { text = isle.name, shape = shapes.Count - 1, camp = isle.outpost != null });

                if (isle.outpost == null) continue;
                var camp = isle.outpost.CampCentre;
                Take(P(new Vector2(camp.x, camp.z)), 24f, 24f);
            }

            // Camps first, then the other named islands; scene order inside each.
            foreach (var n in names) if (n.camp) PlaceName(n);
            foreach (var n in names) if (!n.camp) PlaceName(n);
        }

        /// An island's outline, projected: 48 points, the same ring `Land`
        /// draws. An island with no shape (a glimpse with no `Island`) is
        /// its mean-radius box.
        List<Vector2> OutlinePoly(ChartIsland isle)
        {
            var list = new List<Vector2>(OutlineSamples);
            if (isle.island != null)
            {
                var outline = ChartData.OutlineOf(isle.island);
                for (int i = 0; i < OutlineSamples; i++)
                {
                    float deg = i * 360f / OutlineSamples;
                    float rad = deg * Mathf.Deg2Rad;
                    list.Add(P(isle.centre + new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * outline(deg)));
                }
            }
            else
            {
                float r = Mathf.Max(2f, isle.meanRadius * scale);
                Vector2 c = P(isle.centre);
                list.Add(c + new Vector2(-r, -r)); list.Add(c + new Vector2(r, -r));
                list.Add(c + new Vector2(r, r)); list.Add(c + new Vector2(-r, r));
            }
            return list;
        }

        static Rect BoundsOf(List<Vector2> poly)
        {
            Vector2 lo = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 hi = new Vector2(float.MinValue, float.MinValue);
            foreach (var q in poly) Grow(ref lo, ref hi, q);
            const float pad = 2f;
            return new Rect(lo.x - pad, lo.y - pad, Mathf.Max(4f, hi.x - lo.x + 2f * pad), Mathf.Max(4f, hi.y - lo.y + 2f * pad));
        }

        static float Overlap(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        static Rect Inflate(Rect r, float g) => new Rect(r.x - g, r.y - g, r.width + 2f * g, r.height + 2f * g);

        // --- rect vs outline / ring, for the hard rules ---------------------------

        /// True when the rect and the polygon share any point: a corner of
        /// one inside the other, or an edge crossing the rect.
        static bool RectHitsPoly(Rect r, List<Vector2> poly)
        {
            for (int i = 0; i < poly.Count; i++)
                if (r.Contains(poly[i])) return true;
            for (int i = 0; i < poly.Count; i++)
                if (SegHitsRect(poly[i], poly[(i + 1) % poly.Count], r)) return true;
            return PointInPoly(r.center, poly);          // the rect sits wholly inside the land
        }

        static bool PointInPoly(Vector2 p, List<Vector2> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        /// Liang-Barsky: does the segment touch the rect?
        static bool SegHitsRect(Vector2 a, Vector2 b, Rect r)
        {
            float t0 = 0f, t1 = 1f;
            float dx = b.x - a.x, dy = b.y - a.y;
            for (int k = 0; k < 4; k++)
            {
                float p = k == 0 ? -dx : k == 1 ? dx : k == 2 ? -dy : dy;
                float q = k == 0 ? a.x - r.xMin : k == 1 ? r.xMax - a.x : k == 2 ? a.y - r.yMin : r.yMax - a.y;
                if (Mathf.Abs(p) < 1e-6f) { if (q < 0f) return false; continue; }
                float t = q / p;
                if (p < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
                else { if (t < t0) return false; if (t < t1) t1 = t; }
            }
            return true;
        }

        /// A patrol ring is a dashed LINE: a name may sit wholly inside it or
        /// wholly outside it, but not on the stroke.
        static bool RectTouchesRing(Rect r, Ring ring)
        {
            float nx = Mathf.Max(r.xMin - ring.c.x, 0f, ring.c.x - r.xMax);
            float ny = Mathf.Max(r.yMin - ring.c.y, 0f, ring.c.y - r.yMax);
            float fx = Mathf.Max(Mathf.Abs(ring.c.x - r.xMin), Mathf.Abs(ring.c.x - r.xMax));
            float fy = Mathf.Max(Mathf.Abs(ring.c.y - r.yMin), Mathf.Abs(ring.c.y - r.yMax));
            const float tol = 3f;
            return Mathf.Sqrt(nx * nx + ny * ny) <= ring.r + tol && Mathf.Sqrt(fx * fx + fy * fy) >= ring.r - tol;
        }

        /// The hard rules: inside the frame, off every island's outline (its
        /// own too), off every mark and earlier name, off every ring stroke.
        bool Clean(Rect r)
        {
            if (r.xMin < FrameMargin || r.yMin < FrameMargin || r.xMax > vw - FrameMargin || r.yMax > vh - FrameMargin)
                return false;
            var g = Inflate(r, NameGap);
            foreach (var t in taken)
                if (Overlap(g, t) > 0f) return false;
            for (int s = 0; s < shapes.Count; s++)
            {
                if (Overlap(g, shapes[s]) <= 0f) continue;
                if (RectHitsPoly(g, polys[s])) return false;
            }
            foreach (var ring in rings)
                if (RectTouchesRing(g, ring)) return false;
            return true;
        }

        /// **A name goes beside its island, never on anything.** Candidate
        /// spots round the shape's bounds (right, left, above, below, the
        /// corners and the aligned-above/below ones), at five distances. A
        /// spot that breaks a hard rule (`Clean`) is out; of the clean ones
        /// the nearest and most usual (right first) wins. If none is clean,
        /// a scan of the chart round the island takes the clean spot nearest
        /// to it. Only when the sea is truly full does it settle for the
        /// cheapest overlap, still inside the frame. A name is never
        /// shortened and never dropped.
        void PlaceName(NameWord n)
        {
            // The label's real width is not known before layout; the estimate
            // is the Nunito-Black average, padded.
            float tw = n.text.Length * NameSize * 0.66f + 8f;
            float th = NameSize * 1.4f;
            Rect own = shapes[n.shape];
            float cx = own.center.x, cy = own.center.y;
            float[] ring = { 2f, 8f, 16f, 28f, 44f };
            float loX = FrameMargin, hiX = Mathf.Max(loX, vw - tw - FrameMargin);
            float loY = FrameMargin, hiY = Mathf.Max(loY, vh - th - FrameMargin);

            float bestClean = float.MaxValue, bestSoft = float.MaxValue;
            Rect clean = default, soft = default;
            bool haveClean = false;
            for (int ri = 0; ri < ring.Length; ri++)
            {
                float g = ring[ri];
                for (int d = 0; d < 12; d++)
                {
                    float x, y;
                    switch (d)
                    {
                        case 0: x = own.xMax + g; y = cy - th * 0.5f; break;                  // right
                        case 1: x = own.xMin - g - tw; y = cy - th * 0.5f; break;             // left
                        case 2: x = cx - tw * 0.5f; y = own.yMin - g - th; break;             // above
                        case 3: x = cx - tw * 0.5f; y = own.yMax + g; break;                  // below
                        case 4: x = own.xMax + g; y = own.yMin - g - th; break;               // above right
                        case 5: x = own.xMin - g - tw; y = own.yMin - g - th; break;          // above left
                        case 6: x = own.xMax + g; y = own.yMax + g; break;                    // below right
                        case 7: x = own.xMin - g - tw; y = own.yMax + g; break;              // below left
                        case 8: x = own.xMin; y = own.yMin - g - th; break;                   // above, left-aligned
                        case 9: x = own.xMax - tw; y = own.yMin - g - th; break;              // above, right-aligned
                        case 10: x = own.xMin; y = own.yMax + g; break;                       // below, left-aligned
                        default: x = own.xMax - tw; y = own.yMax + g; break;                  // below, right-aligned
                    }
                    x = Mathf.Clamp(x, loX, hiX);
                    y = Mathf.Clamp(y, loY, hiY);
                    var r = new Rect(x, y, tw, th);
                    float order = ri * 8f + d * 1.5f;
                    if (Clean(r))
                    {
                        if (order < bestClean) { bestClean = order; clean = r; haveClean = true; }
                        continue;
                    }
                    float cost = order + SoftCost(r, n.shape);
                    if (cost < bestSoft) { bestSoft = cost; soft = r; }
                }
            }

            Rect chosen;
            if (haveClean) chosen = clean;
            else
            {
                // Nothing clean beside the island: scan the chart round it for
                // the clean spot nearest to it, 3 px at a time.
                float reach = 120f;
                float bestD = float.MaxValue;
                bool found = false;
                Rect scan = default;
                float y0 = Mathf.Max(loY, cy - reach), y1 = Mathf.Min(hiY, cy + reach);
                float x0 = Mathf.Max(loX, cx - reach - tw), x1 = Mathf.Min(hiX, cx + reach);
                for (float y = y0; y <= y1; y += 3f)
                    for (float x = x0; x <= x1; x += 3f)
                    {
                        var r = new Rect(x, y, tw, th);
                        float dd = (r.center - own.center).sqrMagnitude;
                        if (dd >= bestD) continue;
                        if (!Clean(r)) continue;
                        bestD = dd; scan = r; found = true;
                    }
                chosen = found ? scan
                    : bestSoft < float.MaxValue ? soft
                    : new Rect(Mathf.Clamp(cx - tw * 0.5f, loX, hiX), Mathf.Clamp(own.yMin - th - 2f, loY, hiY), tw, th);
            }
            taken.Add(chosen);
            laid.Add(new Placed { text = n.text, at = chosen.center });
        }

        /// What a spot that breaks a hard rule costs, for the last resort:
        /// overlap with marks and names counts most, the island's own box and
        /// other islands' boxes less, leaving the frame most of all.
        float SoftCost(Rect r, int ownShape)
        {
            float cost = 0f;
            foreach (var t in taken) cost += Overlap(r, t) * 50f;
            for (int s = 0; s < shapes.Count; s++)
                cost += Overlap(r, shapes[s]) * (s == ownShape ? 6f : 2f);
            float outside = r.width * r.height
                - Overlap(r, new Rect(FrameMargin, FrameMargin, vw - 2f * FrameMargin, vh - 2f * FrameMargin));
            return cost + outside * 80f;
        }

        /// A label centred on a point in the drawing. Absolute, inert, and
        /// clipped by the chart's own overflow so a name on an island at the
        /// edge cannot run out over the parchment.
        void Word(string text, Vector2 at, Color col, float size, bool bold)
        {
            var l = new Label(text ?? "");
            l.style.position = Position.Absolute;
            // Wide enough for the whole text (a long ledger line is never wrapped or cut).
            float w = Mathf.Max(220f, (text ?? "").Length * size * 0.7f);
            float h = size * 1.4f;
            l.style.left = at.x - w * 0.5f;
            l.style.top = at.y - h * 0.5f;
            l.style.width = w;
            l.style.height = h;
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

            // A drawn flame (2026-10-04): the emoji drew as tofu on the phone.
            var g = new StationPage.Glyph("fire", new Color32(242, 196, 109, 255), "sheet-glyph");
            g.style.width = 18f;
            g.style.height = 18f;
            // Hungry is the same flame, burnt down: dim it rather than
            // recolour it, so the three states read as one mark in three
            // conditions instead of three different marks.
            g.style.opacity = isle.flame == FlameState.Hungry ? 0.45f : 1f;
            box.Add(g);

            if (isle.flame == FlameState.Raided)
            {
                var x = new Label("×");
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
