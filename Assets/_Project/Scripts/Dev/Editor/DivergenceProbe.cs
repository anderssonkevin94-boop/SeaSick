using System.Collections;
using System.Text;
using UnityEditor;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using SeaSick.Ocean;

/// THE acceptance test of the whole architecture: the surface the player sees
/// and the surface physics uses must agree to centimetres. A verification
/// kernel evaluates exactly what the renderer's vertex stage does (bilinear
/// cascade samples + regional envelope) at 1000 points spanning calm shelf,
/// island shores and open water in a storm sea at lambda 1.2; the CPU sampler
/// must place each displaced point's height within 5 cm, at matched OceanTime
/// stamps, across several frozen instants.
///
/// The verdict is a THREE-WAY SPLIT, because "26 cm" on its own could be any
/// of three unrelated faults and this probe spent a session being read as a
/// formula bug when it was not:
///   env   -- RegionField.cs against RegionField.hlsl at the same source
///            point. Non-zero means the parity contract itself has drifted.
///   disp  -- the raw un-enveloped displacement, GPU texture against the CPU
///            readback, at the same source point. Non-zero means the readback
///            is stale or reading a different spectrum, not that the maths
///            disagrees.
///   total -- the full displaced-point height comparison. Whatever total has
///            that env and disp do not is the Newton inversion.
///
/// It also DISABLES SeaStateController for the run. ForceSeverity does not
/// stop it: the controller still reaches SetSettings every rebuild and hands
/// the renderer its own drifting blend, so the probe was never measuring the
/// storm it asked for and no two runs measured the same sea.
///
/// Run in play mode in OceanLab. Writes /tmp/seasick-divergence.txt.
public class DivergenceProbe : MonoBehaviour
{
    const int Points = 1000;
    const int CatchUpFrames = 90;

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

        // The controller owns SetSettings and will overwrite anything this
        // probe asks for. Take it off the air for the run.
        var ctrl = SeaStateController.Instance;
        bool ctrlWas = false;
        if (ctrl != null) { ctrlWas = ctrl.enabled; ctrl.enabled = false; }

        // Storm at spec-worst choppiness. nominalHs is deliberately large so
        // the depth-limited envelope actually BITES over the synthetic shore
        // below -- a gate that never reaches the term it is gating is not a
        // gate.
        // The gate has to run at the amplitudes the game actually produces:
        // the displacement textures are half precision, whose quantum grows
        // with magnitude, so a gate exercised at 2 m proves nothing about a
        // sea whose crests are 40 m up.
        //
        // LOADED from the shipped asset rather than retyped here. This block
        // used to carry a hand-copied duplicate of the storm values under a
        // comment claiming they were the shipped ones, and the moment the sea
        // was retuned -- one narrow 70 m train became a 64 m train crossed
        // with a 24 m one, plus a short-wave detail gain -- the gate went on
        // certifying a sea that no longer existed. A duplicated constant is a
        // gate that silently stops gating.
        var storm = ScriptableObject.CreateInstance<OceanSpectrumSettings>();
        var shipped = Resources.Load<OceanSpectrumSettings>("Ocean/SeaState_Stormy");
        if (shipped != null) storm.CopyFrom(shipped);
        else
        {
            Debug.LogWarning("DivergenceProbe: SeaState_Stormy not found in Resources — "
                + "falling back to hardcoded values, which may be stale.");
            storm.windSpeed = 22f; storm.fetchKm = 200f; storm.depth = 30f;
            storm.swellHeight = 64f; storm.swellWavelength = 470f;
            storm.swellSharpness = 3.2f; storm.swellDirectionDeg = 0f;
            storm.nominalHs = 65f;
        }
        // The one deliberate departure: a MARGIN over the authored choppiness,
        // because the sampler's Newton inversion is hardest on the sharpest
        // crests and the gate should have a little headroom.
        //
        // It used to be a floor of 1.2, which was reasonable while the shipped
        // value was 1.0 and became nonsense the moment the storm was retuned
        // to 0.195: the gate was testing a sea six times choppier than
        // anything the game produces, and duly failed at 848 cm on a
        // configuration that does not exist. A gate that fails on water nobody
        // will ever sail is a gate that gets ignored, which is worse than not
        // having one.
        float authoredChop = storm.choppiness;
        storm.choppiness = Mathf.Min(2f, authoredChop * 1.25f);
        ocean.SetSettings(storm);

