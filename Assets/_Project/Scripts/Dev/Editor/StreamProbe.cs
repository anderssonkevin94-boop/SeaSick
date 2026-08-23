using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Terrain;

/// Play-mode gate for TerrainStreamer in TerrainLab. Drives the streamer's
/// target on a 2 km sail across the western island at ship speed, then:
/// every chunk within viewRadius is loaded and nothing beyond the hysteresis
/// band is; colliders exist exactly within colliderRadius; every loaded
/// neighbour pair shares bit-identical edge positions and normals; the pool
/// bounds the object count; and main-thread build cost is recorded as the
/// step-5 baseline. Writes /tmp/seasick-stream.txt + /tmp/seasick-stream.png.
public class StreamProbe : MonoBehaviour
{
    static StringBuilder sb;
    static int fails;

    static void Gate(string name, bool ok, string detail)
    {
        sb.AppendLine((ok ? "PASS " : "FAIL ") + name + "  " + detail);
        if (!ok) fails++;
    }

    public static void Execute()
    {
        new GameObject("StreamProbe").AddComponent<StreamProbe>();
    }

    IEnumerator Start()
    {
        sb = new StringBuilder();
        fails = 0;
        TerrainStreamer st = FindFirstObjectByType<TerrainStreamer>();
        if (st == null) { Finish("no TerrainStreamer; run SetupTerrainLab"); yield break; }

        GameObject rig = new GameObject("ProbeRig");
        Vector3 from = new Vector3(-2400f, 30f, 0f), to = new Vector3(-800f, 30f, 1200f);
        rig.transform.position = from;
        st.target = rig.transform;
        Camera cam = Camera.main;

        float speed = 12f; // m/s, a fast ship
        float dist = Vector3.Distance(from, to);
        float t = 0f;
        float worstBuildMs = 0f, sumBuildMs = 0f; int frames = 0, builtBefore = st.TotalBuilt;
        float worstFrameMs = 0f, warmupMs = 0f; int worstFrameIdx = -1;
        for (int w = 0; w < 5; w++) yield return null;
        st.ResetStats();
        int maxLoaded = 0;
        while (t < dist)
        {
            t += speed * Time.deltaTime;
            rig.transform.position = Vector3.Lerp(from, to, t / dist);
            if (cam != null)
            {
                cam.transform.position = rig.transform.position + new Vector3(-250f, 160f, -300f);
                cam.transform.LookAt(rig.transform.position);
            }
            yield return null;
            // The first frames include Burst's synchronous first-use compile in
            // the editor; report them separately from steady state.
            if (frames < 5) warmupMs = math.max(warmupMs, st.LastMainThreadMs);
            else
            {
                if (st.LastMainThreadMs > worstBuildMs) { worstBuildMs = st.LastMainThreadMs; worstFrameIdx = frames; }
                sumBuildMs += st.LastMainThreadMs;
            }
            frames++;
            worstFrameMs = math.max(worstFrameMs, Time.deltaTime * 1000f);
            maxLoaded = math.max(maxLoaded, st.LoadedCount);
        }
        // Let the queue drain.
        float settle = 0f;
        while (st.PendingCount > 0 && settle < 20f) { settle += Time.deltaTime; yield return null; }

        int2 centre = st.ChunkCoordOf(rig.transform.position);
        int r = st.ViewRadius, dropR = r + st.unloadHysteresis;
        int missing = 0, tooFar = 0, collBad = 0, lodBad = 0;
        for (int z = -r; z <= r; z++)
            for (int x = -r; x <= r; x++)
            {
                int2 c = centre + new int2(x, z);
                if (!st.IsLoaded(c)) missing++;
                else if (st.LodAt(c) != st.LodFor(c, centre)) lodBad++;
            }
        foreach (int2 c in st.LoadedCoords)
        {
            int2 d = math.abs(c - centre);
            int cheb = math.max(d.x, d.y);
            if (cheb > dropR) tooFar++;
            bool shouldColl = cheb <= st.ColliderRadius;
            if (st.HasCollider(c) != shouldColl) collBad++;
        }
        int expected = (2 * r + 1) * (2 * r + 1);
        Gate("coverage", missing == 0, missing + " missing of " + expected + " in view radius, loaded=" + st.LoadedCount);
        Gate("unload", tooFar == 0, tooFar + " chunks beyond radius " + dropR);
        Gate("colliders", collBad == 0, collBad + " chunks with wrong collider state (radius " + st.ColliderRadius + ")");
        Gate("lod-assignment", lodBad == 0, lodBad + " chunks at the wrong LOD after settle");
        Gate("pool-bounded", st.transform.childCount <= (2 * dropR + 1) * (2 * dropR + 1) + 8, st.transform.childCount + " chunk objects for " + st.LoadedCount + " loaded");

        // Seams across every loaded +X / +Z neighbour pair.
        int pairs = 0, posBad = 0, nrmBad = 0, lodPairs = 0;
        List<int2> coords = new List<int2>(st.LoadedCoords);
        foreach (int2 c in coords)
        {
            Mesh a = st.MeshAt(c);
            if (a == null) continue;
            int lod = st.LodAt(c);
            int n = (st.settings.chunkResolution - 1) / lod + 1;
            Vector3[] va = a.vertices, na = a.normals;
            float2 oa = new float2(c.x, c.y) * st.settings.chunkSize;
            Mesh bx = st.MeshAt(c + new int2(1, 0));
            if (bx != null && st.LodAt(c + new int2(1, 0)) != lod) { lodPairs++; bx = null; }
            if (bx != null)
            {
                pairs++;
                Vector3[] vb = bx.vertices, nb = bx.normals;
                for (int j = 0; j < n; j++)
                {
                    Vector3 pa = va[j * n + (n - 1)] + new Vector3(oa.x, 0f, oa.y);
                    Vector3 pb = vb[j * n] + new Vector3(oa.x + st.settings.chunkSize, 0f, oa.y);
                    if (pa != pb || pa.y != pb.y) posBad++;
                    if (na[j * n + (n - 1)] != nb[j * n]) nrmBad++;
                }
            }
            Mesh bz = st.MeshAt(c + new int2(0, 1));
            if (bz != null && st.LodAt(c + new int2(0, 1)) != lod) { lodPairs++; bz = null; }
            if (bz != null)
            {
                pairs++;
                Vector3[] vb = bz.vertices, nb = bz.normals;
                for (int i = 0; i < n; i++)
                {
                    Vector3 pa = va[(n - 1) * n + i] + new Vector3(oa.x, 0f, oa.y);
                    Vector3 pb = vb[i] + new Vector3(oa.x, 0f, oa.y + st.settings.chunkSize);
                    if (pa != pb || pa.y != pb.y) posBad++;
                    if (na[(n - 1) * n + i] != nb[i]) nrmBad++;
                }
            }
        }
        Gate("seams", pairs > 0 && posBad == 0 && nrmBad == 0, pairs + " same-LOD neighbour pairs, " + posBad + " position / " + nrmBad + " normal mismatches; " + lodPairs + " LOD-boundary pairs (skirted)");
        Gate("main-thread", worstBuildMs < 10f, "steady-state streamer main-thread ms worst=" + worstBuildMs.ToString("F1") + " (frame " + worstFrameIdx + ") mean=" + (sumBuildMs / math.max(1, frames - 5)).ToString("F2") + "; warmup worst=" + warmupMs.ToString("F1") + " (was 45.3 / 3.12 before jobs)");

        int built = st.TotalBuilt - builtBefore;
        sb.AppendLine("phase worst ms: replan=" + st.WorstReplanMs.ToString("F1") + " apply=" + st.WorstApplyMs.ToString("F1") + " bakeComplete=" + st.WorstBakeMs.ToString("F1")
            + " setCollider=" + st.WorstSetColliderMs.ToString("F1") + " schedule=" + st.WorstScheduleMs.ToString("F1"));
        sb.AppendLine("worst replan breakdown: scan=" + st.WorstScanMs.ToString("F1") + " release=" + st.WorstReleaseMs.ToString("F1") + " (" + st.WorstReplanDropped + " dropped) loop=" + st.WorstLoopMs.ToString("F1")
            + " sort=" + st.WorstSortMs.ToString("F1") + " (" + st.WorstReplanQueued + " queued) gcCollections=" + st.WorstReplanGCs);
        sb.AppendLine("built " + built + " chunks over " + frames + " frames; worst frame " + worstFrameMs.ToString("F1") + " ms; maxLoaded=" + maxLoaded
            + "; jobsInFlight=" + st.settings.jobsInFlight + " viewRadius=" + r);

        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot("/tmp/seasick-stream.png");
        yield return null;
        Finish(null);
    }

    static void Finish(string err)
    {
        if (err != null) sb.AppendLine(err);
        sb.Insert(0, (fails == 0 && err == null ? "ALL PASS" : fails + " FAIL") + "\n");
        System.IO.File.WriteAllText("/tmp/seasick-stream.txt", sb.ToString());
        Debug.Log("StreamProbe:\n" + sb);
    }
}
