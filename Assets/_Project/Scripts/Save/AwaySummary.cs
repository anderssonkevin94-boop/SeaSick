using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using SeaSick.World.Life;
using UnityEngine;

namespace SeaSick.Save
{
    /// **What the away report says, worked out from before and after
    /// (Kevin, 2026-09-30: "tell me what I made, what was gathered and
    /// spent ... raids, if someone died, if someone new joined").** Reads
    /// only -- no hook in the ledger, no new counter in the economy.
    ///
    /// `AwayProgress.Run` takes a `Snapshot` of every camp right after it has
    /// settled to now and before the first chunk, and calls `Fill` once the
    /// last chunk is done. The snapshot holds each resource's total (store +
    /// every station's bay, rack and finished bench + loads in hands' arms:
    /// `CountOf` + `CarriedOf`, the same numbers the Stores sheet shows, so
    /// goods count only where they have been put down), the hand roster and
    /// who was downed, and how long the graveyard was.
    ///
    /// **Gross where the ledger books it, net diff for the rest.** The
    /// catch-up's own `Absence` record already books every unit a gatherer put
    /// down, a bench finished, a hunter brought and a fisher landed (`got`),
    /// so that is the gross GAIN; what it does not book (a farm's harvest) is
    /// picked up from the net diff, so `gain = max(got, net rise)`. Nothing
    /// books what was spent, so `spent = gain - net`. A resource that was both
    /// made and used therefore shows in Made/Gathered AND in Spent/Eaten.
    /// Raw stuff with a gain is Gathered; treated goods and items are Made.
    /// A dish that was used up is Eaten; a raw edible that was used up is
    /// Eaten up to the fill the ledger booked as eaten (`Absence.eaten`), the
    /// rest went into a recipe and is Spent; anything else is Spent.
    ///
    /// **Events** come from the roster and record diffs: hands who appeared,
    /// hands who went down or got up, new `Lives.Graveyard` rows, and the
    /// absence's own raid counters (raids are frozen while the catch-up runs,
    /// so they are 0 today, but the sheet is ready for the day they are not).
    public static class AwaySummary
    {
        /// One resource and how many, for a Made / Gathered / Spent / Eaten row.
        public struct Line
        {
            public string res;
            public int n;
        }

        /// One thing that happened. `tone` 2 bad (ember), 1 warn (amber), 0 good
        /// (moss); `rank` orders the worst first.
        public sealed class Event
        {
            public int tone;
            public int rank;
            public string text;
        }

        public sealed class Snapshot
        {
            public readonly Dictionary<string, int> stock = new Dictionary<string, int>();
            public readonly HashSet<string> hands = new HashSet<string>();
            public readonly HashSet<string> down = new HashSet<string>();
            public int graves;
        }

        /// A camp's totals, roster and the graveyard's length, right now.
        public static Snapshot Take(Outpost o)
        {
            var s = new Snapshot();
            s.graves = Lives.Graveyard.Count;
            var L = o != null ? o.Ledger : null;
            if (L == null) return s;
            foreach (var d in ResDefs.All)
            {
                int n = L.CountOf(d.id) + L.CarriedOf(d.id);
                if (n != 0) s.stock[d.id] = n;
            }
            if (L.hands != null)
                foreach (var h in L.hands)
                {
                    if (h == null || string.IsNullOrEmpty(h.name)) continue;
                    s.hands.Add(h.name);
                    if (h.downed) s.down.Add(h.name);
                }
            return s;
        }

        static int Of(Dictionary<string, int> d, string res) => d.TryGetValue(res, out int n) ? n : 0;

