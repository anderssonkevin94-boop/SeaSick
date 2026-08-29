using System.Collections;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.Terrain;

/// Is the water near the islands actually alive, or just small?
///
/// This is the probe for the wavelength-dependent shoaling change, and it
/// measures the right thing on purpose. "Too calm around the islands" is not a
/// complaint about AMPLITUDE — the old envelope scaled every cascade by one
/// factor, so inshore water was a 300 m swell shrunk to a couple of metres,
/// amp/L about 0.005. That is a flat sheet, and it is the same failure the
/// project already shipped once: a wave looks flat when amp/L is small, not
/// when amplitude is small.
///
/// So the number that matters here is FACE ANGLE at depth, not height at
/// depth. A shoreline should kill the swell and leave the chop behind, which
/// means slope should hold up as the bottom comes up even while height falls.
///
/// Walks a depth ladder from the deep ocean into the shallows, finding real
/// water for each rung rather than assuming a coordinate has the depth it had
/// last week. Play mode, Sea.unity. Writes /tmp/seasick-shorelife.txt.
public class ShoreLifeProbe : MonoBehaviour
{
    // Depths to look for, metres.
    static readonly float[] WantDepths = { 180f, 60f, 25f, 12f, 6f };
    const int Samples = 900;         // per rung, along a transect
    const float Span = 600f;         // transect length, m

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("ShoreLifeProbe: not in play mode"); return; }
        new GameObject("ShoreLifeProbe").AddComponent<ShoreLifeProbe>();
    }

    IEnumerator Start()
    {
        var ctrl = SeaStateController.Instance;
        if (ctrl != null) ctrl.ForceSeverity(1f);   // never disable it: that nulls Instance
        var motor = FindAnyObjectByType<ShipMotor>();
        yield return new WaitForSeconds(7f);

        var streamer = FindFirstObjectByType<TerrainStreamer>(FindObjectsInactive.Include);
        var settings = streamer != null ? streamer.settings : null;
        if (settings == null) { Debug.LogError("ShoreLifeProbe: no TerrainSettings"); yield break; }

        var prm = TerrainParams.From(settings);
        // PERSISTENT, not Temp. A Temp allocation is valid for one frame, and
        // this coroutine yields between rungs -- the first version duly threw
        // ObjectDisposedException on rung two. Disposed by hand below.
        var lut = TerrainCurveLut.Bake(settings.profileCurve, Allocator.Persistent);

        var sb = new StringBuilder();
        sb.AppendLine("ShoreLifeProbe — does the sea keep its TEXTURE as the bottom comes up");
        sb.AppendLine("severity forced 1.00; " + (motor != null ? "ship at " + motor.transform.position.ToString("F0") : ""));
        sb.AppendLine();
        sb.AppendLine("  depth_m   found_at            RMS_m   face_med  face_p90   amp/L    envelope");

        Vector3 origin = motor != null ? motor.transform.position : Vector3.zero;

        foreach (float want in WantDepths)
        {
            if (!FindDepth(origin, want, prm, lut, out float2 spot, out float actual))
            {
                sb.AppendLine($"  {want,7:F0}   -- no water near {want:F0} m found within 6 km --");
                continue;
            }

            // Transect along +X. Step is fine enough to resolve the short chop
            // the whole change is about: 600 m / 900 = 0.67 m.
            float step = Span / Samples;
            var h = new float[Samples];
            for (int i = 0; i < Samples; i++)
            {
                var p = new Vector3(spot.x + (i - Samples * 0.5f) * step, 0f, spot.y);
                h[i] = OceanSampler.Ready ? OceanSampler.SampleImmediate(p).height : 0f;
            }

            float mean = 0f;
            for (int i = 0; i < Samples; i++) mean += h[i];
            mean /= Samples;
            float var2 = 0f;
            for (int i = 0; i < Samples; i++) { float d = h[i] - mean; var2 += d * d; }
            float rms = Mathf.Sqrt(var2 / Samples);

            // Face angle from the local gradient, which is the acceptance
            // criterion everywhere else in this project.
            var slopes = new float[Samples - 1];
            for (int i = 0; i < Samples - 1; i++)
                slopes[i] = Mathf.Atan(Mathf.Abs(h[i + 1] - h[i]) / step) * Mathf.Rad2Deg;
            System.Array.Sort(slopes);
            float med = slopes[slopes.Length / 2];
            float p90 = slopes[(int)(slopes.Length * 0.9f)];

            float env = RegionField.Instance != null
                ? RegionField.Instance.Evaluate(new Vector2(spot.x, spot.y)) : -1f;

            // amp/L is the flatness number: tan(face) is directly pi*H/L, so
            // report it rather than guessing a wavelength.
            float ampOverL = Mathf.Tan(med * Mathf.Deg2Rad) / Mathf.PI;

            sb.AppendLine($"  {want,7:F0}   ({spot.x,7:F0},{spot.y,7:F0}) {actual,5:F0}m"
                + $"  {rms,7:F2}  {med,8:F1}  {p90,8:F1}  {ampOverL,7:F4}  {env,8:F3}");
            yield return null;
        }

        lut.Dispose();
        if (ctrl != null) ctrl.ReleaseForce();

        sb.AppendLine();
        sb.AppendLine("What good looks like: RMS SHOULD fall hard as the bottom comes up (the");
        sb.AppendLine("swell is supposed to die inshore). FACE ANGLE should NOT collapse with");
        sb.AppendLine("it -- short waves do not feel 10 m of bottom, so the chop survives and");
        sb.AppendLine("the water stays lively while getting smaller. A face angle that falls as");
        sb.AppendLine("fast as the height is the flat-sheet failure this change exists to fix.");

        Directory.CreateDirectory("/tmp");
        File.WriteAllText("/tmp/seasick-shorelife.txt", sb.ToString());
        Debug.Log("ShoreLifeProbe:\n" + sb);
        Destroy(gameObject);
    }

    /// Hunt for genuine water of about the wanted depth. Spiralling outward
    /// rather than trusting a coordinate: the world is procedural and every
    /// hardcoded spot in this project has eventually ended up on an island.
    static bool FindDepth(Vector3 origin, float want, in TerrainParams prm,
                          in NativeArray<float> lut, out float2 spot, out float actual)
    {
        spot = default; actual = 0f;
        float best = float.MaxValue;
        for (float r = 100f; r <= 6000f; r += 100f)
            for (int a = 0; a < 24; a++)
            {
                float ang = a * (Mathf.PI * 2f / 24f);
                var p = new float2(origin.x + Mathf.Cos(ang) * r, origin.z + Mathf.Sin(ang) * r);
                float d = -TerrainHeight.Height(p, prm, lut);
                if (d <= 1f) continue;                       // land or beach
                float err = Mathf.Abs(d - want);
                if (err < best) { best = err; spot = p; actual = d; }
                if (best < want * 0.12f) return true;        // close enough
            }
        return best < want * 0.5f;
    }
}
