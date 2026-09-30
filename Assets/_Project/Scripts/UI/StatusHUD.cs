using UnityEngine;

namespace SeaSick.UI
{
    /// **Retired 2026-09-30** (Kevin, island UI restructure phase 6,
    /// approved mockup "8 · Sea: sailing"). This drew the IMGUI hull / water
    /// panel in the top-right corner at sea. The same facts are the sea HUD's
    /// alert chip now (`UI/Sheets/SeaHud.cs`): "Hull 82% · repair" (ember,
    /// only when damaged, tap = the Ship sheet with its Repair pill) and
    /// "Taking water 40% · 2 bailing" while she floods.
    ///
    /// The component stays because `Sea.unity` and `ArtDirectionLab.unity`
    /// carry it; it draws nothing.
    public class StatusHUD : MonoBehaviour
    {
    }
}
