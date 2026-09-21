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

        /// `TimeOfDay.Seconds`. Scrubbed back FIRST on load, before any ledger
        /// is asked to catch up.
        public double timeSeconds;

        public ShipSave ship = new ShipSave();

        /// The hold, per resource.
        public List<StoreEntry> hold = new List<StoreEntry>();
        /// The stores at home, per resource.
        public List<StoreEntry> banked = new List<StoreEntry>();

        public List<OutpostSave> outposts = new List<OutpostSave>();
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
