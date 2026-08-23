using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityEditor;
using SeaSick.Terrain;

/// Edit-mode gate for TerrainNoise: determinism, seed sensitivity, no
/// mirroring across the origin, normalised range regardless of octaves.
/// Writes /tmp/seasick-noise.txt and exports the lab visualiser to
/// /tmp/seasick-noise-N.png for octaves 1 and the asset's value.
/// Deliberately plain C# — Coplay's compiler rejects local functions etc.
public static class NoiseProbe
{
    static StringBuilder sb;
    static int fails;

    static void Gate(string name, bool ok, string detail)
    {
        sb.AppendLine((ok ? "PASS " : "FAIL ") + name + "  " + detail);
        if (!ok) fails++;
    }

    static float F(float2 p, int seed, int oct)
    {
        return TerrainNoise.Fbm(p, seed, oct, 1f / 400f, 2f, 0.5f);
    }

    public static string Execute()
    {
        sb = new StringBuilder();
        fails = 0;
        int seed = 1337;
        float2 p = new float2(1234.5f, -987.25f);
        float a = F(p, seed, 5);
        float b = F(p, seed, 5);
        Gate("deterministic", a == b, a + " == " + b);
        float c = F(p, seed + 1, 5);
        Gate("seed-sensitive", math.abs(a - c) > 1e-4f, "seed+1 -> " + c);
        float m = F(-p, seed, 5);
        float mx = F(new float2(-p.x, p.y), seed, 5);
        Gate("no-mirror", math.abs(a - m) > 1e-4f && math.abs(a - mx) > 1e-4f, "f(-p)=" + m + " f(-x,y)=" + mx);

        Unity.Mathematics.Random rng = new Unity.Mathematics.Random(42);
        int[] octs = new int[] { 1, 3, 8 };
        for (int k = 0; k < octs.Length; k++)
        {
            int oct = octs[k];
            float mn = 1e9f, mxv = -1e9f;
            double sum = 0, sumSq = 0;
            int N = 400000;
            for (int i = 0; i < N; i++)
            {
                float2 q = rng.NextFloat2(-50000f, 50000f);
                float v = F(q, seed, oct);
                mn = math.min(mn, v); mxv = math.max(mxv, v); sum += v; sumSq += v * v;
            }
            double mean = sum / N;
            double sd = System.Math.Sqrt(sumSq / N - mean * mean);
            Gate("range oct=" + oct, mn >= -1.01f && mxv <= 1.01f && mxv > 0.5f && mn < -0.5f,
                "min=" + mn.ToString("F3") + " max=" + mxv.ToString("F3") + " mean=" + mean.ToString("F3") + " sd=" + sd.ToString("F3"));
            Gate("centred oct=" + oct, System.Math.Abs(mean) < 0.03, "mean=" + mean.ToString("F4"));
        }

        float2 far = new float2(80000f, -80000f);
        float f0 = F(far, seed, 5);
        float f1 = F(far + new float2(0.5f, 0f), seed, 5);
        Gate("far-continuity", math.abs(f0 - f1) < 0.05f, "|d|=" + math.abs(f0 - f1).ToString("F4") + " at 80 km");

        TerrainMapVisualiser vis = Object.FindFirstObjectByType<TerrainMapVisualiser>();
        if (vis != null && vis.settings != null)
        {
            int keep = vis.settings.octaves;
            try
            {
                vis.settings.octaves = 1; vis.Regenerate(); vis.ExportPng("/tmp/seasick-noise-1.png");
            }
            finally { vis.settings.octaves = keep; }
            vis.Regenerate(); vis.ExportPng("/tmp/seasick-noise-" + keep + ".png");
            float keepExtent = vis.extent;
            try
            {
                vis.extent = keepExtent * 0.25f; vis.Regenerate(); vis.ExportPng("/tmp/seasick-noise-" + keep + "-zoom.png");
            }
            finally { vis.extent = keepExtent; }
            vis.Regenerate();
            sb.AppendLine("exported /tmp/seasick-noise-1.png and -" + keep + ".png, extent=" + vis.extent + " size=" + vis.textureSize
                + " visRange=[" + vis.LastMin + ", " + vis.LastMax + "]");
        }
        else sb.AppendLine("no TerrainMapVisualiser in scene; run SetupTerrainLab first");

        sb.Insert(0, (fails == 0 ? "ALL PASS" : fails + " FAIL") + "\n");
        System.IO.File.WriteAllText("/tmp/seasick-noise.txt", sb.ToString());
        return sb.ToString();
    }
}
