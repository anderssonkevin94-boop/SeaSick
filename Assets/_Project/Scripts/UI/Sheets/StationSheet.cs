using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The station page, concept A2 (Kevin approved the Melvor-style
    /// redesign, 2026-09-27).** Every production building opens ONE
    /// full-height page (`WantsTallSheet`), read top to bottom:
    ///
    /// 1. **header** -- ☰ (the Ledger drawer, `OpenLedger`), the name,
    ///    "Level N · island", and a status pill (working / waiting / no
    ///    worker / output full);
    /// 2. **the worker** -- avatar, name, role · mood; Assign / Swap /
    ///    Unassign;
    /// 3. **recipes** -- a two-column grid of cards (icon, ×yield, inputs and
    ///    time, the running order's bar and "making 7 of 10"; locked cards
    ///    dashed with the reason). A tap picks the order's recipe. More cards
    ///    than fit PAGE with dots, never a scroll;
    /// 4. **the order** -- one segmented control ∞ / 5 / 10 / 20 / Stop;
    /// 5. **in and out** -- bay → bench ring → rack of THIS building;
    /// 6. **why it's stopped** -- the ledger's `StallReason`, when it is;
    /// 7. **upgrade** -- this building's next level, its price, one button.
    ///
    /// **Sized to fit the phone's tall band without a scroll** (Kevin's
    /// no-scroll rule, 2026-09-22). The body sits in a vertical ScrollView
    /// only as a safety net: on the phone the page fits and it never moves;
    /// on the desk's shorter column it scrolls with the wheel.
    ///
    /// **It reads and it calls; it never decides.** Every number comes off
    /// `OutpostLedger` for THIS built instance (`StationForRaised`); every
    /// button is a verb the ledger or `Outpost` already had (`PlaceOrder`,
    /// `StopOrder`, `Assign`, `OrderIdle`, `UpgradeAt`).
    ///
    /// **Built once, re-texted after (1cf73f9).** `SheetHost` calls
    /// `Refresh` every 0.25 s, and a UI Toolkit click needs its press and
    /// release on the SAME element -- so nothing a finger can land on is
    /// rebuilt on that timer.
    public class StationSheet : ISheetFramed
    {
        /// **The ☰ hook.** Whoever owns the Ledger drawer sets this
        /// (`StationSheet.OpenLedger = ledgerDrawer.Open`, `MidnightLandHud`).
        /// Until it does, ☰ falls back to the camp overview's own hook, then
        /// to the camp overview sheet itself (`StationPage.OpenLedgerFor`).
        public static System.Action OpenLedger;

        readonly Outpost outpost;
        readonly Building building;
        readonly string planId;
        readonly BuildPlan plan;
        readonly bool hasMake;
        readonly bool hasUpgrade;
        readonly bool hasWorker;
        readonly bool isStore;

        /// **This built instance's index into `ledger.raised`** (and into
        /// `Outpost.Built`). Re-resolved every `Refresh`: an earlier
        /// building demolished while this page is open moves it.
        int raisedIndex = -1;

        /// The recipe a tap picked -- the order's recipe for the next amount
        /// tap. Null = whatever is running, else the station's default.
        string pickedRecipe;

        public StationSheet(Outpost o, Building b)
        {
            outpost = o;
            building = b;
            planId = b != null ? b.Id : null;
            plan = BuildPlans.Named(planId);
            hasMake = Recipes.StationHasRecipes(planId);
            hasUpgrade = Techs.MaxLevel(planId) > 1;
            hasWorker = BuildPlans.HasPosition(planId) && hasMake;
            isStore = planId == BuildPlans.Storage.id;
            ResolveRaisedIndex();
        }

        void ResolveRaisedIndex()
        {
            raisedIndex = -1;
            if (outpost == null || building == null) return;
            var built = outpost.Built;
            for (int i = 0; i < built.Count; i++)
                if (built[i] == building) { raisedIndex = i; break; }
        }

        /// **This building's own level** (per building since 2026-09-27).
        int MyLevel(OutpostLedger l) => l != null ? l.LevelAtRaised(raisedIndex, planId) : 1;

        /// The one `StationStock` this page is about, or null.
        StationStock Station(OutpostLedger l) =>
            l != null && raisedIndex >= 0 ? l.StationForRaised(raisedIndex) : null;

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        /// The retired dropdown layout's flag; `SheetHost.FrameSizeScreen`
        /// still asks. Always false: the page is a tall sheet now.
        internal bool ProductionLayout => false;

        // --- the frame -------------------------------------------------------

        public Color Accent => SheetTheme.Timber;
        public string Title => StationPage.Cap(plan.label);
        public bool WantsTallSheet => true;

        public Vector3 AnchorWorld => building != null
            ? building.transform.position
            : (outpost != null ? outpost.CampCentre : Vector3.zero);

        public bool StillValid => outpost != null && outpost.Ledger != null && building != null;

        // One page: no host tab strip, no host action row (the upgrade is a
        // card on the page, the order is the segmented control).
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public VisualElement BuildActions() => null;

        StationPage.Header header;

        public VisualElement BuildHeader()
        {
            header = new StationPage.Header(Title, hasMake, () => StationPage.OpenLedgerFor(outpost));
            return header.Root;
        }

        // --- the thumb floor, shared ----------------------------------------

        /// The old sheets' thumb floor in panel units -- `FireSheet` still
        /// reads it.
        internal const float TouchPx = 44f;

        // --- the pieces kept between refreshes --------------------------------

        VisualElement root;
        StationPage.WorkerCard worker;
        StationPage.UpgradeCard upgrade;

        // recipes
        sealed class RecipeCard
        {
            public Recipe r;
            public VisualElement root, icon, barHolder, bar;
            public Label name, yield, state;
            public Label[] takes;
            public Label time;
            public StationPage.DashedFrame dashes;
            public bool locked;
        }
        readonly List<RecipeCard> cards = new List<RecipeCard>();
        int perPage = 2, recipePage;
        VisualElement dots;
        Label orderEyebrow;

        // the order
        static readonly int[] Amounts = { OutpostLedger.RepeatOrder, 5, 10, 20 };
        readonly Button[] amountBtns = new Button[Amounts.Length];
        Button stopBtn;

        /// The size of each station's running order as it was placed, so a
        /// card can say "making 7 of 10" -- the ledger keeps only what is
        /// left. Best effort: an order placed elsewhere is sized on sight.
        static readonly Dictionary<StationStock, int> OrderTotals = new Dictionary<StationStock, int>();

        // in and out
        VisualElement bayIcon, rackIcon;
        Label bayValue, benchValue, benchLabel, rackValue;
        StationPage.BenchRing ring;
        Label stallLine;

        // the store
        Label[] storeLines;
        const int StoreLinesMax = 5;

        public VisualElement Build()
        {
            cards.Clear();
            storeLines = null; stallLine = null; worker = null; upgrade = null;

            root = StationPage.Root("st-page");
            // The host pins content at its natural height (flex-shrink 0),
            // so the page asks for exactly the band -- the scroll view
            // inside then has a bounded viewport to (almost never) scroll.
            StationPage.FitToParent(root);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("st-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            root.Add(scroll);
            var col = new VisualElement();
            col.AddToClassList("st-content");
            scroll.Add(col);

            bool first = true;
            VisualElement Section(string eyebrow, out Label eyebrowLabel)
            {
                var s = new VisualElement();
                s.AddToClassList("st-section");
                if (first) s.style.marginTop = 0f;
                first = false;
                eyebrowLabel = null;
                if (eyebrow != null)
                {
                    eyebrowLabel = StationPage.Text(eyebrow, "st-eyebrow");
                    s.Add(eyebrowLabel);
                }
                col.Add(s);
                return s;
            }

            if (hasWorker)
            {
                var s = Section(null, out _);
                worker = new StationPage.WorkerCard(outpost, planId, () => Refresh());
                s.Add(worker.Root);
            }

            if (isStore)
            {
                var s = Section("ON THE RACKS", out _);
                var card = StationPage.Card();
                var lines = new VisualElement(); lines.AddToClassList("st-col");
                card.Add(lines);
                storeLines = new Label[StoreLinesMax];
                for (int i = 0; i < storeLines.Length; i++)
                {
                    storeLines[i] = StationPage.Text("", "st-line");
                    lines.Add(storeLines[i]);
                }
                s.Add(card);
            }

            if (hasMake)
            {
                BuildRecipes(Section("RECIPES", out _));
                BuildOrder(Section("ORDER", out orderEyebrow));
                BuildFlow(Section("IN AND OUT", out _));
            }

            if (hasUpgrade)
            {
                upgrade = new StationPage.UpgradeCard(DoUpgrade, TogglePin);
                if (first) upgrade.Root.style.marginTop = 0f;
                col.Add(upgrade.Root);
            }

            Refresh();
            return root;
        }

        // --- 3. recipes -------------------------------------------------------

        void BuildRecipes(VisualElement s)
        {
            var recipes = Recipes.At(planId);
            perPage = PerPage(recipes.Count);
            var grid = new VisualElement();
            grid.AddToClassList("st-grid");
            s.Add(grid);
            for (int i = 0; i < recipes.Count; i++)
            {
                var c = BuildCard(recipes[i]);
                if (i % perPage >= 2) c.root.AddToClassList("st-recipe--row2");
                cards.Add(c);
                grid.Add(c.root);
            }

            dots = null;
            int pages = Mathf.CeilToInt(recipes.Count / (float)perPage);
            if (pages > 1)
            {
                dots = new VisualElement();
                dots.AddToClassList("st-dots");
                for (int p = 0; p < pages; p++)
                {
                    int page = p;
                    var b = new Button(() => { recipePage = page; ShowRecipePage(); });
                    b.AddToClassList("st-dot-btn");
                    b.text = "";
                    var dot = new VisualElement();
                    dot.AddToClassList("st-dot");
                    dot.pickingMode = PickingMode.Ignore;
                    b.Add(dot);
                    dots.Add(b);
                }
                s.Add(dots);
            }

            // Open on the page that holds what is running.
            var l = L;
            var st = Station(l);
            var order = st != null && l != null ? l.OrderAt(planId, st.ordinal) : default;
            recipePage = 0;
            if (order.Active && order.recipe != null)
                for (int i = 0; i < cards.Count; i++)
                    if (cards[i].r.id == order.recipe.id) recipePage = i / perPage;
            ShowRecipePage();
        }

        /// **Two cards a page, or four when two rows fit the band.** Four is
        /// the design (the forge's five page as 4 + 1), but on a phone the
        /// second row costs ~230 units the tall band does not have with the
        /// order, flow and upgrade below it, so the grid pages at two there.
        /// Worked out from the band BEFORE building -- a page never re-plans
        /// under a finger.
        static int PerPage(int count)
        {
            if (count <= 2) return 2;
            float frame = SheetHost.FrameSizeScreen().y * SheetHost.PanelScale;
            float avail = frame - SheetHost.BorderPx - 82f - SheetHost.BodyPadPx - 22f;
            const float Rest = 575f, CardRow = 232f;
            return avail >= Rest + 2f * CardRow + 12f ? 4 : 2;
        }

        void ShowRecipePage()
        {
            for (int i = 0; i < cards.Count; i++)
                cards[i].root.style.display = i / perPage == recipePage ? DisplayStyle.Flex : DisplayStyle.None;
            if (dots != null)
                for (int p = 0; p < dots.childCount; p++)
                    dots[p][0].EnableInClassList("st-dot--on", p == recipePage);
        }

        RecipeCard BuildCard(Recipe r)
        {
            var c = new RecipeCard { r = r };
            c.root = new VisualElement();
            c.root.AddToClassList("st-recipe");

            var tile = new VisualElement(); tile.AddToClassList("st-tile");
            c.icon = StationPage.Icon(r.makes, "st-tile-icon");
            tile.Add(c.icon);
            c.root.Add(tile);

            var nameRow = new VisualElement(); nameRow.AddToClassList("st-name-row");
            c.name = StationPage.Text(StationPage.Cap(r.label), "st-recipe-name");
            c.yield = StationPage.Text($"×{Mathf.Max(1, r.yield)}", "st-yield");
            nameRow.Add(c.name); nameRow.Add(c.yield);
            c.root.Add(nameRow);

            var takes = new VisualElement(); takes.AddToClassList("st-takes");
            c.takes = new Label[r.takes.Length];
            for (int i = 0; i < r.takes.Length; i++)
            {
                takes.Add(StationPage.Icon(r.takes[i].res, "st-small-icon"));
                c.takes[i] = StationPage.Text($"{r.takes[i].n} {ResDefs.Label(r.takes[i].res)}", "st-takes-text");
                takes.Add(c.takes[i]);
            }
            c.time = StationPage.Text("", "st-takes-text");
            takes.Add(c.time);
            c.root.Add(takes);

            c.barHolder = new VisualElement(); c.barHolder.AddToClassList("st-bar");
            c.bar = new VisualElement(); c.bar.AddToClassList("st-bar-fill");
            c.barHolder.Add(c.bar);
            c.root.Add(c.barHolder);

            c.state = StationPage.Text("", "st-recipe-state");
            c.root.Add(c.state);

            // UI Toolkit has no dashed border: a locked card draws its own.
            c.dashes = new StationPage.DashedFrame(18f, 2f, StationPage.Edge);
            c.dashes.style.display = DisplayStyle.None;
            c.root.Add(c.dashes);

            // Registered once, gated on `c.locked` read at the tap.
            c.root.RegisterCallback<ClickEvent>(_ => PickRecipe(c));
            return c;
        }

        void PickRecipe(RecipeCard c)
        {
            if (c.locked) return;
            var l = L;
            ResolveRaisedIndex();
            var st = Station(l);
            pickedRecipe = c.r.id;
            // A running order switches recipe at once, keeping its amount:
            // the tap IS the order's recipe (the mockup's one decision).
            if (st != null && l != null)
            {
                var order = l.OrderAt(planId, st.ordinal);
                if (order.Active && order.recipe != null && order.recipe.id != c.r.id)
                {
                    int count = order.repeat ? OutpostLedger.RepeatOrder : order.remaining;
                    if (l.PlaceOrder(planId, c.r.id, count, st.ordinal)) OrderTotals[st] = count;
                }
            }
            Refresh();
        }

        /// "needs campfire II and a saw blade", or null when the recipe can
        /// be ordered HERE. This building's own level gates its level-2
        /// recipes (`PlaceOrder` refuses them the same way).
        string LockWhy(OutpostLedger l, Recipe r)
        {
            bool ok = l.RecipeAvailable(r, out string why);
            bool levelShort = MyLevel(l) < r.stationLevel;
            if (ok && !levelShort) return null;
            var parts = new List<string>(3);
            if (l.CampfireLevel < r.campfireLevel) parts.Add("campfire " + RecipeGraph.Roman(r.campfireLevel));
            if (levelShort) parts.Add($"level {r.stationLevel}");
            if (r.tool != null && l.CountOf(r.tool) <= 0) parts.Add("a " + ResDefs.Label(r.tool));
            return parts.Count > 0 ? "needs " + string.Join(" and ", parts) : why;
        }

        /// One batch's time on the bench at THIS building's level, from the
        /// same numbers the ledger works with (`ratePerDay` × level, a
        /// `TimeOfDay.DayLength` day).
        string BatchTime(Recipe r, int level)
        {
            float rate = r.ratePerDay * Techs.RateMul(planId, level);
            if (rate <= 0f) return "";
            float secs = TimeOfDay.DayLength * Mathf.Max(1, r.yield) / rate;
            return secs < 90f ? $"· {Mathf.RoundToInt(secs)} s" : $"· {secs / 60f:0.#} min";
        }

        string SelectedId(OutpostLedger l, StationStock st)
        {
            if (pickedRecipe != null)
                foreach (var c in cards) if (c.r.id == pickedRecipe) return pickedRecipe;
            if (st != null)
            {
                var order = l.OrderAt(planId, st.ordinal);
                if (order.Active && order.recipe != null) return order.recipe.id;
            }
            var feed = Feeding(l, st);
            if (feed != null) return feed.id;
            return cards.Count > 0 ? cards[0].r.id : null;
        }

        void FillRecipes(OutpostLedger l, StationStock st, OutpostHand hand, string selected)
        {
            if (cards.Count == 0) return;
            int level = MyLevel(l);
            var order = st != null ? l.OrderAt(planId, st.ordinal) : default;
            string stall = hand != null ? l.StallReason(hand) : null;
            foreach (var c in cards)
            {
                var r = c.r;
                string lockWhy = LockWhy(l, r);
                c.locked = lockWhy != null;
                bool active = order.Active && order.recipe != null && order.recipe.id == r.id;
                bool picked = !c.locked && selected == r.id;

                c.root.EnableInClassList("st-recipe--locked", c.locked);
                c.root.EnableInClassList("st-recipe--on", picked);
                c.dashes.style.display = c.locked ? DisplayStyle.Flex : DisplayStyle.None;
                c.time.text = BatchTime(r, level)
                    + (FoodBook.IsDish(r.makes) ? $" · fill {FoodBook.Fill(r.makes):0.##}" : "");
                for (int ti = 0; ti < c.takes.Length && ti < r.takes.Length; ti++)
                {
                    var line = r.takes[ti];
                    int have = l.StoreCountOf(line.res) + (st != null ? st.BayCount(line.res) : 0);
                    c.takes[ti].text = $"{line.n} {ResDefs.Label(line.res)} ({have})";
                    c.takes[ti].EnableInClassList("st-state--wait", have < line.n);
                }

                c.state.RemoveFromClassList("st-state--make");
                c.state.RemoveFromClassList("st-state--wait");
                c.state.RemoveFromClassList("st-state--lock");
                c.barHolder.style.visibility = active && !c.locked ? Visibility.Visible : Visibility.Hidden;

                if (c.locked)
                {
                    c.state.text = lockWhy;
                    c.state.AddToClassList("st-state--lock");
                }
                else if (active)
                {
                    float p = st.BenchRecipe == r
                        ? (st.benchState == BenchState.Finished ? 1f : Mathf.Clamp01(st.benchProgress)) : 0f;
                    c.bar.style.width = Length.Percent(p * 100f);
                    if (hand == null)
                    {
                        c.state.text = "waiting for a worker";
                        c.state.AddToClassList("st-state--wait");
                    }
                    else if (!string.IsNullOrEmpty(stall) && st.benchState != BenchState.Working)
                    {
                        c.state.text = stall;
                        c.state.AddToClassList("st-state--wait");
                    }
                    else
                    {
                        c.state.text = MakingText(st, order, r);
                        c.state.AddToClassList("st-state--make");
                    }
                }
                else c.state.text = picked ? "pick an amount below" : "tap to make";
            }
        }

        /// "making 7 of 10" off the order's size as placed and what is left
        /// (`orderLeft` counts OUTPUT units, a batch at a time).
        static string MakingText(StationStock st, StationOrder order, Recipe r)
        {
            if (order.repeat) return "making · until stopped";
            if (!OrderTotals.TryGetValue(st, out int total) || total < order.remaining || total <= 0)
                OrderTotals[st] = total = order.remaining;
            int made = total - order.remaining;
            int now = Mathf.Min(total, made + 1);
            return $"making {now} of {total}";
        }

        // --- 4. the order -------------------------------------------------------

        void BuildOrder(VisualElement s)
        {
            var seg = new VisualElement();
            seg.AddToClassList("st-seg");
            for (int i = 0; i < Amounts.Length; i++)
            {
                int count = Amounts[i];
                var b = new Button(() => PlaceAmount(count));
                b.text = count < 0 ? "∞" : count.ToString();
                b.AddToClassList("st-seg-btn");
                if (i == 0) b.AddToClassList("st-seg-btn--first");
                if (count < 0) b.AddToClassList("st-seg-btn--inf");
                amountBtns[i] = b;
                seg.Add(b);
            }
            stopBtn = new Button(() =>
            {
                var l = L;
                ResolveRaisedIndex();
                var st = Station(l);
                if (st == null) return;
                l.StopOrder(planId, st.ordinal);
                OrderTotals.Remove(st);
                Refresh();
            }) { text = "Stop" };
            stopBtn.AddToClassList("st-seg-btn");
            stopBtn.AddToClassList("st-seg-btn--stop");
            seg.Add(stopBtn);
            s.Add(seg);

            // **Keep in stock (2026-09-27, food rework phase 2)**: queue the
            // selected recipe as "keep 10"; the queue below is worked
            // top-down, each line with its own N and status.
            keepBtn = new Button(QueueKeep) { text = "Keep 10 in stock" };
            keepBtn.AddToClassList("st-seg-btn");
            keepBtn.style.marginTop = 8f;
            keepBtn.style.minHeight = TouchPx;
            s.Add(keepBtn);
            queueEyebrow = StationPage.Text("STANDING ORDERS · TOP-DOWN", "st-eyebrow");
            queueEyebrow.style.marginTop = 12f;
            s.Add(queueEyebrow);
            queueList = new VisualElement();
            s.Add(queueList);
        }

        Button keepBtn;
        Label queueEyebrow;
        VisualElement queueList;
        long queueKey = long.MinValue;

        void QueueKeep()
        {
            var l = L;
            ResolveRaisedIndex();
            var st = Station(l);
            if (st == null || l == null) return;
            string id = SelectedId(l, st);
            if (id == null) return;
            l.QueueOrder(StationIndexOf(l, st), id, OrderMode.Keep, 10);
            queueKey = long.MinValue;
            Refresh();
        }

        int StationIndexOf(OutpostLedger l, StationStock st) =>
            l != null && st != null && l.Stations is IList<StationStock> list ? list.IndexOf(st) : -1;

        void FillQueue(OutpostLedger l, StationStock st, string selected)
        {
            if (queueList == null) return;
            int si = StationIndexOf(l, st);
            var q = si >= 0 ? l.QueueAt(si) : null;
            int slots = si >= 0 ? l.QueueSlots(si) : 0;
            var sel = Recipes.Named(selected);
            bool canKeep = si >= 0 && sel != null && LockWhy(l, sel) == null
                && (q.Count < slots || HasLine(q, selected));
            keepBtn.SetEnabled(canKeep);
            queueEyebrow.text = $"STANDING ORDERS · {(q != null ? q.Count : 0)}/{slots} · TOP-DOWN";

            long key = si * 7919L + slots;
            var status = new List<string>();
            if (q != null)
                for (int i = 0; i < q.Count; i++)
                {
                    string line = l.QueueStatus(si, i);
                    status.Add(line);
                    key = key * 31 + q[i].recipe.GetHashCode();
                    key = key * 31 + q[i].n * 3 + (int)q[i].mode;
                    key = key * 31 + line.GetHashCode();
                }
            if (key == queueKey) return;
            queueKey = key;
            queueList.Clear();
            if (q == null || q.Count == 0)
            {
                queueList.Add(StationPage.Text("no standing orders", "st-line"));
                return;
            }
            for (int i = 0; i < q.Count; i++)
            {
                int idx = i;
                var o = q[i];
                var r = Recipes.Named(o.recipe);
                var card = StationPage.Card();
                card.style.marginTop = 6f;
                card.style.alignItems = Align.Center;
                card.Add(StationPage.Icon(r?.makes, "st-small-icon"));
                var colL = new VisualElement(); colL.AddToClassList("st-col");
                colL.style.flexGrow = 1f;
                string mode = o.mode == OrderMode.Keep ? $"Keep {o.n}"
                    : o.mode == OrderMode.Repeat ? "Repeat" : $"Make {o.n}";
                colL.Add(StationPage.Text($"{i + 1}. {mode} {r?.label ?? o.recipe}", "st-line"));
                var st2 = StationPage.Text(status[i], "st-line");
                st2.AddToClassList("st-muted");
                colL.Add(st2);
                card.Add(colL);
                if (o.mode != OrderMode.Repeat)
                {
                    card.Add(SmallBtn("−", () => { l.SetQueuedN(si, idx, o.n - (o.n > 10 ? 5 : 1)); queueKey = long.MinValue; Refresh(); }));
                    card.Add(SmallBtn("+", () => { l.SetQueuedN(si, idx, o.n + (o.n >= 10 ? 5 : 1)); queueKey = long.MinValue; Refresh(); }));
                }
                if (i > 0) card.Add(SmallBtn("↑", () => { l.MoveQueued(si, idx, -1); queueKey = long.MinValue; Refresh(); }));
                card.Add(SmallBtn("✕", () => { l.UnqueueOrder(si, idx); queueKey = long.MinValue; Refresh(); }));
                queueList.Add(card);
            }
        }

        static bool HasLine(IReadOnlyList<QueuedOrder> q, string id)
        {
            if (q == null) return false;
            foreach (var o in q) if (o.recipe == id) return true;
            return false;
        }

        static Button SmallBtn(string text, System.Action act)
        {
            var b = new Button(act) { text = text };
            b.AddToClassList("st-seg-btn");
            b.style.minWidth = TouchPx;
            b.style.minHeight = TouchPx;
            b.style.flexGrow = 0f;
            b.style.marginLeft = 4f;
            return b;
        }

        void PlaceAmount(int count)
        {
            var l = L;
            ResolveRaisedIndex();
            var st = Station(l);
            if (st == null) return;
            string id = SelectedId(l, st);
            if (id == null) return;
            if (l.PlaceOrder(planId, id, count, st.ordinal)) OrderTotals[st] = count;
            Refresh();
        }

        void FillOrder(OutpostLedger l, StationStock st, string selected)
        {
            if (stopBtn == null) return;
            var sel = Recipes.Named(selected);
            orderEyebrow.text = sel != null ? "ORDER · " + sel.label.ToUpperInvariant() : "ORDER";
            var order = st != null ? l.OrderAt(planId, st.ordinal) : default;
            bool active = order.Active && order.recipe != null;
            bool canOrder = st != null && sel != null && LockWhy(l, sel) == null;

            int on = -1;
            if (active && order.recipe.id == selected)
            {
                if (order.repeat) on = 0;
                else if (OrderTotals.TryGetValue(st, out int total))
                    for (int i = 1; i < Amounts.Length; i++) if (Amounts[i] == total) on = i;
            }
            for (int i = 0; i < amountBtns.Length; i++)
            {
                amountBtns[i].EnableInClassList("st-seg-btn--on", i == on);
                amountBtns[i].SetEnabled(canOrder);
            }
            stopBtn.SetEnabled(active);
        }

        // --- 5. in and out ------------------------------------------------------

        void BuildFlow(VisualElement s)
        {
            var card = StationPage.Card();
            card.AddToClassList("st-flow");

            VisualElement Cell()
            {
                var cell = new VisualElement(); cell.AddToClassList("st-flow-cell");
                card.Add(cell);
                return cell;
            }
            void Arrow() => card.Add(new StationPage.Glyph("arrow", StationPage.Dim, "st-arrow"));

            var bay = Cell();
            bayIcon = StationPage.Icon(null, "st-flow-icon"); bay.Add(bayIcon);
            bayValue = StationPage.Text("", "st-flow-value"); bay.Add(bayValue);
            bay.Add(StationPage.Text("waiting", "st-flow-label"));
            Arrow();
            var bench = Cell();
            ring = new StationPage.BenchRing(); bench.Add(ring);
            benchValue = StationPage.Text("", "st-flow-value"); bench.Add(benchValue);
            benchLabel = StationPage.Text("on the bench", "st-flow-label"); bench.Add(benchLabel);
            Arrow();
            var rack = Cell();
            rackIcon = StationPage.Icon(null, "st-flow-icon"); rack.Add(rackIcon);
            rackValue = StationPage.Text("", "st-flow-value"); rack.Add(rackValue);
            rack.Add(StationPage.Text("finished", "st-flow-label"));
            s.Add(card);

            stallLine = StationPage.Text("", "st-stall");
            s.Add(stallLine);
        }

        /// The recipe the bay is being filled for: the order's, else what is
        /// on the bench, else what the station would make by default.
        Recipe Feeding(OutpostLedger l, StationStock s) =>
            (s != null ? (s.OrderRecipe ?? s.BenchRecipe) : null) ?? l.RecipeAt(planId);

        void FillFlow(OutpostLedger l, StationStock st, OutpostHand hand, string selected)
        {
            if (bayValue == null) return;
            var r = (st != null ? (st.BenchRecipe ?? st.OrderRecipe) : null) ?? Recipes.Named(selected) ?? Feeding(l, st);
            string input = r != null && r.takes.Length > 0 ? r.takes[0].res : null;
            string output = st?.BenchMakes ?? r?.makes;
            StationPage.SetIcon(bayIcon, input);
            StationPage.SetIcon(rackIcon, output);

            if (st == null)
            {
                bayValue.text = rackValue.text = benchValue.text = "--";
                ring.Value = 0f;
                stallLine.style.display = DisplayStyle.None;
                return;
            }
            if (r != null && r.takes.Length > 1)
            {
                int held = 0;
                foreach (var t in r.takes) held += st.BayCount(t.res);
                bayValue.text = $"{held} / {st.InputCap * r.takes.Length}";
            }
            else bayValue.text = input != null ? $"{st.BayCount(input)} / {st.InputCap}" : "--";

            float p = st.benchState == BenchState.Finished ? 1f
                    : st.benchState == BenchState.Empty ? 0f : Mathf.Clamp01(st.benchProgress);
            ring.Value = p;
            benchValue.text = st.benchState == BenchState.Empty ? "empty" : $"{Mathf.RoundToInt(p * 100f)}%";
            benchLabel.text = st.benchState == BenchState.Finished ? "done, to the rack" : "on the bench";
            rackValue.text = $"{st.RackTotal} / {st.OutputCap}";
            rackValue.EnableInClassList("st-cost--short", st.RackFull);

            string why = StallText(l, hand, l.OrderAt(planId, st.ordinal).Active);
            stallLine.text = why;
            stallLine.style.display = string.IsNullOrEmpty(why) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// The ledger's `StallReason` for the hand on it; with nobody on it,
        /// an order that is waiting for a hand says so. Empty when working.
        internal static string StallText(OutpostLedger l, OutpostHand hand, bool wantsHand)
        {
            if (hand != null)
            {
                string why = l.StallReason(hand);
                return string.IsNullOrEmpty(why) ? "" : "stopped · " + why;
            }
            return wantsHand ? "stopped · nobody working it" : "";
        }

        // --- the store's racks -----------------------------------------------------

        readonly List<string> held = new List<string>();

        void FillStore(OutpostLedger l)
        {
            if (storeLines == null) return;
            held.Clear();
            foreach (var s in l.stores)
                if (s != null && s.whole > 0 && !string.IsNullOrEmpty(s.resource)) held.Add(s.resource);
            for (int i = 0; i < storeLines.Length; i++)
            {
                int a = i * 2, b = a + 1;
                string text = "";
                if (a < held.Count) text = StoreBit(l, held[a]);
                if (b < held.Count) text += "   ·   " + StoreBit(l, held[b]);
                if (i == storeLines.Length - 1 && held.Count > storeLines.Length * 2)
                    text = $"{StoreBit(l, held[a])} · and {held.Count - a - 1} more kinds";
                if (i == 0 && held.Count == 0) text = "the store is empty";
                storeLines[i].text = text;
                storeLines[i].style.display = text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        static string StoreBit(OutpostLedger l, string res) =>
            $"{ResDefs.Label(res)} {l.StoreCountOf(res)}/{l.ceilingPer}";

        // --- refresh ------------------------------------------------------------------

        public void Refresh()
        {
            var l = L;
            if (outpost == null || l == null || root == null) return;
            outpost.CatchUp();
            ResolveRaisedIndex();
            if (building != null && raisedIndex < 0) return;   // a frame gap around a demolish

            var st = Station(l);
            var hand = HandOn(l, st);
            string selected = hasMake ? SelectedId(l, st) : null;

            if (header != null)
            {
                header.SetSub($"Level {MyLevel(l)} · {StationPage.IslandName(outpost)}");
                if (hasMake)
                {
                    var (text, kind) = Pill(l, st, hand);
                    header.SetPill(text, kind);
                }
                header.Refresh();
            }
            worker?.Update(l, hand, 0);
            FillRecipes(l, st, hand, selected);
            FillOrder(l, st, selected);
            FillQueue(l, st, selected);
            FillFlow(l, st, hand, selected);
            FillStore(l);
            upgrade?.Update(l, raisedIndex, planId, MyLevel(l), GoalPin.IsUpgradePinned(outpost, raisedIndex, planId));
        }

        /// The header's word for this building right now.
        static (string, int) Pill(OutpostLedger l, StationStock st, OutpostHand hand)
        {
            if (st == null) return ("--", StationPage.PillWait);
            if (hand == null) return ("no worker", StationPage.PillBad);
            if (st.RackFull) return ("output full", StationPage.PillBad);
            if (st.benchState == BenchState.Working) return ("working", StationPage.PillGood);
            if (!string.IsNullOrEmpty(l.StallReason(hand))) return ("waiting", StationPage.PillWait);
            if (!st.HasOrder && st.benchState == BenchState.Empty) return ("idle", StationPage.PillWait);
            return ("working", StationPage.PillGood);
        }

        // --- 7. upgrade ------------------------------------------------------------------

        void DoUpgrade()
        {
            var l = L;
            if (l == null) return;
            ResolveRaisedIndex();
            // THIS building goes up, and only it (2026-09-27: "they have
            // their own levels, always").
            if (l.UpgradeAt(raisedIndex, planId))
            {
                // The building on the ground changes now, not on the next
                // reload -- see `Outpost.Retint`/`BuildingLevelLook`.
                outpost?.Retint(building);
                Refresh();
            }
        }

        /// "Set as goal": this building's next level becomes the camp's
        /// goal (the overview and the goal bar chase it); a second tap
        /// clears it back to the fire.
        void TogglePin()
        {
            var l = L;
            if (l == null) return;
            ResolveRaisedIndex();
            if (GoalPin.IsUpgradePinned(outpost, raisedIndex, planId)) GoalPin.Clear(outpost);
            else GoalPin.SetUpgrade(outpost, raisedIndex, planId);
            Refresh();
        }

        // --- shared ----------------------------------------------------------------------

        /// The Work hand the ledger has dealt to THIS built instance --
        /// `StationOfHand` read backwards.
        static OutpostHand HandOn(OutpostLedger l, StationStock station)
        {
            if (l == null || station == null || l.hands == null) return null;
            foreach (var h in l.hands)
            {
                if (h == null || h.order != OutpostOrder.Work || h.target != station.planId) continue;
                if (l.StationOfHand(h) == station) return h;
            }
            return null;
        }

        /// "boards 0/1" -- one ingredient, green when the pile covers it and
        /// red when it does not. Shared with `FireSheet`'s fire level.
        internal static VisualElement IngredientLine(OutpostLedger l, string res, int need)
        {
            int have = l != null ? l.CountOf(res) : 0;
            var lab = SheetKit.Text($"{ResDefs.Label(res)} {have}/{need}", false, false, 12f);
            lab.style.color = have >= need ? SheetTheme.Moss : SheetTheme.Ember;
            return lab;
        }
    }

    /// **The station page's kit** -- the pieces `StationSheet` and
    /// `FarmSheet` share: the header, the worker card, the upgrade card,
    /// item icons and the three drawn glyphs. Styles live in
    /// `Resources/UI/Station.uss`, attached to each root this hands out.
    internal static class StationPage
    {
        public const int PillGood = 0, PillWait = 1, PillBad = 2;

        public static readonly Color Edge = new Color32(44, 74, 94, 255);
        public static readonly Color Dim = new Color32(110, 132, 148, 255);
        public static readonly Color Ink = new Color32(232, 242, 246, 255);
        public static readonly Color Mint = new Color32(159, 224, 194, 255);
        public static readonly Color Track = new Color32(30, 51, 68, 255);

        static StyleSheet sheet;
        static bool loaded;

        static StyleSheet Sheet
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    sheet = Resources.Load<StyleSheet>("UI/Station");
                    if (sheet == null) Debug.LogWarning("[Sheets] Resources/UI/Station.uss is missing — the station page will be unstyled.");
                }
                return sheet;
            }
        }

        /// A root the page's styles reach: `.st` plus its own class.
        public static VisualElement Root(string cls)
        {
            var e = new VisualElement();
            e.AddToClassList("st");
            e.AddToClassList(cls);
            if (Sheet != null) e.styleSheets.Add(Sheet);
            return e;
        }

        /// The page asks for exactly the host's band: 100 % first, then the
        /// band's measured height once it is laid out (a percentage of a
        /// flex-grown parent is not always resolved), so the scroll view
        /// inside has a bounded viewport rather than its content's height.
        public static void FitToParent(VisualElement page)
        {
            page.style.height = Length.Percent(100);
            page.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                var parent = page.parent;
                if (parent == null) return;
                void Fit()
                {
                    float h = parent.contentRect.height;
                    if (h > 1f && Mathf.Abs(page.resolvedStyle.height - h) > 0.5f) page.style.height = h;
                }
                parent.RegisterCallback<GeometryChangedEvent>(__ => Fit());
                Fit();
            });
        }

        public static Label Text(string text, string cls)
        {
            var l = new Label(text ?? "");
            l.AddToClassList(cls);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        public static VisualElement Card()
        {
            var c = new VisualElement();
            c.AddToClassList("st-card");
            return c;
        }

        /// An item icon from `ItemIconSet` (the Stores bank's own PNGs), or
        /// an empty square of the same size when there is none.
        public static VisualElement Icon(string res, string cls)
        {
            var e = new VisualElement();
            e.AddToClassList(cls);
            e.pickingMode = PickingMode.Ignore;
            SetIcon(e, res);
            return e;
        }

        /// Re-points an icon only when the item changes, so the 0.25 s
        /// refresh does not re-set a background four times a second.
        public static void SetIcon(VisualElement e, string res)
        {
            if (e == null || Equals(e.userData, res)) return;
            e.userData = res;
            var tex = res != null ? ItemIconSet.Get(res) : null;
            e.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
        }

        public static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// ☰: the drawer when its owner has wired the hook, else the camp
        /// overview (the drawer's own first row) -- never a dead button.
        public static void OpenLedgerFor(Outpost camp)
        {
            if (StationSheet.OpenLedger != null) { StationSheet.OpenLedger(); return; }
            if (CampOverviewSheet.OpenLedger != null) { CampOverviewSheet.OpenLedger(); return; }
            var overview = camp != null ? LedgerDrawer.CampOverview(camp) : null;
            if (overview != null) Sheets.Open(overview);
        }

        public static string IslandName(Outpost o)
        {
            if (o == null || o.Island == null) return "";
            return o.Island.IsHome ? "home island" : o.Island.gameObject.name;
        }

        // --- the header ----------------------------------------------------

        /// ☰ · name + "Level N · island" · status pill · close.
        public sealed class Header
        {
            public readonly VisualElement Root;
            readonly Label sub;
            readonly VisualElement pill;
            readonly Label pillText;
            readonly Button menu;
            int pillKind = -1;

            public Header(string title, bool withPill, System.Action onMenu)
            {
                Root = StationPage.Root("st-head");

                menu = new Button(() => onMenu?.Invoke()) { text = "" };
                menu.AddToClassList("st-square");
                menu.tooltip = "Open the ledger";
                menu.Add(new Glyph("menu", Ink, "st-glyph"));
                Root.Add(menu);

                var words = new VisualElement(); words.AddToClassList("st-head-words");
                words.Add(Text(title, "st-title"));
                sub = Text("", "st-sub");
                words.Add(sub);
                Root.Add(words);

                pill = new VisualElement(); pill.AddToClassList("st-pill");
                pill.pickingMode = PickingMode.Ignore;
                pillText = Text("", "st-pill-text");
                pill.Add(pillText);
                pill.style.display = withPill ? DisplayStyle.Flex : DisplayStyle.None;
                Root.Add(pill);

                var close = new Button(() => Sheets.Close()) { text = "" };
                close.AddToClassList("st-square");
                close.tooltip = "Close";
                close.Add(new Glyph("close", Ink, "st-glyph"));
                Root.Add(close);
                Refresh();
            }

            public void SetSub(string s) { if (sub.text != s) sub.text = s; }

            public void SetPill(string text, int kind)
            {
                if (pillText.text != text) pillText.text = text;
                if (kind == pillKind) return;
                pillKind = kind;
                pill.EnableInClassList("st-pill--wait", kind == PillWait);
                pill.EnableInClassList("st-pill--bad", kind == PillBad);
            }

            public void Refresh() { }
        }

        // --- the worker card -------------------------------------------------

        /// Avatar, name, role · mood, and the slot's verbs: Assign when it is
        /// empty, Swap (for the first idle hand) and Unassign when it is not
        /// -- the same `Outpost.Assign` / `Outpost.OrderIdle` the old worker
        /// slot pressed. Built once; `Update` re-texts it and the buttons
        /// read who is in the slot at the moment they are pressed.
        public sealed class WorkerCard
        {
            public readonly VisualElement Root;
            readonly Outpost outpost;
            readonly string planId;
            readonly string role;
            readonly System.Action changed;
            readonly Label initial, name, sub;
            readonly Button main, off, person;
            OutpostHand current;

            public WorkerCard(Outpost o, string planId, System.Action changed)
            {
                outpost = o;
                this.planId = planId;
                this.changed = changed;
                role = BuildPlans.PositionAt(planId);
                if (string.IsNullOrEmpty(role)) role = "hand";

                Root = Card();

                // **Tap the person, not just the slot, 2026-09-27.** Kevin:
                // "I can choose to select the individual from the building
                // menu." Avatar + name + sub open the same sheet a tap in
                // the world does -- `HandSheet` is kept by name, not by
                // reference, so this works whether the body is raised or
                // still a ledger row with nobody walking around for it.
                person = new Button(OpenPerson) { text = "" };
                person.AddToClassList("st-worker-person");
                Root.Add(person);

                var avatar = new VisualElement(); avatar.AddToClassList("st-avatar");
                avatar.pickingMode = PickingMode.Ignore;
                initial = Text("?", "st-avatar-text");
                avatar.Add(initial);
                person.Add(avatar);

                var words = new VisualElement(); words.AddToClassList("st-worker-words");
                words.pickingMode = PickingMode.Ignore;
                name = Text("", "st-worker-name");
                sub = Text("", "st-worker-sub");
                words.Add(name); words.Add(sub);
                person.Add(words);

                var chevron = Text("›", "st-worker-chevron");
                chevron.pickingMode = PickingMode.Ignore;
                person.Add(chevron);

                main = new Button(Main) { text = "Assign" };
                main.AddToClassList("st-btn");
                Root.Add(main);
                off = new Button(Off) { text = "Unassign" };
                off.AddToClassList("st-btn");
                Root.Add(off);
            }

            public void Update(OutpostLedger l, OutpostHand hand, int others)
            {
                current = hand;
                var free = SheetBits.FirstIdle(l);
                if (hand != null)
                {
                    string n = string.IsNullOrEmpty(hand.name) ? "?" : hand.name;
                    initial.text = n.Substring(0, 1).ToUpperInvariant();
                    name.text = n;
                    sub.text = $"{role} · {hand.MoodWord}" + (others > 0 ? $" · +{others} more" : "");
                    main.text = "Swap";
                    main.style.display = free != null ? DisplayStyle.Flex : DisplayStyle.None;
                    main.SetEnabled(free != null);
                    off.style.display = DisplayStyle.Flex;
                    person.SetEnabled(true);
                }
                else
                {
                    initial.text = "?";
                    name.text = "Nobody";
                    sub.text = free != null ? $"needs a {role}" : "no idle hand to post";
                    main.text = "Assign";
                    main.style.display = DisplayStyle.Flex;
                    main.SetEnabled(free != null);
                    off.style.display = DisplayStyle.None;
                    person.SetEnabled(false);
                }
            }

            /// Opens the same sheet a tap on this hand in the world does.
            /// `HandSheet` is kept by name, so this works with no live body.
            void OpenPerson()
            {
                if (outpost == null || current == null || string.IsNullOrEmpty(current.name)) return;
                Sheets.Open(new HandSheet(outpost, current.name));
            }

            void Main()
            {
                var l = outpost != null ? outpost.Ledger : null;
                if (l == null) return;
                var free = SheetBits.FirstIdle(l);
                if (free == null) return;
                if (current != null) outpost.OrderIdle(current, reserve: false);
                outpost.Assign(free, planId);
                changed?.Invoke();
            }

            void Off()
            {
                if (outpost == null || current == null) return;
                outpost.OrderIdle(current, reserve: false);
                changed?.Invoke();
            }
        }

        // --- the upgrade card --------------------------------------------------

        /// "Level 2 · 1.5× faster", the price with have/need per item, and
        /// one button that is "Upgrade" when it can be and otherwise names
        /// what is missing ("Campfire II", "Need 2 brick"). The cost row is
        /// rebuilt only when the level changes -- a tap -- and has no
        /// buttons in it; the button itself is built once.
        public sealed class UpgradeCard
        {
            public readonly VisualElement Root;
            readonly Label title;
            readonly VisualElement cost;
            readonly Button btn;
            /// "Set as goal" (GoalPin): shown while the upgrade cannot be
            /// paid, so the camp overview and the goal bar can chase it.
            readonly Button pinBtn;
            readonly List<(Label label, string res, int n)> lines = new List<(Label, string, int)>();
            int builtLevel = -1;

            public UpgradeCard(System.Action upgrade, System.Action pin = null)
            {
                Root = Card();
                Root.AddToClassList("st-upgrade");
                var words = new VisualElement(); words.AddToClassList("st-upgrade-words");
                title = Text("", "st-upgrade-title");
                words.Add(title);
                cost = new VisualElement(); cost.AddToClassList("st-cost");
                words.Add(cost);
                if (pin != null)
                {
                    pinBtn = new Button(pin) { text = "Set as goal" };
                    pinBtn.AddToClassList("st-pin");
                    pinBtn.style.display = DisplayStyle.None;
                    words.Add(pinBtn);
                }
                Root.Add(words);
                btn = new Button(upgrade) { text = "Upgrade" };
                btn.AddToClassList("st-btn");
                btn.AddToClassList("st-upgrade-btn");
                Root.Add(btn);
            }

            public void Update(OutpostLedger l, int raisedIndex, string planId, int level, bool pinned = false)
            {
                var next = l.NextUpgradeAt(raisedIndex, planId);
                if (next == null)
                {
                    if (pinBtn != null) pinBtn.style.display = DisplayStyle.None;
                    title.text = $"Level {level} · top level";
                    if (builtLevel != level) { cost.Clear(); lines.Clear(); builtLevel = level; }
                    btn.style.display = DisplayStyle.None;
                    return;
                }
                title.text = $"Level {next.toLevel} · {Effect(next)}";
                if (builtLevel != level)
                {
                    builtLevel = level;
                    cost.Clear();
                    lines.Clear();
                    foreach (var c in next.cost)
                    {
                        cost.Add(Icon(c.res, "st-small-icon"));
                        var lab = Text("", "st-cost-text");
                        cost.Add(lab);
                        lines.Add((lab, c.res, c.n));
                    }
                }
                foreach (var (label, res, n) in lines)
                {
                    int have = l.SpendableOf(res);
                    label.text = $"{Mathf.Min(have, 9999)}/{n} {ResDefs.Label(res)}";
                    label.EnableInClassList("st-cost--ok", have >= n);
                    label.EnableInClassList("st-cost--short", have < n);
                }

                bool can = l.CanUpgradeAt(raisedIndex, planId, out string why);
                btn.style.display = DisplayStyle.Flex;
                btn.text = can ? "Upgrade" : Short(l, next, why);
                btn.SetEnabled(can);
                btn.EnableInClassList("st-upgrade-btn--go", can);
                if (pinBtn != null)
                {
                    pinBtn.style.display = can ? DisplayStyle.None : DisplayStyle.Flex;
                    string pt = pinned ? "Goal ✓ · clear" : "Set as goal";
                    if (pinBtn.text != pt) pinBtn.text = pt;
                    pinBtn.EnableInClassList("st-pin--on", pinned);
                }
            }

            static string Short(OutpostLedger l, UpgradeStep next, string why)
            {
                if (l.CampfireLevel < next.campfireLevel) return "Campfire " + RecipeGraph.Roman(next.campfireLevel);
                var missing = Cost.Missing(next.cost, l.SpendableOf);
                if (missing.Count > 0) return $"Need {missing[0].n} {ResDefs.Label(missing[0].res)}";
                return string.IsNullOrEmpty(why) ? "Not yet" : Cap(why);
            }

            static string Effect(UpgradeStep step)
            {
                var parts = new List<string>(3);
                if (Mathf.Abs(step.rateMul - 1f) > 0.001f) parts.Add($"{step.rateMul:0.#}× faster");
                if (step.storeBonus != 0) parts.Add($"+{step.storeBonus} stores");
                if (step.housesBonus != 0) parts.Add(step.housesBonus == 1 ? "+1 bed" : $"+{step.housesBonus} beds");
                return parts.Count > 0 ? string.Join(" · ", parts) : "stronger";
            }
        }

        // --- drawn pieces --------------------------------------------------------

        /// ☰, ✕ and → drawn with `Painter2D`, so no font has to carry them.
        public sealed class Glyph : VisualElement
        {
            readonly string kind;
            readonly Color color;

            public Glyph(string kind, Color color, string cls)
            {
                this.kind = kind;
                this.color = color;
                AddToClassList(cls);
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext ctx)
            {
                var r = contentRect;
                float s = Mathf.Min(r.width, r.height) / 24f;
                if (s <= 0f) return;
                var p = ctx.painter2D;
                p.strokeColor = color;
                p.lineWidth = 2.4f * s;
                p.lineCap = LineCap.Round;
                p.lineJoin = LineJoin.Round;
                void Line(float x0, float y0, float x1, float y1)
                {
                    p.BeginPath();
                    p.MoveTo(new Vector2(x0 * s, y0 * s));
                    p.LineTo(new Vector2(x1 * s, y1 * s));
                    p.Stroke();
                }
                switch (kind)
                {
                    case "menu": Line(4, 7, 20, 7); Line(4, 12, 20, 12); Line(4, 17, 20, 17); break;
                    case "close": Line(6, 6, 18, 18); Line(18, 6, 6, 18); break;
                    // **Small world-object sheets, 2026-09-27** (Wall / Ladder
                    // / Road / Chart): a proper drawn icon in the header badge,
                    // replacing the "☰" glyph that used to sit there and read
                    // like a live menu button when it was not one.
                    case "wall":
                        Line(6, 5, 6, 19); Line(12, 3, 12, 19); Line(18, 5, 18, 19);
                        Line(4, 19, 20, 19);
                        break;
                    case "ladder":
                        Line(7, 4, 7, 20); Line(17, 4, 17, 20);
                        Line(7, 7, 17, 7); Line(7, 12, 17, 12); Line(7, 17, 17, 17);
                        break;
                    case "road":
                        Line(9, 20, 11, 4); Line(15, 20, 13, 4);
                        Line(11.6f, 9.5f, 12.1f, 12.5f); Line(11.9f, 14.5f, 12.4f, 17.5f);
                        break;
                    case "chart":
                        Line(12, 4, 12, 8); Line(12, 16, 12, 20);
                        Line(4, 12, 8, 12); Line(16, 12, 20, 12);
                        p.BeginPath();
                        p.Arc(new Vector2(12 * s, 12 * s), 6.5f * s, 0f, 360f);
                        p.Stroke();
                        break;
                    // The campfire card (2026-09-27): a flame over two logs.
                    case "fire":
                        p.BeginPath();
                        p.MoveTo(new Vector2(12 * s, 3 * s));
                        p.BezierCurveTo(new Vector2(17 * s, 8 * s), new Vector2(18 * s, 12 * s), new Vector2(12 * s, 16 * s));
                        p.BezierCurveTo(new Vector2(6 * s, 12 * s), new Vector2(8 * s, 8 * s), new Vector2(12 * s, 3 * s));
                        p.Stroke();
                        Line(5, 17, 19, 21); Line(5, 21, 19, 17);
                        break;
                    case "pier":
                        Line(3, 9, 21, 9);
                        Line(6, 9, 6, 20); Line(12, 9, 12, 20); Line(18, 9, 18, 20);
                        break;
                    case "ship":
                        Line(5, 17, 19, 17); Line(5, 17, 7, 21); Line(19, 17, 17, 21); Line(7, 21, 17, 21);
                        Line(12, 4, 12, 17); Line(12, 6, 17, 10);
                        break;
                    // The "while you were away" card (2026-09-27): a clock,
                    // same drawn-glyph treatment as the other card headers.
                    case "clock":
                        p.BeginPath();
                        p.Arc(new Vector2(12 * s, 12 * s), 8f * s, 0f, 360f);
                        p.Stroke();
                        Line(12, 12, 12, 6); Line(12, 12, 16.5f, 14.5f);
                        break;
                    default:
                        Line(5, 12, 19, 12);
                        p.BeginPath();
                        p.MoveTo(new Vector2(13 * s, 6 * s));
                        p.LineTo(new Vector2(19 * s, 12 * s));
                        p.LineTo(new Vector2(13 * s, 18 * s));
                        p.Stroke();
                        break;
                }
            }
        }

        /// The bench's progress as a ring: a dim track and a mint arc from
        /// twelve o'clock. Repaints only when the value moves.
        public sealed class BenchRing : VisualElement
        {
            float shown = -1f;

            public float Value
            {
                get => shown;
                set
                {
                    float v = Mathf.Clamp01(value);
                    if (Mathf.Abs(v - shown) < 0.002f) return;
                    shown = v;
                    MarkDirtyRepaint();
                }
            }

            public BenchRing()
            {
                AddToClassList("st-ring");
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext ctx)
            {
                var r = contentRect;
                float w = Mathf.Max(3f, r.width * 0.09f);
                float rad = Mathf.Min(r.width, r.height) * 0.5f - w * 0.5f;
                if (rad <= 0f) return;
                var c = new Vector2(r.width * 0.5f, r.height * 0.5f);
                var p = ctx.painter2D;
                p.lineWidth = w;
                p.strokeColor = Track;
                p.BeginPath();
                p.Arc(c, rad, 0f, 360f);
                p.Stroke();
                if (shown <= 0.001f) return;
                p.strokeColor = Mint;
                p.lineCap = LineCap.Round;
                p.BeginPath();
                p.Arc(c, rad, -90f, -90f + 360f * Mathf.Min(shown, 0.999f));
                p.Stroke();
            }
        }

        /// A dashed rounded outline laid over a card (UI Toolkit borders are
        /// solid only) -- the mockup's "locked" recipe.
        public sealed class DashedFrame : VisualElement
        {
            readonly float radius, width;
            readonly Color color;
            const float Dash = 9f, Gap = 6f;

            public DashedFrame(float radius, float width, Color color)
            {
                this.radius = radius;
                this.width = width;
                this.color = color;
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                style.left = 0f; style.top = 0f; style.right = 0f; style.bottom = 0f;
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext ctx)
            {
                var r = contentRect;
                float h = width * 0.5f;
                float x0 = h, y0 = h, x1 = r.width - h, y1 = r.height - h;
                float rad = Mathf.Min(radius, (x1 - x0) * 0.5f, (y1 - y0) * 0.5f);
                if (rad <= 0f) return;
                var p = ctx.painter2D;
                p.strokeColor = color;
                p.lineWidth = width;
                void Dashes(Vector2 a, Vector2 b)
                {
                    float len = Vector2.Distance(a, b);
                    if (len <= 0f) return;
                    var d = (b - a) / len;
                    for (float t = 0f; t < len; t += Dash + Gap)
                    {
                        p.BeginPath();
                        p.MoveTo(a + d * t);
                        p.LineTo(a + d * Mathf.Min(len, t + Dash));
                        p.Stroke();
                    }
                }
                Dashes(new Vector2(x0 + rad, y0), new Vector2(x1 - rad, y0));
                Dashes(new Vector2(x1, y0 + rad), new Vector2(x1, y1 - rad));
                Dashes(new Vector2(x1 - rad, y1), new Vector2(x0 + rad, y1));
                Dashes(new Vector2(x0, y1 - rad), new Vector2(x0, y0 + rad));
                void Corner(float cx, float cy, float a0)
                {
                    p.BeginPath();
                    p.Arc(new Vector2(cx, cy), rad, a0, a0 + 90f);
                    p.Stroke();
                }
                Corner(x0 + rad, y0 + rad, 180f);
                Corner(x1 - rad, y0 + rad, 270f);
                Corner(x1 - rad, y1 - rad, 0f);
                Corner(x0 + rad, y1 - rad, 90f);
            }
        }
    }
}
