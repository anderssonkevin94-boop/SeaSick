using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.Terrain;

/// **Why the water teleports under the hull WHILE SAILING.**
///
/// `HitchProbe` measured this with the ship sitting at spawn and found a worst
/// pop of 0.27 m. Kevin's 2026-09-16 screen recording, read frame by frame,
/// shows `PerfHUD`'s pop row at 0.56-1.24 m continuously, at a steady 60 fps,
/// in MODERATE water near home. The regime that produces the complaint is one
/// the existing probe never enters, and the gap is four to five times.
///
/// The suspected difference is MOTION. A moving query samples a spatially
/// varying envelope, so anything that steps in SPACE arrives as a step in
/// TIME, scaled by speed. A ship at anchor cannot see it at all.
///
/// TWO INDEPENDENT TESTS, because they fail differently:
///
///   TRACK — a virtual point marched through the water at a fixed speed, with
///   NO SHIP ANYWHERE IN IT. No rigidbody, no buoyancy, no anchor, no
///   grounding. If the ocean alone steps under a moving query, the fault is in
///   the ocean and nothing about the hull matters. The metric is the SECOND
///   difference of surface height: riding over waves is smooth and scores near
///   zero however fast the point moves, while a teleport is an impulse. A
///   stationary point is run alongside as the control.
///
///   HULL — the ship actually sailing, anchor weighed, which is the thing
///   Kevin sees. Metric is the one-frame change in the hull-to-water gap, the
///   same quantity PerfHUD's pop row shows.
///
/// ATTRIBUTION IS THE POINT. Reporting "it jumped 1.2 m" again would add
/// nothing; what is missing is WHICH TERM moved. Every frame records the four
/// things that can move the surface under a fixed XZ, and each jump is charged
/// to whichever stepped on the same frame:
///
///   REBUILD  the spectrum regenerated -- EXACT (`SpectrumRebuilds` changed),
///            not PerfHUD's 0.35 s blame window, which is WIDER than the
///            0.25 s rebuild interval and so can only ever say yes
///   ENV      RegionField's cascade-0 envelope at the query stepped
///   SHORE    TerrainShoreField rebuilt its grid (every 512 m sailed)
///   FOLD     the sampler's inversion left a residual, i.e. did not converge
///   ?        none of the above -- which would be the interesting answer
///
/// Plain C# so Coplay can call it. Play mode, Sea.unity.
/// Writes /tmp/seasick-step.txt.
public class StepProbe : MonoBehaviour
{
    const float Settle = 3f;
    const float RunSeconds = 20f;
    const float JumpFloor = 0.20f;
    const float TrackSpeed = 10f;   // m/s, a brisk sail

    public static void Execute() => Launch(-1f, 0f);
    public static void Storm() => Launch(1f, 0f);
    /// The regime of Kevin's recording, read off its own status line: he was
    /// 2846 m from home and opening, making 6.7-8.3 m/s. That is DEEP water
    /// with the envelope near 1, and it is a different ocean from the berth.
    /// The first valid run of this probe sailed 200 m from spawn in 12 m of
    /// water at env 0.34 -- a third amplitude, depth cap biting -- and found
    /// 0.045 m against the 1.24 m on his screen. Wrong water, not a fix.
    public static void Offshore() => Launch(-1f, 3000f);
    /// THE TEST THAT SETTLES THE REBUILD QUESTION. Coincidence counting was
    /// blocked by the chance baseline (a 4 Hz rebuild lands on 40% of frames at
    /// 10 fps), so this compares MAGNITUDE against rebuild RATE instead, which
    /// no frame rate can confound. If the spectrum rebuild is what steps the
    /// surface, then halving the rate doubles the accumulated change each
    /// rebuild applies, and the worst step must grow; raising the rate must
    /// shrink it. If the steps do not move with the rate, the rebuild is not
    /// the cause whatever the coincidence counts say.
    public static void RebuildSweep() => Launch(-1f, 3000f, true);
    /// The same sweep with the rebuild paid WHOLE instead of sliced over three
    /// frames. If slicing is the mechanism, the rate-dependence must vanish.
    public static void RebuildSweepUnsliced()
    {
        OceanRenderer.SliceSpectrumRebuild = false;
        Launch(-1f, 3000f, true);
    }
    public static void OffshoreStorm() => Launch(1f, 3000f);

