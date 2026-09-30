using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    // A session-local experiment: the old sheets and all gameplay verbs remain available.
    public sealed class MidnightLandHud
    {
        public static bool Enabled { get; set; } = true;
        public static bool Active => Enabled && Sheets.SuppressLegacy;
        /// **No bottom nav since 2026-09-27** (the ledger drawer holds its
        /// four destinations). Kept as a named 0 so every frame/reserve sum
        /// in `SheetHost` still reads the same.
        public const float NavHeight = 0f;
        /// The resource bar itself.
        public const float BarHeight = 46f;
        /// Everything the top of the land HUD reserves: the bar, a gap, and
        /// the alert strip under it. `SheetHost` and the chart instrument
        /// read this, so a tall sheet and the dial both start below the
        /// chips.
        public const float TopHeight = BarHeight + 6f + AlertStrip.Height;
        public static Rect NavigationRect { get; private set; }
        public static Rect ResourcesRect { get; private set; }
        public static Color Pearl => new Color32(232, 242, 246, 255);
        public static Color Ice => new Color32(164, 210, 232, 255);
        public static Color Muted => new Color32(166, 186, 198, 255);
        readonly VisualElement top;
        readonly Label mood, people, food, day;
        readonly HudGlyph moodFace, foodTrend;
        readonly CampStatusHud campStatus;
        readonly AlertStrip alerts;
        float nextUpdate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Enabled = true; NavigationRect = ResourcesRect = Rect.zero; }

        /// <summary>
        /// **The top bar, 2026-09-29** (Kevin, iPhone: *"a backpack emblem
        /// ... second, i want to have mood ... third ... village numbers
        /// ... n/n ... food ... with a up or down arrow ... the day counter
        /// is good"*). Left to right:
        /// <list type="bullet">
        /// <item>**Backpack** -- icon only; opens `BackpackSheet`: the camp
        ///   store above, the ship's hold below, and a +/- under every item
        ///   that places a carry order (goods move only when a hand walks
        ///   them -- deliveries on arrival).</item>
        /// <item>**Mood** -- the camp's average hand mood as "72%"
        ///   (`CampReadouts.Mood01`), a face that smiles or frowns with it;
        ///   ice under 50 %, ember under 35 %. Opens `MoodSheet`.</item>
        /// <item>**People** -- "working/total" (`CampReadouts.Working` /
        ///   `Total`). Opens `WorkersSheet`.</item>
        /// <item>**Food** -- sky days of stored food at the current rations
        ///   (`CampReadouts.FoodDays`), ember under 1 day, ice under 3, and
        ///   an up/down arrow off `CampReadouts.FoodTrendPerDay` (none when
        ///   it is flat). Opens `FoodSheet`.</item>
        /// <item>**Day N** -- plain text, not a tap.</item>
        /// </list>
        /// Timber and Boards left the bar with this change (the backpack
        /// shows every store). Every chip is a Button stretched to the bar's
        /// full inner height so it is a thumb target, not a strip of text.
        /// </summary>
        public MidnightLandHud(VisualElement root)
        {
            top = new VisualElement(); top.AddToClassList("land-resources"); root.Add(top);
            var bag = Chip(top, new HudGlyph(HudGlyph.Kind.Backpack), "Backpack: the camp store and the ship's hold",
                c => new BackpackSheet(c), false);
            bag.parent.AddToClassList("land-resource--icon");
            moodFace = new HudGlyph(HudGlyph.Kind.Mood);
            mood = Chip(top, moodFace, "Average mood of the camp's hands", c => new MoodSheet(c), true);
            people = Chip(top, new LandIcon("crew"), "Working hands / all hands", c => new WorkersSheet(c), true);
            food = Chip(top, new LandIcon("food"), "Days of stored food at current rations; the arrow is where it is heading",
                c => new FoodSheet(c), true);
            foodTrend = new HudGlyph(HudGlyph.Kind.Up);
            foodTrend.AddToClassList("land-trend");
            food.parent.Add(foodTrend);
            day = new Label(); day.AddToClassList("land-day"); top.Add(day);
            campStatus = new CampStatusHud(root);
            alerts = new AlertStrip(root);
            // **No GoalBar since 2026-09-30** (island UI phase 2): a pinned
            // goal is the Next card's "YOUR GOAL" above the thumb bar
            // (`NextCard`); the pin itself (`GoalPin`) is unchanged.
            // **No ☰ since 2026-09-30** (island UI restructure 1A): the top
            // bar is status only, and the Camp sheet opens from the thumb
            // bar's Camp button at the bottom (`ThumbBar`); the bottom
            // ledger drawer is gone since phase 4.
        }

        /// One tappable chip: icon, then (when `withLabel`) the number.
        /// The sheet is built from the camp at tap time; no camp, no sheet.
        static Label Chip(VisualElement parent, VisualElement icon, string hint,
                          System.Func<Outpost, ISheet> open, bool withLabel)
        {
            var btn = new Button(() =>
            {
                var camp = Camp;
                if (camp != null) Sheets.Open(open(camp));
            });
            btn.AddToClassList("land-resource");
            btn.AddToClassList("land-resource--tap");
            btn.tooltip = hint;
            btn.Add(icon);
            var label = new Label { pickingMode = PickingMode.Ignore };
            if (!withLabel) label.style.display = DisplayStyle.None;
            btn.Add(label); parent.Add(btn); return label;
        }

        public static Outpost Camp => Sheets.Anchor != null && Sheets.Anchor.CurrentIsland != null
            ? Outpost.Of(Sheets.Anchor.CurrentIsland) : null;

        public void Tick(VisualElement root)
        {
            bool active = Active;
            root.EnableInClassList("midnight-land", active);
            top.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            if (!active)
            {
                NavigationRect = ResourcesRect = Rect.zero;
                campStatus.Tick(Camp, false, SheetHost.PanelScale);
                alerts.Hide(); return;
            }
            float scale = SheetHost.PanelScale;
            var safe = Screen.safeArea;
            float left = safe.xMin * scale + 8f, right = (Screen.width - safe.xMax) * scale + 8f;
            float topY = (Screen.height - safe.yMax) * scale + 8f;
            top.style.left = left;
            top.style.right = right;
            top.style.top = topY;
            alerts.Place(left, right, topY + BarHeight + 6f);
            // The top chrome: the bar, plus the strip while it
            // has a chip up (an empty strip must not eat world taps).
            float chrome = BarHeight + (AlertStrip.Showing ? 6f + AlertStrip.Height : 0f);
            ResourcesRect = new Rect(safe.xMin + 8f / scale, Screen.height - safe.yMax + 8f / scale,
                safe.width - 16f / scale, chrome / scale);
            // No bottom nav: a zero-height line at the foot of the safe area,
            // so `SheetHost`'s `claimed.yMax` and the overlap tests still
            // read a sane rect.
            NavigationRect = new Rect(safe.xMin + 8f / scale, Screen.height - safe.yMin - 8f / scale,
                safe.width - 16f / scale, 0f);
            var statusRect = campStatus.Tick(Camp, true, scale);
            if (statusRect.height > 0f) NavigationRect = statusRect;
            // The IMGUI HUD keeps off the food notice, the thumb bar AND
            // the Next card above it.
            if (!SheetHost.FrameOpen)
            {
                var claim = Union(NavigationRect, ThumbBar.Rect);
                claim = Union(claim, NextCard.Rect);
                HudLayout.ClaimSheet(claim);
            }
            if (Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + .25f;
            alerts.Refresh(Camp);
            var ledger = Camp != null ? Camp.Ledger : null;
            int total = ledger != null ? CampReadouts.Total(ledger) : 0;

            float m = ledger != null ? CampReadouts.Mood01(ledger) : -1f;
            mood.text = m < 0f ? "--" : Mathf.RoundToInt(Mathf.Clamp01(m) * 100f) + "%";
            mood.style.color = m < 0f ? Pearl : m < .35f ? SheetTheme.Ember : m < .5f ? Ice : Pearl;
            moodFace.Set(m < 0f ? .5f : Mathf.Clamp01(m));

            people.text = ledger != null ? CampReadouts.Working(ledger) + "/" + total : "0/0";

            float days = ledger != null ? CampReadouts.FoodDays(ledger) : -1f;
            food.text = ledger == null || total == 0 ? "--"
                : days < 0f ? "Off" : days < .1f ? "<0.1d" : days > 99f ? "99+d" : days.ToString("0.#") + "d";
            food.style.color = ledger != null && total > 0 && days < 1f
                ? SheetTheme.Ember : days < 3f && days >= 0f ? Ice : Pearl;
            float trend = ledger != null && total > 0 ? CampReadouts.FoodTrendPerDay(ledger) : 0f;
            const float Flat = .05f;
            foodTrend.style.display = Mathf.Abs(trend) > Flat ? DisplayStyle.Flex : DisplayStyle.None;
            foodTrend.Set(trend > 0f ? 1f : 0f);
            foodTrend.Tint = trend > 0f ? SheetTheme.Moss : SheetTheme.Ember;
            food.parent.tooltip = SheetBits.FoodDaysLine(ledger)
                + (trend > Flat ? "; rising" : trend < -Flat ? "; falling" : "; steady") + " at the current rate";
            day.text = "Day " + TimeOfDay.Day;
        }

        static Rect Union(Rect a, Rect b) => b.height <= 0f ? a
            : Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        internal static string CompactCount(int count) => count < 1000 ? count.ToString()
            : count < 1000000 ? (count / 1000f).ToString("0.#") + "k" : (count / 1000000f).ToString("0.#") + "m";
    }

    /// **The top bar's own pictograms (2026-09-29)**: a backpack, a mood
    /// face whose mouth follows the camp's mood, and the food trend arrow.
    /// Painted like `LandIcon` (no font glyphs, no emoji -- Kevin), kept
    /// here because only this bar draws them. `Set` takes 0..1: the mood
    /// for the face; above/below 0.5 = up/down for the arrow.
    public sealed class HudGlyph : VisualElement
    {
        public enum Kind { Backpack, Mood, Up }

        readonly Kind kind;
        float level = .5f;
        Color tintColour = MidnightLandHud.Pearl;

        public HudGlyph(Kind kind)
        {
            this.kind = kind;
            AddToClassList("land-icon");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void Set(float v01)
        {
            // Quantised so a mood drifting by 0.1 % does not repaint 4x/s.
            float q = Mathf.Round(Mathf.Clamp01(v01) * 20f) / 20f;
            if (Mathf.Approximately(q, level)) return;
            level = q; MarkDirtyRepaint();
        }

        public Color Tint
        {
            get => tintColour;
            set { if (value == tintColour) return; tintColour = value; MarkDirtyRepaint(); }
        }

        void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            float s = Mathf.Min(contentRect.width, contentRect.height) / 32f;
            if (s <= 0f) return;
            Vector2 V(float x, float y) => new Vector2(x * s, y * s);
            void Poly(Color c, params Vector2[] pts)
            {
                p.fillColor = c; p.BeginPath(); p.MoveTo(pts[0] * s);
                for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i] * s);
                p.ClosePath(); p.Fill();
            }
            var pearl = MidnightLandHud.Pearl;
            var dark = new Color32(22, 42, 56, 255);
            switch (kind)
            {
                case Kind.Backpack:
                    // Carry loop, body, flap seam, front pocket.
                    p.strokeColor = pearl; p.lineWidth = 2.4f * s;
                    p.BeginPath(); p.Arc(V(16, 10), 5f * s, 180f, 360f); p.Stroke();
                    Poly(pearl, new Vector2(8, 9), new Vector2(24, 9), new Vector2(27, 13), new Vector2(27, 28),
                        new Vector2(25, 30), new Vector2(7, 30), new Vector2(5, 28), new Vector2(5, 13));
                    p.strokeColor = dark; p.lineWidth = 1.6f * s;
                    p.BeginPath(); p.MoveTo(V(5, 17)); p.LineTo(V(27, 17)); p.Stroke();
                    p.BeginPath(); p.MoveTo(V(10, 21)); p.LineTo(V(22, 21)); p.LineTo(V(22, 27));
                    p.LineTo(V(10, 27)); p.ClosePath(); p.Stroke();
                    Poly(dark, new Vector2(14.5f, 15), new Vector2(17.5f, 15), new Vector2(17.5f, 19.5f), new Vector2(14.5f, 19.5f));
                    break;
                case Kind.Mood:
                    p.fillColor = pearl; p.BeginPath(); p.Arc(V(16, 16), 13f * s, 0f, 360f); p.Fill();
                    p.fillColor = dark;
                    p.BeginPath(); p.Arc(V(11.5f, 12.5f), 2f * s, 0f, 360f); p.Fill();
                    p.BeginPath(); p.Arc(V(20.5f, 12.5f), 2f * s, 0f, 360f); p.Fill();
                    // Mouth: a smile at 1, flat at 0.5, a frown at 0.
                    float bend = (level - .5f) * 2f;
                    p.strokeColor = dark; p.lineWidth = 2.2f * s; p.lineCap = LineCap.Round;
                    p.BeginPath(); p.MoveTo(V(10, 20.5f - bend * 1.5f));
                    p.QuadraticCurveTo(V(16, 20.5f + bend * 6f), V(22, 20.5f - bend * 1.5f)); p.Stroke();
                    break;
                case Kind.Up:
                    if (level >= .5f) Poly(tintColour, new Vector2(16, 6), new Vector2(28, 25), new Vector2(4, 25));
                    else Poly(tintColour, new Vector2(4, 7), new Vector2(28, 7), new Vector2(16, 26));
                    break;
            }
        }
    }

    /// **The resource card (2026-09-27, audit #11).** A tap on a top-bar
    /// chip used to do nothing; this is what it opens instead: the stock,
    /// the trend when it is cheap to say (`SheetBits.RateLine`, already the
    /// number every ledger line uses), who makes it, and a straight line to
    /// the bank. Small, in the same Midnight header shape every other
    /// restyled sheet uses.
    sealed class ResourceCard : ISheetFramed
    {
        readonly Outpost camp;
        readonly string res;

        public ResourceCard(Outpost o, string resource) { camp = o; res = resource; }

        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public Color Accent => SheetBits.Colour(res);
        public string Title => StationPage.Cap(ResDefs.Label(res));
        public Vector3 AnchorWorld => camp != null ? camp.CampCentre : Vector3.zero;
        public bool StillValid => camp != null && camp.Ledger != null;

        Label sub;
        Label stockLine, trendLine, makerLine;
        Button storesBtn;

        public VisualElement BuildHeader()
        {
            var icon = StationPage.Icon(res, "cp-glyph");
            return CampPages.IconHeader(Title, icon, out sub);
        }

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            stockLine = SheetKit.Text("", true, false, 22f);
            root.Add(stockLine);
            trendLine = SheetKit.Text("", false, true, 13f);
            root.Add(trendLine);
            makerLine = SheetKit.Text("", false, true, 13f);
            root.Add(makerLine);
            Refresh();
            return root;
        }

        public VisualElement BuildActions()
        {
            storesBtn = SheetKit.Btn("Open stores", OpenStores, true);
            return SheetKit.Actions(storesBtn);
        }

        public void Refresh()
        {
            var l = camp != null ? camp.Ledger : null;
            if (l == null) return;
            int have = l.StoreCountOf(res);
            if (sub != null) sub.text = "in the camp store";
            if (stockLine != null) stockLine.text = $"{have} in store";
            if (trendLine != null)
            {
                string rate = SheetBits.RateLine(l, res);
                trendLine.text = rate.Length > 0 ? "trend " + rate : "";
                trendLine.style.display = rate.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (makerLine != null) makerLine.text = MakerLine();
            if (storesBtn != null) storesBtn.SetEnabled(camp != null);
        }

        /// "Made at the sawmill" off the recipe table, or "Gathered from
        /// the island" for a raw resource nothing crafts.
        string MakerLine()
        {
            var recipes = Recipes.Making(res);
            if (recipes.Count == 0) return "Gathered from the island";
            var seen = new System.Collections.Generic.HashSet<string>();
            var names = new System.Collections.Generic.List<string>();
            foreach (var r in recipes)
            {
                var plan = BuildPlans.Named(r.station);
                if (plan.label != null && seen.Add(plan.label)) names.Add(StationPage.Cap(plan.label));
            }
            return names.Count == 0 ? "" : "Made at the " + string.Join(" or the ", names);
        }

        void OpenStores()
        {
            var c = camp;
            Sheets.Close();
            if (c != null) Sheets.Open(new BackpackSheet(c));
        }
    }
}
