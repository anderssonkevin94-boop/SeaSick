using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **Every building's sheet, one template, 2026-09-23.**
    ///
    /// Kevin approved it: the sheet reads top to bottom as the material flow,
    /// the player's one decision in the middle --
    ///
    /// 1. **the worker** -- who is on it, post a hand / stand them down;
    /// 2. **coming in** -- the bay per input and the bench;
    /// 3. **making** -- the running order and STOP, then the recipes, a
    ///    picked one opening the ∞/5/10/20/50/100 chips;
    /// 4. **going out** -- the rack and where it goes;
    /// 5. **why it's stopped**, when it is -- the ledger's `StallReason`;
    /// 6. **upgrade** -- the next level and what it costs.
    ///
    /// A building without a row skips it (a store has no worker, a hut
    /// only an upgrade). **Rows that do not fit one page PAGE** -- the frame
    /// has no scroll (`SheetHost`): `Plan` lays the rows out in order into
    /// as many band-high pages as they take, so the forge's five recipes are
    /// "make 1/2", "make 2/2" on a phone and the same sheet is one or two
    /// pages on a desk. Every height it adds up is `SheetKit`'s (the USS,
    /// rounded up) or a height this file sets itself.
    ///
    /// **It reads and it calls; it never decides.** Every number comes off
    /// `OutpostLedger`; every button is a verb the ledger or `Outpost`
    /// already had (`PlaceOrder`, `StopOrder`, `Assign`, `OrderIdle`,
    /// `Upgrade`).
    ///
    /// **Built once, updated in place (1cf73f9).** `SheetHost` calls
    /// `Refresh` every 0.25 s whatever the thumb is doing, and a UI Toolkit
    /// click needs its press and release on the SAME element -- so nothing a
    /// finger can land on is rebuilt on that timer. Rows are made in `Build`
    /// (and when a TAP changes their shape) and only re-texted after.
    public class StationSheet : ISheetFramed
    {
        readonly Outpost outpost;
        readonly Building building;
        readonly string planId;
        readonly BuildPlan plan;
        readonly bool hasMake;
        readonly bool hasUpgrade;
        readonly bool hasWorker;
        readonly bool isStore;

        int tab = -1;

        /// **This built instance's index into `ledger.raised`** (and into
        /// `Outpost.Built` -- `Outpost.Raise` appends to both together and
        /// `Demolish` removes the pair). `OutpostLedger.StationForRaised`
        /// turns it into the one `StationStock` this sheet is about.
        /// **Re-resolved every `Refresh`**: an earlier building demolished
        /// while this card is open moves it, and a stale index reads
        /// somebody else's bay.
        int raisedIndex = -1;

        /// The recipe a tap has picked but not yet ordered -- local to the
        /// card. Reveals the amount chips under that one row.
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
            if (MidnightOverview) tab = 0;
        }

        void ResolveRaisedIndex()
        {
            raisedIndex = -1;
            if (outpost == null || building == null) return;
            var built = outpost.Built;
            for (int i = 0; i < built.Count; i++)
                if (built[i] == building) { raisedIndex = i; break; }
        }

        /// The one `StationStock` this card is about, or null.
        StationStock Station(OutpostLedger l) =>
            l != null && raisedIndex >= 0 ? l.StationForRaised(raisedIndex) : null;

        OutpostLedger L => outpost != null ? outpost.Ledger : null;
        bool MidnightOverview => MidnightLandHud.Active && planId == BuildPlans.Sawmill.id;

        // --- the frame -------------------------------------------------------

        public Color Accent => SheetTheme.Timber;
        public string Title => plan.label;

        public Vector3 AnchorWorld => building != null
            ? building.transform.position
            : (outpost != null ? outpost.CampCentre : Vector3.zero);

        public bool StillValid => outpost != null && outpost.Ledger != null && building != null;

        public int Tab { get { Plan(); return tab; } }
        public void SetTab(int index) { tab = index; }

        public string[] TabLabels { get { Plan(); return labels.Length > 1 ? labels : null; } }

        public VisualElement BuildHeader()
        {
            var l = L;
            int level = l != null ? l.LevelOf(planId) : 1;
            string where = string.IsNullOrEmpty(plan.position) ? plan.label : plan.position;
            return SheetKit.Header($"level {level} · {where}", Title, SheetTheme.Timber, "⚒",
                () => Sheets.Close());
        }

        Button upgradeBtn;

        /// **Raising it is the upgrade row's button**, pinned under the page
        /// that carries the upgrade row and nowhere else. 44 tall like every
        /// other thumb target here; the six units over `ActionsPx` are
        /// counted in `Band`.
        public VisualElement BuildActions()
        {
            upgradeBtn = null;
            var l = L;
            if (l == null || !PageHas(LivePage, KUpgrade) || l.NextUpgrade(planId) == null) return null;
            int next = l.LevelOf(planId) + 1;
            upgradeBtn = SheetKit.Btn($"Raise to level {next}", DoUpgrade, true);
            upgradeBtn.style.height = TouchPx;
            upgradeBtn.SetEnabled(l.CanUpgrade(planId, out _));
            return SheetKit.Actions(upgradeBtn);
        }

        // --- the page plan ---------------------------------------------------

        /// The thumb floor, in panel units -- `SheetKit.Tabs`' 44.
        internal const float TouchPx = 44f;

        const int KWorker = 0, KIn = 1, KMaking = 2, KRecipe = 3, KOut = 4, KStall = 5,
                  KUpgrade = 6, KStore = 7, KFlow = 8;

        struct Blk { public int kind; public int arg; public float px; }

        readonly List<List<Blk>> pages = new List<List<Blk>>();
        string[] labels = { "make" };
        long planKey = long.MinValue;
        long builtKey = long.MinValue;

        // What each row costs, in panel units. Rows this file lays out set
        // their own heights to these; text rows use `SheetKit`'s.
        // **Phone first, measured 2026-09-23:** the portrait band is ~175
        // units (`SheetHost.BandHeight` at 1080x2340), so the live rows wear
        // a small lead label on their left instead of an eyebrow line above.
        internal const float WorkerPx = TouchPx + 4f;           // 44 button + 2 above/below
        const float OrderRowPx = TouchPx + 4f;
        const float MakingPx = OrderRowPx;
        const float RecipePx = 48f;                              // 4 pad, 18 name, 16 line, 4 pad (+6)
        const float ToolLinePx = 14f;
        const float ChipsPx = TouchPx + 8f;                      // 6 above, 2 below
        const float OutPx = SheetKit.TextPx;
        internal const float StallPx = SheetKit.TextPx + 2f;
        internal const float LeadPx = 72f;                       // the lead label's width
        const float UpgradePx = SheetKit.RulePx + SheetKit.EyebrowPx + 3f * SheetKit.TextPx + SheetKit.NotePx;
        const int StoreLinesMax = 5;                             // two kinds a line
        const float StorePx = SheetKit.EyebrowPx + StoreLinesMax * SheetKit.TextPx;

        /// The band a page may fill: the host's, less the upgrade button's
        /// extra height and a few units of honesty for rounding.
        internal static float Band => SheetHost.BandHeight - 2f;

        int MaxInputs
        {
            get
            {
                int n = 0;
                foreach (var r in Recipes.At(planId)) n = Mathf.Max(n, r.takes.Length);
                return Mathf.Max(1, n);
            }
        }

        float InPx => (MaxInputs + 1) * SheetKit.TextPx;

        /// **The rows, in the template's order, poured into band-high pages.**
        /// Keyed on everything that changes a row's height: the band, the
        /// recipe list, the upgrade. None of those move on the timer, so a
        /// page never re-plans under a finger.
        void Plan()
        {
            float band = Band;
            var recipes = hasMake ? Recipes.At(planId) : null;
            int rc = recipes != null ? recipes.Count : 0;
            long key = Mathf.RoundToInt(band) * 1000003L + rc * 131L + MaxInputs * 7L
                       + (hasUpgrade ? 1 : 0) + (hasWorker ? 2 : 0) + (MidnightOverview ? 1000000007L : 0L);
            if (key == planKey && pages.Count > 0) return;
            planKey = key;

            var flow = new List<Blk>();
            if (hasWorker) flow.Add(new Blk { kind = KWorker, px = WorkerPx });
            if (isStore) flow.Add(new Blk { kind = KStore, px = StorePx });
            if (hasMake)
            {
                // **Kevin's order (2026-09-23): in, the ORDER, out, why.** The
                // rows read as the material flow and the one decision the
                // player owns -- the recipe and its chips -- sits in the
                // middle, under the thumb, before what comes out of it.
                flow.Add(new Blk { kind = KIn, px = InPx });
                flow.Add(new Blk { kind = KMaking, px = MakingPx });
                for (int i = 0; i < rc; i++)
                    flow.Add(new Blk { kind = KRecipe, arg = i,
                        px = RecipePx + (recipes[i].tool != null ? ToolLinePx : 0f) });
                flow.Add(new Blk { kind = KOut, px = OutPx });
                flow.Add(new Blk { kind = KStall, px = StallPx });
            }
            if (hasUpgrade) flow.Add(new Blk { kind = KUpgrade, px = UpgradePx });

            pages.Clear();
            var page = new List<Blk>();
            float used = 0f;
            bool chipsReserved = false;
            foreach (var b in flow)
            {
                // A page that holds a recipe holds room for its chips too,
                // so picking one can never push the page past the band.
                float need = b.px + (b.kind == KRecipe && !chipsReserved ? ChipsPx : 0f);
                if (page.Count > 0 && used + need > band)
                {
                    pages.Add(page);
                    page = new List<Blk>();
                    used = 0f;
                    chipsReserved = false;
                    need = b.px + (b.kind == KRecipe ? ChipsPx : 0f);
                }
                if (b.kind == KRecipe) chipsReserved = true;
                page.Add(b);
                used += need;
            }
            if (page.Count > 0) pages.Add(page);
            if (pages.Count == 0) pages.Add(new List<Blk>());
            // Overview has no pinned action row, so it can use that reserved space.
            if (MidnightOverview && band + SheetHost.ActionsPx >= 86f + WorkerPx + StallPx)
                pages.Insert(0, new List<Blk> {
                    new Blk { kind = KFlow, px = 86f },
                    new Blk { kind = KWorker, px = WorkerPx },
                    new Blk { kind = KStall, px = StallPx }
                });

            // Named by what a page opens with; "make 1/2" where there are two.
            var names = new string[pages.Count];
            for (int i = 0; i < pages.Count; i++) names[i] = NameOf(pages[i]);
            if (labels.Length != pages.Count) labels = new string[pages.Count];
            for (int i = 0; i < pages.Count; i++)
            {
                int of = 0, at = 0;
                for (int j = 0; j < pages.Count; j++)
                    if (names[j] == names[i]) { if (j < i) at++; of++; }
                labels[i] = SheetKit.PageLabel(names[i], at, of);
            }
            if (tab >= pages.Count) tab = pages.Count - 1;
        }

        static string NameOf(List<Blk> page)
        {
            if (page.Count == 0) return "make";
            switch (page[0].kind)
            {
                case KFlow: return "overview";
                case KWorker:
                case KIn:
                case KMaking:
                case KOut:
                case KStall: return "work";
                case KRecipe: return "make";
                case KStore: return "store";
                default: return "upgrade";
            }
        }

        List<Blk> LivePage
        {
            get
            {
                Plan();
                return pages[Mathf.Clamp(tab < 0 ? 0 : tab, 0, pages.Count - 1)];
            }
        }

        static bool PageHas(List<Blk> page, int kind)
        {
            foreach (var b in page) if (b.kind == kind) return true;
            return false;
        }

        // --- the pieces kept between refreshes --------------------------------

        VisualElement root;
        WorkerSlot worker;
        Label[] inLines;
        Label benchLine;
        VisualElement orderRow;
        Label orderTitle, orderSub;
        Button orderStop;
        StationStock orderStopStation;
        Label outLine;
        Label stallLine;
        Label[] storeLines;
        Label upgradeLevel;
        Label flowInput, flowOutput, flowState, flowInputName, flowOutputName;
        VisualElement flowProgress;
        VisualElement upgradeHolder;
        long upgradeKey = long.MinValue;

        /// One row per recipe on the live page, built once per `Build` and
        /// updated in place after -- see `RecipeRowRefs`.
        readonly Dictionary<string, RecipeRowRefs> recipeRows = new Dictionary<string, RecipeRowRefs>();

        public VisualElement Build()
        {
            root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            Fill();
            Refresh();
            return root;
        }

        /// The live page's rows, from nothing. Only ever called from `Build`
        /// (a tab change) or when the plan itself moved (the band changed
        /// shape) -- never because a number did.
        void Fill()
        {
            worker = null; inLines = null; benchLine = null;
            flowInput = flowOutput = flowState = flowInputName = flowOutputName = null; flowProgress = null;
            orderRow = null; orderTitle = null; orderSub = null; orderStop = null; orderStopStation = null;
            outLine = null; stallLine = null; storeLines = null;
            upgradeLevel = null; upgradeHolder = null; upgradeKey = long.MinValue;
            recipeRows.Clear();
            root.Clear();
            builtKey = planKey;

            var page = LivePage;
            foreach (var b in page)
            {
                switch (b.kind)
                {
                    case KFlow: BuildFlow(); break;
                    case KWorker:
                        worker = new WorkerSlot(outpost, planId, () => Refresh());
                        root.Add(worker.Root);
                        break;
                    case KIn: BuildIn(); break;
                    case KMaking: BuildMaking(); break;
                    case KRecipe:
                        var r = Recipes.At(planId)[b.arg];
                        var refs = BuildRecipeRow(r);
                        recipeRows[r.id] = refs;
                        root.Add(refs.row);
                        break;
                    case KOut: BuildOut(); break;
                    case KStall: BuildStall(); break;
                    case KStore: BuildStore(); break;
                    case KUpgrade: BuildUpgrade(); break;
                }
            }
        }

        public void Refresh()
        {
            var l = L;
            if (outpost == null || l == null || root == null) return;
            outpost.CatchUp();
            ResolveRaisedIndex();
            if (building != null && raisedIndex < 0)
            {
                // Standing, but no longer among `outpost.Built` -- a frame
                // gap around a demolish. Blank rather than borrow a station.
                root.Clear();
                recipeRows.Clear();
                worker = null; inLines = null; orderRow = null; outLine = null; stallLine = null;
                storeLines = null; upgradeHolder = null;
                return;
            }

            Plan();
            if (builtKey != planKey) Fill();

            var station = Station(l);
            RefreshFlow(l, station);
            var hand = HandOn(l, station);
            worker?.Update(l, hand);
            FillIn(l, station);
            FillMaking(l, station);
            FillRecipes(l, station);
            FillOut(l, station);
            FillStall(l, station, hand);
            FillStore(l);
            FillUpgrade(l);

            if (upgradeBtn != null) upgradeBtn.SetEnabled(l.CanUpgrade(planId, out _));
        }

        // --- 2. coming in --------------------------------------------------------

        void BuildFlow()
        {
            var row = new VisualElement(); row.AddToClassList("land-flow"); root.Add(row);
            VisualElement Cell(string icon)
            {
                var cell = new VisualElement(); cell.AddToClassList("land-flow-cell");
                cell.Add(new LandIcon(icon)); row.Add(cell); return cell;
            }
            void Arrow() { var arrow = new LandIcon("arrow"); arrow.AddToClassList("land-flow-arrow"); row.Add(arrow); }
            var input = Cell("logs");
            flowInputName = SheetKit.Text("Logs", false, false, 13); input.Add(flowInputName);
            flowInput = SheetKit.Text("", true, false, 20); input.Add(flowInput);
            Arrow();
            var bench = Cell("saw");
            flowState = SheetKit.Text("Idle", false, false, 13); bench.Add(flowState);
            var track = new VisualElement(); track.AddToClassList("land-flow-progress"); bench.Add(track);
            flowProgress = new VisualElement(); flowProgress.AddToClassList("land-flow-progress-fill"); track.Add(flowProgress);
            Arrow();
            var output = Cell("planks");
            flowOutputName = SheetKit.Text("Planks", false, false, 13); output.Add(flowOutputName);
            flowOutput = SheetKit.Text("", true, false, 20); output.Add(flowOutput);
        }

        void RefreshFlow(OutpostLedger ledger, StationStock station)
        {
            if (flowInput == null) return;
            var recipe = Feeding(ledger, station);
            string input = recipe != null && recipe.takes.Length > 0 ? recipe.takes[0].res : Res.Timber;
            string output = station?.BenchMakes ?? recipe?.makes ?? Res.Boards;
            flowInputName.text = input == Res.Timber ? "Logs" : ResDefs.Label(input);
            flowOutputName.text = output == Res.Boards ? "Planks" : ResDefs.Label(output);
            flowInput.text = station == null ? "--" : $"{station.BayCount(input)}/{station.InputCap}";
            flowOutput.text = station == null ? "--" : $"{station.RackTotal}/{station.OutputCap}";
            flowState.text = station == null ? "Unavailable" : station.benchState == BenchState.Working ? "Cutting"
                : station.benchState == BenchState.Finished ? "Ready" : station.benchState == BenchState.Loaded ? "Loaded" : "Idle";
            float progress = station == null ? 0f : station.benchState == BenchState.Finished ? 1f : Mathf.Clamp01(station.benchProgress);
            flowProgress.style.width = Length.Percent(progress * 100f);
        }

        /// A lead label and its line -- "IN  timber 3 of 6 in the bay".
        /// The lead is the row's name; lines after the first leave it blank
        /// so the column still reads as one block.
        internal static Label LeadLine(VisualElement parent, string lead, bool muted = false)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.pickingMode = PickingMode.Ignore;
            var l = SheetKit.Eyebrow(lead ?? "");
            l.style.width = LeadPx;
            l.style.flexShrink = 0f;
            l.style.marginBottom = 0f;
            row.Add(l);
            var t = SheetKit.Text("", false, muted, 13f);
            t.style.flexGrow = 1f;
            t.style.flexShrink = 1f;
            row.Add(t);
            parent.Add(row);
            return t;
        }

        void BuildIn()
        {
            inLines = new Label[MaxInputs];
            for (int i = 0; i < inLines.Length; i++)
                inLines[i] = LeadLine(root, i == 0 ? "coming in" : "");
            benchLine = LeadLine(root, "", true);
        }

        /// The recipe the bay is being filled for: the order's, else what is
        /// on the bench, else what the station would make by default.
        Recipe Feeding(OutpostLedger l, StationStock s) =>
            (s != null ? (s.OrderRecipe ?? s.BenchRecipe) : null) ?? l.RecipeAt(planId);

        void FillIn(OutpostLedger l, StationStock station)
        {
            if (inLines == null) return;
            var r = Feeding(l, station);
            for (int i = 0; i < inLines.Length; i++)
            {
                var lab = inLines[i];
                if (r == null || i >= r.takes.Length || station == null)
                {
                    lab.text = i == 0 ? "nothing ordered yet" : "";
                    lab.style.color = SheetTheme.InkDim;
                    lab.parent.style.display = i == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    continue;
                }
                var t = r.takes[i];
                int bay = station.BayCount(t.res);
                lab.text = $"{ResDefs.Label(t.res)} {bay} of {station.InputCap} in the bay"
                           + $" · {l.StoreCountOf(t.res)} in store";
                lab.style.color = bay >= t.n ? SheetTheme.Moss : SheetTheme.Ember;
                lab.parent.style.display = DisplayStyle.Flex;
            }
            if (benchLine != null)
                benchLine.text = station == null ? "" : BenchLine(station);
        }

        static string BenchLine(StationStock s)
        {
            string what = s.BenchRecipe != null ? " " + s.BenchRecipe.label : "";
            switch (s.benchState)
            {
                case BenchState.Loaded: return $"bench · loaded{what}";
                case BenchState.Working:
                    return $"bench · making{what} ({Mathf.RoundToInt(Mathf.Clamp01(s.benchProgress) * 100f)}%)";
                case BenchState.Finished: return $"bench · finished{what}, waiting for the rack";
                default: return "bench · empty";
            }
        }

        // --- 3. making: the order and STOP -----------------------------------------

        /// **The running order and its STOP** -- Kevin's rule: a station
        /// keeps making what it was told until the player says otherwise,
        /// so "what" and "stop" sit together where the eye lands. The row and
        /// STOP are built once; with no order the same row says so and STOP
        /// is hidden (its height kept, so nothing below moves).
        void BuildMaking()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = OrderRowPx;
            var lead = SheetKit.Eyebrow("making");
            lead.style.width = LeadPx;
            lead.style.flexShrink = 0f;
            row.Add(lead);

            var left = new VisualElement();
            left.style.flexDirection = FlexDirection.Column;
            left.style.flexGrow = 1f;
            left.style.flexShrink = 1f;
            orderTitle = SheetKit.Text("", true, false, 14f);
            left.Add(orderTitle);
            orderSub = SheetKit.Text("", false, true, 12f);
            left.Add(orderSub);
            row.Add(left);

            var l = L;
            orderStop = SheetKit.Btn("STOP", () =>
            {
                if (orderStopStation == null || l == null) return;
                l.StopOrder(planId, orderStopStation.ordinal);
                Refresh();
            }, true);
            orderStop.style.height = TouchPx;
            orderStop.style.minWidth = 96f;
            orderStop.style.flexGrow = 0f;
            orderStop.style.flexShrink = 0f;
            orderStop.style.backgroundColor = SheetTheme.Ember;
            orderStop.style.borderTopColor = orderStop.style.borderBottomColor =
                orderStop.style.borderLeftColor = orderStop.style.borderRightColor = SheetTheme.Ember;
            row.Add(orderStop);

            orderRow = row;
            root.Add(row);
        }

        void FillMaking(OutpostLedger l, StationStock station)
        {
            if (orderRow == null) return;
            var order = station != null ? l.OrderAt(planId, station.ordinal) : default;
            bool active = station != null && order.Active && order.recipe != null;
            // The station this order lives on can shift ordinal under a
            // demolish elsewhere; STOP reads this field fresh.
            orderStopStation = active ? station : null;
            orderStop.style.visibility = active ? Visibility.Visible : Visibility.Hidden;
            orderStop.SetEnabled(active);
            if (active)
            {
                orderTitle.text = order.recipe.label;
                orderSub.text = (order.repeat ? "∞ until stopped" : $"{order.remaining} left") + RateNote(l, order.recipe);
            }
            else
            {
                orderTitle.text = "no order";
                orderSub.text = hasMake ? "pick what to make below" : "";
            }
        }

        /// " · +4 boards a day with 1 hand" -- the ledger's own sum per hand
        /// per day (`ratePerDay` × level × hands on it), nothing more.
        string RateNote(OutpostLedger l, Recipe r)
        {
            int hands = 0;
            foreach (var h in l.hands)
                if (h != null && h.order == OutpostOrder.Work && h.target == planId) hands++;
            if (hands == 0) return " · nobody working it";
            float rate = r.ratePerDay * Techs.RateMul(planId, l.LevelOf(planId)) * hands;
            return $" · +{rate:0.#} {ResDefs.Label(r.makes)}/day with {(hands == 1 ? "1 hand" : hands + " hands")}";
        }

        // --- 3b. the recipes ---------------------------------------------------------

        /// **One recipe row's stable parts.** Everything a tap can land on --
        /// the row and, while it is picked, its amount chips -- is built once
        /// and kept; `Refresh` re-texts labels and toggles visibility. A
        /// rebuilt row between press and release loses the tap (1cf73f9).
        class RecipeRowRefs
        {
            public VisualElement row;
            public Label[] ingLabels;
            public Label toolLine;
            public Label lockLabel;
            public VisualElement waitingChip;
            public VisualElement makingChip;
            public VisualElement amountHolder;
            public bool chipsBuilt;
            public bool available;
        }

        RecipeRowRefs BuildRecipeRow(Recipe r)
        {
            var refs = new RecipeRowRefs();

            var left = new VisualElement();
            left.style.flexDirection = FlexDirection.Column;
            left.style.flexGrow = 1f;
            left.style.flexShrink = 1f;
            left.Add(SheetKit.Text(r.label, true, false, 14f));

            var ingRow = new VisualElement();
            ingRow.style.flexDirection = FlexDirection.Row;
            ingRow.style.flexWrap = Wrap.Wrap;
            var ingLabels = new Label[r.takes.Length];
            for (int i = 0; i < r.takes.Length; i++)
            {
                if (i > 0) ingRow.Add(SheetKit.Text(", ", false, true, 12f));
                var lab = SheetKit.Text("", false, false, 12f);
                ingLabels[i] = lab;
                ingRow.Add(lab);
            }
            ingRow.Add(SheetKit.Text($" → {r.yield} {r.label}", false, true, 12f));
            left.Add(ingRow);
            refs.ingLabels = ingLabels;

            if (r.tool != null)
            {
                refs.toolLine = SheetKit.Text("", false, false, 11f);
                left.Add(refs.toolLine);
            }

            var right = SheetBits.Holder();
            right.style.flexShrink = 0f;
            refs.lockLabel = SheetKit.Text("", false, true, 11f);
            refs.lockLabel.style.display = DisplayStyle.None;
            right.Add(refs.lockLabel);
            refs.waitingChip = SheetKit.Chip("waiting", "", SheetTheme.Ember);
            refs.waitingChip.style.display = DisplayStyle.None;
            right.Add(refs.waitingChip);
            refs.makingChip = SheetKit.Chip("state", "making", SheetTheme.Moss);
            refs.makingChip.style.display = DisplayStyle.None;
            right.Add(refs.makingChip);

            var head = SheetKit.Row(left, right);
            head.style.alignItems = Align.Center;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.paddingTop = 4f;
            row.style.paddingBottom = 4f;
            row.style.minHeight = TouchPx;
            row.Add(head);
            refs.amountHolder = SheetBits.Holder();
            row.Add(refs.amountHolder);

            // Registered once and gated on `refs.available`, so a locked row
            // does nothing without being rebuilt to lose the handler. A
            // picked row can always be un-picked, even if it has locked since.
            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (!refs.available && pickedRecipe != r.id) return;
                pickedRecipe = pickedRecipe == r.id ? null : r.id;
                Refresh();
            });
            refs.row = row;
            return refs;
        }

        void FillRecipes(OutpostLedger l, StationStock station)
        {
            if (recipeRows.Count == 0) return;
            foreach (var r in Recipes.At(planId))
                if (recipeRows.TryGetValue(r.id, out var refs)) UpdateRecipeRow(refs, r, l, station);
        }

        void UpdateRecipeRow(RecipeRowRefs refs, Recipe r, OutpostLedger l, StationStock station)
        {
            bool available = l.RecipeAvailable(r, out string lockWhy);
            var order = station != null ? l.OrderAt(planId, station.ordinal) : default;
            bool isActive = order.Active && order.recipe != null && order.recipe.id == r.id;
            bool isPicked = pickedRecipe == r.id;

            for (int i = 0; i < r.takes.Length; i++)
            {
                int have = l.CountOf(r.takes[i].res);
                int need = r.takes[i].n;
                refs.ingLabels[i].text = $"{ResDefs.Label(r.takes[i].res)} {have}/{need}";
                refs.ingLabels[i].style.color = have >= need ? SheetTheme.Moss : SheetTheme.Ember;
            }
            if (refs.toolLine != null)
            {
                bool hasTool = l.CountOf(r.tool) > 0;
                refs.toolLine.text = $"needs a {ResDefs.Label(r.tool)} (wears)";
                refs.toolLine.style.color = hasTool ? SheetTheme.Moss : SheetTheme.Ember;
            }

            // Chips come up on a pick and down on an un-pick or an order --
            // both taps, never the timer.
            if (available && isPicked && station != null)
            {
                if (!refs.chipsBuilt)
                {
                    SheetBits.Swap(refs.amountHolder, AmountChips(l, station, r.id));
                    refs.chipsBuilt = true;
                }
            }
            else if (refs.chipsBuilt)
            {
                SheetBits.Swap(refs.amountHolder, null);
                refs.chipsBuilt = false;
            }

            refs.lockLabel.style.display = DisplayStyle.None;
            refs.waitingChip.style.display = DisplayStyle.None;
            refs.makingChip.style.display = DisplayStyle.None;
            if (!available)
            {
                refs.lockLabel.text = lockWhy;
                refs.lockLabel.style.display = DisplayStyle.Flex;
            }
            else if (isActive)
            {
                var missing = Cost.Missing(r.takes, res => l.CountOf(res));
                if (missing.Count > 0)
                {
                    SheetKit.SetChip(refs.waitingChip, ResDefs.Label(missing[0].res));
                    refs.waitingChip.style.display = DisplayStyle.Flex;
                }
                else refs.makingChip.style.display = DisplayStyle.Flex;
            }

            refs.row.style.opacity = available ? 1f : 0.5f;
            refs.row.EnableInClassList("sheet-clickable", available);
            refs.row.style.backgroundColor = isPicked
                ? new Color(SheetTheme.Brass.r, SheetTheme.Brass.g, SheetTheme.Brass.b, 0.14f)
                : (StyleColor)StyleKeyword.Null;
            refs.available = available;
        }

        static readonly (string label, int count)[] AmountOptions =
        {
            ("∞", OutpostLedger.RepeatOrder),
            ("5", 5), ("10", 10), ("20", 20), ("50", 50), ("100", 100),
        };

        /// **"make an order and keep making that order until you tell it to
        /// stop, and/or set amounts like 5, 10, 20, 50, 100"** -- Kevin. One
        /// tap places the order; STOP is the confirm and the undo.
        VisualElement AmountChips(OutpostLedger l, StationStock station, string recipeId)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 6f;
            row.style.marginBottom = 2f;
            for (int i = 0; i < AmountOptions.Length; i++)
            {
                var (label, count) = AmountOptions[i];
                var b = SheetKit.Btn(label, () =>
                {
                    l.PlaceOrder(planId, recipeId, count, station.ordinal);
                    pickedRecipe = null;
                    Refresh();
                });
                b.style.height = TouchPx;
                b.style.minWidth = TouchPx;
                b.style.flexGrow = 1f;
                b.style.flexShrink = 1f;
                b.style.paddingLeft = 4f;
                b.style.paddingRight = 4f;
                if (i < AmountOptions.Length - 1) b.style.marginRight = 6f;
                row.Add(b);
            }
            // A chip tap must not also pick/un-pick the row it sits in.
            row.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            return row;
        }

        // --- 4. going out ------------------------------------------------------------

        void BuildOut()
        {
            outLine = LeadLine(root, "going out");
        }

        void FillOut(OutpostLedger l, StationStock station)
        {
            if (outLine == null) return;
            if (station == null) { outLine.text = ""; return; }
            string makes = station.BenchMakes ?? Feeding(l, station)?.makes;
            string what = makes != null ? " " + ResDefs.Label(makes) : "";
            // Racks only ever go to the store (`OutpostLedger.RackChore`);
            // a full store is what holds them on the rack.
            string where = makes != null && l.StoreCountOf(makes) >= l.ceilingPer
                ? "store full, it waits here"
                : "goes to the store";
            outLine.text = $"rack · {station.RackTotal} of {station.OutputCap}{what} · {where}";
            outLine.style.color = station.RackFull ? SheetTheme.Ember : SheetTheme.Ink;
        }

        // --- 5. why it's stopped -------------------------------------------------------

        void BuildStall()
        {
            stallLine = LeadLine(root, "");
            stallLine.style.color = SheetTheme.Ember;
        }

        void FillStall(OutpostLedger l, StationStock station, OutpostHand hand)
        {
            if (stallLine == null) return;
            stallLine.text = StallText(l, hand, station != null && l.OrderAt(planId, station.ordinal).Active);
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

        // --- the store's racks -----------------------------------------------------------

        void BuildStore()
        {
            root.Add(SheetKit.Eyebrow("on the racks"));
            storeLines = new Label[StoreLinesMax];
            for (int i = 0; i < storeLines.Length; i++)
            {
                storeLines[i] = SheetKit.Text("", false, false, 13f);
                root.Add(storeLines[i]);
            }
        }

        readonly List<string> held = new List<string>();

        /// What the store holds, two kinds a line, each against the shelf
        /// it fills (`ceilingPer`) -- the same numbers the camp's gauges use.
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
                // The last line says how many more there are, never drops them.
                if (i == storeLines.Length - 1 && held.Count > storeLines.Length * 2)
                    text = $"{StoreBit(l, held[a])} · and {held.Count - a - 1} more kinds";
                if (i == 0 && held.Count == 0) text = "the store is empty";
                storeLines[i].text = text;
                storeLines[i].style.display = text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        static string StoreBit(OutpostLedger l, string res) =>
            $"{ResDefs.Label(res)} {l.StoreCountOf(res)}/{l.ceilingPer}";

        // --- 6. upgrade ----------------------------------------------------------------

        void BuildUpgrade()
        {
            if (root.childCount > 0) root.Add(SheetKit.Rule());
            root.Add(SheetKit.Eyebrow("upgrade"));
            upgradeLevel = SheetKit.Text("", true, false, 14f);
            root.Add(upgradeLevel);
            upgradeHolder = SheetBits.Holder();
            root.Add(upgradeHolder);
        }

        void FillUpgrade(OutpostLedger l)
        {
            if (upgradeHolder == null) return;
            int level = l.LevelOf(planId);
            int max = Techs.MaxLevel(planId);
            var next = l.NextUpgrade(planId);
            upgradeLevel.text = next != null ? $"level {level} of {max} · next: level {level + 1}"
                                             : $"level {level} of {max}";

            long key = level * 1000003L;
            if (next != null)
                foreach (var c in next.cost) key = key * 31 + l.CountOf(c.res);
            if (key == upgradeKey) return;
            upgradeKey = key;

            // No buttons in here -- the raise button is the action row's --
            // so rebuilding this block on a changed price loses no tap.
            var col = new VisualElement();
            col.style.flexDirection = FlexDirection.Column;
            if (next == null)
            {
                col.Add(SheetKit.Text("as good as it gets", false, true, 12f));
            }
            else
            {
                var costRow = new VisualElement();
                costRow.style.flexDirection = FlexDirection.Row;
                costRow.style.flexWrap = Wrap.Wrap;
                for (int i = 0; i < next.cost.Length; i++)
                {
                    if (i > 0) costRow.Add(SheetKit.Text(", ", false, true, 12f));
                    costRow.Add(IngredientLine(l, next.cost[i].res, next.cost[i].n));
                }
                col.Add(costRow);
                string effect = EffectLine(next);
                if (effect.Length > 0) col.Add(SheetKit.Text(effect, false, true, 12f));
                if (!l.CanUpgrade(planId, out string why))
                    col.Add(SheetKit.Note(why));
            }
            SheetBits.Swap(upgradeHolder, col);
        }

        static string EffectLine(UpgradeStep step)
        {
            var parts = new List<string>(2);
            if (step.storeBonus != 0) parts.Add($"+{step.storeBonus} stores");
            if (step.housesBonus != 0) parts.Add(step.housesBonus == 1 ? "+1 bed" : $"+{step.housesBonus} beds");
            if (Mathf.Abs(step.rateMul - 1f) > 0.001f) parts.Add($"×{step.rateMul:0.#} rate");
            return string.Join(", ", parts);
        }

        void DoUpgrade()
        {
            var l = L;
            if (l == null) return;
            if (l.Upgrade(planId))
            {
                upgradeKey = long.MinValue;
                Refresh();
            }
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

    /// **The worker slot at the top of a building's sheet.** The same verbs
    /// the camp sheet's lookout row and the hand sheet already press -- post
    /// the first idle hand (`SheetBits.FirstIdle` + `Outpost.Assign`, the
    /// "post a lookout" control) and stand them down (`Outpost.OrderIdle`,
    /// the hand sheet's "stand down") -- on one 44-unit button whose verb
    /// follows the slot. Built once; `Update` re-texts it, and the button
    /// reads who is in the slot at the moment it is pressed.
    internal sealed class WorkerSlot
    {
        readonly Outpost outpost;
        readonly string planId;
        readonly string position;
        readonly System.Action changed;
        readonly Label who;
        readonly Label sub;
        readonly Button btn;
        OutpostHand current;

        public readonly VisualElement Root;

        public WorkerSlot(Outpost o, string planId, System.Action changed)
        {
            outpost = o;
            this.planId = planId;
            this.changed = changed;
            position = BuildPlans.PositionAt(planId);
            if (string.IsNullOrEmpty(position)) position = "hand";

            Root = new VisualElement();
            Root.style.flexDirection = FlexDirection.Row;
            Root.style.alignItems = Align.Center;
            Root.style.height = StationSheet.TouchPx;
            Root.style.marginTop = 2f;
            Root.style.marginBottom = 2f;

            var lead = SheetKit.Eyebrow(position);
            lead.style.width = StationSheet.LeadPx;
            lead.style.flexShrink = 0f;
            lead.style.marginBottom = 0f;
            Root.Add(lead);
            who = SheetKit.Text("", true, false, 14f);
            who.style.flexGrow = 1f;
            who.style.flexShrink = 1f;
            Root.Add(who);

            sub = SheetKit.Text("", false, true, 12f);
            sub.style.marginRight = 8f;
            Root.Add(sub);

            btn = SheetKit.Btn("", Press, false, true);
            btn.style.height = StationSheet.TouchPx;
            btn.style.minWidth = 120f;
            btn.style.fontSize = 13f;
            btn.style.flexShrink = 0f;
            Root.Add(btn);
            if (MidnightLandHud.Active)
            {
                lead.style.width = 52f;
                btn.style.minWidth = 108f;
                who.style.minWidth = 0f;
                who.style.whiteSpace = WhiteSpace.NoWrap;
                who.style.overflow = Overflow.Hidden;
                who.style.textOverflow = TextOverflow.Ellipsis;
            }
        }

        public void Update(OutpostLedger l, OutpostHand hand, int others = 0)
        {
            current = hand;
            if (hand != null)
            {
                who.text = hand.name;
                sub.text = others > 0 ? $"+{others} more" : "";
                btn.text = "stand down";
                btn.SetEnabled(true);
            }
            else
            {
                who.text = "nobody";
                sub.text = SheetBits.FirstIdle(l) == null ? "no idle hand" : "";
                btn.text = $"post a {position}";
                btn.SetEnabled(SheetBits.FirstIdle(l) != null);
            }
        }

        void Press()
        {
            var l = outpost != null ? outpost.Ledger : null;
            if (l == null) return;
            if (current != null) outpost.OrderIdle(current);
            else
            {
                var free = SheetBits.FirstIdle(l);
                if (free != null) outpost.Assign(free, planId);
            }
            changed?.Invoke();
        }
    }
}
