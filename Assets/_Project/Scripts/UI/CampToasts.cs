using SeaSick.Ship;
using SeaSick.World;
using UnityEngine;
using SheetsHud = global::SeaSick.UI.Sheets.Sheets;

namespace SeaSick.UI
{
    /// **The two camp toasts, whoever owns the rest of the island.**
    ///
    /// `ReturnSummary` ("while you were gone") and `RaidBanner` (the lookout's
    /// warning, the live score, the verdict) are static drawers with no host
    /// of their own. The legacy camp bar used to call them from its `OnGUI`,
    /// after the line that stood it down for the sheet HUD -- so at any camp
    /// with a fire, which is every camp worth raiding, neither ever drew.
    /// This is their host now, and it never stands down: both are news about
    /// the place she is lying at, and neither covers a button.
    ///
    /// Added on demand by `AnchorController.Awake`, beside the ship, so a
    /// fresh play needs nothing placed in `Sea.unity`.
    public class CampToasts : MonoBehaviour
    {
        void OnGUI()
        {
            // Same IMGUI-blind-spot suppression as the rest of the HUD
            // (2026-09-26 review: "+2 timber" floated over the Home card).
            if (SeaSick.UI.ModularYard.ShipyardModal.IsOpen
                || SeaSick.UI.Menus.GameMenus.Current != SeaSick.UI.Menus.GameMenus.Mode.None) return;
            var outpost = Here();
            if (outpost == null) return;
            // Both declare themselves through `HudLayout.ToastRow`, and both
            // cost nothing when there is nothing to say.
            ReturnSummary.Draw(outpost, TimeOfDay.Seconds);
            RaidBanner.Draw(outpost);
        }

        /// The camp she is lying at, resolved the way the sheet HUD resolves
        /// it (`Sheets.Evaluate`): the cached anchor, stopped at an island,
        /// that island's outpost. Null anywhere else.
        static Outpost Here()
        {
            var anchor = SheetsHud.Anchor;
            if (anchor == null) return null;
            bool stopped = anchor.CurrentState == AnchorController.State.Anchored
                        || anchor.CurrentState == AnchorController.State.Ashore
                        || anchor.CurrentDock != null;
            if (!stopped) return null;
            var isle = anchor.CurrentIsland;
            return isle != null ? Outpost.Of(isle) : null;
        }
    }
}
