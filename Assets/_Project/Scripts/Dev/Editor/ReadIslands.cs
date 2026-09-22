using UnityEngine;
using SeaSick.Ocean;

/// What the ocean actually bound, against what the world actually has.
///
/// The cap was 24 against a 65-island world and nothing reported it, so two
/// thirds of the archipelago had no shelter term for two days. A number that
/// silently truncates needs a reader.
public static class ReadIslands
{
    public static void Execute()
    {
        var rf = RegionField.Instance;
        if (rf == null) { Debug.LogError("ReadIslands: no RegionField (play mode, Sea.unity)"); return; }

        int world = SeaSick.World.Island.All.Count;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ReadIslands: world has {world}, ocean considered {rf.IslandsConsidered}, "
            + $"BOUND {rf.IslandCount} of a cap of {RegionField.MaxIslands}, "
            + $"dropped-in-range {rf.IslandsDropped}");
        sb.AppendLine($"  LIVE islandSelectRadius = {rf.IslandSelectRadius:F0} m "
            + "(read off the component, not the source)");

        // The bound set, nearest first, so a truncation is visible as a jump in
        // the gap column rather than having to be inferred.
        var isles = rf.Islands;
        var cam = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        var focus = new Vector2(cam.x, cam.z);
        for (int i = 0; i < rf.IslandCount && i < 8; i++)
        {
            var v = isles[i];
            float gap = Vector2.Distance(new Vector2(v.x, v.y), focus) - v.z;
            sb.AppendLine($"   [{i}] centre ({v.x,7:F0},{v.y,7:F0}) r {v.z,5:F0}  gap {gap,7:F0} m");
        }
        if (rf.IslandCount > 8) sb.AppendLine($"   ... and {rf.IslandCount - 8} more");
        sb.AppendLine(rf.IslandsDropped > 0
            ? "  => THE CAP IS BINDING on islands close enough to matter. Raise MaxIslands."
            : "  => nothing that could shelter water was dropped.");
        // What the loop LENGTH actually costs. The island loop runs inside
        // EvaluateCascades, which the sampler calls up to eight times per query
        // inside its Newton loop, against a budget already spent -- so "just
        // raise the cap" had to be measured, not assumed.
        sb.AppendLine();
        sb.AppendLine("  sampler cost vs island count (same water, same frame):");
        float wide = Cost(rf, focus, 100000f);
        int nWide = rf.IslandCount;
        float mid = Cost(rf, focus, 1500f);
        int nMid = rf.IslandCount;
        float tight = Cost(rf, focus, 400f);
        int nTight = rf.IslandCount;
        sb.AppendLine($"    {nTight,3} islands : {tight:F3} ms / 1000 queries");
        sb.AppendLine($"    {nMid,3} islands : {mid:F3} ms / 1000 queries");
        sb.AppendLine($"    {nWide,3} islands : {wide:F3} ms / 1000 queries");
        if (tight > 0.001f)
            sb.AppendLine($"    going {nTight} -> {nWide} costs x{wide / tight:F2}");
        rf.SelectIslands(focus);   // put the real set back

        Debug.Log(sb.ToString());
        System.IO.File.WriteAllText("/tmp/seasick-islands.txt", sb.ToString());
    }

    /// Times SampleImmediate over a spread of points. Not the batched path the
    /// ship uses, but it runs the same EvaluateCascades and therefore the same
    /// island loop, so the RATIO between island counts is the honest number
    /// even if the absolute is not the shipped one.
    static float Cost(RegionField rf, Vector2 focus, float radius)
    {
        rf.SelectIslands(focus, radius);
        rf.Publish();
        const int N = 400;
        for (int i = 0; i < 40; i++)   // warm
            OceanSampler.SampleImmediate(new Vector3(focus.x + i, 0f, focus.y));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < N; i++)
        {
            float a = i * 0.137f;
            OceanSampler.SampleImmediate(new Vector3(
                focus.x + Mathf.Cos(a) * (20f + i), 0f, focus.y + Mathf.Sin(a) * (20f + i)));
        }
        sw.Stop();
        return (float)(sw.Elapsed.TotalMilliseconds * 1000.0 / N);
    }
}
