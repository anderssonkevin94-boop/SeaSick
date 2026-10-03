using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ship;

/// Does the engine actually drive her, ahead and astern?
///
/// Astern is new physics, not a rename: the old sail model could only ask
/// for a fraction of full ahead, and `steerageWay` meant a shut throttle
/// still ghosted her along at 12% of top speed. Both of those had to change
/// for "hold S to back off a beach" to mean anything, and neither is
/// something a screenshot can confirm.
public class DriveProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("DriveProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<DriveProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("DriveProbe").AddComponent<DriveProbe>();
    }

    IEnumerator Start()
    {
        var motor = FindAnyObjectByType<ShipMotor>();
        var helm = FindAnyObjectByType<HelmInput>();
        if (motor == null) { Debug.LogError("DriveProbe: no ShipMotor"); yield break; }

        // HelmInput reasserts Rudder and ThrottleOrder every frame from the live stick (a zero stick writes zero) --
        // the recorded trap in this project. Drive the OWNER, not the value.
        if (helm != null) helm.enabled = false;

        var sb = new StringBuilder();
        var rb = motor.GetComponent<Rigidbody>();

        motor.ThrottleOrder = 0f;
        yield return new WaitForSeconds(10f);
        sb.AppendLine($"throttle 0 held 10 s -> {Fwd(motor, rb):F2} m/s "
            + "(the old furled sail still made about 2.4 m/s here)");

        motor.ThrottleOrder = 1f;
        yield return new WaitForSeconds(16f);
        float v = Fwd(motor, rb);
        sb.AppendLine($"full ahead 16 s   -> {v:F2} m/s = {v * 1.94384f:F1} knots   engine at {motor.Throttle:F2}");

        motor.ThrottleOrder = -1f;
        float t = 0f; float minSpeed = 999f;
        while (t < 20f)
        {
            t += 0.25f;
            yield return new WaitForSeconds(0.25f);
            minSpeed = Mathf.Min(minSpeed, Fwd(motor, rb));
        }
        sb.AppendLine($"full astern 20 s  -> {Fwd(motor, rb):F2} m/s   engine at {motor.Throttle:F2}"
            + $"   (most sternway reached {minSpeed:F2} m/s)");
        sb.AppendLine(minSpeed < -0.5f ? "ASTERN WORKS" : "ASTERN FAILED — she will not back up");

        if (helm != null) helm.enabled = true;
        System.IO.File.WriteAllText("/tmp/seasick-drive.txt", sb.ToString());
        Debug.Log("DriveProbe\n" + sb);
        Destroy(gameObject);
    }

    static float Fwd(ShipMotor m, Rigidbody rb)
        => rb == null ? 0f : Vector3.Dot(rb.linearVelocity, m.transform.forward);
}
