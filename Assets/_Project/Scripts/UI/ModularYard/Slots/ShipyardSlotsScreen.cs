using System;
using System.Collections.Generic;
using System.Linq;
using SeaSick.Ship.Modular;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard
{
    /// **The slot shipyard (phase 2+3, 2026-09-27)** -- the screen the dry
    /// dock opens (`ShipyardLiveBridge.Open` -> `ShipyardModal.OpenSlots`).
    /// Mockup: scratchpad shipyard/index.html frames 1-6.
    ///
    /// Owns a `ShipyardSlotDraft` and the little UI state (`YardUiState`);
    /// every tap/drag becomes ONE draft command, then `Refresh` reads
    /// `draft.View()` and binds the Slots views through
    /// `ShipyardSlotsAdapter.Build`. Nothing touches the ship until Confirm
    /// (`draft.Apply`: builds + refit + save, atomic in `ShipyardService`,
    /// rolled back on failure). Tabs: Ship (strip + section card + slot
    /// grid), Workshop (build modules, free), Dock (levels, read-only while
    /// dock levels are not enforced).
    ///
    /// No 3D preview: the painted strip replaces it. Portrait phone first:
    /// one column <= 440 wide, no scrolling, Cancel/Confirm pinned at the
    /// bottom; on the desktop the same column is centred.
    public sealed class ShipyardSlotsScreen : VisualElement, IDisposable
    {
        ShipyardSlotDraft draft;
        readonly Func<ShipyardSlotDraft> restart;
        readonly ModuleLibrary lib;
        readonly Action close;
        readonly YardUiState ui = new YardUiState();
        bool disposed;
        int page;   // 0 Ship, 1 Workshop, 2 Dock
        ShipyardSlotsView view;

        readonly Label subtitle;
        readonly Button undo;
        readonly Button[] segs = new Button[3];
        readonly ShipStripView strip;
        readonly SectionCardView card;
        readonly VisualElement sectionRow, bin, workshop, dock;
        readonly Button lowerBtn, removeBtn;
        readonly TotalsStripView totals;
        readonly BlockerBarView blocker;
        readonly ConfirmRowView confirm;
        readonly ModuleDrawerView drawer;
        readonly DragController drag;

        public ShipyardSlotsScreen(ShipyardSlotDraft draft, ModuleLibrary lib, Action close, Func<ShipyardSlotDraft> restart = null)
        {
            this.draft = draft ?? throw new ArgumentNullException(nameof(draft));
            this.lib = lib; this.close = close; this.restart = restart;
            style.flexGrow = 1;
            YardPalette.Apply(this);

            var col = new VisualElement(); col.AddToClassList("ys-screen");
            Add(col);

            // header: X, title, undo
            var top = new VisualElement(); top.AddToClassList("ys-top");
            var x = new Button(Close) { tooltip = "Close the yard (unconfirmed changes are dropped)" };
            x.AddToClassList("ys-square"); x.Add(new YardGlyph("close", 20f)); top.Add(x);
            var ttl = new VisualElement(); ttl.AddToClassList("ys-ttl");
            var tb = new Label("Shipyard"); tb.AddToClassList("ys-ttl-b");
            subtitle = new Label(); subtitle.AddToClassList("ys-ttl-s");
            ttl.Add(tb); ttl.Add(subtitle); top.Add(ttl);
            undo = new Button(Undo) { tooltip = "Undo" }; undo.AddToClassList("ys-square"); undo.Add(new YardGlyph("undo", 22f)); top.Add(undo);
            col.Add(top);

            var seg = new VisualElement(); seg.AddToClassList("ys-seg");
            string[] names = { "Ship", "Workshop", "Dock" };
            for (int i = 0; i < 3; i++)
            {
                int p = i;
                var b = new Button(() => SetPage(p)) { text = names[i] };
                b.AddToClassList("ys-seg-btn"); b.style.height = 44;
                segs[i] = b; seg.Add(b);
            }
            col.Add(seg);

            // ---- Ship page
            strip = new ShipStripView(); col.Add(strip);
            card = new SectionCardView(); col.Add(card);
            sectionRow = new VisualElement();
            sectionRow.style.flexDirection = FlexDirection.Row;
            lowerBtn = SmallButton(sectionRow, "Lower deck", LowerDeck);
            removeBtn = SmallButton(sectionRow, "Remove section", RemoveSection);
            col.Add(sectionRow);
            bin = new VisualElement(); bin.AddToClassList("ys-bin");
            bin.Add(new Label("Drop here to return it to the store"));
            col.Add(bin);
            totals = new TotalsStripView(); col.Add(totals);

            // ---- Workshop / Dock pages (built on Refresh)
            workshop = new VisualElement(); workshop.style.flexShrink = 1; workshop.style.minHeight = 0; col.Add(workshop);
            dock = new VisualElement(); dock.style.flexShrink = 1; dock.style.minHeight = 0; col.Add(dock);

            var spacer = new VisualElement(); spacer.AddToClassList("ys-spacer"); col.Add(spacer);
            blocker = new BlockerBarView(); col.Add(blocker);
            confirm = new ConfirmRowView(); col.Add(confirm);
            drawer = new ModuleDrawerView(); Add(drawer);

            // ---- drag
            drag = new DragController(this);
            drag.SetStoreZone(bin);
            drag.canDrop = CanDrop;
            card.AttachDrag(drag);
            drag.Lifted += cell =>
            {
                ui.dragModule = ModuleAt(cell);
                strip.ShowDropHints(true, "drop on a section to move it there");
                foreach (var (key, _) in strip.SectionRects()) drag.AddSectionTarget(strip.SectionHitArea(key), key);
                totals.style.display = blocker.style.display = confirm.style.display = sectionRow.style.display = DisplayStyle.None;
            };
            drag.HoverChanged += id => strip.SetDropHover(id != null && id.StartsWith(DragController.SectionPrefix) ? id.Substring(DragController.SectionPrefix.Length) : null);
            drag.Ended += () =>
            {
                ui.dragModule = null;
                strip.ShowDropHints(false);
                totals.style.display = confirm.style.display = DisplayStyle.Flex;
                Refresh();
            };
            drag.onMove += (from, to) =>
            {
                if (ShipyardSlotsAdapter.ParseCell(from, out var fs, out var fd, out var fc) && ShipyardSlotsAdapter.ParseCell(to, out var ts, out var td, out var tc))
                    draft.Move(fs, fd, fc, ts, td, tc);
            };
            drag.onMoveToSection += MoveToSection;
            drag.onReturnToStore += cell => { if (ShipyardSlotsAdapter.ParseCell(cell, out var s, out var d, out var c)) draft.Remove(s, d, c); };

            // ---- taps
            strip.onSectionTap += key =>
            {
                if (drag.IsDragging) return;
                if (key == ui.section) ui.showPlus = !ui.showPlus;
                else ui.showPlus = false;
                ui.section = key; ui.raisePreview = false; ui.drawerCell = null;
                Refresh();
            };
            strip.onPlusTap += i =>
            {
                // chip i sits before sections[i]; sections[0] is the stern
                if (draft.AddSection(i - 1)) { ui.section = ShipAssembler.MiddleKey(i - 1); ui.deck = SlotDeck.Deck; ui.showPlus = false; Refresh(); }
            };
            card.onDeckTab += id =>
            {
                if (id == ShipyardSlotsAdapter.RaiseTab)
                {
                    if (drag.IsDragging) return;
                    ui.raisePreview = !ui.raisePreview; ui.drawerCell = null;
                }
                else if (int.TryParse(id, out int d)) { ui.deck = d; ui.raisePreview = false; }
                Refresh();
                if (drag.IsDragging) card.CellElement(drag.DraggingCell)?.AddToClassList("ys-lifted");
            };
            card.onCellTap += cell =>
            {
                if (ui.applied) return;
                if (ShipyardSlotsAdapter.ParseCell(cell, out _, out _, out _) && ModuleAt(cell) == null) { ui.drawerCell = cell; ui.raisePreview = false; Refresh(); }
            };
            card.onRemove += cell => { if (ShipyardSlotsAdapter.ParseCell(cell, out var s, out var d, out var c)) draft.Remove(s, d, c); };
            drawer.onClose += () => { ui.drawerCell = null; Refresh(); };
            drawer.onPick += Pick;
            blocker.onFix += Fix;
            confirm.onCancel += () => { ui.raisePreview = false; Refresh(); };
            confirm.onConfirm += Confirm;

            this.draft.Changed += Refresh;
            SetPage(0);
        }

        static Button SmallButton(VisualElement parent, string text, Action a)
        {
            var b = new Button(a) { text = text };
            b.AddToClassList("ys-mb");
            b.style.flexGrow = 1; b.style.flexBasis = 0; b.style.marginRight = 6; b.style.marginLeft = 0;
            parent.Add(b);
            return b;
        }

        // ------------------------------------------------------------------
        // Commands
        // ------------------------------------------------------------------

        void Undo()
        {
            if (drag.IsDragging) return;
            ui.raisePreview = false; ui.drawerCell = null;
            draft.Undo();
        }

        void LowerDeck() { if (ui.section != null) draft.LowerDeck(ui.section); }

        void RemoveSection()
        {
            if (ui.section == null) return;
            string key = ui.section;
            if (draft.RemoveSection(key)) { ui.section = null; ui.drawerCell = null; Refresh(); }
        }

        string ModuleAt(string cellKey)
        {
            if (view == null || !ShipyardSlotsAdapter.ParseCell(cellKey, out var s, out var d, out var c)) return null;
            var sec = view.sections.FirstOrDefault(x => x.key == s);
            if (sec == null || d < 0 || d >= sec.decks.Count) return null;
            return sec.decks[d].cells.FirstOrDefault(x => x.cellId == c)?.moduleId;
        }

        bool CanDrop(string from, string target)
        {
            if (!ShipyardSlotsAdapter.ParseCell(from, out var fs, out var fd, out var fc)) return false;
            var cfg = draft.Snapshot();
            var a = SlotModel.At(cfg, fs, fd, fc);
            if (a == null) return false;
            if (target.StartsWith(DragController.SectionPrefix))
            {
                string key = target.Substring(DragController.SectionPrefix.Length);
                return key != fs && FirstFree(cfg, key, a) != null;
            }
            if (!ShipyardSlotsAdapter.ParseCell(target, out var ts, out var td, out var tc)) return false;
            if (SlotModel.CanPlace(cfg, lib, ts, td, tc, a.moduleId, draft.DockLevel, a) != null) return false;
            var b = SlotModel.At(cfg, ts, td, tc);
            return b == null || SlotModel.CanPlace(cfg, lib, fs, fd, fc, b.moduleId, draft.DockLevel, b) == null;
        }

        /// The first empty cell of `section` (Deck first, then up, then the
        /// Hold) that takes `fit`'s module, as (deck, cell).
        (int deck, string cell)? FirstFree(ShipConfiguration cfg, string section, SlotFit fit)
        {
            var sec = view?.sections.FirstOrDefault(s => s.key == section);
            if (sec == null) return null;
            var order = new List<int>();
            for (int d = SlotDeck.Deck; d <= sec.topDeck; d++) order.Add(d);
            order.Add(SlotDeck.Hold);
            foreach (int d in order)
                foreach (var c in sec.decks[d].cells)
                    if (c.moduleId == null && SlotModel.CanPlace(cfg, lib, section, d, c.cellId, fit.moduleId, draft.DockLevel, fit) == null)
                        return (d, c.cellId);
            return null;
        }

        void MoveToSection(string from, string section)
        {
            if (!ShipyardSlotsAdapter.ParseCell(from, out var fs, out var fd, out var fc)) return;
            var cfg = draft.Snapshot();
            var a = SlotModel.At(cfg, fs, fd, fc);
            if (a == null) return;
            var free = FirstFree(cfg, section, a);
            if (free == null) return;
            if (draft.Move(fs, fd, fc, section, free.Value.deck, free.Value.cell)) { ui.section = section; ui.deck = free.Value.deck; Refresh(); }
        }

        void Pick(string moduleId)
        {
            if (!ShipyardSlotsAdapter.ParseCell(ui.drawerCell, out var s, out var d, out var c)) return;
            var opt = draft.Options(s, d, c).FirstOrDefault(o => o.moduleId == moduleId);
            if (opt == null) return;
            ui.drawerCell = null;
            bool ok;
            switch (opt.action)
            {
                case "place": ok = draft.Place(s, d, c, moduleId); break;
                case "build-place": ok = draft.BuildAndPlace(s, d, c, moduleId); break;
                case "move-here":
                    ok = ShipyardSlotsAdapter.FindFit(view, moduleId, out var fs, out var fd, out var fc) && draft.Move(fs, fd, fc, s, d, c);
                    break;
                default: ok = false; break;
            }
            if (!ok) Refresh();
        }

        void Fix(string fixId)
        {
            if (string.IsNullOrEmpty(fixId)) return;
            if (fixId == ShipyardSlotsAdapter.FixRestart) { Restart(); return; }
            if (fixId == "upgrade-dock") { SetPage(2); return; }
            if (fixId.StartsWith("remove:") && ShipyardSlotsAdapter.ParseCell(fixId.Substring(7), out var s, out var d, out var c)) { draft.Remove(s, d, c); return; }
            if (fixId.StartsWith("build:")) { draft.BuildModule(fixId.Substring(6)); return; }
            if (fixId == "add-bunk") AddBunk();
        }

        /// Frame 1's fix: build a bunk into the first slot that takes one
        /// (the selected section first, then the rest, Deck before Hold).
        void AddBunk()
        {
            if (view == null) return;
            var order = view.sections.OrderBy(s => s.key == ui.section ? 0 : 1).ToList();
            foreach (var sec in order)
                for (int step = 0; step <= sec.topDeck; step++)
                {
                    int d = step == sec.topDeck ? SlotDeck.Hold : step + 1;
                    if (d > sec.topDeck) continue;
                    foreach (var c in sec.decks[d].cells)
                    {
                        if (c.moduleId != null) continue;
                        var o = draft.Options(sec.key, d, c.cellId).FirstOrDefault(x => x.moduleId == ModuleCatalog.Bunk);
                        if (o == null || (o.action != "place" && o.action != "build-place")) continue;
                        bool ok = o.action == "place" ? draft.Place(sec.key, d, c.cellId, ModuleCatalog.Bunk) : draft.BuildAndPlace(sec.key, d, c.cellId, ModuleCatalog.Bunk);
                        if (ok) { ui.section = sec.key; ui.deck = d; Refresh(); }
                        return;
                    }
                }
            ui.showPlus = true; Refresh();   // no free slot: point at the '+' chips
        }

        void Confirm()
        {
            if (ui.applied) { Close(); return; }
            if (ui.raisePreview)
            {
                ui.raisePreview = false;
                string key = ui.section;
                if (key != null && draft.RaiseDeck(key))
                {
                    var sec = draft.View().sections.FirstOrDefault(s => s.key == key);
                    if (sec != null) { ui.deck = sec.topDeck; Refresh(); }
                }
                else Refresh();
                return;
            }
            if (draft.Apply()) { ui.applied = true; Refresh(); return; }
            // STALE_DRAFT: the live ship moved under the plan -- offer Start over.
            var live = ShipyardService.Player;
            if (live != null && !live.Current.ValueEquals(draft.Baseline)) ui.stale = true;
            Refresh();
        }

        void Restart()
        {
            if (restart == null) return;
            var fresh = restart();
            if (fresh == null) return;
            draft.Changed -= Refresh;
            draft = fresh;
            draft.Changed += Refresh;
            ui.stale = false; ui.applied = false; ui.drawerCell = null; ui.raisePreview = false; ui.showPlus = false;
            Refresh();
        }

        void SetPage(int p)
        {
            page = Mathf.Clamp(p, 0, 2);
            for (int i = 0; i < 3; i++) segs[i].EnableInClassList("ys-seg-btn--on", i == page);
            if (page != 0) { ui.drawerCell = null; ui.raisePreview = false; if (drag.IsDragging) drag.Cancel(); }
            Refresh();
        }

        // ------------------------------------------------------------------
        // Read + bind
        // ------------------------------------------------------------------

        void Refresh()
        {
            if (disposed) return;
            view = draft.View();
            var vm = ShipyardSlotsAdapter.Build(view, ui, lib, draft.Options, draft.Baseline);

            subtitle.text = drag.IsDragging && ui.dragModule != null
                ? $"Moving a {(ShipyardSlotsAdapter.Row(view, ui.dragModule)?.name ?? "module").ToLowerInvariant()}"
                : $"Home berth · dry dock {SlotModel.Roman(view.dockLevel)}";
            undo.SetEnabled(draft.CanUndo && !ui.applied);

            bool ship = page == 0;
            var show = ship ? DisplayStyle.Flex : DisplayStyle.None;
            strip.style.display = card.style.display = show;
            totals.style.display = ship && !drag.IsDragging ? DisplayStyle.Flex : DisplayStyle.None;
            workshop.style.display = page == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            dock.style.display = page == 2 ? DisplayStyle.Flex : DisplayStyle.None;

            strip.Bind(vm.sections, vm.plusChips, vm.stripNote);
            card.Bind(vm.card);
            totals.Bind(vm.totals);
            blocker.Bind(vm.blockers);
            if (drag.IsDragging) blocker.style.display = DisplayStyle.None;
            confirm.Bind(vm.actions);
            confirm.style.display = drag.IsDragging ? DisplayStyle.None : DisplayStyle.Flex;
            drawer.Bind(page == 0 ? vm.drawer : default);

            var sel = ShipyardSlotsAdapter.Selected(view, ui);
            bool anySectionAction = sel != null && !ui.applied && (sel.canLower || sel.canRemove);
            sectionRow.style.display = ship && anySectionAction && !drag.IsDragging && !ui.raisePreview ? DisplayStyle.Flex : DisplayStyle.None;
            if (sel != null)
            {
                lowerBtn.text = sel.canLower ? $"Lower · drop {SlotDeck.Name(sel.topDeck)}" : "Lower deck";
                lowerBtn.SetEnabled(sel.canLower); lowerBtn.tooltip = sel.lowerReason;
                lowerBtn.style.display = sel.canLower ? DisplayStyle.Flex : DisplayStyle.None;
                removeBtn.text = $"Remove {sel.name}";
                removeBtn.SetEnabled(sel.canRemove); removeBtn.tooltip = sel.removeReason;
                removeBtn.style.display = sel.canRemove ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (page == 1) BuildWorkshop();
            if (page == 2) BuildDock();
        }

        // ---- Workshop: build modules into the store (free, instant, undoable)

        void BuildWorkshop()
        {
            workshop.Clear();
            var head = new Label("Build modules into the store. Free and instant while the economy is set; they are made when you confirm.");
            head.AddToClassList("ys-hint"); head.style.marginBottom = 7;
            workshop.Add(head);
            foreach (var r in view.store)
            {
                var row = new VisualElement(); row.AddToClassList("ys-mod");
                row.style.width = Length.Percent(100); row.style.minHeight = 0;
                row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center;
                if (r.locked) row.AddToClassList("ys-mod--lock");
                var mi = new VisualElement(); mi.AddToClassList("ys-mi");
                var g = new YardGlyph(ShipyardSlotsAdapter.IconKey(r.moduleId), 26f); if (r.locked) g.Alpha = 0.35f;
                mi.Add(g); row.Add(mi);
                var words = new VisualElement(); words.AddToClassList("ys-face-words"); words.style.flexGrow = 1; words.style.flexShrink = 1;
                var n = new Label(r.name); n.AddToClassList("ys-mn");
                var s = new Label($"{ShipyardSlotsAdapter.Stats(r)}\n{r.inStore} in store · {r.aboard} aboard"); s.AddToClassList("ys-ms");
                words.Add(n); words.Add(s); row.Add(words);
                string id = r.moduleId;
                var b = new Button(() => draft.BuildModule(id)) { text = r.locked ? $"Dry dock {SlotModel.Roman(r.dockLevel)}" : "Build · free" };
                b.AddToClassList("ys-mb"); if (!r.locked) b.AddToClassList("ys-mb--pri");
                b.style.width = 112; b.style.flexShrink = 0;
                b.SetEnabled(!r.locked && !ui.applied);
                row.Add(b);
                workshop.Add(row);
            }
        }

        // ---- Dock: the levels, read-only for now

        void BuildDock()
        {
            dock.Clear();
            var levels = lib?.Standards?.slotModel?.dockLevels;
            int cur = view.dockLevel;
            if (levels == null) { dock.Add(new Label("No dock levels in the data.")); return; }
            foreach (var l in levels)
            {
                var row = new VisualElement(); row.AddToClassList("ys-mod");
                row.style.width = Length.Percent(100); row.style.minHeight = 0; row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center;
                row.style.paddingTop = row.style.paddingBottom = 5;
                if (l.level == cur + 1) row.AddToClassList("ys-mod--on");
                else if (l.level > cur + 1) row.AddToClassList("ys-mod--lock");
                var badge = new Label(SlotModel.Roman(l.level)); badge.AddToClassList("ys-mn");
                badge.style.width = 34; badge.style.unityTextAlign = TextAnchor.MiddleCenter; row.Add(badge);
                var words = new VisualElement(); words.AddToClassList("ys-face-words"); words.style.flexGrow = 1; words.style.flexShrink = 1;
                var n = new Label(l.name + (l.level == cur ? "  · here" : l.level == cur + 1 ? "  · next" : "")); n.AddToClassList("ys-mn");
                var s = new Label(DockLine(l)); s.AddToClassList("ys-ms");
                words.Add(n); words.Add(s); row.Add(words);
                dock.Add(row);
            }
            var next = levels.FirstOrDefault(l => l.level == cur + 1);
            string cost = next?.upgradeCost == null ? "" : string.Join(", ", next.upgradeCost.Select(c => $"{c.count} {c.item}"));
            var note = new Label(next != null
                ? $"<b>Level {SlotModel.Roman(next.level)}</b> costs {cost}. {next.feel}\nDock levels are not enforced yet: during the playtest every level is open."
                : "Dock levels are not enforced yet: during the playtest every level is open.");
            note.AddToClassList("ys-hint"); note.enableRichText = true;
            dock.Add(note);
        }

        static string DockLine(DockLevelDef l)
        {
            var bits = new List<string> { $"{l.maxSections} sections" };
            bits.Add(l.wideBeam ? "wide beam" : "standard beam");
            if (l.maxDeck >= SlotDeck.Upper) bits.Add(l.maxUpperSections < 0 ? "upper decks on all" : $"upper on {l.maxUpperSections}");
            if (l.maxDeck >= SlotDeck.Top) bits.Add(l.maxTopSections < 0 ? "top decks on all" : $"top on {l.maxTopSections}");
            return string.Join(" · ", bits);
        }

        // ------------------------------------------------------------------

        void Close() { Dispose(); RemoveFromHierarchy(); close?.Invoke(); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (drag.IsDragging) drag.Cancel();
            draft.Changed -= Refresh;
        }
    }
}
