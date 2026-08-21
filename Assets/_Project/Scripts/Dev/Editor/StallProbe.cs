using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;

/// Speed-stall acceptance: "she hard-stops while sailing". Sails the real ship
/// into head seas at a pinned wave phase and counts how often forward way
/// collapses, how deep, how long recovery takes, and how much of the brake is
/// plow drag — with the plow term ON and OFF over identical water, which is
/// the attribution the first version of this probe could not make.
///
/// The number that matters is peak plow decel against the sail's authority
/// (ShipMotor.acceleration x mass = 10.4 kN): propulsion is a rate-limited
/// servo, so any brake bigger than that wins outright and recovery is capped
/// at 2.6 m/s^2 however hard the water hit her.
///
/// Every leg starts from a full reset (hull repaired, cargo and bilge zeroed,
/// attitude and velocity cleared, wave phase scrubbed to the same instant) so
/// legs cannot contaminate each other — the first version's later legs read
/// 1 m/s in a sea milder than one BuryProbe sailed at 7.7 m/s, which is how
/// that contamination announced itself.
/// Run in play mode in Sea.unity. Plain C# for Coplay.
/// Writes /tmp/seasick-stall.txt.
public class StallProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("StallProbe: not in play mode");
            return;
        }
        StallProbe old = FindAnyObjectByType<StallProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("StallProbe").AddComponent<StallProbe>();
    }

    SeaSick.Ship.ShipMotor motor;
    SeaSick.Ship.HullIntegrity hull;
    Rigidbody rb;
    BuoyantBody body;
    StringBuilder sb;
    float plowDefault;

    IEnumerator Start()
    {
        sb = new StringBuilder();
        motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        if (motor == null || SeaStateController.Instance == null || !OceanSampler.Ready)
        {
            Debug.LogError("StallProbe: missing pieces");
            yield break;
        }
        rb = motor.GetComponent<Rigidbody>();
        body = motor.GetComponent<BuoyantBody>();
        hull = motor.GetComponent<SeaSick.Ship.HullIntegrity>();
        plowDefault = body.PlowDragFactor;
        var helm = FindAnyObjectByType<SeaSick.Ship.HelmInput>();
        if (helm != null) helm.enabled = false;

        sb.AppendLine(string.Format(
            "mass={0:F0}kg   sail authority={1:F1}kN ({2:F2} m/s^2)   plowDragFactor default={3:F3}",
            rb.mass, 2.6f * rb.mass / 1000f, 2.6f, plowDefault));
        sb.AppendLine("");

        yield return Float("calm float, sails furled", 0.05f);
        yield return Leg("lively  plow ON ", 0.40f, plowDefault);
        yield return Leg("lively  plow OFF", 0.40f, 0f);
        yield return Leg("heavy   plow ON ", 0.75f, plowDefault);
        yield return Leg("heavy   plow OFF", 0.75f, 0f);

        body.PlowDragFactor = plowDefault;
        SeaStateController.Instance.ReleaseForce();
        motor.AutopilotTarget = null;
        motor.CargoLoad = 0f;
        if (helm != null) helm.enabled = true;
        System.IO.File.WriteAllText("/tmp/seasick-stall.txt", sb.ToString());
        Debug.Log("StallProbe:\n" + sb);
        Destroy(gameObject);
    }

    /// Put her back to a known state on identical water before every leg.
    IEnumerator Reset(float severity)
    {
        SeaStateController.Instance.ReleaseForce();
        SeaStateController.Instance.ForceSeverity(severity);
        motor.AutopilotTarget = null;
        motor.CargoLoad = 0f;
        motor.BilgeLoad01 = 0f;
        motor.Anchored = false;
        motor.Rowing = false;
        motor.Rudder = 0f;
        if (hull != null) hull.FullRepair();

        Vector3 spot = new Vector3(-1500f, 0f, 0f);
        yield return new WaitForSeconds(6f);      // spectrum blend lands
        OceanTime.Scrub(500.0);
        yield return new WaitForSeconds(1f);      // readback pipeline catches up
        float h0 = OceanSampler.SampleImmediate(spot).height;
        rb.position = new Vector3(spot.x, h0 + 0.5f, spot.z);
        rb.rotation = Quaternion.identity;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    /// Static float: does the reserve/plow term really do nothing at rest?
    IEnumerator Float(string label, float severity)
    {
        body.PlowDragFactor = plowDefault;
        yield return Reset(severity);
        motor.SailOrder = 0f;
        yield return new WaitForSeconds(8f);      // let her settle

        float reserveSum = 0f, reserveMax = 0f, plowSum = 0f, plowMax = 0f;
        float overTime = 0f, total = 0f;
        int n = 0;
        float t0 = Time.time;
        while (Time.time - t0 < 20f)
        {
            yield return new WaitForFixedUpdate();
            total += Time.fixedDeltaTime;
            float r = body.DebugMaxReserve;
            float plow = body.DebugPlowForward.magnitude;
            reserveSum += r;
            plowSum += plow;
            if (r > reserveMax) reserveMax = r;
            if (plow > plowMax) plowMax = plow;
            if (r > 0.001f) overTime += Time.fixedDeltaTime;
            n++;
        }
        sb.AppendLine(string.Format("--- {0}  (severity {1:F2}) ---", label, severity));
        sb.AppendLine(string.Format(
            "  reserve: mean={0:F3} max={1:F3}   ANY reserve on {2:F0}% of steps",
            reserveSum / Mathf.Max(1, n), reserveMax,
            overTime / Mathf.Max(total, 0.01f) * 100f));
        sb.AppendLine(string.Format(
            "  plow drag: mean={0:F2} kN  max={1:F2} kN   (design intent: zero at rest)",
            plowSum / Mathf.Max(1, n) / 1000f, plowMax / 1000f));
        sb.AppendLine("");
        motor.SailOrder = 1f;
    }

    IEnumerator Leg(string label, float severity, float plow)
    {
        body.PlowDragFactor = plow;
        yield return Reset(severity);

        // Dead into the seas: the case the player reports as a hard stop.
        Vector2 w = SeaStateController.Instance.WindDirection;
        Vector3 seasFrom = new Vector3(-w.x, 0f, -w.y).normalized;
        Vector3 startPos = rb.position;
        motor.SailOrder = 1f;
        motor.AutopilotTarget = startPos + seasFrom * 8000f;
        yield return new WaitForSeconds(10f);     // work up to speed

        const int Win = 50;                       // 1.0 s of trailing way @50Hz
        float[] ring = new float[Win];
        int ringHead = 0, ringFilled = 0;

        float total = 0f, waySum = 0f, targetSum = 0f;
        int n = 0;
        float slowTime = 0f;
        int stalls = 0;
        float worstDrop = 0f, dropSum = 0f;
        float peakPlow = 0f, plowSum = 0f, peakReserve = 0f, reserveSum = 0f;
        float peakBrake = 0f;
        float maxRoll = 0f, maxPitch = 0f, subSum = 0f;
        bool inStall = false;
        float stallStart = 0f, recoverySum = 0f;
        int recoveryN = 0;
        Vector3 measureStart = rb.position;
        float t0 = Time.time;

        while (Time.time - t0 < 40f)
        {
            yield return new WaitForFixedUpdate();
            float dt = Time.fixedDeltaTime;
            total += dt;

            Vector3 fwd = motor.transform.forward;
            fwd.y = 0f;
            fwd = fwd.normalized;
            Vector3 through = rb.linearVelocity - motor.WaterVelocity;
            float way = Vector3.Dot(through, fwd);
            float target = motor.MaxSpeed * motor.SeaResistance01;

            waySum += way;
            targetSum += target;
            n++;
            if (way < 0.6f * target) slowTime += dt;

            float plowN = -Vector3.Dot(body.DebugPlowForward, fwd);
            float brakeN = -Vector3.Dot(body.DebugDragForward, fwd);
            plowSum += plowN;
            reserveSum += body.DebugMaxReserve;
            if (plowN > peakPlow) peakPlow = plowN;
            if (brakeN > peakBrake) peakBrake = brakeN;
            if (body.DebugMaxReserve > peakReserve) peakReserve = body.DebugMaxReserve;

            // Sanity: a capsized or pinned hull reads as "slow" too, and that
            // is a different bug. Roll/pitch/submersion say which one this is.
            float roll = Mathf.Abs(Mathf.DeltaAngle(0f, motor.transform.eulerAngles.z));
            float pitch = Mathf.Abs(Mathf.DeltaAngle(0f, motor.transform.eulerAngles.x));
            if (roll > maxRoll) maxRoll = roll;
            if (pitch > maxPitch) maxPitch = pitch;
            subSum += body.Submersion;

            float recentMax = way;
            for (int i = 0; i < ringFilled; i++)
                if (ring[i] > recentMax) recentMax = ring[i];
            float drop = recentMax - way;

            if (!inStall && drop >= 3f)
            {
                inStall = true;
                stalls++;
                stallStart = Time.time;
                dropSum += drop;
                if (drop > worstDrop) worstDrop = drop;
            }
            else if (inStall)
            {
                if (drop > worstDrop) worstDrop = drop;
                if (way >= 0.9f * target)
                {
                    inStall = false;
                    recoverySum += Time.time - stallStart;
                    recoveryN++;
                }
                else if (Time.time - stallStart > 6f) inStall = false;
            }

            ring[ringHead] = way;
            ringHead = (ringHead + 1) % Win;
            if (ringFilled < Win) ringFilled++;
        }

        float meanWay = waySum / Mathf.Max(1, n);
        float meanTarget = targetSum / Mathf.Max(1, n);
        Vector3 made = rb.position - measureStart;
        made.y = 0f;
        float speedMul = hull != null ? hull.SpeedMultiplier : 1f;

        sb.AppendLine(string.Format("--- {0}  (severity {1:F2}, plowDragFactor {2:F3}) ---",
            label, severity, plow));
        sb.AppendLine(string.Format(
            "  meanWay={0:F1} m/s of target {1:F1} ({2:F0}%)   below 60% of target: {3:F1}% of run   madeGood={4:F0} m",
            meanWay, meanTarget, meanWay / Mathf.Max(0.01f, meanTarget) * 100f,
            slowTime / Mathf.Max(total, 0.01f) * 100f, made.magnitude));
        sb.AppendLine(string.Format(
            "  stalls (>=3 m/s lost inside 1 s): {0} in 40 s = {1:F1}/min   worstDrop={2:F1} m/s   meanDrop={3:F1} m/s",
            stalls, stalls / 40f * 60f, worstDrop, dropSum / Mathf.Max(1, stalls)));
        sb.AppendLine(string.Format(
            "  recovery to 90% of target: {0:F1} s mean ({1} of {2} recovered)",
            recoverySum / Mathf.Max(1, recoveryN), recoveryN, stalls));
        sb.AppendLine(string.Format(
            "  plow: mean={0:F1} kN  peak={1:F1} kN ({2:F1} m/s^2, {3:F1}x sail)   totalFwdBrake peak={4:F1} kN",
            plowSum / Mathf.Max(1, n) / 1000f, peakPlow / 1000f, peakPlow / rb.mass,
            peakPlow / (2.6f * rb.mass), peakBrake / 1000f));
        sb.AppendLine(string.Format(
            "  reserve: mean={0:F2} peak={1:F2}   attitude: maxRoll={2:F0} deg maxPitch={3:F0} deg   meanSub={4:F2}   hullSpeedMul={5:F2}",
            reserveSum / Mathf.Max(1, n), peakReserve, maxRoll, maxPitch,
            subSum / Mathf.Max(1, n), speedMul));
        sb.AppendLine("");
    }
}
