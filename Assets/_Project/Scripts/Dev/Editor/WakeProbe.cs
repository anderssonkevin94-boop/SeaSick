using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;
using SeaSick.Ocean;

/// Ripple-sim acceptance: (a) a splash impulse puts real energy into the
/// field and it decays away after the source stops; (b) a hull under way
/// leaves a wake (energy while sailing well above the parked level);
/// (c) screenshot of the wake for eyeballing. Plain C# for Coplay.
/// Writes /tmp/seasick-wake.txt and /tmp/seasick-wake.png.
public class WakeProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("WakeProbe: not in play mode");
            return;
        }
        new GameObject("WakeProbe").AddComponent<WakeProbe>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        DynamicWaterSim sim = DynamicWaterSim.Instance;
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        if (sim == null || motor == null)
        {
            Debug.LogError("WakeProbe: missing pieces");
            yield break;
        }
        Rigidbody rb = motor.GetComponent<Rigidbody>();
        // HelmInput reasserts Rudder and ThrottleOrder every frame from the live stick (the recorded trap):
        // silence it while the probe owns the ship.
        var helm = FindAnyObjectByType<SeaSick.Ship.HelmInput>();
        if (helm != null) helm.enabled = false;

        // Park the ship in calm water.
        SeaStateController.Instance.ForceSeverity(0f);
        motor.ThrottleOrder = 0f;
        motor.Rudder = 0f;
        rb.linearVelocity = Vector3.zero;
        yield return new WaitForSeconds(10f);
        float baseline = Energy(sim);

        // (a) splash: inject, expect a spike, then decay.
        DynamicWaterSim.Splash(motor.transform.position + Vector3.right * 15f, 7f, 2f);
        yield return new WaitForSeconds(0.5f);
        float afterSplash = Energy(sim);
        yield return new WaitForSeconds(8f);
        float decayed = Energy(sim);

        // (b) wake under way.
        motor.ThrottleOrder = 1f;
        yield return new WaitForSeconds(14f);
        float sailing = Energy(sim);
        ScreenCapture.CaptureScreenshot("/tmp/seasick-wake.png");
        yield return null;

        sb.AppendLine(string.Format("baseline={0:F1}  afterSplash={1:F1}  decayed(8s)={2:F1}  sailing={3:F1}",
            baseline, afterSplash, decayed, sailing));
        bool pass = afterSplash > baseline * 3f + 5f
            && decayed < afterSplash * 0.35f
            && sailing > baseline * 3f + 5f;
        if (pass) sb.AppendLine("PASS");
        else sb.AppendLine("FAIL");
        SeaStateController.Instance.ReleaseForce();
        if (helm != null) helm.enabled = true;
        System.IO.File.WriteAllText("/tmp/seasick-wake.txt", sb.ToString());
        Debug.Log("WakeProbe:\n" + sb);
        Destroy(gameObject);
    }

    static float Energy(DynamicWaterSim sim)
    {
        int n = sim.Resolution;
        AsyncGPUReadbackRequest req = AsyncGPUReadback.Request(
            sim.SimTexture, 0, 0, n, 0, n, 0, 1, TextureFormat.RGHalf);
        req.WaitForCompletion();
        var d = req.GetData<half2>();
        double sum = 0;
        for (int i = 0; i < d.Length; i++)
            sum += Mathf.Abs((float)d[i].x) + (float)d[i].y;
        return (float)sum;
    }
}
