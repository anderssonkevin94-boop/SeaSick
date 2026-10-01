using UnityEngine;

namespace SeaSick.World
{
    /// **Villagers keep their own room (2026-10-01).** Kevin's iPhone: two
    /// villagers merged into one body on the quarry's stand. Every camp body
    /// is a disc of `BodyRadius`; once a frame (whichever body's `LateUpdate`
    /// runs first does the whole camp, O(n²) over ~20 bodies) any two closer
    /// than two radii are eased apart:
    ///
    ///  - a man **working** at his spot is anchored -- the other one yields,
    ///    so a work spot stays exact for the worker using it; a downed or
    ///    landing man never moves either;
    ///  - two that are both free share the push; a man **walking** with
    ///    somebody close ahead also sidesteps to his own right, so two hands
    ///    meeting in a lane pass each other instead of pushing head-on;
    ///  - pushes are slow (`PushSpeed`), never into a wall, a building's
    ///    box, the sea or a cliff (`CampPath.Obstructs`, `Walkability`), and
    ///    never stop a walk: in a lane too narrow to pass, they overlap for a
    ///    moment rather than freeze.
    ///
    /// Not: lookouts on a tower, men on a ladder, sleepers in a hut, a body
    /// in the Hand or in the air.
    public partial class CampWorker
    {
        /// Metres from a body's centre to its edge.
        public static float BodyRadius = 0.36f;
        /// Metres per second two overlapping bodies are eased apart.
        public static float PushSpeed = 0.9f;
        /// Metres per second a walker steps aside for one close ahead.
        public static float PassSpeed = 1.8f;
        /// How far ahead a walker looks for somebody to pass.
        public static float PassLook = 2.5f;
        /// Most a walker turns off his line to pass somebody, degrees.
        public static float PassSteerDeg = 35f;

        static int spacingFrame = -1;
        static readonly System.Collections.Generic.List<CampWorker> spacing = new System.Collections.Generic.List<CampWorker>();

        /// Where this body stood at the last spacing pass: "is he walking".
        Vector3 spacingLast;
        bool spacingSeen;

        /// Closest two bodies came at the last pass (any camp), for checks.
        public static float LastMinGap { get; private set; } = float.MaxValue;
        /// Closest two bodies have come since `RunMinGap` was last reset to
        /// +infinity, for checks.
        public static float RunMinGap = float.MaxValue;
        /// Who those two were, for checks.
        public static string RunMinPair;
        /// Microseconds the last pass took, for checks.
        public static float LastSpacingMicros { get; private set; }

        void LateUpdate()
        {
            int f = Time.frameCount;
            if (f == spacingFrame) return;
            spacingFrame = f;
            SpaceBodies(Time.deltaTime);
        }

        /// 0 = left out, 1 = stays put, 2 = may be moved.
        int SpacingRole()
        {
            if (camp == null || !isActiveAndEnabled || bodyHidden || OnTower || climb.Active) return 0;
            if (phase == Phase.Held || phase == Phase.Flying) return 0;
            if (phase == Phase.Downed || phase == Phase.Landing) return 1;
            if (phase == Phase.Working && !Moving) return 1;
            return 2;
        }

        bool Moving { get; set; }

