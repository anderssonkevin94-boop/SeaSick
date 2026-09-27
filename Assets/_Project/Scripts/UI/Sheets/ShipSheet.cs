using System.Collections.Generic;
using SeaSick.Crew;
using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The manifest: what she is carrying, what is going aboard, and who
    /// is sailing.**
    ///
    /// The fourth step of the loop (GDD §6) had one button until 2026-09-20
    /// and three by 2026-09-22, spread across the old camp bar, its store
    /// rows and the anchor prompt's deck-cargo toggle. They are all about the
    /// same decision -- how much you dare take -- so they are one sheet,
    /// unfolded beside the hull.
    ///
    /// **Nothing here moves a unit.** `CampLoading` carries, `VoyageManager`
    /// counts, `AnchorController` casts off; this reads them and presses
    /// their buttons.
    public class ShipSheet : ISheetFramed
    {
        public string Title => "Manifest";

        // --- the frame (2026-09-22, "one sheet, tabs") ----------------------
        //
        // Two natural sections and therefore two tabs: what she is CARRYING
        // and who is SAILING. The action row -- everyone ashore, cast off --
        // is pinned under both, because those two are about the ship whatever
        // you happen to be looking at.
        // 2026-09-22, "pages you swipe between": the manifest is three
        // sections, and the two that are LISTS page themselves. A camp
        // holding eight kinds and a crew of ten used to be a scroll; it is
        // now "cargo 1/2", "cargo 2/2", "crew 1/2".
        public const int SecHold = 0;
        public const int SecCargo = 1;
        public const int SecCrew = 2;

        int tab = -1;
        string[] labels = { "hold", "cargo", "crew" };
        int cargoPages = 1, crewPages = 1;
        int cargoPerPage = 99, crewPerPage = 99;
        int cargoPart, crewPart;
        long shipPlanKey = long.MinValue;

        public int Tab { get { Plan(); return tab; } }
        public void SetTab(int index) { tab = index; }
        public string[] TabLabels { get { Plan(); return labels; } }
        public Color Accent => SheetTheme.Sea;

        /// **How many rows each list section has**, so the camp sheet can
        /// page the manifest inside its own frame with the same arithmetic
        /// this sheet uses standing alone.
        public int CargoCount
        {
            get
            {
                var l = Camp != null ? Camp.Ledger : null;
                if (l == null) return 0;
                int n = 0;
                foreach (var st in l.stores)
                    if (st != null && st.whole > 0 && !string.IsNullOrEmpty(st.resource)) n++;
                return n;
            }
        }

        public int CrewRows
        {
            get
            {
                var roster = SheetBits.Roster;
                int aboard = 0;
                if (roster != null)
                    foreach (var a in roster.All) if (a != null && a.IsAboard) aboard++;
                var l = Camp != null ? Camp.Ledger : null;
                int ashore = l != null ? l.hands.Count : 0;
                return Mathf.Max(aboard, ashore);
            }
        }

        /// Cut the two list sections into pages that fit the band, and name
        /// the pages. Keyed, because the host asks four times a second.
        void Plan()
        {
            int cargo = CargoCount, crew = CrewRows;
            float reasonPx = ShipyardReasonPx();
            long key = cargo * 1000003L + crew * 131L
                       + Mathf.RoundToInt(SheetHost.BandHeight) * 31L + Mathf.RoundToInt(reasonPx) * 7L;
            if (key == shipPlanKey) return;
            shipPlanKey = key;

            cargoPerPage = SheetHost.RowsThatFit(SheetKit.RowPx, SheetKit.EyebrowPx + reasonPx);
            crewPerPage = SheetHost.RowsThatFit(SheetKit.RowPx, SheetKit.EyebrowPx + reasonPx);
            cargoPages = SheetKit.PageCount(Mathf.Max(1, cargo), cargoPerPage);
            crewPages = SheetKit.PageCount(Mathf.Max(1, crew), crewPerPage);

            int n = 1 + cargoPages + crewPages;
            if (labels.Length != n) labels = new string[n];
            labels[0] = "hold";
            for (int i = 0; i < cargoPages; i++)
                labels[1 + i] = SheetKit.PageLabel("cargo", i, cargoPages);
            for (int i = 0; i < crewPages; i++)
                labels[1 + cargoPages + i] = SheetKit.PageLabel("crew", i, crewPages);
            if (tab >= n) tab = n - 1;
        }

        /// Height the Shipyard blocker line takes above the action row
        /// (`BuildActions`), taken out of the page band so rows are never
        /// planned under it. ~60 characters per 11 px line at sheet width.
        static float ShipyardReasonPx()
        {
            var yard = SeaSick.Ship.Modular.ShipyardService.Player;
            if (yard == null) return 0f;
            var blockers = yard.RefitBlockers();
            if (blockers.Count == 0) return 0f;
            int lines = 0;
            foreach (var b in blockers) lines += Mathf.Max(1, Mathf.CeilToInt((b?.Length ?? 0) / 60f));
            return lines * 15f + 3f;
        }

        /// Which section (and which page of it) index `page` is.
        public void SectionOf(int page, out int section, out int part)
        {
            Plan();
            if (page <= 0) { section = SecHold; part = 0; return; }
            if (page - 1 < cargoPages) { section = SecCargo; part = page - 1; return; }
            section = SecCrew;
            part = Mathf.Clamp(page - 1 - cargoPages, 0, crewPages - 1);
        }

        public int CargoPages { get { Plan(); return cargoPages; } }
        public int CrewPages { get { Plan(); return crewPages; } }
        public int CargoPerPage { get { Plan(); return cargoPerPage; } }
        public int CrewPerPage { get { Plan(); return crewPerPage; } }

        Label headerSub;

        /// **Midnight header, 2026-09-27** (menu rework #7): the same drawn
        /// glyph + title + subtitle + close every other restyled sheet
        /// uses, instead of the old round "⚓" badge with an eyebrow over
        /// the title.
        public VisualElement BuildHeader()
        {
            var icon = new StationPage.Glyph("ship", MidnightLandHud.Ice, "cp-glyph");
            var head = CampPages.IconHeader(Title, icon, out headerSub);
            SetHeaderSub();
            return head;
        }

        void SetHeaderSub()
        {
            if (headerSub == null) return;
            var a = Anchor;
            headerSub.text = a != null && a.CurrentDock != null ? "at the pier" : "at anchor";
        }

        public VisualElement BuildActions()
        {
            everyoneAshore = SheetKit.Btn("Everyone ashore", EveryoneAshore);
            var row = new List<VisualElement> { everyoneAshore };
            shipyardBtn = null; shipyardReason = null;
            if (SeaSick.Ship.Modular.ShipyardService.Player != null)
            {
                everyoneAshore.text = "All ashore";
                // **The button's state is visible BEFORE opening the modal**
                // (2026-09-26 UX pass): enabled only when a refit is
                // actually possible, with every blocker printed underneath
                // it -- not a tooltip, there is no hover on a phone. Text
                // and enabled state are set in `ShipyardBlock` (`Refresh`),
                // the same "build once, re-text on refresh" pattern every
                // other button on this sheet follows.
                shipyardBtn = SheetKit.Btn("Shipyard", SeaSick.UI.ModularYard.ShipyardLiveBridge.Open);
                row.Add(shipyardBtn);
                // The reason used to sit UNDER the button inside the 61 px
                // action row and was cut off by the card's bottom edge
                // (2026-09-26 phone shot). It is now its own full-width line
                // ABOVE the buttons (the action row wraps), and `Plan`
                // reserves its height out of the page band so the page
                // above never runs under it.
                shipyardReason = SheetKit.Text("", false, true, 11f);
                shipyardReason.style.whiteSpace = WhiteSpace.Normal;
                shipyardReason.style.flexGrow = 0f; shipyardReason.style.flexShrink = 0f;
                shipyardReason.style.marginBottom = 3f;
                shipyardReason.style.display = DisplayStyle.None;
            }
            // **Make this my home berth** (2026-09-25, Kevin: "make my home
            // berth the pier I built at island_2"). Only where she is lying
            // at a player's pier that is NOT already home -- her own harbour
            // or her own chosen berth has nothing to offer here. Two-tap
            // arm/confirm, the same idiom `HomeTab` uses for the other
            // control that moves something permanent across the world.
            homeBerthBtn = null;
            var a = Anchor;
            if (a != null && a.CurrentDock != null && !a.CurrentDock.IsHome)
            {
                homeBerthBtn = SheetKit.Btn("Make this my home berth", MakeHomeBerthPressed);
                row.Add(homeBerthBtn);
            }
            row.Add(SheetKit.Btn("Cast off", CastOff, true));
            var actions = SheetKit.Actions(row.ToArray());
            if (shipyardReason == null) return actions;
            // A COLUMN (reason line over the button row), not the row's own
            // flex-wrap: a wrapped row under-reports its height in this UI
            // Toolkit version (the same quirk yard-tiles pins around), which
            // left the buttons hanging off the card's bottom edge.
            var col = new VisualElement();
            col.AddToClassList(SheetTheme.Actions);
            col.style.flexDirection = FlexDirection.Column;
            col.style.alignItems = Align.Stretch;
            col.style.flexWrap = Wrap.NoWrap; // .sheet-actions wraps, which in a column spills sideways
            col.style.flexShrink = 0f;
            col.style.paddingTop = 5f; // the line costs the page as little as it can
            actions.RemoveFromClassList(SheetTheme.Actions);
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.NoWrap;
            actions.style.alignItems = Align.Center;
            actions.style.flexShrink = 0f;
            col.Add(shipyardReason);
            col.Add(actions);
            return col;
        }

        Button homeBerthBtn;
        float homeBerthArmedUntil = -99f;
        string homeBerthFeedback;
        float homeBerthFeedbackUntil = -99f;

        /// First press arms it, second confirms. A one-tap control that
        /// permanently moves where every refit, every voyage and her own
        /// spawn happen is exactly the kind of mis-tap `HomeTab` was written
        /// to guard against, and it lives on the same busy action row.
        void MakeHomeBerthPressed()
        {
            var a = Anchor;
            if (a == null || a.CurrentDock == null) return;
            bool armed = Time.unscaledTime < homeBerthArmedUntil;
            if (!armed) { homeBerthArmedUntil = Time.unscaledTime + 3.5f; return; }
            homeBerthArmedUntil = -99f;
            var chosen = a.CurrentDock;
            SeaSick.World.Dock.SetHome(chosen);
            var isle = SeaSick.World.Island.Nearest(chosen.Berth);
            string label = isle != null ? isle.name + " pier" : "her new pier";
            SeaSick.Save.SaveGame.Autosave("home berth moved to " + label);
            homeBerthFeedback = "Home berth: " + label;
            homeBerthFeedbackUntil = Time.unscaledTime + 3f;
        }

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

        // --- the pieces kept between refreshes ---------------------------------

        Label eyebrowLine;
        Label holdBig;
        VisualElement holdBar;
        Button loadBtn;
        VisualElement stopAtHolder;
        VisualElement deckHolder;
        VisualElement repairHolder;
        VisualElement aboardCol;
        VisualElement ashoreCol;
        Button everyoneAshore;
        Button shipyardBtn;
        Label shipyardReason;

        long holdKey = long.MinValue;
        long stopKey = long.MinValue;
        int deckKey = -99;
        int repairKey = -99;
        long crewKey = long.MinValue;

        /// The body of the live tab. Called again on every tab change, so
        /// it drops the last tab's element references first -- the same rule
        /// `FireSheet` follows, and for the same reason.
        public VisualElement Build()
        {
            Forget();
            SectionOf(Mathf.Max(0, tab), out int section, out int part);
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            root.Add(Section(section, part, cargoPerPage, crewPerPage));
            Refresh();
            return root;
        }

        /// **One section, at one page of it.** The camp sheet builds the same
        /// three sections inside its own frame, so there is one manifest in
        /// the game and two frames that show it rather than two copies.
        public VisualElement Section(int section, int part, int cargoPer, int crewPer)
        {
            cargoPart = part; crewPart = part;
            cargoPerPage = Mathf.Max(1, cargoPer);
            crewPerPage = Mathf.Max(1, crewPer);
            switch (section)
            {
                case SecCargo: return CargoSection();
                case SecCrew: return CrewSection();
                default: return HoldSection();
            }
        }

        /// One section for an embedding frame. The fire's manifest pages go
        /// through here, so `Forget` is done for the caller exactly as it is
        /// in `Build`.
        public VisualElement BuildSection(int section, int part, int cargoPer, int crewPer)
        {
            Forget();
            var e = Section(section, part, cargoPer, crewPer);
            Refresh();
            return e;
        }

        void Forget()
        {
            eyebrowLine = null; holdBig = null; holdBar = null; loadBtn = null;
            stopAtHolder = deckHolder = repairHolder = aboardCol = ashoreCol = null;
            holdKey = stopKey = crewKey = long.MinValue;
            deckKey = repairKey = -99;
        }

        /// What she is carrying, what would go aboard next, what stays
        /// ashore, whether the deck is loaded and whether the hull is sound.
        VisualElement HoldSection()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            eyebrowLine = SheetKit.Text("", false, true, 12f);
            root.Add(eyebrowLine);

            holdBig = SheetKit.Text("0 / 0", true, false, 28f);
            holdBar = SheetBits.Holder();
            root.Add(SheetKit.Row(holdBig, holdBar));

            loadBtn = SheetKit.Btn("Load", LoadPressed, true);
            root.Add(loadBtn);

            root.Add(SheetKit.Rule());
            deckHolder = SheetBits.Holder();
            root.Add(deckHolder);
            repairHolder = SheetBits.Holder();
            root.Add(repairHolder);
            return root;
        }

        /// What stays ashore: one row per kind the camp is holding, paged.
        VisualElement CargoSection()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            root.Add(SheetKit.Eyebrow(cargoPages > 1
                ? $"stop at · {cargoPart + 1} of {cargoPages}"
                : "stop at"));
            stopAtHolder = SheetBits.Holder();
            root.Add(stopAtHolder);
            return root;
        }

        /// Who is aboard and who is ashore, with the arrow that moves them.
        VisualElement CrewSection()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            aboardCol = SheetBits.Holder();
            ashoreCol = SheetBits.Holder();
            root.Add(SheetKit.Row(
                SheetKit.Col(SheetKit.Eyebrow("aboard"), aboardCol),
                SheetKit.Col(SheetKit.Eyebrow("ashore"), ashoreCol)));
            return root;
        }

        public void Refresh()
        {
            var v = Voyage;
            var camp = Camp;
            if (camp != null) camp.CatchUp();

            SetHeaderSub();
            HoldBlock(v, camp);
            StopAtBlock(camp);
            DeckBlock(v);
            RepairBlock();
            CrewBlock(camp);
            HomeBerthBlock();
            ShipyardBlock();
        }

        /// **The Shipyard button's own state, before it is ever tapped**
        /// (2026-09-26). Enabled with a plain label when a refit is
        /// possible; disabled with every blocker listed underneath
        /// otherwise (`ShipyardService.RefitBlockers`, same wording
        /// `CanRefitNow` gives the modal, so this is never a second story).
        /// A blocker is refreshed here rather than baked into `BuildActions`
        /// because the state can change under an open sheet -- she casts
        /// off, a raid starts -- and this runs every frame `Refresh` does.
        void ShipyardBlock()
        {
            if (shipyardBtn == null) return;
            var yard = SeaSick.Ship.Modular.ShipyardService.Player;
            if (yard == null) { shipyardBtn.SetEnabled(false); shipyardReason.text = ""; return; }
            var blockers = yard.RefitBlockers();
            shipyardBtn.SetEnabled(blockers.Count == 0);
            shipyardBtn.text = "Shipyard";
            shipyardReason.text = blockers.Count == 0 ? "" : string.Join("\n", blockers);
            shipyardReason.style.display = blockers.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// Ticks the arm/confirm/feedback text on `homeBerthBtn` in place --
        /// `BuildActions` is not called every frame, only `Refresh` is.
        void HomeBerthBlock()
        {
            if (homeBerthBtn == null) return;
            if (Time.unscaledTime < homeBerthFeedbackUntil)
                homeBerthBtn.text = homeBerthFeedback;
            else if (Time.unscaledTime < homeBerthArmedUntil)
                homeBerthBtn.text = "Confirm — home berth?";
            else
                homeBerthBtn.text = "Make this my home berth";
        }

        // --- the hold ------------------------------------------------------------

        void HoldBlock(VoyageManager v, Outpost camp)
        {
            if (v == null) return;
            int room = CampLoading.RoomAboard(v);
            int limit = v.TakeDeckCargo ? v.MaxHold : v.HoldCapacity;

            long key = ((long)v.TotalHeld * 31 + limit) * 31 + room
                       + (CampLoading.Busy ? 1 : 0) * 7919L
                       + (camp != null && camp.Ledger != null ? camp.Ledger.Total : 0) * 131L;
            if (key == holdKey) return;
            holdKey = key;

            if (holdBig != null) holdBig.text = $"{v.TotalHeld} / {limit}";
            SheetBits.Swap(holdBar, SheetKit.Bar(
                limit > 0 ? Mathf.Clamp01((float)v.TotalHeld / limit) : 0f,
                v.Overloaded ? SheetTheme.Ember : SheetTheme.Sea, 10f));

            if (eyebrowLine != null)
                eyebrowLine.text = CampLoading.Busy
                    ? $"loading — {CampLoading.Moved} aboard, room for {room}"
                    : room > 0
                        ? $"{v.TotalHeld} aboard, room for {room} · best first"
                        : v.TakeDeckCargo
                            ? "she is stuffed — nothing more will fit aboard"
                            : "the hold is at her line — deck cargo takes more";

            if (loadBtn == null) return;
            if (CampLoading.Busy)
            {
                loadBtn.text = "Stop";
                loadBtn.SetEnabled(true);
                return;
            }
            string kind = NextKind(camp, room, out int n);
            loadBtn.text = kind == null
                ? "Nothing to load"
                : $"Load {n} {CampLoading.Lower(kind)}";
            loadBtn.SetEnabled(kind != null && room > 0
                && camp != null && CampLoading.Alongside(camp));
        }

        /// **What the first trip would actually carry**, so the button can
        /// say it. `CampLoading.BestFirst` is the order and `RoomAboard` is
        /// the limit -- the same two facts the coroutine works from, read the
        /// same way, so the label cannot promise what the press will not do.
        static string NextKind(Outpost camp, int room, out int n)
        {
            n = 0;
            var l = camp != null ? camp.Ledger : null;
            if (l == null || room <= 0) return null;
            foreach (var res in CampLoading.BestFirst)
            {
                int have = l.CountOf(res);
                if (have <= 0) continue;
                int cap = CampLoading.StopAt(res);
                // A "stop at" is a floor on what stays ashore, so what the
                // hold may take is everything above it.
                int free = cap < 0 ? have : Mathf.Max(0, have - cap);
                if (free <= 0) continue;
                n = Mathf.Min(free, room);
                return res;
            }
            return null;
        }

        /// The lot, best first -- `CampLoading.Begin`. The
        /// stop-at caps below are what keep it from emptying the island.
        void LoadPressed()
        {
            if (CampLoading.Busy) { CampLoading.Cancel(); Refresh(); return; }
            var camp = Camp;
            if (camp == null) return;
            CampLoading.Begin(camp, Voyage, Hold);
            holdKey = long.MinValue;
            Refresh();
        }

        // --- what stays ashore -----------------------------------------------------

        static readonly string[] StopOptions = { "all", "half", "none" };
        readonly List<string> kinds = new List<string>();

        /// **"all / half / none" is about what SAILS, not what stays.** Kevin's
        /// words for the three; `CampLoading.SetStopAt` takes the floor the
        /// carry must leave behind, so "all" is no floor at all (-1), "half"
        /// leaves half of what is piled now, and "none" leaves the lot.
        void StopAtBlock(Outpost camp)
        {
            var l = camp != null ? camp.Ledger : null;
            kinds.Clear();
            if (l != null)
                foreach (var s in l.stores)
                    if (s != null && s.whole > 0 && !string.IsNullOrEmpty(s.resource))
                        kinds.Add(s.resource);

            long key = kinds.Count * 31L + cargoPart * 1000003L + cargoPerPage;
            foreach (var k in kinds)
                key = key * 31 + k.GetHashCode() + l.CountOf(k) * 7 + CampLoading.StopAt(k) * 131;
            if (key == stopKey) return;
            stopKey = key;

            if (stopAtHolder == null) return;
            stopAtHolder.Clear();
            if (kinds.Count == 0)
            {
                stopAtHolder.Add(SheetKit.Note("The camp is holding nothing"));
                return;
            }
            int from = cargoPart * cargoPerPage;
            int to = Mathf.Min(kinds.Count, from + cargoPerPage);
            for (int ki = from; ki < to; ki++)
            {
                string res = kinds[ki];
                int ashore = l.CountOf(res);
                int cap = CampLoading.StopAt(res);
                int sel = cap < 0 ? 0 : cap <= 0 ? 2 : 1;
                // **An icon tile, not a bare word (2026-09-27, menu rework
                // #7).** The same item art `StoresSheet`'s bank and every
                // station's recipe card already draw from `ItemIconSet`.
                var tile = new VisualElement();
                tile.style.flexDirection = FlexDirection.Row;
                tile.style.alignItems = Align.Center;
                var icon = StationPage.Icon(res, "st-small-icon");
                icon.style.width = 20f; icon.style.height = 20f;
                icon.style.marginRight = 6f;
                tile.Add(icon);
                tile.Add(SheetKit.Text(CampLoading.Lower(res), false, false, 13f));
                stopAtHolder.Add(SheetKit.ListRow(
                    tile,
                    SheetKit.Text(ashore.ToString(), false, true, 13f),
                    SheetKit.Segmented(StopOptions, sel, i =>
                    {
                        int keep = i == 0 ? -1 : i == 1 ? ashore / 2 : 0;
                        CampLoading.SetStopAt(res, keep);
                        stopKey = long.MinValue;
                        holdKey = long.MinValue;
                        Refresh();
                    })));
            }
        }

        // --- deck cargo and the hull -------------------------------------------------

        /// The same flag and the same warning the anchor prompt toggles
        /// (AnchorController.cs:826, text at :823, warning at :830).
        void DeckBlock(VoyageManager v)
        {
            if (v == null) return;
            int key = (v.TakeDeckCargo ? 1 : 0) * 100003 + v.MaxHold * 31 + v.HoldCapacity;
            if (key == deckKey) return;
            deckKey = key;

            var btn = SheetKit.Btn(v.TakeDeckCargo
                ? $"◉  deck cargo — to {v.MaxHold}"
                : $"◎  deck cargo — stop at {v.HoldCapacity}", () =>
            {
                v.TakeDeckCargo = !v.TakeDeckCargo;
                deckKey = -99;
                holdKey = long.MinValue;
                Refresh();
            }, false, !v.TakeDeckCargo);

            if (deckHolder == null) return;
            deckHolder.Clear();
            deckHolder.Add(btn);
            if (v.TakeDeckCargo)
                deckHolder.Add(SheetKit.Note("she'll swim low and take water"));
        }

        /// **The hull: a button only when there is something to press.**
        ///
        /// It drew a button in every state and greyed the ones that could not
        /// be pressed, so a sound hull at 99 % and an empty hold both read as
        /// "repair hull — uses timber" in ghost-grey: the player cannot tell
        /// a thing that is FINE from a thing that is BROKEN and unaffordable.
        /// A disabled control now means one thing only -- "you could do this,
        /// but not this second" -- and everything else is a sentence.
        void RepairBlock()
        {
            var a = Anchor;
            var hull = a != null ? a.GetComponent<HullIntegrity>() : null;
            if (repairHolder == null) return;
            if (hull == null) { repairHolder.Clear(); repairKey = -99; return; }

            var v = Voyage;
            bool on = SheetBits.Repairing(a);
            int pct = Mathf.RoundToInt(hull.Integrity01 * 100f);
            int timber = v != null ? v.AmountOf(Res.Timber) : 0;
            int key = ((pct * 4 + (on ? 2 : 0) + (hull.NeedsRepair ? 1 : 0)) * 3)
                      + (timber > 0 ? 1 : 0);
            if (key == repairKey) return;
            repairKey = key;

            repairHolder.Clear();

            // Nothing to mend. `NeedsRepair` is `integrity < 0.995`, so this
            // is "hull sound" at 100 % and at the 99 % that is a scrape.
            if (!on && (!hull.NeedsRepair || pct >= 99))
            {
                repairHolder.Add(SheetKit.Text($"hull sound — {pct}%", false, true, 12f));
                return;
            }
            // Something to mend and nothing to mend it with: a fact, not a
            // dead button, and it names what she is short of.
            if (!on && timber <= 0)
            {
                repairHolder.Add(SheetKit.Text(
                    $"hull {pct}% — no timber aboard to mend her", false, true, 12f));
                return;
            }
            // Live, both ways round -- the same labels the anchor prompt
            // prints (AnchorController.cs:984).
            repairHolder.Add(SheetKit.Btn(on
                ? $"stop repairs — hull {pct}%"
                : $"repair hull ({pct}%) — uses timber", () =>
            {
                SheetBits.ToggleRepair(Anchor);
                repairKey = -99;
                Refresh();
            }, false, !on));
        }

        // --- who sails ----------------------------------------------------------------

        void CrewBlock(Outpost camp)
        {
            var roster = SheetBits.Roster;
            var l = camp != null ? camp.Ledger : null;

            long key = (l != null ? l.hands.Count : 0) * 1000003L
                       + crewPart * 100003L + crewPerPage * 17L;
            if (roster != null)
                foreach (var a in roster.All)
                    if (a != null && a.IsAboard)
                        key = key * 31 + a.DisplayName.GetHashCode();
            if (l != null)
                foreach (var h in l.hands)
                    if (h != null) key = key * 31 + (h.name != null ? h.name.GetHashCode() : 0);
            if (key == crewKey) return;
            crewKey = key;

            // Aboard: the arrow leaves them here (`Outpost.Station`).
            if (aboardCol == null || ashoreCol == null) return;
            aboardCol.Clear();
            int aboard = 0;
            // **Both columns are cut at the same page.** The page is the
            // band's worth of rows, so a crew of twelve is "crew 1/2" and
            // "crew 2/2" rather than a list running off the bottom of a card
            // that no longer scrolls.
            int fromA = crewPart * crewPerPage, toA = fromA + crewPerPage;
            int seenA = 0;
            if (roster != null)
                foreach (var a in roster.All)
                {
                    if (a == null || !a.IsAboard) continue;
                    var hand = a;
                    aboard++;
                    int at = seenA++;
                    if (at < fromA || at >= toA) continue;
                    var arrow = SheetKit.Btn("→", () => Station(hand), false, true);
                    arrow.SetEnabled(camp != null && (camp.HasCamp || camp.Building));
                    aboardCol.Add(SheetKit.ListRow(
                        SheetKit.Token(SheetBits.Initial(hand.DisplayName), false, "⚓"),
                        SheetKit.Text(hand.DisplayName, false, false, 13f),
                        arrow));
                }
            if (aboardCol.childCount == 0)
                aboardCol.Add(SheetKit.Text(aboard == 0 ? "nobody aboard" : "—", false, true, 12f));

            // Ashore: the arrow takes them back (`Outpost.Recall`).
            // **By name, never by enumerator** -- `Recall`
            // takes the row out of `l.hands`, and the list this loop walks is
            // that list.
            ashoreCol.Clear();
            if (l == null || l.hands.Count == 0)
                ashoreCol.Add(SheetKit.Text("nobody ashore", false, true, 12f));
            else
            {
                int fromB = crewPart * crewPerPage, toB = fromB + crewPerPage;
                for (int k = fromB; k < Mathf.Min(l.hands.Count, toB); k++)
                {
                    var h = l.hands[k];
                    if (h == null) continue;
                    string who = h.name;
                    var arrow = SheetKit.Btn("←", () => Recall(who), false, true);
                    arrow.SetEnabled(camp != null && camp.BodyNamed(who) != null);
                    ashoreCol.Add(SheetKit.ListRow(
                        SheetKit.Token(SheetBits.Initial(who), h.Angry,
                            SheetBits.JobGlyph(h)),
                        SheetKit.Text(who, false, false, 13f),
                        arrow));
                }
                if (ashoreCol.childCount == 0)
                    ashoreCol.Add(SheetKit.Text("—", false, true, 12f));
            }

            if (everyoneAshore != null)
                everyoneAshore.SetEnabled(aboard > 0 && camp != null
                    && (camp.HasCamp || camp.Building));
        }

        void Station(CrewAgent hand)
        {
            var camp = Camp;
            if (camp == null || hand == null) return;
            if (camp.Station(hand))
            {
                var roster = SheetBits.Roster;
                if (roster != null) roster.Refresh();
            }
            crewKey = long.MinValue;
            Refresh();
        }

        void Recall(string who)
        {
            var camp = Camp;
            var anchor = Anchor;
            if (camp == null || anchor == null) return;
            var body = camp.BodyNamed(who);
            if (body != null && camp.Recall(body, anchor.transform))
            {
                var roster = SheetBits.Roster;
                if (roster != null) roster.Refresh();
            }
            crewKey = long.MinValue;
            Refresh();
        }

        /// Station everybody who is aboard, one `Outpost.Station` at a time --
        /// the same call, pressed for each of them, so a hand it refuses (no
        /// camp yet, already on the books) is refused for the same reason it
        /// would be on its own.
        void EveryoneAshore()
        {
            var camp = Camp;
            var roster = SheetBits.Roster;
            if (camp == null || roster == null) return;
            var all = roster.All;
            bool any = false;
            foreach (var a in all)
            {
                if (a == null || !a.IsAboard) continue;
                if (camp.Station(a)) any = true;
            }
            if (any) roster.Refresh();
            crewKey = long.MinValue;
            Refresh();
        }

        /// `AnchorController.CastOff` (AnchorController.cs:762) -- the public
        /// door onto `WeighAnchor`, which is what the space bar and the
        /// "⚓ Cast off (space)" prompt press (AnchorController.cs:947).
        void CastOff()
        {
            var a = Anchor;
            if (a == null) return;
            a.CastOff();
            Sheets.Close();
        }
    }
}
