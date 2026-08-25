using System.Collections;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using SeaSick.Terrain;

/// The terrain's cost ledger, measured honestly in the editor: everything
/// here is what the GPU is ASKED to do, which is device-independent, plus the
/// main-thread cost of streaming. GPU milliseconds are a device measurement
/// and this probe does not pretend otherwise (see PerfProbe for the same
/// split on the ocean).
///
/// Two things this gets right that a frame counter would not. The editor Game
/// view is landscape and the target is portrait, so visibility is tested
/// against a portrait frustum built from the real camera. And the editor
/// steals the main thread when job workers saturate the cores, so the
/// streaming numbers are the streamer's own phase timers, not frame time.
///
/// Play mode, Sea.unity. Writes /tmp/seasick-terrainperf.txt.
public class TerrainPerfProbe : MonoBehaviour
{
    // The 60 fps budget this is gated against. 16.6 ms a frame total; terrain
    // shares it with the ocean, the ship and the HUD.
    const int MaxVisibleVerts = 150000;
    const int MaxVisibleChunks = 60;
    const int MaxShadowTris = 120000;
    const float MaxStreamMs = 4.0f;
    const float PortraitAspect = 1080f / 2340f;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("TerrainPerfProbe: not in play mode"); return; }
        new GameObject("TerrainPerfProbe").AddComponent<TerrainPerfProbe>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        TerrainStreamer st = FindFirstObjectByType<TerrainStreamer>();
        if (st == null) { Debug.LogError("TerrainPerfProbe: no TerrainStreamer"); yield break; }

        // Let the streamer fill and the ship settle before reading the ledger.
        for (int w = 0; w < 180; w++) yield return null;

        sb.AppendLine("TerrainPerfProbe — terrain load ledger");
        // Identify the tier by shadow distance, not by QualitySettings.names:
        // names is not indexed by level here and reported "PC" for Mobile.
        sb.AppendLine("quality level " + QualitySettings.GetQualityLevel() +
            ", shadow distance " + string.Format("{0:F0}", QualitySettings.shadowDistance) +
            " m, shadow cascades " + QualitySettings.shadowCascades);
        sb.AppendLine("viewRadius " + st.settings.viewRadius + ", lod0 " + st.settings.lod0Radius +
            ", lod1 " + st.settings.lod1Radius + ", collider " + st.settings.colliderRadius +
            ", chunkRes " + st.settings.chunkResolution + ", jobsInFlight " + st.settings.jobsInFlight);
        sb.AppendLine();

        Camera cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();
        if (cam == null) { Debug.LogError("TerrainPerfProbe: no camera"); yield break; }

        // A portrait copy of the real camera, purely to get portrait frustum
        // planes. Never enabled, so it renders nothing.
        GameObject portraitGo = new GameObject("PortraitFrustum");
        Camera portrait = portraitGo.AddComponent<Camera>();
        portrait.enabled = false;
        portrait.CopyFrom(cam);
        portrait.aspect = PortraitAspect;
        portraitGo.transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(portrait);

        var renderers = st.GetComponentsInChildren<MeshRenderer>(true);
        int loadedChunks = 0, loadedVerts = 0, loadedTris = 0;
        int visChunks = 0, visVerts = 0, visTris = 0;
        int shadowChunks = 0, shadowTris = 0;
        int colChunks = 0, colTris = 0;
        long meshBytes = 0;
        int[] lodChunks = new int[8];
        int[] lodVerts = new int[8];

        float shadowDist = QualitySettings.shadowDistance;
        Vector3 camPos = cam.transform.position;

        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer r = renderers[i];
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            Mesh m = mf.sharedMesh;
            meshBytes += Profiler.GetRuntimeMemorySizeLong(m);
            if (!r.enabled) continue;

            int v = m.vertexCount;
            int t = (int)(m.GetIndexCount(0) / 3);
            loadedChunks++; loadedVerts += v; loadedTris += t;

            int2 coord = new int2(
                Mathf.FloorToInt(r.transform.position.x / st.settings.chunkSize + 0.001f),
                Mathf.FloorToInt(r.transform.position.z / st.settings.chunkSize + 0.001f));
            int lod = st.LodAt(coord);
            if (lod >= 0 && lod < 8) { lodChunks[lod]++; lodVerts[lod] += v; }

            if (GeometryUtility.TestPlanesAABB(planes, r.bounds))
            {
                visChunks++; visVerts += v; visTris += t;
            }

            // Shadow casters: inside the shadow distance of the camera. The
            // cascade is one slice at 50 m, so this is the whole shadow pass.
            if (r.bounds.SqrDistance(camPos) < shadowDist * shadowDist)
            {
                shadowChunks++; shadowTris += t;
            }

            MeshCollider mc = r.GetComponent<MeshCollider>();
            if (mc != null && mc.enabled && mc.sharedMesh != null)
            {
                colChunks++; colTris += t;
            }
        }

        sb.AppendLine("LOADED (everything streamed in, before culling)");
        sb.AppendLine(string.Format("  chunks {0}, verts {1}, tris {2}", loadedChunks, loadedVerts, loadedTris));
        for (int l = 1; l < 8; l++)
            if (lodChunks[l] > 0)
                sb.AppendLine(string.Format("    lodStep {0}: {1} chunks, {2} verts", l, lodChunks[l], lodVerts[l]));
        sb.AppendLine(string.Format("  mesh memory {0:F1} MB", meshBytes / 1048576.0));
        sb.AppendLine();
        sb.AppendLine("SUBMITTED PER FRAME (portrait frustum, aspect " + string.Format("{0:F3}", PortraitAspect) + ")");
        sb.AppendLine(string.Format("  forward pass: {0} chunks, {1} verts, {2} tris", visChunks, visVerts, visTris));
        sb.AppendLine(string.Format("  shadow pass:  {0} chunks, {1} tris (within {2:F0} m)", shadowChunks, shadowTris, shadowDist));
        sb.AppendLine(string.Format("  depth prepass: same {0} chunks again if the depth texture is on", visChunks));
        sb.AppendLine(string.Format("  one draw call per chunk per pass — no instancing, each chunk is a unique mesh"));
        sb.AppendLine();
        sb.AppendLine(string.Format("PHYSICS: {0} mesh colliders enabled, {1} tris", colChunks, colTris));
        sb.AppendLine();

        // Streaming cost while actually sailing: hop the target a chunk at a
        // time and keep the worst main-thread frame of each crossing.
        Transform oldTarget = st.target;
        GameObject rig = new GameObject("PerfRig");
        rig.transform.position = oldTarget != null ? oldTarget.position : Vector3.zero;
        st.target = rig.transform;
        for (int w = 0; w < 30; w++) yield return null;

        float cs = st.settings.chunkSize;
        float worstFrame = 0f, sumWorst = 0f;
        float worstReplan = 0f, worstApply = 0f, worstBake = 0f, worstCollider = 0f;
        int crossings = 24;
        int over4 = 0, over8 = 0;
        StringBuilder perCrossing = new StringBuilder();
        for (int k = 0; k < crossings; k++)
        {
            st.ResetStats();
            rig.transform.position += new Vector3(cs, 0f, k % 3 == 0 ? cs : 0f);
            float worstThis = 0f;
            for (int f = 0; f < 12; f++)
            {
                yield return null;
                worstThis = Mathf.Max(worstThis, st.LastMainThreadMs);
            }
            worstReplan = Mathf.Max(worstReplan, st.WorstReplanMs);
            worstApply = Mathf.Max(worstApply, st.WorstApplyMs);
            worstBake = Mathf.Max(worstBake, st.WorstBakeMs);
            worstCollider = Mathf.Max(worstCollider, st.WorstSetColliderMs);
            // Skip the first crossing: its lazy-init cost is a one-off and
            // lands on every run (this is what CrossingProbe was built for).
            if (k > 0)
            {
                worstFrame = Mathf.Max(worstFrame, worstThis);
                sumWorst += worstThis;
                if (worstThis > 4f) over4++;
                if (worstThis > 8f) over8++;
                perCrossing.Append(string.Format("{0:F1} ", worstThis));
            }
        }
        st.target = oldTarget;
        Destroy(rig);
        Destroy(portraitGo);

        float meanWorst = sumWorst / Mathf.Max(1, crossings - 1);
        sb.AppendLine("STREAMING MAIN THREAD (" + crossings + " chunk crossings, first discarded as one-off)");
        sb.AppendLine(string.Format("  worst crossing frame {0:F2} ms, mean of per-crossing worst {1:F2} ms", worstFrame, meanWorst));
        sb.AppendLine(string.Format("  worst phases: replan {0:F2}, apply {1:F2}, bake {2:F2}, setCollider {3:F2} ms",
            worstReplan, worstApply, worstBake, worstCollider));
        sb.AppendLine(string.Format("  crossings over 4 ms: {0} of {1}; over 8 ms: {2}", over4, crossings - 1, over8));
        sb.AppendLine("  per crossing: " + perCrossing.ToString());
        sb.AppendLine();

        bool passVerts = visVerts < MaxVisibleVerts;
        bool passChunks = visChunks < MaxVisibleChunks;
        bool passShadow = shadowTris < MaxShadowTris;
        bool passStream = worstFrame < MaxStreamMs;
        sb.AppendLine("GATES against a 60 fps budget (16.6 ms a frame)");
        sb.AppendLine(string.Format("  visible verts  {0,8} < {1,-8} {2}", visVerts, MaxVisibleVerts, passVerts ? "PASS" : "FAIL"));
        sb.AppendLine(string.Format("  visible chunks {0,8} < {1,-8} {2}", visChunks, MaxVisibleChunks, passChunks ? "PASS" : "FAIL"));
        sb.AppendLine(string.Format("  shadow tris    {0,8} < {1,-8} {2}", shadowTris, MaxShadowTris, passShadow ? "PASS" : "FAIL"));
        sb.AppendLine(string.Format("  stream worst   {0,8:F2} < {1,-8:F2} {2}", worstFrame, MaxStreamMs, passStream ? "PASS" : "FAIL"));
        sb.AppendLine();
        sb.AppendLine(passVerts && passChunks && passShadow && passStream ? "PASS" : "FAIL");
        sb.AppendLine();
        sb.AppendLine("Not measured here, and only a phone can: GPU ms, fill rate against the");
        sb.AppendLine("ocean's overdraw, and thermal behaviour over a long session.");

        System.IO.File.WriteAllText("/tmp/seasick-terrainperf.txt", sb.ToString());
        Debug.Log("TerrainPerfProbe:\n" + sb);
        Destroy(gameObject);
    }
}
