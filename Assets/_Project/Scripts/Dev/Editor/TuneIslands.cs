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
        so.FindProperty("uplandFrequency").floatValue = 1f / 700f;
        so.FindProperty("uplandStart").floatValue = 0.53f;
        so.FindProperty("uplandFull").floatValue = 0.78f;
        so.FindProperty("plainRelief").floatValue = 26f;
        so.FindProperty("lowlandDetail").floatValue = 0.25f;
        so.FindProperty("detailAmplitude").floatValue = 0.5f;
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
        so.FindProperty("skirtDepth").floatValue = 14f;
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
        sb.AppendLine("upland 1/" + (1f / check.uplandFrequency).ToString("F0") + " m, upland band " + check.uplandStart + ".." + check.uplandFull
            + ", plain relief " + check.plainRelief + " m detail x" + check.lowlandDetail
            + " (detail amp " + check.detailAmplitude + ")");
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

    /// How deep the LOD skirts actually need to be.
    ///
    /// A skirt hides the crack where a full-density chunk meets a strided
    /// one: it must be deeper than the worst height the coarse edge misses.
    /// That error scales with RELIEF, so raising the massifs meant the old
    /// 6 m was no longer safe -- but 24 m was a guess in the other
    /// direction, and a skirt is a vertical curtain of edge-coloured
    /// geometry that hangs in plain sight at the edge of the loaded region.
    /// Too deep is a visible wall; too shallow is a crack you can see the
    /// sky through. So measure it.
    ///
    /// Walks chunk edges at full density and compares each vertex against
    /// the straight line a strided edge would draw between the samples it
    /// keeps -- which IS the crack.
    public static string Skirt()
    {
        var s = AssetDatabase.LoadAssetAtPath<TerrainSettings>(Path);
        if (s == null) return "no TerrainSettings asset at " + Path;
        var prm = TerrainParams.From(s);
        var lut = TerrainCurveLut.Bake(s.profileCurve, Allocator.Temp);

        int cells = s.chunkResolution - 1;
        float spacing = s.chunkSize / cells;
        var worst = new System.Collections.Generic.List<float>();
        float globalWorst = 0f; int worstLod = 0;
        var rng = new System.Random(31337);

        for (int trial = 0; trial < 1400; trial++)
        {
            // A chunk edge somewhere in the archipelago.
            float x0 = (float)(rng.NextDouble() * 20000.0 - 10000.0);
            float z0 = (float)(rng.NextDouble() * 20000.0 - 10000.0);
            bool alongX = rng.Next(2) == 0;

            // Only edges with land on them matter.
            if (TerrainHeight.Height(new float2(x0, z0), prm, lut) < -2f) continue;

            foreach (int stride in new[] { 2, 4 })
            {
                float local = 0f;
                for (int k = 0; k <= cells; k++)
                {
                    int lo = (k / stride) * stride;
                    int hi = math.min(lo + stride, cells);
                    float t = hi > lo ? (k - lo) / (float)(hi - lo) : 0f;

                    float2 P(int idx) => alongX
                        ? new float2(x0 + idx * spacing, z0)
                        : new float2(x0, z0 + idx * spacing);

                    float fine = TerrainHeight.Height(P(k), prm, lut);
                    float coarse = math.lerp(TerrainHeight.Height(P(lo), prm, lut),
                                             TerrainHeight.Height(P(hi), prm, lut), t);
                    local = math.max(local, math.abs(fine - coarse));
                }
                worst.Add(local);
                if (local > globalWorst) { globalWorst = local; worstLod = stride; }
            }
        }
        lut.Dispose();
        worst.Sort();
        var sb = new StringBuilder();
        int n = worst.Count;
        sb.AppendLine(n + " chunk edges over land, strides 2 and 4, " + spacing.ToString("F1") + " m vertices");
        sb.AppendLine("LOD crack depth (m):  p50 " + worst[n / 2].ToString("F2")
            + "   p90 " + worst[n * 9 / 10].ToString("F2")
            + "   p99 " + worst[n - n / 100].ToString("F2")
            + "   max " + globalWorst.ToString("F2") + " (at stride " + worstLod + ")");
        sb.AppendLine("skirtDepth is " + s.skirtDepth + " m; it needs to clear the max, and every metre "
            + "beyond that is curtain hanging in view at the edge of the loaded region");
        System.IO.File.WriteAllText("/tmp/seasick-skirt.txt", sb.ToString());
        return sb.ToString();
    }

    /// What the land is like UNDERFOOT, which is a different question from
    /// what it looks like from the water.
    ///
    /// Kevin: "the people who leave my boat need to be able to traverse the
    /// land... i like the mountains, but its way too much and way too often".
    /// Outposts and walled sections need ground you can walk, stand a
    /// building on, and run a wall across. So the measure is not height or
    /// drama, it is the DISTRIBUTION OF SLOPE over island area:
    ///
    ///   under 1:6  (about 10 deg) -- you can put a building on it
    ///   under 1:3  (about 18 deg) -- you can walk it carrying something
    ///   over  1:1.4 (about 36 deg) -- that is climbing, not walking
    ///
    /// Sampled over dry land only, area-weighted, so it answers "how much of
    /// the island is usable" rather than "how steep is the steepest bit".
    public static string Walkable()
    {
        var s = AssetDatabase.LoadAssetAtPath<TerrainSettings>(Path);
        if (s == null) return "no TerrainSettings asset at " + Path;
        var prm = TerrainParams.From(s);
        var lut = TerrainCurveLut.Bake(s.profileCurve, Allocator.Temp);

        int land = 0, build = 0, walk = 0, climb = 0, sandFlat = 0;
        var slopes = new System.Collections.Generic.List<float>();
        var rng = new System.Random(8080);
        const float e = 3f;
        for (int k = 0; k < 260000 && land < 60000; k++)
        {
            float x = (float)(rng.NextDouble() * 24000.0 - 12000.0);
            float z = (float)(rng.NextDouble() * 24000.0 - 12000.0);
            var p0 = new float2(x, z);
            float h = TerrainHeight.Height(p0, prm, lut);
            if (h <= 0.5f) continue;                     // dry land only
            land++;
            float dx = (TerrainHeight.Height(p0 + new float2(e, 0f), prm, lut)
                      - TerrainHeight.Height(p0 - new float2(e, 0f), prm, lut)) / (2f * e);
            float dz = (TerrainHeight.Height(p0 + new float2(0f, e), prm, lut)
                      - TerrainHeight.Height(p0 - new float2(0f, e), prm, lut)) / (2f * e);
            float slope = math.sqrt(dx * dx + dz * dz);
            slopes.Add(slope);
            if (h < s.sandHeight) sandFlat++;
            if (slope < 0.176f) build++;
            if (slope < 0.325f) walk++;
            if (slope > 0.727f) climb++;
        }
        lut.Dispose();
        if (land < 500) return "not enough land sampled (" + land + ")";
        slopes.Sort();
        var sb = new StringBuilder();
        sb.AppendLine(land + " samples of dry land");
        sb.AppendLine("buildable (under 10 deg): " + (100f * build / land).ToString("F1") + "%");
        sb.AppendLine("walkable  (under 18 deg): " + (100f * walk / land).ToString("F1") + "%");
        sb.AppendLine("climbing  (over  36 deg): " + (100f * climb / land).ToString("F1") + "%");
        // How much of the island is painted as BEACH. Flattening the
        // interior without lifting it put a low island's whole surface under
        // the sand line and rendered it as one enormous sand flat: the land
        // was flat, which was wanted, but flat at the wrong ALTITUDE.
        sb.AppendLine("under the sand line (reads as beach): " + (100f * sandFlat / land).ToString("F1")
            + "% of dry land");
        sb.AppendLine("slope p50 1:" + (1f / math.max(slopes[land / 2], 1e-4f)).ToString("F1")
            + "   p90 1:" + (1f / math.max(slopes[land * 9 / 10], 1e-4f)).ToString("F1"));
        System.IO.File.WriteAllText("/tmp/seasick-walkable.txt", sb.ToString());
        return sb.ToString();
    }

    // ================= CONTIGUOUS BUILDABLE GROUND ========================
    //
    // `Walkable` above answers "what fraction of the land is under 10 deg"
    // by throwing darts at the world, and 41.5 % is a true answer to a
    // question nobody is going to build on. A settlement needs ground that is
    // flat AND JOINED UP: a thousand flat patches a boat-length across add up
    // to the same percentage as one field you could put a walled compound in,
    // and a dart-throwing sampler cannot tell those apart even in principle.
    //
    // So: rasterise, label, and measure the biggest piece.
    //
    // Two numbers come out of it and they are not interchangeable. AREA says
    // how much flat ground is joined together; the INSCRIBED CIRCLE says how
    // big a thing fits inside it. A ribbon of flat ground following a contour
    // can carry hectares and not hold a 40 m palisade anywhere along its
    // length, and that ribbon is exactly what a terraced island produces --
    // the benches ARE contour-following ribbons. Area alone would have
    // reported this island as ready to build on.

    /// Cell size of the fine raster, in metres. Small enough to resolve a
    /// hut's footprint; the slope it reports is therefore a slope over 8 m,
    /// against the 6 m `Walkable` uses, and the two are cross-checked below
    /// rather than assumed to agree.
    const float FineCell = 4f;

    /// Coarse cell for finding the islands in the first place.
    const float CoarseCell = 25f;

    /// How far out to look for islands, in metres from home.
    const float SearchExtent = 8000f;

    /// How many of the nearest islands to raster in full.
    const int IslandsMeasured = 8;

    struct Patch
    {
        public int cells;
        public float insetCells;     // radius of the largest inscribed circle
        public float2 insetAt;
        public float loY, hiY;
    }

    public static string Flats()
    {
        var s = AssetDatabase.LoadAssetAtPath<TerrainSettings>(Path);
        if (s == null) return "no TerrainSettings asset at " + Path;
        var prm = TerrainParams.From(s);
        var lut = TerrainCurveLut.Bake(s.profileCurve, Allocator.Temp);
        var sb = new StringBuilder();

        // --- stage 1: where are the islands ------------------------------
        int cn = (int)(2f * SearchExtent / CoarseCell);
        var coarseLand = new bool[cn * cn];
        for (int j = 0; j < cn; j++)
        {
            float z = -SearchExtent + (j + 0.5f) * CoarseCell;
            for (int i = 0; i < cn; i++)
            {
                float x = -SearchExtent + (i + 0.5f) * CoarseCell;
                coarseLand[j * cn + i] = TerrainHeight.Height(new float2(x, z), prm, lut) > 0.5f;
            }
        }
        var coarseId = Label(coarseLand, cn, cn);
        int nIslands = 0;
        foreach (var v in coarseId) if (v > nIslands) nIslands = v;

        var area = new int[nIslands + 1];
        var sumX = new double[nIslands + 1];
        var sumZ = new double[nIslands + 1];
        for (int j = 0; j < cn; j++)
            for (int i = 0; i < cn; i++)
            {
                int id = coarseId[j * cn + i];
                if (id == 0) continue;
                area[id]++;
                sumX[id] += -SearchExtent + (i + 0.5f) * CoarseCell;
                sumZ[id] += -SearchExtent + (j + 0.5f) * CoarseCell;
            }

        var order = new System.Collections.Generic.List<int>();
        for (int id = 1; id <= nIslands; id++)
            if (area[id] * CoarseCell * CoarseCell > 20000f) order.Add(id);   // 2 ha or bigger
        order.Sort((a, b) =>
        {
            double da = sumX[a] * sumX[a] + sumZ[a] * sumZ[a], db = sumX[b] * sumX[b] + sumZ[b] * sumZ[b];
            return (da / (area[a] * (double)area[a])).CompareTo(db / (area[b] * (double)area[b]));
        });

        sb.AppendLine(order.Count + " islands over 2 ha within " + (SearchExtent / 1000f) + " km of home"
            + " (coarse pass " + CoarseCell + " m); measuring the nearest " + IslandsMeasured);
        sb.AppendLine("fine raster " + FineCell + " m, land = height > 0.5 m, 4-connected");
        sb.AppendLine("buildable = slope < 10 deg (0.176), walkable = slope < 18 deg (0.325)");
        sb.AppendLine();

        float bigFlatBest = 0f, insetBest = 0f;
        var insetAll = new System.Collections.Generic.List<float>();
        var flatFracAll = new System.Collections.Generic.List<float>();
        int shown = 0;

        foreach (int id in order)
        {
            if (shown >= IslandsMeasured) break;
            shown++;

            // bbox of this island in world metres, padded clear of it
            float minX = 9e9f, maxX = -9e9f, minZ = 9e9f, maxZ = -9e9f;
            for (int j = 0; j < cn; j++)
                for (int i = 0; i < cn; i++)
                {
                    if (coarseId[j * cn + i] != id) continue;
                    float x = -SearchExtent + (i + 0.5f) * CoarseCell;
                    float z = -SearchExtent + (j + 0.5f) * CoarseCell;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
                }
            float pad = CoarseCell * 3f;
            minX -= pad; maxX += pad; minZ -= pad; maxZ += pad;

            int w = Mathf.CeilToInt((maxX - minX) / FineCell);
            int h = Mathf.CeilToInt((maxZ - minZ) / FineCell);

            var hgt = new float[w * h];
            for (int j = 0; j < h; j++)
            {
                float z = minZ + (j + 0.5f) * FineCell;
                for (int i = 0; i < w; i++)
                {
                    float x = minX + (i + 0.5f) * FineCell;
                    hgt[j * w + i] = TerrainHeight.Height(new float2(x, z), prm, lut);
                }
            }

            // Land of THIS island only: flood from its coarse cells, so a
            // neighbour sharing the bounding box is not counted as part of it.
            var landM = new bool[w * h];
            for (int k = 0; k < landM.Length; k++) landM[k] = hgt[k] > 0.5f;
            var seeds = new System.Collections.Generic.List<int>();
            for (int j = 0; j < cn; j++)
                for (int i = 0; i < cn; i++)
                {
                    if (coarseId[j * cn + i] != id) continue;
                    float x = -SearchExtent + (i + 0.5f) * CoarseCell;
                    float z = -SearchExtent + (j + 0.5f) * CoarseCell;
                    int fi = (int)((x - minX) / FineCell), fj = (int)((z - minZ) / FineCell);
                    if (fi < 0 || fj < 0 || fi >= w || fj >= h) continue;
                    if (landM[fj * w + fi]) seeds.Add(fj * w + fi);
                }
            var mine = Flood(landM, w, h, seeds);

            // Is this ONE island? The coarse pass groups land at 25 m, so a
            // strait narrower than that reads as solid ground and two islands
            // arrive as one -- which would leave every percentage below with
            // the wrong denominator. Re-labelling the flooded land at 4 m
            // says how many pieces it really is. (Only the denominators are
            // at risk: the buildable and walkable components are labelled on
            // this same fine grid, so a compound never spans a strait.)
            float cellArea = FineCell * FineCell;
            var landPieces = Label(mine, w, h);
            var pieceCount = new System.Collections.Generic.Dictionary<int, int>();
            foreach (var pv in landPieces)
            {
                if (pv == 0) continue;
                pieceCount.TryGetValue(pv, out int pc);
                pieceCount[pv] = pc + 1;
            }
            int biggestPiece = 0, piecesOver1Ha = 0;
            foreach (var kv in pieceCount)
            {
                if (kv.Value > biggestPiece) biggestPiece = kv.Value;
                if (kv.Value * FineCell * FineCell > 10000f) piecesOver1Ha++;
            }

            // Slope from the raster itself: one height evaluation per cell
            // instead of five, and a slope over 8 m is a fairer question to
            // ask on behalf of a building than a slope at a point.
            var build = new bool[w * h];
            var walk = new bool[w * h];
            int landCells = 0, buildCells = 0, walkCells = 0;
            for (int j = 1; j < h - 1; j++)
                for (int i = 1; i < w - 1; i++)
                {
                    int k = j * w + i;
                    if (!mine[k]) continue;
                    landCells++;
                    float dx = (hgt[k + 1] - hgt[k - 1]) / (2f * FineCell);
                    float dz = (hgt[k + w] - hgt[k - w]) / (2f * FineCell);
                    float sl = Mathf.Sqrt(dx * dx + dz * dz);
                    if (sl < 0.176f) { build[k] = true; buildCells++; }
                    if (sl < 0.325f) { walk[k] = true; walkCells++; }
                }
            if (landCells < 50) continue;

            var bigBuild = Biggest(build, w, h, hgt, minX, minZ);
            var bigWalk = Biggest(walk, w, h, hgt, minX, minZ);

            float landHa = landCells * cellArea / 10000f;
            float flatHa = bigBuild.cells * cellArea / 10000f;
            float inset = bigBuild.insetCells * FineCell;
            float cx = (float)(sumX[id] / area[id]), cz = (float)(sumZ[id] / area[id]);

            insetAll.Add(inset);
            flatFracAll.Add(100f * bigBuild.cells / Mathf.Max(1, landCells));
            if (flatHa > bigFlatBest) bigFlatBest = flatHa;
            if (inset > insetBest) insetBest = inset;

            sb.AppendLine("island at (" + cx.ToString("F0") + ", " + cz.ToString("F0") + "), "
                + (Mathf.Sqrt(cx * cx + cz * cz) / 1000f).ToString("F1") + " km from home");
            sb.AppendLine("   land " + landHa.ToString("F1") + " ha, "
                + (maxX - minX).ToString("F0") + " x " + (maxZ - minZ).ToString("F0") + " m across"
                + " -- at 4 m it is " + pieceCount.Count + " piece(s), "
                + piecesOver1Ha + " over 1 ha, biggest "
                + (biggestPiece * cellArea / 10000f).ToString("F1") + " ha");
            sb.AppendLine("   buildable " + (100f * buildCells / landCells).ToString("F1")
                + "%, walkable " + (100f * walkCells / landCells).ToString("F1") + "%");
            sb.AppendLine("   LARGEST CONTIGUOUS BUILDABLE: " + flatHa.ToString("F2") + " ha = "
                + (100f * bigBuild.cells / Mathf.Max(1, buildCells)).ToString("F0") + "% of the island's flat ground, "
                + "y " + bigBuild.loY.ToString("F0") + "-" + bigBuild.hiY.ToString("F0") + " m");
            // How far the crew walk from the water to reach it. A compound
            // they cannot land beside is a compound in the wrong place, and
            // "buildable" says nothing at all about that.
            float toShore = ShoreRun(landM, w, h, bigBuild.insetAt, minX, minZ);
            sb.AppendLine("      biggest thing that fits inside it: " + (2f * inset).ToString("F0")
                + " m across, at (" + bigBuild.insetAt.x.ToString("F0") + ", " + bigBuild.insetAt.y.ToString("F0") + ")"
                + ", " + toShore.ToString("F0") + " m from the water");
            sb.AppendLine("   largest contiguous WALKABLE: "
                + (bigWalk.cells * cellArea / 10000f).ToString("F1") + " ha = "
                + (100f * bigWalk.cells / Mathf.Max(1, walkCells)).ToString("F0")
                + "% of the walkable ground (crew traversing it stay on one piece)");
        }

        // --- the instrument, checked against the one we already had -------
        //
        // This raster reads slope over 8 m; Walkable reads it over 6 m at a
        // point. If the two disagree badly then one of them is measuring the
        // 25 m detail layer and the other is stepping over it, and the
        // buildable fraction here would be a property of the cell size
        // rather than of the terrain.
        {
            int a = 0, b = 0, n2 = 0;
            var rng = new System.Random(5150);
            const float e = 3f;
            for (int k = 0; k < 400000 && n2 < 20000; k++)
            {
                float x = (float)(rng.NextDouble() * 16000.0 - 8000.0);
                float z = (float)(rng.NextDouble() * 16000.0 - 8000.0);
                var p0 = new float2(x, z);
                if (TerrainHeight.Height(p0, prm, lut) <= 0.5f) continue;
                n2++;
                float dx6 = (TerrainHeight.Height(p0 + new float2(e, 0f), prm, lut)
                           - TerrainHeight.Height(p0 - new float2(e, 0f), prm, lut)) / (2f * e);
                float dz6 = (TerrainHeight.Height(p0 + new float2(0f, e), prm, lut)
                           - TerrainHeight.Height(p0 - new float2(0f, e), prm, lut)) / (2f * e);
                if (Mathf.Sqrt(dx6 * dx6 + dz6 * dz6) < 0.176f) a++;
                float g = FineCell;
                float dx8 = (TerrainHeight.Height(p0 + new float2(g, 0f), prm, lut)
                           - TerrainHeight.Height(p0 - new float2(g, 0f), prm, lut)) / (2f * g);
                float dz8 = (TerrainHeight.Height(p0 + new float2(0f, g), prm, lut)
                           - TerrainHeight.Height(p0 - new float2(0f, g), prm, lut)) / (2f * g);
                if (Mathf.Sqrt(dx8 * dx8 + dz8 * dz8) < 0.176f) b++;
            }
            sb.AppendLine();
            sb.AppendLine("instrument check on " + n2 + " land points: buildable reads "
                + (100f * a / Mathf.Max(1, n2)).ToString("F1") + "% over a 6 m baseline, "
                + (100f * b / Mathf.Max(1, n2)).ToString("F1") + "% over the raster's 8 m");
        }

        insetAll.Sort();
        if (insetAll.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("across the " + insetAll.Count + " islands measured:");
            sb.AppendLine("   biggest contiguous flat: " + bigFlatBest.ToString("F2") + " ha");
            sb.AppendLine("   compound that fits, p50 " + (2f * insetAll[insetAll.Count / 2]).ToString("F0")
                + " m across, best " + (2f * insetBest).ToString("F0") + " m");
            sb.AppendLine("   for scale: palisade " + SeaSick.World.WorldScale.Palisade
                + " m tall, longhouse " + SeaSick.World.WorldScale.Longhouse
                + " m, the ship is " + SeaSick.World.WorldScale.ShipLength + " m long");
        }

        lut.Dispose();
        System.IO.File.WriteAllText("/tmp/seasick-flats.txt", sb.ToString());
        return sb.ToString();
    }

    /// Straight-line distance from a spot to the nearest water, in metres.
    /// A ring search outward, so it stops at the first hit rather than
    /// scanning the whole raster.
    static float ShoreRun(bool[] land, int w, int h, float2 at, float minX, float minZ)
    {
        int ci = (int)((at.x - minX) / FineCell), cj = (int)((at.y - minZ) / FineCell);
        for (int r = 1; r < Mathf.Max(w, h); r++)
        {
            for (int j = cj - r; j <= cj + r; j++)
                for (int i = ci - r; i <= ci + r; i++)
                {
                    if (Mathf.Max(Mathf.Abs(i - ci), Mathf.Abs(j - cj)) != r) continue;
                    if (i < 0 || j < 0 || i >= w || j >= h) return r * FineCell;
                    if (!land[j * w + i])
                    {
                        float di = i - ci, dj = j - cj;
                        return Mathf.Sqrt(di * di + dj * dj) * FineCell;
                    }
                }
        }
        return -1f;
    }

    /// 4-connected component labels, 1-based; 0 is not-set.
    static int[] Label(bool[] m, int w, int h)
    {
        var id = new int[w * h];
        var stack = new System.Collections.Generic.Stack<int>();
        int next = 0;
        for (int start = 0; start < m.Length; start++)
        {
            if (!m[start] || id[start] != 0) continue;
            next++;
            stack.Push(start); id[start] = next;
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                int i = k % w, j = k / w;
                if (i > 0 && m[k - 1] && id[k - 1] == 0) { id[k - 1] = next; stack.Push(k - 1); }
                if (i < w - 1 && m[k + 1] && id[k + 1] == 0) { id[k + 1] = next; stack.Push(k + 1); }
                if (j > 0 && m[k - w] && id[k - w] == 0) { id[k - w] = next; stack.Push(k - w); }
                if (j < h - 1 && m[k + w] && id[k + w] == 0) { id[k + w] = next; stack.Push(k + w); }
            }
        }
        return id;
    }

    /// Everything reachable from any seed, 4-connected.
    static bool[] Flood(bool[] m, int w, int h, System.Collections.Generic.List<int> seeds)
    {
        var got = new bool[w * h];
        var stack = new System.Collections.Generic.Stack<int>();
        foreach (int sd in seeds) if (!got[sd]) { got[sd] = true; stack.Push(sd); }
        while (stack.Count > 0)
        {
            int k = stack.Pop();
            int i = k % w, j = k / w;
            if (i > 0 && m[k - 1] && !got[k - 1]) { got[k - 1] = true; stack.Push(k - 1); }
            if (i < w - 1 && m[k + 1] && !got[k + 1]) { got[k + 1] = true; stack.Push(k + 1); }
            if (j > 0 && m[k - w] && !got[k - w]) { got[k - w] = true; stack.Push(k - w); }
            if (j < h - 1 && m[k + w] && !got[k + w]) { got[k + w] = true; stack.Push(k + w); }
        }
        return got;
    }

    /// The biggest connected piece of a mask, and the largest circle that
    /// fits inside it -- which is the number a walled compound actually
    /// cares about, and the one that area cannot substitute for.
    static Patch Biggest(bool[] mask, int w, int h, float[] hgt, float minX, float minZ)
    {
        var id = Label(mask, w, h);
        int best = 0, bestId = 0;
        var count = new System.Collections.Generic.Dictionary<int, int>();
        foreach (var v in id)
        {
            if (v == 0) continue;
            count.TryGetValue(v, out int c);
            count[v] = c + 1;
            if (c + 1 > best) { best = c + 1; bestId = v; }
        }
        var p = new Patch { cells = best, loY = 9e9f, hiY = -9e9f };
        if (bestId == 0) { p.loY = p.hiY = 0f; return p; }

        var inside = new bool[w * h];
        for (int k = 0; k < id.Length; k++)
            if (id[k] == bestId)
            {
                inside[k] = true;
                if (hgt[k] < p.loY) p.loY = hgt[k];
                if (hgt[k] > p.hiY) p.hiY = hgt[k];
            }

        var d2 = Edt(inside, w, h);
        float bestD = -1f; int bestK = 0;
        for (int k = 0; k < d2.Length; k++)
            if (inside[k] && d2[k] > bestD) { bestD = d2[k]; bestK = k; }
        p.insetCells = Mathf.Sqrt(Mathf.Max(0f, bestD));
        p.insetAt = new float2(minX + (bestK % w + 0.5f) * FineCell, minZ + (bestK / w + 0.5f) * FineCell);
        return p;
    }

    /// Exact squared Euclidean distance to the nearest cell OUTSIDE the mask
    /// (Felzenszwalb & Huttenlocher, two separable passes of a lower
    /// envelope). Exact rather than a chamfer approximation because the
    /// answer is a building size and a few per cent is a wall.
    static float[] Edt(bool[] inside, int w, int h)
    {
        const float INF = 1e20f;
        var f = new float[w * h];
        for (int k = 0; k < f.Length; k++) f[k] = inside[k] ? INF : 0f;

        int n = Mathf.Max(w, h);
        var d = new float[n]; var v = new int[n]; var zb = new float[n + 1]; var col = new float[n];

        for (int j = 0; j < h; j++)                     // rows
        {
            for (int i = 0; i < w; i++) col[i] = f[j * w + i];
            Env1D(col, d, v, zb, w);
            for (int i = 0; i < w; i++) f[j * w + i] = d[i];
        }
        for (int i = 0; i < w; i++)                     // columns
        {
            for (int j = 0; j < h; j++) col[j] = f[j * w + i];
            Env1D(col, d, v, zb, h);
            for (int j = 0; j < h; j++) f[j * w + i] = d[j];
        }
        return f;
    }

    static void Env1D(float[] f, float[] d, int[] v, float[] z, int n)
    {
        const float INF = 1e20f;
        int k = 0; v[0] = 0; z[0] = -INF; z[1] = INF;
        for (int q = 1; q < n; q++)
        {
            float s;
            while (true)
            {
                s = ((f[q] + q * q) - (f[v[k]] + v[k] * (float)v[k])) / (2f * q - 2f * v[k]);
                if (s <= z[k] && k > 0) k--; else break;
            }
            k++; v[k] = q; z[k] = s; z[k + 1] = INF;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            float dq = q - v[k];
            d[q] = dq * dq + f[v[k]];
        }
    }

}
