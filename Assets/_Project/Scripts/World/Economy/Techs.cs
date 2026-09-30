using System.Collections.Generic;

namespace SeaSick.World.Economy
{
    /// One level of the fire, and what raising it to that level costs and
    /// opens. **The classic**: level the campfire to level the camp.
    public class CampfireLevel
    {
        public int level;
        public string name;
        /// Paid from the pile, at the fire, all at once. The number as
        /// written (code default, or the tuning asset's row once
        /// `EconomyTuning.EnsureApplied` has run); `cost` is what is paid.
        public Ingredient[] baseCost = Cost.None;
        /// What raising the fire costs: `baseCost` through FEEL's cost
        /// multiplier.
        public Ingredient[] cost { get => Cost.Scaled(baseCost); set => baseCost = value ?? Cost.None; }
        /// Plan ids that become buildable at this level. Anything not named
        /// at any level is buildable from the start.
        public string[] unlocksPlans = new string[0];
        /// One line for the fire's sheet.
        public string blurb;
    }

    /// What a building's next level costs. Paid from the pile at the
    /// building; no labour, no site. Level 2 needs the fire at II.
    public class UpgradeStep
    {
        public string planId;
        public int toLevel;
        public int campfireLevel;
        /// The price as written; `cost` is what is paid (FEEL's multiplier).
        public Ingredient[] baseCost = Cost.None;
        public Ingredient[] cost { get => Cost.Scaled(baseCost); set => baseCost = value ?? Cost.None; }
        /// Multiplier on the station's rate at this level.
        public float rateMul = 1.5f;
        /// Extra store ceiling per resource at this level, for plans with a
        /// `storeCapacity`.
        public int storeBonus;
        /// Extra beds at this level, for huts.
        public int housesBonus;
    }

    /// **How many of one plan a camp may raise, by fire level, 2026-09-27.**
    /// Kevin: *"you can unlock the quantity of some buildings based on
    /// campfire. so like lvl 1 campfire is 2 houses, level 2 is 3 houses,
    /// etc. maybe second sawmill in lvl 3 etc."* `copies[i]` is the cap at
    /// fire level `i + 1`; a fire above the row's length reads its last
    /// entry. Built AND queued copies both count (`OutpostLedger.CopiesHeld`),
    /// and a wall tower is a watchtower like any other.
    public class BuildingCap
    {
        public string planId;
        public int[] copies;
    }

    /// **The tech table.** Fire levels, plan gates and upgrade prices in one
    /// place, so `RecipeGraph.Validate` can walk the whole ladder and say
    /// whether every step is reachable from the one before it.
    public static class Techs
    {
        public const int MaxCampfireLevel = 2;

        public static readonly CampfireLevel[] Campfire =
        {
            new CampfireLevel { level = 1, name = "camp", blurb = "a fire, and somewhere to keep ten of anything" },
            // Boards want a sawmill, stone wants a hand on the boulders,
            // hide wants a spear -- so the first level-up already asks for
            // the whole of campfire I's chain, which is the point of it.
            new CampfireLevel
            {
                level = 2, name = "hamlet",
                // 2026-09-27 first honest pass (GDD "Economy numbers"): 20 boards
                // is 7 logs through the sawmill, 12 stone a quarter-hour of
                // one hand, 4 hide = 4 kills on one stone spear.
                cost = Cost.Of(Cost.I(Res.Boards, 20), Cost.I(Res.Stone, 12), Cost.I(Res.Hide, 4)),
                unlocksPlans = new[] { "Quarry", "Mill" },
                blurb = "opens the quarry, the forge's iron work, and every building's second level",
            },
        };

