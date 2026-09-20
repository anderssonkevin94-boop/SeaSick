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

        /// Made, not found. A camp with nobody assigned never sees these.
        public const string Boards = "Boards";
        public const string Tools = "Tools";
        public const string Food = "Food";
        public const string Meals = "Meals";

        /// What a hand can be told to go and GATHER — the things that are
        /// lying about on an island. The rest are made at a building by
        /// somebody assigned to it.
        public static readonly string[] Gatherable =
            { Timber, Stone, Ore, Spice };

        public static bool IsGatherable(string r)
        {
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
            _ => 10f,
        };

        public static float RegrowPerDay(string r) => r switch
        {
            Timber => 0.02f,
            Spice => 0.01f,      // it grows; slowly
            _ => 0f,             // rock does not
        };

        /// Colour of a pile of it, for the stacks by the fire.
        public static Color Colour(string r) => r switch
        {
            Timber => new Color(0.43f, 0.30f, 0.18f),
            Stone => new Color(0.55f, 0.55f, 0.53f),
            Ore => new Color(0.48f, 0.40f, 0.26f),
            Spice => new Color(0.75f, 0.35f, 0.55f),
            Boards => new Color(0.66f, 0.50f, 0.30f),
            Tools => new Color(0.40f, 0.44f, 0.50f),
            Food => new Color(0.55f, 0.62f, 0.28f),
            Meals => new Color(0.72f, 0.58f, 0.34f),
            _ => new Color(0.5f, 0.5f, 0.5f),
        };
    }
}
