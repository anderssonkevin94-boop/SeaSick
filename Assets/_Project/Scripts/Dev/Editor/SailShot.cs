using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;

/// Sails the western deep on the live system and watches the things that used
/// to go wrong: how tightly the hull tracks the surface (draft statistics),
/// how often the camera's never-go-under clamp fires, and the camera/hull gap.
/// Compare against /tmp/seasick-sail-baseline.txt (the old kinematic system's
/// last run — normalise judgements against what changed).
/// Writes /tmp/seasick-sail.txt and five screenshots. Plain C# on purpose.
public class SailShot : MonoBehaviour
{
    public const string OutPath = "/tmp/seasick-sail.txt";

    SeaSick.Ship.ShipMotor motor;
    Camera cam;
    bool sampling;

    int frames;
    int clamped;
    float gapMin = 9999f;
    float gapMax = -9999f;
    float draftSum;
    float draftSqSum;
    int draftN;
    float rollMax;
    float pitchMax;
    float speedSum;
    int shots;

    public static void Execute()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("SailShot: not in play mode");
            return;
        }
        SailShot old = FindAnyObjectByType<SailShot>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("SailShot").AddComponent<SailShot>();
    }

    IEnumerator Start()
    {
        motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        cam = Camera.main;
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        Vector3 home = voyage != null && voyage.HomePoint != null
            ? voyage.HomePoint.position : Vector3.zero;
        if (motor == null || cam == null) yield break;

        // Out west, seated on the surface, sailing free — the real case.
        Rigidbody rb = motor.GetComponent<Rigidbody>();
        Vector3 spot = new Vector3(home.x - 1500f, 0f, home.z);
        float h0 = OceanSampler.Ready ? OceanSampler.SampleImmediate(spot).height : 0f;
        if (rb != null)
        {
            rb.position = new Vector3(spot.x, h0 + 0.5f, spot.z);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        motor.ThrottleOrder = 1f;
        yield return new WaitForSeconds(6f);

        sampling = true;
        float t0 = Time.time;
        while (Time.time - t0 < 40f)
        {
            yield return null;
            if (shots < 5 && Time.time - t0 > shots * 8f)
            {
                ScreenCapture.CaptureScreenshot("/tmp/seasick-sail-" + shots + ".png");
                shots++;
            }
        }
        Report();
        Destroy(gameObject);
    }

    void LateUpdate()
    {
        if (!sampling || motor == null || !OceanSampler.Ready) return;
        frames++;

        Vector3 hp = motor.transform.position;
        float surface = OceanSampler.SampleImmediate(hp).height;
        float draft = surface - hp.y;
        draftSum += draft;
        draftSqSum += draft * draft;
        draftN++;

        float roll = Signed(motor.transform.eulerAngles.z);
        float pitch = Signed(motor.transform.eulerAngles.x);
        if (Mathf.Abs(roll) > rollMax) rollMax = Mathf.Abs(roll);
        if (Mathf.Abs(pitch) > pitchMax) pitchMax = Mathf.Abs(pitch);
        speedSum += motor.CurrentSpeed;

        Vector3 cp = cam.transform.position;
        float camSurface = OceanSampler.SampleImmediate(cp).height;
        if (cp.y - camSurface < 2.65f) clamped++;
        float gap = cp.y - hp.y;
        if (gap < gapMin) gapMin = gap;
        if (gap > gapMax) gapMax = gap;
    }

    void Report()
    {
        float draftMean = draftSum / Mathf.Max(1, draftN);
        float draftVar = draftSqSum / Mathf.Max(1, draftN) - draftMean * draftMean;
        float draftRms = Mathf.Sqrt(Mathf.Max(0f, draftVar));
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Sailing free at 1500m west (rigidbody ocean).");
        sb.AppendLine(string.Format("frames {0}  camera at clamp height on {1:P0} of them",
            frames, clamped / (float)Mathf.Max(1, frames)));
        sb.AppendLine(string.Format("   camera above hull: {0:F1} .. {1:F1} m", gapMin, gapMax));
        sb.AppendLine(string.Format(
            "   draft: mean {0:F2} m  rms-about-mean {1:F2} m  (hull tracking the surface)",
            draftMean, draftRms));
        sb.AppendLine(string.Format("   max roll {0:F1} deg  max pitch {1:F1} deg", rollMax, pitchMax));
        sb.AppendLine(string.Format("   mean speed {0:F1} m/s", speedSum / Mathf.Max(1, frames)));
        sb.AppendLine(string.Format("   sea severity {0:F2}  state {1}", motor.SeaSeverity01, motor.SeaStateName));
        System.IO.File.WriteAllText(OutPath, sb.ToString());
        Debug.Log("SailShot:\n" + sb);
    }

    static float Signed(float a)
    {
        if (a > 180f) return a - 360f;
        return a;
    }
}
