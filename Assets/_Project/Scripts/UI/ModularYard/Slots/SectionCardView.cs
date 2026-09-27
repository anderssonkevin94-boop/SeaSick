using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard
{
    /// **The selected section: its decks and the slot plan of one deck.**
    /// (Mockup frames 1, 3 and 5.)
    ///
    /// Title + "N of 4 decks" + cannon/bunk/crate counters; a tab per deck
    /// (Hold, Deck, Upper, Top) with used/cap, the last tab being the next
    /// step up ("+ Top", dashed) or a locked tab; then the plan view of the
    /// live deck -- PORT / MID / STBD rows, stern on the left, the brass
    /// notch on a gun-port slot, filled slots with an ✕, empty ones "+ empty".
    ///
    /// `Bind` rebuilds the body (it only runs on a player action, never on a
    /// timer). Callbacks: `onCellTap(cellId)` (an empty slot opens the
    /// drawer), `onRemove(cellId)`, `onDeckTab(tabId)` (a Raise tab too).
    /// `AttachDrag` makes filled slots liftable and every slot, and every
    /// Deck tab (dwell to switch), a target; it re-registers on each Bind.
    public sealed class SectionCardView : VisualElement
    {
        public event Action<string> onCellTap;
        public event Action<string> onRemove;
        public event Action<string> onDeckTab;

        readonly Label title, sub;
        readonly Label nGuns, nBunks, nCrates;
        readonly VisualElement tabs, plan;
        readonly Dictionary<string, YardCellVm> cellById = new Dictionary<string, YardCellVm>();
        readonly Dictionary<string, VisualElement> cellEls = new Dictionary<string, VisualElement>();
        DragController drag;
        YardSectionCardVm vm;

        public SectionCardView()
        {
            AddToClassList("ys-sec");

            var head = new VisualElement(); head.AddToClassList("ys-sec-head");
            title = new Label(); title.AddToClassList("ys-sec-title");
            sub = new Label(); sub.AddToClassList("ys-sec-sub");
            head.Add(title); head.Add(sub);
            var cnt = new VisualElement(); cnt.AddToClassList("ys-cnt");
            nGuns = Counter(cnt, "cannon"); nBunks = Counter(cnt, "bunk"); nCrates = Counter(cnt, "crate");
            head.Add(cnt);
            Add(head);

            tabs = new VisualElement(); tabs.AddToClassList("ys-tabs");
            Add(tabs);
            plan = new VisualElement(); plan.AddToClassList("ys-plan");
            Add(plan);
        }

        static Label Counter(VisualElement parent, string icon)
        {
            var c = new VisualElement(); c.AddToClassList("ys-cnt-chip");
            c.Add(new YardGlyph(icon, 16f));
            var n = new Label("0"); n.AddToClassList("ys-cnt-n");
            c.Add(n); parent.Add(c);
            return n;
        }

        public void AttachDrag(DragController d)
        {
            drag = d;
            if (d == null) return;
            d.Lifted += OnLifted;
            d.Ended += () => { if (!string.IsNullOrEmpty(vm.hint)) return; sub.text = vm.sub ?? ""; };
            RegisterDrag();
        }

        public void Bind(YardSectionCardVm v)
        {
            vm = v;
            title.text = v.title ?? "";
            sub.text = (drag != null && drag.IsDragging && string.IsNullOrEmpty(v.hint)) ? "hover a deck tab to switch" : (v.hint ?? v.sub ?? "");
            nGuns.text = v.guns.ToString(); nBunks.text = v.bunks.ToString(); nCrates.text = v.crates.ToString();
            BuildTabs(v.deckTabs ?? Array.Empty<YardDeckTabVm>());
            BuildPlan(v.deck);
            RegisterDrag();
        }

        // ------------------------------------------------------------------

        readonly List<(VisualElement el, YardDeckTabVm vm)> tabEls = new List<(VisualElement, YardDeckTabVm)>();

        void BuildTabs(YardDeckTabVm[] list)
        {
            tabs.Clear(); tabEls.Clear();
            foreach (var t in list)
            {
                var tab = new VisualElement(); tab.AddToClassList("ys-tab");
                string id = t.id;
                var l = new Label(t.label ?? ""); l.AddToClassList("ys-tab-label"); tab.Add(l);
                switch (t.kind)
                {
                    case YardDeckTabKind.Deck:
                    {
                        string small = !string.IsNullOrEmpty(t.note) ? t.note : $"{t.used}/{t.cap}";
                        var s = new Label(small); s.AddToClassList("ys-tab-small"); tab.Add(s);
                        if (!string.IsNullOrEmpty(t.note) && t.hot) tab.AddToClassList("ys-tab--free");
                        break;
                    }
                    case YardDeckTabKind.Raise: tab.AddToClassList("ys-tab--add"); break;
                    case YardDeckTabKind.Locked:
                        tab.AddToClassList("ys-tab--lock");
                        if (!string.IsNullOrEmpty(t.lockReason)) { var s = new Label(t.lockReason); s.AddToClassList("ys-tab-small"); tab.Add(s); }
                        break;
                }
                if (t.selected) tab.AddToClassList("ys-tab--on");
                if (t.hot && t.kind == YardDeckTabKind.Raise) tab.AddToClassList("ys-tab--hot");
                if (t.kind != YardDeckTabKind.Locked)
                    tab.RegisterCallback<ClickEvent>(_ => { if (drag == null || !drag.TapSuppressed) onDeckTab?.Invoke(id); });
                tabs.Add(tab);
                tabEls.Add((tab, t));
            }
        }

        void BuildPlan(YardDeckVm deck)
        {
            plan.Clear(); cellById.Clear(); cellEls.Clear();
            var cells = deck.cells ?? Array.Empty<YardCellVm>();
            int cols = 0;
            foreach (var c in cells) cols = Mathf.Max(cols, c.col + 1);

            var bowh = new VisualElement(); bowh.AddToClassList("ys-bowh");
            bowh.Add(new Label(deck.sternLabel ?? "← STERN"));
            bowh.Add(new Label(deck.bowLabel ?? "BOW →"));
            plan.Add(bowh);

            foreach (YardRow row in new[] { YardRow.Port, YardRow.Mid, YardRow.Stbd })
            {
                var inRow = new YardCellVm[cols]; bool any = false; var has = new bool[cols];
                foreach (var c in cells)
                    if (c.row == row && c.col >= 0 && c.col < cols) { inRow[c.col] = c; has[c.col] = true; any = true; }
                if (!any) continue;

                var r = new VisualElement(); r.AddToClassList("ys-plan-row");
                var rl = new VisualElement(); rl.AddToClassList("ys-rl");
                var rlt = new Label(row == YardRow.Port ? "PORT" : row == YardRow.Mid ? "MID" : "STBD");
                rlt.AddToClassList("ys-rl-text"); rlt.style.rotate = new Rotate(-90f);
                rl.Add(rlt); r.Add(rl);
                for (int i = 0; i < cols; i++)
                {
                    if (!has[i]) { var gap = new VisualElement(); gap.AddToClassList("ys-cell-gap"); r.Add(gap); continue; }
                    r.Add(BuildCell(inRow[i]));
                }
                plan.Add(r);
            }
        }

        VisualElement BuildCell(YardCellVm c)
        {
            string id = c.cellId;
            cellById[id ?? ""] = c;
            var cell = new VisualElement(); cell.AddToClassList("ys-cell");
            cellEls[id ?? ""] = cell;
            if (c.isGunPort)
            {
                cell.AddToClassList("ys-cell--gun");
                var notch = new VisualElement { pickingMode = PickingMode.Ignore }; notch.AddToClassList("ys-notch");
                cell.Add(notch);
            }
            if (c.selected) cell.AddToClassList("ys-cell--sel");

            if (!string.IsNullOrEmpty(c.lockedReason))
            {
                cell.AddToClassList("ys-cell--empty"); cell.AddToClassList("ys-cell--locked");
                var l = new Label("+ " + c.lockedReason) { pickingMode = PickingMode.Ignore }; l.AddToClassList("ys-empty-small");
                cell.Add(l);
                return cell;
            }

            if (!c.hasModule)
            {
                cell.AddToClassList("ys-cell--empty");
                var plus = new Label("+") { pickingMode = PickingMode.Ignore }; plus.AddToClassList("ys-empty-plus");
                var small = new Label(c.isGunPort ? "gun port" : "empty") { pickingMode = PickingMode.Ignore }; small.AddToClassList("ys-empty-small");
                cell.Add(plus); cell.Add(small);
            }
            else
            {
                cell.AddToClassList("ys-cell--filled");
                cell.Add(ModuleFace(c.module));
                if (c.isNew)
                {
                    var n = new Label("NEW") { pickingMode = PickingMode.Ignore }; n.AddToClassList("ys-new");
                    cell.Add(n);
                }
                var del = new VisualElement(); del.AddToClassList("ys-del");
                var dot = new Label("✕") { pickingMode = PickingMode.Ignore }; dot.AddToClassList("ys-del-dot");
                del.Add(dot);
                del.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                del.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); if (drag == null || !drag.TapSuppressed) onRemove?.Invoke(id); });
                cell.Add(del);
            }
            // shown only while a drag marks this cell `ys-drop-ok` (see USS)
            var swap = new Label { pickingMode = PickingMode.Ignore }; swap.AddToClassList("ys-cell-swap");
            cell.Add(swap);
            cell.RegisterCallback<ClickEvent>(_ => { if (drag == null || !drag.TapSuppressed) onCellTap?.Invoke(id); });
            return cell;
        }

        /// The icon box + name + sub, shared by a slot and the drag ghost.
        public static VisualElement ModuleFace(YardModuleVm m)
        {
            var face = new VisualElement { pickingMode = PickingMode.Ignore }; face.AddToClassList("ys-face");
            var box = new VisualElement { pickingMode = PickingMode.Ignore }; box.AddToClassList("ys-si");
            box.Add(new YardGlyph(m.iconKey, 24f));
            face.Add(box);
            var words = new VisualElement { pickingMode = PickingMode.Ignore }; words.AddToClassList("ys-face-words");
            var n = new Label(m.label ?? "") { pickingMode = PickingMode.Ignore }; n.AddToClassList("ys-sn");
            words.Add(n);
            if (!string.IsNullOrEmpty(m.sub)) { var s = new Label(m.sub) { pickingMode = PickingMode.Ignore }; s.AddToClassList("ys-ss"); words.Add(s); }
            face.Add(words);
            return face;
        }

        // ------------------------------------------------------------------
        // Drag wiring
        // ------------------------------------------------------------------

        void RegisterDrag()
        {
            if (drag == null) return;
            foreach (var kv in cellEls)
            {
                var c = cellById[kv.Key];
                if (!string.IsNullOrEmpty(c.lockedReason)) continue;
                drag.AddCellTarget(kv.Value, c.cellId);
                if (c.hasModule)
                {
                    var m = c.module;
                    drag.AttachSource(kv.Value, c.cellId, () =>
                    {
                        var g = new VisualElement(); g.AddToClassList("ys-ghost-card");
                        g.Add(ModuleFace(new YardModuleVm { id = m.id, label = m.label, sub = "", iconKey = m.iconKey }));
                        return g;
                    });
                }
            }
            foreach (var (el, t) in tabEls)
            {
                if (t.kind != YardDeckTabKind.Deck || t.selected) continue;
                string id = t.id;
                drag.AddDwellTarget(el, "tab:" + id, () => onDeckTab?.Invoke(id));
            }
        }

        void OnLifted(string fromCell)
        {
            if (string.IsNullOrEmpty(vm.hint)) sub.text = "hover a deck tab to switch";
            string label = null;
            if (fromCell != null && cellById.TryGetValue(fromCell, out var from) && from.hasModule) label = from.module.label;
            label ??= "it";
            foreach (var kv in cellEls)
            {
                var c = cellById[kv.Key];
                var swap = kv.Value.Q<Label>(className: "ys-cell-swap");
                if (swap != null) swap.text = "swap with " + label.ToLowerInvariant();
            }
        }

        /// The element for a slot, e.g. to mark it `ys-lifted` again after a
        /// mid-drag deck switch rebuilt the plan.
        public VisualElement CellElement(string cellId) =>
            cellId != null && cellEls.TryGetValue(cellId, out var e) ? e : null;
    }
}
