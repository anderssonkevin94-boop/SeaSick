using System.Collections;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using SeaSick.Ocean;
using SeaSick.World;

/// **What the sea does as you come in on an island.** Walks a transect from
/// open water to the beach and reports, at every station, the water depth, the
/// envelope EACH CASCADE gets, and what that leaves of each band in metres.
///
/// The question it exists to answer is whether inshore water is a pond. The
/// envelope has two quite different reasons to flatten the sea near land:
///
/// * the **per-island radial falloff**, `smoothstep(0, shoreFalloff, s)` — a
///   disc drawn round a centre and a radius, which knows nothing about the
///   seabed, the wavelength or which side of the island you are on; and
/// * the **depth terms**, which are the physical ones.
///
/// Only the depth terms belong on the long swell. Real swell wraps round an
/// island and rolls onto the beach; it does not stop dead at a circle 60 m
/// offshore. So the transect prints both causes side by side — `off edge` for
/// the first, `depth` for the second — with the per-cascade envelope beside
/// them, and which one is doing the flattening is then readable.
///
/// Three traps this had to be built around, all of which it hit first time:
///
/// 1. **The shore grid only covers the ship.** Stations beyond it report a
///    depth of 1e9 and get no depth term at all, so averaging bearings around
///    an island silently mixes "in 12 m of water" with "no seabed known". The
///    transect runs from the island's centre TOWARD THE SHIP for that reason,
///    and every station says whether it was in the grid.
/// 2. **A 14 m patch cannot see a 515 m swell.** RMS over a small patch is a
///    chop measurement whatever you call it. Band heights come from the
///    per-cascade RMS read straight off the displacement textures times the
///    envelope, which is exact; the patch RMS is kept and labelled as what it
///    actually is.
/// 3. **A probe that assumes a coordinate lands on an island** — this one
///    finds the island nearest the ship and reports which it picked.
///
/// Reads the shipped formula through `RegionFieldParams.EvaluateCascades`.
/// Play mode, Sea.unity — it needs real terrain. Writes /tmp/seasick-lee.txt.
public class LeeProbe : MonoBehaviour
{
    static readonly float[] Offsets = { -40f, -20f, -5f, 5f, 20f, 40f, 60f, 90f, 130f, 200f, 320f, 500f };
    const int Fan = 5;              // bearings either side of the ship line
    const float FanDeg = 4f;

