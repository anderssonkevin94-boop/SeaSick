using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;

/// "Why does she go from 30 m/s to 5 over a SMALL wave, mostly heading west?"
///
/// Sails due west from the shelf into the deep at whatever sea the region
/// field actually serves (no forced severity — the point is to reproduce
/// normal play) and decomposes every retarding force each step, so the drop
/// can be attributed instead of guessed:
///
///   sail   — the propulsion servo's whole budget, acceleration x mass
///   base   — BuoyantBody forward drag (linear + QUADRATIC in relative speed)
///   plow   — the reserve-depth slam term
///   surf   — gravity along the surface slope, which RETARDS while climbing
///            and is clamped at 4.5 m/s^2 regardless of how big the wave is
///
/// Plain C# for Coplay. Run in play mode in Sea.unity.
/// Writes /tmp/seasick-westtrace.txt.
public class WestTrace : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("WestTrace: not in play mode");
            return;
        }
        WestTrace old = FindAnyObjectByType<WestTrace>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("WestTrace").AddComponent<WestTrace>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        if (motor == null || !OceanSampler.Ready)
        {
            Debug.LogError("WestTrace: missing pieces");
            yield break;
        }
        Rigidbody rb = motor.GetComponent<Rigidbody>();
        BuoyantBody body = motor.GetComponent<BuoyantBody>();
        var helm = FindAnyObjectByType<SeaSick.Ship.HelmInput>();
        if (helm != null) helm.enabled = false;

        float mass = rb.mass;
        float sailKN = motor.Acceleration * mass / 1000f;
        sb.AppendLine(string.Format(
            "maxSpeed={0:F0} m/s   acceleration={1:F2} m/s^2   sail budget={2:F1} kN   mass={3:F0} kg",
            motor.MaxSpeed, motor.Acceleration, sailKN, mass));
        sb.AppendLine("");

        Vector3 spot = new Vector3(-500f, 0f, 0f);
        yield return new WaitForSeconds(4f);
        float h0 = OceanSampler.SampleImmediate(spot).height;
        rb.position = new Vector3(spot.x, h0 + 0.5f, spot.z);
        rb.rotation = Quaternion.Euler(0f, 270f, 0f);   // due west
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        motor.ThrottleOrder = 1f;
        motor.AutopilotTarget = rb.position + Vector3.left * 9000f;
        yield return new WaitForSeconds(12f);           // work up to speed

        sb.AppendLine("t     speed  target  surf_kN  base_kN  plow_kN  totalBrake  sail_kN  sev  reserve");
        float t0 = Time.time;
        float next = 0f;
        float prevSpeed = motor.CurrentSpeed;
        float worstLoss = 0f;
        float worstSurf = 0f, worstBase = 0f, worstPlow = 0f;
        float lossAtSurf = 0f, lossAtBase = 0f, lossAtPlow = 0f;

        // The metric that matches the design goal: hitting a wave should cost
        // SOME speed and leave her sailing. So measure, per wave event, what
        // fraction of her speed it took and how long she needed to get it back.
        int events = 0;
        float lossPctSum = 0f, worstLossPct = 0f;
        float recoverSum = 0f;
        int recoverN = 0;
        bool inEvent = false;
        float peakBefore = 0f, troughSpeed = 0f, eventStart = 0f;

        while (Time.time - t0 < 70f)
        {
            yield return new WaitForFixedUpdate();
            float t = Time.time - t0;

            Vector3 fwd = motor.transform.forward;
            fwd.y = 0f;
            fwd = fwd.normalized;

            float surfKN = -motor.SurfAccel * mass / 1000f;      // +ve = retarding
            float baseKN = -Vector3.Dot(body.DebugDragForward, fwd) / 1000f;
            float plowKN = -Vector3.Dot(body.DebugPlowForward, fwd) / 1000f;

            // Speed lost in this one physics step, scaled to per-second.
            float dv = (prevSpeed - motor.CurrentSpeed) / Time.fixedDeltaTime;
            prevSpeed = motor.CurrentSpeed;
            if (dv > worstLoss)
            {
                worstLoss = dv;
                lossAtSurf = surfKN;
                lossAtBase = baseKN;
                lossAtPlow = plowKN;
            }
            if (surfKN > worstSurf) worstSurf = surfKN;
            if (baseKN > worstBase) worstBase = baseKN;
            if (plowKN > worstPlow) worstPlow = plowKN;

            // Event detection: a fall of 12%+ from a running peak.
            float sp = motor.CurrentSpeed;
            if (!inEvent)
            {
                if (sp > peakBefore) peakBefore = sp;
                if (peakBefore > 8f && sp < peakBefore * 0.88f)
                {
                    inEvent = true;
                    events++;
                    troughSpeed = sp;
                    eventStart = Time.time;
                }
            }
            else
            {
                if (sp < troughSpeed) troughSpeed = sp;
                bool recovered = sp >= peakBefore * 0.9f;
                bool timedOut = Time.time - eventStart > 15f;
                if (recovered || timedOut)
                {
                    float pct = (peakBefore - troughSpeed) / Mathf.Max(0.01f, peakBefore) * 100f;
                    lossPctSum += pct;
                    if (pct > worstLossPct) worstLossPct = pct;
                    if (recovered)
                    {
                        recoverSum += Time.time - eventStart;
                        recoverN++;
                    }
                    inEvent = false;
                    peakBefore = sp;
                }
            }

            if (t < next) continue;
            next += 1f;
            sb.AppendLine(string.Format(
                "{0,4:F0} {1,6:F1} {2,7:F1} {3,8:F1} {4,8:F1} {5,8:F1} {6,11:F1} {7,8:F1} {8,4:F2} {9,7:F2}",
                t, motor.CurrentSpeed, motor.MaxSpeed * motor.SeaResistance01,
                surfKN, baseKN, plowKN, surfKN + baseKN + plowKN, sailKN,
                motor.SeaSeverity01, body.DebugMaxReserve));
        }

        sb.AppendLine("");
        sb.AppendLine(string.Format(
            "peak retarding force seen:  surf {0:F1} kN   base drag {1:F1} kN   plow {2:F1} kN   (sail can only ever push {3:F1} kN)",
            worstSurf, worstBase, worstPlow, sailKN));
        sb.AppendLine(string.Format(
            "worst single-step loss: {0:F1} m/s per second, with surf={1:F1} base={2:F1} plow={3:F1} kN",
            worstLoss, lossAtSurf, lossAtBase, lossAtPlow));
        sb.AppendLine("");
        sb.AppendLine(string.Format(
            "WAVE EVENTS ({0} in 70 s): mean cost {1:F0}% of speed, worst {2:F0}%   mean recovery {3:F1} s ({4} of {0} recovered)",
            events, lossPctSum / Mathf.Max(1, events), worstLossPct,
            recoverSum / Mathf.Max(1, recoverN), recoverN));
        sb.AppendLine("goal: a wave costs SOME speed and she keeps sailing — target well under 40%, recovery a few seconds.");

        motor.AutopilotTarget = null;
        if (helm != null) helm.enabled = true;
        System.IO.File.WriteAllText("/tmp/seasick-westtrace.txt", sb.ToString());
        Debug.Log("WestTrace written");
        Destroy(gameObject);
    }
}
