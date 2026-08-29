using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;

/// Deck-burial acceptance: sail the real ship hard into head seas at forced
/// severity 1.0 (mountainous) for 60 s and measure how often, and how deep,
/// the poop deck goes under the sampled surface. The design rule is that the
/// deck never submerges unless she is sinking, so the gate is deckUnder ~ 0%.
/// Run in play mode in Sea.unity. Plain C# for Coplay.
/// Writes /tmp/seasick-bury.txt.
public class BuryProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("BuryProbe: not in play mode");
            return;
        }
        BuryProbe old = FindAnyObjectByType<BuryProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("BuryProbe").AddComponent<BuryProbe>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        if (motor == null || SeaStateController.Instance == null || !OceanSampler.Ready)
        {
            Debug.LogError("BuryProbe: missing pieces");
            yield break;
        }
        Rigidbody rb = motor.GetComponent<Rigidbody>();
        var helm = FindAnyObjectByType<SeaSick.Ship.HelmInput>();
        if (helm != null) helm.enabled = false;

        // Deep water, full storm, let the spectrum blend land before seating.
        // Wave phase pinned by scrubbing to a fixed ocean time: without it,
        // run-to-run rogue-crest luck swamps the A/B signal (the documented
        // trap). The surface is a pure function of (settings, seed, time),
        // so every run of this probe sails the same water.
        SeaStateController.Instance.ForceSeverity(1f);
        Vector3 spot = new Vector3(-1500f, 0f, 0f);
        yield return new WaitForSeconds(8f);
        OceanTime.Scrub(500.0);
        yield return new WaitForSeconds(1f);   // let the readback pipeline catch up
        float h0 = OceanSampler.SampleImmediate(spot).height;
        rb.position = new Vector3(spot.x, h0 + 0.5f, spot.z);
        rb.rotation = Quaternion.identity;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Full sail, dead into the seas — the burial case from the field.
        Vector2 w = SeaStateController.Instance.WindDirection;
        Vector3 seasFrom = new Vector3(-w.x, 0f, -w.y).normalized;
        motor.ThrottleOrder = 1f;
        motor.AutopilotTarget = rb.position + seasFrom * 5000f;
        yield return new WaitForSeconds(8f);

        float total = 0f;
        float deckUnderTime = 0f;
        float deckMax = -99f;
        float railUnderTime = 0f;
        float draftSum = 0f, draftSqSum = 0f;
        int draftN = 0;
        float speedSum = 0f;
        int speedN = 0;
        Vector3 poopLocal = new Vector3(0f, 1.3f, -7.0f);
        Vector3 bowLocal = new Vector3(0f, 1.3f, 7.0f);
        var body = motor.GetComponent<BuoyantBody>();
        float t0 = Time.time;

        while (Time.time - t0 < 60f)
        {
            yield return new WaitForFixedUpdate();
            float dt = Time.fixedDeltaTime;
            total += dt;

            Vector3 poop = motor.transform.TransformPoint(poopLocal);
            Vector3 bow = motor.transform.TransformPoint(bowLocal);
            float overPoop = OceanSampler.SampleImmediate(poop).height - poop.y;
            float overBow = OceanSampler.SampleImmediate(bow).height - bow.y;
            float worst = Mathf.Max(overPoop, overBow);
            if (worst > deckMax) deckMax = worst;
            if (overPoop > 0.05f) deckUnderTime += dt;
            if (body != null && body.MaxRailImmersion > 0.25f) railUnderTime += dt;

            Vector3 hp = motor.transform.position;
            float draft = OceanSampler.SampleImmediate(hp).height - hp.y;
            draftSum += draft;
            draftSqSum += draft * draft;
            draftN++;
            speedSum += motor.CurrentSpeed;
            speedN++;
        }

        float draftMean = draftSum / Mathf.Max(1, draftN);
        float draftVar = draftSqSum / Mathf.Max(1, draftN) - draftMean * draftMean;
        sb.AppendLine(string.Format(
            "head seas 60s @severity 1.0: poopDeckUnder {0:F1}%  deckOverMax={1:F2}m  railUnder {2:F1}%",
            deckUnderTime / Mathf.Max(total, 0.01f) * 100f, deckMax,
            railUnderTime / Mathf.Max(total, 0.01f) * 100f));
        sb.AppendLine(string.Format(
            "draftMean={0:F2}m draftRms={1:F2}m meanSpeed={2:F1}m/s",
            draftMean, Mathf.Sqrt(Mathf.Max(0f, draftVar)),
            speedSum / Mathf.Max(1, speedN)));

        SeaStateController.Instance.ReleaseForce();
        motor.AutopilotTarget = null;
        if (helm != null) helm.enabled = true;
        System.IO.File.WriteAllText("/tmp/seasick-bury.txt", sb.ToString());
        Debug.Log("BuryProbe:\n" + sb);
        Destroy(gameObject);
    }
}
