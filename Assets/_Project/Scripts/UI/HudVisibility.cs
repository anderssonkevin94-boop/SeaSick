using UnityEngine;

namespace SeaSick.UI
{
    /// What the player has chosen to keep on screen.
    ///
    /// The first real entries in the settings drawer, and deliberately about
    /// the HUD rather than about the game: this project's screen problem was
    /// never that any one instrument was wrong, it was that there were too
    /// many of them and no way to put one away. (The minimap, compass tape and
    /// crew-bar toggles went with those instruments, 2026-09-30.)
    ///
    /// Saved in `PlayerPrefs` so a choice survives the session that made it.
    /// Read through the properties, never cached: a panel that snapshots these
    /// in `Start` will not notice the toggle being flipped underneath it.
    public static class HudVisibility
    {
        const string Prefix = "seasick.hud.";

        public static bool Perf { get => Get("perf", false); set => Set("perf", value); }

        /// **The FEEL tuning lab's button (2026-09-27).** Kevin tunes with it,
        /// so it stays one tap away in Settings ("Show tuning lab"), but it is
        /// no longer on every player's screen: on by default in the editor and
        /// development builds, off in a release. Cached, because `FeelLab`
        /// asks on every IMGUI event.
        public static bool TuningLab
        {
            get { if (tuningLab < 0) tuningLab = Get("tuninglab", false) ? 1 : 0; return tuningLab == 1; }
            set { tuningLab = value ? 1 : 0; Set("tuninglab", value); }
        }
        static int tuningLab = -1;

        static bool Get(string key, bool fallback = true)
            => PlayerPrefs.GetInt(Prefix + key, fallback ? 1 : 0) != 0;

        static void Set(string key, bool value)
            => PlayerPrefs.SetInt(Prefix + key, value ? 1 : 0);
    }
}
