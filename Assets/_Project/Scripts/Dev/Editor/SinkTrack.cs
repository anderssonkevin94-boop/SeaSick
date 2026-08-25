using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;

/// Does she stay on top of the water at twenty knots?
///
/// OceanShadingCheck found the hull at y = -97 m after about a minute of
/// full throttle into the storm, which is sixty metres below the deepest
/// trough in the field. That is either a real sinking, a physics blow-up, or
/// the ship having sailed somewhere the sea is different. This logs the whole
/// minute a line at a time instead of catching one instant, because the shape
/// of the curve says which: a steady slide is buoyancy losing, a step is a
/// blow-up, and a recovery is just a deep trough.
///
/// Play mode, Sea.unity. Writes Temp/sink-track.txt.
public class SinkTrack : MonoBehaviour
{
    public static float Seconds = 90f;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SinkTrack: not in play mode"); return; }
        new GameObject("SinkTrack").AddComponent<SinkTrack>();
    }

    IEnumerator Start()
    {
        var motor = FindAnyObjectByType<ShipMotor>();
        var rb = motor != null ? motor.GetComponent<Rigidbody>() : null;
        var helm = FindAnyObjectByType<HelmInput>();
        if (helm != null) helm.enabled = false;   // nothing steers but this probe

        yield return new WaitForSeconds(4f);
        if (motor != null) { motor.SailOrder = 1f; motor.Rudder = 0f; }

        var sb = new StringBuilder();
        sb.AppendLine("SinkTrack — full throttle, rudder amidships");
        sb.AppendLine("    t      x        z       ship_y   surface   gap     speed   vy");
        float worstGap = float.MaxValue; float tWorst = 0f;

        float t = 0f;
        while (t < Seconds)
        {
            Vector3 p = motor.transform.position;
            float surface = OceanSampler.Ready ? OceanSampler.SampleImmediate(p).height : 0f;
            float gap = p.y - surface;
            float vy = rb != null ? rb.linearVelocity.y : 0f;
            if (gap < worstGap) { worstGap = gap; tWorst = t; }

            sb.AppendLine($"  {t,4:F0}  {p.x,8:F0} {p.z,8:F0}  {p.y,8:F1}  {surface,8:F1}  "
                        + $"{gap,6:F1}  {motor.CurrentSpeed,6:F1}  {vy,6:F1}");

            yield return new WaitForSeconds(2f);
            t += 2f;
        }

        sb.AppendLine();
        sb.AppendLine($"worst hull-below-surface gap {worstGap:F1} m at t={tWorst:F0}s");
        sb.AppendLine(worstGap < -5f
            ? "VERDICT: she goes under and does not come back — this is a sinking, not a trough."
            : "VERDICT: she stays with the surface.");

        System.IO.File.WriteAllText("Temp/sink-track.txt", sb.ToString());
        Debug.Log("SinkTrack: done — Temp/sink-track.txt");
        Destroy(gameObject);
    }
}
