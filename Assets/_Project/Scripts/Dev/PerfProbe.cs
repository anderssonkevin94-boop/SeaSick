using System.Collections;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;

/// Perf accounting for the ocean, honest about what an unfocused editor can
/// measure: CPU dispatch overhead per frame (Stopwatch around StepSimulation),
/// physics-batch cost per step, and the exact VRAM ledger. GPU milliseconds
/// on device are a user-side task (editor GPU timing is not the phone's).
/// Run in play mode. Writes /tmp/seasick-perf.txt. Plain C# for Coplay.
public class PerfProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying)
        {
            UnityEngine.Debug.LogError("PerfProbe: not in play mode");
            return;
        }
        new GameObject("PerfProbe").AddComponent<PerfProbe>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        yield return new WaitForSeconds(2f);
        OceanRenderer ocean = OceanRenderer.Instance;
        if (ocean == null)
        {
            UnityEngine.Debug.LogError("PerfProbe: no OceanRenderer.Instance");
            yield break;
        }

        // CPU cost of issuing the whole per-frame sim (all dispatches).
        var sw = new Stopwatch();
        float[] times = new float[60];
        for (int i = 0; i < 60; i++)
        {
            yield return null;
            sw.Restart();
            ocean.StepSimulation();
            sw.Stop();
            times[i] = (float)sw.Elapsed.TotalMilliseconds;
        }
        System.Array.Sort(times);
        sb.AppendLine(string.Format(
            "sim dispatch CPU: median {0:F3} ms, p90 {1:F3} ms (issue cost only; GPU time is a device measurement)",
            times[30], times[54]));

        // VRAM ledger.
        int n = ocean.Cascades.N;
        long texels = (long)n * n * 3;
        long floats16 = 8; // RGBAHalf bytes
        long simRT = 0;
        var sim = DynamicWaterSim.Instance;
        if (sim != null && sim.SimTexture != null)
            simRT = (long)sim.Resolution * sim.Resolution * 4 * 3; // RGHalf x3
        long spectral = texels * 16 * 5;      // H0, WaveData, Spec0, Spec1, Scratch (float4)
        long outputs = texels * floats16 * 2  // Displacement, Derivatives
                     + texels * 2 * 2;        // Turbulence x2 (RHalf)
        long readback = 3L * (2 * n * n * 8 * 2 + n * n * 2); // slots: 2 casc x (disp+deriv) + turb
        double mb = (spectral + outputs + simRT) / 1048576.0;
        double rbMb = readback / 1048576.0;
        sb.AppendLine(string.Format(
            "VRAM: cascades+sim {0:F1} MB (N={1}); CPU readback buffers {2:F1} MB",
            mb, n, rbMb));

        bool pass = times[30] < 1.0 && mb < 48.0;
        if (pass) sb.AppendLine("PASS (editor-measurable budget)");
        else sb.AppendLine("FAIL");
        System.IO.File.WriteAllText("/tmp/seasick-perf.txt", sb.ToString());
        UnityEngine.Debug.Log("PerfProbe:\n" + sb);
        Destroy(gameObject);
    }
}
