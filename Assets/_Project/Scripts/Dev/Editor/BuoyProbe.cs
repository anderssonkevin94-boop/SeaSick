using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;

/// Buoyancy acceptance on the lab proxy sloop:
/// (a) 60 s free-float in a storm: no capsize (roll under 45 deg), rails under
///     green water only a small fraction of the time;
/// (b) sea forced calm, hull dropped from 2 m: settles within ~4 s.
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

        rb.position += Vector3.up * 2f;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        float dropT = Time.time;
        float settled = -1f;
        float quiet = 0f;
        while (Time.time - dropT < 12f)
        {
            yield return new WaitForFixedUpdate();
            if (Mathf.Abs(rb.linearVelocity.y) < 0.12f) quiet += Time.fixedDeltaTime;
            else quiet = 0f;
            if (quiet >= 0.5f)
            {
                settled = Time.time - dropT - 0.5f;
                break;
            }
        }
        sb.AppendLine(string.Format("calm drop 2m: settled in {0:F2}s", settled));

        bool pass = !capsized && maxRoll < 45f && railPct < 12f && settled > 0f && settled < 4.5f;
        if (pass) sb.AppendLine("PASS");
        else sb.AppendLine("FAIL");
        System.IO.File.WriteAllText("/tmp/seasick-buoy.txt", sb.ToString());
        Debug.Log("BuoyProbe:\n" + sb);
        Destroy(gameObject);
    }

    static float Signed(float a)
    {
        if (a > 180f) return a - 360f;
        return a;
    }
}
