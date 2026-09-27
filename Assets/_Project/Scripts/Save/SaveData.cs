using System.Collections.Generic;
using SeaSick.World;

namespace SeaSick.Save
{
    /// **The whole playtest save, as one JSON file.**
    ///
    /// Written by `JsonUtility`, so everything here is a `[Serializable]`
    /// class of public fields, lists rather than dictionaries, and no
    /// polymorphism. The trap that shape brings, written down once: a null
    /// reference field comes BACK as a default-constructed object, not as
    /// null -- `OutpostLedger.pending` has to be re-nulled on load (see
    /// `Outpost.Adopt`).
    ///
    /// D4 (docs/PLAN-island-outposts.md) said what a save must carry: the
    /// world seed, the ship's rung and fittings, home, and every ledger.
    /// This is that, plus where each building stands and where the ship is.
    [System.Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;

        /// `WorldSettings.seed` the world was built with. A save from a
        /// different world is not loaded: the island under a camp would be
        /// a different island.
        public int worldSeed;

        /// When, in wall-clock terms, for the CONTINUE line.
        public string savedAt = "";
        /// Why it was written -- an anchor, a departure, a building, the
        /// button -- so the console can say.
        public string reason = "";

        /// **The slot system's label** (`SaveSlots`, 2026-09-26). A manual
        /// save (`SaveManual`/`Rename`) writes whatever the player chose;
        /// an autosave writes its own slot's default name (`"Autosave 1"`
        /// etc.), never a custom one. Empty in a save written before slots
        /// existed (the migrated file too: migration copies the bytes
        /// as-is), which reads back as "" here, not null (see the class
        /// doc) -- `SaveSlots` falls back to a default label whenever this
        /// is blank. Added after version 1 shipped -- NOT a version bump,
        /// same reasoning as `modular` below: an old save must still load,
        /// just without a custom name.
        public string slotDisplayName = "";

        /// `TimeOfDay.Seconds`. Scrubbed back FIRST on load, before any ledger
        /// is asked to catch up.
        public double timeSeconds;

        /// `DateTime.UtcNow.Ticks` when written (2026-09-27, time away). 0 on
        /// a save from before the field: no time away is played for it.
        public long savedAtUtcTicks;

        public ShipSave ship = new ShipSave();

        /// The hold, per resource.
        public List<StoreEntry> hold = new List<StoreEntry>();
        /// The stores at home, per resource.
        public List<StoreEntry> banked = new List<StoreEntry>();

        public List<OutpostSave> outposts = new List<OutpostSave>();

        /// **What the player has seen of the archipelago** — one row per
        /// island that is more than `Seen.Never`. The world is deterministic
        /// from `worldSeed`, so which islands EXIST needs no saving; which
        /// ones are on the chart is knowledge, and knowledge is save state.
        /// See `SeaSick.World.Discovery`.
        public List<SeenSave> seen = new List<SeenSave>();

        /// **The ship's wake**, as three parallel lists — the same shape
        /// `OutpostLedger.Absence` uses, and for the same reason:
        /// `JsonUtility` will not serialize a dictionary or a `Vector2`
        /// list's worth of structure any more happily than this. `trackAt`
        /// is `TimeOfDay.Seconds` per sample, so the chart can still age the
        /// buffer out to one game day after a load.
        public List<float> trackX = new List<float>();
        public List<float> trackZ = new List<float>();
        public List<double> trackAt = new List<double>();

        /// **The global life registry** (death/rescue phase 1, 2026-09-27,
        /// `SeaSick.World.Life.Lives`): one row per name with any event on
        /// them, and one per grave. Global rather than per-outpost -- a hand
        /// moves between the ship and any camp over a life. Added after
        /// version 1 shipped and deliberately NOT a version bump, same
        /// reasoning as `seen`/`trackX` above: an old save has nobody's
        /// story logged yet, which is exactly what an empty list means.
        public List<SeaSick.World.Life.LifeRecord> lives = new List<SeaSick.World.Life.LifeRecord>();
        public List<SeaSick.World.Life.GraveRecord> graveyard = new List<SeaSick.World.Life.GraveRecord>();

        /// **Phase 5a (man overboard).** Crew the sea gave back to an
        /// island rather than the ship -- phase 7 fetches them. Added after
        /// version 1 shipped, deliberately NOT a version bump, same
        /// reasoning as `lives`/`graveyard` above.
        public List<SeaSick.World.Life.CastawayRecord> castaways = new List<SeaSick.World.Life.CastawayRecord>();

        /// **Phase 5a.** Set once the scripted first man-overboard has
        /// resolved (rescued, washed ashore, or lost) -- until then, no
        /// OTHER crew member can go over, and once it fires nobody else's
        /// grip is even ticked down. False (absent) in an old save is
        /// exactly right: the scripted event still has to happen for them.
        public bool firstOverboardDone;
    }

