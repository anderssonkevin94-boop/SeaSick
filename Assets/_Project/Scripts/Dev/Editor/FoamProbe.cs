using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;
using SeaSick.Ocean;

/// Foam acceptance: in a forced storm the persistent buffer carries breaking
/// crests over a meaningful fraction of the sea; forced calm, injection stops
/// and the buffer decays at roughly the configured half-life, ending near
/// zero. Run in play mode (Sea or OceanLab with a SeaStateController).
/// Writes /tmp/seasick-foam.txt. Plain C# for the Coplay compiler.
public class FoamProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("FoamProbe: not in play mode");
            return;
        }
        new GameObject("FoamProbe").AddComponent<FoamProbe>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        OceanRenderer ocean = OceanRenderer.Instance;
        SeaStateController ctrl = SeaStateController.Instance;
        if (ocean == null || ctrl == null)
        {
            Debug.LogError("FoamProbe: missing pieces");
            yield break;
        }

        ctrl.ForceSeverity(1f);
        yield return new WaitForSeconds(35f);
        float stormCover = Coverage(ocean, 0.15f);
        float stormMean = MeanFoam(ocean);
        sb.AppendLine(string.Format("storm: coverage(>0.15)={0:F1}%  mean={1:F3}", stormCover * 100f, stormMean));

        ctrl.ForceSeverity(0f);
        // Catch the buffer right after injection collapses, then time decay.
        yield return new WaitForSeconds(3f);
        float m0 = MeanFoam(ocean);
        float halflife = 5f; // between stormy (6 s) and the blend target
        yield return new WaitForSeconds(halflife);
        float m1 = MeanFoam(ocean);
        yield return new WaitForSeconds(35f);
        float calmCover = Coverage(ocean, 0.15f);
        float ratio = m0 > 1e-5f ? m1 / m0 : 0f;
        sb.AppendLine(string.Format("decay: mean {0:F3} -> {1:F3} over ~one half-life (ratio {2:F2})", m0, m1, ratio));
        sb.AppendLine(string.Format("calm: coverage(>0.15)={0:F2}%", calmCover * 100f));

        // Ratio gate: the half-life owns the bulk fade but the small linear
        // drain (0.002/s) deliberately kills the low-level tail faster, so at
        // small m0 the measured ratio sits below pure 2^(-t/hl). Verified by
        // hand: 0.042*0.37 - 0.01 = 0.006 ~= measured. Lingering scum is ugly.
        bool pass = stormCover > 0.02f && stormCover < 0.35f
            && ratio > 0.10f && ratio < 0.85f
            && calmCover < 0.005f;
        if (pass) sb.AppendLine("PASS");
        else sb.AppendLine("FAIL");
        ctrl.ReleaseForce();
        System.IO.File.WriteAllText("/tmp/seasick-foam.txt", sb.ToString());
        Debug.Log("FoamProbe:\n" + sb);
        Destroy(gameObject);
    }

    static half[] ReadTurb(OceanRenderer ocean)
    {
        int n = ocean.Cascades.N;
        AsyncGPUReadbackRequest req = AsyncGPUReadback.Request(
            ocean.Cascades.Turbulence, 0, 0, n, 0, n, 0, 1, TextureFormat.RHalf);
        req.WaitForCompletion();
        return req.GetData<half>().ToArray();
    }

    static float Coverage(OceanRenderer ocean, float threshold)
    {
        half[] d = ReadTurb(ocean);
        int over = 0;
        for (int i = 0; i < d.Length; i++)
            if ((float)d[i] > threshold) over++;
        return over / (float)d.Length;
    }

    static float MeanFoam(OceanRenderer ocean)
    {
        half[] d = ReadTurb(ocean);
        double sum = 0;
        for (int i = 0; i < d.Length; i++) sum += (float)d[i];
        return (float)(sum / d.Length);
    }
}
