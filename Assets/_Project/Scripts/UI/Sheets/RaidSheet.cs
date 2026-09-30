using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The Raid card (menu rework #2, Kevin approved 2026-09-27).**
    ///
    /// The RAID chip used to open the old campfire sheet's orders page --
    /// rations and work priority in the middle of a raid. Now it opens the
    /// threat and the one decision that answers it, in the Midnight `.st`
    /// look of `HandSheet`:
    ///
    /// 1. **header** -- sword glyph, "Raid · island", pill "3 ashore"
    ///    (ember) / "making for the beach" (amber) / "over" (moss);
    /// 2. **chips** -- Raiders (ashore / incoming / taken so far), Wall
    ///    (sections standing / breached), Gate (shut / broken / none);
    /// 3. **the lookout card** -- who is on the tower, arrows, the gun;
    /// 4. **Defenders** -- the same `WatchTiles` as the Lookout card: tap
    ///    to post, tap again to recall;
    /// 5. **thumb row** -- Walls (Build › Defence) · Post {first idle}.
    ///
    /// Reads only `RaidParty.Active`, `RaidDirector.Incoming / WarnedOf /
    /// LastResult` and the camp's ledger and walls; every verb is
    /// `Outpost.Assign` / `OrderIdle`.
    public sealed class RaidSheet : ISheetFramed
    {
        readonly Outpost outpost;

        public RaidSheet(Outpost camp)
        {
            outpost = camp;
        }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        // --- ISheet / ISheetFramed -------------------------------------------

        public string Title => "Raid";
        public Color Accent => MidnightLandHud.Ice;
        public bool WantsTallSheet => true;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public VisualElement BuildActions() => null;

        public bool StillValid =>
            outpost != null && outpost.Ledger != null && (outpost.HasCamp || outpost.Building);

        public Vector3 AnchorWorld => outpost != null ? outpost.CampCentre : Vector3.zero;

        // --- the threat, read once per refresh --------------------------------

        enum Phase { Ashore, Incoming, Over }

        Phase PhaseOf(out int ashore, out int stolen)
        {
            ashore = 0; stolen = 0;
            var party = SeaSick.Combat.RaidParty.Active;
            if (party != null && party.Camp == outpost)
            {
                ashore = party.Ashore;
                stolen = party.Stolen;
                return Phase.Ashore;
            }
            var incoming = SeaSick.Combat.RaidDirector.Incoming(outpost);
            if (incoming != null) return Phase.Incoming;
            return Phase.Over;
        }

        // --- header -------------------------------------------------------------

        WatchTiles.Head head;

        public VisualElement BuildHeader()
        {
            head = new WatchTiles.Head("sword", "Raid");
            FillHeader();
            return head.Root;
        }

        void FillHeader()
        {
            if (head == null || outpost == null) return;
            head.SetSub(StationPage.Cap(StationPage.IslandName(outpost)));
            switch (PhaseOf(out int ashore, out _))
            {
                case Phase.Ashore: head.SetPill($"{ashore} ashore", StationPage.PillBad); break;
                case Phase.Incoming: head.SetPill("Making for the beach", StationPage.PillWait); break;
                default: head.SetPill("Over", StationPage.PillGood); break;
            }
        }

        // --- the page -------------------------------------------------------------

        VisualElement root, toast;
        Label toastText;
        IVisualElementScheduledItem toastHide;
        Label raidersV, wallV, gateV;
        VisualElement watchCard;
        Label watchT, watchS;
        WatchTiles tiles;
        Button wallsBtn, mainBtn, hideBtn;

        public VisualElement Build()
        {
            root = WatchTiles.Root("st-page");
            StationPage.FitToParent(root);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("st-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            root.Add(scroll);
            var col = new VisualElement();
            col.AddToClassList("st-content");
            scroll.Add(col);

            // --- chips: the threat and the defences
            var chips = WatchTiles.Box("hs-chips");
            chips.style.marginTop = 0;
            raidersV = WatchTiles.Chip(chips, "RAIDERS", true);
            wallV = WatchTiles.Chip(chips, "WALL", false);
            gateV = WatchTiles.Chip(chips, "GATE", false);
            col.Add(chips);

            // --- the lookout
            watchCard = StationPage.Card();
            watchCard.AddToClassList("hs-now");
            watchCard.AddToClassList("lk-card");
            watchCard.style.marginTop = 12;
            var top = WatchTiles.Box("hs-now-top");
            var icoBox = WatchTiles.Box("hs-now-ico");
            icoBox.Add(new WatchTiles.Glyph("tower"));
            top.Add(icoBox);
            var words = WatchTiles.Box("hs-now-words");
            watchT = StationPage.Text("", "hs-now-t");
            watchS = StationPage.Text("", "hs-now-s");
            words.Add(watchT);
            words.Add(watchS);
            top.Add(words);
            watchCard.Add(top);
            col.Add(watchCard);

            // --- defenders
            var eye = WatchTiles.Box("hs-eye-row");
            eye.Add(StationPage.Text("DEFENDERS", "st-eyebrow"));
            eye.Add(StationPage.Text("tap to post · tap again to recall", "hs-eye-em"));
            col.Add(eye);
            tiles = new WatchTiles(outpost, WatchTiles.PerPage(330f), ShowToast, Refresh);
            col.Add(tiles.Note);
            col.Add(tiles.Grid);
            col.Add(tiles.Dots);

            // --- the thumb row
            var acts = WatchTiles.Box("hs-acts");
            acts.pickingMode = PickingMode.Position;
            wallsBtn = new Button(Walls) { text = "Walls" };
            wallsBtn.AddToClassList("st-btn");
            wallsBtn.AddToClassList("hs-act");
            mainBtn = new Button(Main) { text = "" };
            mainBtn.AddToClassList("st-btn");
            mainBtn.AddToClassList("hs-act");
            // **Hide everyone (2026-09-30)** -- the raid switch, here instead of
            // the floating IMGUI button (`RaidBanner`). Only while raiders are
            // on the sand; first in the row, and the loud one; "Walls" steps aside while it shows.
            hideBtn = new Button(HideAll) { text = "Hide everyone" };
            hideBtn.AddToClassList("st-btn");
            hideBtn.AddToClassList("hs-act");
            hideBtn.AddToClassList("hs-act--stop");
            hideBtn.style.display = DisplayStyle.None;
            acts.Add(hideBtn);
            acts.Add(wallsBtn);
            acts.Add(mainBtn);
            root.Add(acts);

            toast = WatchTiles.Box("hs-toast");
            toastText = StationPage.Text("", "hs-toast-t");
            toast.Add(toastText);
            toast.style.display = DisplayStyle.None;
            root.Add(toast);

            Refresh();
            return root;
        }

        void ShowToast(string text)
        {
            if (toast == null || string.IsNullOrEmpty(text)) return;
            WatchTiles.Set(toastText, StationPage.Cap(text));
            toast.style.display = DisplayStyle.Flex;
            toast.BringToFront();
            toastHide?.Pause();
            toastHide = toast.schedule.Execute(() => toast.style.display = DisplayStyle.None);
            toastHide.ExecuteLater(2600);
        }

        // --- refresh ----------------------------------------------------------------

        public void Refresh()
        {
            var l = L;
            if (l == null) return;
            FillHeader();
            if (root == null) return;

            // Raiders.
            var phase = PhaseOf(out int ashore, out int stolen);
            switch (phase)
            {
                case Phase.Ashore:
                    WatchTiles.Set(raidersV, stolen > 0 ? $"{ashore} · took {stolen}" : $"{ashore} ashore");
                    WatchTiles.Tone(raidersV, ashore > 0 ? 2 : 1);
                    break;
                case Phase.Incoming:
                    WatchTiles.Set(raidersV, "Incoming");
                    WatchTiles.Tone(raidersV, 1);
                    break;
                default:
                    WatchTiles.Set(raidersV, "Gone");
                    WatchTiles.Tone(raidersV, 0);
                    break;
            }

            // Walls and gates.
            int sections = 0, up = 0, gates = 0, gatesBroken = 0;
            foreach (var w in outpost.Walls)
            {
                if (w == null) continue;
                if (w.IsGate) { gates++; if (w.Breached) gatesBroken++; continue; }
                sections++;
                if (!w.Breached) up++;
            }
            if (sections == 0) { WatchTiles.Set(wallV, "None"); WatchTiles.Tone(wallV, 1); }
            else
            {
                WatchTiles.Set(wallV, $"{up}/{sections} up");
                WatchTiles.Tone(wallV, up == sections ? 0 : 2);
            }
            if (gates == 0) { WatchTiles.Set(gateV, "None"); WatchTiles.Tone(gateV, 1); }
            else if (gatesBroken > 0) { WatchTiles.Set(gateV, gates == 1 ? "Broken" : $"{gatesBroken}/{gates} broken"); WatchTiles.Tone(gateV, 2); }
            else { WatchTiles.Set(gateV, gates == 1 ? "Shut" : $"{gates} shut"); WatchTiles.Tone(gateV, 0); }

            // The lookout.
            int towers = outpost.CountOf(OutpostLedger.WatchtowerId);
            int manned = l.HandsOn(OutpostOrder.Work, OutpostLedger.WatchtowerId);
            var post = SheetBits.Lookout(l);
            int arrows = l.CountOf(Res.Arrows);
            string t, s;
            if (towers <= 0)
            {
                t = "No watchtower";
                s = "Nobody can shoot back · raise a tower from Build › Defence";
            }
            else if (post == null)
            {
                t = "No lookout, no volley";
                s = "Post someone on the tower to shoot back";
            }
            else
            {
                t = manned > 1 ? $"{post.name} and {manned - 1} more on watch" : $"{post.name} on the tower";
                s = arrows > 0
                    ? $"Working the tower gun · {arrows} arrow{(arrows == 1 ? "" : "s")} in the quiver"
                    : "Working the tower gun · no arrows left for a volley";
            }
            WatchTiles.Set(watchT, t);
            WatchTiles.Set(watchS, s);
            watchCard.EnableInClassList("lk-card--bad", post == null);
            watchCard.EnableInClassList("lk-card--good", post != null);

            tiles.Refresh(l);
            FillMain(l, towers);
            FillHide(phase == Phase.Ashore);
        }

        /// Show the Hide / Send-out switch while raiders are ashore; the
        /// first visible button in the row carries the flush-left margin.
        void FillHide(bool live)
        {
            bool hiding = live && SeaSick.Combat.RaidAlarm.IsHiding(outpost);
            hideBtn.style.display = live ? DisplayStyle.Flex : DisplayStyle.None;
            string text = hiding ? "Send the armed out" : "Hide everyone";
            if (hideBtn.text != text) hideBtn.text = text;
            hideBtn.EnableInClassList("hs-act--first", live);
            // Three buttons crowd a phone row; mid-raid "Walls" (a build
            // shortcut) gives way to the switch.
            wallsBtn.style.display = live ? DisplayStyle.None : DisplayStyle.Flex;
            wallsBtn.EnableInClassList("hs-act--first", !live);
        }

        void HideAll()
        {
            SeaSick.Combat.RaidAlarm.HideAll(outpost, !SeaSick.Combat.RaidAlarm.IsHiding(outpost));
            Refresh();
        }

        // --- the thumb row ---------------------------------------------------------------

        string mainWho;
        bool mainBuild;

        void FillMain(OutpostLedger l, int towers)
        {
            var free = WatchTiles.FirstFree(l);
            string text;
            if (towers <= 0) { mainBuild = true; mainWho = null; text = "Build a tower"; }
            else if (free != null) { mainBuild = false; mainWho = free.name; text = "Post " + free.name; }
            else { mainBuild = false; mainWho = null; text = "Close"; }
            if (mainBtn.text != text) mainBtn.text = text;
            mainBtn.EnableInClassList("lk-act--pri", mainBuild || mainWho != null);
        }

        void Main()
        {
            if (L == null) return;
            if (mainBuild) { Sheets.Open(CampAlerts.BuildList(outpost, BuildPlans.Watchtower.id)); return; }
            if (mainWho == null) { Sheets.Close(); return; }
            var h = outpost.HandNamed(mainWho);
            if (h != null) tiles.Post(h);
            Refresh();
        }

        /// Build › Defence (the watchtower's group is the wall's).
        void Walls() => Sheets.Open(CampAlerts.BuildList(outpost, BuildPlans.Watchtower.id));
    }
}
