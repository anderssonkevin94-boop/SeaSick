using System;
using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The Build screen, 2026-09-30 (phase 1B of the island UI
    /// restructure).** One sheet, no tabs. Every plan the camp knows is
    /// sorted by READINESS, not by kind:
    ///
    /// * **READY TO PLACE** (moss): unlocked, a copy is allowed, the stock
    ///   covers it. Card: name, one-line purpose, cost.
    /// * **SHORT · PLACE IT, HANDS GATHER THE REST** (amber): unlocked, a
    ///   copy is allowed, the stock does not cover it. Same card; the cost
    ///   line names ONLY what is missing. Tapping still places it (the
    ///   builders fetch and cut what is missing, as they always have).
    /// * **CAMPFIRE N UNLOCKS** (grey): plans the fire has not opened, and
    ///   copies past the fire's cap ("×2 of 2 — 3rd at campfire II"), as
    ///   compact dashed chips. A tap shows the requirement inline; nothing
    ///   here is a dead end.
    ///
    /// Three cards to a row, so the whole catalogue fits the phone's tall
    /// band; the ScrollView is only the fallback when the list is longer
    /// than the space. The palisade, ladder and road are cards like the
    /// others (their purpose line says how they are drawn); tapping one
    /// starts its existing siting flow.
    ///
    /// **Badges.** NEW (ice) marks a plan the first time it became available
    /// -- persisted per save slot in PlayerPrefs (`SeenKey`), and cleared
    /// once a Build sheet has shown it. GOAL (amber, plus a 2 px ice border)
    /// marks the pinned build goal and the plan this sheet was opened on
    /// (`Open(camp, highlightPlanId)`).
    ///
    /// **The fix row** (only when something is SHORT): the one resource the
    /// short plans lack most, who is gathering it, and a primary
    /// "Gather <resource>" that sends the first idle hand
    /// (`Outpost.OrderGather`, what `GatherSheet`'s send button presses), or
    /// opens that resource's `GatherSheet` when nobody is idle.
    ///
    /// **It reads and it calls; it never decides.** Prices are
    /// `OutpostLedger.PriceOfNext`, caps `CopyLimit`/`CanAddCopy`, locks
    /// `PlanUnlocked`. A card tap is `CampSiting.Begin(outpost, plan,
    /// ship)` (or the wall/ladder/road tool) followed by `Sheets.Close()`.
    /// Every element is built once and re-texted on the 0.25 s refresh
    /// (pools grow, never rebuild).
    public sealed class BuildSheet : ISheetFramed
    {
        /// **The ☰ hook**, set by `LedgerDrawer`'s constructor. Kept for its
        /// caller; this page has no ☰ of its own now.
        public static Action OpenLedger;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { OpenLedger = null; }

        /// **The goal chain's door:** open the Build screen with this plan
        /// carrying the GOAL badge (and scrolled into view).
        public static void Open(Outpost camp, string highlightPlanId = null)
        {
            if (camp == null || camp.Ledger == null) return;
            Sheets.Open(new BuildSheet(camp, highlightPlanId));
        }

        /// **Start placing `p` exactly as the old cards did** -- the wall,
        /// ladder and road are drawn by their own tools, everything else is
        /// sited by `CampSiting.Begin` -- then fold the sheet away so the
        /// ground is clear. Shared with `PlanSheet`'s Build button.
        internal static void StartPlacement(Outpost o, BuildPlan p)
        {
            if (o == null || string.IsNullOrEmpty(p.id)) return;
            if (p.id == BuildPlans.Palisade.id)
            {
                if (Outpost.BeginWallSiting == null) return;
                Outpost.BeginWallSiting(o);
            }
            else if (p.id == BuildPlans.Ladder.id) LadderSiting.Start(o);
            else if (p.id == BuildPlans.Road.id) RoadSiting.Start(o);
            else CampSiting.Begin(o, p, SheetBits.ShipTransform);
            // Siting takes the whole screen's attention; a sheet lying over
            // the ground you are about to tap is the old bottom bar's bug.
            Sheets.Close();
        }

        /// True for the plans that are drawn, not sited by one tap.
        internal static bool IsDrawn(string planId) =>
            planId == BuildPlans.Palisade.id || planId == BuildPlans.Ladder.id || planId == BuildPlans.Road.id;

        /// **One-line purpose, three words or so** -- for a plan not listed
        /// here, the first clause of its blurb.
        internal static string PurposeOf(BuildPlan p)
        {
            if (p.id == BuildPlans.Hut.id) return p.houses > 0 ? $"Beds for {p.houses}" : "Beds";
            // Runners and the store (stores unlimited since 2026-10-03; the
            // Storehouse's runner progression is its levels since 2026-10-04).
            if (p.id == BuildPlans.Storage.id) return "Runners and goods";
            if (p.id == BuildPlans.Farm.id) return "Grows food";
            if (p.id == BuildPlans.FishingHut.id) return "Fish for food";
            if (p.id == BuildPlans.Kitchen.id) return "Cooks dishes";
            if (p.id == BuildPlans.Sawmill.id) return "Logs into boards";
            if (p.id == BuildPlans.Quarry.id) return "Stone into brick";
            if (p.id == BuildPlans.Mine.id) return "Digs stone";
            if (p.id == BuildPlans.Blacksmith.id) return "Iron tools, weapons and armor";
            if (p.id == BuildPlans.Fletcher.id) return "Bows, leather armor and shields";
            if (p.id == BuildPlans.Mill.id) return "Wheat into flour";
            if (p.id == BuildPlans.Watchtower.id) return "Lookout, defence";
            if (p.id == BuildPlans.Pier.id) return "Ship berth";
            if (p.id == BuildPlans.DryDock.id) return "Refit your ship";
            if (p.id == BuildPlans.Palisade.id) return "Draw a run";
            if (p.id == BuildPlans.Road.id) return "Tap start, tap end";
            if (p.id == BuildPlans.Ladder.id) return "Foot, then top";
            string b = p.blurb ?? "";
            int cut = b.IndexOfAny(new[] { ',', ';', '.' });
            return cut > 0 ? b.Substring(0, cut) : b;
        }

        readonly Outpost outpost;
        readonly string highlightId;

        /// `focusPlanId` is the plan to carry the GOAL badge (the "no spear"
        /// alert asks for the forge); the sheet no longer has tabs to open on.
        public BuildSheet(Outpost camp, string focusPlanId = null)
        {
            outpost = camp;
            highlightId = string.IsNullOrEmpty(focusPlanId) ? null : focusPlanId;
        }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

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

        /// Hammer · Build · what you have · ✕.
        public VisualElement BuildHeader() =>
            CampPages.IconHeader("Build", new HammerGlyph(), out subtitle);

        // --- entries -------------------------------------------------------------

        struct Entry
        {
            public BuildPlan plan;
            public string id, label, purpose, cost, reason;
            public bool amber, goal, isNew;
            public int level;
        }

        // --- cards ---------------------------------------------------------------

        sealed class Card
        {
            public Button root;
            public VisualElement face;
            public Label name, purpose, cost, badge;
            public Entry entry;
            string nameT, purposeT, costT, badgeT, faceCls;
            bool amberNow, goalNow;

            public void Set(in Entry e, bool shortSection)
            {
                entry = e;
                if (nameT != e.label) { nameT = e.label; name.text = e.label; }
                if (purposeT != e.purpose) { purposeT = e.purpose; purpose.text = e.purpose; }
                if (costT != e.cost) { costT = e.cost; cost.text = e.cost; }
                if (amberNow != e.amber) { amberNow = e.amber; cost.EnableInClassList("bs-cost--short", e.amber); }
                if (goalNow != e.goal) { goalNow = e.goal; face.EnableInClassList("bs-card--goal", e.goal); }
                string cls = shortSection ? "bs-card--short" : "bs-card--ready";
                if (faceCls != cls)
                {
                    if (faceCls != null) face.RemoveFromClassList(faceCls);
                    faceCls = cls;
                    face.AddToClassList(cls);
                }
                string b = e.goal ? "GOAL" : e.isNew ? "NEW" : null;
                if (badgeT != b)
                {
                    badgeT = b;
                    badge.style.display = b == null ? DisplayStyle.None : DisplayStyle.Flex;
                    if (b != null) badge.text = b;
                    badge.EnableInClassList("bs-badge--goal", b == "GOAL");
                    badge.EnableInClassList("bs-badge--new", b == "NEW");
                }
            }
        }

        sealed class Chip
        {
            public Button root;
            public VisualElement face;
            public Label name;
            public Entry entry;
            string nameT;
            bool onNow;

            public void Set(in Entry e, bool on)
            {
                entry = e;
                if (nameT != e.label) { nameT = e.label; name.text = e.label; }
                if (onNow != on) { onNow = on; face.EnableInClassList("bs-chip--on", on); }
            }
        }

        VisualElement rootEl, readyGrid, shortGrid, lockedGrid, fixRow;
        Label readyLabel, shortLabel, lockedLabel, lockNote, emptyNote, fixText;
        Button fixBtn;
        ScrollView scroll;
        readonly List<Card> readyCards = new List<Card>(), shortCards = new List<Card>();
        readonly List<Chip> chips = new List<Chip>();

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.AddToClassList("bs-root");
            var sheet = Resources.Load<StyleSheet>("UI/BuildStrip");
            if (sheet != null) root.styleSheets.Add(sheet);
            else Debug.LogWarning("[BuildSheet] Resources/UI/BuildStrip.uss is missing -- the page is unstyled.");
            rootEl = root;

            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("bs-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            root.Add(scroll);
            var list = new VisualElement();
            list.AddToClassList("bs-list");
            scroll.Add(list);

            readyLabel = Section(list, "READY TO PLACE", "bs-label--ready", out readyGrid);
            shortLabel = Section(list, "SHORT · PLACE IT, HANDS GATHER THE REST", "bs-label--short", out shortGrid);
            lockedLabel = Section(list, "", "bs-label--locked", out lockedGrid);
            lockNote = new Label();
            lockNote.AddToClassList("bs-note");
            lockNote.AddToClassList("bs-note--lock");
            lockNote.style.display = DisplayStyle.None;
            list.Add(lockNote);
            emptyNote = new Label("Nothing to build yet.");
            emptyNote.AddToClassList("bs-note");
            emptyNote.style.display = DisplayStyle.None;
            list.Add(emptyNote);

            // The fix row: built once, pinned under the scroll.
            fixRow = new VisualElement();
            fixRow.AddToClassList("bs-fix");
            fixText = new Label();
            fixText.AddToClassList("bs-fix-text");
            fixRow.Add(fixText);
            fixBtn = new Button(Fix) { text = "" };
            fixBtn.AddToClassList("bs-fix-btn");
            fixRow.Add(fixBtn);
            fixRow.style.display = DisplayStyle.None;
            root.Add(fixRow);

            LoadSeen();
            Refresh();
            return root;
        }

        static Label Section(VisualElement into, string text, string cls, out VisualElement grid)
        {
            var label = new Label(text);
            label.AddToClassList("bs-label");
            label.AddToClassList(cls);
            label.style.display = DisplayStyle.None;
            into.Add(label);
            grid = new VisualElement();
            grid.AddToClassList("bs-grid");
            into.Add(grid);
            return label;
        }

        Card NewCard(VisualElement into)
        {
            var c = new Card();
            c.root = new Button(() => Tap(c.entry)) { text = "" };
            c.root.AddToClassList("bs-slot");
            c.face = Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "bs-card");
            c.name = Classed(new Label { pickingMode = PickingMode.Ignore }, "bs-name");
            c.purpose = Classed(new Label { pickingMode = PickingMode.Ignore }, "bs-purpose");
            c.cost = Classed(new Label { pickingMode = PickingMode.Ignore }, "bs-cost");
            c.badge = Classed(new Label { pickingMode = PickingMode.Ignore }, "bs-badge");
            c.badge.style.display = DisplayStyle.None;
            c.face.Add(c.name);
            c.face.Add(c.purpose);
            c.face.Add(c.cost);
            c.face.Add(c.badge);
            c.root.Add(c.face);
            into.Add(c.root);
            return c;
        }

        Chip NewChip(VisualElement into)
        {
            var c = new Chip();
            c.root = new Button(null) { text = "" };
            c.root.clicked += () => PickChip(c.entry);
            c.root.AddToClassList("bs-slot");
            c.face = Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "bs-chip");
            c.face.Add(new DashedFrame());
            c.face.Add(new LockGlyph());
            c.name = Classed(new Label { pickingMode = PickingMode.Ignore }, "bs-chip-name");
            c.face.Add(c.name);
            c.root.Add(c.face);
            into.Add(c.root);
            return c;
        }

        static T Classed<T>(T e, string cls) where T : VisualElement
        {
            e.AddToClassList(cls);
            return e;
        }

        // --- taps ------------------------------------------------------------------

        void Tap(Entry e)
        {
            if (outpost == null || string.IsNullOrEmpty(e.id)) return;
            StartPlacement(outpost, e.plan);
        }

        string pickedChip;

        void PickChip(Entry e)
        {
            // A second tap on the same chip folds its line away again.
            pickedChip = pickedChip == e.id ? null : e.id;
            pickedReason = pickedChip == null ? null : e.reason;
            Refresh();
        }

        string pickedReason;

        // --- the seen list (NEW badges) --------------------------------------------

        /// Plan ids this save has already been shown as available, joined by
        /// commas in PlayerPrefs under `SeenKey` (per save slot).
        readonly HashSet<string> seen = new HashSet<string>();
        /// The ids new to THIS sheet: badged while it is open, stored as seen
        /// straight away so the next opening starts clean.
        readonly HashSet<string> freshHere = new HashSet<string>();
        bool seenLoaded, seenDirty, seedSeen;

        static string SeenKey =>
            "seasick.build.seen." + (string.IsNullOrEmpty(SeaSick.Save.SaveSlots.ActiveSlotId) ? "none" : SeaSick.Save.SaveSlots.ActiveSlotId);

        void LoadSeen()
        {
            if (seenLoaded) return;
            seenLoaded = true;
            seen.Clear();
            if (!PlayerPrefs.HasKey(SeenKey))
            {
                // First time this save opens the screen: everything on offer
                // now is "already known", so a fresh camp shows no NEW badges.
                seedSeen = true;
                return;
            }
            foreach (var id in PlayerPrefs.GetString(SeenKey, "").Split(','))
                if (id.Length > 0) seen.Add(id);
        }

        void SaveSeen()
        {
            if (!seenDirty && !seedSeen) return;
            seenDirty = false;
            PlayerPrefs.SetString(SeenKey, string.Join(",", seen));
            PlayerPrefs.Save();
        }

        /// Note an available plan: the first sight of it is NEW.
        bool Note(string id)
        {
            if (seen.Add(id))
            {
                seenDirty = true;
                if (!seedSeen) freshHere.Add(id);
            }
            return freshHere.Contains(id);
        }

        // --- state -----------------------------------------------------------------

        static string TimberOf(BuildPlan p) => string.IsNullOrEmpty(p.resource) ? Res.Timber : p.resource;

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        readonly List<Entry> ready = new List<Entry>(), shorts = new List<Entry>(), locked = new List<Entry>();
        readonly Dictionary<string, int> missing = new Dictionary<string, int>();
        readonly List<string> parts = new List<string>(3);
        readonly List<string> haveRes = new List<string>(4);

        void Lack(string res, int need, int have)
        {
            if (need <= have) return;
            parts.Add(ResDefs.Counted(res, need - have));
            missing.TryGetValue(res, out int m);
            missing[res] = m + (need - have);
        }

        void Collect(OutpostLedger l)
        {
            ready.Clear(); shorts.Clear(); locked.Clear(); missing.Clear();
            haveRes.Clear();
            haveRes.Add(Res.Timber);
            haveRes.Add(Res.Stone);

            foreach (var p in outpost.Buildable())
            {
                if (!l.PlanUnlocked(p.id))
                {
                    int need = Techs.PlanLevel(p.id);
                    locked.Add(new Entry
                    {
                        plan = p, id = p.id, label = Cap(p.label), level = need,
                        reason = $"{Cap(p.label)} needs campfire {RecipeGraph.Roman(need)} (this camp is {RecipeGraph.Roman(l.CampfireLevel)}).",
                    });
                    continue;
                }
                if (!l.CanAddCopy(p.id, out _))
                {
                    // At its cap: a chip only when a higher fire opens another copy.
                    int have = l.CopiesHeld(p.id), cap = l.CopyLimit(p.id);
                    int top = Techs.MaxCopies(p.id, Techs.CapTableLevels);
                    int unlock = top > have ? Techs.FireLevelForCopies(p.id, have + 1) : 0;
                    if (unlock > l.CampfireLevel)
                        locked.Add(new Entry
                        {
                            plan = p, id = p.id, label = Cap(p.label), level = unlock,
                            reason = $"{Cap(p.label)} ×{have} of {cap} — {OutpostLedger.Nth(have + 1)} at campfire {RecipeGraph.Roman(unlock)}",
                        });
                    continue;
                }

                string homeNeed = outpost.HomeBerthNeed(p.id);
                if (homeNeed != null)
                {
                    // Locked until a home berth stands (the dry dock): no
                    // campfire level, so it sorts after the fire's unlocks.
                    locked.Add(new Entry
                    {
                        plan = p, id = p.id, label = Cap(p.label), level = 0,
                        reason = Cap(homeNeed) + ".",
                    });
                    continue;
                }

                var priced = l.PriceOfNext(p);
                string timberRes = TimberOf(p);
                int haveT = l.SpendableOf(timberRes), haveS = l.SpendableOf(Res.Stone), haveB = l.SpendableOf(Res.Brick);
                if (timberRes != Res.Timber && !haveRes.Contains(timberRes)) haveRes.Add(timberRes);
                if (priced.brickCost > 0 && !haveRes.Contains(Res.Brick)) haveRes.Add(Res.Brick);

                parts.Clear();
                Lack(timberRes, priced.cost, haveT);
                Lack(Res.Stone, priced.stoneCost, haveS);
                Lack(Res.Brick, priced.brickCost, haveB);
                bool isShort = parts.Count > 0;

                string cost;
                if (isShort) cost = "need " + string.Join(", ", parts);
                else
                {
                    parts.Clear();
                    if (priced.cost > 0) parts.Add($"{priced.cost} {ResDefs.Label(timberRes)}");
                    if (priced.stoneCost > 0) parts.Add($"{priced.stoneCost} {ResDefs.Label(Res.Stone)}");
                    if (priced.brickCost > 0) parts.Add($"{priced.brickCost} {ResDefs.Label(Res.Brick)}");
                    cost = parts.Count == 0 ? "free" : string.Join(" · ", parts);
                }
                Add(l, new Entry { plan = p, id = p.id, label = Cap(p.label), purpose = PurposeOf(p), cost = cost, amber = isShort },
                    isShort);
            }

            // The drawn things: palisade, ladder, road. Priced by length, so
            // "short" only means there is none of the material at all.
            int timber = l.SpendableOf(Res.Timber), stone = l.SpendableOf(Res.Stone);
            if (Outpost.BeginWallSiting != null)
                AddDrawn(l, BuildPlans.Palisade, "Palisade", Res.Timber, timber,
                    $"1 timber / {BuildPlans.MetresPerPalisadeLog:0.#} m");
            AddDrawn(l, BuildPlans.Ladder, "Ladder", Res.Timber, timber,
                $"{BuildPlans.LadderTimberPerMetre:0.#} timber / m");
            AddDrawn(l, BuildPlans.Road, "Road", Res.Stone, stone,
                $"1 stone / {BuildPlans.RoadMetresPerStone:0.#} m");

            // The goal card leads its section.
            MoveGoalFirst(ready);
            MoveGoalFirst(shorts);
        }

        void AddDrawn(OutpostLedger l, BuildPlan p, string label, string res, int have, string costWords)
        {
            bool isShort = have <= 0;
            if (isShort) { missing.TryGetValue(res, out int m); missing[res] = m + 1; }
            Add(l, new Entry
            {
                plan = p, id = p.id, label = label, purpose = PurposeOf(p), amber = isShort,
                cost = isShort ? "need " + ResDefs.Label(res) : costWords,
            }, isShort);
        }

        void Add(OutpostLedger l, Entry e, bool isShort)
        {
            e.goal = (highlightId != null && highlightId == e.id) || GoalPin.IsBuildPinned(outpost, e.id);
            e.isNew = Note(e.id);
            (isShort ? shorts : ready).Add(e);
        }

        static void MoveGoalFirst(List<Entry> list)
        {
            for (int i = 1; i < list.Count; i++)
                if (list[i].goal)
                {
                    var e = list[i];
                    list.RemoveAt(i);
                    list.Insert(0, e);
                    return;
                }
        }

        // --- refresh ---------------------------------------------------------------

        bool scrolledToGoal;

        public void Refresh()
        {
            var l = L;
            if (l == null || rootEl == null || outpost == null) return;
            LoadSeen();
            Collect(l);
            SaveSeen();
            seedSeen = false;

            if (subtitle != null)
            {
                parts.Clear();
                foreach (var r in haveRes) parts.Add($"{Cap(ResDefs.Label(r))} {l.SpendableOf(r)}");
                string s = string.Join(" · ", parts);
                if (subtitle.text != s) subtitle.text = s;
            }

            BindCards(readyCards, readyGrid, readyLabel, ready, false);
            BindCards(shortCards, shortGrid, shortLabel, shorts, true);
            BindLocked(l);
            bool empty = ready.Count == 0 && shorts.Count == 0 && locked.Count == 0;
            emptyNote.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            BindFix(l);

            if (!scrolledToGoal && highlightId != null)
            {
                foreach (var c in readyCards)
                    if (c.root.style.display != DisplayStyle.None && c.entry.id == highlightId) { ScrollTo(c.root); break; }
                if (!scrolledToGoal)
                    foreach (var c in shortCards)
                        if (c.root.style.display != DisplayStyle.None && c.entry.id == highlightId) { ScrollTo(c.root); break; }
            }
        }

        void ScrollTo(VisualElement el)
        {
            scrolledToGoal = true;
            // Layout has not run on the very first refresh; scroll a beat later.
            el.schedule.Execute(() => { if (scroll != null && el.panel != null) scroll.ScrollTo(el); }).StartingIn(60);
        }

        void BindCards(List<Card> pool, VisualElement grid, Label label, List<Entry> entries, bool shortSection)
        {
            while (pool.Count < entries.Count) pool.Add(NewCard(grid));
            for (int i = 0; i < pool.Count; i++)
            {
                var c = pool[i];
                if (i >= entries.Count)
                {
                    if (c.root.style.display != DisplayStyle.None) c.root.style.display = DisplayStyle.None;
                    continue;
                }
                if (c.root.style.display != DisplayStyle.Flex) c.root.style.display = DisplayStyle.Flex;
                c.Set(entries[i], shortSection);
            }
            var want = entries.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (label.style.display != want) label.style.display = want;
        }

        void BindLocked(OutpostLedger l)
        {
            while (chips.Count < locked.Count) chips.Add(NewChip(lockedGrid));
            int minLevel = int.MaxValue;
            bool pickedStill = false;
            for (int i = 0; i < chips.Count; i++)
            {
                var c = chips[i];
                if (i >= locked.Count)
                {
                    if (c.root.style.display != DisplayStyle.None) c.root.style.display = DisplayStyle.None;
                    continue;
                }
                if (c.root.style.display != DisplayStyle.Flex) c.root.style.display = DisplayStyle.Flex;
                var e = locked[i];
                if (e.level > 0 && e.level < minLevel) minLevel = e.level;
                bool on = pickedChip == e.id;
                if (on) { pickedStill = true; pickedReason = e.reason; }
                c.Set(e, on);
            }
            if (!pickedStill) { pickedChip = null; pickedReason = null; }

            bool any = locked.Count > 0;
            var want = any ? DisplayStyle.Flex : DisplayStyle.None;
            if (lockedLabel.style.display != want) lockedLabel.style.display = want;
            if (any)
            {
                string t = minLevel == int.MaxValue ? "LOCKED" : "CAMPFIRE " + RecipeGraph.Roman(minLevel) + " UNLOCKS";
                if (lockedLabel.text != t) lockedLabel.text = t;
            }
            var noteWant = pickedReason != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (lockNote.style.display != noteWant) lockNote.style.display = noteWant;
            if (pickedReason != null && lockNote.text != pickedReason) lockNote.text = pickedReason;
        }

        // --- the fix row -----------------------------------------------------------

        string fixRes;
        ShortFix.Fix fix;

        void BindFix(OutpostLedger l)
        {
            // The resource the short plans lack most; a gatherable one wins a tie
            // over boards/brick, which no hand can simply go and gather
            // (`ShortFix.Most`, the one rule every sheet's fix button uses).
            var most = new ShortFix.Most();
            foreach (var kv in missing) most.Add(kv.Key, kv.Value);
            string best = most.Res;
            fixRes = best;
            fix = ShortFix.For(outpost, best);

            var want = best != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (fixRow.style.display != want) fixRow.style.display = want;
            if (best == null) return;

            string label = ResDefs.Label(best);
            string line;
            if (Res.IsGatherable(best))
            {
                int n = l.HandsOn(OutpostOrder.Gather, best);
                string who = n == 0 ? "Nobody is gathering." : n == 1 ? "1 hand is gathering." : $"{n} hands are gathering.";
                line = $"{Cap(label)} is what's short. {who}";
            }
            else line = $"{Cap(label)} is what's short. It is made at a station, not gathered.";
            if (fixText.text != line) fixText.text = line;

            // "Gather X", or (2026-09-30, island UI rule 2) "Make X" / "Build
            // <station>" for a made good -- the recipe system's answer.
            fixBtn.style.display = fix.Valid ? DisplayStyle.Flex : DisplayStyle.None;
            if (fix.Valid && fixBtn.text != fix.label) fixBtn.text = fix.label;
        }

        /// **The fix button**: `ShortFix.Run` -- "Gather <resource>" sends the
        /// first idle hand after it (the call `GatherSheet`'s send button
        /// makes); when nobody is idle, or the order is refused, that
        /// resource's own page opens, which says who is on it and why. A made
        /// good opens its station, or the Build sheet on the station.
        void Fix()
        {
            var l = L;
            if (outpost == null || l == null || string.IsNullOrEmpty(fixRes) || !fix.Valid) return;
            if (fix.Run(outpost)) Refresh();
        }

        // --- drawn bits --------------------------------------------------------------

        /// A hammer, painted (no font glyph): the Build screen's mark.
        sealed class HammerGlyph : VisualElement
        {
            public HammerGlyph()
            {
                pickingMode = PickingMode.Ignore;
                style.width = 24; style.height = 24;
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;
                float s = Mathf.Min(contentRect.width, contentRect.height) / 24f;
                if (s <= 0f) return;
                Vector2 V(float x, float y) => new Vector2(x * s, y * s);
                p.strokeColor = MidnightLandHud.Ice;
                p.lineWidth = 2.2f * s;
                p.lineCap = LineCap.Round;
                p.lineJoin = LineJoin.Round;
                // handle
                p.BeginPath(); p.MoveTo(V(5, 20)); p.LineTo(V(14, 11)); p.Stroke();
                // head
                p.lineWidth = 3.4f * s;
                p.BeginPath(); p.MoveTo(V(11, 5)); p.LineTo(V(19, 13)); p.Stroke();
                p.lineWidth = 2.2f * s;
                p.BeginPath(); p.MoveTo(V(9, 9)); p.LineTo(V(15, 3)); p.Stroke();
            }
        }

        /// A padlock, painted: the chip's "not yet".
        sealed class LockGlyph : VisualElement
        {
            public LockGlyph()
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("bs-lock");
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;
                float s = Mathf.Min(contentRect.width, contentRect.height) / 16f;
                if (s <= 0f) return;
                Vector2 V(float x, float y) => new Vector2(x * s, y * s);
                p.strokeColor = new Color32(127, 151, 166, 255);
                p.fillColor = new Color32(127, 151, 166, 255);
                p.lineWidth = 1.6f * s;
                p.lineCap = LineCap.Round;
                p.BeginPath(); p.MoveTo(V(4.5f, 7)); p.LineTo(V(4.5f, 5)); p.Arc(V(8, 5), 3.5f * s, 180f, 360f); p.LineTo(V(11.5f, 7)); p.Stroke();
                p.BeginPath(); p.MoveTo(V(3, 7)); p.LineTo(V(13, 7)); p.LineTo(V(13, 14)); p.LineTo(V(3, 14)); p.ClosePath(); p.Fill();
            }
        }

        /// **A dashed rounded-rect-ish outline** (USS has no dashed border):
        /// short strokes along the four straight edges, inset from the corners.
        sealed class DashedFrame : VisualElement
        {
            public DashedFrame()
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("bs-dash");
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext ctx)
            {
                var r = contentRect;
                if (r.width < 20f || r.height < 20f) return;
                var p = ctx.painter2D;
                p.strokeColor = new Color32(74, 104, 124, 255);
                p.lineWidth = 1.5f;
                p.lineCap = LineCap.Butt;
                const float dash = 5f, gap = 4f, inset = 8f, edge = 0.75f;
                void Run(Vector2 a, Vector2 b)
                {
                    float len = Vector2.Distance(a, b);
                    var dir = (b - a) / len;
                    for (float t = 0f; t < len; t += dash + gap)
                    {
                        float e = Mathf.Min(t + dash, len);
                        p.BeginPath(); p.MoveTo(a + dir * t); p.LineTo(a + dir * e); p.Stroke();
                    }
                }
                float x0 = r.xMin + edge, x1 = r.xMax - edge, y0 = r.yMin + edge, y1 = r.yMax - edge;
                Run(new Vector2(x0 + inset, y0), new Vector2(x1 - inset, y0));
                Run(new Vector2(x0 + inset, y1), new Vector2(x1 - inset, y1));
                Run(new Vector2(x0, y0 + inset), new Vector2(x0, y1 - inset));
                Run(new Vector2(x1, y0 + inset), new Vector2(x1, y1 - inset));
            }
        }
    }
}
