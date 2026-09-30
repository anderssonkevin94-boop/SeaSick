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

        static readonly Dictionary<string, bool> done = new Dictionary<string, bool>();
        static readonly Dictionary<string, string> prefKeys = new Dictionary<string, string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => done.Clear();

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
                foreach (var k in prefKeys.Values) PlayerPrefs.DeleteKey(k);
                PlayerPrefs.DeleteKey(PrefKey(Stick));
                done.Clear();
            }
            else { PlayerPrefs.DeleteKey(PrefKey(key)); done.Remove(key); }
            PlayerPrefs.Save();
        }

        static string PrefKey(string key)
        {
            if (!prefKeys.TryGetValue(key, out var k)) prefKeys[key] = k = Prefix + key;
            return k;
        }
    }
}
