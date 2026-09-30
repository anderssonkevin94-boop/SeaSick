using UnityEngine;

namespace SeaSick.UI.Sheets
{
    /// **The sea action card's provider API, 2026-09-30 (island UI
    /// restructure, phase 6).**
    ///
    /// Kevin, 2026-09-30, approved mockups "8 · Sea: sailing" / "8c ·
    /// Anchored off a fresh island": at sea there is ONE card above the thumb,
    /// in the Next card's look, with ONE tap action. Whatever can be done
    /// right now (pull a castaway aboard, land here, come alongside, pick up
    /// salvage) OFFERS itself here every frame; the highest priority wins and
    /// `SeaHud` draws it. Nothing offered = no card.
    ///
    /// **Call `Offer` every frame from `Update`** while the action is
    /// available. Offers are cleared frame by frame: the first offer of a new
    /// frame drops the last frame's winner, so an action that stops being
    /// offered is gone one frame later. Ties keep the first offer.
    ///
    /// No allocation: pass cached strings and a cached `System.Action` (a
    /// method-group converted once into a field), not a fresh lambda or an
    /// interpolated string each frame. The card re-texts only when a string
    /// changes. A moving number (a pull, a haul) goes in `progress01`, which
    /// draws as a bar in the card and costs no string.
    ///
    /// `enabled: false` shows the offer as INFORMATION, not a button: no
    /// chevron, no tap, and the detail should say what is needed ("Slow to
    /// under 3 m/s").
    public static class SeaActions
    {
        /// Man overboard, castaways: a life is at stake.
        public const int PriorityRescue = 300;
        /// Land here / come alongside (the anchor's own offer).
        public const int PriorityLand = 200;
        /// Everything else (salvage and the like).
        public const int PriorityOther = 100;

        public struct Offered
        {
            public int priority;
            public string eyebrow, title, detail;
            public System.Action tap;
            public bool enabled;
            /// 0..1 draws a bar in the card; below 0 = no bar.
            public float progress01;
        }

        static Offered best;
        static int bestFrame = -10;

        /// Offer an action for this frame. See the class notes.
        public static void Offer(int priority, string eyebrow, string title, string detail,
            System.Action tap, bool enabled = true, float progress01 = -1f)
        {
            int f = Time.frameCount;
            if (bestFrame != f)
            {
                bestFrame = f;
                best.priority = int.MinValue;
            }
            if (priority <= best.priority) return;
            best.priority = priority;
            best.eyebrow = eyebrow;
            best.title = title;
            best.detail = detail;
            best.tap = tap;
            best.enabled = enabled && tap != null;
            best.progress01 = progress01;
        }

        /// True while an offer from this frame (or the last complete one)
        /// stands. `SeaHud` reads it in `LateUpdate`, after every `Update`.
        public static bool HasOffer => Time.frameCount - bestFrame <= 1 && best.priority != int.MinValue;

        /// The standing offer (valid while `HasOffer`).
        public static Offered Current => best;

        /// True while the card is on screen.
        public static bool Visible { get; internal set; }

        /// The card's rect in screen pixels, GUI space (origin top-left);
        /// zero while hidden. `UIBlocker.SheetBlocked` reads it (via
        /// `SeaHud.Blocks`) so the helm stick ignores a tap on the card.
        public static Rect Rect { get; internal set; }

        /// Perform the standing offer's tap, if it is a button (desktop
        /// keys and the card's own click).
        public static bool Tap()
        {
            if (!HasOffer || !best.enabled || best.tap == null) return false;
            best.tap();
            return true;
        }

        // --- allocation-free text for offerers --------------------------------

        /// A joined string ("Pull " + name + " aboard") rebuilt only when one
        /// of its parts changes (compared by value, so a name read fresh each
        /// frame is fine). Keep one per line of text, as a field.
        public sealed class Joined
        {
            string a, b, c, text = "";
            public string Of(string p0, string p1, string p2 = null)
            {
                if (p0 == a && p1 == b && p2 == c) return text;
                a = p0; b = p1; c = p2;
                text = string.Concat(p0, p1, p2);
                return text;
            }
        }

        static int slowKey = int.MinValue;
        static string slowText = "";

        /// "Slow to under 3 m/s" for a speed gate, cached on the tenth.
        public static string SlowTo(float metresPerSecond)
        {
            int k = Mathf.RoundToInt(metresPerSecond * 10f);
            if (k == slowKey) return slowText;
            slowKey = k;
            slowText = "Slow to under " + (k % 10 == 0 ? (k / 10).ToString() : (k / 10f).ToString("0.0")) + " m/s";
            return slowText;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            best = default;
            best.priority = int.MinValue;
            bestFrame = -10;
            Visible = false;
            Rect = Rect.zero;
        }
    }
}
