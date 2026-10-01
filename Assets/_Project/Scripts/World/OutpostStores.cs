using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// One resource lying on the ground at an outpost.
    ///
    /// **A list, not a dictionary, and the reason is the save.** `JsonUtility`
    /// cannot serialise a dictionary, and this whole object exists to be
    /// savable before there is a writer for it. A camp keeps four or five
    /// kinds of thing at the most, so a linear scan is cheaper than the hash
    /// anyway.
    ///
    /// `part` is the sub-unit accrual, kept for the same reason the timber one
    /// always was: without it, ticking often would produce less than ticking
    /// rarely, and the two have to agree.
    [System.Serializable]
    public class OutpostStore
    {
        public string resource;
        public int whole;
        public float part;
    }

    /// What is left to take out of the ground here, per resource.
    ///
    /// Timber regrows; a seam of ore does not, or not on any scale a voyage
    /// would notice. `regrowPerDay` is what separates them, and it is a
    /// property of the RESOURCE at this place rather than a global constant —
    /// an island's stock is a fact about that island.
    [System.Serializable]
    public class OutpostStock
    {
        public string resource;
        public float standing;
        public float standingMax;
        public float regrowPerDay;
    }

    /// The resource vocabulary, in one place.
    ///
    /// **These are the names the populator already uses.** `WorldSettings`
    /// places one kind per island as props with `ResourceNode`s on them —
    /// Timber, Stone, Ore, Spice, unlocked further out — and the trees are
    /// Timber wherever they stand. Nothing here invents a resource that the
    /// world does not already put on the ground; what IS new is the far side
    /// of a building, where timber becomes boards and ore becomes tools.
    public static class Res
    {
        public const string Timber = "Timber";
        public const string Stone = "Stone";
        public const string Ore = "Ore";
        public const string Spice = "Spice";

        /// **Counted in ANIMALS, not in meat.** A hand told to gather Game is
        /// a hunter, and what he brings back lands in the Food pile at
        /// `MeatPerAnimal` apiece -- so this is the only gatherable whose
        /// stock and whose store are different things. The herd on the
        /// ground is the authority, exactly as the trees are for Timber.
        public const string Game = "Game";

        /// Food per animal. **A guess** -- a goat feeds the camp for about as
        /// long as four beds of wheat, which is what makes half a day's stalk
        /// worth walking out for. **Live**: FEEL's `EconomyFeel.meatPerAnimal`
        /// (seeded from the tuning asset), 2026-09-27.
        public static float MeatPerAnimal => Mathf.Max(0f, SeaSick.World.Economy.EconomyFeel.meatPerAnimal);

        /// Made, not found. A camp with nobody assigned never sees these.
        public const string Boards = "Boards";
        public const string Tools = "Tools";
        /// **Rough stone, cut square, 2026-09-22.** Kevin: *"a stone quarry
        /// building that takes rough stone and turns them into bricks for
        /// future building upgrades."* The far side of a quarryman exactly as
        /// Boards is the far side of a sawyer -- gathered stone goes in, a
        /// squared brick comes out, and nothing builds out of them yet. That
        /// is on purpose: `BuildPlan.brickCost` exists and is zero everywhere,
        /// so the good is in the world and priced at nothing until an upgrade
        /// asks for it.
        public const string Brick = "Brick";

        /// **Made out of timber and spent by the people who use them,
        /// 2026-09-22.** Kevin: *"build a fletcher's building as well for bow
        /// and arrow."* The first made good that is CONSUMED rather than
        /// carried home: a hunter spends one per animal and shoots half again
        /// as well for it (`OutpostLedger.Step`), and a posted lookout spends
        /// up to five in a volley that cuts what a raid takes
        /// (`OutpostLedger.LookoutVolley`). So a full quiver is not a number
        /// going up -- it is two other numbers that get better while it
        /// lasts.
        public const string Arrows = "Arrows";
        /// **Wild forage since the food rework (2026-09-27)**: berries and
        /// roots off the island itself, what a hand told to gather food
        /// brings in, eaten raw at a quarter. The farm, the fishing hut and
        /// the hunt no longer land here -- they make crops, fish and meat
        /// (`Economy.FoodBook`). An old save's Food pile is turned into potatoes
        /// once on load (`OutpostLedger.MigrateFood`). For "how much food
        /// does this camp have" read `OutpostLedger.FoodFill`, never this.
        public const string Food = "Food";
        /// **Ship's biscuit since the food rework**: hard bread for the hold,
        /// baked at the kitchen from flour. Kept under the old key so every
        /// old Meals pile and ship-hold row is biscuit now.
        public const string Meals = "Meals";

        // --- the food rework, 2026-09-27 (docs/GDD.md "Food") -----------------
        // Crops grow on farm plots, fish come off the fishing hut, meat off
        // the hunt; the kitchen cooks them into dishes; villagers eat the
        // best dish in store. Fill/buff values live in `Economy.FoodBook`.
        public const string Potato = "Potato";
        public const string Carrot = "Carrot";
        public const string Onion = "Onion";
        public const string Wheat = "Wheat";
        public const string Apple = "Apple";
        public const string Fish = "Fish";
        public const string Meat = "Meat";
        /// Wheat ground at the mill (fire II).
        public const string Flour = "Flour";
        public const string BakedPotato = "BakedPotato";
        public const string GrilledFish = "GrilledFish";
        public const string GrilledMeat = "GrilledMeat";
        public const string RoastCarrots = "RoastCarrots";
        public const string Bread = "Bread";
        public const string VegStew = "VegStew";
        public const string FishPie = "FishPie";
        public const string HuntersStew = "HuntersStew";

        // --- the chains, 2026-09-23 -----------------------------------------
        //
        // Kevin: *"there should be steps to things ... to hunt you need a
        // spear so the blacksmith needs a wood plank for the shaft and stone
        // or metal for the tip."* Every key below is registered in
        // `Economy/ResDefs` with a TIER (raw, treated, item) and made by a
        // `Recipe`; `RecipeGraph.Validate` refuses a build in which any of
        // them cannot be reached from a campfire and the ground. Add a key
        // here, add its def there, or the probe names it.

        /// **Dropped, not gathered.** One per animal a hunter brings home,
        /// beside the meat. The first thing the fire wants when it is raised
        /// to level II, so a camp that never hunts never grows.
        public const string Hide = "Hide";
        /// Ore smelted at the forge. Two ore to a bar.
        public const string Iron = "Iron";
        /// A board and a stone tip. **What a hunter needs in his hand**, and
        /// what he wears out: a spear lasts about four animals.
        public const string Spear = "Spear";
        /// A board and an iron tip. Lasts three times as long as the stone
        /// one; the hunter takes it first if the pile has both.
        public const string IronSpear = "IronSpear";
        /// Iron, ground to an edge at the forge. The tool the sawmill needs
        /// before it can cut Fine Boards, and worn a little by every one.
        public const string SawBlade = "SawBlade";
        /// Boards cut true on a steel-edged saw. What a building's second
        /// level is framed with.
        public const string FineBoards = "FineBoards";
        /// **A fine board bent and strung with hide, 2026-09-30.** Kevin:
        /// *"The Fletcher makes Bows from fine boards + hide, in addition to
        /// arrows"* -- used for defence, hunting and by sailors from the
        /// ship. **A bow shoots arrows and nothing else**: every shot spends
        /// one `Arrows`, and a bow with no arrows cannot shoot. Worn like a
        /// spear (`Economy.Techs.BowWear`, animals or raiders per bow).
        public const string Bow = "Bow";

        /// What a hand can be told to go and GATHER — the things that are
        /// lying about on an island. The rest are made at a building by
        /// somebody assigned to it.
        public static readonly string[] Gatherable =
            { Timber, Stone, Ore, Spice, Food, Game };

        /// **Never a hunt drop** (2026-09-27, Kevin: hide is fine as the
        /// only road to fire II "as long as the hide can't be gathered"): a
        /// resource whose `ResDefs` source is `Drop` is refused here even if
        /// somebody adds it to `Gatherable`, so no Gather order, Hand drop,
        /// gather party or site trip can ever target it; `RecipeGraph.Validate`
        /// names the mistake.
        public static bool IsGatherable(string r)
        {
            if (Economy.ResDefs.TryGet(r, out var d) && d.source == Economy.ResSource.Drop) return false;
            foreach (var g in Gatherable) if (g == r) return true;
            return false;
        }

        /// How fast one hand takes it out of the ground, units a day.
        /// **Every one of these is a guess and none has been played.** Timber
        /// is the original 4 and the others are set against it: stone and ore
        /// are slower because a prop is four strikes where a tree is three,
        /// and spice is quick to pick and rare to find.
        public static float GatherRate(string r) => r switch
        {
            Timber => 4f,
            Stone => 2.5f,
            Ore => 2f,
            Spice => 3f,
            Food => 3f,          // a bed of wheat is a morning's work
            Game => 0.5f,        // ANIMALS a day: half a day's stalk per kill
            _ => 2f,
        };

        /// Units standing per hectare of worked ground, and what comes back in
        /// a day as a share of the maximum. **Guesses.** Timber keeps its
        /// original 40/ha and 2%/day; minerals are thinner on the ground and
        /// do not come back at all, which is what makes a mining island a
        /// thing you use up rather than a thing you farm.
        public static float PerHectare(string r) => r switch
        {
            Timber => 40f,
            Stone => 14f,
            Ore => 9f,
            Spice => 7f,
            Food => 12f,         // seed only: where the beds can be SEEN they set the ceiling
            Game => 0f,          // never seeded by the hectare: the herd IS the stock
            _ => 10f,
        };

        public static float RegrowPerDay(string r) => r switch
        {
            Timber => 0.02f,
            Spice => 0.01f,      // it grows; slowly
            Food => 0.05f,       // wheat stands again in twenty days
            Game => 0.01f,       // a herd breeds back, slowly
            _ => 0f,             // rock does not
        };

        /// **Units one hand carries in one haul trip (2026-09-23).** Logs are
        /// heavy and long, so fewer of them than bricks; small made things go
        /// by the bundle. **Every one is a guess, none played.** The trip
        /// itself takes its walked distance there and back plus the handling
        /// (`OutpostLedger.TripDays`), so units a day = armful / that.
        public static int Armful(string r) => r switch
        {
            Timber => 2,
            Stone => 3,
            Ore => 3,
            // **Small goods ride in the open carry crate, 8 slots**
            // (2026-10-01, Kevin: "carry these things (up to 8 at a time) in
            // an open lid crate"): every crop, fish, meat, forage, spice,
            // flour and dish is an armful of `CrateArmful`. Was 3-6.
            Spice => CrateArmful,
            Food => CrateArmful,
            Hide => 3,
            Boards => 4,
            FineBoards => 4,
            Brick => 4,
            Iron => 3,
            Tools => 2,
            SawBlade => 1,
            Spear => 2,
            IronSpear => 2,
            Bow => 2,
            Arrows => 12,
            Meals => CrateArmful,
            Potato => CrateArmful, Carrot => CrateArmful, Onion => CrateArmful, Wheat => CrateArmful, Apple => CrateArmful,
            Fish => FishArmful, Meat => CrateArmful, Flour => CrateArmful,
            BakedPotato => CrateArmful, GrilledFish => CrateArmful, GrilledMeat => CrateArmful,
            RoastCarrots => CrateArmful, Bread => CrateArmful,
            VegStew => CrateArmful, FishPie => CrateArmful, HuntersStew => CrateArmful,
            _ => 3,
        };

        /// One carry crate's worth of small goods (`CarryLook.CrateSlots`).
        public const int CrateArmful = 8;

        /// **Fish go all at once (Kevin, 2026-09-30):** *"For the fish in
        /// particular he will carry all the fish at once since they aren't
        /// that heavy compared to logs."* A full fishing-hut box is ONE trip
        /// to the store, the fisher's or an idle hand's (both go through
        /// `OutpostLedger.RackChore`, capped by this) -- so the armful is
        /// never below the hut's output capacity, whatever that is tuned to.
        public static int FishArmful => System.Math.Max(8, BuildPlans.FishingHut.outputSlots);

        /// Colour of a pile of it, for the stacks by the fire.
        public static Color Colour(string r) => r switch
        {
            Timber => new Color(0.43f, 0.30f, 0.18f),
            Stone => new Color(0.55f, 0.55f, 0.53f),
            Ore => new Color(0.48f, 0.40f, 0.26f),
            Spice => new Color(0.75f, 0.35f, 0.55f),
            Boards => new Color(0.66f, 0.50f, 0.30f),
            // Fired clay gone grey with the dust of the yard it was cut in:
            // warm enough not to be read as another pile of rough stone,
            // grey enough to be read as masonry rather than as food.
            Brick => new Color(0.62f, 0.42f, 0.35f),
            // Pale ash shafts and paler fletching: the one made good that is
            // lighter than anything the ground gives up, so a bundle of them
            // reads against the timber and the stone either side of it.
            Arrows => new Color(0.84f, 0.80f, 0.68f),
            Tools => new Color(0.40f, 0.44f, 0.50f),
            Food => new Color(0.55f, 0.62f, 0.28f),
            Game => new Color(0.42f, 0.20f, 0.16f),
            Meals => new Color(0.72f, 0.58f, 0.34f),
            Hide => new Color(0.60f, 0.44f, 0.30f),
            Iron => new Color(0.36f, 0.36f, 0.40f),
            Spear => new Color(0.70f, 0.58f, 0.40f),
            IronSpear => new Color(0.56f, 0.52f, 0.46f),
            // Fine-board stave, darker than the arrows it shoots so a bow
            // and a quiver lying side by side do not read as one pile.
            Bow => new Color(0.60f, 0.42f, 0.24f),
            SawBlade => new Color(0.62f, 0.64f, 0.68f),
            FineBoards => new Color(0.78f, 0.62f, 0.38f),
            Potato => new Color(0.70f, 0.56f, 0.36f),
            Carrot => new Color(0.90f, 0.50f, 0.18f),
            Onion => new Color(0.85f, 0.78f, 0.62f),
            Wheat => new Color(0.86f, 0.74f, 0.40f),
            Apple => new Color(0.78f, 0.22f, 0.20f),
            Fish => new Color(0.55f, 0.65f, 0.72f),
            Meat => new Color(0.66f, 0.28f, 0.26f),
            Flour => new Color(0.94f, 0.92f, 0.86f),
            BakedPotato => new Color(0.62f, 0.44f, 0.24f),
            GrilledFish => new Color(0.66f, 0.54f, 0.40f),
            GrilledMeat => new Color(0.55f, 0.30f, 0.20f),
            RoastCarrots => new Color(0.82f, 0.42f, 0.16f),
            Bread => new Color(0.80f, 0.62f, 0.34f),
            VegStew => new Color(0.60f, 0.42f, 0.22f),
            FishPie => new Color(0.84f, 0.66f, 0.38f),
            HuntersStew => new Color(0.52f, 0.30f, 0.20f),
            _ => new Color(0.5f, 0.5f, 0.5f),
        };
    }
}
