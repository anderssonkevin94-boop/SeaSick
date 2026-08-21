using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;

/// Buoyancy acceptance on the lab proxy sloop:
/// (a) 60 s free-float in a storm: no capsize (roll under 45 deg), rails under
///     green water only a small fraction of the time;
/// (b) sea forced calm, hull dropped from 2 m: she must catch the drop without
///     pogoing.
///
/// (b) used to be scored as "|vy| under 0.12 m/s for 0.5 s". That is a knife
/// edge on a live sea — the water's own orbital motion is the same order as
/// the threshold — and the SAME build measured 1.04 s, 2.49 s, 5.93 s and
/// never-settled across runs. It failed this build once and sent a session
/// hunting a regression that did not exist. It now measures, over three drops
/// from PINNED wave phases, two statistics that hold still:
///   peakOvershoot — how far past her final draft the drop drives her, and
///   lateRms       — draft wobble after t=3 s, i.e. is she still ringing.
/// Observed on a healthy build: overshoot 1.95-2.16 m (very repeatable, mean
/// 2.02-2.04), lateRms per-drop 0.03-0.22 m for a 3-drop mean of 0.07-0.14.
/// The gates sit clear of that spread on purpose — the failure this replaces
/// was a gate reading noise, and a loose gate beats a flapping one. A real
/// pogo (the symmetric-reserve-lift regression) rings far harder than 0.3.
/// Both are computed on DRAFT (water minus hull), which subtracts the sea's
/// motion. Settle times are still printed, but they are informational only.
/// Run in play mode in OceanLab. Writes /tmp/seasick-buoy.txt.
/// NOTE deliberately plain C# throughout: the Coplay script compiler chokes on
/// this file's earlier, more idiomatic form and cannot report why.
public class BuoyProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("BuoyProbe: not in play mode");
            return;
        }
        new GameObject("BuoyProbe").AddComponent<BuoyProbe>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        OceanRenderer ocean = OceanRenderer.Instance;
        BuoyantBody body = FindAnyObjectByType<BuoyantBody>();
        if (ocean == null || body == null)
        {
            Debug.LogError("BuoyProbe: missing pieces");
            yield break;
        }
        Rigidbody rb = body.GetComponent<Rigidbody>();

        OceanSpectrumSettings storm = ScriptableObject.CreateInstance<OceanSpectrumSettings>();
        storm.windSpeed = 22f;
        storm.fetchKm = 200f;
        storm.choppiness = 1f;
        ocean.SetSettings(storm);
        yield return new WaitForSeconds(4f);

        // Reseat the hull on the new sea: measure the ride, not the spawn.
        OceanSample seat = OceanSampler.SampleImmediate(Vector3.zero);
        rb.position = new Vector3(0f, seat.height + 0.3f, 0f);
        rb.rotation = Quaternion.identity;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        yield return new WaitForSeconds(2f);

        float maxRoll = 0f;
        float maxPitch = 0f;
        float railUnderTime = 0f;
        float totalTime = 0f;
        float draftSum = 0f;
        float draftSqSum = 0f;
        int draftN = 0;
        bool capsized = false;
        BuoyancyProbeSet.Probe[] probes = body.Probes.Probes;
        float t0 = Time.time;

        while (Time.time - t0 < 60f)
        {
            yield return new WaitForFixedUpdate();
            float roll = Mathf.Abs(Signed(body.transform.eulerAngles.z));
            float pitch = Mathf.Abs(Signed(body.transform.eulerAngles.x));
            if (roll > maxRoll) maxRoll = roll;
            if (pitch > maxPitch) maxPitch = pitch;
            if (roll > 80f) capsized = true;

            float dt = Time.fixedDeltaTime;
            totalTime += dt;
            OceanSample centre = OceanSampler.SampleImmediate(body.transform.position);
            float draft = centre.height - body.transform.position.y;
            draftSum += draft;
            draftSqSum += draft * draft;
            draftN++;

            for (int i = 6; i <= 9 && i < probes.Length; i++)
            {
                Vector3 w = body.transform.TransformPoint(probes[i].localPosition);
                OceanSample rs = OceanSampler.SampleImmediate(w);
                if (rs.height - w.y > 0.25f)
                {
                    railUnderTime += dt;
                    break;
                }
            }
        }

        float railPct = railUnderTime / Mathf.Max(totalTime, 0.01f) * 100f;
        float draftMean = draftSum / draftN;
        float draftVar = draftSqSum / draftN - draftMean * draftMean;
        float draftRms = Mathf.Sqrt(Mathf.Max(0f, draftVar));
        sb.AppendLine(string.Format(
            "storm 60s: maxRoll={0:F1}deg maxPitch={1:F1}deg railUnder {2:F1}% draftMean={3:F2}m draftRms={4:F2}m capsized={5}",
            maxRoll, maxPitch, railPct, draftMean, draftRms, capsized));

        OceanSpectrumSettings calm = ScriptableObject.CreateInstance<OceanSpectrumSettings>();
        calm.windSpeed = 5f;
        calm.fetchKm = 50f;
        calm.choppiness = 0.8f;
        ocean.SetSettings(calm);
        yield return new WaitForSeconds(6f);

        float overshootSum = 0f;
        float lateRmsSum = 0f;
        for (int k = 0; k < 3; k++)
        {
            yield return Drop(rb, body, 500.0 + k * 37.0, k, sb);
            overshootSum += lastOvershoot;
            lateRmsSum += lastLateRms;
        }
        float meanOvershoot = overshootSum / 3f;
        float meanLateRms = lateRmsSum / 3f;
        sb.AppendLine(string.Format(
            "calm drop 2m x3: meanOvershoot={0:F2}m (gate <2.80)  meanLateRms={1:F3}m (gate <0.300)",
            meanOvershoot, meanLateRms));

        bool pass = !capsized && maxRoll < 45f && railPct < 12f
                    && meanOvershoot < 2.8f && meanLateRms < 0.3f;
        if (pass) sb.AppendLine("PASS");
        else sb.AppendLine("FAIL");
        System.IO.File.WriteAllText("/tmp/seasick-buoy.txt", sb.ToString());
        Debug.Log("BuoyProbe:\n" + sb);
        Destroy(gameObject);
    }

    float lastOvershoot;
    float lastLateRms;

    /// One drop from a pinned wave phase, scored on draft.
    IEnumerator Drop(Rigidbody rb, BuoyantBody body, double phase, int k, StringBuilder sb)
    {
        OceanTime.Scrub(phase);
        yield return new WaitForSeconds(1f);
        OceanSample seat = OceanSampler.SampleImmediate(Vector3.zero);
        rb.position = new Vector3(0f, seat.height + 0.3f, 0f);
        rb.rotation = Quaternion.identity;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        yield return new WaitForSeconds(4f);      // let her find her waterline

        OceanTime.Scrub(phase);                   // identical water every build
        yield return new WaitForSeconds(0.5f);
        rb.position += Vector3.up * 2f;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        float[] draft = new float[600];           // 12 s at 50 Hz
        int n = 0;
        float t0 = Time.time;
        while (Time.time - t0 < 12f && n < draft.Length)
        {
            yield return new WaitForFixedUpdate();
            draft[n] = body.MeanWaterHeight - rb.position.y;
            n++;
        }

        int tailFrom = Mathf.Max(0, n - 100);     // final value = last 2 s
        float tail = 0f;
        for (int i = tailFrom; i < n; i++) tail += draft[i];
        float finalDraft = tail / Mathf.Max(1, n - tailFrom);

        float peak = 0f;
        int lastOut = -1;
        for (int i = 0; i < n; i++)
        {
            float d = draft[i] - finalDraft;
            if (d < 0f) d = -d;
            if (d > peak) peak = d;
            if (d > 0.15f) lastOut = i;
        }
        float settle = lastOut < 0 ? 0f : (lastOut + 1) * Time.fixedDeltaTime;
        if (lastOut >= 0 && (n - lastOut) * Time.fixedDeltaTime < 1f) settle = -1f;

        int lateFrom = Mathf.Min(n, 150);         // t > 3 s
        float sq = 0f;
        int lateN = 0;
        for (int i = lateFrom; i < n; i++)
        {
            float d = draft[i] - finalDraft;
            sq += d * d;
            lateN++;
        }
        lastLateRms = Mathf.Sqrt(sq / Mathf.Max(1, lateN));
        lastOvershoot = peak;

        sb.AppendLine(string.Format(
            "  drop {0} (phase {1:F0}): finalDraft={2:F2}m overshoot={3:F2}m lateRms={4:F3}m (settle {5:F2}s, informational)",
            k, phase, finalDraft, peak, lastLateRms, settle));
    }

    static float Signed(float a)
    {
        if (a > 180f) return a - 360f;
        return a;
    }
}