        /// Every plan's level-2 price. Brick and fine boards both, so a
        /// second level needs the quarry AND the saw blade, which needs the
        /// forge's iron, which needs an ore island: the far-ring pull the
        /// ship ladder already has, now on the camp too.
        public static readonly UpgradeStep[] Upgrades =
        {
            new UpgradeStep { planId = "Sawmill", toLevel = 2, campfireLevel = 2,
                cost = Cost.Of(Cost.I(Res.Brick, 6), Cost.I(Res.FineBoards, 4)) },
            new UpgradeStep { planId = "Blacksmith", toLevel = 2, campfireLevel = 2,
                cost = Cost.Of(Cost.I(Res.Brick, 6), Cost.I(Res.FineBoards, 4)) },
            new UpgradeStep { planId = "Kitchen", toLevel = 2, campfireLevel = 2,
                cost = Cost.Of(Cost.I(Res.Brick, 4), Cost.I(Res.FineBoards, 4)) },
            // Food rework (2026-09-27): Kitchen III (fish pie, hunter's
            // stew) and Farm II/III (onion+wheat, apple; 9/12 plots) all at
            // fire II until fire III exists. Provisional prices.
            new UpgradeStep { planId = "Kitchen", toLevel = 3, campfireLevel = 2,
                cost = Cost.Of(Cost.I(Res.Brick, 8), Cost.I(Res.FineBoards, 6)) },
            new UpgradeStep { planId = "Farm", toLevel = 2, campfireLevel = 2, rateMul = 1f,
                cost = Cost.Of(Cost.I(Res.Brick, 4), Cost.I(Res.Boards, 6)) },
            new UpgradeStep { planId = "Farm", toLevel = 3, campfireLevel = 2, rateMul = 1f,
                cost = Cost.Of(Cost.I(Res.Brick, 8), Cost.I(Res.FineBoards, 4)) },
            new UpgradeStep { planId = "Mill", toLevel = 2, campfireLevel = 2,
                cost = Cost.Of(Cost.I(Res.Brick, 6), Cost.I(Res.FineBoards, 3)) },
            new UpgradeStep { planId = "Fletcher", toLevel = 2, campfireLevel = 2,
                cost = Cost.Of(Cost.I(Res.Brick, 4), Cost.I(Res.FineBoards, 3)) },
            // Fishing hut, 2026-09-27: priced like the fletcher (a small
            // station), rate x1.5 like every station. Provisional.
            new UpgradeStep { planId = "FishingHut", toLevel = 2, campfireLevel = 2,
                cost = Cost.Of(Cost.I(Res.Brick, 4), Cost.I(Res.FineBoards, 3)) },
            new UpgradeStep { planId = "Quarry", toLevel = 2, campfireLevel = 2,
                cost = Cost.Of(Cost.I(Res.Brick, 8), Cost.I(Res.FineBoards, 2)) },
            new UpgradeStep { planId = "Hut", toLevel = 2, campfireLevel = 2, rateMul = 1f, housesBonus = 1,
                cost = Cost.Of(Cost.I(Res.Brick, 4), Cost.I(Res.FineBoards, 4)) },
            new UpgradeStep { planId = "Storehouse", toLevel = 2, campfireLevel = 2, rateMul = 1f, storeBonus = 20,
                cost = Cost.Of(Cost.I(Res.Brick, 8), Cost.I(Res.FineBoards, 4)) },
            new UpgradeStep { planId = "Storage", toLevel = 2, campfireLevel = 2, rateMul = 1f, storeBonus = 10,
                cost = Cost.Of(Cost.I(Res.Brick, 6), Cost.I(Res.FineBoards, 2)) },
        };

        /// **Copies per fire level (I, II, III, IV) -- PROVISIONAL, Kevin to
        /// tune (GDD "Multiple buildings").** A plan with no row here keeps
        /// the old one-of-each rule (the campfire, the pier, the dry dock).
        /// Fire III and IV do not exist yet (`MaxCampfireLevel`); the columns
        /// are here so the day they land the second sawmill needs no data
        /// pass. A cap of 0 is never written: a plan the fire has not opened
        /// is refused by `PlanUnlocked`, not by this table.
        public static readonly BuildingCap[] Caps =
        {
            new BuildingCap { planId = "Hut",        copies = new[] { 2, 3, 4, 5 } },
            new BuildingCap { planId = "Storage",    copies = new[] { 1, 2, 3, 3 } },
            new BuildingCap { planId = "Storehouse", copies = new[] { 1, 1, 2, 2 } },
            new BuildingCap { planId = "Watchtower", copies = new[] { 2, 4, 6, 8 } },
            new BuildingCap { planId = "Farm",       copies = new[] { 1, 2, 2, 3 } },
            // Food without a field (2026-09-27): a second hut at fire II,
            // like the farm. Provisional.
            new BuildingCap { planId = "FishingHut", copies = new[] { 1, 2, 2, 3 } },
            // The second station of a kind at fire III (Kevin: "maybe
            // second sawmill in lvl 3").
            new BuildingCap { planId = "Sawmill",    copies = new[] { 1, 1, 2, 2 } },
            new BuildingCap { planId = "Blacksmith", copies = new[] { 1, 1, 2, 2 } },
            new BuildingCap { planId = "Kitchen",    copies = new[] { 1, 1, 2, 2 } },
            new BuildingCap { planId = "Mill",       copies = new[] { 1, 1, 1, 2 } },
            new BuildingCap { planId = "Fletcher",   copies = new[] { 1, 1, 2, 2 } },
            new BuildingCap { planId = "Quarry",     copies = new[] { 1, 1, 2, 2 } },
        };

