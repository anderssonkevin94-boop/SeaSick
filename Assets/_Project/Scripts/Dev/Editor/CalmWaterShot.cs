using System.Collections;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.World;

/// **Does sheltered water still have texture?** The chop floor exists because
/// the region envelope multiplied wave height to literal zero near land, which
/// reads as a mirror rather than water. The number that matters is the RMS
/// ratio: small enough that the boat is genuinely sheltered, non-zero so the
/// sea still moves.
///
/// **IT HAS TO GO AND FIND SHELTERED WATER.** This used to call wherever the
/// ship happened to be "inshore" and a point 2500 m west of her "offshore",
/// which was true when she spawned at home and stopped being true when
/// `PlaytestStart` moved her to (-2400, 725) in 180 m of water: it was then
/// comparing open water with open water and reporting a ratio of 0.795, where
/// the number it was written to catch was 0.24. It passed, which is worse than
/// failing.
///
/// And since the island falloff became a SHORT-WAVE term (2026-08-28), "near
/// an island" no longer means sheltered at all -- swell wraps round and rolls
/// on through. Shelter is DEPTH now, so this hunts for genuinely shallow water
/// by reading the seabed, warps the ship to it so the shore grid follows, and
/// asserts the depth it actually found.
///
/// Play mode, Sea.unity. Writes /tmp/seasick-calm.txt and -calm-inshore.png.
public class CalmWaterShot : MonoBehaviour
{
    const float WantDepthMin = 3f, WantDepthMax = 14f;
    static bool running;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("CalmWaterShot: not in play mode"); return; }
        if (running) { Debug.LogError("CalmWaterShot: already running"); return; }
        running = true;
        new GameObject("CalmWaterShot").AddComponent<CalmWaterShot>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        System.IO.File.WriteAllText("/tmp/seasick-calm.txt", "CalmWaterShot: did not finish\n");
        ShipMotor motor = FindAnyObjectByType<ShipMotor>();
        HelmInput helm = FindAnyObjectByType<HelmInput>();
        if (helm != null) helm.enabled = false;
        SeaStateController sea = SeaStateController.Instance;
        RegionField region = RegionField.Instance;
        Rigidbody rb = motor != null ? motor.GetComponent<Rigidbody>() : null;
        if (motor != null) { motor.SailOrder = 0f; motor.Rudder = 0f; }
        if (region == null || sea == null) { Finish(sb, "ABORT: no RegionField / SeaStateController"); yield break; }

        // The gentlest weather the game ever produces.
        sea.ForceSeverity(0f);
        float wait = 0f;
        while (Island.All.Count == 0 && wait < 20f) { wait += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(5f);
        if (Island.All.Count == 0) { Finish(sb, "ABORT: no islands generated"); yield break; }

        sb.AppendLine("CalmWaterShot -- is sheltered water still moving?");
        sb.AppendLine("severity forced to 0 (the calmest the sea ever gets)");
        sb.AppendLine();

        // FIND shallow water. Shelter is depth, not proximity to an island:
        // since the island falloff became short-wave-only the swell rolls
        // straight past a headland, and only the seabed lies it down.
        Vector3 shipPos = motor != null ? motor.transform.position : Vector3.zero;
        Island isle = null;
        float best = float.MaxValue;
        foreach (var i in Island.All)
        {
            if (i == null) continue;
            float d = Vector2.Distance(new Vector2(i.transform.position.x, i.transform.position.z),
                                       new Vector2(shipPos.x, shipPos.z)) - i.MaxRadius;
            if (d < best) { best = d; isle = i; }
        }
        Vector2 centre = new Vector2(isle.transform.position.x, isle.transform.position.z);
        Vector2 outward = (new Vector2(shipPos.x, shipPos.z) - centre).normalized;

        // Park her off the island first so the shore grid streams in, then read
        // the seabed off it to pick the shallow station.
        Vector2 park = centre + outward * (isle.MaxRadius * 0.45f);
        if (rb != null)
        {
            rb.position = new Vector3(park.x, 2f, park.y);
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        }
        yield return new WaitForSeconds(4f);

        // Take the SHALLOWEST station on the ray, not the first one inside the
        // band. An anchorage is the shallowest water she can safely lie in, and
        // stopping at the first hit walking inward finds the DEEP end of the
        // band every time -- which measured 13.9 m and reported the swell
        // rolling through at 0.96, correctly, about water that is not an
        // anchorage.
        Vector2 inshore = Vector2.zero; float inshoreDepth = float.MaxValue; bool found = false;
        var prm = region.Params;
        for (float r = isle.MaxRadius * 0.9f; r > 5f; r -= 5f)
        {
            Vector2 q = centre + outward * r;
            float d = prm.ShoreWetDepth(new float2(q.x, q.y), region.Shore).z;
            if (d > 1e8f) continue;                       // outside the grid: no seabed known
            if (d < WantDepthMin || d > WantDepthMax) continue;
            if (d < inshoreDepth) { inshore = q; inshoreDepth = d; found = true; }
        }
        if (!found) { sea.ReleaseForce(); Finish(sb, $"ABORT: no water {WantDepthMin}-{WantDepthMax} m deep found off '{isle.name}'"); yield break; }

        // Sit her in it so the grid is centred on the water being measured.
        if (rb != null)
        {
            rb.position = new Vector3(inshore.x, 2f, inshore.y);
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        }
        yield return new WaitForSeconds(4f);
        float3 envIn = prm.EvaluateCascades(new float2(inshore.x, inshore.y),
                                            region.Islands, region.Shore, region.Weather);
        float shallow = RmsAt(new Vector3(inshore.x, 0f, inshore.y), 90);
        yield return null;

        sb.AppendLine($"island '{isle.name}'; sheltered station ({inshore.x:F0}, {inshore.y:F0}) " +
                      $"in {inshoreDepth:F1} m of water");
        sb.AppendLine($"  envelope there: swell {envIn.x:F3}  mid {envIn.y:F3}  chop {envIn.z:F3}");
        Shoot("/tmp/seasick-calm-inshore.png");
        yield return new WaitForSeconds(2f);

        // Now genuinely open water, with the grid taken there too.
        Vector2 deep = centre + outward * (isle.MaxRadius + 2500f);
        if (rb != null)
        {
            rb.position = new Vector3(deep.x, 2f, deep.y);
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        }
        yield return new WaitForSeconds(4f);
        float deepDepth = prm.ShoreWetDepth(new float2(deep.x, deep.y), region.Shore).z;
        float offshore = RmsAt(new Vector3(deep.x, 0f, deep.y), 90);

        sb.AppendLine($"open station    ({deep.x:F0}, {deep.y:F0}) in " +
                      (deepDepth > 1e8f ? "water past the shore grid (deep)" : $"{deepDepth:F1} m"));
        sb.AppendLine();
        sb.AppendLine("surface RMS inshore  " + shallow.ToString("F3") + " m");
        sb.AppendLine("surface RMS offshore " + offshore.ToString("F3") + " m");
        sb.AppendLine("ratio " + (offshore > 0.0001f ? (shallow / offshore).ToString("F3") : "n/a"));
        sb.AppendLine();
        // Two gates, not one: it must still MOVE, and it must still be SHELTER.
        // The old version only checked the first, so a probe measuring open
        // water against open water sailed through it.
        bool moves = shallow > 0.02f;
        bool sheltered = offshore > 0.0001f && shallow / offshore < 0.6f;
        sb.AppendLine((moves ? "PASS " : "FAIL ") + "still-has-texture   inshore RMS " + shallow.ToString("F3") + " m > 0.02");
        sb.AppendLine((sheltered ? "PASS " : "FAIL ") + "still-is-shelter    ratio < 0.60 (if this fails the two stations are the same water)");

        sea.ReleaseForce();
        Finish(sb, null);
    }

    /// RMS of the surface about its own mean, sampled on a grid. Sampling one
    /// point over time would measure the swell period as much as its height.
    static float RmsAt(Vector3 centre, int n)
    {
        if (!OceanSampler.Ready) return 0f;
        float sum = 0f, sumSq = 0f;
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            for (int r = 1; r <= 4; r++)
            {
                Vector3 p = centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (r * 9f);
                float h = OceanSampler.SampleImmediate(p).height;
                sum += h; sumSq += h * h; count++;
            }
        }
        if (count == 0) return 0f;
        float mean = sum / count;
        return Mathf.Sqrt(Mathf.Max(0f, sumSq / count - mean * mean));
    }

    static void Shoot(string path)
    {
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
    }

    void Finish(StringBuilder sb, string err)
    {
        running = false;
        if (err != null) sb.AppendLine(err);
        System.IO.File.WriteAllText("/tmp/seasick-calm.txt", sb.ToString());
        Debug.Log("CalmWaterShot:\n" + sb);
        Destroy(gameObject);
    }
}
