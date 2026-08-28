using System.Text;
using UnityEngine;
using SeaSick.Ocean;

/// Measures the two things "the ocean feels alive" actually means, in metres,
/// because both of them are invisible to a screenshot:
///
///   1. GRADUAL OVER DISTANCE. Sweeps west from home and reports Hs and, more
///      importantly, the RATIO of wave height gained per 100 m sailed. Ratio
///      rather than metres because the eye reads wave height as a ratio: 3->6 m
///      is the same step as 30->60. A crossing that doubles the sea every
///      200 m is the "abrupt" complaint no matter how smooth its curve looks.
///
///   2. NEVER STATIC, NEVER REPEATING. Scrubs OceanTime over half an hour at a
///      fixed spot and reports the spread, the drift rate, and the closest the
///      trace ever comes to repeating itself.
///
/// Reads the shipped rule through SeaStateController.TargetHsAt/SetFactor
/// rather than re-deriving it, so it cannot certify a sea that no longer
/// exists -- the failure mode DivergenceProbe shipped with for a fortnight.
///
/// Plain C# for Coplay. Run in play mode in Sea.unity.
/// Writes /tmp/seasick-livingsea.txt.
public class LivingSeaTrace : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("LivingSeaTrace: not in play mode"); return; }

        var ctrl = SeaStateController.Instance;
        var region = RegionField.Instance;
        if (ctrl == null || region == null)
        {
            Debug.LogError("LivingSeaTrace: no SeaStateController / RegionField");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== LivingSeaTrace ===");

        // The severity ladder, so every number below can be read against it.
        sb.AppendLine("\n-- severity -> Hs (the blend coordinate is not a unit) --");
        for (float s = 0f; s <= 1.0001f; s += 0.1f)
        {
            int band = 0;
            sb.AppendLine($"  {s:F2}  {ctrl.HsAt(s),6:F1} m   {SeaStateController.NameForHs(ctrl.HsAt(s), ref band)}");
        }

        // 1. Distance. Sample the steady-state weather along a westward line,
        // with the wander pinned at one instant so this measures geography and
        // not the clock.
        var homeGo = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        Vector2 home = homeGo != null && homeGo.HomePoint != null
            ? new Vector2(homeGo.HomePoint.position.x, homeGo.HomePoint.position.z)
            : Vector2.zero;

        double tFixed = 1000.0;
        sb.AppendLine("\n-- west from home, weather target (wander pinned at t=1000) --");
        sb.AppendLine("   m west     Hs      x/100m   state");
        float prev = -1f;
        float worstRatio = 1f; float worstAt = 0f;
        for (float d = 0f; d <= 3000f; d += 100f)
        {
            Vector2 p = home + new Vector2(-d, 0f);
            float hs = ctrl.TargetHsAt(p, tFixed);
            float ratio = prev > 0f ? hs / prev : 1f;
            if (d > 0f && ratio > worstRatio) { worstRatio = ratio; worstAt = d; }
            int band = 0;
            sb.AppendLine($"  {d,7:F0}  {hs,7:F2}   {(d > 0f ? ratio.ToString("F3") : "  -  "),6}   {SeaStateController.NameForHs(hs, ref band)}");
            prev = hs;
        }
        sb.AppendLine($"  steepest gradient: x{worstRatio:F2} per 100 m, at {worstAt:F0} m west");
        sb.AppendLine("  (x1.15/100 m or gentler reads as growing; x1.5 reads as a wall)");

        // 2. Time. Half an hour at a fixed offshore spot, sets included.
        Vector2 spot = home + new Vector2(-800f, 0f);
        const int N = 361;          // 30 min at 5 s
        var trace = new float[N];
        for (int i = 0; i < N; i++)
        {
            double t = i * 5.0;
            trace[i] = ctrl.TargetHsAt(spot, t) * ctrl.SetFactor(t);
        }
        float min = float.MaxValue, max = 0f, sum = 0f, maxStep = 0f;
        for (int i = 0; i < N; i++)
        {
            min = Mathf.Min(min, trace[i]); max = Mathf.Max(max, trace[i]); sum += trace[i];
            if (i > 0) maxStep = Mathf.Max(maxStep, Mathf.Abs(trace[i] - trace[i - 1]) / 5f);
        }
        sb.AppendLine($"\n-- 30 min at 800 m west, sets included --");
        sb.AppendLine($"  Hs  min {min:F2}  mean {sum / N:F2}  max {max:F2}   spread x{max / Mathf.Max(min, 0.01f):F2}");
        sb.AppendLine($"  fastest change {maxStep * 60f:F2} m/min");

        // Closest repeat: the smallest RMS difference between the trace and
        // itself shifted, over shifts of a minute or more. A sea driven by one
        // period scores near zero here and reads as a loop.
        float bestRms = float.MaxValue; int bestLag = 0;
        for (int lag = 12; lag < N / 2; lag++)
        {
            float acc = 0f; int n = 0;
            for (int i = 0; i + lag < N; i++) { float d2 = trace[i] - trace[i + lag]; acc += d2 * d2; n++; }
            float rms = Mathf.Sqrt(acc / n);
            if (rms < bestRms) { bestRms = rms; bestLag = lag; }
        }
        sb.AppendLine($"  closest self-repeat: {bestRms:F2} m RMS at {bestLag * 5}s lag " +
                      $"({bestRms / Mathf.Max(sum / N, 0.01f) * 100f:F0}% of mean -- under 5% would read as a loop)");

        // 3. What a set actually does, in metres, at three sea sizes. This is
        // the number that shipped wrong: setDepth used to be a fraction of
        // SEVERITY and 0.32 of it took 57% off the waves.
        sb.AppendLine("\n-- sets and lulls, as they land --");
        float sfMin = 1f, sfMax = 1f;
        for (int i = 0; i < 2000; i++)
        {
            float f = ctrl.SetFactor(i * 3.0);
            sfMin = Mathf.Min(sfMin, f); sfMax = Mathf.Max(sfMax, f);
        }
        sb.AppendLine($"  set factor ranges x{sfMin:F3} .. x{sfMax:F3}");
        foreach (float baseHs in new[] { 3.5f, 14f, 65f })
            sb.AppendLine($"  a {baseHs,5:F1} m sea breathes {baseHs * sfMin,6:F2} .. {baseHs * sfMax,6:F2} m");

        System.IO.File.WriteAllText("/tmp/seasick-livingsea.txt", sb.ToString());
        Debug.Log("LivingSeaTrace: wrote /tmp/seasick-livingsea.txt");
    }
}