        /// The highest fire level the cap table speaks for (its widest row).
        public static int CapTableLevels
        {
            get
            {
                int n = 1;
                foreach (var c in Caps) if (c.copies != null && c.copies.Length > n) n = c.copies.Length;
                return n;
            }
        }

        /// How many of `planId` a camp whose fire is at `fireLevel` may hold.
        /// 1 for a plan the table does not name.
        public static int MaxCopies(string planId, int fireLevel)
        {
            foreach (var c in Caps)
            {
                if (c.planId != planId || c.copies == null || c.copies.Length == 0) continue;
                int i = System.Math.Max(0, System.Math.Min(fireLevel, c.copies.Length) - 1);
                return System.Math.Max(1, c.copies[i]);
            }
            return 1;
        }

        /// The lowest fire level at which a camp may hold `copies` of
        /// `planId`, or 0 when no level in the table allows that many.
        public static int FireLevelForCopies(string planId, int copies)
        {
            int top = CapTableLevels;
            for (int L = 1; L <= top; L++)
                if (MaxCopies(planId, L) >= copies) return L;
            return 0;
        }

        public static CampfireLevel CampfireAt(int level)
        {
            foreach (var c in Campfire) if (c.level == level) return c;
            return null;
        }

        /// The step from `level` to `level + 1`, or null at the top.
        public static CampfireLevel NextCampfire(int level) => CampfireAt(level + 1);

        /// Fire level a plan needs before it is offered. 1 for anything no
        /// level names.
        public static int PlanLevel(string planId)
        {
            foreach (var c in Campfire)
                foreach (var p in c.unlocksPlans)
                    if (p == planId) return c.level;
            return 1;
        }

        public static UpgradeStep Upgrade(string planId, int toLevel)
        {
            foreach (var u in Upgrades)
                if (u.planId == planId && u.toLevel == toLevel) return u;
            return null;
        }

        /// The highest level a plan can be taken to.
        public static int MaxLevel(string planId)
        {
            int max = 1;
            foreach (var u in Upgrades) if (u.planId == planId && u.toLevel > max) max = u.toLevel;
            return max;
        }

        /// Rate multiplier for a station at `level`, product of every step
        /// up to it. Level 1 (and the 0 an old save reads) is 1.
        public static float RateMul(string planId, int level)
        {
            float m = 1f;
            for (int l = 2; l <= level; l++)
            {
                var u = Upgrade(planId, l);
                if (u != null) m *= u.rateMul;
            }
            return m;
        }

        public static int StoreBonus(string planId, int level)
        {
            int b = 0;
            for (int l = 2; l <= level; l++) { var u = Upgrade(planId, l); if (u != null) b += u.storeBonus; }
            return b;
        }

        public static int HousesBonus(string planId, int level)
        {
            int b = 0;
            for (int l = 2; l <= level; l++) { var u = Upgrade(planId, l); if (u != null) b += u.housesBonus; }
            return b;
        }

        // --- hunting ---------------------------------------------------------

        /// What a hunter needs in his hand, best first. **Hard gate**, Kevin
        /// 2026-09-23: no spear, no kills.
        public static readonly string[] HuntingSpears = { Res.IronSpear, Res.Spear };

        /// Spears used up per animal taken. A stone spear lasts four beasts,
        /// an iron one twelve.
        /// (Animals per spear: the tuning asset's `stoneSpearAnimals` /
        /// `ironSpearAnimals`.)
        public static float SpearWear(string spear) =>
            spear == Res.Bow ? BowWear : 1f / EconomyTuning.SpearAnimals(spear == Res.IronSpear);

        /// **Bows used up per kill (2026-09-30)**, hunting or raid: one over
        /// the tuning asset's `bowAnimals`. The arrow each shot spends is
        /// separate and whole (`OutpostLedger.SpendArrow`).
        public static float BowWear => 1f / EconomyTuning.BowAnimals;

        /// **What comes home beside the meat, per animal -- and the ONLY way
        /// Hide enters a camp** (Kevin, 2026-09-27: "fine that hide is the
        /// only way to reach campfire 2, as long as the hide can't be
        /// gathered"). The count is FEEL's `hidePerAnimal`, rounded.
        public static Ingredient[] HuntDrops
        {
            get
            {
                int n = UnityEngine.Mathf.Max(0, UnityEngine.Mathf.RoundToInt(EconomyFeel.hidePerAnimal));
                if (huntDrops == null || huntDrops[0].n != n) huntDrops = new[] { Cost.I(Res.Hide, n) };
                return huntDrops;
            }
        }
        static Ingredient[] huntDrops;
    }
}
