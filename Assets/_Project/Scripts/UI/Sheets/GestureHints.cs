using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.UI.Sheets
{
    /// **First-use gesture hints, 2026-09-30 (island UI restructure,
    /// phase 6).** Kevin's approved mockup "8 · Sea: sailing" draws a faint
    /// dashed ring and "drag here to sail" in the stick zone until the player
    /// has steered once. Rule 7 (teach by doing): the hint is the real
    /// gesture's own spot, and it goes for good the first time the gesture is
    /// used -- one `PlayerPrefs` flag per hint, so a new save does not bring
    /// it back and a second device does.
    ///
    /// Tiny on purpose: the drawing belongs to whoever owns the spot (the
    /// sea HUD draws the stick ring); this only remembers.
    /// <code>
    /// bool show = GestureHints.ShowOnce("hint.stick", helm.StickInUse);
    /// </code>
    /// No allocation after the first ask of a key (the flag is cached).
    public static class GestureHints
    {
        const string Prefix = "SeaSick.hint.";

        /// The stick at sea: "drag here to sail".
        public const string Stick = "stick";

        // **The rest of phase 6's hints (2026-09-30)**, drawn by
        // `GestureHintPill` (camp and sea) and `BackpackSheet` (its info line).
        /// At camp, the first time the camp view shows: "Drag to look around ·
        /// pinch to zoom". Retired by a pan, zoom or turn, or a building tap.
        public const string Look = "look";
        /// At camp, the first time a villager is on screen: "Press and hold a
        /// hand to carry them to a job". Retired by picking one up.
        public const string Carry = "carry";
        /// At camp, after the first pan: "Double-tap the ground to fly there".
        public const string FlyTo = "flyto";
        /// At sea, the first time an enemy is in lock range: "Tap the enemy
        /// ship to lock your guns". Retired by a lock (any path).
        public const string Lock = "lock";
        /// The Backpack's first open with island tiles: "Hold a tile to keep
        /// some ashore". Retired by a hold on an island tile.
        public const string Keep = "keep";

        /// **How long a hint may stand before it retires on its own**
        /// (Kevin's brief: "auto-hide after the gesture is done once, or
        /// after ~6 s"). Counted only while the hint is actually on screen
        /// (`Shown`), so a hint that was covered by a sheet still gets its
        /// six seconds.
        public const float ShowSeconds = 6f;

        static readonly string[] All = { Stick, Look, Carry, FlyTo, Lock, Keep };
        static readonly Dictionary<string, float> shownFor = new Dictionary<string, float>();

        /// Count `dt` of on-screen time against `key`; true (and the flag
        /// saved) once it has stood `ShowSeconds`. Call only on frames the
        /// hint is drawn.
        public static bool Shown(string key, float dt)
        {
            if (string.IsNullOrEmpty(key) || IsDone(key)) return true;
            shownFor.TryGetValue(key, out float t);
            t += Mathf.Max(0f, dt);
            shownFor[key] = t;
            if (t < ShowSeconds) return false;
            MarkDone(key);
            return true;
        }

        static readonly Dictionary<string, bool> done = new Dictionary<string, bool>();
        static readonly Dictionary<string, string> prefKeys = new Dictionary<string, string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { done.Clear(); shownFor.Clear(); }

        /// True while the hint `key` should still show. Pass `usedNow` true
        /// on the frame the gesture is performed: the flag is saved and every
        /// later call answers false.
        public static bool ShowOnce(string key, bool usedNow)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (IsDone(key)) return false;
            if (!usedNow) return true;
            MarkDone(key);
            return false;
        }

        public static bool IsDone(string key)
        {
            if (done.TryGetValue(key, out bool d)) return d;
            d = PlayerPrefs.GetInt(PrefKey(key), 0) != 0;
            done[key] = d;
            return d;
        }

        public static void MarkDone(string key)
        {
            if (IsDone(key)) return;
            done[key] = true;
            PlayerPrefs.SetInt(PrefKey(key), 1);
            PlayerPrefs.Save();
        }

        /// Dev: bring a hint back (all of them with null).
        public static void Forget(string key = null)
        {
            if (key == null)
            {
                foreach (var k in All) PlayerPrefs.DeleteKey(PrefKey(k));
                foreach (var k in prefKeys.Values) PlayerPrefs.DeleteKey(k);
                done.Clear();
                shownFor.Clear();
            }
            else { PlayerPrefs.DeleteKey(PrefKey(key)); done.Remove(key); shownFor.Remove(key); }
            PlayerPrefs.Save();
        }

        static string PrefKey(string key)
        {
            if (!prefKeys.TryGetValue(key, out var k)) prefKeys[key] = k = Prefix + key;
            return k;
        }
    }
}
