using System.Collections;
using System.Text;
using UnityEngine;

/// Sails the ship through the western deep and watches the RIG rather than the
/// water: how often the camera's "never go under the surface" clamp fires, and
/// how far apart the camera and the hull get vertically.
///
/// The clamp samples the sea at the CAMERA's position, roughly 20m astern of
/// the ship. In a big sea that is a different wave from the one the hull is on,
/// so a crest passing under the lens shoves it up while the ship is still in
/// the trough — which looks exactly like the boat dropping away underwater.
/// It barely mattered while the rig sat at a fixed 19m; it matters a great deal
/// now that a storm pulls it down to about 10m.
[DefaultExecutionOrder(800)]
public class SailShot : MonoBehaviour
{
    public const string OutPath = "/tmp/seasick-sail.txt";

    SeaSick.Ship.ShipMotor motor;
    SeaSick.Ocean.WaveField field;
    Camera cam;
    bool sampling;

    int frames, clamped;
    float pushMax, gapMin = 9999f, gapMax = -9999f;
    float seaAtCamMin = 9999f, seaAtCamMax = -9999f;
    float shipMin = 9999f, shipMax = -9999f;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SailShot: not in play mode"); return; }
        var old = FindAnyObjectByType<SailShot>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("SailShot").AddComponent<SailShot>();
    }

    IEnumerator Start()
    {
        motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        field = SeaSick.Ocean.WaveField.Instance;
        cam = Camera.main;
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        Vector3 home = voyage != null && voyage.HomePoint != null
            ? voyage.HomePoint.position : Vector3.zero;
        if (motor == null || cam == null || field == null) yield break;

        // Put her out west and let her sail — no pinning, this is the real case.
        motor.transform.position = new Vector3(home.x - 1500f, motor.transform.position.y, home.z);
        yield return new WaitForSeconds(5f);

        sampling = true;
        for (int i = 0; i < 5; i++)
        {
            yield return new WaitForSeconds(3f);
            string path = $"/tmp/seasick-sail-{i}.png";
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
        }
        sampling = false;

        var sb = new StringBuilder();
        sb.AppendLine("Sailing free at 1500m west.");
        sb.AppendLine($"frames {frames}   camera clamped to the surface on " +
                      $"{(frames > 0 ? 100f * clamped / frames : 0f):F0}% of them");
        sb.AppendLine($"   worst upward shove from the clamp: {pushMax:F1} m");
        sb.AppendLine($"   camera above hull:  {gapMin:F1} .. {gapMax:F1} m  " +
                      $"(swing {(gapMax - gapMin):F1} m)");
        sb.AppendLine($"   sea under the CAMERA: {seaAtCamMin:F1} .. {seaAtCamMax:F1} m");
        sb.AppendLine($"   hull y:               {shipMin:F1} .. {shipMax:F1} m");
        sb.AppendLine($"   sea state {field.SeaState01:F2}");
        System.IO.File.WriteAllText(OutPath, sb.ToString());
        Debug.Log("SailShot done\n" + sb);
    }

    void LateUpdate()
    {
        if (!sampling || cam == null || motor == null || field == null) return;
        Vector3 cp = cam.transform.position;
        float seaAtCam = field.SampleHeight(new Vector2(cp.x, cp.z), Time.time);
        float floor = seaAtCam + 2.6f;   // ChaseCamera.minHeightAboveWater

        frames++;
        // Sitting within a few centimetres of the floor means the clamp put it
        // there this frame rather than the framing.
        if (cp.y <= floor + 0.05f) clamped++;
        float shipY = motor.transform.position.y;
        float push = floor - shipY - 10f;   // how far above a nominal rig the floor sits
        if (push > pushMax) pushMax = push;

        float gap = cp.y - shipY;
        if (gap < gapMin) gapMin = gap;
        if (gap > gapMax) gapMax = gap;
        if (seaAtCam < seaAtCamMin) seaAtCamMin = seaAtCam;
        if (seaAtCam > seaAtCamMax) seaAtCamMax = seaAtCam;
        if (shipY < shipMin) shipMin = shipY;
        if (shipY > shipMax) shipMax = shipY;
    }
}
