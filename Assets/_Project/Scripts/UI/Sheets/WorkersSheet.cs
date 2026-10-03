using System;
using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **Workers, the one home for hands (2026-09-30, island UI phase 4).**
    /// Rule 3, "one home per topic": the top bar's working/total chip and
    /// the drawer's People page used to be two sheets over the same hands
    /// (`WorkersSheet` for who is not working, `PeopleSheet` for the roster
    /// and its filters). This is both, and `PeopleSheet` is gone.
    ///
    /// From the top: the headline "3 of 5 working" (`CampReadouts.Working` /
    /// `Total`); the camp's standing orders ("work first on", the next-hand
    /// line -- moved from the campfire sheet in the 2026-09-27 menu rework,
    /// they steer hands so they live with hands); the chips **All / Stuck /
    /// Unhappy** with their counts; then ONE row per hand -- initial, name,
    /// the mood word only when he is unhappy, the job with its place ("sawyer
    /// at the sawmill", "gathering stone"), and under it what is wrong, in
    /// ember (`OutpostLedger.StallReason`, shortened as the alert strip
    /// shortens it; amber when nothing is broken and nobody asked: idle,
    /// reserve, busy). Hands that need looking at sort first (unassigned,
    /// held up, down, busy), the ones at work last. A tap pans the camera to
    /// the hand and opens his sheet (`CampReadouts.GoToHand`), where his
    /// orders are.
    ///
    /// **The one main action, in the thumb row: "Assign N idle".** Every
    /// station with work waiting and nobody at it (`StationUnmanned`) gets the
    /// best free hand (`FreeHandFor` -> `Outpost.Assign`), the same rule the
    /// station sheet's "Assign free hand" uses, run for each. It only ever
    /// runs when the player presses it (Kevin: hands are never auto-assigned
    /// to stations); hidden when there is nobody free or nothing unmanned.
    ///
    /// "Stuck" is the alert strip's rule (`CampAlerts.Collect`): idle, or
    /// stalled / body-blocked once off the beach, or a hunter with no spear,
    /// or down. "Unhappy" is anyone with a mood word (hungry or angry). The
    /// alert strip and the mood sheet open the page pre-filtered.
    ///
    /// Rows are pooled and re-texted on the 0.25 s refresh, never rebuilt (a
    /// rebuilt button loses the tap it is in the middle of). It is a list, so
    /// it keeps the tall band and pages with a clamped scroll.
    public sealed class WorkersSheet : ISheetFramed
    {
        /// **The ☰ hook**, set by `LedgerDrawer`'s constructor.
        public static Action OpenLedger;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { OpenLedger = null; }

        /// Which hands the list shows. The alert strip and the mood sheet
        /// open the page on one (idle -> Stuck, angry -> Unhappy).
        public enum Filter { All, Stuck, Unhappy }

        readonly Outpost outpost;
        Filter filter;

        public WorkersSheet(Outpost camp, Filter startOn = Filter.All)
        {
            outpost = camp;
            filter = startOn;
        }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        // --- frame --------------------------------------------------------------

        public string Title => "Workers";
        public Vector3 AnchorWorld => outpost != null ? outpost.CampCentre : Vector3.zero;
        public bool StillValid => outpost != null && outpost.Ledger != null;
        public Color Accent => MidnightLandHud.Ice;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public bool WantsTallSheet => true;

        Label subtitle;

        public VisualElement BuildHeader() =>
            CampPages.Header("Workers", out subtitle, () => CampPages.OpenLedger(OpenLedger, outpost));

        // --- the thumb row: Assign N idle ---------------------------------------

        Button assignBtn;
        VisualElement assignRow;

        public VisualElement BuildActions()
        {
            assignBtn = SheetKit.Btn("Assign free hands", AssignIdle, primary: true);
            assignBtn.style.height = 48f;
            assignRow = SheetKit.Actions(assignBtn);
            assignRow.style.display = DisplayStyle.None;
            // Shown by Refresh once there is something to assign.
            RefreshAssign(L);
            return assignRow;
        }

        /// A hand `FreeHandFor` would offer: up, not the player's reserve,
        /// with no job (Idle, or Build with no plot).
        static bool IsFree(OutpostLedger l, OutpostHand h) =>
            h != null && !h.Busy && !OutpostLedger.Reserve(h)
            && (h.order == OutpostOrder.Idle
                || (h.order == OutpostOrder.Build && l.BuildSiteFor(h) == null));

        /// How many stations can be staffed right now: the smaller of the
        /// unmanned stations and the free hands.
        static int Assignable(OutpostLedger l)
        {
            if (l == null || l.hands == null || l.stations == null) return 0;
            int unmanned = 0;
            foreach (var s in l.stations) if (l.StationUnmanned(s)) unmanned++;
            if (unmanned == 0) return 0;
            int free = 0;
            foreach (var h in l.hands) if (IsFree(l, h)) free++;
            return Mathf.Min(unmanned, free);
        }

        int assignShown = -1;

        void RefreshAssign(OutpostLedger l)
        {
            if (assignBtn == null || assignRow == null) return;
            int n = Assignable(l);
            if (n != assignShown)
            {
                assignShown = n;
                // "free", not "idle" (2026-10-03): the hands it moves are
                // those with no job AND builders with no plot (`IsFree`).
                assignBtn.text = n == 1 ? "Assign 1 free hand" : n > 0 ? $"Assign {n} free hands" : "Assign free hands";
            }
            var d = n > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            assignRow.style.display = d;
            // The host's strip carries the border and padding; hide it with
            // the button so nothing is left over the thumb row.
            if (assignRow.parent != null) assignRow.parent.style.display = d;
        }

        /// The player pressed it: each unmanned station takes the best free
        /// hand, one by one (`FreeHandFor` is asked again after every
        /// assignment, so nobody is dealt twice).
        void AssignIdle()
        {
            var l = L;
            if (l == null || outpost == null) return;
            foreach (var s in l.UnmannedStations())
            {
                var free = l.FreeHandFor(s.planId);
                if (free == null) break;
                outpost.Assign(free, s);
            }
            Refresh();
        }

        // --- the page ----------------------------------------------------------

        sealed class Row
        {
            public Button root;
            public Label initial, name, mood, job, stall;
            public string who;
            string iText, nText, mText, jText, sText;
            bool? stuck, angry, stallWarn;

            public void Set(string i, string n, string m, string j, string s, bool warn, bool isStuck, bool isAngry)
            {
                if (iText != i) { iText = i; initial.text = i; }
                if (nText != n) { nText = n; name.text = n; }
                if (mText != m)
                {
                    mText = m;
                    mood.text = m ?? "";
                    mood.style.display = string.IsNullOrEmpty(m) ? DisplayStyle.None : DisplayStyle.Flex;
                }
                if (jText != j) { jText = j; job.text = j ?? ""; }
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

        /// One hand's facts for this refresh.
        struct Info
        {
            public OutpostHand h;
            public CampReadouts.HandKind kind;
            public string status;
            public bool warn, stuck;
        }

        static readonly CampReadouts.HandKind[] Order =
        {
            CampReadouts.HandKind.Unassigned,
            CampReadouts.HandKind.HeldUp,
            CampReadouts.HandKind.Down,
            CampReadouts.HandKind.Busy,
            CampReadouts.HandKind.Working,
        };

        VisualElement rootEl, listHolder;
        Label big, small;
        Button[] chips;
        Label emptyNote;
        readonly List<Row> rows = new List<Row>();
        readonly List<Info> infos = new List<Info>();

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.AddToClassList("cp-root");
            CampPages.Styled(root);
            rootEl = root;

            root.Add(ReadoutUi.Headline(out big, out small));
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
            r.root.style.minHeight = 58f;
            var avatar = CampPages.Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cp-avatar");
            r.initial = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-avatar-l");
            avatar.Add(r.initial);
            r.root.Add(avatar);

            var words = CampPages.Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cp-hand-words");
            var top = CampPages.Classed(new VisualElement { pickingMode = PickingMode.Ignore }, "cp-hand-top");
            r.name = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-hand-name");
            r.mood = CampPages.Classed(new Label { pickingMode = PickingMode.Ignore }, "cp-mood");
            r.mood.AddToClassList("cp-mood--bad");
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
            CampReadouts.GoToHand(outpost, who);
        }

        // --- the facts -----------------------------------------------------------

        /// What is wrong with this hand, shortened, or null. `warn` for the
        /// amber kind (nothing is broken: idle, in reserve, busy elsewhere);
        /// `stuck` for the ones the Stuck chip counts (the alert strip's rule,
        /// plus the downed, who are not working either).
        static string StatusOf(OutpostLedger l, OutpostHand h, CampReadouts.HandKind kind,
                               out bool warn, out bool stuck)
        {
            warn = false;
            stuck = false;
            if (h.downed)
            {
                stuck = true;
                return string.IsNullOrEmpty(h.downedCause) ? "down" : "down · " + h.downedCause;
            }
            if (OutpostLedger.Reserve(h)) { warn = true; stuck = true; return "held in reserve"; }
            if (h.order == OutpostOrder.Idle) { warn = true; stuck = true; return "no job"; }
            if (!h.walkingIn)
            {
                if (h.order == OutpostOrder.Gather && h.target == Res.Game && l.HunterBlocker() != null)
                {
                    stuck = true;
                    return "no spear · no hunting";
                }
                if (l.Stalled(h) || !string.IsNullOrEmpty(h.bodyBlocked))
                {
                    string why = l.StallReason(h);
                    if (!string.IsNullOrEmpty(why)) { stuck = true; return CampAlerts.Short(why); }
                }
                // **A builder held up for a material (2026-10-03)**: "waiting
                // for stone" -- amber, his job is fine; the material is the fix.
                string word = l.JobWord(h);
                if (OutpostLedger.IsBuilderWait(word)) { warn = true; return "waiting for " + word.Substring(OutpostLedger.BuilderWaitPrefix.Length); }
            }
            // Busy with something that is not his job (pouting, rescuing, a
            // raid): worth a word, not a problem.
            if (kind == CampReadouts.HandKind.Busy && !string.IsNullOrEmpty(h.Doing))
            {
                warn = true;
                return h.Doing;
            }
            return null;
        }

        public void Refresh()
        {
            var l = L;
            if (l == null || rootEl == null) return;

            int n = l.hands.Count;
            int beds = l.HousingCapacity;
            int warm = Mathf.Min(n, l.WarmBedCapacity);

            // Facts, once per hand.
            infos.Clear();
            int stuckN = 0, unhappyN = 0, idle = 0;
            foreach (var h in l.hands)
            {
                if (h == null) continue;
                var kind = CampReadouts.KindOf(l, h, out _);
                string status = StatusOf(l, h, kind, out bool warn, out bool stuck);
                infos.Add(new Info { h = h, kind = kind, status = status, warn = warn, stuck = stuck });
                if (stuck) stuckN++;
                if (h.MoodWord.Length > 0) unhappyN++;
                // "No job" only (2026-10-03): the player's reserve is his choice.
                if (kind == CampReadouts.HandKind.Unassigned && !OutpostLedger.Reserve(h)) idle++;
            }

            int total = CampReadouts.Total(l);
            int working = CampReadouts.Working(l);
            ReadoutUi.SetText(big, $"{working} of {total}");
            ReadoutUi.SetText(small, total == 0 ? "nobody lives here yet"
                : working == total ? "working · everyone is at work" : "working");
            if (subtitle != null)
                ReadoutUi.SetText(subtitle, $"{n} / {beds} beds · {warm} warm" + (idle > 0 ? $" · {idle} with no job" : ""));

            FillCampOrders(l);
            SetChip(0, $"All {n}");
            SetChip(1, $"Stuck {stuckN}");
            SetChip(2, $"Unhappy {unhappyN}");
            RefreshAssign(l);

            // Rows: the ones that need looking at first, roster order inside
            // each group.
            int shown = 0;
            foreach (var kind in Order)
            {
                for (int i = 0; i < infos.Count; i++)
                {
                    var info = infos[i];
                    if (info.kind != kind) continue;
                    var h = info.h;
                    if (filter == Filter.Stuck && !info.stuck) continue;
                    if (filter == Filter.Unhappy && h.MoodWord.Length == 0) continue;
                    if (shown >= rows.Count) rows.Add(NewRow());
                    var r = rows[shown++];
                    r.root.style.display = DisplayStyle.Flex;
                    r.who = h.name;
                    r.Set(SheetBits.Initial(h.name), h.name ?? "", h.MoodWord,
                        CampReadouts.JobOf(l, h), info.status,
                        info.warn, info.stuck && !info.warn, h.Angry);
                }
            }
            for (int i = shown; i < rows.Count; i++) rows[i].root.style.display = DisplayStyle.None;

            string empty = n == 0 ? "Nobody lives here yet."
                : filter == Filter.Stuck ? "Nobody is stuck."
                : filter == Filter.Unhappy ? "Nobody is unhappy."
                : "";
            ReadoutUi.SetText(emptyNote, empty);
            ReadoutUi.Show(emptyNote, shown == 0);
        }

        // --- the camp's standing orders (menu rework #8, 2026-09-27) ----------
        //
        // Moved here from `PeopleSheet` with the merge: what the hands work
        // on first (`OutpostLedger.priority`, the same field the ledger's
        // tick reads) and when the next hand arrives.

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
            rec.style.marginTop = 4;
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

            // One line about the next hand; the beds are in the subtitle.
            bool room = l.Housed < l.HousingCapacity;
            // Work days on the books, sky days on the sheet (2026-09-29).
            float days = Mathf.Max(0f, OutpostLedger.DaysPerRecruit - l.recruitProgress)
                * TimeOfDay.SkyDaysPerWorkDay;
            string text = room
                ? $"Next hand in {days:0.#} days · needs {OutpostLedger.RecruitFoodCost} food"
                : "No room for another hand · build a hut";
            ReadoutUi.SetText(recruitLine, text);
            ReadoutUi.Show(recruitTrack, room);
            recruitFill.style.width = Length.Percent(Mathf.Clamp01(l.RecruitProgress01) * 100f);
        }

        void SetChip(int i, string text)
        {
            if (chips != null && chips[i].text != text) chips[i].text = text;
        }
    }
}
