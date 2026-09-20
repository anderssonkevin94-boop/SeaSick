using UnityEngine;

namespace SeaSick.World
{
    /// What a plan LOOKS like when it is raised.
    ///
    /// A plan with a `prefab` wears the authored model from
    /// `SettlementKitV1`; one without is extruded from its footprint and
    /// ridge. Both go up through the same call, so the blueprint, the ghost,
    /// the corner test and the footing are identical either way — which is
    /// what let six authored buildings join a system built for primitives
    /// without touching any of it.
    public enum BuildKind
    {
        /// Walls, corner posts and a thatched roof.
        Hut,
        /// A ring of stones and a few logs, with a light in it. Not a
        /// building -- the mark that somebody means to stay.
        Fire,
    }

    public struct BuildPlan
    {
        public string id;
        public BuildKind kind;
        /// What the button says.
        public string label;
        /// One line on what it buys you. The player is spending a voyage's
        /// haul; they should know what for before they spend it.
        public string blurb;
        public string resource;
        public int cost;
        /// Units of stores it adds to what this place can keep, PER RESOURCE.
        public int storeCapacity;
        /// Metres: length along the ridge, then width across it.
        public Vector2 footprint;
        /// Ridge height, off the charter in WorldScale.
        public float ridge;

        /// **The job somebody can be assigned to here**, or null if the
        /// building works on its own. This is the name the Assign menu shows,
        /// so it is a person ("sawyer"), not a place.
        public string position;
        /// What one assigned hand consumes, or null if the building's input is
        /// the ground it stands on.
        public string takes;
        /// What one assigned hand produces. Null means it produces nothing and
        /// its whole effect is in the numbers above.
        public string makes;
        /// Units made per assigned hand per day. **Every one is a guess.**
        public float rate;
        /// Hands this building supports living here. Declared now; starvation
        /// is a later pass.
        public int supports;

        /// Path under `Resources/` of the authored model, or null to extrude
        /// one. Loaded at raise time and quietly fallen back on, so a missing
        /// asset costs a plainer building and never a broken camp.
        public string prefab;
    }

    /// Everything that can be built, in the order it is offered.
    public static class BuildPlans
    {
        /// **The first building in the game.**
        ///
        /// Cost is set against the hold, not against a spreadsheet: she
        /// carries 24 to the marked line and 38 stuffed with deck cargo, so
        /// a storehouse is one full hold and a log over. **Two voyages,
        /// never one** -- and the second one has to come home, which is the
        /// decision the whole loop is made of.
        public static readonly BuildPlan Storehouse = new BuildPlan
        {
            id = "Storehouse",
            label = "storehouse",
            blurb = "keeps 40 more out of the weather",
            resource = Res.Timber,
            cost = 25,
            storeCapacity = 40,
            footprint = new Vector2(8f, 5f),
            ridge = WorldScale.Storehouse,
        };

        /// **The first thing you put on an island that is not home.**
        ///
        /// What it buys is a place that KEEPS things: ten of anything, which
        /// is what the fire can watch over, and the ceiling that stops a camp
        /// producing for ever. Everything after this raises that ceiling.
        ///
        /// **Four logs, which is one hand for one day** -- the rate is
        /// `OutpostLedger.TimberPerHandPerDay`, so the cost is legible in the
        /// only unit the player has: leave one man and he has a fire by
        /// nightfall, leave four and it is up before you have cleared the bay.
        /// **A guess, never played.**
        public static readonly BuildPlan Campfire = new BuildPlan
        {
            id = "Campfire",
            kind = BuildKind.Fire,
            label = "make camp",
            blurb = "a fire, and somewhere to keep ten of anything",
            resource = Res.Timber,
            cost = 4,
            storeCapacity = OutpostLedger.CampfireCeiling,
            footprint = new Vector2(3.13f, 1.78f),
            ridge = 0.84f,
            prefab = "Settlement/campfire_02",
        };