        static void SpaceBodies(float dt)
        {
            if (dt <= 0f) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            spacing.Clear();
            for (int i = 0; i < Bodies.Count; i++)
            {
                var w = Bodies[i];
                if (w == null) continue;
                Vector3 p = w.transform.position;
                // Moved by his own feet since the last pass (the last pass's
                // nudges are already in `spacingLast`).
                // 0.1 m/s: the slowest walk (tired, 0.29) still counts.
                w.Moving = w.spacingSeen && FlatDistance(p, w.spacingLast) > 0.1f * dt;
                if (w.SpacingRole() != 0) spacing.Add(w);
            }

            float gap = 2f * BodyRadius;
            float minGap = float.MaxValue;
            for (int i = 0; i < spacing.Count; i++)
            {
                var a = spacing[i];
                for (int j = i + 1; j < spacing.Count; j++)
                {
                    var b = spacing[j];
                    if (a.camp != b.camp) continue;
                    Vector3 pa = a.transform.position, pb = b.transform.position;
                    if (Mathf.Abs(pa.y - pb.y) > 1.5f) continue;
                    float dx = pb.x - pa.x, dz = pb.z - pa.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < minGap) minGap = d;
                    if (d < RunMinGap)
                        RunMinPair = $"{a.name}({a.phase},{a.SpacingRole()},{(a.Moving ? "walking" : "standing")}) & "
                                     + $"{b.name}({b.phase},{b.SpacingRole()},{(b.Moving ? "walking" : "standing")}) at {pa:F1} t={Time.time:0}";

                    // Passing: a walker with the other close ahead steps to
                    // his own right (both do, so a head-on pair swings past).
                    if (d < PassLook && d > 1e-4f)
                    {
                        Vector3 ab = new Vector3(dx / d, 0f, dz / d);
                        if (a.Moving) a.Sidestep(ab, d, dt);
                        if (b.Moving) b.Sidestep(-ab, d, dt);
                    }

                    if (d >= gap) continue;
                    Vector3 dir = d > 1e-4f ? new Vector3(dx / d, 0f, dz / d)
                        : Quaternion.AngleAxis((a.GetInstanceID() * 47) % 360, Vector3.up) * Vector3.forward;
                    float push = Mathf.Min(gap - d, PushSpeed * dt);
                    int ra = a.SpacingRole(), rb = b.SpacingRole();
                    if (ra == 2 && rb == 2) { a.Nudge(-dir * (push * 0.5f)); b.Nudge(dir * (push * 0.5f)); }
                    else if (ra == 2) a.Nudge(-dir * push);
                    else if (rb == 2) b.Nudge(dir * push);
                    // Both anchored (two at one tree): the later one yields,
                    // unless he is down.
                    else if (b.phase != Phase.Downed && b.phase != Phase.Landing) b.Nudge(dir * push);
                }
            }
            for (int i = 0; i < Bodies.Count; i++)
            {
                var w = Bodies[i];
                if (w == null) continue;
                w.spacingLast = w.transform.position;
                w.spacingSeen = true;
            }
            LastMinGap = minGap;
            if (minGap < RunMinGap) RunMinGap = minGap;   // (RunMinPair set in the loop)
            LastSpacingMicros = (System.Diagnostics.Stopwatch.GetTimestamp() - t0)
                                * 1e6f / System.Diagnostics.Stopwatch.Frequency;
        }

        void Sidestep(Vector3 toOther, float d, float dt)
        {
            Vector3 fw = transform.forward;
            fw.y = 0f;
            if (fw.sqrMagnitude < 1e-4f) return;
            fw.Normalize();
            if (Vector3.Dot(fw, toOther) < 0.5f) return;            // not ahead of him
            Vector3 right = new Vector3(fw.z, 0f, -fw.x);
            // Already off to one side of him: keep that side.
            float side = Vector3.Dot(right, toOther) > 0.15f ? -1f : 1f;
            float k = 1f - d / PassLook;
            // **Steer past, mostly (2026-10-01):** the walk turns its body
            // up to `PassSteerDeg` off the line (next frame's `Stride`), and
            // only a third of the old sideways shove is left for a body
            // already close -- a sideways slide is a skate.
            passSteer = side * PassSteerDeg * k;
            passSteerFrame = Time.frameCount;
            Nudge(right * (side * PassSpeed * 0.33f * dt * k * k));
        }

        /// Move this body by a small flat offset if that ground is his to
        /// stand on: no wall, no building box, no cliff, no sea.
        void Nudge(Vector3 delta)
        {
            if (delta.sqrMagnitude < 1e-10f) return;
            Vector3 p = transform.position;
            Vector3 q = p + delta;
            q.y = p.y;
            if (CampPath.Obstructs(camp, p, q, FarGoal, CampPath.Walker.Hand, out _)) return;
            if (!Walkability.MayStep(camp, Grounded(p), q, Walkability.Feet.Man)) return;
            q.y = WorkerPad.Foot(q, camp.GroundAt(q));
            transform.position = q;
        }

        /// Is another body standing (not walking) on `spot`? Never true
        /// for this man's own work spot: there the other one yields.
        bool SpotHeldByOther(Vector3 spot)
        {
            var r = row;
            if (r != null && r.order == OutpostOrder.Work)
            {
                var post = WorkPostOf(r);
                if (post != null && FlatDistance(WorkSpot(camp, post), spot) < 0.3f) return false;
            }
            // Held by a body standing on it -- or by one walking that got
            // there first (two haulers converging on the store's one drop
            // spot: the second stops beside the first).
            float near = BodyRadius * 1.2f;
            float mine = FlatDistance(transform.position, spot);
            for (int i = 0; i < Bodies.Count; i++)
            {
                var o = Bodies[i];
                if (o == null || o == this || o.camp != camp || o.SpacingRole() == 0) continue;
                float od = FlatDistance(o.transform.position, spot);
                if (od < near && (!o.Moving || od < mine)) return true;
            }
            return false;
        }

        static readonly Vector3 FarGoal = new Vector3(1e9f, 0f, 1e9f);
    }
}
