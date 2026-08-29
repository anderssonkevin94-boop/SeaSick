using System.Collections;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Terrain;
using SeaSick.World;

/// Play-mode gate (Sea.unity) for the terrain -> ocean shore coupling. Forces
/// severity 1.0 and measures surface-height RMS at three places: open water,
/// the shoreline, and a point on land. Waves must be intact in deep water,
/// gone at the shoreline, and zero on land. Also checks the CPU ShoreFactor
/// against a direct terrain-height evaluation and shoots a look at the beach.
///
/// **THE SHORE GRID FOLLOWS THE SHIP, so this probe has to take her with it.**
/// It used to hunt the shoreline along the line south of the WORLD ORIGIN and
/// measure there while the ship sat wherever she had spawned. That was fine
/// when she spawned at home; `PlaytestStart` now puts her at (-2400, 725) in
/// 180 m of water, so the grid never covered the test points, `ShoreFactor`
/// returned "outside the grid, sea untouched" for all of them, and the probe
/// reported three red gates -- worst grid error 1.000, 1.14 m of waves on dry
/// land -- with nothing whatever wrong with the ocean. A gate that fails for
/// its own reasons is worse than no gate.
///
/// So it FINDS a real shoreline (nearest island to the ship, walking in along
/// the bearing to her), WARPS her to deep water off it, waits for the streamer
/// and the grid, and then ASSERTS the grid actually covers all three points
/// before it believes a single measurement.
///
/// Writes /tmp/seasick-shore.txt and -shore.png.
public class ShoreProbe : MonoBehaviour
{
    static bool running;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("ShoreProbe: not in play mode"); return; }
        if (running) { Debug.LogError("ShoreProbe: already running"); return; }
        running = true;
        new GameObject("ShoreProbe").AddComponent<ShoreProbe>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        int fails = 0;
        TerrainShoreField field = FindAnyObjectByType<TerrainShoreField>();
        TerrainSettings s = field != null ? field.settings : null;
        if (field == null || s == null) { Finish(sb, 1, "no TerrainShoreField"); yield break; }
        if (SeaStateController.Instance != null) SeaStateController.Instance.ForceSeverity(1f);

        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var helm = FindAnyObjectByType<SeaSick.Ship.HelmInput>();
        if (helm != null) helm.enabled = false;
        var rb = motor != null ? motor.GetComponent<Rigidbody>() : null;
        if (motor != null) { motor.ThrottleOrder = 0f; motor.Rudder = 0f; }

        float wait = 0f;
        while ((!field.Ready || Island.All.Count == 0) && wait < 20f) { wait += Time.deltaTime; yield return null; }
        if (!field.Ready) { Finish(sb, 1, "shore grid never built"); yield break; }
        if (Island.All.Count == 0) { Finish(sb, 1, "no islands generated"); yield break; }
        yield return new WaitForSeconds(3f);

        // FIND a shoreline instead of assuming one. The nearest island to the
        // ship is the only one whose depths can be streamed in without sailing.
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

        // Walk in along that bearing with the exact height function until the
        // land starts, then step back out for the deep station.
        TerrainParams prm = TerrainParams.From(s);
        // PERSISTENT, not Temp. A Temp allocation is valid for ONE FRAME, and
        // this probe now yields between baking the curve and using it (it has
        // to wait for the streamer). The old version got away with Temp only
        // because it never yielded in between; the symptom is an
        // ObjectDisposedException from inside the height function, several
        // seconds after the array was made.
        NativeArray<float> lut = TerrainCurveLut.Bake(s.profileCurve, Allocator.Persistent);
        float2 shoreline = default, land = default;
        bool foundShore = false, foundLand = false;
        for (float r = isle.MaxRadius + 400f; r > 5f; r -= 4f)
        {
            Vector2 q = centre + outward * r;
            float h = TerrainHeight.Height(new float2(q.x, q.y), prm, lut);
            if (!foundShore && h > -0.5f) { shoreline = new float2(q.x, q.y); foundShore = true; }
            if (foundShore && h > 3f) { land = new float2(q.x, q.y); foundLand = true; break; }
        }
        if (!foundShore || !foundLand)
        {
            lut.Dispose();
            Finish(sb, 1, $"no shoreline found on '{isle.name}' along the bearing to the ship");
            yield break;
        }

        Vector2 deepV = centre + outward * (Vector2.Distance(centre, new Vector2(shoreline.x, shoreline.y)) + 500f);
        float2 deep = new float2(deepV.x, deepV.y);

        // Take the ship with us, and give the streamer and the shore grid time
        // to centre on her before anything is believed.
        Vector2 park = Vector2.Lerp(new Vector2(deep.x, deep.y),
                                    new Vector2(shoreline.x, shoreline.y), 0.35f);
        if (rb != null)
        {
            rb.position = new Vector3(park.x, 2f, park.y);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        yield return new WaitForSeconds(4f);

        RegionField rf = RegionField.Instance;
        sb.AppendLine($"island '{isle.name}' at ({centre.x:F0}, {centre.y:F0}), ship parked at ({park.x:F0}, {park.y:F0})");
        sb.AppendLine("deep=" + deep + " h=" + TerrainHeight.Height(deep, prm, lut).ToString("F1")
            + "  shoreline=" + shoreline + " h=" + TerrainHeight.Height(shoreline, prm, lut).ToString("F1")
            + "  land=" + land + " h=" + TerrainHeight.Height(land, prm, lut).ToString("F1"));

        // ASSERT COVERAGE BEFORE MEASURING. Outside the grid ShoreWetDepth
        // returns a 1e9 sentinel and the sea is left untouched, which looks
        // exactly like a broken depth coupling. This is the check whose absence
        // let the probe report three red gates about nothing.
        var prmR = rf.Params;
        bool covered = prmR.ShoreWetDepth(deep, rf.Shore).z < 1e8f
                    && prmR.ShoreWetDepth(shoreline, rf.Shore).z < 1e8f
                    && prmR.ShoreWetDepth(land, rf.Shore).z < 1e8f;
        Gate(sb, ref fails, "grid-covers-the-test-points", covered,
            covered ? $"all three inside the {rf.ShoreN}-texel grid"
                    : "AT LEAST ONE POINT IS OUTSIDE THE SHORE GRID -- every gate below is meaningless");

        // CPU ShoreFactor vs direct terrain height, around the SHIP (which is
        // where the grid is) rather than around the world origin.
        float worst = 0f;
        Unity.Mathematics.Random rng = new Unity.Mathematics.Random(5);
        for (int i = 0; i < 200; i++)
        {
            float2 p = new float2(park.x, park.y) + rng.NextFloat2(-1200f, 1200f);
            float h = TerrainHeight.Height(p, prm, lut);
            float expect = math.smoothstep(0.5f, 8f, -h);
            float got = rf.Params.ShoreFactor(p, rf.Shore);
            worst = math.max(worst, math.abs(got - expect));
        }
        lut.Dispose();
        // The grid is 16 m texels, so bilinear vs exact differs on steep slopes; gate loosely.
        Gate(sb, ref fails, "shore-factor-grid", worst < 0.35f, "worst |grid - exact| = " + worst.ToString("F3") + " over 200 points (16 m texels)");

        // Wave RMS over 12 s at the three points.
        double sDeep = 0, sShore = 0, sLand = 0; int n = 0;
        float t = 0f;
        while (t < 12f)
        {
            t += Time.deltaTime;
            float hd = OceanSampler.SampleImmediate(new Vector3(deep.x, 0f, deep.y)).height;
            float hs = OceanSampler.SampleImmediate(new Vector3(shoreline.x, 0f, shoreline.y)).height;
            float hl = OceanSampler.SampleImmediate(new Vector3(land.x, 0f, land.y)).height;
            sDeep += hd * hd; sShore += hs * hs; sLand += hl * hl; n++;
            yield return null;
        }
        float rmsDeep = (float)System.Math.Sqrt(sDeep / n), rmsShore = (float)System.Math.Sqrt(sShore / n), rmsLand = (float)System.Math.Sqrt(sLand / n);
        Gate(sb, ref fails, "deep-water-waves", rmsDeep > 0.3f, "rms " + rmsDeep.ToString("F2") + " m at " + deep);
        Gate(sb, ref fails, "shoreline-calm", rmsShore < rmsDeep * 0.1f, "rms " + rmsShore.ToString("F3") + " m at shoreline vs " + rmsDeep.ToString("F2") + " deep");
        Gate(sb, ref fails, "land-flat", rmsLand < 0.01f, "rms " + rmsLand.ToString("F3") + " m on land");

        // Look shot: low over the water toward the beach.
        Camera cam = new GameObject("ShotCam").AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.depth = 100f;
        Vector2 eye = Vector2.Lerp(new Vector2(shoreline.x, shoreline.y), new Vector2(deep.x, deep.y), 0.4f);
        cam.transform.position = new Vector3(eye.x, 8f, eye.y);
        cam.transform.LookAt(new Vector3(land.x, 2f, land.y));
        yield return new WaitForSeconds(0.5f);
        ScreenCapture.CaptureScreenshot("/tmp/seasick-shore.png");
        yield return null;
        Destroy(cam.gameObject);
        if (SeaStateController.Instance != null) SeaStateController.Instance.ReleaseForce();
        Finish(sb, fails, null);
    }

    static void Gate(StringBuilder sb, ref int fails, string name, bool ok, string detail)
    {
        sb.AppendLine((ok ? "PASS " : "FAIL ") + name + "  " + detail);
        if (!ok) fails++;
    }

    void Finish(StringBuilder sb, int fails, string err)
    {
        running = false;
        if (err != null) sb.AppendLine(err);
        sb.Insert(0, (fails == 0 ? "ALL PASS" : fails + " FAIL") + "\n");
        System.IO.File.WriteAllText("/tmp/seasick-shore.txt", sb.ToString());
        Debug.Log("ShoreProbe:\n" + sb);
        Destroy(gameObject);
    }
}
