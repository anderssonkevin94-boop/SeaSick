using UnityEngine;

namespace SeaSick.World.Economy
{
    /// **What a wall segment's or a gate's next level costs (2026-10-03).**
    ///
    /// Not a row in `Techs.Upgrades`: that table prices a building, and a
    /// wall segment is any length from 2 to 12 m, so its price is worked
    /// out from its length here. Same shape (`UpgradeStep`), so the sheet's
    /// upgrade card shows it like any other.
    ///
    /// **PROVISIONAL, Kevin to tune.** In line with the level 2 tower (6
    /// brick + 4 fine boards, the fire at II), scaled per metre and
    /// stone-heavy, because level 2 IS a stone base (three courses and a
    /// capstone) under squared oak:
    /// - a wall: 2 stone a metre, 1 brick and 1 fine board per 2 m (rounded
    ///   up) -- a 4 m segment is 8 stone + 2 brick + 2 fine boards;
    /// - a gate: flat, 6 stone + 6 brick (the two dressed stone pillars) +
    ///   4 fine boards (the oak leaves).
    /// Level 2 is the top for now.
    public static class WallUpgrades
    {
        public const int CampfireLevel = 2;

        /// **The stone a level 2 segment is made of, on top of its logs
        /// (Kevin 2026-10-03: "adjust the repair price").** A repair site
        /// prices logs from the palisade plan; a level 2 wall also has a
        /// stone base, so its repair takes the same share of the level 2
        /// stone and brick as of the logs. Fine boards stay out: a site has
        /// no fine-board count, and the oak timbers are what the logs pay
        /// for. Level 1 adds nothing.
        public static void StoneOf(float length, bool gate, int level, out int stone, out int brick)
        {
            stone = 0; brick = 0;
            if (level < 2) return;
            if (gate) { stone = 6; brick = 6; return; }
            stone = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0.5f, length) * 2f - 0.01f));
            brick = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0.5f, length) * 0.5f - 0.01f));
        }

        /// The step from `level` to the next, or null at the top.
        public static UpgradeStep Next(float length, bool gate, int level)
        {
            if (level >= 2) return null;
            int perTwo = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0.5f, length) * 0.5f - 0.01f));
            int stone = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0.5f, length) * 2f - 0.01f));
            var cost = gate
                ? Cost.Of(Cost.I(Res.Stone, 6), Cost.I(Res.Brick, 6), Cost.I(Res.FineBoards, 4))
                : Cost.Of(Cost.I(Res.Stone, stone), Cost.I(Res.Brick, perTwo), Cost.I(Res.FineBoards, perTwo));
            return new UpgradeStep
            {
                planId = gate ? BuildPlans.Gate.id : BuildPlans.Palisade.id,
                toLevel = 2,
                campfireLevel = CampfireLevel,
                rateMul = 1f,
                cost = cost,
            };
        }
    }
}
