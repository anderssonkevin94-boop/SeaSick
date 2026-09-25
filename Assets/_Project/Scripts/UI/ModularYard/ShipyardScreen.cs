using System;
using System.Collections.Generic;
using SeaSick.Ship.Modular;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard
{
    public sealed class ShipyardScreen : VisualElement, IDisposable
    {
        readonly ShipyardDraft draft;
        readonly ShipyardLiveBridge live;
        readonly ShipyardPreview preview;
        readonly Image image;
        readonly Label count, length, message, badge;
        readonly Label details;
        readonly Button undo, add, remove, timber, reinforced, beamStandard, beamWide, deckSingle, deckRaised, confirm;
        readonly VisualElement guns;
        readonly Label dryDock;
        readonly Dictionary<int, Vector2> pointers = new Dictionary<int, Vector2>();
        readonly Action close;
        string builtKey;
        bool disposed;
        readonly IVisualElementScheduledItem refresh;

        public ShipyardScreen(ShipyardDraft draft, Action close, ShipyardLiveBridge live = null)
        {
            this.draft = draft; this.close = close; this.live = live;
            AddToClassList("yard");
            var css = Resources.Load<StyleSheet>("UI/ModularShipyard");
            if (css == null) throw new InvalidOperationException("Missing UI/ModularShipyard.uss");
            styleSheets.Add(css);
            var header = Row(this, "yard-header");
            IconButton(header, "close", "Cancel and close", Close);
            var title = new Label("Shipyard"); title.AddToClassList("yard-title"); header.Add(title);
            undo = IconButton(header, "undo", "Undo last change", draft.Undo);
            var body = Row(this, "yard-body");
            var viewport = new VisualElement(); viewport.AddToClassList("yard-viewport"); body.Add(viewport);
            image = new Image { scaleMode = ScaleMode.StretchToFill }; image.AddToClassList("yard-render"); viewport.Add(image);
            badge = new Label("PREVIEW"); badge.AddToClassList("yard-badge"); viewport.Add(badge);
            var views = new VisualElement(); views.AddToClassList("yard-views"); viewport.Add(views);
            IconButton(views, "top", "Top view", () => preview.SetView(true));
            IconButton(views, "perspective", "Three-quarter view", () => preview.SetView(false));

            var panel = new VisualElement(); panel.AddToClassList("yard-panel"); body.Add(panel);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { verticalScrollerVisibility = ScrollerVisibility.Auto };
            scroll.AddToClassList("yard-options"); panel.Add(scroll);
            var h = new Label("Hull & paddle wheel"); h.AddToClassList("yard-heading"); scroll.Add(h);
            var lengthRow = Row(scroll, "yard-length");
            var sectionLabel = new Label("Middle sections"); sectionLabel.AddToClassList("yard-length-title"); lengthRow.Add(sectionLabel);
            remove = IconButton(lengthRow, "minus", "Remove last middle section", () => draft.RemoveMiddle());
            count = new Label(); count.AddToClassList("yard-count"); lengthRow.Add(count);
            add = IconButton(lengthRow, "plus", "Add middle section before bow", () => draft.AddMiddle());
            length = new Label(); length.AddToClassList("yard-measure"); scroll.Add(length);
            var caption = new Label("Paddle wheel"); caption.AddToClassList("yard-caption"); scroll.Add(caption);
            var wheels = Row(scroll, "yard-wheels");
            timber = Command(wheels, "Timber", () => draft.ChooseWheel(ShipConfiguration.TimberRotor));
            reinforced = Command(wheels, "Reinforced", () => draft.ChooseWheel(ShipConfiguration.ReinforcedRotor));
            var beamCaption = new Label("Beam"); beamCaption.AddToClassList("yard-caption"); scroll.Add(beamCaption);
            var beams = Row(scroll, "yard-wheels");
            beamStandard = Command(beams, "Standard", () => draft.SetWideBeam(false));
            beamWide = Command(beams, "Wide", () => draft.SetWideBeam(true));
            var deckCaption = new Label("Deck"); deckCaption.AddToClassList("yard-caption"); scroll.Add(deckCaption);
            var decks = Row(scroll, "yard-wheels");
            deckSingle = Command(decks, "Single", () => draft.SetRaisedDeck(false));
            deckRaised = Command(decks, "Raised", () => draft.SetRaisedDeck(true));
            var gunsCaption = new Label("Guns"); gunsCaption.AddToClassList("yard-caption"); scroll.Add(gunsCaption);
            guns = new VisualElement(); guns.AddToClassList("yard-guns"); scroll.Add(guns);
            dryDock = new Label(); dryDock.AddToClassList("yard-caption"); dryDock.AddToClassList("yard-dock"); scroll.Add(dryDock);
            details = new Label(); details.AddToClassList("yard-details"); scroll.Add(details);
            message = new Label(); message.AddToClassList("yard-message"); panel.Add(message);
            var footer = Row(panel, "yard-footer");
            Command(footer, "Cancel", Close);
            confirm = Command(footer, "Confirm refit", () => { if (draft.Confirm()) Close(); });
            confirm.AddToClassList("yard-confirm");

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
            string key = configuration.ToJson() + "|" + draft.Highlight;
            if (key != builtKey) { preview.Build(draft.Assembly, draft.Highlight, configuration); builtKey = key; }
            count.text = draft.Count.ToString();
            var report = live?.Report(configuration);
            length.text = live == null ? $"Length   {draft.OriginalLength:0.0} m  \u2192  {draft.Assembly.overallLengthM:0.0} m" :
                FormatFigure(report?.Figure("overallLength"), "Overall length");
            add.SetEnabled(draft.Count < draft.Maximum && !draft.Committed && draft.CanSelect(ModuleKind.Middle, ShipConfiguration.V3Middle));
            string removal = draft.RemovalReason();
            remove.SetEnabled(string.IsNullOrEmpty(removal) && !draft.Committed);
            remove.tooltip = removal ?? "Remove last middle section";
            undo.SetEnabled(draft.CanUndo);
            timber.SetEnabled(!draft.Committed && draft.CanSelect(ModuleKind.Rotor, ShipConfiguration.TimberRotor));
            reinforced.SetEnabled(!draft.Committed && draft.CanSelect(ModuleKind.Rotor, ShipConfiguration.ReinforcedRotor));
            timber.EnableInClassList("yard-selected", draft.Rotor == ShipConfiguration.TimberRotor);
            reinforced.EnableInClassList("yard-selected", draft.Rotor == ShipConfiguration.ReinforcedRotor);
            // While raised, the beam is locked to wide (docs/RAISED-DECK.md
            // sec 3/8: "a raised deck needs the wide beam") -- both buttons
            // disabled, the reason on the one a tap would refuse.
            beamStandard.SetEnabled(!draft.Committed && !draft.IsRaisedDeck && draft.CanSelect(ModuleKind.Stern, ShipConfiguration.V3Stern));
            beamStandard.tooltip = draft.IsRaisedDeck ? "A raised deck needs the wide beam." : "Standard beam";
            beamWide.SetEnabled(!draft.Committed && !draft.IsRaisedDeck && draft.CanSelect(ModuleKind.Stern, ExpandedPresets.ExpandedStern));
            beamStandard.EnableInClassList("yard-selected", !draft.IsWideBeam && !draft.IsRaisedDeck);
            beamWide.EnableInClassList("yard-selected", draft.IsWideBeam || draft.IsRaisedDeck);
            string raisedReason = draft.RaisedDeckUnavailableReason();
            deckSingle.SetEnabled(!draft.Committed && draft.IsRaisedDeck);
            deckSingle.tooltip = "Back to a single (non-raised) deck";
            deckRaised.SetEnabled(!draft.Committed && (draft.IsRaisedDeck || raisedReason == null));
            deckRaised.tooltip = raisedReason ?? "A flush upper deck over 1-2 middle bays";
            deckSingle.EnableInClassList("yard-selected", !draft.IsRaisedDeck);
            deckRaised.EnableInClassList("yard-selected", draft.IsRaisedDeck);
            // `add`/`remove` above already read `draft.Maximum` and
            // `draft.RemovalReason()`, both raised-aware (docs/RAISED-DECK.md
            // sec 3/8: the +/- bay controls lock to the family's own 1-2
            // bound while raised) -- no separate gate needed here.
            RefreshGuns(report);
            string blocked = draft.CannotConfirm();
            confirm.SetEnabled(string.IsNullOrEmpty(blocked) && preview.Error == null);
            confirm.tooltip = blocked ?? "Apply this refit";
            string status = preview.Error ?? (!string.IsNullOrEmpty(draft.Message) ? draft.Message :
                draft.HasBackend ? (draft.Dirty ? blocked ?? "Ready to refit" : "") : "Preview only - live refitting not connected");
            // Long reasons live in the scrollable report; the footer never pushes the preview away.
            message.text = status != null && status.Length > 100 ? "Refit blocked - see details" : status;
            message.tooltip = status;
            details.text = ReportText(report, removal, status);
            details.style.display = live == null ? DisplayStyle.None : DisplayStyle.Flex;
            badge.text = draft.Highlight != null && draft.Highlight.StartsWith("middle[") ? "NEW SECTION" : "PREVIEW";
        }

        /// Big, one-thumb rows: tap a fitted gun to send it to the dry dock,
        /// tap an empty usable slot with stock in the dock to fit one from
        /// it. The row only decides whether to call FitGun or RemoveGun --
        /// the backend decides whether the tap succeeds (docs/SHIPYARD-API.md
        /// §15: "the UI only displays what comes back").
        void RefreshGuns(ShipyardReport report)
        {
            guns.Clear();
            if (!draft.HasBackend)
            {
                var placeholder = new Label("Guns: preview only, live refitting not connected.");
                placeholder.AddToClassList("yard-caption"); guns.Add(placeholder);
                dryDock.text = "";
                return;
            }
            int cannonsInDock = DockCount(report, ShipConfiguration.EquipmentCannon);
            var slots = draft.EquipmentSlots();
            foreach (var s in slots)
            {
                bool occupied = !string.IsNullOrEmpty(s.occupantModuleId);
                string status; bool enabled;
                if (occupied) { status = "Fitted — tap to send to the dry dock"; enabled = !draft.Committed; }
                else if (!s.usable) { status = s.blockedReason; enabled = false; }
                else if (cannonsInDock > 0) { status = "Empty — tap to fit from the dry dock"; enabled = !draft.Committed; }
                else { status = "Empty — no gun in the dry dock"; enabled = false; }
                string slotId = s.slotId;
                var row = new Button(() => { if (occupied) draft.RemoveGun(slotId); else draft.FitGun(slotId); });
                row.AddToClassList("yard-gun-row");
                row.SetEnabled(enabled);
                var label = new Label(s.label); label.AddToClassList("yard-gun-label"); row.Add(label);
                var statusLabel = new Label(status); statusLabel.AddToClassList("yard-gun-status"); row.Add(statusLabel);
                guns.Add(row);
            }
            if (slots.Count == 0)
            {
                var none = new Label("No gun slots on this hull."); none.AddToClassList("yard-caption"); guns.Add(none);
            }
            var rows = report?.dryDock;
            if (rows == null || rows.Count == 0) { dryDock.text = "Dry dock: empty."; return; }
            var parts = new List<string>();
            foreach (var r in rows) if (r != null && r.inDockNow > 0) parts.Add($"{r.name} ×{r.inDockNow}");
            dryDock.text = parts.Count > 0 ? "Dry dock: " + string.Join(", ", parts) + "." : "Dry dock: empty.";
        }

        static int DockCount(ShipyardReport report, string moduleId)
        {
            if (report?.dryDock == null) return 0;
            foreach (var r in report.dryDock) if (r != null && r.moduleId == moduleId) return r.inDockNow;
            return 0;
        }

        static string FormatFigure(ShipyardFigure figure, string label = null)
        {
            if (figure == null) return (label ?? "Value") + " unavailable";
            return (label ?? figure.label) + (figure.provisional ? "*" : "") + "   " +
                (figure.available ? figure.Format(figure.current) + " \u2192 " + figure.Format(figure.proposed) : "Unavailable");
        }

        string ReportText(ShipyardReport report, string removal, string status)
        {
            if (live == null) return "";
            if (report == null) return "Ship report unavailable. Close and reopen the shipyard.";
            var lines = new List<string> { "Current \u2192 Proposed" };
            foreach (string id in new[] { "beam", "depth", "holdCells", "weightAllowance", "crewBerths", "displacement", "draft", "simDraft", "guns", "gunSlots" })
            {
                var figure = report.Figure(id);
                if (figure != null) lines.Add(FormatFigure(figure));
            }
            lines.Add("* Provisional. Report and sailing flotation use different models.");
            lines.Add("Wheel choice changes appearance only. No refit cost in this prototype.");
            if (draft.Count > 0 && !string.IsNullOrEmpty(removal)) lines.Add("Cannot remove section: " + removal);
            foreach (var note in report.warnings) lines.Add(note.message);
            foreach (var issue in report.blocking) lines.Add(issue.message);
            if (!string.IsNullOrEmpty(report.refitNowBlockedBecause)) lines.Add(report.refitNowBlockedBecause);
            if (!string.IsNullOrEmpty(status) && status != "Ready to refit") lines.Add(status);
            return string.Join("\n\n", lines);
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
