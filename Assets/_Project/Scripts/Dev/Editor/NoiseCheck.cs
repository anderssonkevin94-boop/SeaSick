using System.Text;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Terrain;

/// Three questions about the eroded noise, in the order that matters.
///
/// 1. **Is the analytic derivative right?** A wrong one does not error — it
///    produces a plausible-looking field that is damped by the wrong thing,
///    and nothing downstream would ever disagree with it. Checked against a
///    central finite difference.
/// 2. **Does erosion = 0 reproduce Fbm exactly?** If it does not, the new
///    path is not a superset of the old one and every number the project has
///    measured is in play.
/// 3. **What is its distribution?** `RidgeShaped` exists in this project
///    because mixing a field with a different mean into fBm dropped the land
///    field by a quarter of its range and flattened every island, and it read
///    as the profile curve being wrong. Same trap, same fix: measure, then
///    remap onto Fbm01's mean and spread.
public static class NoiseCheck
{
    public static void Execute()
    {
        var sb = new StringBuilder();
        const int Seed = 1337;
        const int Oct = 5;
        const float F = 1f / 400f, Lac = 2f, Gain = 0.5f;

        // --- 1. derivative vs finite difference -------------------------
        var rng = new Unity.Mathematics.Random(12345);
        double maxErr = 0, sumErr = 0; int n = 0;
        for (int i = 0; i < 4000; i++)
        {
            float2 p = rng.NextFloat2(-500f, 500f);
            float v = TerrainNoise.SimplexD(p, Seed, out float2 d);
            const float h = 0.002f;
            float dx = (TerrainNoise.Simplex(p + new float2(h, 0f), Seed)
                      - TerrainNoise.Simplex(p - new float2(h, 0f), Seed)) / (2f * h);
            float dy = (TerrainNoise.Simplex(p + new float2(0f, h), Seed)
                      - TerrainNoise.Simplex(p - new float2(0f, h), Seed)) / (2f * h);
            float mag = math.max(1e-3f, math.length(new float2(dx, dy)));
            float err = math.length(d - new float2(dx, dy)) / mag;
            maxErr = math.max(maxErr, err); sumErr += err; n++;
            if (i == 0) sb.AppendLine($"  sample: value {v:F4}, analytic ({d.x:F3},{d.y:F3}), fd ({dx:F3},{dy:F3})");
        }
        sb.AppendLine($"derivative vs central difference: mean rel err {sumErr / n:E2}, worst {maxErr:E2}");
        sb.AppendLine($"  {(maxErr < 0.02 ? "OK" : "WRONG — the damping would be driven by the wrong gradient")}");

        // --- 2. erosion 0 must be Fbm ------------------------------------
        double worstDiff = 0;
        for (int i = 0; i < 4000; i++)
        {
            float2 p = rng.NextFloat2(-4000f, 4000f);
            float a = TerrainNoise.Fbm(p, Seed, Oct, F, Lac, Gain);
            float b = TerrainNoise.ErodedRaw(p, Seed, Oct, F, Lac, Gain, 0f);
            worstDiff = math.max(worstDiff, math.abs(a - b));
        }
        sb.AppendLine($"erosion 0 vs Fbm: worst |diff| {worstDiff:E2}  "
            + $"{(worstDiff < 1e-5 ? "OK — the new path is a superset of the old" : "DIVERGED")}");

        // --- 3. distribution ---------------------------------------------
        sb.AppendLine();
        sb.AppendLine("erosion    mean      sd     (Fbm01 reference below)");
        foreach (float e in new[] { 0f, 0.5f, 1f, 2f, 4f, 8f })
        {
            double sum = 0, sum2 = 0; int m = 0;
            var r2 = new Unity.Mathematics.Random(777);
            for (int i = 0; i < 60000; i++)
            {
                float2 p = r2.NextFloat2(-20000f, 20000f);
                float v = TerrainNoise.ErodedRaw(p, Seed, Oct, F, Lac, Gain, e) * 0.5f + 0.5f;
                sum += v; sum2 += (double)v * v; m++;
            }
            double mean = sum / m, sd = System.Math.Sqrt(sum2 / m - mean * mean);
            sb.AppendLine($"{e,6:F1}   {mean:F4}  {sd:F4}");
        }
        {
            double sum = 0, sum2 = 0; int m = 0;
            var r2 = new Unity.Mathematics.Random(777);
            for (int i = 0; i < 60000; i++)
            {
                float2 p = r2.NextFloat2(-20000f, 20000f);
                float v = TerrainNoise.Fbm01(p, Seed, Oct, F, Lac, Gain);
                sum += v; sum2 += (double)v * v; m++;
            }
            double mean = sum / m, sd = System.Math.Sqrt(sum2 / m - mean * mean);
            sb.AppendLine($" Fbm01   {mean:F4}  {sd:F4}   <- what the eroded field must be remapped onto");
        }

        Debug.Log("NOISE CHECK\n" + sb);
        System.IO.File.WriteAllText("/tmp/noise-check.txt", sb.ToString());
    }
}
