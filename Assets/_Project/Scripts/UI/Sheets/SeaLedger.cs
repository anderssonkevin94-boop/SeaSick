using System;
using System.Collections.Generic;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The ledger at sea, 2026-09-27.** Kevin: *"let's do it."* The land
    /// ledger was a camp-first drawer (replaced by the Camp sheet,
    /// `CampSheet`, 2026-09-30); this is its SEA-first twin, in the same look (`Resources/UI/Ledger.uss`, the same
    /// classes), opened from the rail's **Ledger** button while she is under
    /// way (`Menus.PauseChip`). A separate class on purpose: the land drawer's
    /// CAMP rows are another session's, and a mode flag through them would
    /// have both of us in one file.
    ///
    /// **SHIP** -- Ship (the manifest's hold page), Cargo, Crew, Chart, and
    /// Shipyard while a refit is possible (home berth with a dry dock, the
    /// same `RefitBlockers()` the dry dock's tap asks).
    /// **CAMPS** -- one row per camp: its food days and its alert count; a
    /// tap sets the chart's course to it (`ChartData.SetCourse`), the same
    /// decision the chart's own Set course makes.
    ///
    /// Combat controls are not here and are not touched: the lock button and
    /// the guns stay where the sea HUD puts them.
    public sealed class SeaLedger
    {
        public static bool IsOpen { get; private set; }
        static SeaLedger instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { IsOpen = false; instance = null; }

        /// True while the sea drawer may be shown: under way, not ashore at a
        /// camp (the land drawer owns that), and past the boot screen.
        public static bool Available =>
            !MidnightLandHud.Active && SeaSick.Save.GameBoot.Decided && SheetBits.Anchor != null;

        public static void Toggle() { if (instance != null) { if (IsOpen) instance.Close(); else instance.Open(); } }

        sealed class Row
        {
            public Button root;
            public Label name, sub;
            public Action tap;
            string subText, tone;

            public void Set(string nameText, string text, string toneName)
            {
                if (name.text != nameText) name.text = nameText;
                if (subText != text) { subText = text; sub.text = text; }
                if (tone != toneName)
                {
                    if (tone != null) sub.RemoveFromClassList("ledger-sub--" + tone);
                    tone = toneName;
                    if (tone != null) sub.AddToClassList("ledger-sub--" + tone);
                }
            }
        }

        readonly VisualElement layer, scrim, drawer, shipCard, campCard, campGroup;
        readonly Label title, subtitle;
        readonly Row ship, cargo, crew, chart, yard;
        readonly List<Row> camps = new List<Row>();
        readonly List<Outpost> campList = new List<Outpost>();
        readonly List<CampAlerts.Alert> alerts = new List<CampAlerts.Alert>(8);
        float nextRefresh;
        Vector2 swipeStart;
        bool swiping;

        public SeaLedger(VisualElement root)
        {
            instance = this;
            var style = Resources.Load<StyleSheet>("UI/Ledger");
            if (style != null && !root.styleSheets.Contains(style)) root.styleSheets.Add(style);

            layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.AddToClassList("ledger-layer");
            root.Add(layer);
            scrim = new VisualElement { pickingMode = PickingMode.Ignore };
            scrim.AddToClassList("ledger-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => Close());
            layer.Add(scrim);
            drawer = new VisualElement();
            drawer.AddToClassList("ledger-drawer");
            layer.Add(drawer);
            drawer.RegisterCallback<PointerDownEvent>(e => { swipeStart = e.position; swiping = true; }, TrickleDown.TrickleDown);
            drawer.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!swiping) return;
                var d = (Vector2)e.position - swipeStart;
                if (d.x < -60f && Mathf.Abs(d.x) > Mathf.Abs(d.y) * 1.5f) { swiping = false; Close(); }
            }, TrickleDown.TrickleDown);
            drawer.RegisterCallback<PointerUpEvent>(_ => swiping = false, TrickleDown.TrickleDown);

            var head = new VisualElement();
            head.AddToClassList("ledger-head");
            var col = new VisualElement();
            col.AddToClassList("ledger-head-col");
            title = new Label("At sea"); title.AddToClassList("ledger-title");
            subtitle = new Label(); subtitle.AddToClassList("ledger-subtitle");
            col.Add(title); col.Add(subtitle);
            head.Add(col);
            var gear = new Button(() => { Close(); SeaSick.UI.Menus.GameMenus.TogglePause(); });
            gear.AddToClassList("ledger-head-btn");
            gear.tooltip = "Settings and save";
            gear.Add(new CampPages.Glyph(CampPages.Glyph.Kind.Gear));
            head.Add(gear);
            var close = new Button(Close);
            close.AddToClassList("ledger-head-btn");
            close.tooltip = "Close the ledger";
            close.Add(new CampPages.Glyph(CampPages.Glyph.Kind.Close));
            head.Add(close);
            drawer.Add(head);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("ledger-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            drawer.Add(scroll);
            var groups = scroll.contentContainer;

            shipCard = Group(groups, "SHIP", out _);
            campCard = Group(groups, "CAMPS", out campGroup);

            ship = NewRow(shipCard, Land("ship"), () => OpenShip(null));
            cargo = NewRow(shipCard, Item(Res.Boards, "stores"), () => OpenShip("cargo"));
            crew = NewRow(shipCard, Land("crew"), () => OpenShip("crew"));
            chart = NewRow(shipCard, Land("ship"), () => { Close(); ChartHook.TryOpen(); });
            yard = NewRow(shipCard, Land("build"), () => { Close(); SeaSick.UI.ModularYard.ShipyardLiveBridge.Open(); });
            SetOpen(false);
        }

        static VisualElement Group(VisualElement groups, string label, out VisualElement group)
        {
            group = new VisualElement();
            group.AddToClassList("ledger-group");
            var l = new Label(label);
            l.AddToClassList("ledger-group-label");
            group.Add(l);
            var card = new VisualElement();
            card.AddToClassList("ledger-card");
            group.Add(card);
            groups.Add(group);
            return card;
        }

        Row NewRow(VisualElement card, VisualElement icon, Action tap)
        {
            var r = new Row { tap = tap };
            r.root = new Button(() => r.tap?.Invoke());
            r.root.AddToClassList("ledger-row");
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList("ledger-icon-box");
            if (icon != null) { icon.pickingMode = PickingMode.Ignore; box.Add(icon); }
            r.root.Add(box);
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("ledger-row-text");
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.AddToClassList("ledger-row-line");
            r.name = new Label { pickingMode = PickingMode.Ignore };
            r.name.AddToClassList("ledger-row-name");
            r.sub = new Label { pickingMode = PickingMode.Ignore };
            r.sub.AddToClassList("ledger-sub");
            line.Add(r.name); line.Add(r.sub);
            text.Add(line);
            r.root.Add(text);
            card.Add(r.root);
            return r;
        }

        static VisualElement Land(string kind)
        {
            var icon = new LandIcon(kind);
            icon.AddToClassList("ledger-icon");
            return icon;
        }

        static VisualElement Item(string res, string fallback)
        {
            var tex = ItemIconSet.Get(res);
            if (tex == null) return Land(fallback);
            var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit };
            img.AddToClassList("ledger-icon");
            return img;
        }

        /// The manifest, at its hold page, first cargo page or first crew page.
        void OpenShip(string page)
        {
            Close();
            if (SheetBits.Anchor == null) return;
            var s = new ShipSheet();
            if (page != null)
            {
                var labels = s.TabLabels;
                if (labels != null)
                    for (int i = 0; i < labels.Length; i++)
                        if (labels[i] != null && labels[i].StartsWith(page)) { s.SetTab(i); break; }
            }
            Sheets.Open(s);
        }

        public void Open()
        {
            if (!Available) return;
            nextRefresh = 0f;
            SetOpen(true);
        }

        public void Close() => SetOpen(false);

        void SetOpen(bool open)
        {
            IsOpen = open;
            swiping = false;
            layer.EnableInClassList("ledger-layer--open", open);
            scrim.pickingMode = open ? PickingMode.Position : PickingMode.Ignore;
        }

        /// From `SheetHost`, every frame, after the land HUD.
        public void Tick(VisualElement root)
        {
            bool avail = Available;
            layer.style.display = avail ? DisplayStyle.Flex : DisplayStyle.None;
            if (!avail) { if (IsOpen) Close(); return; }

            float scale = SheetHost.PanelScale;
            var safe = Screen.safeArea;
            float w = root.resolvedStyle.width;
            if (float.IsNaN(w) || w < 1f) w = 430f;
            drawer.style.width = Mathf.Min(w * .81f, 380f + safe.xMin * scale);
            drawer.style.paddingTop = (Screen.height - safe.yMax) * scale + 16f;
            drawer.style.paddingBottom = safe.yMin * scale + 16f;
            drawer.style.paddingLeft = safe.xMin * scale + 12f;

            if (!IsOpen || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .25f;

            var roster = SheetBits.Roster;
            int aboard = 0;
            if (roster != null) foreach (var a in roster.All) if (a != null && a.IsAboard) aboard++;
            var hold = SheetBits.Hold;
            int held = hold != null ? hold.VisibleCount : 0;
            string st = $"Day {TimeOfDay.Day} · {aboard} aboard · {held} in the hold";
            if (subtitle.text != st) subtitle.text = st;

            ship.Set("Ship", "the manifest", "dim");
            cargo.Set("Cargo", held > 0 ? held + " in the hold" : "empty", held > 0 ? "blue" : "dim");
            crew.Set("Crew", aboard == 1 ? "1 aboard" : aboard + " aboard", "dim");
            chart.Set("Chart", ChartData.HasCourse && ChartData.Course != null
                ? "course: " + ChartData.PrettyName(ChartData.Course.Island) : "set a course", ChartData.HasCourse ? "ok" : "dim");
            var service = SeaSick.Ship.Modular.ShipyardService.Player;
            bool canRefit = service != null && service.RefitBlockers().Count == 0;
            yard.root.style.display = canRefit ? DisplayStyle.Flex : DisplayStyle.None;
            if (canRefit) yard.Set("Shipyard", "dry dock ready", "ok");

            RefreshCamps();
        }

        void RefreshCamps()
        {
            campList.Clear();
            foreach (var o in Outpost.All)
                if (o != null && o.Ledger != null && o.HasCamp) campList.Add(o);
            while (camps.Count < campList.Count)
            {
                var r = NewRow(campCard, Land("build"), null);
                camps.Add(r);
            }
            for (int i = 0; i < camps.Count; i++)
            {
                var r = camps[i];
                if (i >= campList.Count) { r.root.style.display = DisplayStyle.None; r.tap = null; continue; }
                var o = campList[i];
                r.root.style.display = DisplayStyle.Flex;
                // **A tap opens a camp card, 2026-09-27 (audit #11).** It
                // used to set the chart's course to this camp silently, with
                // no look at what you were tapping. `CampCard` shows what
                // the row's own text already summarises (food, alerts) and
                // makes "Set course" an explicit button on it.
                r.tap = () => { Close(); Sheets.Open(new CampCard(o)); };
                var l = o.Ledger;
                float days = SheetBits.FoodDays(l);
                CampAlerts.Collect(o, alerts);
                string food = l.hands.Count == 0 ? "no hands"
                    : days < 0f ? "food off" : days > 99f ? "99+d food" : days.ToString("0.#") + "d food";
                bool course = ChartData.HasCourse && ChartData.Course == o;
                string text = course ? "course set · " + food
                    : alerts.Count > 0 ? $"{food} · {alerts.Count} alert{(alerts.Count == 1 ? "" : "s")}" : food;
                string tone = course ? "ok"
                    : l.hands.Count > 0 && days >= 0f && days < 1f ? "bad" : alerts.Count > 0 ? "warn" : "dim";
                r.Set(ChartData.PrettyName(o.Island), text, tone);
            }
            campGroup.style.display = campList.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    /// **The camp card (2026-09-27, audit #11).** What a Sea Ledger CAMPS
    /// row opens now, instead of silently setting the chart's course to it:
    /// food, alerts, and an explicit "Set course" button. Small, like
    /// `WallSheet` -- one page, no tabs.
    sealed class CampCard : ISheetFramed
    {
        readonly Outpost camp;
        public CampCard(Outpost o) { camp = o; }

        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public Color Accent => SheetTheme.Sea;
        public string Title => ChartData.PrettyName(camp != null ? camp.Island : null);
        public Vector3 AnchorWorld => camp != null ? camp.CampCentre : Vector3.zero;
        public bool StillValid => camp != null && camp.Ledger != null && camp.HasCamp;

        Label sub;
        Label handsLine, alertLine;
        Button courseBtn;
        readonly List<CampAlerts.Alert> alerts = new List<CampAlerts.Alert>(8);

        public VisualElement BuildHeader()
        {
            var icon = new StationPage.Glyph("chart", MidnightLandHud.Ice, "cp-glyph");
            return CampPages.IconHeader(Title, icon, out sub);
        }

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            handsLine = SheetKit.Text("", true, false, 20f);
            root.Add(handsLine);
            alertLine = SheetKit.Text("", false, true, 13f);
            root.Add(alertLine);
            Refresh();
            return root;
        }

        public VisualElement BuildActions()
        {
            courseBtn = SheetKit.Btn("Set course", OnCourse, true);
            return SheetKit.Actions(courseBtn);
        }

        public void Refresh()
        {
            if (camp == null || camp.Ledger == null) return;
            var l = camp.Ledger;
            float days = SheetBits.FoodDays(l);
            string food = l.hands.Count == 0 ? "no hands"
                : days < 0f ? "food off" : days > 99f ? "food, 99+ days" : $"food, {days:0.#} days";
            if (sub != null) sub.text = food;
            if (handsLine != null)
                handsLine.text = l.hands.Count == 1 ? "1 hand" : $"{l.hands.Count} hands";

            alerts.Clear();
            CampAlerts.Collect(camp, alerts);
            if (alertLine != null)
                alertLine.text = alerts.Count == 0 ? "nothing needs attention"
                    : alerts.Count == 1 ? "1 alert" : $"{alerts.Count} alerts";

            bool already = ChartData.HasCourse && ChartData.Course == camp;
            if (courseBtn != null)
            {
                courseBtn.text = already ? "Course set" : "Set course";
                courseBtn.SetEnabled(!already);
            }
        }

        void OnCourse()
        {
            if (camp == null) return;
            ChartData.SetCourse(camp);
            Sheets.Close();
        }
    }
}
