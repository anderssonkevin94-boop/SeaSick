using SeaSick.Steamer;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Steamer.EditorTools
{
    /// The switch between the two player ships.
    ///
    /// A PlayerPrefs key rather than a scene flag or a define, because the
    /// steamer has to run side by side with the ladder ship without the scene
    /// file changing and without a recompile between A and B. It is read once
    /// per play by `SteamerBootstrap`, before the scene loads, so toggling it
    /// mid-play changes the NEXT play, which is what the log line says.
    public static class SteamerMenu
    {
        const string MenuPath = "SeaSick/Dev/Sail the Steamer";

        [MenuItem(MenuPath)]
        static void Toggle()
        {
            bool steamer = !SteamerBootstrap.Selected;
            PlayerPrefs.SetInt(SteamerBootstrap.PrefKey, steamer ? 1 : 0);
            // Written through now: an editor crash between the toggle and the
            // next play would otherwise quietly put back the other ship.
            PlayerPrefs.Save();
            Debug.Log(steamer
                ? "[Steamer] next Play sails the PADDLE STEAMER (PlayerShip is converted at load)."
                : "[Steamer] next Play sails the LADDER SHIP (the scene as authored).");
        }

        /// The validate function is where Unity lets a menu item draw its
        /// checkmark; it runs each time the menu opens, so the tick always
        /// reflects the stored preference rather than a cached bool that
        /// domain-reload-off would let go stale.
        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, SteamerBootstrap.Selected);
            return true;
        }
    }
}
