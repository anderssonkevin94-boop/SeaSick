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

        SeaStateController.Instance.ForceSeverity(1f);
        motor.SailOrder = 1f;
        motor.Rudder = 0.4f;   // constant turn keeps the wake stamping hard
        yield return new WaitForSeconds(4f);

        float maxAbs = 0f;
        int badTexels = 0;
        float t0 = Time.time;
        float nextSplash = 0f;
        float nextRead = 2f;
        while (Time.time - t0 < 30f)
        {
            yield return null;
            float t = Time.time - t0;
            if (t >= nextSplash)
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
                float m = MaxAbs(sim, ref badTexels);
                if (m > maxAbs) maxAbs = m;
            }
        }
        float finalMax = MaxAbs(sim, ref badTexels);
        if (finalMax > maxAbs) maxAbs = finalMax;

        sb.AppendLine(string.Format(
            "30s splash+wake storm stress: max|offset|={0:F2}m  nonFiniteTexels={1}",
            maxAbs, badTexels));
        bool pass = badTexels == 0 && maxAbs < 3f;
        sb.AppendLine(pass ? "PASS" : "FAIL");

        SeaStateController.Instance.ReleaseForce();
        motor.Rudder = 0f;
        if (helm != null) helm.enabled = true;
        System.IO.File.WriteAllText("/tmp/seasick-ripplestress.txt", sb.ToString());
        Debug.Log("RippleStressProbe:\n" + sb);
        Destroy(gameObject);
    }

    static float MaxAbs(DynamicWaterSim sim, ref int badTexels)
    {
        int n = sim.Resolution;
        AsyncGPUReadbackRequest req = AsyncGPUReadback.Request(
            sim.SimTexture, 0, 0, n, 0, n, 0, 1, TextureFormat.RGHalf);
        req.WaitForCompletion();
        var d = req.GetData<half2>();
        float m = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float x = (float)d[i].x;
            if (float.IsNaN(x) || float.IsInfinity(x)) { badTexels++; continue; }
            float a = Mathf.Abs(x);
            if (a > m) m = a;
        }
        return m;
    }
}
