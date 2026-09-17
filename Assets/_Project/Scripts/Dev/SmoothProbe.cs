using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// **Does the DRAWN water move smoothly, frame to frame?**
///
/// Everything else in this investigation measured the SAMPLED surface, and that
/// is blind to what Kevin is describing, for two separate reasons:
///
///   1. `DisplacementReadback.PhysicsCascades` is 2. Cascade 2 -- the near-field
///      detail band, 0.5-16 m, the one the eye reads as surface texture and the
///      one BOTH of Astra's edits target -- is NOT in the readback at all.
///   2. The readback is 2-3 frames stale by design, so it cannot say what any
///      particular rendered frame looked like.
///
/// So this reads the PIXELS, with a LAND+SKY control region measured in the
/// same frames. Land and ship are driven by the camera alone, so water lumpy +
/// control steady means the ocean is stepping and the frame pacing is not --
/// which is exactly the claim being tested.
///
/// THE FIRST VERSION OF THIS PROBE WAS ITS OWN WORST ARTEFACT. It called
/// `ScreenCapture.CaptureScreenshotAsTexture()` and `GetPixels32()` every
/// frame: 3.8 million pixels through the CPU per frame, which dragged the
/// editor to 7 fps and destroyed the thing it was trying to see. An instrument
/// that perturbs the measurement this hard is not an instrument.
///
/// So the work is on the GPU now: capture into a RenderTexture, Blit it down to
/// a thumbnail (the stutter is a whole-surface effect; it does not need
/// resolution), and pull that back with AsyncGPUReadback so nothing blocks.
/// Frames are tagged with their index and differenced in pairs afterwards.
///
/// Play mode, Sea.unity. Writes /tmp/seasick-smooth.txt.
public class SmoothProbe : MonoBehaviour
{
    const int Frames = 240;
    const int W = 160, H = 90;      // thumbnail; ~14k pixels instead of 3.8M

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SmoothProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<SmoothProbe>();
        if (old != null) DestroyImmediate(old.gameObject);
        new GameObject("SmoothProbe").AddComponent<SmoothProbe>();
    }

    struct Shot { public int frame; public Color32[] px; }
    readonly Dictionary<int, Shot> shots = new Dictionary<int, Shot>();
    int pending;

    IEnumerator Start()
    {
        var full = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
        var small = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
        var ms = new List<float>();

        for (int f = 0; f < Frames; f++)
        {
            yield return new WaitForEndOfFrame();
            ms.Add(Time.unscaledDeltaTime * 1000f);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
            Graphics.Blit(full, small);       // GPU downscale, no CPU stall
            int idx = f;
            pending++;
            AsyncGPUReadback.Request(small, 0, TextureFormat.RGBA32, req =>
            {
                pending--;
                if (req.hasError) return;
                var d = req.GetData<Color32>();
                var copy = new Color32[d.Length];
                d.CopyTo(copy);
                shots[idx] = new Shot { frame = idx, px = copy };
            });
        }

        float until = Time.realtimeSinceStartup + 3f;
        while (pending > 0 && Time.realtimeSinceStartup < until) yield return null;
        AsyncGPUReadback.WaitAllRequests();
        yield return null;

        full.Release(); small.Release(); Destroy(full); Destroy(small);

        // Water: lower left, clear of the ship and the HUD. Control: upper
        // right, where the sky and any land sit. Blit flips nothing, so y=0 is
        // the bottom.
        var water = Region(0.04f, 0.42f, 0.12f, 0.46f);
        var ctrl = Region(0.55f, 0.95f, 0.66f, 0.95f);

        var wS = new List<float>(); var cS = new List<float>();
        for (int f = 1; f < Frames; f++)
        {
            if (!shots.TryGetValue(f, out var a) || !shots.TryGetValue(f - 1, out var b)) continue;
            if (a.px.Length != b.px.Length) continue;
            wS.Add(Diff(a.px, b.px, water));
            cS.Add(Diff(a.px, b.px, ctrl));
        }

        var sb = new StringBuilder();
        float mean = Mean(ms);
        sb.AppendLine($"SmoothProbe — {wS.Count} frame pairs of {Frames}, "
            + $"mean {mean:F1} ms ({1000f / Mathf.Max(0.01f, mean):F0} fps)"
            + (mean > 25f ? "   <-- NOT 60 fps, THIS RUN MEANS NOTHING" : ""));
        sb.AppendLine("Per-frame change in the PICTURE. Water vs a land+sky control.");
        sb.AppendLine();
        Report("WATER  (lower left)", wS, sb);
        Report("LAND+SKY control (upper right)", cS, sb);

        float wq = Lump(wS), cq = Lump(cS);
        sb.AppendLine("lumpiness = p90/p50 of the per-frame change. 1.0 is perfectly even;");
        sb.AppendLine("a surface that holds then catches up pushes p90 far above p50.");
        sb.AppendLine($"   water {wq:F2}    land+sky {cq:F2}");
        sb.AppendLine(mean > 25f
            ? "  => CANNOT CONCLUDE: the editor throttled this run. Click into the Game\n"
            + "     view, leave it frontmost, and run it again."
            : wq > cq * 1.4f && wq > 1.8f
                ? "  => THE WATER IS STEPPING and the rest of the picture is not."
                : wq > 1.8f && cq > 1.8f
                    ? "  => the WHOLE PICTURE is lumpy: frame pacing, not the ocean."
                    : "  => the water is as even as the control in this shot.");
        sb.AppendLine();
        sb.AppendLine("water per-frame change, first 72 pairs (x1000):");
        for (int i = 0; i < Mathf.Min(72, wS.Count); i++)
            sb.Append($"{wS[i] * 1000f,6:F1}" + ((i + 1) % 12 == 0 ? "\n" : ""));

        System.IO.File.WriteAllText("/tmp/seasick-smooth.txt", sb.ToString());
        Debug.Log("SmoothProbe: wrote /tmp/seasick-smooth.txt\n" + sb);
        Destroy(gameObject);
    }

    static List<int> Region(float x0, float x1, float y0, float y1)
    {
        var o = new List<int>();
        for (int y = Mathf.RoundToInt(y0 * H); y < Mathf.RoundToInt(y1 * H); y++)
            for (int x = Mathf.RoundToInt(x0 * W); x < Mathf.RoundToInt(x1 * W); x++)
                o.Add(y * W + x);
        return o;
    }

    static float Diff(Color32[] a, Color32[] b, List<int> idx)
    {
        float s = 0f;
        foreach (int i in idx)
        {
            if (i >= a.Length || i >= b.Length) continue;
            float la = (a[i].r * 0.299f + a[i].g * 0.587f + a[i].b * 0.114f) / 255f;
            float lb = (b[i].r * 0.299f + b[i].g * 0.587f + b[i].b * 0.114f) / 255f;
            s += Mathf.Abs(la - lb);
        }
        return idx.Count > 0 ? s / idx.Count : 0f;
    }

    static void Report(string label, List<float> v, StringBuilder sb)
    {
        if (v.Count == 0) { sb.AppendLine($"  {label}: no data"); return; }
        var s = new List<float>(v); s.Sort();
        float med = s[s.Count / 2];
        int held = 0;
        foreach (var x in v) if (x < med * 0.25f) held++;
        sb.AppendLine($"  {label}");
        sb.AppendLine($"    per-frame change  p10 {P(s, .10f) * 1000f:F2}  p50 {med * 1000f:F2}  "
            + $"p90 {P(s, .90f) * 1000f:F2}  max {P(s, 1f) * 1000f:F2}   (x1000)");
        sb.AppendLine($"    frames that barely changed (<25% of median): {held} of {v.Count} "
            + $"({100f * held / v.Count:F0}%)");
    }

    static float Lump(List<float> v)
    {
        if (v.Count == 0) return 0f;
        var s = new List<float>(v); s.Sort();
        float med = s[s.Count / 2];
        return med < 1e-7f ? 99f : P(s, .90f) / med;
    }

    static float Mean(List<float> v) { float s = 0f; foreach (var x in v) s += x; return v.Count > 0 ? s / v.Count : 0f; }
    static float P(List<float> s, float q) =>
        s.Count == 0 ? 0f : s[Mathf.Clamp(Mathf.RoundToInt(q * (s.Count - 1)), 0, s.Count - 1)];
}
