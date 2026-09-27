using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The Lookout card (menu rework #1, Kevin approved 2026-09-27).**
    ///
    /// Kevin's report: the "Nobody on watch" chip opened the old campfire
    /// sheet's orders page -- rations and priority first, a tiny "post a
    /// lookout" button at the bottom that posted the first idle hand blindly
    /// and went grey with no word when nobody was idle. Now the chip AND a
    /// tap on the watchtower open this card, in the Midnight `.st` look of
    /// `HandSheet`:
    ///
    /// 1. **header** -- tower glyph, "Watchtower · island", a pill: Nobody
    ///    on watch (ember) / {name} on watch (moss);
    /// 2. **why** -- chips: Unwatched (raid every N days, from
    ///    `OutpostLedger.RaidDaysUnwatched`), Watched (no raids), On watch
    ///    (manned / towers); the quiver card (arrows the lookout looses at
    ///    a landing raid, `OutpostLedger.LookoutVolley`, and the tower gun
    ///    only fires with a hand in it, `WatchtowerGun.Manned`);
    /// 3. **Who could go** -- `WatchTiles`: tap a villager to post them,
    ///    tap the one on watch to relieve them; a card saying why in place
    ///    of the tiles when nobody can be posted;
    /// 4. **thumb row** -- Show tower · Post {first idle} (or Relieve {name}
    ///    when the watch is full and nobody is idle, or Close).
    ///
    /// A view only: every verb is `Outpost.Assign` / `OrderIdle`.
    public sealed class LookoutSheet : ISheetFramed
    {
        readonly Outpost outpost;
        readonly Building tower;
        int towerCursor;

        public LookoutSheet(Outpost camp, Building tower = null)
        {
            outpost = camp;
            this.tower = tower;
        }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        // --- ISheet / ISheetFramed -------------------------------------------

        public string Title => "Watchtower";
        public Color Accent => MidnightLandHud.Ice;
        public bool WantsTallSheet => true;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public VisualElement BuildActions() => null;

        public bool StillValid =>
            outpost != null && outpost.Ledger != null && (outpost.HasCamp || outpost.Building);

        public Vector3 AnchorWorld
        {
            get
            {
                if (tower != null) return tower.transform.position;
                var list = WatchTiles.TowersOf(outpost);
                if (list.Count > 0) return list[0].transform.position;
                return outpost != null ? outpost.CampCentre : Vector3.zero;
            }
        }

        // --- header -------------------------------------------------------------

        WatchTiles.Head head;

        public VisualElement BuildHeader()
        {
            head = new WatchTiles.Head("tower", "Watchtower");
            FillHeader(L);
            return head.Root;
        }

        void FillHeader(OutpostLedger l)
        {
            if (head == null || l == null) return;
            int towers = outpost.CountOf(OutpostLedger.WatchtowerId);
            string isle = StationPage.Cap(StationPage.IslandName(outpost));
            head.SetSub(towers > 1 ? $"{towers} towers · {isle}" : isle);
            var post = SheetBits.Lookout(l);
            if (towers <= 0) head.SetPill("No tower", StationPage.PillWait);
            else if (post == null) head.SetPill("Nobody on watch", StationPage.PillBad);
            else head.SetPill(post.name + " on watch", StationPage.PillGood);
        }

        // --- the page -------------------------------------------------------------

        VisualElement root, toast;
        Label toastText;
        IVisualElementScheduledItem toastHide;
        Label unwatchedV, watchedV, mannedV;
        VisualElement quiverCard, quiverIco;
        Label quiverT, quiverS;
        WatchTiles tiles;
        Button showBtn, mainBtn;

        public VisualElement Build()
        {
            root = WatchTiles.Root("st-page");
            StationPage.FitToParent(root);

            // A safety net only: the phone band fits; a desk column can wheel.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("st-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            root.Add(scroll);
            var col = new VisualElement();
            col.AddToClassList("st-content");
            scroll.Add(col);

            // --- why: chips
            var chips = WatchTiles.Box("hs-chips");
            chips.style.marginTop = 0;
            unwatchedV = WatchTiles.Chip(chips, "UNWATCHED", true);
            watchedV = WatchTiles.Chip(chips, "WATCHED", false);
            mannedV = WatchTiles.Chip(chips, "ON WATCH", false);
            col.Add(chips);

            // --- the quiver
            quiverCard = StationPage.Card();
            quiverCard.AddToClassList("hs-now");
            quiverCard.AddToClassList("lk-card");
            quiverCard.style.marginTop = 12;
            var top = WatchTiles.Box("hs-now-top");
            var icoBox = WatchTiles.Box("hs-now-ico");
            quiverIco = StationPage.Icon(Res.Arrows, "hs-now-ico-img");
            icoBox.Add(quiverIco);
            top.Add(icoBox);
            var words = WatchTiles.Box("hs-now-words");
            quiverT = StationPage.Text("", "hs-now-t");
            quiverS = StationPage.Text("", "hs-now-s");
            words.Add(quiverT);
            words.Add(quiverS);
            top.Add(words);
            quiverCard.Add(top);
            col.Add(quiverCard);

            // --- who could go
            var eye = WatchTiles.Box("hs-eye-row");
            eye.Add(StationPage.Text("WHO COULD GO", "st-eyebrow"));
            eye.Add(StationPage.Text("tap to post · tap again to relieve", "hs-eye-em"));
            col.Add(eye);
            // Chips + quiver card + eyebrow + dots + actions + gaps.
            tiles = new WatchTiles(outpost, WatchTiles.PerPage(330f), ShowToast, Refresh);
            col.Add(tiles.Note);
            col.Add(tiles.Grid);
            col.Add(tiles.Dots);

            // --- the thumb row, pinned under the scroll
            var acts = WatchTiles.Box("hs-acts");
            acts.pickingMode = PickingMode.Position;
            showBtn = new Button(ShowTower) { text = "Show tower" };
            showBtn.AddToClassList("st-btn");
            showBtn.AddToClassList("hs-act");
            showBtn.AddToClassList("hs-act--first");
            mainBtn = new Button(Main) { text = "" };
            mainBtn.AddToClassList("st-btn");
            mainBtn.AddToClassList("hs-act");
            acts.Add(showBtn);
            acts.Add(mainBtn);
            root.Add(acts);

            // --- the toast, over everything
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
            FillHeader(l);
            if (root == null) return;

            // Why: the two ends of the choice, side by side.
            float every = l.RaidDaysUnwatched;
            bool nobody = float.IsInfinity(every) || float.IsNaN(every) || every <= 0f;
            WatchTiles.Set(unwatchedV, nobody ? "No raiders" : $"Raid every {every:0.#} d");
            WatchTiles.Tone(unwatchedV, nobody ? 0 : 2);
            WatchTiles.Set(watchedV, "No raids");
            WatchTiles.Tone(watchedV, 0);
            int towers = outpost.CountOf(OutpostLedger.WatchtowerId);
            int manned = l.HandsOn(OutpostOrder.Work, OutpostLedger.WatchtowerId);
            WatchTiles.Set(mannedV, towers <= 0 ? "No tower" : $"{Mathf.Min(manned, towers)} / {towers}");
            WatchTiles.Tone(mannedV, towers <= 0 ? 1 : manned >= towers ? 0 : manned > 0 ? 1 : 2);

            // The quiver.
            int arrows = l.CountOf(Res.Arrows);
            var post = SheetBits.Lookout(l);
            WatchTiles.Set(quiverT, arrows == 0 ? "Quiver empty" : arrows == 1 ? "Quiver 1 arrow" : $"Quiver {arrows} arrows");
            string s;
            if (post != null)
                s = arrows > 0
                    ? $"{post.name} looses a volley at landing raiders and works the tower gun"
                    : $"{post.name} works the tower gun · no arrows to loose, the fletcher makes them";
            else
                s = arrows > 0
                    ? "A lookout looses a volley at raiders and works the tower gun"
                    : "A lookout works the tower gun · the fletcher makes arrows for a volley";
            WatchTiles.Set(quiverS, s);
            quiverCard.EnableInClassList("lk-card--warn", post == null);
            quiverCard.EnableInClassList("lk-card--good", post != null);

            tiles.Refresh(l);
            FillMain(l, towers, manned);
        }

        // --- the thumb row ---------------------------------------------------------------

        enum MainKind { Close, Post, Relieve, Build }
        MainKind mainKind;
        string mainWho;

        void FillMain(OutpostLedger l, int towers, int manned)
        {
            var free = WatchTiles.FirstFree(l);
            var post = SheetBits.Lookout(l);
            string text;
            if (towers <= 0) { mainKind = MainKind.Build; mainWho = null; text = "Build a tower"; }
            else if (free != null && (manned < towers || post != null))
            {
                mainKind = MainKind.Post; mainWho = free.name;
                text = "Post " + free.name;
            }
            else if (post != null && free == null)
            {
                mainKind = MainKind.Relieve; mainWho = post.name;
                text = "Relieve " + post.name;
            }
            else { mainKind = MainKind.Close; mainWho = null; text = "Close"; }
            if (mainBtn.text != text) mainBtn.text = text;
            mainBtn.EnableInClassList("lk-act--pri", mainKind == MainKind.Post || mainKind == MainKind.Build);
            showBtn.SetEnabled(towers > 0);
        }

        void Main()
        {
            var l = L;
            if (l == null) return;
            switch (mainKind)
            {
                case MainKind.Post:
                {
                    var h = outpost.HandNamed(mainWho);
                    if (h != null) tiles.Post(h);
                    break;
                }
                case MainKind.Relieve:
                {
                    var h = outpost.HandNamed(mainWho);
                    if (h != null) tiles.Relieve(h);
                    break;
                }
                case MainKind.Build:
                    Sheets.Open(CampAlerts.BuildList(outpost, BuildPlans.Watchtower.id));
                    return;
                default:
                    Sheets.Close();
                    return;
            }
            Refresh();
        }

        /// Swing the camera onto the tower (the next one, each press, when
        /// the camp has several).
        void ShowTower()
        {
            var list = WatchTiles.TowersOf(outpost);
            if (list.Count == 0) return;
            Building b;
            if (tower != null && towerCursor == 0) b = tower;
            else b = list[towerCursor % list.Count];
            towerCursor++;
            WatchTiles.LookAt(b);
        }
    }
}
