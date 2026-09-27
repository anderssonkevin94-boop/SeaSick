using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    // ---------------------------------------------------------------------
    // Shipyard SLOTS, the UI-facing half (2026-09-27, PHASE 1): a plain-data
    // read model (ShipyardSlotsView) and the draft that owns every edit
    // (ShipyardSlotDraft: place/remove/move/build, add/remove section,
    // raise/lower deck, undo, validate, apply). Pure C#: the phase-2 screen
    // binds to it; the live ship is reached only through
    // IShipyardSlotsBackend (ShipyardService implements it).
    // docs/SHIPYARD-API.md "Slots API".
    // ---------------------------------------------------------------------

    /// What the draft needs from the live game. ShipyardService implements
    /// it; tests use a plain stand-in.
    public interface IShipyardSlotsBackend
    {
        /// The live ship's configuration (always a slot config, schema 3).
        ShipConfiguration ReadCurrent();
        /// The dry-dock store as it is now.
        DryDock ReadStore();
        int DockLevel { get; }
        int CrewAboard { get; }
        int CargoAboard { get; }
        /// Full validation of `draft` against the live ship (never changes anything).
        ShipyardReport Report(ShipConfiguration draft);
        /// Atomic: build `builds` into the store, refit expected -> draft, save.
        bool TryApply(ShipConfiguration expected, ShipConfiguration draft, IReadOnlyList<string> builds, out string reason);
    }

    [Serializable]
    public class SlotCellView
    {
        public string cellId;
        /// SlotRow: "Port", "Mid", "Stbd".
        public string row;
        public bool isGunPort;
        /// A gun port whose cannon the battery can actually draw (false = the
        /// art has no mount there yet; a cannon still counts, and warns).
        public bool hasMount;
        /// Position along the section, 0 = aft end, 1 = forward (the grid
        /// def's `along`; the screen turns it into a column).
        public float along;
        /// Catalog id of what stands here, or null when empty.
        public string moduleId;
    }

    [Serializable]
    public class SlotDeckView
    {
        /// SlotDeck: 0 Hold, 1 Deck, 2 Upper, 3 Top.
        public int deckIndex;
        public string name;
        public bool unlocked;
        /// Why a locked deck is locked, and whether RaiseDeck would open it
        /// now (it is the next one up).
        public string lockedReason = "";
        public bool isNextRaise;
        public int used, total;
        public List<SlotCellView> cells = new List<SlotCellView>();
    }

    [Serializable]
    public class SlotSectionView
    {
        /// "stern", "middle[0]", ..., "bow".
        public string key;
        /// ModuleKind: "Stern" / "Middle" / "Bow".
        public string kind;
        /// "Stern", "Mid 1", "Bow".
        public string name;
        public string moduleId;
        public bool wide;
        public int topDeck;
        public bool canRemove, canRaise, canLower;
        public string removeReason = "", raiseReason = "", lowerReason = "";
        /// Always 4 entries, index = deckIndex.
        public List<SlotDeckView> decks = new List<SlotDeckView>();
    }

    [Serializable]
    public class SlotTotalsView
    {
        /// Cannons fitted / gun ports on open decks.
        public int guns, gunPorts;
        /// Hands aboard now / berths (bunks + the stern cabin).
        public int crew, berths;
        /// Cargo aboard now / cargo cells (crates).
        public int cargo, cargoCap;
        public int cells, cellsUsed;
        /// As loaded, above the keel (module station tables). NaN = unknown.
        public float draftM = float.NaN;
        public float fitMassT;
    }

    [Serializable]
    public class SlotIssueView
    {
        public string code;
        public string text;
        /// What the UI's fix button does, or "" for none:
        /// "remove:<section>/<deck>/<cell>", "build:<moduleId>", "add-bunk",
        /// "unload", "lighten", "upgrade-dock", "leave-ashore".
        public string fixId = "";
    }

    [Serializable]
    public class StoreRowView
    {
        public string moduleId, name, description;
        public int inStore, aboard;
        /// Needs a higher dock level than this one.
        public bool locked;
        public int dockLevel;
        public bool onePerShip;
        public string placement;
        public int cargo, berths, crew;
        public float massKg;
    }

    /// One row of the add drawer (design frame 2) for a chosen cell.
    [Serializable]
    public class ModuleOptionView
    {
        public string moduleId, name;
        /// "place" (in store), "build-place" (none in store: builds then
        /// places, free now), "move-here" (a one-per-ship module fitted
        /// elsewhere), "not-here" (wrong kind of slot), "locked" (dock level).
        public string action;
        public string reason = "";
        public int inStore;
    }

    /// The whole screen's data in one read (plain data; no Unity types).
    public class ShipyardSlotsView
    {
        public List<SlotSectionView> sections = new List<SlotSectionView>();
        public SlotTotalsView totals = new SlotTotalsView();
        /// Stop Confirm. Empty = Apply may go ahead (subject to the berth rules).
        public List<SlotIssueView> blockers = new List<SlotIssueView>();
        public List<SlotIssueView> warnings = new List<SlotIssueView>();
        /// One row per catalog module: store count (after this draft) and aboard.
        public List<StoreRowView> store = new List<StoreRowView>();
        public int dockLevel;
        public string dockName = "";
        public bool dirty, canUndo, canApply;
        /// Edits since the baseline (the Confirm button's change count).
        public int changes;
        /// The last command's refusal/confirmation sentence and code ("" = none).
        public string message = "", messageCode = "";
        /// Modules the draft builds at Apply (free and instant for now).
        public List<string> builds = new List<string>();
        /// Why she cannot be refitted right now, if so (moored elsewhere...).
        public string refitNowBlockedBecause = "";

        public int StoreCount(string moduleId)
        {
            foreach (var r in store) if (r.moduleId == moduleId) return r.inStore;
            return 0;
        }
    }

    /// The draft behind the slot screen. Every command either changes the
    /// draft (one undo step, returns true) or refuses with `Message` /
    /// `MessageCode` and changes nothing. Nothing touches the ship, the
    /// store or the save until Apply.
    public sealed class ShipyardSlotDraft
    {
        readonly ModuleLibrary lib;
        readonly IShipyardSlotsBackend backend;
        readonly ShipConfiguration baseline;
        readonly DryDock baseStore;
        ShipConfiguration draft;
        readonly List<string> builds = new List<string>();
        readonly Stack<(ShipConfiguration cfg, List<string> builds)> undo = new Stack<(ShipConfiguration, List<string>)>();
        int dockLevelOverride = -1;

        public string Message { get; private set; } = "";
        public string MessageCode { get; private set; } = "";
        public bool Applied { get; private set; }
        public event Action Changed;

        public int DockLevel => dockLevelOverride >= 0 ? dockLevelOverride : backend != null ? backend.DockLevel : DockLimits.Unenforced;
        public bool Dirty => !draft.ValueEquals(baseline) || builds.Count > 0;
        public bool CanUndo => undo.Count > 0 && !Applied;
        public int Changes => undo.Count;
        public ShipConfiguration Snapshot() => draft.Clone();
        public ShipConfiguration Baseline => baseline.Clone();
        public IReadOnlyList<string> Builds => builds;

        /// From the live ship (backend.ReadCurrent / ReadStore).
        public ShipyardSlotDraft(ModuleLibrary lib, IShipyardSlotsBackend backend)
            : this(lib, backend?.ReadCurrent(), backend?.ReadStore(), backend) { }

        /// Detached (tests, previews): `current` is migrated to slots if it
        /// is not already; `store` null = empty.
        public ShipyardSlotDraft(ModuleLibrary lib, ShipConfiguration current, DryDock store, IShipyardSlotsBackend backend = null, int dockLevel = -1)
        {
            this.lib = lib ?? throw new ArgumentNullException(nameof(lib));
            this.backend = backend;
            dockLevelOverride = dockLevel;
            var cur = current ?? ShipConfiguration.Long();
            baseline = SlotModel.Migrate(cur, lib, out var overflow);
            baseStore = store != null ? store.Clone() : DryDock.Empty();
            foreach (var id in overflow) baseStore.Add(id);
            draft = baseline.Clone();
        }

        // ---- reading ------------------------------------------------------------

        /// The store as it would be after Apply: live store + builds +
        /// modules the draft took off - modules it fitted.
        public DryDock Store()
        {
            var s = baseStore.Clone();
            foreach (var b in builds) s.Add(b);
            foreach (var d in DryDock.Diff(baseline, draft))
            {
                if (d.delta < 0) s.Add(d.moduleId, -d.delta);
                else if (d.delta > 0 && !s.TryTake(d.moduleId, d.delta))
                {
                    int have = s.Count(d.moduleId);
                    if (have > 0) s.TryTake(d.moduleId, have);
                }
            }
            return s;
        }

        /// How many more of `moduleId` the draft fits than the store (with
        /// builds) can give -- 0 on any draft the commands produced.
        public int Shortfall(string moduleId)
        {
            var s = baseStore.Clone();
            foreach (var b in builds) s.Add(b);
            foreach (var d in DryDock.Diff(baseline, draft))
                if (d.moduleId == moduleId && d.delta > 0) return Mathf.Max(0, d.delta - s.Count(moduleId));
            return 0;
        }

        public ShipyardSlotsView View()
        {
            var v = new ShipyardSlotsView
            {
                dockLevel = DockLevel, dirty = Dirty, canUndo = CanUndo, changes = Changes,
                message = Message, messageCode = MessageCode,
            };
            v.builds.AddRange(builds);
            var lim = DockLimits.For(lib, DockLevel);
            if (lim != null) v.dockName = lim.name;
            var layout = SlotModel.Layout(draft, lib);
            var mountsBySection = new Dictionary<string, List<GunMount>>();
            foreach (var s in layout)
            {
                var sv = new SlotSectionView { key = s.key, kind = s.kind, name = SlotModel.SectionName(s.key), moduleId = s.moduleId, wide = s.wide, topDeck = s.TopDeck };
                var mounts = mountsBySection[s.key] = SlotModel.GunMounts(draft, lib, s.key);
                for (int d = 0; d < SlotDeck.Count; d++)
                {
                    var dv = new SlotDeckView { deckIndex = d, name = SlotDeck.Name(d), unlocked = s.unlocked[d], isNextRaise = d == s.TopDeck + 1 };
                    if (!s.unlocked[d]) dv.lockedReason = RaiseWhy(s, d) ?? (dv.isNextRaise ? "Raise a deck to open it." : $"Open the {SlotDeck.Name(d - 1)} first.");
                    if (s.cells[d] != null)
                        foreach (var c in s.cells[d])
                        {
                            if (c == null) continue;
                            var f = SlotModel.At(draft, s.key, d, c.id);
                            bool mount = false;
                            if (c.gunPort) foreach (var gm in mounts) if (gm.deck == d && gm.row == c.row) mount = true;
                            dv.cells.Add(new SlotCellView { cellId = c.id, row = c.row, isGunPort = c.gunPort, hasMount = mount, along = c.along, moduleId = f?.moduleId });
                            if (s.unlocked[d]) { dv.total++; if (f != null) dv.used++; }
                        }
                    sv.decks.Add(dv);
                }
                sv.removeReason = RemoveSectionWhy(s.key) ?? ""; sv.canRemove = sv.removeReason == "";
                sv.raiseReason = RaiseWhy(s, s.TopDeck + 1) ?? ""; sv.canRaise = sv.raiseReason == "";
                sv.lowerReason = LowerWhy(s) ?? ""; sv.canLower = sv.lowerReason == "";
                v.sections.Add(sv);
            }

            var t = SlotModel.Totals(draft, lib);
            v.totals.guns = t.cannons; v.totals.gunPorts = t.gunPorts; v.totals.berths = t.berths; v.totals.cargoCap = t.cargo;
            v.totals.cells = t.cells; v.totals.cellsUsed = t.cellsUsed; v.totals.fitMassT = t.massKg / 1000f;
            v.totals.crew = backend != null ? backend.CrewAboard : 0;
            v.totals.cargo = backend != null ? backend.CargoAboard : 0;

            var store = Store();
            var aboard = SlotModel.FittedCounts(draft);
            if (lib.Catalog != null)
                foreach (var m in lib.Catalog.All)
                    v.store.Add(new StoreRowView
                    {
                        moduleId = m.id, name = m.displayName, description = m.description,
                        inStore = store.Count(m.id), aboard = aboard.TryGetValue(m.id, out var a) ? a : 0,
                        locked = m.dockLevel > DockLevel, dockLevel = m.dockLevel, onePerShip = m.onePerShip,
                        placement = m.placement, cargo = m.capacity.cargo, berths = m.capacity.berths, crew = m.crew, massKg = m.massKg,
                    });

            // Blockers and warnings: the backend's full report (hull rules,
            // cargo/crew, hydrostatics, slot rules) or, detached, the slot
            // rules + assembly alone.
            if (backend != null)
            {
                var r = backend.Report(draft);
                foreach (var b in r.blocking) v.blockers.Add(Issue(b.code, b.message, b.partId));
                foreach (var w in r.warnings) if (w.code != "PROVISIONAL_TUNING") v.warnings.Add(Issue(w.code, w.message, ""));
                if (!float.IsNaN(r.tableDraftM)) v.totals.draftM = r.tableDraftM;
                v.refitNowBlockedBecause = r.refitNowBlockedBecause ?? "";
            }
            else
            {
                var bl = new List<Rejection>(); var wn = new List<ShipyardNote>();
                bl.AddRange(ShipAssembler.Assemble(draft, lib).rejections);
                SlotModel.Check(draft, lib, DockLevel, -1, bl, wn);
                foreach (var b in bl) v.blockers.Add(Issue(b.code, b.message, b.partId));
                foreach (var w in wn) v.warnings.Add(Issue(w.code, w.message, ""));
            }
            v.canApply = Dirty && v.blockers.Count == 0 && string.IsNullOrEmpty(v.refitNowBlockedBecause) && !Applied && backend != null;
            return v;
        }

        static SlotIssueView Issue(string code, string text, string part)
        {
            var i = new SlotIssueView { code = code, text = text };
            switch (code)
            {
                case SlotCodes.SlotWrongKind: case SlotCodes.ModuleLocked: case SlotCodes.OnePerShip: case SlotCodes.ModuleUnknown:
                    i.fixId = string.IsNullOrEmpty(part) ? "" : "remove:" + part; break;
                case SlotCodes.DockLevel: i.fixId = "upgrade-dock"; break;
                case SlotCodes.GunsShortOfHands: i.fixId = "add-bunk"; break;
                case ShipyardCodes.CargoWouldNotFit: i.fixId = "unload"; break;
                case ShipyardCodes.Overloaded: case ShipyardCodes.SimOutOfRange: i.fixId = "lighten"; break;
                case ShipyardCodes.NotInDryDock: i.fixId = string.IsNullOrEmpty(part) ? "" : "build:" + part; break;
                case "HANDS_ASHORE": i.fixId = "add-bunk"; break;
            }
            return i;
        }

        /// The add drawer for one cell (design frame 2): every catalog
        /// module, whether and how it can go there.
        public List<ModuleOptionView> Options(string sectionKey, int deck, string cellId)
        {
            var list = new List<ModuleOptionView>();
            if (lib.Catalog == null) return list;
            var store = Store();
            foreach (var m in lib.Catalog.All)
            {
                var o = new ModuleOptionView { moduleId = m.id, name = m.displayName, inStore = store.Count(m.id) };
                SlotFit elsewhere = null;
                if (m.onePerShip) foreach (var f in draft.fits) if (f.moduleId == m.id) elsewhere = f;
                var why = SlotModel.CanPlace(draft, lib, sectionKey, deck, cellId, m.id, DockLevel, elsewhere);
                if (why != null)
                {
                    o.action = why.code == SlotCodes.ModuleLocked ? "locked" : "not-here";
                    o.reason = why.message;
                }
                else if (elsewhere != null && !elsewhere.SameCell(sectionKey, deck, cellId)) o.action = "move-here";
                else o.action = o.inStore > 0 ? "place" : "build-place";
                list.Add(o);
            }
            return list;
        }

        // ---- commands -------------------------------------------------------------

        bool Refuse(string code, string message) { MessageCode = code ?? ""; Message = message ?? ""; Changed?.Invoke(); return false; }
        bool Refuse(Rejection r) => Refuse(r.code, r.message);

        bool Commit(ShipConfiguration next, List<string> nextBuilds, string message)
        {
            if (Applied) return Refuse("APPLIED", "This refit is already confirmed.");
            var n = SlotModel.Normalized(next, lib, out var stranded);
            undo.Push((draft, new List<string>(builds)));
            draft = n;
            if (nextBuilds != null) { builds.Clear(); builds.AddRange(nextBuilds); }
            MessageCode = "";
            Message = message ?? "";
            if (stranded.Count > 0)
                Message = (Message.Length > 0 ? Message + " " : "") +
                          (stranded.Count == 1 ? "1 module went back to the store." : $"{stranded.Count} modules went back to the store.");
            Changed?.Invoke();
            return true;
        }

        /// Fits `moduleId` (taken from the store) into an EMPTY cell.
        /// NOT_IN_STORE when none is there: call BuildModule first, or
        /// BuildAndPlace. A one-per-ship module already aboard: use Move.
        public bool Place(string sectionKey, int deck, string cellId, string moduleId)
        {
            var why = SlotModel.CanPlace(draft, lib, sectionKey, deck, cellId, moduleId, DockLevel);
            if (why != null) return Refuse(why);
            if (SlotModel.Occupied(draft, sectionKey, deck, cellId))
                return Refuse(SlotCodes.SlotOccupied, "That slot is taken. Remove what is there, or drag onto it to swap.");
            if (Store().Count(moduleId) < 1)
                return Refuse(SlotCodes.NotInStore, $"There is no {Name(moduleId)} in the store. Build one first.");
            var next = draft.Clone();
            next.fits.Add(new SlotFit { section = sectionKey, deck = deck, cell = cellId, moduleId = moduleId });
            return Commit(next, null, "");
        }

        /// Builds one (free, instant) and fits it -- ONE undo step (frame 2's
        /// "Build + place").
        public bool BuildAndPlace(string sectionKey, int deck, string cellId, string moduleId)
        {
            var why = SlotModel.CanPlace(draft, lib, sectionKey, deck, cellId, moduleId, DockLevel);
            if (why != null) return Refuse(why);
            if (SlotModel.Occupied(draft, sectionKey, deck, cellId))
                return Refuse(SlotCodes.SlotOccupied, "That slot is taken. Remove what is there, or drag onto it to swap.");
            var next = draft.Clone();
            next.fits.Add(new SlotFit { section = sectionKey, deck = deck, cell = cellId, moduleId = moduleId });
            var nb = new List<string>(builds);
            if (Store().Count(moduleId) < 1) nb.Add(moduleId);
            return Commit(next, nb, "");
        }

        /// Takes the module in that cell off, into the store.
        public bool Remove(string sectionKey, int deck, string cellId)
        {
            var f = SlotModel.At(draft, sectionKey, deck, cellId);
            if (f == null) return Refuse(SlotCodes.SlotUnknown, "That slot is empty.");
            var next = draft.Clone();
            next.fits.RemoveAll(x => x.SameCell(sectionKey, deck, cellId));
            return Commit(next, null, $"{Name(f.moduleId)} went back to the store.");
        }

        /// Moves the module in `from` to `to` (another cell, deck or
        /// section). A filled `to` SWAPS -- both modules must suit their new
        /// cells. One undo step.
        public bool Move(string fromSection, int fromDeck, string fromCell, string toSection, int toDeck, string toCell)
        {
            var a = SlotModel.At(draft, fromSection, fromDeck, fromCell);
            if (a == null) return Refuse(SlotCodes.SlotUnknown, "There is nothing to move there.");
            if (a.SameCell(toSection, toDeck, toCell)) return false;
            var b = SlotModel.At(draft, toSection, toDeck, toCell);
            var why = SlotModel.CanPlace(draft, lib, toSection, toDeck, toCell, a.moduleId, DockLevel, a);
            if (why != null) return Refuse(why);
            if (b != null)
            {
                why = SlotModel.CanPlace(draft, lib, fromSection, fromDeck, fromCell, b.moduleId, DockLevel, b);
                if (why != null) return Refuse(why.code, "Cannot swap: " + why.message);
            }
            var next = draft.Clone();
            var na = SlotModel.At(next, fromSection, fromDeck, fromCell);
            var nb = SlotModel.At(next, toSection, toDeck, toCell);
            na.section = toSection; na.deck = toDeck; na.cell = toCell;
            if (nb != null) { nb.section = fromSection; nb.deck = fromDeck; nb.cell = fromCell; }
            return Commit(next, null, b != null ? "Swapped." : "");
        }

        /// Builds one module into the store (free and instant for now; it
        /// is really built at Apply). One undo step.
        public bool BuildModule(string moduleId)
        {
            if (lib.Catalog == null || !lib.Catalog.TryGet(moduleId, out var m))
                return Refuse(SlotCodes.ModuleUnknown, $"There is no module called {moduleId}.");
            if (m.dockLevel > DockLevel)
                return Refuse(SlotCodes.ModuleLocked, $"The {m.displayName} needs dry dock level {SlotModel.Roman(m.dockLevel)}.");
            var nb = new List<string>(builds) { moduleId };
            return Commit(draft.Clone(), nb, $"Built a {m.displayName}.");
        }

        /// Inserts a middle section at `index` (0 = right behind the stern,
        /// Count = right before the bow), in the hull's own beam family.
        public bool AddSection(int index)
        {
            int n = draft.middleIds.Count;
            if (index < 0 || index > n) return Refuse(SlotCodes.SlotUnknown, "There is no such place for a section.");
            if (n >= lib.MaxMiddles) return Refuse("TOO_MANY_MIDDLES", $"{lib.MaxMiddles} middle sections is the longest hull the slip takes.");
            var lim = DockLimits.For(lib, DockLevel);
            if (lim != null && n + 3 > lim.maxSections)
                return Refuse(SlotCodes.DockLevel, $"Dry dock {SlotModel.Roman(lim.level)} builds {lim.maxSections} sections at most.");
            var next = draft.Clone();
            ShipConfiguration.ShiftMiddleKeys(next, index, +1);
            bool w1x = IsW1xFamily(next);
            next.middleIds.Insert(index, w1x ? ExpandedPresets.ExpandedMiddle : ShipConfiguration.V3Middle);
            if (w1x) Recompute(next);
            if (!Assembles(next, out var why)) return Refuse(why);
            return Commit(next, null, $"Added {SlotModel.SectionName(ShipAssembler.MiddleKey(index))}.");
        }

        /// Removes a middle section; its modules go to the store.
        public bool RemoveSection(string sectionKey)
        {
            var why = RemoveSectionWhy(sectionKey);
            if (why != null) return Refuse(SlotCodes.SlotUnknown, why);
            int i = MiddleIndex(sectionKey);
            var next = draft.Clone();
            int returned = next.fits.RemoveAll(f => f.section == sectionKey);
            next.fittings.RemoveAll(f => f?.socketId != null && f.socketId.StartsWith(sectionKey + "/"));
            next.equipment.RemoveAll(e => e?.slotId != null && (e.slotId.StartsWith(sectionKey + "/") || e.slotId.StartsWith("fitting:" + sectionKey + "/")));
            next.middleIds.RemoveAt(i);
            ShipConfiguration.ShiftMiddleKeys(next, i + 1, -1);
            if (IsW1xFamily(next)) Recompute(next);
            if (!Assembles(next, out var r)) return Refuse(r);
            return Commit(next, null, returned > 0 ? $"Removed {SlotModel.SectionName(sectionKey)}; {returned} modules went to the store." : $"Removed {SlotModel.SectionName(sectionKey)}.");
        }

        /// Opens the next deck up on one section: Upper (a raised section --
        /// wide beam only -- or the foredeck on a standard bow), then Top
        /// (a third deck).
        public bool RaiseDeck(string sectionKey)
        {
            var s = SlotModel.Find(SlotModel.Layout(draft, lib), sectionKey);
            if (s == null) return Refuse(SlotCodes.SlotUnknown, "That section is not on this ship.");
            int target = s.TopDeck + 1;
            var why = RaiseWhy(s, target);
            if (why != null) return Refuse(SlotCodes.DeckLocked, why);
            var next = RaisedConfig(s, target);
            if (!Assembles(next, out var r)) return Refuse(r);
            return Commit(next, null, $"{SlotModel.SectionName(s.key)} now has a {SlotDeck.Name(target).ToLowerInvariant()} deck.");
        }

        /// Takes the top deck of one section off; its modules go to the store.
        public bool LowerDeck(string sectionKey)
        {
            var s = SlotModel.Find(SlotModel.Layout(draft, lib), sectionKey);
            if (s == null) return Refuse(SlotCodes.SlotUnknown, "That section is not on this ship.");
            var why = LowerWhy(s);
            if (why != null) return Refuse(SlotCodes.DeckLocked, why);
            int top = s.TopDeck;
            var next = draft.Clone();
            bool foredeck = UpperDeckLayers.Has(next, sectionKey) && LayerIs(next, sectionKey, UpperDeckLayers.Foredeck);
            if (top == SlotDeck.Top || (top == SlotDeck.Upper && foredeck))
                next = UpperDeckLayers.With(next, sectionKey, false, lib);
            else
            {
                SetLevel(next, sectionKey, DeckLevel.Low);
                UpperDeckLayers.DropOrphaned(next, lib);
            }
            if (!Assembles(next, out var r)) return Refuse(r);
            return Commit(next, null, $"{SlotModel.SectionName(sectionKey)} lost its {SlotDeck.Name(top)}.");
        }

        public void Undo()
        {
            if (!CanUndo) return;
            var (cfg, b) = undo.Pop();
            draft = cfg; builds.Clear(); builds.AddRange(b);
            Message = ""; MessageCode = "";
            Changed?.Invoke();
        }

        /// The blockers and warnings of the draft as it stands.
        public ShipyardSlotsView Validate() => View();

        /// Builds the draft's modules, refits and saves -- atomic, through
        /// the backend. False (with Message) and nothing changed on refusal.
        public bool Apply()
        {
            if (Applied) return Refuse("APPLIED", "This refit is already confirmed.");
            if (!Dirty) return Refuse("NO_CHANGES", "No changes yet.");
            if (backend == null) return Refuse("NO_BACKEND", "Preview only. Live refitting is not connected.");
            var v = View();
            if (v.blockers.Count > 0) return Refuse(v.blockers[0].code, v.blockers[0].text);
            if (!backend.TryApply(baseline.Clone(), draft.Clone(), new List<string>(builds), out string reason))
                return Refuse("APPLY_REFUSED", string.IsNullOrEmpty(reason) ? "Refit could not be applied. Your ship is unchanged." : reason);
            Applied = true; undo.Clear();
            Message = "Refit confirmed."; MessageCode = "";
            Changed?.Invoke();
            return true;
        }

        // ---- helpers ----------------------------------------------------------------

        string Name(string moduleId) => lib.Catalog != null && lib.Catalog.TryGet(moduleId, out var m) ? m.displayName : moduleId;

        static int MiddleIndex(string key) =>
            key != null && key.StartsWith("middle[") && key.EndsWith("]") && int.TryParse(key.Substring(7, key.Length - 8), out int i) ? i : -1;

        string RemoveSectionWhy(string key)
        {
            int i = MiddleIndex(key);
            if (i < 0 || i >= draft.middleIds.Count) return "Only a middle section can be removed; the bow and stern must remain.";
            return null;
        }

        static bool IsW1xFamily(ShipConfiguration c)
        {
            if (c.sternId == null || !c.sternId.Contains(".w1x") || c.bowId == null || !c.bowId.Contains(".w1x")) return false;
            foreach (var m in c.middleIds) if (m == null || !m.Contains(".w1x")) return false;
            return true;
        }

        static void Recompute(ShipConfiguration c)
        {
            var lv = RaisedSections.FromIds(c.sternId, c.middleIds, c.bowId);
            var ids = RaisedSections.RecomputeIds(lv);
            c.sternId = ids.sternId; c.bowId = ids.bowId;
            c.middleIds = new List<string>(ids.middleIds);
        }

        static void SetLevel(ShipConfiguration c, string key, DeckLevel level)
        {
            var lv = RaisedSections.FromIds(c.sternId, c.middleIds, c.bowId);
            int i = MiddleIndex(key);
            if (key == ShipAssembler.StdKeyStern) lv.stern = level;
            else if (key == ShipAssembler.StdKeyBow) lv.bow = level;
            else if (i >= 0 && i < lv.middles.Length) lv.middles[i] = level;
            var ids = RaisedSections.RecomputeIds(lv);
            c.sternId = ids.sternId; c.bowId = ids.bowId;
            c.middleIds = new List<string>(ids.middleIds);
        }

        static bool LayerIs(ShipConfiguration c, string key, string moduleId)
        {
            foreach (var f in c.fittings) if (f?.socketId != null && f.socketId.StartsWith(key + "/") && f.moduleId == moduleId) return true;
            return false;
        }

        /// Why `target` cannot be opened on `s` by RaiseDeck now (null = it can).
        string RaiseWhy(SectionSlots s, int target)
        {
            if (target >= SlotDeck.Count) return "This section has every deck it can have.";
            if (target != s.TopDeck + 1) return $"Open the {SlotDeck.Name(target - 1)} first.";
            var lim = DockLimits.For(lib, DockLevel);
            if (lim != null && target > lim.maxDeck) return $"Dry dock {SlotModel.Roman(lim.level)} does not build a {SlotDeck.Name(target)} deck.";
            var next = RaisedConfig(s, target);
            if (next == null)
                return target == SlotDeck.Upper
                    ? (s.wide ? "This section cannot be raised." : "An upper deck needs the wide beam.")
                    : "No top deck fits this section.";
            if (!Assembles(next, out var r)) return r.message;
            return null;
        }

        /// The config with `target` opened on `s`, or null when no module does it.
        ShipConfiguration RaisedConfig(SectionSlots s, int target)
        {
            var next = draft.Clone();
            if (target == SlotDeck.Upper)
            {
                if (IsW1xFamily(next) && RaisedSections.LevelOf(s.moduleId) == DeckLevel.Low)
                {
                    SetLevel(next, s.key, DeckLevel.Raised);
                    UpperDeckLayers.DropOrphaned(next, lib);
                    return next;
                }
                string opt = UpperDeckLayers.OptionFor(next, s.key, lib, out _);
                if (opt == UpperDeckLayers.Foredeck) return UpperDeckLayers.With(next, s.key, true, lib);
                return null;
            }
            if (target == SlotDeck.Top)
            {
                string opt = UpperDeckLayers.OptionFor(next, s.key, lib, out _);
                if (opt != null && opt != UpperDeckLayers.Foredeck && !UpperDeckLayers.Has(next, s.key)) return UpperDeckLayers.With(next, s.key, true, lib);
                return null;
            }
            return null;
        }

        string LowerWhy(SectionSlots s) => s.TopDeck <= SlotDeck.Deck ? "The Hold and the Deck are part of the hull." : null;

        bool Assembles(ShipConfiguration next, out Rejection why)
        {
            why = null;
            var n = SlotModel.Normalized(next, lib);
            var r = ShipAssembler.Assemble(n, lib);
            if (r.ok) return true;
            why = r.rejections.Count > 0 ? r.rejections[0] : new Rejection { code = "ASSEMBLY_FAILED", partId = "", message = "This configuration is unavailable." };
            return false;
        }
    }
}
