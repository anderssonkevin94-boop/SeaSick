using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;

/// Diagnostic trace of the BuryProbe run: same setup (severity 1.0, phase
/// pinned, dead into the seas) but instead of a verdict it logs a time series
/// of where she is, how deep, how much lift and plow are acting, and — the
/// cross-check that matters — the one-shot sampler against the batched
/// samples the physics itself used. If those two disagree the draft numbers
/// are an instrument fault, not a sinking ship.
/// Run in play mode in Sea.unity. Plain C# for Coplay.
/// Writes /tmp/seasick-burytrace.txt.
public class BuryTrace : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("BuryTrace: not in play mode");
            return;
        }
        BuryTrace old = FindAnyObjectByType<BuryTrace>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("BuryTrace").AddComponent<BuryTrace>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        if (motor == null || SeaStateController.Instance == null || !OceanSampler.Ready)
        {
            Debug.LogError("BuryTrace: missing pieces");
            yield break;
        }
        Rigidbody rb = motor.GetComponent<Rigidbody>();
        BuoyantBody body = motor.GetComponent<BuoyantBody>();
        var helm = FindAnyObjectByType<SeaSick.Ship.HelmInput>();
        if (helm != null) helm.enabled = false;

        SeaStateController.Instance.ForceSeverity(1f);
        Vector3 spot = new Vector3(-1500f, 0f, 0f);
        yield return new WaitForSeconds(8f);
        OceanTime.Scrub(500.0);
        yield return new WaitForSeconds(1f);
        float h0 = OceanSampler.SampleImmediate(spot).height;
        rb.position = new Vector3(spot.x, h0 + 0.5f, spot.z);
        rb.rotation = Quaternion.identity;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        Vector2 w = SeaStateController.Instance.WindDirection;
        Vector3 seasFrom = new Vector3(-w.x, 0f, -w.y).normalized;
        motor.SailOrder = 1f;
        motor.AutopilotTarget = rb.position + seasFrom * 5000f;

        sb.AppendLine("t     shipY   sampleH  batchH  draft   sub   reserve  plowkN  speed  pitch  roll");
        float t0 = Time.time;
        float next = 0f;
        while (Time.time - t0 < 60f)
        {
            yield return new WaitForFixedUpdate();
            float t = Time.time - t0;
            if (t < next) continue;
            next += 0.5f;

            Vector3 p = motor.transform.position;
            float sampleH = OceanSampler.SampleImmediate(p).height;
            float batchH = body.MeanWaterHeight;
            float pitch = Mathf.DeltaAngle(0f, motor.transform.eulerAngles.x);
            float roll = Mathf.DeltaAngle(0f, motor.transform.eulerAngles.z);

            sb.AppendLine(string.Format(
                "{0,5:F1} {1,8:F2} {2,8:F2} {3,7:F2} {4,7:F2} {5,5:F2} {6,7:F2} {7,7:F1} {8,6:F1} {9,6:F0} {10,6:F0}",
                t, p.y, sampleH, batchH, sampleH - p.y,
                body.Submersion, body.DebugMaxReserve,
                body.DebugPlowForward.magnitude / 1000f,
                motor.CurrentSpeed, pitch, roll));
        }

        SeaStateController.Instance.ReleaseForce();
        motor.AutopilotTarget = null;
        if (helm != null) helm.enabled = true;
        System.IO.File.WriteAllText("/tmp/seasick-burytrace.txt", sb.ToString());
        Debug.Log("BuryTrace written");
        Destroy(gameObject);
    }
}
