using System.Collections;
using System.Text;
using SeaSick.Ocean;
using SeaSick.Ship;
using UnityEngine;

/// Regional sea state: does the water actually get worse the further out you
/// go, and does the ship's own resistance agree with it?
public class RegionProbe : MonoBehaviour
{
    public const string OutPath = "/tmp/seasick-region-probe.txt";

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("RegionProbe: not in play mode"); return; }
        new GameObject("RegionProbe").AddComponent<RegionProbe>();
    }

    StringBuilder log = new StringBuilder();
    void Line(string s) { log.AppendLine(s); System.IO.File.WriteAllText(OutPath, log.ToString()); }

    IEnumerator Start()
    {
        var field = WaveField.Instance;
        var motor = FindAnyObjectByType<ShipMotor>();
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        if (field == null) { Line("FAIL: no WaveField"); yield break; }

        Vector2 home = voyage != null && voyage.HomePoint != null
            ? new Vector2(voyage.HomePoint.position.x, voyage.HomePoint.position.z)
            : Vector2.zero;
        Line($"home at {home.x:F0},{home.y:F0}   global SeaState01 {field.SeaState01:F2}");
        Line("");
        Line("dist\tregion\tseverity\tcrest(m)\ttrough(m)\trange(m)");

        float t = Time.time;
        foreach (float dist in new[] { 0f, 150f, 260f, 450f, 700f, 950f, 1150f, 1400f })
        {
            // Sample a ring and take the extremes, so one unlucky phase doesn't
            // masquerade as the sea state.
            float hi = -999f, lo = 999f;
            for (int i = 0; i < 64; i++)
            {
                float a = i / 64f * Mathf.PI * 2f;
                Vector2 p = home + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * dist;
                float h = field.SampleHeight(p, t);
                if (h > hi) hi = h;
                if (h < lo) lo = h;
            }
            Vector2 probe = home + new Vector2(dist, 0f);
            Line($"{dist:F0}\t{field.RegionScale(probe):F2}\t{field.SeaSeverity01(probe, t):F2}\t" +
                 $"{hi:F2}\t{lo:F2}\t{(hi - lo):F2}");
        }

        Line("");
        Line("=== does the ship's resistance follow the water? ===");
        if (motor != null)
        {
            Line("dist\tseverity\tresist(dead into)");
            foreach (float dist in new[] { 150f, 700f, 1150f })
            {
                Vector2 p = home + new Vector2(dist, 0f);
                float r = ShipMotor.SeaResistanceAt(p, Vector3.forward, 0.45f,
                    out float sev, out float head, out _);
                // Report the worst case: dead into it, whatever direction that is.
                float worst = 1f - 0.45f * 1f * sev;
                Line($"{dist:F0}\t{sev:F2}\t{worst:F2}");
            }
        }

        Line("");
        Line("DONE");
    }
}
