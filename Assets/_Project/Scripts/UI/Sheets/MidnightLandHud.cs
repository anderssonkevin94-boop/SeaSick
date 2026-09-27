using SeaSick.World;
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
            logs = Resource(top, "logs", "Timber in store");
            boards = Resource(top, "planks", "Boards in store");
            crew = Resource(top, "crew", "Crew ashore");
            food = Resource(top, "food", "Stored food at current rations; excludes future gathering");
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

        static Label Resource(VisualElement parent, string icon, string hint)
        {
            var group = new VisualElement(); group.AddToClassList("land-resource"); group.tooltip = hint;
            group.Add(new LandIcon(icon));
            var label = new Label(); group.Add(label); parent.Add(group); return label;
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
}