    // A second instance is not a second run, it is a corrupted one: both
    // fight over the forced sea state and the quality tier, and the output
    // looks plausible. `execute_script` times out constantly and the script
    // usually ran anyway, so double-spawning is the normal accident, not an
    // exotic one.
    static bool running;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("LeeProbe: not in play mode"); return; }
        if (running) { Debug.LogError("LeeProbe: already running -- refusing to start a second"); return; }
        running = true;
        new GameObject("LeeProbe").AddComponent<LeeProbe>();
    }

    IEnumerator Start()
    {
        var sb = new StringBuilder();
        System.IO.File.WriteAllText("/tmp/seasick-lee.txt", "LeeProbe: did not finish\n");

        var sea = SeaStateController.Instance;
        var rf = RegionField.Instance;
        var ocean = OceanRenderer.Instance;
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var helm = FindAnyObjectByType<SeaSick.Ship.HelmInput>();
        if (helm != null) helm.enabled = false;
        if (rf == null || sea == null || ocean == null)
        {
            Finish(sb, $"ABORT regionField={rf != null} seaState={sea != null} ocean={ocean != null}");
            yield break;
        }

        float wait = 0f;
        while (Island.All.Count == 0 && wait < 15f) { wait += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(3f);
        if (Island.All.Count == 0) { Finish(sb, "ABORT: no islands generated"); yield break; }

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
        float radius = isle.MaxRadius;
        Vector2 toShip = (new Vector2(shipPos.x, shipPos.z) - centre).normalized;
        var prm0 = rf.Params;
        sb.AppendLine($"island '{isle.name}' at ({centre.x:F0}, {centre.y:F0}) radius {radius:F0} m; " +
                      $"ship {best:F0} m off its edge, transect runs out along the bearing to her");
        sb.AppendLine($"shore grid {rf.ShoreN} texels a side; {Island.All.Count} islands in the world; " +
                      $"shoreFalloff {prm0.shoreFalloff:F0} m, chopFloor {prm0.chopFloor:F2}, " +
                      $"breakFraction {prm0.breakFraction:F2}");
        sb.AppendLine("distance from home enters through nearScale/farScale and varies along the " +
                      "transect too, so read the CASCADES AGAINST EACH OTHER, not the absolute level.");
        sb.AppendLine();

        foreach (float sev in new[] { 0f, 1f })
        {
            sea.ForceSeverity(sev);
            yield return new WaitForSeconds(6f);

            // Per-cascade RMS of the open sea this state is producing, so the
            // envelope can be reported in metres rather than as a fraction.
            var sigma = new float[3];
            var cas = ocean.Cascades;
            int n = cas.N;
            var reqs = new AsyncGPUReadbackRequest[3];
            for (int c = 0; c < 3; c++)
                reqs[c] = AsyncGPUReadback.Request(cas.Displacement, 0, 0, n, 0, n, c, 1,
                    TextureFormat.RGBAHalf);
            AsyncGPUReadback.WaitAllRequests();
            for (int c = 0; c < 3; c++)
            {
                if (reqs[c].hasError) continue;
                var d = reqs[c].GetData<half4>();
                double sy = 0;
                for (int i = 0; i < d.Length; i++) { float y = d[i].y; sy += (double)y * y; }
                sigma[c] = Mathf.Sqrt((float)(sy / d.Length));
            }

            var prm = rf.Params;
            sb.AppendLine($"=== severity {sev:F2}  (Hs {sea.CurrentHs:F1} m, '{sea.CurrentStateName}', " +
                          $"envelope's nominal Hs {prm.waveHs:F1} m) ===");
            sb.AppendLine($"open-sea RMS per cascade: swell {sigma[0]:F3} m, mid {sigma[1]:F3} m, " +
                          $"chop {sigma[2]:F3} m");
            sb.AppendLine("  off edge  in grid   depth    env c0   env c1   env c2 |  swell m   mid m   chop m | patch RMS");
            foreach (float off in Offsets)
            {
                float r = radius + off;
                if (r < 1f) continue;
                double e0 = 0, e1 = 0, e2 = 0, dep = 0, rms = 0;
                int nn = 0, inGrid = 0;
                for (int b = 0; b < Fan; b++)
                {
                    float a = (b - (Fan - 1) * 0.5f) * FanDeg * Mathf.Deg2Rad;
                    float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                    var dir = new Vector2(toShip.x * cs - toShip.y * sn, toShip.x * sn + toShip.y * cs);
                    var p = centre + dir * r;
                    var pf = new float2(p.x, p.y);
                    float3 env = prm.EvaluateCascades(pf, rf.Islands, rf.Shore, rf.Weather);
                    e0 += env.x; e1 += env.y; e2 += env.z;
                    float dd = prm.ShoreWetDepth(pf, rf.Shore).z;
                    // 1e9 is the "no seabed known here" sentinel; never average it in.
                    if (dd < 1e8f) { dep += dd; inGrid++; }
                    rms += RmsAt(new Vector3(p.x, 0f, p.y));
                    nn++;
                    yield return null;      // the sampler has a per-frame budget
                }
                string depth = inGrid > 0 ? (dep / inGrid).ToString("F1") : "  --  ";
                sb.AppendLine($"  {off,8:F0}  {inGrid,3}/{nn,-3}  {depth,7}  " +
                              $"{e0 / nn,8:F3} {e1 / nn,8:F3} {e2 / nn,8:F3} | " +
                              $"{e0 / nn * sigma[0],8:F3}{e1 / nn * sigma[1],8:F3}{e2 / nn * sigma[2],8:F3} | " +
                              $"{rms / nn,7:F3} m");
            }
            sb.AppendLine("  (patch RMS is over a 14 m patch, so it is a CHOP measurement whatever");
            sb.AppendLine("   the sea state -- a 515 m swell is flat across 14 m.)");

            // ---- and the actual shallow water --------------------------------
            // `MaxRadius` is the island's outer bound, not its beach: on this
            // world the transect above sits in 180 m of water the whole way,
            // so it says nothing about whether the swell now runs aground.
            // Walk in until the seabed appears and report the envelope at the
            // depths the breaking rule is written against.
            sb.AppendLine("  where the water actually shallows (walking in along the same bearing):");
            sb.AppendLine("     depth   at r    off edge    env c0   env c1   env c2   cap c0   swell m");
            float lastR = -1f;
            foreach (float want in new[] { 12f, 8f, 4f, 2f, 1f })
            {
                float found = -1f, foundDepth = 0f;
                for (float r = radius + 300f; r > 5f; r -= 5f)
                {
                    var p = centre + toShip * r;
                    float dd = prm.ShoreWetDepth(new float2(p.x, p.y), rf.Shore).z;
                    if (dd > 1e8f) continue;          // outside the grid, no seabed known
                    if (dd <= want) { found = r; foundDepth = dd; break; }
                }
                if (found < 0f || found == lastR) continue;
                lastR = found;
                var q = centre + toShip * found;
                float3 env = prm.EvaluateCascades(new float2(q.x, q.y), rf.Islands, rf.Shore, rf.Weather);
                float cap = prm.waveHs > 0.01f ? prm.breakFraction * foundDepth / prm.waveHs : 99f;
                sb.AppendLine($"  {foundDepth,8:F1}  {found,6:F0}  {found - radius,9:F0}    " +
                              $"{env.x,8:F3} {env.y,8:F3} {env.z,8:F3}  {Mathf.Min(cap, 9.999f),7:F3}  " +
                              $"{env.x * sigma[0],8:F3}");
                yield return null;
            }
            sb.AppendLine();
        }
        sea.ReleaseForce();
        Finish(sb, null);
    }

    static float RmsAt(Vector3 centre)
    {
        if (!OceanSampler.Ready) return 0f;
        float sum = 0f, sumSq = 0f;
        int count = 0;
        for (int i = 0; i < 16; i++)
        {
            float a = i / 16f * Mathf.PI * 2f;
            for (int r = 1; r <= 2; r++)
            {
                Vector3 p = centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (r * 7f);
                float h = OceanSampler.SampleImmediate(p).height;
                sum += h; sumSq += h * h;
                count++;
            }
        }
        if (count == 0) return 0f;
        float mean = sum / count;
        return Mathf.Sqrt(Mathf.Max(0f, sumSq / count - mean * mean));
    }

    void Finish(StringBuilder sb, string err)
    {
        running = false;
        if (err != null) sb.AppendLine(err);
        System.IO.File.WriteAllText("/tmp/seasick-lee.txt", sb.ToString());
        Debug.Log("LeeProbe:\n" + sb);
        Destroy(gameObject);
    }
}
