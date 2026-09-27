using System;
using System.Collections.Generic;

namespace SeaSick.Ship.Modular
{
    // ---------------------------------------------------------------------
    // Shipyard SLOTS, data half (2026-09-27, Kevin's decisions; design page
    // scratchpad shipyard/index.html, PHASE 1). The player fills a small plan
    // grid per deck per hull section with MODULES (cannon, bunk, crate, ...)
    // built at the dock; the ship's capacity is the SUM of what is fitted
    // (replacing the sections' authored capacity blocks). All numbers are
    // data: the grids and dock levels live in standards.json `slotModel`,
    // one JSON per module under Resources/ShipModules/Catalog.
    // See docs/SHIPYARD-API.md "Slots API".
    // ---------------------------------------------------------------------

    /// Deck index on a section: 0 Hold (below the deck, no gun ports),
    /// 1 Deck, 2 Upper (raised deck / foredeck), 3 Top (third deck).
    public static class SlotDeck
    {
        public const int Hold = 0, Deck = 1, Upper = 2, Top = 3, Count = 4;
        public static readonly string[] Names = { "Hold", "Deck", "Upper", "Top" };
        public static string Name(int d) => d >= 0 && d < Count ? Names[d] : "Deck " + d;
    }

    /// Plan-view row of a cell (port = authoring +Y = game -X).
    public static class SlotRow
    {
        public const string Port = "Port", Mid = "Mid", Stbd = "Stbd";
    }

    /// Where a catalog module may stand.
    public static class SlotPlacement
    {
        /// Gun ports only (a cannon).
        public const string Port = "port";
        /// Any cell that is NOT a gun port.
        public const string Inner = "inner";
        /// Any cell, port or not.
        public const string Any = "any";
        /// A Hold cell only.
        public const string Hold = "hold";
        /// A cell on the section's top-most unlocked deck (never the Hold).
        public const string Topmost = "topmost";
    }

    /// One cell of a plan grid.
    [Serializable]
    public class SlotCellDef
    {
        /// Stable within its grid ("P0", "S0", "M1"); saves store it.
        public string id;
        public string row = SlotRow.Port;
        /// The one gun port per side on this deck (never on the Hold).
        public bool gunPort;
        /// Position along the section, 0 = aft interface, 1 = forward end.
        public float along = 0.5f;
    }

    /// One plan grid: a section kind x deck(s) x beam.
    [Serializable]
    public class SlotGridDef
    {
        /// ModuleKind.Stern / Middle / Bow.
        public string sectionKind;
        /// Deck indices this grid serves (the Hold grid: [0]; the deck grid: [1,2,3]).
        public int[] decks;
        /// "standard" (W1-r2, half-beam <= wideHalfBeamU) or "wide" (W1x / W1xR).
        public string beam = "standard";
        public SlotCellDef[] cells;
    }

    [Serializable]
    public class DockCost
    {
        public string item;
        public int count;
    }

    /// What one dry dock level allows (the design page's progression
    /// table). Levels 2-5 are DATA ONLY in phase 1 -- see DockLimits.
    [Serializable]
    public class DockLevelDef
    {
        public int level;
        public string name;
        /// Hull sections in total (stern + middles + bow).
        public int maxSections = 3;
        /// Highest deck index allowed anywhere (1 = Hold + Deck only).
        public int maxDeck = 1;
        public bool wideBeam;
        /// How many sections may carry an Upper (deck 2) / Top (deck 3). -1 = all.
        public int maxUpperSections;
        public int maxTopSections;
        /// Fire level the upgrade TO this level needs (0 = none).
        public int fireLevel;
        public DockCost[] upgradeCost;
        public string feel;
    }

    /// standards.json `slotModel`.
    [Serializable]
    public class SlotModelDef
    {
        public int schemaVersion = 1;
        /// Berths the stern's fixed cabin always gives (part of the hull, so
        /// an empty hull can sail). Counted on the stern section.
        public int sternCabinBerths = 2;
        /// A section whose join profile's half-beam is above this uses the
        /// "wide" grids.
        public float wideHalfBeamU = 5f;
        /// Authoring Z of each deck's floor, index = SlotDeck (mass/CG).
        public float[] deckZU;
        /// Catalog module ids, loaded from Resources/ShipModules/Catalog/<id>.json.
        public string[] catalog;
        public SlotGridDef[] grids;
        public DockLevelDef[] dockLevels;
        public string notes;
    }

    [Serializable]
    public class CatalogCapacity
    {
        /// Cargo cells (VoyageManager hold units) this module adds.
        public int cargo;
        public int berths;
    }

