using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SeaSick.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.Dev
{
    /// **Watches the camp's walkers for walk-in / step-back loops** (2026-10-04,
    /// Kevin: the kitchen runner "walks into this, steps back and walks in
    /// again"). Play mode, at a camp, nothing borrowed: the villagers live
    /// their own day while it watches. Every 0.25 s of player loop it reads
    /// each body's walk goal (`pinFor`, the pin detector's copy of what `Walk` was
    /// asked for) and its pin-recovery state (`CampWorker`'s
    /// `pinFails` / `backtrack` / `escapeLeft`, by reflection) and counts a
    /// **pin event** each time a recovery starts. A **loop** is the same
    /// target (within 0.5 m) taking 3 or more pin events before the walker
    /// gets there or moves on. Read-only: it changes nothing.
    ///
    /// `Start(seconds)` -- "queued"; `Report()` -- "watching… N s" while
    /// busy, then the totals, every loop (who, job, target, where he got
    /// stuck, the nearest building and marker), and every runner's last
    /// target; `Stop()`.
    public static class WalkLoopWatch
    {
        const float Sample = 0.25f;
        const int LoopEvents = 3;

        static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly FieldInfo fTarget = typeof(CampWorker).GetField("pinFor", F);
        static readonly FieldInfo fPinFails = typeof(CampWorker).GetField("pinFails", F);
        static readonly FieldInfo fBacktrack = typeof(CampWorker).GetField("backtrack", F);
        static readonly FieldInfo fEscape = typeof(CampWorker).GetField("escapeLeft", F);
        static readonly FieldInfo fCamp = typeof(CampWorker).GetField("camp", F);
        static readonly PropertyInfo pRow = typeof(CampWorker).GetProperty("Row", F);

        class Track
        {
            public string who, job;
            public Vector3 target, lastPos;
            public float since;
            public int events, lastFails, reversals;
            public float minDist, lastDist, longest;
            public bool closing, revFlagged, pressFlagged;
            public float pressedFor;
            public bool lastBack, lastEsc, flagged;
            public readonly List<Vector3> stuckAt = new List<Vector3>();
        }

        static readonly Dictionary<CampWorker, Track> tracks = new Dictionary<CampWorker, Track>();
        static readonly List<string> loops = new List<string>();
        static float until, next, started;
        static int pinEvents, targetsSeen, reversalsTotal, pressedTotal;
        static bool running;

        public static string Start(float seconds)
        {
            if (!Application.isPlaying) return "FAIL: play mode only";
            Stop();
            tracks.Clear(); loops.Clear();
            pinEvents = 0; targetsSeen = 0; reversalsTotal = 0; pressedTotal = 0;
            started = -1f; until = seconds;
            running = true;
            InputSystem.onAfterUpdate += Tick;
            return "queued: watching " + seconds.ToString("F0") + " s of game time";
        }

        public static string Stop()
        {
            InputSystem.onAfterUpdate -= Tick;
            bool was = running;
            running = false;
            return was ? "stopped" : "not running";
        }

        static void Tick()
        {
            if (!Application.isPlaying) { Stop(); return; }
            if (started < 0f) { started = Time.time; until += Time.time; next = Time.time; }
            if (Time.time < next) return;
            next = Time.time + Sample;
            if (Time.time >= until) { Stop(); return; }
            foreach (var w in CampWorker.Bodies)
            {
                if (w == null || !w.isActiveAndEnabled) continue;
                if (!tracks.TryGetValue(w, out var t)) { t = new Track(); tracks[w] = t; }
                var row = pRow.GetValue(w) as OutpostHand;
                t.who = row != null ? row.name : w.name;
                t.job = row == null ? "?" : OutpostLedger.IsRunner(row) ? "runner" : string.IsNullOrEmpty(row.target) ? "idle" : row.target;
                Vector3 to = (Vector3)fTarget.GetValue(w);
                Vector3 here = w.transform.position;
                Vector3 prevPos = t.lastPos;
                if (Flat(to - t.target) > 0.5f)
                {
                    t.target = to; t.since = Time.time; t.events = 0; t.flagged = false; t.stuckAt.Clear();
                    t.reversals = 0; t.revFlagged = false; t.pressFlagged = false; t.pressedFor = 0f; t.minDist = t.lastDist = Flat(to - here); t.closing = true;
                    targetsSeen++;
                }
                // Walk in / step back, whatever drives it (pin recovery, body
                // avoidance, a hand-off re-walk): the distance to the same goal
                // shrinking, then growing again by more than 0.3 m.
                float dist = Flat(to - here);
                if (dist < t.minDist) t.minDist = dist;
                if (t.closing && dist > t.minDist + 0.3f && t.minDist > 0.36f && t.minDist < 2.5f)
                {
                    t.closing = false; t.reversals++; reversalsTotal++;
                    if (t.stuckAt.Count < 6) t.stuckAt.Add(here);
                    if (t.reversals >= LoopEvents && !t.revFlagged)
                    {
                        t.revFlagged = true;
                        loops.Add($"OSCILLATION {t.who} ({t.job}) -> target {V(t.target)} {Near(w, t.target)}: {t.reversals} walk-in/step-back cycles in {Time.time - t.since:F1} s, closest {t.minDist:F2} m, turned back at {Points(t.stuckAt)}");
                    }
                }
                else if (!t.closing && dist < t.lastDist - 0.05f) { t.closing = true; t.minDist = dist; }
                // Pressed into a building: against a box, barely moving, goal
                // not reached, for 1.5 s.
                var camp = fCamp.GetValue(w) as Outpost;
                bool moved = Flat(here - prevPos) > 0.1f;
                t.lastPos = here;
                if (camp != null && dist > 0.36f && !moved && CampPath.SolidDistance(camp, here) < 0.2f) t.pressedFor += Sample;
                else t.pressedFor = 0f;
                if (t.pressedFor >= 1.5f && !t.pressFlagged)
                {
                    t.pressFlagged = true; pressedTotal++;
                    loops.Add($"PRESSED {t.who} ({t.job}) -> target {V(t.target)} {Near(w, t.target)}: against a box at {V(here)} for 1.5 s, {dist:F2} m short");
                }
                t.lastDist = dist;
                if (dist > 0.36f) t.longest = Mathf.Max(t.longest, Time.time - t.since);
                int fails = (int)fPinFails.GetValue(w);
                bool back = (bool)fBacktrack.GetValue(w);
                bool esc = (float)fEscape.GetValue(w) > 0f;
                bool began = fails > t.lastFails || (back && !t.lastBack) || (esc && !t.lastEsc);
                t.lastFails = fails; t.lastBack = back; t.lastEsc = esc;
                if (!began) continue;
                pinEvents++;
                t.events++;
                if (t.stuckAt.Count < 6) t.stuckAt.Add(here);
                if (t.events >= LoopEvents && !t.flagged)
                {
                    t.flagged = true;
                    loops.Add($"LOOP {t.who} ({t.job}) -> target {V(t.target)} {Near(w, t.target)}: {t.events} pin events in {Time.time - t.since:F1} s, stuck at {Points(t.stuckAt)}");
                }
            }
        }

        public static string Report()
        {
            var sb = new StringBuilder();
            if (running && started >= 0f) sb.AppendLine($"watching… {Time.time - started:F0} / {until - started:F0} s");
            else if (running) sb.AppendLine("queued");
            sb.AppendLine($"{(loops.Count == 0 ? "PASS" : "FAIL")} walk loops: {loops.Count}; pin events {pinEvents}; near-goal step-backs {reversalsTotal}; pressed {pressedTotal}; targets {targetsSeen}; bodies {tracks.Count}");
            foreach (var l in loops) sb.AppendLine("  " + l);
            foreach (var kv in tracks)
                if (kv.Value.job == "runner")
                    sb.AppendLine($"  runner {kv.Value.who}: target {V(kv.Value.target)} {Near(kv.Key, kv.Value.target)}, at {V(kv.Value.lastPos)}, pin events on it {kv.Value.events}");
            return sb.ToString();
        }

        /// The building nearest the target and its nearest named marker.
        static string Near(CampWorker w, Vector3 p)
        {
            Building best = null; float bd = float.MaxValue;
            foreach (var b in Object.FindObjectsByType<Building>(FindObjectsSortMode.None))
            {
                float d = Flat(b.transform.position - p);
                if (d < bd) { bd = d; best = b; }
            }
            if (best == null) return "";
            string mk = ""; float md = float.MaxValue;
            foreach (var t in best.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.Contains("_")) continue;
                float d = Flat(t.position - p);
                if (d < md) { md = d; mk = t.name; }
            }
            return $"[{best.Id}@{V(best.transform.position)} {bd:F1} m; nearest marker {mk} {md:F2} m]";
        }

        static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }
        static string V(Vector3 p) => $"({p.x:F1},{p.z:F1})";
        static string Points(List<Vector3> ps)
        {
            var sb = new StringBuilder();
            foreach (var p in ps) sb.Append(V(p)).Append(' ');
            return sb.ToString().TrimEnd();
        }
    }
}
