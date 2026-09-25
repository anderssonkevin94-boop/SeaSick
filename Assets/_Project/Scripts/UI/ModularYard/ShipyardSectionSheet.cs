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
    /// Built once per section opened, like `ShipyardScreen` itself -- no
    /// scrolling (GDD "no scrolling: pages you swipe between"), so a page
    /// that would overflow the band is trimmed to what matters most rather
    /// than grown; the prototype's three pages are short enough not to need
    /// `SheetKit.SheetPager`'s own cut-into-pages machinery.
    public sealed class ShipyardSectionSheet : VisualElement
    {
        readonly ShipyardDraft draft;
        readonly ShipyardLiveBridge live;
        readonly ShipyardPreview preview;
        readonly string key;
        readonly Action onDone;
        readonly ShipyardDraft.DraftSnapshot opened;

        readonly VisualElement strip;
        readonly VisualElement body;
        readonly Label sectionTitle;
        int page;
        string builtKey;

        static readonly string[] PageLabels = { "Structure", "Guns", "Interior" };

        public ShipyardSectionSheet(ShipyardDraft draft, ShipyardPreview preview, string sectionKey, Action onDone, ShipyardLiveBridge live = null)
        {
            this.draft = draft; this.preview = preview; this.key = sectionKey; this.onDone = onDone; this.live = live;
            opened = draft.BeginSection(sectionKey);
            AddToClassList("yard-sheet");
            preview.SetFocus(true);

            var head = new VisualElement(); head.AddToClassList("yard-sheet-head"); Add(head);
            sectionTitle = new Label(TitleOf(sectionKey)); sectionTitle.AddToClassList("yard-sheet-title"); head.Add(sectionTitle);

            strip = SheetKit.Strip(PageLabels, page, SetPage, SheetTheme.Sea);
            Add(strip);

            body = new VisualElement(); body.AddToClassList("yard-sheet-body"); Add(body);

            var footer = new VisualElement(); footer.AddToClassList("yard-sheet-footer"); Add(footer);
            var reset = new Button(ResetSection) { text = "Reset section" }; reset.AddToClassList("yard-sheet-reset"); footer.Add(reset);
            var done = new Button(Done) { text = "Done" }; done.AddToClassList("yard-sheet-done"); footer.Add(done);

            Fill();
        }

        static string TitleOf(string key) => key == "stern" ? "Stern" : key == "bow" ? "Bow"
            : key.StartsWith("middle[") ? "Middle section" : key;

        void SetPage(int index)
        {
            page = Mathf.Clamp(index, 0, PageLabels.Length - 1);
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
            SheetKit.SetStrip(strip, page, PageLabels);
            body.Clear();
            switch (page)
            {
                case 0: BuildStructure(); break;
                case 1: BuildGuns(); break;
                default: BuildInterior(); break;
            }
        }

        // ---- Structure ------------------------------------------------------

        void BuildStructure()
        {
            bool isStern = key == ShipAssembler.StdKeyStern;
            bool isBow = key == ShipAssembler.StdKeyBow;
            bool isMiddle = !isStern && !isBow;

            if (!draft.IsWideBeam)
            {
                body.Add(SheetKit.Text("Deck level needs the wide beam. Set it on the overview first.", false, true));
            }
            else
            {
                bool raised = draft.IsSectionRaised(key);
                string reason = raised ? null : draft.SectionUnavailableReason(key);
                var row = new VisualElement(); row.AddToClassList("yard-sheet-row"); body.Add(row);
                var lowBtn = new Button(() => { if (raised) draft.ToggleSection(key); Fill(); }) { text = "Low" };
                lowBtn.AddToClassList("yard-seg-button"); lowBtn.EnableInClassList("yard-selected", !raised); row.Add(lowBtn);
                var raiseBtn = new Button(() => { if (!raised) draft.ToggleSection(key); Fill(); }) { text = "Raised" };
                raiseBtn.AddToClassList("yard-seg-button"); raiseBtn.EnableInClassList("yard-selected", raised);
                raiseBtn.SetEnabled(raised || reason == null); raiseBtn.tooltip = reason ?? "";
                row.Add(raiseBtn);
                if (!raised && reason != null) body.Add(SheetKit.Text(reason, false, true, 12f));
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

            if (isStern)
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

        void BuildGuns()
        {
            if (!draft.HasBackend)
            {
                body.Add(SheetKit.Text("Guns: preview only, live refitting not connected.", false, true));
                return;
            }
            var slots = new List<EquipmentSlotView>(draft.EquipmentSlots());
            // Starboard together, then port together (the physical grouping
            // the sheet reads as); within a side, forward before aft, so two
            // rows that would otherwise both read e.g. "starboard, forward"
            // never sit next to each other out of order.
            slots.Sort((a, b) =>
            {
                int side = string.CompareOrdinal(b.side, a.side); // "starboard" before "port"
                return side != 0 ? side : b.positionM.z.CompareTo(a.positionM.z);
            });
            int inDock = 0;
            var report = live?.Report(draft.Snapshot());
            if (report?.dryDock != null)
                foreach (var r in report.dryDock) if (r != null && r.moduleId == ShipConfiguration.EquipmentCannon) inDock = r.inDockNow;

            bool any = false;
            foreach (var s in slots)
            {
                if (s.sectionKey != key) continue;
                any = true;
                bool occupied = !string.IsNullOrEmpty(s.occupantModuleId);
                string status; bool enabled;
                if (occupied) { status = "Fitted — tap to send to the dry dock"; enabled = !draft.Committed; }
                else if (!s.usable) { status = s.blockedReason; enabled = false; }
                else if (inDock > 0) { status = "Empty — tap to fit from the dry dock"; enabled = !draft.Committed; }
                else { status = "Empty — no gun in the dry dock"; enabled = false; }
                string slotId = s.slotId;
                var row = new Button(() => { if (occupied) draft.RemoveGun(slotId); else draft.FitGun(slotId); Fill(); });
                row.AddToClassList("yard-gun-row"); row.SetEnabled(enabled);
                var label = new Label(s.label); label.AddToClassList("yard-gun-label"); row.Add(label);
                var statusLabel = new Label(status); statusLabel.AddToClassList("yard-gun-status"); row.Add(statusLabel);
                body.Add(row);
            }
            if (!any) body.Add(SheetKit.Text("No gun slots on this section.", false, true));
            var dock = new Label($"Dry dock: {(inDock > 0 ? "cannon x" + inDock : "empty")}."); dock.AddToClassList("yard-caption"); body.Add(dock);
        }

        // ---- Interior (Step 2, docs/SHIPYARD-SECTIONS-UI.md) ------------------
        // ISOLATED: `ShipyardService.SectionSpace`/`WithBerths` and
        // `SectionSpaceView` are the Step 2 backend's types, built in
        // parallel in another worktree -- not present here yet. Every call
        // into them is kept to this one page (mirrors the seam in
        // ShipyardLiveBridge.cs) so reconciling only ever touches this block.

        void BuildInterior()
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

            body.Add(SheetKit.Eyebrow("berths"));
            var row = new VisualElement(); row.AddToClassList("yard-sheet-row"); body.Add(row);
            var minus = new Button(() => SetBerths(space.berths - 1));
            minus.AddToClassList("yard-icon-button"); minus.SetEnabled(space.berths > space.minBerths);
            minus.Add(new YardIcon("minus")); row.Add(minus);
            var count = new Label($"{space.berths} berths"); count.AddToClassList("yard-berth-count"); row.Add(count);
            var plus = new Button(() => SetBerths(space.berths + 1));
            plus.AddToClassList("yard-icon-button"); plus.SetEnabled(space.berths < space.maxBerths);
            plus.Add(new YardIcon("plus")); row.Add(plus);
            body.Add(SheetKit.Text($"{space.minBerths}-{space.maxBerths} berths fit this section.", false, true, 12f));

            body.Add(PinnedRule());
            body.Add(SheetKit.Eyebrow("space"));
            body.Add(SheetKit.Text($"Hold: {space.holdCells} cells", false, false, 13f));
            int used = Mathf.RoundToInt(space.berths * space.berthCost) + space.holdCells;
            int total = Mathf.Max(used, Mathf.RoundToInt(space.budgetUnits));
            float frac = space.budgetUnits > 0f ? Mathf.Clamp01((space.berths * space.berthCost) / space.budgetUnits) : 0f;
            var bar = SheetKit.Bar(frac, SheetTheme.Sea, 10f);
            // `.sheet-bar` has no width of its own (Sheets.uss) -- every
            // other place it's used sits in a container that stretches it,
            // this one does not, so without an explicit width AND an
            // explicit min-height/flex-shrink:0 the track measured 0x0 and
            // never drew (2026-09-25 review: "the budget bar needs a
            // visible track" -- confirmed live, `resolvedStyle.height` read
            // 0 even with `style.height` set, because a bare VisualElement
            // with no text/children has no intrinsic size for Yoga to fall
            // back on).
            bar.style.width = Length.Percent(100f);
            bar.style.minHeight = 10f;
            bar.style.flexShrink = 0f;
            body.Add(bar);
            body.Add(SheetKit.Text($"Space {used} / {total} used", false, true, 12f));

            body.Add(PinnedRule());
            int deltaBerths = space.berths - space.defaultBerths;
            int deltaHold = space.holdCells - Mathf.FloorToInt(space.budgetUnits - space.berthCost * space.defaultBerths);
            string sign(int n) => n > 0 ? "+" : n < 0 ? "−" : "±";
            string effect = deltaBerths == 0 && deltaHold == 0
                ? "Same as the current fit."
                : $"{sign(deltaBerths)}{Mathf.Abs(deltaBerths)} berths, {sign(deltaHold)}{Mathf.Abs(deltaHold)} hold vs now";
            body.Add(SheetKit.Text(effect, false, true, 12f));
            body.Add(SheetKit.Text(
                "Every berth this section carries costs hold space; fewer berths leaves more room for cargo.",
                false, true, 12f));
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
