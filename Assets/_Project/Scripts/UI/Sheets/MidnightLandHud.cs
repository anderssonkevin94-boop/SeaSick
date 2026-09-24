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
        public const float NavHeight = 64f;
        public const float TopHeight = 46f;
        public static Rect NavigationRect { get; private set; }
        public static Rect ResourcesRect { get; private set; }
        public static Color Pearl => new Color32(232, 242, 246, 255);
        public static Color Ice => new Color32(164, 210, 232, 255);
        public static Color Muted => new Color32(166, 186, 198, 255);
        readonly VisualElement top, nav;
        readonly Label logs, boards, crew, day;
        readonly Button[] buttons = new Button[4];
        float nextUpdate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Enabled = true; NavigationRect = ResourcesRect = Rect.zero; }

        public MidnightLandHud(VisualElement root)
        {
            top = new VisualElement(); top.AddToClassList("land-resources"); root.Add(top);
            logs = Resource(top, "logs", "Logs in store");
            boards = Resource(top, "planks", "Planks in store");
            crew = Resource(top, "crew", "Crew ashore");
            day = new Label(); day.AddToClassList("land-day"); top.Add(day);
            nav = new VisualElement(); nav.AddToClassList("land-nav"); root.Add(nav);
            string[] names = { "Build", "Crew", "Stores", "Ship" };
            string[] icons = { "build", "crew", "stores", "ship" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var button = new Button(() => Open(index));
                button.AddToClassList("land-nav-button");
                button.tooltip = names[i];
                button.Add(new LandIcon(icons[i])); button.Add(new Label(names[i]));
                nav.Add(button); buttons[i] = button;
            }
        }

        static Label Resource(VisualElement parent, string icon, string hint)
        {
            var group = new VisualElement(); group.AddToClassList("land-resource"); group.tooltip = hint;
            group.Add(new LandIcon(icon));
            var label = new Label(); group.Add(label); parent.Add(group); return label;
        }

        public static Outpost Camp => Sheets.Anchor != null && Sheets.Anchor.CurrentIsland != null
            ? Outpost.Of(Sheets.Anchor.CurrentIsland) : null;

        void Open(int index)
        {
            var camp = Camp;
            if (camp == null) return;
            if (index == 3) { var s = SheetBootstrap.ShipFor(); if (s != null) Sheets.Open(s); return; }
            var sheet = new FireSheet(camp);
            sheet.FocusSection(index == 0 ? "build" : index == 1 ? "hands" : "camp");
            Sheets.Open(sheet);
        }

        public void Tick(VisualElement root)
        {
            bool active = Active;
            root.EnableInClassList("midnight-land", active);
            top.style.display = nav.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            if (!active) { NavigationRect = ResourcesRect = Rect.zero; return; }
            float scale = SheetHost.PanelScale;
            var safe = Screen.safeArea;
            float left = safe.xMin * scale + 8f, right = (Screen.width - safe.xMax) * scale + 8f;
            top.style.left = nav.style.left = left;
            top.style.right = nav.style.right = right;
            top.style.top = (Screen.height - safe.yMax) * scale + 8f;
            nav.style.bottom = safe.yMin * scale + 8f;
            ResourcesRect = new Rect(safe.xMin + 8f / scale, Screen.height - safe.yMax + 8f / scale,
                safe.width - 16f / scale, TopHeight / scale);
            NavigationRect = new Rect(safe.xMin + 8f / scale, Screen.height - safe.yMin - (NavHeight + 8f) / scale,
                safe.width - 16f / scale, NavHeight / scale);
            if (!SheetHost.FrameOpen) HudLayout.ClaimSheet(NavigationRect);
            if (Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + .25f;
            var ledger = Camp != null ? Camp.Ledger : null;
            logs.text = ledger != null ? ledger.StoreCountOf(Res.Timber).ToString() : "0";
            boards.text = ledger != null ? ledger.StoreCountOf(Res.Boards).ToString() : "0";
            crew.text = ledger != null ? ledger.hands.Count.ToString() : "0";
            day.text = "Day " + TimeOfDay.Day + " / Ashore";
            for (int i = 0; i < buttons.Length; i++)
            {
                bool selected = Sheets.Current is StationSheet && i == 0;
                if (Sheets.Current is FireSheet fire)
                    selected = fire.CurrentSection == (i == 0 ? "build" : i == 1 ? "hands" : i == 2 ? "camp" : "ship");
                if (Sheets.Current is ShipSheet && i == 3) selected = true;
                buttons[i].EnableInClassList("land-nav-selected", selected);
            }
        }
    }
}
