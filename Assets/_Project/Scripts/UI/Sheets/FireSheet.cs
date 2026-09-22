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
        public const int TabCamp = 0;
        public const int TabHands = 1;
        public const int TabBuild = 2;
        public const int TabShip = 3;

        readonly Outpost outpost;
        readonly string focus;
        readonly string islandName;

        int tab = -1;

        public FireSheet(Outpost o, string focus = null)
        {
            outpost = o;
            this.focus = focus;
            islandName = o != null && o.Island != null ? o.Island.name : "the camp";
            // The watch is an ORDER, and the orders live on the camp tab. A
            // tap on the watchtower therefore pins that tab rather than
            // taking whichever one the session was left on -- the host only
            // restores a remembered tab when `Tab` is still -1.
            if (focus == FocusLookout) tab = TabCamp;
        }

        // --- the frame ---------------------------------------------------------

        public int Tab => tab;
        public void SetTab(int index) { tab = index; }
        public Color Accent => SheetTheme.Ember;

        readonly string[] labels = { "camp", "hands", "build", "ship" };

        /// Short and sentence-case, with the one count worth carrying: how
        /// many hands live here, so the roster can be read without opening
        /// it. Rebuilt into the same array every time, because the host
        /// re-labels the strip four times a second and an allocation per
        /// refresh is an allocation per refresh.
        public string[] TabLabels
        {
            get
            {
                var l = L;
                int n = l != null ? l.hands.Count : 0;
                labels[TabHands] = n > 0 ? "hands · " + n : "hands";
                return labels;
            }
        }

        public VisualElement BuildHeader() =>
            SheetKit.Header("the camp", Title, SheetTheme.Ember, "🔥", () => Sheets.Close());

        /// **The action row is the ship tab's alone.** The camp, hands and
        /// build tabs are made of rows that ARE their own actions -- a pill
        /// group, a "change", a plan with its price on it -- and a pinned row
        /// under them would be a second place to look for the same verbs.
        public VisualElement BuildActions() =>
            tab == TabShip ? ship.BuildActions() : null;

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
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            switch (tab)
            {
                case TabHands: BuildHands(root); break;
                case TabBuild: BuildBuild(root); break;
                case TabShip: BuildShip(root); break;
                default: BuildCamp(root); break;
            }

            Refresh();

            // A tap on the watchtower is a question about the watch, so the
            // sheet opens looking at that row. One frame later, because the
            // panel has not laid itself out yet and a scroll before layout
            // scrolls nothing.
            if (focus == FocusLookout && tab == TabCamp && lookoutHolder != null)
                lookoutHolder.schedule.Execute(() =>
                {
                    var sv = lookoutHolder.GetFirstAncestorOfType<ScrollView>();
                    if (sv != null) sv.ScrollTo(lookoutHolder);
                }).ExecuteLater(1);

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

            root.Add(SheetKit.Rule());

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
            if (outpost.Building)
            {
                buildListHolder.Add(SheetKit.Note("Something is already going up"));
                return;
            }
            int n = 0;
            foreach (var plan in outpost.Buildable())
            {
                var p = plan;
                n++;
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

        void BuildShip(VisualElement root)
        {
            if (!CampLoading.Alongside(outpost))
            {
                root.Add(SheetKit.Note("She is not lying alongside"));
                return;
            }
            root.Add(ship.BuildWhole());
        }

        public void Refresh()
        {
            var l = L;
            if (outpost == null || l == null) return;

            // Settle the books before reading them. Everything below is a
            // read of numbers this call brings up to now; `CatchUp` is
            // idempotent within a frame (`Outpost.CatchUp`).
            outpost.CatchUp();

            switch (tab)
            {
                case TabHands:
                    Hands(l);
                    break;
                case TabBuild:
                    // Keyed on what the list SAYS: the stores it prices
                    // against, and whether a drawing is already up.
                    long bkey = l.Total * 31L + (outpost.Building ? 1 : 0) * 7919L
                                + l.built.Count * 131L;
                    if (bkey != buildKey) { buildKey = bkey; FillBuildList(); }
                    break;
                case TabShip:
                    if (CampLoading.Alongside(outpost)) ship.Refresh();
                    break;
                default:
                    Stores(l);
                    Note(l);
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
            var p = l.pending;
            long key = p == null ? 0L
                : ((p.planId != null ? p.planId.GetHashCode() : 0) * 31L + p.done) * 31L
                  + p.stoneDone + l.HandsOn(OutpostOrder.Build) * 7919L;
            if (key == noteKey) return;
            noteKey = key;

            if (p == null)
            {
                SheetBits.Swap(noteHolder, SheetKit.Note("Nothing going up"));
                return;
            }

            var plan = BuildPlans.Named(p.planId).WithLength(p.length);
            string label = string.IsNullOrEmpty(plan.label) ? "something" : plan.label;

            // Who is on it: the first builder by name, and how many after him.
            string who = null;
            int builders = 0;
            foreach (var h in l.hands)
                if (h != null && h.order == OutpostOrder.Build)
                {
                    builders++;
                    if (who == null) who = h.name;
                }

            int timberLeft = Mathf.Max(0, p.needed - p.done);
            int stoneLeft = Mathf.Max(0, p.stoneNeeded - p.stoneDone);

            string crew = builders == 0 ? "nobody on it"
                : builders == 1 ? who + " on it"
                : $"{who} and {builders - 1} more on it";

            string stock;
            if (timberLeft == 0 && stoneLeft == 0) stock = "Stocked";
            else if (stoneLeft == 0) stock = $"Needs {timberLeft} timber";
            else if (timberLeft == 0) stock = $"Needs {stoneLeft} stone";
            else stock = $"Needs {timberLeft} timber, {stoneLeft} stone";

            // The clock is dropped when it would only repeat the crew phrase:
            // "nobody on it. Needs 5 timber, nobody is building it." was the
            // same fact three times in one sentence.
            string left = SiteSheet.DaysLeftLine(l, p, builders);
            SheetBits.Swap(noteHolder, SheetKit.Note(left == SiteSheet.Nobody
                ? $"{Cap(label)} going up, {crew}. {stock}."
                : $"{Cap(label)} going up, {crew}. {stock}, {left}."));
        }

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
            long key = l.hands.Count * 1000003L + l.HousingCapacity;
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

            foreach (var h in l.hands)
            {
                if (h == null) continue;
                var who = h;      // the closure's own copy; rows outlive the loop
                string mood = who.MoodWord;
                // **One line, beside the name, not under it.** A column of
                // name-over-job wrapped "lookout · angry" onto two lines and
                // pushed the row past the 40 px it is allowed, so each hand
                // took two rows' worth of sheet and the roster ran off the
                // bottom. Name, then what they are doing, then the verb.
                handsHolder.Add(SheetKit.Row(
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
