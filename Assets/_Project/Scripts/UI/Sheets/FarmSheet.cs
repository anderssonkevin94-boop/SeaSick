using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The farm's sheet, reworked 2026-10-04** (Kevin on the phone: "Farm
    /// plot UI is broken and needs to be reworked" -- tofu after every count,
    /// the crop picker drawn on top of itself, nine ripe plots with nobody
    /// picking and no way to say so). `StationSheet`'s frame and kit, three
    /// pages in the hugging frame (phone) and on the desk's side third:
    ///
    /// **plots** --
    /// 1. **no farmer** (only while nobody works this farm): "No farmer ·
    ///    9 plots ripe, nobody picking" and a real **Assign** button (the
    ///    worker card's own verb, `FreeHandFor` + `Outpost.Assign` for THIS
    ///    farm; "People" opens the roster when there is no free hand);
    /// 2. **plot cards**, three a row: the crop's icon (a drawn "+" on an
    ///    empty plot), its name, ONE state ("4:12 left" with a thin bar,
    ///    "4 to pick" in moss, "to plant", or "Choose crop" in ice on an empty
    ///    plot) and "next: Potato" while a crop change waits behind the
    ///    harvest (`FarmPlot.nextCrop`) or "then empty" when it will not be
    ///    replanted. Tap a card: its detail. More than fit page through a
    ///    last "More" card, never a scroll;
    /// 3. one summary line ("9 ripe · 0 growing · next ripe in 3:20").
    ///
    /// **the picked plot** takes the cards' place: "‹ Plots" + "Plot 5 ·
    /// Wheat" and its state; the crop cards (icon, name, "10 in 20 min",
    /// "30 an hour"; a locked one says "needs Farm III"); one
    /// wrapping line on what a pick does here; ONE row of real buttons --
    /// the replant toggle and Clear plot -- side by side, wrapping, never
    /// stacked on each other.
    ///
    /// **work** -- the farmhand's card, food out, why it stopped.
    /// **level** -- the upgrade card.
    ///
    /// No glyph from outside Nunito (the phone draws those as tofu): icons
    /// are the item PNGs or drawn `StationPage.Glyph`s.
    ///
    /// **Built once, re-texted after** (as `StationSheet`): the cards are a
    /// pool built with the page, a click handler registered once reads what
    /// the card shows at the tap, and `Refresh` only re-texts and flips
    /// classes -- nothing a finger can land on is rebuilt on the 0.25 s timer.
    public class FarmSheet : ISheetFramed
    {
        readonly Outpost outpost;
        readonly Building building;
        readonly string planId;
        readonly BuildPlan plan;
        readonly bool hasUpgrade;
        int raisedIndex = -1;

        public FarmSheet(Outpost o, Building b)
        {
            outpost = o;
            building = b;
            planId = b != null ? b.Id : BuildPlans.Farm.id;
            plan = BuildPlans.Named(planId);
            hasUpgrade = Techs.MaxLevel(planId) > 1;
            tabs = hasUpgrade ? new[] { PagePlots, PageWork, PageLevel } : new[] { PagePlots, PageWork };
        }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        public string Title => StationPage.Cap(plan.label);
        public Color Accent => SheetTheme.Moss;
        public bool WantsTallSheet => true;
        public VisualElement BuildActions() => null;

        // --- pages -------------------------------------------------------------

        const string PagePlots = "plots", PageWork = "work", PageLevel = "level";
        readonly string[] tabs;
        int tab = -1;

        /// Pages on the phone's hugging frame AND the desk's side third (both
        /// too short for the stacked page -- `StationSheet.Paged`'s rule).
        static bool Paged => StationPage.Hugging || HudLayout.Wide;
        public string[] TabLabels => Paged ? tabs : null;
        public int Tab => tab;
        public void SetTab(int index) { tab = index; }

        public Vector3 AnchorWorld => building != null
            ? building.transform.position
            : (outpost != null ? outpost.CampCentre : Vector3.zero);

        public bool StillValid => outpost != null && outpost.Ledger != null && building != null;

        StationPage.Header header;

        public VisualElement BuildHeader()
        {
            header = new StationPage.Header(Title, true, () => StationPage.OpenLedgerFor(outpost));
            // Move / turn the farm, field and all (Kevin, 2026-09-30).
            MoveButton.AddTo(header.Root, outpost, building);
            return header.Root;
        }

        // --- the pieces kept between refreshes ---------------------------------

        VisualElement root;
        StationPage.WorkerCard worker;
        StationPage.UpgradeCard upgrade;
        Label yieldLine, keptLine, stallLine;

        /// The page shown on its own (tabs), not stacked under the others.
        bool paged;

        // no farmer
        VisualElement farmerRow;
        Label farmerSub;
        Button farmerBtn;
        bool farmerCanAssign;

        // plot cards
        sealed class Tile
        {
            public Button root;
            public VisualElement icon, bar, fill;
            public StationPage.Glyph glyph;
            public Label name, state, sub, next;
            /// What the card is right now: a plot index, the More card (-2),
            /// or nothing (-1). Read at the tap.
            public int plot = -1;
            /// The crop card's crop, read at the tap.
            public string crop;
            public string iconRes;
            public string glyphKind;
        }
        Tile[] plotTiles;
        VisualElement plotGrid;
        Label summary;
        int plotPage;
        int plotsPerPage;
        const int PerRow = 3;
        const int MoreCard = -2;

        // the picked plot
        VisualElement detail;
        Label detailTitle, detailState, detailInfo;
        Tile[] cropTiles;
        Button repeatBtn, clearBtn;
        int pickedPlot = -1;

        /// Panel units one row of plot cards costs, for planning how many rows
        /// fit the hugging frame BEFORE building. Planned for the WORST card,
        /// three lines (name + "Choose / crop", or name + state + "next:
        /// Potato"): 20.5 + 17.7 + 16.4 + 2 margins + 5+5 pad + 3 border = 70,
        /// + 7 gap; rounded to 78. Measured 2026-10-04 on the 16 Pro shape: a
        /// two-line card is 56. Three rows (+ the farmer card) = 302 of the
        /// phone's 312. The farmer card: 4+4 pad + 4 border + 48 button + 8
        /// margin. The summary line (8 + ~20) shows only while the farmer card
        /// does not -- the card's own line says the same -- so one reserve
        /// covers either, and assigning a farmer never needs a re-plan.
        const float PlotRowPx = 78f;
        const float FarmerRowPx = 68f;

        // --- the model -----------------------------------------------------------

        int Ordinal => outpost != null ? Mathf.Max(0, outpost.OrdinalOf(building)) : 0;

        List<FarmPlot> Plots(OutpostLedger l) => l != null ? l.PlotsOf(Ordinal) : new List<FarmPlot>();

        FarmPlot Picked()
        {
            var l = L;
            if (l == null || pickedPlot < 0) return null;
            var list = Plots(l);
            return pickedPlot < list.Count ? list[pickedPlot] : null;
        }

        /// The first hand working THIS farm, and how many do. Farmhands are
        /// dealt round the farms (`OutpostLedger.OrdinalOfHand`).
        OutpostHand Farmer(OutpostLedger l, out int count)
        {
            count = 0;
            OutpostHand first = null;
            int mine = outpost.OrdinalOf(building);
            foreach (var h in l.hands)
            {
                if (h == null || h.order != OutpostOrder.Work || h.target != planId) continue;
                if (mine >= 0 && l.OrdinalOfHand(h) != mine) continue;
                if (first == null) first = h;
                count++;
            }
            return first;
        }

        static string Clock(float secs)
        {
            int s = Mathf.CeilToInt(secs);
            return $"{s / 60}:{s % 60:00}";
        }

        static string CropName(string res) => StationPage.Cap(ResDefs.Label(res));

        static string GrowTime(float secs)
        {
            float min = secs / 60f;
            return min < 60f ? $"{min:0.#} min" : $"{min / 60f:0.#} h";
        }

        // --- build ---------------------------------------------------------------

        public VisualElement Build()
        {
            worker = null; upgrade = null;
            yieldLine = keptLine = stallLine = null;
            farmerRow = null; farmerSub = null; farmerBtn = null;
            plotTiles = null; plotGrid = null; summary = null;
            detail = null; detailTitle = detailState = detailInfo = null; cropTiles = null;
            repeatBtn = clearBtn = null;
            paged = Paged;
            string on = paged ? tabs[Mathf.Clamp(tab, 0, tabs.Length - 1)] : null;
            bool all = on == null;

            root = StationPage.Root("st-page");
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

            if (all || on == PageWork)
            {
                worker = new StationPage.WorkerCard(outpost, planId, () => Refresh());
                Section(null).Add(worker.Root);
            }
            if (all || on == PagePlots) BuildPlots(Section(all ? "PLOTS" : null));
            if (all || on == PageWork) BuildWork(Section(all ? "FOOD OUT" : null));
            if (hasUpgrade && (all || on == PageLevel))
            {
                upgrade = new StationPage.UpgradeCard(DoUpgrade, null, outpost);
                if (first) upgrade.Root.style.marginTop = 0f;
                col.Add(upgrade.Root);
            }

            Refresh();
            return root;
        }

        void BuildPlots(VisualElement s)
        {
            // 1. no farmer -- only where the worker card is not on the page.
            if (paged)
            {
                farmerRow = StationPage.Card();
                farmerRow.AddToClassList("st-farmer");
                var words = new VisualElement(); words.AddToClassList("st-farmer-words");
                words.pickingMode = PickingMode.Ignore;
                words.Add(StationPage.Text("No farmer", "st-farmer-title"));
                farmerSub = StationPage.Text("", "st-farmer-sub");
                words.Add(farmerSub);
                farmerRow.Add(words);
                farmerBtn = new Button(PressFarmer) { text = "Assign" };
                farmerBtn.AddToClassList("st-btn");
                farmerBtn.AddToClassList("st-farmer-btn");
                farmerRow.Add(farmerBtn);
                farmerRow.style.display = DisplayStyle.None;
                s.Add(farmerRow);
            }

            // 2. the plot cards, a pool planned to the band.
            int count = Plots(L).Count;
            int rows = PlanRows(count);
            plotsPerPage = Mathf.Max(PerRow, rows * PerRow);
            int pool = Mathf.Min(count, plotsPerPage);
            plotGrid = new VisualElement(); plotGrid.AddToClassList("st-pgrid");
            plotTiles = new Tile[pool];
            for (int i = 0; i < pool; i++)
            {
                var t = NewTile(withNext: true);
                if (i % PerRow == PerRow - 1) t.root.AddToClassList("st-ptile--end");
                if (i >= PerRow) t.root.AddToClassList("st-ptile--row2");
                t.root.clicked += () => PressPlot(t);
                plotTiles[i] = t;
                plotGrid.Add(t.root);
            }
            s.Add(plotGrid);

            // 3. the summary line.
            summary = StationPage.Text("", "st-psum");
            s.Add(summary);

            // The picked plot: in place of the cards when paged, under them
            // on the stacked desk page.
            BuildDetail(s);
        }

        /// **How many rows of plot cards fit**, planned from the band before
        /// building (a page never re-plans under a finger). The farmer card
        /// is always budgeted, shown or not, so assigning one never needs a
        /// re-plan.
        int PlanRows(int count)
        {
            int need = Mathf.Max(1, Mathf.CeilToInt(count / (float)PerRow));
            if (!paged) return need;
            float budget = StationPage.Hugging
                ? SheetHost.HugBodyBudget(true)
                : SheetHost.BandHeight;
            int fit = Mathf.FloorToInt((budget - FarmerRowPx) / PlotRowPx);
            return Mathf.Clamp(fit, 1, need);
        }

        Tile NewTile(bool withNext)
        {
            var t = new Tile();
            t.root = new Button { text = "" };
            t.root.AddToClassList("st-ptile");
            t.icon = StationPage.Icon(null, "st-ptile-icon");
            t.root.Add(t.icon);
            t.glyph = new StationPage.Glyph("plus", StationPage.Ink, "st-ptile-glyph");
            t.glyphKind = "plus";
            t.glyph.style.display = DisplayStyle.None;
            t.root.Add(t.glyph);
            var words = new VisualElement(); words.AddToClassList("st-ptile-words");
            words.pickingMode = PickingMode.Ignore;
            t.name = StationPage.Text("", "st-ptile-name");
            t.state = StationPage.Text("", "st-ptile-state");
            t.sub = StationPage.Text("", "st-ptile-sub");
            words.Add(t.name); words.Add(t.state); words.Add(t.sub);
            if (withNext)
            {
                t.next = StationPage.Text("", "st-ptile-next");
                words.Add(t.next);
            }
            t.root.Add(words);
            t.bar = new VisualElement { pickingMode = PickingMode.Ignore };
            t.bar.AddToClassList("st-ptile-bar");
            t.fill = new VisualElement { pickingMode = PickingMode.Ignore };
            t.fill.AddToClassList("st-ptile-fill");
            t.bar.Add(t.fill);
            t.bar.style.display = DisplayStyle.None;
            t.root.Add(t.bar);
            return t;
        }

        void BuildDetail(VisualElement s)
        {
            detail = new VisualElement(); detail.AddToClassList("st-pdetail");
            if (!paged) detail.style.marginTop = 10f;
            var head = new VisualElement(); head.AddToClassList("st-pdetail-head");
            var back = new Button(() => { pickedPlot = -1; Refresh(); }) { text = paged ? "‹ Plots" : "Close" };
            back.AddToClassList("st-btn");
            back.AddToClassList("st-pback");
            head.Add(back);
            var words = new VisualElement(); words.AddToClassList("st-pdetail-words");
            words.pickingMode = PickingMode.Ignore;
            detailTitle = StationPage.Text("", "st-pdetail-title");
            detailState = StationPage.Text("", "st-pdetail-state");
            words.Add(detailTitle); words.Add(detailState);
            head.Add(words);
            detail.Add(head);

            var grid = new VisualElement(); grid.AddToClassList("st-pgrid");
            var crops = FoodBook.Crops;
            cropTiles = new Tile[crops.Length];
            for (int i = 0; i < crops.Length; i++)
            {
                var t = NewTile(withNext: false);
                if (i % PerRow == PerRow - 1) t.root.AddToClassList("st-ptile--end");
                if (i >= PerRow) t.root.AddToClassList("st-ptile--row2");
                t.crop = crops[i].res;
                t.root.AddToClassList("st-ptile--crop");
                t.root.clicked += () => PressCrop(t);
                cropTiles[i] = t;
                grid.Add(t.root);
            }
            detail.Add(grid);

            detailInfo = StationPage.Text("", "st-pinfo");
            detail.Add(detailInfo);

            // ONE row of real buttons: side by side, each wraps its own words.
            var actions = new VisualElement(); actions.AddToClassList("st-pactions");
            repeatBtn = new Button(PressRepeat) { text = "" };
            repeatBtn.AddToClassList("st-btn");
            repeatBtn.AddToClassList("st-pact");
            repeatBtn.AddToClassList("st-pact--first");
            actions.Add(repeatBtn);
            clearBtn = new Button(PressClear) { text = "Clear plot" };
            clearBtn.AddToClassList("st-btn");
            clearBtn.AddToClassList("st-pact");
            actions.Add(clearBtn);
            detail.Add(actions);

            detail.style.display = DisplayStyle.None;
            s.Add(detail);
        }

        void BuildWork(VisualElement fs)
        {
            var foodCard = StationPage.Card();
            var icon = StationPage.Icon(Res.Potato, "st-flow-icon");
            icon.style.marginRight = 14f;
            icon.style.marginBottom = 0f;
            foodCard.Add(icon);
            var foodCol = new VisualElement(); foodCol.AddToClassList("st-col");
            yieldLine = StationPage.Text("", "st-line");
            keptLine = StationPage.Text("", "st-line");
            keptLine.AddToClassList("st-muted");
            // Wrap inside the card rather than run off its right edge
            // (Kevin's 2026-09-28 screenshot: "carried in by the farm...").
            foodCol.style.flexGrow = 1f;
            foodCol.style.flexShrink = 1f;
            foodCol.style.minWidth = 0f;
            yieldLine.style.whiteSpace = WhiteSpace.Normal;
            keptLine.style.whiteSpace = WhiteSpace.Normal;
            foodCol.Add(yieldLine);
            foodCol.Add(keptLine);
            foodCard.Add(foodCol);
            fs.Add(foodCard);

            // why it's stopped
            stallLine = StationPage.Text("", "st-stall");
            fs.Add(stallLine);
        }

        // --- presses ---------------------------------------------------------------

        void PressPlot(Tile t)
        {
            if (t.plot == MoreCard)
            {
                // The same split `FillPlots` draws: the pool less the More card.
                int per = Mathf.Max(1, (plotTiles != null ? plotTiles.Length : plotsPerPage) - 1);
                int pages = Mathf.Max(1, Mathf.CeilToInt(Plots(L).Count / (float)per));
                plotPage = (plotPage + 1) % pages;
            }
            else if (t.plot >= 0) pickedPlot = pickedPlot == t.plot && !paged ? -1 : t.plot;
            Refresh();
        }

        void PressCrop(Tile t)
        {
            var l = L;
            var p = Picked();
            if (l == null || p == null || t.crop == null) return;
            if (!l.CropUnlocked(p, t.crop, out _)) return;
            // A bare plot (cleared, nothing queued) is planted on repeat --
            // the default every new plot has; otherwise its switch stands.
            bool repeat = string.IsNullOrEmpty(p.crop) && !p.HasNext || p.repeat;
            l.SetPlotCrop(p, t.crop, repeat);
            Refresh();
        }

        void PressRepeat()
        {
            var p = Picked();
            if (p != null) L?.SetPlotRepeat(p, !p.repeat);
            Refresh();
        }

        void PressClear()
        {
            var p = Picked();
            if (p != null) L?.SetPlotCrop(p, "", p.repeat);
            Refresh();
        }

        /// **"Assign"**: the worker card's own verb for THIS farm -- the best
        /// free hand (`FreeHandFor`) onto this copy (`Outpost.Assign` with
        /// the farm's ordinal). No free hand: the roster, where one is made.
        void PressFarmer()
        {
            var l = L;
            if (l == null || outpost == null) return;
            if (!farmerCanAssign)
            {
                Sheets.Open(new WorkersSheet(outpost));
                return;
            }
            var free = l.FreeHandFor(planId);
            if (free == null) return;
            int ord = outpost.OrdinalOf(building);
            if (!(ord >= 0 ? outpost.Assign(free, planId, ord) : outpost.Assign(free, planId)))
                outpost.Assign(free, planId);
            Refresh();
        }

        // --- fill ---------------------------------------------------------------------

        static void SetText(Label l, string s)
        {
            if (l == null) return;
            s = s ?? "";
            if (l.text != s) l.text = s;
            var d = s.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            if (l.style.display != d) l.style.display = d;
        }

        static void Show(VisualElement e, bool on)
        {
            if (e == null) return;
            var d = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (e.style.display != d) e.style.display = d;
        }

        static void Tone(Label l, int kind)
        {
            l.EnableInClassList("st-tone--good", kind == 0);
            l.EnableInClassList("st-tone--wait", kind == 1);
            l.EnableInClassList("st-tone--bad", kind == 2);
            l.EnableInClassList("st-ptile-act", kind == 3);
        }

        static void SetIconOrGlyph(Tile t, string res, string glyph)
        {
            bool useGlyph = glyph != null;
            Show(t.icon, !useGlyph);
            Show(t.glyph, useGlyph);
            if (!useGlyph) StationPage.SetIcon(t.icon, res);
            else if (t.glyphKind != glyph)
            {
                // The glyph's kind is fixed at construction: swap the element.
                var g = new StationPage.Glyph(glyph, StationPage.Ink, "st-ptile-glyph");
                int at = t.root.IndexOf(t.glyph);
                t.root.Remove(t.glyph);
                t.root.Insert(at, g);
                t.glyph = g;
                t.glyphKind = glyph;
            }
        }

        void FillPlots(OutpostLedger l, OutpostHand farmer)
        {
            var list = Plots(l);
            var pk = Picked();
            bool showDetail = pk != null;
            bool showCards = !(paged && showDetail);

            int ripe = 0, growing = 0, toPlant = 0, bare = 0;
            float soonest = float.MaxValue;
            foreach (var p in list)
            {
                if (p.state == PlotState.Ripe) ripe++;
                else if (p.state == PlotState.Growing) { growing++; soonest = Mathf.Min(soonest, p.SecondsLeft); }
                else if (string.IsNullOrEmpty(p.crop)) bare++;
                else toPlant++;
            }

            // 1. no farmer
            if (farmerRow != null)
            {
                Show(farmerRow, farmer == null && showCards);
                if (farmer == null)
                {
                    string why = ripe > 0 ? $"{ripe} plot{(ripe == 1 ? "" : "s")} ripe, nobody picking"
                        : toPlant > 0 ? $"{toPlant} plot{(toPlant == 1 ? "" : "s")} to plant, nobody planting"
                        : growing > 0 ? "crops grow, nobody will pick them"
                        : "nobody works the field";
                    SetText(farmerSub, why);
                    farmerCanAssign = l.FreeHandFor(planId) != null;
                    string bt = farmerCanAssign ? "Assign" : "People";
                    if (farmerBtn.text != bt) farmerBtn.text = bt;
                }
            }

            // 2. the plot cards
            Show(plotGrid, showCards);
            if (plotTiles != null)
            {
                bool paging = list.Count > plotTiles.Length;
                int perPage = paging ? plotTiles.Length - 1 : plotTiles.Length;
                int pages = paging ? Mathf.CeilToInt(list.Count / (float)perPage) : 1;
                plotPage = Mathf.Clamp(plotPage, 0, pages - 1);
                int start = plotPage * perPage;
                for (int i = 0; i < plotTiles.Length; i++)
                {
                    var t = plotTiles[i];
                    if (paging && i == plotTiles.Length - 1)
                    {
                        BindMore(t, plotPage, pages);
                        continue;
                    }
                    int idx = start + i;
                    if (idx >= list.Count) { t.plot = -1; Show(t.root, false); continue; }
                    Show(t.root, true);
                    BindPlot(t, idx, list[idx], farmer != null);
                }
            }
            SetText(summary, list.Count == 0 ? "no plots yet"
                : $"{ripe} ripe · {growing} growing"
                  + (soonest < float.MaxValue ? $" · next ripe in {Clock(soonest)}" : "")
                  + (bare > 0 ? $" · {bare} empty" : ""));
            Show(summary, showCards && farmer != null);

            // the picked plot
            Show(detail, showDetail);
            if (showDetail) FillDetail(l, pk, farmer != null);
        }

        void BindMore(Tile t, int page, int pages)
        {
            t.plot = MoreCard;
            Show(t.root, true);
            SetIconOrGlyph(t, null, "arrow");
            SetText(t.name, "More");
            SetText(t.state, $"page {page + 1} of {pages}");
            Tone(t.state, -1);
            SetText(t.sub, "");
            SetText(t.next, "");
            Show(t.bar, false);
            t.root.EnableInClassList("st-ptile--ripe", false);
            t.root.EnableInClassList("st-ptile--empty", false);
            t.root.EnableInClassList("st-ptile--on", false);
        }

        void BindPlot(Tile t, int idx, FarmPlot p, bool hasFarmer)
        {
            t.plot = idx;
            bool bare = string.IsNullOrEmpty(p.crop);
            SetIconOrGlyph(t, p.crop, bare ? "plus" : null);
            SetText(t.name, bare ? "Empty" : CropName(p.crop));

            // ONE state, from the plot's own line (`FarmPlot.StateLine`, the
            // one place a plot's yield is read: `RipeUnits`, never the full
            // yield); the queued crop rides on its own line.
            string line = p.StateLine(CropName, Clock(p.SecondsLeft));
            int nl = line.IndexOf('\n');
            string state = nl >= 0 ? line.Substring(0, nl) : line;
            // A ripe card says only the count ("10 to pick", one line in a
            // third of the phone): its green edge and moss line say ripe, and
            // the picked plot's head keeps the whole "ripe · 10 to pick".
            if (p.state == PlotState.Ripe) state = $"{p.RipeUnits} to pick";
            int kind;
            if (p.state == PlotState.Ripe) kind = hasFarmer ? 0 : 1;
            else if (p.state == PlotState.Growing) kind = -1;
            else if (bare) { state = "Choose crop"; kind = 3; }
            else kind = hasFarmer ? -1 : 1;
            SetText(t.state, state);
            Tone(t.state, kind);
            SetText(t.sub, "");

            string next = p.HasNext ? $"next: {CropName(p.nextCrop)}"
                : !bare && !p.repeat ? "then empty" : "";
            SetText(t.next, next);

            bool growingNow = p.state == PlotState.Growing;
            Show(t.bar, growingNow);
            if (growingNow) t.fill.style.width = Length.Percent(Mathf.Clamp01(p.Grow01) * 100f);

            t.root.EnableInClassList("st-ptile--ripe", p.state == PlotState.Ripe);
            t.root.EnableInClassList("st-ptile--empty", bare);
            t.root.EnableInClassList("st-ptile--on", idx == pickedPlot);
        }

        void FillDetail(OutpostLedger l, FarmPlot pk, bool hasFarmer)
        {
            bool bare = string.IsNullOrEmpty(pk.crop);
            SetText(detailTitle, $"Plot {pickedPlot + 1} · " + (bare ? "empty" : CropName(pk.crop)));
            string line = pk.StateLine(CropName, Clock(pk.SecondsLeft));
            if (pk.state == PlotState.Growing) line = "growing · " + line;
            else if (bare && !pk.HasNext) line = "empty · choose a crop";
            SetText(detailState, line.Replace("\n", " · "));

            // the crop cards: the lit one is what will be planted next.
            string target = pk.HasNext ? pk.nextCrop : pk.crop;
            foreach (var t in cropTiles)
            {
                var def = FoodBook.Crop(t.crop);
                bool ok = l.CropUnlocked(pk, t.crop, out string why);
                SetIconOrGlyph(t, t.crop, null);
                SetText(t.name, CropName(t.crop));
                if (ok && def != null)
                {
                    // "4 in 5 min" / "48 an hour": short enough for a third
                    // of the phone, so two rows of crops and the buttons
                    // stay inside half the screen (measured 2026-10-04).
                    SetText(t.state, $"{def.yield} in {GrowTime(def.growSeconds)}");
                    Tone(t.state, -1);
                    SetText(t.sub, $"{def.PerHour:0} an hour");
                }
                else
                {
                    // "Farm III" never splits (Kevin 2026-10-04): its spaces
                    // are no-break (U+00A0, which Nunito has), so the line
                    // wraps as "needs" / "Farm III".
                    SetText(t.state, "needs " + StationPage.Cap(why ?? "a higher farm").Replace(' ', '\u00a0'));
                    Tone(t.state, 1);
                    SetText(t.sub, "");
                }
                t.root.EnableInClassList("st-ptile--locked", !ok);
                t.root.EnableInClassList("st-ptile--on", ok && t.crop == target);
            }

            // what a pick does on this plot, whole (it wraps, never cut).
            string info;
            string standing = bare ? "" : ResDefs.Label(pk.crop);
            if (pk.state == PlotState.Ripe)
                info = pk.HasNext
                    ? $"{pk.RipeUnits} {standing} ripe, picked first; then {ResDefs.Label(pk.nextCrop)} is planted."
                    : $"{pk.RipeUnits} {standing} ripe, picked first; a crop you choose is planted after.";
            else if (pk.state == PlotState.Growing)
                info = "Choosing another crop digs this one up and plants the new one.";
            else
                info = bare ? "Choose a crop for the farmer to plant." : $"The farmer plants {standing} next.";
            SetText(detailInfo, info);

            // the one row of buttons
            string rt = pk.repeat ? "Replant: on" : "Replant: off";
            if (repeatBtn.text != rt) repeatBtn.text = rt;
            repeatBtn.EnableInClassList("st-pact--on", pk.repeat);
            bool clearable = !bare || pk.HasNext;
            Show(clearBtn, clearable);
        }

        void ResolveRaisedIndex()
        {
            raisedIndex = -1;
            if (outpost == null || building == null) return;
            var built = outpost.Built;
            for (int i = 0; i < built.Count; i++)
                if (built[i] == building) { raisedIndex = i; break; }
        }

        public void Refresh()
        {
            var l = L;
            if (outpost == null || l == null || root == null) return;
            outpost.CatchUp();
            ResolveRaisedIndex();
            int level = outpost.LevelOfBuilding(building);

            var first = Farmer(l, out int hands);
            worker?.Update(l, first, Mathf.Max(0, hands - 1));

            if (plotGrid != null) FillPlots(l, first);

            int ripe = 0;
            foreach (var p in Plots(l))
                if (p != null && p.state == PlotState.Ripe) ripe++;

            string why = StationSheet.StallText(l, first, false);
            if (yieldLine != null)
            {
                int crops = 0;
                foreach (var c in FoodBook.Crops) crops += l.StoreCountOf(c.res);
                yieldLine.text = $"{crops} crops in store · carried in by the farmhand";
                float days = SheetBits.FoodDays(l);
                keptLine.text = days < 0f
                    ? SheetBits.FoodDaysLine(l)
                    : $"{l.FoodFill():0.#} meals of food · lasts {days:0.#} days";
                stallLine.text = why;
                stallLine.style.display = string.IsNullOrEmpty(why) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (header != null)
            {
                header.SetSub($"Level {level} · {StationPage.IslandName(outpost)}");
                if (first == null) header.SetPill("no worker", StationPage.PillBad);
                else if (!string.IsNullOrEmpty(why)) header.SetPill("waiting", StationPage.PillWait);
                else if (ripe > 0) header.SetPill("working", StationPage.PillGood);
                else header.SetPill("growing", StationPage.PillWait);
                header.Refresh();
            }

            upgrade?.Update(l, raisedIndex, planId, level);
        }

        void DoUpgrade()
        {
            var l = L;
            if (l == null) return;
            ResolveRaisedIndex();
            if (l.UpgradeAt(raisedIndex, planId))
            {
                outpost?.Retint(building);
                Refresh();
            }
        }
    }
}
