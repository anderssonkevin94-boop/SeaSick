using UnityEngine;

namespace SeaSick.UI
{
    /// What the player has chosen to keep on screen.
    ///
    /// The first real entries in the settings drawer, and deliberately about
    /// the HUD rather than about the game: this project's screen problem was
    /// never that any one instrument was wrong, it was that there were too
    /// many of them and no way to put one away. Being able to drop the minimap
    /// and sail on the compass alone is a legitimate way to play a game whose
    /// pitch is reading the water.
    ///
    /// Saved in `PlayerPrefs` so a choice survives the session that made it.
    /// Read through the properties, never cached: a panel that snapshots these
    /// in `Start` will not notice the toggle being flipped underneath it.
    public static class HudVisibility
    {
        const string Prefix = "seasick.hud.";

        public static bool Minimap { get => Get("minimap"); set => Set("minimap", value); }
        public static bool Compass { get => Get("compass"); set => Set("compass", value); }
        public static bool Crew { get => Get("crew"); set => Set("crew", value); }
        public static bool Perf { get => Get("perf", false); set => Set("perf", value); }

        static bool Get(string key, bool fallback = true)
            => PlayerPrefs.GetInt(Prefix + key, fallback ? 1 : 0) != 0;

        static void Set(string key, bool value)
            => PlayerPrefs.SetInt(Prefix + key, value ? 1 : 0);
    }
}
