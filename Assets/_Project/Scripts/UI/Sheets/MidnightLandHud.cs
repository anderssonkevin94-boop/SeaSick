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
        const float MenuWidth = 46f;
        public static Rect NavigationRect { get; private set; }
        public static Rect ResourcesRect { get; private set; }
        public static Color Pearl => new Color32(232, 242, 246, 255);
        public static Color Ice => new Color32(164, 210, 232, 255);
        public static Color Muted => new Color32(166, 186, 198, 255);
        readonly VisualElement top;
        readonly Button menu;
        readonly Label logs, boards, crew, food, day;
        readonly BuildingStatusLabels buildingStatus;
        readonly AlertStrip alerts;
        readonly GoalBar goalBar;
        readonly LedgerDrawer ledgerDrawer;
        float nextUpdate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Enabled = true; NavigationRect = ResourcesRect = Rect.zero; }

        public MidnightLandHud(VisualElement root)
        {
            top = new VisualElement(); top.AddToClassList("land-resources"); root.Add(top);
            // **A resource chip is now a tap, not just a number (2026-09-27,
            // audit #11).** Timber and boards are real stock the player can
            // ask about -- stock, trend, who makes it, and a straight line
            // to the bank. Crew and food chips stay plain: they are not
            // store resources and PeopleSheet/FireSheet stay untouched here.
            logs = Resource(top, "logs", "Timber in store", Res.Timber);
            boards = Resource(top, "planks", "Boards in store", Res.Boards);
            crew = Resource(top, "crew", "Crew ashore", null);
            food = Resource(top, "food", "Stored food at current rations; excludes future gathering", null);
            day = new Label(); day.AddToClassList("land-day"); top.Add(day);
            buildingStatus = new BuildingStatusLabels(root);
            // **The ☰ replaces the bottom nav, 2026-09-27** (Melvor
            // redesign): Build/Crew/Stores/Ship live in the ledger drawer.
            // Added last, so the drawer and its scrim draw over everything
            // else in this document.
            alerts = new AlertStrip(root);
            goalBar = new GoalBar(root);
            menu = new Button(() => ledgerDrawer.Toggle());
            menu.AddToClassList("ledger-menu");
            menu.tooltip = "The ledger: camp, gather, make, sea";
            menu.Add(new LedgerDrawer.Glyph(LedgerDrawer.Glyph.Kind.Menu));
            root.Add(menu);
            ledgerDrawer = new LedgerDrawer(root);
        }

        static Label Resource(VisualElement parent, string icon, string hint, string res)
        {
            VisualElement group;
            if (res != null)
            {
                var btn = new Button(() =>
                {
                    var camp = Camp;
                    if (camp != null) Sheets.Open(new ResourceCard(camp, res));
                });
                btn.AddToClassList("land-resource");
                btn.AddToClassList("land-resource--tap");
                group = btn;
            }
            else
            {
                group = new VisualElement { pickingMode = PickingMode.Ignore };
                group.AddToClassList("land-resource");
            }
            group.tooltip = hint;
            group.Add(new LandIcon(icon));
            var label = new Label { pickingMode = PickingMode.Ignore };
            group.Add(label); parent.Add(group); return label;
        }

        public static Outpost Camp => Sheets.Anchor != null && Sheets.Anchor.CurrentIsland != null
            ? Outpost.Of(Sheets.Anchor.CurrentIsland) : null;

        public void Tick(VisualElement root)
        {
            bool active = Active;
            root.EnableInClassList("midnight-land", active);
            top.style.display = menu.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            ledgerDrawer.Tick(active, root, alerts.LastCount);
            if (!active)
            {
                NavigationRect = ResourcesRect = Rect.zero;
                buildingStatus.Hide(); alerts.Hide(); goalBar.Hide(); return;
            }
            float scale = SheetHost.PanelScale;
            var safe = Screen.safeArea;
            float left = safe.xMin * scale + 8f, right = (Screen.width - safe.xMax) * scale + 8f;
            float topY = (Screen.height - safe.yMax) * scale + 8f;
            menu.style.left = left;
            menu.style.top = topY;
            top.style.left = left + MenuWidth + 6f;
            top.style.right = right;
            top.style.top = topY;
            alerts.Place(left, right, topY + BarHeight + 6f);
            // The top chrome: the ☰ and the bar, plus the strip while it
            // has a chip up (an empty strip must not eat world taps).
            float chrome = BarHeight + (AlertStrip.Showing ? 6f + AlertStrip.Height : 0f);
            // The pinned goal's slim bar (GoalBar): under the strip, or in
            // its slot when the strip is empty.
            float goalH = goalBar.Place(Camp, left, right, topY + BarHeight + 6f);
            if (goalH > 0f) chrome += AlertStrip.Showing ? goalH : 6f + goalH;
            ResourcesRect = new Rect(safe.xMin + 8f / scale, Screen.height - safe.yMax + 8f / scale,
                safe.width - 16f / scale, chrome / scale);
            // No bottom nav: a zero-height line at the foot of the safe area,
            // so `SheetHost`'s `claimed.yMax` and the overlap tests still
            // read a sane rect.
            NavigationRect = new Rect(safe.xMin + 8f / scale, Screen.height - safe.yMin - 8f / scale,
                safe.width - 16f / scale, 0f);
            if (!SheetHost.FrameOpen) HudLayout.ClaimSheet(NavigationRect);
            buildingStatus.Tick(Camp, scale);
            if (Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + .25f;
            alerts.Refresh(Camp);
            var ledger = Camp != null ? Camp.Ledger : null;
            logs.text = CompactCount(ledger != null ? ledger.StoreCountOf(Res.Timber) : 0);
            boards.text = CompactCount(ledger != null ? ledger.StoreCountOf(Res.Boards) : 0);
            crew.text = ledger != null ? ledger.hands.Count.ToString() : "0";
            float days = SheetBits.FoodDays(ledger);
            food.text = ledger == null || ledger.hands.Count == 0 ? "--"
                : days < 0f ? "Off" : days < .1f ? "<0.1d" : days > 99f ? "99+d" : days.ToString("0.#") + "d";
            food.style.color = ledger != null && ledger.hands.Count > 0 && (days < 1f)
                ? SheetTheme.Ember : days < 3f && days >= 0f ? Ice : Pearl;
            food.parent.tooltip = SheetBits.FoodDaysLine(ledger) + "; stored supply only, excludes future gathering";
            day.text = "Day " + TimeOfDay.Day;
        }

        internal static string CompactCount(int count) => count < 1000 ? count.ToString()
            : count < 1000000 ? (count / 1000f).ToString("0.#") + "k" : (count / 1000000f).ToString("0.#") + "m";
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
            if (c != null) Sheets.Open(new StoresSheet(c));
        }
    }
}
