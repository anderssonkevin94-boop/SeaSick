using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.ModularYard
{
    /// **Dev preview of the slot screen, on fake data -- no backend.**
    ///
    /// `ShipyardSlotsPreview.Open()` (or `Open(2)` for the drawer, `Open(4)`
    /// for the '+' chips, `Open(5)` for a raise preview) builds the whole
    /// screen from the Slots views over a little in-memory ship, so it can
    /// be captured and played with before the Slots API lands:
    /// `unity cmd eval --code 'SeaSick.UI.ModularYard.ShipyardSlotsPreview.Open()'`
    /// then `capture_game_view`; `Close()` removes it.
    ///
    /// Everything works against the fake: tap a section / deck tab, tap an
    /// empty slot -> drawer -> Place / Build, ✕ returns to store, press and
    /// hold a module to move / swap / drop on a strip section / return it,
    /// "+ Top" raises, tapping the selected section toggles the '+' chips.
    /// The fake's rules (cannon only on a gun port; lookout top deck only;
    /// repair bench locked) are placeholders, not the design.
    ///
    /// The frame (header, Ship/Workshop/Dock) lives here, not in a view
    /// class, because the adapter's screen will own its own header.
    public sealed class ShipyardSlotsPreview : MonoBehaviour
    {
        static ShipyardSlotsPreview active;
        PanelSettings settings;
        UIDocument document;

        public static void Open() => Open(1);

        public static void Open(int frame)
        {
            Close();
            var template = Resources.Load<PanelSettings>("UI/SheetPanel");
            if (template == null) { Debug.LogError("[ShipyardSlotsPreview] UI/SheetPanel is missing."); return; }
            var go = new GameObject("Shipyard slots preview");
            active = go.AddComponent<ShipyardSlotsPreview>();
            active.settings = Instantiate(template);
            active.settings.sortingOrder = 1001;
            active.settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            active.settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            active.settings.referenceResolution = new Vector2Int(390, 844);
            active.document = go.AddComponent<UIDocument>();
            active.document.panelSettings = active.settings;
            active.Build(frame);
            active.Update();
        }

        public static void Close()
        {
            if (active != null)
            {
                var go = active.gameObject; active = null;
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
            }
        }

        void Update()
        {
            if (document == null || settings == null) return;
            // phone: match width (390 units across); desk: match height so
            // the 440-wide column spans the full height, centred.
            settings.match = Screen.width > Screen.height ? 1f : 0f;
            var root = document.rootVisualElement;
            float scale = settings.match > 0.5f ? 844f / Mathf.Max(1, Screen.height) : 390f / Mathf.Max(1, Screen.width);
            var safe = Screen.safeArea;
            root.style.paddingLeft = safe.xMin * scale;
            root.style.paddingRight = (Screen.width - safe.xMax) * scale;
            root.style.paddingTop = (Screen.height - safe.yMax) * scale;
            root.style.paddingBottom = safe.yMin * scale;
            root.style.backgroundColor = YardPalette.Page;
        }

        void OnDestroy() { if (settings != null) { if (Application.isPlaying) Destroy(settings); else DestroyImmediate(settings); } }

        // ==================================================================
        // The fake ship
        // ==================================================================

        sealed class FakeSection { public string key; public YardSectionKind kind; public int decks; public Dictionary<string, string> mods = new Dictionary<string, string>(); }

        readonly List<FakeSection> ship = new List<FakeSection>();
        readonly Dictionary<string, int> store = new Dictionary<string, int>();
        string selKey; int selDeck = 1; string drawerCell; bool showPlus; bool raisePreview; int crew = 10; int nextKey;

        static readonly string[] DeckNames = { "Hold", "Deck", "Upper", "Top" };
        const int DecksMax = 4;

        static readonly Dictionary<string, (string label, string sub, string stats)> Catalog = new Dictionary<string, (string, string, string)>
        {
            ["cannon"] = ("Cannon", "1 hand", "needs 1 hand · 500 kg"),
            ["bunk"] = ("Bunk", "2 berths", "2 berths · 150 kg"),
            ["crate"] = ("Crate", "4 cargo", "4 cargo · 120 kg"),
            ["pump"] = ("Bilge pump", "bails", "bails faster · 1 per ship"),
            ["lookout"] = ("Lookout", "spots sails", "top-most deck only"),
            ["bench"] = ("Repair bench", "mends", "mends hull at sea"),
        };

        void Seed(int frame)
        {
            ship.Clear(); store.Clear();
            FakeSection S(YardSectionKind k, int decks) { var s = new FakeSection { key = "s" + (nextKey++), kind = k, decks = decks }; ship.Add(s); return s; }
            var stern = S(YardSectionKind.Stern, 3); var m1 = S(YardSectionKind.Mid, 3); var m2 = S(YardSectionKind.Mid, 2); var bow = S(YardSectionKind.Bow, 2);
            stern.mods[Cell(1, YardRow.Port, 0)] = "cannon"; stern.mods[Cell(1, YardRow.Stbd, 0)] = "cannon"; stern.mods[Cell(0, YardRow.Mid, 0)] = "pump";
            stern.mods[Cell(2, YardRow.Port, 0)] = "cannon"; stern.mods[Cell(2, YardRow.Stbd, 0)] = "cannon";
            m1.mods[Cell(1, YardRow.Port, 0)] = "cannon"; m1.mods[Cell(1, YardRow.Port, 1)] = "bunk";
            m1.mods[Cell(1, YardRow.Mid, 0)] = "crate"; m1.mods[Cell(1, YardRow.Stbd, 0)] = "cannon"; m1.mods[Cell(1, YardRow.Stbd, 1)] = "crate";
            m1.mods[Cell(0, YardRow.Port, 0)] = "crate"; m1.mods[Cell(2, YardRow.Port, 0)] = "cannon"; m1.mods[Cell(2, YardRow.Stbd, 0)] = "cannon";
            m2.mods[Cell(1, YardRow.Port, 1)] = "bunk"; m2.mods[Cell(0, YardRow.Mid, 1)] = "crate"; bow.mods[Cell(1, YardRow.Mid, 0)] = "bunk";
            store["bunk"] = 2; store["crate"] = 1;
            selKey = m1.key; selDeck = 1; drawerCell = null; crew = 10;
            showPlus = frame == 4; raisePreview = frame == 5;
            if (frame == 5) { selKey = m2.key; }
            if (frame == 2) { m1.mods.Remove(Cell(1, YardRow.Port, 0)); drawerCell = Cell(1, YardRow.Port, 0); }
        }

        static string Cell(int deck, YardRow row, int col) => $"{deck}:{(int)row}:{col}";
        static (int deck, YardRow row, int col) Parse(string id)
        {
            var p = id.Split(':');
            return (int.Parse(p[0]), (YardRow)int.Parse(p[1]), int.Parse(p[2]));
        }
        // cell ids handed to the views are "<sectionKey>/<deck>:<row>:<col>"
        static string Full(FakeSection s, string cell) => s.key + "/" + cell;
        (FakeSection s, string cell) Split(string full)
        {
            int i = full.IndexOf('/');
            return (ship.FirstOrDefault(x => x.key == full.Substring(0, i)), full.Substring(i + 1));
        }

        static bool IsGunPort(int deck, YardRow row, int col) => deck >= 1 && col == 0 && row != YardRow.Mid;
        FakeSection Sel => ship.FirstOrDefault(s => s.key == selKey) ?? ship[0];

        bool Fits(string module, string full)
        {
            var (s, cell) = Split(full);
            if (s == null || module == null) return true;
            var (deck, row, col) = Parse(cell);
            if (module == "cannon" && !IsGunPort(deck, row, col)) return false;
            if (module == "lookout" && deck != s.decks - 1) return false;
            return true;
        }

        // ==================================================================
        // The screen
        // ==================================================================

        ShipStripView strip; SectionCardView card; TotalsStripView totals; BlockerBarView blocker;
        ConfirmRowView confirm; ModuleDrawerView drawer; DragController drag; VisualElement bin;

        void Build(int frame)
        {
            Seed(frame);
            var root = document.rootVisualElement;
            root.Clear();
            var host = new VisualElement(); host.style.flexGrow = 1;
            YardPalette.Apply(host);
            root.Add(host);

            var col = new VisualElement(); col.AddToClassList("ys-screen");
            host.Add(col);

            // header + tabs (preview-only frame)
            var top = new VisualElement(); top.AddToClassList("ys-top");
            var x = new Button(Close); x.AddToClassList("ys-square"); x.Add(new YardGlyph("close", 20f)); top.Add(x);
            var ttl = new VisualElement(); ttl.AddToClassList("ys-ttl");
            var tb = new Label("Shipyard"); tb.AddToClassList("ys-ttl-b");
            var ts = new Label("Home berth · dry dock III"); ts.AddToClassList("ys-ttl-s");
            ttl.Add(tb); ttl.Add(ts); top.Add(ttl);
            var undo = new Button(() => Build(1)); undo.AddToClassList("ys-square"); undo.Add(new YardGlyph("undo", 22f)); top.Add(undo);
            col.Add(top);
            var seg = new VisualElement(); seg.AddToClassList("ys-seg");
            foreach (var (name, on) in new[] { ("Ship", true), ("Workshop", false), ("Dock", false) })
            { var b = new Button { text = name }; b.AddToClassList("ys-seg-btn"); if (on) b.AddToClassList("ys-seg-btn--on"); seg.Add(b); }
            col.Add(seg);

            strip = new ShipStripView(); col.Add(strip);
            card = new SectionCardView(); col.Add(card);
            bin = new VisualElement(); bin.AddToClassList("ys-bin");
            bin.Add(new Label("Drop here to return it to the store"));
            col.Add(bin);
            totals = new TotalsStripView(); col.Add(totals);
            var spacer = new VisualElement(); spacer.AddToClassList("ys-spacer"); col.Add(spacer);
            blocker = new BlockerBarView(); col.Add(blocker);
            confirm = new ConfirmRowView(); col.Add(confirm);
            drawer = new ModuleDrawerView(); host.Add(drawer);

            drag = new DragController(host);
            drag.SetStoreZone(bin);
            drag.canDrop = CanDrop;
            card.AttachDrag(drag);
            drag.Lifted += _ =>
            {
                strip.ShowDropHints(true, "drop on a section to move it there");
                foreach (var (key, _) in strip.SectionRects()) drag.AddSectionTarget(strip.SectionHitArea(key), key);
                totals.style.display = blocker.style.display = confirm.style.display = DisplayStyle.None;
            };
            drag.HoverChanged += id => strip.SetDropHover(id != null && id.StartsWith(DragController.SectionPrefix) ? id.Substring(DragController.SectionPrefix.Length) : null);
            drag.Ended += () => { strip.ShowDropHints(false); totals.style.display = confirm.style.display = DisplayStyle.Flex; Refresh(); };
            drag.onMove += Move;
            drag.onMoveToSection += MoveToSection;
            drag.onReturnToStore += full => { var (s, c) = Split(full); if (s != null && s.mods.TryGetValue(c, out var m)) { s.mods.Remove(c); Add(m, 1); } Refresh(); };

            strip.onSectionTap += key =>
            {
                if (drag.IsDragging) return;
                if (key == selKey) showPlus = !showPlus;
                selKey = key; selDeck = Mathf.Min(selDeck, Sel.decks - 1); raisePreview = false; Refresh();
            };
            strip.onPlusTap += i =>
            {
                if (ship.Count(s => s.kind != YardSectionKind.Stern && s.kind != YardSectionKind.Bow) >= 3) return;
                var s = new FakeSection { key = "s" + (nextKey++), kind = YardSectionKind.Mid, decks = 2 };
                ship.Insert(i, s); selKey = s.key; selDeck = 1; showPlus = false; Refresh();
            };
            card.onDeckTab += id =>
            {
                if (id == "raise") { if (raisePreview) { Sel.decks = Mathf.Min(DecksMax, Sel.decks + 1); selDeck = Sel.decks - 1; raisePreview = false; } else raisePreview = true; }
                else { selDeck = int.Parse(id); raisePreview = false; }
                Refresh();
                if (drag.IsDragging) card.CellElement(drag.DraggingCell)?.AddToClassList("ys-lifted");
            };
            card.onCellTap += full =>
            {
                var (s, c) = Split(full);
                if (s != null && !s.mods.ContainsKey(c)) { drawerCell = full; Refresh(); }
            };
            card.onRemove += full => { var (s, c) = Split(full); if (s != null && s.mods.TryGetValue(c, out var m)) { s.mods.Remove(c); Add(m, 1); } Refresh(); };
            drawer.onClose += () => { drawerCell = null; Refresh(); };
            drawer.onPick += Pick;
            blocker.onFix += id => { if (id == "leave") crew = Berths(); Refresh(); };
            confirm.onCancel += () => { raisePreview = false; Refresh(); };
            confirm.onConfirm += () =>
            {
                if (raisePreview) { Sel.decks = Mathf.Min(DecksMax, Sel.decks + 1); selDeck = Sel.decks - 1; raisePreview = false; Refresh(); }
                else Debug.Log("[ShipyardSlotsPreview] Confirm (fake: nothing to apply).");
            };

            if (drawerCell != null) drawerCell = Full(Sel, drawerCell);
            Refresh();
        }

        void Add(string module, int n) { store.TryGetValue(module, out var have); store[module] = have + n; }

        bool CanDrop(string fromFull, string target)
        {
            var (fs, fc) = Split(fromFull);
            if (fs == null || !fs.mods.TryGetValue(fc, out var mod)) return false;
            if (target.StartsWith(DragController.SectionPrefix))
            {
                var s = ship.FirstOrDefault(x => x.key == target.Substring(DragController.SectionPrefix.Length));
                return s != null && s != fs && s.kind != YardSectionKind.New && FirstFree(s, mod) != null;
            }
            if (!Fits(mod, target)) return false;
            var (ts, tc) = Split(target);
            return ts == null || !ts.mods.TryGetValue(tc, out var other) || Fits(other, fromFull);
        }

        string FirstFree(FakeSection s, string mod)
        {
            for (int d = 0; d < s.decks; d++)
                foreach (YardRow r in new[] { YardRow.Port, YardRow.Mid, YardRow.Stbd })
                    for (int c = 0; c < 2; c++)
                    {
                        string id = Cell(d, r, c);
                        if (!s.mods.ContainsKey(id) && Fits(mod, Full(s, id))) return id;
                    }
            return null;
        }

        void Move(string fromFull, string toFull)
        {
            var (fs, fc) = Split(fromFull); var (ts, tc) = Split(toFull);
            if (fs == null || ts == null || !fs.mods.TryGetValue(fc, out var m)) return;
            bool swap = ts.mods.TryGetValue(tc, out var other);
            fs.mods.Remove(fc);
            if (swap) fs.mods[fc] = other;
            ts.mods[tc] = m;
            Refresh();
        }

        void MoveToSection(string fromFull, string key)
        {
            var (fs, fc) = Split(fromFull); var s = ship.FirstOrDefault(x => x.key == key);
            if (fs == null || s == null || !fs.mods.TryGetValue(fc, out var m)) return;
            var free = FirstFree(s, m); if (free == null) return;
            fs.mods.Remove(fc); s.mods[free] = m; Refresh();
        }

        void Pick(string module)
        {
            if (drawerCell == null) return;
            var (s, c) = Split(drawerCell);
            if (module == "pump")
                foreach (var o in ship) foreach (var k in o.mods.Where(kv => kv.Value == "pump").Select(kv => kv.Key).ToList()) o.mods.Remove(k);
            else if (store.TryGetValue(module, out var have) && have > 0) store[module] = have - 1;
            s.mods[c] = module; drawerCell = null; Refresh();
        }

        int Count(string m) => ship.Sum(s => s.mods.Values.Count(v => v == m));
        int Berths() => Count("bunk") * 2 + 4;

        // ==================================================================
        // Fake -> view model
        // ==================================================================

        void Refresh()
        {
            var vm = new YardVm();
            var sel = Sel;
            int mid = 0;
            vm.sections = ship.Select(s => new YardSectionVm
            {
                key = s.key, kind = s.kind, decksUsed = s.decks, decksMax = DecksMax, selected = s.key == selKey, canRemove = s.kind == YardSectionKind.Mid,
                label = s.kind == YardSectionKind.Stern ? "Stern" : s.kind == YardSectionKind.Bow ? "Bow" : "Mid " + (++mid),
                raisePreview = raisePreview && s.key == selKey && s.decks < DecksMax, raiseLabel = "+ " + (s.decks < DecksMax ? DeckNames[s.decks] : ""),
            }).ToArray();
            int mids = ship.Count(s => s.kind == YardSectionKind.Mid);
            vm.plusChips = showPlus && mids < 3 ? Enumerable.Range(1, ship.Count - 1).Select(i => new YardPlusChipVm { index = i }).ToArray() : null;

            // the card
            var tabs = new List<YardDeckTabVm>();
            for (int d = 0; d < sel.decks; d++)
            {
                int used = sel.mods.Keys.Count(k => Parse(k).deck == d);
                tabs.Add(new YardDeckTabVm { id = d.ToString(), label = DeckNames[d], kind = YardDeckTabKind.Deck, used = used, cap = 6, selected = d == selDeck && !raisePreview });
            }
            if (sel.decks < DecksMax)
                tabs.Add(new YardDeckTabVm { id = "raise", label = "+ " + DeckNames[sel.decks], kind = YardDeckTabKind.Raise, hot = raisePreview, selected = raisePreview });
            var cells = new List<YardCellVm>();
            foreach (YardRow r in new[] { YardRow.Port, YardRow.Mid, YardRow.Stbd })
                for (int c = 0; c < 2; c++)
                {
                    string id = Cell(selDeck, r, c);
                    bool has = sel.mods.TryGetValue(id, out var m);
                    var cv = new YardCellVm { cellId = Full(sel, id), row = r, col = c, isGunPort = IsGunPort(selDeck, r, c), hasModule = has, selected = drawerCell == Full(sel, id) };
                    if (has) { var cat = Catalog[m]; cv.module = new YardModuleVm { id = m, label = cat.label, sub = cat.sub, iconKey = m }; cv.isNew = m == "bunk" && sel.kind == YardSectionKind.Mid && id == Cell(1, YardRow.Port, 1); }
                    cells.Add(cv);
                }
            string secLabel = vm.sections.First(s => s.key == selKey).label;
            vm.card = new YardSectionCardVm
            {
                sectionKey = sel.key, title = secLabel, sub = $"{sel.decks} of {DecksMax} decks",
                guns = sel.mods.Values.Count(v => v == "cannon"), bunks = sel.mods.Values.Count(v => v == "bunk"), crates = sel.mods.Values.Count(v => v == "crate"),
                deckTabs = tabs.ToArray(),
                deck = new YardDeckVm { name = DeckNames[selDeck], cells = cells.ToArray() },
            };

            int ports = ship.Sum(s => s.kind == YardSectionKind.New ? 0 : Mathf.Max(0, s.decks - 1) * 2);
            int crates = Count("crate"), mods = ship.Sum(s => s.mods.Count);
            float draft = 0.86f + 0.012f * mods;
            vm.totals = new YardTotalsVm
            {
                guns = Count("cannon"), gunPorts = ports, crew = crew, berths = Berths(), cargo = crates * 4 + 6, cargoCap = 28,
                draft = draft, draftTone = draft < 1.1f ? YardTone.Good : draft < 1.25f ? YardTone.Warn : YardTone.Bad,
            };

            var blockers = new List<YardBlockerVm>();
            if (raisePreview)
                blockers.Add(new YardBlockerVm { text = "<b>Guns up high make her roll.</b> Stability shows before you confirm.", warnOnly = true });
            else if (crew > vm.totals.berths)
            {
                int over = crew - vm.totals.berths;
                blockers.Add(new YardBlockerVm { text = $"<b>{crew} crew aboard, {vm.totals.berths} beds.</b> Add a bunk, or leave {over} ashore.", fixLabel = $"Leave {over}", fixId = "leave" });
            }
            vm.blockers = blockers.ToArray();
            int hard = blockers.Count(b => !b.warnOnly);
            vm.actions = raisePreview
                ? new YardActionsVm { cancelLabel = "Cancel", confirmLabel = "Raise deck", confirmEnabled = true }
                : new YardActionsVm { confirmLabel = hard > 0 ? $"Fix {hard} problem{(hard == 1 ? "" : "s")} to confirm" : "Confirm refit", confirmEnabled = hard == 0 };

            if (drawerCell != null)
            {
                var (ds, dc) = Split(drawerCell);
                var (dd, dr, dcol) = Parse(dc);
                string pumpAt = null; int dmid = 0;
                foreach (var s in ship)
                {
                    string lab = s.kind == YardSectionKind.Stern ? "stern" : s.kind == YardSectionKind.Bow ? "bow" : "mid " + (++dmid);
                    if (s.mods.ContainsValue("pump")) pumpAt = lab;
                }
                vm.drawer = new YardDrawerVm
                {
                    open = true, title = "Add to this slot",
                    sub = $"{secLabel} · {DeckNames[dd]} · {dr.ToString().ToLowerInvariant()}{(IsGunPort(dd, dr, dcol) ? " gun port" : "")}",
                    footnote = "Build costs are <b>free and instant</b> while the economy is set. Later: cannon <color=#F0C36A>2 iron, 2 boards</color>.",
                    items = Catalog.Keys.Select(k =>
                    {
                        store.TryGetValue(k, out var have);
                        var it = new YardDrawerItemVm { moduleId = k, label = Catalog[k].label, stats = Catalog[k].stats, iconKey = k, inStore = have, canPlace = Fits(k, drawerCell) };
                        if (k == "pump") it.aboardAt = pumpAt;
                        if (k == "lookout" && !it.canPlace) it.reason = "Not on this deck";
                        if (k == "cannon" && !it.canPlace) it.reason = "Needs a gun port";
                        if (k == "bench") { it.canPlace = false; it.lockedByDock = true; it.reason = "Dry dock IV"; }
                        it.highlighted = k == "cannon" && it.canPlace;
                        return it;
                    }).ToArray(),
                };
            }

            strip.Bind(vm.sections, vm.plusChips, vm.plusChips != null ? "tap + to add a middle section" : null);
            card.Bind(vm.card);
            totals.Bind(vm.totals);
            blocker.Bind(vm.blockers);
            if (drag.IsDragging) blocker.style.display = DisplayStyle.None;
            confirm.Bind(vm.actions);
            drawer.Bind(vm.drawer);
        }
    }
}
