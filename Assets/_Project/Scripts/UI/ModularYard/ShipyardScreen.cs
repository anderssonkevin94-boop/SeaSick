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
        readonly Label warnings;
        readonly Label message;
        readonly Button beamStandard, beamWide, raiseAll, lowerAll, confirm;
        string tilesKey;

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
            var h = new Label("Hull"); h.AddToClassList("yard-heading"); scroll.Add(h);
            tiles = new VisualElement(); tiles.AddToClassList("yard-tiles"); scroll.Add(tiles);
            var beamCaption = new Label("Beam"); beamCaption.AddToClassList("yard-caption"); scroll.Add(beamCaption);
            var beams = Row(scroll, "yard-wheels");
            beamStandard = Command(beams, "Standard", () => draft.SetWideBeam(false));
            beamWide = Command(beams, "Wide", () => draft.SetWideBeam(true));
            var deckCaption = new Label("Deck (tap a section tile above, or)"); deckCaption.AddToClassList("yard-caption"); scroll.Add(deckCaption);
            var decks = Row(scroll, "yard-wheels");
            lowerAll = Command(decks, "Lower all", () => draft.LowerAll());
            raiseAll = Command(decks, "Raise all", () => draft.RaiseAll());
            summary = new VisualElement(); summary.AddToClassList("yard-summary"); scroll.Add(summary);
            warnings = new Label(); warnings.AddToClassList("yard-warnings"); scroll.Add(warnings);
            message = new Label(); message.AddToClassList("yard-message"); overview.Add(message);
            var footer = Row(overview, "yard-footer");
            Command(footer, "Cancel", Close);
            confirm = Command(footer, "Confirm refit", () => { if (draft.Confirm()) Close(); });
            confirm.AddToClassList("yard-confirm");

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
            string raisedReason = draft.RaisedDeckUnavailableReason();
            lowerAll.SetEnabled(!draft.Committed && draft.IsRaisedDeck);
            lowerAll.tooltip = "Back to a single (non-raised) deck, every section";
            raiseAll.SetEnabled(!draft.Committed && (draft.IsRaisedDeck || raisedReason == null));
            raiseAll.tooltip = raisedReason ?? "A flush upper deck over every section";
            lowerAll.EnableInClassList("yard-selected", !draft.IsRaisedDeck);
            raiseAll.EnableInClassList("yard-selected", draft.IsRaisedDeck);

            RefreshSummary(report);
            RefreshWarnings(report);

            string blocked = draft.CannotConfirm();
            confirm.SetEnabled(string.IsNullOrEmpty(blocked) && preview.Error == null);
            confirm.tooltip = blocked ?? "Apply this refit";
            string status = preview.Error ?? (!string.IsNullOrEmpty(draft.Message) ? draft.Message :
                draft.HasBackend ? (draft.Dirty ? blocked ?? "Ready to refit" : "") : "Preview only - live refitting not connected");
            message.text = status != null && status.Length > 100 ? "Refit blocked - see details" : status;
            message.tooltip = status;
        }

        /// One tile per `SectionKeys()` entry, a "+" insert tile between
        /// (and before/after) them -- `[Stern] [+] [Mid 1] [+] [Mid 2] [+] [Bow]`.
        /// Rebuilt only when the section count changes (`tilesKey`); every
        /// refresh just re-texts/re-enables what is there, same caching
        /// idea the old per-section overlay used.
        void RefreshTiles()
        {
            var keys = draft.SectionKeys();
            string key = string.Join(",", keys) + "|" + draft.IsWideBeam + "|" + (draft.Count < draft.Maximum);
            if (key != tilesKey)
            {
                tiles.Clear();
                bool canInsert = draft.Count < draft.Maximum;
                for (int i = 0; i < keys.Count; i++)
                {
                    tiles.Add(SectionTile(keys[i]));
                    bool afterLast = i == keys.Count - 1;
                    // A "+" belongs between every pair of hull sections --
                    // i.e. after every tile except the very last (the bow) --
                    // so it always inserts a MIDDLE at this position.
                    if (canInsert && !afterLast) tiles.Add(InsertTile(MiddleInsertIndexAfter(keys[i])));
                }
                tilesKey = key;
            }
            for (int i = 0; i < tiles.childCount; i++)
            {
                if (tiles[i].userData is string sectionKey) UpdateTile(tiles[i], sectionKey);
            }
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
            var status = new Label(); status.AddToClassList("yard-tile-status"); b.Add(status);
            return b;
        }

        Button InsertTile(int index)
        {
            var b = new Button(() => draft.InsertMiddle(index)) { text = "+" };
            b.AddToClassList("yard-tile-insert");
            b.tooltip = "Add a middle section here";
            return b;
        }

        void UpdateTile(VisualElement tile, string key)
        {
            if (!(tile is Button b) || b.childCount < 2) return;
            var name = b[0] as Label;
            var status = b[1] as Label;
            if (name == null || status == null) return;
            name.text = key == ShipAssembler.StdKeyStern ? "Stern" : key == ShipAssembler.StdKeyBow ? "Bow"
                : $"Mid {IndexOfMiddle(key) + 1}";
            string level = draft.IsWideBeam ? (draft.IsSectionRaised(key) ? "raised" : "low") : "";
            var occupancy = live?.Report(draft.Snapshot())?.Section(key);
            var bits = new List<string>();
            if (!string.IsNullOrEmpty(level)) bits.Add(level);
            if (occupancy != null)
            {
                bits.Add($"guns {occupancy.guns}");
                bits.Add($"berths {occupancy.berths}");
                bits.Add($"hold {occupancy.holdCells}");
            }
            status.text = string.Join(" · ", bits);
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

        void RefreshWarnings(ShipyardReport report)
        {
            if (report == null) { warnings.text = ""; return; }
            var lines = new List<string>();
            foreach (var note in report.warnings) if (note.code != "PROVISIONAL_TUNING") lines.Add(note.message);
            foreach (var issue in report.blocking) lines.Add(issue.message);
            if (!string.IsNullOrEmpty(report.refitNowBlockedBecause)) lines.Add(report.refitNowBlockedBecause);
            warnings.text = string.Join("\n", lines);
            warnings.style.display = lines.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
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
