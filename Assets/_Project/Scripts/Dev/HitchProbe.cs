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

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("HitchProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<HitchProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("HitchProbe").AddComponent<HitchProbe>();
    }

    IEnumerator Start()
    {
        var motor = FindAnyObjectByType<ShipMotor>();
        if (motor == null) { Debug.LogError("HitchProbe: no ShipMotor"); yield break; }

        // Let the readback ring fill before the first sample, or the opening
        // frames report a lag that is just start-up.
        yield return new WaitForSeconds(2f);

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

            dt.Add(frameMs); lag.Add(readbackLag); err.Add(gap);

            if (n > 0 && (frameMs > 40f || jump > 0.75f))
                worst.AppendLine($"  t={Time.realtimeSinceStartup - t0,6:F2}s  "
                    + $"frame={frameMs,7:F1}ms  readbackLag={readbackLag,6:F3}s  "
                    + $"gap={gap,7:F2}m  JUMP={jump,6:F2}m");

            n++;
            yield return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"HitchProbe — {n} frames over {Duration:F0}s");
        sb.AppendLine(Pct("frame time (ms)", dt));
        sb.AppendLine(Pct("readback lag (s)", lag));
        sb.AppendLine(Pct("hull gap (m)", err));
        sb.AppendLine();
        sb.AppendLine("Frames over 40 ms, or where the hull jumped more than 0.75 m:");
        sb.Append(worst.Length == 0 ? "  (none)\n" : worst.ToString());

        System.IO.File.WriteAllText("/tmp/seasick-hitch.txt", sb.ToString());
        Debug.Log("HitchProbe: wrote /tmp/seasick-hitch.txt");
        Destroy(gameObject);
    }

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
