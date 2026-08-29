using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;

/// **Why the ship pops above and under the water.**
///
/// Kevin's report was "the ocean skips to a different frame every now and
/// again and I'm either submerged or above the water". Two very different
/// things produce that and they need telling apart, because the fix is not
/// the same:
///
///   1. a FRAME HITCH — the picture stops, the world keeps moving, and the
///      hull arrives somewhere new when it resumes; or
///   2. the SAMPLER CLOCK drifting from the rendered surface — the ship rides
///      `OceanSampler.SurfaceTime` while the eye sees `OceanTime.Now`, so the
///      hull is riding water that is not the water being drawn. The readback
///      ring is asynchronous, so this widens exactly when the frame time
///      spikes and is why the two get confused.
///
/// So this samples all three together every frame and reports them against
/// each other, rather than reporting a frame-rate number that could not tell
/// you which one you have.
///
/// Plain C# so Coplay can call it. Play mode, Sea.unity. NOT in an Editor
/// folder — a MonoBehaviour there cannot be AddComponent-ed.
/// Writes /tmp/seasick-hitch.txt.
public class HitchProbe : MonoBehaviour
{
    const float Duration = 30f;

    public static void Execute() => Launch(false);

    /// The regime the complaint actually came from, which the plain run does
    /// NOT reproduce and quietly passes instead.
    ///
    /// Two things have to be pinned or the measurement is theatre:
    ///
    ///   SEA. Spawn water is moderate, and both faults scale with the sea --
    ///   a 1.5 deg rotation of a calm spectrum moves the surface centimetres,
    ///   a storm's metres. Probes that trust wherever the ship happens to
    ///   float have certified a calm sea before in this project.
    ///
    ///   FRAME RATE. This is the one that made the first A/B meaningless. The
    ///   readback ring's staleness is counted in FRAMES, so its cost in
    ///   SECONDS -- which is what the hull feels -- is frames x frame time.
    ///   Driven from Coplay with the Game view focused the editor runs at
    ///   ~215 fps, where even the old 3-slot ring is stale by 11 ms and
    ///   nothing is visible. At 60 fps the same ring is stale by 50-65 ms and
    ///   refreshes the sampled surface every 3rd or 4th frame, which is the
    ///   staircase. Measuring the fix at 215 fps proves only that 215 fps
    ///   hides it.
    public static void Storm() => Launch(true);

