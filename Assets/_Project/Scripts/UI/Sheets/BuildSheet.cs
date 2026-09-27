using System;
using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **Camp › Build, 2026-09-27** (Kevin approved the Ledger follow-ups:
    /// the Build tab of the nav mockup, cards not rows). What the drawer's
    /// CAMP › Build row opens now, instead of the old campfire sheet's build
    /// tab. One tall page: a segmented control (Shelter · Food · Industry ·
    /// Defence · Sea), then a card per plan in that group -- a building
    /// glyph, the name, the plan's one-line blurb, the price of the NEXT
    /// copy as have/need item icons (red when short), and the copy count
    /// ("×2 of 3 — 4th at campfire III"). Order: can build, then short,
    /// then at its cap; plans the fire has not opened are grouped under
    /// "Needs campfire II", dashed and not tappable.
    ///
    /// **It reads and it calls; it never decides.** Prices are
    /// `OutpostLedger.PriceOfNext` (`BuildPlans.PriceForCopy`), caps
    /// `CopyLimit`/`CanAddCopy` (`Techs.Caps`), locks `PlanUnlocked`. A tap
    /// on a card is exactly what the old build rows did:
    /// `CampSiting.Begin(outpost, plan, ship)` and the sheet folds away so
    /// the ground is clear. A SHORT plan is still tappable (the builders
    /// fetch and cut what is missing, as they always have); a plan at its
    /// cap or still locked is not. Defence also carries the palisade
    /// (`Outpost.BeginWallSiting`, the wall tool) and the ladder
    /// (`LadderSiting.Start`), which are drawn, not sited.
    ///
    /// Kevin's no-scroll preference: a group holds at most five cards,
    /// which fit the tall band on the phone; the list sits in a clamped
    /// ScrollView only as the fallback for a short screen.
    public sealed class BuildSheet : ISheetFramed
    {
        /// **The ☰ hook**, set by `LedgerDrawer`'s constructor.
        public static Action OpenLedger;

        enum Group { Shelter, Food, Industry, Defence, Sea }
        static readonly string[] TabNames = { "Shelter", "Food", "Industry", "Defence", "Sea" };

        /// The tab this page was last left on, this session.
        static int lastTab = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { lastTab = -1; OpenLedger = null; }

        static Group GroupOf(string planId)
        {
            if (planId == BuildPlans.Hut.id || planId == BuildPlans.Storage.id || planId == BuildPlans.Storehouse.id)
                return Group.Shelter;
            if (planId == BuildPlans.Farm.id || planId == BuildPlans.FishingHut.id || planId == BuildPlans.Kitchen.id)
                return Group.Food;
            if (planId == BuildPlans.Watchtower.id) return Group.Defence;
            if (planId == BuildPlans.Pier.id || planId == BuildPlans.DryDock.id) return Group.Sea;
            return Group.Industry;
        }

        readonly Outpost outpost;
        int tab;

        /// `focusPlanId` opens the page on the group that plan is in (the
        /// "no spear" alert asks for the forge).
        public BuildSheet(Outpost camp, string focusPlanId = null)
        {
            outpost = camp;
            if (!string.IsNullOrEmpty(focusPlanId)) tab = (int)GroupOf(focusPlanId);
            else if (lastTab >= 0) tab = lastTab;
            else tab = FirstTabWithWork();
            lastTab = tab;
        }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        /// First group with a plan the camp can put up right now, else
        /// Shelter.
        int FirstTabWithWork()
        {
            var l = L;
            if (l == null || outpost == null) return 0;
            for (int g = 0; g < TabNames.Length; g++)
                foreach (var p in outpost.Buildable())
                    if ((int)GroupOf(p.id) == g && StateOf(l, p) == State.Ready) return g;
            return 0;
        }

        // --- ISheet / ISheetFramed -------------------------------------------

        public string Title => "Build";
        public Vector3 AnchorWorld => outpost != null ? outpost.CampCentre : Vector3.zero;
        public bool StillValid =>
            outpost != null && outpost.Ledger != null && (outpost.HasCamp || outpost.Building);
        public Color Accent => MidnightLandHud.Ice;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public bool WantsTallSheet => true;
        public VisualElement BuildActions() => null;

        Label subtitle;

        public VisualElement BuildHeader() =>
            CampPages.Header("Build", out subtitle, () => CampPages.OpenLedger(OpenLedger, outpost));

        // --- the page ----------------------------------------------------------

        enum State { Ready, Short, Capped, Locked }

        sealed class CostSlot
        {
            public VisualElement root;
            public Image icon;
            public Label q;
            string res, text;
            bool? shortNow;

            public void Set(string resId, string qText, bool isShort)
            {
                root.style.display = DisplayStyle.Flex;
                if (res != resId) { res = resId; icon.image = ItemIconSet.Get(resId); }
                if (text != qText) { text = qText; q.text = qText; }
                if (shortNow != isShort) { shortNow = isShort; root.EnableInClassList("cp-cost--short", isShort); }
            }

            public void Hide() => root.style.display = DisplayStyle.None;
        }

        sealed class Card
        {
            public Button root;
            public Label name, tag, blurb, copies;
            public VisualElement costLine;
            public CostSlot[] costs;
            public BuildPlan plan;
            public State state;
            public Action custom;
            string nameText, tagText, blurbText, copiesText, tagTone, stateClass;

            public void Texts(string n, string t, string tone, string b, string c)
            {
                if (nameText != n) { nameText = n; name.text = n; }
                if (tagText != t) { tagText = t; tag.text = t; }
                if (tagTone != tone)
                {
                    tagTone = tone;
                    tag.EnableInClassList("cp-tag--ok", tone == "ok");
                    tag.EnableInClassList("cp-tag--bad", tone == "bad");
                }
                if (blurbText != b) { blurbText = b; blurb.text = b; }
                if (copiesText != c)
                {
                    copiesText = c;
                    copies.text = c ?? "";
                    copies.style.display = string.IsNullOrEmpty(c) ? DisplayStyle.None : DisplayStyle.Flex;
                }
            }

            public void Look(State s)
            {
                string cls = s == State.Ready ? "cp-card--ready" : s == State.Capped ? "cp-card--capped"
                    : s == State.Locked ? "cp-card--locked" : null;
                if (stateClass == cls) return;
                if (stateClass != null) root.RemoveFromClassList(stateClass);
                stateClass = cls;
                if (cls != null) root.AddToClassList(cls);
                costLine.style.display = s == State.Locked ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        VisualElement rootEl, tabsRow, mainHolder, extrasHolder, lockedHolder;
        Label lockedEyebrow, emptyNote, gateNote;
        Button[] tabButtons;
        readonly List<Card> mainCards = new List<Card>(), lockedCards = new List<Card>();
        Card wallCard, ladderCard;

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.AddToClassList("cp-root");
            CampPages.Styled(root);
            rootEl = root;

            tabsRow = CampPages.Classed(new VisualElement(), "cp-tabs");
            tabButtons = new Button[TabNames.Length];
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                var b = new Button(() => PickTab(index)) { text = TabNames[i] };
                b.AddToClassList("cp-tab");
                tabsRow.Add(b);
                tabButtons[i] = b;
            }
            root.Add(tabsRow);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("cp-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            root.Add(scroll);
            var list = CampPages.Classed(new VisualElement(), "cp-list");
            scroll.Add(list);

            mainHolder = new VisualElement();
            list.Add(mainHolder);
            emptyNote = CampPages.Classed(new Label("Nothing in this group the fire allows yet."), "cp-note");
            list.Add(emptyNote);

            // Defence's drawn things: the wall and the ladder. Built once and
            // shown only on that tab.
            extrasHolder = new VisualElement();
            list.Add(extrasHolder);
            wallCard = NewCard(extrasHolder);
            wallCard.custom = () =>
            {
                if (Outpost.BeginWallSiting == null || outpost == null) return;
                Outpost.BeginWallSiting(outpost);
                Sheets.Close();
            };
            ladderCard = NewCard(extrasHolder);
            ladderCard.custom = () =>
            {
                if (outpost == null) return;
                LadderSiting.Start(outpost);
                Sheets.Close();
            };
            gateNote = CampPages.Classed(new Label(), "cp-note");
            extrasHolder.Add(gateNote);

            lockedEyebrow = CampPages.Classed(new Label(), "cp-eyebrow");
            list.Add(lockedEyebrow);
            lockedHolder = new VisualElement();
            list.Add(lockedHolder);

            mainCards.Clear();
            lockedCards.Clear();
            MarkTabs();
            Refresh();
            return root;
        }

        Card NewCard(VisualElement into)
        {
            var c = new Card();
            c.root = new Button(() => Tap(c)) { text = "" };
            c.root.AddToClassList("cp-card");

            var iconBox = CampPages.Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cp-card-icon");
            iconBox.Add(new CampPages.HouseGlyph());
            c.root.Add(iconBox);

            var words = CampPages.Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cp-card-words");
            var top = CampPages.Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cp-card-top");
            c.name = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-card-name");
            c.tag = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-tag");
            top.Add(c.name);
            top.Add(c.tag);
            words.Add(top);
            c.blurb = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-blurb");
            words.Add(c.blurb);

            c.costLine = CampPages.Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cp-cost-line");
            c.costs = new CostSlot[3];
            for (int i = 0; i < c.costs.Length; i++)
            {
                var slot = new CostSlot
                {
                    root = CampPages.Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cp-cost"),
                    icon = CampPages.Classed(new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit }, "cp-cost-icon"),
                    q = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-cost-q"),
                };
                slot.root.Add(slot.icon);
                slot.root.Add(slot.q);
                c.costLine.Add(slot.root);
                c.costs[i] = slot;
            }
            c.copies = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-copies");
            c.costLine.Add(c.copies);
            words.Add(c.costLine);
            c.root.Add(words);
            into.Add(c.root);
            return c;
        }

        void PickTab(int index)
        {
            if (index == tab) return;
            tab = index;
            lastTab = index;
            MarkTabs();
            Refresh();
        }

        void MarkTabs()
        {
            if (tabButtons == null) return;
            for (int i = 0; i < tabButtons.Length; i++)
                tabButtons[i].EnableInClassList("cp-tab--on", i == tab);
        }

        void Tap(Card c)
        {
            if (c.custom != null) { c.custom(); return; }
            if (outpost == null || c.plan.id == null) return;
            if (c.state != State.Ready && c.state != State.Short) return;
            CampSiting.Begin(outpost, c.plan, SheetBits.ShipTransform);
            // Siting takes the whole screen's attention; a sheet lying over
            // the ground you are about to tap is the old bottom bar's bug.
            Sheets.Close();
        }

        // --- state -------------------------------------------------------------

        static State StateOf(OutpostLedger l, BuildPlan p)
        {
            if (!l.PlanUnlocked(p.id)) return State.Locked;
            if (!l.CanAddCopy(p.id, out _)) return State.Capped;
            var priced = l.PriceOfNext(p);
            if (l.SpendableOf(TimberOf(p)) < priced.cost) return State.Short;
            if (priced.stoneCost > 0 && l.SpendableOf(Res.Stone) < priced.stoneCost) return State.Short;
            if (priced.brickCost > 0 && l.SpendableOf(Res.Brick) < priced.brickCost) return State.Short;
            return State.Ready;
        }

        static string TimberOf(BuildPlan p) => string.IsNullOrEmpty(p.resource) ? Res.Timber : p.resource;

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        readonly List<BuildPlan> plans = new List<BuildPlan>();
        readonly List<State> states = new List<State>();
        readonly List<int> order = new List<int>();
        readonly List<BuildPlan> locked = new List<BuildPlan>();

        public void Refresh()
        {
            var l = L;
            if (l == null || rootEl == null || outpost == null) return;

            int timber = l.SpendableOf(Res.Timber), stone = l.SpendableOf(Res.Stone);
            int sites = l.SiteCount;
            if (subtitle != null)
            {
                string s = $"{timber} timber · {stone} stone to spend";
                if (sites > 0) s += sites == 1 ? " · 1 going up" : $" · {sites} going up";
                if (subtitle.text != s) subtitle.text = s;
            }

            plans.Clear(); states.Clear(); order.Clear(); locked.Clear();
            foreach (var p in outpost.Buildable())
            {
                if ((int)GroupOf(p.id) != tab) continue;
                var st = StateOf(l, p);
                if (st == State.Locked) { locked.Add(p); continue; }
                plans.Add(p);
                states.Add(st);
                order.Add(order.Count);
            }
            // Can build, then short, then at its cap; stable within each.
            order.Sort((a, b) => states[a] != states[b] ? states[a].CompareTo(states[b]) : a.CompareTo(b));

            while (mainCards.Count < plans.Count) mainCards.Add(NewCard(mainHolder));
            for (int i = 0; i < mainCards.Count; i++)
            {
                var c = mainCards[i];
                if (i >= plans.Count) { c.root.style.display = DisplayStyle.None; continue; }
                c.root.style.display = DisplayStyle.Flex;
                int k = order[i];
                BindMain(l, c, plans[k], states[k]);
            }

            bool defence = tab == (int)Group.Defence;
            extrasHolder.style.display = defence ? DisplayStyle.Flex : DisplayStyle.None;
            if (defence) BindDefence(l, timber);
            emptyNote.style.display = plans.Count == 0 && !defence && locked.Count == 0
                ? DisplayStyle.Flex : DisplayStyle.None;

            while (lockedCards.Count < locked.Count) lockedCards.Add(NewCard(lockedHolder));
            int minLevel = int.MaxValue;
            for (int i = 0; i < lockedCards.Count; i++)
            {
                var c = lockedCards[i];
                if (i >= locked.Count) { c.root.style.display = DisplayStyle.None; continue; }
                c.root.style.display = DisplayStyle.Flex;
                var p = locked[i];
                int need = Techs.PlanLevel(p.id);
                if (need < minLevel) minLevel = need;
                c.plan = p;
                c.state = State.Locked;
                c.Look(State.Locked);
                c.Texts(Cap(p.label), "campfire " + RecipeGraph.Roman(need), null, p.blurb ?? "", null);
            }
            bool anyLocked = locked.Count > 0;
            lockedEyebrow.style.display = anyLocked ? DisplayStyle.Flex : DisplayStyle.None;
            if (anyLocked)
            {
                string eb = "NEEDS CAMPFIRE " + RecipeGraph.Roman(minLevel);
                if (lockedEyebrow.text != eb) lockedEyebrow.text = eb;
            }
        }

        void BindMain(OutpostLedger l, Card c, BuildPlan p, State st)
        {
            c.plan = p;
            c.state = st;
            c.custom = null;
            c.Look(st);

            var priced = l.PriceOfNext(p);
            int slot = 0;
            string timberRes = TimberOf(p);
            int haveT = l.SpendableOf(timberRes);
            c.costs[slot++].Set(timberRes, $"{haveT}/{priced.cost}", haveT < priced.cost);
            if (priced.stoneCost > 0)
            {
                int have = l.SpendableOf(Res.Stone);
                c.costs[slot++].Set(Res.Stone, $"{have}/{priced.stoneCost}", have < priced.stoneCost);
            }
            if (priced.brickCost > 0)
            {
                int have = l.SpendableOf(Res.Brick);
                c.costs[slot++].Set(Res.Brick, $"{have}/{priced.brickCost}", have < priced.brickCost);
            }
            for (; slot < c.costs.Length; slot++) c.costs[slot].Hide();

            // Copies: "×2 of 3 — 4th at campfire III" (Techs.Caps).
            int have2 = l.CopiesHeld(p.id);
            int cap = l.CopyLimit(p.id);
            int top = Techs.MaxCopies(p.id, Techs.CapTableLevels);
            string copies = null;
            if (top > 1)
            {
                copies = $"×{have2} of {cap}";
                if (have2 + 1 >= cap)
                {
                    int unlock = Techs.FireLevelForCopies(p.id, cap + 1);
                    if (unlock > l.CampfireLevel)
                        copies += $" — {OutpostLedger.Nth(cap + 1)} at campfire {RecipeGraph.Roman(unlock)}";
                }
            }
            else if (have2 > 0)
                copies = l.CountBuilt(p.id) > 0 ? "built · one per camp" : "going up · one per camp";

            string tag, tone;
            switch (st)
            {
                case State.Ready: tag = "can build"; tone = "ok"; break;
                case State.Short: tag = "short"; tone = "bad"; break;
                default: tag = top > 1 ? "full" : "built"; tone = null; break;
            }
            c.Texts(Cap(p.label), tag, tone, p.blurb ?? "", copies);
        }

        void BindDefence(OutpostLedger l, int timber)
        {
            var wall = BuildPlans.Palisade;
            bool wallOn = Outpost.BeginWallSiting != null;
            wallCard.state = wallOn ? State.Ready : State.Capped;
            wallCard.Look(wallOn ? State.Short : State.Capped);   // outlined like a plan, not "can build" bright
            wallCard.costs[0].Set(Res.Timber, $"{timber} · 1 per {BuildPlans.MetresPerPalisadeLog:0.#} m", timber <= 0);
            wallCard.costs[1].Hide(); wallCard.costs[2].Hide();
            wallCard.Texts("Palisade", "draw a run", "ok", wall.blurb ?? "", null);

            var ladder = BuildPlans.Ladder;
            ladderCard.Look(State.Short);
            ladderCard.costs[0].Set(Res.Timber, $"{timber} · {BuildPlans.LadderTimberPerMetre:0.#} per m", timber <= 0);
            ladderCard.costs[1].Hide(); ladderCard.costs[2].Hide();
            ladderCard.Texts("Ladder", "foot, then top", "ok", ladder.blurb ?? "", null);

            string g = $"Gate: tap a length of standing wall to put one in it ({BuildPlans.Gate.cost} timber).";
            if (gateNote.text != g) gateNote.text = g;
        }
    }
}
