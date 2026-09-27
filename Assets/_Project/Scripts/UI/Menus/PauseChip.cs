using SeaSick.UI.ModularYard;
using SeaSick.Voyage;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeaSick.UI.Menus
{
    /// **The one button that opens the pause menu with a thumb.**
    ///
    /// Drawn in the same IMGUI pass as the rest of the HUD (`StatusHUD`,
    /// `SettingsPanel`) and in the same rail column, so it costs nothing new
    /// to reach and cannot land on anything else -- `HudLayout.Slot.RailPause`
    /// stacks it below Settings/Yard/Home. Hidden on a scene with no
    /// `VoyageManager` (the lab scenes) and while any other full-screen modal
    /// already owns the screen.
    ///
    /// **It installs itself**, the same way the rail's other tabs do.
    public class PauseChip : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<PauseChip>() != null) return;
            if (FindAnyObjectByType<VoyageManager>() == null) return;
            new GameObject("PauseChip").AddComponent<PauseChip>();
        }

        /// Same fix as `GameBoot.HookReinstallOnReload` and for the same
        /// reason: `RuntimeInitializeOnLoadMethod` does not refire on a
        /// mid-session `SceneManager.LoadScene` (Continue/New/Load all
        /// reload), so without this the pause chip -- along with the whole
        /// way back to a menu -- silently vanished after the first slot
        /// load or new voyage. `Install()` is idempotent.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HookReinstallOnReload()
        {
            SceneManager.sceneLoaded -= OnSceneLoadedReinstall;
            SceneManager.sceneLoaded += OnSceneLoadedReinstall;
        }

        static void OnSceneLoadedReinstall(Scene scene, LoadSceneMode mode) => Install();

        void OnGUI()
        {
            if (!SeaSick.Save.GameBoot.Decided) return;              // still on the boot overlay
            if (ShipyardModal.IsOpen) return;
            if (GameMenus.Current != GameMenus.Mode.None) return;    // a menu already owns the screen

            // **The ledger at sea (2026-09-27)**: the sea twin of the land
            // drawer's ☰, top of the rail while she is under way. Takes the
            // Settings slot, whose tab only shows while its drawer is open --
            // and then this steps aside, so the two never share it.
            if (SeaSick.UI.Sheets.SeaLedger.Available && !SettingsPanel.IsOpen && !SeaSick.UI.Sheets.SeaLedger.IsOpen)
            {
                var led = HudLayout.Place(HudLayout.Slot.RailSettings, HudLayout.RailWidth, HudLayout.RailButtonHeight);
                UIBlocker.Block(led);
                if (GUI.Button(led, "Ledger", UITheme.Button)) SeaSick.UI.Sheets.SeaLedger.Toggle();
            }
            if (SeaSick.UI.Sheets.SeaLedger.IsOpen) return;   // IMGUI would draw over the drawer

            var tab = HudLayout.Place(HudLayout.Slot.RailPause, HudLayout.RailWidth, HudLayout.RailButtonHeight);
            UIBlocker.Block(tab);
            // `UITheme.Button` directly, not a copy -- see `SettingsPanel`'s
            // gear tab for the same pattern. A style built per event is
            // exactly the allocation `SheetKit`'s notes on `ShipyardPanel`
            // warn against.
            if (GUI.Button(tab, "❚❚ pause", UITheme.Button)) GameMenus.TogglePause();
        }
    }
}