    /// One buildable module (Resources/ShipModules/Catalog/<id>.json).
    [Serializable]
    public class CatalogModule
    {
        public int schemaVersion = 1;
        /// Stable, dotted, never reused ("module.cannon"). Saves and the
        /// dry-dock store key on it.
        public string id;
        public int version = 1;
        public string displayName;
        public string description;
        /// SlotPlacement.*
        public string placement = SlotPlacement.Inner;
        public CatalogCapacity capacity = new CatalogCapacity();
        /// Hands it needs to work (a cannon 1, the repair bench 1).
        public int crew;
        /// PROVISIONAL. Its own mass, kg, at its slot's position.
        public float massKg;
        public bool onePerShip;
        /// Lowest dry-dock level that can build and fit it.
        public int dockLevel = 1;
        /// A cannon: the Equipment module the assembler/battery draws for it
        /// ("equipment.cannon.astra.v1"). Empty for everything else.
        public string equipmentId;
        /// Free-form effect tags for later systems ("bail:2", "spot:1.5", "repair").
        public string[] effects;
        /// Provisional build price, shown struck through while building is free.
        public DockCost[] buildCost;

        public bool IsCannon => !string.IsNullOrEmpty(equipmentId);
    }

    /// The loaded catalog. Never guesses: an unreadable or newer file is
    /// left out and the reason kept in `errors`.
    public class ModuleCatalog
    {
        public const int SupportedSchemaVersion = 1;
        public const string ResourceFolder = "ShipModules/Catalog";

        public const string Cannon = "module.cannon";
        public const string Bunk = "module.bunk";
        public const string Crate = "module.crate";
        public const string BilgePump = "module.bilgepump";
        public const string Lookout = "module.lookout";
        public const string RepairBench = "module.repairbench";

        readonly Dictionary<string, CatalogModule> byId = new Dictionary<string, CatalogModule>();
        readonly List<CatalogModule> ordered = new List<CatalogModule>();
        public readonly List<string> errors = new List<string>();
        public IReadOnlyList<CatalogModule> All => ordered;
        public bool Ok => errors.Count == 0 && ordered.Count > 0;

        public bool TryGet(string id, out CatalogModule m)
        {
            m = null;
            return !string.IsNullOrEmpty(id) && byId.TryGetValue(id, out m);
        }

        /// The catalog id whose `equipmentId` is `equipmentModuleId` (a v2
        /// cannon in the dry dock -> "module.cannon"), or null.
        public string ForEquipment(string equipmentModuleId)
        {
            if (string.IsNullOrEmpty(equipmentModuleId)) return null;
            foreach (var m in ordered) if (m.equipmentId == equipmentModuleId) return m.id;
            return null;
        }

        /// `readText`: Resources path (no extension) -> JSON text, or null.
        public static ModuleCatalog Load(IEnumerable<string> ids, Func<string, string> readText)
        {
            var c = new ModuleCatalog();
            if (ids == null) { c.errors.Add("CATALOG_EMPTY: standards.json slotModel.catalog lists no modules."); return c; }
            foreach (var id in ids)
            {
                string text = null;
                try { text = readText?.Invoke(ResourceFolder + "/" + id); } catch (Exception e) { c.errors.Add($"{id}: {e.Message}"); continue; }
                if (string.IsNullOrEmpty(text)) { c.errors.Add($"{id}: no file at Resources/{ResourceFolder}/{id}.json"); continue; }
                CatalogModule m;
                try { m = ModularJson.From<CatalogModule>(text); } catch (Exception e) { c.errors.Add($"{id}: unreadable ({e.Message})"); continue; }
                if (m == null || m.id != id) { c.errors.Add($"{id}: file id '{m?.id}' does not match its name"); continue; }
                if (m.schemaVersion > SupportedSchemaVersion) { c.errors.Add($"{id}: schema {m.schemaVersion} is newer than {SupportedSchemaVersion}"); continue; }
                if (m.capacity == null) m.capacity = new CatalogCapacity();
                if (c.byId.ContainsKey(id)) { c.errors.Add($"{id}: listed twice"); continue; }
                c.byId[id] = m; c.ordered.Add(m);
            }
            return c;
        }
    }

    /// One fitted module: which cell of which deck of which section.
    [Serializable]
    public class SlotFit
    {
        /// "stern", "middle[i]", "bow" (ShipAssembler instance keys).
        public string section;
        public int deck;
        public string cell;
        /// Catalog id ("module.bunk").
        public string moduleId;

        public SlotFit Copy() => new SlotFit { section = section, deck = deck, cell = cell, moduleId = moduleId };
        public bool SameCell(string s, int d, string c) => section == s && deck == d && cell == c;
        public override string ToString() => $"{section}/{SlotDeck.Name(deck)}/{cell}={moduleId}";
    }

    /// What a dry-dock level allows, from `slotModel.dockLevels`.
    public static class DockLimits
    {
        public const int MinLevel = 1, MaxLevel = 5;

        /// Phase 1: the yard is NOT limited by the dock building's level yet
        /// (today's prototype already offers wide, raised and 3 bays). The
        /// service passes this until phase 4 wires the building's level in.
        public const int Unenforced = MaxLevel;

        public static DockLevelDef For(ModuleLibrary lib, int level)
        {
            var rows = lib?.Standards?.slotModel?.dockLevels;
            if (rows == null || rows.Length == 0) return null;
            DockLevelDef best = null;
            foreach (var r in rows)
                if (r != null && r.level <= level && (best == null || r.level > best.level)) best = r;
            return best ?? rows[0];
        }
    }
}
