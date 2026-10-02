using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SeaSick.Save
{
    /// **Time away (Kevin, 2026-09-27: "it didn't keep building after I closed
    /// the app").** When the app comes back -- a load of a save, or a resume
    /// from the background -- the real time it was gone (clamped 0..12 h) is
    /// played through every camp's OWN ledger, the way an unwatched camp is
    /// always played: `Outpost.CatchUp` against a clock that is walked forward
    /// in quarter-day chunks. Same code path, same 0.02-day quantum, same
    /// invisible walkers, so goods still count only when carried
    /// (docs/DELIVERY-ON-ARRIVAL.md). Buildings the arithmetic finishes are
    /// raised by `CatchUp` between chunks, so a hut done at hour one recruits
    /// for the other eleven.
    /// Supper too (2026-10-02): every quantum carries its own instant, so the
    /// 21:00 bell rings once per sky day away (`OutpostLedger.SupperBellAt`)
    /// and `eaten`/`hungryDays` below are those suppers and their shortfalls.
    ///
    /// **Frozen:** raids and threat (each ledger's absence record is swapped
    /// for a closed one, so the threat block never runs, and the threat is put
    /// back afterwards), the ship and anything at sea (she is cast off during
    /// a load; on a resume `Time.timeScale` is 0 for the catch-up).
    ///
    /// **Cost:** 12 h = 240 game days = 12,000 quanta per camp. Spread over
    /// frames (`FrameBudgetMs` of work per frame), progress in `Progress01`.
    /// If the measured rate projects past `SoftBudgetSeconds` the step widens
    /// to a whole multiple of the quantum (`OutpostLedger.CatchUpStride`, 2
    /// then 5) -- a small, known drift (StationStockSelfTest mixed-step) rather
    /// than a long wait. Past `HardBudgetSeconds` the rest is dropped (each
    /// ledger's `lastTicked` set to the end). Every run logs its wall time.
    public static class AwayProgress
    {
        public const double CapSeconds = 12.0 * 3600.0;
        /// Under this nothing is simulated (a probe's save/restore round trip).
        public const double MinSimSeconds = 30.0;
        /// Under this the card is skipped.
        public const double MinCardSeconds = 120.0;
        const float ChunkDays = 0.25f;

        /// Work per frame: at least this, and up to `MaxFrameBudgetMs` when a
        /// frame's own cost (render, the rest of the game) is high, so the
        /// catch-up is ~3/4 work and ~1/4 frames. The card shows a progress
        /// line; 10-15 fps on it is not a hitch anyone sees.
        public static float FrameBudgetMs = 33f;
        public static float MaxFrameBudgetMs = 100f;
        public static float SoftBudgetSeconds = 8f;
        public static float HardBudgetSeconds = 40f;
        /// A frame that takes longer than this between two slices of work is
        /// a stall, not waiting (the app suspended again, a drawable that
        /// never came): only this much of it counts toward the budgets.
        public const float MaxFrameGapMs = 250f;

        public static bool Running { get; private set; }
        public static float Progress01 { get; private set; }
        /// The last finished catch-up, for the card and the dev launcher.
        public static Report Last { get; private set; }
        /// The card should come up once nothing covers the screen.
        public static bool CardPending;

        static System.DateTime pausedAtUtc;

        /// Set by a load's catch-up; `SaveGame.Restore` writes the autosave
        /// once the ship is back at her mooring (`FlushSave`).
        public static bool SavePending;

        /// Domain reload is off: statics outlive a stopped Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Running = false; Progress01 = 0f; Last = null;
            CardPending = false; SavePending = false; pausedAtUtc = default;
            OutpostLedger.CatchUpStride = 1;
            CampPath.NoBuildForRoutes = false;
        }

        public static void FlushSave()
        {
            if (!SavePending) return;
            SavePending = false;
            if (!SaveGame.Suppressed) SaveSlots.WriteAutosave("away");
        }

        public class CampReport
        {
            public Outpost camp;
            public string name;
            public List<string> built = new List<string>();
            public List<string> born = new List<string>();
            public List<KeyValuePair<string, float>> got = new List<KeyValuePair<string, float>>();
            public float eaten, hungryDays, stalledDays;
            public string stall;
            public int hands, unhappy;
            /// **The report's sections (2026-09-30, `AwaySummary`).** Gross
            /// where the ledger books it, else the net diff: made products,
            /// gathered raw, spent, eaten -- each one row per resource -- and
            /// what happened (raids, deaths, joined, downed), worst first.
            public List<AwaySummary.Line> made = new List<AwaySummary.Line>();
            public List<AwaySummary.Line> gathered = new List<AwaySummary.Line>();
            public List<AwaySummary.Line> spent = new List<AwaySummary.Line>();
            public List<AwaySummary.Line> ate = new List<AwaySummary.Line>();
            public List<AwaySummary.Event> events = new List<AwaySummary.Event>();
        }

        public class Report
        {
            public double awaySeconds, simSeconds;
            public bool capped, dropped;
            public float wallSeconds;
            /// Main-thread work only (between frames), and frames it spread over.
            public float cpuMs;
            public int frames;
            /// The slowest single chunk (every camp once), for the log.
            public float maxChunkMs;
            /// Setup (settle + hide bodies) and bodies-back, inside `cpuMs`.
            public float setupMs, finishMs;
            /// Frame gaps past `MaxFrameGapMs`, left out of the budgets.
            public float stalledSeconds;
            /// The card's footer: honest about where the time went.
            public string Timing =>
                $"{cpuMs:0} ms work over {frames} frame{(frames == 1 ? "" : "s")}"
                + (stalledSeconds >= 1f ? $", {wallSeconds:0.0} s wall" : "")
                + (stride > 1 ? $", step ×{stride}" : "")
                + (dropped ? ", cut short" : "");
            public long steps;
            public int stride = 1;
            public List<CampReport> camps = new List<CampReport>();
            public bool Anything
            {
                get
                {
                    foreach (var c in camps)
                        if (c.built.Count > 0 || c.got.Count > 0 || c.eaten > 0f || c.hungryDays > 0f || c.stall != null
                            || c.made.Count > 0 || c.gathered.Count > 0 || c.spent.Count > 0 || c.ate.Count > 0 || c.events.Count > 0) return true;
                    return false;
                }
            }
        }

        // --- entry points --------------------------------------------------------

        /// Real seconds since `savedAtUtcTicks`: negative (clock set back) is
        /// 0, more than 12 h is 12 h. 0 for a save written before the field.
        public static double RealAway(long savedAtUtcTicks, out bool capped)
        {
            capped = false;
            if (savedAtUtcTicks <= 0) return 0.0;
            double s = (System.DateTime.UtcNow - new System.DateTime(savedAtUtcTicks, System.DateTimeKind.Utc)).TotalSeconds;
            if (double.IsNaN(s) || s < 0.0) return 0.0;
            if (s > CapSeconds) { capped = true; return CapSeconds; }
            return s;
        }

        /// From `SaveGame.Restore`, after the camps are back and before the
        /// anchor step (so no camp is watched yet). Saves afterwards.
        public static IEnumerator FromSave(SaveData data)
        {
            double away = RealAway(data != null ? data.savedAtUtcTicks : 0, out bool capped);
            if (away < MinSimSeconds) yield break;
            yield return Run(away, capped, "load", true, true);
        }

        /// `GameBoot.OnApplicationPause(true)`.
        public static void NotePaused() => pausedAtUtc = System.DateTime.UtcNow;

        /// `GameBoot.OnApplicationPause(false)`: the app was suspended, not
        /// killed; the world is live, so the game is frozen for the catch-up.
        public static void Resumed(MonoBehaviour host)
        {
            if (host == null || Running || pausedAtUtc == default) return;
            double away = RealAway(pausedAtUtc.Ticks, out bool capped);
            pausedAtUtc = default;
            if (away < MinSimSeconds || !GameBoot.Decided || SaveGame.Restoring) return;
            host.StartCoroutine(ResumeRun(away, capped));
        }

        static IEnumerator ResumeRun(double away, bool capped)
        {
            float scale = Time.timeScale;
            Time.timeScale = 0f;
            try
            {
                // `Run` opens the card itself so the progress shows on it.
                yield return Run(away, capped, "resume", true, true);
            }
            finally { Time.timeScale = scale > 0f ? scale : 1f; }
        }

        /// **Dev launcher** (`RunProbe.AwayHours(h)`): play `hours` of time
        /// away through the live game right now, print wall time and the
        /// per-camp summary, and write the result to a scratch save (never the
        /// player's own slots). Shows the card too.
        public static void Simulate(float hours)
        {
            if (!Application.isPlaying) { Debug.LogWarning("AwayProgress: enter Play first"); return; }
            if (Running) { Debug.LogWarning("AwayProgress: already running"); return; }
            double s = Mathf.Max(0f, hours) * 3600.0;
            bool capped = s > CapSeconds;
            if (capped) s = CapSeconds;
            AwayProgressRunner.Get().StartCoroutine(SimulateRun(s, capped));
        }

        static IEnumerator SimulateRun(double s, bool capped)
        {
            yield return Run(s, capped, "dev", false, true);
            string path = System.IO.Path.Combine(SaveSlots.DirectoryOverride ?? Application.persistentDataPath, "away-scratch.json");
            SaveGame.SaveTo(path, "away-probe");
            Debug.Log("AwayProgress: scratch save -> " + path);
        }

        // --- the run -------------------------------------------------------------

        static IEnumerator Run(double awaySeconds, bool capped, string why, bool save, bool card)
        {
            if (Running) yield break;
            Running = true;
            Progress01 = 0f;
            var rep = new Report { awaySeconds = awaySeconds, capped = capped };
            if (card && awaySeconds >= MinCardSeconds && why == "resume")
                UI.Sheets.Sheets.Open(new UI.Sheets.AwaySheet());

            // `wall` is what the player waited, `cpu` is main-thread work
            // only. The budgets run on wall MINUS stalls (`MaxFrameGapMs`):
            // on the phone the one "cut short" 4-minute resume (2026-09-27:
            // 53.6 s for ~100 ms of work) was time the coroutine was not
            // being given frames, and it must never drop real progress.
            var wall = Stopwatch.StartNew();
            var cpu = Stopwatch.StartNew();
            var gap = new Stopwatch();
            double stalledMs = 0.0;
            int frames = 1;
            float budgetMs = FrameBudgetMs;
            long steps0 = OutpostLedger.StepsRun;
            double start = TimeOfDay.Seconds;
            double target = start + awaySeconds * System.Math.Max(0.0, TimeOfDay.Scale);
            float day = TimeOfDay.WorkDaySeconds;   // the ledger's day: stalledDays are work days (AwaySheet)
            double chunk = ChunkDays * day;

            var camps = new List<Outpost>();
            var watched = new List<Outpost>();
            var prevAway = new List<OutpostLedger.Absence>();
            var snaps = new List<AwaySummary.Snapshot>();
            var told = new HashSet<string>();   // names already in an event, so a death is told once
            var threat0 = new List<float>();
            foreach (var o in Outpost.All)
            {
                if (o == null || o.Ledger == null) continue;
                o.CatchUp();   // settle to now and bind router/ceiling first
                if (o.Watched) { watched.Add(o); o.ShowHands(false); }
                camps.Add(o);
                snaps.Add(AwaySummary.Take(o));   // the report is this against what the camp holds after
                var L = o.Ledger;
                prevAway.Add(L.away);
                threat0.Add(L.threat);
                L.away = new OutpostLedger.Absence();   // closed: raids cannot bank
                rep.camps.Add(new CampReport { camp = o, name = CampName(o) });
            }

            rep.setupMs = (float)cpu.Elapsed.TotalMilliseconds;
            OutpostLedger.CatchUpStride = 1;
            // An invisible walker's leg is measured on a grid that is up, or
            // is the straight line (as when the frame's plan budget is spent):
            // a camp whose grid was thrown away (a load's `Adopt`) would
            // otherwise rebuild it inside a chunk -- 1 s in the editor, several
            // on the phone, measured 2026-09-27.
            CampPath.NoBuildForRoutes = true;
            try
            {
                double now = start;
                double rateFrom = start; float rateW = 0f;
                var frame = Stopwatch.StartNew();
                while (now < target)
                {
                    double chunk0 = cpu.Elapsed.TotalMilliseconds;
                    double next = System.Math.Min(target, now + chunk);
                    TimeOfDay.Scrub(next);
                    float chunkDays = (float)((next - now) / day);
                    for (int i = 0; i < camps.Count; i++)
                    {
                        var o = camps[i];
                        if (o == null || o.Ledger == null) continue;
                        o.CatchUp();
                        string stall = o.Ledger.SiteShortfall();
                        if (stall != null) { rep.camps[i].stalledDays += chunkDays; rep.camps[i].stall = stall; }
                    }
                    now = next;
                    rep.maxChunkMs = Mathf.Max(rep.maxChunkMs, (float)(cpu.Elapsed.TotalMilliseconds - chunk0));
                    Progress01 = (float)((now - start) / System.Math.Max(1e-6, target - start));

                    float w = (float)((wall.Elapsed.TotalMilliseconds - stalledMs) / 1000.0);
                    if (w > HardBudgetSeconds && now < target)
                    {
                        // Out of time: the rest is dropped, not banked.
                        foreach (var o in camps) if (o != null && o.Ledger != null) o.Ledger.lastTicked = target;
                        rep.dropped = true;
                        break;
                    }
                    // Project the rest at the rate measured since the last
                    // stride change, over at least two game days of work:
                    // one slow first chunk (a settle, a GC) is not a rate.
                    double done = now - rateFrom;
                    if (done >= 8.0 * chunk && OutpostLedger.CatchUpStride < 5)
                    {
                        double projected = w + (w - rateW) / done * (target - now);
                        if (projected > SoftBudgetSeconds)
                        {
                            OutpostLedger.CatchUpStride = OutpostLedger.CatchUpStride < 2 ? 2 : 5;
                            rep.stride = OutpostLedger.CatchUpStride;
                            rateFrom = now; rateW = w;
                        }
                    }
                    if (frame.Elapsed.TotalMilliseconds > budgetMs)
                    {
                        cpu.Stop();
                        gap.Restart();
                        yield return null;
                        double g = gap.Elapsed.TotalMilliseconds;
                        if (g > MaxFrameGapMs) stalledMs += g - MaxFrameGapMs;
                        budgetMs = Mathf.Clamp(3f * Mathf.Min((float)g, MaxFrameGapMs), FrameBudgetMs, MaxFrameBudgetMs);
                        cpu.Start();
                        frames++;
                        frame.Restart();
                    }
                }
                rep.simSeconds = now - start;
            }
            finally
            {
                OutpostLedger.CatchUpStride = 1;
                CampPath.NoBuildForRoutes = false;
                TimeOfDay.Scrub(target);
                for (int i = 0; i < camps.Count; i++)
                {
                    var o = camps[i];
                    if (o == null || o.Ledger == null) continue;
                    var L = o.Ledger;
                    var rec = L.away;
                    Fill(rep.camps[i], rec, L);
                    AwaySummary.Fill(rep.camps[i], snaps[i], o, rec, told);
                    L.threat = threat0[i];
                    var prev = prevAway[i];
                    if (prev != null && prev.Open) Merge(prev, rec);
                    L.away = prev ?? new OutpostLedger.Absence();
                }
                rep.frames = frames;
                rep.stalledSeconds = (float)(stalledMs / 1000.0);
                rep.steps = OutpostLedger.StepsRun - steps0;
                Running = false;
                Progress01 = 1f;
                Last = rep;
            }

            // Bodies back on the camps that were being watched; their own
            // "while you were gone" card would repeat this one.
            double fin0 = cpu.Elapsed.TotalMilliseconds;
            foreach (var o in watched)
            {
                if (o == null) continue;
                o.CatchUp();
                o.ShowHands(true);
                o.DismissReturn();
            }
            wall.Stop();
            cpu.Stop();
            rep.finishMs = (float)(cpu.Elapsed.TotalMilliseconds - fin0);
            rep.cpuMs = (float)cpu.Elapsed.TotalMilliseconds;
            rep.wallSeconds = (float)wall.Elapsed.TotalSeconds;

            Debug.Log(Describe(rep, why));
            // A load saves once the restore has finished (the ship is not
            // anchored yet at this point); a resume saves now.
            if (save && why == "load") SavePending = true;
            else if (save && !SaveGame.Suppressed) SaveSlots.WriteAutosave("away");
            if (card && awaySeconds >= MinCardSeconds) CardPending = true;
            if (why == "resume") CardPending = false;   // its card is already open
        }

        static string CampName(Outpost o)
        {
            if (o == null || o.Island == null) return "camp";
            return o.Island.IsHome ? "Home" : o.Island.gameObject.name;
        }

        static void Fill(CampReport c, OutpostLedger.Absence rec, OutpostLedger L)
        {
            if (rec != null)
            {
                foreach (var id in rec.raised)
                {
                    var p = BuildPlans.Named(id);
                    c.built.Add(!string.IsNullOrEmpty(p.label) ? p.label : id);
                }
                c.born.AddRange(rec.born);
                for (int k = 0; k < rec.res.Count && k < rec.got.Count; k++)
                    if (rec.got[k] >= 1f) c.got.Add(new KeyValuePair<string, float>(rec.res[k], rec.got[k]));
                c.got.Sort((a, b) => b.Value.CompareTo(a.Value));
                c.eaten = rec.eaten;
                c.hungryDays = rec.hungryDays;
            }
            if (L.SiteShortfall() == null && c.stalledDays < ChunkDays * 1.5f) { c.stall = null; c.stalledDays = 0f; }
            c.hands = 0; c.unhappy = 0;
            foreach (var h in L.hands)
            {
                if (h == null) continue;
                c.hands++;
                if (h.mood < 0.35f) c.unhappy++;
            }
        }

        static void Merge(OutpostLedger.Absence into, OutpostLedger.Absence rec)
        {
            if (into == null || rec == null) return;
            for (int k = 0; k < rec.res.Count && k < rec.got.Count; k++) into.Add(rec.res[k], rec.got[k]);
            into.eaten += rec.eaten;
            into.hungryDays += rec.hungryDays;
            into.raised.AddRange(rec.raised);
            into.born.AddRange(rec.born);
        }

        public static string Span(double seconds)
        {
            int m = Mathf.Max(0, (int)(seconds / 60.0));
            return m >= 60 ? $"{m / 60} h {m % 60:00} m" : $"{m} m";
        }

        public static string Describe(Report r, string why)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"AwayProgress ({why}): {Span(r.awaySeconds)} away{(r.capped ? " (capped at 12 h)" : "")}, ")
              .Append($"{r.simSeconds / Mathf.Max(1f, TimeOfDay.DayLength):0.#} game days in {r.wallSeconds:0.00} s wall ({r.cpuMs:0} ms CPU over {r.frames} frames: setup {r.setupMs:0}, worst chunk {r.maxChunkMs:0.0}, bodies back {r.finishMs:0}; stalled {r.stalledSeconds:0.0} s), ")
              .Append($"{r.steps} ledger steps, stride {r.stride}{(r.dropped ? ", REST DROPPED (hard budget)" : "")}");
            foreach (var c in r.camps)
            {
                sb.Append($"\n  {c.name}: {c.hands} hands, built [{string.Join(", ", c.built)}], got [");
                for (int k = 0; k < c.got.Count; k++)
                    sb.Append(k > 0 ? ", " : "").Append($"{c.got[k].Key} {c.got[k].Value:0}");
                sb.Append($"], eaten {c.eaten:0.#}, hungry {c.hungryDays:0.##} d, unhappy {c.unhappy}");
                if (c.stall != null) sb.Append($", stalled {c.stalledDays:0.#} d: {c.stall}");
            }
            return sb.ToString();
        }
    }
}
