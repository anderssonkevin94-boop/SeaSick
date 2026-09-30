using System.Collections.Generic;
using SeaSick.Crew;
using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The Ship sheet, slimmed (2026-09-30, island UI phase 4).**
    ///
    /// It was the Manifest: pills, a hold card (Load all, Deck cargo), an
    /// "Ashore" goods grid and the crew. Rule 3 ("one home per topic") gave
    /// every good and every cargo control to the Backpack, so this is now:
    ///
    /// 1. **header** -- ship glyph, where she lies, a pill (at the pier /
    ///    at anchor, "· home" at her home berth);
    /// 2. **pills** -- Shipyard (its blocker in the pill itself: there is no
    ///    hover on a phone), Make home berth (tap twice), Repair hull when
    ///    there is something to mend;
    /// 3. **one link row** -- "Cargo · 12/34 ->", which opens the Backpack on
    ///    the ship side (`BackpackSheet`); dimmed while no camp lies
    ///    alongside;
    /// 4. **Crew** -- villager tiles, aboard first (blue border): tap an
    ///    aboard one to put them ashore (`Outpost.Station`), an ashore one to
    ///    bring them aboard (`Outpost.Recall`);
    /// 5. **thumb row** -- All ashore · **Cast off**.
    ///
    /// A grid longer than its six tiles pages: its last tile turns "More".
    /// (The old hold line glued "Hold 0 / 34" to "Room for 34" with no gap;
    /// that card is gone, and the Backpack's ship head is one string.)
    ///
    /// **At sea too (Kevin, 2026-09-30, island UI phase 6).** The sea top
    /// bar's aboard count and the hull chip open this under way. Opened at
    /// sea it stays valid while she sails (and closes when she stops, as the
    /// lying-somewhere sheet closes when she casts off); the header says "at
    /// sea", Cast off hides, and two pills join: **Home** (tap, "sure?", tap
    /// = `AnchorController.BerthAtHome`, the retired IMGUI `HomeTab`, its
    /// refusal shown on the pill) and **Ledger** (the sea ledger drawer,
    /// whose rail button went with the IMGUI `PauseChip`). The cargo link
    /// opens the Backpack's ship-only page.
    public class ShipSheet : ISheetFramed
    {
        /// Opened under way (see the class notes); fixed for the sheet's life.
        readonly bool atSea;

        public ShipSheet()
        {
            var a = SheetBits.Anchor;
            atSea = a != null && a.CurrentState != AnchorController.State.Anchored
                    && a.CurrentState != AnchorController.State.Ashore;
        }

        public string Title => "Ship";
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
                if (a == null) return false;
                bool lying = a.CurrentState == AnchorController.State.Anchored
                             || a.CurrentState == AnchorController.State.Ashore;
                return atSea ? !lying && !MidnightLandHud.Active : lying;
            }
        }

        // --- header -------------------------------------------------------------

        WatchTiles.Head head;

        public VisualElement BuildHeader()
        {
            head = CardKit.Head("ship", "Ship");
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
            if (atSea)
            {
                head.SetSub("at sea");
                head.SetPill("under way", StationPage.PillWait);
            }
            else if (dock != null)
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
        Button yardPill, homePill, repairPill, seaHomePill, ledgerPill;
        Button castOffBtn;
        Button cargoLink;
        Label cargoLinkT, cargoLinkArrow;
        Paged crew;
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
            seaHomePill = ledgerPill = null;
            // At sea, and off an island with no camp (where the IMGUI Home
            // tab also stood): there is no camp here to hold these.
            if (atSea || !Sheets.SuppressLegacy)
            {
                seaHomePill = CardKit.Pill(pills, "Home", SeaHomePressed, "ice");
                ledgerPill = CardKit.Pill(pills, "Ledger", OpenLedger, "ice");
            }
            col.Add(pills);

            // --- the one cargo link: goods live in the Backpack
            cargoLink = new Button(OpenCargo);
            cargoLink.AddToClassList("st-btn");
            cargoLink.style.flexDirection = FlexDirection.Row;
            cargoLink.style.alignItems = Align.Center;
            cargoLink.style.justifyContent = Justify.SpaceBetween;
            cargoLink.style.minHeight = 48f;
            cargoLink.style.marginTop = 8f;
            cargoLink.style.paddingLeft = 14f;
            cargoLink.style.paddingRight = 14f;
            cargoLinkT = new Label { pickingMode = PickingMode.Ignore };
            cargoLinkT.style.whiteSpace = WhiteSpace.NoWrap;
            cargoLinkArrow = new Label("→") { pickingMode = PickingMode.Ignore };
            cargoLink.text = "";
            cargoLink.Add(cargoLinkT);
            cargoLink.Add(cargoLinkArrow);
            col.Add(cargoLink);

            // --- crew
            CardKit.Eye(col, "CREW", "tap to move aboard / ashore");
            crew = new Paged(CardKit.Grid(col), true, TapCrew, null);

            // --- thumb row
            var acts = CardKit.Acts(root);
            ashoreBtn = CardKit.Act(acts, "All ashore", EveryoneAshore);
            castOffBtn = CardKit.Act(acts, "Cast off", CastOff, 1);
            if (atSea) WatchTiles.Show(castOffBtn, false);   // she is already under way

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
            FillCargoLink(Voyage, camp);
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
            FillSeaPills(a);
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
            if (atSea && hull != null && pct < 99)
            {
                // Repairs run only while she lies stopped (`AnchorController`
                // mends at anchor or ashore): under way the pill says so.
                WatchTiles.Show(repairPill, true);
                string st = timber > 0 ? $"Hull {pct}% · mend at anchor" : $"Hull {pct}% · no timber aboard";
                if (repairPill.text != st) repairPill.text = st;
                repairPill.SetEnabled(false);
                CardKit.PillTone(repairPill, "wait");
                return;
            }
            WatchTiles.Show(repairPill, show);
            if (show)
            {
                string rt = on ? $"Stop repairs · {pct}%" : $"Repair hull · {pct}%";
                if (repairPill.text != rt) repairPill.text = rt;
                CardKit.PillTone(repairPill, on ? "good" : "wait");
            }
        }

        // --- at sea: Home and Ledger (2026-09-30, phase 6) ------------------

        float seaHomeArmedUntil = -99f, seaHomeRefusedUntil = -99f;
        string seaHomeRefusal;

        void FillSeaPills(AnchorController a)
        {
            if (seaHomePill == null) return;
            // Nothing to do at her own pier, or before there is a home.
            bool can = a != null && !a.AtHomeDock && SeaSick.World.Dock.Home != null;
            bool refused = Time.unscaledTime < seaHomeRefusedUntil;
            WatchTiles.Show(seaHomePill, can || refused);
            string t = refused ? "Home · " + seaHomeRefusal
                : Time.unscaledTime < seaHomeArmedUntil ? "Tap again · sail home?" : "Home";
            if (seaHomePill.text != t) seaHomePill.text = t;
            CardKit.PillTone(seaHomePill, refused ? "wait" : "ice");
            if (ledgerPill != null) WatchTiles.Show(ledgerPill, SeaLedger.Available);
        }

        /// The retired IMGUI HomeTab's two taps: the first arms it, the
        /// second calls `BerthAtHome` (which may refuse: crew ashore).
        void SeaHomePressed()
        {
            var a = Anchor;
            if (a == null) return;
            if (Time.unscaledTime >= seaHomeArmedUntil)
            {
                seaHomeArmedUntil = Time.unscaledTime + 3.5f;
                seaHomeRefusedUntil = -99f;
                FillPills();
                return;
            }
            seaHomeArmedUntil = -99f;
            if (!a.BerthAtHome(out string why))
            {
                seaHomeRefusal = string.IsNullOrEmpty(why) ? "not now" : why;
                seaHomeRefusedUntil = Time.unscaledTime + 3f;
                FillPills();
                return;
            }
            Sheets.Close();
        }

        static void OpenLedger()
        {
            Sheets.Close();
            if (!SeaLedger.IsOpen) SeaLedger.Toggle();
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

        // --- the cargo link -------------------------------------------------------------

        /// "Cargo · 12/34 ->". The count is the hold's held / limit (the
        /// deck-cargo limit when it is on); the whole row opens the Backpack
        /// while a camp lies alongside, and is dimmed (a toast says why) otherwise.
        void FillCargoLink(VoyageManager v, Outpost camp)
        {
            if (cargoLink == null) return;
            int limit = v == null ? 0 : v.TakeDeckCargo ? v.MaxHold : v.HoldCapacity;
            string t = v == null ? "Cargo · no hold" : "Cargo · " + v.TotalHeld + "/" + limit;
            if (cargoLinkT.text != t) cargoLinkT.text = t;
            // At sea the hold alone opens (the Backpack's ship-only page).
            bool open = atSea || (camp != null && (camp.HasCamp || camp.Building) && camp.Ledger != null);
            cargoLink.style.opacity = open ? 1f : 0.6f;
            WatchTiles.Show(cargoLinkArrow, open);
        }

        void OpenCargo()
        {
            var camp = Camp;
            if (atSea) { Sheets.Open(new BackpackSheet(null, true)); return; }
            if (camp == null || !(camp.HasCamp || camp.Building)) { ShowToast("Moor at a camp to move goods"); return; }
            Sheets.Open(new BackpackSheet(camp, true));
        }

        // --- crew --------------------------------------------------------------------------

        readonly List<string> ids = new List<string>();

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
            }, "Sailing alone: slow. Hands make her faster."); // 2026-09-30: the captain alone can sail

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
