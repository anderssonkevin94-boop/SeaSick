using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.World;

/// How big are the waves, really. Measures the surface over a 600 m patch of
/// the western deep at four sea states and reports it the way oceanography
/// does: significant wave height Hs = 4 x RMS, which is the average of the
/// highest third and the number "wave height" normally means. Also the worst
/// crest-to-trough actually seen, because that is what hits the boat.
///
/// Sampled over AREA at one instant rather than one point over time: a single
/// point measures the wave period as much as its height.
///
/// Play mode, Sea.unity. Writes /tmp/seasick-wavesize.txt.
public class WaveSizeProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("WaveSizeProbe: not in play mode"); return; }
        new GameObject("WaveSizeProbe").AddComponent<WaveSizeProbe>();
    }

    static readonly float[] Severities = { 0.25f, 0.50f, 0.75f, 1.00f };

    /// Is the whole sample patch in undamped water? The threshold is -9 m,
    /// not some deep-sounding number: the open-ocean floor is seabedDepth
    /// = -12 m by design, and the shore factor stops damping at 8 m of depth.
    /// Asking for -30 m found nowhere in the world at all.
    static bool DeepEverywhere(Vector3 centre)
    {
        var h = Island.TerrainHeight;
        if (h == null) return false;
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
            {
                float x = centre.x + i * 320f, z = centre.z + j * 320f;
                if (h(x, z) > -9f) return false;
            }
        return true;
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        ShipMotor motor = FindAnyObjectByType<ShipMotor>();
        HelmInput helm = FindAnyObjectByType<HelmInput>();
        if (helm != null) helm.enabled = false;
        SeaStateController sea = SeaStateController.Instance;
        Rigidbody rb = motor != null ? motor.GetComponent<Rigidbody>() : null;

        // FIND deep water, do not assume it. The first run of this probe
        // warped to a hardcoded (-3200, 0) which turned out to be an ISLAND:
        // the region envelope there is 0 because the shore factor kills waves
        // over land, so it measured damped water and under-reported every
        // number. The whole 600 m patch has to be afloat, not just its centre.
        Vector3 spot = Vector3.zero;
        bool found = false;
        for (float dist = 2000f; dist <= 9000f && !found; dist += 250f)
        {
            for (int b = 0; b < 12 && !found; b++)
            {
                float ang = b / 12f * Mathf.PI * 2f;
                Vector3 c = new Vector3(Mathf.Sin(ang) * dist, 0f, Mathf.Cos(ang) * dist);
                if (DeepEverywhere(c)) { spot = c; found = true; }
            }
        }
        if (!found) { Debug.LogError("WaveSizeProbe: no deep-water patch found"); yield break; }
        if (rb != null)
        {
            rb.position = new Vector3(spot.x, 2f, spot.z);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        if (motor != null) { motor.SailOrder = 0f; motor.Rudder = 0f; }
        yield return new WaitForSeconds(8f);

        sb.AppendLine("WaveSizeProbe — the wave field in open ocean at "
            + spot.ToString("F0") + ", " + (spot.magnitude).ToString("F0") + " m from origin");
        sb.AppendLine("seabed under the whole 600 m patch is below -9 m, past the 8 m shoaling depth (checked, not assumed)");
        sb.AppendLine("NOTE: SwellDirector (the GDD's separate 100 m mountain-sea obstacles)");
        sb.AppendLine("      no longer exists. This is the FFT wave field and nothing else.");
        sb.AppendLine();
        sb.AppendLine("severity   name       Hs (4xRMS)   biggest crest-to-trough   vs the 24.2 m boat");

        for (int s = 0; s < Severities.Length; s++)
        {
            if (sea != null) sea.ForceSeverity(Severities[s]);
            // The spectrum rebuild is throttled; give it time to settle.
            yield return new WaitForSeconds(14f);

            float sum = 0f, sumSq = 0f, lo = 9999f, hi = -9999f;
            int n = 0;
            // 600 m patch, 12 m steps, spread over frames so the one-shot
            // sampler is never asked for thousands of queries in a frame.
            for (int i = -25; i <= 25; i++)
            {
                for (int j = -25; j <= 25; j++)
                {
                    Vector3 p = spot + new Vector3(i * 12f, 0f, j * 12f);
                    if (!OceanSampler.Ready) continue;
                    float h = OceanSampler.SampleImmediate(p).height;
                    sum += h; sumSq += h * h; n++;
                    if (h < lo) lo = h;
                    if (h > hi) hi = h;
                }
                if (i % 5 == 0) yield return null;
            }
            if (n == 0) continue;

            float mean = sum / n;
            float rms = Mathf.Sqrt(Mathf.Max(0f, sumSq / n - mean * mean));
            float hs = 4f * rms;
            float span = hi - lo;
            // The local severity is the guard: if the envelope is damping the
            // patch, this is well below the forced value and the row is void.
            float local = sea != null ? sea.SeaSeverityAt(new Vector2(spot.x, spot.z)) : 0f;
            string name = motor != null ? motor.SeaStateName : "?";
            if (local < Severities[s] * 0.9f)
                sb.AppendLine("   (WARNING: local severity " + local.ToString("F2")
                    + " is below the forced " + Severities[s].ToString("F2") + " — patch is damped)");

            sb.AppendLine(string.Format("{0,8:F2}   {1,-9}  {2,8:F2} m   {3,18:F2} m   {4,10:F2} x hull",
                Severities[s], name, hs, span, span / 24.2f));
        }

        if (sea != null) sea.ReleaseForce();
        sb.AppendLine();
        sb.AppendLine("Hs is the significant wave height (mean of the highest third) —");
        sb.AppendLine("the figure a forecast quotes. Crest-to-trough is the worst single");
        sb.AppendLine("wave in the patch at that instant, which is what the hull meets.");

        System.IO.File.WriteAllText("/tmp/seasick-wavesize.txt", sb.ToString());
        Debug.Log("WaveSizeProbe:\n" + sb);
        Destroy(gameObject);
    }
}
