using UnityEngine;

namespace SeaSick.World
{
    /// One thing the village can put up, and what it costs to put up.
    ///
    /// A plan is data, not a prefab: the buildings are primitives raised at
    /// runtime on ground that was chosen, never flattened (`TerrainHeight` is
    /// a pure function of position -- see Outpost), so there is nothing to
    /// author in the editor and nothing to keep in sync with a mesh.
    public struct BuildPlan
    {
        public string id;
        /// What the button says.
        public string label;
        /// One line on what it buys you. The player is spending a voyage's
        /// haul; they should know what for before they spend it.
        public string blurb;
        public string resource;
        public int cost;
        /// Units of stores it adds to what home can keep.
        public int storeCapacity;
        /// Metres: length along the ridge, then width across it.
        public Vector2 footprint;
        /// Ridge height, off the charter in WorldScale.
        public float ridge;
    }

    /// Everything that can be built, in the order it is offered.
    ///
    /// One entry for now. The loop it closes is the point: a voyage lands
    /// more timber than the beach can keep, so the first thing you build is
    /// somewhere to keep it, and the next voyage is worth more than the last.
    public static class BuildPlans
    {
        /// **The first building in the game.**
        ///
        /// Cost is set against the hold, not against a spreadsheet: she
        /// carries 24 to the marked line and 38 stuffed with deck cargo, so
        /// a storehouse is one full hold and a log over. **Two voyages,
        /// never one** -- and the second one has to come home, which is the
        /// decision the whole loop is made of.
        public static readonly BuildPlan Storehouse = new BuildPlan
        {
            id = "Storehouse",
            label = "storehouse",
            blurb = "keeps 40 more out of the weather",
            resource = "Timber",
            cost = 25,
            storeCapacity = 40,
            footprint = new Vector2(8f, 5f),
            ridge = WorldScale.Storehouse,
        };

        public static readonly BuildPlan[] All = { Storehouse };
    }
}
