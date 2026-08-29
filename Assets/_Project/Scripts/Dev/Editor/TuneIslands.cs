using System.Text;
using UnityEngine;
using UnityEditor;
using Unity.Collections;
using Unity.Mathematics;
using SeaSick.Terrain;

/// Pushes the island profile and the massif/ridge tuning into
/// TerrainSettings.asset.
///
/// This exists because the ASSET wins. Every number the terrain reads is
/// serialised there, so editing a default in TerrainSettings.cs changes
/// nothing at all -- this project's most-repeated trap. The profile curve in
/// particular MUST be pushed rather than defaulted: the asset holds the old
/// four-step staircase authored in METRES (3, 22, 41, 60), and the new
/// pipeline multiplies the curve by an amplitude in metres, so leaving it
/// would ask for a 3600 m mountain.
///
/// Idempotent. Re-run after changing any number here.
public static class TuneIslands
{
    const string Path = "Assets/_Project/Settings/Terrain/TerrainSettings.asset";

    // The profile: a rising curve with benches cut into it.
    //
    // The old curve was four DEAD FLAT shelves at fixed altitudes joined by
    // near-vertical risers, which is a mesa generator -- it holds ~85% of the
    // noise range at one of four heights and cannot produce a summit at all,
    // because nothing in it climbs. Every island came out a layer cake and
    // every skyline was the same four lines.
    //
    // Here the terrace rides ON a climb instead of replacing it: Base is a
    // gentle power curve, Terrace quantises it, and the two are mixed. The
    // benches still read as cliffs and ledges from the water -- that drama is
    // worth keeping, it suits the boat -- but the land gains height across
    // the whole range, so an island has a top, and the tops differ.
    const int Steps = 4;
    const float Sharp = 3f;        // riser sharpness: bench, then a fast rise
    const float Strength = 0.55f;  // 0 = pure climb, 1 = pure staircase
    // Below 1, and this is NOT a taste knob -- it sets where the coastline is.
    //
    // The shoreline is wherever lerp(seabed, land, mask) crosses zero, so the
    // island's FOOTPRINT is decided by how high the land stands at LOW noise,
    // out at the fringe where the mask is small. The old staircase jumped to
    // 22 m by noise 0.25, and that early jump is what held the coast out
    // where it was. The first version of this curve used 1.15, which spends
    // more of the range on low ground -- it dropped the fringe to about 16 m,
    // moved the zero crossing inland, and shrank the islands off the map:
    // the look sheet came back as trees floating on open water with the shore
    // foam still tracing a coastline that no longer had any land behind it.
    //
    // 0.72 tracks the old curve's envelope where it matters (0.25 -> 0.37 of
    // full relief against the old 0.37, 0.5 -> 0.61 against 0.68) and then
    // keeps CLIMBING through the top of the range instead of flatlining at
    // 60 m, which is the whole point of the pass.
    const float BaseExp = 0.72f;

    static float Base(float t) { return Mathf.Pow(t, BaseExp); }

    static float Terraced(float v)
    {
        float x = v * Steps;
        float i = Mathf.Floor(x);
        float f = x - i;
        float e = Mathf.SmoothStep(0f, 1f, Mathf.Pow(f, Sharp));
        return (i + e) / Steps;
    }

    static float Profile(float t)
    {
        float b = Base(Mathf.Clamp01(t));
        return Mathf.Lerp(b, Terraced(b), Strength);
    }

    /// 41 keys with finite-difference tangents. Sampling a formula beats
    /// hand-placing keys with auto tangents, which overshoot on a stepped
    /// curve and put dips in the benches.
    static AnimationCurve BuildCurve()
    {
        const int N = 41;
        var keys = new Keyframe[N];
        float h = 1f / (N - 1);
        for (int i = 0; i < N; i++)
        {
            float t = i * h;
            float v = Profile(t);
            float a = Profile(Mathf.Clamp01(t - h * 0.5f));
            float b = Profile(Mathf.Clamp01(t + h * 0.5f));
            float slope = (b - a) / (h * (i == 0 || i == N - 1 ? 0.5f : 1f));
            keys[i] = new Keyframe(t, v, slope, slope);
        }
        return new AnimationCurve(keys);
    }

