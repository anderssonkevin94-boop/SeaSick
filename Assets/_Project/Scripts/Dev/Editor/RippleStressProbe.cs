using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;
using SeaSick.Ocean;

/// Ripple-sim stability under abuse: sail at speed in a storm while hammering
/// the sim with splash impulses, and read the field back every 2 s. The gate
/// is that max |offset| stays bounded (a few metres at most) and never goes
/// non-finite — the stretched-spike bug is this field exploding. Plain C#.
/// Run in play mode in Sea.unity. Writes /tmp/seasick-ripplestress.txt.
public class RippleStressProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("RippleStressProbe: not in play mode");
            return;
        }
        RippleStressProbe old = FindAnyObjectByType<RippleStressProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("RippleStressProbe").AddComponent<RippleStressProbe>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        DynamicWaterSim sim = DynamicWaterSim.Instance;
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        if (sim == null || motor == null || SeaStateController.Instance == null)
        {
            Debug.LogError("RippleStressProbe: missing pieces");
            yield break;
        }
        var helm = FindAnyObjectByType<SeaSick.Ship.HelmInput>();
        if (helm != null) helm.enabled = false;

        bool ok = true;
        yield return Leg("A driving + splash spam", sim, motor, 1f, 0.4f, true, sb);
        if (worstGrad > 0.35f) ok = false;
        if (worstBad > 0) ok = false;
        if (worstClamped > 0) ok = false;
        yield return Leg("B stalled in a storm ", sim, motor, 0.12f, 0f, false, sb);
        if (worstGrad > 0.35f) ok = false;
        if (worstBad > 0) ok = false;
        if (worstClamped > 0) ok = false;

        sb.AppendLine("");
        sb.AppendLine("gates: maxGradient < 0.35 m/texel, texelsAtClamp 0, nonFinite 0");
        sb.AppendLine(ok ? "PASS" : "FAIL");

        SeaStateController.Instance.ReleaseForce();
        motor.Rudder = 0f;
        motor.ThrottleOrder = 1f;
        if (helm != null) helm.enabled = true;
        System.IO.File.WriteAllText("/tmp/seasick-ripplestress.txt", sb.ToString());
        Debug.Log("RippleStressProbe:\n" + sb);
        Destroy(gameObject);
    }

    float worstGrad;
    int worstClamped;
    int worstBad;
    float worstAbs;

    IEnumerator Leg(string label, DynamicWaterSim sim, SeaSick.Ship.ShipMotor motor,
                    float sail, float rudder, bool splash, StringBuilder sb)
    {
        worstGrad = 0f;
        worstClamped = 0;
        worstBad = 0;
        worstAbs = 0f;

        SeaStateController.Instance.ForceSeverity(1f);
        motor.ThrottleOrder = sail;
        motor.Rudder = rudder;
        yield return new WaitForSeconds(6f);

        float t0 = Time.time;
        float nextSplash = 0f;
        float nextRead = 2f;
        float speedSum = 0f;
        int speedN = 0;
        while (Time.time - t0 < 30f)
        {
            yield return null;
            float t = Time.time - t0;
            speedSum += motor.CurrentSpeed;
            speedN++;
            if (splash && t >= nextSplash)
            {
                nextSplash = t + 0.15f;
                Vector3 off = new Vector3(
                    UnityEngine.Random.Range(-20f, 20f), 0f,
                    UnityEngine.Random.Range(-20f, 20f));
                DynamicWaterSim.Splash(motor.transform.position + off, 6f, 2.5f);
            }
            if (t >= nextRead)
            {
                nextRead = t + 2f;
                Scan(sim);
            }
        }
        Scan(sim);

        float meanSpeed = speedSum / Mathf.Max(1, speedN);
        sb.AppendLine(string.Format(
            "{0}: meanSpeed={1:F1}m/s  max|offset|={2:F2}m  maxGradient={3:F3}m/texel  atClamp={4}  nonFinite={5}",
            label, meanSpeed, worstAbs, worstGrad, worstClamped, worstBad));
    }

    void Scan(DynamicWaterSim sim)
    {
        int n = sim.Resolution;
        AsyncGPUReadbackRequest req = AsyncGPUReadback.Request(
            sim.SimTexture, 0, 0, n, 0, n, 0, 1, TextureFormat.RGHalf);
        req.WaitForCompletion();
        var d = req.GetData<half2>();
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float h = (float)d[y * n + x].x;
                if (float.IsNaN(h) || float.IsInfinity(h)) { worstBad++; continue; }
                float a = Mathf.Abs(h);
                if (a > worstAbs) worstAbs = a;
                if (a >= 1.99f) worstClamped++;
                if (x + 1 < n)
                {
                    float g = Mathf.Abs((float)d[y * n + x + 1].x - h);
                    if (g > worstGrad) worstGrad = g;
                }
                if (y + 1 < n)
                {
                    float g = Mathf.Abs((float)d[(y + 1) * n + x].x - h);
                    if (g > worstGrad) worstGrad = g;
                }
            }
        }
    }
}