        /// Fills `c.made / gathered / spent / ate / events` from `before`, the
        /// camp as it is now, and the catch-up's absence record. `reported` is
        /// shared across camps so one death is told once.
        public static void Fill(AwayProgress.CampReport c, Snapshot before, Outpost o,
            OutpostLedger.Absence rec, HashSet<string> reported)
        {
            if (c == null || before == null || o == null || o.Ledger == null) return;
            try { FillInner(c, before, o, rec, reported); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        static void FillInner(AwayProgress.CampReport c, Snapshot before, Outpost o,
            OutpostLedger.Absence rec, HashSet<string> reported)
        {
            var L = o.Ledger;
            var after = Take(o);

            // --- goods ------------------------------------------------------
            var gross = new Dictionary<string, int>();
            if (rec != null)
                for (int k = 0; k < rec.res.Count && k < rec.got.Count; k++)
                {
                    int g = Mathf.RoundToInt(rec.got[k]);
                    if (g > 0) gross[rec.res[k]] = Of(gross, rec.res[k]) + g;
                }
            var ids = new HashSet<string>(before.stock.Keys);
            foreach (var k in after.stock.Keys) ids.Add(k);
            foreach (var k in gross.Keys) ids.Add(k);

            var rawEdible = new List<Line>();
            int dishFill100 = 0;   // fill eaten as dishes, in hundredths (no float drift)
            foreach (var id in ids)
            {
                int net = Of(after.stock, id) - Of(before.stock, id);
                int gain = Mathf.Max(Of(gross, id), Mathf.Max(net, 0));
                int use = gain - net;
                if (gain > 0)
                {
                    var line = new Line { res = id, n = gain };
                    if (ResDefs.IsRaw(id)) c.gathered.Add(line); else c.made.Add(line);
                }
                if (use <= 0) continue;
                var edible = FoodBook.Edible(id);
                if (edible == null) c.spent.Add(new Line { res = id, n = use });
                else if (!edible.raw)
                {
                    c.ate.Add(new Line { res = id, n = use });
                    dishFill100 += Mathf.RoundToInt(FoodBook.Fill(id) * use * 100f);
                }
                else rawEdible.Add(new Line { res = id, n = use });
            }
            // What the ledger booked as eaten, less the dishes, was raw food.
            float rawFill = Mathf.Max(0f, (rec != null ? rec.eaten : 0f) - dishFill100 / 100f);
            rawEdible.Sort((a, b) => b.n.CompareTo(a.n));
            foreach (var r in rawEdible)
            {
                float per = Mathf.Max(0.01f, FoodBook.Fill(r.res));
                int eaten = Mathf.Min(r.n, Mathf.RoundToInt(rawFill / per));
                rawFill = Mathf.Max(0f, rawFill - eaten * per);
                if (eaten > 0) c.ate.Add(new Line { res = r.res, n = eaten });
                if (r.n - eaten > 0) c.spent.Add(new Line { res = r.res, n = r.n - eaten });
            }
            Sort(c.made); Sort(c.gathered); Sort(c.spent); Sort(c.ate);

            // --- events -----------------------------------------------------
            var ev = c.events;
            if (rec != null && rec.raids > 0)
            {
                var lost = new System.Text.StringBuilder();
                for (int k = 0; k < rec.raidRes.Count && k < rec.raidGot.Count; k++)
                    if (rec.raidGot[k] > 0)
                        lost.Append(lost.Length > 0 ? ", " : "").Append(ResDefs.Counted(rec.raidRes[k], rec.raidGot[k]));
                string times = rec.raids == 1 ? "Raided" : $"Raided {rec.raids} times";
                if (lost.Length > 0) ev.Add(new Event { tone = 2, rank = 80, text = $"{times}: lost {lost}" });
                else ev.Add(new Event { tone = 0, rank = 20, text = $"{times}: driven off, nothing taken" });
            }

            for (int gi = before.graves; gi < Lives.Graveyard.Count; gi++)
            {
                var g = Lives.Graveyard[gi];
                if (g == null || string.IsNullOrEmpty(g.name)) continue;
                if (!before.hands.Contains(g.name) && g.camp != L.CampLabel) continue;
                if (!reported.Add(g.name)) continue;
                ev.Add(new Event { tone = 2, rank = 100, text = $"{g.name} died{Cause(g.cause)}" });
            }

            foreach (var n in after.hands)
                if (!before.hands.Contains(n) && !reported.Contains(n))
                {
                    reported.Add(n);
                    ev.Add(new Event { tone = 0, rank = 30, text = $"{n} joined the camp" });
                }
            if (rec != null)
                foreach (var n in rec.born)
                    if (!string.IsNullOrEmpty(n) && reported.Add(n))
                        ev.Add(new Event { tone = 0, rank = 30, text = $"{n} joined the camp" });

            foreach (var n in after.down)
                if (!before.down.Contains(n))
                {
                    var h = L.Hand(n);
                    string why = h != null ? Cause(h.downedCause) : "";
                    ev.Add(new Event { tone = 2, rank = 90, text = $"{n} was downed{why}" });
                }
            foreach (var n in before.down)
                if (!after.down.Contains(n) && after.hands.Contains(n))
                    ev.Add(new Event { tone = 0, rank = 25, text = $"{n} is back on their feet" });

            if (c.unhappy > 0 && c.hands > 0)
                ev.Add(new Event { tone = 1, rank = 40,
                    text = c.unhappy == c.hands && c.hands > 1 ? $"All {c.hands} hands are unhappy"
                        : $"{c.unhappy} of {c.hands} hand{(c.hands == 1 ? "" : "s")} unhappy" });

            ev.Sort((a, b) => b.rank.CompareTo(a.rank));
        }

        /// **A voyage's absence as the same report (2026-09-30, island UI
        /// phase 6).** The ship sailing away and back closes an `Absence` on
        /// the camp (`Outpost.LastReturn`); the old IMGUI "while you were
        /// gone" card read it. `AwaySheet` now shows it, so it is cast into
        /// the shape the sheet already draws: one camp, `Report.awaySeconds`
        /// in GAME seconds (the sheet's sub line says days, not minutes).
        /// Only what the absence booked: goods gathered / made (gross `got`),
        /// buildings raised, hands who joined, raids, and food eaten or
        /// missed as events (the ledger keeps fill, not items, for food).
        /// Reads only.
        public static AwayProgress.Report FromAbsence(Outpost o, OutpostLedger.Absence a, double nowSeconds)
        {
            var rep = new AwayProgress.Report();
            if (o == null || a == null) return rep;
            rep.awaySeconds = System.Math.Max(0.0, nowSeconds - a.sinceSeconds);
            rep.simSeconds = rep.awaySeconds;
            var c = new AwayProgress.CampReport
            {
                camp = o,
                name = o.Island == null ? "camp" : o.Island.IsHome ? "Home" : o.Island.gameObject.name,
            };
            rep.camps.Add(c);

            for (int k = 0; k < a.res.Count && k < a.got.Count; k++)
            {
                int n = Mathf.FloorToInt(a.got[k]);
                if (n < 1) continue;
                var line = new Line { res = a.res[k], n = n };
                if (ResDefs.IsRaw(a.res[k])) c.gathered.Add(line); else c.made.Add(line);
            }
            Sort(c.made); Sort(c.gathered);

            foreach (var id in a.raised)
            {
                var p = BuildPlans.Named(id);
                c.built.Add(!string.IsNullOrEmpty(p.label) ? p.label : id);
            }

            var ev = c.events;
            if (a.raids > 0)
            {
                var lost = new System.Text.StringBuilder();
                for (int k = 0; k < a.raidRes.Count && k < a.raidGot.Count; k++)
                    if (a.raidGot[k] > 0)
                        lost.Append(lost.Length > 0 ? ", " : "").Append(ResDefs.Counted(a.raidRes[k], a.raidGot[k]));
                string times = a.raids == 1 ? "Raided" : $"Raided {a.raids} times";
                if (lost.Length > 0) ev.Add(new Event { tone = 2, rank = 80, text = $"{times}: lost {lost}" });
                else ev.Add(new Event { tone = 0, rank = 20, text = $"{times}: nothing taken" });
            }
            foreach (var n in a.born)
                if (!string.IsNullOrEmpty(n))
                    ev.Add(new Event { tone = 0, rank = 30, text = $"{n} joined the camp" });
            if (a.eaten >= 1f)
                ev.Add(new Event { tone = 0, rank = 10, text = $"Ate {Mathf.FloorToInt(a.eaten)} food" });
            if (a.hungryDays > 0.05f)
                ev.Add(new Event { tone = 1, rank = 50, text = $"Went hungry for {a.hungryDays:0.#} days" });
            ev.Sort((x, y) => y.rank.CompareTo(x.rank));
            return rep;
        }

        static void Sort(List<Line> l) => l.Sort((a, b) => b.n.CompareTo(a.n));

        /// " (killed in a raid)" -- or nothing when the cause is unknown.
        static string Cause(string cause)
        {
            switch (cause)
            {
                case LifeEvents.KilledInRaid: return " (killed in a raid)";
                case LifeEvents.LostAtSea: return " (lost at sea)";
                case LifeEvents.HuntingAccident: return " (hunting accident)";
                case LifeEvents.Shipwreck: return " (shipwreck)";
                default: return "";
            }
        }
    }
}
