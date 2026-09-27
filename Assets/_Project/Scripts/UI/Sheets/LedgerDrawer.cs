using System;
using System.Collections.Generic;
using System.Text;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The ledger drawer, 2026-09-27 (concept A1, Melvor redesign).**
    ///
    /// Kevin, on the Melvor Idle mock: *"this looks good, the UI that is"*.
    /// A left slide-in list of everything the camp is, opened by the ☰
    /// button at the top-left of the land HUD. It replaces the old four
    /// bottom buttons (Build/Crew/Stores/Ship) while ashore. Four framed
    /// groups: CAMP (overview, stores, people, build), GATHER (one row per
    /// seam the island has), MAKE (one row per production building
    /// instance, locked plans grey), SEA (ship, chart). A row opens its
    /// sheet and closes the drawer.
    ///
    /// Rows are built once per SHAPE (which seams, which buildings) and
    /// re-texted on the 0.25 s tick; only a new seam or building rebuilds
    /// its group. Dismiss: the ✕, a tap on the scrim, or a swipe left.
    public sealed class LedgerDrawer
    {
        /// **The camp overview seam.** Another sheet (`CampOverviewSheet`,
        /// goal chain + campfire) is being written beside this one. Set this
        /// to `c => new CampOverviewSheet(c)` from wherever that lands; until
        /// then the drawer looks the type up by name, and falls back to the
        /// campfire sheet.
        public static Func<Outpost, ISheet> CampOverviewFactory;

        public static bool IsOpen { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { IsOpen = false; }

        internal static ISheet CampOverview(Outpost camp)
        {
            if (camp == null) return null;
            var made = CampOverviewFactory != null ? CampOverviewFactory(camp) : null;
            if (made != null) return made;
            var t = Type.GetType("SeaSick.UI.Sheets.CampOverviewSheet");
            if (t != null && typeof(ISheet).IsAssignableFrom(t))
            {
                try { if (Activator.CreateInstance(t, camp) is ISheet s) return s; }
                catch (Exception e) { Debug.LogWarning("[Ledger] CampOverviewSheet(camp) failed: " + e.Message); }
            }
            return new FireSheet(camp);
        }

        // --- tones (mockup C table) ---
        const string Ok = "ok", Bad = "bad", Warn = "warn", Dim = "dim", Off = "off", Blue = "blue";
        static readonly string[] Tones = { Ok, Bad, Warn, Dim, Off, Blue };

        sealed class Row
        {
            public Button root;
            public Label name, sub;
            public VisualElement track, fill;
            public Func<ISheet> open;
            public Func<bool> current;
            string subText, tone, barTone, nameText;
            float bar = -2f;
            bool on, locked;

            public void Name(string text)
            {
                if (nameText == text) return;
                nameText = text; name.text = text;
            }

            public void Set(string text, string toneName, float bar01 = -1f, string barToneName = null, bool isLocked = false)
            {
                if (subText != text) { subText = text; sub.text = text; }
                if (tone != toneName)
                {
                    tone = toneName;
                    foreach (var t in Tones) sub.EnableInClassList("ledger-sub--" + t, t == toneName);
                }
                if (locked != isLocked) { locked = isLocked; root.EnableInClassList("ledger-row--locked", isLocked); }
                if (track == null) return;
                bool hasBar = bar01 >= 0f;
                float b = hasBar ? Mathf.Round(Mathf.Clamp01(bar01) * 100f) : -1f;
                if (b != bar)
                {
                    bar = b;
                    track.style.visibility = hasBar ? Visibility.Visible : Visibility.Hidden;
                    fill.style.width = Length.Percent(Mathf.Max(0f, b));
                }
                string bt = barToneName ?? toneName;
                if (barTone != bt)
                {
                    barTone = bt;
                    foreach (var t in Tones) fill.EnableInClassList("ledger-bar--" + t, t == bt);
                }
            }

            public void MarkCurrent()
            {
                bool now = current != null && current();
                if (now == on) return;
                on = now; root.EnableInClassList("ledger-row--on", now);
            }
        }

        readonly VisualElement layer, scrim, drawer, groups;
        readonly Label title, subtitle;
        readonly VisualElement campCard, gatherCard, makeCard, seaCard;
        readonly VisualElement gatherGroup, makeGroup;
        readonly List<Row> campRows = new List<Row>(), gatherRows = new List<Row>(),
            makeRows = new List<Row>(), seaRows = new List<Row>();
        readonly List<Action> updates = new List<Action>();
        readonly Dictionary<Row, Action> gatherUpdates = new Dictionary<Row, Action>();
        string gatherShape, makeShape, titleText, subText;
        Outpost builtFor;
        float nextRefresh;
        Vector2 swipeStart;
        bool swiping;

        public LedgerDrawer(VisualElement root)
        {
            // The two seams the other sheets left for the drawer: the camp
            // overview is the drawer's own Overview row, and its ☰ opens us.
            CampOverviewFactory = c => new CampOverviewSheet(c);
            CampOverviewSheet.OpenLedger = Open;
            StationSheet.OpenLedger = Open;
            BuildSheet.OpenLedger = Open;
            PeopleSheet.OpenLedger = Open;
            var style = Resources.Load<StyleSheet>("UI/Ledger");
            if (style != null) root.styleSheets.Add(style);
            else Debug.LogWarning("[Ledger] Resources/UI/Ledger.uss is missing — the ledger drawer will be unstyled.");

            layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.AddToClassList("ledger-layer");
            root.Add(layer);

            scrim = new VisualElement { pickingMode = PickingMode.Ignore };
            scrim.AddToClassList("ledger-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => Close());   // on the release: a close on the press would hand the release to WorldPicker
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

            // header: island, day · campfire · hands, settings, close
            var head = new VisualElement();
            head.AddToClassList("ledger-head");
            var col = new VisualElement();
            col.AddToClassList("ledger-head-col");
            title = new Label(); title.AddToClassList("ledger-title");
            subtitle = new Label(); subtitle.AddToClassList("ledger-subtitle");
            col.Add(title); col.Add(subtitle);
            head.Add(col);
            var gear = new Button(() => { Close(); SeaSick.UI.Menus.GameMenus.TogglePause(); });
            gear.AddToClassList("ledger-head-btn");
            gear.tooltip = "Settings and save";
            gear.Add(new Glyph(Glyph.Kind.Gear));
            head.Add(gear);
            var close = new Button(Close);
            close.AddToClassList("ledger-head-btn");
            close.tooltip = "Close the ledger";
            close.Add(new Glyph(Glyph.Kind.Close));
            head.Add(close);
            drawer.Add(head);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("ledger-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            drawer.Add(scroll);
            groups = scroll.contentContainer;

            campCard = Group("CAMP", out _);
            gatherCard = Group("GATHER", out gatherGroup);
            makeCard = Group("MAKE", out makeGroup);
            seaCard = Group("SEA", out _);

            BuildFixedRows();
            SetOpen(false);
        }

        VisualElement Group(string label, out VisualElement group)
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

        Row NewRow(VisualElement card, VisualElement icon, string name, bool bar)
        {
            var r = new Row();
            r.root = new Button(() => Go(r));
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
            if (bar)
            {
                r.track = new VisualElement { pickingMode = PickingMode.Ignore };
                r.track.AddToClassList("ledger-bar");
                r.fill = new VisualElement { pickingMode = PickingMode.Ignore };
                r.fill.AddToClassList("ledger-bar-fill");
                r.track.Add(r.fill);
                text.Add(r.track);
            }
            r.root.Add(text);
            r.Name(name);
            card.Add(r.root);
            return r;
        }

        static VisualElement ItemIcon(string res, string fallbackLand = null)
        {
            var tex = ItemIconSet.Get(res);
            if (tex != null)
            {
                var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit };
                img.AddToClassList("ledger-icon");
                return img;
            }
            var icon = new LandIcon(fallbackLand ?? "stores");
            icon.AddToClassList("ledger-icon");
            return icon;
        }

        static VisualElement Land(string kind)
        {
            var icon = new LandIcon(kind);
            icon.AddToClassList("ledger-icon");
            return icon;
        }

        void Go(Row r)
        {
            var sheet = r.open != null ? r.open() : null;
            Close();
            if (sheet != null) Sheets.Open(sheet);
        }

        // --- CAMP and SEA: fixed rows ---

        void BuildFixedRows()
        {
            var overview = NewRow(campCard, ItemIcon(Res.Food, "food"), "Overview", false);
            overview.open = () => CampOverview(MidnightLandHud.Camp);
            overview.current = () => Sheets.Current != null
                && Sheets.Current.GetType().Name == "CampOverviewSheet";
            updates.Add(() =>
            {
                int n = alertCount;
                overview.Set(n == 0 ? "all well" : n == 1 ? "1 alert" : n + " alerts", n == 0 ? Dim : Bad);
            });
            campRows.Add(overview);

            var stores = NewRow(campCard, ItemIcon(Res.Boards, "stores"), "Stores", false);
            stores.open = () => { var c = MidnightLandHud.Camp; return c != null ? new StoresSheet(c) : null; };
            stores.current = () => Sheets.Current is StoresSheet;
            updates.Add(() =>
            {
                var l = Ledger;
                int kinds = 0;
                if (l != null) foreach (var s in l.stores) if (s != null && s.whole > 0) kinds++;
                stores.Set(kinds == 1 ? "1 kind" : kinds + " kinds", Dim);
            });
            campRows.Add(stores);

            var people = NewRow(campCard, Land("crew"), "People", false);
            people.open = () => CampAlerts.People(MidnightLandHud.Camp);
            people.current = () => Sheets.Current is PeopleSheet || Sheets.Current is HandSheet;
            updates.Add(() =>
            {
                var l = Ledger;
                int idle = 0, n = l != null ? l.hands.Count : 0;
                if (l != null) foreach (var h in l.hands) if (h != null && h.order == OutpostOrder.Idle) idle++;
                if (idle > 0) people.Set(idle + " waiting", Warn);
                else people.Set(n == 1 ? "1 hand" : n + " hands", Dim);
            });
            campRows.Add(people);

            var build = NewRow(campCard, Land("build"), "Build", false);
            build.open = () => CampAlerts.BuildList(MidnightLandHud.Camp);
            build.current = () => Sheets.Current is BuildSheet;
            updates.Add(() =>
            {
                var l = Ledger;
                int sites = l != null ? l.SiteCount : 0;
                build.Set(sites == 0 ? "nothing queued" : sites == 1 ? "1 on the ground" : sites + " on the ground",
                    sites == 0 ? Dim : Blue);
            });
            campRows.Add(build);

            var ship = NewRow(seaCard, Land("ship"), "Ship", false);
            ship.open = () => SheetBootstrap.ShipFor();
            ship.current = () => Sheets.Current is ShipSheet;
            updates.Add(() =>
            {
                var c = MidnightLandHud.Camp;
                ship.Set(c != null && c.IsHome ? "home berth" : "at anchor", Dim);
            });
            seaRows.Add(ship);

            var chart = NewRow(seaCard, Land("ship"), "Chart", false);
            chart.open = () => ChartHook.Factory != null ? ChartHook.Factory() : null;
            chart.current = () => Sheets.Current is ChartSheet;
            updates.Add(() =>
            {
                int n = ChartData.Islands().Count;
                chart.Set(n == 1 ? "1 island" : n + " islands", Dim);
            });
            seaRows.Add(chart);
        }

        int alertCount;
        static OutpostLedger Ledger { get { var c = MidnightLandHud.Camp; return c != null ? c.Ledger : null; } }

        // --- GATHER: one row per seam this island has ---

        void RebuildGather(Outpost camp)
        {
            gatherCard.Clear();
            gatherRows.Clear();
            gatherUpdates.Clear();
            if (camp == null) return;
            foreach (var res in camp.Gatherable())
            {
                string r = res;
                bool hunt = r == Res.Game;
                var row = NewRow(gatherCard, hunt ? ItemIcon("Hide", "food") : ItemIcon(r, "logs"),
                    hunt ? "Hunting" : r, true);
                row.open = () => GatherSheet(MidnightLandHud.Camp, r);
                gatherRows.Add(row);
                gatherUpdates[row] = () => UpdateGather(row, r);
            }
        }

        /// The hand on this seam if there is one (his orders), else the
        /// people list, where somebody is sent.
        static ISheet GatherSheet(Outpost camp, string res)
        {
            var l = camp != null ? camp.Ledger : null;
            if (l == null) return null;
            foreach (var h in l.hands)
                if (h != null && h.order == OutpostOrder.Gather && h.target == res)
                    return new HandSheet(camp, h.name);
            return CampAlerts.People(camp);
        }

        readonly StringBuilder sb = new StringBuilder(48);

        void UpdateGather(Row row, string res)
        {
            var l = Ledger;
            if (l == null) return;
            string first = null, stall = null;
            int on = 0;
            foreach (var h in l.hands)
            {
                if (h == null || h.order != OutpostOrder.Gather || h.target != res) continue;
                on++;
                if (first == null) first = h.name;
                if (stall == null && (l.Stalled(h) || !string.IsNullOrEmpty(h.bodyBlocked)))
                    stall = CampAlerts.Short(l.StallReason(h));
            }
            var stock = l.Stock(res);
            bool worked = res != Res.Game && (stock == null || stock.standing < 1f);
            float fill = l.Fill01(res == Res.Game ? Res.Food : res);
            if (res == Res.Game && l.HunterBlocker() != null) { row.Set("no spear", Bad, 0f); return; }
            if (worked) { row.Set("none left", Bad, 0f); return; }
            if (stall != null) { row.Set(stall, Bad, fill); return; }
            if (on == 0) { row.Set("nobody", Off, fill, Blue); return; }
            sb.Clear(); sb.Append(first);
            if (on > 1) sb.Append(" +").Append(on - 1);
            row.Set(sb.ToString(), Dim, fill, Blue);
        }

        // --- MAKE: one row per production building instance ---

        static bool IsProduction(string planId) =>
            planId == BuildPlans.Farm.id || Recipes.StationHasRecipes(planId);

        static string MakeShape(Outpost camp)
        {
            if (camp == null || camp.Ledger == null) return "";
            var s = new StringBuilder(64);
            s.Append(camp.Ledger.CampfireLevel).Append('|');
            for (int i = 0; i < camp.Built.Count; i++)
            {
                var b = camp.Built[i];
                if (b != null && IsProduction(b.Id)) s.Append(b.Id).Append(i).Append(',');
            }
            return s.ToString();
        }

        void RebuildMake(Outpost camp)
        {
            makeCard.Clear();
            makeRows.Clear();
            if (camp == null || camp.Ledger == null) return;
            var l = camp.Ledger;
            foreach (var plan in BuildPlans.AtACamp)
            {
                if (!IsProduction(plan.id)) continue;
                string icon = MakesOf(plan);
                int count = 0, ordinal = 0;
                for (int i = 0; i < camp.Built.Count; i++)
                    if (camp.Built[i] != null && camp.Built[i].Id == plan.id) count++;
                for (int i = 0; i < camp.Built.Count; i++)
                {
                    var b = camp.Built[i];
                    if (b == null || b.Id != plan.id) continue;
                    ordinal++;
                    int raised = i;
                    var building = b;
                    var row = NewRow(makeCard, ItemIcon(icon, "saw"),
                        count > 1 ? plan.label + " " + ordinal : plan.label, true);
                    row.open = () =>
                    {
                        var c = MidnightLandHud.Camp;
                        if (building == null || c == null) return null;
                        if (building.Id == BuildPlans.Farm.id) return new FarmSheet(c, building);
                        return new StationSheet(c, building);
                    };
                    row.current = () => (Sheets.Current is StationSheet || Sheets.Current is FarmSheet)
                        && building != null && SameSpot(Sheets.Current.AnchorWorld, building.transform.position);
                    string position = plan.position;
                    string planId = plan.id;
                    updates.Add(() => UpdateMake(row, planId, raised, position));
                    makeRows.Add(row);
                }
                if (count > 0) continue;
                var p = plan;
                var ghost = NewRow(makeCard, ItemIcon(icon, "saw"), plan.label, true);
                if (!l.PlanUnlocked(plan.id))
                {
                    ghost.Set("campfire " + RecipeGraph.Roman(Techs.PlanLevel(plan.id)), Off, 0f, null, true);
                    ghost.open = () => CampOverview(MidnightLandHud.Camp);
                }
                else
                {
                    ghost.Set("not built", Off, -1f, null, true);
                    ghost.open = () => CampAlerts.BuildList(MidnightLandHud.Camp);
                }
                makeRows.Add(ghost);
            }
        }

        static bool SameSpot(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return (a - b).sqrMagnitude < 1f;
        }

        static string MakesOf(BuildPlan plan)
        {
            if (!string.IsNullOrEmpty(plan.makes)) return plan.makes;
            var at = Recipes.At(plan.id);
            return at != null && at.Count > 0 ? at[0].makes : plan.resource;
        }

        void UpdateMake(Row row, string planId, int raised, string position)
        {
            var l = Ledger;
            if (l == null) return;
            var st = l.StationForRaised(raised);
            OutpostHand worker = null;
            foreach (var h in l.hands)
            {
                if (h == null || h.order != OutpostOrder.Work) continue;
                if (st != null ? l.StationOfHand(h) == st : h.target == planId) { worker = h; break; }
            }
            string noOne = string.IsNullOrEmpty(position) ? "no worker" : "no " + position;
            if (st == null)
            {
                // The farm (no bench): who tends it, no job bar.
                if (worker == null) row.Set(noOne, Warn, 0f);
                else row.Set(worker.name, Ok, 1f);
                return;
            }
            float job = st.benchState == BenchState.Working ? st.benchProgress
                : st.benchState == BenchState.Finished ? 1f : 0f;
            string status = BuildingStatusLabels.Status(l, st);
            if (worker == null) { row.Set(noOne, Warn, 0f); return; }
            if (status == "Output full") { row.Set("output full", Bad, job); return; }
            if (status == "Needs supplies") { row.Set(worker.name + " · needs supplies", Bad, job); return; }
            if (!st.HasOrder && st.benchState == BenchState.Empty) { row.Set(worker.name + " · no order", Warn, 0f); return; }
            row.Set(worker.name, Ok, job);
        }

        // --- open / close / tick ---

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        public void Open()
        {
            if (MidnightLandHud.Camp == null) return;
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

        /// Called from `MidnightLandHud.Tick`; `alerts` is the strip's own
        /// count, so the Overview row and the strip always agree.
        public void Tick(bool active, VisualElement root, int alerts)
        {
            alertCount = alerts;
            layer.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            if (!active) { if (IsOpen) Close(); return; }

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

            var camp = MidnightLandHud.Camp;
            if (camp == null) { Close(); return; }
            if (camp != builtFor) { builtFor = camp; gatherShape = makeShape = null; }

            string gs = string.Join(",", camp.Gatherable());
            if (gs != gatherShape) { gatherShape = gs; RebuildGather(camp); }
            string ms = MakeShape(camp);
            if (ms != makeShape)
            {
                makeShape = ms;
                // Make rows' updaters live in `updates` after the fixed six;
                // drop them before rebuilding.
                if (updates.Count > FixedUpdates) updates.RemoveRange(FixedUpdates, updates.Count - FixedUpdates);
                RebuildMake(camp);
            }
            gatherGroup.style.display = gatherRows.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            makeGroup.style.display = makeRows.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            var l = camp.Ledger;
            string t = ChartData.PrettyName(camp.Island);
            if (t != titleText) { titleText = t; title.text = t; }
            int hands = l != null ? l.hands.Count : 0;
            string s = $"Day {TimeOfDay.Day} · campfire {RecipeGraph.Roman(l != null ? l.CampfireLevel : 1)} · {hands} hand{(hands == 1 ? "" : "s")}";
            if (s != subText) { subText = s; subtitle.text = s; }

            for (int i = 0; i < updates.Count; i++) updates[i]();
            foreach (var kv in gatherUpdates) kv.Value();
            foreach (var r in campRows) r.MarkCurrent();
            foreach (var r in makeRows) r.MarkCurrent();
            foreach (var r in seaRows) r.MarkCurrent();
        }

        /// CAMP (4) + SEA (2) updaters, added once in `BuildFixedRows`.
        const int FixedUpdates = 6;

        /// The ☰, gear and ✕ pictograms, painted (no font glyphs).
        internal sealed class Glyph : VisualElement
        {
            public enum Kind { Menu, Gear, Close }
            readonly Kind kind;

            public Glyph(Kind kind)
            {
                this.kind = kind;
                pickingMode = PickingMode.Ignore;
                AddToClassList("ledger-glyph");
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;
                float s = Mathf.Min(contentRect.width, contentRect.height) / 24f;
                if (s <= 0f) return;
                Vector2 V(float x, float y) => new Vector2(x * s, y * s);
                void Line(float x, float y, float a, float b)
                { p.BeginPath(); p.MoveTo(V(x, y)); p.LineTo(V(a, b)); p.Stroke(); }
                p.strokeColor = MidnightLandHud.Pearl;
                p.lineCap = LineCap.Round;
                switch (kind)
                {
                    case Kind.Menu:
                        p.lineWidth = 2.4f * s;
                        Line(4, 6, 20, 6); Line(4, 12, 20, 12); Line(4, 18, 20, 18);
                        break;
                    case Kind.Close:
                        p.lineWidth = 2.6f * s;
                        Line(6, 6, 18, 18); Line(18, 6, 6, 18);
                        break;
                    default:
                        p.lineWidth = 2.2f * s;
                        p.BeginPath(); p.Arc(V(12, 12), 3f * s, 0, 360); p.Stroke();
                        Line(12, 2, 12, 5); Line(12, 19, 12, 22); Line(2, 12, 5, 12); Line(19, 12, 22, 12);
                        Line(4.9f, 4.9f, 7f, 7f); Line(17f, 17f, 19.1f, 19.1f);
                        Line(4.9f, 19.1f, 7f, 17f); Line(17f, 7f, 19.1f, 4.9f);
                        break;
                }
            }
        }
    }
}
