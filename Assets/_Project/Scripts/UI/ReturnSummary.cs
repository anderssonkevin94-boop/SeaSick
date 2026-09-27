using System.Text;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI
{
    /// "While you were gone": a toast that reads back an absence the moment
    /// the ship arrives back at a camp, then gets out of the way.
    ///
    /// Camps run their economy whether or not anyone is standing on them --
    /// that is the whole point of `OutpostLedger.away` -- but a tick nobody
    /// saw is a tick that didn't happen, as far as a player is concerned. So
    /// arrival gets one card, for `ShowFor` seconds or until tapped, that
    /// says what the place did without her.
    public static class ReturnSummary
    {
        /// How long the card stays up before it dismisses itself.
        public const float ShowFor = 14f;

        static readonly HudLabel label = new HudLabel();
        static readonly StringBuilder sb = new StringBuilder();
        static float shownAt;

        static string heightForText;
        static float heightForWidth;
        static float cachedHeight;

        /// Draw the card if a fresh return is waiting, and age it out once
        /// its time is up. Called every OnGUI event; costs nothing when there
        /// is nothing to show.
        public static void Draw(Outpost outpost, double nowSeconds)
        {
            if (outpost == null || outpost.LastReturn == null) return;

            // The clock runs from the first DRAW, not from the arrival.
            // Arrival fires at landing range while she is still under way
            // (`AnchorController.SurveyWhatIsNear`), and the sheet that hosts
            // this card may not be up yet; a card timed from the water's
            // edge could be gone before she has anchored to read it.
            var absence = outpost.LastReturn;
            long key = HudLabel.Key(outpost.GetInstanceID(), (int)(outpost.ReturnedAt * 100f));
            if (label.Changed(key))
            {
                label.Set(Format(absence, nowSeconds, sb));
                shownAt = Time.unscaledTime;
            }

            if (Time.unscaledTime - shownAt > ShowFor)
            {
                outpost.DismissReturn();
                return;
            }

            var style = ToastStyle;
            float width = Mathf.Min(HudLayout.Safe.width - HudLayout.Unit * 2f, HudLayout.Unit * 30f);
            float pad = HudLayout.Pad;
            float height = TextHeight(label.Content.text, width - pad * 2f, style) + HudLayout.Unit * 1.2f;

            var rect = HudLayout.ToastRow(height, width);
            UITheme.ToastCard(rect, UITheme.LedgerIce);   // Ledger toast card, ice: news from the camp
            var inset = new Rect(rect.x + pad, rect.y, rect.width - pad * 2f, rect.height);
            GUI.Label(inset, label.Content, style);

            UIBlocker.Block(rect);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                outpost.DismissReturn();
                Event.current.Use();
            }
        }

        static GUIStyle toastWrap;

        /// `UITheme.Toast` is centred and never wraps -- fine for a one-line
        /// pickup toast, wrong for a card that can carry five lines. Built
        /// once and reused so IMGUI keys its text mesh cache on one instance.
        static GUIStyle ToastStyle
        {
            get
            {
                if (toastWrap == null)
                    toastWrap = new GUIStyle(UITheme.Toast) { wordWrap = true, alignment = TextAnchor.MiddleLeft };
                return toastWrap;
            }
        }

        /// `GUIStyle.CalcSize` ignores wrapping entirely, so the card's
        /// height has to come from `CalcHeight` at the wrapped width instead
        /// -- cached against the string, since it walks the whole layout.
        static float TextHeight(string text, float width, GUIStyle style)
        {
            if (text == heightForText && width == heightForWidth) return cachedHeight;
            heightForText = text;
            heightForWidth = width;
            cachedHeight = style.CalcHeight(new GUIContent(text), width);
            return cachedHeight;
        }

        /// Turns one closed `Absence` into the lines worth reading. Only the
        /// lines that apply appear -- a quiet return says just how long she
        /// was gone.
        public static string Format(OutpostLedger.Absence a, double nowSeconds, StringBuilder sb)
        {
            sb.Clear();
            sb.Append("while you were gone · ").Append(a.DaysAway(nowSeconds).ToString("0.#")).Append(" days");

            AppendGathered(a, sb);
            AppendRaised(a, sb);
            AppendBorn(a, sb);
            AppendRaided(a, sb);

            if (a.eaten >= 1f)
                sb.Append('\n').Append("ate ").Append(Mathf.FloorToInt(a.eaten)).Append(" food");
            if (a.hungryDays > 0.05f)
                sb.Append('\n').Append("went hungry ").Append(a.hungryDays.ToString("0.#")).Append(" days");

            return sb.ToString();
        }

        static void AppendGathered(OutpostLedger.Absence a, StringBuilder sb)
        {
            bool any = false;
            for (int i = 0; i < a.got.Count; i++)
            {
                if (a.got[i] < 1f) continue;
                sb.Append(any ? ", " : "\ngathered ");
                any = true;
                sb.Append(Mathf.FloorToInt(a.got[i])).Append(' ').Append(Lower(a.res[i]));
            }
        }

        /// "raiders took 12 timber, 4 boards" -- the line the watchtower
        /// exists to make unnecessary. Twice: "raided twice — took ...".
        static void AppendRaided(OutpostLedger.Absence a, StringBuilder sb)
        {
            if (a.raids <= 0) return;
            sb.Append('\n');
            if (a.raids == 1) sb.Append("raiders took ");
            else sb.Append("raided ").Append(a.raids).Append(" times — they took ");
            bool any = false;
            for (int i = 0; i < a.raidGot.Count; i++)
            {
                if (a.raidGot[i] <= 0) continue;
                if (any) sb.Append(", ");
                any = true;
                sb.Append(a.raidGot[i]).Append(' ').Append(Lower(a.raidRes[i]));
            }
            if (!any) sb.Append("nothing");
        }

        static void AppendRaised(OutpostLedger.Absence a, StringBuilder sb)
        {
            if (a.raised.Count == 0) return;
            sb.Append('\n').Append("raised ");
            for (int i = 0; i < a.raised.Count; i++)
            {
                if (i > 0) sb.Append(i == a.raised.Count - 1 ? " and " : ", ");
                sb.Append("a ").Append(PlanLabel(a.raised[i]));
            }
        }

        static void AppendBorn(OutpostLedger.Absence a, StringBuilder sb)
        {
            int n = a.born.Count;
            if (n == 0) return;
            sb.Append('\n');
            if (n == 1) sb.Append(a.born[0]).Append(" joined the camp");
            else if (n == 2) sb.Append(a.born[0]).Append(" and ").Append(a.born[1]).Append(" joined the camp");
            else sb.Append(a.born[0]).Append(", ").Append(a.born[1]).Append(" and ").Append(n - 2).Append(" more joined");
        }

        static string PlanLabel(string id)
        {
            var plan = BuildPlans.Named(id);
            return string.IsNullOrEmpty(plan.label) ? id : plan.label;
        }

        /// A resource id in lower case. `CampLoading.Lower` already caches
        /// this for the stores summary, but it is that class's own cache to
        /// own -- this one is small (a handful of ids) and kept local so
        /// `ReturnSummary` doesn't reach into another feature's internals.
        static readonly System.Collections.Generic.Dictionary<string, string> loweredCache
            = new System.Collections.Generic.Dictionary<string, string>();

        static string Lower(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (loweredCache.TryGetValue(s, out string lower)) return lower;
            lower = s.ToLowerInvariant();
            loweredCache[s] = lower;
            return lower;
        }
    }
}
