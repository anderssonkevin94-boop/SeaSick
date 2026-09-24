using UnityEngine;

namespace SeaSick.World
{
    /// One unit of cargo as a thing you can see -- a log, a block of stone, a
    /// sack -- and the grid a simple stack of them sits on.
    ///
    /// **There is one drawing of a unit, and it is the camp's (2026-09-24).**
    /// This used to build its own meshes, sized in 2026-08 for the brig: a
    /// 1.5 m log 0.42 m thick, a 1.1 x 0.75 x 1.1 m block of stone, a 0.95 m
    /// cube of ore. The piles by the fire (`CampPiles`) draw a log a quarter
    /// of a metre thick and a stone under half a metre across, so the same
    /// load was two to three times the size (eight to twenty times the bulk)
    /// on the deck as it had been on the ground a minute earlier. Kevin,
    /// iPhone playtest: *"the cargo gets HUGE on the ship. much larger than
    /// it is on land."* `Build` now hands out `CampPiles.BuildUnit`, so a
    /// resource is the same size and shape wherever it is. The deck load
    /// itself (`Ship.ShipHold`) draws whole piles with `CampPiles.DrawPile`.
    public static class CargoVisual
    {
        /// One unit of `resource`, at the size it is in the pile by the fire,
        /// under a new root parented to `parent` (centred on the root, base
        /// at about y = 0). Callers place and scale the root.
        public static GameObject Build(string resource, Transform parent) =>
            CampPiles.BuildUnit(resource, parent);

        /// Where the nth item sits in a stack: rows across, then layers up.
        public static Vector3 StackSlot(int index, int perRow, float spacing, float layerHeight)
        {
            int layer = index / (perRow * perRow);
            int inLayer = index % (perRow * perRow);
            int row = inLayer / perRow;
            int col = inLayer % perRow;
            return new Vector3(
                (col - (perRow - 1) * 0.5f) * spacing,
                layer * layerHeight,
                (row - (perRow - 1) * 0.5f) * spacing);
        }
    }
}