    public static string Execute()
    {
        var s = AssetDatabase.LoadAssetAtPath<TerrainSettings>(Path);
        if (s == null) return "no TerrainSettings asset at " + Path;

        var so = new SerializedObject(s);
        so.FindProperty("profileCurve").animationCurveValue = BuildCurve();
        so.FindProperty("baseHeight").floatValue = 3f;
        so.FindProperty("reliefHeight").floatValue = 60f;
        so.FindProperty("massifFrequency").floatValue = 1f / 5200f;
        so.FindProperty("massifMin").floatValue = 1f;
        so.FindProperty("massifMax").floatValue = 5.5f;
        so.FindProperty("massifBias").floatValue = 1.5f;
        so.FindProperty("massifMaskStart").floatValue = 0.35f;
        so.FindProperty("ridgeAmount").floatValue = 0.85f;
        so.FindProperty("ridgeLow").floatValue = 0.42f;
        so.FindProperty("ridgeHigh").floatValue = 0.62f;
        so.FindProperty("shoreSlopeMin").floatValue = 0.032f;
        so.FindProperty("shoreSlopeMax").floatValue = 0.45f;
        so.FindProperty("shoreSlopeBias").floatValue = 2.5f;
        so.FindProperty("shoreFrequency").floatValue = 1f / 900f;
        so.FindProperty("shoreFlat").floatValue = 19f;
        so.FindProperty("shoreTop").floatValue = 70f;
        so.FindProperty("shoreBottom").floatValue = 12f;
        so.FindProperty("sandHeight").floatValue = 3.2f;
        so.FindProperty("snowHeight").floatValue = 165f;
        so.FindProperty("skirtDepth").floatValue = 24f;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();

        // Read back, because a push that silently did not land is the whole
        // reason this file exists.
        var check = AssetDatabase.LoadAssetAtPath<TerrainSettings>(Path);
        var sb = new StringBuilder();
        sb.AppendLine("profile curve: " + check.profileCurve.length + " keys, "
            + "f(0)=" + check.profileCurve.Evaluate(0f).ToString("F3")
            + " f(.25)=" + check.profileCurve.Evaluate(0.25f).ToString("F3")
            + " f(.5)=" + check.profileCurve.Evaluate(0.5f).ToString("F3")
            + " f(.75)=" + check.profileCurve.Evaluate(0.75f).ToString("F3")
            + " f(1)=" + check.profileCurve.Evaluate(1f).ToString("F3"));
        sb.AppendLine("baseHeight " + check.baseHeight + "  reliefHeight " + check.reliefHeight
            + "  massif " + check.massifMin + ".." + check.massifMax + " bias " + check.massifBias
            + " from mask " + check.massifMaskStart);
        sb.AppendLine("shore slope x" + check.shoreSlopeMin + ".." + check.shoreSlopeMax
            + " over " + check.shoreFrequency + "/m, identity by +" + check.shoreTop
            + " m (flat to " + check.shoreFlat + " m) and -" + check.shoreBottom + " m; sand to " + check.sandHeight + " m");
        sb.AppendLine("ridge " + check.ridgeAmount + " over " + check.ridgeLow + ".." + check.ridgeHigh
            + "   snow " + check.snowHeight + "   skirt " + check.skirtDepth);

        // What that means in metres, which is the number worth checking.
        float coast = check.baseHeight + check.reliefHeight;
        float tallest = check.baseHeight + check.reliefHeight * check.massifMax;
        sb.AppendLine("ceiling at the coast " + coast.ToString("F0") + " m (was 60, unchanged); "
            + "tallest possible massif " + tallest.ToString("F0") + " m = "
            + (tallest / 24.24f).ToString("F1") + " ship-lengths");
        return sb.ToString();
    }