    static void Launch(float severity, float offshore, bool sweep = false)
    {
        if (!Application.isPlaying) { Debug.LogError("StepProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<StepProbe>();
        if (old != null) DestroyImmediate(old.gameObject);
        var go = new GameObject("StepProbe");
        var pr = go.AddComponent<StepProbe>();
        pr.pinSeverity = severity;
        pr.offshoreMetres = offshore;
        pr.sweepRebuild = sweep;
    }

    public float pinSeverity = -1f;
    public float offshoreMetres;
    public bool sweepRebuild;

    ShipMotor motor;
    OceanRenderer ocean;
    RegionField region;
    TerrainShoreField shoreField;
    SeaStateController sea;

    int restoreTarget = -1;
    bool restoreBackground, forcedSea;

    struct Row
    {
        public float t, metric, h, env, depth, hs, residual, frameMs;
        public bool rebuilt, shoreRebuilt, envStep;
    }

    IEnumerator Start()
    {
        ocean = OceanRenderer.Instance;
        region = RegionField.Instance;
        sea = SeaStateController.Instance;
        motor = FindAnyObjectByType<ShipMotor>();
        shoreField = FindAnyObjectByType<TerrainShoreField>();
        if (ocean == null || region == null || sea == null)
        {
            Debug.LogError("StepProbe: missing ocean singletons"); yield break;
        }

        // THE LAST RUN OF THIS PROBE WAS VOID BECAUSE OF THIS LINE'S ABSENCE.
        // The editor throttles play mode to ~10 fps whenever its window is not
        // frontmost, and re-activating the app from a shell loop does not beat
        // it -- measured 92-108 ms frames through a run that had asked for 60.
        // The readback ring's staleness is counted in FRAMES, so its cost in
        // seconds is frames x frame time and every number here moves with the
        // frame rate. runInBackground takes focus out of the question entirely.
        restoreBackground = Application.runInBackground;
        Application.runInBackground = true;
        restoreTarget = Application.targetFrameRate;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;

        if (pinSeverity >= 0f) { sea.ForceSeverity(pinSeverity); forcedSea = true; }
        yield return new WaitForSeconds(Settle);

        var sb = new StringBuilder();
        sb.AppendLine($"StepProbe — {RunSeconds:F0}s per run, 60 fps, "
            + $"sea {(forcedSea ? $"PINNED severity {pinSeverity:F2}" : "as she lies")}");
        sb.AppendLine();

        if (offshoreMetres > 0f) yield return WarpOffshore(sb);

        Vector3 origin = motor != null ? motor.transform.position
                                       : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);

        // --- TRACK: no ship in it at all ---------------------------------
        var still = new List<Row>();
        yield return Track("T0  virtual point, STATIONARY (control)", origin, Vector3.zero, still, sb);

        var west = new List<Row>();
        yield return Track($"T1  virtual point, {TrackSpeed:F0} m/s WEST",
                           origin, new Vector3(-1f, 0f, 0f), west, sb);

        var north = new List<Row>();
        yield return Track($"T2  virtual point, {TrackSpeed:F0} m/s NORTH",
                           origin, new Vector3(0f, 0f, 1f), north, sb);

        // --- The rebuild-rate sweep ---------------------------------------
        if (sweepRebuild)
        {
            float hz0 = GetRebuildHz();
            var res = new List<(float hz, float worst, float p99)>();
            foreach (float hz in new[] { 0.5f, 4f, 20f })
            {
                SetRebuildHz(hz);
                yield return new WaitForSeconds(2f);
                var r = new List<Row>();
                yield return Track($"S  {TrackSpeed:F0} m/s west, rebuildHz {hz}",
                                   origin, new Vector3(-1f, 0f, 0f), r, sb);
                var v = new List<float>();
                foreach (var x in r) v.Add(x.metric);
                v.Sort();
                res.Add((hz, P(v, 1f), P(v, .99f)));
            }
            SetRebuildHz(hz0);
            sb.AppendLine($"  (rebuild paid {(OceanRenderer.SliceSpectrumRebuild ? "SLICED over 3 frames" : "WHOLE")})");

            sb.AppendLine("REBUILD-RATE SWEEP  (magnitude vs rate — immune to frame rate)");
            foreach (var x in res)
                sb.AppendLine($"    rebuildHz {x.hz,5:F1}  worst {x.worst:F3} m   p99 {x.p99:F3} m");
            float slow = res[0].worst, fast = res[2].worst;
            sb.AppendLine(slow > fast * 2f
                ? "  => THE SPECTRUM REBUILD IS THE CAUSE: 40x fewer rebuilds made the\n"
                + "     steps far bigger, which is what batching up the same change does."
                : fast > slow * 2f
                    ? "  => INVERTED — more rebuilds made it worse. Not a batching effect;\n"
                    + "     the rebuild itself is costing something. Look at the dispatch."
                    : "  => THE REBUILD IS NOT THE CAUSE. The steps do not track the rate,\n"
                    + "     so whatever moves the surface is not the spectrum being rebuilt.");
            sb.AppendLine();
        }

        // --- HULL: the ship actually sailing ------------------------------
        var hull = new List<Row>();
        yield return Hull("H1  the ship, sailing", hull, sb);

        sb.AppendLine("VERDICT");
        float s0 = Peak(still), s1 = Peak(west), s2 = Peak(north);
        sb.AppendLine($"  surface 2nd-difference, worst:  stationary {s0:F3}  west {s1:F3}  north {s2:F3} m");
        float moving = Mathf.Max(s1, s2);
        if (s0 > 0.0001f)
            sb.AppendLine($"  moving / stationary = {moving / s0:F1}x");
        sb.AppendLine(moving > s0 * 3f
            ? "  => MOTION THROUGH THE FIELD is what makes the surface step.\n"
            + "     The ocean does this with no ship involved. Read the attribution."
            : "  => motion alone does NOT reproduce it; the fault needs the hull.");
        sb.AppendLine($"  hull one-frame gap change, worst: {Peak(hull):F3} m");

        System.IO.File.WriteAllText("/tmp/seasick-step.txt", sb.ToString());
        Debug.Log("StepProbe: wrote /tmp/seasick-step.txt\n" + sb);
        Unpin();
        Destroy(gameObject);
    }

    /// Puts her in the water the complaint came from. A HARDCODED offshore
    /// coordinate is a trap this project has already paid for -- 1500 m due
    /// west of home is 40 m ABOVE sea level -- so this SEARCHES: it tries
    /// bearings at increasing range and keeps the first that the shore grid
    /// agrees is deep. The grid is centred on the ship and has to be rebuilt
    /// after each hop, which is what the wait is for.
    IEnumerator WarpOffshore(StringBuilder sb)
    {
        if (motor == null) yield break;
        var rb = motor.GetComponent<Rigidbody>();
        Vector2 home = Vector2.zero;
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        if (voyage != null && voyage.HomePoint != null)
            home = new Vector2(voyage.HomePoint.position.x, voyage.HomePoint.position.z);

        Vector3 best = motor.transform.position; float bestDepth = -1f;
        for (int ring = 0; ring < 3 && bestDepth < 100f; ring++)
        {
            float range = offshoreMetres + ring * 700f;
            for (int i = 0; i < 8 && bestDepth < 100f; i++)
            {
                float ang = (i * 45f + 180f) * Mathf.Deg2Rad;   // west first
                var p = new Vector3(home.x + Mathf.Cos(ang) * range, 0f,
                                    home.y + Mathf.Sin(ang) * range);
                yield return Place(rb, p);
                float d = DepthAt(p);
                if (d > bestDepth) { bestDepth = d; best = p; }
            }
        }
        yield return Place(rb, best);

        float env = region.Evaluate(new Vector2(best.x, best.z));
        var at = motor.transform.position;
        float fromHome = Vector2.Distance(new Vector2(at.x, at.z), home);
        sb.AppendLine($"  warped offshore: she is {fromHome:F0} m from home, "
            + $"depth {bestDepth:F0} m, env {env:F3}");
        if (fromHome < offshoreMetres * 0.8f)
            sb.AppendLine("  <-- SHE DID NOT MOVE. Every run below is near home and VOID.");
        else if (env < 0.8f)
            sb.AppendLine("  <-- the envelope is still holding the sea down; this is not open water.");
        sb.AppendLine();
    }

    /// Moving a NON-KINEMATIC rigidbody by its transform does not stick: the
    /// body keeps its own position and snaps back on the next physics step.
    /// One run was lost to exactly that -- the warp reported success and every
    /// measurement after it was taken 3 km away from where it claimed. Set
    /// `rb.position`, and wait for the shore grid to rebuild around the new
    /// spot before believing anything it says about depth.
    IEnumerator Place(Rigidbody rb, Vector3 p)
    {
        Vector3 to = p + Vector3.up * 3f;
        motor.transform.position = to;
        if (rb != null)
        {
            rb.position = to;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        int before = shoreField != null ? shoreField.BuildCount : 0;
        if (shoreField != null) shoreField.MarkDirty();
        float until = Time.realtimeSinceStartup + 4f;
        while (shoreField != null && shoreField.BuildCount == before
               && Time.realtimeSinceStartup < until)
            yield return null;
        yield return new WaitForSeconds(0.4f);
    }

    /// Depth from the shore grid, with the trap spelled out: OUTSIDE the grid
    /// the lookup returns 1e9, which means "no data here", NOT "deep water".
    /// Treating that as deep is what made the first offshore search accept a
    /// spot it had never actually measured.
    float DepthAt(Vector3 p)
    {
        if (region.ShoreN <= 0 || !region.Shore.IsCreated) return -1f;
        float d = region.Params.ShoreWetDepth(new float2(p.x, p.z), region.Shore).z;
        return d > 1e8f ? -1f : d;
    }

    /// Marches a bare query point and scores the SECOND difference of surface
    /// height. Riding waves -- at any speed -- is smooth and scores near zero;
    /// only a discontinuity registers. The stationary run is the control, so
    /// the comparison is against this same statistic and not against intuition.
    IEnumerator Track(string label, Vector3 origin, Vector3 dir, List<Row> rows, StringBuilder sb)
    {
        dir = dir.sqrMagnitude > 0f ? dir.normalized : Vector3.zero;
        int lastRebuild = ocean.SpectrumRebuilds;
        int lastShore = shoreField != null ? shoreField.BuildCount : -1;
        float h1 = 0f, h2 = 0f, prevEnv = 0f;
        int have = 0;
        float t0 = Time.realtimeSinceStartup;

        while (Time.realtimeSinceStartup - t0 < RunSeconds)
        {
            if (!OceanSampler.Ready) { yield return null; continue; }
            float t = Time.realtimeSinceStartup - t0;
            Vector3 p = origin + dir * (TrackSpeed * t);
            var s = OceanSampler.SampleImmediate(p);
            float h = s.height;
            float env = region.Evaluate(new Vector2(p.x, p.z));

            float depth = -1f;
            if (region.ShoreN > 0 && region.Shore.IsCreated)
                depth = region.Params.ShoreWetDepth(new float2(p.x, p.z), region.Shore).z;

            bool rebuilt = ocean.SpectrumRebuilds != lastRebuild;
            if (rebuilt) lastRebuild = ocean.SpectrumRebuilds;
            bool shoreRebuilt = shoreField != null && shoreField.BuildCount != lastShore;
            if (shoreRebuilt) lastShore = shoreField.BuildCount;

            if (have >= 2)
                rows.Add(new Row
                {
                    t = t,
                    metric = Mathf.Abs(h - 2f * h1 + h2),
                    h = h, env = env, envStep = Mathf.Abs(env - prevEnv) > 0.002f,
                    depth = depth, hs = sea.CurrentHs, residual = s.residual,
                    frameMs = Time.unscaledDeltaTime * 1000f,
                    rebuilt = rebuilt, shoreRebuilt = shoreRebuilt,
                });
            h2 = h1; h1 = h; prevEnv = env; have++;
            yield return null;
        }
        Report(label, rows, sb, "2nd-diff of surface height");
    }

    IEnumerator Hull(string label, List<Row> rows, StringBuilder sb)
    {
        if (motor == null) { sb.AppendLine($"--- {label} --- no ShipMotor, skipped\n"); yield break; }

        // Getting her under way through the ship's own systems does not work
        // from a probe, and two runs were wasted proving it: she sits at the
        // home dock, `if (Anchored) targetSpeed = 0`, and even with the anchor
        // cleared `Throttle` only ramps toward `ThrottleOrder` at a rate
        // multiplied by `roster.Labour01` -- so with the hands ashore at an
        // outpost she never makes way at all. Measured: 9 m in 20 s.
        //
        // So the way is IMPOSED on the rigidbody instead. The question here is
        // "does the surface step under a hull moving at speed", not "can she
        // sail", and driving the body directly answers it without depending on
        // crew, sails, wind or the anchor. Vertical velocity is left alone, so
        // buoyancy still does all the floating and the gap stays honest.
        var anchor = FindAnyObjectByType<AnchorController>();
        if (anchor != null) anchor.enabled = false;
        motor.Anchored = false;
        motor.ThrottleOrder = 1f;
        var rb = motor.GetComponent<Rigidbody>();
        if (rb == null) { sb.AppendLine($"--- {label} --- no Rigidbody, skipped\n"); yield break; }
        Vector3 way = motor.transform.forward; way.y = 0f; way.Normalize();
        yield return new WaitForSeconds(Settle);

        int lastRebuild = ocean.SpectrumRebuilds;
        int lastShore = shoreField != null ? shoreField.BuildCount : -1;
        float prevGap = 0f, prevEnv = 0f;
        bool have = false;
        Vector3 start = motor.transform.position;
        float t0 = Time.realtimeSinceStartup;
        float sumSpeed = 0f; int n = 0;

        while (Time.realtimeSinceStartup - t0 < RunSeconds)
        {
            motor.Anchored = false;
            motor.ThrottleOrder = 1f;
            // Re-imposed every frame: drag, the motor and the sea all pull it
            // back down within a frame or two otherwise.
            var v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(way.x * TrackSpeed, v.y, way.z * TrackSpeed);
            if (!OceanSampler.Ready) { yield return null; continue; }

            Vector3 p = motor.transform.position;
            var s = OceanSampler.SampleImmediate(p);
            float gap = p.y - s.height;
            float env = region.Evaluate(new Vector2(p.x, p.z));

            float depth = -1f;
            if (region.ShoreN > 0 && region.Shore.IsCreated)
                depth = region.Params.ShoreWetDepth(new float2(p.x, p.z), region.Shore).z;

            bool rebuilt = ocean.SpectrumRebuilds != lastRebuild;
            if (rebuilt) lastRebuild = ocean.SpectrumRebuilds;
            bool shoreRebuilt = shoreField != null && shoreField.BuildCount != lastShore;
            if (shoreRebuilt) lastShore = shoreField.BuildCount;

            if (have)
                rows.Add(new Row
                {
                    t = Time.realtimeSinceStartup - t0,
                    metric = Mathf.Abs(gap - prevGap),
                    h = gap, env = env, envStep = Mathf.Abs(env - prevEnv) > 0.002f,
                    depth = depth, hs = sea.CurrentHs, residual = s.residual,
                    frameMs = Time.unscaledDeltaTime * 1000f,
                    rebuilt = rebuilt, shoreRebuilt = shoreRebuilt,
                });
            prevGap = gap; prevEnv = env; have = true;
            sumSpeed += motor.CurrentSpeed; n++;
            yield return null;
        }

        float sailed = Vector3.Distance(start, motor.transform.position);
        float mean = n > 0 ? sumSpeed / n : 0f;
        Report(label, rows, sb, "one-frame hull-to-water gap change");
        sb.AppendLine($"  (sailed {sailed:F0} m at {mean:F1} m/s"
            + (sailed < 20f ? "  <-- SHE DID NOT SAIL; THIS RUN IS VOID" : "") + ")");
        sb.AppendLine();
    }

    void Report(string label, List<Row> rows, StringBuilder sb, string what)
    {
        var v = new List<float>();
        foreach (var r in rows) v.Add(r.metric);
        v.Sort();

        int nBig = 0, cReb = 0, cEnv = 0, cShore = 0, cFold = 0, cNone = 0;
        int rebuildFrames = 0;
        float fps = 0f, hs = 0f;
        foreach (var r in rows)
        {
            fps += r.frameMs; hs += r.hs;
            if (r.rebuilt) rebuildFrames++;
            if (r.metric < JumpFloor) continue;
            nBig++;
            if (r.rebuilt) cReb++;
            else if (r.shoreRebuilt) cShore++;
            else if (r.envStep) cEnv++;
            else if (r.residual > 0.05f) cFold++;
            else cNone++;
        }
        int n = Mathf.Max(1, rows.Count);
        float meanMs = fps / n;

        sb.AppendLine($"--- {label} ---");
        sb.AppendLine($"  {rows.Count} frames at {meanMs:F1} ms ({1000f / Mathf.Max(0.01f, meanMs):F0} fps)"
            + (meanMs > 25f ? "  <-- NOT 60 fps, RUN IS SUSPECT" : "")
            + $", mean Hs {hs / n:F2} m");
        sb.AppendLine($"  {what}:  p50 {P(v, .50f):F3}  p95 {P(v, .95f):F3}  "
            + $"p99 {P(v, .99f):F3}  WORST {P(v, 1f):F3} m");
        sb.AppendLine($"  over {JumpFloor:F2} m: {nBig}  [rebuild {cReb}] [shore grid {cShore}] "
            + $"[envelope {cEnv}] [fold {cFold}] [unexplained {cNone}]");

        // THE ATTRIBUTION IS WORTHLESS WITHOUT THIS LINE. A rebuild fires at
        // 4 Hz, so at 60 fps it lands on 1 frame in 15 (7%) and at 10 fps on
        // 2 frames in 5 (40%) -- by chance alone. "9 of 19 jumps had a
        // rebuild" is damning at 60 fps and pure noise at 10, and the raw
        // count cannot tell you which. So the baseline is printed beside it
        // and the two are compared here rather than by eye. Same fault as
        // PerfHUD's 0.35 s blame window against a 0.25 s interval: a test that
        // cannot say no.
        float baseRate = 100f * rebuildFrames / n;
        if (nBig > 0)
        {
            float hitRate = 100f * cReb / nBig;
            sb.AppendLine($"  rebuild landed on {baseRate:F0}% of ALL frames "
                + $"vs {hitRate:F0}% of the big jumps"
                + (baseRate > 25f
                    ? "   <-- baseline too high to conclude anything; needs 60 fps"
                    : hitRate > baseRate * 2f
                        ? "   <-- SIGNIFICANT: jumps prefer rebuild frames"
                        : "   <-- no preference; the rebuild is NOT the cause"));
        }
        else sb.AppendLine($"  rebuild landed on {baseRate:F0}% of all frames");

        rows.Sort((x, y) => y.metric.CompareTo(x.metric));
        sb.AppendLine("  worst five:");
        for (int i = 0; i < Mathf.Min(5, rows.Count); i++)
        {
            var r = rows[i];
            sb.AppendLine($"    t={r.t,6:F2}s  {r.metric,6:F2}m  val={r.h,7:F2} "
                + $"env={r.env,5:F3} depth={r.depth,8:F1} Hs={r.hs,6:F2} "
                + $"res={r.residual,5:F3} frame={r.frameMs,5:F1}ms"
                + (r.rebuilt ? " REBUILD" : "") + (r.shoreRebuilt ? " SHORE" : "")
                + (r.envStep ? " ENVSTEP" : ""));
        }
        sb.AppendLine();
    }

    // rebuildHz is a private serialized field; a probe may drive it but the
    // shipped class should not grow an API for it.
    static System.Reflection.FieldInfo HzField() => typeof(SeaStateController)
        .GetField("rebuildHz", System.Reflection.BindingFlags.Instance
                             | System.Reflection.BindingFlags.NonPublic);
    float GetRebuildHz() { var f = HzField(); return f != null ? (float)f.GetValue(sea) : -1f; }
    void SetRebuildHz(float hz) { HzField()?.SetValue(sea, hz); }

    static float Peak(List<Row> rows)
    {
        float m = 0f;
        foreach (var r in rows) if (r.metric > m) m = r.metric;
        return m;
    }

    static float P(List<float> sorted, float q)
    {
        if (sorted.Count == 0) return float.NaN;
        return sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1)];
    }

    /// Statics and forced state survive leaving play mode here (domain reload
    /// is off), so a run stopped early must still put everything back.
    void Unpin()
    {
        if (forcedSea && SeaStateController.Instance != null)
            SeaStateController.Instance.ReleaseForce();
        forcedSea = false;
        if (motor != null) motor.ThrottleOrder = 0f;
        if (restoreTarget != -1) { Application.targetFrameRate = restoreTarget; restoreTarget = -1; }
        Application.runInBackground = restoreBackground;
    }

    void OnDestroy() => Unpin();
}
