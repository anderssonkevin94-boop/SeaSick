using System.Text;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.World;
using SeaSick.Terrain;

/// **Are the islands actually different from each other?**
///
/// The complaint this exists to answer is "they always look like one weird
/// shape", and that is not a thing you can settle by looking at two of them.
/// It is a question about the SPREAD across the world: if every island lands
/// on the same peak-to-width, the same rock fraction and the same walkable
/// fraction, then there is one island in this game and sixteen copies of it,
/// whatever the silhouettes happen to do.
///
/// So this reports the distribution, and the summary line at the bottom is
/// the actual answer.
public class IslandVariety : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("IslandVariety: not in play mode"); return; }
        var pop = FindFirstObjectByType<TerrainWorldPopulator>();
        if (pop == null || pop.terrain == null || Island.TerrainHeight == null)
        { Debug.LogError("IslandVariety: no populator"); return; }
        var prm = TerrainParams.From(pop.terrain);

        var sb = new StringBuilder();
        // Size is the FOOTPRINT, derived from the land actually sampled --
        // not `MaxRadius`, which is the radial API's largest sector and on a
        // lobed island is the length of one thin arm. This project has been
        // fooled by that number before (the "355 m island" that was really
        // 900-2000 m of land), and the first version of this probe reported
        // a 3684 m island that has 47 ha in it.
        sb.AppendLine("island           area ha  r_eff  r_max   peak  peak/r_eff  rocky%  walk%  rockiness");
        var ratios = new System.Collections.Generic.List<float>();
        var rockies = new System.Collections.Generic.List<float>();
        var walks = new System.Collections.Generic.List<float>();
        var sizes = new System.Collections.Generic.List<float>();
        var peaks = new System.Collections.Generic.List<float>();

        foreach (var isle in Island.All)
        {
            if (isle == null) continue;
            Vector3 c = isle.transform.position;
            float reach = isle.MaxRadius;
            float step = Mathf.Max(6f, reach / 45f);
            int land = 0, rocky = 0, walkable = 0;
            float peak = 0f;
            for (float z = -reach; z <= reach; z += step)
                for (float x = -reach; x <= reach; x += step)
                {
                    float wx = c.x + x, wz = c.z + z;
                    float h = Island.TerrainHeight(wx, wz);
                    if (h < 0.5f) continue;
                    land++;
                    if (h > peak) peak = h;
                    float sx = (Island.TerrainHeight(wx + 3f, wz) - Island.TerrainHeight(wx - 3f, wz)) / 6f;
                    float sz = (Island.TerrainHeight(wx, wz + 3f) - Island.TerrainHeight(wx, wz - 3f)) / 6f;
                    if (Mathf.Sqrt(sx * sx + sz * sz) < 0.466f) walkable++;   // under 25 degrees
                    if (TerrainHeight.RockBreak(new float2(wx, wz),
                            TerrainHeight.Rock01(new float2(wx, wz), prm), prm) > 0.5f) rocky++;
                }
            if (land == 0) continue;

            float areaHa = land * step * step / 10000f;
            float rEff = Mathf.Sqrt(land * step * step / Mathf.PI);
            float ratio = peak / Mathf.Max(1f, rEff);
            float rockPct = 100f * rocky / land;
            float walkPct = 100f * walkable / land;
            float rockiness = TerrainHeight.Rock01(new float2(c.x, c.z), prm);
            ratios.Add(ratio); rockies.Add(rockPct); walks.Add(walkPct);
            sizes.Add(rEff); peaks.Add(peak);
            sb.AppendLine($"{isle.name,-14} {areaHa,8:F1} {rEff,6:F0} {reach,6:F0} {peak,6:F0} "
                + $"{ratio,11:F3} {rockPct,7:F1} {walkPct,6:F1} {rockiness,10:F2}");
        }

        sb.AppendLine();
        sb.AppendLine($"peak/r_eff  {Spread(ratios)}   (references read 0.30-0.45)");
        sb.AppendLine($"rocky %     {Spread(rockies)}");
        sb.AppendLine($"walkable %  {Spread(walks)}");
        sb.AppendLine();
        // Does size predict height? In the references the big island IS the
        // dramatic one, so a strong POSITIVE correlation is the target and a
        // negative one is the bug.
        sb.AppendLine($"corr(r_eff, peak)      {Corr(sizes, peaks),6:F2}   (want strongly positive)");
        sb.AppendLine($"corr(r_eff, peak/r_eff) {Corr(sizes, ratios),6:F2}   (want >= 0; negative means big islands are pancakes)");
        sb.AppendLine();
        sb.AppendLine("A world with variety has a WIDE spread on every line. One shape at");
        sb.AppendLine("sixteen sizes has a narrow one, however different the sizes are.");

        Debug.Log("ISLAND VARIETY\n" + sb);
        System.IO.File.WriteAllText("/tmp/island-variety.txt", sb.ToString());
    }

    /// Pearson correlation. The lists stay in island order, which is why
    /// `Spread` sorts a COPY -- sorting these in place would silently pair
    /// each island's size with a different island's height.
    static float Corr(System.Collections.Generic.List<float> a,
                      System.Collections.Generic.List<float> b)
    {
        int n = Mathf.Min(a.Count, b.Count);
        if (n < 3) return 0f;
        double ma = 0, mb = 0;
        for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
        ma /= n; mb /= n;
        double sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < n; i++)
        {
            double da = a[i] - ma, db = b[i] - mb;
            sab += da * db; saa += da * da; sbb += db * db;
        }
        return saa <= 0 || sbb <= 0 ? 0f : (float)(sab / System.Math.Sqrt(saa * sbb));
    }

    static string Spread(System.Collections.Generic.List<float> v)
    {
        if (v.Count == 0) return "no data";
        v = new System.Collections.Generic.List<float>(v);
        v.Sort();
        float min = v[0], max = v[v.Count - 1], med = v[v.Count / 2];
        double mean = 0; foreach (var x in v) mean += x;
        mean /= v.Count;
        double sd = 0; foreach (var x in v) sd += (x - mean) * (x - mean);
        sd = System.Math.Sqrt(sd / v.Count);
        return $"min {min,7:F2}  median {med,7:F2}  max {max,7:F2}  sd {sd,7:F2}";
    }
}
