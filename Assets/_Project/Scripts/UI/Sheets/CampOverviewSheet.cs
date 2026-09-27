using System;
using System.Collections.Generic;
using System.Reflection;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **Camp › Overview, the goal chain (2026-09-27, Kevin's Melvor-style
    /// redesign, concept C).** What a tap on the campfire opens now: the
    /// camp's NEXT GOAL (the fire's next level), the chain of what it is
    /// waiting on -- hide 0/4 → hunting → a spear → a forge with no smith --
    /// and ONE first step with a Go button that opens the sheet that fixes
    /// it. Under it (or on a second segment when the band is short): the
    /// campfire card with its have/need tiles and the Raise button, what is
    /// being built, and today's three numbers (food, stone left, the watch).
    ///
    /// **It reads and it calls; it never decides.** The chain is
    /// `GoalChain.NextCampfire` (pure, in `World/Economy`); the Raise button
    /// is `OutpostLedger.RaiseCampfire`, exactly what `FireSheet` calls. The
    /// old camp sheet is still one tap away -- the ☰ button, and every Go
    /// that needs the build list or the hands -- until the ledger drawer's
    /// destinations replace it.
    ///
    /// Kevin's no-scroll rule: everything on one page when it fits, else a
    /// two-segment control (Goal / Today). Never a pager.
    public sealed class CampOverviewSheet : ISheetFramed
    {
        /// **The ☰ hook.** The ledger drawer sets this; while nobody has, the
        /// button looks for a `LedgerDrawer.Open()` by name and, failing
        /// that, opens the old camp sheet so nothing becomes unreachable.
        public static Action OpenLedger;

        readonly Outpost outpost;
        readonly string islandName;

        public CampOverviewSheet(Outpost camp)
        {
            outpost = camp;
            islandName = camp != null && camp.Island != null ? camp.Island.name : "the camp";
        }

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
        public VisualElement BuildActions() => null;

        static StyleSheet style;
        static StyleSheet Style => style != null ? style : (style = Resources.Load<StyleSheet>("UI/Overview"));

        static void Styled(VisualElement e)
        {
            if (Style != null) e.styleSheets.Add(Style);
        }

        Label subtitle;

        public VisualElement BuildHeader()
        {
            var head = new VisualElement();
            head.AddToClassList(SheetTheme.Head);
            Styled(head);

            var words = new VisualElement();
            words.AddToClassList("ov-head-words");
            var title = new Label($"Camp · {islandName}");
            title.AddToClassList("ov-title");
            words.Add(title);
            subtitle = new Label();
            subtitle.AddToClassList("ov-subtitle");
            words.Add(subtitle);
            head.Add(words);

            var menu = new Button(Ledger) { text = "☰", tooltip = "Ledger" };
            menu.AddToClassList("ov-head-btn");
            head.Add(menu);

            var x = new Button(() => Sheets.Close());
            x.AddToClassList(SheetTheme.Close);
            x.tooltip = "Close";
            if (MidnightLandHud.Active)
            {
                x.text = "";
                x.style.alignItems = Align.Center;
                x.style.justifyContent = Justify.Center;
                x.Add(new LandIcon("close"));
            }
            else x.text = "✕";
            head.Add(x);
            return head;
        }

        void Ledger()
        {
            if (OpenLedger != null) { OpenLedger(); return; }
            if (TryLedgerDrawer()) return;
            if (outpost != null) Sheets.Open(new FireSheet(outpost));
        }

        /// `LedgerDrawer.Open()`, if the drawer agent's type is in the
        /// assembly -- by name, so this file compiles with or without it.
        static bool TryLedgerDrawer()
        {
            var t = typeof(CampOverviewSheet).Assembly.GetType("SeaSick.UI.Sheets.LedgerDrawer")
                    ?? typeof(CampOverviewSheet).Assembly.GetType("SeaSick.UI.LedgerDrawer");
            var m = t?.GetMethod("Open", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (m == null) return false;
            m.Invoke(null, null);
            return true;
        }

        // --- the body ---------------------------------------------------------

        VisualElement rootEl, seg, goalBlock, todayBlock;
        // goal
        Label goalTitle, goalWhy, goalProgress;
        VisualElement goalBar, chainHolder, stepCard;
        Label stepTitle, stepDetail;
        Button goBtn;
        // today
        Label fireTitle, fireBlurb, fireNote;
        VisualElement tilesHolder, builtHolder, todayHolder;
        Button raiseBtn;

        GoalChain chain;
        long chainKey = long.MinValue, tilesKey = long.MinValue, builtKey = long.MinValue, todayKey = long.MinValue;
        float nextCompute;

        bool split;          // two segments, not one page
        int segment;         // 0 goal, 1 today
        float goalH, todayH; // natural heights, last measured while shown

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.AddToClassList("ov-root");
            Styled(root);
            rootEl = root;

            seg = SheetKit.Segmented(new[] { "Goal", "Today" }, segment, i =>
            {
                segment = i;
                SheetKit.SetSegmented(seg, i);
                ApplySplit();
            });
            seg.AddToClassList("ov-seg");
            root.Add(seg);

            goalBlock = BuildGoal();
            root.Add(goalBlock);
            todayBlock = BuildToday();
            root.Add(todayBlock);

            chainKey = tilesKey = builtKey = todayKey = long.MinValue;
            nextCompute = 0f;
            ApplySplit();

            root.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            goalBlock.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            todayBlock.RegisterCallback<GeometryChangedEvent>(_ => Fit());

            Refresh();
            return root;
        }

        VisualElement BuildGoal()
        {
            var block = new VisualElement();
            block.AddToClassList("ov-block");

            var head = new VisualElement();
            head.AddToClassList("ov-goal-head");
            head.Add(Classed(new Label("NEXT GOAL"), "ov-eyebrow"));
            goalTitle = Classed(new Label(), "ov-goal-title");
            head.Add(goalTitle);
            goalWhy = Classed(new Label(), "ov-goal-why");
            head.Add(goalWhy);
            block.Add(head);

            var track = Classed(new VisualElement(), "ov-bar");
            goalBar = Classed(new VisualElement(), "ov-bar-fill");
            track.Add(goalBar);
            block.Add(track);
            goalProgress = Classed(new Label(), "ov-progress");
            block.Add(goalProgress);

            chainHolder = Classed(new VisualElement(), "ov-chain");
            block.Add(chainHolder);

            stepCard = Classed(new VisualElement(), "ov-step");
            var words = Classed(new VisualElement(), "ov-step-words");
            stepTitle = Classed(new Label(), "ov-step-title");
            stepDetail = Classed(new Label(), "ov-step-detail");
            words.Add(stepTitle);
            words.Add(stepDetail);
            stepCard.Add(words);
            // Built once and re-texted: a rebuilt button loses the tap it is
            // in the middle of (DEV-TOOLS, sheet facts).
            goBtn = new Button(Go) { text = "Go" };
            goBtn.AddToClassList("ov-go");
            stepCard.Add(goBtn);
            block.Add(stepCard);
            return block;
        }

        VisualElement BuildToday()
        {
            var block = new VisualElement();
            block.AddToClassList("ov-block");

            var fire = Classed(new VisualElement(), "ov-card");
            var top = Classed(new VisualElement(), "ov-card-top");
            fireTitle = Classed(new Label(), "ov-card-title");
            top.Add(fireTitle);
            fire.Add(top);
            fireBlurb = Classed(new Label(), "ov-card-blurb");
            fire.Add(fireBlurb);
            tilesHolder = Classed(new VisualElement(), "ov-tiles");
            fire.Add(tilesHolder);
            fireNote = Classed(new Label(), "ov-card-note");
            fire.Add(fireNote);
            raiseBtn = new Button(Raise) { text = "Raise" };
            raiseBtn.AddToClassList("ov-raise");
            fire.Add(raiseBtn);
            block.Add(fire);

            block.Add(Classed(new Label("BEING BUILT"), "ov-eyebrow"));
            builtHolder = Classed(new VisualElement(), "ov-card");
            block.Add(builtHolder);

            block.Add(Classed(new Label("TODAY"), "ov-eyebrow"));
            todayHolder = Classed(new VisualElement(), "ov-card");
            block.Add(todayHolder);
            return block;
        }

        static T Classed<T>(T e, string cls) where T : VisualElement
        {
            e.AddToClassList(cls);
            return e;
        }

        // --- fit: one page, or Goal / Today --------------------------------------

        void ApplySplit()
        {
            if (seg == null) return;
            seg.style.display = split ? DisplayStyle.Flex : DisplayStyle.None;
            goalBlock.style.display = !split || segment == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            todayBlock.style.display = !split || segment == 1 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// Measured, not guessed (the `StoresSheet` lesson: `BandHeight` can
        /// disagree with the card actually laid out). Each block's natural
        /// height is remembered while it is shown; the page splits when the
        /// two together overrun the band and joins again only with a margin
        /// to spare, so it cannot flicker between the two on a one-row change.
        void Fit()
        {
            if (rootEl == null) return;
            var page = rootEl.parent;
            if (page == null) return;
            float avail = page.resolvedStyle.height;
            if (float.IsNaN(avail) || avail <= 1f) return;
            if (goalBlock.resolvedStyle.display == DisplayStyle.Flex && goalBlock.layout.height > 1f)
                goalH = goalBlock.layout.height + goalBlock.resolvedStyle.marginTop + goalBlock.resolvedStyle.marginBottom;
            if (todayBlock.resolvedStyle.display == DisplayStyle.Flex && todayBlock.layout.height > 1f)
                todayH = todayBlock.layout.height + todayBlock.resolvedStyle.marginTop + todayBlock.resolvedStyle.marginBottom;
            if (goalH <= 1f || todayH <= 1f) return;
            const float JoinMargin = 24f;
            bool want = split ? goalH + todayH > avail - JoinMargin : goalH + todayH > avail;
            if (want == split) return;
            split = want;
            // The page's question first: the goal is what the camp is for.
            if (split) segment = 0;
            SheetKit.SetSegmented(seg, segment);
            ApplySplit();
        }

        // --- refresh ---------------------------------------------------------------

        public void Refresh()
        {
            var l = L;
            if (l == null || rootEl == null) return;
            if (subtitle != null)
            {
                var cur = Techs.CampfireAt(l.CampfireLevel);
                subtitle.text = $"campfire {RecipeGraph.Roman(l.CampfireLevel)}" + (cur != null ? $" · {cur.name}" : "");
            }
            // The chain is recomputed once a second -- it walks the books,
            // and nothing on it moves faster than a hand's trip.
            float now = Time.unscaledTime;
            if (chain == null || now >= nextCompute)
            {
                nextCompute = now + 1f;
                chain = GoalChain.NextCampfire(l);
                DrawGoal(chain);
            }
            DrawFire(l);
            DrawBuilt(l);
            DrawToday(l);
        }

        void DrawGoal(GoalChain c)
        {
            long key = c.Key;
            if (key == chainKey) return;
            chainKey = key;

            goalTitle.text = c.title ?? "";
            goalWhy.text = c.why ?? "";
            float f = c.total > 0 ? (float)c.ready / c.total : 1f;
            goalBar.style.width = Length.Percent(Mathf.Clamp01(f) * 100f);
            goalProgress.text = c.HasGoal ? $"{c.ready} of {c.total} ready" : "";

            var list = new VisualElement();
            list.AddToClassList("ov-chain-list");
            foreach (var r in c.rows) list.Add(ChainRow(r));
            SheetBits.Swap(chainHolder, list);

            var s = c.firstStep;
            if (s != null)
            {
                stepCard.style.display = DisplayStyle.Flex;
                stepTitle.text = "First step: " + s.title;
                stepDetail.text = s.detail ?? "";
                goBtn.text = s.action == GoalAction.Complete ? "Raise" : "Go";
                goBtn.SetEnabled(s.action != GoalAction.None);
            }
            else if (c.HasGoal)
            {
                stepCard.style.display = DisplayStyle.Flex;
                stepTitle.text = "Nothing to tap yet";
                stepDetail.text = "the hands are on it; check back soon";
                goBtn.SetEnabled(false);
            }
            else stepCard.style.display = DisplayStyle.None;
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
                var indent = new VisualElement();
                indent.style.width = r.depth * 16f;
                indent.style.flexShrink = 0f;
                row.Add(indent);
            }
            var dot = new Label(r.state == GoalState.Ok ? "✓" : r.state == GoalState.Blocked ? "!" : "…");
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

            var words = new VisualElement();
            words.AddToClassList("ov-row-words");
            words.Add(Classed(new Label(r.name ?? ""), "ov-row-name"));
            if (!string.IsNullOrEmpty(r.how)) words.Add(Classed(new Label(r.how), "ov-row-how"));
            row.Add(words);

            row.Add(Classed(new Label(r.qty ?? ""), "ov-row-qty"));
            return row;
        }

        void DrawFire(OutpostLedger l)
        {
            var next = l.NextCampfire;
            long key = l.CampfireLevel * 1000003L + (next != null ? 1 : 0);
            if (next != null)
                foreach (var line in next.cost) key = key * 31 + l.SpendableOf(line.res);
            bool can = l.CanRaiseCampfire(out string why);
            key = key * 7 + (can ? 1 : 0);
            if (key == tilesKey) return;
            tilesKey = key;

            fireTitle.text = $"Campfire · level {RecipeGraph.Roman(l.CampfireLevel)}";
            var tiles = new VisualElement();
            tiles.AddToClassList("ov-tiles-row");
            if (next == null)
            {
                fireBlurb.text = "The fire is as high as it goes.";
                fireNote.text = "";
                fireNote.style.display = DisplayStyle.None;
                raiseBtn.style.display = DisplayStyle.None;
            }
            else
            {
                string blurb = next.blurb ?? "";
                fireBlurb.text = $"Level {RecipeGraph.Roman(next.level)} {blurb}".TrimEnd() + (blurb.EndsWith(".") ? "" : ".");
                foreach (var line in next.cost)
                {
                    int have = l.SpendableOf(line.res);
                    bool ok = have >= line.n;
                    var tile = new VisualElement();
                    tile.AddToClassList("ov-tile");
                    if (!ok) tile.AddToClassList("ov-tile--short");
                    var tex = ItemIconSet.Get(line.res);
                    if (tex != null)
                    {
                        var img = new Image { image = tex };
                        img.AddToClassList("ov-tile-icon");
                        tile.Add(img);
                    }
                    else tile.Add(Classed(new Label(ResDefs.Label(line.res)), "ov-tile-name"));
                    tile.Add(Classed(new Label($"{have}/{line.n}"), "ov-tile-q"));
                    tiles.Add(tile);
                }
                fireNote.style.display = can ? DisplayStyle.None : DisplayStyle.Flex;
                fireNote.text = can ? "" : Sentence(why);
                raiseBtn.style.display = DisplayStyle.Flex;
                raiseBtn.text = $"Raise to level {RecipeGraph.Roman(next.level)}";
                raiseBtn.SetEnabled(can);
            }
            SheetBits.Swap(tilesHolder, tiles);
        }

        void DrawBuilt(OutpostLedger l)
        {
            int builders = l.HandsOn(OutpostOrder.Build);
            long key = builders;
            var sites = l.sites;
            int n = sites != null ? sites.Count : 0;
            for (int i = 0; i < n && i < 3; i++)
            {
                var p = sites[i];
                if (p == null) continue;
                key = key * 31 + (p.planId?.GetHashCode() ?? 0) + Mathf.RoundToInt(p.Progress01 * 20f)
                      + (p.PhaseLine?.GetHashCode() ?? 0) * 7;
            }
            key = key * 131 + n;
            if (key == builtKey) return;
            builtKey = key;

            var col = new VisualElement();
            if (n == 0)
            {
                string who = builders == 0 ? "" : builders == 1 ? " One builder is waiting." : $" {builders} builders are waiting.";
                col.Add(Classed(new Label("Nothing on order." + who), "ov-line"));
            }
            else
            {
                for (int i = 0; i < n && i < 2; i++)
                {
                    var p = sites[i];
                    if (p == null) continue;
                    string label = p.isWall ? "wall" : BuildPlans.Named(p.planId).label ?? p.planId;
                    col.Add(KeyValue(Cap(label), p.PhaseLine ?? $"{Mathf.RoundToInt(p.Progress01 * 100f)}%", false));
                }
                if (n > 2) col.Add(Classed(new Label($"and {n - 2} more queued"), "ov-line-muted"));
                if (builders == 0) col.Add(KeyValue("Builders", "nobody building", true));
            }
            SheetBits.Swap(builtHolder, col);
        }

        void DrawToday(OutpostLedger l)
        {
            float days = SheetBits.FoodDays(l);
            var stone = l.Stock(Res.Stone);
            int stoneLeft = stone != null ? Mathf.FloorToInt(stone.standing) : 0;
            bool noStone = stone == null || stone.standing < 1f;
            bool watchBad = l.raiders > 0 && !l.LookoutPosted;
            string watch = l.LookoutPosted ? "lookout posted"
                : l.HasWatchtower ? "nobody on watch" : "no watchtower";
            long key = Mathf.RoundToInt(days * 10f) * 1000003L + stoneLeft * 131L
                       + (l.LookoutPosted ? 1 : 0) + (l.HasWatchtower ? 2 : 0) + (watchBad ? 4 : 0) + l.hands.Count * 8;
            if (key == todayKey) return;
            todayKey = key;

            var col = new VisualElement();
            string food = l.hands.Count == 0 ? "nobody to feed" : days < 0f ? "they eat nothing" : $"{days:0.#} days left";
            col.Add(KeyValue("Food", food, days >= 0f && days < 1.5f && l.hands.Count > 0));
            col.Add(KeyValue("Stone on the island", noStone ? "0 left" : $"{stoneLeft} left", noStone));
            col.Add(KeyValue("Watch", watch, !l.LookoutPosted && (l.HasWatchtower || l.raiders > 0)));
            SheetBits.Swap(todayHolder, col);
        }

        static VisualElement KeyValue(string k, string v, bool bad)
        {
            var row = new VisualElement();
            row.AddToClassList("ov-kv");
            row.Add(Classed(new Label(k), "ov-kv-k"));
            var val = Classed(new Label(v), "ov-kv-v");
            if (bad) val.AddToClassList("ov-kv-v--bad");
            row.Add(val);
            return row;
        }

        // --- the verbs -----------------------------------------------------------

        void Raise()
        {
            var l = L;
            if (l == null) return;
            if (l.RaiseCampfire())
            {
                chain = null;
                chainKey = tilesKey = long.MinValue;
                Refresh();
            }
        }

        /// The first step's Go: open the sheet that fixes it.
        void Go()
        {
            var s = chain != null ? chain.firstStep : null;
            var l = L;
            if (s == null || l == null || outpost == null) return;
            switch (s.action)
            {
                case GoalAction.Complete:
                    Raise();
                    break;
                case GoalAction.OpenStation:
                {
                    var b = BuiltOf(s.planId);
                    if (b != null) Sheets.Open(new StationSheet(outpost, b));
                    else OpenFire("build");
                    break;
                }
                case GoalAction.AssignHand:
                {
                    var idle = SheetBits.FirstIdle(l);
                    if (idle != null) Sheets.Open(new HandSheet(outpost, idle.name));
                    else OpenFire("hands");
                    break;
                }
                case GoalAction.Build:
                    OpenFire("build");
                    break;
            }
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

        /// The old camp sheet, at one of its sections -- the build list and
        /// the hands still live there until the drawer's pages replace them.
        void OpenFire(string section)
        {
            var fs = new FireSheet(outpost);
            fs.FocusSection(section);
            Sheets.Open(fs);
        }

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static string Sentence(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Cap(s);
            return s.EndsWith(".") ? s : s + ".";
        }
    }
}