    /// How tall the islands ACTUALLY come out, which is the only honest way
    /// to set the massif range.
    ///
    /// Reasoning about it from the curve does not work: the height a spot
    /// gets is profile(noise) x amplitude(massif), and what matters is the
    /// noise at an island's SUMMIT, not its average -- and that depends on
    /// how far the mask lets an island extend into high-noise ground. The
    /// first pass guessed, and produced islands SHORTER than the ones it
    /// replaced.
    ///
    /// Tiles the survey area at 500 m, takes each tile's highest land, and
    /// reports the distribution of those. A tile maximum is a decent stand-in
    /// for an island peak and needs no flood fill.
    public static string Survey()
    {
        var s = AssetDatabase.LoadAssetAtPath<TerrainSettings>(Path);
        if (s == null) return "no TerrainSettings asset at " + Path;
        var prm = TerrainParams.From(s);
        var lut = TerrainCurveLut.Bake(s.profileCurve, Allocator.Temp);

        const float Extent = 14000f, Tile = 500f, Step = 25f;
        int tiles = (int)(Extent * 2f / Tile);
        var peaks = new System.Collections.Generic.List<float>();
        float highest = -9999f; float2 highestAt = float2.zero;
        for (int ty = 0; ty < tiles; ty++)
        {
            for (int tx = 0; tx < tiles; tx++)
            {
                float x0 = -Extent + tx * Tile, z0 = -Extent + ty * Tile;
                float top = -9999f;
                for (float z = z0; z < z0 + Tile; z += Step)
                {
                    for (float x = x0; x < x0 + Tile; x += Step)
                    {
                        var p = new float2(x, z);
                        float h = TerrainHeight.Height(p, prm, lut);
                        if (h > top) top = h;
                        if (h > highest) { highest = h; highestAt = p; }
                    }
                }
                if (top > 8f) peaks.Add(top);   // a tile with real land in it
            }
        }
        peaks.Sort();
        var sb = new StringBuilder();
        if (peaks.Count == 0) return "no land found in the survey area";
        sb.AppendLine("survey " + (Extent * 2f / 1000f) + " km square, " + Tile + " m tiles, "
            + peaks.Count + " tiles with land");
        sb.AppendLine("tile peak heights (m):  p10 " + peaks[peaks.Count / 10].ToString("F0")
            + "   p50 " + peaks[peaks.Count / 2].ToString("F0")
            + "   p90 " + peaks[peaks.Count * 9 / 10].ToString("F0")
            + "   max " + peaks[peaks.Count - 1].ToString("F0"));
        sb.AppendLine("in ship-lengths (24.2 m): p50 " + (peaks[peaks.Count / 2] / 24.24f).ToString("F1")
            + "   p90 " + (peaks[peaks.Count * 9 / 10] / 24.24f).ToString("F1")
            + "   max " + (peaks[peaks.Count - 1] / 24.24f).ToString("F1"));
        sb.AppendLine("highest ground " + highest.ToString("F0") + " m at " + highestAt);

        // DRY LAND, as a fraction of the world -- not the mask's land ratio,
        // which is what HeightProbe gates and which cannot see this at all.
        // The mask says where an island is ALLOWED to be; this says where one
        // actually breaks the surface. Lowering the profile at low noise
        // moves every coastline inland while leaving the mask untouched, so
        // the gate passed at 0.121 while the islands were vanishing.
        int dry = 0, total = 0;
        var rng2 = new System.Random(7);
        for (int k = 0; k < 200000; k++)
        {
            float x = (float)(rng2.NextDouble() * 28000.0 - 14000.0);
            float z = (float)(rng2.NextDouble() * 28000.0 - 14000.0);
            total++;
            if (TerrainHeight.Height(new float2(x, z), prm, lut) > 0f) dry++;
        }
        sb.AppendLine("dry land above sea level: " + (100f * dry / total).ToString("F2")
            + "% of the world (the mask allows " + (100f * 0.15f).ToString("F0") + "%)");

        // The additive ridge can push the shape field past 1, where it
        // clips -- and a clipped shape field is a FLAT TOP, which is the
        // exact defect this whole pass exists to remove. Count it.
        int clipped = 0, landPts = 0;
        var rng = new System.Random(99);
        for (int k = 0; k < 120000; k++)
        {
            float x = (float)(rng.NextDouble() * 28000.0 - 14000.0);
            float z = (float)(rng.NextDouble() * 28000.0 - 14000.0);
            var q = new float2(x, z);
            if (TerrainHeight.Mask(q, prm) < 0.9f) continue;
            landPts++;
            if (TerrainHeight.Noise01(q, prm) >= 0.999f) clipped++;
        }
        sb.AppendLine("shape field clipped at 1.0 on " + clipped + " of " + landPts
            + " island samples (" + (landPts > 0 ? (100f * clipped / landPts) : 0f).ToString("F3")
            + "%) -- anything above a fraction of a percent is flat tops coming back");
        lut.Dispose();
        System.IO.File.WriteAllText("/tmp/seasick-survey.txt", sb.ToString());
        return sb.ToString();
    }

