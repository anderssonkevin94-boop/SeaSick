using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **One villager, option A (Kevin picked it, 2026-09-27).** One page,
    /// no tabs, in the Midnight `.st` cards the Station / Stores / People
    /// pages already wear:
    ///
    /// 1. **header** -- avatar with a job badge, the name, "Job · Place",
    ///    a status pill (working / helping build / waiting / stuck, from
    ///    `OutpostLedger.StallReason`) and ×;
    /// 2. **Now** -- what he carries (item icon + count) and the walked
    ///    trip's legs (fetch · work · carry · drop, from the saved walker
    ///    state `OutpostHand.tripLeg`); when stuck, the reason under it;
    /// 3. **three chips** -- Mood, Sleeps (warm / cold hut), Food (the
    ///    camp's days of food, `SheetBits.FoodDays`);
    /// 4. **Jobs** -- a 3-wide tile grid, posts → gather → the open site.
    ///    A tile IS the order: one tap, the same `Outpost.Assign` /
    ///    `OrderGather` / `OrderBuild` the old list pressed. A full post
    ///    reads "swap with Tam" and trades jobs; a locked tile is dashed
    ///    with a two-word reason and toasts the whole reason on tap; a
    ///    worked-out seam is dimmed. More than fit PAGE with dots, never a
    ///    scroll;
    /// 5. **Stand down / Back aboard** pinned at the bottom.
    ///
    /// Siting a new building is gone from here -- it lives in ☰ Build.
    ///
    /// **Kept by name, not by reference.** The rows are rebuilt from the
    /// ledger and a cached row is a row that outlives the hand.
    ///
    /// **The V2 Split layout (Kevin picked it, 2026-09-27).** Same content,
    /// compact: 3-wide tiles are one short row each (icon left, name and
    /// line right), chips are single-line pills, and on the phone the frame
    /// is `PhoneHeight` of the screen (`SheetHost.FrameSizeScreen`) so the
    /// island camera frames him in the world above it -- `IslandCam`'s hero
    /// shot, opened through `FollowTarget` by `Sheets.Open`. No second
    /// camera, no render texture. On a desk the sheet is the right-hand
    /// column as before and he is framed to the left of it.
    ///
    /// **Built once, re-texted after.** `SheetHost` refreshes every 0.25 s
    /// and a UI Toolkit click needs press and release on the SAME element,
    /// so tiles are rebuilt only when the SET of jobs changes (a building
    /// raised, a site sited), never on the timer.
    public class HandSheet : ISheetFramed
    {
        readonly Outpost outpost;
        readonly string who;

        public HandSheet(Outpost o, string who)
        {
            outpost = o;
            this.who = who;
        }

        OutpostHand Hand => outpost != null ? outpost.HandNamed(who) : null;
        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        // --- ISheet / ISheetFramed -------------------------------------------

        public string Title => who;
        public Color Accent => MidnightLandHud.Ice;
        /// Not the full band: the top of the screen is the world now.
        public bool WantsTallSheet => false;

        /// The phone frame's top edge, as a fraction of the safe height up
        /// from the bottom. ~40% of the screen stays world above it.
        public static float PhoneHeight = 0.62f;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public VisualElement BuildActions() => null;

        public bool StillValid => outpost != null && outpost.Ledger != null && Hand != null;

        /// **The body the camera should watch.** `Sheets.Open` reads this and
        /// puts `IslandCam` on him (Kevin, 2026-09-22: "I want the camera to
        /// follow them"). Null before his body is raised.
        public Transform FollowTarget
        {
            get
            {
                var body = outpost != null ? outpost.BodyNamed(who) : null;
                return body != null ? body.transform : null;
            }
        }

        /// Where he is standing; the fire before his body is raised.
        public Vector3 AnchorWorld
        {
            get
            {
                if (outpost == null) return Vector3.zero;
                var body = outpost.BodyNamed(who);
                return body != null ? body.transform.position : outpost.CampCentre;
            }
        }

        // --- styles ------------------------------------------------------------

        static StyleSheet handSheet;
        static bool handLoaded;

        static VisualElement Styled(VisualElement e)
        {
            if (!handLoaded)
            {
                handLoaded = true;
                handSheet = Resources.Load<StyleSheet>("UI/Hand");
                if (handSheet == null) Debug.LogWarning("[Sheets] Resources/UI/Hand.uss is missing — the hand sheet will be half styled.");
            }
            if (handSheet != null) e.styleSheets.Add(handSheet);
            return e;
        }

        static VisualElement Box(string cls)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(cls);
            return e;
        }

        static Label Text(string cls) => StationPage.Text("", cls);

        static void Set(Label l, string s)
        {
            s = s ?? "";
            if (l != null && l.text != s) l.text = s;
        }

        static void Show(VisualElement e, bool on)
        {
            if (e == null) return;
            e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static void Tone(Label l, int tone)
        {
            l.EnableInClassList("hs-tone--good", tone == 0);
            l.EnableInClassList("hs-tone--warn", tone == 1);
            l.EnableInClassList("hs-tone--bad", tone == 2);
        }

        // --- the header ---------------------------------------------------------

        Label hInitial, hName, hSub, hPill;
        VisualElement hPillBox, hBadge, hBadgeIco;
        int pillKind = -1;

        public VisualElement BuildHeader()
        {
            var root = Styled(StationPage.Root("st-head"));
            root.AddToClassList("hs-head");

            var av = Box("hs-av");
            var circle = Box("st-avatar");
            hInitial = Text("st-avatar-text");
            circle.Add(hInitial);
            av.Add(circle);
            hBadge = Box("hs-badge");
            hBadgeIco = Box("hs-badge-ico");
            hBadge.Add(hBadgeIco);
            av.Add(hBadge);
            root.Add(av);

            var words = Box("st-head-words");
            hName = Text("st-title");
            hSub = Text("st-sub");
            words.Add(hName);
            words.Add(hSub);
            root.Add(words);

            hPillBox = Box("st-pill");
            hPill = Text("st-pill-text");
            hPillBox.Add(hPill);
            root.Add(hPillBox);

            var close = new Button(() => Sheets.Close()) { text = "" };
            close.AddToClassList("st-square");
            close.tooltip = "Close";
            close.Add(new StationPage.Glyph("close", StationPage.Ink, "st-glyph"));
            root.Add(close);

            pillKind = -1;
            FillHeader(L, Hand);
            return root;
        }

        void FillHeader(OutpostLedger l, OutpostHand h)
        {
            if (hName == null || l == null || h == null) return;
            Set(hInitial, SheetBits.Initial(who));
            Set(hName, who);
            Set(hSub, JobPlace(l, h));
            string icon = JobIcon(h);
            Show(hBadge, icon != null);
            StationPage.SetIcon(hBadgeIco, icon);

            var (text, kind) = Pill(l, h);
            Set(hPill, text);
            if (kind != pillKind)
            {
                pillKind = kind;
                hPillBox.EnableInClassList("st-pill--wait", kind == StationPage.PillWait);
                hPillBox.EnableInClassList("st-pill--bad", kind == StationPage.PillBad);
            }
        }

        /// "Sawyer · Sawmill", "Gatherer · Timber", "Hunter · the wilds",
        /// "Builder · Hut site", "No job · by the fire".
        static string JobPlace(OutpostLedger l, OutpostHand h)
        {
            switch (h.order)
            {
                case OutpostOrder.Work:
                {
                    var plan = BuildPlans.Named(h.target);
                    string post = string.IsNullOrEmpty(plan.position) ? "worker" : plan.position;
                    // A runner's job is the wheelbarrow (2026-10-02), whatever
                    // the store hut's plan calls its post.
                    if (OutpostLedger.IsRunner(h)) post = "runner";
                    return $"{StationPage.Cap(post)} · {StationPage.Cap(plan.label)}";
                }
                case OutpostOrder.Gather:
                    if (h.target == Res.Game) return "Hunter · the wilds";
                    return "Gatherer · " + StationPage.Cap(ResDefs.Label(h.target));
                case OutpostOrder.Build:
                {
                    var site = l.BuildSiteFor(h) ?? l.Focus;
                    return site == null ? "Builder · nothing sited" : "Builder · " + SiteName(site);
                }
                default:
                    return "No job · by the fire";
            }
        }

        /// The status pill. Stuck = `StallReason` names something that
        /// stops him (or the hunt gate / a wall); slow and walking up are
        /// amber; an idle hand is waiting; a builder is "helping build"
        /// whether the player sent him or the idle-hand ladder did
        /// (`OutpostLedger.EnlistFree` puts every free hand on the sites).
        static (string, int) Pill(OutpostLedger l, OutpostHand h)
        {
            if (h.order == OutpostOrder.Idle) return ("waiting", StationPage.PillWait);
            if (h.walkingIn) return ("walking up", StationPage.PillWait);
            if (StuckReason(l, h) != null) return ("stuck", StationPage.PillBad);
            if (l.RunnerWaiting(h)) return ("on call", StationPage.PillGood);
            string why = l.StallReason(h);
            if (why != null) return ("slow", StationPage.PillWait);
            if (h.order == OutpostOrder.Build) return ("helping build", StationPage.PillGood);
            return ("working", StationPage.PillGood);
        }

        /// Why he is stopped, or null -- the same rule `PeopleSheet` uses
        /// for its Stuck filter: a stall, a body walled off, or a hunter
        /// with no spear. "Working slowly" is not stuck.
        static string StuckReason(OutpostLedger l, OutpostHand h)
        {
            if (h.order == OutpostOrder.Idle || h.walkingIn) return null;
            if (h.order == OutpostOrder.Gather && h.target == Res.Game && l.HunterBlocker() != null)
                return "no spear, no hunting — the forge makes one from a board and a stone";
            if (!l.Stalled(h) && string.IsNullOrEmpty(h.bodyBlocked)) return null;
            return l.StallReason(h);
        }

        // --- the page -------------------------------------------------------------

        VisualElement root, toast;
        Label toastText;
        IVisualElementScheduledItem toastHide;

        // now
        VisualElement nowIco;
        Label nowT, nowS, stuckLine;
        VisualElement legsRow;
        readonly VisualElement[] legs = new VisualElement[4];
        readonly Label[] legText = new Label[4];

        // chips
        Label moodV, sleepV, foodV;

        // jobs
        Label jobsEm;
        VisualElement grid, dots;
        readonly List<Tile> tiles = new List<Tile>();
        string jobsKey;
        int perPage = 9, page;

        // actions
        Button stand, back;

        public VisualElement Build()
        {
            tiles.Clear();
            jobsKey = null;
            page = 0;

            root = Styled(StationPage.Root("st-page"));
            StationPage.FitToParent(root);

            // A safety net only: on the phone the page fits; the desk's
            // shorter column can wheel it.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("st-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            root.Add(scroll);
            var col = new VisualElement();
            col.AddToClassList("st-content");
            scroll.Add(col);

            // --- Now
            var now = StationPage.Card();
            now.AddToClassList("hs-now");
            var top = Box("hs-now-top");
            var icoBox = Box("hs-now-ico");
            nowIco = Box("hs-now-ico-img");
            icoBox.Add(nowIco);
            top.Add(icoBox);
            var words = Box("hs-now-words");
            nowT = Text("hs-now-t");
            nowS = Text("hs-now-s");
            words.Add(nowT);
            words.Add(nowS);
            top.Add(words);
            now.Add(top);
            legsRow = Box("hs-legs");
            for (int i = 0; i < legs.Length; i++)
            {
                legs[i] = Box("hs-leg");
                if (i == 0) legs[i].AddToClassList("hs-leg--first");
                legText[i] = Text("hs-leg-t");
                legs[i].Add(legText[i]);
                legsRow.Add(legs[i]);
            }
            now.Add(legsRow);
            col.Add(now);

            stuckLine = Text("st-stall");
            col.Add(stuckLine);

            // --- chips
            var chips = Box("hs-chips");
            moodV = Chip(chips, "MOOD", true);
            sleepV = Chip(chips, "SLEEPS", false);
            foodV = Chip(chips, "FOOD", false);
            col.Add(chips);

            // --- jobs
            var eye = Box("hs-eye-row");
            eye.Add(StationPage.Text("JOBS", "st-eyebrow"));
            jobsEm = Text("hs-eye-em");
            eye.Add(jobsEm);
            col.Add(eye);
            grid = Box("hs-grid");
            grid.pickingMode = PickingMode.Position;
            col.Add(grid);
            dots = Box("st-dots");
            dots.pickingMode = PickingMode.Position;
            col.Add(dots);

            // --- actions, pinned under the scroll
            var acts = Box("hs-acts");
            acts.pickingMode = PickingMode.Position;
            stand = new Button(StandDown) { text = "Stand down" };
            stand.AddToClassList("st-btn");
            stand.AddToClassList("hs-act");
            stand.AddToClassList("hs-act--first");
            stand.AddToClassList("hs-act--stop");
            back = new Button(BackAboard) { text = "Back aboard" };
            back.AddToClassList("st-btn");
            back.AddToClassList("hs-act");
            acts.Add(stand);
            acts.Add(back);
            root.Add(acts);

            // --- the toast, over everything
            toast = Box("hs-toast");
            toastText = Text("hs-toast-t");
            toast.Add(toastText);
            toast.style.display = DisplayStyle.None;
            root.Add(toast);

            perPage = TilesPerPage();
            Refresh();
            return root;
        }

        static Label Chip(VisualElement row, string key, bool first)
        {
            var c = Box("hs-chip");
            if (first) c.AddToClassList("hs-chip--first");
            c.Add(StationPage.Text(key, "hs-chip-k"));
            var v = Text("hs-chip-v");
            c.Add(v);
            row.Add(c);
            return v;
        }

        /// **Three rows a page when they fit, fewer when they do not** --
        /// worked out from the frame BEFORE building (same arithmetic as
        /// `StationSheet.PerPage`), so a page never re-plans under a finger.
        static int TilesPerPage()
        {
            float frame = SheetHost.FrameSizeScreen().y * SheetHost.PanelScale;
            float avail = frame - SheetHost.BorderPx - 82f - SheetHost.BodyPadPx - 22f;
            // Now card (with legs) + chips + eyebrow + actions + gaps, in
            // the compact V2 sizes of Hand.uss; a stall line or a dots row
            // is absorbed by the scroll's safety net.
            const float Rest = 205f, Row = 56f;
            int rows = Mathf.Clamp(Mathf.FloorToInt((avail - Rest) / Row), 1, 3);
            return rows * 3;
        }

        void ShowToast(string text)
        {
            if (toast == null || string.IsNullOrEmpty(text)) return;
            Set(toastText, StationPage.Cap(text));
            toast.style.display = DisplayStyle.Flex;
            toast.BringToFront();
            toastHide?.Pause();
            toastHide = toast.schedule.Execute(() => toast.style.display = DisplayStyle.None);
            toastHide.ExecuteLater(2600);
        }

        // --- refresh ------------------------------------------------------------------

        public void Refresh()
        {
            var l = L;
            var h = Hand;
            if (l == null || h == null) return;
            outpost.CatchUp();

            FillHeader(l, h);
            if (root == null) return;
            FillNow(l, h);
            FillChips(l, h);
            FillJobs(l, h);

            stand.SetEnabled(h.order != OutpostOrder.Idle);
            back.SetEnabled(SheetBits.Anchor != null && outpost.BodyNamed(who) != null);
        }

        // --- Now ------------------------------------------------------------------------

        void FillNow(OutpostLedger l, OutpostHand h)
        {
            string main, sub, icon;
            if (h.Hauling)
            {
                icon = h.haulRes;
                string load = ResDefs.Counted(h.haulRes, h.haulCount);
                if (h.HuntTrip && !h.huntKilled) { main = "Out after game"; icon = Res.Game; }
                else if (h.haulPicked) main = $"Carrying {load} to the {PlaceName(l, h.haulTo, h.haulToStation)}";
                else if (h.haulFrom == HaulPlace.Shore) main = "Fishing at the shore";
                else main = $"Fetching {load} from the {PlaceName(l, h.haulFrom, h.haulFromStation)}";
                sub = DoingLine(l, h);
            }
            else
            {
                icon = JobIcon(h);
                main = DoingLine(l, h);
                sub = NowSub(l, h);
            }
            StationPage.SetIcon(nowIco, icon);
            Set(nowT, main);
            Set(nowS, sub);

            // The walked trip: fetch (to pickup) · work (at pickup) · carry
            // (to drop) · drop (at drop). Hidden with no trip on.
            var leg = h.Leg;
            bool trip = leg != TripLeg.None && h.Hauling;
            Show(legsRow, trip);
            if (trip)
            {
                int on = (int)leg - 1;
                for (int i = 0; i < legs.Length; i++)
                {
                    Set(legText[i], LegName(h, i));
                    legs[i].EnableInClassList("hs-leg--done", i < on);
                    legs[i].EnableInClassList("hs-leg--on", i == on);
                }
            }

            string reason = l.StatusReason(h);
            Set(stuckLine, StationPage.Cap(reason));
            stuckLine.style.color = l.StatusWord(h) == "Stuck" ? SheetTheme.Ember : MidnightLandHud.Muted;
            Show(stuckLine, !string.IsNullOrEmpty(reason));
        }

        static string LegName(OutpostHand h, int i)
        {
            switch (i)
            {
                case 0: return "fetch";
                case 1:
                    if (h.haulFrom == HaulPlace.Shore) return "fish";
                    if (h.haulFrom != HaulPlace.Field) return "load";
                    if (h.haulRes == Res.Game) return "hunt";
                    if (h.haulRes == Res.Timber) return "cut";
                    if (h.haulRes == Res.Stone || h.haulRes == Res.Ore) return "dig";
                    return "work";
                case 2: return "carry";
                default: return "drop";
            }
        }

        static string PlaceName(OutpostLedger l, HaulPlace p, int station)
        {
            switch (p)
            {
                case HaulPlace.Store: return "store";
                case HaulPlace.Site: return "site";
                case HaulPlace.Field: return "island";
                case HaulPlace.Shore: return "shore";
                case HaulPlace.Station:
                {
                    var list = l.Stations;
                    if (list != null && station >= 0 && station < list.Count && list[station] != null)
                        return BuildPlans.Named(list[station].planId).label;
                    return "bench";
                }
                default: return "camp";
            }
        }

        /// "Sawyer at the sawmill", "Gathering timber", "Building the hut".
        static string DoingLine(OutpostLedger l, OutpostHand h)
        {
            switch (h.order)
            {
                case OutpostOrder.Work:
                {
                    // "Runner, waiting" / "Running 6 boards to Sawmill".
                    if (OutpostLedger.IsRunner(h)) return StationPage.Cap(l.StatusWord(h));
                    var plan = BuildPlans.Named(h.target);
                    return $"{StationPage.Cap(h.Doing)} at the {plan.label}";
                }
                case OutpostOrder.Build:
                {
                    var site = l.BuildSiteFor(h) ?? l.Focus;
                    return site == null ? "Nothing sited to build" : "Building the " + SiteName(site).ToLowerInvariant();
                }
                case OutpostOrder.Idle:
                    return "Standing by the fire";
                default:
                    return StationPage.Cap(h.Doing);
            }
        }

        static string NowSub(OutpostLedger l, OutpostHand h)
        {
            switch (h.order)
            {
                case OutpostOrder.Gather:
                    if (h.target == Res.Game) return $"{l.StoreCountOf(Res.Food)} food in the store";
                    return $"{l.StoreCountOf(h.target)} / {l.ceilingPer} kept in the store";
                case OutpostOrder.Build:
                {
                    var site = l.BuildSiteFor(h) ?? l.Focus;
                    return site != null ? l.SiteLine(site) : "";
                }
                case OutpostOrder.Idle:
                    if (OutpostLedger.Reserve(h)) return "held in reserve";
                    return l.Building ? "free hands help at the sites on their own" : "waiting for orders";
                case OutpostOrder.Work when OutpostLedger.IsRunner(h):
                    return "moves goods to workshops and sites";
                default:
                    return "between trips";
            }
        }

        static string SiteName(PendingBuild p)
        {
            if (p == null) return "";
            if (p.isWall) return "Wall";
            return StationPage.Cap(BuildPlans.Named(p.planId).label);
        }

        // --- chips ------------------------------------------------------------------------

        void FillChips(OutpostLedger l, OutpostHand h)
        {
            string mood = h.MoodWord;
            Set(moodV, mood.Length == 0 ? "Content" : StationPage.Cap(mood));
            Tone(moodV, h.Angry ? 2 : mood.Length > 0 ? 1 : 0);

            int index = l.hands.IndexOf(h);
            bool warm = l.IsHandWarm(h);
            bool bed = index >= 0 && index < l.HousingCapacity;
            Set(sleepV, warm ? "Warm hut" : bed ? "Cold hut" : "No bed");
            Tone(sleepV, warm ? 0 : bed ? 1 : 2);

            float days = SheetBits.FoodDays(l);
            if (days < 0f) { Set(foodV, "None"); Tone(foodV, 2); }
            else
            {
                Set(foodV, days >= 10f ? $"{days:0} days" : days < 1f ? "< 1 day" : $"{days:0.#} days");
                Tone(foodV, days < 1f ? 2 : days < 3f ? 1 : 0);
            }
        }

        // --- jobs -------------------------------------------------------------------------

        enum JobKind { Post, Gather, Build }

        struct Job
        {
            public JobKind kind;
            public string id;
        }

        sealed class Tile
        {
            public Job job;
            public Button root;
            public VisualElement icon, who;
            public Label name, sub, whoText;
            public StationPage.DashedFrame dashes;
            public string toast;     // what a tap says instead of ordering, or null
            public bool here;
        }

        /// Posts → gather → the open site, in the order the menu offers them.
        List<Job> Jobs()
        {
            var list = new List<Job>();
            foreach (var id in outpost.Positions()) list.Add(new Job { kind = JobKind.Post, id = id });
            foreach (var r in outpost.Gatherable()) list.Add(new Job { kind = JobKind.Gather, id = r });
            if (outpost.Building) list.Add(new Job { kind = JobKind.Build, id = "" });
            return list;
        }

        void FillJobs(OutpostLedger l, OutpostHand h)
        {
            Set(jobsEm, $"tap to move {who}");

            var jobs = Jobs();
            var sb = new System.Text.StringBuilder();
            foreach (var j in jobs) sb.Append((int)j.kind).Append(j.id).Append('|');
            string key = sb.ToString();
            if (key != jobsKey)
            {
                jobsKey = key;
                RebuildTiles(jobs);
            }

            foreach (var t in tiles) FillTile(l, h, t);
        }

        void RebuildTiles(List<Job> jobs)
        {
            grid.Clear();
            tiles.Clear();
            for (int i = 0; i < jobs.Count; i++)
            {
                var t = new Tile { job = jobs[i] };
                t.root = new Button(() => Press(t)) { text = "" };
                t.root.AddToClassList("hs-tile");
                if ((i % perPage) % 3 == 2) t.root.AddToClassList("hs-tile--col3");
                t.icon = Box("hs-tile-ico");
                StationPage.SetIcon(t.icon, JobIconOf(jobs[i]));
                t.root.Add(t.icon);
                var words = Box("hs-tile-words");
                t.name = Text("hs-tile-n");
                t.sub = Text("hs-tile-s");
                words.Add(t.name);
                words.Add(t.sub);
                t.root.Add(words);
                t.who = Box("hs-who");
                t.whoText = Text("hs-who-t");
                t.who.Add(t.whoText);
                t.root.Add(t.who);
                t.dashes = new StationPage.DashedFrame(16f, 2f, StationPage.Edge);
                t.root.Add(t.dashes);
                tiles.Add(t);
                grid.Add(t.root);
            }

            dots.Clear();
            int pages = Mathf.Max(1, Mathf.CeilToInt(jobs.Count / (float)perPage));
            if (page >= pages) page = pages - 1;
            if (pages > 1)
                for (int p = 0; p < pages; p++)
                {
                    int pg = p;
                    var b = new Button(() => { page = pg; ShowPage(); }) { text = "" };
                    b.AddToClassList("st-dot-btn");
                    var dot = Box("st-dot");
                    b.Add(dot);
                    dots.Add(b);
                }
            Show(dots, pages > 1);
            ShowPage();
        }

        void ShowPage()
        {
            for (int i = 0; i < tiles.Count; i++)
                tiles[i].root.style.display = i / perPage == page ? DisplayStyle.Flex : DisplayStyle.None;
            for (int p = 0; p < dots.childCount; p++)
                dots[p][0].EnableInClassList("st-dot--on", p == page);
        }

        void FillTile(OutpostLedger l, OutpostHand h, Tile t)
        {
            string name, sub, badge = null, toastText = null;
            bool here = false, locked = false, dim = false;

            switch (t.job.kind)
            {
                case JobKind.Post:
                {
                    var plan = BuildPlans.Named(t.job.id);
                    bool runner = t.job.id == BuildPlans.Storage.id;
                    // The store hut's post is the runner (2026-10-02): its
                    // tile says "Runner" and counts the store's own slots.
                    name = runner ? "Runner" : StationPage.Cap(plan.label);
                    here = h.order == OutpostOrder.Work && h.target == t.job.id;
                    var other = FirstOn(l, h, t.job.id);
                    int filled = CountOn(l, h, t.job.id);
                    int posts = runner ? Mathf.Max(1, l.RunnerSlots()) : Mathf.Max(1, outpost.CountOf(t.job.id));
                    string role = runner ? "runner" : string.IsNullOrEmpty(plan.position) ? "hand" : plan.position;
                    if (here) sub = "here now";
                    else if (other != null && filled >= posts)
                    {
                        sub = "swap with " + other.name;
                        badge = SheetBits.Initial(other.name);
                    }
                    else if (filled > 0) { sub = "join " + other.name; badge = filled.ToString(); }
                    else sub = "no " + role;
                    break;
                }
                case JobKind.Gather:
                {
                    string r = t.job.id;
                    name = r == Res.Game ? "Hunt" : StationPage.Cap(ResDefs.Label(r));
                    here = h.order == OutpostOrder.Gather && h.target == r;
                    string blocker = r == Res.Game ? l.HunterBlocker() : null;
                    var stock = l.Stock(r);
                    float standing = stock != null ? stock.standing : 0f;
                    int others = CountGather(l, h, r);
                    if (others > 0) badge = others.ToString();
                    if (blocker != null && !here)
                    {
                        locked = true;
                        sub = "needs spear or bow";
                        toastText = "Hunting needs a spear (the forge: a board and a stone) or a bow with arrows (the hunting lodge).";
                    }
                    else if (standing < 1f && !here)
                    {
                        dim = true;
                        sub = "worked out";
                        toastText = $"The {ResDefs.Label(r)} here is worked out — nothing left to gather.";
                    }
                    else if (here) sub = "here now";
                    else if (r == Res.Game) sub = $"{l.StoreCountOf(Res.Food)} food";
                    else sub = $"{l.StoreCountOf(r)} / {l.ceilingPer}";
                    break;
                }
                default:
                {
                    int n = l.SiteCount;
                    var site = l.Focus;
                    name = n > 1 ? $"{n} sites" : SiteName(site) + " site";
                    here = h.order == OutpostOrder.Build;
                    sub = here ? "here now" : "help build";
                    int crew = 0;
                    foreach (var o in l.hands)
                        if (o != null && o != h && o.order == OutpostOrder.Build) crew++;
                    if (crew > 0) badge = crew.ToString();
                    break;
                }
            }

            t.here = here;
            t.toast = toastText;
            Set(t.name, name);
            Set(t.sub, sub);
            Show(t.who, badge != null);
            Set(t.whoText, badge ?? "");
            t.root.EnableInClassList("hs-tile--on", here);
            t.root.EnableInClassList("hs-tile--lock", locked);
            t.root.EnableInClassList("hs-tile--out", dim);
            Show(t.dashes, locked);
        }

        static OutpostHand FirstOn(OutpostLedger l, OutpostHand me, string planId)
        {
            foreach (var o in l.hands)
                if (o != null && o != me && o.order == OutpostOrder.Work && o.target == planId) return o;
            return null;
        }

        static int CountOn(OutpostLedger l, OutpostHand me, string planId)
        {
            int n = 0;
            foreach (var o in l.hands)
                if (o != null && o != me && o.order == OutpostOrder.Work && o.target == planId) n++;
            return n;
        }

        static int CountGather(OutpostLedger l, OutpostHand me, string res)
        {
            int n = 0;
            foreach (var o in l.hands)
                if (o != null && o != me && o.order == OutpostOrder.Gather && o.target == res) n++;
            return n;
        }

        // --- the tap ------------------------------------------------------------------

        /// **A tile is the order.** The same `Outpost` verbs the old list
        /// pressed; a full post trades jobs (he takes the post, whoever was
        /// on it takes his old job).
        void Press(Tile t)
        {
            var l = L;
            var h = Hand;
            if (l == null || h == null) return;
            if (t.here) { ShowToast($"{who} is already on this."); return; }
            if (t.toast != null) { ShowToast(t.toast); return; }

            bool ok;
            switch (t.job.kind)
            {
                case JobKind.Post:
                {
                    string planId = t.job.id;
                    var other = FirstOn(l, h, planId);
                    int posts = planId == BuildPlans.Storage.id ? Mathf.Max(1, l.RunnerSlots())
                        : Mathf.Max(1, outpost.CountOf(planId));
                    bool swap = other != null && CountOn(l, h, planId) >= posts;
                    var oldOrder = h.order;
                    string oldTarget = h.target;
                    // One worker per station (2026-10-01): the one there
                    // steps off first, so the post has room for the swap.
                    if (swap) outpost.OrderIdle(other, reserve: false);
                    ok = outpost.Assign(h, planId);
                    if (!ok && swap) outpost.Assign(other, planId);
                    if (ok && swap)
                    {
                        GiveJob(other, oldOrder, oldTarget, planId);
                        ShowToast($"{who} and {other.name} swapped.");
                    }
                    if (!ok && outpost.AssignRefusal != null) { ShowToast(outpost.AssignRefusal); Refresh(); return; }
                    break;
                }
                case JobKind.Gather:
                    ok = outpost.OrderGather(h, t.job.id);
                    break;
                default:
                    ok = outpost.OrderBuild(h);
                    break;
            }
            if (!ok) ShowToast("That order didn't take.");
            Refresh();
        }

        /// The swapped-out hand takes the job the tapped hand just left.
        void GiveJob(OutpostHand o, OutpostOrder order, string target, string leftPost)
        {
            switch (order)
            {
                case OutpostOrder.Work:
                    if (target != leftPost && outpost.Assign(o, target)) return;
                    break;
                case OutpostOrder.Gather:
                    if (outpost.OrderGather(o, target)) return;
                    break;
                case OutpostOrder.Build:
                    if (outpost.OrderBuild(o)) return;
                    break;
            }
            outpost.OrderIdle(o, reserve: false);
        }

        // --- icons ----------------------------------------------------------------------

        /// The item a post makes, for its tile: the plan's own `makes`, else
        /// its first recipe's, else a generic mark.
        static string PostIcon(string planId)
        {
            var plan = BuildPlans.Named(planId);
            if (planId == BuildPlans.Storage.id) return Res.Boards;   // the goods a runner moves
            if (!string.IsNullOrEmpty(plan.makes) && ItemIconSet.Get(plan.makes) != null) return plan.makes;
            var recipes = Recipes.At(planId);
            if (recipes.Count > 0 && recipes[0] != null && !string.IsNullOrEmpty(recipes[0].makes)) return recipes[0].makes;
            if (planId == OutpostLedger.WatchtowerId) return "helmet";
            return "Tools";
        }

        static string JobIconOf(Job j)
        {
            switch (j.kind)
            {
                case JobKind.Post: return PostIcon(j.id);
                case JobKind.Gather: return j.id;
                default: return "Tools";
            }
        }

        static string JobIcon(OutpostHand h)
        {
            switch (h.order)
            {
                case OutpostOrder.Work: return PostIcon(h.target);
                case OutpostOrder.Gather: return string.IsNullOrEmpty(h.target) ? null : h.target;
                case OutpostOrder.Build: return "Tools";
                default: return null;
            }
        }

        // --- the two pinned verbs --------------------------------------------------------

        /// **Idle = held in reserve (2026-09-28, Kevin: "make idle stick").**
        /// The player's own stand-down keeps him off the sites, the food
        /// draft and the station hauling until he is given another order
        /// (`OutpostHand.playerIdle`); a hand merely bumped off a job is
        /// `OrderIdle(h, reserve: false)` and joins the idle-hand ladder.
        void StandDown()
        {
            var h = Hand;
            if (outpost == null || h == null) return;
            outpost.OrderIdle(h);
            ShowToast($"{who} stands down — held in reserve.");
            Refresh();
        }

        /// `Outpost.Recall(body, ship)` -- the same call the ashore column
        /// makes, with the roster recount the ship needs afterwards.
        void BackAboard()
        {
            var anchor = SheetBits.Anchor;
            if (outpost == null || anchor == null) return;
            var body = outpost.BodyNamed(who);
            if (body == null) return;
            if (outpost.Recall(body, anchor.transform))
            {
                var roster = SheetBits.Roster;
                if (roster != null) roster.Refresh();
                Sheets.Close();
            }
        }
    }
}
