using System.Collections;
using System.Text;
using SeaSick.Ocean;
using UnityEngine;

/// Measures the storm sea instead of guessing at it: how tall the water
/// actually gets, and what the Gerstner Jacobian actually ranges over — which
/// is what the breaking-foam threshold has to be set from.
public class StormMeasure : MonoBehaviour
{
    public const string OutPath = "/tmp/seasick-storm-measure.txt";

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("StormMeasure: not in play mode"); return; }
        new GameObject("StormMeasure").AddComponent<StormMeasure>();
    }

    StringBuilder log = new StringBuilder();
    void Line(string s) { log.AppendLine(s); System.IO.File.WriteAllText(OutPath, log.ToString()); }

    IEnumerator Start()
    {
        var field = WaveField.Instance;
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        if (field == null) { Line("FAIL: no WaveField"); yield break; }
        yield return null;

        Vector2 home = voyage != null && voyage.HomePoint != null
            ? new Vector2(voyage.HomePoint.position.x, voyage.HomePoint.position.z)
            : Vector2.zero;

        Line($"seaState {field.SeaState01:F2}   (ship is 21m long, deck 2.05m up)");
        Line("");
        Line("westOf\tstorm\tregion\tcrest\ttrough\tHEIGHT\tJmin\tJmean\tbreak%");

        float t = Time.time;
        foreach (float west in new[] { 0f, 400f, 800f, 1200f, 1600f, 2200f })
        {
            Vector2 centre = home + new Vector2(-west, 0f);
            float hi = -9999f, lo = 9999f;
            float jmin = 9999f, jsum = 0f;
            int n = 0, breaking = 0;

            // A 400m patch, sampled on a grid.
            for (int a = 0; a < 40; a++)
            {
                for (int b = 0; b < 40; b++)
                {
                    Vector2 p = centre + new Vector2((a - 20) * 10f, (b - 20) * 10f);
                    float h = field.SampleHeight(p, t);
                    if (h > hi) hi = h;
                    if (h < lo) lo = h;

                    float j = field.JacobianAt(p);
                    if (j < jmin) jmin = j;
                    jsum += j; n++;
                    if (j < 0.62f) breaking++;
                }
            }

            Line($"{west:F0}\t{field.StormAmount01(centre):F2}\t{field.RegionScale(centre):F2}\t" +
                 $"{hi:F1}\t{lo:F1}\t{(hi - lo):F1}\t{jmin:F2}\t{(jsum / n):F2}\t" +
                 $"{(100f * breaking / n):F0}%");
            yield return null;
        }

        Line("");
        Line("Jmin/Jmean are the Gerstner Jacobian: 1 = undisturbed, 0 = folding over.");
        Line("break% is the share of samples already under the current 0.62 threshold.");
        Line("DONE");
    }
}
