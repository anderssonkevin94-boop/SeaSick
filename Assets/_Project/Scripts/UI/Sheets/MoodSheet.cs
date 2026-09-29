using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **Why the camp feels the way it does (top bar, 2026-09-29).** Kevin:
    /// *"just a percentage showing their mood ... pressing on it shows why
    /// they are upset."* Opened from the bar's mood figure.
    ///
    /// Headline: the camp's mood (`CampReadouts.Mood01`) with its word and
    /// which way it is heading. "Why": every term `EatStep` applies to a
    /// hand's mood, counted over the hands it applies to and ordered by
    /// what it costs the camp a day -- the worst first, the missing bonuses
    /// (no bed, a cold bed) next, what is helping last -- each with the fix.
    /// Then a row per unhappy hand (under 95 %); a tap goes to him.
    ///
    /// Not shown: a raid's one-off hit (`RaidMoodHit`) -- the ledger keeps
    /// no time of the last raid, only a count, so "raided recently" cannot
    /// be said truthfully yet.
    public sealed class MoodSheet : ISheetFramed
    {
        readonly Outpost outpost;

        public MoodSheet(Outpost camp) { outpost = camp; }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        // --- frame --------------------------------------------------------------

        public string Title => "Mood";
        public Vector3 AnchorWorld => outpost != null ? outpost.CampCentre : Vector3.zero;
        public bool StillValid => outpost != null && outpost.Ledger != null;
        public Color Accent => MidnightLandHud.Ice;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public bool WantsTallSheet => true;

        Label subtitle;

        public VisualElement BuildHeader() =>
            CampPages.Header("Mood", out subtitle, () => CampPages.OpenLedger(PeopleSheet.OpenLedger, outpost));

        public VisualElement BuildActions() =>
            SheetKit.Actions(SheetKit.Btn("All people", () =>
            {
                if (outpost != null) Sheets.Open(new PeopleSheet(outpost, PeopleSheet.Filter.Unhappy));
            }));

        // --- body ----------------------------------------------------------------

        VisualElement rootEl;
        Label big, small, whyHead, whoHead, emptyWhy, emptyWho;
        ReadoutUi.Lines why;
        ReadoutUi.People who;

        public VisualElement Build()
        {
            rootEl = ReadoutUi.Root(out var col);
            col.Add(ReadoutUi.Headline(out big, out small));

            whyHead = ReadoutUi.Eyebrow(col);
            whyHead.text = "WHY";
            why = new ReadoutUi.Lines(col);
            emptyWhy = CampPages.Classed(new Label(), "cp-note");
            col.Add(emptyWhy);

            whoHead = ReadoutUi.Eyebrow(col);
            who = new ReadoutUi.People(col, name => CampReadouts.GoToHand(outpost, name));
            emptyWho = CampPages.Classed(new Label(), "cp-note");
            col.Add(emptyWho);

            Refresh();
            return rootEl;
        }

        // One term of `EatStep`, summed over the hands it applies to.
        struct Reason
        {
            public string key, text, hint;
            public float perHand;   // mood a sky day for each hand it applies to
            public int n;
            public float Impact => perHand * n;
        }

        readonly Dictionary<string, Reason> reasons = new Dictionary<string, Reason>();
        readonly List<Reason> sorted = new List<Reason>();
        readonly List<int> unhappy = new List<int>();

        void Count(string key, float perHand, string text, string hint)
        {
            if (reasons.TryGetValue(key, out var r)) { r.n++; reasons[key] = r; return; }
            reasons[key] = new Reason { key = key, perHand = perHand, text = text, hint = hint, n = 1 };
        }

        public void Refresh()
        {
            var l = L;
            if (l == null || rootEl == null) return;

            // --- headline ------------------------------------------------------
            float mood = CampReadouts.Mood01(l);
            float trend = CampReadouts.MoodTrendPerDay(l);
            if (mood < 0f)
            {
                ReadoutUi.SetText(big, "--");
                ReadoutUi.SetText(small, "nobody lives here yet");
            }
            else
            {
                ReadoutUi.SetText(big, Mathf.RoundToInt(mood * 100f) + "%");
                string dir = trend > 0.01f ? "rising" : trend < -0.01f ? "falling" : "steady";
                string word = CampReadouts.MoodWordFor(mood);
                ReadoutUi.SetText(small, word + " · " + dir
                    + (Mathf.Abs(trend) > 0.01f ? " " + CampReadouts.SignedPct(trend) + " a day" : ""));
                ReadoutUi.Tone(small, mood < 0.5f ? "bad" : mood < 0.95f ? "warm" : "ok");
            }
            if (subtitle != null)
            {
                int angry = 0;
                foreach (var h in l.hands) if (h != null && !h.downed && h.Angry) angry++;
                ReadoutUi.SetText(subtitle, l.hands.Count == 0 ? "" :
                    angry > 0 ? $"{angry} of {l.hands.Count} angry" : $"{l.hands.Count} hands");
            }

            // --- the reasons -----------------------------------------------------
            reasons.Clear();
            unhappy.Clear();
            int beds = l.HousingCapacity;
            float warmBonus = OutpostLedger.WarmMoodBonusPerDay;
            float drop = OutpostLedger.MoodDropPerHungryDay;
            bool storeEmpty = l.FoodFill() <= 0.01f;
            for (int i = 0; i < l.hands.Count; i++)
            {
                var h = l.hands[i];
                if (h == null) continue;
                if (h.mood < 0.95f || h.pouting) unhappy.Add(i);
                if (h.downed) continue;

                if (l.rations == Rations.None)
                    Count("none", -drop, "on no rations", "set rations to full in the larder");
                else if (h.full <= 0f)
                    Count("empty", -drop, "starving, nothing eaten",
                        storeEmpty ? "the store is empty: fish, hunt or farm" : "food is on its way to them");
                else
                {
                    if (l.rations == Rations.Half)
                        Count("half", -drop * 0.5f, "on half rations", "set rations to full in the larder");
                    else if (h.mood < 1f)
                        Count("fed", OutpostLedger.MoodRecoverPerFedDay, "fed, recovering", null);

                    float bonus = FoodBook.MoodPerDay(h.lastMeal);
                    if (bonus < 0f)
                        Count("meal:" + h.lastMeal, bonus, "last ate raw " + CampReadouts.Label(h.lastMeal),
                            "cook it at the kitchen");
                    else if (bonus > 0f)
                        Count("meal:" + h.lastMeal, bonus, "well fed on " + CampReadouts.Label(h.lastMeal), null);

                    if (h.full < EconomyTuning.HungryBelow && l.BestMeal() == null)
                        Count("hungry", 0f, "getting hungry, nothing to eat",
                            "fish, hunt or farm; unsave a dish in the larder");
                }

                if (l.IsHandWarm(i))
                    Count("warm", warmBonus, "sleep warm by the fire", null);
                else if (i < beds)
                    Count("cold", 0f, "sleep in a cold bed",
                        $"a hut near the fire is warm: {CampReadouts.SignedPct(warmBonus)} a day");
                else
                    Count("nobed", 0f, "have no bed", "build a hut near the fire");

                if (h.pouting) Count("pout", 0f, "pouting at the fire", "they come round as mood rises");
            }

            sorted.Clear();
            foreach (var r in reasons.Values) sorted.Add(r);
            // Costs first (worst first), then the missing bonuses and
            // warnings, then what is helping (best first).
            sorted.Sort((a, b) =>
            {
                int ga = a.Impact < 0f ? 0 : a.Impact == 0f ? 1 : 2;
                int gb = b.Impact < 0f ? 0 : b.Impact == 0f ? 1 : 2;
                if (ga != gb) return ga.CompareTo(gb);
                if (ga == 0) return a.Impact.CompareTo(b.Impact);
                if (ga == 2) return b.Impact.CompareTo(a.Impact);
                return b.n.CompareTo(a.n);
            });

            why.Begin();
            foreach (var r in sorted)
            {
                string val = r.perHand == 0f ? "" : CampReadouts.SignedPct(r.perHand);
                string tone = r.perHand < 0f ? "bad" : r.perHand > 0f ? "ok" : "warm";
                string each = r.perHand == 0f ? "" : " (each, a day)";
                why.Add(val, tone, $"{r.n} {r.text}{each}", r.hint);
            }
            why.End();
            ReadoutUi.SetText(emptyWhy, l.hands.Count == 0 ? "Nobody lives here yet." : "");
            ReadoutUi.Show(emptyWhy, why.Count == 0);

            // --- unhappy hands ----------------------------------------------------
            unhappy.Sort((a, b) => l.hands[a].mood.CompareTo(l.hands[b].mood));
            ReadoutUi.SetText(whoHead, $"UNHAPPY · {unhappy.Count}");
            who.Begin();
            foreach (int i in unhappy)
            {
                var h = l.hands[i];
                int pct = Mathf.RoundToInt(h.mood * 100f);
                who.Add(h.name, pct + "%", h.Angry ? "bad" : "warm", HandReasons(l, i, beds), h.Angry);
            }
            who.End();
            ReadoutUi.SetText(emptyWho, "Nobody is unhappy.");
            ReadoutUi.Show(emptyWho, unhappy.Count == 0 && l.hands.Count > 0);
        }

        /// "starving · no bed", "half rations · cold bed · rising".
        static string HandReasons(OutpostLedger l, int i, int beds)
        {
            var h = l.hands[i];
            var parts = new List<string>(4);
            if (h.downed) parts.Add("down");
            else if (l.rations == Rations.None) parts.Add("no rations");
            else if (h.full <= 0f) parts.Add("starving");
            else
            {
                if (l.rations == Rations.Half) parts.Add("half rations");
                if (FoodBook.MoodPerDay(h.lastMeal) < 0f) parts.Add("ate raw " + CampReadouts.Label(h.lastMeal));
            }
            if (!l.IsHandWarm(i)) parts.Add(i < beds ? "cold bed" : "no bed");
            if (h.pouting) parts.Add("pouting");
            float d = CampReadouts.MoodDriftPerDay(l, i);
            if (!h.downed) parts.Add(d > 0.001f ? "rising" : d < -0.001f ? "falling" : "steady");
            return string.Join(" · ", parts);
        }
    }
}
