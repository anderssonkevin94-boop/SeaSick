using System.Collections.Generic;

namespace SeaSick.World.Economy
{
    /// One level of the fire, and what raising it to that level costs and
    /// opens. **The classic**: level the campfire to level the camp.
    public class CampfireLevel
    {
        public int level;
        public string name;
        /// Paid from the pile, at the fire, all at once.
        public Ingredient[] cost = Cost.None;
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
        public Ingredient[] cost = Cost.None;
        /// Multiplier on the station's rate at this level.
        public float rateMul = 1.5f;
        /// Extra store ceiling per resource at this level, for plans with a
        /// `storeCapacity`.
        public int storeBonus;
        /// Extra beds at this level, for huts.
        public int housesBonus;
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
                cost = Cost.Of(Cost.I(Res.Boards, 10), Cost.I(Res.Stone, 6), Cost.I(Res.Hide, 4)),
                unlocksPlans = new[] { "Quarry" },
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
            new UpgradeStep { planId = "Fletcher", toLevel = 2, campfireLevel = 2,
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
        public static float SpearWear(string spear) => spear == Res.IronSpear ? 1f / 12f : 0.25f;

        /// What comes home beside the meat, per animal.
        public static readonly Ingredient[] HuntDrops = { Cost.I(Res.Hide, 1) };
    }
}
