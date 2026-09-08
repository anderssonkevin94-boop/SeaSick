using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;

/// Does the sea SWALLOW her, at a severity the player actually sails?
///
/// BuryProbe's storm gate answers a different question and cannot answer this
/// one: at severity 1.0 a 15 m hull's response is chaotic and the gate stops
/// being an A/B -- measured 2026-09-08, identical physics returned
/// poopDeckUnder 4.7% and 10.6% (deckOverMax 20.6 m and 43.4 m) in two
/// control runs. The complaint this probe exists for is a GAMEPLAY one:
/// small hulls visibly going under and re-emerging in ordinary seas. So it
/// runs at severity 0.60, and it reports in units of HER OWN FREEBOARD,
/// because "swallowed" is a fraction of the hull, not a number of metres.
///
///   awash    -- deepest rail probe under by more than 0.3 x freeboard
///               (green water over the deck edge; dramatic, acceptable)
///   swallowed - deepest rail probe under by more than 1.0 x freeboard
///               (the hull is visually inside the wave; the complaint)
///
/// SEGMENTED on purpose: one continuous 60 s run diverges chaotically from
/// the pinned start (60 s at 14 m/s is 870 m of different water for a
/// millimetre of early difference), so two legs stop being comparable. Six
/// segments of 15 s, each re-warped to the same spot with the phase
/// re-pinned to the same instants, keep every leg sailing the same six
/// stretches of the same water.
///
/// Also prints WHY she is where she is: forward way against the motor's own
/// target, the sea-resistance factor, and the plow / dynamic-lift terms off
/// the body's debug surface -- because the last change was aimed at a brake
/// that turned out not to be firing, and no one had measured it.
public class SwallowProbe : MonoBehaviour
{
    public static void ExecuteNew4() { Begin(4, false); }
    public static void ExecuteOld4() { Begin(4, true); }
    public static void ExecuteNew12() { Begin(12, false); }
    public static void ExecuteOld12() { Begin(12, true); }
    public static void Execute() { Begin(4, false); }

    const float Severity = 0.60f;
    const int Segments = 6;
    const float SettleSecs = 6f;
    const float MeasureSecs = 15f;

    static int rungWanted;
    static bool restoreOld;

