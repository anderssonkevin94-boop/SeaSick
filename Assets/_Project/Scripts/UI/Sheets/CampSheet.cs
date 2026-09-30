using System;
using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The Camp sheet, 2026-09-30 (island UI restructure, phase 4).** The
    /// one camp hub. It REPLACES the bottom ledger drawer (`LedgerDrawer`),
    /// the camp overview (`CampOverviewSheet`) and the alert overflow: the
    /// thumb bar's **Camp** button, the alert strip's "+N" chip, the Next
    /// card's pinned goal and every sheet header's ☰ open it. A tall sheet,
    /// "Camp" + "Island 5 · Campfire I · 5 hands", top to bottom:
    /// <list type="number">
    /// <item>**NEEDS YOU · N** -- every `CampAlerts.Collect` alert: the text
    ///   on the left, its fix as a 44 px button on the right (`Alert.fixLabel`,
    ///   "Fix" when a maker gave none); the tap runs the alert's `open`.</item>
    /// <item>**YOUR GOAL** -- only while a goal is pinned (`GoalPin`, "Set
    ///   as goal"): the chain from the old overview (rows, first step with
    ///   its Go, Clear goal). Unpinned, the fire's next level is section 3
    ///   and the Next card carries the tutorial.</item>
    /// <item>**RAISE THE FIRE TO N** -- the next campfire level's cost as
    ///   item · bar · have/need, then ONE link to the fix for the most
    ///   missing item (`ShortFix.Most` / `ShortFix.For`), or the Raise button
    ///   once it is affordable. Hidden at the top level.</item>
    /// <item>**BUILDINGS** -- one row per built building (identical ones
    ///   grouped, "Hut ×2 · 3 of 4 beds used"), a short status on the right
    ///   ("no farmhand" in ember, "working"), and the blueprints on the
    ///   ground ("being built · 40%"). A tap opens that building's sheet
    ///   (`Sheets.TryCreateFor`).</item>
    /// </list>
    /// The pinned thumb row: **People n/m** (`WorkersSheet`), **Stores**
    /// (`BackpackSheet`) and **Save · settings** (the pause menu).
    ///
    /// **It reads and it calls; it never decides.** Built once; the 0.25 s
    /// refresh only re-texts and re-shows pooled rows (a rebuilt button
    /// loses the tap it is in the middle of). The body is a clamped
    /// scroll view, so a big camp scrolls instead of being cut off.
    public sealed class CampSheet : ISheetFramed
    {
        readonly Outpost outpost;

        public CampSheet(Outpost camp) { outpost = camp; }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        // --- ISheet / ISheetFramed -------------------------------------------

        public string Title => "Camp";
        public Vector3 AnchorWorld => outpost != null ? outpost.CampCentre : Vector3.zero;
        public bool StillValid =>
            outpost != null && outpost.Ledger != null && (outpost.HasCamp || outpost.Building);
        public Color Accent => MidnightLandHud.Ice;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public bool WantsTallSheet => true;

        Label subtitle;
        Button peopleBtn;

        public VisualElement BuildHeader()
        {
            var house = new CampPages.HouseGlyph();
            house.style.width = 22f;
            house.style.height = 22f;
            return CampPages.IconHeader("Camp", house, out subtitle);
        }

        /// The thumb row. Navigation only, so none of the three is primary.
        public VisualElement BuildActions()
        {
            peopleBtn = SheetKit.Btn("People", OpenPeople);
            var stores = SheetKit.Btn("Stores", OpenStores);
            var save = SheetKit.Btn("Save · settings", OpenSettings);
            peopleBtn.style.minHeight = 48f;
            stores.style.minHeight = 48f;
            save.style.minHeight = 48f;
            peopleBtn.text = PeopleText();
            return SheetKit.Actions(peopleBtn, stores, save);
        }

        // --- styles --------------------------------------------------------------

        static StyleSheet overviewStyle;
        static StyleSheet OverviewStyle =>
            overviewStyle != null ? overviewStyle : (overviewStyle = Resources.Load<StyleSheet>("UI/Overview"));

        static T Classed<T>(T e, string cls) where T : VisualElement => CampPages.Classed(e, cls);

        // --- pooled parts ----------------------------------------------------------

        sealed class AlertRow
        {
            public VisualElement root;
            public Label text;
            public Button fix;
            public Func<ISheet> open;
            public string lastText, lastFix, lastTone;
        }

        sealed class CostRow
        {
            public VisualElement root, icon, fill;
            public Label name, qty;
            public string lastRes, lastQty;
            public float lastFill = -1f;
            public bool lastShort;
        }

        sealed class BuildRow
        {
            public Button root;
            public Label name, sub, status;
            public Building building;
            public PendingBuild site;
            public string planId;
            public string lastName, lastSub, lastStatus, lastTone;
        }

        VisualElement rootEl;
        // needs
        Label needsHead, needsNone;
        VisualElement needsHolder;
        readonly List<AlertRow> alertRows = new List<AlertRow>();
        readonly List<CampAlerts.Alert> alerts = new List<CampAlerts.Alert>(8);
        // goal
        VisualElement goalBlock, goalBar, stepCard, chainList;
        Label goalEyebrow, goalTitle, goalWhy, goalProgress, stepTitle, stepDetail;
        Button clearGoalBtn, goBtn;
        GoalChain chain;
        long chainKey = long.MinValue;
        int pinVersion = -1;
        float nextChain;
        // fire
        VisualElement fireBlock, costHolder;
        Label fireHead, fireNote;
        Button linkBtn, raiseBtn;
        readonly List<CostRow> costRows = new List<CostRow>();
        ShortFix.Fix linkFix;
        // buildings
        Label builtHead, builtNone;
        VisualElement builtHolder;
        readonly List<BuildRow> buildRows = new List<BuildRow>();
        readonly List<Group> groups = new List<Group>();
        readonly List<SiteGroup> siteGroups = new List<SiteGroup>();

        public VisualElement Build()
        {
            alertRows.Clear(); costRows.Clear(); buildRows.Clear();
            chainKey = long.MinValue;
            chain = null;
            pinVersion = -1;
            nextChain = 0f;

            rootEl = ReadoutUi.Root(out var col);
            var ov = OverviewStyle;
            if (ov != null) rootEl.styleSheets.Add(ov);

            // 1. NEEDS YOU
            needsHead = ReadoutUi.Eyebrow(col);
            needsHolder = Classed(new VisualElement(), "cs-list");
            col.Add(needsHolder);
            needsNone = Classed(new Label("Nothing needs you right now."), "cp-note");
            col.Add(needsNone);

            // 2. YOUR GOAL (pinned only)
            goalBlock = BuildGoal();
            col.Add(goalBlock);

            // 3. RAISE THE FIRE
            fireBlock = Classed(new VisualElement(), "cs-block");
            fireHead = ReadoutUi.Eyebrow(fireBlock);
            costHolder = Classed(new VisualElement(), "cs-list");
            fireBlock.Add(costHolder);
            linkBtn = new Button(OnLink) { text = "" };
            linkBtn.AddToClassList("cs-link");
            fireBlock.Add(linkBtn);
            raiseBtn = new Button(Raise) { text = "" };
            raiseBtn.AddToClassList("cs-raise");
            fireBlock.Add(raiseBtn);
            fireNote = Classed(new Label(), "cp-note");
            fireBlock.Add(fireNote);
            col.Add(fireBlock);

            // 4. BUILDINGS
            builtHead = ReadoutUi.Eyebrow(col);
            // **Two to a row (2026-09-30).** One row per building ran past
            // the tall frame at 13 buildings -- the last rows cut in half
            // under the thumb row (Kevin's rule: a control is never
            // clipped). Name over status in half-width tiles halves it.
            builtHolder = Classed(new VisualElement(), "cs-list");
            builtHolder.AddToClassList("cs-grid2");
            col.Add(builtHolder);
            builtNone = Classed(new Label("Nothing built yet."), "cp-note");
            col.Add(builtNone);

            Refresh();
            return rootEl;
        }

        // --- refresh ---------------------------------------------------------------

        public void Refresh()
        {
            var l = L;
            if (l == null || rootEl == null) return;

            var island = outpost.Island;
            string place = island != null ? ChartData.PrettyName(island) : "the camp";
            int hands = l.hands != null ? l.hands.Count : 0;
            SetText(subtitle,
                $"{place} · Campfire {RecipeGraph.Roman(l.CampfireLevel)} · {hands} hand{(hands == 1 ? "" : "s")}");
            if (peopleBtn != null) SetText(peopleBtn, PeopleText());

            DrawNeeds();
            DrawGoal(l);
            DrawFire(l);
            DrawBuildings(l);
        }

        /// Label or Button: re-text only when it changed.
        static void SetText(TextElement label, string s)
        {
            s ??= "";
            if (label != null && label.text != s) label.text = s;
        }

        // --- 1. NEEDS YOU -------------------------------------------------------------

        void DrawNeeds()
        {
            CampAlerts.Collect(outpost, alerts);
            SetText(needsHead, "NEEDS YOU · " + alerts.Count);
            ReadoutUi.Show(needsNone, alerts.Count == 0);
            for (int i = 0; i < alerts.Count; i++)
            {
                if (i >= alertRows.Count) alertRows.Add(MakeAlertRow());
                var r = alertRows[i];
                var a = alerts[i];
                ReadoutUi.Show(r.root, true);
                r.open = a.open;
                if (r.lastText != a.text) { r.lastText = a.text; r.text.text = a.text ?? ""; }
                string fix = string.IsNullOrEmpty(a.fixLabel) ? "Fix" : a.fixLabel;
                if (r.lastFix != fix) { r.lastFix = fix; r.fix.text = fix; }
                string tone = a.tone == CampAlerts.Tone.Raid ? "raid" : a.tone == CampAlerts.Tone.Bad ? "bad" : "warn";
                if (r.lastTone != tone)
                {
                    r.lastTone = tone;
                    r.root.EnableInClassList("cs-alert--raid", tone == "raid");
                    r.root.EnableInClassList("cs-alert--bad", tone == "bad");
                    r.root.EnableInClassList("cs-alert--warn", tone == "warn");
                }
            }
            for (int i = alerts.Count; i < alertRows.Count; i++)
            {
                ReadoutUi.Show(alertRows[i].root, false);
                alertRows[i].open = null;
            }
        }

        AlertRow MakeAlertRow()
        {
            var r = new AlertRow();
            r.root = Classed(new VisualElement(), "cs-alert");
            r.text = Classed(new Label { pickingMode = PickingMode.Ignore }, "cs-alert-text");
            r.fix = new Button(() => RunAlert(r)) { text = "" };
            r.fix.AddToClassList("cp-fix");
            r.root.Add(r.text);
            r.root.Add(r.fix);
            needsHolder.Add(r.root);
            return r;
        }

        static void RunAlert(AlertRow r)
        {
            var make = r.open;
            var sheet = make != null ? make() : null;
            if (sheet != null) Sheets.Open(sheet);
        }

        // --- 2. YOUR GOAL ---------------------------------------------------------------

        VisualElement BuildGoal()
        {
            var block = Classed(new VisualElement(), "cs-block");

            var eyeRow = Classed(new VisualElement(), "ov-eyebrow-row");
            goalEyebrow = Classed(new Label("YOUR GOAL"), "ov-eyebrow");
            eyeRow.Add(goalEyebrow);
            clearGoalBtn = new Button(ClearGoal) { text = "Clear goal" };
            clearGoalBtn.AddToClassList("ov-clear-goal");
            eyeRow.Add(clearGoalBtn);
            block.Add(eyeRow);
            goalTitle = Classed(new Label(), "ov-goal-title");
            block.Add(goalTitle);
            goalWhy = Classed(new Label(), "ov-goal-why");
            block.Add(goalWhy);

            var track = Classed(new VisualElement(), "ov-bar");
            goalBar = Classed(new VisualElement(), "ov-bar-fill");
            track.Add(goalBar);
            block.Add(track);
            goalProgress = Classed(new Label(), "ov-progress");
            block.Add(goalProgress);

            chainList = Classed(new VisualElement(), "ov-chain-list");
            block.Add(chainList);

            stepCard = Classed(new VisualElement(), "ov-step");
            var words = Classed(new VisualElement(), "ov-step-words");
            stepTitle = Classed(new Label(), "ov-step-title");
            stepDetail = Classed(new Label(), "ov-step-detail");
            words.Add(stepTitle);
            words.Add(stepDetail);
            stepCard.Add(words);
            goBtn = new Button(Go) { text = "Go" };
            goBtn.AddToClassList("ov-go");
            stepCard.Add(goBtn);
            block.Add(stepCard);
            block.style.display = DisplayStyle.None;
            return block;
        }

        void DrawGoal(OutpostLedger l)
        {
            bool pinned = l.ActiveGoal != null;
            if (!pinned)
            {
                chain = null;
                chainKey = long.MinValue;
                ReadoutUi.Show(goalBlock, false);
                return;
            }
            float now = Time.unscaledTime;
            if (chain == null || now >= nextChain || pinVersion != GoalPin.Version)
            {
                nextChain = now + 1f;
                pinVersion = GoalPin.Version;
                chain = GoalChain.ForCamp(l);
            }
            var c = chain;
            if (c == null || !c.HasGoal || !c.Pinned)
            {
                ReadoutUi.Show(goalBlock, false);
                return;
            }
            ReadoutUi.Show(goalBlock, true);
            long key = c.Key;
            if (key == chainKey) return;
            chainKey = key;

            SetText(goalTitle, c.title ?? "");
            SetText(goalWhy, c.why ?? "");
            float f = c.total > 0 ? (float)c.ready / c.total : 1f;
            goalBar.style.width = Length.Percent(Mathf.Clamp01(f) * 100f);
            SetText(goalProgress, $"{c.ready} of {c.total} ready");

            // The chain rows are plain text (no taps to lose), so this part
            // is rebuilt when the chain's own key moves, as the overview did.
            chainList.Clear();
            foreach (var r in c.rows) chainList.Add(ChainRow(r));

            var s = c.firstStep;
            if (s != null)
            {
                ReadoutUi.Show(stepCard, true);
                SetText(stepTitle, "First step: " + s.title);
                SetText(stepDetail, s.detail ?? "");
                SetText(goBtn, s.action != GoalAction.Complete ? "Go"
                    : c.kind == GoalKind.Upgrade ? "Upgrade" : "Raise");
                goBtn.SetEnabled(s.action != GoalAction.None);
            }
            else
            {
                ReadoutUi.Show(stepCard, true);
                SetText(stepTitle, "Nothing to tap yet");
                SetText(stepDetail, "the hands are on it; check back soon");
                goBtn.SetEnabled(false);
            }
        }

        static VisualElement ChainRow(GoalRow r)
        {
            var row = new VisualElement();
            row.AddToClassList("ov-row");
            row.AddToClassList(r.state == GoalState.Ok ? "ov-row--ok"
                : r.state == GoalState.Blocked ? "ov-row--no" : "ov-row--wait");
            row.pickingMode = PickingMode.Ignore;

            if (r.depth > 0)
            {
                var indent = new VisualElement { pickingMode = PickingMode.Ignore };
                indent.style.width = r.depth * 16f;
                indent.style.flexShrink = 0f;
                row.Add(indent);
            }
            var dot = new Label(r.state == GoalState.Ok ? "✓" : r.state == GoalState.Blocked ? "!" : "…")
                { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("ov-dot");
            row.Add(dot);

            var tex = ItemIconSet.Get(r.icon ?? (r.kind == GoalRowKind.Station ? Res.Boards : null));
            if (tex != null)
            {
                var img = new Image { image = tex };
                img.AddToClassList("ov-icon");
                img.pickingMode = PickingMode.Ignore;
                row.Add(img);
            }

            var words = new VisualElement { pickingMode = PickingMode.Ignore };
            words.AddToClassList("ov-row-words");
            words.Add(Classed(new Label(r.name ?? "") { pickingMode = PickingMode.Ignore }, "ov-row-name"));
            if (!string.IsNullOrEmpty(r.how))
                words.Add(Classed(new Label(r.how) { pickingMode = PickingMode.Ignore }, "ov-row-how"));
            row.Add(words);
            row.Add(Classed(new Label(r.qty ?? "") { pickingMode = PickingMode.Ignore }, "ov-row-qty"));
            return row;
        }

        /// The first step's Go: open the sheet that fixes it (the old
        /// overview's, verbatim).
        void Go()
        {
            var s = chain != null ? chain.firstStep : null;
            var l = L;
            if (s == null || l == null || outpost == null) return;
            switch (s.action)
            {
                case GoalAction.Complete:
                    if (chain.kind == GoalKind.Upgrade) UpgradeGoal(chain);
                    else Raise();
                    break;
                case GoalAction.OpenStation:
                {
                    var b = BuiltOf(s.planId);
                    if (b != null) Sheets.Open(new StationSheet(outpost, b));
                    else Open(CampAlerts.BuildList(outpost, s.planId));
                    break;
                }
                case GoalAction.AssignHand:
                {
                    var idle = SheetBits.FirstIdle(l);
                    if (idle != null) Sheets.Open(new HandSheet(outpost, idle.name));
                    else Open(CampAlerts.People(outpost));
                    break;
                }
                case GoalAction.Build:
                    Open(CampAlerts.BuildList(outpost, s.planId));
                    break;
            }
        }

        /// A pinned building's level-up, paid from here: THIS building, the
        /// same call its station page's Upgrade makes.
        void UpgradeGoal(GoalChain c)
        {
            var l = L;
            if (l == null || !l.UpgradeAt(c.raisedIndex, c.planId)) return;
            var built = outpost.Built;
            if (built != null && c.raisedIndex >= 0 && c.raisedIndex < built.Count && built[c.raisedIndex] != null)
                outpost.Retint(built[c.raisedIndex]);
            chain = null;
            chainKey = long.MinValue;
            Refresh();
        }

        void ClearGoal()
        {
            GoalPin.Clear(outpost);
            chain = null;
            chainKey = long.MinValue;
            Refresh();
        }

        Building BuiltOf(string planId)
        {
            if (outpost == null || string.IsNullOrEmpty(planId)) return null;
            var built = outpost.Built;
            if (built == null) return null;
            for (int i = 0; i < built.Count; i++)
                if (built[i] != null && built[i].Id == planId) return built[i];
            return null;
        }

        static void Open(ISheet sheet) { if (sheet != null) Sheets.Open(sheet); }

        // --- 3. RAISE THE FIRE ------------------------------------------------------------

        void DrawFire(OutpostLedger l)
        {
            var next = l.NextCampfire;
            if (next == null)
            {
                ReadoutUi.Show(fireBlock, false);
                return;
            }
            ReadoutUi.Show(fireBlock, true);
            SetText(fireHead, "RAISE THE FIRE TO " + RecipeGraph.Roman(next.level));

            var cost = next.cost;
            int n = cost != null ? cost.Length : 0;
            var most = new ShortFix.Most();
            for (int i = 0; i < n; i++)
            {
                if (i >= costRows.Count) costRows.Add(MakeCostRow());
                var r = costRows[i];
                var line = cost[i];
                int have = l.SpendableOf(line.res);
                bool ok = have >= line.n;
                if (!ok) most.Add(line.res, line.n - have);
                ReadoutUi.Show(r.root, true);
                if (r.lastRes != line.res)
                {
                    r.lastRes = line.res;
                    r.name.text = Cap(ResDefs.Label(line.res));
                    StationPage.SetIcon(r.icon, line.res);
                }
                string q = $"{Mathf.Min(have, line.n)}/{line.n}";
                if (r.lastQty != q) { r.lastQty = q; r.qty.text = q; }
                float f = line.n > 0 ? Mathf.Round(Mathf.Clamp01((float)have / line.n) * 100f) : 100f;
                if (f != r.lastFill) { r.lastFill = f; r.fill.style.width = Length.Percent(f); }
                if (r.lastShort == ok)
                {
                    r.lastShort = !ok;
                    r.root.EnableInClassList("cs-cost--short", !ok);
                }
            }
            for (int i = n; i < costRows.Count; i++) ReadoutUi.Show(costRows[i].root, false);

            bool can = l.CanRaiseCampfire(out string why);
            ReadoutUi.Show(raiseBtn, can);
            if (can) SetText(raiseBtn, "Raise the fire to " + RecipeGraph.Roman(next.level));

            // One link to the fix for the most-missing item.
            linkFix = default;
            if (!can && most.Res != null) linkFix = ShortFix.For(outpost, most.Res);
            bool link = linkFix.Valid;
            ReadoutUi.Show(linkBtn, link);
            if (link) SetText(linkBtn, LinkText(linkFix));
            // Nothing short and not payable (should not happen), or no fix
            // for what is short: say why, in words.
            bool note = !can && !link && !string.IsNullOrEmpty(why);
            ReadoutUi.Show(fireNote, note);
            if (note) SetText(fireNote, Cap(why) + ".");
        }

        /// "Next: build sawmill for boards →", "Next: gather timber →".
        static string LinkText(ShortFix.Fix f)
        {
            string label = f.label ?? "";
            string lower = label.Length > 0 ? char.ToLowerInvariant(label[0]) + label.Substring(1) : label;
            string tail = f.kind == ShortFix.Kind.Build && !string.IsNullOrEmpty(f.res)
                ? " for " + ResDefs.Label(f.res).ToLowerInvariant() : "";
            return "Next: " + lower + tail + " →";
        }

        CostRow MakeCostRow()
        {
            var r = new CostRow();
            r.root = Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cs-cost");
            r.icon = StationPage.Icon(null, "cs-cost-icon");
            r.name = Classed(new Label { pickingMode = PickingMode.Ignore }, "cs-cost-name");
            var track = Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cs-cost-track");
            r.fill = Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cs-cost-fill");
            track.Add(r.fill);
            r.qty = Classed(new Label { pickingMode = PickingMode.Ignore }, "cs-cost-qty");
            r.root.Add(r.icon);
            r.root.Add(r.name);
            r.root.Add(track);
            r.root.Add(r.qty);
            costHolder.Add(r.root);
            return r;
        }

        void OnLink()
        {
            if (linkFix.Valid && linkFix.Run(outpost)) Refresh();
        }

        void Raise()
        {
            var l = L;
            if (l == null) return;
            if (l.RaiseCampfire())
            {
                chain = null;
                chainKey = long.MinValue;
                Refresh();
            }
        }

        // --- 4. BUILDINGS -------------------------------------------------------------------

        sealed class Group
        {
            public string id, name;
            public Building first;
            public int count, bad, good;
            public string badText, goodText;
        }

        sealed class SiteGroup
        {
            public string planId, name;
            public PendingBuild first;
            public int count;
            public float progress, fill;
        }

        void DrawBuildings(OutpostLedger l)
        {
            groups.Clear();
            siteGroups.Clear();
            var built = outpost.Built;
            int total = 0;
            if (built != null)
            {
                for (int i = 0; i < built.Count; i++)
                {
                    var b = built[i];
                    if (b == null || b.Kind == BuildKind.Fire) continue;
                    Group g = null;
                    for (int k = 0; k < groups.Count; k++)
                        if (groups[k].id == b.Id) { g = groups[k]; break; }
                    if (g == null)
                    {
                        var plan = BuildPlans.Named(b.Id);
                        string label = !string.IsNullOrEmpty(plan.label) ? plan.label : b.Label;
                        g = new Group { id = b.Id, name = Cap(string.IsNullOrEmpty(label) ? b.Id : label), first = b };
                        groups.Add(g);
                    }
                    g.count++;
                    StatusOf(l, i, b, out string text, out string tone);
                    if (tone == "bad") { g.bad++; if (g.badText == null) g.badText = text; }
                    else if (tone == "ok") { g.good++; if (g.goodText == null) g.goodText = text; }
                    total++;
                }
            }
            var sites = l.sites;
            if (sites != null)
            {
                foreach (var p in sites)
                {
                    if (p == null) continue;
                    string key = p.isWall ? "wall:" + p.planId : p.planId;
                    SiteGroup g = null;
                    for (int k = 0; k < siteGroups.Count; k++)
                        if (siteGroups[k].planId == key) { g = siteGroups[k]; break; }
                    if (g == null)
                    {
                        string label = p.isWall
                            ? (BuildPlans.Named(p.planId).label ?? "wall")
                            : BuildPlans.Named(p.planId).label ?? p.planId;
                        g = new SiteGroup { planId = key, name = Cap(label), first = p };
                        siteGroups.Add(g);
                    }
                    g.count++;
                    g.progress += p.Progress01;
                    g.fill += p.Fill01;
                }
            }

            SetText(builtHead, "BUILDINGS · " + (total + siteGroups.Count));
            ReadoutUi.Show(builtNone, groups.Count == 0 && siteGroups.Count == 0);

            int used = 0;
            foreach (var g in groups)
            {
                var r = Row(used++);
                r.building = g.first; r.site = null; r.planId = g.id;
                var plan = BuildPlans.Named(g.id);
                string name = g.count > 1 ? $"{g.name} ×{g.count}" : g.name;
                string sub = null;
                string status, tone;
                if (plan.houses > 0)
                {
                    int cap = l.HousingCapacity, used2 = l.Housed;
                    sub = $"{used2} of {cap} beds used";
                    int without = Mathf.Max(0, used2 - cap);
                    status = without > 0 ? $"{without} without a bed" : "";
                    tone = without > 0 ? "bad" : "";
                }
                else if (g.bad > 0)
                {
                    status = g.bad > 1 && g.count > 1 ? $"{g.bad} × {g.badText}" : g.badText;
                    tone = "bad";
                }
                else if (g.good > 0) { status = g.goodText; tone = "ok"; }
                else { status = ""; tone = ""; }
                SetRow(r, name, sub, status, tone);
            }
            foreach (var g in siteGroups)
            {
                var r = Row(used++);
                r.building = null; r.site = g.first; r.planId = g.first.planId;
                string name = g.count > 1 ? $"{g.name} ×{g.count}" : g.name;
                float prog = g.progress / g.count, fill = g.fill / g.count;
                string status = prog > 0f
                    ? $"being built · {Mathf.RoundToInt(prog * 100f)}%"
                    : $"getting supplies · {Mathf.RoundToInt(fill * 100f)}%";
                SetRow(r, name, null, status, "info");
            }
            for (int i = used; i < buildRows.Count; i++) ReadoutUi.Show(buildRows[i].root, false);
        }

        /// One building's short status: ember (bad) for "no farmhand" and the
        /// like, moss (ok) for "working", "" for nothing to say.
        void StatusOf(OutpostLedger l, int raisedIndex, Building b, out string text, out string tone)
        {
            text = ""; tone = "";
            var plan = BuildPlans.Named(b.Id);
            if (b.Id == OutpostLedger.WatchtowerId)
            {
                bool watched = SheetBits.Lookout(l) != null;
                text = watched ? "on watch" : "nobody on watch";
                tone = watched ? "ok" : "bad";
                return;
            }
            bool production = b.Id == BuildPlans.Farm.id || Recipes.StationHasRecipes(b.Id);
            if (!production) return;

            string noOne = string.IsNullOrEmpty(plan.position) ? "no worker" : "no " + plan.position;
            var st = l.StationForRaised(raisedIndex);
            OutpostHand worker = null;
            foreach (var h in l.hands)
            {
                if (h == null || h.order != OutpostOrder.Work) continue;
                if (st != null ? l.StationOfHand(h) == st : h.target == b.Id) { worker = h; break; }
            }
            if (worker == null) { text = noOne; tone = "bad"; return; }
            if (st == null) { text = "working"; tone = "ok"; return; }   // the farm: no bench to read
            string status = BuildingStatusLabels.Status(l, st);
            if (status == "Output full") { text = "output full"; tone = "bad"; return; }
            if (status == "Needs supplies") { text = "needs supplies"; tone = "bad"; return; }
            if (!st.HasOrder && st.benchState == BenchState.Empty) { text = "no order"; tone = "bad"; return; }
            text = "working";
            tone = "ok";
        }

        BuildRow Row(int index)
        {
            while (index >= buildRows.Count) buildRows.Add(MakeBuildRow());
            var r = buildRows[index];
            ReadoutUi.Show(r.root, true);
            return r;
        }

        static void SetRow(BuildRow r, string name, string sub, string status, string tone)
        {
            if (r.lastName != name) { r.lastName = name; r.name.text = name ?? ""; }
            if (r.lastSub != sub)
            {
                r.lastSub = sub;
                r.sub.text = sub ?? "";
                ReadoutUi.Show(r.sub, !string.IsNullOrEmpty(sub));
            }
            if (r.lastStatus != status) { r.lastStatus = status; r.status.text = status ?? ""; }
            if (r.lastTone != tone)
            {
                r.lastTone = tone;
                r.status.EnableInClassList("cs-st--bad", tone == "bad");
                r.status.EnableInClassList("cs-st--ok", tone == "ok");
                r.status.EnableInClassList("cs-st--info", tone == "info");
            }
        }

        BuildRow MakeBuildRow()
        {
            var r = new BuildRow();
            r.root = new Button(() => OpenRow(r)) { text = "" };
            r.root.AddToClassList("cp-hand");
            var words = Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cp-hand-words");
            r.name = Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-hand-name");
            r.sub = Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-job");
            words.Add(r.name);
            words.Add(r.sub);
            r.root.Add(words);
            r.status = Classed(new Label { pickingMode = PickingMode.Ignore }, "cs-st");
            words.Add(r.status);
            builtHolder.Add(r.root);
            return r;
        }

        /// A tap on a building row: that building's own sheet. Buildings
        /// with no page of their own (a hut asks for the camp sheet, this
        /// one) open their plan card in the Build list instead; a blueprint
        /// opens its site sheet.
        void OpenRow(BuildRow r)
        {
            if (outpost == null) return;
            if (r.building != null)
            {
                var sheet = Sheets.TryCreateFor(r.building);
                if (sheet == null || sheet is CampSheet) sheet = CampAlerts.BuildList(outpost, r.planId);
                Open(sheet);
                return;
            }
            if (r.site != null)
            {
                ISheet sheet = null;
                foreach (var s in UnityEngine.Object.FindObjectsByType<BuildSite>(FindObjectsSortMode.None))
                    if (s != null && s.Row == r.site) { sheet = Sheets.TryCreateFor(s); break; }
                Open(sheet ?? CampAlerts.BuildList(outpost, r.planId));
            }
        }

        // --- the thumb row --------------------------------------------------------------------

        string PeopleText()
        {
            var l = L;
            return $"People {CampReadouts.Working(l)}/{CampReadouts.Total(l)}";
        }

        void OpenPeople() { if (outpost != null) Sheets.Open(new WorkersSheet(outpost)); }

        void OpenStores() { if (outpost != null) Sheets.Open(new BackpackSheet(outpost)); }

        /// What the drawer's gear opened: the pause menu (save, settings).
        void OpenSettings()
        {
            Sheets.Close();
            SeaSick.UI.Menus.GameMenus.TogglePause();
        }

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
