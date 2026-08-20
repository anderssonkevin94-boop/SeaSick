using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;

/// Measures the three things Kevin actually complained about, rather than the
/// three things it is tempting to assume he meant.
///
///  1. "very unison ... like a row of waves going by" -> ACROSS-WIND variation.
///     A pure 1-D wave train has ZERO height variation along a line
///     perpendicular to its travel. The ratio of across-wind to along-wind
///     roughness is therefore a direct row-ness score: 1.0 is an isotropic
///     noise field, near 0 is corrugated iron.
///
///  2. "the top of the waves are very sharp" -> SKEWNESS of the height field.
///     A sine is 0. Sharp crests standing over broad flat troughs skew
///     positive, and that is exactly what Gerstner does as total steepness
///     approaches the self-intersection limit.
///
///  3. "the larger a wave, the wider it needs to be" -> where the amplitude
///     actually sits in the spectrum, and whether amplitude rises with
///     wavelength or peaks in the middle and falls away again.
public class SpectrumProbe : MonoBehaviour
{
    public const string OutPath = "/tmp/seasick-spectrum.txt";
    public static float West = 1500f;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SpectrumProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<SpectrumProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("SpectrumProbe").AddComponent<SpectrumProbe>();
    }

    static object Priv(object o, string field)
        => o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(o);

    IEnumerator Start()
    {
        var field = SeaSick.Ocean.WaveField.Instance;
        if (field == null) { Debug.LogError("SpectrumProbe: no WaveField"); yield break; }
        yield return new WaitForSeconds(1f);

        var sb = new StringBuilder();
        float t = Time.time;

        // --- the spectrum itself ---
        var waves = Priv(field, "waves") as System.Array;
        int stormStart = (int)Priv(field, "stormStart");
        Vector2 wind = field.WindDirection;
        float windAng = Mathf.Atan2(wind.x, wind.y) * Mathf.Rad2Deg;

        sb.AppendLine($"SPECTRUM  {waves.Length} waves ({stormStart} base + {waves.Length - stormStart} storm)  " +
                      $"seaState {field.SeaState01:F2}");
        sb.AppendLine("   #   L(m)   off-wind   steepness   amp(m)   amp/L");
        float ampBase = 0f, ampStorm = 0f, steepBase = 0f, steepStorm = 0f;
        float bestAmp = -1f, bestAmpL = 0f;
        for (int i = 0; i < waves.Length; i++)
        {
            object w = waves.GetValue(i);
            var ty = w.GetType();
            float L = (float)ty.GetField("wavelength").GetValue(w);
            float st = (float)ty.GetField("steepness").GetValue(w);
            Vector2 dir = (Vector2)ty.GetField("direction").GetValue(w);
            float k = 2f * Mathf.PI / L;
            float amp = st / k;
            float off = Mathf.DeltaAngle(windAng, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg);
            sb.AppendLine($"  {i,2}  {L,6:F1}   {off,7:F0}     {st,7:F4}   {amp,6:F2}   {amp / L,6:F4}"
                          + (i >= stormStart ? "   storm" : ""));
            if (i < stormStart) { ampBase += amp; steepBase += st; }
            else { ampStorm += amp; steepStorm += st; }
            if (amp > bestAmp) { bestAmp = amp; bestAmpL = L; }
        }
        sb.AppendLine($"   totals: steepness base {steepBase:F2} + storm {steepStorm:F2} = {steepBase + steepStorm:F2}" +
                      "   (Gerstner self-intersects near 1.0)");
        sb.AppendLine($"   amplitude base {ampBase:F2}m + storm {ampStorm:F2}m = {ampBase + ampStorm:F2}m");
        sb.AppendLine($"   TALLEST single wave is {bestAmpL:F0}m long ({bestAmp:F2}m)  " +
                      "<- 'bigger must be wider' wants this to be one of the LONGEST");

        // --- the sea as actually sampled ---
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        Vector3 home = voyage != null && voyage.HomePoint != null ? voyage.HomePoint.position : Vector3.zero;
        Report(sb, field, t, new Vector2(home.x, home.z) + new Vector2(-West, 0f), "1500m WEST (full storm)", wind);
        Report(sb, field, t, new Vector2(home.x, home.z) + new Vector2(-600f, 0f), "600m west (storm building)", wind);
        Report(sb, field, t, new Vector2(home.x, home.z) + new Vector2(-160f, 0f), "160m out (home shelf)", wind);

        System.IO.File.WriteAllText(OutPath, sb.ToString());
        Debug.Log("SpectrumProbe: done\n" + sb);
    }

    /// Walks two 400m lines through the same patch of sea — one along the wind,
    /// one across it — plus a grid for the height statistics.
    static void Report(StringBuilder sb, SeaSick.Ocean.WaveField field, float t, Vector2 centre,
                       string label, Vector2 wind)
    {
        Vector2 along = wind.normalized;
        Vector2 across = new Vector2(-along.y, along.x);
        const int N = 400;
        const float Span = 400f;
        const float GridSpan = 300f;

        float alongRough = LineRoughness(field, t, centre, along, N, Span);
        float acrossRough = LineRoughness(field, t, centre, across, N, Span);

        // Height statistics over a grid: spread, and the skew that says how
        // sharp the crests are relative to the troughs.
        //
        // 48 samples across 400m is 8.3m spacing, which cannot resolve a 10m
        // wave at all — the first version of this probe was aliasing the short
        // half of the spectrum and reported skew that swung negative for no
        // physical reason. 160 across 300m is 1.9m, four-plus samples on the
        // shortest wave in the set.
        // SampleHeight, not SampleHeightFast. The fast one is documented as
        // decoration-grade — one iteration of the inverse, "a few centimetres
        // of error will never be noticed" — and its error is signed
        // differently at crests than at troughs, which is precisely the shape
        // this statistic is trying to read. It was reporting NEGATIVE skew, a
        // sea with sharper troughs than crests, which Gerstner cannot produce.
        const int G = 160;
        var heights = new float[G * G];
        float sum = 0f, sum2 = 0f, sum3 = 0f, hi = -9999f, lo = 9999f;
        int n = 0;
        for (int a = 0; a < G; a++)
            for (int b = 0; b < G; b++)
            {
                Vector2 p = centre + new Vector2((a / (float)(G - 1) - 0.5f) * GridSpan,
                                                 (b / (float)(G - 1) - 0.5f) * GridSpan);
                float h = field.SampleHeight(p, t);
                heights[n++] = h;
                sum += h;
                if (h > hi) hi = h;
                if (h < lo) lo = h;
            }
        float mean = sum / n;
        for (int i = 0; i < n; i++)
        {
            float d = heights[i] - mean;
            sum2 += d * d; sum3 += d * d * d;
        }
        float sd = Mathf.Sqrt(sum2 / n);
        float skew = sd > 0.0001f ? (sum3 / n) / (sd * sd * sd) : 0f;

        sb.AppendLine();
        sb.AppendLine($"-- {label}   storm {field.StormAmount01(centre):F2}  region {field.RegionScale(centre):F2}");
        sb.AppendLine($"   height  {lo:F1} .. {hi:F1} m   (spread {(hi - lo):F1} m, sd {sd:F2})");
        sb.AppendLine($"   ROW-NESS  across-wind {acrossRough:F2}m vs along-wind {alongRough:F2}m  " +
                      $"=> ratio {(alongRough > 0.001f ? acrossRough / alongRough : 0f):F2}   " +
                      "(1.0 = noise field, 0 = corrugated)");
        sb.AppendLine($"   CREST SKEW {skew:F2}   (0 = sine, higher = sharper tops over flatter troughs)");
    }

    /// Mean absolute slope walking in one direction — how much the sea moves
    /// as you go that way. Slope rather than height spread, so one long swell
    /// crossing the line does not read the same as genuine texture.
    ///
    /// Averaged over LINES parallel lines. A single line is one realisation of
    /// a 20-wave field, not an estimate of it: the first version of this probe
    /// walked one line each way and reported row-ness 2.13 on the home shelf
    /// against 0.99 at 600m west, from what is very nearly the same spectrum.
    /// That spread was the instrument, not the sea.
    const int Lines = 32;

    static float LineRoughness(SeaSick.Ocean.WaveField field, float t, Vector2 centre,
                               Vector2 dir, int n, float span)
    {
        Vector2 side = new Vector2(-dir.y, dir.x);
        float step = span / n;
        float total = 0f;
        for (int L = 0; L < Lines; L++)
        {
            Vector2 origin = centre + side * ((L / (float)(Lines - 1) - 0.5f) * span);
            float prev = field.SampleHeight(origin - dir * (span * 0.5f), t);
            float sum = 0f;
            for (int i = 1; i <= n; i++)
            {
                Vector2 p = origin + dir * (-span * 0.5f + step * i);
                float h = field.SampleHeight(p, t);
                sum += Mathf.Abs(h - prev);
                prev = h;
            }
            total += sum / n;
        }
        return total / Lines * 10f;   // scaled to a readable magnitude
    }
}