    [System.Serializable]
    public class StoreEntry
    {
        public string resource;
        public int count;
    }

    /// One bay decision. Bay and tier are kept apart rather than as
    /// `Shipyard.Key`, because the key joins them with an underscore and a
    /// label may contain one.
    [System.Serializable]
    public class CellSave
    {
        public string bay;
        public string tier;
        public int use;
    }

    [System.Serializable]
    public class ShipSave
    {
        /// `Shipyard.NodeIndex`.
        public int rung;
        /// One per `FitTrack`, in enum order.
        public List<int> fit = new List<int>();
        /// Every cell that is not empty.
        public List<CellSave> cells = new List<CellSave>();

        public float x, y, z;
        /// World degrees. Pitch and roll are the water's business.
        public float yaw;

        /// 0 = under way, 1 = anchored off an island, 2 = alongside at home.
        public int anchor;

        /// **The hands she is carrying who were never in the scene.**
        ///
        /// The authored crew come back for free: the ship is rebuilt from her
        /// rung and the bodies are her own children. A villager recruited at a
        /// camp (`OutpostHand.born`) and then carried aboard is not -- his
        /// body was cloned at runtime, and without his name written down here
        /// he would simply not be on the ship the next time she sailed.
        ///
        /// Names only: the figure is a clone of a hand already aboard, so
        /// there is nothing else about him to keep. Empty in an old save,
        /// which is exactly right -- an old save has none.
        public List<string> crewNames = new List<string>();

        /// **Her modular configuration** (`SeaSick.Ship.Modular.ShipConfiguration`
        /// as its own versioned JSON), written only once she has been refitted
        /// or loaded from a save that had one. Empty in an old save -- and in
        /// a new one whose ship was never refitted -- which loads as the
        /// standard long steamer, exactly as before. Added after version 1
        /// shipped and deliberately NOT a version bump (see `SaveGame.Read`).
        public string modular = "";

        /// **Her dry dock** (`SeaSick.Ship.Modular.DryDock` as its own
        /// versioned JSON): equipment a refit has taken off her, waiting to
        /// be fitted again. Empty while the dock is empty (the common case).
        /// Added after version 1 shipped, next to `modular` and the same way
        /// -- NOT a version bump (see `SaveGame.Read`).
        public string dryDock = "";

        /// **Her home berth** (2026-09-25, Kevin: "make my home berth the
        /// pier I built at island_2"), as the world XZ of the chosen
        /// `Dock.Berth` -- harbour or a player's pier, whichever `Dock.Home`
        /// is at the moment of saving. Matched back to a `Dock` by nearest
        /// position once every outpost (and the piers it raises) is rebuilt
        /// (`SaveGame.Restore`, after step 5). `hasHomeBerth` false in an old
        /// save changes nothing: `Dock.Home` is already the harbour, from
        /// the world build's own `Configure`. Added after version 1 shipped
        /// -- NOT a version bump, same as `modular`/`dryDock`.
        public bool hasHomeBerth;
        public float homeBerthX, homeBerthZ;
    }

    [System.Serializable]
    public class OutpostSave
    {
        /// The ledger as it is. Its key (`keyX`/`keyZ`) is how the island is
        /// found again on load: rounded camp XZ, never an island index (D3).
        public OutpostLedger ledger;

        /// Where the fire is -- `Outpost.CampCentre` -- and whether the
        /// player put it there or it is still the survey's guess.
        public float campX, campY, campZ;
        public bool hasCampCentre;

        /// The island the camp is ON, by its centre. The land mask under a
        /// shore camp resolved to the neighbouring islet once (2026-09-21:
        /// a camp on Island_1 came back inside Island_3's outpost, and the
        /// sheet offered to make a new camp on Island_1), so the mask is the
        /// fallback now, not the rule. Islands are deterministic from the
        /// seed, so a centre is as stable as the world itself.
        public bool hasIsle;
        public float isleX, isleZ;

        /// True for outpost zero. Home is never surveyed on load; it is
        /// there from the world build and adopts the ledger in place.
        public bool isHome;
    }
}
