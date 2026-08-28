using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;

/// "In calm water she seems to be floating above the water, not in it -- the
/// arms holding the paddle wheels should be on surface level."
///
/// Measures where she actually sits, in metres, against the landmarks that
/// make the complaint checkable. The ship's origin IS the designed waterline
/// (SetupPaddleBoat lifts the visual root by VisualYOffset so the model's
/// waterline lands on it), so `shipY - waterY` is the whole question: positive
/// means she floats HIGHER than she was drawn to float.
///
/// Measured at rest and at full throttle, because the two answers want
/// opposite fixes -- a static offset is displacement, a speed-dependent one is
/// dynamic lift.
///
/// Sea pinned with ForceHs and OceanTime left running, then averaged over
/// several seconds: a single frame of a moving sea proves nothing.
///
/// Plain C# for Coplay. Run in play mode in Sea.unity.
/// Writes /tmp/seasick-waterline.txt.
public class WaterlineProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("WaterlineProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<WaterlineProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("WaterlineProbe").AddComponent<WaterlineProbe>();
    }

    Transform portWheel, stbdWheel;
    float wheelRadius;
    bool pin; Vector3 pinAt;

    IEnumerator Start()
    {
        var sb = new StringBuilder("=== WaterlineProbe ===\n");
        var motor = FindAnyObjectByType<ShipMotor>();
        var ctrl = SeaStateController.Instance;
        if (motor == null || ctrl == null || !OceanSampler.Ready)
        {
            Debug.LogError("WaterlineProbe: no ship / controller / sampler");
            yield break;
        }
        var rb = motor.GetComponent<Rigidbody>();
        var drive = motor.GetComponent<PaddleDrive>();
        wheelRadius = drive != null ? drive.WheelRadius : 2.53f;
        portWheel = FindDeep(motor.transform, "PaddleWheel_Port");
        stbdWheel = FindDeep(motor.transform, "PaddleWheel_Stbd");
        if (portWheel == null)
        {
            sb.AppendLine("no PaddleWheel_Port under the ship -- wheel numbers omitted");
        }

        // Pin the calmest sea the open ocean ever serves, so this measures
        // flotation and not which wave she happened to be on.
        ctrl.ForceHs(1.6f);
        yield return new WaitForSeconds(3f);

        sb.AppendLine($"sea pinned at Hs {ctrl.CurrentHs:F2} m ({ctrl.CurrentStateName}), " +
                      $"wheel radius {wheelRadius:F2} m");

        // --- at rest -------------------------------------------------------
        pinAt = motor.transform.position; pin = true;
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        yield return new WaitForSeconds(6f);          // let her settle
        yield return Sample(sb, motor, "AT REST", 4f);

        // --- under way -----------------------------------------------------
        pin = false;
        yield return new WaitForSeconds(20f);          // build to full speed
        yield return Sample(sb, motor, $"UNDER WAY ({(rb != null ? rb.linearVelocity.magnitude : 0f):F1} m/s)", 4f);

        // The complaint is visual, so end on a picture of the thing measured:
        // her at rest in the pinned calm, which is the case that reads wrong.
        pinAt = motor.transform.position; pin = true;
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        yield return new WaitForSeconds(6f);
        ScreenCapture.CaptureScreenshot("/tmp/seasick-waterline.png");
        yield return new WaitForSeconds(1.5f);
        pin = false;

        ctrl.ReleaseForce();
        System.IO.File.WriteAllText("/tmp/seasick-waterline.txt", sb.ToString());
        Debug.Log("WaterlineProbe: wrote /tmp/seasick-waterline.txt");
        Destroy(gameObject);
    }

    IEnumerator Sample(StringBuilder sb, ShipMotor motor, string label, float seconds)
    {
        float draft = 0f, arm = 0f, bite = 0f, deck = 0f, keel = 0f;
        int n = 0;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            Vector3 p = motor.transform.position;
            float waterY = OceanSampler.SampleImmediate(p).height;

            // The ship origin is the DESIGNED waterline. Positive = riding high.
            draft += p.y - waterY;
            // Landmarks in ship space, from SetupPaddleBoat's measured model:
            // keel -0.51, deck 1.23, both at ReferenceScale 1.7 and the boat
            // ships at 3.4, so K = 2.
            keel += (p.y - 1.02f) - waterY;
            deck += (p.y + 2.46f) - waterY;
            if (portWheel != null)
            {
                float axleY = portWheel.position.y;
                float wy = OceanSampler.SampleImmediate(portWheel.position).height;
                arm += axleY - wy;                 // the arms sit at the axle
                bite += wy - (axleY - wheelRadius); // how deep the blades reach
            }
            n++;
            yield return null;
        }
        sb.AppendLine($"\n-- {label}, mean of {n} frames --");
        sb.AppendLine($"  designed waterline above water   {draft / n,7:F2} m   <- 0 = sitting exactly as drawn");
        sb.AppendLine($"  keel below water                 {-keel / n,7:F2} m");
        sb.AppendLine($"  deck above water                 {deck / n,7:F2} m");
        if (portWheel != null)
        {
            sb.AppendLine($"  wheel axle (the arms) above water{arm / n,7:F2} m   <- Kevin wants this near 0");
            sb.AppendLine($"  paddle bite depth                {bite / n,7:F2} m   <- designed 1.32 m");
        }
    }

    void LateUpdate()
    {
        if (!pin) return;
        var motor = FindAnyObjectByType<ShipMotor>();
        if (motor == null) return;
        // x/z only -- leave y and rotation free, which is the whole measurement.
        var p = motor.transform.position;
        motor.transform.position = new Vector3(pinAt.x, p.y, pinAt.z);
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var f = FindDeep(root.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }
}
