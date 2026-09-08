using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using SeaSick.Ocean;

/// **What does heavy water COST, against calm water, per frame?**
///
/// Kevin: "when hitting wild and heavy water the water starts lagging a lot,
/// it's choppy." HitchProbe answers a different question (does the sampled
/// surface step) and driven from Coplay it cannot even see frame cost: the
/// unfocused editor throttles play mode to a flat 10 fps, so every frame
/// reads 100 ms whatever the game is doing. Profiler MARKERS and COUNTERS are
/// still honest under that throttle -- the idle is outside them -- so this
/// probe records them through ProfilerRecorder and prints calm beside heavy.
///
/// One session, same spot, same course, full throttle: 15 s at severity 0.15
/// then 15 s at severity 1.0 (after the spectrum and the sky's storminess
/// have had time to arrive, because the spray keys on the SKY, not the sea).
/// Everything that could plausibly scale with the weather is read every
/// frame: the main-thread markers, physics, particles, render batches, GC
/// allocation, the live particle count across every system in the scene,
/// the number of main-thread ocean queries, and the GPU frame time if the
/// platform reports it. p50 / p95 / max per leg, and the p50 ratio.
///
/// Plain C# for Coplay. Play mode, Sea.unity. Writes /tmp/seasick-cost.txt.
public class CostProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("CostProbe: not in play mode"); return; }
        CostProbe old = FindAnyObjectByType<CostProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("CostProbe").AddComponent<CostProbe>();
    }

    const float LegSecs = 15f;

    class Metric
    {
        public string name;
        public ProfilerRecorder rec;
        public bool isMarker;      // ns -> ms
        public List<float> calm = new List<float>(2048);
        public List<float> heavy = new List<float>(2048);
    }

    List<Metric> metrics = new List<Metric>();
    List<float> frameCalm = new List<float>(2048), frameHeavy = new List<float>(2048);
    List<float> partsCalm = new List<float>(2048), partsHeavy = new List<float>(2048);
    List<float> queriesCalm = new List<float>(2048), queriesHeavy = new List<float>(2048);
    List<float> gpuCalm = new List<float>(2048), gpuHeavy = new List<float>(2048);
    ParticleSystem[] systems;
    FrameTiming[] timings = new FrameTiming[1];

    void AddMarker(string name, ProfilerCategory cat)
    {
        var m = new Metric { name = name, isMarker = true };
        m.rec = ProfilerRecorder.StartNew(cat, name, 1);
        metrics.Add(m);
    }

    void AddCounter(string name, ProfilerCategory cat)
    {
        var m = new Metric { name = name, isMarker = false };
        m.rec = ProfilerRecorder.StartNew(cat, name, 1);
        metrics.Add(m);
    }

    IEnumerator Start()
    {
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var sea = SeaStateController.Instance;
        if (motor == null || sea == null || !OceanSampler.Ready)
        {
            Debug.LogError("CostProbe: missing pieces");
            yield break;
        }
        Rigidbody rb = motor.GetComponent<Rigidbody>();
        var helm = FindAnyObjectByType<SeaSick.Ship.HelmInput>();
        if (helm != null) helm.enabled = false;

        AddMarker("PlayerLoop", ProfilerCategory.Internal);
        AddMarker("BehaviourUpdate", ProfilerCategory.Scripts);
        AddMarker("LateBehaviourUpdate", ProfilerCategory.Scripts);
        AddMarker("FixedBehaviourUpdate", ProfilerCategory.Scripts);
        AddMarker("Physics.Processing", ProfilerCategory.Physics);
        AddMarker("Physics.Simulate", ProfilerCategory.Physics);
        AddMarker("ParticleSystem.Update", ProfilerCategory.Particles);
        AddMarker("ParticleSystem.EndUpdateAll", ProfilerCategory.Particles);
        AddMarker("Camera.Render", ProfilerCategory.Render);
        AddMarker("Gfx.WaitForPresentOnGfxThread", ProfilerCategory.Render);
        AddMarker("GUI.Repaint", ProfilerCategory.Gui);
        AddMarker("GC.Collect", ProfilerCategory.Memory);
        AddCounter("GC Allocated In Frame", ProfilerCategory.Memory);
        AddCounter("Batches Count", ProfilerCategory.Render);
        AddCounter("SetPass Calls Count", ProfilerCategory.Render);
        AddCounter("Triangles Count", ProfilerCategory.Render);

        // The stage: deep water west, a course along the seas at full throttle.
        Vector3 spot = new Vector3(-1500f, 0f, 0f);
        yield return Leg(motor, rb, sea, spot, 0.15f, 6f, frameCalm, partsCalm, queriesCalm, gpuCalm, true);
        yield return Leg(motor, rb, sea, spot, 1.00f, 10f, frameHeavy, partsHeavy, queriesHeavy, gpuHeavy, false);

        var sb = new StringBuilder();
        sb.AppendLine("CostProbe -- per-frame cost, calm (severity 0.15) vs heavy (1.0), same water, full throttle");
        sb.AppendLine("NOTE: driven from Coplay the editor throttles to ~10 fps; marker ms are honest, frame ms is not.");
        sb.AppendLine(string.Format("{0,-34} {1,26}   {2,26}   {3,7}", "metric", "calm p50 / p95 / max", "heavy p50 / p95 / max", "x p50"));
        sb.AppendLine(Row("frame (ms)", frameCalm, frameHeavy));
        sb.AppendLine(Row("gpu frame (ms, if reported)", gpuCalm, gpuHeavy));
        foreach (var m in metrics)
        {
            if (!m.rec.Valid) { sb.AppendLine(string.Format("{0,-34} (recorder invalid)", m.name)); continue; }
            sb.AppendLine(Row(m.name + (m.isMarker ? " (ms)" : ""), m.calm, m.heavy));
        }
        sb.AppendLine(Row("live particles (all systems)", partsCalm, partsHeavy));
        sb.AppendLine(Row("SampleImmediate calls / frame", queriesCalm, queriesHeavy));

        foreach (var m in metrics) m.rec.Dispose();
        sea.ReleaseForce();
        motor.AutopilotTarget = null;
        if (helm != null) helm.enabled = true;
        System.IO.File.WriteAllText("/tmp/seasick-cost.txt", sb.ToString());
        Debug.Log("CostProbe:\n" + sb);
        Destroy(gameObject);
    }

    IEnumerator Leg(SeaSick.Ship.ShipMotor motor, Rigidbody rb, SeaStateController sea,
                    Vector3 spot, float severity, float settle,
                    List<float> frame, List<float> parts, List<float> queries, List<float> gpu,
                    bool calmLeg)
    {
        sea.ForceSeverity(severity);
        yield return new WaitForSeconds(settle);
        float h0 = OceanSampler.SampleImmediate(spot).height;
        Vector2 w = sea.WindDirection;
        Vector3 course = new Vector3(w.x, 0f, w.y).normalized;
        rb.position = new Vector3(spot.x, h0 + 0.3f, spot.z);
        rb.rotation = Quaternion.LookRotation(course, Vector3.up);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        motor.ThrottleOrder = 1f;
        motor.AutopilotTarget = rb.position + course * 5000f;
        yield return new WaitForSeconds(4f);

        systems = FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
        long lastQueries = OceanSampler.ImmediateCalls;
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < LegSecs)
        {
            yield return null;
            frame.Add(Time.unscaledDeltaTime * 1000f);
            foreach (var m in metrics)
            {
                if (!m.rec.Valid) continue;
                float v = m.isMarker ? m.rec.LastValue / 1e6f : (float)m.rec.LastValue;
                if (calmLeg) m.calm.Add(v); else m.heavy.Add(v);
            }
            int live = 0;
            for (int i = 0; i < systems.Length; i++)
                if (systems[i] != null) live += systems[i].particleCount;
            parts.Add(live);
            long q = OceanSampler.ImmediateCalls;
            queries.Add(q - lastQueries);
            lastQueries = q;

            FrameTimingManager.CaptureFrameTimings();
            uint got = FrameTimingManager.GetLatestTimings(1, timings);
            gpu.Add(got > 0 ? (float)timings[0].gpuFrameTime : 0f);
        }
    }

    static string Row(string name, List<float> a, List<float> b)
    {
        float a50 = Pct(a, 0.5f), a95 = Pct(a, 0.95f), amax = Pct(a, 1f);
        float b50 = Pct(b, 0.5f), b95 = Pct(b, 0.95f), bmax = Pct(b, 1f);
        string ratio = a50 > 1e-6f ? string.Format("{0:F2}", b50 / a50) : "-";
        return string.Format("{0,-34} {1,8:F2} {2,8:F2} {3,8:F2}   {4,8:F2} {5,8:F2} {6,8:F2}   {7,7}",
            name, a50, a95, amax, b50, b95, bmax, ratio);
    }

    static float Pct(List<float> v, float p)
    {
        if (v.Count == 0) return 0f;
        var s = new List<float>(v);
        s.Sort();
        int i = Mathf.Clamp(Mathf.RoundToInt((s.Count - 1) * p), 0, s.Count - 1);
        return s[i];
    }
}