    /// What a beach here actually MEASURES, in the units a beach is
    /// described in: how far you walk from the waterline to get 2 m above it.
    ///
    /// "Walkable" is not "a beach". HeightProbe's gate passes anything under
    /// 1.5 m of rise per metre, which is a 56 degree slope -- it is a
    /// cliff-detector, and it was letting through exactly the yellow
    /// mountainside Kevin is complaining about. Real foreshores run 1:15 to
    /// 1:40, so the number to chase is a DISTANCE: tens of metres of sand
    /// between the water and the back of the beach.
    ///
    /// Finds real shoreline crossings and walks inland up the gradient.
    public static string Beaches()
    {
        var s = AssetDatabase.LoadAssetAtPath<TerrainSettings>(Path);
        if (s == null) return "no TerrainSettings asset at " + Path;
        var prm = TerrainParams.From(s);
        var lut = TerrainCurveLut.Bake(s.profileCurve, Allocator.Temp);

        var widths = new System.Collections.Generic.List<float>();
        var slopes = new System.Collections.Generic.List<float>();
        var rng = new System.Random(4242);
        int tried = 0, found = 0;
        const float Target = 2f;      // "the back of the beach"
        const float Step = 2f;        // walk resolution, metres
        const float MaxWalk = 400f;

        while (found < 900 && tried < 900000)
        {
            tried++;
            float x = (float)(rng.NextDouble() * 24000.0 - 12000.0);
            float z = (float)(rng.NextDouble() * 24000.0 - 12000.0);
            var p0 = new float2(x, z);
            float h0 = TerrainHeight.Height(p0, prm, lut);
            if (h0 < -0.4f || h0 > 0.4f) continue;   // on the waterline

            // Uphill direction from the local gradient.
            const float e = 4f;
            float hx = TerrainHeight.Height(p0 + new float2(e, 0f), prm, lut)
                     - TerrainHeight.Height(p0 - new float2(e, 0f), prm, lut);
            float hz = TerrainHeight.Height(p0 + new float2(0f, e), prm, lut)
                     - TerrainHeight.Height(p0 - new float2(0f, e), prm, lut);
            var g = new float2(hx, hz);
            if (math.lengthsq(g) < 1e-9f) continue;
            g = math.normalize(g);

            float walked = 0f;
            float h = h0;
            while (walked < MaxWalk && h < Target)
            {
                walked += Step;
                h = TerrainHeight.Height(p0 + g * walked, prm, lut);
            }
            if (h < Target) continue;                 // never got there: a flat
            found++;
            widths.Add(walked);
            slopes.Add((h - h0) / walked);
        }
        lut.Dispose();
        if (found < 20) return "only " + found + " shoreline crossings found in " + tried + " tries";

        widths.Sort(); slopes.Sort();
        var sb = new StringBuilder();
        sb.AppendLine(found + " shoreline crossings, walking from the waterline to +" + Target + " m");
        sb.AppendLine("beach WIDTH (m):  p10 " + widths[found / 10].ToString("F1")
            + "   p50 " + widths[found / 2].ToString("F1")
            + "   p90 " + widths[found * 9 / 10].ToString("F1"));
        sb.AppendLine("beach SLOPE:      p10 1:" + (1f / slopes[found * 9 / 10]).ToString("F0")
            + "   p50 1:" + (1f / slopes[found / 2]).ToString("F0")
            + "   p90 1:" + (1f / slopes[found / 10]).ToString("F0")
            + "   (real foreshores are 1:15 to 1:40)");
        int steep = 0;
        for (int k = 0; k < found; k++) if (slopes[k] > 0.2f) steep++;   // steeper than 1:5
        sb.AppendLine("coasts steeper than 1:5 (a yellow mountainside): "
            + (100f * steep / found).ToString("F0") + "%");
        System.IO.File.WriteAllText("/tmp/seasick-beaches.txt", sb.ToString());
        return sb.ToString();
    }
}
