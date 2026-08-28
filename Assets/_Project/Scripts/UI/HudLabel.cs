using UnityEngine;

namespace SeaSick.UI
{
    /// A readout whose string is rebuilt only when the thing it displays
    /// actually changes.
    ///
    /// IMGUI regenerates a text mesh for every string it is handed, and
    /// `TextGenerator.Prepare` allocates while doing it. A HUD that formats
    /// `$"hull {integrity:P0}"` every frame therefore pays for a new text mesh
    /// sixty times a second to draw the same four characters — measured at
    /// 3.62 KB a frame for StatusHUD alone, which is what a GC pause, a
    /// dropped frame and a visibly jerky sea are made of.
    ///
    /// The key is the DISPLAYED value, not the underlying float: the hull bar
    /// shows whole percent, so it only needs a new string when that integer
    /// moves, not when the float twitches in the sixth decimal.
    ///
    /// Usage:
    ///     if (hullText.Changed(Mathf.RoundToInt(integrity * 100f)))
    ///         hullText.Set($"hull {integrity:P0}");
    ///     GUI.Label(rect, hullText.Content, UITheme.Small);
    public sealed class HudLabel
    {
        public readonly GUIContent Content = new GUIContent(string.Empty);

        long key = long.MinValue;
        bool everSet;
        Vector2 size;
        GUIStyle sizedFor;

        /// True when the value has moved since the last rebuild — and the only
        /// time the caller should pay for formatting a string.
        public bool Changed(long displayedValue)
        {
            if (everSet && displayedValue == key) return false;
            key = displayedValue;
            everSet = true;
            return true;
        }

        public void Set(string text)
        {
            Content.text = text;
            sizedFor = null;      // measured lazily, and only once per string
        }

        /// CalcSize allocates a GUIContent and generates a text mesh of its
        /// own, so it is cached against the string as well.
        public Vector2 Size(GUIStyle style)
        {
            if (sizedFor != style)
            {
                size = style.CalcSize(Content);
                sizedFor = style;
            }
            return size;
        }

        /// Combines several values into one key. Any of them moving rebuilds
        /// the string, which is what you want for a line like
        /// "12.3 m/s   home 5111 m   hold 4/24".
        public static long Key(int a, int b = 0, int c = 0, int d = 0) =>
            ((long)a * 73856093) ^ ((long)b * 19349663) ^ ((long)c * 83492791) ^ ((long)d * 2654435761);
    }
}
