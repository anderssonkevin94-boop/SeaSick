using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **Do the villagers' feet skate?** (2026-10-01, Kevin: "they just
    /// slide around ... playing an animation and gliding".)
    ///
    /// Every frame, for every body with an Animator (camp villagers and ship
    /// crew), the STANCE foot -- the lower of `foot.L`/`foot.R`, within a few
    /// centimetres of the ground -- is followed in the body's PARENT frame
    /// (the camp, or the ship's deck, so a moving ship does not count as
    /// slip), and its horizontal speed is binned under the locomotion state
    /// playing. A planted foot reads ~0. Only frames where the body itself is
    /// moving, the state is not in a cross-fade and the same foot was the
    /// stance foot last frame are counted.
    ///
    /// Also: the body's sideways speed relative to its own facing (0 when it
    /// only walks forward), and the first 90+ degree turn seen while walking
    /// (a trace of heading, speed and position).
    ///
    /// `WalkSlipProbe.Begin()` in play mode, wait, `WalkSlipProbe.Report()`.
    public class WalkSlipProbe : MonoBehaviour
    {
        static WalkSlipProbe inst;
        static readonly string[] States = { "Walk", "WalkBrisk", "WalkTired", "WalkDeck", "Run", "RunScared",
            "Carry", "HuntWalk", "SickWalk", "Gangway" };

        class Body
        {
            public Transform t, fl, fr;
            public Animator anim;
            public Vector3 lastPos; public float lastYaw;
            public Vector3 lastFoot; public int lastFootSide; public bool seen;
            public float footFloor;
            public readonly List<(float time, float yaw, float speed, Vector3 pos, string state)> trace = new List<(float, float, float, Vector3, string)>();
        }

        readonly Dictionary<Transform, Body> bodies = new Dictionary<Transform, Body>();
        readonly Dictionary<string, List<float>> slip = new Dictionary<string, List<float>>();
        readonly Dictionary<string, List<float>> speed = new Dictionary<string, List<float>>();
        readonly Dictionary<string, List<float>> rate = new Dictionary<string, List<float>>();
        readonly List<float> lateral = new List<float>();
        readonly List<float> yawRate = new List<float>();
        string turnTrace;
        float started;
        int frames;

        public static string Begin()
        {
            if (inst != null) Destroy(inst.gameObject);
            var go = new GameObject("WalkSlipProbe");
            inst = go.AddComponent<WalkSlipProbe>();
            inst.started = Time.time;
            return "begun";
        }

        void LateUpdate()
        {
            // After the Animator: LateUpdate order vs VillagerActing does not
            // matter for the feet, the Animator has already written them.
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            frames++;
            foreach (var a in FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                if (!a.isActiveAndEnabled) continue;
                var root = a.GetComponentInParent<Crew.CrewAgent>();
                Transform t = root != null ? root.transform : null;
                if (t == null) { var rw = a.GetComponentInParent<Combat.RaidWalker>(); if (rw != null) t = rw.transform; }
                if (t == null) continue;
                if (!bodies.TryGetValue(t, out var b))
                {
                    b = new Body { t = t, anim = a };
                    foreach (var x in a.GetComponentsInChildren<Transform>(true))
                    { if (x.name == "foot.L") b.fl = x; if (x.name == "foot.R") b.fr = x; }
                    b.footFloor = 0.125f * a.transform.lossyScale.y / 1.31f;
                    bodies[t] = b;
                }
                if (b.fl == null || b.fr == null) continue;
                Transform frame = t.parent;
                Vector3 P(Vector3 w) => frame != null ? frame.InverseTransformPoint(w) : w;
                Vector3 pos = P(t.position);
                Vector3 fwdL = frame != null ? frame.InverseTransformDirection(t.forward) : t.forward;
                float yaw = Mathf.Atan2(fwdL.x, fwdL.z) * Mathf.Rad2Deg;
                Vector3 pl = P(b.fl.position), pr = P(b.fr.position);
                Vector3 rootP = pos;
                int side = pl.y <= pr.y ? 0 : 1;
                Vector3 foot = side == 0 ? pl : pr;
                bool planted = foot.y - rootP.y < b.footFloor;

                var st = a.GetCurrentAnimatorStateInfo(0);
                string name = null;
                for (int i = 0; i < States.Length; i++)
                    if (st.shortNameHash == Animator.StringToHash(States[i])) { name = States[i]; break; }
                bool fading = a.IsInTransition(0);

                if (b.seen)
                {
                    Vector3 v = (pos - b.lastPos) / dt; v.y = 0f;
                    float bodySpeed = v.magnitude;
                    float dyaw = Mathf.DeltaAngle(b.lastYaw, yaw);
                    if (bodySpeed > 0.1f && name != null)
                    {
                        Vector3 right = new Vector3(Mathf.Cos(yaw * Mathf.Deg2Rad), 0f, -Mathf.Sin(yaw * Mathf.Deg2Rad));
                        lateral.Add(Mathf.Abs(Vector3.Dot(v, right)));
                        yawRate.Add(Mathf.Abs(dyaw) / dt);
                    }
                    if (bodySpeed > 0.1f && name != null && !fading && planted && b.lastFootSide == side)
                    {
                        Vector3 fv = (foot - b.lastFoot) / dt; fv.y = 0f;
                        Add(slip, name, fv.magnitude);
                        Add(speed, name, bodySpeed);
                        Add(rate, name, a.GetFloat(name == "Carry" || name == "HuntWalk" ? "ClipRate" : "WalkRate") * st.speed);
                    }
                    // A 90+ degree turn while walking: the first one, traced.
                    if (turnTrace == null && name != null)
                    {
                        b.trace.Add((Time.time, yaw, bodySpeed, pos, name));
                        while (b.trace.Count > 0 && Time.time - b.trace[0].time > 2.5f) b.trace.RemoveAt(0);
                        if (b.trace.Count > 10)
                        {
                            float turned = Mathf.Abs(Mathf.DeltaAngle(b.trace[0].yaw, yaw));
                            bool walkedBoth = b.trace[0].speed > 0.15f && bodySpeed > 0.15f;
                            if (turned > 90f && walkedBoth)
                            {
                                var sb = new StringBuilder();
                                sb.Append($"turn by {t.name}: {turned:0} deg over {Time.time - b.trace[0].time:0.00} s\n");
                                for (int ti = 0; ti < b.trace.Count; ti += 4)
                                {
                                    var e = b.trace[ti];
                                    sb.Append($"  t+{e.time - b.trace[0].time:0.00}s yaw {e.yaw:0} speed {e.speed:0.00} {e.state} pos ({e.pos.x:0.00},{e.pos.z:0.00})\n");
                                }
                                turnTrace = sb.ToString();
                            }
                        }
                    }
                }
                b.lastPos = pos; b.lastYaw = yaw; b.lastFoot = foot; b.lastFootSide = planted ? side : -1; b.seen = true;
            }
        }

        static void Add(Dictionary<string, List<float>> d, string k, float v)
        {
            if (!d.TryGetValue(k, out var l)) d[k] = l = new List<float>();
            if (l.Count < 50000) l.Add(v);
        }

        static string Stats(List<float> l)
        {
            if (l == null || l.Count == 0) return "-";
            var s = new List<float>(l); s.Sort();
            float m = 0f; foreach (var x in s) m += x; m /= s.Count;
            return $"mean {m:0.000} p50 {s[s.Count / 2]:0.000} p95 {s[(int)(s.Count * 0.95f)]:0.000} max {s[s.Count - 1]:0.000} (n {s.Count})";
        }

        public static string Report(bool withTurn = true)
        {
            if (inst == null) return "not begun";
            var sb = new StringBuilder();
            sb.Append($"{inst.frames} frames, {Time.time - inst.started:0} s, bodies {inst.bodies.Count}\n");
            foreach (var k in States)
            {
                if (!inst.slip.ContainsKey(k)) continue;
                sb.Append($"{k}: SLIP {Stats(inst.slip[k])}\n   body speed {Stats(inst.speed[k])}\n   rate {Stats(inst.rate[k])}\n");
            }
            sb.Append($"lateral m/s (moving): {Stats(inst.lateral)}\n");
            sb.Append($"yaw rate deg/s (moving): {Stats(inst.yawRate)}\n");
            if (withTurn) sb.Append(inst.turnTrace ?? "no 90+ turn seen\n");
            return sb.ToString();
        }

        public static void End() { Release(); if (inst != null) Destroy(inst.gameObject); inst = null; }

        // --- driven walks ------------------------------------------------------
        //
        // `Drive(n, gait)` borrows up to n camp villagers (their CampWorker is
        // switched off so it does not fight) and walks them, frame by frame
        // with the REAL private `CampWorker.Walk`, round a triangle from
        // where each stands: 6 m out, a 90 degree turn, 5 m across, and back
        // (two ~135 degree turns). `gait` = Walk / WalkBrisk / WalkTired /
        // Run / RunScared / Carry. `Release()` hands them back.

        static readonly List<(World.CampWorker w, Vector3[] pts, int at)> driven = new List<(World.CampWorker, Vector3[], int)>();
        static System.Reflection.MethodInfo walkM;
        const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;

        public static string Drive(int n, string gait)
        {
            if (inst == null) Begin();
            Release();
            walkM = typeof(World.CampWorker).GetMethod("Walk", Any, null, new[] { typeof(Vector3), typeof(float) }, null);
            var campF = typeof(World.CampWorker).GetField("camp", Any);
            var sb = new StringBuilder();
            foreach (var w in new List<World.CampWorker>(World.CampWorker.Bodies))
            {
                if (driven.Count >= n) break;
                if (w == null || !w.isActiveAndEnabled || w.OnTower) continue;
                var camp = campF.GetValue(w) as World.Outpost;
                if (camp == null) continue;
                var acting = w.GetComponent<World.VillagerActing>();
                if (acting == null) continue;
                Vector3 a = w.transform.position;
                Vector3 d = camp.CampCentre - a; d.y = 0f;
                if (d.sqrMagnitude < 1f) d = Vector3.forward;
                d.Normalize();
                Vector3 r = new Vector3(d.z, 0f, -d.x);
                var pts = new[] { a + d * 6f, a + d * 6f + r * 5f, a };
                for (int i = 0; i < pts.Length; i++)
                {
                    if (World.CampPath.PushOut(camp, pts[i], out var q)) pts[i] = q;
                    pts[i].y = camp.GroundAt(pts[i]);
                }
                w.enabled = false;
                switch (gait)
                {
                    case "Walk": acting.WalkGait = World.VillagerActing.Gait.Stroll; acting.Set(World.VillagerActing.Mode.None); break;
                    case "WalkTired": acting.WalkGait = World.VillagerActing.Gait.Tired; acting.Set(World.VillagerActing.Mode.None); break;
                    case "Run": acting.WalkGait = World.VillagerActing.Gait.Run; acting.Set(World.VillagerActing.Mode.None); break;
                    case "RunScared": acting.WalkGait = World.VillagerActing.Gait.Scared; acting.Set(World.VillagerActing.Mode.None); break;
                    case "Carry": acting.Set(World.VillagerActing.Mode.Carry, "Timber", 1); break;
                    default: acting.WalkGait = World.VillagerActing.Gait.Errand; acting.Set(World.VillagerActing.Mode.None); break;
                }
                driven.Add((w, pts, 0));
                sb.Append($"{w.name} ");
            }
            return $"driving {driven.Count} ({gait}): {sb}";
        }

        public static string Release()
        {
            foreach (var (w, _, _) in driven)
                if (w != null)
                {
                    w.GetComponent<World.VillagerActing>()?.Set(World.VillagerActing.Mode.None);
                    w.enabled = true;
                }
            int c = driven.Count;
            driven.Clear();
            return $"released {c}";
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < driven.Count; i++)
            {
                var (w, pts, at) = driven[i];
                if (w == null) continue;
                bool there = (bool)walkM.Invoke(w, new object[] { pts[at], dt });
                if (there) driven[i] = (w, pts, (at + 1) % pts.Length);
            }
        }
    }
}
