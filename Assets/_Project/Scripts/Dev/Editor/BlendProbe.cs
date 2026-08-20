using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean2;

/// Weather-blend acceptance: force Calm, then ramp the forced severity to
/// full Storm over 45 s while measuring Hs at a fixed grid every second.
/// The rise must be smooth (no 1 s step above 18% of the range — steps mean
/// the throttled rebuild pops) and end near the pure-storm Hs. Screenshots at
/// the ends and middle catch ghosting. Writes /tmp/seasick-blend.txt.
/// Plain C# on purpose: the Coplay script compiler chokes on idiomatic forms.
public class BlendProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("BlendProbe: not in play mode");
            return;
        }
        new GameObject("BlendProbe").AddComponent<BlendProbe>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        SeaStateController ctrl = SeaStateController.Instance;
        OceanRenderer ocean = OceanRenderer.Instance;
        if (ctrl == null || ocean == null)
        {
            Debug.LogError("BlendProbe: missing pieces");
            yield break;
        }

        ctrl.ForceSeverity(0f);
        yield return new WaitForSeconds(5f);

        float[] series = new float[51];
        series[0] = MeasureHs();
        ScreenCapture.CaptureScreenshot("/tmp/seasick-blend-0.png");

        for (int s = 1; s <= 50; s++)
        {
            ctrl.ForceSeverity(Mathf.Clamp01(s / 45f));
            yield return new WaitForSeconds(1f);
            series[s] = MeasureHs();
            if (s == 25) ScreenCapture.CaptureScreenshot("/tmp/seasick-blend-1.png");
        }
        ScreenCapture.CaptureScreenshot("/tmp/seasick-blend-2.png");

        float hsCalm = series[0];
        float hsStorm = series[50];
        float range = Mathf.Max(hsStorm - hsCalm, 0.01f);
        float maxStep = 0f;
        for (int s = 1; s <= 50; s++)
        {
            float step = Mathf.Abs(series[s] - series[s - 1]);
            if (step > maxStep) maxStep = step;
        }
        float maxStepPct = maxStep / range * 100f;

        sb.AppendLine(string.Format("calm Hs={0:F2}m  after ramp Hs={1:F2}m", hsCalm, hsStorm));
        sb.AppendLine(string.Format("max 1s step: {0:F1}% of range", maxStepPct));
        for (int s = 0; s <= 50; s += 5)
            sb.AppendLine(string.Format("  t={0,2}s Hs={1:F2}", s, series[s]));
        bool pass = maxStepPct < 18f && hsStorm > 4.0f;
        if (pass) sb.AppendLine("PASS");
        else sb.AppendLine("FAIL");

        ctrl.ReleaseForce();
        System.IO.File.WriteAllText("/tmp/seasick-blend.txt", sb.ToString());
        Debug.Log("BlendProbe:\n" + sb);
        Destroy(gameObject);
    }

    /// Hs proxy: 4 x std of heights over a coarse spatial grid.
    static float MeasureHs()
    {
        double sum = 0;
        double sq = 0;
        int n = 0;
        for (int gx = -12; gx <= 12; gx++)
        {
            for (int gz = -12; gz <= 12; gz++)
            {
                Vector3 p = new Vector3(gx * 17.3f, 0f, gz * 17.3f);
                float h = OceanSampler.SampleImmediate(p).height;
                sum += h;
                sq += h * h;
                n++;
            }
        }
        double mean = sum / n;
        double variance = sq / n - mean * mean;
        if (variance < 0) variance = 0;
        return 4f * Mathf.Sqrt((float)variance);
    }
}
