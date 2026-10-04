#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using SeaSick.CameraRig;
using SeaSick.Combat;
using SeaSick.Ship;
using SeaSick.Ship.Harpoon;
using SeaSick.Ship.Overboard;
using SeaSick.UI;
using SeaSick.UI.Sheets;
using SeaSick.Voyage;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **The kraken + harpoon sweep** (2026-10-04). A dev check, NOT game
    /// code: nothing references it. Play mode, at sea, under way, sea HUD up.
    /// Each check is a job that runs INSIDE the player loop (a hidden runner
    /// object), takes real game time, cleans up after itself and ends in a
    /// report of PASS / FAIL lines with the measured numbers.
    ///
    /// * `Start("arm_hit")` / `Start("arm_hit", "/tmp/shots")` -- queue one
    ///   check (returns "queued"); the optional dir receives the screenshots.
    /// * `All(dir)` -- every check in turn, one combined report.
    /// * `Report()` -- "running… <check>" while busy, else the last report
    ///   (first line PASS or FAIL).
    /// * `Stop()` -- stop now, undo everything the check changed.
    ///
    /// The shape checks (chevron, settings, ship_height) run at whatever
    /// shape the Game view has; switch it (`RunProbe.ViewPhone()` /
    /// `ViewDesk()`) and run them again for the other one. docs/DEV-TOOLS.md
    /// "Kraken + harpoon sweep" has the calls, PASS meanings and traps.
    public static class SweepCheck
    {
        public static readonly string[] Checks =
        {
            "arm_hit", "warning", "chevron", "settings", "ship_height",
            "loot_size", "hook_loot", "hook_wreck", "miss", "smoothness",
        };

        static readonly HashSet<string> ShapeChecks = new HashSet<string> { "chevron", "settings", "ship_height" };

        static Runner inst;
        static string lastReport = "no sweep run yet";

        public static string Start(string name, string dir = null)
        {
            if (!Application.isPlaying) return "not in play mode";
            if (inst != null && inst.Busy) return "busy: " + inst.Current;
            name = (name ?? "").Trim().ToLowerInvariant();
            if (Array.IndexOf(Checks, name) < 0)
                return "unknown check '" + name + "'; one of: " + string.Join(", ", Checks);
            Ensure().Queue(new List<string> { name }, dir, false);
            return "queued";
        }

        public static string All(string dir = null)
        {
            if (!Application.isPlaying) return "not in play mode";
            if (inst != null && inst.Busy) return "busy: " + inst.Current;
            Ensure().Queue(new List<string>(Checks), dir, true);
            return "queued";
        }

        public static string Report()
        {
            if (inst != null && inst.Busy) return inst.Progress();
            return lastReport;
        }

        public static string Stop()
        {
            if (inst == null || !inst.Busy) return "nothing running";
            inst.Halt();
            return "stopped";
        }

        static Runner Ensure()
        {
            if (inst != null) return inst;
            var go = new GameObject("SweepCheck (dev)") { hideFlags = HideFlags.HideInHierarchy };
            inst = go.AddComponent<Runner>();
            return inst;
        }

        // ================================================================
        /// The player-loop half: queued from an eval, started in `Update`
        /// (where `Screen` is the Game view's), driven by a coroutine that
        /// catches exceptions per step so a throw still runs the undo list.
        sealed class Runner : MonoBehaviour
        {
            const float FastScale = 2f;
            static readonly WaitForEndOfFrame Eof = new WaitForEndOfFrame();

            List<string> pending;
            string dir;
            bool sweep;
            bool busy;
            string current = "";
            float startedAt;
            Coroutine job;

            readonly List<Action> undo = new List<Action>();
            StringBuilder sb = new StringBuilder();
            readonly StringBuilder all = new StringBuilder();
            int pass, fail, sweepPass, sweepFail;
            readonly List<string> verdicts = new List<string>();
            float timeScale0 = 1f;
            bool ok;

            // world
            ShipMotor motor;
            VoyageManager voyage;
            ChaseCamera chase;
            Camera cam;
            Kraken kraken;

            // the swat guard: no slam ever reaches the ship during a check
            bool guardSwats;
            // A warning this check started, or a dev kraken it left rising:
            // dismissed the moment it is up, guarded until then, even after
            // the check (or a Stop) has ended.
            bool startedWarning;
            bool cleanupPending;
            readonly float[] windStart = NewWind();
            Kraken guardOf;
            int guardCancels;

            // smoothness sampling
            Vector3 prevPos;
            float prevYaw;
            bool prevValid;

            public bool Busy => busy || pending != null;
            public string Current => current;

            static float[] NewWind()
            {
                var w = new float[KrakenArms.ArmCount];
                for (int i = 0; i < w.Length; i++) w[i] = -1f;
                return w;
            }

            public void Queue(List<string> names, string shotDir, bool isSweep)
            {
                pending = names;
                dir = string.IsNullOrEmpty(shotDir) ? null : shotDir;
                sweep = isSweep;
                current = names.Count > 0 ? names[0] : "";
            }

            public string Progress()
            {
                return "running… " + current + " (" + (Time.realtimeSinceStartup - startedAt).ToString("F0")
                       + " s)\n" + all + sb;
            }

            public void Halt()
            {
                StopAllCoroutines();
                job = null;
                Line("STOPPED by SweepCheck.Stop()");
                fail++;
                EndCheck();
                Finish();
            }

            void Update()
            {
                if (pending == null || busy) return;
                var names = pending;
                pending = null;
                busy = true;
                startedAt = Time.realtimeSinceStartup;
                all.Length = 0;
                verdicts.Clear();
                sweepPass = sweepFail = 0;
                job = StartCoroutine(Sweep(names));
            }

            /// The guard: a windup still up `windupSeconds - 0.25` s after it
            /// began is called off (`KrakenSwat.TryInterrupt`), so no check
            /// ever lands a slam on the ship (hull, knockdown, a hand over the
            /// side). Runs after every Update, so a check's own reading of a
            /// ball's hit in the same frame comes first.
            void LateUpdate()
            {
                if (cleanupPending)
                {
                    var up = Kraken.Active;
                    if (up != null && up.DevSummoned && !up.Retreating) up.Dismiss();
                    if (!KrakenDirector.Warning && (up == null || up.Retreating)) cleanupPending = false;
                }
                if (!guardSwats && !cleanupPending) return;
                var k = Kraken.Active;
                if (k != guardOf) { guardOf = k; for (int i = 0; i < windStart.Length; i++) windStart[i] = -1f; }
                if (k == null || k.Swat == null) return;
                float limit = Mathf.Max(0.3f, KrakenTuning.windupSeconds) - 0.25f;
                for (int a = 0; a < windStart.Length; a++)
                {
                    if (!k.Swat.IsWindingUp(a)) { windStart[a] = -1f; continue; }
                    if (windStart[a] < 0f) { windStart[a] = Time.time; continue; }
                    if (Time.time - windStart[a] < limit) continue;
                    if (k.Swat.TryInterrupt(a)) guardCancels++;
                    windStart[a] = -1f;
                }
            }

            // ---------------------------------------------------------- driver

            IEnumerator Sweep(List<string> names)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    BeginCheck(names[i]);
                    yield return StartCoroutine(Drive(Body(names[i])));
                    EndCheck();
                    // The next check needs an empty sea: no warning still
                    // running, no kraken still sinking.
                    float until = Time.realtimeSinceStartup + 40f;
                    while ((cleanupPending || Kraken.Active != null && Kraken.Active.Retreating)
                           && Time.realtimeSinceStartup < until)
                        yield return null;
                    if (i < names.Count - 1) yield return new WaitForSeconds(1f);
                }
                job = null;
                Finish();
            }

            IEnumerator Drive(IEnumerator root)
            {
                var stack = new Stack<IEnumerator>();
                stack.Push(root);
                while (stack.Count > 0)
                {
                    var top = stack.Peek();
                    bool more;
                    object cur = null;
                    try
                    {
                        more = top.MoveNext();
                        if (more) cur = top.Current;
                    }
                    catch (Exception e)
                    {
                        Fail("no exception", e.GetType().Name + ": " + e.Message + " | " + FirstFrame(e));
                        yield break;
                    }
                    if (!more) { stack.Pop(); continue; }
                    if (cur is IEnumerator nested) { stack.Push(nested); continue; }
                    yield return cur;
                }
            }

            static string FirstFrame(Exception e)
            {
                var st = e.StackTrace ?? "";
                int nl = st.IndexOf('\n');
                return (nl > 0 ? st.Substring(0, nl) : st).Trim();
            }

            IEnumerator Body(string name)
            {
                switch (name)
                {
                    case "arm_hit": return ArmHit();
                    case "warning": return Warning();
                    case "chevron": return Chevron();
                    case "settings": return Settings();
                    case "ship_height": return ShipHeight();
                    case "loot_size": return LootSize();
                    case "hook_loot": return HookLoot();
                    case "hook_wreck": return HookWreck();
                    case "miss": return Miss();
                    case "smoothness": return Smoothness();
                }
                return Nothing();
            }

            static IEnumerator Nothing() { yield break; }

            void BeginCheck(string name)
            {
                current = name;
                sb = new StringBuilder();
                pass = fail = 0;
                undo.Clear();
                timeScale0 = Time.timeScale > 0f ? Time.timeScale : 1f;
                guardSwats = false;
                guardCancels = 0;
                kraken = null;
                prevValid = false;
                string shape = ShapeName();
                sb.AppendLine("== " + name + " (" + shape + " " + Screen.width + "x" + Screen.height + ") ==");
                if (sweep && ShapeChecks.Contains(name))
                    Line("NOTE shape check: ran at " + shape + "; switch the Game view to the "
                         + (HudLayout.Wide ? "portrait (RunProbe.ViewPhone)" : "desktop (RunProbe.ViewDesk)")
                         + " shape and Start(\"" + name + "\") again for the other half.");
            }

            void EndCheck()
            {
                for (int i = undo.Count - 1; i >= 0; i--)
                {
                    try { undo[i](); }
                    catch (Exception e) { Line("UNDO step " + i + " threw " + e.Message); }
                }
                undo.Clear();
                guardSwats = false;
                if (startedWarning && (KrakenDirector.Warning || Kraken.Active != null && !Kraken.Active.Retreating))
                {
                    cleanupPending = true;
                    Line("NOTE the warning this check started is still running; its dev kraken is dismissed the moment it rises.");
                }
                startedWarning = false;
                Time.timeScale = timeScale0;
                if (guardCancels > 0)
                    Line("NOTE the swat guard called off " + guardCancels + " windup(s) so no slam reached the ship.");
                string verdict = (fail == 0 ? "PASS " : "FAIL ") + current + " (" + pass + " pass, " + fail + " fail)";
                verdicts.Add(verdict);
                if (fail == 0) sweepPass++; else sweepFail++;
                all.Append(sb).AppendLine(verdict).AppendLine();
                sb = new StringBuilder();
            }

            void Finish()
            {
                var head = new StringBuilder();
                head.AppendLine((sweepFail == 0 ? "PASS" : "FAIL") + " sweep: " + sweepPass + " check(s) pass, "
                                + sweepFail + " fail -- " + ShapeName() + " " + Screen.width + "x" + Screen.height);
                foreach (var v in verdicts) head.AppendLine("  " + v);
                head.AppendLine();
                lastReport = head.ToString() + all;
                busy = false;
                pending = null;
                current = "";
            }

            // --------------------------------------------------------- report

            void Line(string s) => sb.AppendLine(s);
            void Pass(string what, string detail) { pass++; sb.AppendLine("PASS " + what + " -- " + detail); }
            void Fail(string what, string detail) { fail++; sb.AppendLine("FAIL " + what + " -- " + detail); }
            void Check(bool cond, string what, string detail) { if (cond) Pass(what, detail); else Fail(what, detail); }
            void Info(string s) => sb.AppendLine("INFO " + s);
            void Gdd(string s) => sb.AppendLine("GDD  " + s);

            static string F(float v, string fmt = "F2") => v.ToString(fmt);
            static string V(Vector3 v) => "(" + v.x.ToString("F1") + ", " + v.y.ToString("F1") + ", " + v.z.ToString("F1") + ")";
            static string R(Rect r) => "[x " + r.x.ToString("F0") + " y " + r.y.ToString("F0") + " w " + r.width.ToString("F0") + " h " + r.height.ToString("F0") + "]";
            static string ShapeName() => HudLayout.Wide ? "desktop" : "portrait";

            void Undo(Action a) => undo.Add(a);

            void Shot(string tag)
            {
                if (dir == null) { Info("screenshot '" + tag + "' skipped: no dir argument"); return; }
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "sweep_" + current + "_" + tag + "_" + ShapeName() + ".png");
                ScreenCapture.CaptureScreenshot(path);
                Line("SHOT " + path + " (" + Screen.width + "x" + Screen.height + ", written at the end of the frame)");
            }

            // ------------------------------------------------------ waiting

            IEnumerator Wait(float seconds, bool fast = false)
            {
                if (fast) Time.timeScale = timeScale0 * FastScale;
                float end = Time.time + seconds;
                while (Time.time < end) yield return null;
                if (fast) Time.timeScale = timeScale0;
            }

            /// Sets `ok`. Timeout in game seconds.
            IEnumerator WaitFor(Func<bool> cond, float timeout, bool fast = false)
            {
                if (fast) Time.timeScale = timeScale0 * FastScale;
                ok = false;
                float end = Time.time + timeout;
                while (true)
                {
                    if (cond()) { ok = true; break; }
                    if (Time.time >= end) break;
                    yield return null;
                }
                if (fast) Time.timeScale = timeScale0;
            }

            IEnumerator Frames(int n)
            {
                for (int i = 0; i < n; i++) yield return Eof;
            }

            // -------------------------------------------------------- world

            bool World()
            {
                motor = FindAnyObjectByType<ShipMotor>();
                voyage = FindAnyObjectByType<VoyageManager>();
                chase = FindAnyObjectByType<ChaseCamera>();
                cam = chase != null ? chase.GetComponent<Camera>() : null;
                if (cam == null) cam = Camera.main;
                if (motor == null) { Fail("player ship", "no ShipMotor in the scene"); return false; }
                if (Time.timeScale <= 0f) { Fail("game running", "Time.timeScale is 0 (paused)"); return false; }
                if (motor.Anchored) { Fail("at sea", "the ship is anchored"); return false; }
                if (voyage != null && voyage.AtHome) { Fail("at sea", "the ship is at home"); return false; }
                if (cam == null) { Fail("camera", "no chase camera / Camera.main"); return false; }
                return true;
            }

            bool KrakenFree()
            {
                if (Kraken.Active != null || KrakenDirector.Warning)
                {
                    Fail("no kraken up", "a kraken is already up or a warning is running; dismiss it first (it is the player's, the check leaves it alone)");
                    return false;
                }
                return true;
            }

            static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
            Vector3 ShipFwd => Flat(motor.transform.forward).normalized;
            Vector3 ShipRight => Flat(motor.transform.right).normalized;

            Vector3 Ahead(float metres)
            {
                Vector3 p = motor.transform.position + ShipFwd * (motor.HullLength * 0.5f + metres);
                p.y = 0f;
                return p;
            }

            static float SeaAt(Vector3 p) =>
                SeaSick.Ocean.OceanSampler.Ready ? SeaSick.Ocean.OceanSampler.SampleImmediate(p).height : 0f;

            float BowAngle(Vector3 world)
            {
                Vector3 d = Flat(world - motor.transform.position);
                return d.sqrMagnitude > 1f ? Vector3.Angle(ShipFwd, d) : 0f;
            }

            /// Throttle by the owner: HelmInput rewrites the orders every
            /// frame from the stick, so it is switched off and put back.
            void HoldShip(float throttle)
            {
                var helm = motor.GetComponent<HelmInput>();
                if (helm == null) helm = FindAnyObjectByType<HelmInput>();
                if (helm != null && helm.enabled)
                {
                    helm.enabled = false;
                    var h = helm;
                    Undo(() => { if (h != null) h.enabled = true; });
                }
                float t0 = motor.ThrottleOrder, r0 = motor.Rudder;
                var m = motor;
                Undo(() => { if (m != null) { m.ThrottleOrder = t0; m.Rudder = r0; } });
                motor.ThrottleOrder = throttle;
                motor.Rudder = 0f;
            }

            /// The hull is put back exactly (a check must not cost her
            /// integrity), through the private field: `FullRepair` would
            /// over-repair a hull that was already battered.
            void HullGuard()
            {
                var hull = motor.GetComponent<HullIntegrity>();
                if (hull == null) return;
                float h0 = hull.Integrity01;
                Undo(() =>
                {
                    if (hull == null || Mathf.Abs(hull.Integrity01 - h0) < 1e-4f) return;
                    var f = typeof(HullIntegrity).GetField("integrity", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (f != null) f.SetValue(hull, h0);
                    Line("NOTE hull integrity put back to " + F(h0, "F3") + " (it read " + F(hull.Integrity01, "F3") + " before the reset).");
                });
            }

            void OwnKraken(Kraken k)
            {
                kraken = k;
                Undo(() => { if (k != null && !k.Retreating) k.Dismiss(); });
            }

            IEnumerator DismissAndWait()
            {
                if (kraken != null && !kraken.Retreating) kraken.Dismiss();
                yield return WaitFor(() => kraken == null, 20f, true);
                if (!ok) Info("the kraken was still sinking after 20 s");
            }

            static float Dmg(Kraken k) => (1f - k.Health01) * Mathf.Max(1f, KrakenTuning.hitPoints);

            static int WindingArm(KrakenSwat s)
            {
                if (s == null) return -1;
                for (int a = 0; a < KrakenArms.ArmCount; a++) if (s.IsWindingUp(a)) return a;
                return -1;
            }

            string CamPose()
            {
                var t = cam.transform;
                Vector3 d = t.position - motor.transform.position;
                float back = -Vector3.Dot(Flat(d), ShipFwd), side = Vector3.Dot(Flat(d), ShipRight);
                float yaw = Mathf.DeltaAngle(Mathf.Atan2(ShipFwd.x, ShipFwd.z) * Mathf.Rad2Deg, t.eulerAngles.y);
                float pitch = Mathf.DeltaAngle(0f, t.eulerAngles.x);
                string focus = chase != null && chase.SeaFocus.HasValue
                    ? "SeaFocus at " + F(Flat(chase.SeaFocus.Value - motor.transform.position).magnitude, "F0") + " m, "
                      + F(BowAngle(chase.SeaFocus.Value), "F0") + " deg off the bow, level " + F(chase.SeaFocusLevel)
                    : "SeaFocus none (level " + (chase != null ? F(chase.SeaFocusLevel) : "-") + ")";
                string lockT = chase != null && chase.LockTarget != null ? ", LockTarget " + chase.LockTarget.name : "";
                return "seat " + F(back, "F1") + " m behind / " + F(side, "F1") + " m right / " + F(t.position.y, "F1")
                       + " m up, yaw " + F(yaw, "F0") + " deg off her heading, pitch " + F(pitch, "F1")
                       + " deg, fov " + F(cam.fieldOfView, "F1") + "; " + focus + lockT;
            }

            Vector3 VP(Vector3 w) => cam.WorldToViewportPoint(w);
            static bool OnScreen(Vector3 vp) => vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;

            Vector2 Gui(Vector3 w)
            {
                var s = cam.WorldToScreenPoint(w);
                return new Vector2(s.x, Screen.height - s.y);
            }

            static List<KeyValuePair<string, Rect>> HudRects()
            {
                var l = new List<KeyValuePair<string, Rect>>();
                void Add(string n, Rect r) { if (r.width > 0.5f && r.height > 0.5f) l.Add(new KeyValuePair<string, Rect>(n, r)); }
                Add("SeaHud.TopRect", SeaHud.TopRect);
                Add("SeaHud.AlertRect", SeaHud.AlertRect);
                Add("SeaHud.HelmRect", SeaHud.HelmRect);
                Add("SeaHud.BoostRect", SeaHud.BoostRect);
                Add("SeaHud.HarpoonRect", SeaHud.HarpoonRect);
                if (CombatHud.Visible)
                {
                    Add("CombatHud.Rect", CombatHud.Rect);
                    Add("CombatHud.ChipRect", CombatHud.ChipRect);
                    Add("CombatHud.LockRect", CombatHud.LockRect);
                }
                var issued = HudLayout.Issued;
                var names = HudLayout.IssuedTo;
                for (int i = 0; i < issued.Count; i++) Add("HudLayout:" + (i < names.Count ? names[i] : "?"), issued[i]);
                return l;
            }

            string HudLive()
            {
                return "top bar " + SeaHud.TopBarShowing + ", helm strip " + SeaHud.HelmShowing
                       + ", combat row " + CombatHud.Visible + (CombatHud.Visible && CombatHud.ChipRect.height > 0f ? " (chip up)" : "")
                       + ", harpoon " + R(SeaHud.HarpoonRect) + ", bolt " + R(SeaHud.BoostRect)
                       + ", HudLayout.Issued " + HudLayout.Issued.Count;
            }

            static Rect Union(Rect a, Rect b)
            {
                if (a.width <= 0f || a.height <= 0f) return b;
                if (b.width <= 0f || b.height <= 0f) return a;
                return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                    Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
            }

            static Rect Shrink(Rect r, float by) => new Rect(r.x + by, r.y + by, Mathf.Max(0f, r.width - 2f * by), Mathf.Max(0f, r.height - 2f * by));

            static float Across(GameObject g)
            {
                bool any = false;
                var b = new Bounds();
                foreach (var r in g.GetComponentsInChildren<Renderer>())
                {
                    if (r == null || !r.enabled || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                return any ? Mathf.Max(b.size.x, b.size.z) : 0f;
            }

            static float Height(GameObject g)
            {
                bool any = false;
                var b = new Bounds();
                foreach (var r in g.GetComponentsInChildren<Renderer>())
                {
                    if (r == null || !r.enabled || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                return any ? b.size.y : 0f;
            }

            // ============================================================
            // 1. arm_hit

            /// Tracks every cannonball that appears after `Prime`, and how
            /// close each one came to an arm's hit capsule.
            sealed class Balls
            {
                readonly HashSet<int> seen = new HashSet<int>();
                readonly List<CannonBall> live = new List<CannonBall>();
                public int Count;
                public float MinClear = float.PositiveInfinity;
                public float DyAtMin;
                public Vector3 PosAtMin;

                public void Prime()
                {
                    foreach (var b in UnityEngine.Object.FindObjectsByType<CannonBall>(FindObjectsSortMode.None)) seen.Add(b.GetInstanceID());
                }

                public bool AllDone
                {
                    get
                    {
                        for (int i = 0; i < live.Count; i++) if (live[i] != null) return false;
                        return true;
                    }
                }

                public void Track(KrakenArmTarget arm)
                {
                    foreach (var b in UnityEngine.Object.FindObjectsByType<CannonBall>(FindObjectsSortMode.None))
                        if (seen.Add(b.GetInstanceID())) { live.Add(b); Count++; }
                    if (arm == null) return;
                    Vector3 c = arm.HitCentre, ax = arm.HitAxis;
                    Vector3 p = c - ax, q = c + ax;
                    float r = arm.HitRadius + 0.21f;
                    for (int i = 0; i < live.Count; i++)
                    {
                        var b = live[i];
                        if (b == null) continue;
                        Vector3 x = b.transform.position;
                        Vector3 pq = q - p;
                        float t = pq.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(x - p, pq) / pq.sqrMagnitude) : 0f;
                        Vector3 near = p + pq * t;
                        float clear = Vector3.Distance(x, near) - r;
                        if (clear < MinClear) { MinClear = clear; DyAtMin = x.y - near.y; PosAtMin = x; }
                    }
                }
            }

            bool Starboard(Cannon c) => Vector3.Dot(c.RestDirection, ShipRight) > 0f;

            /// Why each gun on that side would or would not fire at the arm:
            /// the same gates `CannonBattery.AutoFireSide` applies, plus where
            /// a ball at the gun's fixed elevation actually is at that range.
            string GunGates(CannonBattery b, KrakenArmTarget arm, float side)
            {
                var s = new StringBuilder();
                int i = 0;
                foreach (var c in b.Guns)
                {
                    if (c == null) continue;
                    bool stb = Starboard(c);
                    if ((stb ? 1f : -1f) != side) { i++; continue; }
                    Vector3 from = c.MuzzlePoint;
                    Vector3 to = arm.HitCentre - from;
                    float up = to.y;
                    to.y = 0f;
                    float dist = to.magnitude;
                    float ang = Vector3.Angle(c.RestDirection, to);
                    float half = Mathf.Atan2(arm.HitRadius, Mathf.Max(0.1f, dist)) * Mathf.Rad2Deg;
                    Vector3 bore = c.FireDirection;
                    float elev = Mathf.Asin(Mathf.Clamp(bore.normalized.y, -1f, 1f));
                    bore.y = 0f;
                    float off = bore.sqrMagnitude > 1e-6f ? Vector3.Angle(bore, to) : 0f;
                    float v = c.MuzzleSpeed, g = Mathf.Abs(Physics.gravity.y);
                    float cosE = Mathf.Cos(elev);
                    float ballUp = dist * Mathf.Tan(elev) - g * dist * dist / (2f * v * v * cosE * cosE);
                    s.Append("\n     gun" + i + " " + (stb ? "stbd" : "port") + ": ready " + c.Ready + " (manned " + c.Manned
                             + "), range " + F(dist, "F1") + "/" + F(c.FlatRange, "F1") + " m, arc " + F(ang, "F1") + "/"
                             + F(c.MaxTraverseDeg + half, "F1") + " deg, bore off " + F(off, "F1")
                             + " deg; arm centre " + F(up, "F1") + " m over the muzzle, a ball at "
                             + F(elev * Mathf.Rad2Deg, "F1") + " deg elevation is " + F(ballUp, "F1") + " m over it there");
                    i++;
                }
                return s.Length > 0 ? s.ToString() : " (no guns on that side)";
            }

            IEnumerator ArmHit()
            {
                Gdd("§6 The Kraken, Three answers / Fight: \"Hitting the raised arm during its windup cancels that swat\"; "
                    + "build step 3: \"a raised-arm hit cancels that swat and counts 0.35\" (KrakenTuning.armHitDamage).");
                if (!World() || !KrakenFree()) yield break;
                var battery = motor.GetComponent<CannonBattery>();
                var lockC = motor.GetComponent<CombatLock>();
                var self = motor.GetComponent<PlayerHull>();
                if (battery == null || battery.TotalGuns == 0) { Fail("guns fitted", "no CannonBattery or no guns on the player ship"); yield break; }

                HullGuard();
                HoldShip(0f);
                yield return WaitFor(() => motor.CurrentSpeed < 1f, 15f, true);
                Info("ship held at throttle 0: " + F(motor.CurrentSpeed) + " m/s");

                int sReady = 0, pReady = 0;
                foreach (var c in battery.Guns) if (c != null && c.Ready) { if (Starboard(c)) sReady++; else pReady++; }
                float side = sReady >= pReady ? 1f : -1f;
                Info("guns ready: starboard " + sReady + ", port " + pReady + "; the kraken goes up on the "
                     + (side > 0f ? "starboard" : "port") + " beam, 34 m off");
                var k = Kraken.SummonAt(motor.transform.position + ShipRight * side * 34f, motor.transform.position, true);
                if (k == null) { Fail("summon", "Kraken.SummonAt returned null (prefab missing?)"); yield break; }
                OwnKraken(k);
                guardSwats = true;
                yield return WaitFor(() => kraken == null || kraken.State == Kraken.Phase.Surfaced, 25f, true);
                if (!ok || kraken == null) { Fail("surfaced", "not surfaced after 25 s"); yield break; }
                var swat = kraken.Swat;
                int slams = 0;
                Action<Vector3, bool> onSlam = (p, h) => slams++;
                swat.Slammed += onSlam;
                Undo(() => { if (swat != null) swat.Slammed -= onSlam; });

                // ---- shot 1: the real guns, auto-fire laid on the raised arm
                int arm = -1;
                yield return WaitFor(() => kraken == null || (arm = WindingArm(swat)) >= 0, 20f, true);
                if (!ok || arm < 0) { Fail("a windup", "no arm wound up within 20 s of surfacing"); yield break; }
                var armT = swat.ArmTarget(arm) as KrakenArmTarget;
                float wStart = Time.time;
                bool lockWas = lockC != null && lockC.enabled;
                if (lockWas)
                {
                    // CombatLock writes AutoFireTarget every frame (the lock or
                    // null); off, the battery keeps what we lay it on.
                    lockC.enabled = false;
                    Undo(() => { if (lockC != null) lockC.enabled = true; });
                }
                Undo(() => { if (battery != null) battery.AutoFireTarget = null; });
                float dmg0 = Dmg(kraken);
                int int0 = swat.Interrupts, hits0 = swat.Hits, slam0 = slams, guard0 = guardCancels, auto0 = battery.AutoShots;
                var balls = new Balls();
                balls.Prime();
                bool hit = false;
                float dmgHit = 0f, hitAt = 0f;
                string gates = null;
                float limit = Mathf.Max(0.3f, KrakenTuning.windupSeconds) + 0.6f;
                while (kraken != null && Time.time - wStart < limit)
                {
                    battery.AutoFireTarget = armT;
                    balls.Track(armT);
                    if (!hit && swat.Interrupts > int0 && guardCancels == guard0)
                    {
                        hit = true;
                        dmgHit = Dmg(kraken) - dmg0;
                        hitAt = Time.time - wStart;
                    }
                    if (gates == null && Time.time - wStart >= 1.0f) gates = GunGates(battery, armT, side);
                    if (!swat.IsWindingUp(arm) && balls.AllDone && Time.time - wStart > 0.3f) break;
                    yield return null;
                }
                battery.AutoFireTarget = null;
                if (lockWas && lockC != null) lockC.enabled = true;
                yield return Wait(Mathf.Max(0.05f, KrakenTuning.slamSeconds) + 0.6f);
                int shots = battery.AutoShots - auto0;
                bool guarded = guardCancels > guard0;

                Line("     arm " + arm + " wound up; gates at windup +1.0 s:" + (gates ?? " (windup ended before 1 s)"));
                Check(shots > 0, "a real gun fires at the raised arm (CannonBattery.AutoFire, AutoFireTarget = the arm)",
                      shots + " auto shot(s) during the " + F(KrakenTuning.windupSeconds, "F1") + " s windup");
                if (shots > 0 || balls.Count > 0)
                {
                    string near = float.IsPositiveInfinity(balls.MinClear) ? "never tracked"
                        : F(balls.MinClear, "F1") + " m outside the arm capsule (" + F(balls.DyAtMin, "F1")
                          + " m " + (balls.DyAtMin < 0f ? "below" : "above") + " its axis)";
                    Check(hit, "the ball collides with the raised arm (HitTargets.SweepFirst -> KrakenArmTarget.TakeHit -> KrakenSwat.TryInterrupt)",
                          hit ? "hit " + F(hitAt) + " s into the windup" : "missed; closest pass " + near);
                }
                if (hit)
                {
                    Check(slams == slam0 && swat.Hits == hits0 && !guarded, "the swat is cancelled (no slam on the ship)",
                          "slams " + (slams - slam0) + ", hits " + (swat.Hits - hits0) + ", guard cancels " + (guardCancels - guard0));
                    float want = 1f * KrakenTuning.armHitDamage;
                    Check(Mathf.Abs(dmgHit - want) < 0.02f, "health drops by the raised-arm amount",
                          "damage +" + F(dmgHit, "F3") + " hit points (want " + F(want, "F3") + " = 1 ball x armHitDamage "
                          + F(KrakenTuning.armHitDamage) + "; 1.35 would mean a head hit too), Health01 " + F(kraken != null ? kraken.Health01 : 0f, "F3"));
                }
                else if (guarded) Info("the guard called that windup off at " + F(KrakenTuning.windupSeconds - 0.25f) + " s (protective, not a result)");

                // ---- shot 2: the hit path itself, one ball solved onto the arm
                if (kraken == null) { Fail("kraken still up for shot 2", "it went away"); yield break; }
                arm = -1;
                yield return WaitFor(() => kraken == null || (arm = WindingArm(swat)) >= 0, 20f, true);
                if (!ok || arm < 0) { Fail("a second windup", "none within 20 s"); yield return DismissAndWait(); yield break; }
                armT = swat.ArmTarget(arm) as KrakenArmTarget;
                wStart = Time.time;
                yield return WaitFor(() => Time.time - wStart >= 1.0f || !swat.IsWindingUp(arm), 3f);
                if (!swat.IsWindingUp(arm)) { Fail("arm still raised at +1 s", "the windup ended early"); yield return DismissAndWait(); yield break; }
                Cannon bestGun = null;
                float best = -2f;
                Vector3 toArm = Flat(armT.HitCentre - motor.transform.position).normalized;
                foreach (var c in battery.Guns)
                {
                    if (c == null) continue;
                    float d = Vector3.Dot(c.RestDirection, toArm);
                    if (d > best) { best = d; bestGun = c; }
                }
                dmg0 = Dmg(kraken);
                int0 = swat.Interrupts; hits0 = swat.Hits; slam0 = slams; guard0 = guardCancels;
                balls = new Balls();
                balls.Prime();
                Vector3 aim = armT.HitCentre;
                CannonBall.FireAt(bestGun.MuzzlePoint, aim, bestGun.MuzzleSpeed, 0f, self);
                Info("shot 2: CannonBall.FireAt from the best-bearing gun's muzzle " + V(bestGun.MuzzlePoint) + " at the arm centre "
                     + V(aim) + " (" + F(aim.y - bestGun.MuzzlePoint.y, "F1") + " m up), " + F(bestGun.MuzzleSpeed, "F0")
                     + " m/s, no spread -- the ballistic solve the raiders use, so it tests the hit path, not the laying");
                hit = false;
                float shot2 = Time.time;
                while (kraken != null && Time.time - shot2 < 3f)
                {
                    balls.Track(armT);
                    if (!hit && swat.Interrupts > int0 && guardCancels == guard0) { hit = true; dmgHit = Dmg(kraken) - dmg0; hitAt = Time.time - shot2; }
                    if (balls.AllDone && Time.time - shot2 > 0.1f) break;
                    yield return null;
                }
                yield return Wait(Mathf.Max(0.05f, KrakenTuning.slamSeconds) + 0.6f);
                Check(hit, "a ball laid on the raised arm collides with it (the hit path works)",
                      hit ? "hit " + F(hitAt) + " s after the shot" : "missed; closest pass " + F(balls.MinClear, "F1") + " m outside the capsule ("
                            + F(balls.DyAtMin, "F1") + " m " + (balls.DyAtMin < 0f ? "below" : "above") + " its axis)");
                if (hit)
                {
                    Check(slams == slam0 && swat.Hits == hits0 && guardCancels == guard0, "that swat is cancelled (no slam)",
                          "slams " + (slams - slam0) + ", hits " + (swat.Hits - hits0) + ", flinch via TryInterrupt, Interrupts " + swat.Interrupts);
                    float want = KrakenTuning.armHitDamage;
                    Check(Mathf.Abs(dmgHit - want) < 0.02f, "health drops by the raised-arm amount (0.35)",
                          "damage +" + F(dmgHit, "F3") + " (want " + F(want, "F3") + ")");
                }
                yield return DismissAndWait();
            }

            // ============================================================
            // 2. warning

            IEnumerator Warning()
            {
                Gdd("§6 The Kraken, A warning, never an ambush: ~10 s before it surfaces the water off one bow darkens, "
                    + "a red edge chevron points at it, a short toast \"Something huge below…\", a vibration; "
                    + "it surfaces off a bow quarter, never dead ahead. Camera: KrakenDirector.FrameWarning swings the chase "
                    + "camera onto the shadow (SeaFocus) and releases it the frame before the breach, when Kraken takes it over.");
                if (!World() || !KrakenFree()) yield break;
                if (KrakenDirector.Instance == null) { Fail("KrakenDirector", "no director in the scene"); yield break; }
                HullGuard();
                guardSwats = true;
                Info("camera before: " + CamPose());
                int hap0 = OverboardHaptics.DevWarningCalls;
                float t0 = Time.time;
                string said = KrakenDirector.DevWarnNow();
                startedWarning = KrakenDirector.Warning;
                Info("KrakenDirector.DevWarnNow() -> \"" + said + "\" (the wild path minus the dice, distance, cooldown and deep-water test)");
                if (!KrakenDirector.Warning) { Fail("warning started", said); yield break; }
                Undo(() => { if (Kraken.Active != null && Kraken.Active.DevSummoned && !Kraken.Active.Retreating) Kraken.Active.Dismiss(); });
                yield return Frames(2);

                var wp0 = KrakenDirector.WarningPoint;
                float ang0 = wp0.HasValue ? BowAngle(wp0.Value) : -1f;
                Check(FindAnyObjectByType<KrakenShadow>() != null && wp0.HasValue, "dark water (KrakenShadow) off one bow",
                      wp0.HasValue ? "shadow at " + F(Flat(wp0.Value - motor.transform.position).magnitude, "F0") + " m, "
                                     + F(ang0, "F0") + " deg off the bow" : "no WarningPoint");
                Check(wp0.HasValue && ang0 >= 30f && ang0 <= 70f, "the shadow is off a bow quarter (38..62 deg picked), not dead ahead",
                      F(ang0, "F0") + " deg");
                Check(Banner.Text == "Something huge below…" && Banner.ShownAt >= t0 - 0.01f, "the toast",
                      "Banner \"" + Banner.Text + "\" shown " + F(Banner.ShownAt - t0) + " s after the call");
                Check(OverboardHaptics.DevWarningCalls - hap0 >= 1, "a haptic call at the start (OverboardHaptics.Warning)",
                      (OverboardHaptics.DevWarningCalls - hap0) + " call(s); native haptics switch on = " + JuiceTuning.overboardHapticsOn);

                yield return WaitFor(() => Time.time - t0 >= 3f || Kraken.Active != null, 5f);
                yield return Frames(1);
                var wp = KrakenDirector.WarningPoint;
                bool chevDrawn = SeaEdgeMarkers.DevKrakenFrame >= Time.frameCount - 2 && SeaEdgeMarkers.DevKrakenWhy == "drawn";
                string chev = SeaEdgeMarkers.DevKrakenFrame >= Time.frameCount - 2 ? SeaEdgeMarkers.DevKrakenWhy : "not reached on the last repaint";
                Vector3 shadowVp = wp.HasValue ? VP(new Vector3(wp.Value.x, SeaAt(wp.Value), wp.Value.z)) : Vector3.zero;
                Check(chevDrawn || chev == "on screen", "the edge marker points at it (or the shadow is on screen and speaks for itself)",
                      "kraken marker: " + chev + (chevDrawn ? " " + R(SeaEdgeMarkers.DevKrakenBox) : ""));
                Info("camera at warning +3 s: " + CamPose());
                bool focusOnShadow = chase != null && chase.SeaFocus.HasValue && wp.HasValue
                                     && Flat(chase.SeaFocus.Value - wp.Value).magnitude < 1f;
                Check(focusOnShadow, "the warning camera frames the shadow (ChaseCamera.SeaFocus = the shadow point)",
                      chase != null && chase.SeaFocus.HasValue ? "SeaFocus " + F(wp.HasValue ? Flat(chase.SeaFocus.Value - wp.Value).magnitude : -1f, "F1") + " m from the shadow, level " + F(chase.SeaFocusLevel)
                                                               : "SeaFocus is null (frameCamera " + KrakenTuning.frameCamera + ", lock " + (chase != null && chase.LockTarget != null) + ")");
                Check(OnScreen(shadowVp), "the shadow is in frame at +3 s", "viewport " + V(shadowVp));
                Shot("warn3s");

                yield return WaitFor(() => Kraken.Active != null, KrakenSpawnTuning.warningSeconds + 5f);
                float tSurf = Time.time - t0;
                if (!ok) { Fail("it surfaces", "no kraken " + F(KrakenSpawnTuning.warningSeconds + 5f, "F0") + " s after the warning"); yield break; }
                OwnKraken(Kraken.Active);
                Check(Mathf.Abs(tSurf - KrakenSpawnTuning.warningSeconds) <= 1.0f, "it surfaces ~warningSeconds after the warning",
                      F(tSurf, "F1") + " s (warningSeconds " + F(KrakenSpawnTuning.warningSeconds, "F0") + ")");
                float angS = BowAngle(kraken.transform.position);
                Check(angS >= 30f && angS <= 115f, "it surfaces off a bow quarter, never dead ahead",
                      F(angS, "F0") + " deg off the bow, " + F(Flat(kraken.transform.position - motor.transform.position).magnitude, "F0") + " m");
                Check(OverboardHaptics.DevWarningCalls - hap0 >= 2, "a second haptic pulse before the breach (2.5 s left)",
                      (OverboardHaptics.DevWarningCalls - hap0) + " call(s) in all");

                yield return WaitFor(() => kraken == null || kraken.Breached, 15f);
                if (kraken == null || !kraken.Breached) { Fail("it breaches", "no breach 15 s after it started rising"); yield break; }
                Info("camera at the breach (" + F(Time.time - t0, "F1") + " s): " + CamPose());
                Shot("surfacing");
                yield return Wait(3f);
                if (kraken == null) { Fail("still up 3 s after the breach", "gone"); yield break; }
                Info("camera 3 s after the breach: " + CamPose());
                bool focusOnKraken = chase != null && chase.SeaFocus.HasValue
                                     && Flat(chase.SeaFocus.Value - kraken.transform.position).magnitude < 20f;
                Check(focusOnKraken && chase.SeaFocusLevel > 0.9f, "after surfacing the kraken's own SeaFocus framing has the camera",
                      chase != null && chase.SeaFocus.HasValue ? "SeaFocus " + F(Flat(chase.SeaFocus.Value - kraken.transform.position).magnitude, "F1")
                                                                 + " m from the beast, level " + F(chase.SeaFocusLevel) : "SeaFocus null");
                yield return DismissAndWait();
                yield return WaitFor(() => chase == null || !chase.SeaFocus.HasValue, 5f);
                Info("camera after it left: " + CamPose());
            }

            // ============================================================
            // 3. chevron

            IEnumerator EvalChevron(string label)
            {
                Rect box = new Rect(), lab = new Rect();
                int drawn = 0, frames = 0;
                string why = "";
                var hits = new Dictionary<string, string>();
                float end = Time.time + 0.6f;
                while (Time.time < end || frames < 3)
                {
                    yield return Eof;
                    frames++;
                    if (SeaEdgeMarkers.DevKrakenFrame < Time.frameCount - 1) { why = "not reached on the last repaint (band too small, HUD suppressed, or no kraken marker)"; continue; }
                    why = SeaEdgeMarkers.DevKrakenWhy;
                    if (why != "drawn") continue;
                    drawn++;
                    Rect b = SeaEdgeMarkers.DevKrakenBox, l = SeaEdgeMarkers.DevKrakenLabel;
                    box = Union(box, b);
                    lab = Union(lab, l);
                    foreach (var kv in HudRects())
                    {
                        if (Shrink(kv.Value, 0.5f).Overlaps(b)) hits[kv.Key + " x chevron"] = R(kv.Value) + " vs " + R(b);
                        if (Shrink(kv.Value, 0.5f).Overlaps(l)) hits[kv.Key + " x label"] = R(kv.Value) + " vs " + R(l);
                    }
                }
                if (drawn == 0)
                {
                    if (why == "on screen") Info(label + ": the kraken marker point is ON screen, so no chevron is drawn (nothing to test at this pose)");
                    else Fail("chevron drawn for the " + label, why);
                    yield break;
                }
                Pass("chevron drawn for the " + label, drawn + "/" + frames + " frames, box " + R(box) + " (union, it breathes), label " + R(lab));
                var hits2 = new StringBuilder();
                foreach (var kv in hits) hits2.Append("\n     " + kv.Key + ": " + kv.Value);
                Check(hits.Count == 0, "the " + label + " chevron and its label clear every HUD rect (SeaHud top/alert/helm/bolt/harpoon, CombatHud row/chip/lock, HudLayout.Issued)",
                      hits.Count == 0 ? HudRects().Count + " rects checked" : hits.Count + " overlap(s):" + hits2);
                Rect safe = HudLayout.Safe;
                Check(safe.Contains(box.min) && safe.Contains(box.max), "the chevron lies inside the safe area", "safe " + R(safe));
            }

            IEnumerator Chevron()
            {
                Gdd("§6 The Kraken: \"a red edge chevron points at it (SeaEdgeMarkers, new kind)\"; CLAUDE.md: thumb controls at the bottom, nothing over them.");
                if (!World() || !KrakenFree()) yield break;
                HullGuard();
                bool frame0 = KrakenTuning.frameCamera;
                KrakenTuning.frameCamera = false;
                Undo(() => KrakenTuning.frameCamera = frame0);
                Info("KrakenTuning.frameCamera off for this check, so the camera does not swing the kraken into view");
                guardSwats = true;
                yield return Frames(2);
                Info("live HUD: " + HudLive());
                if (!SeaHud.HelmShowing) { Fail("live sea HUD", "the helm strip is not showing, SeaEdgeMarkers draws nothing"); yield break; }

                string said = KrakenDirector.DevWarnNow();
                startedWarning = KrakenDirector.Warning;
                if (!KrakenDirector.Warning) { Fail("warning started", said); yield break; }
                Undo(() => { if (Kraken.Active != null && Kraken.Active.DevSummoned && !Kraken.Active.Retreating) Kraken.Active.Dismiss(); });
                yield return Wait(2f);
                Info("phase A, the warning shadow: " + F(BowAngle(KrakenDirector.WarningPoint ?? motor.transform.position), "F0") + " deg off the bow; " + HudLive());
                yield return EvalChevron("warning shadow");
                Shot("warning");

                yield return WaitFor(() => Kraken.Active != null, KrakenSpawnTuning.warningSeconds + 5f);
                if (!ok) { Fail("it surfaces", "no kraken after the warning"); yield break; }
                OwnKraken(Kraken.Active);
                yield return WaitFor(() => kraken == null || kraken.Breached, 15f);
                if (kraken == null) { Fail("kraken up", "gone"); yield break; }
                Vector3 p = motor.transform.position - ShipFwd * 62f + ShipRight * 14f;
                p.y = kraken.transform.position.y;
                kraken.transform.position = p;
                yield return Wait(1.5f);
                Info("phase B, the kraken moved 62 m astern / 14 m to starboard (behind the camera): " + HudLive());
                yield return EvalChevron("kraken astern");
                Shot("kraken");

                var lockC = motor.GetComponent<CombatLock>();
                if (lockC != null && kraken != null && lockC.LockOn(kraken.BodyTarget))
                {
                    Undo(() => { if (lockC != null && lockC.Locked != null) lockC.ToggleLock(); });
                    yield return Wait(2f);
                    Info("phase C, locked on its head (target chip up): " + HudLive());
                    yield return EvalChevron("locked kraken");
                    Shot("locked");
                    if (lockC.Locked != null) lockC.ToggleLock();
                }
                else Info("phase C skipped: CombatLock.LockOn refused the head (no lock component, or out of reach)");
                yield return DismissAndWait();
            }

            // ============================================================
            // 4. settings

            IEnumerator Settings()
            {
                Gdd("Settings drawer TUNING rows (SettingsPanel: Summon/Dismiss kraken, Wild spawn now, the kraken status line, the LIFE page row); "
                    + "memory rule \"never truncate text\" (wrap or restack, no clipped labels).");
                if (!Application.isPlaying) yield break;
                if (SettingsPanel.Instance == null) { Fail("SettingsPanel", "no SettingsPanel instance"); yield break; }
                bool wasOpen = SettingsPanel.IsOpen;
                var tool0 = DevTools.Open;
                Vector2 scroll0 = SettingsPanel.DevScroll;
                Undo(() =>
                {
                    SettingsPanel.DevScroll = scroll0;
                    DevTools.Open = tool0;
                    if (!wasOpen) SettingsPanel.DevClose();
                });
                DevTools.Open = null;
                SettingsPanel.Open();
                yield return Frames(3);
                if (SettingsPanel.DevRowsFrame < Time.frameCount - 2 || !SettingsPanel.IsOpen)
                {
                    Fail("the drawer's list is drawn", "SettingsPanel did not repaint its list (a menu or the shipyard up?)");
                    yield break;
                }
                int u = HudLayout.Unit;
                float tuningY = -1f;
                foreach (var r in SettingsPanel.DevRows) if (r.name == "tuning header") tuningY = r.local.y;
                if (tuningY < 0f) { Fail("TUNING block drawn", "no TUNING rows (not an editor/dev build?)"); yield break; }
                SettingsPanel.DevScroll = new Vector2(0f, Mathf.Max(0f, tuningY - u * 0.5f));
                yield return Frames(3);

                var rows = new List<SettingsPanel.DevRowInfo>(SettingsPanel.DevRows);
                Rect view = SettingsPanel.DevView;
                Vector2 sc = SettingsPanel.DevScrollAtDraw;
                Info("drawer list view " + R(view) + ", scrolled to " + F(sc.y, "F0") + " px (TUNING header at " + F(tuningY, "F0") + "), unit " + u);
                var want = new List<string> { "tuning header", "summon kraken", "dismiss kraken", "wild spawn now", "kraken status" };
                if (SeaSick.Dev.LifeDevPanel.Instance != null) { want.Add("LIFE page"); want.Add("LIFE note"); }
                else Info("LifeDevPanel.Instance is null, so the LIFE row is not drawn in this scene");
                var screen = new Dictionary<string, Rect>();
                foreach (var r in rows)
                    screen[r.name] = new Rect(view.x + r.local.x, view.y + r.local.y - sc.y, r.local.width, r.local.height);
                var missing = new List<string>();
                foreach (var n in want) if (!screen.ContainsKey(n)) missing.Add(n);
                Check(missing.Count == 0, "every TUNING row is drawn", missing.Count == 0 ? rows.Count + " rows" : "missing: " + string.Join(", ", missing));

                var overlaps = new StringBuilder();
                for (int i = 0; i < rows.Count; i++)
                    for (int j = i + 1; j < rows.Count; j++)
                    {
                        Rect a = Shrink(screen[rows[i].name], 0.5f), b = Shrink(screen[rows[j].name], 0.5f);
                        if (a.Overlaps(b)) overlaps.Append("\n     " + rows[i].name + " " + R(a) + " x " + rows[j].name + " " + R(b));
                    }
                Check(overlaps.Length == 0, "the TUNING rows do not overlap each other", overlaps.Length == 0 ? "clear" : overlaps.ToString());

                foreach (var r in rows)
                {
                    Rect s = screen[r.name];
                    bool fits = r.wrap ? r.needH <= r.local.height + 0.5f : r.needW <= r.local.width + 0.5f;
                    Check(fits, "'" + r.name + "' text fits (" + (r.wrap ? "wrapped height" : "one line") + ")",
                          (r.wrap ? "needs " + F(r.needH, "F0") + " px tall of " + F(r.local.height, "F0")
                                  : "needs " + F(r.needW, "F0") + " px of " + F(r.local.width, "F0")) + " -- \"" + r.text.Replace("\n", " / ") + "\"");
                    bool inX = s.xMin >= view.xMin - 0.5f && s.xMax <= view.xMax + 0.5f;
                    Check(inX, "'" + r.name + "' lies inside the drawer's width", R(s) + " in " + R(view));
                    bool inY = s.yMin >= view.yMin - 0.5f && s.yMax <= view.yMax + 0.5f;
                    if (!inY) Info("'" + r.name + "' is " + (s.yMin < view.yMin ? "above" : "below") + " the visible part " + R(s) + " (the list scrolls)");
                }
                Shot("tuning");
            }

            // ============================================================
            // 5. ship_height

            IEnumerator ShipHeight()
            {
                Gdd("§6 The Kraken, Phone: \"The camera widens to frame ship and kraken\". Kraken.FrameCamera: the ship's deck at "
                    + "SeaFocusShip01 (camShip01 " + F(KrakenTuning.camShip01) + " portrait, camShip01Desk " + F(KrakenTuning.camShip01Desk)
                    + " desktop) and its full height under SeaFocusTop01 = min(camTop01 " + F(KrakenTuning.camTop01)
                    + ", the top bar's bottom - 0.025). ChaseCamera.ComposeSeaFocus solves exactly that.");
                if (!World() || !KrakenFree()) yield break;
                bool desk = HudLayout.Wide;
                Info(desk ? "desktop shape: the intended run" : "portrait shape: the comparison run (the desktop run is the one asked for; switch with RunProbe.ViewDesk)");
                HullGuard();
                guardSwats = true;
                var k = Kraken.Summon(motor.transform.position, motor.transform.forward, true);
                if (k == null) { Fail("summon", "Kraken.Summon returned null"); yield break; }
                OwnKraken(k);
                yield return WaitFor(() => kraken == null || kraken.State == Kraken.Phase.Surfaced, 25f, true);
                if (kraken == null || !ok) { Fail("surfaced", "not surfaced in 25 s"); yield break; }
                yield return WaitFor(() => chase != null && chase.SeaFocusLevel >= 0.99f, 8f);
                yield return Wait(2.5f);
                if (kraken == null) { Fail("kraken up", "gone"); yield break; }

                float shipSum = 0f, topMax = -9f, fitTopMax = -9f;
                int n = 0;
                Vector2 topGui = Vector2.zero;
                var shipHits = new Dictionary<string, string>();
                float end = Time.time + 1f;
                while (Time.time < end || n < 3)
                {
                    yield return Eof;
                    if (kraken == null) break;
                    Vector3 sp = motor.transform.position;
                    float sea = SeaAt(sp);
                    float deckVp = VP(new Vector3(sp.x, sea + 2f, sp.z)).y;
                    shipSum += deckVp;
                    n++;
                    Vector3 top = Highest(kraken);
                    Vector3 tvp = VP(top);
                    if (tvp.y > topMax) { topMax = tvp.y; topGui = Gui(top); }
                    if (chase.SeaFocus.HasValue)
                    {
                        Vector3 f = chase.SeaFocus.Value;
                        float fy = VP(new Vector3(f.x, SeaAt(f) + chase.SeaFocusHeight, f.z)).y;
                        fitTopMax = Mathf.Max(fitTopMax, fy);
                    }
                    float half = motor.HullLength * 0.5f;
                    var pts = new[]
                    {
                        new Vector3(sp.x, sea + 2f, sp.z),
                        new Vector3(sp.x, sea + 2f, sp.z) + ShipFwd * half,
                        new Vector3(sp.x, sea + 2f, sp.z) - ShipFwd * half,
                    };
                    foreach (var kv in HudRects())
                        foreach (var w in pts)
                            if (kv.Value.Contains(Gui(w))) shipHits[kv.Key] = R(kv.Value) + " holds " + Gui(w).ToString("F0");
                }
                if (n == 0) { Fail("measured", "the kraken left before a frame was measured"); yield break; }
                float ship01 = shipSum / n;
                float wantShip = desk ? KrakenTuning.camShip01Desk : KrakenTuning.camShip01;
                Info("camera: " + CamPose());
                Check(chase.SeaFocus.HasValue && chase.SeaFocusLevel >= 0.98f, "the kraken's SeaFocus framing is on and settled",
                      "level " + F(chase.SeaFocusLevel) + ", SeaFocusShip01 " + F(chase.SeaFocusShip01) + ", SeaFocusTop01 " + F(chase.SeaFocusTop01));
                Check(Mathf.Abs(chase.SeaFocusShip01 - wantShip) < 0.005f, "the framing asks for this shape's ship height",
                      "SeaFocusShip01 " + F(chase.SeaFocusShip01) + " vs " + (desk ? "camShip01Desk " : "camShip01 ") + F(wantShip));
                Check(Mathf.Abs(ship01 - wantShip) <= 0.04f, "her deck (2 m over the sea) sits at the intended screen height",
                      "deck at " + F(ship01, "F3") + " of the height from the bottom (want " + F(wantShip) + " +-0.04)");
                Check(topMax <= chase.SeaFocusTop01 + 0.02f, "the kraken's top (highest bone or mantle dome) fits under SeaFocusTop01",
                      "top at " + F(topMax, "F3") + " (limit " + F(chase.SeaFocusTop01, "F3") + "; the composed SeaFocusHeight top at " + F(fitTopMax, "F3") + ")");
                if (SeaHud.TopBarShowing)
                    Check(topGui.y >= SeaHud.TopRect.yMax, "the kraken's top is below the top bar",
                          "top at GUI y " + F(topGui.y, "F0") + ", bar bottom " + F(SeaHud.TopRect.yMax, "F0"));
                else Info("top bar not showing; the camTop01 fallback is the ceiling");
                var sh = new StringBuilder();
                foreach (var kv in shipHits) sh.Append("\n     " + kv.Key + ": " + kv.Value);
                Check(shipHits.Count == 0, "her deck, bow and stern are clear of every HUD rect",
                      shipHits.Count == 0 ? "clear" : shipHits.Count + " rect(s):" + sh);
                Shot("framed");
                yield return DismissAndWait();
            }

            /// Like `Kraken.MeasureAboveWater`: the highest of the 70 arm bones
            /// and the mantle's dome (16 m over the root at scale 49).
            static Vector3 Highest(Kraken k)
            {
                Vector3 c = k.transform.position;
                Vector3 best = c + Vector3.up * (16f * k.transform.lossyScale.y / 49f);
                if (k.Arms == null) return best;
                for (int a = 0; a < KrakenArms.ArmCount; a++)
                    for (int j = 0; j < KrakenArms.BonesPerArm; j++)
                    {
                        var b = k.Arms.ArmBone(a, j);
                        if (b != null && b.position.y > best.y) best = b.position;
                    }
                return best;
            }

            // ============================================================
            // 6. loot_size

            IEnumerator LootSize()
            {
                Gdd("§6 The Kraken, Loot: \"A big haul of kraken meat for the island, plus one rare trophy, kraken ink\"; "
                    + "drive off: \"After enough hits it sinks away wounded and leaves loot floating\" (KrakenLoot.Drop: "
                    + KrakenSpawnTuning.lootMeatCrates + " meat crates + 1 ink, FloatingCargo with HarpoonKind \"loot\").");
                if (!World() || !KrakenFree()) yield break;
                HullGuard();
                guardSwats = true;
                var k = Kraken.Summon(motor.transform.position, motor.transform.forward, true);
                if (k == null) { Fail("summon", "Kraken.Summon returned null"); yield break; }
                OwnKraken(k);
                yield return WaitFor(() => kraken == null || kraken.Breached, 25f, true);
                if (kraken == null || !ok) { Fail("breached", "not up in 25 s"); yield break; }
                var before = new HashSet<FloatingCargo>(FloatingCargo.All);
                float left = Mathf.Max(0.01f, KrakenTuning.hitPoints - Dmg(kraken)) + 0.01f;
                kraken.TakeDamage(kraken.transform.position, left);
                Info("drove it off: Kraken.TakeDamage(" + F(left) + ") -- the call every body hit makes; it sinks and drops the loot");
                yield return WaitFor(() => kraken == null, 20f, true);
                if (!ok) { Fail("it sinks away", "still up 20 s after the drive-off"); yield break; }
                yield return Frames(2);
                var loot = new List<FloatingCargo>();
                foreach (var c in FloatingCargo.All) if (c != null && !before.Contains(c) && c.HarpoonKind == "loot") loot.Add(c);
                Undo(() => { foreach (var c in loot) if (c != null) Destroy(c.gameObject); });
                int wantN = Mathf.Max(1, Mathf.RoundToInt(KrakenSpawnTuning.lootMeatCrates)) + 1;
                Check(loot.Count == wantN, "the haul floats up where it sank", loot.Count + " loot crate(s) (want " + wantN + ")");
                if (loot.Count == 0) yield break;

                var refCrate = FloatingCargo.Spawn(SeaSick.World.Res.Timber, 2, motor.transform, Ahead(25f));
                yield return Frames(2);
                float refAcross = refCrate != null ? Across(refCrate.gameObject) : 0f;
                float refH = refCrate != null ? Height(refCrate.gameObject) : 0f;
                if (refCrate != null) Destroy(refCrate.gameObject);
                float wreckAcross = 0f;
                var wreck = FindAnyObjectByType<WreckSalvage>();
                if (wreck != null) wreckAcross = Across(wreck.gameObject);
                float hullL = motor.HullLength;
                Info("reference: a lost-cargo crate (2 timber) " + F(refAcross) + " m across x " + F(refH) + " m tall; a wreckage cluster "
                     + (wreck != null ? F(wreckAcross) + " m across" : "not found") + "; hull " + F(hullL, "F1") + " m long");
                foreach (var c in loot)
                {
                    float a = Across(c.gameObject), h = Height(c.gameObject);
                    Vector3 vp = VP(c.transform.position);
                    string what = c.Units + " " + c.Resource;
                    Check(a >= 0.8f && a <= 3f, "loot crate (" + what + ") is 0.8..3 m across",
                          F(a) + " m across x " + F(h) + " m tall = " + F(a / Mathf.Max(0.1f, hullL) * 100f, "F0") + " % of her length; "
                          + (OnScreen(vp) ? "on screen at " + V(vp) : "off screen (viewport " + V(vp) + ")"));
                    Check(a >= refAcross * 0.98f, "loot crate (" + what + ") is at least as big as a normal crate",
                          F(a) + " m vs " + F(refAcross) + " m");
                }
                Shot("loot");
            }

            // ============================================================
            // harpoon helpers

            HarpoonGun gun;

            bool GunReady()
            {
                gun = HarpoonGun.Player;
                if (gun == null) { Fail("harpoon fitted", "no HarpoonGun.Player"); return false; }
                if (!gun.Available) { Fail("harpoon available", "anchored, at home, a menu up or time paused"); return false; }
                if (gun.State != HarpoonState.Ready) { Fail("harpoon ready", "state " + gun.State + " (line out or reloading)"); return false; }
                return true;
            }

            IEnumerator WaitTarget(IHarpoonable t, string label)
            {
                yield return WaitFor(() => ReferenceEquals(gun.Target, t), 3f);
                Check(ok, "the gun picks the " + label + " (nearest in the 90 deg / 35 m arc)",
                      ok ? "Target \"" + gun.TargetLabel + "\" at " + F(gun.TargetDistance, "F1") + " m"
                         : "Target is " + (gun.Target != null ? "\"" + gun.TargetLabel + "\" at " + F(gun.TargetDistance, "F1") + " m (something nearer)" : "none"));
            }

            void HoldDelta(string res, int delta)
            {
                if (voyage == null || delta <= 0) return;
                int took = voyage.RemoveLoot(delta, res);
                var hold = motor.GetComponent<ShipHold>();
                if (hold != null) for (int i = 0; i < took; i++) hold.RemoveVisual(res);
                Line("NOTE took the " + took + " " + res + " back out of the hold (the check leaves the hold as found).");
            }

            // ============================================================
            // 7. hook_loot

            IEnumerator HookLoot()
            {
                Gdd("Bow harpoon: targets include \"the kraken's loot\"; \"A bite inside 1.5 m hooks it\"; \"The winch reels on its own\"; "
                    + "\"Delivery is the existing haul path (IOverboardTarget.OnHauled), so every reward is exactly the sail-over one\".");
                if (!World() || !GunReady()) yield break;
                if (voyage == null) { Fail("VoyageManager", "none"); yield break; }
                if (!voyage.CargoFits(3)) { Fail("room in the hold", "hold has no room for 3 units"); yield break; }
                HoldShip(0f);
                yield return WaitFor(() => motor.CurrentSpeed < 1f, 15f, true);
                int meat0 = voyage.HeldOf(SeaSick.World.Res.Meat), total0 = voyage.TotalHeld;
                var loot = FloatingCargo.Spawn(SeaSick.World.Res.Meat, 3, motor.transform, Ahead(18f));
                if (loot == null) { Fail("spawn", "FloatingCargo.Spawn returned null"); yield break; }
                loot.HarpoonKind = "loot";
                Undo(() => { if (loot != null) Destroy(loot.gameObject); });
                Info("floated 3 meat 18 m off the bow as KrakenLoot.Drop marks its crates (HarpoonKind \"loot\")");
                yield return WaitTarget(loot, "loot");
                if (!ok) yield break;
                float tFire = Time.time;
                gun.FireOrCut();
                var states = new HashSet<HarpoonState>();
                float maxT = 0f, hookAt = -1f;
                bool missed = false;
                float startDist = -1f, minDist = 999f;
                while (Time.time - tFire < 45f)
                {
                    states.Add(gun.State);
                    if (gun.LastEventAt >= tFire && gun.LastEventWord == "Missed") { missed = true; break; }
                    if (gun.State == HarpoonState.Hooked)
                    {
                        if (hookAt < 0f) hookAt = Time.time - tFire;
                        maxT = Mathf.Max(maxT, gun.Tension01);
                        if (loot != null)
                        {
                            float d = Flat(loot.transform.position - motor.transform.position).magnitude;
                            if (startDist < 0f) startDist = d;
                            minDist = Mathf.Min(minDist, d);
                        }
                    }
                    if (gun.LastEventAt >= tFire && gun.LastEventWord == "Aboard") break;
                    yield return null;
                }
                Check(hookAt >= 0f && !missed, "it bites (captain at the gun, accuracy 1)",
                      hookAt >= 0f ? "Hooked " + F(hookAt) + " s after the press" : "never hooked" + (missed ? " (Missed)" : ""));
                Check(hookAt >= 0f && startDist > minDist + 2f, "it reels in",
                      "load " + F(startDist, "F1") + " m -> " + F(minDist, "F1") + " m from her centre, peak tension " + F(maxT));
                bool aboard = gun.LastEventAt >= tFire && gun.LastEventWord == "Aboard";
                Check(aboard, "delivered: LastEventWord \"Aboard\"", "\"" + gun.LastEventWord + "\" " + F(Time.time - tFire, "F1") + " s after the press");
                yield return Frames(2);
                int dMeat = voyage.HeldOf(SeaSick.World.Res.Meat) - meat0, dTotal = voyage.TotalHeld - total0;
                Check(dMeat == 3 && dTotal == 3, "the hold rises by its units", "meat +" + dMeat + ", total +" + dTotal + " (want +3)");
                Check(loot == null, "the crate is gone from the sea (FloatingCargo.Recover destroyed it)", loot == null ? "gone" : "still floating");
                Check(Banner.Text.StartsWith("Recovered 3"), "the sail-over reward path ran (FloatingCargo.Recover -> VoyageManager.ReturnCargo + its toast)",
                      "Banner \"" + Banner.Text + "\"");
                HoldDelta(SeaSick.World.Res.Meat, dMeat);
            }

            // ============================================================
            // 8. hook_wreck

            IEnumerator HookWreck()
            {
                Gdd("Bow harpoon targets: \"wreckage clusters (the sail-over timber crates)\"; delivery through the same pickup "
                    + "(WreckSalvage.OnHauled -> SalvageSpawner.Collect: +2 timber, the toast, back into the sea).");
                if (!World() || !GunReady()) yield break;
                if (voyage == null) { Fail("VoyageManager", "none"); yield break; }
                var spawner = FindAnyObjectByType<SalvageSpawner>();
                if (spawner == null) { Fail("SalvageSpawner", "none in the scene"); yield break; }
                WreckSalvage wreck = null;
                float bestD = float.MaxValue;
                foreach (var w in FindObjectsByType<WreckSalvage>(FindObjectsSortMode.None))
                {
                    if (w == null || !w.CanBeHarpooned) continue;
                    float d = Flat(w.transform.position - motor.transform.position).magnitude;
                    if (d > 30f && d < bestD) { bestD = d; wreck = w; }
                }
                if (wreck == null) { Fail("a wreckage cluster", "no WreckSalvage on the spawner's crates (more than 30 m off)"); yield break; }
                int value = 2;
                var vf = typeof(SalvageSpawner).GetField("salvageValue", BindingFlags.Instance | BindingFlags.NonPublic);
                if (vf != null) value = (int)vf.GetValue(spawner);
                if (!voyage.CargoFits(value)) { Fail("room in the hold", "no room for " + value); yield break; }
                HoldShip(0f);
                yield return WaitFor(() => motor.CurrentSpeed < 1f, 15f, true);
                Vector3 home = wreck.transform.position;
                var wt = wreck.transform;
                bool delivered = false;
                Undo(() => { if (!delivered && wt != null) wt.position = home; });
                Vector3 at = Ahead(18f);
                at.y = wt.position.y;
                wt.position = at;
                Info("moved an existing cluster (was " + F(bestD, "F0") + " m off) to 18 m off the bow; salvageValue " + value);
                yield return Frames(2);
                int timber0 = voyage.HeldOf(SeaSick.World.Res.Timber);
                yield return WaitTarget(wreck, "wreckage");
                if (!ok) yield break;
                float tFire = Time.time;
                gun.FireOrCut();
                float hookAt = -1f;
                while (Time.time - tFire < 45f)
                {
                    if (gun.State == HarpoonState.Hooked && hookAt < 0f) hookAt = Time.time - tFire;
                    if (gun.LastEventAt >= tFire && (gun.LastEventWord == "Aboard" || gun.LastEventWord == "Missed")) break;
                    yield return null;
                }
                bool aboard = gun.LastEventAt >= tFire && gun.LastEventWord == "Aboard";
                delivered = aboard;
                Check(hookAt >= 0f, "it bites", hookAt >= 0f ? "Hooked " + F(hookAt) + " s after the press" : "never hooked (\"" + gun.LastEventWord + "\")");
                Check(aboard, "delivered: LastEventWord \"Aboard\"", "\"" + gun.LastEventWord + "\" " + F(Time.time - tFire, "F1") + " s after the press");
                float aboardAt = Time.time;
                int toasts = 0;
                float lastShown = Banner.ShownAt;
                if (Banner.Text == "+" + value + " timber" && lastShown >= tFire) toasts = 1;
                int maxDelta = voyage.HeldOf(SeaSick.World.Res.Timber) - timber0;
                while (Time.time - aboardAt < 3f)
                {
                    if (Banner.ShownAt != lastShown) { lastShown = Banner.ShownAt; if (Banner.Text == "+" + value + " timber") toasts++; }
                    maxDelta = Mathf.Max(maxDelta, voyage.HeldOf(SeaSick.World.Res.Timber) - timber0);
                    yield return null;
                }
                int dT = voyage.HeldOf(SeaSick.World.Res.Timber) - timber0;
                Check(dT == value, "+" + value + " timber through SalvageSpawner.Collect", "timber +" + dT);
                Check(maxDelta == value && toasts == 1, "no double pickup (the sail-over skips a hooked cluster)",
                      "peak +" + maxDelta + " over 3 s after delivery, \"+" + value + " timber\" toast " + toasts + "x");
                float away = wt != null ? Flat(wt.position - motor.transform.position).magnitude : -1f;
                Check(away > 30f, "the cluster went back into the sea (Respawn)", F(away, "F0") + " m off now");
                HoldDelta(SeaSick.World.Res.Timber, dT);
            }

            // ============================================================
            // 9. miss

            sealed class BlindHand : IHarpoonCrewSource
            {
                public HarpoonCrew Man(HarpoonGun g) => new HarpoonCrew
                {
                    hand = null, captain = false, atGun = true, workRate = 1f, accuracy01 = 0f,
                };

                public void Release(HarpoonGun g) { }
            }

            IEnumerator Miss()
            {
                Gdd("Bow harpoon: \"A bite inside 1.5 m hooks it; a miss splashes and the line reels back empty in ~2 s, with no reload\"; "
                    + "\"accuracy scales the lead error\" (leadErrorMetres " + F(HarpoonTuning.leadErrorMetres) + " x (1 - accuracy01)).");
                if (!World() || !GunReady()) yield break;
                HoldShip(0f);
                yield return WaitFor(() => motor.CurrentSpeed < 1f, 15f, true);
                var src0 = HarpoonGun.CrewSource;
                HarpoonGun.CrewSource = new BlindHand();
                Undo(() => HarpoonGun.CrewSource = src0);
                int total0 = voyage != null ? voyage.TotalHeld : 0;
                var crate = FloatingCargo.Spawn(SeaSick.World.Res.Timber, 2, motor.transform, Ahead(18f));
                if (crate == null) { Fail("spawn", "FloatingCargo.Spawn returned null"); yield break; }
                Undo(() => { if (crate != null) Destroy(crate.gameObject); });
                Info("a test crew source at the gun with accuracy01 = 0 (lead error up to " + F(HarpoonTuning.leadErrorMetres) + " m), and the crate jumps "
                     + "5 m sideways the frame the barb leaves (5 - 3 m worst-case error = 2 m, more than biteRadius " + F(HarpoonTuning.biteRadius) + " m), so the miss is certain");
                yield return WaitTarget(crate, "crate");
                if (!ok) yield break;
                float tFire = Time.time;
                gun.FireOrCut();
                yield return WaitFor(() => gun.State != HarpoonState.Ready, 3f);
                if (gun.State != HarpoonState.Flying) { Fail("the barb flies", "state " + gun.State); yield break; }
                crate.transform.position += ShipRight * 5f;
                var states = new HashSet<HarpoonState>();
                float missAt = -1f, readyAt = -1f;
                bool splash = false;
                while (Time.time - tFire < 8f)
                {
                    states.Add(gun.State);
                    if (missAt < 0f && gun.LastEventAt >= tFire && gun.LastEventWord == "Missed")
                    {
                        missAt = Time.time;
                        splash = GameObject.Find("HarpoonSplash") != null;
                    }
                    if (missAt >= 0f && !splash) splash = GameObject.Find("HarpoonSplash") != null;
                    if (missAt >= 0f && gun.State == HarpoonState.Ready) { readyAt = Time.time; break; }
                    yield return null;
                }
                Check(missAt >= 0f, "LastEventWord \"Missed\"", "\"" + gun.LastEventWord + "\"" + (missAt >= 0f ? " " + F(missAt - tFire) + " s after the press" : ""));
                Check(splash, "a splash where it fell (KrakenFx.Splash \"HarpoonSplash\")", splash ? "found" : "no HarpoonSplash object");
                float reel = readyAt - missAt;
                Check(readyAt > 0f && Mathf.Abs(reel - HarpoonTuning.missReelSeconds) <= 0.5f, "the line reels back empty in ~missReelSeconds",
                      readyAt > 0f ? F(reel) + " s (missReelSeconds " + F(HarpoonTuning.missReelSeconds) + ")" : "never back to Ready in 8 s (state " + gun.State + ")");
                Check(!states.Contains(HarpoonState.Reloading) && !states.Contains(HarpoonState.Hooked), "Ready with NO reload (and never hooked)",
                      "states seen: " + string.Join(", ", states));
                bool nothing = crate != null && voyage != null && voyage.TotalHeld == total0 && crate.CanBeHarpooned;
                Check(nothing, "nothing is delivered", "crate " + (crate != null ? "still afloat" : "gone") + ", hold " + (voyage != null ? (voyage.TotalHeld - total0).ToString("+0;-0;0") : "?"));
            }

            // ============================================================
            // 10. smoothness

            sealed class Stat
            {
                public float peak, sum, latPeak, latSum;
                public int n;
                public void Add(float r, float lat) { peak = Mathf.Max(peak, r); sum += r; latPeak = Mathf.Max(latPeak, lat); latSum += lat; n++; }
                public float Mean => n > 0 ? sum / n : 0f;
                public string Text => "roughness peak " + F(peak, "F3") + " mean " + F(Mean, "F3") + "; lateral accel peak "
                                      + F(latPeak) + " mean " + F(n > 0 ? latSum / n : 0f) + " m/s2 (" + n + " frames)";
            }

            void SampleFrame(SmoothnessMeter meter, Stat st)
            {
                var t = motor.transform;
                float dt = Time.deltaTime;
                float lat = 0f;
                if (prevValid && dt > 0f)
                {
                    float speed = Flat(t.position - prevPos).magnitude / dt;
                    float yawRate = Mathf.Abs(Mathf.DeltaAngle(prevYaw, t.eulerAngles.y)) * Mathf.Deg2Rad / dt;
                    lat = speed * yawRate;
                }
                prevPos = t.position;
                prevYaw = t.eulerAngles.y;
                prevValid = true;
                st.Add(meter.Roughness01, lat);
            }

            IEnumerator Smoothness()
            {
                Gdd("Bow harpoon: \"a yank or a snap shows on the smoothness meter (pillar #2)\"; \"A load pulls back on her: modestly "
                    + "(a crate is felt, it never tows her)\"; \"Tension near full held ~1.2 s snaps the line\".");
                if (!World() || !GunReady()) yield break;
                var meter = motor.GetComponent<SmoothnessMeter>();
                if (meter == null) meter = FindAnyObjectByType<SmoothnessMeter>();
                if (meter == null) { Fail("SmoothnessMeter", "none on the ship"); yield break; }
                Info("SmoothnessMeter exposes Roughness01 only (EMA, half-life 1.2 s, of heave/pitch/roll rate, lateral accel, gust, wallow); "
                     + "the lateral accel (speed x yaw rate) is measured here the way the meter does it");
                HoldShip(0f);
                yield return WaitFor(() => motor.CurrentSpeed < 0.5f, 20f, true);
                yield return Wait(3f);

                var baseS = new Stat();
                prevValid = false;
                float end = Time.time + 8f;
                while (Time.time < end) { SampleFrame(meter, baseS); yield return null; }
                Info("baseline (idle, no harpoon, 8 s): " + baseS.Text);

                // (a) a normal reel
                int timber0 = voyage != null ? voyage.HeldOf(SeaSick.World.Res.Timber) : 0;
                var crate = FloatingCargo.Spawn(SeaSick.World.Res.Timber, 2, motor.transform, Ahead(18f));
                Undo(() => { if (crate != null) Destroy(crate.gameObject); });
                yield return WaitTarget(crate, "crate");
                var reel = new Stat();
                if (ok)
                {
                    float tFire = Time.time;
                    gun.FireOrCut();
                    prevValid = false;
                    while (Time.time - tFire < 40f)
                    {
                        SampleFrame(meter, reel);
                        if (gun.LastEventAt >= tFire && (gun.LastEventWord == "Aboard" || gun.LastEventWord == "Missed")) break;
                        yield return null;
                    }
                    Info("(a) normal reel of a 2-timber crate, ship idle, until \"" + gun.LastEventWord + "\": " + reel.Text);
                    Check(reel.n > 0 && reel.peak <= Mathf.Max(baseS.peak * 1.5f, baseS.peak + 0.01f), "the normal reel's peak roughness stays within ~1.5x the baseline",
                          F(reel.peak, "F3") + " vs baseline " + F(baseS.peak, "F3") + " (x" + F(reel.peak / Mathf.Max(1e-4f, baseS.peak)) + ")");
                    if (voyage != null) HoldDelta(SeaSick.World.Res.Timber, voyage.HeldOf(SeaSick.World.Res.Timber) - timber0);
                }
                yield return Wait(4f);
                yield return WaitFor(() => gun.State == HarpoonState.Ready, 8f);

                // (b) a forced snap: weak line, heavy load, full astern
                float snap0 = HarpoonTuning.snapForce;
                HarpoonTuning.snapForce = 3f;
                Undo(() => HarpoonTuning.snapForce = snap0);
                var heavy = FloatingCargo.Spawn(SeaSick.World.Res.Stone, 4, motor.transform, Ahead(16f));
                Undo(() => { if (heavy != null) Destroy(heavy.gameObject); });
                yield return WaitTarget(heavy, "heavy crate");
                if (!ok) yield break;
                var snapS = new Stat();
                float t0 = Time.time;
                gun.FireOrCut();
                yield return WaitFor(() => gun.State == HarpoonState.Hooked || (gun.LastEventAt >= t0 && gun.LastEventWord == "Missed"), 5f);
                if (gun.State != HarpoonState.Hooked) { Fail("the heavy load hooks", "\"" + gun.LastEventWord + "\", state " + gun.State); yield break; }
                motor.ThrottleOrder = -1f;
                Info("(b) snapForce " + F(snap0) + " -> 3, heavy (4 stone) hooked, full astern (HelmInput off, ThrottleOrder -1)");
                prevValid = false;
                float snappedAt = -1f;
                while (Time.time - t0 < 30f)
                {
                    SampleFrame(meter, snapS);
                    if (snappedAt < 0f && gun.LastEventAt >= t0 && gun.LastEventWord == "Snapped") snappedAt = Time.time;
                    if (snappedAt < 0f && gun.LastEventAt >= t0 && gun.LastEventWord == "Aboard") break;
                    if (snappedAt > 0f && Time.time - snappedAt >= 3f) break;
                    yield return null;
                }
                motor.ThrottleOrder = 0f;
                Check(snappedAt > 0f, "the line snaps under held strain", snappedAt > 0f ? F(snappedAt - t0, "F1") + " s after the press" : "\"" + gun.LastEventWord + "\" (no snap in 30 s)");
                Info("(b) forced snap, press to snap + 3 s: " + snapS.Text + " (x" + F(snapS.peak / Mathf.Max(1e-4f, baseS.peak)) + " the baseline peak; the snap may spike)");
                yield return WaitFor(() => motor.CurrentSpeed < 1f, 15f, true);
                yield return WaitFor(() => gun.State == HarpoonState.Ready, 8f);
            }
        }
    }
}
#endif
