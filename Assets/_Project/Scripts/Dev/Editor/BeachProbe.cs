using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.World;

/// Beach-slope tuning instrument. For every discovered island it re-measures
/// the shore on all 46 sector bearings and records the rise per metre over
/// the first 12 m inland — the exact quantity WorldSettings.beachMaxSlope is
/// compared against. It then reports the landable fraction, and the count of
/// islands with NO landing at all, at a range of candidate thresholds.
///
/// The point is to choose the threshold by reading a distribution once
/// instead of rebuilding the world per candidate. Play mode, Sea.unity.
/// Writes /tmp/seasick-beach.txt.
public class BeachProbe : MonoBehaviour
{
    static readonly float[] Thresholds = { 0.30f, 0.40f, 0.50f, 0.60f, 0.70f };

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("BeachProbe: not in play mode"); return; }
        new GameObject("BeachProbe").AddComponent<BeachProbe>();
    }

    IEnumerator Start()
    {
        // The populator runs in Start; give it a beat to finish discovery.
        yield return new WaitForSeconds(2f);

        System.Func<float, float, float> h = Island.TerrainHeight;
        if (h == null)
        {
            Debug.LogError("BeachProbe: Island.TerrainHeight is null (no populator?)");
            yield break;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("BeachProbe — shore rise per metre over 12 m inland, per sector bearing");
        sb.AppendLine("current WorldSettings.beachMaxSlope = " + string.Format("{0:F2}", Island.BeachMaxSlope));
        sb.AppendLine();

        int sectors = Island.Sectors;
        var all = new System.Collections.Generic.List<float>();
        int[] noBeachIslands = new int[Thresholds.Length];
        int[] totalBeaches = new int[Thresholds.Length];
        int totalSectors = 0;
        int islandCount = 0;

        sb.AppendLine("island                 meanR   sectors   landable % at slope 0.30 / 0.40 / 0.50 / 0.60 / 0.70");

        foreach (var isle in Island.All)
        {
            if (isle == null) continue;
            islandCount++;
            Vector3 c = isle.transform.position;
            int[] hits = new int[Thresholds.Length];
            int measured = 0;

            for (int s = 0; s < sectors; s++)
            {
                float ang = s / (float)sectors * Mathf.PI * 2f;
                float dx = Mathf.Sin(ang), dz = Mathf.Cos(ang);
                float r = 2f, shore = -1f;
                for (; r < 2000f; r += 2f)
                {
                    if (h(c.x + dx * r, c.z + dz * r) < -0.3f) { shore = r; break; }
                }
                if (shore < 0f) shore = r;
                if (shore <= 13f) continue;   // too thin a spit to land on at all

                float h0 = h(c.x + dx * (shore - 1f), c.z + dz * (shore - 1f));
                float h1 = h(c.x + dx * (shore - 13f), c.z + dz * (shore - 13f));
                float slope = (h1 - h0) / 12f;
                all.Add(slope);
                measured++;
                for (int t = 0; t < Thresholds.Length; t++)
                    if (slope < Thresholds[t]) hits[t]++;
            }

            totalSectors += measured;
            for (int t = 0; t < Thresholds.Length; t++)
            {
                totalBeaches[t] += hits[t];
                if (hits[t] == 0) noBeachIslands[t]++;
            }

            string name = isle.name;
            if (name.Length > 20) name = name.Substring(0, 20);
            sb.Append(name.PadRight(22));
            sb.Append(string.Format("{0,6:F0}", isle.Radius));
            sb.Append(string.Format("{0,10}", measured));
            sb.Append("   ");
            for (int t = 0; t < Thresholds.Length; t++)
                sb.Append(string.Format("{0,5:F0}", measured > 0 ? 100f * hits[t] / measured : 0f));
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("islands " + islandCount + ", shore bearings measured " + totalSectors);
        sb.AppendLine();
        sb.AppendLine("threshold   landable shore   islands with NO landing");
        for (int t = 0; t < Thresholds.Length; t++)
        {
            sb.AppendLine(string.Format("{0,9:F2}{1,16:F0}%{2,25}",
                Thresholds[t],
                totalSectors > 0 ? 100f * totalBeaches[t] / totalSectors : 0f,
                noBeachIslands[t]));
        }

        // The distribution itself, so a threshold between the sampled ones can
        // be read off rather than guessed.
        all.Sort();
        sb.AppendLine();
        sb.AppendLine("shore slope percentiles (rise per metre):");
        int[] pct = { 5, 10, 25, 50, 75, 90, 95 };
        for (int i = 0; i < pct.Length; i++)
        {
            int idx = Mathf.Clamp(Mathf.RoundToInt(all.Count * pct[i] / 100f), 0, all.Count - 1);
            sb.AppendLine(string.Format("  p{0,-3}  {1:F2}", pct[i], all.Count > 0 ? all[idx] : 0f));
        }

        System.IO.File.WriteAllText("/tmp/seasick-beach.txt", sb.ToString());
        Debug.Log("BeachProbe:\n" + sb);
        Destroy(gameObject);
    }
}
