using UnityEngine;

namespace SeaSick.UI
{
    /// **Retired 2026-09-30** (Kevin, island UI restructure phase 6). This
    /// drew the IMGUI "⌂ Home" tab on the left rail at sea (tap, "sure?",
    /// tap again = `AnchorController.BerthAtHome`). That is the Ship sheet's
    /// **Home** pill now (`UI/Sheets/ShipSheet.cs`, same arm-then-confirm,
    /// the refusal shown on the pill), reached from the sea top bar's aboard
    /// count.
    ///
    /// The component stays because `Sea.unity` and `ArtDirectionLab.unity`
    /// carry it (and `HomeTabProbe` finds it); it draws nothing.
    public class HomeTab : MonoBehaviour
    {
    }
}
