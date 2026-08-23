using System.Collections;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Terrain;

/// Play-mode gate (Sea.unity) for the terrain → ocean shore coupling.
/// Forces severity 1.0, waits for the shore grid, then over 12 s measures
/// surface-height RMS from the CPU sampler at: open water off the spawn,
/// the shoreline south of spawn, and a point on land. Waves must be intact
/// in deep water, gone at the shoreline, and zero on land. Also checks the
/// CPU ShoreFactor against a direct terrain-height evaluation at 200 points
/// and renders a look shot of the shore.
public class ShoreProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("ShoreProbe: not in play mode"); return; }
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

        float wait = 0f;
        while (!field.Ready && wait < 10f) { wait += Time.deltaTime; yield return null; }
        if (!field.Ready) { Finish(sb, 1, "shore grid never built"); yield break; }
        yield return new WaitForSeconds(3f);

        // Find the shoreline on the line from spawn south: first x where terrain height crosses -shoalDepthZero.
        TerrainParams prm = TerrainParams.From(s);
        NativeArray<float> lut = TerrainCurveLut.Bake(s.terraceCurve, Allocator.Temp);
        float2 deep = new float2(0f, 40f);
        float2 shoreline = deep; float2 land = deep;
        for (float z = 40f; z > -600f; z -= 2f)
        {
            float h = TerrainHeight.Height(new float2(0f, z), prm, lut);
            if (shoreline.Equals(deep) && h > -0.5f) shoreline = new float2(0f, z);
            if (h > 3f) { land = new float2(0f, z); break; }
        }
        sb.AppendLine("deep=" + deep + " h=" + TerrainHeight.Height(deep, prm, lut).ToString("F1")
            + "  shoreline=" + shoreline + " h=" + TerrainHeight.Height(shoreline, prm, lut).ToString("F1")
            + "  land=" + land + " h=" + TerrainHeight.Height(land, prm, lut).ToString("F1"));

        // CPU ShoreFactor vs direct terrain height (same formula on the exact height).
        RegionField rf = RegionField.Instance;
        float worst = 0f;
        Unity.Mathematics.Random rng = new Unity.Mathematics.Random(5);
        for (int i = 0; i < 200; i++)
        {
            float2 p = rng.NextFloat2(-1500f, 1500f);
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
        cam.transform.position = new Vector3(-60f, 8f, shoreline.y + 160f);
        cam.transform.LookAt(new Vector3(0f, 2f, shoreline.y - 40f));
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

    static void Finish(StringBuilder sb, int fails, string err)
    {
        if (err != null) sb.AppendLine(err);
        sb.Insert(0, (fails == 0 ? "ALL PASS" : fails + " FAIL") + "\n");
        System.IO.File.WriteAllText("/tmp/seasick-shore.txt", sb.ToString());
        Debug.Log("ShoreProbe:\n" + sb);
    }
}
