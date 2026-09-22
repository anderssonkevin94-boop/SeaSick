using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// **Is 60 fps out of reach because of this machine, or because of what the
/// game asks of it?**
///
/// One frame time cannot separate those. Two costs are mixed in it: a FIXED
/// part that does not care how many pixels there are (culling, draw
/// submission, physics, the CPU) and a PER-PIXEL part that scales with the
/// render target. Sweeping the render scale and fitting a line through the
/// results splits them, and the split is the answer: a large fixed part means
/// the build is doing too much work per frame, a large per-pixel slope at a
/// sane resolution means the GPU is genuinely the limit.
///
/// Every leg is a reversible property write. Two traps, both paid for once:
/// an unfocused editor throttles play to ~10 fps unless `runInBackground` is
/// set, and a run started before the islands finish streaming reads the
/// loader rather than the frame -- hence the settle, and the repeated
/// baseline whose disagreement invalidates the run.
public class ShadowCostProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("ShadowCostProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<ShadowCostProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("ShadowCostProbe").AddComponent<ShadowCostProbe>();
    }

    const string Out = "/tmp/seasick-shadow.txt";
    const int Frames = 70;
    const float SettleSeconds = 25f;

    static FieldInfo F(string n) =>
        typeof(UniversalRenderPipelineAsset).GetField(n, BindingFlags.Instance | BindingFlags.NonPublic);
    static void Set(UniversalRenderPipelineAsset a, string f, object v) { F(f)?.SetValue(a, v); }
    static object Get(UniversalRenderPipelineAsset a, string f) => F(f)?.GetValue(a);

    static string P(List<double> v, float p)
    {
        if (v.Count == 0) return "-";
        var s = new List<double>(v); s.Sort();
        return s[Mathf.Clamp(Mathf.RoundToInt((s.Count - 1) * p), 0, s.Count - 1)].ToString("F1");
    }
    static double Med(List<double> v) { var s = new List<double>(v); if (s.Count == 0) return 0; s.Sort(); return s[s.Count / 2]; }

    IEnumerator Start()
    {
        bool oBg = Application.runInBackground;
        int oTarget = Application.targetFrameRate, oVsync = QualitySettings.vSyncCount;
        Application.runInBackground = true;
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;

        var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (asset == null) { Debug.LogError("ShadowCostProbe: no URP asset"); yield break; }
        object oScale = Get(asset, "m_RenderScale");

        Material ocean = null;
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_PaintedStrength"))
            { ocean = r.sharedMaterial; break; }
        float oPainted = ocean != null ? ocean.GetFloat("_PaintedStrength") : 0f;

        // Let the islands finish streaming; a run started into the loader
        // reads 100 ms of asset work and calls it the frame.
        float t = 0f;
        while (t < SettleSeconds) { t += Time.unscaledDeltaTime; yield return null; }

        var sb = new StringBuilder();
        sb.AppendLine("ShadowCostProbe v5 -- render-scale sweep: fixed cost vs per-pixel cost");
        sb.AppendFormat("GPU: {0}  ({1} MB reported)\n", SystemInfo.graphicsDeviceName, SystemInfo.graphicsMemorySize);
        sb.AppendFormat("CPU: {0} x {1}   system memory {2} MB\n",
            SystemInfo.processorCount, SystemInfo.processorType, SystemInfo.systemMemorySize);
        sb.AppendFormat("Game view: {0}x{1} = {2:F2} Mpixel at scale 1.0\n\n",
            Screen.width, Screen.height, Screen.width * Screen.height / 1e6f);
        sb.AppendLine("leg                                Mpixel    frame ms p50/p95     fps");

        var rows = new List<(float px, float painted, double ms)>();

        IEnumerator Leg(string name, float scale, float painted)
        {
            Set(asset, "m_RenderScale", scale);
            if (ocean != null) ocean.SetFloat("_PaintedStrength", painted);
            for (int i = 0; i < 25; i++) yield return null;

            var frame = new List<double>();
            for (int i = 0; i < Frames; i++)
            {
                yield return new WaitForEndOfFrame();
                frame.Add(Time.unscaledDeltaTime * 1000.0);
            }
            double med = Med(frame);
            float mp = Screen.width * Screen.height * scale * scale / 1e6f;
            rows.Add((mp, painted, med));
            sb.AppendFormat("{0,-32} {1,7:F2}  {2,8} /{3,7}   {4,5}\n",
                name, mp, P(frame, .5f), P(frame, .95f), med > 0 ? (1000.0 / med).ToString("F0") : "-");
        }

        foreach (var s in new[] { 1.0f, 0.75f, 0.5f, 0.35f })
        {
            yield return Leg($"scale {s:F2}  painted ON", s, oPainted);
            yield return Leg($"scale {s:F2}  painted OFF", s, 0f);
        }
        yield return Leg("scale 1.00  painted ON (drift)", 1.0f, oPainted);

        Set(asset, "m_RenderScale", oScale);
        if (ocean != null) ocean.SetFloat("_PaintedStrength", oPainted);
        Application.runInBackground = oBg;
        Application.targetFrameRate = oTarget;
        QualitySettings.vSyncCount = oVsync;

        // Least-squares ms = fixed + slope * Mpixel, fitted separately for the
        // painted ocean on and off. `fixed` is what no resolution change can
        // remove; `slope` is what this GPU costs per megapixel.
        sb.AppendLine();
        foreach (var on in new[] { true, false })
        {
            var set = rows.FindAll(r => (r.painted > 0.001f) == on);
            if (set.Count < 2) continue;
            double sx = 0, sy = 0, sxx = 0, sxy = 0; int n = set.Count;
            foreach (var r in set) { sx += r.px; sy += r.ms; sxx += r.px * r.px; sxy += r.px * r.ms; }
            double slope = (n * sxy - sx * sy) / (n * sxx - sx * sx);
            double fixedMs = (sy - slope * sx) / n;
            double budget = (16.667 - fixedMs) / slope;
            sb.AppendFormat("painted {0,-3}: fixed {1,5:F1} ms  +  {2,5:F1} ms per Mpixel   -> 60 fps needs {3:F2} Mpixel ({4})\n",
                on ? "ON" : "OFF", fixedMs, slope, budget,
                budget <= 0 ? "unreachable: the fixed cost alone exceeds 16.7 ms"
                            : $"about {Mathf.RoundToInt(Mathf.Sqrt((float)budget * 1e6f * 16f / 9f))}x{Mathf.RoundToInt(Mathf.Sqrt((float)budget * 1e6f * 9f / 16f))}");
        }
        sb.AppendLine("\nall settings restored.");
        System.IO.File.WriteAllText(Out, sb.ToString());
        Debug.Log("ShadowCostProbe: wrote " + Out);
        Destroy(gameObject);
    }
}
