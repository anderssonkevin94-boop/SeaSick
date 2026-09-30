using System.Text;
using SeaSick.Save;
using SeaSick.UI.Sheets;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI
{
    /// **"While you were gone": what the camp did without her, told the
    /// moment the ship arrives back, then out of the way.**
    ///
    /// Camps run their economy whether or not anyone is standing on them --
    /// that is the whole point of `OutpostLedger.away` -- but a tick nobody
    /// saw is a tick that didn't happen, as far as a player is concerned. So
    /// arrival gets one card for `ShowFor` seconds that says, in a line or
    /// two, what the place did ("raised a hut · Bo joined the camp").
    ///
    /// **UI Toolkit since 2026-09-30 (island UI phase 6).** This class no
    /// longer draws anything. `PartyReportToast` (the game's one toast, under
    /// the chart) asks `Fresh` every frame and shows the card; a tap on it
    /// calls `Open`, which shows the full report on the same `AwaySheet` the
    /// time-away "Welcome back" uses (`AwaySummary.FromAbsence`). The IMGUI
    /// card `CampToasts` used to host is gone.
    ///
    /// The clock runs from the first frame the card is asked for, not from the
    /// arrival: arrival fires at landing range while she is still under way
    /// (`AnchorController.SurveyWhatIsNear`), and a card timed from the
    /// water's edge could be gone before she has anchored to read it.
    public static class ReturnSummary
    {
        /// How long the card stays up before it dismisses itself.
        public const float ShowFor = 12f;

        static readonly StringBuilder sb = new StringBuilder();
        static Outpost shownFor;
        static float shownForAt = -1f;
        static float shownAt;
        static string cachedLabel, cachedLine;
        static bool cachedRaided;

        /// Is a fresh return waiting at this camp? True with the card's label
        /// ("WHILE YOU WERE GONE · 2.3 DAYS"), its one or two line gist, and
        /// whether raiders took something (the card wears the raid rim). Ages
        /// the return out -- and dismisses it on the outpost -- once its time
        /// is up. Costs nothing when there is nothing to show: a steady frame
        /// allocates nothing (the strings are built once per return).
        public static bool Fresh(Outpost outpost, double nowSeconds,
            out string label, out string line, out bool raided)
        {
            label = line = null;
            raided = false;
            if (outpost == null || outpost.LastReturn == null) return false;

            if (!ReferenceEquals(shownFor, outpost) || shownForAt != outpost.ReturnedAt)
            {
                shownFor = outpost;
                shownForAt = outpost.ReturnedAt;
                shownAt = Time.unscaledTime;
                var a = outpost.LastReturn;
                cachedLabel = Label(a, nowSeconds);
                cachedLine = Gist(a, sb);
                cachedRaided = a.raids > 0;
            }

            if (Time.unscaledTime - shownAt > ShowFor)
            {
                outpost.DismissReturn();
                return false;
            }
            label = cachedLabel;
            line = cachedLine;
            raided = cachedRaided;
            return true;
        }

        /// The card was tapped: show the full report on `AwaySheet` and let
        /// the card go. (The report is cast before the return is dismissed;
        /// `Outpost.DismissReturn` nulls it.)
        public static void Open(Outpost outpost, double nowSeconds)
        {
            if (outpost == null) return;
            var a = outpost.LastReturn;
            if (a == null) return;
            var report = AwaySummary.FromAbsence(outpost, a, nowSeconds);
            string sub = "Away " + Days(a.DaysAway(nowSeconds));
            outpost.DismissReturn();
            Sheets.Sheets.Open(new AwaySheet(report, sub));
        }

        /// The player waved the card away (or it aged out elsewhere).
        public static void Dismiss(Outpost outpost)
        {
            if (outpost != null) outpost.DismissReturn();
        }

        /// "2.3 days", "1 day", "less than a day".
        static string Days(float days)
        {
            if (days < 0.95f) return "less than a day";
            string n = days.ToString("0.#");
            return n == "1" ? "1 day" : n + " days";
        }

        static string Label(OutpostLedger.Absence a, double nowSeconds) =>
            "WHILE YOU WERE GONE · " + Days(a.DaysAway(nowSeconds)).ToUpperInvariant();

        /// The one or two lines worth reading, most important first, at most
        /// three parts: what raiders took, what was raised, who joined, what
        /// was gathered (the two biggest), and hunger. A quiet return reads
        /// "the camp kept working".
        public static string Gist(OutpostLedger.Absence a, StringBuilder sb)
        {
            sb.Clear();
            int parts = 0, extra = 0;
            void Part(string s)
            {
                if (parts >= 3) { extra++; return; }
                if (parts > 0) sb.Append(" · ");
                sb.Append(s);
                parts++;
            }

            if (a.raids > 0) Part(Raided(a));
            if (a.raised.Count > 0) Part(Raised(a));
            if (a.born.Count > 0) Part(Born(a));
            string got = Gathered(a);
            if (got != null) Part(got);
            if (a.hungryDays > 0.05f) Part("went hungry");
            else if (a.eaten >= 1f) Part("ate " + Mathf.FloorToInt(a.eaten) + " food");

            if (parts == 0) return "the camp kept working";
            if (extra > 0) sb.Append(" · +").Append(extra).Append(" more");
            return Cap(sb.ToString());
        }

        /// "raiders took 12 timber, 4 boards" -- the line the watchtower
        /// exists to make unnecessary. Twice: "raided 2 times: took ...".
        static string Raided(OutpostLedger.Absence a)
        {
            var t = new StringBuilder();
            t.Append(a.raids == 1 ? "raiders took " : "raided " + a.raids + " times: took ");
            bool any = false;
            for (int i = 0; i < a.raidGot.Count && i < a.raidRes.Count; i++)
            {
                if (a.raidGot[i] <= 0) continue;
                if (any) t.Append(", ");
                any = true;
                t.Append(a.raidGot[i]).Append(' ').Append(Lower(a.raidRes[i]));
            }
            if (!any) t.Append("nothing");
            return t.ToString();
        }

        static string Raised(OutpostLedger.Absence a)
        {
            if (a.raised.Count > 1) return "raised " + a.raised.Count + " buildings";
            string what = PlanLabel(a.raised[0]).ToLowerInvariant();
            return (what.Length > 0 && "aeiou".IndexOf(what[0]) >= 0 ? "raised an " : "raised a ") + what;
        }

        static string Born(OutpostLedger.Absence a)
        {
            int n = a.born.Count;
            if (n == 1) return a.born[0] + " joined the camp";
            if (n == 2) return a.born[0] + " and " + a.born[1] + " joined";
            return n + " new hands joined";
        }

        /// The two biggest hauls: "gathered 12 timber, 7 stone".
        static string Gathered(OutpostLedger.Absence a)
        {
            int b1 = -1, b2 = -1;
            for (int i = 0; i < a.got.Count && i < a.res.Count; i++)
            {
                if (a.got[i] < 1f) continue;
                if (b1 < 0 || a.got[i] > a.got[b1]) { b2 = b1; b1 = i; }
                else if (b2 < 0 || a.got[i] > a.got[b2]) b2 = i;
            }
            if (b1 < 0) return null;
            string s = "gathered " + Mathf.FloorToInt(a.got[b1]) + " " + Lower(a.res[b1]);
            if (b2 >= 0) s += ", " + Mathf.FloorToInt(a.got[b2]) + " " + Lower(a.res[b2]);
            return s;
        }

        static string PlanLabel(string id)
        {
            var plan = BuildPlans.Named(id);
            return string.IsNullOrEmpty(plan.label) ? id : plan.label;
        }

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

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
