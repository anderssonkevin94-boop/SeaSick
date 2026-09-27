using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard
{
    /// **Press, hold, drag a module.** (Mockup frame 3.)
    ///
    /// Hold a filled slot ~0.35 s without moving more than a thumb's slop
    /// and it lifts: a tilted ghost follows the pointer, every registered
    /// target is marked `ys-drop-ok` (green; a filled one swaps) or
    /// `ys-drop-no`, the store zone appears, and the one under the pointer
    /// gets `ys-drop-hover`. Letting go over a valid target calls
    /// `onMove(fromCell, toCell)`, `onMoveToSection(fromCell, sectionKey)` or
    /// `onReturnToStore(fromCell)`; anywhere else puts it back. A move
    /// before the hold completes, or a release, is an ordinary tap.
    ///
    /// Touch and mouse are the same pointer events here. The pointer is
    /// captured by the ghost (a child of `overlay`), and moves are read by a
    /// trickle-down handler on `overlay`, so the drag keeps tracking when the
    /// finger leaves the slot it started on.
    ///
    /// Targets are plain elements the views register; a view that rebuilds
    /// just registers again -- detached elements are dropped at every lift.
    /// Nothing here knows what a module is: `canDrop(fromCell, targetId)`
    /// decides validity, where targetId is a cell id or "section:<key>".
    public sealed class DragController
    {
        public const string SectionPrefix = "section:";

        public float HoldSeconds = 0.35f;
        public float SlopUnits = 12f;
        public float DwellSeconds = 0.5f;

        public Func<string, string, bool> canDrop;
        public event Action<string, string> onMove;
        public event Action<string, string> onMoveToSection;
        public event Action<string> onReturnToStore;
        /// Fired as the module lifts (before targets are marked), so a host
        /// can switch the card into its drag look.
        public event Action<string> Lifted;
        /// Fired when a drag ends, dropped or not.
        public event Action Ended;
        /// The target id under the pointer (cell id, "section:key", "store") or null.
        public event Action<string> HoverChanged;

        public bool IsDragging => ghost != null;
        public string DraggingCell { get; private set; }

        enum Kind { Cell, Section, Store, Dwell }
        sealed class Target { public VisualElement el; public string id; public Kind kind; public Action dwell; public bool ok; }

        readonly VisualElement overlay;
        readonly List<Target> targets = new List<Target>();
        VisualElement storeZone;

        // pending hold
        int pendPointer = -1; Vector2 pendStart; string pendCell; VisualElement pendSource;
        Func<VisualElement> pendGhost; IVisualElementScheduledItem holdTimer;
        // live drag
        VisualElement ghost, source; int dragPointer = -1; Target hover; float hoverSince; bool dwellFired;
        IVisualElementScheduledItem ticker;
        float suppressTapUntil;

        public DragController(VisualElement overlay)
        {
            this.overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
            overlay.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            overlay.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            overlay.RegisterCallback<PointerCancelEvent>(_ => Cancel(), TrickleDown.TrickleDown);
        }

        /// True for a moment after a drop: a cell's tap handler checks this
        /// so the release does not also open the drawer.
        public bool TapSuppressed => IsDragging || Time.realtimeSinceStartup < suppressTapUntil;

        // ------------------------------------------------------------------
        // Registration
        // ------------------------------------------------------------------

        /// Make `el` liftable. `makeGhost` builds the card that follows the
        /// finger (called at lift time).
        public void AttachSource(VisualElement el, string cellId, Func<VisualElement> makeGhost)
        {
            if (el == null) return;
            el.RegisterCallback<PointerDownEvent>(e =>
            {
                if (IsDragging || (e.pointerType == UnityEngine.UIElements.PointerType.mouse && e.button != 0)) return;
                CancelPending();
                pendPointer = e.pointerId; pendStart = e.position; pendCell = cellId;
                pendSource = el; pendGhost = makeGhost;
                el.AddToClassList("ys-pressing");
                holdTimer = overlay.schedule.Execute(Lift).StartingIn((long)(HoldSeconds * 1000f));
            });
        }

        public void AddCellTarget(VisualElement el, string cellId) => Add(el, cellId, Kind.Cell, null);
        public void AddSectionTarget(VisualElement el, string sectionKey) => Add(el, SectionPrefix + sectionKey, Kind.Section, null);
        /// Hovering here for `DwellSeconds` during a drag runs `onDwell`
        /// (a deck tab switches the card to that deck mid-drag).
        public void AddDwellTarget(VisualElement el, string id, Action onDwell) => Add(el, id, Kind.Dwell, onDwell);

        /// The "Drop here to return it to the store" zone; shown only while
        /// dragging (display is set here).
        public void SetStoreZone(VisualElement el)
        {
            storeZone = el;
            if (el != null) el.style.display = IsDragging ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void ClearTargets() { foreach (var t in targets) Unmark(t); targets.Clear(); }

        void Add(VisualElement el, string id, Kind kind, Action dwell)
        {
            if (el == null) return;
            targets.RemoveAll(t => t.el == el);
            var t = new Target { el = el, id = id, kind = kind, dwell = dwell };
            targets.Add(t);
            if (IsDragging) Mark(t);
        }

        // ------------------------------------------------------------------
        // The drag
        // ------------------------------------------------------------------

        void Lift()
        {
            holdTimer = null;
            if (pendPointer < 0 || pendSource == null || pendSource.panel == null) { CancelPending(); return; }
            source = pendSource; DraggingCell = pendCell; dragPointer = pendPointer;
            var make = pendGhost; var at = pendStart;
            pendPointer = -1; pendSource = null; pendGhost = null;
            source.RemoveFromClassList("ys-pressing");
            source.AddToClassList("ys-lifted");

            ghost = make?.Invoke() ?? new VisualElement();
            ghost.AddToClassList("ys-ghost");
            ghost.pickingMode = PickingMode.Ignore;
            ghost.style.position = Position.Absolute;
            ghost.style.rotate = new Rotate(-4f);
            ghost.style.scale = new Scale(new Vector2(1.06f, 1.06f));
            overlay.Add(ghost);
            Place(at);
            ghost.CapturePointer(dragPointer);

            Lifted?.Invoke(DraggingCell);
            targets.RemoveAll(t => t.el.panel == null);
            foreach (var t in targets) Mark(t);
            if (storeZone != null) storeZone.style.display = DisplayStyle.Flex;
            hover = null;
            ticker = overlay.schedule.Execute(Tick).Every(100);
        }

        void Place(Vector2 panelPos)
        {
            if (ghost == null) return;
            var o = overlay.worldBound.position;
            ghost.style.left = panelPos.x - o.x - 40f;
            ghost.style.top = panelPos.y - o.y - 44f;
        }

        void OnMove(PointerMoveEvent e)
        {
            if (pendPointer >= 0 && e.pointerId == pendPointer)
            {
                if (((Vector2)e.position - pendStart).sqrMagnitude > SlopUnits * SlopUnits) CancelPending();
                return;
            }
            if (!IsDragging || e.pointerId != dragPointer) return;
            Place(e.position);
            SetHover(HitTest(e.position));
        }

        void OnUp(PointerUpEvent e)
        {
            if (pendPointer >= 0 && e.pointerId == pendPointer) { CancelPending(); return; }
            if (!IsDragging || e.pointerId != dragPointer) return;
            var t = HitTest(e.position);
            string from = DraggingCell;
            Finish();
            if (t == null || !t.ok) return;
            switch (t.kind)
            {
                case Kind.Cell: onMove?.Invoke(from, t.id); break;
                case Kind.Section: onMoveToSection?.Invoke(from, t.id.Substring(SectionPrefix.Length)); break;
                case Kind.Store: onReturnToStore?.Invoke(from); break;
            }
        }

        Target HitTest(Vector2 p)
        {
            if (storeZone != null && storeZone.panel != null && storeZone.worldBound.Contains(p))
                return new Target { el = storeZone, id = "store", kind = Kind.Store, ok = true };
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                var t = targets[i];
                if (t.el.panel != null && t.el.resolvedStyle.display != DisplayStyle.None && t.el.worldBound.Contains(p)) return t;
            }
            return null;
        }

        void SetHover(Target t)
        {
            string id = t?.id;
            if ((hover?.id) == id && (hover?.el) == (t?.el)) return;
            if (hover != null) hover.el.RemoveFromClassList("ys-drop-hover");
            hover = t; hoverSince = Time.realtimeSinceStartup; dwellFired = false;
            if (hover != null && (hover.ok || hover.kind == Kind.Dwell)) hover.el.AddToClassList("ys-drop-hover");
            HoverChanged?.Invoke(id);
        }

        void Tick()
        {
            if (!IsDragging) return;
            if (hover != null && hover.kind == Kind.Dwell && !dwellFired && Time.realtimeSinceStartup - hoverSince >= DwellSeconds)
            {
                dwellFired = true;
                var act = hover.dwell;
                act?.Invoke();
            }
        }

        void Mark(Target t)
        {
            Unmark(t);
            if (t.kind == Kind.Dwell) { t.ok = true; return; }
            t.ok = t.id != DraggingCell && (canDrop == null || canDrop(DraggingCell, t.id));
            t.el.AddToClassList(t.ok ? "ys-drop-ok" : "ys-drop-no");
        }

        static void Unmark(Target t)
        {
            t.el.RemoveFromClassList("ys-drop-ok");
            t.el.RemoveFromClassList("ys-drop-no");
            t.el.RemoveFromClassList("ys-drop-hover");
        }

        void Finish()
        {
            ticker?.Pause(); ticker = null;
            if (ghost != null)
            {
                if (ghost.HasPointerCapture(dragPointer)) ghost.ReleasePointer(dragPointer);
                ghost.RemoveFromHierarchy();
            }
            ghost = null; dragPointer = -1;
            source?.RemoveFromClassList("ys-lifted"); source = null;
            foreach (var t in targets) Unmark(t);
            if (storeZone != null) storeZone.style.display = DisplayStyle.None;
            hover = null; DraggingCell = null;
            suppressTapUntil = Time.realtimeSinceStartup + 0.25f;
            HoverChanged?.Invoke(null);
            Ended?.Invoke();
        }

        void CancelPending()
        {
            holdTimer?.Pause(); holdTimer = null;
            pendSource?.RemoveFromClassList("ys-pressing");
            pendPointer = -1; pendSource = null; pendGhost = null; pendCell = null;
        }

        /// Drop everything, no callback (the sheet closed, the pointer was lost).
        public void Cancel()
        {
            CancelPending();
            if (IsDragging) Finish();
        }
    }
}
