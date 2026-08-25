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

        var buoy = motor != null ? motor.GetComponent<SeaSick.Ocean.BuoyantBody>() : null;
        var bilge = motor != null ? motor.GetComponent<Bilge>() : null;

        var sb = new StringBuilder();
        sb.AppendLine("SinkTrack — full throttle, rudder amidships");
        sb.AppendLine("rail = green water over the deepest rail probe (the real 'is she under'");
        sb.AppendLine("number); gap is against the UNFILTERED surface, so a small offset is");
        sb.AppendLine("expected and correct now that the hull feels a hull-length-filtered sea.");
        sb.AppendLine();
        sb.AppendLine("    t      x        z       ship_y   surface   gap     speed   vy    rail   buried  bilge");
        float worstGap = float.MaxValue; float tWorst = 0f;
        float worstRail = float.MinValue; float tRail = 0f;
        int buriedSteps = 0, steps = 0;

        float t = 0f;
        while (t < Seconds)
        {
            Vector3 p = motor.transform.position;
            float surface = OceanSampler.Ready ? OceanSampler.SampleImmediate(p).height : 0f;
            float gap = p.y - surface;
            float vy = rb != null ? rb.linearVelocity.y : 0f;
            if (gap < worstGap) { worstGap = gap; tWorst = t; }

            float rail = buoy != null ? buoy.MaxRailImmersion : 0f;
            bool buried = buoy != null && buoy.Buried;
            float bilge01 = bilge != null ? bilge.Bilge01 : 0f;
            if (rail > worstRail) { worstRail = rail; tRail = t; }
            if (buried) buriedSteps++;
            steps++;

            sb.AppendLine($"  {t,4:F0}  {p.x,8:F0} {p.z,8:F0}  {p.y,8:F1}  {surface,8:F1}  "
                        + $"{gap,6:F1}  {motor.CurrentSpeed,6:F1}  {vy,6:F1}"
                        + $"  {rail,6:F2}  {(buried ? "  YES" : "    -"),6}  {bilge01,5:F2}");

            yield return new WaitForSeconds(2f);
            t += 2f;
        }

        sb.AppendLine();
        sb.AppendLine($"worst hull-below-surface gap {worstGap:F1} m at t={tWorst:F0}s");
        sb.AppendLine($"worst green water over the rail {worstRail:F2} m at t={tRail:F0}s");
        sb.AppendLine($"burial clamp engaged on {(steps > 0 ? 100f * buriedSteps / steps : 0f):F0}% of samples"
            + $"  ({buriedSteps}/{steps})");
        sb.AppendLine($"bilge at the end {(bilge != null ? bilge.Bilge01 : 0f):F2}");
        sb.AppendLine(worstRail > 2f
            ? "VERDICT: she is still being buried deeply — the clamp is not holding her."
            : worstRail > 0.4f
                ? "VERDICT: she takes green water aboard but is not swimming. That is the intent."
                : "VERDICT: dry. If the sea is genuinely mountainous this may be TOO dry.");

        System.IO.File.WriteAllText("Temp/sink-track.txt", sb.ToString());
        Debug.Log("SinkTrack: done — Temp/sink-track.txt");
        Destroy(gameObject);
    }
}