    static void Launch(bool storm)
    {
        if (!Application.isPlaying) { Debug.LogError("HitchProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<HitchProbe>();
        if (old != null) Destroy(old.gameObject);
        var go = new GameObject("HitchProbe");
        go.AddComponent<HitchProbe>().storm = storm;
    }

    public bool storm;
    int restoreTarget = -1;
    bool forcedSea;

    IEnumerator Start()
    {
        var motor = FindAnyObjectByType<ShipMotor>();
        if (motor == null) { Debug.LogError("HitchProbe: no ShipMotor"); yield break; }

        if (storm)
        {
            // 60 fps, because the ring's staleness in seconds is frames x
            // frame time and the fault lives in the seconds.
            restoreTarget = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            var sea = SeaStateController.Instance;
            if (sea == null) { Debug.LogError("HitchProbe: no SeaStateController"); yield break; }
            sea.ForceSeverity(1f);
            forcedSea = true;
            // The spectrum rebuild is throttled, so the pinned sea needs a
            // moment to actually BE the pinned sea before sampling starts.
            yield return new WaitForSeconds(3f);
        }

        // Let the readback ring fill before the first sample, or the opening
        // frames report a lag that is just start-up.
        yield return new WaitForSeconds(2f);

        var ocean = OceanRenderer.Instance;
        int rebuilds0 = ocean != null ? ocean.SpectrumRebuilds : -1;
        int lastRebuilds = rebuilds0;
        int rebuildPops = 0;
        float worstRebuildPop = 0f;

        int n = 0;
        var dt = new System.Collections.Generic.List<float>(4096);
        var lag = new System.Collections.Generic.List<float>(4096);
        var err = new System.Collections.Generic.List<float>(4096);
        var worst = new StringBuilder();

        float prevErr = 0f;
        float t0 = Time.realtimeSinceStartup;

        while (Time.realtimeSinceStartup - t0 < Duration)
        {
            float frameMs = Time.unscaledDeltaTime * 1000f;

            // How far behind the drawn surface the SAMPLED surface is.
            float readbackLag = OceanSampler.Ready
                ? (float)(OceanTime.Now - OceanSampler.SurfaceTime) : float.NaN;

            // Where the hull sits against the water directly under it.
            Vector3 p = motor.transform.position;
            float surface = OceanSampler.Ready
                ? OceanSampler.SampleImmediate(p).height : float.NaN;
            float gap = p.y - surface;              // + above water, - submerged

            // A POP is a sudden change in the gap, not a large gap: a boat
            // riding low is fine, a boat that teleports 3 m between frames is
            // the complaint.
            float jump = n > 0 ? Mathf.Abs(gap - prevErr) : 0f;
            prevErr = gap;

            // Did the spectrum regenerate on THIS frame? A rebuild applies the
            // whole accumulated axis turn at once, so a pop landing on one is
            // the weather gate, and a pop landing anywhere else is not. Without
            // this the two are indistinguishable in the output and the wrong
            // constant gets tuned.
            bool rebuiltNow = false;
            if (ocean != null && ocean.SpectrumRebuilds != lastRebuilds)
            {
                lastRebuilds = ocean.SpectrumRebuilds;
                rebuiltNow = true;
                if (n > 0)
                {
                    if (jump > 0.05f) rebuildPops++;
                    if (jump > worstRebuildPop) worstRebuildPop = jump;
                }
            }

            dt.Add(frameMs); lag.Add(readbackLag); err.Add(gap);

            if (n > 0 && (frameMs > 40f || jump > 0.75f || (rebuiltNow && jump > 0.25f)))
                worst.AppendLine($"  t={Time.realtimeSinceStartup - t0,6:F2}s  "
                    + $"frame={frameMs,7:F1}ms  readbackLag={readbackLag,6:F3}s  "
                    + $"gap={gap,7:F2}m  JUMP={jump,6:F2}m"
                    + (rebuiltNow ? "  <- SPECTRUM REBUILD" : ""));

            n++;
            yield return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"HitchProbe — {n} frames over {Duration:F0}s");
        sb.AppendLine(Pct("frame time (ms)", dt));
        sb.AppendLine(Pct("readback lag (s)", lag));
        sb.AppendLine(Pct("hull gap (m)", err));
        sb.AppendLine();
        if (ocean != null)
        {
            int total = ocean.SpectrumRebuilds - rebuilds0;
            sb.AppendLine($"  spectrum rebuilds   : {total} in {Duration:F0}s "
                + $"({total / Duration:F2}/s), {rebuildPops} of them moved the hull "
                + $"more than 0.05 m, worst {worstRebuildPop:F2} m");
        }
        sb.AppendLine();
        sb.AppendLine($"Sea: {(storm ? "PINNED storm (severity 1)" : "whatever the ship drifted into")}"
            + $", frame rate: {(storm ? "capped 60" : "editor's own")}");
        sb.AppendLine("Frames over 40 ms, hull jumps over 0.75 m, or over 0.25 m on a rebuild:");
        sb.Append(worst.Length == 0 ? "  (none)\n" : worst.ToString());

        System.IO.File.WriteAllText("/tmp/seasick-hitch.txt", sb.ToString());
        Debug.Log("HitchProbe: wrote /tmp/seasick-hitch.txt");
        Unpin();
        Destroy(gameObject);
    }

    /// Statics and forced state survive leaving play mode in this project
    /// (domain reload is disabled), so a run that is stopped early must still
    /// let the sea go.
    void Unpin()
    {
        if (forcedSea && SeaStateController.Instance != null)
            SeaStateController.Instance.ReleaseForce();
        forcedSea = false;
        if (restoreTarget != -1) { Application.targetFrameRate = restoreTarget; restoreTarget = -1; }
    }

    void OnDestroy() => Unpin();

    static string Pct(string name, System.Collections.Generic.List<float> v)
    {
        var s = new System.Collections.Generic.List<float>();
        foreach (var x in v) if (!float.IsNaN(x)) s.Add(x);
        if (s.Count == 0) return $"  {name,-20}: no valid samples";
        s.Sort();
        float At(float f) => s[Mathf.Clamp(Mathf.RoundToInt(f * (s.Count - 1)), 0, s.Count - 1)];
        float mean = 0f; foreach (var x in s) mean += x; mean /= s.Count;
        return $"  {name,-20}: mean {mean,8:F3}   p50 {At(0.5f),8:F3}   "
             + $"p95 {At(0.95f),8:F3}   p99 {At(0.99f),8:F3}   max {s[s.Count-1],8:F3}";
    }
}
