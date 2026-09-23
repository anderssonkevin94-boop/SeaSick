using System.Collections.Generic;

namespace SeaSick.World.Economy
{
    /// Where a resource sits in the chain. **The rule every future recipe
    /// is held to**: raw is never made, and treated goods and items are made
    /// of raw and treated only. `RecipeGraph.Validate` enforces it, so a
    /// designer who breaks it finds out from the probe and not from a
    /// player who cannot reach the thing.
    public enum ResTier
    {
        /// Comes out of the ground or off an animal. Has no recipe.
        Raw,
        /// A raw made better at a station: boards, iron, brick.
        Treated,
        /// A thing somebody uses and wears out: a spear, a saw blade, a meal.
        Item,
    }

    /// How a resource first enters a pile.
    public enum ResSource
    {
        /// A hand told to gather it: the ground is the stock.
        Gathered,
        /// A hunter's kill, counted in animals (`Res.Game`).
        Hunted,
        /// Comes home beside another gatherable's yield -- Hide with meat.
        Drop,
        /// Only ever a recipe's output.
        Made,
    }

    /// One resource, described once.
    public struct ResDef
    {
        public string id;
        public string label;
        public ResTier tier;
        public ResSource source;
        /// The fire level at which this first appears in the game. A
        /// `Recipe` that makes it cannot be reachable earlier, and the
        /// validator says so if one is.
        public int campfireLevel;
        /// One line for the sheet: what it is for.
        public string blurb;
    }

    /// **The resource registry.** Every `Res` key is here or the validator
    /// names it. The existing `Res` switch functions (gather rate, colour)
    /// stay where they are; this table holds only what the chain needs to
    /// know.
    public static class ResDefs
    {
        public static readonly ResDef[] All =
        {
            // --- raw, campfire I ---
            new ResDef { id = Res.Timber, label = "timber", tier = ResTier.Raw, source = ResSource.Gathered, campfireLevel = 1,
                blurb = "logs, off any wooded island" },
            new ResDef { id = Res.Stone, label = "stone", tier = ResTier.Raw, source = ResSource.Gathered, campfireLevel = 1,
                blurb = "rough stone, off the grey boulders" },
            new ResDef { id = Res.Food, label = "food", tier = ResTier.Raw, source = ResSource.Gathered, campfireLevel = 1,
                blurb = "berries, wheat, meat: what the camp eats" },
            new ResDef { id = Res.Game, label = "game", tier = ResTier.Raw, source = ResSource.Hunted, campfireLevel = 1,
                blurb = "the herd on the island, counted in animals" },
            new ResDef { id = Res.Hide, label = "hide", tier = ResTier.Raw, source = ResSource.Drop, campfireLevel = 1,
                blurb = "one off every animal a hunter brings home" },
            // --- raw, further out ---
            new ResDef { id = Res.Ore, label = "ore", tier = ResTier.Raw, source = ResSource.Gathered, campfireLevel = 2,
                blurb = "dark rock with the metal in it, on the far islands" },
            new ResDef { id = Res.Spice, label = "spice", tier = ResTier.Raw, source = ResSource.Gathered, campfireLevel = 2,
                blurb = "picked far out; what the last rungs of the ship cost" },

            // --- treated ---
            new ResDef { id = Res.Boards, label = "boards", tier = ResTier.Treated, source = ResSource.Made, campfireLevel = 1,
                blurb = "timber sawn square" },
            new ResDef { id = Res.Iron, label = "iron", tier = ResTier.Treated, source = ResSource.Made, campfireLevel = 2,
                blurb = "ore smelted to a bar" },
            new ResDef { id = Res.Brick, label = "brick", tier = ResTier.Treated, source = ResSource.Made, campfireLevel = 2,
                blurb = "stone cut square with iron tools; what a building's second level is built of" },
            new ResDef { id = Res.FineBoards, label = "fine boards", tier = ResTier.Treated, source = ResSource.Made, campfireLevel = 2,
                blurb = "boards cut true on an iron saw" },

            // --- items ---
            new ResDef { id = Res.Meals, label = "meals", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 1,
                blurb = "food cooked; feeds better than it was" },
            new ResDef { id = Res.Arrows, label = "arrows", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 1,
                blurb = "spent by hunters and lookouts" },
            new ResDef { id = Res.Spear, label = "spear", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 1,
                blurb = "a board and a stone tip; a hunter cannot hunt without one" },
            new ResDef { id = Res.Tools, label = "tools", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "iron and a handle; the quarry wears them cutting brick, the ship's later rungs want them" },
            new ResDef { id = Res.SawBlade, label = "saw blade", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "an iron edge for the sawmill; cuts fine boards" },
            new ResDef { id = Res.IronSpear, label = "iron spear", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "a board and an iron tip; lasts three stone spears" },
        };

        static Dictionary<string, ResDef> byId;

        public static bool TryGet(string id, out ResDef def)
        {
            if (byId == null)
            {
                byId = new Dictionary<string, ResDef>();
                foreach (var d in All) byId[d.id] = d;
            }
            if (id != null && byId.TryGetValue(id, out def)) return true;
            def = default;
            return false;
        }

        public static bool Known(string id) => TryGet(id, out _);

        /// The lower-case name a sheet prints, or the raw key for one the
        /// table does not know (which the validator will have flagged).
        public static string Label(string id) => TryGet(id, out var d) ? d.label : (id ?? "?");

        public static ResTier Tier(string id) => TryGet(id, out var d) ? d.tier : ResTier.Raw;

        /// Raw and not made: what a camp can have before any building
        /// stands. Hunting's drop counts, because the herd is on the ground.
        public static bool IsRaw(string id) => Tier(id) == ResTier.Raw;
    }
}
