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
        /// **A working yard rather than a room, 2026-09-22.** An open-fronted
        /// stone shed with a cut face beside it and squared brick stacked at
        /// the front. It is a `BuildKind` rather than a detail hung off the
        /// plan id because what a quarry looks like is not a hut with an
        /// ornament on it: three walls, no fourth, and a low roof. Sited,
        /// tested and footed exactly like a hut -- only the geometry differs.
        Quarry,
        /// **A bench, a butt and a bundle of shafts, 2026-09-22.** A small
        /// hut with a lean-to working bench at the open side and a target
        /// butt set out in front of it. Like the quarry it is a `BuildKind`
        /// rather than an ornament on a hut, because what says "fletcher" is
        /// the butt standing three metres off in the open -- a thing no
        /// other building in the camp has outside its own footprint.
        Fletcher,
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

        /// **The third part of a price, and nothing charges it yet,
        /// 2026-09-22.** Kevin asked for a quarry that makes "bricks for
        /// future building upgrades" -- so the brick has to be spendable
        /// before there is anything to spend it on, or the upgrade pass
        /// arrives and has to thread a whole new material through the
        /// blueprint, the site, the sheet and the save at the same time as
        /// it designs what an upgrade is.
        ///
        /// Zero on every plan below. `PendingBuild.brickNeeded` is therefore
        /// zero on every site, `OutpostLedger.PayBrick` sees no room and
        /// returns having touched nothing, and every reader that prints a
        /// price is gated on `> 0` -- so today this changes nothing at all,
        /// which is exactly the property that makes it safe to land now.
        ///
        /// **Paid from the pile only, never from the ground.** Timber can be
        /// cut and stone can be quarried where the blueprint stands; a brick
        /// cannot be found on an island at all. Somebody made it at a quarry
        /// and it is lying by the fire, or the building waits.
        public int baseBrickCost;
        /// What anything pays, under the same playtest cap as the other two.
        public int brickCost => BuildPlans.PlaytestCostCap > 0
            ? Mathf.Min(baseBrickCost, BuildPlans.PlaytestCostCap) : baseBrickCost;
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

        /// **Units of `makes` got out of ONE unit of `takes`, 2026-09-22.**
        ///
        /// Every building until the fletcher converted one for one, so the
        /// ledger's Work loop simply consumed as many inputs as it made
        /// outputs and nobody had to say so. A fletcher does not: Kevin asked
        /// for a bow-and-arrow building, and one log is plainly a fistful of
        /// arrows rather than one arrow. So the ratio moves out of the loop's
        /// assumption and onto the plan, where the rest of the conversion
        /// already lives.
        ///
        /// **Zero means one**, deliberately. `BuildPlan` is a struct, so
        /// every plan that does not mention this field gets 0 from the
        /// default initialiser and every SAVE that predates the field reads
        /// back the same -- and both have to keep meaning "one for one".
        /// Read it through `Yield`, never raw.
        public float yieldPerInput;
        /// `yieldPerInput`, with 0 and anything negative read as one for one.
        /// The sawmill, the forge, the kitchen and the quarry all come
        /// through here unchanged.
        public float Yield => yieldPerInput > 0f ? yieldPerInput : 1f;
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

        /// **A station's input bay, in UNITS per resource (2026-09-23).**
        /// Zero on a non-station plan; `StationStock.InputCap` reads 0 as
        /// its default. A two-input recipe gets this much of EACH.
        public int inputSlots;
        /// **A station's output rack, in units, all resources together.**
        public int outputSlots;

        /// Path under `Resources/` of the authored model, or null to extrude
        /// one. Loaded at raise time and quietly fallen back on, so a missing
        /// asset costs a plainer building and never a broken camp.
        public string prefab;

        /// **Which way the building faces, in its own local space.** The
        /// extruded and kit buildings keep their door in the -X gable end
        /// (the convention `Outpost.Raise` has always turned toward the
        /// camp); Astra's authored models have their open working front on
        /// +Z (input bay +X, output rack -X). Auto-siting turns THIS toward
        /// the clearing, and a blueprint marks it with a triangle (Kevin,
        /// 2026-09-24: "a triangle showing ... the face of the building").
        public Vector3 front;   // zero = the -X door gable (a struct cannot default it)
        public Vector3 Front => front.sqrMagnitude > 0.001f ? front.normalized : Vector3.left;

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
            // Astra's level-one campfire (art-staging/campfire-astra-lvl1-v2,
            // 2026-09-23); `campfire_02` (the earlier kit fallback) is no
            // longer worn but stays in Resources if this needs reverting.
            prefab = "Settlement/campfire_astra",
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
            // Astra's level-one storage hut (art-staging/storage-astra-lvl1-v1,
            // 2026-09-23); its validated bounds (6.03 x 4.34 x 3.57 m) sit
            // inside this footprint/ridge, so neither needed correcting.
            prefab = "Settlement/storage_astra",
            front = Vector3.forward,
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
            // Astra's level-one crew shelter (art-staging/shelter-astra-lvl1-v1,
            // 2026-09-23); its validated bounds (4.33 x 4.07 x 2.91 m) sit
            // inside this footprint/ridge.
            prefab = "Settlement/hut_astra",
            front = Vector3.forward,
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
            // Bay/rack capacity in units. Kevin confirmed 2026-09-23.
            inputSlots = 6,
            outputSlots = 12,
            takes = Res.Timber,
            makes = Res.Boards,
            // Informational only -- `Recipes.All` ("boards": 1 timber -> 3, 12 a
            // hand-day since 2026-09-24) is what the ledger runs. Kept in step.
            rate = 12f,
            prefab = "Settlement/sawmill",
            front = Vector3.forward,
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
            // **2026-09-23: 2.03 m, not the old 1.01 m.** Astra's kit
            // (art-staging/farm-astra-lvl1-v1) flagged its own tool canopy
            // as taller than the old ridge metadata
            // (`requires_height_metadata_review` in its validation.json,
            // measured bounds 0 to 2.026 m) and said explicitly not to
            // compress the art to fit the stale number. This is a metadata
            // correction to the asset's real height, not a scale -- the
            // footprint (4.29 x 4.47 m measured) already sat inside the
            // plot unchanged.
            ridge = 2.03f,
            position = "farmhand",
            makes = Res.Food,
            rate = OutpostLedger.FoodPerHandPerDay,
            beds = FarmBeds,
            unitsPerBed = FarmUnitsPerBed,
            bedRegrowPerDay = FarmRegrowPerDay,
            bedSpacing = FarmBedSpacing,
            prefab = "Settlement/farm_astra",
            front = Vector3.forward,
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
            // Bay/rack capacity in units, 2026-09-23: Astra's forge kit
            // (art-staging/forge-astra-lvl1-v1) has 5 Input_Ore and 4
            // Output_Tool display slots -- real capacities now, not the
            // sawmill's borrowed guess.
            inputSlots = 5,
            outputSlots = 4,
            takes = Res.Ore,
            makes = Res.Tools,
            rate = 1.5f,
            // Astra's level-one blacksmith (art-staging/forge-astra-lvl1-v1,
            // 2026-09-23); its validated bounds (5.90 x 3.31 x 4.46 m) sit
            // inside this footprint/ridge, so neither needed correcting.
            prefab = "Settlement/blacksmith_astra",
            front = Vector3.forward,
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
            // Bay/rack capacity in units, 2026-09-23: Astra's kitchen kit
            // (art-staging/kitchen-astra-lvl1-v1) has 4 Input_Food and 6
            // Output_Meal display slots -- real capacities now, not the
            // sawmill's borrowed guess.
            inputSlots = 4,
            outputSlots = 6,
            takes = Res.Food,
            makes = Res.Meals,
            rate = 3f,
            prefab = "Settlement/kitchen_astra",
            front = Vector3.forward,
        };

        /// **The fourth building that earns its place by preventing rather
        /// than producing, 2026-09-22.** Every plan above turns a hand into a
        /// pile; the watchtower turns a hand into a camp raiders leave alone.
        /// It costs timber and stone like the rest and makes nothing --
        /// `takes` and `makes` are both null, because the thing it spends is
        /// somebody's whole day standing watch, not a pile.
        ///
        /// The effect lives in `OutpostLedger.threat`, not here: manned, the
        /// watchtower halts threat outright; unmanned, it only halves the
        /// rate it would otherwise climb at; and a camp with no watchtower at
        /// all is the one a raid actually strikes. `OutpostLedger.WatchtowerId`
        /// reads this plan's `id` string, so it must stay exactly
        /// "Watchtower".
        ///
        /// Tall and narrow on purpose -- a lookout, not a room -- and the kit
        /// has no tower model to wear, so like the pier it stands extruded
        /// from `footprint` and `ridge` rather than an authored prefab.
        /// **A guess, never played.**
        public static readonly BuildPlan Watchtower = new BuildPlan
        {
            id = "Watchtower",
            kind = BuildKind.Hut,
            baseStoneCost = 6,
            label = "watchtower",
            blurb = "a lookout on watch keeps the raiders off the piles",
            resource = Res.Timber,
            baseCost = 12,
            footprint = new Vector2(2.6f, 2.6f),
            // **2026-09-23: 4.65 m, not the old 7.5 m.** Astra's V2 bare-
            // platform tower (art-staging/watchtower-astra-lvl1-v2 -- no
            // roof, no rail, no bracing, Kevin approved) measures 4.65 m
            // overall (validated bounds 2.275 x 4.651 x 2.404 m, footprint
            // fits the 2.6 x 2.6 plot). The old 7.5 m was the V1 study's
            // allowance, not this asset's real height -- do not stretch the
            // model to fill it.
            ridge = 4.65f,
            position = "lookout",
            // Astra's level-one watchtower (art-staging/watchtower-astra-lvl1-v2);
            // `BuildKind.Hut` (the extruded shed) stays as the fallback if it
            // fails to load.
            prefab = "Settlement/watchtower_astra",
            front = Vector3.forward,
        };

        /// **The quarry, 2026-09-22.** Kevin: *"we need a stone quarry
        /// building that takes rough stone and turns them into bricks for
        /// future building upgrades."*
        ///
        /// The sawmill's shape exactly, one material along: a quarryman takes
        /// Stone off the pile and puts Brick back, one for one, at two a day.
        /// Two rather than the sawmill's three because a stone is cut, not
        /// sawn -- and because the pile it eats from fills at 2.5 a day
        /// (`Res.GatherRate`), so one quarryman and one stone-gatherer very
        /// nearly balance and the pair of them is a legible unit of work.
        ///
        /// Twenty-two logs and six stone: a shade under the sawmill's
        /// twenty-four because there is less roof on it, and the most stone
        /// of any camp building except the watchtower, because a yard for
        /// cutting rock is mostly rock. **All guesses, none played.**
        /// (Capped at 5 for the playtest -- see `PlaytestCostCap`.)
        ///
        /// No kit model to wear, so like the watchtower and the pier it
        /// stands extruded -- as `BuildKind.Quarry`, which is a three-walled
        /// shed with a cut face and a brick stack rather than a hut.
        public static readonly BuildPlan Quarry = new BuildPlan
        {
            id = "Quarry",
            kind = BuildKind.Quarry,
            baseStoneCost = 6,
            label = "quarry",
            blurb = "a quarryman cuts rough stone into brick",
            resource = Res.Timber,
            baseCost = 22,
            footprint = new Vector2(7.4f, 5.8f),
            ridge = 2.9f,
            position = "quarryman",
            // Bay/rack capacity in units. Kevin confirmed 2026-09-23.
            inputSlots = 5,
            outputSlots = 12,
            takes = Res.Stone,
            makes = Res.Brick,
            rate = 2f,
            // Astra's level-one stonecutting yard (art-staging/quarry-astra-lvl1-v2);
            // `BuildKind.Quarry` (the extruded shed) stays as the fallback if it fails to load.
            prefab = "Settlement/quarry",
            front = Vector3.forward,
        };

        /// **The fletcher's, 2026-09-22.** Kevin: *"build a fletcher's
        /// building as well for bow and arrow."*
        ///
        /// The first building whose output is SPENT rather than stacked. A
        /// fletcher takes timber and makes arrows at three a day out of one
        /// log a day (`yieldPerInput = 3`), and what the arrows buy is in
        /// `OutpostLedger.Step`: a hunter with a quiver kills half again as
        /// much and spends an arrow an animal, and a posted lookout looses a
        /// volley of up to five that cuts what a raid carries off.
        ///
        /// **Cheap and small on purpose.** Eighteen logs and two stone, the
        /// hut's own footprint: it is not a mill or a forge, it is a bench
        /// under a roof, and it has to be affordable at the point in a camp
        /// where the herd is thinning and there are raiders offshore --
        /// which is exactly when a player would want one and would have
        /// spent everything else. **All guesses, none played.**
        /// (Capped at 5 for the playtest -- see `PlaytestCostCap`.)
        public static readonly BuildPlan Fletcher = new BuildPlan
        {
            id = "Fletcher",
            kind = BuildKind.Fletcher,
            baseStoneCost = 2,
            label = "fletcher's",
            blurb = "a fletcher makes arrows from timber; the hunt and the watch both want them",
            resource = Res.Timber,
            baseCost = 18,
            footprint = new Vector2(4.84f, 4.93f),
            ridge = 3.2f,
            position = "fletcher",
            // Bay/rack capacity in units, 2026-09-23: Astra's fletcher kit
            // (art-staging/fletcher-astra-lvl1-v1) has 5 Input_Timber and 4
            // Output_Arrows display slots -- real capacities now, not the
            // sawmill's borrowed guess.
            inputSlots = 5,
            outputSlots = 4,
            takes = Res.Timber,
            makes = Res.Arrows,
            rate = 3f,
            yieldPerInput = 3f,
            // Astra's level-one fletcher (art-staging/fletcher-astra-lvl1-v1,
            // 2026-09-23); its validated bounds (4.13 x 2.97 x 4.02 m) sit
            // inside this footprint/ridge, so neither needed correcting.
            // `BuildKind.Fletcher` (the extruded bench+butt) already runs
            // as `Raise`'s fallback only when `Dress` fails to load a
            // prefab -- see `BuildingFactory.Raise`, which tries `Dress`
            // before any `plan.kind` branch -- so naming a prefab here is
            // enough for the authored kit to win with no factory change.
            prefab = "Settlement/fletcher_astra",
            front = Vector3.forward,
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

        // --- the fortification (Phase 1, 2026-09-23) -------------------------

        /// **A run of sharpened logs between two posts.**
        ///
        /// Kevin: *"I see in my mind walls, watch towers..."* and then D1:
        /// *"1 log / 2 m of palisade, hauled like any site."* So the price
        /// is not on the plan -- it is on the SEGMENT, because a segment is
        /// as long as the player dragged it. `PalisadeCost` is the only
        /// thing that prices one; `baseCost` here is zero and nothing reads
        /// it, which is deliberate: a wall row whose `needed` came from the
        /// plan instead of from its own length would be a wall you could
        /// make cheaper by drawing it longer.
        ///
        /// No stone. Stone wall is a later tier (D1), and a palisade is
        /// logs in the ground.
        ///
        /// **Not in `AtACamp`.** Everything in that list is offered by the
        /// build page and sited by tapping ONE point; a wall is two points
        /// and its own tool (D5, the connect-the-dots run). It is found by
        /// `Named` through `Fortifications` instead, which is all a saved
        /// ledger row needs.
        public static readonly BuildPlan Palisade = new BuildPlan
        {
            id = "palisade",
            kind = BuildKind.Hut,
            label = "palisade",
            blurb = "a run of sharpened logs — raiders break it or use the gate",
            resource = Res.Timber,
            baseCost = 0,
            baseStoneCost = 0,
            // Post-to-post, so the length is the segment's and this is only
            // how WIDE a wall is: two metres of footing either side of the
            // line is what `Outpost.CanPlaceWall` keeps clear of a hut.
            footprint = new Vector2(2f, 1.2f),
            ridge = PalisadeHeight,
        };

        /// **A way through your own wall.** D3: automatic -- open to the
        /// camp's people, shut to a raiding party, nothing to toggle. Four
        /// logs, flat, because a gate is a fixed thing whatever the segment
        /// it replaces was: two taller posts and a lintel.
        public static readonly BuildPlan Gate = new BuildPlan
        {
            id = "gate",
            kind = BuildKind.Hut,
            label = "gate",
            blurb = "your people walk through it; raiders do not",
            resource = Res.Timber,
            baseCost = 4,
            baseStoneCost = 0,
            footprint = new Vector2(2f, 1.2f),
            ridge = GateHeight,
        };

        /// Metres of palisade per log (D1: 1 log / 2 m).
        public const float MetresPerPalisadeLog = 2f;

        /// How tall a palisade stands, world metres. Taller than a man
        /// (`WorldScale`'s crew are ~1.8 m) and short enough that a camp
        /// behind one is still a camp you can see into from the deck.
        public const float PalisadeHeight = 2.6f;
        /// A gate's posts, which stand proud of the wall so a run reads as
        /// having a door in it from the water.
        public const float GateHeight = 3.6f;

        /// **What a segment of this length costs in timber.** Ceil, so a
        /// three-metre stub still costs two logs and nothing is ever free.
        /// Through the playtest cap like every other price, so a wall
        /// cannot escape an experiment the buildings are inside.
        public static int PalisadeCost(float metres)
        {
            int logs = Mathf.CeilToInt(Mathf.Max(0f, metres) / MetresPerPalisadeLog);
            return PlaytestCostCap > 0 ? Mathf.Min(logs, PlaytestCostCap) : logs;
        }

        /// The two plans that go on a wall line rather than on a plot.
        public static readonly BuildPlan[] Fortifications = { Palisade, Gate };

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
            { Campfire, Storage, Hut, Farm, Sawmill, Quarry, Fletcher, Kitchen, Blacksmith, Watchtower, Pier };

        /// Look a plan up by the id a ledger row carries. A save restores ids,
        /// not structs, and so does an assignment.
        public static BuildPlan Named(string id)
        {
            foreach (var p in AtACamp) if (p.id == id) return p;
            foreach (var p in Fortifications) if (p.id == id) return p;
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
