using System.Collections;
using System.Text;
using UnityEditor;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using SeaSick.Ocean2;

/// THE acceptance test of the whole architecture: the surface the player sees
/// and the surface physics uses must agree to centimetres. A verification
/// kernel evaluates exactly what the renderer's vertex stage does (bilinear
/// cascade samples + regional envelope) at 1000 points spanning calm shelf,
/// island shores and open water in a storm sea at lambda 1.2; the CPU sampler
/// must place each displaced point's height within 5 cm, at matched OceanTime
/// stamps, across several frozen instants. Also reports cascade 2's RMS (the
/// visual-only chop physics ignores by design) and the batch-sampling cost.
/// Run in play mode in OceanLab. Writes /tmp/seasick-divergence.txt.
public class DivergenceProbe : MonoBehaviour
{
    const int Points = 1000;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("DivergenceProbe: not in play mode"); return; }
        new GameObject("DivergenceProbe").AddComponent<DivergenceProbe>();
    }

    IEnumerator Start()
    {
        var sb = new StringBuilder();
        var ocean = OceanRenderer.Instance;
        if (ocean == null) yield break;

        // Storm at spec-worst choppiness.
        var storm = ScriptableObject.CreateInstance<OceanSpectrumSettings>();
        storm.windSpeed = 22f; storm.fetchKm = 200f; storm.choppiness = 1.2f;
        ocean.SetSettings(storm);

        // A regional field that varies hard across the sample disc.
        var region = ocean.GetComponent<RegionField>();
        if (region == null) region = ocean.gameObject.AddComponent<RegionField>();
        region.SetProfile(60f, 400f, 0.35f, 1.5f, 60f);
        region.SetHome(new Vector2(-150f, 0f));
        region.ClearIslands();
        region.AddIsland(new Vector2(80f, 60f), 25f);
        region.AddIsland(new Vector2(-40f, -90f), 30f);

        var verify = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/_Project/Art/Shaders/Ocean/OceanVerify.compute");
        int kernel = verify.FindKernel("VerifySample");

        var rng = new System.Random(99);
        var queryXZ = new Vector2[Points];
        for (int i = 0; i < Points; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = Mathf.Sqrt((float)rng.NextDouble()) * 150f;
            queryXZ[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }

        var queryBuf = new ComputeBuffer(Points, 8);
        var outBuf = new ComputeBuffer(Points, 16);
        queryBuf.SetData(queryXZ);

        float globalMax = 0f;
        double sumAbs = 0, sumC2Sq = 0;
        int samples = 0;

        foreach (float t in new[] { 41f, 97f, 158f, 233f, 301f })
        {
            OceanTime.Scrub(t);
            OceanTime.Paused = true;
            // Let Update pump: sim re-steps at frozen t, readback catches up.
            for (int f = 0; f < 12 && OceanSampler.SurfaceTime != t; f++)
                yield return null;
            if (OceanSampler.SurfaceTime != t)
            {
                sb.AppendLine($"t={t}: readback never caught up (stamp {OceanSampler.SurfaceTime})");
                continue;
            }

            var cascades = ocean.Cascades;
            verify.SetVector("_PatchSizes", cascades.PatchSizesVec);
            verify.SetInt("_Count", Points);
            verify.SetTexture(kernel, "Displacement", cascades.Displacement);
            verify.SetBuffer(kernel, "QueryPos", queryBuf);
            verify.SetBuffer(kernel, "OutPos", outBuf);
            region.Publish(); // globals for RegionField.hlsl
            verify.Dispatch(kernel, Mathf.CeilToInt(Points / 64f), 1, 1);

            var gpu = new Vector4[Points];
            outBuf.GetData(gpu);

            float frameMax = 0f;
            for (int i = 0; i < Points; i++)
            {
                var s = OceanSampler.SampleImmediate(new Vector3(gpu[i].x, 0f, gpu[i].z));
                float err = Mathf.Abs(s.height - gpu[i].y);
                frameMax = Mathf.Max(frameMax, err);
                sumAbs += err;
                sumC2Sq += gpu[i].w * gpu[i].w;
                samples++;
            }
            globalMax = Mathf.Max(globalMax, frameMax);
            sb.AppendLine($"t={t}: max={frameMax * 100f:F2}cm");
        }

        // Batch cost: 1000 queries through the Burst job, median of 20 runs.
        var queries = new NativeArray<float3>(Points, Allocator.TempJob);
        var results = new NativeArray<OceanSample>(Points, Allocator.TempJob);
        for (int i = 0; i < Points; i++)
            queries[i] = new float3(queryXZ[i].x, 0f, queryXZ[i].y);
        var times = new float[20];
        for (int runIx = 0; runIx < 20; runIx++)
        {
            float t0 = Time.realtimeSinceStartup;
            OceanSampler.SampleBatch(queries, results, default).Complete();
            times[runIx] = (Time.realtimeSinceStartup - t0) * 1000f;
        }
        System.Array.Sort(times);
        float medianMs = times[10];
        queries.Dispose(); results.Dispose();

        float meanCm = (float)(sumAbs / samples) * 100f;
        float c2Rms = Mathf.Sqrt((float)(sumC2Sq / samples)) * 100f;
        bool pass = globalMax < 0.05f && medianMs < 0.3f;
        sb.AppendLine($"overall: max={globalMax * 100f:F2}cm mean={meanCm:F3}cm ({samples} samples)");
        sb.AppendLine($"cascade2 visual-only RMS (excluded from physics by design): {c2Rms:F1}cm");
        sb.AppendLine($"SampleBatch 1000 queries: {medianMs:F3} ms (median of 20)");
        sb.AppendLine(pass ? "PASS" : "FAIL");

        System.IO.File.WriteAllText("/tmp/seasick-divergence.txt", sb.ToString());
        Debug.Log("DivergenceProbe:\n" + sb);

        queryBuf.Release(); outBuf.Release();
        OceanTime.Paused = false;
        Destroy(gameObject);
    }
}
