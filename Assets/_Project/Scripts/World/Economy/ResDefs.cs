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
        /// Grown on a farm plot (food rework, 2026-09-27): raw, never made,
        /// never gathered off the island.
        Grown,
        /// Won at sea and nowhere else (2026-10-03, kraken ink): raw, never
        /// made, never gathered or hunted off an island. Appended last; the
        /// enum is not serialised (`ResDefs` is a static table).
        Salvaged,
    }

    /// **Which Stores tab a resource lands on** (2026-09-26). Independent of
    /// `ResTier`/`ResSource` -- the chain cares whether a thing is raw or
    /// made, the Stores sheet cares where a player would go looking for it,
    /// and the two groupings do not agree (Game is raw but sits with Food;
    /// Tools is made but sits with Gear). `Armor` and `Ship` have no
    /// resources yet -- their tabs show "Nothing here yet." until something
    /// does.
    public enum ResCategory
    {
        Raw,
        Material,
        Food,
        Gear,
        Armor,
        Ship,
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
        /// The Stores tab this resource lands on -- set explicitly on every
        /// entry below, never left to the enum's default, so a resource
        /// nobody assigned a tab does not quietly read as "Raw".
        public ResCategory category;
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
                blurb = "logs, off any wooded island", category = ResCategory.Raw },
            new ResDef { id = Res.Stone, label = "stone", tier = ResTier.Raw, source = ResSource.Gathered, campfireLevel = 1,
                blurb = "rough stone, off the grey boulders", category = ResCategory.Raw },
            new ResDef { id = Res.Food, label = "food", tier = ResTier.Raw, source = ResSource.Gathered, campfireLevel = 1,
                blurb = "wild berries and roots, foraged; eaten raw at a quarter", category = ResCategory.Food },
            // --- food rework, 2026-09-27: crops, catch, meat ---
            new ResDef { id = Res.Potato, label = "potato", tier = ResTier.Raw, source = ResSource.Grown, campfireLevel = 1,
                blurb = "the staple; fast to grow, fine raw in a pinch", category = ResCategory.Food },
            new ResDef { id = Res.Carrot, label = "carrot", tier = ResTier.Raw, source = ResSource.Grown, campfireLevel = 1,
                blurb = "second veg; stew and roast", category = ResCategory.Food },
            new ResDef { id = Res.Onion, label = "onion", tier = ResTier.Raw, source = ResSource.Grown, campfireLevel = 2,
                blurb = "the stew gate; a farm II crop", category = ResCategory.Food },
            new ResDef { id = Res.Wheat, label = "wheat", tier = ResTier.Raw, source = ResSource.Grown, campfireLevel = 2,
                blurb = "only useful through the mill; a farm II crop", category = ResCategory.Food },
            new ResDef { id = Res.Apple, label = "apple", tier = ResTier.Raw, source = ResSource.Grown, campfireLevel = 2,
                blurb = "an orchard crop; a farm III plot", category = ResCategory.Food },
            new ResDef { id = Res.Fish, label = "fish", tier = ResTier.Raw, source = ResSource.Gathered, campfireLevel = 1,
                blurb = "off the fishing hut; grill it or eat it raw", category = ResCategory.Food },
            new ResDef { id = Res.Meat, label = "meat", tier = ResTier.Raw, source = ResSource.Hunted, campfireLevel = 1,
                blurb = "off every animal a hunter brings home", category = ResCategory.Food },
            new ResDef { id = Res.Game, label = "game", tier = ResTier.Raw, source = ResSource.Hunted, campfireLevel = 1,
                blurb = "the herd on the island, counted in animals", category = ResCategory.Food },
            new ResDef { id = Res.Hide, label = "hide", tier = ResTier.Raw, source = ResSource.Drop, campfireLevel = 1,
                blurb = "one off every animal a hunter brings home", category = ResCategory.Raw },
            // --- raw, further out ---
            new ResDef { id = Res.Ore, label = "iron ore", tier = ResTier.Raw, source = ResSource.Gathered, campfireLevel = 2,
                blurb = "mined from a hillside or gathered on far islands; smelted into iron at the forge", category = ResCategory.Raw },
            new ResDef { id = Res.Spice, label = "spice", tier = ResTier.Raw, source = ResSource.Gathered, campfireLevel = 2,
                blurb = "picked far out; what the last rungs of the ship cost", category = ResCategory.Food },
            new ResDef { id = Res.KrakenInk, label = "kraken ink", tier = ResTier.Raw, source = ResSource.Salvaged, campfireLevel = 1,
                blurb = "a trophy from a driven-off kraken; its use is still to be decided", category = ResCategory.Raw },

            // --- treated ---
            new ResDef { id = Res.Boards, label = "boards", tier = ResTier.Treated, source = ResSource.Made, campfireLevel = 1,
                blurb = "timber sawn square", category = ResCategory.Material },
            new ResDef { id = Res.Flour, label = "flour", tier = ResTier.Treated, source = ResSource.Made, campfireLevel = 2,
                blurb = "wheat ground at the mill; bread and biscuit", category = ResCategory.Food },
            new ResDef { id = Res.Iron, label = "iron", tier = ResTier.Treated, source = ResSource.Made, campfireLevel = 2,
                blurb = "ore smelted to a bar", category = ResCategory.Material },
            new ResDef { id = Res.Brick, label = "brick", tier = ResTier.Treated, source = ResSource.Made, campfireLevel = 2,
                blurb = "stone cut square with iron tools; what a building's second level is built of", category = ResCategory.Material },
            new ResDef { id = Res.FineBoards, label = "fine boards", tier = ResTier.Treated, source = ResSource.Made, campfireLevel = 2,
                blurb = "boards cut true on an iron saw", category = ResCategory.Material },

            new ResDef { id = "WoodShield", label = "wooden shield", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 1,
                blurb = "Villager defense equipment", category = ResCategory.Armor },
            new ResDef { id = "LeatherHelmet", label = "leather helmet", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 1,
                blurb = "Villager defense equipment", category = ResCategory.Armor },
            new ResDef { id = "LeatherVest", label = "leather vest", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 1,
                blurb = "Villager defense equipment", category = ResCategory.Armor },
            new ResDef { id = "LeatherPants", label = "leather pants", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 1,
                blurb = "Villager defense equipment", category = ResCategory.Armor },
            new ResDef { id = "LeatherBoots", label = "leather boots", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 1,
                blurb = "Villager defense equipment", category = ResCategory.Armor },
            new ResDef { id = "IronHelmet", label = "iron helmet", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "Forged villager equipment", category = ResCategory.Armor },
            new ResDef { id = "IronArmor", label = "iron breastplate", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "Forged villager equipment", category = ResCategory.Armor },
            new ResDef { id = "IronPants", label = "iron greaves", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "Forged villager equipment", category = ResCategory.Armor },
            new ResDef { id = "IronBoots", label = "iron boots", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "Forged villager equipment", category = ResCategory.Armor },
            new ResDef { id = "IronShield", label = "iron shield", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "Forged villager equipment", category = ResCategory.Armor },
            new ResDef { id = "IronSword", label = "iron sword", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "Forged villager equipment", category = ResCategory.Gear },
            // --- items ---
            // --- dishes (food rework, 2026-09-27; fill values in `FoodBook`) ---
            new ResDef { id = Res.BakedPotato, label = "baked potato", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 1,
                blurb = "a potato in the coals", category = ResCategory.Food },
            new ResDef { id = Res.GrilledFish, label = "grilled fish", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 1,
                blurb = "a fish over the fire", category = ResCategory.Food },
            new ResDef { id = Res.GrilledMeat, label = "grilled meat", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 1,
                blurb = "meat off the spit", category = ResCategory.Food },
            new ResDef { id = Res.RoastCarrots, label = "roast carrots", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 1,
                blurb = "two carrots, a proper plate", category = ResCategory.Food },
            new ResDef { id = Res.Bread, label = "bread", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "flour baked; a little cheer", category = ResCategory.Food },
            new ResDef { id = Res.VegStew, label = "vegetable stew", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "potato, carrot, onion; lifts the mood", category = ResCategory.Food },
            new ResDef { id = Res.FishPie, label = "fish pie", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "fish, potato and flour; hands work faster on it", category = ResCategory.Food },
            new ResDef { id = Res.HuntersStew, label = "hunter's stew", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "meat and three veg; the best plate in camp", category = ResCategory.Food },
            new ResDef { id = Res.Meals, label = "ship's biscuit", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "hard bread for the hold; keeps a crew at sea", category = ResCategory.Food },
            new ResDef { id = Res.Arrows, label = "arrows", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 1,
                blurb = "what a bow shoots, one a shot; lookouts loose them at raiders", category = ResCategory.Gear },
            new ResDef { id = Res.Spear, label = "spear", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 1,
                blurb = "a board and a stone tip; a hunter cannot hunt without one", category = ResCategory.Gear },
            new ResDef { id = Res.Tools, label = "tools", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "iron and a handle; the quarry wears them cutting brick, the ship's later rungs want them", category = ResCategory.Gear },
            new ResDef { id = Res.SawBlade, label = "saw blade", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "an iron edge for the sawmill; cuts fine boards", category = ResCategory.Gear },
            new ResDef { id = Res.IronSpear, label = "iron spear", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "a board and an iron tip; lasts three stone spears", category = ResCategory.Gear },
            // Kevin, 2026-09-30: the fletcher makes bows from fine boards +
            // hide. Fire II because fine boards are.
            new ResDef { id = Res.Bow, label = "bow", tier = ResTier.Item, source = ResSource.Made, campfireLevel = 2,
                blurb = "a fine board strung with hide; shoots arrows to hunt, defend and fight from the ship", category = ResCategory.Gear },
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

        /// **A count with its noun, singular when it is one (2026-09-30).**
        /// Labels such as "tools", "boards", "fine boards" and "arrows" are
        /// plural-only, so a bare `$"{n} {Label}"` read "1 tools". `Counted`
        /// says "1 tool" / "1 fine board" and leaves every other count
        /// ("3 tools", "4 timber", "1 spear") exactly as it was.
        public static string Counted(string id, int n)
        {
            string label = Label(id);
            if (n == 1 && label.Length > 2 && label.EndsWith("s")) label = label.Substring(0, label.Length - 1);
            return n + " " + label;
        }

        public static ResTier Tier(string id) => TryGet(id, out var d) ? d.tier : ResTier.Raw;

        /// The Stores tab a resource lands on. `ResCategory.Raw` for an id
        /// the table does not know, same fallback as every other accessor
        /// here -- the validator is what should have caught it.
        public static ResCategory Category(string id) => TryGet(id, out var d) ? d.category : ResCategory.Raw;

        /// Every known id in one category, in table order -- what the
        /// Stores sheet's "All" tab and every other tab both iterate.
        public static List<string> InCategory(ResCategory cat)
        {
            var list = new List<string>();
            foreach (var d in All) if (d.category == cat) list.Add(d.id);
            return list;
        }

        /// Raw and not made: what a camp can have before any building
        /// stands. Hunting's drop counts, because the herd is on the ground.
        public static bool IsRaw(string id) => Tier(id) == ResTier.Raw;
    }
}