    static void Begin(int rung, bool old)
    {
        if (!Application.isPlaying) { Debug.LogError("SwallowProbe: not in play mode"); return; }
        SwallowProbe prev = FindAnyObjectByType<SwallowProbe>();
        if (prev != null) Destroy(prev.gameObject);
        rungWanted = rung;
        restoreOld = old;
        new GameObject("SwallowProbe").AddComponent<SwallowProbe>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        if (motor == null || SeaStateController.Instance == null || !OceanSampler.Ready)
        {
            Debug.LogError("SwallowProbe: missing pieces");
            yield break;
        }
        var yard = motor.GetComponent<SeaSick.Ship.Shipyard>();
        if (yard == null) { Debug.LogError("SwallowProbe: no Shipyard"); yield break; }
        yard.Apply(rungWanted);
        var node = yard.Node;
        var body = motor.GetComponent<BuoyantBody>();
        Rigidbody rb = motor.GetComponent<Rigidbody>();
        if (node == null || body == null) { Debug.LogError("SwallowProbe: no node/body"); yield break; }
        var helm = FindAnyObjectByType<SeaSick.Ship.HelmInput>();
        if (helm != null) helm.enabled = false;

        float fb = node.RailY;
        if (restoreOld)
        {
            SetField(body, "burialDepth", 1.2f);
            SetField(body, "reservePerMetre", 1.5f);
            SetField(body, "plowSpeedFloor", 4.5f);
            SetField(body, "dynamicLiftDepth", 1.0f);
            SetField(body, "plowOnset", 0.6f);
            SetField(body, "maxPlowDecel", 4.0f);
        }
        sb.AppendLine(string.Format(
            "rung {0} ({1})  freeboard {2:F2} m  severity {3:F2}  constants: {4}",
            node.node, node.label, fb, Severity, restoreOld ? "OLD" : "NEW"));
        sb.AppendLine(string.Format(
            "burialDepth {0:F2}  reservePerMetre {1:F2}  plowSpeedFloor {2:F2}  maxPlowDecel {3:F2}  motor.MaxSpeed {4:F1}",
            GetField(body, "burialDepth"), GetField(body, "reservePerMetre"),
            GetField(body, "plowSpeedFloor"), GetField(body, "maxPlowDecel"),
            motor.MaxSpeed));

        SeaStateController.Instance.ForceSeverity(Severity);
        Vector3 spot = new Vector3(-1500f, 0f, 0f);
        yield return new WaitForSeconds(8f);

        float total = 0f, awash = 0f, swallowed = 0f, buried = 0f;
        float railMax = -99f;
        float waySum = 0f, resistSum = 0f, plowSum = 0f, liftSum = 0f;
        float plowActive = 0f;
        float draftSum = 0f, draftSqSum = 0f;
        int n = 0;
        float mass = rb.mass;

        for (int seg = 0; seg < Segments; seg++)
        {
            // Re-pin: same instants every run, 60 s of sea apart so the six
            // segments sample different water.
            OceanTime.Scrub(500.0 + seg * 60.0);
            yield return new WaitForSeconds(1f);
            float h0 = OceanSampler.SampleImmediate(spot).height;
            Vector2 w = SeaStateController.Instance.WindDirection;
            Vector3 seasFrom = new Vector3(-w.x, 0f, -w.y).normalized;
            rb.position = new Vector3(spot.x, h0 + 0.5f, spot.z);
            // FACING the seas at the warp. The first version left her at
            // identity and let the autopilot turn her: with the seas astern
            // of the spawn heading that is a 9 s turn at her rate, so the
            // measurement window caught her BEAM-ON and mid-transient and
            // reported 43% swallowed with rails 6.8 freeboards under --
            // rolling, not spearing, and none of it what a player sailing
            // head seas experiences (seaResistance 0.87 was the tell: she
            // was 60 degrees off the sea the whole run).
            rb.rotation = Quaternion.LookRotation(seasFrom, Vector3.up);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            motor.ThrottleOrder = 1f;
            motor.AutopilotTarget = rb.position + seasFrom * 5000f;
            yield return new WaitForSeconds(SettleSecs);

            float t0 = Time.time;
            while (Time.time - t0 < MeasureSecs)
            {
                yield return new WaitForFixedUpdate();
                float dt = Time.fixedDeltaTime;
                total += dt;
                float rail = body.MaxRailImmersion;
                if (rail > railMax) railMax = rail;
                if (rail > 0.3f * fb) awash += dt;
                if (rail > 1.0f * fb) swallowed += dt;
                if (body.Buried) buried += dt;

                Vector3 fwd = motor.transform.forward; fwd.y = 0f; fwd.Normalize();
                waySum += Vector3.Dot(rb.linearVelocity, fwd);
                resistSum += motor.SeaResistance01;
                float plow = body.DebugPlowForward.magnitude / mass;
                plowSum += plow;
                if (plow > 0.2f) plowActive += dt;
                liftSum += body.DebugDynamicLift.magnitude / mass;

                Vector3 hp = motor.transform.position;
                float draft = OceanSampler.SampleImmediate(hp).height - hp.y;
                draftSum += draft;
                draftSqSum += draft * draft;
                n++;
            }
        }

        float draftMean = draftSum / Mathf.Max(1, n);
        float draftVar = draftSqSum / Mathf.Max(1, n) - draftMean * draftMean;
        sb.AppendLine(string.Format(
            "{0}x{1}s head seas: awash(>0.3fb) {2:F1}%  SWALLOWED(>1fb) {3:F1}%  clampActive {4:F1}%",
            Segments, MeasureSecs, awash / total * 100f, swallowed / total * 100f,
            buried / total * 100f));
        sb.AppendLine(string.Format(
            "railOverMax {0:F2} m ({1:F2} x freeboard)  draftMean {2:F2} m  draftRms {3:F2} m",
            railMax, railMax / Mathf.Max(0.01f, fb), draftMean,
            Mathf.Sqrt(Mathf.Max(0f, draftVar))));
        sb.AppendLine(string.Format(
            "meanForwardWay {0:F1} m/s (of max {1:F1})  seaResistance {2:F2}  plow mean {3:F2} m/s2 (active {4:F1}%)  dynLift mean {5:F2} m/s2",
            waySum / Mathf.Max(1, n), motor.MaxSpeed, resistSum / Mathf.Max(1, n),
            plowSum / Mathf.Max(1, n), plowActive / total * 100f,
            liftSum / Mathf.Max(1, n)));

        SeaStateController.Instance.ReleaseForce();
        motor.AutopilotTarget = null;
        if (helm != null) helm.enabled = true;
        System.IO.File.WriteAllText("/tmp/seasick-swallow.txt", sb.ToString());
        Debug.Log("SwallowProbe:\n" + sb);
        Destroy(gameObject);
    }

    static void SetField(object o, string name, float v)
    {
        FieldInfo f = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null) { Debug.LogError("SwallowProbe: no field " + name); return; }
        f.SetValue(o, v);
    }

    static float GetField(object o, string name)
    {
        FieldInfo f = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return f != null ? (float)f.GetValue(o) : -1f;
    }
}