        // --- the camp buildings, 2026-09-19 ----------------------------------
        //
        // **All six wear the authored kit.** `SettlementKitV1` was built on
        // 2026-09-17 and sat unwired: fifteen models on a vertex palette,
        // scaled to the crew's own height, with `Entry`, `Resident` and worker
        // markers already in the prefabs. The footprints below are the
        // manifest's own, so a blueprint is the size of the thing that will
        // stand in it.
        //
        // Costs are in logs, and a log is a quarter of a hand-day. Read them
        // as days of one man's work: a hut is three, a store five, a farm
        // four, a sawmill six, a smithy seven. **All guesses, none played.**
        //
        // The footprints and ridges are NOT guesses. They are the measured
        // game bounds out of the kit's own import validation, so the four
        // corners the ground is tested at are the corners the building
        // actually stands on and the blueprint is the size of the thing.

        public static readonly BuildPlan Storage = new BuildPlan
        {
            id = "Storage",
            label = "store hut",
            blurb = "keeps 20 more of each thing",
            resource = Res.Timber,
            cost = 20,
            storeCapacity = 20,
            footprint = new Vector2(6.46f, 5.14f),
            ridge = 3.84f,
            prefab = "Settlement/storage",
        };

        public static readonly BuildPlan Hut = new BuildPlan
        {
            id = "Hut",
            label = "shelter",
            blurb = "somewhere for four hands to live",
            resource = Res.Timber,
            cost = 12,
            footprint = new Vector2(4.84f, 4.93f),
            ridge = 3.81f,
            supports = 4,
            prefab = "Settlement/hut_01",
        };

        public static readonly BuildPlan Sawmill = new BuildPlan
        {
            id = "Sawmill",
            label = "sawmill",
            blurb = "a sawyer turns timber into boards",
            resource = Res.Timber,
            cost = 24,
            footprint = new Vector2(7.56f, 5.85f),
            ridge = 3.84f,
            position = "sawyer",
            takes = Res.Timber,
            makes = Res.Boards,
            rate = 3f,
            prefab = "Settlement/sawmill",
        };

        public static readonly BuildPlan Farm = new BuildPlan
        {
            id = "Farm",
            label = "farm plot",
            blurb = "a farmhand grows food out of the ground",
            resource = Res.Timber,
            cost = 16,
            footprint = new Vector2(4.66f, 4.69f),
            ridge = 1.01f,
            position = "farmhand",
            makes = Res.Food,
            rate = 3f,
            prefab = "Settlement/farm_01",
        };

        public static readonly BuildPlan Blacksmith = new BuildPlan
        {
            id = "Blacksmith",
            label = "forge",
            blurb = "a smith turns ore into tools",
            resource = Res.Timber,
            cost = 28,
            footprint = new Vector2(6.53f, 5.85f),
            ridge = 4.29f,
            position = "smith",
            takes = Res.Ore,
            makes = Res.Tools,
            rate = 1.5f,
            prefab = "Settlement/blacksmith",
        };

        public static readonly BuildPlan Kitchen = new BuildPlan
        {
            id = "Kitchen",
            label = "kitchen",
            blurb = "a cook turns food into meals",
            resource = Res.Timber,
            cost = 18,
            footprint = new Vector2(6.26f, 6.12f),
            ridge = 4.18f,
            position = "cook",
            takes = Res.Food,
            makes = Res.Meals,
            rate = 3f,
            prefab = "Settlement/kitchen",
        };

        /// What sizes the HOME village clearing. Not the camp list: home is
        /// the one place with a hand-composed shot to fit buildings into.
        public static readonly BuildPlan[] All = { Storehouse };

        /// **What a camp can put up, in the order the Build menu offers it.**
        ///
        /// The fire is first and is not really one of them -- it is the thing
        /// that makes the rest possible -- but it goes through exactly the
        /// same blueprint, so keeping it in the list is what stops it becoming
        /// a special case.
        public static readonly BuildPlan[] AtACamp =
            { Campfire, Storage, Hut, Farm, Sawmill, Kitchen, Blacksmith };

        /// Look a plan up by the id a ledger row carries. A save restores ids,
        /// not structs, and so does an assignment.
        public static BuildPlan Named(string id)
        {
            foreach (var p in AtACamp) if (p.id == id) return p;
            foreach (var p in All) if (p.id == id) return p;
            return default;
        }

        /// The job at this building, or null if it has none. Used by the hand
        /// rows to say what somebody IS rather than where they are standing.
        public static string PositionAt(string planId) => Named(planId).position;

        /// Everything with a job somebody could be assigned to.
        public static bool HasPosition(string planId) =>
            !string.IsNullOrEmpty(PositionAt(planId));
    }
}
