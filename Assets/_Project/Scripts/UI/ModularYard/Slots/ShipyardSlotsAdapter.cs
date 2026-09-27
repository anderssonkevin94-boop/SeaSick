using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SeaSick.Ship.Modular;

namespace SeaSick.UI.ModularYard
{
    /// What the slot screen itself remembers between draft reads: which
    /// section / deck is up, which slot the drawer fills, the '+' chips and
    /// the raise preview. Everything else comes from `ShipyardSlotsView`.
    public sealed class YardUiState
    {
        public string section;          // "stern", "middle[0]", "bow"
        public int deck = SlotDeck.Deck;
        public string drawerCell;       // full cell key, or null
        public bool showPlus;
        public bool raisePreview;
        public string dragModule;       // catalog id being dragged, or null
        public bool applied;
        public bool stale;
    }

    /// **`ShipyardSlotsView` (the draft's read model) -> `YardVm` (what the
    /// Slots views draw).** Pure mapping, no Unity objects: the screen calls
    /// `Build` after every draft command and binds the result.
    ///
    /// Cell ids handed to the views are `"<section>/<deck>/<cell>"` -- the
    /// same shape as a blocker's `remove:` fix id -- so every callback can be
    /// turned straight back into a draft command with `ParseCell`.
    public static class ShipyardSlotsAdapter
    {
        public const string FixRestart = "restart";
        public const string RaiseTab = "raise";

        public static string CellKey(string section, int deck, string cell) => section + "/" + deck + "/" + cell;

        public static bool ParseCell(string key, out string section, out int deck, out string cell)
        {
            section = cell = null; deck = -1;
            if (string.IsNullOrEmpty(key)) return false;
            var p = key.Split('/');
            if (p.Length != 3 || !int.TryParse(p[1], out deck)) return false;
            section = p[0]; cell = p[2];
            return true;
        }

        public static string IconKey(string moduleId)
        {
            switch (moduleId)
            {
                case ModuleCatalog.Cannon: return "cannon";
                case ModuleCatalog.Bunk: return "bunk";
                case ModuleCatalog.Crate: return "crate";
                case ModuleCatalog.BilgePump: return "pump";
                case ModuleCatalog.Lookout: return "lookout";
                case ModuleCatalog.RepairBench: return "bench";
                default: return moduleId ?? "";
            }
        }

        /// Short line under a module's name in a slot ("1 hand", "2 berths").
        public static string ShortSub(StoreRowView r)
        {
            if (r == null) return "";
            if (r.berths > 0) return r.berths + (r.berths == 1 ? " berth" : " berths");
            if (r.cargo > 0) return r.cargo + " cargo";
            if (r.crew > 0 && r.moduleId == ModuleCatalog.Cannon) return r.crew + (r.crew == 1 ? " hand" : " hands");
            switch (r.moduleId)
            {
                case ModuleCatalog.BilgePump: return "bails";
                case ModuleCatalog.Lookout: return "spots sails";
                case ModuleCatalog.RepairBench: return "mends";
            }
            return "";
        }

        /// The drawer's stats line ("needs 1 hand · 500 kg").
        public static string Stats(StoreRowView r)
        {
            if (r == null) return "";
            var bits = new List<string>();
            if (r.berths > 0) bits.Add(r.berths + " berths");
            if (r.cargo > 0) bits.Add(r.cargo + " cargo");
            if (r.crew > 0) bits.Add("needs " + r.crew + (r.crew == 1 ? " hand" : " hands"));
            if (r.moduleId == ModuleCatalog.BilgePump) bits.Add("bails faster");
            if (r.moduleId == ModuleCatalog.Lookout) bits.Add("top-most deck only");
            if (r.moduleId == ModuleCatalog.RepairBench) bits.Add("mends hull at sea");
            if (r.onePerShip) bits.Add("1 per ship");
            else bits.Add(Math.Round(r.massKg).ToString(CultureInfo.InvariantCulture) + " kg");
            return string.Join(" · ", bits);
        }

        static YardSectionKind Kind(string kind) =>
            kind == ModuleKind.Stern.ToString() ? YardSectionKind.Stern : kind == ModuleKind.Bow.ToString() ? YardSectionKind.Bow : YardSectionKind.Mid;

        static YardRow Row(string row) => row == SlotRow.Mid ? YardRow.Mid : row == SlotRow.Stbd ? YardRow.Stbd : YardRow.Port;

        static readonly Regex DockWord = new Regex(@"[Dd]ry dock (?:level )?([IVX]+)");

