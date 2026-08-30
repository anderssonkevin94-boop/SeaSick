using System.Diagnostics;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Terrain;
using Debug = UnityEngine.Debug;

/// **How much more expensive did erosion make the hottest function?**
///
/// `TerrainPerfProbe` cannot answer this: it measures the streaming MAIN
/// THREAD, and chunk meshing runs in Burst jobs on worker threads. A noise
/// function that got slower shows up there as chunks arriving later — as
/// pop-in — not as frame time, right up until the workers saturate and then
/// it shows up as everything.
///
/// So this times `TerrainHeight.Height` itself, which is what the mesher
/// calls per vertex, at erosion 0 and at the shipped value. The ratio is the
/// number that matters and it is device-independent in the way that counts:
/// it is the same arithmetic on any CPU.
///
/// Not Burst-compiled here — this runs as plain managed code, so the
/// absolute nanoseconds are pessimistic against the real jobbed path. The
/// RATIO is what is being measured.
public class HeightBench : MonoBehaviour
{
    public static void Execute()
    {
        var pop = FindAnyObjectByType<TerrainWorldPopulator>();
        var streamer = FindAnyObjectByType<TerrainStreamer>();
        var settings = streamer != null ? streamer.settings
                     : (pop != null ? pop.terrain : null);
        if (settings == null) { Debug.LogError("HeightBench: no TerrainSettings"); return; }

        var lut = TerrainCurveLut.Bake(settings.profileCurve, Allocator.Temp);
        var sb = new StringBuilder();
        sb.AppendLine($"octaves {settings.octaves}, baseFrequency 1/{1f / settings.baseFrequency:F0}");
        sb.AppendLine("erosion   ns/sample   relative");

        float baseline = 0f;
        foreach (float e in new[] { 0f, 0.5f, 1f, 2f, 4f })
        {
            var s2 = TerrainParams.From(settings);
            s2.erosion = e;
            s2.erosionAmount = 1f;
            float ns = Time(s2, lut);
            if (e == 0f) baseline = ns;
            sb.AppendLine($"{e,6:F1}   {ns,9:F1}   {(baseline > 0f ? ns / baseline : 1f),8:F2}x");
        }
        lut.Dispose();

        // **The rows above answer the wrong question, and are kept to show
        // why.** `Noise01` now always goes through `ErodedShaped`, so every
        // one of them runs `SimplexD` and they differ only in the value of a
        // multiplier — the erosion PARAMETER is free, which was never in
        // doubt. What the change actually cost is the derivative-carrying
        // simplex replacing the plain one, so that is measured directly,
        // against the function it replaced.
        sb.AppendLine();
        sb.AppendLine("the change that was actually made, at the noise level:");
        int oct = settings.octaves;
        float f = settings.baseFrequency, lac = settings.lacunarity, g = settings.gain;
        float oldNs = TimeNoise(() => TerrainNoise.Fbm01(NextP(), 1337, oct, f, lac, g));
        float newNs = TimeNoise(() => TerrainNoise.ErodedRaw(NextP(), 1337, oct, f, lac, g, 2f));
        sb.AppendLine($"  Fbm01      {oldNs,8:F1} ns   1.00x   (what the landform used before)");
        sb.AppendLine($"  ErodedRaw  {newNs,8:F1} ns   {newNs / oldNs,5:F2}x   (what it uses now)");
        sb.AppendLine($"  the landform noise is one of ~6 fields per height sample, so the");
        sb.AppendLine($"  whole-sample cost moves by rather less than this ratio.");

        Debug.Log("HEIGHT BENCH\n" + sb);
        System.IO.File.WriteAllText("/tmp/height-bench.txt", sb.ToString());
    }

    static Unity.Mathematics.Random pRng = new Unity.Mathematics.Random(99);
    static float2 NextP() => pRng.NextFloat2(-4000f, 4000f);

    static float TimeNoise(System.Func<float> call)
    {
        const int Warm = 20000, N = 400000;
        float acc = 0f;
        pRng = new Unity.Mathematics.Random(99);
        for (int i = 0; i < Warm; i++) acc += call();
        pRng = new Unity.Mathematics.Random(99);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < N; i++) acc += call();
        sw.Stop();
        if (acc == 12345.6789f) Debug.Log("never");
        return (float)(sw.Elapsed.TotalMilliseconds * 1e6 / N);
    }

    static float Time(TerrainParams prm, NativeArray<float> lut)
    {
        const int Warm = 20000, N = 300000;
        // Sampled across a wide area so the mix of interior and open ocean is
        // realistic: erosion is faded by `interior`, so benchmarking a single
        // island's middle would report the worst case as the average.
        float acc = 0f;
        var rng = new Unity.Mathematics.Random(4242);
        for (int i = 0; i < Warm; i++)
            acc += TerrainHeight.Height(rng.NextFloat2(-4000f, 4000f), prm, lut);

        var rng2 = new Unity.Mathematics.Random(4242);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < N; i++)
            acc += TerrainHeight.Height(rng2.NextFloat2(-4000f, 4000f), prm, lut);
        sw.Stop();
        if (acc == 12345.6789f) Debug.Log("never");   // keep the loop alive
        return (float)(sw.Elapsed.TotalMilliseconds * 1e6 / N);
    }
}
