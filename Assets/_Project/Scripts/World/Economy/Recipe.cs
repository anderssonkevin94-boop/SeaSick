using System.Collections.Generic;

namespace SeaSick.World.Economy
{
    /// One thing a station can make, and what it takes to make it.
    ///
    /// A station (a `BuildPlan` with a `position`) holds several of these
    /// and the player picks one on the station's sheet; the ledger's Work
    /// loop then spends `takes` and produces `makes` at `ratePerDay` per
    /// hand, exactly as the old one-input `plan.takes/makes` did. A station
    /// with no recipe in this table keeps its old plan fields, so the farm
    /// and the watchtower are untouched.
    ///
    /// **Rates are per OUTPUT unit.** `takes` is priced per `yield` outputs,
    /// so "2 ore makes 1 iron" is `takes = {2 ore}, yield = 1`, and "1 timber
    /// makes 3 arrows" is `takes = {1 timber}, yield = 3`.
    public class Recipe
    {
        public string id;
        /// `BuildPlan.id` of the station this is made at.
        public string station;
        public string makes;
        /// Outputs per batch; `takes` is the price of one batch.
        public int yield = 1;
        public Ingredient[] takes = Cost.None;
        /// Output units one hand makes in a day as written (code default, or
        /// the tuning asset's row). **Every one is a guess.**
        public float baseRatePerDay;
        /// What the ledger runs: `baseRatePerDay` times FEEL's
        /// `stationSpeedMultiplier`.
        public float ratePerDay { get => baseRatePerDay * EconomyFeel.StationSpeed; set => baseRatePerDay = value; }
        /// Fire level needed before the recipe is offered.
        public int campfireLevel = 1;
        /// Station level needed (1 = as raised).
        public int stationLevel = 1;
        /// An item that must be in the pile for the work to go on, and is
        /// worn by it: `toolWear` of one is used up per output unit. Null
        /// for no tool. The sawmill needs a saw blade for fine boards; the
        /// quarry wears tools cutting brick.
        public string tool;
        public float toolWear;

        public string label => ResDefs.Label(makes);

        /// "2 ore → 1 iron" for the sheet.
        public string Describe() => $"{Cost.Describe(takes)} → {yield} {label}";
    }

    /// **Every recipe in the game.** Ordered by station, then by the level
    /// they unlock at, which is the order a station sheet lists them in.
    public static class Recipes
    {
        /// Output units a hand-day for a batch of `yield` that takes
        /// `seconds` of REAL time at the 180 s game day the design table was
        /// priced against (food rework, 2026-09-27).
        public const float PricedDaySeconds = SeaSick.World.TimeOfDay.WorkDaySeconds;
        public static float Cook(float seconds, int yield) =>
            yield * PricedDaySeconds / UnityEngine.Mathf.Max(0.1f, seconds);

