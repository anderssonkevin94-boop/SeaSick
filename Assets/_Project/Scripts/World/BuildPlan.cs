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
        /// Planks on posts, out from the beach into water deep enough to
        /// lie alongside. Half of it stands over the sea, so it is sited,
        /// tested and stood up by its own rules -- see `Outpost.SnapPier`.
        Pier,
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
        /// What the plan was PRICED at -- the number the rationale comments
        /// below argue for. Nothing pays this directly; see `cost`.
        public int baseCost;
        /// **Logs it costs to raise, and the only number anything pays.**
        /// `BuildPlans.PlaytestCostCap` sits between this and `baseCost`, so
        /// while the cap is on every reader -- the ledger's blueprint, the
        /// Build menu, the probes -- sees the capped price, and none of them
        /// can reach the raw one by accident.
        public int cost => BuildPlans.PlaytestCostCap > 0
            ? Mathf.Min(baseCost, BuildPlans.PlaytestCostCap) : baseCost;

        /// **Stone it costs to raise, as priced.** Kevin, 2026-09-21: *"the
        /// buildings require wood and stone ... all buildings require at
        /// least wood and stone."* Every plan but the campfire wants both,
        /// and the campfire is the exception on purpose: it is the FIRST
        /// thing you build, before there is a pile, a quarry or anybody to
        /// work one, so pricing it in stone would price the loop's own door.
        ///
        /// `resource` above still names the timber part; stone is the second
        /// part and does not need naming, because there are exactly two and
        /// every reader knows which is which.
        public int baseStoneCost;
        /// What anything actually pays, under the same playtest cap `cost` is
        /// under. Every stone price below is already inside it, so today this
        /// is `baseStoneCost` -- but it goes through the cap so that a raised
        /// stone price can never escape an experiment the timber price is in.
        public int stoneCost => BuildPlans.PlaytestCostCap > 0
            ? Mathf.Min(baseStoneCost, BuildPlans.PlaytestCostCap) : baseStoneCost;
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

        /// **Beds: how many hands this plan houses, 2026-09-21.** Zero on
        /// everything that is not a hut -- the campfire houses nobody, and
        /// so does the storehouse at home -- so `OutpostLedger.HousingCapacity`
        /// can sum this over every plan raised without a special case for
        /// what a hut is. Placeholder number, balance later.
        public int houses;

        // --- the farm's field, 2026-09-21 ------------------------------------
        //
        // **Wheat on the island is gathered by hand; a farm is wheat that
        // grows by the camp.** A farm is the one building whose input is not
        // a pile but a FIELD, and the field is described here so that the
        // ledger (which bounds the farmhand by it), the raise hook (which
        // plants it) and the scenery (which draws it) all read one set of
        // numbers. Zero on every plan that is not a farm.

        /// Beds of wheat this building plants when it is raised. The kit's
        /// farms have 4/6/8 crop modules of their own (`farm_01/02/03`) and
        /// `BuildingFactory` names them `Bed_00..`; the coordinator plants
        /// gatherable beds through `SceneryCrops.Plant` with this count.
        public int beds;
        /// Units of Food one bed holds standing. `beds * unitsPerBed` is what
        /// raising the farm adds to the camp's standing Food
        /// (`OutpostLedger.AddStanding`).
        public int unitsPerBed;
        /// Share of the field that grows back in a day, the way
        /// `OutpostStock.regrowPerDay` is. Linear on `standingMax`, so a
        /// field of 24 at 0.25 comes back 6 a day -- one farmhand's harvest.
        public float bedRegrowPerDay;
        /// Metres between planted beds, for beds laid out beside the building
        /// in two rows rather than in the kit's own slots.
        public float bedSpacing;
        /// What the whole field holds when it is raised, in units of Food.
        public float FieldStanding => beds * unitsPerBed;

        /// Path under `Resources/` of the authored model, or null to extrude
        /// one. Loaded at raise time and quietly fallen back on, so a missing
        /// asset costs a plainer building and never a broken camp.
        public string prefab;

        /// **This plan, at a different length along the ridge.** A pier is
        /// as long as the beach makes it (14 m, or up to 24 m out to water
        /// that will float a hull), and everything downstream -- the ghost,
        /// the stakes, the corner test, the planks -- reads `footprint.x`.
        /// So the chosen length is carried IN the plan rather than beside
        /// it, and none of those readers has to know a pier is different.
        public BuildPlan WithLength(float length)
        {
            var copy = this;
            if (length > 0f) copy.footprint.x = length;
            return copy;
        }
    }

    /// Everything that can be built, in the order it is offered.
    public static class BuildPlans
    {
        /// **TEMP for playtesting, 2026-09-21.** Kevin: *"set the limit at 5
        /// for each building (temporarily)."* While this is above zero every
        /// plan's `cost` is `min(baseCost, PlaytestCostCap)`; the priced
        /// numbers below stay in source untouched and come straight back when
        /// this is set to **0 = off**. The fire is priced under the cap (4),
        /// so `LedgerProbe`'s one-hand-one-day gate is unaffected either way.
        public const int PlaytestCostCap = 5;

        /// **The first building in the game.**
        ///
        /// Cost is set against the hold, not against a spreadsheet: she
        /// carries 24 to the marked line and 38 stuffed with deck cargo, so
        /// a storehouse is one full hold and a log over. **Two voyages,
        /// never one** -- and the second one has to come home, which is the
        /// decision the whole loop is made of. (**Capped at 5 for the
        /// playtest** -- see `PlaytestCostCap`; the 25 is what it goes back to.)
        public static readonly BuildPlan Storehouse = new BuildPlan
        {
            id = "Storehouse",
            baseStoneCost = 4,
            label = "storehouse",
            blurb = "keeps 40 more out of the weather",
            resource = Res.Timber,
            baseCost = 25,
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
            // **No stone.** The one plan that stays timber-only: see
            // `BuildPlan.baseStoneCost`.
            baseStoneCost = 0,
            kind = BuildKind.Fire,
            label = "make camp",
            blurb = "a fire, and somewhere to keep ten of anything",
            resource = Res.Timber,
            baseCost = 4,
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
        // **And, for the playtest, none of them charged**: `PlaytestCostCap`
        // holds every one of these at five logs until it is switched off. The
        // numbers below are the design; the cap is the experiment.
        //
        // The footprints and ridges are NOT guesses. They are the measured
        // game bounds out of the kit's own import validation, so the four
        // corners the ground is tested at are the corners the building
        // actually stands on and the blueprint is the size of the thing.

        public static readonly BuildPlan Storage = new BuildPlan
        {
            id = "Storage",
            baseStoneCost = 3,
            label = "store hut",
            blurb = "keeps 20 more of each thing",
            resource = Res.Timber,
            baseCost = 20,
            storeCapacity = 20,
            footprint = new Vector2(6.46f, 5.14f),
            ridge = 3.84f,
            prefab = "Settlement/storage",
        };

        public static readonly BuildPlan Hut = new BuildPlan
        {
            id = "Hut",
            baseStoneCost = 2,
            label = "shelter",
            blurb = "somewhere for four hands to live",
            resource = Res.Timber,
            baseCost = 12,
            footprint = new Vector2(4.84f, 4.93f),
            ridge = 3.81f,
            supports = 4,
            houses = 2,
            prefab = "Settlement/hut_01",
        };

        public static readonly BuildPlan Sawmill = new BuildPlan
        {
            id = "Sawmill",
            baseStoneCost = 3,
            label = "sawmill",
            blurb = "a sawyer turns timber into boards",
            resource = Res.Timber,
            baseCost = 24,
            footprint = new Vector2(7.56f, 5.85f),
            ridge = 3.84f,
            position = "sawyer",
            takes = Res.Timber,
            makes = Res.Boards,
            rate = 3f,
            prefab = "Settlement/sawmill",
        };

        /// **The farm, 2026-09-21.** GDD 6: *"food is local ... wheat feeds
        /// a settlement."* A farmhand takes nothing from the piles: the field
        /// is the input. It is the same shape as the sawmill -- `position`,
        /// `makes`, `rate`, one Work order with `target = "Farm"` -- with the
        /// field described by `beds`, `unitsPerBed` and `bedRegrowPerDay`.
        ///
        /// **The numbers.** Six beds of four is 24 Food standing when it is
        /// raised; a farmhand harvests `OutpostLedger.FoodPerHandPerDay` (6)
        /// a day, and the field grows back a quarter of itself a day, which
        /// is also 6. So one farm keeps exactly one farmhand busy, and a
        /// second farmhand on the same farm strips it in four days and then
        /// shares the regrowth. Into the camp stores, under the same ceiling
        /// as everything else. **All guesses, none played.**
        ///
        /// `farm_01` wears four crop modules; `farm_02` is the six-bed kit
        /// model (6.24 x 4.69 m) and is the one to switch to once it is in
        /// `Resources/Settlement`. The plan says six either way, because the
        /// field is planted from the plan, not counted off the model.
        public static readonly BuildPlan Farm = new BuildPlan
        {
            id = "Farm",
            baseStoneCost = 2,
            label = "farm plot",
            blurb = "a farmhand grows food out of the ground",
            resource = Res.Timber,
            baseCost = 16,
            footprint = new Vector2(4.66f, 4.69f),
            ridge = 1.01f,
            position = "farmhand",
            makes = Res.Food,
            rate = OutpostLedger.FoodPerHandPerDay,
            beds = FarmBeds,
            unitsPerBed = FarmUnitsPerBed,
            bedRegrowPerDay = FarmRegrowPerDay,
            bedSpacing = FarmBedSpacing,
            prefab = "Settlement/farm_01",
        };

        /// Beds a farm plants. Six: the kit's `farm_02` count, and a field
        /// that one hand can keep up with.
        public const int FarmBeds = 6;
        /// Food standing in one bed when it is full.
        public const int FarmUnitsPerBed = 4;
        /// Share of the field that grows back in a day. A quarter: 24 * 0.25
        /// is 6, one farmhand's day.
        public const float FarmRegrowPerDay = 0.25f;
        /// Metres between beds laid out beside the building, two rows of
        /// three. The kit's own slots are 1.8 m apart across the aisle.
        public const float FarmBedSpacing = 1.8f;

        public static readonly BuildPlan Blacksmith = new BuildPlan
        {
            id = "Blacksmith",
            baseStoneCost = 4,
            label = "forge",
            blurb = "a smith turns ore into tools",
            resource = Res.Timber,
            baseCost = 28,
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
            baseStoneCost = 3,
            label = "kitchen",
            blurb = "a cook turns food into meals",
            resource = Res.Timber,
            baseCost = 18,
            footprint = new Vector2(6.26f, 6.12f),
            ridge = 4.18f,
            position = "cook",
            takes = Res.Food,
            makes = Res.Meals,
            rate = 3f,
            prefab = "Settlement/kitchen",
        };

        /// **A pier, 2026-09-21.** Kevin: *"I'd like a pier asset to be
        /// buildable to make it easier to dock with the island."*
        ///
        /// Nobody works at it and it keeps nothing: what it buys is a berth.
        /// Three metres wide and fourteen long as priced, but the beach has
        /// the last word on the length -- `Outpost.SnapPier` runs it out as
        /// far as 24 m to reach 2.5 m of water, and refuses a beach that
        /// never gets there. Eight logs, two days of one man: the planks
        /// are cheap, the posts are what cost. **A guess, never played.**
        /// (Capped at 5 for the playtest -- see `PlaytestCostCap`.)
        public static readonly BuildPlan Pier = new BuildPlan
        {
            id = "Pier",
            baseStoneCost = 2,
            kind = BuildKind.Pier,
            label = "pier",
            blurb = "planks out to deep water, so she can lie alongside",
            resource = Res.Timber,
            baseCost = 8,
            footprint = new Vector2(PierLength, PierWidth),
            ridge = PierDeck,
        };

        /// Metres of pier as priced; the beach may ask for more.
        public const float PierLength = 14f;
        /// The furthest the planks will run out looking for water.
        public const float PierLongest = 24f;
        public const float PierWidth = 3f;
        /// Deck height above MEAN WATER, world metres -- not above the
        /// ground, since the ground under a pier runs from beach to sea bed.
        public const float PierDeck = 1.2f;
        /// Water under the sea end, metres. Enough for any hull on the ladder
        /// to lie alongside without touching.
        public const float PierBerthDepth = 2.5f;

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
            { Campfire, Storage, Hut, Farm, Sawmill, Kitchen, Blacksmith, Pier };

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