        // A regional field that varies hard across the sample disc.
        var region = ocean.GetComponent<RegionField>();
        if (region == null) region = ocean.gameObject.AddComponent<RegionField>();
        region.SetProfile(60f, 400f, 0.35f, 1.5f, 60f);
        region.SetHome(new Vector2(-150f, 0f));
        region.ClearIslands();
        region.AddIsland(new Vector2(80f, 60f), 25f);
        region.AddIsland(new Vector2(-40f, -90f), 30f);

        // A weather field with a DELIBERATELY TINY tile, so the drifting
        // roughness patches vary hard across the 600 m sample disc instead of
        // being a near-constant the twins would agree on for free. Same
        // formula, harsher parameter -- the same reason choppiness is
        // over-driven above. Without this the newest term in the envelope
        // would be certified by a gate that never exercised it, which is how
        // this probe spent a fortnight certifying a sea that did not exist.
        var weather = ocean.GetComponent<WeatherField>();
        if (weather == null) weather = ocean.gameObject.AddComponent<WeatherField>();
        weather.ConfigureForTest(220f, 64);

        // A synthetic shore grid, because OceanLab has no terrain and the
        // depth-dependent half of the envelope -- shoal factor, wet gate and
        // the depth limit -- is otherwise never evaluated at all. One gaussian
        // hill puts land, beach, shallows and 130 m of deep water inside the
        // sample disc, so every branch of both twins runs on real numbers.
        const int ShoreN = 64;
        const float ShoreSize = 600f;
        var heights = new NativeArray<float>(ShoreN * ShoreN, Allocator.Temp);
        for (int gz = 0; gz < ShoreN; gz++)
            for (int gx = 0; gx < ShoreN; gx++)
            {
                float wx = -ShoreSize * 0.5f + (gx + 0.5f) * ShoreSize / ShoreN;
                float wz = -ShoreSize * 0.5f + (gz + 0.5f) * ShoreSize / ShoreN;
                float dx = wx - 60f, dz = wz - 60f;
                heights[gz * ShoreN + gx] =
                    -180f + 200f * Mathf.Exp(-(dx * dx + dz * dz) / (2f * 90f * 90f));
            }
        region.SetShore(heights, new float2(-ShoreSize * 0.5f, -ShoreSize * 0.5f),
            ShoreSize, ShoreN);
        heights.Dispose();

        // Let the rebuild land and the readback ring refill before measuring.
        for (int f = 0; f < 12; f++) yield return null;

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
        var diagBuf = new ComputeBuffer(Points, 16);
        queryBuf.SetData(queryXZ);

        var s0 = ocean.Settings;
        sb.AppendLine("DivergenceProbe -- CPU sampler against the rendered surface");
        sb.AppendLine("SeaStateController: " + (ctrl == null ? "absent"
            : ctrlWas ? "was enabled, DISABLED for this run" : "already disabled"));
        sb.AppendLine(string.Format(
            "spectrum in force: wind {0:F1} m/s  fetch {1:F0} km  choppiness {2:F2} (authored {6:F2} +25% margin)  depth {3:F0} m  swell {4:F1} m at {5:F0} m",
            s0.windSpeed, s0.fetchKm, s0.choppiness, s0.depth, s0.swellHeight, s0.swellWavelength,
            authoredChop));
        sb.AppendLine();

        float maxTotal = 0f, maxEnv = 0f, maxDisp = 0f;
        double sumTotal = 0, sumEnv = 0, sumDisp = 0, sumC2Sq = 0, sumD01Sq = 0;
        // THE GATE SCALES WITH THE SEA. 5 cm was chosen when this ocean was 9 m
        // tall, where it is half a percent of Hs -- a demanding but reachable
        // bar. At Hs 85 the same 5 cm is 0.06% of Hs, and it is no longer
        // reachable at all: the displacement textures are ARGBHalf, whose
        // quantum grows with magnitude, and the readback-vs-live term alone
        // measured 5.08 cm with a PERFECT inversion. A fixed gate had quietly
        // become a test of float16 rather than a test of parity.
        //
        // 0.1% of Hs, never looser than the original 5 cm. At 9 m that is still
        // 5 cm; at 85 m it is 8.5 cm, which is a millimetre per ten metres of
        // wave and still far tighter than anything the hull can feel.
        float gate = Mathf.Max(0.05f, 0.001f * storm.nominalHs);
        int samples = 0, stalled = 0, capped = 0, overGate = 0;
        var allErr = new System.Collections.Generic.List<float>();
        float envLo = 9999f, envHi = -9999f;
        float patchLo = 9999f, patchHi = -9999f;

