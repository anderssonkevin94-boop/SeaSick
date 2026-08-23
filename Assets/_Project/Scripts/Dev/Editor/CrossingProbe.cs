using System.Collections;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Terrain;

/// Play-mode micro-probe: hop the streamer target one chunk every 12 frames
/// for 40 crossings and log each crossing's replan/release cost, to tell a
/// one-off first-crossing cost from a systemic one. /tmp/seasick-crossing.txt
public class CrossingProbe : MonoBehaviour
{
    public static void Execute() { new GameObject("CrossingProbe").AddComponent<CrossingProbe>(); }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        TerrainStreamer st = FindFirstObjectByType<TerrainStreamer>();
        st.settings.jobsInFlight = 2;
        GameObject rig = new GameObject("ProbeRig");
        rig.transform.position = new Vector3(-2400f, 30f, 0f);
        st.target = rig.transform;
        for (int w = 0; w < 30; w++) yield return null; // initial fill
        sb.AppendLine("jobsInFlight=" + st.settings.jobsInFlight + " pending after fill=" + st.PendingCount);
        float cs = st.settings.chunkSize;
        for (int k = 0; k < 40; k++)
        {
            st.ResetStats();
            rig.transform.position += new Vector3(cs, 0f, k % 3 == 0 ? cs : 0f);
            yield return null; // the crossing frame
            float replan = st.WorstReplanMs, rel = st.WorstReleaseMs, scan = st.WorstScanMs, loop = st.WorstLoopMs;
            int dropped = st.WorstReplanDropped, queued = st.WorstReplanQueued;
            float worstAfter = 0f;
            for (int f = 0; f < 11; f++) { yield return null; worstAfter = math.max(worstAfter, st.LastMainThreadMs); }
            sb.AppendLine("crossing " + k + ": replan=" + replan.ToString("F1") + " (scan " + scan.ToString("F1") + " release " + rel.ToString("F1") + " loop " + loop.ToString("F1")
                + ") dropped=" + dropped + " queued=" + queued + " worst-other-frame=" + worstAfter.ToString("F1") + " pending=" + st.PendingCount);
        }
        System.IO.File.WriteAllText("/tmp/seasick-crossing.txt", sb.ToString());
        Debug.Log("CrossingProbe done");
    }
}
