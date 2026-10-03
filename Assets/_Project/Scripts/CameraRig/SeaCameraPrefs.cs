using UnityEngine;

namespace SeaSick.CameraRig
{
    /// **The player's sea-camera choices** (2026-10-03, DREDGE controls step 2,
    /// docs/PLAN-dredge-controls.md §3.3), the Settings drawer's "Camera" rows.
    /// DREDGE ships the same four: follow mode, sensitivity, invert X / Y.
    ///
    /// Saved in `PlayerPrefs` (not the save game: they are about the player,
    /// not the voyage). Cached, because `ChaseCamera` asks every frame; the
    /// setters write through.
    public static class SeaCameraPrefs
    {
        const string Prefix = "seasick.seacam.";

        /// On (default): the camera swings with the hull's turning, the player's
        /// look offset riding on top. Off: the camera holds its world bearing
        /// while she turns under it (DREDGE's detached Camera Follow Mode).
        public static bool Follow
        {
            get { Load(); return follow; }
            set { Load(); follow = value; PlayerPrefs.SetInt(Prefix + "follow", value ? 1 : 0); }
        }

        /// Look-around speed multiplier, 0.5..2 (1 = the tuned rate).
        public static float Sensitivity
        {
            get { Load(); return sensitivity; }
            set { Load(); sensitivity = Mathf.Clamp(value, 0.5f, 2f); PlayerPrefs.SetFloat(Prefix + "sens", sensitivity); }
        }

        public static bool InvertX
        {
            get { Load(); return invertX; }
            set { Load(); invertX = value; PlayerPrefs.SetInt(Prefix + "invx", value ? 1 : 0); }
        }

        public static bool InvertY
        {
            get { Load(); return invertY; }
            set { Load(); invertY = value; PlayerPrefs.SetInt(Prefix + "invy", value ? 1 : 0); }
        }

        static bool loaded, follow = true, invertX, invertY;
        static float sensitivity = 1f;

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            follow = PlayerPrefs.GetInt(Prefix + "follow", 1) != 0;
            sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "sens", 1f), 0.5f, 2f);
            invertX = PlayerPrefs.GetInt(Prefix + "invx", 0) != 0;
            invertY = PlayerPrefs.GetInt(Prefix + "invy", 0) != 0;
        }
    }
}