        /// "Dry dock IV" out of a long refusal sentence, or a short fallback.
        public static string ShortReason(string reason, string fallback)
        {
            if (string.IsNullOrEmpty(reason)) return fallback;
            var m = DockWord.Match(reason);
            if (m.Success) return "Dry dock " + m.Groups[1].Value;
            if (reason.Contains("wide beam")) return "needs wide beam";
            return fallback;
        }

        public static StoreRowView Row(ShipyardSlotsView v, string moduleId) =>
            v.store.FirstOrDefault(r => r.moduleId == moduleId);

        public static SlotSectionView Selected(ShipyardSlotsView v, YardUiState ui)
        {
            var s = v.sections.FirstOrDefault(x => x.key == ui.section);
            if (s == null)
            {
                // default: the first middle, else the stern
                s = v.sections.FirstOrDefault(x => Kind(x.kind) == YardSectionKind.Mid) ?? v.sections.FirstOrDefault();
                ui.section = s?.key;
            }
            if (s != null) ui.deck = Math.Max(0, Math.Min(ui.deck, s.topDeck));
            return s;
        }

        /// Where a one-per-ship module stands now ("stern", "mid 2"), or null.
        static string FittedAt(ShipyardSlotsView v, string moduleId)
        {
            foreach (var s in v.sections)
                foreach (var d in s.decks)
                    foreach (var c in d.cells)
                        if (c.moduleId == moduleId) return s.name.ToLowerInvariant();
            return null;
        }

        public static bool FindFit(ShipyardSlotsView v, string moduleId, out string section, out int deck, out string cell)
        {
            foreach (var s in v.sections)
                foreach (var d in s.decks)
                    foreach (var c in d.cells)
                        if (c.moduleId == moduleId) { section = s.key; deck = d.deckIndex; cell = c.cellId; return true; }
            section = cell = null; deck = -1;
            return false;
        }

