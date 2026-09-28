using System;
using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **Camp › People, 2026-09-27** (Kevin approved the Ledger follow-ups:
    /// the People › Crew tab of the nav mockup). What the drawer's CAMP ›
    /// People row opens now, instead of the old campfire sheet's hands tab.
    /// One tall page: the housing line in the header ("5 / 8 beds · 3
    /// warm"), filter chips All / Stuck / Unhappy with their counts, then a
    /// row per hand -- avatar initial, name, the job with its place
    /// ("sawyer at the sawmill", "gathering stone"), the mood word and
    /// whether they sleep warm, and the stall reason in red
    /// (`OutpostLedger.StallReason`, shortened the way the alert strip
    /// shortens it). A tap opens that hand's own sheet (`HandSheet`), where
    /// the orders are.
    ///
    /// "Stuck" is the alert strip's rule (`CampAlerts.Collect`): idle, or
    /// stalled / body-blocked once they are off the beach, or a hunter with
    /// no spear. "Unhappy" is anyone with a mood word (hungry or angry).
    ///
    /// Above the chips, the camp's standing orders moved here from the old
    /// campfire sheet (menu rework #8, 2026-09-27): "work first on"
    /// (Even / Food first / Timber first) and the next-hand line with its
    /// bar. The alert strip opens the page pre-filtered (`Filter`).
    ///
    /// Rows are pooled and re-texted on the 0.25 s refresh, never rebuilt
    /// (a rebuilt button loses the tap it is in the middle of). The list is
    /// a clamped ScrollView only for a camp bigger than the tall band.
    public sealed class PeopleSheet : ISheetFramed
    {
        /// **The ☰ hook**, set by `LedgerDrawer`'s constructor.
        public static Action OpenLedger;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { OpenLedger = null; }

        /// Which hands the list shows. The alert strip opens the page on
        /// one (idle → Stuck, angry → Unhappy; menu rework #4).
        public enum Filter { All, Stuck, Unhappy }

        readonly Outpost outpost;
        Filter filter;

        public PeopleSheet(Outpost camp, Filter startOn = Filter.All)
        {
            outpost = camp;
            filter = startOn;
        }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        // --- ISheet / ISheetFramed -------------------------------------------

        public string Title => "People";
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
            CampPages.Header("People", out subtitle, () => CampPages.OpenLedger(OpenLedger, outpost));

        // --- the page ----------------------------------------------------------

        sealed class Row
        {
            public Button root;
            public Label initial, name, mood, job, stall;
            public string who;
            string iText, nText, mText, jText, sText, moodTone;
            bool? stuck, angry, stallWarn;

            public void Set(string i, string n, string m, string mTone, string j, string s, bool warn, bool isStuck, bool isAngry)
            {
                if (iText != i) { iText = i; initial.text = i; }
                if (nText != n) { nText = n; name.text = n; }
                if (mText != m) { mText = m; mood.text = m; }
                if (moodTone != mTone)
                {
                    moodTone = mTone;
                    mood.EnableInClassList("cp-mood--warm", mTone == "warm");
                    mood.EnableInClassList("cp-mood--bad", mTone == "bad");
                }
                if (jText != j) { jText = j; job.text = j; }
                if (sText != s)
                {
                    sText = s;
                    stall.text = s ?? "";
                    stall.style.display = string.IsNullOrEmpty(s) ? DisplayStyle.None : DisplayStyle.Flex;
                }
                if (stallWarn != warn) { stallWarn = warn; stall.EnableInClassList("cp-stall--warn", warn); }
                if (stuck != isStuck) { stuck = isStuck; root.EnableInClassList("cp-hand--stuck", isStuck); }
                if (angry != isAngry) { angry = isAngry; root.EnableInClassList("cp-hand--angry", isAngry); }
            }
        }

        VisualElement rootEl, listHolder;
        Button[] chips;
        Label emptyNote;
        readonly List<Row> rows = new List<Row>();

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.AddToClassList("cp-root");
            CampPages.Styled(root);
            rootEl = root;

            BuildCampOrders(root);

            var chipRow = CampPages.Classed(new VisualElement(), "cp-chips");
            chips = new Button[3];
            for (int i = 0; i < chips.Length; i++)
            {
                int index = i;
                var b = new Button(() => Pick((Filter)index)) { text = "" };
                b.AddToClassList("cp-chip");
                chipRow.Add(b);
                chips[i] = b;
            }
            root.Add(chipRow);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("cp-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            root.Add(scroll);
            listHolder = CampPages.Classed(new VisualElement(), "cp-list");
            scroll.Add(listHolder);
            emptyNote = CampPages.Classed(new Label(), "cp-note");
            scroll.Add(emptyNote);

            rows.Clear();
            priorityKey = -1;
            MarkChips();
            Refresh();
            return root;
        }

        void Pick(Filter f)
        {
            if (f == filter) return;
            filter = f;
            MarkChips();
            Refresh();
        }

        void MarkChips()
        {
            if (chips == null) return;
            for (int i = 0; i < chips.Length; i++)
                chips[i].EnableInClassList("cp-chip--on", i == (int)filter);
        }

        Row NewRow()
        {
            var r = new Row();
            r.root = new Button(() => OpenHand(r.who)) { text = "" };
            r.root.AddToClassList("cp-hand");
            var avatar = CampPages.Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cp-avatar");
            r.initial = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-avatar-l");
            avatar.Add(r.initial);
            r.root.Add(avatar);

            var words = CampPages.Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cp-hand-words");
            var top = CampPages.Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cp-hand-top");
            r.name = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-hand-name");
            r.mood = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-mood");
            top.Add(r.name);
            top.Add(r.mood);
            words.Add(top);
            r.job = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-job");
            words.Add(r.job);
            r.stall = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-stall");
            words.Add(r.stall);
            r.root.Add(words);
            listHolder.Add(r.root);
            return r;
        }

        void OpenHand(string who)
        {
            if (outpost == null || string.IsNullOrEmpty(who)) return;
            Sheets.Open(new HandSheet(outpost, who));
        }

        // --- the facts -----------------------------------------------------------

        /// Why this hand is stuck, shortened, or null. `warn` for the amber
        /// kind (idle: nothing is wrong, nobody has asked).
        static string StuckReason(OutpostLedger l, OutpostHand h, out bool warn)
        {
            warn = false;
            if (OutpostLedger.Reserve(h)) return "held in reserve";
            if (h.order == OutpostOrder.Idle) { warn = true; return "waiting for orders"; }
            if (h.walkingIn) return null;
            if (h.order == OutpostOrder.Gather && h.target == Res.Game && l.HunterBlocker() != null)
                return "no spear · no hunting";
            if (!l.Stalled(h) && string.IsNullOrEmpty(h.bodyBlocked)) return null;
            string why = l.StallReason(h);
            return string.IsNullOrEmpty(why) ? null : CampAlerts.Short(why);
        }

        static bool Unhappy(OutpostHand h) => h.MoodWord.Length > 0;

        /// "sawyer at the sawmill", "gathering stone", "builder · shelter",
        /// "builder · nothing on order".
        static string JobOf(OutpostLedger l, OutpostHand h)
        {
            switch (h.order)
            {
                case OutpostOrder.Work:
                {
                    string label = BuildPlans.Named(h.target).label;
                    string post = h.Doing;
                    if (string.IsNullOrEmpty(label)) return post;
                    return $"{post} at the {label}";
                }
                case OutpostOrder.Build:
                {
                    var f = l.Focus;
                    if (f == null || string.IsNullOrEmpty(f.planId)) return "builder · nothing on order";
                    string label = f.isWall ? "wall" : BuildPlans.Named(f.planId).label;
                    return string.IsNullOrEmpty(label) ? "builder" : "builder · " + label;
                }
                case OutpostOrder.Idle:
                    return "no job";
                default:
                    return h.Doing;
            }
        }

        /// "content · warm", "hungry · cold", "angry · no bed".
        static string MoodOf(OutpostLedger l, OutpostHand h, int index, int beds, out string tone)
        {
            string word = h.MoodWord;
            bool bad = word.Length > 0;
            if (!bad) word = "content";
            string bed;
            if (l.IsHandWarm(index)) bed = "warm";
            else if (index < beds) bed = "cold";
            else bed = "no bed";
            tone = bad ? "bad" : bed == "warm" ? "warm" : null;
            return word + " · " + bed;
        }

        public void Refresh()
        {
            var l = L;
            if (l == null || rootEl == null) return;

            int n = l.hands.Count;
            int beds = l.HousingCapacity;
            int warm = Mathf.Min(n, l.WarmBedCapacity);
            if (subtitle != null)
            {
                string s = $"{n} / {beds} beds · {warm} warm";
                if (subtitle.text != s) subtitle.text = s;
            }

            FillCampOrders(l);

            int stuckN = 0, unhappyN = 0;
            foreach (var h in l.hands)
            {
                if (h == null) continue;
                if (StuckReason(l, h, out _) != null) stuckN++;
                if (Unhappy(h)) unhappyN++;
            }
            SetChip(0, $"All {n}");
            SetChip(1, $"Stuck {stuckN}");
            SetChip(2, $"Unhappy {unhappyN}");

            int shown = 0;
            for (int i = 0; i < l.hands.Count; i++)
            {
                var h = l.hands[i];
                if (h == null) continue;
                string stall = StuckReason(l, h, out bool warn);
                if (filter == Filter.Stuck && stall == null) continue;
                if (filter == Filter.Unhappy && !Unhappy(h)) continue;
                if (shown >= rows.Count) rows.Add(NewRow());
                var r = rows[shown++];
                r.root.style.display = DisplayStyle.Flex;
                r.who = h.name;
                string mood = MoodOf(l, h, i, beds, out string tone);
                r.Set(SheetBits.Initial(h.name), h.name ?? "", mood, tone, JobOf(l, h),
                    stall, warn, stall != null && !warn, h.Angry);
            }
            for (int i = shown; i < rows.Count; i++) rows[i].root.style.display = DisplayStyle.None;

            string empty = n == 0 ? "Nobody lives here yet."
                : filter == Filter.Stuck ? "Nobody is stuck."
                : filter == Filter.Unhappy ? "Nobody is unhappy."
                : "";
            if (emptyNote.text != empty) emptyNote.text = empty;
            emptyNote.style.display = shown == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // --- the camp's standing orders (menu rework #8, 2026-09-27) ----------
        //
        // Moved here from the old campfire sheet's orders page: what the
        // hands work on first (`OutpostLedger.priority`, the same field the
        // ledger's tick reads) and when the next hand arrives. Rations went
        // to the Larder, the watch to the Lookout card.

        static readonly string[] PriorityOptions = { "Even", "Food first", "Timber first" };

        Button[] priorityBtns;
        int priorityKey = -1;
        Label recruitLine;
        VisualElement recruitTrack, recruitFill;

        void BuildCampOrders(VisualElement root)
        {
            root.Add(CampPages.Classed(new Label("WORK FIRST ON"), "cp-eyebrow"));
            var seg = CampPages.Classed(new VisualElement(), "cp-tabs");
            priorityBtns = new Button[PriorityOptions.Length];
            for (int i = 0; i < PriorityOptions.Length; i++)
            {
                int index = i;
                var b = new Button(() => SetPriority(index)) { text = PriorityOptions[i] };
                b.AddToClassList("cp-tab");
                seg.Add(b);
                priorityBtns[i] = b;
            }
            root.Add(seg);

            var rec = new VisualElement();
            rec.style.flexDirection = FlexDirection.Row;
            rec.style.alignItems = Align.Center;
            rec.style.flexShrink = 0;
            rec.style.marginTop = 6;
            rec.style.marginBottom = 4;
            recruitLine = CampPages.Classed(new Label(), "cp-note");
            recruitLine.style.marginTop = 0;
            recruitLine.style.marginBottom = 0;
            recruitLine.style.flexShrink = 1;
            recruitLine.style.flexGrow = 1;
            rec.Add(recruitLine);
            recruitTrack = new VisualElement { pickingMode = PickingMode.Ignore };
            recruitTrack.style.width = 90;
            recruitTrack.style.height = 8;
            recruitTrack.style.flexShrink = 0;
            recruitTrack.style.marginLeft = 8;
            recruitTrack.style.backgroundColor = (Color)new Color32(30, 51, 68, 255);
            SetRadius(recruitTrack, 4);
            recruitFill = new VisualElement { pickingMode = PickingMode.Ignore };
            recruitFill.style.height = Length.Percent(100);
            recruitFill.style.backgroundColor = (Color)new Color32(159, 224, 194, 255);
            SetRadius(recruitFill, 4);
            recruitTrack.Add(recruitFill);
            rec.Add(recruitTrack);
            root.Add(rec);
        }

        static void SetRadius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }

        void SetPriority(int i)
        {
            var l = L;
            if (l == null) return;
            l.priority = (WorkPriority)i;
            Refresh();
        }

        void FillCampOrders(OutpostLedger l)
        {
            if (priorityBtns == null) return;
            int sel = (int)l.priority;
            if (sel != priorityKey)
            {
                priorityKey = sel;
                for (int i = 0; i < priorityBtns.Length; i++)
                    priorityBtns[i].EnableInClassList("cp-tab--on", i == sel);
            }

            // One line about the next hand, and nothing about beds (the
            // header's subtitle already counts them).
            bool room = l.Housed < l.HousingCapacity;
            float days = Mathf.Max(0f, OutpostLedger.DaysPerRecruit - l.recruitProgress);
            string text = room
                ? $"Next hand in {days:0.#} days · needs {OutpostLedger.RecruitFoodCost} food"
                : "No room for another hand · build a hut";
            if (recruitLine.text != text) recruitLine.text = text;
            recruitTrack.style.display = room ? DisplayStyle.Flex : DisplayStyle.None;
            recruitFill.style.width = Length.Percent(Mathf.Clamp01(l.RecruitProgress01) * 100f);
        }

        void SetChip(int i, string text)
        {
            if (chips != null && chips[i].text != text) chips[i].text = text;
        }
    }
}
