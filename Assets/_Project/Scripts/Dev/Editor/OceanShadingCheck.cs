using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;

/// Why does a 62 m storm render as a turquoise lagoon?
///
/// Three things could each produce that picture on their own, and a screenshot
/// cannot tell them apart: the storm colours never reach the shader, the storm
/// colours reach it but the deep/shallow blend is saturated, or the surface
/// genuinely has no fine structure to shade. This reads all three off the
/// running game rather than off the source, because a .mat snapshots a shader
/// property's default when the property is created and never sees a later
/// change to it — the _StormDeep colour was wrong at runtime for a whole
/// session for exactly that reason.
///
/// Play mode, Sea.unity. Writes Temp/ocean-shading.txt.
public class OceanShadingCheck : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("OceanShadingCheck: not in play mode"); return; }
        new GameObject("OceanShadingCheck").AddComponent<OceanShadingCheck>();
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(6f);

        var sb = new StringBuilder();
        var motor = FindAnyObjectByType<ShipMotor>();
        var sea = SeaStateController.Instance;
        Vector3 shipPos = motor != null ? motor.transform.position : Vector3.zero;

        sb.AppendLine("OceanShadingCheck — read off the running game");
        sb.AppendLine("ship at " + shipPos.ToString("F0"));
        sb.AppendLine("severity " + (sea != null ? sea.Severity01.ToString("F2") : "-")
            + "   Storminess01 " + (sea != null ? sea.Storminess01.ToString("F2") : "-"));
        sb.AppendLine();

        // --- 1. does the storm actually arrive at the shader? -------------
        float ssStorm = Shader.GetGlobalFloat("_SS_Storminess");
        sb.AppendLine("GLOBAL _SS_Storminess = " + ssStorm.ToString("F3")
            + (ssStorm > 0.95f ? "   (full storm)" : "   <-- NOT full storm"));

        var renderer = FindAnyObjectByType<OceanRenderer>();
        Material mat = null;
        var mrs = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        foreach (var mr in mrs)
        {
            if (mr.sharedMaterial != null && mr.sharedMaterial.shader != null
                && mr.sharedMaterial.shader.name.Contains("Ocean"))
            { mat = mr.sharedMaterial; break; }
        }

        if (mat == null) sb.AppendLine("no ocean material found on any MeshRenderer");
        else
        {
            sb.AppendLine("ocean material: " + mat.name + "  shader " + mat.shader.name);
            string[] cols = { "_DeepColor", "_ShallowColor", "_SubsurfaceColor",
                              "_StormDeep", "_StormShallow", "_StormSubsurface", "_FoamColor" };
            foreach (var c in cols)
                if (mat.HasProperty(c))
                    sb.AppendLine("  " + c.PadRight(18) + mat.GetColor(c).ToString("F3"));
            string[] floats = { "_SubsurfaceStrength", "_PeakMaskScale", "_FoamJThreshold",
                                "_FoamNoiseScale", "_SpecStrength" };
            foreach (var f in floats)
                if (mat.HasProperty(f))
                    sb.AppendLine("  " + f.PadRight(18) + mat.GetFloat(f).ToString("F3"));
        }
        sb.AppendLine();

        // --- 2. is the deep/shallow blend saturated? ----------------------
        // The shader does heightLift = saturate(heightY * 0.18 + 0.35), which
        // reaches 1.0 at 3.6 m above mean and 0.0 at -1.9 m. Those numbers were
        // chosen for a sea a few metres tall. Measure the sea we HAVE.
        const float Gain = 0.18f, Bias = 0.35f;
        int n = 0, atTop = 0, atBottom = 0;
        float minH = float.MaxValue, maxH = float.MinValue, sum = 0f, sumSq = 0f;
        if (OceanSampler.Ready)
        {
            for (int i = 0; i < 40; i++)
                for (int j = 0; j < 40; j++)
                {
                    var p = new Vector3(shipPos.x + (i - 20) * 40f, 0f, shipPos.z + (j - 20) * 40f);
                    float h = OceanSampler.SampleImmediate(p).height;
                    float lift = Mathf.Clamp01(h * Gain + Bias);
                    if (lift >= 0.999f) atTop++;
                    if (lift <= 0.001f) atBottom++;
                    minH = Mathf.Min(minH, h); maxH = Mathf.Max(maxH, h);
                    sum += h; sumSq += h * h; n++;
                }
        }

        if (n > 0)
        {
            float mean = sum / n;
            float rms = Mathf.Sqrt(Mathf.Max(0f, sumSq / n - mean * mean));
            sb.AppendLine($"surface over a 1600 m square, {n} samples:");
            sb.AppendLine($"  height min {minH:F1} m, max {maxH:F1} m, RMS {rms:F1} m, Hs(4xRMS) {4f * rms:F1} m");
            sb.AppendLine($"  heightLift = saturate(h * {Gain} + {Bias})  ->  saturates high above "
                + $"{(1f - Bias) / Gain:F1} m and low below {-Bias / Gain:F1} m");
            sb.AppendLine($"  PEGGED AT SHALLOW COLOUR: {100f * atTop / n:F0}% of samples");
            sb.AppendLine($"  pegged at deep colour:    {100f * atBottom / n:F0}% of samples");
            sb.AppendLine($"  -> only {100f * (n - atTop - atBottom) / n:F0}% of the sea is inside the gradient at all");
        }
        else sb.AppendLine("OceanSampler not ready — no surface stats");

        System.IO.File.WriteAllText("Temp/ocean-shading.txt", sb.ToString());
        Debug.Log("OceanShadingCheck: written to Temp/ocean-shading.txt\n" + sb);
        Destroy(gameObject);
    }
}