        /// Everything the screen shows. `options` = draft.Options (the
        /// drawer); `baseline` = the ship as she was (the amber NEW tag).
        public static YardVm Build(ShipyardSlotsView v, YardUiState ui, ModuleLibrary lib,
            Func<string, int, string, List<ModuleOptionView>> options, ShipConfiguration baseline)
        {
            var vm = new YardVm();
            var sel = Selected(v, ui);
            if (sel == null) return vm;
            if (ui.raisePreview && !sel.canRaise) ui.raisePreview = false;

            // ---- the strip
            vm.sections = v.sections.Select(s => new YardSectionVm
            {
                key = s.key, label = s.name, kind = Kind(s.kind),
                decksUsed = s.topDeck + 1, decksMax = SlotDeck.Count,
                selected = s.key == sel.key, canRemove = s.canRemove,
                raisePreview = ui.raisePreview && s.key == sel.key && s.topDeck + 1 < SlotDeck.Count,
                raiseLabel = s.topDeck + 1 < SlotDeck.Count ? "+ " + SlotDeck.Name(s.topDeck + 1) : "",
            }).ToArray();
            int mids = v.sections.Count(s => Kind(s.kind) == YardSectionKind.Mid);
            int maxMids = lib != null ? lib.MaxMiddles : 3;
            var dockLim = DockLimits.For(lib, v.dockLevel);
            bool dockFull = dockLim != null && mids + 3 > dockLim.maxSections;
            if (ui.showPlus)
            {
                string why = mids >= maxMids ? $"{maxMids} middles is the longest hull"
                    : dockFull ? $"Dry dock {SlotModel.Roman(v.dockLevel)} takes {dockLim.maxSections} sections" : null;
                vm.plusChips = Enumerable.Range(1, v.sections.Count - 1)
                    .Select(i => new YardPlusChipVm { index = i, locked = why != null, lockReason = why }).ToArray();
                vm.stripNote = why ?? "tap + to add a middle section";
            }
            else if (ui.raisePreview) vm.stripNote = null;

            // ---- the card
            var tabs = new List<YardDeckTabVm>();
            for (int d = 0; d <= sel.topDeck && d < sel.decks.Count; d++)
            {
                var dv = sel.decks[d];
                var tab = new YardDeckTabVm
                {
                    id = d.ToString(CultureInfo.InvariantCulture), label = dv.name, kind = YardDeckTabKind.Deck,
                    used = dv.used, cap = dv.total, selected = d == ui.deck && !ui.raisePreview,
                };
                if (ui.dragModule == ModuleCatalog.Cannon)
                {
                    int free = dv.cells.Count(c => c.isGunPort && c.moduleId == null);
                    tab.note = d == SlotDeck.Hold ? "no guns" : free > 0 ? $"{free} port{(free == 1 ? "" : "s")} free" : "ports full";
                    tab.hot = free > 0 && d != SlotDeck.Hold;
                }
                tabs.Add(tab);
            }
            int next = sel.topDeck + 1;
            if (next < SlotDeck.Count)
            {
                if (sel.canRaise)
                    tabs.Add(new YardDeckTabVm { id = RaiseTab, label = "+ " + SlotDeck.Name(next), kind = YardDeckTabKind.Raise, hot = ui.raisePreview, selected = ui.raisePreview });
                else
                    tabs.Add(new YardDeckTabVm { id = "locked", label = SlotDeck.Name(next), kind = YardDeckTabKind.Locked, lockReason = ShortReason(sel.raiseReason, "can't raise") });
            }

            var deck = sel.decks[ui.deck];
            var alongs = deck.cells.Select(c => (float)Math.Round(c.along, 2)).Distinct().OrderBy(a => a).ToList();
            string dragPlacement = ui.dragModule != null ? Row(v, ui.dragModule)?.placement : null;
            var cells = deck.cells.Select(c =>
            {
                string key = CellKey(sel.key, deck.deckIndex, c.cellId);
                var cv = new YardCellVm
                {
                    cellId = key, row = Row(c.row), col = alongs.IndexOf((float)Math.Round(c.along, 2)),
                    isGunPort = c.isGunPort, hasModule = c.moduleId != null, selected = ui.drawerCell == key,
                };
                if (c.moduleId != null)
                {
                    var r = Row(v, c.moduleId);
                    cv.module = new YardModuleVm { id = c.moduleId, label = r?.name ?? c.moduleId, sub = ShortSub(r), iconKey = IconKey(c.moduleId) };
                    var was = baseline != null ? SlotModel.At(baseline, sel.key, deck.deckIndex, c.cellId) : null;
                    cv.isNew = baseline != null && (was == null || was.moduleId != c.moduleId);
                }
                else if (dragPlacement == SlotPlacement.Port && !c.isGunPort) cv.lockedReason = "not a port";
                return cv;
            }).ToArray();

            int CountIn(string id) => sel.decks.Where(d => d.deckIndex <= sel.topDeck).Sum(d => d.cells.Count(c => c.moduleId == id));
            vm.card = new YardSectionCardVm
            {
                sectionKey = sel.key, title = sel.name, sub = $"{sel.topDeck + 1} of {SlotDeck.Count} decks",
                guns = CountIn(ModuleCatalog.Cannon), bunks = CountIn(ModuleCatalog.Bunk), crates = CountIn(ModuleCatalog.Crate),
                deckTabs = tabs.ToArray(),
                deck = new YardDeckVm { name = deck.name, cells = cells },
            };

            // ---- totals
            var t = v.totals;
            bool heavy = v.blockers.Any(b => b.code == ShipyardCodes.Overloaded || b.code == ShipyardCodes.SimOutOfRange);
            bool heavyWarn = v.warnings.Any(b => b.code == ShipyardCodes.Overloaded || b.code == ShipyardCodes.SimOutOfRange);
            vm.totals = new YardTotalsVm
            {
                guns = t.guns, gunPorts = t.gunPorts, crew = t.crew, berths = t.berths, cargo = t.cargo, cargoCap = t.cargoCap,
                draft = t.draftM, draftTone = float.IsNaN(t.draftM) ? YardTone.Plain : heavy ? YardTone.Bad : heavyWarn ? YardTone.Warn : YardTone.Good,
            };

            // ---- blockers (hard first; the bar shows the first one)
            var list = new List<YardBlockerVm>();
            int hard = 0;
            if (ui.applied)
                list.Add(new YardBlockerVm { text = "<b>Refit done.</b> She is saved as she stands.", warnOnly = true });
            else if (ui.raisePreview)
            {
                var nd = sel.decks[next];
                int slots = nd.cells.Count, ports = nd.cells.Count(c => c.isGunPort);
                list.Add(new YardBlockerVm
                {
                    text = $"<b>Raise {sel.name} to a{(SlotDeck.Name(next).StartsWith("U") ? "n" : "")} {SlotDeck.Name(next).ToLowerInvariant()} deck:</b> +{slots} slots" +
                           (ports > 0 ? $", +{ports} gun ports" : "") + ". Guns up high make her roll.",
                    warnOnly = true,
                });
            }
            else
            {
                if (ui.stale)
                {
                    hard++;
                    list.Add(new YardBlockerVm { text = "<b>The ship changed since you opened the yard.</b> Start over to plan on her as she is.", fixLabel = "Start over", fixId = FixRestart });
                }
                if (!string.IsNullOrEmpty(v.messageCode) && !ui.stale)
                    list.Add(new YardBlockerVm { text = v.message, warnOnly = true });
                foreach (var b in v.blockers)
                {
                    hard++;
                    list.Add(new YardBlockerVm { text = b.text, fixId = b.fixId, fixLabel = FixLabel(b.fixId) });
                }
                if (!string.IsNullOrEmpty(v.refitNowBlockedBecause))
                {
                    hard++;
                    list.Add(new YardBlockerVm { text = v.refitNowBlockedBecause });
                }
                foreach (var w in v.warnings)
                    list.Add(new YardBlockerVm { text = w.text, fixId = w.fixId, fixLabel = FixLabel(w.fixId), warnOnly = true });
                // hard ones first, the refusal line stays on top of advice
                list = list.Where(b => !b.warnOnly).Concat(list.Where(b => b.warnOnly)).ToList();
                if (!string.IsNullOrEmpty(v.messageCode) && !ui.stale)
                {
                    var m = list.First(b => b.warnOnly && b.text == v.message);
                    list.Remove(m); list.Insert(0, m);
                }
            }
            vm.blockers = list.ToArray();

            // ---- pinned row
            if (ui.applied) vm.actions = new YardActionsVm { confirmLabel = "Done", confirmEnabled = true };
            else if (ui.raisePreview) vm.actions = new YardActionsVm { cancelLabel = "Cancel", confirmLabel = "Raise deck", confirmEnabled = true };
            else if (hard > 0) vm.actions = new YardActionsVm { confirmLabel = $"Fix {hard} problem{(hard == 1 ? "" : "s")} to confirm", confirmEnabled = false };
            else if (!v.dirty) vm.actions = new YardActionsVm { confirmLabel = "No changes yet", confirmEnabled = false };
            else vm.actions = new YardActionsVm
            {
                confirmLabel = "Confirm refit",
                confirmEnabled = v.canApply,
            };

            // ---- the drawer
            if (ui.drawerCell != null && ParseCell(ui.drawerCell, out var ds, out var dd, out var dc) && options != null)
            {
                var dsec = v.sections.FirstOrDefault(s => s.key == ds);
                var dcell = dsec?.decks[dd].cells.FirstOrDefault(c => c.cellId == dc);
                if (dsec == null || dcell == null || dcell.moduleId != null) ui.drawerCell = null;
                else
                {
                    bool highlighted = false;
                    var items = options(ds, dd, dc).Select(o =>
                    {
                        var r = Row(v, o.moduleId);
                        var it = new YardDrawerItemVm
                        {
                            moduleId = o.moduleId, label = o.name, stats = Stats(r), iconKey = IconKey(o.moduleId),
                            inStore = o.inStore, canPlace = o.action == "place" || o.action == "build-place" || o.action == "move-here",
                            lockedByDock = o.action == "locked",
                        };
                        if (it.lockedByDock) it.reason = ShortReason(o.reason, "Locked");
                        else if (!it.canPlace) it.reason = NotHere(o.reason);
                        if (o.action == "move-here") it.aboardAt = FittedAt(v, o.moduleId) ?? "elsewhere";
                        if (!highlighted && it.canPlace && (dcell.isGunPort ? o.moduleId == ModuleCatalog.Cannon : o.moduleId != ModuleCatalog.Cannon))
                        { it.highlighted = true; highlighted = true; }
                        return it;
                    }).ToArray();
                    vm.drawer = new YardDrawerVm
                    {
                        open = true, title = "Add to this slot",
                        sub = $"{dsec.name} · {SlotDeck.Name(dd)} · {dcell.row.ToLowerInvariant()}{(dcell.isGunPort ? " gun port" : "")}",
                        items = items,
                        footnote = "Build costs are <b>free and instant</b> while the economy is set.",
                    };
                }
            }
            return vm;
        }

        /// A short "why not" for a greyed drawer card.
        static string NotHere(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "Not here";
            string r = reason.ToLowerInvariant();
            if (r.Contains("gun port") && r.Contains("only")) return "Needs a gun port";
            if (r.Contains("hold")) return "Hold only";
            if (r.Contains("top")) return "Not on this deck";
            if (r.Contains("one per ship") || r.Contains("only one")) return "1 per ship";
            if (r.Contains("port")) return "Not in a gun port";
            return reason.Length <= 22 ? reason : "Not here";
        }

        public static string FixLabel(string fixId)
        {
            if (string.IsNullOrEmpty(fixId)) return null;
            if (fixId.StartsWith("remove:")) return "Remove it";
            if (fixId.StartsWith("build:")) return "Build one";
            switch (fixId)
            {
                case "add-bunk": return "Add a bunk";
                case "upgrade-dock": return "Dock";
                case FixRestart: return "Start over";
                default: return null;   // unload / lighten: nothing to press here
            }
        }
    }
}
