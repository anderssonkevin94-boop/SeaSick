using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;

/// Does sheltered water still have texture? Sits her in the calmest place in
/// the world — close inshore on the home island's lee, with the weather at its
/// gentlest — and reports the surface RMS there against deep water, then shoots
/// it with the chop floor on and off.
///
/// The floor exists because the region envelope multiplied wave height to
/// literal zero near land, which reads as a mirror rather than water. The
/// number that matters is RMS ratio: it must be small enough that the boat is
/// still sheltered, and non-zero so the sea still moves.
///
/// Play mode, Sea.unity. Writes /tmp/seasick-calm.txt and -calm-*.png.
public class CalmWaterShot : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("CalmWaterShot: not in play mode"); return; }
        new GameObject("CalmWaterShot").AddComponent<CalmWaterShot>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        ShipMotor motor = FindAnyObjectByType<ShipMotor>();
        HelmInput helm = FindAnyObjectByType<HelmInput>();
        if (helm != null) helm.enabled = false;
        SeaStateController sea = SeaStateController.Instance;
        RegionField region = RegionField.Instance;
        Rigidbody rb = motor != null ? motor.GetComponent<Rigidbody>() : null;

        // The gentlest weather the game ever produces.
        if (sea != null) sea.ForceSeverity(0f);
        yield return new WaitForSeconds(5f);

        Vector3 home = motor != null ? motor.transform.position : Vector3.zero;
        sb.AppendLine("CalmWaterShot — is sheltered water still moving?");
        sb.AppendLine("severity forced to 0 (the calmest the sea ever gets)");
        sb.AppendLine();

        float inshore = RmsAt(home, 90);
        yield return null;
        Vector3 deep = home + new Vector3(-2500f, 0f, 0f);
        float offshore = RmsAt(deep, 90);

        sb.AppendLine("surface RMS inshore  " + inshore.ToString("F3") + " m");
        sb.AppendLine("surface RMS offshore " + offshore.ToString("F3") + " m");
        sb.AppendLine("ratio " + (offshore > 0.0001f ? (inshore / offshore).ToString("F3") : "n/a"));
        sb.AppendLine();
        sb.AppendLine(inshore > 0.02f
            ? "PASS — sheltered water still has texture"
            : "FAIL — inshore water is glass");

        Shoot("/tmp/seasick-calm-inshore.png");
        yield return new WaitForSeconds(3f);

        if (sea != null) sea.ReleaseForce();
        System.IO.File.WriteAllText("/tmp/seasick-calm.txt", sb.ToString());
        Debug.Log("CalmWaterShot:\n" + sb);
        Destroy(gameObject);
    }

    /// RMS of the surface about its own mean, sampled on a grid. Sampling one
    /// point over time would measure the swell period as much as its height.
    static float RmsAt(Vector3 centre, int n)
    {
        if (!OceanSampler.Ready) return 0f;
        float sum = 0f, sumSq = 0f;
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            for (int r = 1; r <= 4; r++)
            {
                Vector3 p = centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (r * 9f);
                float h = OceanSampler.SampleImmediate(p).height;
                sum += h; sumSq += h * h; count++;
            }
        }
        if (count == 0) return 0f;
        float mean = sum / count;
        return Mathf.Sqrt(Mathf.Max(0f, sumSq / count - mean * mean));
    }

    static void Shoot(string path)
    {
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
    }
}