        public static readonly Recipe[] All =
        {
            // --- sawmill ---
            // **1 log -> 3 planks, 15 s a plank** (Kevin, phone playtest
            // 2026-09-24: plank making "far too slow"). 180 s day / 15 s =
            // 12 boards a hand-day, so one log's job is 45 s on the bench.
            // The three come off together; a rack with less room takes what
            // fits and the rest wait on the bench, blocking it
            // (`OutpostLedger.UnloadBench`). A playtest number, not balance.
            new Recipe { id = "boards", station = "Sawmill", makes = Res.Boards, yield = 3,
                takes = Cost.Of(Cost.I(Res.Timber, 1)), ratePerDay = 12f },
            new Recipe { id = "fine-boards", station = "Sawmill", makes = Res.FineBoards, yield = 1,
                takes = Cost.Of(Cost.I(Res.Boards, 2)), ratePerDay = 2f,
                campfireLevel = 2, tool = Res.SawBlade, toolWear = 0.05f },

            // --- kitchen: dishes (food rework, 2026-09-27) ---
            // Cook times are REAL seconds at today's 180 s day (`Cook`), per
            // the approved design table; the first recipe is the kitchen's
            // default. Fill values and buffs are `Economy.FoodBook`'s.
            new Recipe { id = "baked-potato", station = "Kitchen", makes = Res.BakedPotato, yield = 1,
                takes = Cost.Of(Cost.I(Res.Potato, 1)), ratePerDay = Cook(15f, 1) },
            new Recipe { id = "grilled-fish", station = "Kitchen", makes = Res.GrilledFish, yield = 1,
                takes = Cost.Of(Cost.I(Res.Fish, 1)), ratePerDay = Cook(20f, 1) },
            // Kevin, 2026-09-28: *"Grilled meat needs to be added to the
            // kitchen recipes. I have 30 meat and my village is starving."*
            // Meat only went into hunter's stew (Kitchen III, fire II).
            new Recipe { id = "grilled-meat", station = "Kitchen", makes = Res.GrilledMeat, yield = 1,
                takes = Cost.Of(Cost.I(Res.Meat, 1)), ratePerDay = Cook(20f, 1) },
            new Recipe { id = "roast-carrots", station = "Kitchen", makes = Res.RoastCarrots, yield = 1,
                takes = Cost.Of(Cost.I(Res.Carrot, 2)), ratePerDay = Cook(20f, 1) },
            new Recipe { id = "bread", station = "Kitchen", makes = Res.Bread, yield = 3,
                takes = Cost.Of(Cost.I(Res.Flour, 2)), ratePerDay = Cook(45f, 3),
                campfireLevel = 2, stationLevel = 2 },
            new Recipe { id = "veg-stew", station = "Kitchen", makes = Res.VegStew, yield = 3,
                takes = Cost.Of(Cost.I(Res.Potato, 2), Cost.I(Res.Carrot, 1), Cost.I(Res.Onion, 1)),
                ratePerDay = Cook(60f, 3), campfireLevel = 2, stationLevel = 2 },
            new Recipe { id = "ships-biscuit", station = "Kitchen", makes = Res.Meals, yield = 4,
                takes = Cost.Of(Cost.I(Res.Flour, 2)), ratePerDay = Cook(60f, 4),
                campfireLevel = 2, stationLevel = 2 },
            new Recipe { id = "fish-pie", station = "Kitchen", makes = Res.FishPie, yield = 4,
                takes = Cost.Of(Cost.I(Res.Fish, 2), Cost.I(Res.Potato, 2), Cost.I(Res.Flour, 1)),
                ratePerDay = Cook(90f, 4), campfireLevel = 2, stationLevel = 3 },
            new Recipe { id = "hunters-stew", station = "Kitchen", makes = Res.HuntersStew, yield = 4,
                takes = Cost.Of(Cost.I(Res.Meat, 1), Cost.I(Res.Potato, 2), Cost.I(Res.Carrot, 1), Cost.I(Res.Onion, 1)),
                ratePerDay = Cook(90f, 4), campfireLevel = 2, stationLevel = 3 },
            // (Harvest feast -- Kitchen IV, fire III -- waits for fire III.)

            // --- mill (fire II): 2 wheat -> 1 flour, 30 s ---
            new Recipe { id = "flour", station = "Mill", makes = Res.Flour, yield = 1,
                takes = Cost.Of(Cost.I(Res.Wheat, 2)), ratePerDay = Cook(30f, 1), campfireLevel = 2 },

            // --- fishing hut (2026-09-27) ---
            // **A catch: no takes.** The sea is the input, so the bench loads
            // with nothing in the bay (`TryLoad` checks no lines) and the
            // fisher never waits on a hauler. Fish is Raw; `RecipeGraph`
            // allows a raw good ONLY from a recipe that takes nothing, which
            // is gathering at a station, not making. 4 a hand-day.
            // **Provisional, never played.** Makes fish since the food rework.
            new Recipe { id = "fish", station = "FishingHut", makes = Res.Fish, yield = 1,
                takes = Cost.None, ratePerDay = 4f },

            // --- fletcher ---
            new Recipe { id = "arrows", station = "Fletcher", makes = Res.Arrows, yield = 3,
                takes = Cost.Of(Cost.I(Res.Timber, 1)), ratePerDay = 3f },

            // --- forge ---
            // The first thing a forge makes needs no ore at all: Kevin's
            // spear is "a wood plank for the shaft and stone or metal for
            // the tip", and the stone one is what gets the first hunter out.
            new Recipe { id = "spear", station = "Blacksmith", makes = Res.Spear, yield = 1,
                takes = Cost.Of(Cost.I(Res.Boards, 1), Cost.I(Res.Stone, 1)), ratePerDay = 1.5f },
            new Recipe { id = "iron", station = "Blacksmith", makes = Res.Iron, yield = 1,
                takes = Cost.Of(Cost.I(Res.Ore, 2)), ratePerDay = 1.5f, campfireLevel = 2 },
            new Recipe { id = "saw-blade", station = "Blacksmith", makes = Res.SawBlade, yield = 1,
                takes = Cost.Of(Cost.I(Res.Iron, 2)), ratePerDay = 0.5f, campfireLevel = 2 },
            new Recipe { id = "tools", station = "Blacksmith", makes = Res.Tools, yield = 1,
                takes = Cost.Of(Cost.I(Res.Iron, 1), Cost.I(Res.Boards, 1)), ratePerDay = 1f, campfireLevel = 2 },
            new Recipe { id = "iron-spear", station = "Blacksmith", makes = Res.IronSpear, yield = 1,
                takes = Cost.Of(Cost.I(Res.Boards, 1), Cost.I(Res.Iron, 1)), ratePerDay = 1f,
                campfireLevel = 2, stationLevel = 2 },

            // --- quarry ---
            new Recipe { id = "brick", station = "Quarry", makes = Res.Brick, yield = 1,
                takes = Cost.Of(Cost.I(Res.Stone, 1)), ratePerDay = 2f,
                campfireLevel = 2, tool = Res.Tools, toolWear = 0.1f },
        };

        static Dictionary<string, Recipe> byId;
        static Dictionary<string, List<Recipe>> byStation;

        static void Index()
        {
            if (byId != null) return;
            byId = new Dictionary<string, Recipe>();
            byStation = new Dictionary<string, List<Recipe>>();
            foreach (var r in All)
            {
                byId[r.id] = r;
                if (!byStation.TryGetValue(r.station, out var list))
                    byStation[r.station] = list = new List<Recipe>();
                list.Add(r);
            }
        }

        public static Recipe Named(string id)
        {
            Index();
            return id != null && byId.TryGetValue(id, out var r) ? r : null;
        }

        /// Everything a station can ever make, locked ones included -- the
        /// sheet shows those greyed with the reason, so the player can see
        /// the step after the one they are on.
        public static IReadOnlyList<Recipe> At(string stationId)
        {
            Index();
            return stationId != null && byStation.TryGetValue(stationId, out var list)
                ? list : (IReadOnlyList<Recipe>)System.Array.Empty<Recipe>();
        }

        public static bool StationHasRecipes(string stationId) => At(stationId).Count > 0;

        /// The recipe a station works when the player has not chosen: its
        /// first, which is the old one-input conversion for every station
        /// that had one.
        public static Recipe Default(string stationId)
        {
            var list = At(stationId);
            return list.Count > 0 ? list[0] : null;
        }

        /// Every recipe that makes `res`, any station.
        public static List<Recipe> Making(string res)
        {
            var list = new List<Recipe>();
            foreach (var r in All) if (r.makes == res) list.Add(r);
            return list;
        }
    }
}
