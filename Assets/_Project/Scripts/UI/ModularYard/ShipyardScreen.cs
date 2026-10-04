using System;
using System.Collections.Generic;
using System.Linq;
using SeaSick.Ship.Modular;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard
{
    /// **The shipyard overview** (docs/SHIPYARD-SECTIONS-UI.md Step 1): the
    /// 3D preview on top, a strip of section tiles under it -- tap one to
    /// open its `ShipyardSectionSheet` (Structure / Guns / Interior) --
    /// then the whole-ship summary, the ship-wide Beam setting, the
    /// report's warnings, and [Confirm refit] / [Cancel]. Nothing touches
    /// the ship until Confirm (the existing atomic `ShipyardDraft.Confirm`).
    /// Portrait phone first, landscape desktop second (`yard-wide`, set from
    /// this element's own aspect, same as before).
    public sealed class ShipyardScreen : VisualElement, IDisposable
    {
        readonly ShipyardDraft draft;
        readonly ShipyardLiveBridge live;
        readonly ShipyardPreview preview;
        readonly Image image;
        readonly Label badge;
        readonly Button undo;
        readonly Dictionary<int, Vector2> pointers = new Dictionary<int, Vector2>();
        readonly Action close;
        string builtKey;
        bool disposed;
        readonly IVisualElementScheduledItem refresh;

        // ---- overview ----------------------------------------------------
        readonly VisualElement overview;
        readonly VisualElement tiles;
        readonly VisualElement summary;
        readonly Label blocking;
        readonly Label warnings;
        readonly Label message;
        readonly Button beamStandard, beamWide, confirm;
        readonly Label maxLengthReason;
        readonly Label undoReason;
        string tilesKey;
        // 2 sections fit the tile row full-size; 4+ (a full 3-middle hull)
        // do not -- rather than let the row WRAP (a flex-wrap row's own
        // auto height does not reliably track wrapped children in this UI
        // Toolkit version, see the .yard-tiles CSS comment, and a wrapped
        // second row drew straight over Beam/Deck below it, 2026-09-26
        // phone screenshot), the row stays single-line and compacts its
        // tiles instead: smaller type, one short stats line, thinner "+".
        bool compactTiles;

        // ---- overview paging (docs/SHIPYARD-UX-AUDIT.md item 1/3):
        // "Hull" (tiles/beam/deck, the editing controls) and "Report"
        // (summary/blocking/warnings, the read-only figures) never shared
        // the fixed panel well -- the report half is exactly what the audit
        // found running off the bottom on both phone and desktop. Splitting
        // them into two always-separate pages (same fixed-split idiom the
        // section sheet already uses for Structure/Guns/Interior -- those
        // never merge even when they'd fit) gives each half the WHOLE
        // scroll band instead of fighting the other for it.
        readonly VisualElement hullPage, reportPage;
        readonly Button pageHull, pageReport;
        int overviewPage;

        // ---- success feedback (docs/SHIPYARD-UX-AUDIT.md item 2): Confirm
        // used to close in the same call that set Message = "Refit
        // confirmed.", so nobody ever saw it. This view replaces the
        // overview for one beat after a successful Confirm with a plain-
        // language summary of what just happened and an explicit OK.
        readonly VisualElement successView;
        readonly Label successBody;
        bool successShown;

        // ---- section sheet -------------------------------------------------
        readonly VisualElement sheetHost;
        ShipyardSectionSheet sheet;
        string openSectionKey;

        public ShipyardScreen(ShipyardDraft draft, Action close, ShipyardLiveBridge live = null)
        {
            this.draft = draft; this.close = close; this.live = live;
            AddToClassList("yard");
            var css = Resources.Load<StyleSheet>("UI/ModularShipyard");
            if (css == null) throw new InvalidOperationException("Missing UI/ModularShipyard.uss");
            styleSheets.Add(css);
            // The section sheet (ShipyardSectionSheet.cs) borrows SheetKit's
            // tab strip/buttons/text helpers for its Structure/Guns/Interior
            // pages -- load Sheets.uss too so those come out styled instead
            // of bare UI Toolkit defaults. Never `Sheets.Open`/`SheetHost`
            // themselves: the shipyard stays its own full-screen modal.
            var sheetsCss = Resources.Load<StyleSheet>("UI/Sheets");
            if (sheetsCss != null) styleSheets.Add(sheetsCss);
            var header = Row(this, "yard-header");
            IconButton(header, "close", "Cancel and close", Close);
            var title = new Label("Shipyard"); title.AddToClassList("yard-title"); header.Add(title);
            undo = IconButton(header, "undo", "Undo last change", () => { CloseSection(); draft.Undo(); });
            // The tooltip above is invisible on a phone with no hover --
            // this line says the same thing on screen, only while there is
            // nothing to undo (docs/SHIPYARD-UX-AUDIT.md item 4).
            undoReason = new Label(); undoReason.AddToClassList("yard-caption"); undoReason.AddToClassList("yard-deck-reason");
            undoReason.style.marginLeft = 12; undoReason.style.marginTop = 0; Add(undoReason);
            var body = Row(this, "yard-body");
            var viewport = new VisualElement(); viewport.AddToClassList("yard-viewport"); body.Add(viewport);
            image = new Image { scaleMode = ScaleMode.StretchToFill }; image.AddToClassList("yard-render"); viewport.Add(image);
            badge = new Label("PREVIEW"); badge.AddToClassList("yard-badge"); viewport.Add(badge);
            var views = new VisualElement(); views.AddToClassList("yard-views"); viewport.Add(views);
            IconButton(views, "top", "Top view", () => preview.SetView(true));
            IconButton(views, "perspective", "Three-quarter view", () => preview.SetView(false));

            var panel = new VisualElement(); panel.AddToClassList("yard-panel"); body.Add(panel);

            // ---- overview: tiles, summary, beam, warnings, confirm/cancel
            overview = new VisualElement(); overview.style.flexGrow = 1f; overview.style.minHeight = 0; panel.Add(overview);
            var scroll = new VisualElement(); scroll.AddToClassList("yard-options"); overview.Add(scroll);

            var pager = Row(scroll, "yard-wheels");
            pageHull = Command(pager, "Hull", () => SetOverviewPage(0));
            pageReport = Command(pager, "Report", () => SetOverviewPage(1));

            hullPage = new VisualElement(); hullPage.style.flexGrow = 1f; hullPage.style.minHeight = 0; scroll.Add(hullPage);
            tiles = new VisualElement(); tiles.AddToClassList("yard-tiles"); hullPage.Add(tiles);
            var beamCaption = new Label("Beam"); beamCaption.AddToClassList("yard-caption"); hullPage.Add(beamCaption);
            var beams = Row(hullPage, "yard-wheels");
            beamStandard = Command(beams, "Standard", () => draft.SetWideBeam(false));
            beamWide = Command(beams, "Wide", () => draft.SetWideBeam(true));
            if (draft.IsCoaster) { beams.style.display=DisplayStyle.None; beamCaption.text="Fixed beam · 4.64 m · up to two decks"; }
            maxLengthReason = new Label(); maxLengthReason.AddToClassList("yard-caption"); maxLengthReason.AddToClassList("yard-deck-reason"); hullPage.Add(maxLengthReason);

            reportPage = new VisualElement(); reportPage.style.flexGrow = 1f; reportPage.style.minHeight = 0; scroll.Add(reportPage);
            summary = new VisualElement(); summary.AddToClassList("yard-summary"); reportPage.Add(summary);
            // Blocking (report.blocking / CannotConfirm -- would refuse
            // Confirm) reads in the same red/ember style a blocked gun row
            // already uses; warnings (report.warnings, advisory only) stay
            // the neutral note colour. Never concatenated into one label
            // (docs/SHIPYARD-UX-AUDIT.md item 3).
            blocking = new Label(); blocking.AddToClassList("yard-blocking-text"); reportPage.Add(blocking);
            warnings = new Label(); warnings.AddToClassList("yard-warnings"); reportPage.Add(warnings);

            message = new Label(); message.AddToClassList("yard-message"); overview.Add(message);
            var footer = Row(overview, "yard-footer");
            Command(footer, "Cancel", Close);
            confirm = Command(footer, "Confirm refit", () => {
                string summaryText = BuildConfirmSummary();
                if (draft.Confirm()) ShowSuccess(summaryText);
            });
            confirm.AddToClassList("yard-confirm");
            SetOverviewPage(0);

            // ---- success view (docs/SHIPYARD-UX-AUDIT.md item 2) -- swapped
            // in over `overview` after a successful Confirm, not closed
            // straight away, so "Refit done: ..." is actually seen.
            successView = new VisualElement(); successView.style.flexGrow = 1f; successView.style.minHeight = 0;
            successView.style.display = DisplayStyle.None; panel.Add(successView);
            var successScroll = new VisualElement(); successScroll.AddToClassList("yard-options"); successView.Add(successScroll);
            var successHeading = new Label("Refit done"); successHeading.AddToClassList("yard-heading"); successScroll.Add(successHeading);
            successBody = new Label(); successBody.AddToClassList("yard-details"); successScroll.Add(successBody);
            var successFooter = Row(successView, "yard-footer");
            var successOk = Command(successFooter, "OK", Close);
            successOk.AddToClassList("yard-confirm");

            // ---- section sheet host (built lazily, swapped in over the overview)
            sheetHost = new VisualElement(); sheetHost.style.flexGrow = 1f; sheetHost.style.minHeight = 0;
            sheetHost.style.display = DisplayStyle.None; panel.Add(sheetHost);

            preview = new ShipyardPreview(live == null ? null : live.BuildPreview);
            preview.TextureChanged += SetTexture;
            image.RegisterCallback<GeometryChangedEvent>(e => {
                if (!disposed && e.newRect.width > 1 && e.newRect.height > 1)
                    preview.Resize(Mathf.RoundToInt(e.newRect.width * 2), Mathf.RoundToInt(e.newRect.height * 2));
            });
            RegisterCallback<GeometryChangedEvent>(e => EnableInClassList("yard-wide", e.newRect.width > e.newRect.height * 1.15f));
            image.RegisterCallback<PointerDownEvent>(Down);
            image.RegisterCallback<PointerMoveEvent>(Move);
            image.RegisterCallback<PointerUpEvent>(Up);
            image.RegisterCallback<PointerCancelEvent>(e => Release(e.pointerId));
            image.RegisterCallback<PointerCaptureOutEvent>(e => pointers.Remove(e.pointerId));
            image.RegisterCallback<WheelEvent>(e => { preview.Zoom(Mathf.Exp(-e.delta.y * .035f)); e.StopPropagation(); });
            draft.Changed += Refresh; Refresh(); SetTexture();
            refresh = schedule.Execute(Refresh).Every(250);
        }

        static VisualElement Row(VisualElement parent, string css)
        { var row = new VisualElement(); row.AddToClassList(css); parent.Add(row); return row; }
        static Button Command(VisualElement parent, string text, Action action)
        { var b = new Button(action) { text = text }; parent.Add(b); return b; }
        static Button IconButton(VisualElement parent, string icon, string tooltip, Action action)
        {
            var b = new Button(action) { tooltip = tooltip }; b.AddToClassList("yard-icon-button");
            b.Add(new YardIcon(icon)); parent.Add(b); return b;
        }
        void SetTexture() => image.image = preview.Texture;

        void Refresh()
        {
            if (disposed) return;
            var configuration = draft.Snapshot();
            // While a section sheet is open, the preview is highlighted and
            // framed on THAT section, not on whatever `draft.Highlight` last
            // changed (docs/SHIPYARD-SECTIONS-UI.md: "the preview highlights
            // that section ... and frames it").
            string previewHighlight = openSectionKey ?? draft.Highlight;
            string key = configuration.ToJson() + "|" + previewHighlight;
            if (key != builtKey) { preview.Build(draft.Assembly, previewHighlight, configuration); builtKey = key; }
            undo.SetEnabled(draft.CanUndo);
            undoReason.text = draft.CanUndo ? "" : "Nothing to undo yet.";
            // Hidden on the success view: after a Confirm the undo stack is
            // cleared, and "Nothing to undo yet." there reads like an error.
            undoReason.style.display = draft.CanUndo || successShown ? DisplayStyle.None : DisplayStyle.Flex;
            badge.text = openSectionKey != null ? "SECTION" : (draft.Highlight != null && draft.Highlight.StartsWith("middle[") ? "NEW SECTION" : "PREVIEW");

            if (openSectionKey != null)
            {
                // The section sheet may have made this key invalid (removed
                // it, or the ship shrank under it) -- close back to the
                // overview rather than showing a sheet for nothing.
                if (!draft.SectionKeys().Contains(openSectionKey)) { CloseSection(); return; }
                sheet?.Refresh();
                return;
            }
            RefreshOverview(configuration);
        }

        // ---- overview --------------------------------------------------------

        void RefreshOverview(ShipConfiguration configuration)
        {
            var report = live?.Report(configuration);
            RefreshTiles();

            beamStandard.SetEnabled(!draft.Committed && !draft.IsRaisedDeck && draft.CanSelect(ModuleKind.Stern, ShipConfiguration.V3Stern));
            beamStandard.tooltip = draft.IsRaisedDeck ? "A raised deck needs the wide beam." : "Standard beam";
            beamWide.SetEnabled(!draft.Committed && !draft.IsRaisedDeck && draft.CanSelect(ModuleKind.Stern, ExpandedPresets.ExpandedStern));
            beamStandard.EnableInClassList("yard-selected", !draft.IsWideBeam && !draft.IsRaisedDeck);
            beamWide.EnableInClassList("yard-selected", draft.IsWideBeam || draft.IsRaisedDeck);

            RefreshSummary(report);
            RefreshWarnings(report);

            string blocked = draft.CannotConfirm();
            confirm.SetEnabled(string.IsNullOrEmpty(blocked) && preview.Error == null);
            confirm.tooltip = blocked ?? "Apply this refit";
            string status = preview.Error ?? (!string.IsNullOrEmpty(draft.Message) ? draft.Message :
                draft.HasBackend ? (draft.Dirty ? blocked ?? "Ready to refit" : "") : "Preview only - live refitting not connected");
            // The full reason, always -- a tooltip is invisible on a phone
            // with no hover, so truncating to "see details" used to hide
            // the only place the reason was shown at all
            // (docs/SHIPYARD-UX-AUDIT.md item 4). `.yard-message` wraps and
            // has no height cap for exactly this.
            message.text = status ?? "";
            message.tooltip = status ?? "";
        }

        void SetOverviewPage(int index)
        {
            overviewPage = Mathf.Clamp(index, 0, 1);
            hullPage.style.display = overviewPage == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            reportPage.style.display = overviewPage == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            pageHull.EnableInClassList("yard-selected", overviewPage == 0);
            pageReport.EnableInClassList("yard-selected", overviewPage == 1);
        }

        /// Plain-language summary of what a successful Confirm just did,
        /// captured BEFORE `ShipyardDraft.Confirm()` runs (it overwrites
        /// `Message` with "Refit confirmed." and clears the undo stack) --
        /// so this reads the same report the overview's Report page was
        /// already showing the player (docs/SHIPYARD-UX-AUDIT.md item 2).
        string BuildConfirmSummary()
        {
            // An itemised list, one short line per thing that changed
            // (2026-09-26, Kevin: the old one-liner repeated the "Refit done"
            // heading and buried the dry-dock/hands consequences). Every
            // number is read off the same report the Report page shows.
            var report = live?.Report(draft.Snapshot());
            var lines = new List<string>();
            void FigureLine(string id, string one, string many)
            {
                var f = report?.Figure(id);
                if (f == null || !f.available) return;
                int from = Mathf.RoundToInt(f.current), to = Mathf.RoundToInt(f.proposed);
                if (from == to) return;
                int d = to - from;
                lines.Add($"{(d > 0 ? "+" : "\u2212")}{Mathf.Abs(d)} {(Mathf.Abs(d) == 1 ? one : many)} ({from} to {to})");
            }
            FigureLine("sections", "middle section", "middle sections");
            FigureLine("crewBerths", "bunk", "bunks");
            FigureLine("holdCells", "cargo cell", "cargo cells");
            FigureLine("guns", "gun fitted", "guns fitted");
            if (report?.dryDock != null)
                foreach (var row in report.dryDock)
                {
                    if (row == null || row.moduleId != ShipConfiguration.EquipmentCannon) continue;
                    int d = row.inDockAfterApply - row.inDockNow;
                    if (d > 0) lines.Add(d == 1 ? "1 gun sent to the dry dock" : $"{d} guns sent to the dry dock");
                    else if (d < 0) lines.Add(-d == 1 ? "1 gun taken from the dry dock" : $"{-d} guns taken from the dry dock");
                }
            if (report != null)
                foreach (var note in report.warnings)
                    if (note.code == "HANDS_ASHORE" || note.code == "CANNONS_BUILT") lines.Add(note.message.TrimEnd('.'));
            if (lines.Count == 0) return "No changes to the ship.";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Count; i++) { if (i > 0) sb.Append('\n'); sb.Append("\u2022 ").Append(lines[i]); }
            return sb.ToString();
        }

        void ShowSuccess(string text)
        {
            CloseSection();
            overview.style.display = DisplayStyle.None;
            successBody.text = text;
            successView.style.display = DisplayStyle.Flex;
            successShown = true;
            undoReason.style.display = DisplayStyle.None;
        }

        /// One tile per `SectionKeys()` entry, a "+" insert tile between
        /// (and before/after) them -- `[Stern] [+] [Mid 1] [+] [Mid 2] [+] [Bow]`.
        /// Rebuilt only when the section count changes (`tilesKey`); every
        /// refresh just re-texts/re-enables what is there, same caching
        /// idea the old per-section overlay used.
        void RefreshTiles()
        {
            var keys = draft.SectionKeys();
            // The "+" tiles are now ALWAYS present (see InsertTile/
            // UpdateInsertTile) -- at max length they stay, disabled, with
            // the reason on screen, rather than vanishing with no
            // explanation (docs/SHIPYARD-UX-AUDIT.md item 2). So the
            // rebuild key no longer needs `Count < Maximum`: that only
            // changes enabled state, handled every refresh below.
            compactTiles = keys.Count >= 4;
            tiles.EnableInClassList("yard-tiles--compact", compactTiles);
            string key = string.Join(",", keys) + "|" + draft.IsWideBeam;
            if (key != tilesKey)
            {
                tiles.Clear();
                for (int i = 0; i < keys.Count; i++)
                {
                    tiles.Add(SectionTile(keys[i]));
                    bool afterLast = i == keys.Count - 1;
                    // A "+" belongs between every pair of hull sections --
                    // i.e. after every tile except the very last (the bow) --
                    // so it always inserts a MIDDLE at this position.
                    if (!afterLast) tiles.Add(InsertTile(MiddleInsertIndexAfter(keys[i])));
                }
                tilesKey = key;
            }
            for (int i = 0; i < tiles.childCount; i++)
            {
                if (tiles[i].userData is string sectionKey) UpdateTile(tiles[i], sectionKey);
                else if (tiles[i].userData is int insertIndex) UpdateInsertTile(tiles[i], insertIndex);
            }
            bool atMax = draft.Count >= draft.Maximum;
            maxLengthReason.text = atMax ? $"Longest hull: {draft.Maximum} middle section{(draft.Maximum == 1 ? "" : "s")}." : "";
            maxLengthReason.style.display = atMax ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// The `InsertMiddle` index a "+" tile placed right after `key`
        /// should use: the middle bays before `key` (inclusive, if `key`
        /// itself is a middle) plus one.
        int MiddleInsertIndexAfter(string key)
        {
            if (key == ShipAssembler.StdKeyStern) return 0;
            int idx = IndexOfMiddle(key);
            return idx >= 0 ? idx + 1 : draft.Count;
        }
        static int IndexOfMiddle(string key)
        {
            if (string.IsNullOrEmpty(key) || !key.StartsWith("middle[") || !key.EndsWith("]")) return -1;
            return int.TryParse(key.Substring(7, key.Length - 8), out int i) ? i : -1;
        }

        Button SectionTile(string key)
        {
            var b = new Button(() => OpenSection(key)) { userData = key };
            b.AddToClassList("yard-tile");
            var name = new Label(); name.AddToClassList("yard-tile-name"); b.Add(name);
            var deck = new Label(); deck.AddToClassList("yard-tile-deck"); b.Add(deck);
            var status = new Label(); status.AddToClassList("yard-tile-status"); b.Add(status);
            return b;
        }

        Button InsertTile(int index)
        {
            var b = new Button(() => draft.InsertMiddle(index)) { text = "+", userData = index };
            b.AddToClassList("yard-tile-insert");
            return b;
        }

        /// Kept enabled/reasoned every refresh rather than rebuilt --
        /// stays on screen at max length instead of disappearing
        /// (docs/SHIPYARD-UX-AUDIT.md item 2, "the overview's Insert tile
        /// table"). The reason is ALSO shown as `maxLengthReason` below the
        /// tile row (tooltip alone is invisible on phone).
        void UpdateInsertTile(VisualElement tile, int index)
        {
            if (!(tile is Button b)) return;
            bool atMax = draft.Count >= draft.Maximum;
            b.SetEnabled(!draft.Committed && !atMax);
            b.tooltip = atMax ? $"Longest hull: {draft.Maximum} middle section{(draft.Maximum == 1 ? "" : "s")}." : "Add a middle section here";
        }

        void UpdateTile(VisualElement tile, string key)
        {
            if (!(tile is Button b) || b.childCount < 3) return;
            var name = b[0] as Label;
            var deck = b[1] as Label;
            var status = b[2] as Label;
            if (name == null || deck == null || status == null) return;
            name.text = key == ShipAssembler.StdKeyStern ? "Stern" : key == ShipAssembler.StdKeyBow ? "Bow"
                : $"Mid {IndexOfMiddle(key) + 1}";
            // Deck level of ITS OWN chip (docs/SHIPYARD-SECTIONS-UI.md
            // "tiny status (deck level, guns n, berths n, hold n)") -- a
            // lowercase word buried in the stats line read as part of the
            // sentence, not as the ship's raised/low state at a glance
            // (2026-09-25 review).
            // `IsWideBeam` alone misses a raised deck: `RaiseAll` puts a
            // RAISED preset (not the EXPANDED one `IsWideBeam` checks) on
            // the stern, the same reason the beam row above shows Wide as
            // selected on `IsWideBeam || IsRaisedDeck`, not `IsWideBeam`
            // alone -- without it, a raised ship's tiles hid the chip this
            // was added to show (2026-09-25 review).
            bool wideOrRaised = draft.IsWideBeam || draft.IsRaisedDeck;
            bool raised = wideOrRaised && draft.IsSectionRaised(key);
            deck.style.display = wideOrRaised ? DisplayStyle.Flex : DisplayStyle.None;
            deck.text = raised ? "Raised" : "Low";
            deck.EnableInClassList("yard-tile-deck--raised", raised);
            var occupancy = live?.Report(draft.Snapshot())?.Section(key);
            // Compact (4+ sections, docs above): one short line -- "G2 B4
            // H5" -- instead of three spelled-out, dot-joined items, which
            // is what forced yard-tile-status to wrap to a second line and
            // grow the tile taller than the row it shares (2026-09-26
            // phone screenshot, 5-section hull).
            if (compactTiles)
            {
                status.text = occupancy == null ? "" :
                    $"G{occupancy.guns} B{occupancy.berths} H{occupancy.holdCells}";
            }
            else
            {
                var bits = new List<string>();
                if (occupancy != null)
                {
                    bits.Add($"guns {occupancy.guns}");
                    bits.Add($"berths {occupancy.berths}");
                    bits.Add($"hold {occupancy.holdCells}");
                }
                status.text = string.Join(" · ", bits);
            }
            b.SetEnabled(!draft.Committed);
        }

        void RefreshSummary(ShipyardReport report)
        {
            summary.Clear();
            var row = new VisualElement(); row.AddToClassList("yard-summary-row"); summary.Add(row);
            void Item(string id, string label)
            {
                var f = report?.Figure(id);
                var l = new Label(FormatFigure(f, label)); l.AddToClassList("yard-summary-item"); row.Add(l);
            }
            Item("overallLength", "Length");
            Item("holdCells", "Hold");
            Item("crewBerths", "Berths");
            Item("guns", "Guns");
            Item("simDraft", "Draft");
            Item("weightAllowance", "Cargo allowance");
        }

        /// Blocking (report.blocking, plus `refitNowBlockedBecause` -- would
        /// refuse Confirm) and warnings (report.warnings, advisory) are kept
        /// in SEPARATE labels/styles now, never joined into one list
        /// (docs/SHIPYARD-UX-AUDIT.md item 3: "a blocking reason and an FYI
        /// read identically"). Every line that exists is listed -- nothing
        /// is dropped for space; the Report page has the whole band to
        /// itself for exactly this (see the overview paging above).
        void RefreshWarnings(ShipyardReport report)
        {
            if (report == null)
            {
                blocking.text = ""; blocking.style.display = DisplayStyle.None;
                warnings.text = ""; warnings.style.display = DisplayStyle.None;
                return;
            }
            var blockingLines = new List<string>();
            foreach (var issue in report.blocking) blockingLines.Add(issue.message);
            if (!string.IsNullOrEmpty(report.refitNowBlockedBecause)) blockingLines.Add(report.refitNowBlockedBecause);
            blocking.text = string.Join("\n", blockingLines);
            blocking.style.display = blockingLines.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            var warningLines = new List<string>();
            foreach (var note in report.warnings) if (note.code != "PROVISIONAL_TUNING") warningLines.Add(note.message);
            warnings.text = string.Join("\n", warningLines);
            warnings.style.display = warningLines.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static string FormatFigure(ShipyardFigure figure, string label = null)
        {
            if (figure == null) return (label ?? "Value") + " --";
            return (label ?? figure.label) + " " + (figure.available ? figure.Format(figure.proposed) : "--");
        }

        // ---- section sheet -----------------------------------------------------

        void OpenSection(string key)
        {
            if (draft.Committed) return;
            openSectionKey = key;
            overview.style.display = DisplayStyle.None;
            sheetHost.style.display = DisplayStyle.Flex;
            sheetHost.Clear();
            sheet = new ShipyardSectionSheet(draft, preview, key, CloseSection, live);
            sheetHost.Add(sheet);
            builtKey = null; // force the preview to rebuild with the section highlight/frame
            Refresh();
        }

        void CloseSection()
        {
            if (openSectionKey == null) return;
            openSectionKey = null;
            sheetHost.style.display = DisplayStyle.None;
            sheetHost.Clear();
            sheet = null;
            overview.style.display = DisplayStyle.Flex;
            builtKey = null;
            Refresh();
        }

        void Down(PointerDownEvent e)
        {
            if (e.button != 0) return;
            pointers[e.pointerId] = e.position; image.CapturePointer(e.pointerId); e.StopPropagation();
        }
        float PinchDistance()
        {
            var values = new List<Vector2>(pointers.Values);
            return values.Count < 2 ? 0 : Vector2.Distance(values[0], values[1]);
        }
        void Move(PointerMoveEvent e)
        {
            if (!pointers.TryGetValue(e.pointerId, out var previous)) return;
            float oldDistance = PinchDistance(); pointers[e.pointerId] = e.position;
            if (pointers.Count == 1) preview.Orbit((Vector2)e.position - previous);
            else if (oldDistance > 1) preview.Zoom(PinchDistance() / oldDistance);
            e.StopPropagation();
        }
        void Up(PointerUpEvent e) { Release(e.pointerId); e.StopPropagation(); }
        void Release(int id) { pointers.Remove(id); if (image.HasPointerCapture(id)) image.ReleasePointer(id); }
        void Close() { Dispose(); RemoveFromHierarchy(); close?.Invoke(); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; draft.Changed -= Refresh; preview.TextureChanged -= SetTexture;
            refresh?.Pause();
            pointers.Clear(); image.image = null; preview.Dispose();
        }
    }

    sealed class YardIcon : VisualElement
    {
        public YardIcon(string kind)
        {
            AddToClassList("yard-icon"); pickingMode = PickingMode.Ignore;
            generateVisualContent += ctx => {
                var p = ctx.painter2D; float s = Mathf.Min(contentRect.width, contentRect.height) / 24;
                p.strokeColor = new Color32(225, 240, 246, 255); p.lineWidth = 1.8f * s;
                void Path(params Vector2[] points) { p.BeginPath(); p.MoveTo(points[0] * s); for (int i=1;i<points.Length;i++) p.LineTo(points[i]*s); p.Stroke(); }
                Vector2 V(float x, float y) => new Vector2(x,y);
                if (kind == "close") { Path(V(5,5),V(19,19)); Path(V(19,5),V(5,19)); }
                else if (kind == "plus" || kind == "minus") { Path(V(5,12),V(19,12)); if (kind == "plus") Path(V(12,5),V(12,19)); }
                else if (kind == "undo") { Path(V(9,4),V(4,9),V(9,14)); Path(V(4,9),V(15,9),V(20,13),V(20,17),V(16,20),V(11,20)); }
                else if (kind == "top") Path(V(12,3),V(19,9),V(19,21),V(5,21),V(5,9),V(12,3));
                else { Path(V(3,8),V(12,3),V(21,8),V(21,18),V(12,22),V(3,18),V(3,8),V(12,13),V(21,8)); Path(V(12,13),V(12,22)); }
            };
        }
    }
}
