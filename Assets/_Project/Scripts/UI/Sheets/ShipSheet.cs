using System.Collections.Generic;
using SeaSick.Crew;
using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The Manifest, on one page (2026-09-27, menu rework #7).**
    ///
    /// It was three tabs (hold / cargo / crew, each paging itself) under four
    /// equal buttons, and `FireSheet` embedded a second copy of all three.
    /// Now it is one Midnight `.st` card, as Kevin approved in the mockup:
    ///
    /// 1. **header** -- ship glyph, where she lies, a pill (at the pier /
    ///    at anchor, "· home" at her home berth);
    /// 2. **pills** -- Shipyard (its blocker in the pill itself: there is no
    ///    hover on a phone), Make home berth (tap twice), Repair hull when
    ///    there is something to mend;
    /// 3. **the hold card** -- "Hold n / cap", the bar, what loading is
    ///    doing, and two pills: Load all (Stop while loading) · Deck cargo;
    /// 4. **Ashore** -- the camp's goods as item tiles: tap = load that kind
    ///    (`CampLoading.BeginOne`), hold = cycle what stays ashore
    ///    (all sails / half / none, `CampLoading.SetStopAt`);
    /// 5. **Crew** -- villager tiles, aboard first (blue border): tap an
    ///    aboard one to put them ashore (`Outpost.Station`), an ashore one to
    ///    bring them aboard (`Outpost.Recall`);
    /// 6. **thumb row** -- All ashore · **Cast off**.
    ///
    /// A grid longer than its six tiles pages: its last tile turns "More".
    public class ShipSheet : ISheetFramed
    {
        public string Title => "Manifest";
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public Color Accent => SheetTheme.Sea;
        public bool WantsTallSheet => true;
        public VisualElement BuildActions() => null;

        AnchorController Anchor => SheetBits.Anchor;
        VoyageManager Voyage => SheetBits.Voyage;
        ShipHold Hold => SheetBits.Hold;
        Outpost Camp => SheetBits.CampAlongside();

        public Vector3 AnchorWorld
        {
            get
            {
                var a = Anchor;
                return a != null ? a.transform.position : Vector3.zero;
            }
        }

        /// She has to still be lying somewhere. Underway there is no manifest
        /// to argue with -- the cargo is decided and you live with it.
        public bool StillValid
        {
            get
            {
                var a = Anchor;
                return a != null
                    && (a.CurrentState == AnchorController.State.Anchored
                        || a.CurrentState == AnchorController.State.Ashore);
            }
        }

        // --- header -------------------------------------------------------------

        WatchTiles.Head head;

        public VisualElement BuildHeader()
        {
            head = CardKit.Head("ship", "Manifest");
            FillHeader();
            return head.Root;
        }

        void FillHeader()
        {
            if (head == null) return;
            var a = Anchor;
            var dock = a != null ? a.CurrentDock : null;
            var camp = Camp;
            string isle = camp != null ? StationPage.Cap(StationPage.IslandName(camp)) : "";
            if (dock != null)
            {
                head.SetSub(isle.Length > 0 ? isle + " pier" : "a pier");
                head.SetPill(dock.IsHome ? "home berth" : "at the pier", StationPage.PillGood);
            }
            else
            {
                head.SetSub(isle.Length > 0 ? "off " + isle : "at sea");
                head.SetPill("at anchor", StationPage.PillWait);
            }
        }

        // --- the page -------------------------------------------------------------

        VisualElement pills;
        Button yardPill, homePill, repairPill;
        Label holdT, holdS;
        CardKit.Bar holdBar;
        Button loadPill, deckPill;
        Paged cargo, crew;
        Button ashoreBtn;
        VisualElement root, toast;
        Label toastText;
        IVisualElementScheduledItem toastHide;

        public VisualElement Build()
        {
            root = CardKit.Page(out var col);

            // --- secondary pills
            pills = WatchTiles.Box("ck-pills");
            pills.pickingMode = PickingMode.Position;
            pills.style.marginTop = 0;
            yardPill = null;
            if (SeaSick.Ship.Modular.ShipyardService.Player != null)
                yardPill = CardKit.Pill(pills, "Shipyard", SeaSick.UI.ModularYard.ShipyardLiveBridge.Open, "ice");
            homePill = CardKit.Pill(pills, "Make home berth", HomeBerthPressed, "ice");
            repairPill = CardKit.Pill(pills, "Repair hull", () => { SheetBits.ToggleRepair(Anchor); Refresh(); });
            col.Add(pills);

            // --- the hold
            var card = StationPage.Card();
            card.AddToClassList("ck-card");
            holdT = StationPage.Text("", "hs-now-t");
            holdS = StationPage.Text("", "hs-now-s");
            card.Add(holdT);
            holdBar = new CardKit.Bar(card);
            card.Add(holdS);
            var holdPills = WatchTiles.Box("ck-pills");
            holdPills.pickingMode = PickingMode.Position;
            loadPill = CardKit.Pill(holdPills, "Load all", LoadPressed, "ice");
            deckPill = CardKit.Pill(holdPills, "Deck cargo", ToggleDeck);
            card.Add(holdPills);
            col.Add(card);

            // --- ashore
            CardKit.Eye(col, "ASHORE", "tap to load · hold: keep some");
            cargo = new Paged(CardKit.Grid(col), false, TapCargo, HoldCargo);

            // --- crew
            CardKit.Eye(col, "CREW", "tap to move aboard / ashore");
            crew = new Paged(CardKit.Grid(col), true, TapCrew, null);

            // --- thumb row
            var acts = CardKit.Acts(root);
            ashoreBtn = CardKit.Act(acts, "All ashore", EveryoneAshore);
            CardKit.Act(acts, "Cast off", CastOff, 1);

            // --- toast
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

        // --- refresh ------------------------------------------------------------------

        public void Refresh()
        {
            var camp = Camp;
            if (camp != null) camp.CatchUp();
            FillHeader();
            if (root == null) return;
            FillPills();
            FillHold(Voyage, camp);
            FillCargo(camp);
            FillCrew(camp);
        }

        // --- pills --------------------------------------------------------------------

        float homeArmedUntil = -99f, homeFeedbackUntil = -99f;
        string homeFeedback;

        void FillPills()
        {
            if (yardPill != null)
            {
                var yard = SeaSick.Ship.Modular.ShipyardService.Player;
                var blockers = yard != null ? yard.RefitBlockers() : null;
                bool ok = blockers != null && blockers.Count == 0;
                string t = ok ? "Shipyard" : "Shipyard · " + (blockers != null && blockers.Count > 0 ? blockers[0] : "unavailable");
                if (yardPill.text != t) yardPill.text = t;
                yardPill.SetEnabled(ok);
                CardKit.PillTone(yardPill, ok ? "ice" : "wait");
            }

            var a = Anchor;
            bool canHome = CanMakeHome(a);
            bool feedback = Time.unscaledTime < homeFeedbackUntil;
            WatchTiles.Show(homePill, canHome || feedback);
            string ht = feedback ? homeFeedback
                : Time.unscaledTime < homeArmedUntil ? "Tap again · make this home?"
                : SeaSick.World.Dock.Home == null ? "Make this island home" : "Move home here";
            if (homePill.text != ht) homePill.text = ht;

            // The hull: a pill only when there is something to press.
            var hull = a != null ? a.GetComponent<HullIntegrity>() : null;
            var v = Voyage;
            bool on = SheetBits.Repairing(a);
            int pct = hull != null ? Mathf.RoundToInt(hull.Integrity01 * 100f) : 100;
            int timber = v != null ? v.AmountOf(Res.Timber) : 0;
            bool show = hull != null && (on || (hull.NeedsRepair && pct < 99 && timber > 0));
            WatchTiles.Show(repairPill, show);
            if (show)
            {
                string rt = on ? $"Stop repairs · {pct}%" : $"Repair hull · {pct}%";
                if (repairPill.text != rt) repairPill.text = rt;
                CardKit.PillTone(repairPill, on ? "good" : "wait");
            }
        }

        /// **Can this island be made home?** (Kevin, 2026-09-29: "you choose
        /// yourself which island to settle and once you've built a campfire
        /// and a pier/dock you can make it into your home island".) Tied up
        /// at a pier (every dock is a player-built pier now -- there is no
        /// harbour), on an island whose camp has its campfire, and not
        /// already home. Home can move later by the same rule.
        static bool CanMakeHome(AnchorController a)
        {
            if (a == null || a.CurrentDock == null || a.CurrentDock.IsHome) return false;
            var isle = SeaSick.World.Island.Nearest(a.CurrentDock.Berth);
            var camp = isle != null ? SeaSick.World.Outpost.Of(isle) : null;
            return camp != null && camp.HasCamp;
        }

        /// First press arms it, second confirms: it moves where every refit,
        /// every voyage and the stores' home pile are.
        void HomeBerthPressed()
        {
            var a = Anchor;
            if (!CanMakeHome(a)) return;
            if (Time.unscaledTime >= homeArmedUntil) { homeArmedUntil = Time.unscaledTime + 3.5f; FillPills(); return; }
            homeArmedUntil = -99f;
            var chosen = a.CurrentDock;
            SeaSick.World.Dock.SetHome(chosen);
            var isle = SeaSick.World.Island.Nearest(chosen.Berth);
            // The voyage banks its homecoming into home's pile; a camp that has
            // never had anything set down on it has none yet.
            SeaSick.World.Stockpile.EnsureOn(isle);
            string label = isle != null ? isle.name : "this island";
            SeaSick.Save.SaveGame.Autosave("home moved to " + label);
            homeFeedback = "Home: " + label;
            homeFeedbackUntil = Time.unscaledTime + 3f;
            FillPills();
            FillHeader();
        }

        // --- the hold -------------------------------------------------------------------

        void FillHold(VoyageManager v, Outpost camp)
        {
            if (v == null) { WatchTiles.Set(holdT, "No hold"); return; }
            int room = CampLoading.RoomAboard(v);
            int limit = v.TakeDeckCargo ? v.MaxHold : v.HoldCapacity;
            WatchTiles.Set(holdT, $"Hold {v.TotalHeld} / {limit}");
            holdBar.Set(limit > 0 ? (float)v.TotalHeld / limit : 0f, v.Overloaded ? CardKit.Ember : CardKit.Ice);
            WatchTiles.Set(holdS, CampLoading.Busy
                ? $"Loading · {CampLoading.Moved} aboard, room for {room}"
                : room > 0
                    ? $"Room for {room} · Load all takes the best first"
                    : v.TakeDeckCargo
                        ? "She is stuffed · nothing more will fit"
                        : "The hold is at her line · deck cargo takes more");

            bool alongside = camp != null && CampLoading.Alongside(camp);
            string lt = CampLoading.Busy ? "Stop loading" : alongside ? "Load all" : "Moor to load";
            if (loadPill.text != lt) loadPill.text = lt;
            loadPill.SetEnabled(CampLoading.Busy || (alongside && room > 0));
            CardKit.PillTone(loadPill, CampLoading.Busy ? "bad" : "ice");

            string dt = v.TakeDeckCargo ? $"Deck cargo on · {v.MaxHold}" : "Deck cargo off";
            if (deckPill.text != dt) deckPill.text = dt;
            CardKit.PillTone(deckPill, v.TakeDeckCargo ? "wait" : null);
        }

        void LoadPressed()
        {
            if (CampLoading.Busy) { CampLoading.Cancel(); Refresh(); return; }
            var camp = Camp;
            if (camp == null) return;
            if (!CampLoading.Begin(camp, Voyage, Hold)) ShowToast("Nothing to load");
            Refresh();
        }

        void ToggleDeck()
        {
            var v = Voyage;
            if (v == null) return;
            v.TakeDeckCargo = !v.TakeDeckCargo;
            if (v.TakeDeckCargo) ShowToast("She'll swim low and take water");
            Refresh();
        }

        // --- ashore: the camp's goods ------------------------------------------------------

        readonly List<string> ids = new List<string>();

        void FillCargo(Outpost camp)
        {
            var l = camp != null ? camp.Ledger : null;
            ids.Clear();
            if (l != null)
                foreach (var s in l.stores)
                    if (s != null && s.whole > 0 && !string.IsNullOrEmpty(s.resource)) ids.Add(s.resource);
            cargo.Fill(ids, (t, res) =>
            {
                int have = l.CountOf(res);
                int cap = CampLoading.StopAt(res);
                string keep = cap < 0 ? "Load" : cap <= 0 ? "keep all" : $"keep {cap}";
                t.SetItem(res);
                t.Set(StationPage.Cap(CampLoading.Lower(res)), $"{have} · {keep}");
                t.State(false);
                t.Root.EnableInClassList("hs-tile--out", cap == 0);
            }, camp == null ? "No camp alongside" : "The camp is holding nothing");
        }

        void TapCargo(string res)
        {
            var camp = Camp;
            if (camp == null) return;
            if (!CampLoading.Alongside(camp)) { ShowToast("Moor alongside to load"); return; }
            if (CampLoading.StopAt(res) == 0) { ShowToast($"All {CampLoading.Lower(res)} stays ashore · hold to change"); return; }
            if (!CampLoading.BeginOne(camp, Voyage, Hold, res)) ShowToast("No room aboard");
            Refresh();
        }

        /// Long press: what SAILS -- all → half (keep half of what is piled
        /// now) → none (keep the lot) → all.
        void HoldCargo(string res)
        {
            var l = Camp != null ? Camp.Ledger : null;
            if (l == null) return;
            int ashore = l.CountOf(res);
            int cap = CampLoading.StopAt(res);
            string name = CampLoading.Lower(res);
            if (cap < 0) { CampLoading.SetStopAt(res, ashore / 2); ShowToast($"Half the {name} sails, half stays"); }
            else if (cap > 0) { CampLoading.SetStopAt(res, 0); ShowToast($"All {name} stays ashore"); }
            else { CampLoading.SetStopAt(res, -1); ShowToast($"All {name} can sail"); }
            Refresh();
        }

        // --- crew --------------------------------------------------------------------------

        void FillCrew(Outpost camp)
        {
            var roster = SheetBits.Roster;
            var l = camp != null ? camp.Ledger : null;
            ids.Clear();
            int aboard = 0;
            if (roster != null)
                foreach (var a in roster.All)
                    if (a != null && a.IsAboard) { ids.Add("A:" + a.DisplayName); aboard++; }
            if (l != null)
                foreach (var h in l.hands)
                    if (h != null && h.name != null) ids.Add("H:" + h.name);
            bool camped = camp != null && (camp.HasCamp || camp.Building);
            crew.Fill(ids, (t, id) =>
            {
                bool isAboard = id.StartsWith("A:");
                string who = id.Substring(2);
                t.SetPerson(who);
                string sub = "aboard";
                if (!isAboard)
                {
                    var h = l != null ? camp.HandNamed(who) : null;
                    sub = h != null ? WatchTiles.JobWord(h) : "ashore";
                }
                t.Set(who, sub);
                t.State(isAboard);
                t.Root.EnableInClassList("hs-tile--out", isAboard ? !camped : camp == null || camp.BodyNamed(who) == null);
            }, "Nobody aboard or ashore");

            ashoreBtn.SetEnabled(aboard > 0 && camped);
        }

        void TapCrew(string id)
        {
            var camp = Camp;
            if (camp == null) { ShowToast("No camp here"); return; }
            string who = id.Substring(2);
            var roster = SheetBits.Roster;
            if (id.StartsWith("A:"))
            {
                if (!(camp.HasCamp || camp.Building)) { ShowToast("Make camp first"); return; }
                CrewAgent hand = null;
                if (roster != null)
                    foreach (var a in roster.All)
                        if (a != null && a.IsAboard && a.DisplayName == who) { hand = a; break; }
                if (hand != null && camp.Station(hand)) { roster.Refresh(); ShowToast(who + " goes ashore"); }
            }
            else
            {
                var anchor = Anchor;
                var body = camp.BodyNamed(who);
                if (anchor == null || body == null) { ShowToast(who + " can't reach her from here"); return; }
                if (camp.Recall(body, anchor.transform)) { roster?.Refresh(); ShowToast(who + " comes aboard"); }
            }
            Refresh();
        }

        /// Station everybody who is aboard, one `Outpost.Station` at a time.
        void EveryoneAshore()
        {
            var camp = Camp;
            var roster = SheetBits.Roster;
            if (camp == null || roster == null) return;
            bool any = false;
            foreach (var a in roster.All)
            {
                if (a == null || !a.IsAboard) continue;
                if (camp.Station(a)) any = true;
            }
            if (any) roster.Refresh();
            Refresh();
        }

        /// `AnchorController.CastOff` -- what the space bar and the anchor
        /// prompt press.
        void CastOff()
        {
            var a = Anchor;
            if (a == null) return;
            a.CastOff();
            Sheets.Close();
        }

        // --- a six-tile grid that pages ------------------------------------------------------

        /// Six tiles built once and re-filled (a rebuilt button loses the tap
        /// it is in the middle of). More than six ids: five per page and the
        /// last tile is "More", which turns the page.
        sealed class Paged
        {
            const int Slots = 6;
            readonly CardKit.Tile[] tiles = new CardKit.Tile[Slots];
            readonly System.Action<string> tap, hold;
            readonly VisualElement note;
            readonly Label noteT;
            int page;
            bool longFired;

            public Paged(VisualElement grid, bool people, System.Action<string> tap, System.Action<string> hold)
            {
                this.tap = tap;
                this.hold = hold;
                for (int i = 0; i < Slots; i++)
                {
                    var t = new CardKit.Tile(Press, people).Col3(i);
                    tiles[i] = t;
                    grid.Add(t.Root);
                    if (hold != null) LongPress(t);
                }
                note = StationPage.Card();
                note.AddToClassList("lk-note");
                noteT = StationPage.Text("", "lk-note-t");
                note.Add(noteT);
                note.style.display = DisplayStyle.None;
                grid.parent?.Insert(grid.parent.IndexOf(grid) + 1, note);
                if (grid.parent == null) grid.RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    if (note.parent == null && grid.parent != null) grid.parent.Insert(grid.parent.IndexOf(grid) + 1, note);
                });
            }

            void LongPress(CardKit.Tile t)
            {
                IVisualElementScheduledItem timer = null;
                t.Root.RegisterCallback<PointerDownEvent>(_ =>
                {
                    longFired = false;
                    timer?.Pause();
                    timer = t.Root.schedule.Execute(() =>
                    {
                        if (string.IsNullOrEmpty(t.Key) || t.Key == More) return;
                        longFired = true;
                        hold?.Invoke(t.Key);
                    });
                    timer.ExecuteLater(550);
                }, TrickleDown.TrickleDown);
                t.Root.RegisterCallback<PointerUpEvent>(_ => timer?.Pause(), TrickleDown.TrickleDown);
                t.Root.RegisterCallback<PointerLeaveEvent>(_ => timer?.Pause());
            }

            const string More = "\u0001more";

            void Press(CardKit.Tile t)
            {
                if (longFired) { longFired = false; return; }
                if (t.Key == More) { page++; return; }
                if (!string.IsNullOrEmpty(t.Key)) tap?.Invoke(t.Key);
            }

            public void Fill(List<string> ids, System.Action<CardKit.Tile, string> fill, string empty)
            {
                int n = ids.Count;
                bool paged = n > Slots;
                int per = paged ? Slots - 1 : Slots;
                int pages = Mathf.Max(1, Mathf.CeilToInt(n / (float)per));
                if (page >= pages) page = 0;
                int from = page * per;
                for (int i = 0; i < Slots; i++)
                {
                    var t = tiles[i];
                    if (paged && i == Slots - 1)
                    {
                        t.Key = More;
                        t.Set("More", $"page {page + 1} / {pages}");
                        t.State(false);
                        t.Root.EnableInClassList("hs-tile--out", false);
                        if (t.Initial != null) WatchTiles.Set(t.Initial, "›"); else t.SetItem(null);
                        WatchTiles.Show(t.Root, true);
                        continue;
                    }
                    int k = from + i;
                    if (k >= n) { t.Key = null; WatchTiles.Show(t.Root, false); continue; }
                    t.Key = ids[k];
                    WatchTiles.Show(t.Root, true);
                    fill(t, ids[k]);
                }
                WatchTiles.Set(noteT, n == 0 ? (empty ?? "Nothing here") : "");
                WatchTiles.Show(note, n == 0);
            }
        }
    }
}