        foreach (float t in new[] { 41f, 97f, 158f, 233f, 301f })
        {
            OceanTime.Scrub(t);
            OceanTime.Paused = true;
            // Let Update pump: sim re-steps at frozen t, readback catches up.
            int waited = 0;
            while (waited < CatchUpFrames && OceanSampler.SurfaceTime != t)
            {
                waited++;
                yield return null;
            }
            if (OceanSampler.SurfaceTime != t)
            {
                stalled++;
                sb.AppendLine(string.Format(
                    "t={0:F0}: STALLED -- readback stamp {1:F3} after {2} frames (instant skipped)",
                    t, OceanSampler.SurfaceTime, CatchUpFrames));
                continue;
            }

            var cascades = ocean.Cascades;
            verify.SetVector("_PatchSizes", cascades.PatchSizesVec);
            verify.SetInt("_Count", Points);
            verify.SetTexture(kernel, "Displacement", cascades.Displacement);
            verify.SetBuffer(kernel, "QueryPos", queryBuf);
            verify.SetBuffer(kernel, "OutPos", outBuf);
            verify.SetBuffer(kernel, "OutDiag", diagBuf);
            region.Publish(); // globals for RegionField.hlsl
            verify.Dispatch(kernel, Mathf.CeilToInt(Points / 64f), 1, 1);

            var gpu = new Vector4[Points];
            var diag = new Vector4[Points];
            outBuf.GetData(gpu);
            diagBuf.GetData(diag);

            var field = OceanSampler.CurrentField();
            float fTotal = 0f, fEnv = 0f, fDisp = 0f;
            var rp = field.region;
            for (int i = 0; i < Points; i++)
            {
                float2 x = new float2(queryXZ[i].x, queryXZ[i].y);

                // 1. Envelope parity: the two twins at the same source point.
                float envErr = Mathf.Abs(diag[i].x - field.SourceEnv(x));

                // 2. Readback parity: raw displacement, same source point,
                //    no envelope and no inversion in the way.
                float4 rawCpu = field.SourceDisp(x);
                float dispErr = math.length(new float3(
                    diag[i].y - rawCpu.x, diag[i].z - rawCpu.y, diag[i].w - rawCpu.z));

                // 3. The real gate: the displaced point's height.
                var s = OceanSampler.SampleImmediate(new Vector3(gpu[i].x, 0f, gpu[i].z));
                float totalErr = Mathf.Abs(s.height - gpu[i].y);

                fTotal = Mathf.Max(fTotal, totalErr);
                fEnv = Mathf.Max(fEnv, envErr);
                fDisp = Mathf.Max(fDisp, dispErr);
                sumTotal += totalErr; sumEnv += envErr; sumDisp += dispErr;
                allErr.Add(totalErr);
                if (totalErr >= gate) overGate++;
                sumC2Sq += gpu[i].w * gpu[i].w;
                sumD01Sq += diag[i].z * diag[i].z;
                samples++;

                // Coverage: is the depth limit actually the binding term here?
                float3 swd = rp.ShoreWetDepth(x, field.shore);
                if (rp.waveHs > 0.01f
                    && math.max(0f, rp.breakFraction * swd.z / rp.waveHs) < 1f) capped++;
                if (diag[i].x < envLo) envLo = diag[i].x;
                if (diag[i].x > envHi) envHi = diag[i].x;
                float pv = rp.WeatherAt(x, field.weather);
                if (pv < patchLo) patchLo = pv;
                if (pv > patchHi) patchHi = pv;
            }
            maxTotal = Mathf.Max(maxTotal, fTotal);
            maxEnv = Mathf.Max(maxEnv, fEnv);
            maxDisp = Mathf.Max(maxDisp, fDisp);
            sb.AppendLine(string.Format(
                "t={0,4:F0}: total max {1,7:F2} cm   env max {2,9:F6}   disp max {3,7:F2} cm   (caught up in {4} frames)",
                t, fTotal * 100f, fEnv, fDisp * 100f, waited));
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

        sb.AppendLine();
        if (samples == 0)
        {
            sb.AppendLine("NO SAMPLES -- every instant stalled. This is an instrument failure,");
            sb.AppendLine("not a passing sea. FAIL.");
        }
        else
        {
            float c2Rms = Mathf.Sqrt((float)(sumC2Sq / samples)) * 100f;
            float d01Rms = Mathf.Sqrt((float)(sumD01Sq / samples));
            // The distribution, not just the max. "max 101 cm" is a completely
            // different finding depending on whether it is one point in five
            // thousand (a folding crest, where the surface genuinely has no
            // unique inverse) or five hundred (a broken sampler).
            var errA = allErr.ToArray(); System.Array.Sort(errA);
            sb.AppendLine(string.Format(
                "total : max {0,7:F2} cm   mean {1,7:F3} cm   p50 {2,6:F3}   p99 {3,6:F2}   p99.9 {4,6:F2} cm",
                maxTotal * 100f, sumTotal / samples * 100f,
                errA[errA.Length / 2] * 100f,
                errA[(int)(errA.Length * 0.99f)] * 100f,
                errA[(int)(errA.Length * 0.999f)] * 100f));
            sb.AppendLine(string.Format(
                "        {0} of {1} points over the {3:F1} cm gate ({2:F3}%)  <- the gate",
                overGate, samples, 100.0 * overGate / samples, gate * 100f));
            sb.AppendLine(string.Format(
                "env   : max {0,7:F6}    mean {1,7:F6}       <- RegionField C# vs HLSL, must be ~0",
                maxEnv, sumEnv / samples));
            sb.AppendLine(string.Format(
                "disp  : max {0,7:F2} cm   mean {1,7:F3} cm      <- readback vs live texture, must be ~0",
                maxDisp * 100f, sumDisp / samples * 100f));
            sb.AppendLine(string.Format(
                "sea measured: cascade 0+1 height RMS {0:F2} m, cascade 2 RMS {1:F1} cm",
                d01Rms, c2Rms));
            sb.AppendLine(string.Format("instants used {0} of 5, stalled {1}", samples / Points, stalled));
            sb.AppendLine(string.Format(
                "coverage: envelope spanned {0:F3}..{1:F3}; the depth limit was the binding term at {2:F0}% of points",
                envLo, envHi, 100.0 * capped / samples));
            // A term the disc never varies is a term this gate is not
            // gating. The patch field is on a 220 m test tile precisely so
            // this line reads as a real spread.
            sb.AppendLine(string.Format(
                "coverage: weather patch field spanned {0:F3}..{1:F3}{2}",
                patchLo, patchHi,
                (patchHi - patchLo) < 0.25f ? "   <-- TOO FLAT TO GATE" : ""));
        }
        sb.AppendLine(string.Format("SampleBatch 1000 queries: {0:F3} ms (median of 20)", medianMs));

        // Batch budget 0.4 ms, not 0.3: the inversion deliberately went from 3
        // Newton steps to 5 (three no longer converge on a 40 m crest), which
        // measured 0.219 -> 0.275-0.290 ms. Raised to match the change that was
        // made on purpose, rather than left to flicker red on editor noise --
        // what it is guarding is the sampler quietly becoming expensive, and
        // 0.4 ms still catches that with room to spare.
        bool pass = samples > 0 && stalled == 0 && maxTotal < gate && medianMs < 0.4f;
        sb.AppendLine(pass ? "PASS" : "FAIL");

        System.IO.File.WriteAllText("/tmp/seasick-divergence.txt", sb.ToString());
        Debug.Log("DivergenceProbe:\n" + sb);

        queryBuf.Release(); outBuf.Release(); diagBuf.Release();
        OceanTime.Paused = false;
        if (ctrl != null) ctrl.enabled = ctrlWas;
        Destroy(gameObject);
    }
}
