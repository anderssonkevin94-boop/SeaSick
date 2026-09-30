using SeaSick.World;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **"Move" on a building's sheet (Kevin, 2026-09-30: "I want to be able
    /// to turn and move buildings even after they're built. Don't allow this
    /// for the walls or roads.").**
    ///
    /// A small secondary button in the sheet's header, just left of ✕, so it
    /// never displaces the page's own main action (the sheets hug their
    /// content on the phone since f1a34f5 and have no room for another row).
    /// The header's own `st-btn` look, 56 px tall -- over the 44 px thumb
    /// floor. A tap closes the sheet (the building must be in view to be
    /// placed) and starts the ordinary placement flow for THIS building:
    /// `CampSiting.BeginMove`, the same ghost, Turn and bottom bar as
    /// building a new one, "Move here" to confirm, Cancel to leave it exactly
    /// where it was. Absent for anything `Outpost.CanMove` refuses (walls,
    /// gates, roads, ladders, a pier, the dry dock, a tower on the wall).
    public static class MoveButton
    {
        /// Put the button into `headerRoot` (a `st-head` row: ... pill, ✕),
        /// before its last child, the close button. No-op for a building
        /// that cannot move.
        public static void AddTo(VisualElement headerRoot, Outpost outpost, Building building)
        {
            if (headerRoot == null || outpost == null || building == null) return;
            if (!outpost.CanMove(building, out _)) return;
            var btn = new Button(() => Begin(outpost, building)) { text = "Move" };
            btn.AddToClassList("st-btn");
            btn.tooltip = "Move or turn this building";
            btn.style.flexShrink = 0;
            btn.style.minWidth = 44;
            btn.style.minHeight = 44;
            btn.style.marginLeft = 0;
            btn.style.marginRight = 10;
            int at = headerRoot.childCount > 0 ? headerRoot.childCount - 1 : 0;
            headerRoot.Insert(at, btn);
        }

        /// Pick the building up. The sheet closes first thing after, the way
        /// the blueprint's own Move does (`SiteSheet.MoveBuild`).
        public static void Begin(Outpost outpost, Building building)
        {
            if (outpost == null || building == null) return;
            CampSiting.BeginMove(outpost, building, SheetBits.ShipTransform);
            Sheets.Close();
        }
    }
}
