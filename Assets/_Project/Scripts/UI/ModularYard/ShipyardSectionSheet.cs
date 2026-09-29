using System;
using System.Collections.Generic;
using SeaSick.Ship.Modular;
using SeaSick.UI.Sheets;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard
{
    /// **The section sheet** (docs/SHIPYARD-SECTIONS-UI.md Step 1 "Section
    /// sheet"): tap a tile on the overview, the preview highlights and
    /// frames that section, and this sheet opens with three swipe pages --
    /// Structure, Guns, Interior -- each showing this section's own effect
    /// on the ship. [Done] keeps the changes (they are already IN the draft;
    /// every control here calls straight through to `ShipyardDraft`, same
    /// as the overview's own controls always have) and returns to the
    /// overview; [Reset section] puts the draft back to what it was when
    /// the sheet opened (`ShipyardDraft.BeginSection`/`ResetSection`).
    ///
    /// Built once per section opened, like `ShipyardScreen` itself.
    /// **Paging** (docs/SHIPYARD-UX-AUDIT.md, 2026-09-26): the GDD's "no
    /// scrolling: pages you swipe between" does not mean a page may clip --
    /// Guns/Interior grow extra entries on this SAME top strip ("Guns 1/2")
    /// when their content does not fit the measured band, exactly the
    /// pattern `FireSheet`/`ShipSheet` already use for the other sheet
    /// system. This sheet is its own modal (not `SheetHost`'s frame), so the
    /// band is measured live off `body`'s own rendered height rather than
    /// `SheetHost.BandHeight` (a different frame's metric).
    public sealed class ShipyardSectionSheet : VisualElement
    {
        readonly ShipyardDraft draft;
        readonly ShipyardLiveBridge live;
        readonly ShipyardPreview preview;
        readonly string key;
        readonly Action onDone;
        readonly ShipyardDraft.DraftSnapshot opened;

        readonly VisualElement stripHolder;
        readonly VisualElement body;
        readonly Label sectionTitle;
        int page;
        string builtKey;

        /// The first-time cutaway hint (PlayerPrefs so it really only shows
        /// once, not once per sheet instance).
        const string HintPrefKey = "SeaSick.Yard.CutawayHintShown";
        bool hintShown;

        /// A sane guess before the first layout pass hands back the real
        /// number via `OnBodyGeometry`; a page planned from this guess is
        /// replanned (and, if the entry count changed, re-strung) the
        /// moment the true height arrives, so nothing settles on a wrong
        /// page count for more than one frame.
        float bandHeight = 220f;
        bool bandKnown;

        enum PageKind { Structure, Guns, Interior }
        readonly struct Pg { public readonly PageKind kind; public readonly int part; public Pg(PageKind k, int p) { kind = k; part = p; } }
        readonly List<Pg> pages = new List<Pg>();
        string[] stripLabels = Array.Empty<string>();

        public ShipyardSectionSheet(ShipyardDraft draft, ShipyardPreview preview, string sectionKey, Action onDone, ShipyardLiveBridge live = null)
        {
            this.draft = draft; this.preview = preview; this.key = sectionKey; this.onDone = onDone; this.live = live;
            opened = draft.BeginSection(sectionKey);
            hintShown = PlayerPrefs.GetInt(HintPrefKey, 0) != 0;
            AddToClassList("yard-sheet");
            preview.SetFocus(true);

            var head = new VisualElement(); head.AddToClassList("yard-sheet-head"); Add(head);
            sectionTitle = new Label(TitleOf(sectionKey)); sectionTitle.AddToClassList("yard-sheet-title"); head.Add(sectionTitle);

            stripHolder = new VisualElement(); stripHolder.AddToClassList("yard-sheet-strip"); Add(stripHolder);

            body = new VisualElement(); body.AddToClassList("yard-sheet-body"); Add(body);
            body.RegisterCallback<GeometryChangedEvent>(OnBodyGeometry);

            var footer = new VisualElement(); footer.AddToClassList("yard-sheet-footer"); Add(footer);
            var reset = new Button(ResetSection) { text = "Reset section" }; reset.AddToClassList("yard-sheet-reset"); footer.Add(reset);
            var done = new Button(Done) { text = "Done" }; done.AddToClassList("yard-sheet-done"); footer.Add(done);

            Fill();
        }

        /// The real, rendered band height for this modal's own fixed panel
        /// (460px portrait / auto-but-flex-clamped landscape -- both belong
        /// to `ShipyardScreen.cs`, not this file) -- measured rather than
        /// guessed, so paging is correct regardless of that layout's own
        /// numbers ever changing. Rebuilding only on a real change (>1px)
        /// avoids a layout/measure loop: `Fill()` clears and refills `body`,
        /// it never resizes it (fixed by `.yard-sheet-body`'s flex-grow +
        /// the panel's own fixed/clamped height), so a re-Fill triggered
        /// from here does not itself raise another geometry event.
        void OnBodyGeometry(GeometryChangedEvent e)
        {
            float h = e.newRect.height;
            if (h <= 1f || (bandKnown && Mathf.Abs(h - bandHeight) < 1f)) return;
            bandHeight = h; bandKnown = true;
            Fill();
        }

        static string TitleOf(string key) => key == "stern" ? "Stern" : key == "bow" ? "Bow"
            : key.StartsWith("middle[") ? "Middle section" : key;

        void SetPage(int index)
        {
            page = pages.Count == 0 ? 0 : Mathf.Clamp(index, 0, pages.Count - 1);
            Fill();
        }

        void Done()
        {
            preview.SetFocus(false);
            onDone?.Invoke();
        }

        void ResetSection()
        {
            draft.ResetSection(key, opened);
            Fill();
        }

        /// Rebuilds the live page every time something changes -- the same
        /// full-rebuild-on-refresh convention this (non-`Sheets`) screen's
        /// gun rows already used before this file existed; a press
        /// interrupted by a 250 ms parent tick can lose its release the
        /// same way it already could there.
        public void Refresh() => Fill();

        void Fill()
        {
            sectionTitle.text = TitleOf(key);
            PlanPages();
            page = pages.Count == 0 ? 0 : Mathf.Clamp(page, 0, pages.Count - 1);
            RebuildStrip();
            body.Clear();
            var pg = pages.Count == 0 ? new Pg(PageKind.Structure, 0) : pages[page];
            if (pg.kind == PageKind.Structure) BuildStructure();
            else if (pg.kind == PageKind.Guns) BuildGuns(pg.part);
            else BuildInterior(pg.part);
        }

        /// The strip's own child COUNT changes when a page splits/unsplits
        /// (e.g. a gun fitted/removed changes nothing here, but a section
        /// swap that changes slot count could), so it is rebuilt whole each
        /// `Fill()` rather than relabeled in place (`SheetKit.SetStrip`
        /// assumes the same number of tabs it was built with).
        void RebuildStrip()
        {
            stripHolder.Clear();
            stripHolder.Add(SheetKit.Strip(stripLabels, page, SetPage, SheetTheme.Sea));
        }

        // ---- page planning ---------------------------------------------

        /// **Estimated** pixel cost of one gun row and the dry-dock caption
        /// line -- a gun row can wrap its status text to two lines
        /// (`.yard-gun-status` is `white-space: normal`), and this sheet has
        /// no exact per-row measurement the way `SheetKit`'s own constants
        /// do for the `Sheets` system. Generous on purpose (better to open
        /// a page early than clip a row) -- NOT verified live on a phone
        /// this session (no Unity launch); flagged in the handoff report.
        const float GunRowPxEstimate = 58f;
        const float DryDockLinePxEstimate = 26f;

        void PlanPages()
        {
            pages.Clear();
            pages.Add(new Pg(PageKind.Structure, 0));

            int gunSlotCount = 0;
            if (draft.HasBackend) foreach (var s in draft.EquipmentSlots()) if (s.sectionKey == key) gunSlotCount++;
            int gunRowsPerPage = draft.IsCoaster ? 2 : Mathf.Max(1, Mathf.FloorToInt((bandHeight - DryDockLinePxEstimate) / GunRowPxEstimate));
            int gunPages = SheetKit.PageCount(Mathf.Max(1, gunSlotCount), gunRowsPerPage);
            for (int i = 0; i < gunPages; i++) pages.Add(new Pg(PageKind.Guns, i));

            // The cutaway's own footprint is bounded -- a fixed-height hull
            // picture, one (non-wrapping-in-practice) row of cells, and a
            // handful of single-line totals -- rather than a growing list,
            // so it stays one page. A section with an unusually large space
            // budget could still overflow the cell row's height if it wraps
            // to several lines; not verified live this session, flagged in
            // the handoff report alongside the estimate above.
            interiorPages = InteriorPageCount(gunSlotCount);
            for (int i = 0; i < interiorPages; i++) pages.Add(new Pg(PageKind.Interior, i));

            stripLabels = new string[pages.Count];
            for (int i = 0; i < pages.Count; i++)
            {
                var p = pages[i];
                stripLabels[i] = p.kind == PageKind.Structure ? "Structure"
                    : p.kind == PageKind.Guns ? SheetKit.PageLabel("Guns", p.part, gunPages)
                    // "Details", not "Interior 2/2": five tabs on the 420px
                    // landscape panel clipped "Interior 1/2" to "nterior 1/".
                    : p.part == 0 ? "Interior" : "Details";
            }
        }

        // Interior page budget (panel units). The cutaway's own height is
        // exact (`SectionCutaway.EstimateHeight` runs the same layout math
        // it draws with); the one-line rows were measured live (2026-09-26,
        // height + margins); notes are generous estimates, same "open a
        // page early rather than clip" rule as the gun rows above.
        const float TotalsPx = 28.5f, DeltaPx = 30.5f, HintPx = 34f, DockPx = 32f, NotePx = 44f, BlockPx = 34f;
        const float BodyPadPx = 26f; // .yard-sheet-body padding 18 + 8, measured live
        int interiorPages = 1;

        /// 1, or 2 when the cutaway + totals and the consequences/dry-dock
        /// lines do not fit one band together (a short landscape band, a
        /// raised section with a long consequence list): page 1 keeps the
        /// picture and its numbers, page 2 carries what follows from them.
        int InteriorPageCount(int gunSlotCount)
        {
            if (live == null) return 1;
            try
            {
                var snap = draft.Snapshot();
                var sp = live.SectionSpace(snap, key);
                if (sp == null || !string.IsNullOrEmpty(sp.reason)) return 1;
                int cellsN = Mathf.FloorToInt(sp.budgetUnits);
                float top = SectionCutaway.EstimateHeight(cellsN, draft.IsSectionRaised(key),
                    key == ShipAssembler.StdKeyStern, key == ShipAssembler.StdKeyBow, gunSlotCount > 0)
                    + TotalsPx + DeltaPx + (hintShown ? 0f : HintPx);
                float rest = DockPx + (string.IsNullOrEmpty(draft.Message) ? 0f : NotePx);
                var report = live.Report(snap);
                if (report != null)
                {
                    foreach (var w in report.warnings)
                        if (w.code == "HANDS_ASHORE" || w.code == "FEWER_BERTHS" || w.code == "HOLD_SMALLER") rest += NotePx;
                    foreach (var b in report.blocking)
                        if (b.code == ShipyardCodes.CargoWouldNotFit || b.code == ShipyardCodes.GunsNeedCrew) rest += BlockPx;
                }
                return top + rest <= bandHeight - BodyPadPx ? 1 : 2;
            }
            catch { return 1; }
        }

        // ---- Structure ------------------------------------------------------

        void BuildStructure()
        {
            bool isStern = key == ShipAssembler.StdKeyStern;
            bool isBow = key == ShipAssembler.StdKeyBow;
            bool isMiddle = !isStern && !isBow;

            if (!draft.IsWideBeam && !draft.IsCoaster)
            {
                body.Add(SheetKit.Text("Deck level needs the wide beam. Set it on the overview first.", false, true));
            }
            else
            {
                bool raised = draft.IsSectionRaised(key);
                string reason = raised ? null : draft.SectionUnavailableReason(key);
                var row = new VisualElement(); row.AddToClassList("yard-sheet-row"); body.Add(row);
                var lowBtn = new Button(() => { if (raised) draft.ToggleSection(key); Fill(); }) { text = draft.IsCoaster ? "One deck" : "Low" };
                lowBtn.AddToClassList("yard-seg-button"); lowBtn.EnableInClassList("yard-selected", !raised); row.Add(lowBtn);
                var raiseBtn = new Button(() => { if (!raised) draft.ToggleSection(key); Fill(); }) { text = draft.IsCoaster ? "Two decks" : "Raised" };
                raiseBtn.AddToClassList("yard-seg-button"); raiseBtn.EnableInClassList("yard-selected", raised);
                raiseBtn.SetEnabled(raised || reason == null); raiseBtn.tooltip = reason ?? "";
                row.Add(raiseBtn);
                if (!raised && reason != null) body.Add(SheetKit.Text(reason, false, true, 12f));
            }

            // Upper-deck layer (2026-09-27): third deck over a connected
            // raised section, gun foredeck on the standard bow. Astra's art is
            // unreviewed; the report says so (ART_UNREVIEWED).
            string layer = draft.UpperDeckOption(key);
            if (layer != null || draft.HasUpperDeck(key))
            {
                bool fitted = draft.HasUpperDeck(key);
                bool isFore = layer == UpperDeckLayers.Foredeck;
                body.Add(SheetKit.Eyebrow(isFore ? "gun foredeck" : "third deck"));
                var urow = new VisualElement(); urow.AddToClassList("yard-sheet-row"); body.Add(urow);
                var noneBtn = new Button(() => { if (fitted) draft.ToggleUpperDeck(key); Fill(); }) { text = "None" };
                noneBtn.AddToClassList("yard-seg-button"); noneBtn.EnableInClassList("yard-selected", !fitted); urow.Add(noneBtn);
                var fitBtn = new Button(() => { if (!fitted) draft.ToggleUpperDeck(key); Fill(); }) { text = isFore ? "Foredeck" : "Third deck" };
                fitBtn.AddToClassList("yard-seg-button"); fitBtn.EnableInClassList("yard-selected", fitted); urow.Add(fitBtn);
                body.Add(SheetKit.Text("New art, not reviewed yet.", false, true, 12f));
            }

            if (isMiddle)
            {
                int index = IndexOf(key);
                string blocked = draft.RemovalReasonFor(index);
                var remove = new Button(() => { draft.RemoveSection(key); Fill(); }) { text = "Remove this section" };
                remove.AddToClassList("yard-sheet-danger");
                remove.SetEnabled(string.IsNullOrEmpty(blocked));
                remove.tooltip = string.IsNullOrEmpty(blocked) ? "Guns on it go to the dry dock." : blocked;
                body.Add(remove);
                if (!string.IsNullOrEmpty(blocked)) body.Add(SheetKit.Text(blocked, false, true, 12f));
            }
            else
            {
                body.Add(SheetKit.Text(isStern ? "The stern and bow always stay." : "The bow and stern always stay.", false, true, 12f));
            }

            if (isStern && draft.IsCoaster) body.Add(SheetKit.Text("The paddle wheel grows with the stern level.",false,true,12f));
            if (isStern && !draft.IsCoaster)
            {
                body.Add(PinnedRule());
                body.Add(SheetKit.Eyebrow("paddle wheel"));
                var wheels = new VisualElement(); wheels.AddToClassList("yard-sheet-row"); body.Add(wheels);
                var timber = new Button(() => { draft.ChooseWheel(ShipConfiguration.TimberRotor); Fill(); }) { text = "Timber" };
                timber.AddToClassList("yard-seg-button"); timber.EnableInClassList("yard-selected", draft.Rotor == ShipConfiguration.TimberRotor);
                wheels.Add(timber);
                var reinforced = new Button(() => { draft.ChooseWheel(ShipConfiguration.ReinforcedRotor); Fill(); }) { text = "Reinforced" };
                reinforced.AddToClassList("yard-seg-button"); reinforced.EnableInClassList("yard-selected", draft.Rotor == ShipConfiguration.ReinforcedRotor);
                wheels.Add(reinforced);
            }

            if (!string.IsNullOrEmpty(draft.Message)) body.Add(SheetKit.Note(draft.Message));
        }

        static int IndexOf(string middleKey)
        {
            if (!middleKey.StartsWith("middle[") || !middleKey.EndsWith("]")) return -1;
            return int.TryParse(middleKey.Substring(7, middleKey.Length - 8), out int i) ? i : -1;
        }

        /// A bare VisualElement (a rule, the interior page's budget bar) has
        /// no text/children of its own for Yoga to size from -- without
        /// pinning min-height and flex-shrink:0 it measures 0x0 and never
        /// draws, even with an explicit `style.height` set (2026-09-25
        /// review: the budget bar and the section sheet's rules were both
        /// invisible; confirmed live via `resolvedStyle`).
        static VisualElement PinnedRule()
        {
            var r = SheetKit.Rule();
            r.style.minHeight = 1f; r.style.flexShrink = 0f;
            return r;
        }

        // ---- Guns -------------------------------------------------------------

        /// One page of this section's gun-slot rows (`part`, 0-based --
        /// `PlanPages` works out how many parts there are). The dry-dock
        /// caption is shown on EVERY part (docs/SHIPYARD-UX-AUDIT.md item 3
        /// -- "always show the dry-dock stock on any page that fits/removes
        /// guns"), not just the last one.
        void BuildGuns(int part)
        {
            if (!draft.HasBackend)
            {
                body.Add(SheetKit.Text("Guns: preview only, live refitting not connected.", false, true));
                return;
            }
            var sectionSlots = SectionGunSlots();
            var report = live?.Report(draft.Snapshot());
            int inDock = DryDockCount(report);

            int rowsPerPage = draft.IsCoaster ? 2 : Mathf.Max(1, Mathf.FloorToInt((bandHeight - DryDockLinePxEstimate) / GunRowPxEstimate));
            int from = part * rowsPerPage;
            int to = Mathf.Min(sectionSlots.Count, from + rowsPerPage);

            if (sectionSlots.Count == 0)
            {
                body.Add(SheetKit.Text("No gun slots on this section.", false, true));
            }
            else
            {
                for (int i = from; i < to; i++) body.Add(GunRow(sectionSlots[i], inDock));
            }
            body.Add(DryDockLine(inDock));
            if (!string.IsNullOrEmpty(draft.Message)) body.Add(SheetKit.Note(draft.Message));
        }

        /// This section's gun slots, in the SAME order/grouping the old
        /// single-page Guns list used (starboard together, then port
        /// together; forward before aft within a side) -- the cutaway reads
        /// the identical `EquipmentSlotView.label` for these slots, so the
        /// two pages never tell a different story about where a gun is
        /// (docs/SHIPYARD-UX-AUDIT.md "For the two changes in flight").
        List<EquipmentSlotView> SectionGunSlots()
        {
            var slots = new List<EquipmentSlotView>(draft.EquipmentSlots());
            slots.Sort((a, b) =>
            {
                int side = string.CompareOrdinal(b.side, a.side); // "starboard" before "port"
                return side != 0 ? side : b.positionM.z.CompareTo(a.positionM.z);
            });
            var section = new List<EquipmentSlotView>();
            foreach (var s in slots) if (s.sectionKey == key) section.Add(s);
            return section;
        }

        static int DryDockCount(ShipyardReport report)
        {
            int inDock = 0;
            if (report?.dryDock != null)
                foreach (var r in report.dryDock) if (r != null && r.moduleId == ShipConfiguration.EquipmentCannon) inDock = Mathf.Max(0,r.inDockAfterApply);
            return inDock;
        }

        /// Always visible, whatever the stock: an empty dock now SAYS how a
        /// gun gets there instead of a flat "empty" (docs/SHIPYARD-UX-AUDIT.md
        /// "nowhere does the UI explain how a cannon gets into the dry dock" --
        /// the only path this prototype has is removing one already fitted,
        /// so that is what this line says).
        VisualElement DryDockLine(int inDock)
        {
            string text = inDock > 0
                ? (inDock == 1 ? "Dry dock: 1 cannon." : $"Dry dock: {inDock} cannons.")
                : draft.IsCoaster ? "Empty slots can build a cannon. Free during this prototype." : "Dry dock: empty. Remove a gun from any slot to store it here.";
            var l = new Label(text); l.AddToClassList("yard-caption"); l.AddToClassList("yard-dock-line");
            return l;
        }

        Button GunRow(EquipmentSlotView s, int inDock)
        {
            bool occupied = !string.IsNullOrEmpty(s.occupantModuleId);
            string status; bool enabled;
            if (occupied) { status = draft.IsCoaster ? "Fitted — tap to store" : "Fitted — tap to send to the dry dock"; enabled = !draft.Committed; }
            else if (!s.usable) { status = s.blockedReason; enabled = false; }
            else if (inDock > 0) { status = "Empty — tap to fit from the dry dock"; enabled = !draft.Committed; }
            else if(draft.IsCoaster) { status="Build and fit — free";enabled=!draft.Committed; }
            else { status = "Empty — no gun in the dry dock."; enabled = false; }
            string slotId = s.slotId;
            var row = new Button(() => { if (occupied) draft.RemoveGun(slotId); else draft.FitGun(slotId); Fill(); });
            row.AddToClassList("yard-gun-row"); row.SetEnabled(enabled);
            // A disabled-and-empty row (no gun in dock, or blocked by
            // clearance) reads as a genuine BLOCK, not just a duller
            // button -- stronger, visible styling rather than opacity
            // alone (docs/SHIPYARD-UX-AUDIT.md: "worth a stronger disabled
            // state, it's subtle").
            bool blocked = !enabled && !occupied;
            row.EnableInClassList("yard-gun-row--blocked", blocked);
            var label = new Label(s.label); label.AddToClassList("yard-gun-label"); row.Add(label);
            var statusLabel = new Label(status); statusLabel.AddToClassList("yard-gun-status");
            if (blocked) statusLabel.AddToClassList("yard-blocking-text");
            row.Add(statusLabel);
            return row;
        }

        // ---- Interior: the 2D cutaway (Step 2, docs/SHIPYARD-SECTIONS-UI.md) --
        // ISOLATED: `ShipyardService.SectionSpace`/`WithBerths` and
        // `SectionSpaceView` are the Step 2 backend's types. Every call into
        // them is kept to this one block (mirrors the seam in
        // ShipyardLiveBridge.cs) so reconciling only ever touches this file.

        void BuildInterior(int part)
        {
            if (live == null)
            {
                body.Add(SheetKit.Text("Interior: preview only, live refitting not connected.", false, true));
                return;
            }
            SectionSpaceView space;
            try { space = live.SectionSpace(draft.Snapshot(), key); }
            catch (Exception e) { body.Add(SheetKit.Text("Interior space unavailable: " + e.Message, false, true)); return; }
            if (space == null) { body.Add(SheetKit.Text("Interior space unavailable for this section.", false, true)); return; }
            if (!string.IsNullOrEmpty(space.reason))
            {
                body.Add(SheetKit.Text(space.reason, false, true));
                return;
            }

            // ---- cells: floor(budgetUnits) compartments, each cargo (1
            // hold cell) or a bunk pair (berthCost 0.5 each -> 2 berths per
            // cell; an odd total gets one 1-bunk cell). `space.holdCells`
            // is already the backend's own floor -- never recomputed here,
            // only split into cells. ---------------------------------------
            int totalCells = Mathf.FloorToInt(space.budgetUnits);
            int cargoCells = Mathf.Clamp(space.holdCells, 0, totalCells);
            int bunkCells = Mathf.Max(0, totalCells - cargoCells);
            bool oddLastBunk = bunkCells > 0 && space.berths % 2 != 0;

            // Bunks first: the cutaway fills its grid top row first, so the
            // bunks read as quarters above the cargo in the hold (and, on a
            // raised section, sit in the between-deck).
            var cells = new List<SectionCutaway.Cell>(totalCells);
            for (int i = 0; i < bunkCells; i++)
            {
                bool isOdd = oddLastBunk && i == bunkCells - 1;
                cells.Add(new SectionCutaway.Cell(true, isOdd ? 1 : 2, isOdd ? -1 : -2));
            }
            for (int i = 0; i < cargoCells; i++) cells.Add(new SectionCutaway.Cell(false, 0, +2));

            var sectionSlots = SectionGunSlots();
            var report = live.Report(draft.Snapshot());
            int inDock = DryDockCount(report);
            var markers = new List<SectionCutaway.GunMarker>(sectionSlots.Count);
            for (int si = 0; si < sectionSlots.Count; si++)
            {
                var s = sectionSlots[si];
                bool occupied = !string.IsNullOrEmpty(s.occupantModuleId);
                string status; bool enabled;
                if (occupied) { status = draft.IsCoaster ? "Fitted — tap to store" : "Fitted — tap to send to the dry dock"; enabled = !draft.Committed; }
                else if (!s.usable) { status = s.blockedReason; enabled = false; }
                else if (inDock > 0) { status = "Empty — tap to fit from the dry dock"; enabled = !draft.Committed; }
                else if(draft.IsCoaster) { status="Build and fit — free";enabled=!draft.Committed; }
            else { status = "Empty — no gun in the dry dock."; enabled = false; }
                float along = (si + 0.5f) / Mathf.Max(1, sectionSlots.Count);
                if (preview != null && preview.TrySlotAlong(key, s.slotId, out float real)) along = real;
                markers.Add(new SectionCutaway.GunMarker
                {
                    slotId = s.slotId, side = s.side, label = s.label,
                    occupied = occupied, enabled = enabled, status = status, along = along,
                });
            }

            bool isStern = key == ShipAssembler.StdKeyStern;
            bool isBow = key == ShipAssembler.StdKeyBow;
            bool raised = draft.IsSectionRaised(key);

            // Tap feedback: the cell the last tap changed (the bunk/cargo
            // boundary moved by one cell) glows for a beat. Kept here, not
            // on the element, because `Fill()` rebuilds the cutaway on every
            // refresh tick.
            int flash = -1;
            if (Time.unscaledTime < flashUntil && totalCells > 0)
                flash = Mathf.Clamp(lastTapAddedBunks ? bunkCells - 1 : bunkCells, 0, totalCells - 1);

            bool showTop = part == 0;
            bool showRest = interiorPages <= 1 || part >= 1;

            if (showTop)
            {
            var cutaway = new SectionCutaway();
            cutaway.Set(isStern, isBow, raised, cells, TapCell, markers, TapGun, flash);
            body.Add(cutaway);
            }

            // Totals, with the same icons the cells use -- this row is the
            // legend as well as the count.
            int guns = report?.Section(key)?.guns ?? 0;
            var totals = new VisualElement(); totals.AddToClassList("yard-cutaway-totals"); if (showTop) body.Add(totals);
            totals.Add(Total("bunk2", new Color32(171, 218, 239, 255), $"Bunks {space.berths}"));
            totals.Add(Total("crate", new Color32(246, 220, 170, 255), $"Cargo {cargoCells}"));
            if (sectionSlots.Count > 0)
                totals.Add(Total("cannon", new Color32(171, 218, 239, 255), $"Guns {guns}/{sectionSlots.Count}"));

            // One line: what changed vs the opened fit, then the range.
            // The min/max RANGE stays visible (the one thing the old +/-
            // page got right, per docs/SHIPYARD-UX-AUDIT.md): a tap that
            // would exceed it is clamped by WithBerths, so it must be
            // readable up front.
            string delta = DeltaVsOpened(space, cargoCells, guns);
            bool changed = !string.IsNullOrEmpty(delta) && delta != "Same as now";
            string range = $"{space.minBerths}–{space.maxBerths} bunks fit";
            // A leftover half unit is said here, in a few words, rather
            // than on a line of its own that pushed the dry-dock line off
            // a full bow page (2026-09-26 phone shot).
            if (totalCells > 0 && space.budgetUnits - totalCells > 0.01f) range += " · ½ unit spare";
            var deltaLine = new Label(string.IsNullOrEmpty(delta) ? range + "." : $"{delta} · {range}.");
            deltaLine.AddToClassList("yard-cutaway-delta");
            deltaLine.EnableInClassList("yard-cutaway-delta--changed", changed);
            body.Add(deltaLine);

            if (totalCells == 0 && showTop)
                body.Add(SheetKit.Text("This section has no interior space.", false, true, 12f));

            if (!hintShown && showTop)
            {
                body.Add(SheetKit.Text("Tap a compartment to swap cargo ↔ bunks. Tap a gun to fit or unfit it.", false, true, 12f));
                hintShown = true;
                PlayerPrefs.SetInt(HintPrefKey, 1); PlayerPrefs.Save();
            }

            // ---- consequences, right here -- not just on the overview
            // (docs/SHIPYARD-UX-AUDIT.md: hands ashore / cargo won't fit /
            // guns need crew). `warnings` (soft/advisory) and `blocking`
            // (hard, would refuse Confirm) are kept visually distinct --
            // warnings use the note style, blocking reasons the same
            // stronger red text a blocked gun row uses. ---------------------
            if (!showRest)
            {
                foreach (var child in body.Children()) child.style.flexShrink = 0f;
                return;
            }
            if (report != null)
            {
                foreach (var w in report.warnings)
                    if (w.code == "HANDS_ASHORE" || w.code == "FEWER_BERTHS" || w.code == "HOLD_SMALLER")
                        body.Add(SheetKit.Note(w.message));
                foreach (var b in report.blocking)
                    if (b.code == ShipyardCodes.CargoWouldNotFit || b.code == ShipyardCodes.GunsNeedCrew)
                        body.Add(BlockingText(b.message));
            }

            body.Add(DryDockLine(inDock));
            if (!string.IsNullOrEmpty(draft.Message)) body.Add(SheetKit.Note(draft.Message));
            // Natural height for every line: if the page ever runs long it
            // is cut at the band's bottom edge (overflow: hidden) instead of
            // squashing two lines on top of each other.
            foreach (var child in body.Children()) child.style.flexShrink = 0f;
        }

        /// "+2 bunks, −1 cargo" vs the ship's fit when the sheet was opened
        /// (`opened`, the same snapshot `Reset section` restores) -- "the
        /// ship's current fit" the brief asks for, read off the exact same
        /// `SectionSpace`/`Report` views the live numbers above use, never
        /// a second calculation.
        string DeltaVsOpened(SectionSpaceView space, int cargoCells, int guns)
        {
            SectionSpaceView baseline = null;
            try { baseline = live.SectionSpace(opened.configuration, key); } catch { }
            if (baseline == null || !string.IsNullOrEmpty(baseline.reason)) return "";
            int baseGuns = live.Report(opened.configuration)?.Section(key)?.guns ?? 0;
            int baseTotal = Mathf.FloorToInt(baseline.budgetUnits);
            int baseCargo = Mathf.Clamp(baseline.holdCells, 0, baseTotal);
            int dBerths = space.berths - baseline.berths;
            int dCargo = cargoCells - baseCargo;
            int dGuns = guns - baseGuns;
            if (dBerths == 0 && dCargo == 0 && dGuns == 0) return "Same as now";
            var parts = new List<string>();
            if (dBerths != 0) parts.Add(Delta(dBerths, "bunk"));
            if (dCargo != 0) parts.Add(Delta(dCargo, "cargo"));
            if (dGuns != 0) parts.Add(Delta(dGuns, "gun"));
            return string.Join(", ", parts) + " vs now";
        }

        static string Delta(int n, string noun) =>
            n == 0 ? $"±0 {noun}" : n > 0 ? $"+{n} {noun}{(n == 1 ? "" : "s")}" : $"−{-n} {noun}{(n == -1 ? "" : "s")}";

        static VisualElement BlockingText(string s)
        {
            var l = new Label(s ?? ""); l.AddToClassList("yard-blocking-text"); l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        float flashUntil;
        bool lastTapAddedBunks;

        void TapCell(SectionCutaway.Cell c)
        {
            SectionSpaceView space;
            try { space = live.SectionSpace(draft.Snapshot(), key); } catch { return; }
            if (space == null) return;
            lastTapAddedBunks = c.berthDelta > 0;
            flashUntil = Time.unscaledTime + 0.9f;
            int before = space.berths;
            SetBerths(space.berths + c.berthDelta);
            // A clamped tap (already at min/max) changed nothing: no glow,
            // the range on the delta line says why.
            SectionSpaceView after = null;
            try { after = live.SectionSpace(draft.Snapshot(), key); } catch { }
            if (after == null || after.berths == before) { flashUntil = 0f; Fill(); return; }
            // One more rebuild once the glow is over, so it does not wait
            // for an unrelated refresh to go away.
            schedule.Execute(() => { if (Time.unscaledTime >= flashUntil) Fill(); }).StartingIn(950);
        }

        static VisualElement Total(string icon, Color color, string text)
        {
            var e = new VisualElement(); e.AddToClassList("yard-cutaway-total");
            e.Add(new CutawayIcon(icon, color));
            var l = new Label(text); l.AddToClassList("yard-cutaway-total-text"); e.Add(l);
            return e;
        }

        void TapGun(SectionCutaway.GunMarker g)
        {
            if (g.occupied) draft.RemoveGun(g.slotId); else draft.FitGun(g.slotId);
            Fill();
        }

        void SetBerths(int berths)
        {
            var next = live?.WithBerths(draft.Snapshot(), key, berths);
            if (next == null) return;
            draft.ReplaceForInterior(next, key);
            Fill();
        }
    }
}
