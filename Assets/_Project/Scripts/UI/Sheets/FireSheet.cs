using System.Collections.Generic;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The camp, on one sheet of parchment.**
    ///
    /// What the fire is holding, what it is doing with it, and who is doing
    /// it. The old answer was three things at once -- `CampSheet`'s bar and
    /// store rows along the bottom, `CampCrewList`'s names down the right,
    /// and a build list that appeared under whichever of them you had
    /// tapped. This is all of that in one place, unfolded beside the fire it
    /// is about.
    ///
    /// **It reads and it calls; it never decides.** Every store, rate, day
    /// and mood word comes out of `OutpostLedger`, and every button is the
    /// same `Outpost` / `CampSiting` call the legacy panels make. The only
    /// arithmetic in this file is division for display.
    public class FireSheet : ISheetFramed
    {
        /// Passed as `focus` to open with the lookout row expanded -- what a
        /// tap on the watchtower itself asks for.
        public const string FocusLookout = "lookout";

        // --- the four tabs (2026-09-22, "one sheet, tabs") -------------------
        //
        // Kevin, after three mockups on the phone: the camp sheet is ONE
        // frame with four tabs, and "Raise a building" and "Load ship" stop
        // being buttons that grow the card. They are the build and ship tabs.
        // Nothing was dropped -- every store, order, hand, plan and manifest
        // line the sheet had is still here, on one of the four.
        // 2026-09-22, "pages you swipe between": a tab is a PAGE, and a
        // section that would not fit the band becomes as many pages as it
        // takes. Nothing scrolls and nothing is dropped.
        const int PgCamp = 0;      // the piles, and what is going up
        const int PgOrders = 1;    // rations, priority, the watch, the next hand
        const int PgHands = 2;
        const int PgBuild = 3;
        const int PgShip = 4;      // the manifest's three sections, embedded
        const int PgCargo = 5;
        const int PgCrew = 6;

        readonly Outpost outpost;
        readonly string focus;
        readonly string islandName;

        int tab = -1;

        public FireSheet(Outpost o, string focus = null)
        {
            outpost = o;
            this.focus = focus;
            islandName = o != null && o.Island != null ? o.Island.name : "the camp";
            // The watch is an ORDER, and which PAGE the orders are on depends
            // on the band -- their own page on a phone, the bottom of the
            // camp page on a desk. `Plan` resolves it (see `OrdersPage`) and
            // pins `tab` there before the host reads it; this only has to
            // stop the session's remembered page winning, which a non-negative
            // `tab` does.
            if (focus == FocusLookout) tab = 0;
        }

        // --- the frame ---------------------------------------------------------

        public int Tab { get { Plan(); return tab; } }
        public void SetTab(int index) { tab = index; }
        public Color Accent => SheetTheme.Ember;

        /// One entry per page: which section it is, and which page OF that
        /// section. `part` on `PgCamp` doubles as "the orders block is on
        /// this page too", which is what happens on a desk where the band is
        /// tall enough to carry both.
        struct Pg { public int kind; public int part; }

        readonly List<Pg> pages = new List<Pg>();
        string[] labels = { "camp" };
        long planKey = long.MinValue;

        int handsPerPage = 5, buildPerPage = 5;
        int handPart, buildPart;
        bool campCarriesOrders;

        public string[] TabLabels { get { Plan(); return labels; } }

        /// **The page plan, out of the band the frame actually has.**
        ///
        /// Every count here is read, never assumed: how many store tiles the
        /// camp is showing, how many hands live on it, how many plans it can
        /// afford, how much cargo and crew the manifest has. The band comes
        /// from `SheetHost`, which is the safe area and the frame's own
        /// chrome and nothing else -- so the same camp is five pages on a
        /// phone and three on a desk without a number being written twice.
        void Plan()
        {
            var l = L;
            if (outpost == null || l == null) return;

            bool alongside = CampLoading.Alongside(outpost);
            float band = SheetHost.BandHeight;
            int storeTiles = ShownCount(l);
            int handCount = l.hands.Count;
            int planCount = CountBuildable();
            int siteCount = Mathf.Min(l.SiteCount, SitesShown);
            int cargo = alongside ? ship.CargoCount : 0;
            int crew = alongside ? ship.CrewRows : 0;

            long key = Mathf.RoundToInt(band) * 1000003L
                       + storeTiles * 7919L + handCount * 131L + planCount * 31L
                       + cargo * 17L + crew * 7L + (alongside ? 1L : 0L)
                       + siteCount * 3L;
            if (key == planKey) return;
            planKey = key;

            // What the two halves of the camp page cost, in panel units.
            // `SheetKit`'s constants are the USS heights rounded up, so this
            // is arithmetic on the stylesheet rather than a guess at it.
            float storeRows = storeTiles <= 4 ? 1f : 2f;
            // The "what is going up" block is one `ListRow` per queued site
            // now, and an empty queue is still one note's worth of "Nothing
            // going up" -- so the band arithmetic counts rows, not a note.
            float campPx = storeRows * SheetKit.StorePx
                           + (siteCount == 0 ? SheetKit.NotePx : siteCount * SheetKit.RowPx);
            float ordersPx = SheetKit.EyebrowPx + SheetKit.SegPx + SheetKit.TextPx
                             + SheetKit.SegPx + SheetKit.QuietPx + SheetKit.TextPx
                             + SheetKit.BarPx;
            campCarriesOrders = campPx + SheetKit.RulePx + ordersPx <= band;

            handsPerPage = SheetHost.RowsThatFit(SheetKit.RowPx, SheetKit.EyebrowPx);
            buildPerPage = SheetHost.RowsThatFit(SheetKit.QuietPx + 4f, SheetKit.EyebrowPx);
            int handPages = SheetKit.PageCount(Mathf.Max(1, handCount), handsPerPage);
            int buildPages = SheetKit.PageCount(Mathf.Max(1, planCount), buildPerPage);

            pages.Clear();
            pages.Add(new Pg { kind = PgCamp, part = campCarriesOrders ? 1 : 0 });
            if (!campCarriesOrders) pages.Add(new Pg { kind = PgOrders, part = 0 });
            for (int i = 0; i < handPages; i++) pages.Add(new Pg { kind = PgHands, part = i });
            for (int i = 0; i < buildPages; i++) pages.Add(new Pg { kind = PgBuild, part = i });
            pages.Add(new Pg { kind = PgShip, part = 0 });
            if (alongside)
            {
                for (int i = 0; i < ship.CargoPages; i++)
                    pages.Add(new Pg { kind = PgCargo, part = i });
                for (int i = 0; i < ship.CrewPages; i++)
                    pages.Add(new Pg { kind = PgCrew, part = i });
            }

            if (labels.Length != pages.Count) labels = new string[pages.Count];
            int hp = 0, bp = 0, cp = 0, rp = 0;
            for (int i = 0; i < pages.Count; i++)
            {
                switch (pages[i].kind)
                {
                    case PgCamp: labels[i] = "camp"; break;
                    case PgOrders: labels[i] = "orders"; break;
                    case PgHands:
                        labels[i] = handPages > 1
                            ? SheetKit.PageLabel("hands", hp, handPages)
                            : (handCount > 0 ? "hands · " + handCount : "hands");
                        hp++;
                        break;
                    case PgBuild:
                        labels[i] = SheetKit.PageLabel("build", bp, buildPages); bp++; break;
                    case PgShip: labels[i] = "ship"; break;
                    case PgCargo:
                        labels[i] = SheetKit.PageLabel("cargo", cp, ship.CargoPages); cp++; break;
                    default:
                        labels[i] = SheetKit.PageLabel("crew", rp, ship.CrewPages); rp++; break;
                }
            }

            // A tap on the watchtower is a question about the watch, and the
            // watch lives in the orders block -- which is its own page on a
            // phone and part of the camp page on a desk. Resolved here rather
            // than in the constructor, because only the plan knows which.
            if (focus == FocusLookout && !focusDone)
            {
                focusDone = true;
                tab = OrdersPage;
            }
            if (tab >= pages.Count) tab = pages.Count - 1;
        }

        bool focusDone;

        /// The page the rations / priority / watch / recruit block is on.
        int OrdersPage
        {
            get
            {
                for (int i = 0; i < pages.Count; i++)
                    if (pages[i].kind == PgOrders
                        || (pages[i].kind == PgCamp && pages[i].part == 1)) return i;
                return 0;
            }
        }

        Pg Live
        {
            get
            {
                Plan();
                if (pages.Count == 0) return new Pg { kind = PgCamp, part = 1 };
                return pages[Mathf.Clamp(tab, 0, pages.Count - 1)];
            }
        }

        int CountBuildable()
        {
            int n = 0;
            foreach (var _ in outpost.Buildable()) n++;
            return n;
        }

        public VisualElement BuildHeader() =>
            SheetKit.Header("the camp", Title, SheetTheme.Ember, "🔥", () => Sheets.Close());

        /// **The action row is the ship tab's alone.** The camp, hands and
        /// build tabs are made of rows that ARE their own actions -- a pill
        /// group, a "change", a plan with its price on it -- and a pinned row
        /// under them would be a second place to look for the same verbs.
        public VisualElement BuildActions()
        {
            int k = Live.kind;
            return k == PgShip || k == PgCargo || k == PgCrew ? ship.BuildActions() : null;
        }

        public string Title => islandName;

        public Vector3 AnchorWorld => outpost != null ? outpost.CampCentre : Vector3.zero;

        /// The camp is still there and she is still lying at it. Sailing away
        /// closes the sheet, because every button on it is a thing you can
        /// only do from the beach.
        public bool StillValid =>
            outpost != null && outpost.Ledger != null
            && (outpost.HasCamp || outpost.Building)
            && CampLoading.Alongside(outpost);

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        // --- the pieces kept between refreshes ---------------------------------

        VisualElement storesHolder;
        VisualElement noteHolder;
        VisualElement rationsHolder;
        Label rationsLine;
        VisualElement priorityHolder;
        VisualElement lookoutHolder;
        Label recruitLine;
        VisualElement recruitBar;
        Label bedsEyebrow;
        VisualElement handsHolder;
        VisualElement buildListHolder;

        // Keys, so a block is rebuilt when what it SAYS has changed and not
        // once a frame. The same idea as `CampSheet.HeadKey` -- IMGUI's reason
        // for it was text meshes, ours is that a rebuilt element loses the
        // press that is happening on it.
        long storesKey = long.MinValue;
        long noteKey = long.MinValue;
        int rationsKey = -99;
        int priorityKey = -99;
        long lookoutKey = long.MinValue;
        long recruitKey = long.MinValue;
        long handsKey = long.MinValue;

        /// **The body of the LIVE tab, and only that.**
        ///
        /// The host calls this again on every tab change, so each call starts
        /// by dropping the element references the last tab left behind --
        /// otherwise `Refresh` would keep writing into a tree that is no
        /// longer in the card, which costs nothing visible and hides a real
        /// fault for a week.
        public VisualElement Build()
        {
            Forget();
            var page = Live;
            handPart = page.kind == PgHands ? page.part : 0;
            buildPart = page.kind == PgBuild ? page.part : 0;

            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            switch (page.kind)
            {
                case PgOrders: BuildOrders(root); break;
                case PgHands: BuildHands(root); break;
                case PgBuild: BuildBuild(root); break;
                case PgShip: BuildShip(root, ShipSheet.SecHold, 0); break;
                case PgCargo: BuildShip(root, ShipSheet.SecCargo, page.part); break;
                case PgCrew: BuildShip(root, ShipSheet.SecCrew, page.part); break;
                default:
                    BuildCamp(root);
                    // On a desk the band carries the piles AND the orders, so
                    // the camp is one page rather than two half-empty ones.
                    if (page.part == 1) { root.Add(SheetKit.Rule()); BuildOrders(root); }
                    break;
            }

            Refresh();
            return root;
        }

        /// Every cached element and every cache key, back to nothing. Called
        /// on each `Build`, so a stale reference cannot outlive its tab and a
        /// key cannot suppress the first fill of a freshly built block.
        void Forget()
        {
            storesHolder = noteHolder = rationsHolder = priorityHolder = null;
            lookoutHolder = recruitBar = handsHolder = buildListHolder = null;
            rationsLine = recruitLine = bedsEyebrow = null;
            storesKey = noteKey = lookoutKey = recruitKey = handsKey = long.MinValue;
            rationsKey = priorityKey = -99;
            buildKey = long.MinValue;
        }

        // --- tab: camp ----------------------------------------------------------

        /// What the fire is holding, what it is doing with it, and the four
        /// standing orders. Everything on this tab was on the old sheet above
        /// the roster.
        void BuildCamp(VisualElement root)
        {
            storesHolder = SheetBits.Holder();
            root.Add(storesHolder);

            noteHolder = SheetBits.Holder();
            root.Add(noteHolder);
        }

        // --- page: orders --------------------------------------------------
        //
        // The four standing orders: how much they eat, what they work on
        // first, who is at the tower, and when the next hand arrives. Its own
        // page on a phone, the bottom of the camp page on a desk -- the plan
        // decides, out of the band.
        void BuildOrders(VisualElement root)
        {
            root.Add(SheetKit.Eyebrow("orders"));

            rationsHolder = SheetBits.Holder();
            root.Add(rationsHolder);
            rationsLine = SheetKit.Text("", false, true, 12f);
            root.Add(rationsLine);

            priorityHolder = SheetBits.Holder();
            root.Add(priorityHolder);

            lookoutHolder = SheetBits.Holder();
            root.Add(lookoutHolder);

            recruitLine = SheetKit.Text("", false, true, 12f);
            root.Add(recruitLine);
            recruitBar = SheetBits.Holder();
            root.Add(recruitBar);
        }

        // --- tab: hands ---------------------------------------------------------

        void BuildHands(VisualElement root)
        {
            bedsEyebrow = SheetKit.Eyebrow("hands ashore");
            root.Add(bedsEyebrow);
            handsHolder = SheetBits.Holder();
            root.Add(handsHolder);
        }

        /// The rows this page of the roster covers.
        void Slice(int count, int part, int perPage, out int from, out int to)
        {
            from = Mathf.Clamp(part * perPage, 0, Mathf.Max(0, count));
            to = Mathf.Min(count, from + perPage);
        }

        // --- tab: build ---------------------------------------------------------

        /// **The build list is a tab, not a drawer.** It used to hang off a
        /// "Raise a building" button that grew the card by however many plans
        /// the camp could afford; on a phone that pushed the roster off the
        /// bottom and moved every button under the thumb. Same list, same
        /// `CampSiting.Begin`, same prices -- it simply has its own third of
        /// the screen now.
        void BuildBuild(VisualElement root)
        {
            root.Add(SheetKit.Eyebrow("raise a building"));
            buildListHolder = SheetBits.Holder();
            root.Add(buildListHolder);
        }

        void FillBuildList()
        {
            if (buildListHolder == null) return;
            var l = L;
            buildListHolder.Clear();
            if (l == null) return;
            // **No "Something is already going up" any more (2026-09-22).**
            // Kevin: *"I want to be able to place more blueprints at once."*
            // The list is the list; what is already queued is on the camp
            // page, and `Outpost.SiteFresh` is what still refuses a SECOND
            // copy of the same plan.
            Slice(CountBuildable(), buildPart, buildPerPage, out int bFrom, out int bTo);
            int n = 0, seen = 0;
            foreach (var plan in outpost.Buildable())
            {
                var p = plan;
                n++;
                int at = seen++;
                if (at < bFrom || at >= bTo) continue;
                string price = p.stoneCost > 0
                    ? $"{p.label} — {p.cost} timber {p.stoneCost} stone"
                    : $"{p.label} — {p.cost} timber";
                buildListHolder.Add(SheetKit.Btn(price, () =>
                {
                    CampSiting.Begin(outpost, p, SheetBits.ShipTransform);
                    // Siting takes the whole screen's attention; a sheet lying
                    // over the ground you are about to tap is the bug the old
                    // bottom bar had.
                    Sheets.Close();
                }, false, true));
            }
            if (n == 0)
                buildListHolder.Add(SheetKit.Note("Nothing the camp can afford yet"));
        }

        // --- tab: ship ------------------------------------------------------------

        /// **The manifest, inside the camp's frame.** "Load ship" used to
        /// open a second sheet; the decision it is about -- how much you dare
        /// take -- belongs to the same visit to the beach, so it is a tab.
        /// The content is `ShipSheet`'s own, built by `ShipSheet`, so the two
        /// cannot drift: there is one manifest in the game and this embeds
        /// it rather than copying it.
        readonly ShipSheet ship = new ShipSheet();

        void BuildShip(VisualElement root, int section, int part)
        {
            if (!CampLoading.Alongside(outpost))
            {
                root.Add(SheetKit.Note("She is not lying alongside"));
                return;
            }
            root.Add(ship.BuildSection(section, part, ship.CargoPerPage, ship.CrewPerPage));
        }

        public void Refresh()
        {
            var l = L;
            if (outpost == null || l == null) return;

            // Settle the books before reading them. Everything below is a
            // read of numbers this call brings up to now; `CatchUp` is
            // idempotent within a frame (`Outpost.CatchUp`).
            outpost.CatchUp();

            switch (Live.kind)
            {
                case PgHands:
                    Hands(l);
                    break;
                case PgBuild:
                    // Keyed on what the list SAYS: the stores it prices
                    // against, and whether a drawing is already up.
                    long bkey = l.Total * 31L + l.SiteCount * 7919L
                                + l.built.Count * 131L;
                    bkey = bkey * 31L + buildPart;
                    if (bkey != buildKey) { buildKey = bkey; FillBuildList(); }
                    break;
                case PgShip:
                case PgCargo:
                case PgCrew:
                    if (CampLoading.Alongside(outpost)) ship.Refresh();
                    break;
                case PgOrders:
                    RationsBlock(l);
                    PriorityBlock(l);
                    Lookout(l);
                    Recruit(l);
                    break;
                default:
                    Stores(l);
                    Note(l);
                    // The camp page carries the orders too where the band is
                    // tall enough; the blocks below are no-ops when their
                    // holders were not built.
                    RationsBlock(l);
                    PriorityBlock(l);
                    Lookout(l);
                    Recruit(l);
                    break;
            }
        }

        long buildKey = long.MinValue;

        // --- the piles ---------------------------------------------------------

        /// Timber, stone and food always; anything else only when the camp
        /// actually holds some, the same rule `CampSheet.BuildRows` follows --
        /// a kind that has been carried away entirely drops off the list.
        /// Boards, Tools, Brick and Arrows are made, not found, so "holds some" is
        /// not the only way one earns its place here -- a quarryman with an
        /// empty brick pile is still worth a tile, so a hand assigned to a
        /// building that makes the kind counts too (`AnyWorkerMakes`).
        static readonly string[] Always = { Res.Timber, Res.Stone, Res.Food };
        static readonly string[] Sometimes =
            { Res.Ore, Res.Spice, Res.Game, Res.Boards, Res.Tools, Res.Brick, Res.Arrows };
        readonly List<string> shown = new List<string>();

        /// How many store tiles the camp page will show -- the same rule
        /// `Stores` follows, asked before the page is built so the plan can
        /// tell a one-row tile band from a two-row one.
        static int ShownCount(OutpostLedger l)
        {
            int n = Always.Length;
            foreach (var r in Sometimes)
                if (l.CountOf(r) > 0 || AnyWorkerMakes(l, r)) n++;
            return n;
        }

        static bool AnyWorkerMakes(OutpostLedger l, string res)
        {
            foreach (var h in l.hands)
            {
                if (h == null || h.order != OutpostOrder.Work) continue;
                if (BuildPlans.Named(h.target).makes == res) return true;
            }
            return false;
        }

        void Stores(OutpostLedger l)
        {
            shown.Clear();
            foreach (var r in Always) shown.Add(r);
            foreach (var r in Sometimes)
                if (l.CountOf(r) > 0 || AnyWorkerMakes(l, r)) shown.Add(r);

            // More than four stores wraps to a second row -- the tile row was
            // never meant to hold more than the four gatherables it shipped
            // with, and a quarry (or a sawmill and a smithy both running) can
            // now put five or six kinds on the fire at once.
            long key = l.ceilingPer * 1000003L + l.hands.Count;
            foreach (var r in shown)
            {
                key = key * 31 + r.GetHashCode();
                key = key * 31 + l.CountOf(r);
                key = key * 31 + Mathf.RoundToInt(l.RatePerDay(r) * 10f);
            }
            if (key == storesKey) return;
            storesKey = key;

            // **One short string per slot.** The store tile has a big number
            // and a small line and nothing else; a rate label stacked under it
            // (the first cut) landed on top of the next tile's heading. So the
            // rate rides in the small line when there is one to print, and is
            // simply absent when the pile is not moving -- "of 30" says
            // everything a steady pile has to say.
            var cols = new VisualElement[shown.Count];
            for (int i = 0; i < shown.Count; i++)
            {
                string res = shown[i];
                int kept = l.CountOf(res);
                string rate = SheetBits.RateLine(l, res);
                string big, small;

                if (res == Res.Food)
                {
                    // **Food is counted in days, not in units.** How many
                    // logs are on the ground is a fact; how long the camp
                    // eats is the decision, and it is the one the rations
                    // pills below are about.
                    float days = SheetBits.FoodDays(l);
                    if (l.hands.Count == 0 || days < 0f)
                    {
                        big = kept.ToString();
                        small = l.hands.Count == 0 ? "kept · nobody eats" : "kept · they eat nothing";
                    }
                    else
                    {
                        big = days.ToString("0.#");
                        small = $"days · {kept} kept";
                    }
                }
                else
                {
                    big = kept.ToString();
                    small = $"of {l.ceilingPer}";
                }
                if (rate.Length > 0) small += " · " + rate;

                // Brick reads oddly in the singular a pile of stone or timber
                // does not -- "1 brick" is fine, but the tile's own label sits
                // above a count and wants the plural, the same way the store
                // shelf itself would say "bricks".
                string label = res == Res.Brick ? "bricks" : CampLoading.Lower(res);
                cols[i] = SheetKit.Store(label, big, small,
                    l.Fill01(res), SheetBits.Colour(res));
            }

            // Four to a row, same as the row always shipped with; a fifth
            // kind (a quarry running alongside a sawmill, say) starts a
            // second row rather than squeezing a fifth tile into the first.
            if (cols.Length <= 4)
            {
                SheetBits.Swap(storesHolder, SheetKit.Row(cols));
            }
            else
            {
                int firstRow = (cols.Length + 1) / 2;
                firstRow = Mathf.Min(firstRow, 4);
                var top = new VisualElement[firstRow];
                var bottom = new VisualElement[cols.Length - firstRow];
                System.Array.Copy(cols, 0, top, 0, firstRow);
                System.Array.Copy(cols, firstRow, bottom, 0, bottom.Length);
                SheetBits.Swap(storesHolder, SheetKit.Col(SheetKit.Row(top), SheetKit.Row(bottom)));
            }
        }

        // --- what is going up --------------------------------------------------

        /// "Kitchen going up, Bo on it. Stocked, about a day." -- one
        /// sentence, out of the pending row. The words are the sheet's; every
        /// number in them is the ledger's.
        void Note(OutpostLedger l)
        {
            // Keyed on the whole QUEUE, not on one row: a second drawing
            // sited while the sheet is open has to show up.
            long key = l.HandsOn(OutpostOrder.Build) * 7919L;
            foreach (var q in l.sites)
            {
                if (q == null) continue;
                key = key * 31L + (q.planId != null ? q.planId.GetHashCode() : 0);
                key = key * 31L + q.done * 31L + q.stoneDone * 7L + q.brickDone;
                // The building phase moves without a counter moving.
                key = key * 31L + Mathf.RoundToInt(q.Build01 * 100f);
            }
            if (key == noteKey) return;
            noteKey = key;

            if (l.SiteCount == 0)
            {
                SheetBits.Swap(noteHolder, SheetKit.Note("Nothing going up"));
                return;
            }

            // Who is on it: the first builder by name, and how many after
            // him. The crew is the CAMP's, not the site's -- builders serve
            // the queue in order (`OutpostLedger.Focus`), so the same names
            // are on whichever drawing is next -- and the line says so by
            // naming them only against the one being worked.
            string who = null;
            int builders = 0;
            foreach (var h in l.hands)
                if (h != null && h.order == OutpostOrder.Build)
                {
                    builders++;
                    if (who == null) who = h.name;
                }
            string crew = builders == 0 ? "nobody on it"
                : builders == 1 ? who + " on it"
                : $"{who} and {builders - 1} more on it";

            // **One `ListRow` per queued site**, so the pager can count them
            // the way it counts the roster -- see `Plan`, which asks the
            // band how many of these fit before it decides whether the
            // orders block shares this page.
            var col = new VisualElement();
            var focus = l.Focus;
            int shown = 0;
            foreach (var q in l.sites)
            {
                if (q == null || string.IsNullOrEmpty(q.planId)) continue;
                if (shown >= SitesShown) break;
                shown++;
                var plan = BuildPlans.Named(q.planId).WithLength(q.length);
                string label = string.IsNullOrEmpty(plan.label) ? "something" : plan.label;

                // **The phase, 2026-09-23**: "stocking 3/6 logs, 2/2
                // stone" while they fetch, then "building 40%" while they
                // raise it. One string (`PendingBuild.PhaseLine`), shared
                // with the drawing's own sheet, so the camp page and the
                // blueprint cannot say different things.
                string stock = q.PhaseLine;

                // Only the site being SERVED gets the crew on it; the ones
                // behind it in the queue are waiting their turn and say so,
                // and a stocked one is waiting for the ground, not for wood.
                string tail = q.Complete ? "going up"
                    : q == focus ? crew
                    : "waiting its turn";

                col.Add(SheetKit.ListRow(
                    SheetKit.Text(label, true),
                    SheetKit.Text(stock, false, true, 12f),
                    SheetKit.Text(tail, false, true, 12f)));
            }
            if (l.SiteCount > shown)
                col.Add(SheetKit.Note($"and {l.SiteCount - shown} more queued"));
            SheetBits.Swap(noteHolder, col);
        }

        /// **How many queued sites the camp page prints before it summarises
        /// the rest.** The page is a third of a phone screen and the roster
        /// is under it; past this the queue says "and 3 more queued" rather
        /// than pushing the sheet off the bottom, which is the failure the
        /// pager exists to prevent.
        const int SitesShown = 4;

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // --- rations -----------------------------------------------------------

        static readonly string[] RationOptions = { "full", "half", "none" };

        void RationsBlock(OutpostLedger l)
        {
            int sel = (int)l.rations;
            // The line under it moves with the pile as well as with the
            // choice, so it is keyed on both.
            if (rationsLine != null) rationsLine.text = SheetBits.FoodDaysLine(l);
            if (sel == rationsKey) return;
            rationsKey = sel;
            SheetBits.Swap(rationsHolder, SheetKit.Segmented(RationOptions, sel, i =>
            {
                // The same field the ledger's own tick reads. Nothing here
                // works out what a ration costs -- `EatMultiplier` does.
                l.rations = (Rations)i;
                rationsKey = i;
                Refresh();
            }));
        }

        // --- what they work on first -------------------------------------------

        static readonly string[] PriorityOptions = { "even", "food first", "timber first" };

        void PriorityBlock(OutpostLedger l)
        {
            int sel = (int)l.priority;
            if (sel == priorityKey) return;
            priorityKey = sel;
            SheetBits.Swap(priorityHolder, SheetKit.Segmented(PriorityOptions, sel, i =>
            {
                l.priority = (WorkPriority)i;
                priorityKey = i;
                Refresh();
            }));
        }

        // --- the watch ----------------------------------------------------------

        /// **The one place the raid clock is a decision and not a warning.**
        /// `RaidLine` says one is coming; this says what stops it, and hands
        /// the player the hand who would stand it.
        void Lookout(OutpostLedger l)
        {
            var post = SheetBits.Lookout(l);
            bool tower = l.HasWatchtower;
            var idle = SheetBits.FirstIdle(l);
            // **Rounded through `Tenths`, never `RoundToInt` directly.** Both
            // raid figures are infinite whenever nothing can raid this camp,
            // and `Mathf.RoundToInt(Infinity)` is an int with no meaning --
            // which made this key thrash and the line rebuild every refresh.
            // **The quiver is part of this line, 2026-09-22.** A posted
            // lookout with arrows looses a volley when a raid lands
            // (`OutpostLedger.LookoutVolley`), so the arrows the camp holds
            // change what this row is promising -- and therefore have to be
            // in the key, or the promise would go stale the moment a
            // fletcher finished one.
            int quiver = l.CountOf(Res.Arrows);
            long key = (tower ? 1L : 0L) * 7
                       + (post != null ? post.name.GetHashCode() : 0) * 31L
                       + (idle != null ? 3 : 0)
                       + quiver * 10007L
                       + Tenths(l.RaidDaysIfWatched) * 131L
                       + Tenths(l.RaidDaysUnwatched) * 1031L;
            if (key == lookoutKey) return;
            lookoutKey = key;

            VisualElement made;
            if (!tower)
            {
                made = SheetKit.Text("No watchtower · " + EveryLine(l.RaidDaysUnwatched),
                    false, true, 12f);
            }
            else if (post != null)
            {
                // **A watched camp is not raided on a longer clock; it is not
                // raided.** `ThreatRatePerDay` is zero with a full guard, so
                // `RaidDaysIfWatched` is infinite -- and "raids every Infinity
                // days" is the arithmetic leaking through a sentence. The
                // sentence says what infinity MEANS here.
                string watch = float.IsInfinity(l.RaidDaysIfWatched) || float.IsNaN(l.RaidDaysIfWatched)
                    ? $"{post.name} at the tower · no raids while she keeps watch"
                    : $"{post.name} at the tower · raids every {l.RaidDaysIfWatched:0.#} days";
                // What she has to shoot with, when there is anything. Named
                // only when the camp holds some: a lookout with an empty
                // quiver is the line as it always read.
                if (quiver > 0)
                    watch += quiver == 1 ? " · 1 arrow" : $" · {quiver} arrows";
                made = SheetKit.Text(watch, false, true, 12f);
            }
            else
            {
                var btn = SheetKit.Btn("post a lookout", () =>
                {
                    var free = SheetBits.FirstIdle(l);
                    // The assign verb, exactly as `CampCrewList` presses it
                    // (CampCrewList.cs:177): the plan id IS the position.
                    if (free != null) outpost.Assign(free, OutpostLedger.WatchtowerId);
                    lookoutKey = long.MinValue;
                    Refresh();
                }, false, true);
                btn.SetEnabled(idle != null);
                made = SheetKit.Row(
                    SheetKit.Text("Tower unmanned · " + EveryLine(l.RaidDaysUnwatched),
                        false, true, 12f),
                    btn);
            }
            SheetBits.Swap(lookoutHolder, made);
        }

        /// "raids every 4 days", or what an infinite clock actually means.
        static string EveryLine(float days) =>
            float.IsInfinity(days) || float.IsNaN(days) || days <= 0f
                ? "no raiders about"
                : $"raids every {days:0.#} days";

        /// A float rounded to tenths as a whole number, safe on the
        /// infinities the raid clock hands out.
        static long Tenths(float v) =>
            float.IsInfinity(v) || float.IsNaN(v) ? long.MaxValue / 4
                                                  : Mathf.RoundToInt(v * 10f);

        // --- the next hand -------------------------------------------------------

        /// **One line about the next hand, and nothing about beds.**
        ///
        /// It printed `OutpostLedger.RecruitLine` when the huts were full,
        /// which is "4 of 2 beds" -- the bed count a second time (the roster
        /// eyebrow below already carried it) and an empty bar under it for a
        /// recruit that is not coming. A camp with no room says so in four
        /// words and drops the bar.
        void Recruit(OutpostLedger l)
        {
            bool room = l.Housed < l.HousingCapacity;
            float days = Mathf.Max(0f, OutpostLedger.DaysPerRecruit - l.recruitProgress);
            long key = Mathf.RoundToInt(l.RecruitProgress01 * 100f) * 31L
                       + l.HousingCapacity * 7L + (room ? 1L : 0L);
            if (key == recruitKey) return;
            recruitKey = key;

            if (recruitLine != null)
                recruitLine.text = room
                    ? $"Next hand in {days:0.#} days · needs {OutpostLedger.RecruitFoodCost} food"
                    : "No room for another hand";
            if (room)
                SheetBits.Swap(recruitBar, SheetKit.Bar(l.RecruitProgress01, SheetTheme.Moss));
            else
                { if (recruitBar != null) recruitBar.Clear(); }
        }

        // --- who lives here ------------------------------------------------------

        void Hands(OutpostLedger l)
        {
            long key = l.hands.Count * 1000003L + l.HousingCapacity
                       + handPart * 100003L + handsPerPage * 17L;
            foreach (var h in l.hands)
            {
                if (h == null) continue;
                key = key * 31 + (h.name != null ? h.name.GetHashCode() : 0);
                key = key * 31 + (int)h.order;
                key = key * 31 + (h.target != null ? h.target.GetHashCode() : 0);
                key = key * 31 + Mathf.RoundToInt(h.mood * 20f);
            }
            if (key == handsKey) return;
            handsKey = key;

            // **Beds are counted in ONE place.** They were here and in the
            // recruit line above it, and the two said different things about
            // the same camp ("4 hands ashore · 2 beds" over "4 of 2 beds").
            // The recruit line owns the beds now; this owns the roster.
            Slice(l.hands.Count, handPart, handsPerPage, out int hFrom, out int hTo);
            if (bedsEyebrow != null)
                bedsEyebrow.text = l.hands.Count == 1
                    ? "1 hand ashore"
                    : $"{l.hands.Count} hands ashore";

            if (handsHolder == null) return;
            handsHolder.Clear();
            if (l.hands.Count == 0)
            {
                handsHolder.Add(SheetKit.Text("nobody lives here yet", false, true, 12f));
                return;
            }

            for (int i = hFrom; i < hTo; i++)
            {
                var h = l.hands[i];
                if (h == null) continue;
                var who = h;      // the closure's own copy; rows outlive the loop
                string mood = who.MoodWord;
                // **One line, beside the name, not under it.** A column of
                // name-over-job wrapped "lookout · angry" onto two lines and
                // pushed the row past the 40 px it is allowed, so each hand
                // took two rows' worth of sheet and the roster ran off the
                // bottom. Name, then what they are doing, then the verb.
                handsHolder.Add(SheetKit.ListRow(
                    SheetKit.Token(SheetBits.Initial(who.name), who.Angry,
                        SheetBits.JobGlyph(who), () => OpenHand(who.name)),
                    SheetKit.Text(who.name, true),
                    SheetKit.Text(mood.Length > 0 ? $"{who.Doing} · {mood}" : who.Doing,
                        false, true, 12f),
                    SheetKit.Btn("change", () => OpenHand(who.name), false, true)));
            }
        }

        void OpenHand(string who)
        {
            if (outpost == null || string.IsNullOrEmpty(who)) return;
            Sheets.Open(new HandSheet(outpost, who));
        }

    }
}
