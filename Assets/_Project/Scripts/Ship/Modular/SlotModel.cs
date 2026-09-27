using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// Stable codes of the slot model (2026-09-27). Blockers unless noted.
    public static class SlotCodes
    {
        /// A module stands in a cell its placement rule forbids (a cannon
        /// off a gun port, a bunk in a port, a pump above the Hold, ...).
        public const string SlotWrongKind = "SLOT_WRONG_KIND";
        /// The module needs a higher dry-dock level than this dock has.
        public const string ModuleLocked = "MODULE_LOCKED";
        /// A one-per-ship module is fitted more than once.
        public const string OnePerShip = "ONE_PER_SHIP";
        /// The HULL is bigger than this dock level builds (sections, decks, beam).
        public const string DockLevel = "DOCK_LEVEL";
        /// WARNING: more cannons than hands aboard; the extra guns stay silent.
        public const string GunsShortOfHands = "GUNS_SHORT_OF_HANDS";
        /// WARNING: a cannon stands in a port the art has no gun mount for
        /// yet (e.g. a Top deck); it counts, but the battery cannot draw it.
        public const string GunPortNoMount = "GUN_PORT_NO_MOUNT";
        /// Command refusals (never in a report of a normalized draft).
        public const string SlotOccupied = "SLOT_OCCUPIED";
        public const string SlotUnknown = "SLOT_UNKNOWN";
        public const string DeckLocked = "DECK_LOCKED";
        public const string NotInStore = "NOT_IN_STORE";
        public const string ModuleUnknown = "MODULE_UNKNOWN";
        public const string NoCatalog = "NO_CATALOG";
    }

    /// One hull section's slot layout for a configuration.
    public class SectionSlots
    {
        public string key, kind, moduleId;
        public bool wide;
        public float lengthU;
        /// Index = SlotDeck. Hold and Deck are always unlocked.
        public readonly bool[] unlocked = new bool[SlotDeck.Count];
        /// Index = SlotDeck; empty array for a deck with no grid data.
        public readonly SlotCellDef[][] cells = new SlotCellDef[SlotDeck.Count][];
        public int TopDeck { get { int t = 0; for (int d = 0; d < SlotDeck.Count; d++) if (unlocked[d]) t = d; return t; } }

        public SlotCellDef Cell(int deck, string id)
        {
            if (deck < 0 || deck >= SlotDeck.Count || cells[deck] == null) return null;
            foreach (var c in cells[deck]) if (c != null && c.id == id) return c;
            return null;
        }
    }

    /// A deck-gun mount (an authored gun slot) a port cell's cannon uses.
    public struct GunMount
    {
        public int deck;
        public string row;
        /// Qualified equipment slot id ("middle[0]/DeckSlot_1_1",
        /// "fitting:bow/ForedeckMount/Foredeck_Cannon_Port").
        public string slotId;
    }

    /// Sum of a configuration's fits.
    public struct SlotTotals
    {
        public int cargo, berths, cabinBerths, cannons, gunPorts, crewNeeded, cells, cellsUsed;
        public float massKg, nonCannonMassKg;
        public override string ToString() =>
            $"cargo {cargo}, berths {berths} (cabin {cabinBerths}), cannons {cannons}/{gunPorts} ports, crew needed {crewNeeded}, cells {cellsUsed}/{cells}, {massKg:0} kg";
    }

    /// The pure slot model: layouts, normalisation, v2 -> v3 migration,
    /// totals and rule checks. Nothing here touches a scene.
    public static class SlotModel
    {
        public const string StandardBeam = "standard", WideBeam = "wide";

        // ---- layout ---------------------------------------------------------

        public static List<string> SectionKeys(ShipConfiguration cfg)
        {
            var keys = new List<string>();
            if (cfg == null) return keys;
            if (!string.IsNullOrEmpty(cfg.sternId)) keys.Add(ShipAssembler.StdKeyStern);
            for (int i = 0; i < (cfg.middleIds?.Count ?? 0); i++) keys.Add(ShipAssembler.MiddleKey(i));
            if (!string.IsNullOrEmpty(cfg.bowId)) keys.Add(ShipAssembler.StdKeyBow);
            return keys;
        }

        public static string HullIdOf(ShipConfiguration cfg, string key)
        {
            if (cfg == null || string.IsNullOrEmpty(key)) return null;
            if (key == ShipAssembler.StdKeyStern) return cfg.sternId;
            if (key == ShipAssembler.StdKeyBow) return cfg.bowId;
            if (key.StartsWith("middle[") && key.EndsWith("]") && int.TryParse(key.Substring(7, key.Length - 8), out int i)
                && cfg.middleIds != null && i >= 0 && i < cfg.middleIds.Count) return cfg.middleIds[i];
            return null;
        }

        /// Every hull section of `cfg`, aft to fore, with its grids and
        /// which decks are open. Upper opens on a RAISED hull section (its
        /// profile has an upper deck) or under a foredeck layer; Top opens
        /// under a third-deck layer.
        public static List<SectionSlots> Layout(ShipConfiguration cfg, ModuleLibrary lib)
        {
            var list = new List<SectionSlots>();
            var sm = lib?.Standards?.slotModel;
            foreach (var key in SectionKeys(cfg))
            {
                string id = HullIdOf(cfg, key);
                var s = new SectionSlots { key = key, moduleId = id };
                list.Add(s);
                if (lib == null || !lib.TryGet(id, out var def)) continue;
                s.kind = def.kind;
                s.lengthU = def.lengthU;
                if (def.kind == ModuleKind.Bow)
                {
                    var stem = ModuleLibrary.FindSocket(def, SocketRole.HullStem);
                    if (stem != null) s.lengthU = stem.posU.x;
                }
                float half = 0f; bool raised = false;
                if (def.sockets != null)
                    foreach (var so in def.sockets)
                        if (so != null && (so.role == SocketRole.HullAft || so.role == SocketRole.HullFwd))
                        {
                            var prof = lib.FindProfile(so.standard);
                            if (prof == null) continue;
                            half = Mathf.Max(half, prof.halfBeamU);
                            if (prof.upperDeckZU > prof.deckZU) raised = true;
                        }
                // A walled raised variant joins on the LOW profile, so its id says it too.
                if (RaisedSections.LevelOf(id) == DeckLevel.Raised) raised = true;
                s.wide = sm != null && half > sm.wideHalfBeamU;
                bool foredeck = false, third = false;
                if (cfg.fittings != null)
                    foreach (var f in cfg.fittings)
                    {
                        if (f?.socketId == null || !f.socketId.StartsWith(key + "/") || !UpperDeckLayers.IsLayerId(f.moduleId)) continue;
                        if (f.moduleId == UpperDeckLayers.Foredeck) foredeck = true; else third = true;
                    }
                s.unlocked[SlotDeck.Hold] = true;
                s.unlocked[SlotDeck.Deck] = true;
                s.unlocked[SlotDeck.Upper] = raised || foredeck;
                s.unlocked[SlotDeck.Top] = s.unlocked[SlotDeck.Upper] && third;
                for (int d = 0; d < SlotDeck.Count; d++) s.cells[d] = GridFor(sm, def.kind, d, s.wide);
            }
            return list;
        }

        static SlotCellDef[] GridFor(SlotModelDef sm, string kind, int deck, bool wide)
        {
            if (sm?.grids == null) return Array.Empty<SlotCellDef>();
            string beam = wide ? WideBeam : StandardBeam;
            foreach (var g in sm.grids)
                if (g != null && g.sectionKind == kind && g.beam == beam && g.decks != null && Array.IndexOf(g.decks, deck) >= 0)
                    return g.cells ?? Array.Empty<SlotCellDef>();
            return Array.Empty<SlotCellDef>();
        }

        public static SectionSlots Find(List<SectionSlots> layout, string key)
        {
            foreach (var s in layout) if (s.key == key) return s;
            return null;
        }

        // ---- gun mounts -------------------------------------------------------

        /// The authored deck-gun slots of `key` (its hull module's and any
        /// upper-deck layer's `capacity.gunSlots`), classified by deck (the
        /// nearest `slotModel.deckZU`) and side. First per (deck, side) wins:
        /// ONE gun port per side per deck.
        public static List<GunMount> GunMounts(ShipConfiguration cfg, ModuleLibrary lib, string key)
        {
            var list = new List<GunMount>();
            string id = HullIdOf(cfg, key);
            if (lib == null || !lib.TryGet(id, out var hull)) return list;
            var owners = new List<(ModuleDef def, string prefix, float z0)> { (hull, key + "/", 0f) };
            if (cfg.fittings != null)
                foreach (var f in cfg.fittings)
                {
                    if (f?.socketId == null || !f.socketId.StartsWith(key + "/") || !UpperDeckLayers.IsLayerId(f.moduleId)) continue;
                    if (!lib.TryGet(f.moduleId, out var layer)) continue;
                    var mount = ModuleLibrary.FindSocketById(hull, f.socketId.Substring(key.Length + 1));
                    owners.Add((layer, "fitting:" + f.socketId + "/", mount != null ? mount.posU.z : 0f));
                }
            var zs = lib.Standards?.slotModel?.deckZU;
            foreach (var (def, prefix, z0) in owners)
            {
                var ids = def.capacity?.gunSlots?.ids;
                if (ids == null || def.equipmentSlots == null) continue;
                foreach (var gid in ids)
                {
                    EquipmentSlotDef es = null;
                    foreach (var e in def.equipmentSlots) if (e != null && e.id == gid) es = e;
                    var so = es != null ? ModuleLibrary.FindSocketById(def, es.socketId) : null;
                    if (so == null) continue;
                    int deck = DeckAt(zs, z0 + so.posU.z);
                    string row = so.posU.y > 0f ? SlotRow.Port : SlotRow.Stbd;
                    bool taken = false;
                    foreach (var m in list) if (m.deck == deck && m.row == row) taken = true;
                    if (!taken) list.Add(new GunMount { deck = deck, row = row, slotId = prefix + gid });
                }
            }
            return list;
        }

        static int DeckAt(float[] zs, float z)
        {
            if (zs == null || zs.Length < 2) return SlotDeck.Deck;
            int best = SlotDeck.Deck; float bd = float.MaxValue;
            for (int d = SlotDeck.Deck; d < zs.Length && d < SlotDeck.Count; d++)
            {
                float dz = Mathf.Abs(zs[d] - z);
                if (dz < bd) { bd = dz; best = d; }
            }
            return best;
        }

        // ---- normalisation ----------------------------------------------------

        /// A copy of `cfg` whose fits all stand in cells that exist on open
        /// decks (one per cell) and whose deck-gun `equipment` is DERIVED
        /// from its cannon fits (one EquipmentChoice per cannon on a port
        /// with a mount). Anything that has no place any more is removed and
        /// listed in `stranded` -- the dry-dock diff counts fits, so a
        /// stranded module lands in the store at Apply: never lost.
        /// A non-slot (v1/v2) config is returned as an unchanged copy.
        public static ShipConfiguration Normalized(ShipConfiguration cfg, ModuleLibrary lib, out List<SlotFit> stranded)
        {
            stranded = new List<SlotFit>();
            if (cfg == null) return null;
            var n = cfg.Clone();
            if (!n.UsesSlots || lib == null) return n;
            var layout = Layout(n, lib);
            var kept = new List<SlotFit>();
            foreach (var f in n.fits)
            {
                var s = Find(layout, f.section);
                bool ok = s != null && f.deck >= 0 && f.deck < SlotDeck.Count && s.unlocked[f.deck] && s.Cell(f.deck, f.cell) != null;
                if (ok) foreach (var k in kept) if (k.SameCell(f.section, f.deck, f.cell)) ok = false;
                if (ok) kept.Add(f); else stranded.Add(f.Copy());
            }
            kept.Sort((a, b) => Order(layout, a).CompareTo(Order(layout, b)));
            n.fits = kept;
            n.layouts.Clear();
            DeriveGunEquipment(n, lib, layout);
            return n;
        }

        public static ShipConfiguration Normalized(ShipConfiguration cfg, ModuleLibrary lib) => Normalized(cfg, lib, out _);

        static long Order(List<SectionSlots> layout, SlotFit f)
        {
            int si = layout.FindIndex(s => s.key == f.section);
            var s0 = si >= 0 ? layout[si] : null;
            int ci = 0;
            if (s0?.cells[f.deck] != null) ci = Array.FindIndex(s0.cells[f.deck], c => c != null && c.id == f.cell);
            return (long)si * 10000 + f.deck * 100 + ci;
        }

        static void DeriveGunEquipment(ShipConfiguration n, ModuleLibrary lib, List<SectionSlots> layout)
        {
            var cat = lib.Catalog;
            n.equipment.RemoveAll(e => e != null && IsDeckGun(lib, e.moduleId));
            if (cat == null) return;
            var mounts = new Dictionary<string, List<GunMount>>();
            foreach (var f in n.fits)
            {
                if (!cat.TryGet(f.moduleId, out var m) || !m.IsCannon) continue;
                var s = Find(layout, f.section);
                var cell = s?.Cell(f.deck, f.cell);
                if (cell == null || !cell.gunPort) continue;
                if (!mounts.TryGetValue(f.section, out var ms)) mounts[f.section] = ms = GunMounts(n, lib, f.section);
                foreach (var gm in ms)
                    if (gm.deck == f.deck && gm.row == cell.row)
                    {
                        n.equipment.Add(new EquipmentChoice { slotId = gm.slotId, moduleId = m.equipmentId });
                        break;
                    }
            }
        }

        static bool IsDeckGun(ModuleLibrary lib, string moduleId) =>
            lib.TryGet(moduleId, out var d) && d.equipment != null && d.equipment.equipmentClass == "equipment.deck-gun";

        /// The mount a cannon in this cell would use, or null.
        public static string MountFor(ShipConfiguration cfg, ModuleLibrary lib, string section, int deck, string row)
        {
            foreach (var gm in GunMounts(cfg, lib, section)) if (gm.deck == deck && gm.row == row) return gm.slotId;
            return null;
        }

        /// The gun-port cell a deck-gun mount (qualified equipment slot id)
        /// serves on `cfg`: false when no open port uses it.
        public static bool CellForMount(ShipConfiguration cfg, ModuleLibrary lib, string mountSlotId, out string section, out int deck, out string cellId)
        {
            section = null; deck = 0; cellId = null;
            var layout = Layout(cfg, lib);
            foreach (var s in layout)
                foreach (var gm in GunMounts(cfg, lib, s.key))
                {
                    if (gm.slotId != mountSlotId || !s.unlocked[gm.deck] || s.cells[gm.deck] == null) continue;
                    foreach (var c in s.cells[gm.deck])
                        if (c != null && c.gunPort && c.row == gm.row) { section = s.key; deck = gm.deck; cellId = c.id; return true; }
                }
            return false;
        }

        // ---- migration ----------------------------------------------------------

        /// A v1/v2 configuration as slots (schema 3), fitted so its capacity
        /// is what it was: every gun becomes a cannon in the port its mount
        /// stands on; hold cells become crates (ceil(hold / crate cargo)),
        /// berths beyond the stern cabin become bunks (ceil(extra / bunk
        /// berths)), placed in free non-port cells, Hold first, aft to fore.
        /// Today's Long -> 6 cannons, 4 crates, 3 bunks: 16 cargo, 8 berths
        /// (2 cabin + 6), 6 guns. Anything with no cell left goes to
        /// `overflow` (catalog ids the caller puts in the dry-dock store).
        /// A v3 config comes back normalized, its stranded fits in overflow.
        public static ShipConfiguration Migrate(ShipConfiguration cfg, ModuleLibrary lib, out List<string> overflow)
        {
            overflow = new List<string>();
            if (cfg == null) return null;
            if (cfg.UsesSlots)
            {
                var n = Normalized(cfg, lib, out var st);
                foreach (var f in st) overflow.Add(f.moduleId);
                return n;
            }
            var cat = lib?.Catalog;
            var v2 = cfg.MigratedToV2();
            int hold = 0, berths = 0;
            var asm = ShipAssembler.Assemble(v2, lib);
            if (asm.ok)
                foreach (var sc in ShipyardPlanner.SectionCapacities(asm, lib, v2, 0f, out _)) { hold += sc.holdCells; berths += sc.berths; }

            var m = v2.Clone();
            m.schemaVersion = ShipConfiguration.SlotsSchemaVersion;
            m.layouts.Clear();
            m.fits.Clear();
            var layout = Layout(m, lib);
            if (cat == null) return Normalized(m, lib);

            string cannonId = Cannon(cat);
            // Guns: each onto the port its mount is.
            foreach (var e in v2.equipment)
            {
                if (e == null || !IsDeckGun(lib, e.moduleId)) continue;
                string placed = null;
                foreach (var s in layout)
                {
                    foreach (var gm in GunMounts(m, lib, s.key))
                    {
                        if (gm.slotId != e.slotId || !s.unlocked[gm.deck]) continue;
                        var cells = s.cells[gm.deck];
                        if (cells == null) continue;
                        foreach (var c in cells)
                            if (c != null && c.gunPort && c.row == gm.row && !Occupied(m, s.key, gm.deck, c.id))
                            { m.fits.Add(new SlotFit { section = s.key, deck = gm.deck, cell = c.id, moduleId = cannonId }); placed = c.id; break; }
                    }
                    if (placed != null) break;
                }
                if (placed == null && cannonId != null) overflow.Add(cannonId);
            }
            // Hold -> crates, extra berths -> bunks.
            var crate = Best(cat, cm => cm.capacity.cargo > 0 && cm.capacity.berths == 0 && !cm.onePerShip);
            var bunk = Best(cat, cm => cm.capacity.berths > 0 && cm.capacity.cargo == 0 && !cm.onePerShip);
            int cabin = lib.Standards.slotModel != null ? lib.Standards.slotModel.sternCabinBerths : 0;
            var want = new List<string>();
            if (crate != null) for (int i = 0; i < CeilDiv(hold, crate.capacity.cargo); i++) want.Add(crate.id);
            if (bunk != null) for (int i = 0; i < CeilDiv(Mathf.Max(0, berths - cabin), bunk.capacity.berths); i++) want.Add(bunk.id);
            foreach (var id in want)
            {
                cat.TryGet(id, out var cm);
                bool done = false;
                for (int d = 0; d < SlotDeck.Count && !done; d++)
                    foreach (var s in layout)
                    {
                        if (!s.unlocked[d] || s.cells[d] == null) continue;
                        foreach (var c in s.cells[d])
                        {
                            if (c == null || Occupied(m, s.key, d, c.id) || !PlacementAllows(cm, s, d, c)) continue;
                            m.fits.Add(new SlotFit { section = s.key, deck = d, cell = c.id, moduleId = id });
                            done = true; break;
                        }
                        if (done) break;
                    }
                if (!done) overflow.Add(id);
            }
            return Normalized(m, lib);
        }

        static int CeilDiv(int a, int b) => b <= 0 || a <= 0 ? 0 : (a + b - 1) / b;

        static CatalogModule Best(ModuleCatalog cat, Func<CatalogModule, bool> ok)
        {
            foreach (var m in cat.All) if (ok(m) && m.dockLevel <= DockLimits.MinLevel) return m;
            foreach (var m in cat.All) if (ok(m)) return m;
            return null;
        }

        public static string Cannon(ModuleCatalog cat)
        {
            if (cat == null) return null;
            if (cat.TryGet(ModuleCatalog.Cannon, out _)) return ModuleCatalog.Cannon;
            foreach (var m in cat.All) if (m.IsCannon) return m.id;
            return null;
        }

        public static bool Occupied(ShipConfiguration cfg, string section, int deck, string cell)
        {
            foreach (var f in cfg.fits) if (f.SameCell(section, deck, cell)) return true;
            return false;
        }

        public static SlotFit At(ShipConfiguration cfg, string section, int deck, string cell)
        {
            if (cfg?.fits == null) return null;
            foreach (var f in cfg.fits) if (f.SameCell(section, deck, cell)) return f;
            return null;
        }

        // ---- rules ----------------------------------------------------------------

        /// Whether module `m`'s placement rule lets it stand in `cell` of deck
        /// `deck` of `s` (ignores occupancy, store, dock level).
        public static bool PlacementAllows(CatalogModule m, SectionSlots s, int deck, SlotCellDef cell)
        {
            if (m == null || s == null || cell == null) return false;
            switch (m.placement)
            {
                case SlotPlacement.Port: return cell.gunPort;
                case SlotPlacement.Any: return true;
                case SlotPlacement.Hold: return deck == SlotDeck.Hold;
                case SlotPlacement.Topmost: return deck != SlotDeck.Hold && deck == s.TopDeck;
                default: return !cell.gunPort; // Inner
            }
        }

        public static string PlacementText(CatalogModule m)
        {
            switch (m?.placement)
            {
                case SlotPlacement.Port: return "only in a gun port";
                case SlotPlacement.Hold: return "only in the Hold";
                case SlotPlacement.Topmost: return "only on the top-most deck";
                case SlotPlacement.Any: return "anywhere";
                default: return "anywhere but a gun port";
            }
        }

        /// Why `moduleId` cannot go into that cell of `cfg` (null = it can),
        /// ignoring the cell's current occupant and the store. `ignoreFit`
        /// is left out of the one-per-ship count (a move).
        public static Rejection CanPlace(ShipConfiguration cfg, ModuleLibrary lib, string section, int deck, string cellId,
            string moduleId, int dockLevel, SlotFit ignoreFit = null)
        {
            var cat = lib?.Catalog;
            if (cat == null) return new Rejection { code = SlotCodes.NoCatalog, partId = "", message = "The shipyard's module list could not be loaded." };
            if (!cat.TryGet(moduleId, out var m)) return new Rejection { code = SlotCodes.ModuleUnknown, partId = moduleId ?? "", message = $"There is no module called {moduleId}." };
            var s = Find(Layout(cfg, lib), section);
            var cell = s?.Cell(deck, cellId);
            if (s == null || deck < 0 || deck >= SlotDeck.Count) return new Rejection { code = SlotCodes.SlotUnknown, partId = section ?? "", message = "That section is not on this ship." };
            if (!s.unlocked[deck]) return new Rejection { code = SlotCodes.DeckLocked, partId = section, message = $"{SectionName(section)} has no {SlotDeck.Name(deck)} yet." };
            if (cell == null) return new Rejection { code = SlotCodes.SlotUnknown, partId = $"{section}/{deck}/{cellId}", message = "That slot does not exist." };
            var fitName = $"{section}/{SlotDeck.Name(deck)}/{cellId}";
            if (!PlacementAllows(m, s, deck, cell))
                return new Rejection { code = SlotCodes.SlotWrongKind, partId = fitName, message = $"A {m.displayName} goes {PlacementText(m)}." };
            if (m.dockLevel > dockLevel)
                return new Rejection { code = SlotCodes.ModuleLocked, partId = m.id, message = $"The {m.displayName} needs dry dock level {Roman(m.dockLevel)}." };
            if (m.onePerShip)
                foreach (var f in cfg.fits)
                    if (f.moduleId == m.id && f != ignoreFit && !f.SameCell(section, deck, cellId))
                        return new Rejection { code = SlotCodes.OnePerShip, partId = m.id, message = $"She already has a {m.displayName} ({SectionName(f.section)}, {SlotDeck.Name(f.deck)}); move it instead." };
            return null;
        }

        /// Every rule a whole (normalized) slot configuration breaks, and its
        /// warnings. `crewAboard` < 0 = unknown (no hands warning).
        public static void Check(ShipConfiguration cfg, ModuleLibrary lib, int dockLevel, int crewAboard,
            List<Rejection> blockers, List<ShipyardNote> warnings)
        {
            if (cfg == null || !cfg.UsesSlots) return;
            var cat = lib?.Catalog;
            if (cat == null || !cat.Ok)
            {
                blockers.Add(new Rejection { code = SlotCodes.NoCatalog, partId = "",
                    message = "The shipyard's module list could not be loaded" + (cat != null && cat.errors.Count > 0 ? " (" + cat.errors[0] + ")" : "") + "." });
                return;
            }
            var layout = Layout(cfg, lib);
            var seenOne = new HashSet<string>();
            foreach (var f in cfg.fits)
            {
                string part = $"{f.section}/{f.deck}/{f.cell}";
                if (!cat.TryGet(f.moduleId, out var m))
                {
                    blockers.Add(new Rejection { code = SlotCodes.ModuleUnknown, partId = part, message = $"{f.moduleId} is not a module this build knows." });
                    continue;
                }
                var s = Find(layout, f.section);
                var cell = s?.Cell(f.deck, f.cell);
                if (cell != null && !PlacementAllows(m, s, f.deck, cell))
                    blockers.Add(new Rejection { code = SlotCodes.SlotWrongKind, partId = part,
                        message = $"The {m.displayName} in {SectionName(f.section)}, {SlotDeck.Name(f.deck)} goes {PlacementText(m)}." });
                if (m.dockLevel > dockLevel)
                    blockers.Add(new Rejection { code = SlotCodes.ModuleLocked, partId = part,
                        message = $"The {m.displayName} needs dry dock level {Roman(m.dockLevel)}." });
                if (m.onePerShip && !seenOne.Add(m.id))
                    blockers.Add(new Rejection { code = SlotCodes.OnePerShip, partId = part,
                        message = $"Only one {m.displayName} per ship." });
                if (m.IsCannon && cell != null && cell.gunPort && MountFor(cfg, lib, f.section, f.deck, cell.row) == null)
                    warnings.Add(new ShipyardNote { code = SlotCodes.GunPortNoMount,
                        message = $"The cannon on {SectionName(f.section)}, {SlotDeck.Name(f.deck)} has no gun mount in the art yet; it will not fire." });
            }
            // The hull against the dock level.
            var lim = DockLimits.For(lib, dockLevel);
            if (lim != null)
            {
                int sections = layout.Count, upper = 0, top = 0; bool wide = false; int maxDeck = 0;
                foreach (var s in layout)
                {
                    if (s.unlocked[SlotDeck.Upper]) upper++;
                    if (s.unlocked[SlotDeck.Top]) top++;
                    if (s.wide) wide = true;
                    maxDeck = Mathf.Max(maxDeck, s.TopDeck);
                }
                string why = null;
                if (sections > lim.maxSections) why = $"{sections} sections (this dock builds {lim.maxSections})";
                else if (wide && !lim.wideBeam) why = "the wide beam";
                else if (maxDeck > lim.maxDeck) why = $"a {SlotDeck.Name(maxDeck)} deck";
                else if (lim.maxUpperSections >= 0 && upper > lim.maxUpperSections) why = $"{upper} upper decks (this dock builds {lim.maxUpperSections})";
                else if (lim.maxTopSections >= 0 && top > lim.maxTopSections) why = $"{top} top decks (this dock builds {lim.maxTopSections})";
                if (why != null)
                    blockers.Add(new Rejection { code = SlotCodes.DockLevel, partId = "dock",
                        message = $"Dry dock {Roman(lim.level)} ({lim.name}) cannot build {why}." });
            }
            if (crewAboard >= 0)
            {
                var t = Totals(cfg, lib);
                int hands = Mathf.Min(crewAboard, t.berths);
                if (t.cannons > hands)
                    warnings.Add(new ShipyardNote { code = SlotCodes.GunsShortOfHands,
                        message = $"{t.cannons} cannons and {Plural(hands, "hand")} aboard: {t.cannons - hands} will stay silent." });
            }
        }

        // ---- totals ---------------------------------------------------------------

        public static SlotTotals Totals(ShipConfiguration cfg, ModuleLibrary lib)
        {
            var t = new SlotTotals();
            if (cfg == null || lib == null) return t;
            var sm = lib.Standards?.slotModel;
            var layout = Layout(cfg, lib);
            foreach (var s in layout)
                for (int d = 0; d < SlotDeck.Count; d++)
                {
                    if (!s.unlocked[d] || s.cells[d] == null) continue;
                    t.cells += s.cells[d].Length;
                    foreach (var c in s.cells[d]) if (c != null && c.gunPort) t.gunPorts++;
                }
            if (sm != null && Find(layout, ShipAssembler.StdKeyStern) != null) t.cabinBerths = sm.sternCabinBerths;
            t.berths = t.cabinBerths;
            if (cfg.UsesSlots && lib.Catalog != null)
                foreach (var f in cfg.fits)
                {
                    t.cellsUsed++;
                    if (!lib.Catalog.TryGet(f.moduleId, out var m)) continue;
                    t.cargo += m.capacity.cargo;
                    t.berths += m.capacity.berths;
                    t.crewNeeded += m.crew;
                    t.massKg += m.massKg;
                    if (m.IsCannon) t.cannons++; else t.nonCannonMassKg += m.massKg;
                }
            return t;
        }

        /// The non-cannon fit-out mass of the standard steamer (Long
        /// migrated: 4 crates + 3 bunks). The hull modules' lightship masses
        /// were calibrated on today's steamer, which already carries that
        /// fit-out, so a slot ship's lightship is hull + (fit-out - this):
        /// the standard Long weighs exactly what she always did, an empty
        /// hull is lighter by it, extra modules heavier.
        public static float StandardFitOutKg(ModuleLibrary lib)
        {
            if (lib?.Catalog == null) return 0f;
            if (lib.standardFitOutKg < 0f) lib.standardFitOutKg = Totals(Migrate(ShipConfiguration.Long(), lib, out _), lib).nonCannonMassKg;
            return lib.standardFitOutKg;
        }

        /// One section's cargo cells and berths from its fits (the stern
        /// also carries the fixed cabin).
        public static void SectionTotals(ShipConfiguration cfg, ModuleLibrary lib, string key, out int cargo, out int berths)
        {
            cargo = 0; berths = 0;
            if (key == ShipAssembler.StdKeyStern && lib?.Standards?.slotModel != null) berths = lib.Standards.slotModel.sternCabinBerths;
            if (cfg?.fits == null || lib?.Catalog == null) return;
            foreach (var f in cfg.fits)
                if (f.section == key && lib.Catalog.TryGet(f.moduleId, out var m)) { cargo += m.capacity.cargo; berths += m.capacity.berths; }
        }

        /// Ship-frame position of a fit, metres (`sectionPosU` = the placed
        /// section's origin in assembly authoring units; `viewZ` = the
        /// plan's viewOffset.z). Game = (-y, z, x) * k.
        public static Vector3 FitPositionM(SectionSlots s, SlotFit f, Vector3 sectionPosU, float halfBeamU, float[] deckZU, float k, float viewZ)
        {
            var cell = s?.Cell(f.deck, f.cell);
            float along = cell != null ? cell.along : 0.5f;
            float y = cell == null || cell.row == SlotRow.Mid ? 0f : (cell.row == SlotRow.Port ? 0.55f : -0.55f) * halfBeamU;
            float z = deckZU != null && f.deck < deckZU.Length ? deckZU[f.deck] : 1.76f;
            float x = sectionPosU.x + along * (s != null ? s.lengthU : 0f);
            return new Vector3(-y * k, (sectionPosU.z + z) * k, viewZ + x * k);
        }

        // ---- the store --------------------------------------------------------------

        /// Catalog id -> count fitted on `cfg` (v3: its fits; v1/v2: its
        /// deck guns as cannons -- what the store would have to account for).
        public static Dictionary<string, int> FittedCounts(ShipConfiguration cfg)
        {
            var d = new Dictionary<string, int>();
            if (cfg?.fits == null || !cfg.UsesSlots) return d;
            foreach (var f in cfg.fits) d[f.moduleId] = (d.TryGetValue(f.moduleId, out var n) ? n : 0) + 1;
            return d;
        }

        /// A v2 dry dock's equipment ids renamed to their catalog ids (a
        /// stored "equipment.cannon.astra.v1" -> "module.cannon"). In place.
        public static void MigrateStore(DryDock dock, ModuleLibrary lib)
        {
            if (dock?.entries == null || lib?.Catalog == null) return;
            var moved = new List<(string from, string to, int n)>();
            foreach (var e in dock.entries)
            {
                if (e == null || lib.Catalog.TryGet(e.moduleId, out _)) continue;
                string to = lib.Catalog.ForEquipment(e.moduleId);
                if (to != null) moved.Add((e.moduleId, to, e.count));
            }
            foreach (var (from, to, n) in moved) { dock.TryTake(from, n); dock.Add(to, n); }
        }

        // ---- words ---------------------------------------------------------------------

        public static string SectionName(string key)
        {
            if (key == ShipAssembler.StdKeyStern) return "Stern";
            if (key == ShipAssembler.StdKeyBow) return "Bow";
            if (key != null && key.StartsWith("middle[") && key.EndsWith("]") && int.TryParse(key.Substring(7, key.Length - 8), out int i)) return "Mid " + (i + 1);
            return key ?? "";
        }

        public static string Roman(int n) => n >= 1 && n <= 5 ? new[] { "I", "II", "III", "IV", "V" }[n - 1] : n.ToString();
        static string Plural(int n, string w) => n == 1 ? $"1 {w}" : $"{n} {w}s";
    }
}
