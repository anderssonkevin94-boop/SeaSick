using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The station screen, Melvor-style (Kevin approved, 2026-09-30,
    /// island UI phase 5; mockups "5 · Kitchen (Melvor): Grill picked" and
    /// "5b · Cauldron picked").** Every production building opens a
    /// half-height sheet over itself (the building stays in view above, the
    /// `SheetHost` hug rules), read top to bottom:
    ///
    /// 1. **header** -- the name, "Level N · Ada works both" (or "no
    ///    worker"), Move and ✕;
    /// 2. **spot tiles** -- only for a station with two or more spots
    ///    (Kitchen = Grill + Cauldron, Forge = Smelter + Forge): a glyph, the
    ///    spot's name, one status line ("Grilled meat · 12 s" moss, "Idle ·
    ///    pick a dish" amber, or the pause reason) and a thin bar. The one
    ///    being viewed has the ice outline; a tap switches the grid below;
    /// 3. **the recipe grid** for the viewed spot -- four tiles a row: the
    ///    item's icon (its name alone when there is none -- never an empty
    ///    box), the name, and ONE status: COOKING / RUNNING (moss) when it is
    ///    this spot's recipe, "8 potato" (moss) when the inputs are here,
    ///    "no fish" / "short: onion" when they are not, or LOCKED (dashed,
    ///    lock glyph, "needs Campfire II"). The tapped tile has the thick ice
    ///    border. More than fit PAGE through a last "More" tile, never a
    ///    scroll. One recipe on the spot: no grid, the detail alone;
    /// 4. **the detail** of the tapped recipe -- name + "15 s each"; input
    ///    chips "potato 8/1" (moss / ember) → the output ("1 baked potato ·
    ///    fills ½ day"); one line ("Runs until the potatoes run out or 10 are
    ///    in the store."); when short, the fix as a text link on the right
    ///    ("Gather ore →", `ShortFix`);
    /// 5. **the thumb row** (`BuildActions`) -- ONE bright primary:
    ///    "Select for the grill" ("Select" on a one-spot station), "Stop
    ///    grill" when the tapped recipe is the one running there, or the
    ///    lock's fix ("Raise Campfire to II") when it is locked; plus "Stop
    ///    grill" beside it while the spot runs something else. Never a
    ///    disabled primary: no verb, no button.
    ///
    /// The old **make** + **orders** pages (amount segments ∞/5/10/20,
    /// standing orders "Keep 10") are gone: a selected recipe runs until it
    /// is paused (inputs out, store full) or stopped. **work** (the worker,
    /// in and out, why it stopped) and **level** (the one-button upgrade
    /// card) stay as the secondary tabs.
    ///
    /// **It reads and it calls; it never decides.** Every number comes off
    /// `OutpostLedger` / `StationStock` for THIS built instance
    /// (`StationForRaised`); every button is a verb the ledger or `Outpost`
    /// already has (`SelectRecipe`, `StopSpot`, `Assign`, `UpgradeAt`,
    /// `ShortFix.Run`).
    ///
    /// **Built once, re-texted after (1cf73f9).** `SheetHost` calls
    /// `Refresh` every 0.25 s, and a UI Toolkit click needs its press and
    /// release on the SAME element -- so the tiles are a pool built with the
    /// page and only re-bound; nothing a finger can land on is rebuilt on
    /// that timer.
    public class StationSheet : ISheetFramed
    {
        /// **The old ☰ hook**, unused since the ledger drawer went
        /// (2026-09-30): ☰ opens the Camp sheet (`StationPage.OpenLedgerFor`).
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

        // --- the spots (Kevin, 2026-09-30: Melvor-style station screen) --------

        /// The station's spots in the ledger's order
        /// (`StationSpots.SpotsFor`); a one-spot station holds one entry,
        /// null when the ledger names none.
        readonly IReadOnlyList<string> spotNames;
        /// Two or more spots: the spot tiles row is drawn.
        readonly bool multiSpot;
        /// The station makes dishes: "COOKING", "pick a dish".
        readonly bool foodStation;
        /// The spot whose grid is shown (an index into `spotNames`).
        int viewSpot;
        /// Per spot: the recipe a tap picked (null = the running one, else
        /// the first unlocked) and the grid page it is on.
        readonly string[] pickedBy;
        readonly int[] pageBy;
        /// A refused Select's reason, shown on the detail's line until the
        /// next tap.
        string refusal;

        public StationSheet(Outpost o, Building b)
        {
            outpost = o;
            building = b;
            planId = b != null ? b.Id : null;
            plan = BuildPlans.Named(planId);
            hasMake = Recipes.StationHasRecipes(planId);
            hasUpgrade = Techs.MaxLevel(planId) > 1;
            hasWorker = BuildPlans.HasPosition(planId) && hasMake;
            isStore = OutpostLedger.IsRunnerPost(planId);

            var names = hasMake ? StationSpots.SpotsFor(planId) : null;
            spotNames = names != null && names.Count > 0 ? names : new string[] { null };
            multiSpot = spotNames.Count >= 2;
            pickedBy = new string[spotNames.Count];
            pageBy = new int[spotNames.Count];
            if (hasMake)
                foreach (var r in Recipes.At(planId))
                    if (r != null && FoodBook.IsDish(r.makes)) { foodStation = true; break; }

            if (hasMake)
                hugTabs = hasUpgrade
                    ? new[] { PageMake, PageWork, PageLevel }
                    : new[] { PageMake, PageWork };
            // **The store hut pages too (2026-10-02):** its racks, the
            // runners' slots (`RunnerCard`) and the upgrade do not fit one
            // half-screen page together.
            else if (isStore)
                hugTabs = hasUpgrade
                    ? new[] { PageRacks, PageRunners, PageLevel }
                    : new[] { PageRacks, PageRunners };
            ResolveRaisedIndex();
            // Open on the recipe a fix asked for (`FocusNext`), else on the
            // first spot that is running something.
            if (!TakeFocus())
            {
                var st = Station(L);
                for (int i = 0; i < spotNames.Count; i++)
                {
                    var sp = SpotOf(st, i);
                    if (sp != null && !string.IsNullOrEmpty(sp.recipeId)) { viewSpot = i; break; }
                }
            }
        }

        // --- a fix's focus (Kevin, 2026-09-30) ------------------------------------

        /// **Open the next station sheet ON the recipe that makes `res`.**
        /// Kevin's bug: the quarry's "Make tools" fix opened the forge on
        /// the spear, its first recipe. `ShortFix` calls this just before it
        /// opens the station; the next `StationSheet` built consumes it --
        /// viewing that recipe's spot (the Forge, not the Smelter) with the
        /// recipe tapped, so the detail shows it and Select is the primary.
        /// One-shot: cleared on use, or ignored after `FocusSeconds` when no
        /// sheet took it.
        public static void FocusNext(string res)
        {
            focusRes = res;
            focusAt = Time.unscaledTime;
        }

        static string focusRes;
        static float focusAt = -100f;
        const float FocusSeconds = 2f;

        /// Takes the pending focus when it is fresh and this station makes
        /// it; true when a spot and recipe were picked.
        bool TakeFocus()
        {
            string res = focusRes;
            if (res == null) return false;
            focusRes = null;
            if (!hasMake || Time.unscaledTime - focusAt > FocusSeconds) return false;
            for (int i = 0; i < spotNames.Count; i++)
            {
                var list = SpotRecipes(i);
                for (int k = 0; k < list.Count; k++)
                    if (list[k] != null && list[k].makes == res)
                    {
                        viewSpot = i;
                        pickedBy[i] = list[k].id;
                        return true;
                    }
            }
            return false;
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

        // Resolve at the tap as well as refresh: demolition can shift raised indices.
        StationStock CurrentStation()
        {
            ResolveRaisedIndex();
            return Station(L);
        }

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

        // **Pages in a hugging frame (Kevin, 2026-09-30).** On the phone the
        // sheet is at most half the screen (`SheetHost.HugsContent`) so the
        // building stays in view. A station splits into **make** (the
        // Melvor screen: spots, grid, detail, the thumb row), **work** (the
        // worker + in and out + why it stopped) and **level** (the upgrade).
        // A building with nothing to make (a hut, the store) stays one page
        // and simply hugs it. On the desk (2026-09-30) it pages the same way:
        // the one tall page of worker + make + flow + upgrade ran 120-480 px
        // past the side third, so the last dish row and the "Make ..." /
        // "Build ..." / "Set as goal" buttons were cut.
        public string[] TabLabels => Paged ? hugTabs : null;

        /// **Does this station split into make / work / level pages?** On the
        /// phone's hugging frame and on the desk's side third -- both too
        /// short for the stacked page.
        bool Paged => hugTabs != null && (StationPage.Hugging || HudLayout.Wide);
        public int Tab => tab;
        public void SetTab(int index) { tab = index; }

        int tab = -1;
        readonly string[] hugTabs;
        const string PageMake = "make", PageWork = "work", PageLevel = "level";
        const string PageRacks = "racks", PageRunners = "runners";

        StationPage.Header header;

        public VisualElement BuildHeader()
        {
            // No status pill (2026-09-30): the spot tiles and the grid's
            // badge say what runs; the sub line says who works it.
            header = new StationPage.Header(Title, false, () => StationPage.OpenLedgerFor(outpost));
            // Move / turn this building (Kevin, 2026-09-30) -- beside ✕,
            // never in place of the page's own action.
            MoveButton.AddTo(header.Root, outpost, building);
            return header.Root;
        }

        // --- the thumb floor, shared ----------------------------------------

        /// The old sheets' thumb floor in panel units -- `FireSheet`,
        /// `GatherSheet` and `FarmSheet` still read it.
        internal const float TouchPx = 44f;

        // --- the pieces kept between refreshes --------------------------------

        VisualElement root;
        StationPage.WorkerCard worker;
        RunnerCard runners;
        StationPage.UpgradeCard upgrade;

        /// The make page (spots + grid + detail) was built this tab; the
        /// thumb row belongs to it.
        bool makeBuilt;

        // spot tiles
        sealed class SpotTile
        {
            public Button root;
            public StationPage.Glyph glyph;
            public Label name, status;
            public VisualElement fill;
            public int kind = -1;
        }
        SpotTile[] spotTiles;

        // the recipe grid
        sealed class RecipeTile
        {
            public Button root;
            public VisualElement icon, bar, fill;
            public StationPage.Glyph lockGlyph, moreGlyph;
            public Label name, status, badge;
            public StationPage.DashedFrame dashes;
            /// What the tile shows right now: a recipe, "More", or nothing.
            public Recipe r;
            public bool more;
            public int kind = -1;
        }
        RecipeTile[] tiles;
        VisualElement grid;
        Label gridEyebrow;
        const int PerRow = 4;

        // the detail
        sealed class IoChip
        {
            public VisualElement root, icon;
            public Label text;
        }
        VisualElement detail, detailArrow;
        Label detailName, detailTime, detailLine;
        IoChip[] inChips;
        IoChip outChip;
        Button detailFix;
        ShortFix.Fix detailFixF;
        const int MaxInputs = 5;

        // the thumb row
        enum Main { None, Select, Stop, Fix, Upgrade }
        Main mainMode;
        ShortFix.Fix mainFix;
        /// The hand working the viewed station, or null (set by `FillMake`):
        /// a spot paused "no cook" / "no smith" / "no worker" offers
        /// "Assign <name>" instead of only Stop (island UI rule 2, 2026-09-30).
        OutpostHand viewHand;
        Button primaryBtn, stopBtn;
        VisualElement thumbRow;

        // in and out
        VisualElement bayIcon, rackIcon;
        Label bayValue, benchValue, benchLabel, rackValue;
        StationPage.BenchRing ring;
        Label stallLine;

        // the store
        /// **The store hut's contents as icon tiles (Kevin 2026-09-30: "Why
        /// is the store house information shown like this?").** Was five
        /// lines of lower-case text with "and 3 more kinds". Now a grid of
        /// read-only tiles -- icon, have/ceiling, a thin fill bar, ember at
        /// the ceiling -- fullest first; whatever does not fit is one tap
        /// away on the Backpack's Island page (the one home for goods).
        sealed class StoreTile
        {
            public VisualElement root, icon, fill;
            public Label count;
            public string res;
        }
        StoreTile[] storeTiles;
        Label storeHolds;
        Button storeAll;
        const int StoreTileCount = 8;
        const int StoreTilesPerRow = 4;

        /// Built for a hugging frame (condensed chrome, no eyebrows the tab
        /// already says, one of `hugTabs` per page).
        bool hugged;

        public VisualElement Build()
        {
            storeTiles = null; storeHolds = null; storeAll = null; stallLine = null; worker = null; runners = null; upgrade = null;
            // Every refresh target is re-bound by the page that builds it;
            // the others stay null so `Refresh` skips them.
            spotTiles = null; tiles = null; grid = null; gridEyebrow = null;
            detail = null; detailArrow = null; detailName = detailTime = detailLine = null;
            inChips = null; outChip = null; detailFix = null;
            primaryBtn = stopBtn = null; thumbRow = null;
            bayValue = benchValue = benchLabel = rackValue = null; bayIcon = rackIcon = null; ring = null;
            makeBuilt = false;
            hugged = StationPage.Hugging;
            string on = Paged ? hugTabs[Mathf.Clamp(tab, 0, hugTabs.Length - 1)] : null;
            bool all = on == null;

            root = StationPage.Root("st-page");
            // The host pins content at its natural height (flex-shrink 0),
            // so the page asks for exactly the band -- the scroll view
            // inside then has a bounded viewport to (almost never) scroll.
            // In a hugging frame neither: the page is its content.
            StationPage.FitToParent(root);
            var col = StationPage.Column(root);

            bool first = true;
            VisualElement Section(string eyebrow)
            {
                var s = new VisualElement();
                s.AddToClassList("st-section");
                if (first) s.style.marginTop = 0f;
                first = false;
                if (eyebrow != null) s.Add(StationPage.Text(eyebrow, "st-eyebrow"));
                col.Add(s);
                return s;
            }

            if (hasWorker && (all || on == PageWork))
            {
                var s = Section(null);
                worker = new StationPage.WorkerCard(outpost, planId, () => Refresh(), CurrentStation);
                s.Add(worker.Root);
            }

            if (isStore && (all || on == PageRacks))
            {
                // Two rows of icon tiles, no eyebrow when hugging -- the
                // Backpack's Island page is the goods' home, this is the glance.
                var s = Section(hugged ? null : "ON THE RACKS");
                var card = StationPage.Card();
                card.AddToClassList("st-store-card");
                var sg = new VisualElement(); sg.AddToClassList("st-store-grid");
                card.Add(sg);
                int n = StoreTileCount;
                storeTiles = new StoreTile[n];
                for (int i = 0; i < n; i++)
                {
                    var t = new StoreTile();
                    t.root = new VisualElement(); t.root.AddToClassList("st-store-tile");
                    if (i % StoreTilesPerRow == StoreTilesPerRow - 1) t.root.AddToClassList("st-store-tile--end");
                    var top = new VisualElement(); top.AddToClassList("st-store-top");
                    t.icon = new VisualElement { pickingMode = PickingMode.Ignore };
                    t.icon.AddToClassList("st-store-icon");
                    top.Add(t.icon);
                    t.count = StationPage.Text("", "st-store-count");
                    top.Add(t.count);
                    t.root.Add(top);
                    var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                    bar.AddToClassList("st-store-bar");
                    t.fill = new VisualElement { pickingMode = PickingMode.Ignore };
                    t.fill.AddToClassList("st-store-fill");
                    bar.Add(t.fill);
                    t.root.Add(bar);
                    t.root.style.display = DisplayStyle.None;
                    sg.Add(t.root);
                    storeTiles[i] = t;
                }
                var foot = new VisualElement(); foot.AddToClassList("st-store-foot");
                storeHolds = StationPage.Text("", "st-store-holds");
                foot.Add(storeHolds);
                storeAll = new Button(() => Sheets.Open(new BackpackSheet(outpost))) { text = "All stores ›" };
                storeAll.AddToClassList("st-link");
                foot.Add(storeAll);
                card.Add(foot);
                s.Add(card);
            }

            if (isStore && (all || on == PageRunners))
            {
                // **Runners (2026-10-02):** the store hut's own worker slots
                // -- two at level 1, four at level 2 -- for the hands who push
                // wheelbarrows. Assign / Swap / Free like any station's worker.
                var s = Section(null);
                runners = new RunnerCard(outpost, planId, () => { ResolveRaisedIndex(); return L != null ? L.OrdinalOfRaised(raisedIndex, planId) : -1; }, () => Refresh());
                s.Add(runners.Root);
            }

            if (hasMake && (all || on == PageMake))
            {
                BuildMake(Section(null));
                makeBuilt = true;
            }
            if (hasMake && (all || on == PageWork))
                BuildFlow(Section(hugged ? null : "IN AND OUT"));

            if (hasUpgrade && (all || on == PageLevel))
            {
                upgrade = new StationPage.UpgradeCard(DoUpgrade, TogglePin, outpost);
                if (first) upgrade.Root.style.marginTop = 0f;
                col.Add(upgrade.Root);
            }

            Refresh();
            return root;
        }

        // --- the thumb row ------------------------------------------------------

        /// **The one main action, in the thumb row (island UI rule 1).** Built
        /// with the make page only; the work and level pages have their own
        /// buttons on the page. The primary is re-labelled and re-pointed by
        /// `FillThumb`, never rebuilt.
        public VisualElement BuildActions()
        {
            primaryBtn = stopBtn = null; thumbRow = null;
            if (!makeBuilt) return null;
            primaryBtn = SheetKit.Btn("Select", PressMain, true);
            stopBtn = SheetKit.Btn("Stop", PressStop);
            ThumbStyle(primaryBtn, 18f);
            ThumbStyle(stopBtn, 15f);
            thumbRow = SheetKit.Actions(primaryBtn, stopBtn);
            // The primary takes the row; Stop is the narrow one beside it.
            primaryBtn.style.flexGrow = 2.4f;
            stopBtn.style.flexGrow = 1f;
            Refresh();
            return thumbRow;
        }

        /// Thumb-sized (≥ 44, the mockup's 54 pt rounded down to fit the
        /// half-height frame), wraps rather than clips a long "Raise
        /// Campfire to II".
        static void ThumbStyle(Button b, float font)
        {
            b.style.height = StyleKeyword.Auto;
            b.style.minHeight = 50f;
            b.style.fontSize = font;
            b.style.whiteSpace = WhiteSpace.Normal;
            b.style.borderTopLeftRadius = b.style.borderTopRightRadius =
                b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 14f;
        }

        // --- 2-4. the make page: spots, grid, detail -------------------------------

        /// Panel units the make page's pieces cost, for planning how many grid
        /// rows fit the hugging frame BEFORE building (a page never re-plans
        /// under a finger). The sums of the `st-spot*` / `st-rtile*` /
        /// `st-detail*` rules in Station.uss, rounded UP.
        const float SpotRowPx = 80f;     // 8 pad + 22 name row + 4 + 17 status + 4 + 5 bar + 8 pad + 4 border + 8 gap
        const float TileRowPx = 88f;     // 7 pad + 28 icon + 3 + 16 name + 3 + 15 status + 5 pad + 3 border + 8 gap
        const float DetailPx = 112f;     // 10 pad + 20 head + 6 + 26 chips + 6 + 18 line + 10 pad + 2 border + 8 gap + wrap slack
        const float EyebrowRowPx = 22f;
        const float ThumbRowPx = 73f;    // 10 pad + 50 button + 12 pad + 1 rule

        void BuildMake(VisualElement s)
        {
            if (multiSpot)
            {
                var row = new VisualElement(); row.AddToClassList("st-spots");
                spotTiles = new SpotTile[spotNames.Count];
                for (int i = 0; i < spotNames.Count; i++)
                {
                    int idx = i;
                    var t = new SpotTile();
                    t.root = new Button(() => ViewSpot(idx)) { text = "" };
                    t.root.AddToClassList("st-spot");
                    if (i == 0) t.root.AddToClassList("st-spot--first");
                    var top = new VisualElement(); top.AddToClassList("st-spot-top");
                    top.pickingMode = PickingMode.Ignore;
                    t.glyph = new StationPage.Glyph(SpotGlyph(spotNames[i]), StationPage.Dim, "st-spot-glyph");
                    top.Add(t.glyph);
                    t.name = StationPage.Text(StationPage.Cap(spotNames[i]), "st-spot-name");
                    top.Add(t.name);
                    t.root.Add(top);
                    t.status = StationPage.Text("", "st-spot-status");
                    t.root.Add(t.status);
                    var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                    bar.AddToClassList("st-spot-bar");
                    t.fill = new VisualElement { pickingMode = PickingMode.Ignore };
                    t.fill.AddToClassList("st-spot-fill");
                    bar.Add(t.fill);
                    t.root.Add(bar);
                    row.Add(t.root);
                    spotTiles[i] = t;
                }
                s.Add(row);
            }

            // The eyebrow teaches the tap on the desk; in the hugging frame
            // the ice outline already says which spot, and the 22 units are
            // a grid row's worth of the half screen.
            if (!hugged)
            {
                gridEyebrow = StationPage.Text("", "st-eyebrow");
                gridEyebrow.AddToClassList("st-grid-eyebrow");
                s.Add(gridEyebrow);
            }

            int most = 0;
            for (int i = 0; i < spotNames.Count; i++) most = Mathf.Max(most, SpotRecipes(i).Count);
            int rows = PlanRows(most);
            int pool = Mathf.Min(most, rows * PerRow);
            grid = new VisualElement(); grid.AddToClassList("st-rgrid");
            tiles = new RecipeTile[pool];
            for (int i = 0; i < pool; i++)
            {
                var t = BuildTile();
                if (i % PerRow == PerRow - 1) t.root.AddToClassList("st-rtile--end");
                if (i >= PerRow) t.root.AddToClassList("st-rtile--row2");
                tiles[i] = t;
                grid.Add(t.root);
            }
            s.Add(grid);

            BuildDetail(s);

            // Open every spot on the page that holds its running recipe.
            var st = Station(L);
            for (int i = 0; i < spotNames.Count; i++) pageBy[i] = PageOf(i, Tapped(L, st, i));
        }

        /// **How many grid rows fit**, planned from the band before building.
        /// Hugging: the half-screen body (`SheetHost.HugBodyBudget`) less the
        /// thumb row, the spot tiles and the detail -- on a 16 Pro that is
        /// one row, and the cauldron's five dishes page as 3 + More. Desk
        /// (2026-09-30): the side third's `BandHeight`, More-paged the same way.
        int PlanRows(int most)
        {
            int need = Mathf.CeilToInt(most / (float)PerRow);
            if (need <= 1) return 1;
            if (!hugged)
            {
                // **Desk (2026-09-30):** the side third's page band, not "every
                // row" -- a cauldron of dishes ran past it. The spots, the
                // grid eyebrow (22 + gap) and the detail come off the band, the
                // tab strip and thumb row are already in `BandHeight`; what
                // does not fit pages through the More tile.
                if (!Paged) return need;
                float deskFixed = (multiSpot ? SpotRowPx : 0f) + 30f + DetailPx;
                int deskFit = Mathf.FloorToInt((SheetHost.BandHeight - deskFixed) / TileRowPx);
                return Mathf.Clamp(deskFit, 1, need);
            }
            float budget = SheetHost.HugBodyBudget(hugTabs != null && hugTabs.Length > 1) - ThumbRowPx;
            float fixedPx = (multiSpot ? SpotRowPx : 0f) + DetailPx;
            int fit = Mathf.FloorToInt((budget - fixedPx) / TileRowPx);
            return Mathf.Clamp(fit, 1, need);
        }

        RecipeTile BuildTile()
        {
            var t = new RecipeTile();
            t.root = new Button { text = "" };
            t.root.AddToClassList("st-rtile");
            t.icon = StationPage.Icon(null, "st-rtile-icon");
            t.root.Add(t.icon);
            t.lockGlyph = new StationPage.Glyph("lock", StationPage.Dim, "st-rtile-glyph");
            t.lockGlyph.style.display = DisplayStyle.None;
            t.root.Add(t.lockGlyph);
            t.moreGlyph = new StationPage.Glyph("arrow", StationPage.Ink, "st-rtile-glyph");
            t.moreGlyph.style.display = DisplayStyle.None;
            t.root.Add(t.moreGlyph);
            t.name = StationPage.Text("", "st-rtile-name");
            t.root.Add(t.name);
            t.status = StationPage.Text("", "st-rtile-status");
            t.root.Add(t.status);
            t.badge = StationPage.Text("", "st-badge");
            t.badge.style.display = DisplayStyle.None;
            t.root.Add(t.badge);
            // The running recipe's progress: a thin strip along the foot.
            t.bar = new VisualElement { pickingMode = PickingMode.Ignore };
            t.bar.AddToClassList("st-rtile-bar");
            t.fill = new VisualElement { pickingMode = PickingMode.Ignore };
            t.fill.AddToClassList("st-rtile-fill");
            t.bar.Add(t.fill);
            t.bar.style.visibility = Visibility.Hidden;
            t.root.Add(t.bar);
            // UI Toolkit has no dashed border: a locked tile draws its own.
            t.dashes = new StationPage.DashedFrame(12f, 1.5f, StationPage.Edge);
            t.dashes.style.display = DisplayStyle.None;
            t.root.Add(t.dashes);
            // Registered once; what the tile IS is read at the tap.
            t.root.clicked += () => PressTile(t);
            return t;
        }

        void BuildDetail(VisualElement s)
        {
            detail = new VisualElement(); detail.AddToClassList("st-detail");
            var head = new VisualElement(); head.AddToClassList("st-detail-head");
            detailName = StationPage.Text("", "st-detail-name");
            detailTime = StationPage.Text("", "st-detail-time");
            head.Add(detailName); head.Add(detailTime);
            detail.Add(head);

            var io = new VisualElement(); io.AddToClassList("st-detail-io");
            inChips = new IoChip[MaxInputs];
            for (int i = 0; i < MaxInputs; i++)
            {
                inChips[i] = Chip();
                inChips[i].root.style.display = DisplayStyle.None;
                io.Add(inChips[i].root);
            }
            detailArrow = new StationPage.Glyph("arrow", StationPage.Dim, "st-io-arrow");
            io.Add(detailArrow);
            outChip = Chip();
            outChip.text.AddToClassList("st-io-text--out");
            io.Add(outChip.root);
            detail.Add(io);

            var foot = new VisualElement(); foot.AddToClassList("st-detail-foot");
            detailLine = StationPage.Text("", "st-detail-line");
            foot.Add(detailLine);
            detailFix = new Button(() => { if (detailFixF.Run(outpost)) Refresh(); }) { text = "" };
            detailFix.AddToClassList("st-link");
            detailFix.AddToClassList("st-detail-fix");
            detailFix.style.display = DisplayStyle.None;
            foot.Add(detailFix);
            detail.Add(foot);
            s.Add(detail);
        }

        static IoChip Chip()
        {
            var c = new IoChip();
            c.root = new VisualElement { pickingMode = PickingMode.Ignore };
            c.root.AddToClassList("st-io-chip");
            c.icon = StationPage.Icon(null, "st-io-icon");
            c.root.Add(c.icon);
            c.text = StationPage.Text("", "st-io-text");
            c.root.Add(c.text);
            return c;
        }

        // --- the spot and recipe model ------------------------------------------------

        /// The recipes at spot `i` (`StationSpots.RecipesFor`). A one-spot
        /// station whose ledger names no spot falls back to every recipe of
        /// the station, the list the old grid showed.
        IReadOnlyList<Recipe> SpotRecipes(int i)
        {
            if (!hasMake || i < 0 || i >= spotNames.Count) return System.Array.Empty<Recipe>();
            var list = StationSpots.RecipesFor(planId, spotNames[i]);
            if ((list == null || list.Count == 0) && !multiSpot) return Recipes.At(planId);
            return list ?? (IReadOnlyList<Recipe>)System.Array.Empty<Recipe>();
        }

        /// The ledger's index of spot `i` in `StationStock.Spots`: matched by
        /// name, else the same position (a one-spot station is index 0).
        static int SpotIndexIn(StationStock st, IReadOnlyList<string> names, int i)
        {
            var list = st != null ? st.Spots : null;
            if (list == null || list.Count == 0) return i;
            string name = names[i];
            if (name != null)
                for (int k = 0; k < list.Count; k++)
                    if (list[k] != null && list[k].spot == name) return k;
            return Mathf.Clamp(i, 0, list.Count - 1);
        }

        int SpotIndex(StationStock st, int i) => SpotIndexIn(st, spotNames, i);

        /// Spot `i`'s live state, or null (no station, no spots yet).
        SpotState SpotOf(StationStock st, int i)
        {
            var list = st != null ? st.Spots : null;
            if (list == null || list.Count == 0) return null;
            int k = SpotIndex(st, i);
            return k >= 0 && k < list.Count ? list[k] : null;
        }

        static bool HasRecipe(SpotState sp) => sp != null && !string.IsNullOrEmpty(sp.recipeId);

        /// The recipe the detail is about at spot `i`: the one a tap picked,
        /// else the one the spot runs, else the first the camp can make,
        /// else the first.
        Recipe Tapped(OutpostLedger l, StationStock st, int i)
        {
            var list = SpotRecipes(i);
            string id = i >= 0 && i < pickedBy.Length ? pickedBy[i] : null;
            if (id != null)
                for (int k = 0; k < list.Count; k++) if (list[k].id == id) return list[k];
            var sp = SpotOf(st, i);
            if (HasRecipe(sp))
                for (int k = 0; k < list.Count; k++) if (list[k].id == sp.recipeId) return list[k];
            if (l != null)
                for (int k = 0; k < list.Count; k++) if (LockShort(l, list[k]) == null) return list[k];
            return list.Count > 0 ? list[0] : null;
        }

        /// Recipes a page shows: the whole pool when they all fit, else one
        /// less -- the last tile is "More".
        int PerPage(int count) =>
            tiles == null || tiles.Length == 0 ? 1
            : count <= tiles.Length ? tiles.Length : Mathf.Max(1, tiles.Length - 1);

        int Pages(int count) => Mathf.Max(1, Mathf.CeilToInt(count / (float)PerPage(count)));

        int PageOf(int spot, Recipe r)
        {
            if (r == null) return 0;
            var list = SpotRecipes(spot);
            int per = PerPage(list.Count);
            for (int k = 0; k < list.Count; k++) if (list[k] == r) return k / per;
            return 0;
        }

        /// "needs Campfire II" / "needs level 2" / "needs a saw blade", or
        /// null when the recipe can be selected HERE. This building's own
        /// level gates its level-2 recipes (`RecipeAvailable` reads the
        /// plan's best copy).
        string LockShort(OutpostLedger l, Recipe r)
        {
            bool ok = l.RecipeAvailable(r, out string why);
            if (l.CampfireLevel < r.campfireLevel) return "needs Campfire " + RecipeGraph.Roman(r.campfireLevel);
            if (MyLevel(l) < r.stationLevel) return $"needs level {r.stationLevel}";
            if (r.tool != null && !l.Holds(r.tool)) return "needs a " + ResDefs.Label(r.tool);
            return ok ? null : why;
        }

        /// What this building has of `res` for a recipe: the camp store plus
        /// this bench's own bay -- the count the old cards showed.
        static int Have(OutpostLedger l, StationStock st, string res) =>
            l.StoreCountOf(res) + (st != null ? st.BayCount(res) : 0);

        /// The input the recipe lacks most (`ShortFix.Most`: a gatherable
        /// first), or null when every input is here.
        static string MostShort(OutpostLedger l, StationStock st, Recipe r)
        {
            var most = new ShortFix.Most();
            foreach (var line in r.takes) most.Add(line.res, line.n - Have(l, st, line.res));
            return most.Res;
        }

        static int Need(Recipe r, string res)
        {
            foreach (var line in r.takes) if (line.res == res) return line.n;
            return 0;
        }

        // --- taps -----------------------------------------------------------------------

        void ViewSpot(int i)
        {
            if (i == viewSpot) return;
            viewSpot = i;
            refusal = null;
            pageBy[i] = PageOf(i, Tapped(L, CurrentStation(), i));
            Refresh();
        }

        void PressTile(RecipeTile t)
        {
            refusal = null;
            if (t.more)
            {
                var list = SpotRecipes(viewSpot);
                pageBy[viewSpot] = (pageBy[viewSpot] + 1) % Pages(list.Count);
            }
            else if (t.r != null) pickedBy[viewSpot] = t.r.id;
            Refresh();
        }

        void PressMain()
        {
            var l = L;
            var st = CurrentStation();
            if (l == null) return;
            var r = Tapped(l, st, viewSpot);
            switch (mainMode)
            {
                case Main.Select:
                    if (st == null || r == null) return;
                    refusal = l.SelectRecipe(st, SpotIndex(st, viewSpot), r.id, out string why) ? null
                        : (string.IsNullOrEmpty(why) ? "Can't start that here yet." : StationPage.Cap(why));
                    break;
                case Main.Stop:
                    if (st != null) l.StopSpot(st, SpotIndex(st, viewSpot));
                    refusal = null;
                    break;
                case Main.Fix:
                    if (!mainFix.Run(outpost)) return;
                    break;
                case Main.Upgrade:
                    DoUpgrade();
                    return;
                default: return;
            }
            Refresh();
        }

        void PressStop()
        {
            var l = L;
            var st = CurrentStation();
            if (l == null || st == null) return;
            l.StopSpot(st, SpotIndex(st, viewSpot));
            refusal = null;
            Refresh();
        }

        // --- fill -------------------------------------------------------------------------

        /// Re-binds the make page (spot tiles, the grid, the detail, the thumb
        /// row). Only texts, classes and display flip here -- nothing is built.
        void FillMake(OutpostLedger l, StationStock st, OutpostHand hand, Recipe tapped)
        {
            viewHand = hand;
            FillSpots(l, st, hand);
            var list = SpotRecipes(viewSpot);
            var sp = SpotOf(st, viewSpot);
            FillGrid(l, st, sp, list, tapped);
            FillDetail(l, st, sp, tapped);
        }

        void FillSpots(OutpostLedger l, StationStock st, OutpostHand hand)
        {
            if (spotTiles == null) return;
            for (int i = 0; i < spotTiles.Length; i++)
            {
                var t = spotTiles[i];
                var sp = SpotOf(st, i);
                t.root.EnableInClassList("st-spot--on", i == viewSpot);
                string text; int kind; float p = 0f;
                var r = HasRecipe(sp) ? Recipes.Named(sp.recipeId) : null;
                if (!HasRecipe(sp))
                {
                    text = foodStation ? "Idle · pick a dish" : "Idle · pick one";
                    kind = 1;
                }
                else if (!string.IsNullOrEmpty(sp.pauseReason))
                {
                    // The tile is half a screen wide: the whole "fine boards
                    // need a saw blade (...) and boards (...)" sentence
                    // (`MissingWords`, 2026-10-04) is the detail line's, which
                    // wraps in full below the grid; the tile says that it is
                    // short and of what kind.
                    text = MissingWords.IsSupplyLine(sp.pauseReason)
                        ? $"{StationPage.Cap(r != null ? r.label : sp.recipeId)} · short of supplies"
                        : StationPage.Cap(sp.pauseReason);
                    kind = hand == null ? 2 : 1;
                    p = sp.progress01;
                }
                else if (sp.Running)
                {
                    text = $"{StationPage.Cap(r != null ? r.label : sp.recipeId)} · {Secs(sp.SecondsLeft)}";
                    kind = 0;
                    p = sp.progress01;
                }
                else
                {
                    text = $"{StationPage.Cap(r != null ? r.label : sp.recipeId)} · waiting";
                    kind = 1;
                    p = sp.progress01;
                }
                if (t.status.text != text) t.status.text = text;
                if (kind != t.kind)
                {
                    t.kind = kind;
                    t.status.EnableInClassList("st-tone--good", kind == 0);
                    t.status.EnableInClassList("st-tone--wait", kind == 1);
                    t.status.EnableInClassList("st-tone--bad", kind == 2);
                    t.fill.EnableInClassList("st-spot-fill--wait", kind != 0);
                    t.glyph.SetColor(kind == 0 ? StationPage.Amber : StationPage.Dim);
                }
                t.fill.style.width = Length.Percent(Mathf.Clamp01(p) * 100f);
            }
        }

        /// "12 s", "2 min" -- what is left on the spot's batch.
        static string Secs(float s)
        {
            s = Mathf.Max(0f, s);
            return s < 90f ? $"{Mathf.CeilToInt(s)} s" : $"{Mathf.CeilToInt(s / 60f)} min";
        }

        void FillGrid(OutpostLedger l, StationStock st, SpotState sp, IReadOnlyList<Recipe> list, Recipe tapped)
        {
            if (tiles == null || grid == null) return;
            // One recipe on this spot (the fishing hut, the mill, the
            // smelter...): no grid, the detail says it all.
            bool showGrid = list.Count > 1;
            var want = showGrid ? DisplayStyle.Flex : DisplayStyle.None;
            if (grid.style.display != want) grid.style.display = want;
            int per = PerPage(list.Count), pages = Pages(list.Count);
            int page = Mathf.Clamp(pageBy[viewSpot], 0, pages - 1);
            pageBy[viewSpot] = page;
            bool paged = pages > 1;
            if (gridEyebrow != null)
            {
                gridEyebrow.style.display = want;
                string eb = (multiSpot ? (spotNames[viewSpot] ?? "").ToUpperInvariant() + " · " : "")
                    + (foodStation ? "TAP A DISH" : "TAP A RECIPE")
                    + (paged ? $" · {page + 1}/{pages}" : "");
                if (gridEyebrow.text != eb) gridEyebrow.text = eb;
            }
            if (!showGrid) return;

            for (int k = 0; k < tiles.Length; k++)
            {
                var t = tiles[k];
                if (paged && k == tiles.Length - 1)
                {
                    BindMore(t, page, pages);
                    continue;
                }
                int ri = page * per + k;
                if (ri >= list.Count)
                {
                    // A short last page keeps "More" in its corner: the
                    // empty slots hold their place, invisibly.
                    t.r = null; t.more = false;
                    t.root.style.display = paged ? DisplayStyle.Flex : DisplayStyle.None;
                    t.root.style.visibility = Visibility.Hidden;
                    continue;
                }
                t.root.style.display = DisplayStyle.Flex;
                t.root.style.visibility = Visibility.Visible;
                BindTile(t, list[ri], l, st, sp, tapped);
            }
        }

        void BindMore(RecipeTile t, int page, int pages)
        {
            t.r = null; t.more = true;
            t.root.style.display = DisplayStyle.Flex;
            t.root.style.visibility = Visibility.Visible;
            Tone(t, 3);
            t.root.EnableInClassList("st-rtile--on", false);
            t.root.EnableInClassList("st-rtile--run", false);
            t.root.EnableInClassList("st-rtile--locked", false);
            t.root.EnableInClassList("st-rtile--more", true);
            t.dashes.style.display = DisplayStyle.None;
            t.icon.style.display = DisplayStyle.None;
            t.lockGlyph.style.display = DisplayStyle.None;
            t.moreGlyph.style.display = DisplayStyle.Flex;
            t.bar.style.visibility = Visibility.Hidden;
            t.badge.style.display = DisplayStyle.None;
            t.status.style.display = DisplayStyle.Flex;
            SetText(t.name, "More");
            SetText(t.status, $"{page + 1} of {pages}");
        }

        /// One tile, ONE status: the running badge, the lock, short, or the
        /// count on hand.
        void BindTile(RecipeTile t, Recipe r, OutpostLedger l, StationStock st, SpotState sp, Recipe tapped)
        {
            t.r = r; t.more = false;
            t.root.EnableInClassList("st-rtile--more", false);
            t.moreGlyph.style.display = DisplayStyle.None;
            string lockWhy = l != null ? LockShort(l, r) : null;
            bool locked = lockWhy != null;
            bool mine = HasRecipe(sp) && sp.recipeId == r.id;
            bool picked = tapped != null && tapped.id == r.id;

            t.root.EnableInClassList("st-rtile--on", picked);
            t.root.EnableInClassList("st-rtile--run", mine && !picked);
            t.root.EnableInClassList("st-rtile--locked", locked);
            var dash = locked && !picked ? DisplayStyle.Flex : DisplayStyle.None;
            if (t.dashes.style.display != dash) t.dashes.style.display = dash;

            // The item's picture; a locked tile shows the lock instead, and
            // an item with no icon shows its name alone -- never an empty box.
            bool hasIcon = !locked && ItemIconSet.Get(r.makes) != null;
            StationPage.SetIcon(t.icon, hasIcon ? r.makes : null);
            t.icon.style.display = hasIcon ? DisplayStyle.Flex : DisplayStyle.None;
            t.lockGlyph.style.display = locked ? DisplayStyle.Flex : DisplayStyle.None;
            SetText(t.name, StationPage.Cap(r.label));

            bool badge = mine && !locked;
            t.badge.style.display = badge ? DisplayStyle.Flex : DisplayStyle.None;
            t.status.style.display = badge ? DisplayStyle.None : DisplayStyle.Flex;
            t.bar.style.visibility = badge ? Visibility.Visible : Visibility.Hidden;
            if (badge)
            {
                bool running = sp.Running && string.IsNullOrEmpty(sp.pauseReason);
                SetText(t.badge, running ? (foodStation ? "COOKING" : "RUNNING") : "PAUSED");
                t.badge.EnableInClassList("st-badge--wait", !running);
                t.fill.style.width = Length.Percent(Mathf.Clamp01(sp.progress01) * 100f);
                return;
            }
            if (locked)
            {
                SetText(t.status, lockWhy);
                Tone(t, 3);
                return;
            }
            if (l == null) { SetText(t.status, ""); return; }
            string res = MostShort(l, st, r);
            if (res != null)
            {
                int have = Have(l, st, res);
                string label = ResDefs.Label(res);
                // An input nobody here can make yet names the building.
                var fix = ShortFix.For(outpost, res);
                if (fix.kind == ShortFix.Kind.Build)
                {
                    SetText(t.status, "needs a " + BuildPlans.Named(fix.planId).label);
                    Tone(t, 1);
                }
                else if (r.takes.Length == 1 && have <= 0)
                {
                    SetText(t.status, "no " + label);
                    Tone(t, 2);
                }
                else
                {
                    SetText(t.status, "short: " + label);
                    Tone(t, 1);
                }
                return;
            }
            // Everything is here: the one input's count, or "ready".
            SetText(t.status, r.takes.Length == 1
                ? ResDefs.Counted(r.takes[0].res, Mathf.Min(Have(l, st, r.takes[0].res), 999))
                : "ready");
            Tone(t, 0);
        }

        /// 0 good (moss), 1 wait (amber), 2 bad (ember), 3 muted.
        static void Tone(RecipeTile t, int kind)
        {
            if (kind == t.kind) return;
            t.kind = kind;
            t.status.EnableInClassList("st-tone--good", kind == 0);
            t.status.EnableInClassList("st-tone--wait", kind == 1);
            t.status.EnableInClassList("st-tone--bad", kind == 2);
        }

        static void SetText(Label l, string s)
        {
            if (l != null && l.text != s) l.text = s ?? "";
        }

        static void SetTone(VisualElement e, int kind)
        {
            e.EnableInClassList("st-tone--good", kind == 0);
            e.EnableInClassList("st-tone--wait", kind == 1);
            e.EnableInClassList("st-tone--bad", kind == 2);
        }

        void FillDetail(OutpostLedger l, StationStock st, SpotState sp, Recipe r)
        {
            if (detail == null) return;
            var show = r != null && l != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (detail.style.display != show) detail.style.display = show;
            if (r == null || l == null) { FillThumb(l, st, sp, null); return; }

            SetText(detailName, StationPage.Cap(r.label));
            SetText(detailTime, EachTime(r, MyLevel(l)));

            // Inputs: "potato 8/1", moss when enough, ember when short.
            int n = Mathf.Min(r.takes.Length, inChips.Length);
            for (int i = 0; i < inChips.Length; i++)
            {
                var c = inChips[i];
                bool on = i < n;
                var d = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (c.root.style.display != d) c.root.style.display = d;
                if (!on) continue;
                var line = r.takes[i];
                int have = Have(l, st, line.res);
                BindChipIcon(c, line.res);
                SetText(c.text, $"{ResDefs.Label(line.res)} {Mathf.Min(have, 999)}/{line.n}");
                c.text.EnableInClassList("st-tone--good", have >= line.n);
                c.text.EnableInClassList("st-tone--bad", have < line.n);
            }
            detailArrow.style.display = n > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            // The output: "1 baked potato · fills ½ day"; a good that is not
            // eaten just says its yield.
            int y = Mathf.Max(1, r.yield);
            string outText = ResDefs.Counted(r.makes, y);
            if (FoodBook.IsEdible(r.makes))
            {
                string fill = FillWords(FoodBook.Fill(r.makes));
                if (fill != null) outText += y > 1 ? $" · fills {fill} each" : $" · fills {fill}";
            }
            BindChipIcon(outChip, r.makes);
            SetText(outChip.text, outText);

            // The one line, and the fix beside it when an input is short.
            string lockWhy = LockShort(l, r);
            string shortRes = lockWhy == null ? MostShort(l, st, r) : null;
            bool mine = HasRecipe(sp) && sp.recipeId == r.id;
            string text; int tone;
            var fix = default(ShortFix.Fix);
            if (!string.IsNullOrEmpty(refusal)) { text = refusal; tone = 2; }
            else if (lockWhy != null)
            {
                // **Everything it lacks, not the first thing (2026-10-04, Kevin:
                // 'it's set to fine boards but I don't have a saw blade')**:
                // a locked card names the lock, the tool AND the inputs, each
                // with where to get it (`MissingWords`); the short lock is
                // the fallback and the tile's own line.
                string whole = l.MissingLine(st, r, out _);
                text = StationPage.Cap(whole ?? lockWhy) + ".";
                tone = 2;
            }
            else if (mine && !string.IsNullOrEmpty(sp.pauseReason))
            {
                text = StationPage.Cap(sp.pauseReason);
                tone = 1;
                if (viewHand == null) fix = ShortFix.AssignHand(outpost, st);
                else if (shortRes != null) fix = ShortFix.For(outpost, shortRes);
            }
            else if (shortRes != null)
            {
                // Every input and the tool, with where to get each
                // (`MissingWords`, 2026-10-04); the old one-input sentence
                // only when the ledger sees the stock somewhere (a rack).
                string whole = l.MissingLine(st, r, out _);
                int have = Have(l, st, shortRes);
                string lab = ResDefs.Label(shortRes);
                if (whole != null) text = StationPage.Cap(whole) + ". It will wait.";
                else text = have <= 0
                    ? $"No {Plural(lab)} yet. It will wait for {(Mass(lab) ? "it" : "them")}."
                    : $"Short of {Plural(lab)} ({have}/{Need(r, shortRes)}). It will wait.";
                tone = 1;
                fix = ShortFix.For(outpost, shortRes);
            }
            else
            {
                // **No "or N are in the store" any more** (Kevin 2026-10-03,
                // infinite stacking): the island store never fills, so a
                // spot runs until stopped or an input runs out.
                if (r.takes.Length == 0) text = "Runs until you stop it.";
                else if (r.takes.Length == 1) text = $"Runs until {RunsOut(ResDefs.Label(r.takes[0].res))} or you stop it.";
                else text = "Runs until an input runs out or you stop it.";
                tone = 3;
            }
            // **A worn tool says so (2026-10-04, Kevin: 'I don't have a saw
            // blade' with one at 90%)**: "Saw blade: 90% left."
            if (lockWhy == null && r.tool != null)
            {
                string life = l.LifeLeft(r.tool);
                if (life != null) text += " " + StationPage.Cap(ResDefs.Label(r.tool)) + ": " + life + ".";
            }
            SetText(detailLine, text);
            SetTone(detailLine, tone);

            detailFixF = fix;
            var fd = fix.Valid ? DisplayStyle.Flex : DisplayStyle.None;
            if (detailFix.style.display != fd) detailFix.style.display = fd;
            if (fix.Valid) { string ft = fix.label + " ›"; if (detailFix.text != ft) detailFix.text = ft; }

            FillThumb(l, st, sp, r);
        }

        static void BindChipIcon(IoChip c, string res)
        {
            bool has = res != null && ItemIconSet.Get(res) != null;
            StationPage.SetIcon(c.icon, has ? res : null);
            var d = has ? DisplayStyle.Flex : DisplayStyle.None;
            if (c.icon.style.display != d) c.icon.style.display = d;
        }

        /// **The thumb row (island UI rule 1): ONE bright primary, never a
        /// disabled one.** The tapped recipe running here -> "Stop grill";
        /// locked -> its fix; otherwise "Select for the grill". "Stop grill"
        /// sits beside it while the spot runs something else.
        void FillThumb(OutpostLedger l, StationStock st, SpotState sp, Recipe r)
        {
            if (primaryBtn == null) return;
            string spot = multiSpot ? (spotNames[viewSpot] ?? "").ToLowerInvariant() : null;
            string stopText = spot != null ? "Stop " + spot : "Stop";
            bool busy = HasRecipe(sp);
            bool thisRuns = busy && r != null && sp.recipeId == r.id;

            var mode = Main.None;
            string text = null;
            mainFix = default;
            if (l != null && st != null && r != null)
            {
                string lockWhy = LockShort(l, r);
                if (thisRuns && viewHand == null && ShortFix.AssignHand(outpost, st).Valid)
                {
                    // Paused "no cook": the fix is the primary, Stop stays beside it.
                    mainFix = ShortFix.AssignHand(outpost, st);
                    mode = Main.Fix;
                    text = mainFix.label;
                }
                else if (thisRuns) { mode = Main.Stop; text = stopText; }
                else if (lockWhy != null)
                {
                    mode = LockFix(l, r, out mainFix, out text);
                }
                else
                {
                    mode = Main.Select;
                    text = spot != null ? $"Select for the {spot}" : "Select";
                }
            }
            mainMode = mode;
            var pd = mode != Main.None ? DisplayStyle.Flex : DisplayStyle.None;
            if (primaryBtn.style.display != pd) primaryBtn.style.display = pd;
            if (text != null && primaryBtn.text != text) primaryBtn.text = text;

            bool stopShown = busy && mode != Main.Stop;
            var sd = stopShown ? DisplayStyle.Flex : DisplayStyle.None;
            if (stopBtn.style.display != sd) stopBtn.style.display = sd;
            if (stopBtn.text != stopText) stopBtn.text = stopText;
            // Stop alone keeps its own width, not the whole row.
            stopBtn.style.flexGrow = mode == Main.None ? 0f : 1f;

            var rd = mode != Main.None || stopShown ? DisplayStyle.Flex : DisplayStyle.None;
            if (thumbRow != null && thumbRow.style.display != rd) thumbRow.style.display = rd;
        }

        /// The fix for a locked recipe, as the primary: the fire too low ->
        /// "Raise Campfire to II"; this building's level too low -> "Raise to
        /// level 2" when it can be paid, else the upgrade's own fix (the item
        /// it lacks most, or the fire); a tool missing -> make it. None when
        /// nothing can be pressed (the detail's line says why).
        Main LockFix(OutpostLedger l, Recipe r, out ShortFix.Fix fix, out string text)
        {
            fix = default; text = null;
            if (l.CampfireLevel < r.campfireLevel)
                fix = ShortFix.RaiseFire(outpost, r.makes);
            else if (MyLevel(l) < r.stationLevel)
            {
                var next = l.NextUpgradeAt(raisedIndex, planId);
                if (next != null && l.CanUpgradeAt(raisedIndex, planId, out _))
                {
                    text = "Raise to level " + next.toLevel;
                    return Main.Upgrade;
                }
                if (next != null)
                {
                    if (l.CampfireLevel < next.campfireLevel) fix = ShortFix.RaiseFire(outpost);
                    else
                    {
                        var most = new ShortFix.Most();
                        foreach (var c in next.cost) most.Add(c.res, c.n - l.SpendableOf(c.res));
                        if (most.Res != null) fix = ShortFix.For(outpost, most.Res);
                    }
                }
            }
            else if (r.tool != null && !l.Holds(r.tool))
                fix = ShortFix.For(outpost, r.tool);
            if (!fix.Valid) return Main.None;
            text = fix.label;
            return Main.Fix;
        }

        // --- words ---------------------------------------------------------------------------

        /// One batch's time on the bench at THIS building's level, from the
        /// same numbers the ledger works with (`ratePerDay` × level, a
        /// `TimeOfDay.WorkDaySeconds` day -- the fixed 180 s the recipes are
        /// priced in, so a longer sky day never slows a bench): "15 s each",
        /// "45 s for 3".
        string EachTime(Recipe r, int level)
        {
            float rate = r.ratePerDay * Techs.RateMul(planId, level);
            if (rate <= 0f) return "";
            int y = Mathf.Max(1, r.yield);
            float secs = TimeOfDay.WorkDaySeconds * y / rate;
            string t = secs < 90f ? $"{Mathf.RoundToInt(secs)} s" : $"{secs / 60f:0.#} min";
            return y > 1 ? $"{t} for {y}" : $"{t} each";
        }

        /// A day's food in words: "½ day", "a day", "1¼ days".
        static string FillWords(float f)
        {
            if (f <= 0.001f) return null;
            if (Mathf.Abs(f - 1f) < 0.01f) return "a day";
            int whole = Mathf.FloorToInt(f + 0.01f);
            float frac = f - whole;
            string part = Mathf.Abs(frac) < 0.01f ? ""
                : Mathf.Abs(frac - 0.25f) < 0.01f ? "¼"
                : Mathf.Abs(frac - 0.5f) < 0.01f ? "½"
                : Mathf.Abs(frac - 0.75f) < 0.01f ? "¾" : null;
            if (part == null) return $"{f:0.##} day";
            if (whole == 0) return part + " day";
            return whole + part + " days";
        }

        /// Goods that read as a mass, not a count ("the timber runs out").
        static bool Mass(string s)
        {
            switch (s)
            {
                case "timber": case "stone": case "ore": case "flour": case "meat": case "fish":
                case "wheat": case "iron": case "food": case "game": case "hide":
                    return true;
            }
            return false;
        }

        static string Plural(string s)
        {
            if (string.IsNullOrEmpty(s) || Mass(s) || s.EndsWith("s")) return s;
            if (s.EndsWith("o")) return s + "es";
            return s + "s";
        }

        /// "the potatoes run out" / "the timber runs out".
        static string RunsOut(string s) =>
            Mass(s) ? $"the {s} runs out" : $"the {Plural(s)} run out";

        /// A spot's drawn glyph, by its name.
        static string SpotGlyph(string spot)
        {
            string s = (spot ?? "").ToLowerInvariant();
            if (s.Contains("grill")) return "grill";
            if (s.Contains("cauldron") || s.Contains("pot")) return "cauldron";
            if (s.Contains("smelt") || s.Contains("furnace")) return "smelter";
            if (s.Contains("forge") || s.Contains("anvil")) return "anvil";
            return "spot";
        }

        // --- work: in and out ------------------------------------------------------

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

        /// The first spot that has a recipe set (the one the in-and-out card
        /// follows), and its index into `spotNames`; -1 when none has.
        int BusySpot(StationStock st)
        {
            for (int i = 0; i < spotNames.Count; i++) if (HasRecipe(SpotOf(st, i))) return i;
            return -1;
        }

        /// **In and out (2026-09-30, per spot).** Bay -> the working spot's
        /// batch -> rack. The bench cell follows the viewed spot when it has
        /// a recipe, else the first spot that does, and names it on a
        /// two-spot station ("grill").
        void FillFlow(OutpostLedger l, StationStock st, OutpostHand hand, Recipe tapped)
        {
            if (bayValue == null) return;
            int bi = HasRecipe(SpotOf(st, viewSpot)) ? viewSpot : BusySpot(st);
            var sp = bi >= 0 ? SpotOf(st, bi) : null;
            var r = (HasRecipe(sp) ? Recipes.Named(sp.recipeId) : null) ?? tapped ?? Recipes.Default(planId);
            string input = r != null && r.takes.Length > 0 ? r.takes[0].res : null;
            string output = r?.makes;
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

            float p = sp != null ? Mathf.Clamp01(sp.progress01) : 0f;
            ring.Value = sp != null && sp.Running ? p : 0f;
            benchValue.text = sp == null ? "idle" : $"{Mathf.RoundToInt(p * 100f)}%";
            benchLabel.text = multiSpot && bi >= 0 ? (spotNames[bi] ?? "").ToLowerInvariant() : "on the bench";
            // **The fisher fishes at the water (2026-09-30, 0a17d6c).** The
            // fishing hut's bench is never used -- a catch is a trip from
            // the shore into the box (`OutpostLedger.FishesAtShore`) -- so
            // "empty" there was a lie while he stood at the water.
            bool shore = OutpostLedger.FishesAtShore(st);
            benchValue.EnableInClassList("st-flow-value--words", shore || sp == null);
            if (shore)
            {
                ring.Value = 0f;
                bool fishing = hand != null && hand.Hauling && hand.haulFrom == HaulPlace.Shore;
                benchValue.text = fishing ? "at the shore" : hand != null ? "at the hut" : "no fisher";
                benchLabel.text = "fishing";
            }
            rackValue.text = $"{st.RackTotal} / {st.OutputCap}";
            rackValue.EnableInClassList("st-cost--short", st.RackFull);

            string why = StallText(l, hand, BusySpot(st) >= 0);
            stallLine.text = why;
            stallLine.style.display = string.IsNullOrEmpty(why) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// The ledger's `StallReason` for the hand on it; with nobody on it,
        /// a spot that has a recipe waiting for a hand says so. Empty when
        /// working.
        static readonly List<string> allSpotLines = new List<string>(2);

        internal static string StallText(OutpostLedger l, OutpostHand hand, bool wantsHand)
        {
            if (hand != null)
            {
                // **One line per stalled spot (2026-10-04)**: a kitchen with
                // the grill AND the cauldron short says both, worst first,
                // each whole (the label wraps; the alert chip says the worst
                // and "+N more").
                var all = allSpotLines;
                string why = l.StallReasonAll(hand, all);
                if (string.IsNullOrEmpty(why)) return "";
                if (all.Count < 2) return "stopped · " + why;
                var sb = new System.Text.StringBuilder(160);
                for (int i = 0; i < all.Count; i++)
                    sb.Append(i > 0 ? "\nstopped · " : "stopped · ").Append(all[i]);
                return sb.ToString();
            }
            return wantsHand ? "stopped · nobody working it" : "";
        }

        // --- the store's racks -----------------------------------------------------

        readonly List<string> held = new List<string>();

        /// Fills the tiles with the fullest piles first (ties keep the
        /// ledger's order); tiles past the last kind are hidden, and the
        /// "All stores" link says how many kinds there are when they do not
        /// all fit.
        void FillStore(OutpostLedger l)
        {
            if (storeTiles == null) return;
            held.Clear();
            foreach (var s in l.stores)
                if (s != null && !string.IsNullOrEmpty(s.resource) && l.ShownStoreCount(s.resource) > 0) held.Add(s.resource);
            // Insertion sort by count, descending: a handful of kinds, no allocation.
            for (int i = 1; i < held.Count; i++)
            {
                var r = held[i]; int c = l.ShownStoreCount(r); int j = i - 1;
                while (j >= 0 && l.ShownStoreCount(held[j]) < c) { held[j + 1] = held[j]; j--; }
                held[j + 1] = r;
            }
            // True counts only (Kevin 2026-10-03, infinite stacking): no
            // "/ ceiling", no full state; the bar is the visible slots' fill.
            for (int i = 0; i < storeTiles.Length; i++)
            {
                var t = storeTiles[i];
                bool show = i < held.Count;
                var want = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (t.root.style.display != want) t.root.style.display = want;
                if (!show) continue;
                string res = held[i];
                string have = l.StoreCountText(res);   // "1 (90%)" for a worn blade
                StationPage.SetIcon(t.icon, res);
                t.res = res;
                string text = ItemIconSet.Get(res) == null ? $"{ResDefs.Label(res)} {have}" : have;
                if (t.count.text != text) t.count.text = text;
                t.fill.style.width = Length.Percent(l.Fill01(res) * 100f);
                t.root.EnableInClassList("st-store-tile--full", false);
            }
            string holds = held.Count == 0 ? "The store is empty" : "Kept in sacks, racks and bays";
            if (storeHolds.text != holds) storeHolds.text = holds;
            string all = held.Count > storeTiles.Length ? $"All {held.Count} kinds ›" : "All stores ›";
            if (storeAll.text != all) storeAll.text = all;
        }

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
            var tapped = hasMake ? Tapped(l, st, viewSpot) : null;

            if (header != null)
            {
                if (hasMake)
                {
                    // "Level 1 · Ada works both" (Kevin's mockup: who is on it
                    // is what a station's owner wants at a glance).
                    string who = hand == null ? "no worker"
                        : $"{(string.IsNullOrEmpty(hand.name) ? "a hand" : hand.name)} works "
                          + (!multiSpot ? "here" : spotNames.Count == 2 ? "both" : "all");
                    header.SetSub($"Level {MyLevel(l)} · {who}");
                }
                else header.SetSub($"Level {MyLevel(l)} · {StationPage.IslandName(outpost)}");
                header.Refresh();
            }
            worker?.Update(l, hand, 0);
            runners?.Update(l);
            if (makeBuilt) FillMake(l, st, hand, tapped);
            FillFlow(l, st, hand, tapped);
            FillStore(l);
            upgrade?.Update(l, raisedIndex, planId, MyLevel(l), GoalPin.IsUpgradePinned(outpost, raisedIndex, planId));
        }

        // --- level: the upgrade --------------------------------------------------------

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
        /// goal (the overview and the Next card chase it); a second tap
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
        /// The Melvor screen's amber (a running spot's glyph), 2026-09-30.
        public static readonly Color Amber = new Color32(242, 196, 109, 255);

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
            // **Not in a hugging frame (2026-09-30).** There the host reads
            // the page's natural height to size the card
            // (`SheetHost.HugsContent`); a page pinned to its parent's height
            // would measure as whatever the card already was, and never shrink.
            if (Hugging) return;
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

        /// True while the open sheet's frame hugs its content (phone, land
        /// HUD, a structure's sheet -- `SheetHost.HugsContent`). Read while a
        /// sheet builds: the host decides it before `Build` runs.
        public static bool Hugging => SheetHost.HugsContent(Sheets.Current);

        /// **The page's column**: straight into `root` in a hugging frame (it
        /// is measured at its natural height and pages instead of scrolling),
        /// otherwise inside the safety-net ScrollView the tall page had.
        public static VisualElement Column(VisualElement root)
        {
            var col = new VisualElement();
            col.AddToClassList("st-content");
            if (Hugging) { root.Add(col); return col; }
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("st-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            root.Add(scroll);
            scroll.Add(col);
            return col;
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

        /// ☰: the Camp sheet (`CampPages.OpenLedger`, the one chokepoint) --
        /// never a dead button.
        public static void OpenLedgerFor(Outpost camp) => CampPages.OpenLedger(null, camp);

        public static string IslandName(Outpost o)
        {
            if (o == null || o.Island == null) return "";
            return o.Island.IsHome ? "home island" : o.Island.DisplayName;
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

                // **Hugging, 2026-09-30 (Kevin: "it isn't obvious where to
                // press").** Half a phone wide row held ☰ + name + pill + Move
                // + ✕ and the NAME lost: "Sa...". So in the hugging frame the
                // ☰ goes -- an icon-only button whose only label was a
                // tooltip the phone never shows, for a ledger the Camp tab
                // already opens -- and the status pill drops under the name,
                // beside "Level 1 · island". Tall/desk header unchanged.
                bool hug = Hugging;
                menu = new Button(() => onMenu?.Invoke()) { text = "" };
                menu.AddToClassList("st-square");
                menu.tooltip = "Open the ledger";
                menu.Add(new Glyph("menu", Ink, "st-glyph"));
                if (!hug) Root.Add(menu);

                var words = new VisualElement(); words.AddToClassList("st-head-words");
                if (hug) words.style.marginLeft = 0f;
                words.Add(Text(title, "st-title"));
                sub = Text("", "st-sub");
                // The sub line wraps inside its own column on every shape. Left
                // to its default it kept one line and ran on under the Move
                // button on the 400 px desk column (2026-10-04, 1920x1080).
                sub.style.whiteSpace = WhiteSpace.Normal;

                pill = new VisualElement(); pill.AddToClassList("st-pill");
                pill.pickingMode = PickingMode.Ignore;
                pillText = Text("", "st-pill-text");
                pill.Add(pillText);
                pill.style.display = withPill ? DisplayStyle.Flex : DisplayStyle.None;
                if (hug)
                {
                    var line = new VisualElement();
                    line.style.flexDirection = FlexDirection.Row;
                    line.style.alignItems = Align.Center;
                    line.style.marginTop = 2f;
                    pill.style.marginRight = 8f;
                    line.Add(pill);
                    sub.style.flexShrink = 1f;
                    sub.style.minWidth = 0f;
                    // Wraps, never cut (2026-10-02 rule: no ellipsis in UI text).
                    sub.style.whiteSpace = WhiteSpace.Normal;
                    line.style.minWidth = 0f;
                    line.Add(sub);
                    words.Add(line);
                    Root.Add(words);
                }
                else
                {
                    words.Add(sub);
                    Root.Add(words);
                    Root.Add(pill);
                }

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
            readonly System.Func<StationStock> stationOf;
            readonly Label initial, name, sub;
            readonly Button main, off, person;
            OutpostHand current;

            public WorkerCard(Outpost o, string planId, System.Action changed, System.Func<StationStock> stationOf = null)
            {
                outpost = o;
                this.planId = planId;
                this.changed = changed;
                this.stationOf = stationOf;
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
                main.AddToClassList("st-worker-assign");
                Root.Add(main);
                off = new Button(Off) { text = "Unassign" };
                off.AddToClassList("st-btn");
                Root.Add(off);
            }

            public void Update(OutpostLedger l, OutpostHand hand, int others)
            {
                current = hand;
                var free = l.FreeHandFor(planId);
                if (hand != null)
                {
                    string n = string.IsNullOrEmpty(hand.name) ? "?" : hand.name;
                    initial.text = n.Substring(0, 1).ToUpperInvariant();
                    name.text = n;
                    // **"Waiting for a runner" (2026-10-02):** a worker whose
                    // goods are on a runner's barrow says so in amber, not
                    // "role · mood", so a stopped bench has a reason.
                    string handWord = l.StatusWord(hand);
                    bool waiting = handWord == "Waiting for a runner";
                    // No recipe chosen (2026-10-02): the same amber, and the fix.
                    bool idleBench = handWord == "Idle at the bench";
                    sub.text = waiting ? "Waiting for a runner"
                        : idleBench ? "Idle at the bench · pick a recipe"
                        : $"{role} · {hand.MoodWord}" + (others > 0 ? $" · +{others} more" : "");
                    sub.style.color = waiting || idleBench ? new StyleColor(Amber) : new StyleColor(StyleKeyword.Null);
                    main.text = "Swap";
                    main.style.display = free != null ? DisplayStyle.Flex : DisplayStyle.None;
                    main.SetEnabled(free != null);
                    off.style.display = DisplayStyle.Flex;
                    person.SetEnabled(true);
                }
                else
                {
                    initial.text = "?";
                    name.text = "No worker";
                    sub.text = free != null ? $"needs a {role}" : "no free hand to assign";
                    sub.style.color = new StyleColor(StyleKeyword.Null);
                    main.text = "Assign free hand";
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
                var station = stationOf?.Invoke();
                if (stationOf != null && (station == null || station.removed)) return;
                var free = l.FreeHandFor(station != null ? station.planId : planId);
                if (free == null) return;
                var previous = station != null ? WorkerAt(l, station) : current;
                // **One worker per station (2026-10-01):** the old worker
                // steps off first so the seat is free; a refused assignment
                // puts him straight back, so it still changes nobody.
                if (previous != null) outpost.OrderIdle(previous, reserve: false);
                bool assigned = station != null ? outpost.Assign(free, station) : outpost.Assign(free, planId);
                if (!assigned && previous != null)
                {
                    if (station != null) outpost.Assign(previous, station); else outpost.Assign(previous, planId);
                }
                changed?.Invoke();
            }

            void Off()
            {
                if (outpost == null) return;
                var station = stationOf?.Invoke();
                if (stationOf != null && (station == null || station.removed)) return;
                var hand = station != null ? WorkerAt(outpost.Ledger, station) : current;
                if (hand == null) return;
                outpost.OrderIdle(hand, reserve: false);
                changed?.Invoke();
            }

            static OutpostHand WorkerAt(OutpostLedger ledger, StationStock station)
            {
                if (ledger == null) return null;
                foreach (var hand in ledger.hands)
                    if (hand != null && ledger.StationOfHand(hand) == station) return hand;
                return null;
            }
        }

        // --- the upgrade card --------------------------------------------------

        /// **The upgrade card (rebuilt 2026-09-30, Kevin: "two button-looking
        /// things compete, so it's unclear what to press").** "Level 2 ·
        /// +10 stores", the price as icon + have/need chips (ember where
        /// short), then EXACTLY ONE button:
        /// <list type="bullet">
        /// <item>affordable: **"Raise to level N"** (amber, the primary);</item>
        /// <item>short or fire-locked: **the fix** (`ShortFix`: "Make brick",
        /// "Gather timber", "Raise Campfire to II", "Build a kiln"...);</item>
        /// <item>short with no fix at all: no button, one plain ember line.</item>
        /// </list>
        /// "Set as goal" is a text link under it, never a second button.
        /// The chip row is rebuilt only when the level changes -- a tap.
        public sealed class UpgradeCard
        {
            public readonly VisualElement Root;
            readonly Label title;
            readonly VisualElement cost;
            readonly Button btn;
            /// A short upgrade with no fix to press: plain ember words.
            readonly Label status;
            /// "Set as goal" (GoalPin): a link while the upgrade cannot be
            /// paid, so the camp overview and the Next card can chase it.
            readonly Button pinBtn;
            readonly List<(Label label, string res, int n)> lines = new List<(Label, string, int)>();
            int builtLevel = -1;
            /// **The fix for a short upgrade (2026-09-30, island UI rule 2):**
            /// the one button under the price for the item lacking most, or
            /// the fire when it is too low. Only when the caller passes its camp.
            readonly ShortFix.Slot fixSlot;
            readonly Outpost camp;

            public UpgradeCard(System.Action upgrade, System.Action pin = null, Outpost camp = null)
            {
                this.camp = camp;
                Root = Card();
                Root.AddToClassList("st-upgrade");
                var words = new VisualElement(); words.AddToClassList("st-upgrade-words");
                title = Text("", "st-upgrade-title");
                words.Add(title);
                cost = new VisualElement(); cost.AddToClassList("st-cost");
                words.Add(cost);
                Root.Add(words);

                if (camp != null)
                {
                    fixSlot = new ShortFix.Slot();
                    Root.Add(fixSlot.button);
                }
                btn = new Button(upgrade) { text = "Raise" };
                btn.AddToClassList("st-btn");
                btn.AddToClassList("st-upgrade-btn");
                btn.AddToClassList("st-upgrade-btn--go");
                Root.Add(btn);
                status = Text("", "st-upgrade-status");
                status.style.display = DisplayStyle.None;
                Root.Add(status);
                if (pin != null)
                {
                    pinBtn = new Button(pin) { text = "Set as goal" };
                    pinBtn.AddToClassList("st-link");
                    pinBtn.AddToClassList("st-pin");
                    pinBtn.style.display = DisplayStyle.None;
                    Root.Add(pinBtn);
                }
            }

            public void Update(OutpostLedger l, int raisedIndex, string planId, int level, bool pinned = false)
            {
                var next = l.NextUpgradeAt(raisedIndex, planId);
                string why = null;
                bool can = next != null && l.CanUpgradeAt(raisedIndex, planId, out why);
                Show(l, next, level, can, why, pinned);
            }

            /// The same card for a step that is not a `raised` row's: a wall
            /// segment's (2026-10-03, `WallSheet`), priced by its length.
            /// `can` / `why` are the caller's own answer to "can it go up".
            public void Show(OutpostLedger l, UpgradeStep next, int level, bool can, string why, bool pinned = false)
            {
                if (next == null)
                {
                    fixSlot?.Bind(camp, default(ShortFix.Fix));
                    if (pinBtn != null) pinBtn.style.display = DisplayStyle.None;
                    status.style.display = DisplayStyle.None;
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
                        var chip = new VisualElement(); chip.AddToClassList("st-chip");
                        chip.pickingMode = PickingMode.Ignore;
                        // An item without an icon says its name instead of a blank square.
                        if (ItemIconSet.Get(c.res) != null) chip.Add(Icon(c.res, "st-small-icon"));
                        var lab = Text("", "st-cost-text");
                        chip.Add(lab);
                        cost.Add(chip);
                        lines.Add((lab, c.res, c.n));
                    }
                }
                foreach (var (label, res, n) in lines)
                {
                    int have = l.SpendableOf(res);
                    string t = ItemIconSet.Get(res) != null
                        ? $"{Mathf.Min(have, 9999)}/{n}"
                        : $"{Mathf.Min(have, 9999)}/{n} {ResDefs.Label(res)}";
                    if (label.text != t) label.text = t;
                    label.EnableInClassList("st-cost--ok", have >= n);
                    label.EnableInClassList("st-cost--short", have < n);
                }

                var fix = default(ShortFix.Fix);
                if (!can && fixSlot != null)
                {
                    if (l.CampfireLevel < next.campfireLevel) fix = ShortFix.RaiseFire(camp);
                    else
                    {
                        var most = new ShortFix.Most();
                        foreach (var (_, res, n) in lines) most.Add(res, n - l.SpendableOf(res));
                        if (most.Res != null) fix = ShortFix.For(camp, most.Res);
                    }
                }
                fixSlot?.Bind(camp, fix);

                // Exactly one of: the primary, the fix, or (no fix exists) plain words.
                btn.style.display = can ? DisplayStyle.Flex : DisplayStyle.None;
                if (can) btn.text = "Raise to level " + next.toLevel;
                bool words = !can && !fix.Valid;
                status.style.display = words ? DisplayStyle.Flex : DisplayStyle.None;
                if (words) status.text = Short(l, next, why);

                if (pinBtn != null)
                {
                    pinBtn.style.display = can ? DisplayStyle.None : DisplayStyle.Flex;
                    string pt = pinned ? "Goal set · clear" : "Set as goal";
                    if (pinBtn.text != pt) pinBtn.text = pt;
                    pinBtn.EnableInClassList("st-pin--on", pinned);
                }
            }

            static string Short(OutpostLedger l, UpgradeStep next, string why)
            {
                if (l.CampfireLevel < next.campfireLevel) return "Needs Campfire " + RecipeGraph.Roman(next.campfireLevel);
                var missing = Cost.Missing(next.cost, l.SpendableOf);
                if (missing.Count > 0) return "Need " + ResDefs.Counted(missing[0].res, missing[0].n);
                return string.IsNullOrEmpty(why) ? "Not yet" : Cap(why);
            }

            static string Effect(UpgradeStep step)
            {
                var parts = new List<string>(3);
                if (Mathf.Abs(step.rateMul - 1f) > 0.001f) parts.Add($"{step.rateMul:0.#}× faster");
                if (step.storeBonus != 0) parts.Add($"+{step.storeBonus} stores");
                // The store hut's runner posts and perks (2026-10-04).
                string runnerWords = OutpostLedger.RunnerUpgradeWords(step.planId, step.toLevel);
                if (runnerWords != null) parts.Add(runnerWords);
                if (step.housesBonus != 0) parts.Add(step.housesBonus == 1 ? "+1 bed" : $"+{step.housesBonus} beds");
                // The level 2 tower's whole point (2026-10-01).
                if (parts.Count == 0 && step.planId == OutpostLedger.WatchtowerId) return "bigger gun deck";
                // A wall or gate (2026-10-03, `WallUpgrades`).
                if (parts.Count == 0 && (step.planId == BuildPlans.Palisade.id || step.planId == BuildPlans.Gate.id))
                    return $"stone, +{Mathf.RoundToInt((WallSegment.Level2HpMultiplier - 1f) * 100f)}% strength";
                return parts.Count > 0 ? string.Join(" · ", parts) : "stronger";
            }
        }

        // --- drawn pieces --------------------------------------------------------

        /// ☰, ✕ and → drawn with `Painter2D`, so no font has to carry them.
        public sealed class Glyph : VisualElement
        {
            readonly string kind;
            Color color;

            /// The kinds this draws (anything else draws the arrow). A caller
            /// handed a string that may be a kind or plain text asks here.
            public static bool Knows(string kind) => kind != null && Kinds.Contains(kind);

            static readonly HashSet<string> Kinds = new HashSet<string>
            {
                "menu", "close", "wall", "ladder", "road", "chart", "fire", "pier", "ship",
                "grill", "cauldron", "smelter", "anvil", "lock", "plus", "spot", "clock", "grave", "arrow",
                "check", "hammer", "axe", "pick", "bow", "wheat", "eye", "gear", "flag", "anchor", "pencil",
            };

            public Glyph(string kind, Color color, string cls)
            {
                this.kind = kind;
                this.color = color;
                AddToClassList(cls);
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            /// Re-tints the stroke; repaints only when it changed (the spot
            /// tiles' glyph goes amber while the spot works, 2026-09-30).
            public void SetColor(Color c)
            {
                if (c == color) return;
                color = c;
                MarkDirtyRepaint();
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
                    // **The station spots (Melvor screen, 2026-09-30)**: the
                    // grill, the cauldron, the smelter and the forge's anvil;
                    // "spot" (a ring) for any other; "lock" on a locked tile.
                    case "grill":
                        Line(3, 11, 21, 11); Line(5, 11, 5, 14); Line(19, 11, 19, 14);
                        Line(7, 6, 8, 8.5f); Line(12, 5, 12, 8.5f); Line(17, 6, 16, 8.5f);
                        Line(8, 18, 16, 18);
                        break;
                    case "cauldron":
                        Line(3, 10, 21, 10); Line(4.5f, 10, 6.5f, 19); Line(19.5f, 10, 17.5f, 19);
                        Line(6.5f, 19, 17.5f, 19);
                        Line(9, 7, 10, 4); Line(14, 7, 15, 4);
                        break;
                    case "smelter":
                        Line(5, 20, 5, 9); Line(19, 20, 19, 9); Line(5, 9, 12, 4); Line(19, 9, 12, 4);
                        Line(4, 20, 20, 20);
                        Line(9, 20, 9, 15); Line(15, 20, 15, 15); Line(9, 15, 15, 15);
                        break;
                    case "anvil":
                        Line(3, 8, 20, 8); Line(3, 8, 7, 11.5f); Line(7, 11.5f, 20, 11.5f); Line(20, 8, 20, 11.5f);
                        Line(10, 11.5f, 10, 16); Line(15, 11.5f, 15, 16);
                        Line(10, 16, 7, 19.5f); Line(15, 16, 18, 19.5f); Line(7, 19.5f, 18, 19.5f);
                        break;
                    case "lock":
                        Line(6, 11, 18, 11); Line(18, 11, 18, 20); Line(18, 20, 6, 20); Line(6, 20, 6, 11);
                        Line(8.5f, 11, 8.5f, 8); Line(15.5f, 11, 15.5f, 8);
                        p.BeginPath();
                        p.Arc(new Vector2(12 * s, 8 * s), 3.5f * s, 180f, 360f);
                        p.Stroke();
                        break;
                    // An empty farm plot (2026-10-04): "+", choose a crop.
                    case "plus": Line(12, 5, 12, 19); Line(5, 12, 19, 12); break;
                    // **The glyphs that were font characters (2026-10-04).**
                    // Nunito has no ✓ ⚒ ⚓ ✎ 🔥 ⚙ 👁 🏹 ⛏ 🪓 🌾 ⚑ ◆, and the
                    // phone draws a missing glyph as tofu ("□"), so every
                    // sheet badge and token mark is drawn here instead.
                    case "check": Line(5, 13, 10, 18); Line(10, 18, 19, 7); break;
                    case "hammer":
                        Line(5, 20, 13, 12);
                        Line(9, 8, 15, 14); Line(12, 5, 18, 11); Line(9, 8, 12, 5); Line(15, 14, 18, 11);
                        break;
                    case "axe":
                        Line(6, 20, 15, 6);
                        Line(13, 4, 19, 7); Line(19, 7, 17, 12); Line(17, 12, 14, 9);
                        break;
                    case "pick":
                        Line(13, 9, 6, 20);
                        Line(4, 10, 12, 5); Line(12, 5, 20, 10);
                        break;
                    case "bow":
                        p.BeginPath();
                        p.Arc(new Vector2(7 * s, 12 * s), 9f * s, -60f, 60f);
                        p.Stroke();
                        Line(11.5f, 4.2f, 11.5f, 19.8f);
                        Line(4, 12, 20, 12); Line(20, 12, 17, 9.5f); Line(20, 12, 17, 14.5f);
                        break;
                    case "wheat":
                        Line(12, 21, 12, 5);
                        Line(12, 9, 8.5f, 6); Line(12, 9, 15.5f, 6);
                        Line(12, 13, 8.5f, 10); Line(12, 13, 15.5f, 10);
                        Line(12, 17, 8.5f, 14); Line(12, 17, 15.5f, 14);
                        break;
                    case "eye":
                        p.BeginPath();
                        p.MoveTo(new Vector2(3 * s, 12 * s));
                        p.BezierCurveTo(new Vector2(8 * s, 5 * s), new Vector2(16 * s, 5 * s), new Vector2(21 * s, 12 * s));
                        p.BezierCurveTo(new Vector2(16 * s, 19 * s), new Vector2(8 * s, 19 * s), new Vector2(3 * s, 12 * s));
                        p.Stroke();
                        p.BeginPath();
                        p.Arc(new Vector2(12 * s, 12 * s), 2.6f * s, 0f, 360f);
                        p.Stroke();
                        break;
                    case "gear":
                        p.BeginPath();
                        p.Arc(new Vector2(12 * s, 12 * s), 5f * s, 0f, 360f);
                        p.Stroke();
                        Line(12, 3, 12, 6.5f); Line(12, 17.5f, 12, 21); Line(3, 12, 6.5f, 12); Line(17.5f, 12, 21, 12);
                        Line(5.6f, 5.6f, 8.1f, 8.1f); Line(15.9f, 15.9f, 18.4f, 18.4f);
                        Line(18.4f, 5.6f, 15.9f, 8.1f); Line(8.1f, 15.9f, 5.6f, 18.4f);
                        break;
                    case "flag": Line(6, 21, 6, 3); Line(6, 4, 18, 7.5f); Line(18, 7.5f, 6, 11); break;
                    case "anchor":
                        Line(12, 6, 12, 20); Line(8, 9, 16, 9);
                        p.BeginPath();
                        p.Arc(new Vector2(12 * s, 13 * s), 7f * s, 0f, 180f);
                        p.Stroke();
                        p.BeginPath();
                        p.Arc(new Vector2(12 * s, 4 * s), 2f * s, 0f, 360f);
                        p.Stroke();
                        break;
                    case "pencil":
                        Line(4, 20, 5.5f, 14.5f); Line(4, 20, 9.5f, 18.5f);
                        Line(5.5f, 14.5f, 15.5f, 4.5f); Line(9.5f, 18.5f, 19.5f, 8.5f); Line(15.5f, 4.5f, 19.5f, 8.5f);
                        break;
                    case "spot":
                        p.BeginPath();
                        p.Arc(new Vector2(12 * s, 12 * s), 6.5f * s, 0f, 360f);
                        p.Stroke();
                        break;
                    case "clock":
                        p.BeginPath();
                        p.Arc(new Vector2(12 * s, 12 * s), 8f * s, 0f, 360f);
                        p.Stroke();
                        Line(12, 12, 12, 6); Line(12, 12, 16.5f, 14.5f);
                        break;
                    // The grave sheet (2026-09-30): a headstone with a cross.
                    case "grave":
                        Line(7, 20, 7, 10); Line(17, 20, 17, 10); Line(4, 20, 20, 20);
                        p.BeginPath();
                        p.Arc(new Vector2(12 * s, 10 * s), 5f * s, 180f, 360f);
                        p.Stroke();
                        Line(12, 9, 12, 16); Line(9.5f, 11.5f, 14.5f, 11.5f);
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
