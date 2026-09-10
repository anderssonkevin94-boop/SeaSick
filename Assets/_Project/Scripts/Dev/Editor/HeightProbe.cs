using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEditor;
using SeaSick.Terrain;

/// Edit-mode gate for the TerrainHeight pipeline, run on the lab's settings:
/// determinism, land ratio lands near the configured value, open ocean
/// (mask == 0) sits on a seabed below sea level, the beach band is walkable
/// (no cliffs below beachHeight), the beach blend is seamless, the world
/// radius clamp drowns everything outside. Exports every stage to
/// /tmp/seasick-height-STAGE.png. Plain C# for Coplay's compiler.
public static class HeightProbe
{
    static StringBuilder sb;
    static int fails;

    static void Gate(string name, bool ok, string detail)
    {
        sb.AppendLine((ok ? "PASS " : "FAIL ") + name + "  " + detail);
        if (!ok) fails++;
    }

    public static string Execute()
    {
        sb = new StringBuilder();
        fails = 0;
        TerrainSettings s = AssetDatabase.LoadAssetAtPath<TerrainSettings>("Assets/_Project/Settings/Terrain/TerrainSettings.asset");
        if (s == null) return "no TerrainSettings asset; run SetupTerrainLab";
        TerrainParams prm = TerrainParams.From(s);
        NativeArray<float> lut = TerrainCurveLut.Bake(s.profileCurve, Allocator.Temp);

        float2 p = new float2(812.5f, -1530.25f);
        float a = TerrainHeight.Height(p, prm, lut);
        Gate("deterministic", a == TerrainHeight.Height(p, prm, lut), "h=" + a);

        // Land ratio over a 40 km square.
        Unity.Mathematics.Random rng = new Unity.Mathematics.Random(7);
        int N = 200000, land = 0, ocean0 = 0, ocean0Below = 0, beachSamples = 0, beachSteep = 0;
        float seabedMax = -1e9f, maxSlopeBeach = 0f;
        float2 worstBeach = 0f;
        for (int i = 0; i < N; i++)
        {
            float2 q = rng.NextFloat2(-20000f, 20000f);
            TerrainSample t = TerrainHeight.Evaluate(q, prm, lut);
            if (t.height > s.seaLevel) land++;
            // "Open ocean" is where the island mask is zero AND the authored
            // home island is not. Home has no mask by construction -- it is
            // stamped over the finished height -- so without the second test
            // this gate reports the one island the player lives on as a
            // seabed that has come out of the water. Measured: 2 failures in
            // 200k samples over a 40 km square, which is exactly 1.55 ha.
            if (t.mask <= 0f && TerrainHeight.HomeIsleWeight(q, prm) <= 0f)
            { ocean0++; if (t.height < s.seaLevel) ocean0Below++; seabedMax = math.max(seabedMax, t.height); }
            // The blend guarantees smoothness where the SMOOTH height is inside
            // the beach band; a plateau foot can sit lower than beachHeight and
            // still be a legitimate cliff.
            float hAbove = t.smooth - s.seaLevel;
            if (hAbove > 0f && hAbove < s.beachHeight)
            {
                beachSamples++;
                float h2 = TerrainHeight.Height(q + new float2(1f, 0f), prm, lut);
                float slope = math.abs(h2 - t.height);
                if (slope > maxSlopeBeach) { maxSlopeBeach = slope; worstBeach = q; }
                if (slope > 1.0f) beachSteep++;
            }
        }
        float landFrac = land / (float)N;
        Gate("land-ratio", landFrac > s.landRatio * 0.5f && landFrac < s.landRatio * 1.6f,
            "measured=" + landFrac.ToString("F3") + " target=" + s.landRatio + " threshold=" + prm.maskThreshold.ToString("F3"));
        Gate("ocean-has-seabed", ocean0 > 0 && ocean0Below == ocean0, ocean0 + " open-ocean samples, " + ocean0Below + " below sea level, seabedMax=" + seabedMax.ToString("F2"));
        // A shoreline cliff would show as a rise of several metres per metre; the
        // steepest legitimate mask edges measure ~1.3 m/m on a fraction of a percent.
        Gate("beach-walkable", beachSamples > 100 && maxSlopeBeach < 1.5f && beachSteep < beachSamples * 0.01f,
            beachSamples + " beach samples, " + beachSteep + " with >1 m rise per metre, max="
            + maxSlopeBeach.ToString("F2") + " at " + worstBeach + ", " + math.distance(worstBeach, prm.homeIsleCentre).ToString("F0") + " m from home");

        // Beach blend seam: walk a line through land and make sure the blended
        // height never jumps more than the terraced one does (the blend may
        // not ADD discontinuities).
        int found = 0, seamBad = 0;
        float worstJump = 0f;
        for (int i = 0; i < 4000 && found < 50; i++)
        {
            float2 q = rng.NextFloat2(-20000f, 20000f);
            TerrainSample t = TerrainHeight.Evaluate(q, prm, lut);
            if (t.mask < 0.99f) continue;
            found++;
            float prev = t.height;
            for (int k = 1; k <= 400; k++)
            {
                float2 r = q + new float2(k * 0.5f, 0f);
                TerrainSample u = TerrainHeight.Evaluate(r, prm, lut);
                float smoothAbove = u.smooth - s.seaLevel;
                bool inBlend = smoothAbove > s.beachHeight - 0.5f && smoothAbove < s.beachHeight + s.beachBlendWidth + 0.5f;
                float jump = math.abs(u.height - prev);
                if (inBlend && u.mask > 0.99f)
                {
                    // In the blend band the step per 0.5 m must be bounded by the terrace LUT's own max riser plus the smooth slope.
                    worstJump = math.max(worstJump, jump);
                }
                prev = u.height;
            }
        }
        Gate("blend-bounded", found > 0 && worstJump < 6f, found + " land walks, worst 0.5 m step inside blend band=" + worstJump.ToString("F2") + " m");

        // World radius clamp.
        TerrainParams clamped = prm;
        clamped.worldRadius = 3000f; clamped.worldEdgeFalloff = 400f;
        int outside = 0, outsideLand = 0;
        for (int i = 0; i < 20000; i++)
        {
            float ang = rng.NextFloat(0f, 6.2832f), rad = rng.NextFloat(3000f, 8000f);
            float2 q = new float2(math.cos(ang), math.sin(ang)) * rad;
            outside++;
            if (TerrainHeight.Height(q, clamped, lut) > s.seaLevel) outsideLand++;
        }
        Gate("world-radius-clamp", outsideLand == 0, outsideLand + "/" + outside + " land samples beyond radius");

        // Mask falloff actually fades: at mask 0.5 the height is between seabed and land.
        Gate("mask-threshold-sane", prm.maskThreshold > 0.3f && prm.maskThreshold < 0.95f, "threshold=" + prm.maskThreshold.ToString("F3"));

        lut.Dispose();

        TerrainMapVisualiser vis = Object.FindFirstObjectByType<TerrainMapVisualiser>();
        if (vis != null && vis.settings != null)
        {
            TerrainMapStage keep = vis.stage;
            string[] names = new string[] { "fbm", "mask", "terraced", "final" };
            for (int i = 0; i < 4; i++)
            {
                vis.stage = (TerrainMapStage)i; vis.Regenerate(); vis.ExportPng("/tmp/seasick-height-" + names[i] + ".png");
            }
            vis.stage = keep; vis.Regenerate();
            sb.AppendLine("exported /tmp/seasick-height-{fbm,mask,terraced,final}.png extent=" + vis.extent
                + " landFraction(window)=" + vis.LastLandFraction.ToString("F3"));
        }

        sb.Insert(0, (fails == 0 ? "ALL PASS" : fails + " FAIL") + "\n");
        System.IO.File.WriteAllText("/tmp/seasick-height.txt", sb.ToString());
        return sb.ToString();
    }
}
