using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.Steamer;
using SeaSick.World;

/// THE STEAMER'S SEA TRIALS. One instrument, six modes, one mode per play
/// session (the fresh-session rule in docs/DEV-TOOLS.md):
///
///   FLOAT  does she sit on her marks: origin against the sampled surface,
///          trim, heel, submersion, wheel dips, and the book against the
///          station tables.
///   DECAY  a roll kick and a pitch kick in the pinned calm: period and
///          damping ratio from the first half-cycle peaks.
///   DRIVE  from rest to full ahead, the coast-down, then full astern.
///   TURN   the pivot at rest, then the turn at full ahead and how it ends.
///   SWAY   local Hs 3.5 on three headings about the swell.
///   STORM  the same at local Hs 9, green water in waist freeboards.
///
/// Play mode, Sea.unity, with the steamer selected (PlayerPrefs int
/// "SeaSick.Steamer" == 1 BEFORE pressing Play). Launched through
/// `RunSteamerProbe`. Writes /tmp/seasick-steamer-MODE.txt, last line DONE.
///
/// Three things differ from HandlingProbe, all because of what she is:
///
///  * Her hull samples the sea through REGISTRY HANDLES, and the driver gives
///    those the raw field -- `hullFeelsCascade0/1` only filter BuoyantBody
///    probes. HandlingProbe's "flat as the hull feels it" trick therefore does
///    nothing to her, and is not used. Calm here means the pinned local Hs and
///    nothing more; the residual is measured and printed before each kick.
///  * She has no attitude springs and no burial clamp, so a warp SEATS her at
///    the sampled surface (her origin is her waterline) instead of dropping
///    her from 3 m, then waits 0.5 s and for `HullFormBody.Ready`.
///  * Every time here is GAME time counted in fixed steps. The unfocused
///    editor runs near 10 fps and the maximum allowed timestep is 0.15 s, so
///    wall-clock seconds are not simulation seconds.
///
/// The helm is driven by disabling `HelmInput` and writing the motor's
/// Rudder and ThrottleOrder. The telegraph's own ramp (ShipMotor.Throttle is
/// rate-limited) IS in the drive numbers and is reported beside them.
public class SteamerProbe : MonoBehaviour
{
    static string mode = "float";

    public static void Float() { Launch("float"); }
    public static void Decay() { Launch("decay"); }
    public static void Drive() { Launch("drive"); }
    public static void Turn() { Launch("turn"); }
    public static void Sway() { Launch("sway"); }
    public static void Storm() { Launch("storm"); }
    public static void Execute() { Launch("float"); }

    static string PathFor(string m) { return "/tmp/seasick-steamer-" + m + ".txt"; }

    static void Launch(string m)
    {
        if (!Application.isPlaying) { Debug.LogError("SteamerProbe: play mode only"); return; }
        var hullBody = FindAnyObjectByType<HullFormBody>();
        if (hullBody == null)
        {
            string msg = "SteamerProbe REFUSED: there is no SteamerShip / HullFormBody in the scene.\n"
                + "The steamer is a runtime conversion of PlayerShip and only happens at play start:\n"
                + "leave play mode, set PlayerPrefs int \"" + SteamerBootstrap.PrefKey + "\" to 1\n"
                + "(RunSteamerProbe.Select(), or the SeaSick/Dev menu), open Sea.unity, press Play, run again.\n"
                + "Preference now: " + PlayerPrefs.GetInt(SteamerBootstrap.PrefKey, 0) + "\n";
            System.IO.File.WriteAllText(PathFor(m), msg + "DONE\n");
            Debug.LogError(msg + PathFor(m));
            return;
        }
        var old = FindAnyObjectByType<SteamerProbe>();
        if (old != null) Destroy(old.gameObject);
        mode = m;
        new GameObject("SteamerProbe").AddComponent<SteamerProbe>();
    }

    // Waves are untouched below 8 m of water and capped at 0.55 x depth. 12 m
    // is clear of the bottom for the calm and for Hs 3.5; Hs 9 needs 16.4 m
    // of water before the cap lets it exist, so STORM stops the ladder at 25.
    static readonly float[] DepthLadder = { -60f, -40f, -25f, -12f };
    const float CalmHs = 0.15f;
    const float SwayHs = 3.5f;
    const float StormHs = 9f;
    const float ClearRadius = 480f;
    const double PinBase = 500.0;

    // The brig's recorded numbers (HandlingProbe sway, down / beam / up).
    static readonly float[] BrigRollRms = { 2.9f, 2.9f, 3.9f };
    static readonly float[] BrigRollP90 = { 4.9f, 4.8f, 6.3f };
    static readonly float[] BrigRollMax = { 7.3f, 6.8f, 9.6f };
    static readonly float[] BrigRough = { 0.07f, 0.04f, 0.09f };

    HullFormBody body;
    PaddleDrive drive;
    HullFormData data;
    ShipMotor motor;
    Rigidbody rb;
    SmoothnessMeter meter;
    SeaStateController sea;
    HelmInput helm;
    OceanProbeRegistry.Handle gauge;
    Vector3 spot;
    StringBuilder sb;
    string failure;
    bool restored;
    float reff;

    // A raw point gauge at her origin, sampled in the driver's one batch like
    // everything else. Written here at order 0, so after the driver: the
    // position it was sampled at is one step old. That is 0.3 m at full
    // ahead, which is nothing to a wave-height statistic, and nothing at all
    // at rest, which is the only place it is used as a level.
    void FixedUpdate()
    {
        if (gauge != null && rb != null)
            gauge.position = new Vector3(rb.position.x, 0f, rb.position.z);
    }

    IEnumerator Start()
    {
        sb = new StringBuilder();
        sb.AppendLine("=== SteamerProbe: " + mode + " ===");
        body = FindAnyObjectByType<HullFormBody>();
        sea = SeaStateController.Instance;
        if (body == null || sea == null)
        {
            Fail("no HullFormBody or no SeaStateController in the scene");
            Finish();
            yield break;
        }
        drive = body.GetComponent<PaddleDrive>();
        motor = body.GetComponent<ShipMotor>();
        rb = body.GetComponent<Rigidbody>();
        meter = body.GetComponent<SmoothnessMeter>();
        var marker = body.GetComponent<SteamerShip>();
        data = body.Data;
        if (drive == null || motor == null || rb == null)
        {
            Fail("the hull has no PaddleDrive / ShipMotor / Rigidbody beside it (drive "
                + (drive != null) + " motor " + (motor != null) + " rb " + (rb != null) + ")");
            Finish();
            yield break;
        }

        helm = FindAnyObjectByType<HelmInput>();
        if (helm != null) helm.enabled = false;

        // She starts tied up, and the anchor is a spring to the drop point.
        var anchor = motor.GetComponent<AnchorController>();
        if (anchor != null) anchor.CastOff();
        motor.Anchored = false;
        motor.MooringHeading = null;
        motor.AutopilotTarget = null;
        motor.Rudder = 0f;
        motor.ThrottleOrder = 0f;

        float waited = 0f;
        while ((!OceanSampler.Ready || !body.Ready || body.Data == null) && waited < 30f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        data = body.Data;
        if (!OceanSampler.Ready || !body.Ready || data == null)
        {
            Fail("INSTRUMENT FAILURE: after 30 s OceanSampler.Ready=" + OceanSampler.Ready
                + " HullFormBody.Ready=" + body.Ready + " data=" + (data != null));
            Finish();
            yield break;
        }
        sb.AppendLine("SteamerShip marker " + (marker != null ? "present" : "ABSENT (HullFormBody placed by hand?)")
            + "   ShipMotor.ExternalDrive " + motor.ExternalDrive
            + (motor.ExternalDrive ? "" : "   !! the servo motor is still driving her -- every number below is suspect"));

        OceanTime.Paused = false;
        OceanTime.Scale = 1.0;
        gauge = OceanProbeRegistry.Register(rb.position);

        yield return FindDeepWater(mode == "storm" ? 3 : DepthLadder.Length);
        if (!foundWater)
        {
            Fail("no clear deep water found on the depth ladder");
            Finish();
            yield break;
        }
        Header();

        if (mode == "float") yield return RunFloat();
        else if (mode == "decay") yield return RunDecay();
        else if (mode == "drive") yield return RunDrive();
        else if (mode == "turn") yield return RunTurn();
        else if (mode == "sway") yield return RunLegs(SwayHs, false);
        else if (mode == "storm") yield return RunLegs(StormHs, true);
        else Fail("unknown mode " + mode);

        Finish();
    }

    void Fail(string why)
    {
        failure = why;
        sb.AppendLine();
        sb.AppendLine("!! " + why);
        Debug.LogError("SteamerProbe: " + why);
    }

    void Restore()
    {
        if (restored) return;
        restored = true;
        if (motor != null)
        {
            motor.AutopilotTarget = null;
            motor.Rudder = 0f;
            motor.ThrottleOrder = 0f;
        }
        if (sea != null) sea.ReleaseForce();
        if (helm != null) helm.enabled = true;
        if (gauge != null) { OceanProbeRegistry.Unregister(gauge); gauge = null; }
    }

    void Finish()
    {
        Restore();
        if (failure != null) sb.AppendLine("RUN FAILED: " + failure);
        sb.AppendLine("DONE");
        string path = PathFor(mode);
        System.IO.File.WriteAllText(path, sb.ToString());
        Debug.Log("SteamerProbe wrote " + path + "\n" + sb);
        Destroy(gameObject);
    }

    // The registry is static and domain reload is off: a handle left behind
    // is sampled for ever. Also covers the probe being destroyed mid-run.
    void OnDestroy() { Restore(); }

    // ------------------------------------------------------------ setup --

    bool foundWater;
    /// Read ONCE: the swell turns on its own clock, and legs laid off a
    /// bearing that moved would leave the disc that was checked.
    float swellBearing;

    /// HandlingProbe's search: a disc of ClearRadius, every point of a 40 m
    /// grid deeper than the rung, nearest ring first.
    IEnumerator FindDeepWater(int rungs)
    {
        foundWater = false;
        var h = Island.TerrainHeight;
        if (h == null) yield break;

        var offsets = new List<Vector2>();
        for (float x = -ClearRadius; x <= ClearRadius; x += 40f)
            for (float z = -ClearRadius; z <= ClearRadius; z += 40f)
                if (x * x + z * z <= ClearRadius * ClearRadius) offsets.Add(new Vector2(x, z));
        swellBearing = BearingOf(sea.SwellDirection);

        for (int d = 0; d < rungs && d < DepthLadder.Length; d++)
        {
            int tried = 0;
            for (float dist = 1500f; dist <= 20000f; dist += 250f)
                for (int b = 0; b < 16; b++)
                {
                    float a = b / 16f * Mathf.PI * 2f;
                    Vector3 c = new Vector3(Mathf.Sin(a) * dist, 0f, Mathf.Cos(a) * dist);
                    if (h(c.x, c.z) > DepthLadder[d]) continue;
                    bool deep = true;
                    float shallowest = float.NegativeInfinity;
                    for (int i = 0; i < offsets.Count && deep; i++)
                    {
                        float g = h(c.x + offsets[i].x, c.z + offsets[i].y);
                        if (g > shallowest) shallowest = g;
                        if (g > DepthLadder[d]) deep = false;
                    }
                    if ((++tried & 7) == 0) yield return null;
                    if (!deep) continue;
                    spot = c;
                    foundWater = true;
                    sb.AppendLine("clear water at " + spot.ToString("F0") + ": a " + ClearRadius.ToString("F0")
                        + " m disc, shallowest seabed " + shallowest.ToString("F0") + " m on a 40 m grid; swell runs "
                        + swellBearing.ToString("F0") + " deg, wind sea " + BearingOf(sea.WindDirection).ToString("F0"));
                    yield break;
                }
        }
    }

    /// What she IS, read off the live components. Every gate below is judged
    /// against these, and a probe that assumed them would be reporting on the
    /// code it was written against.
    void Header()
    {
        Vector3 it = rb.inertiaTensor;
        Vector3 com = rb.centerOfMass;
        float g = Physics.gravity.magnitude;
        reff = ReadFloat(drive, "reff");
        if (float.IsNaN(reff) || reff <= 0f)
            reff = Mathf.Max(0.2f, data.wheelRadius - 0.5f * data.wheelDesignDip);

        sb.AppendLine(string.Format(
            "hull   LWL {0:F2}  LOA {1:F2}  beam {2:F2}  draft {3:F2}  depth {4:F2}  waist freeboard {5:F2} m   stations {6}",
            data.lwl, data.loa, data.beam, data.draft, data.depth, data.depth - data.draft, data.StationCount));
        sb.AppendLine(string.Format(
            "body   mass {0:F0} kg  CoM ({1:F2}, {2:F2}, {3:F2})  inertia pitch/yaw/roll {4:E2} {5:E2} {6:E2}  fixed dt {7:F4} s",
            rb.mass, com.x, com.y, com.z, it.x, it.y, it.z, Time.fixedDeltaTime));
        sb.AppendLine(string.Format(
            "       stiffness roll {0:E2} pitch {1:E2} N m/rad   -> undamped T roll {2:F2} s  pitch {3:F2} s",
            body.RollStiffness, body.PitchStiffness,
            body.RollStiffness > 0f ? 2f * Mathf.PI * Mathf.Sqrt(it.z / body.RollStiffness) : 0f,
            body.PitchStiffness > 0f ? 2f * Mathf.PI * Mathf.Sqrt(it.x / body.PitchStiffness) : 0f));
        sb.AppendLine(string.Format(
            "       zeta heave {0:F2} roll {1:F2} pitch {2:F2}   damping strip roll {3:E2} + extra {4:E2}, strip pitch {5:E2} + extra {6:E2}",
            ReadFloat(body, "heaveDampingRatio"), ReadFloat(body, "rollDampingRatio"), ReadFloat(body, "pitchDampingRatio"),
            body.DebugStripRollDamping, body.DebugExtraRollDamping,
            body.DebugStripPitchDamping, body.DebugExtraPitchDamping));
        sb.AppendLine(string.Format(
            "       surge a1 {0:F4} a2 {1:F5}   crossflow Cd {2:F2} lift {3:F2} lin {4:F2}  skeg {5:F2} x {6}  lateral depth {7:F2}  yaw damp {8:F2}  maxSlope {9:F2}  green threshold {10:F2} m",
            body.SurgeLinear, body.SurgeQuadratic,
            ReadFloat(body, "crossflowCd"), ReadFloat(body, "crossflowLift"), ReadFloat(body, "crossflowLinear"),
            ReadFloat(body, "skegFactor"), ReadInt(body, "skegStations"), ReadFloat(body, "lateralForceDepth"),
            ReadFloat(body, "yawDampingFactor"), ReadFloat(body, "maxSlope"), ReadFloat(body, "greenWaterThreshold")));
        sb.AppendLine(string.Format(
            "drive  topSpeed {0:F2}  slipAtTop {1:F2}  bollardAccel {2:F2}  spinUp {3:F2} s  governorBand {4:F3}  rudderGain {5:F2}  rudderMax {6:F0} deg  raceGain {7:F2}  rudderHeelLever {8:F2}",
            drive.TopSpeed, ReadFloat(drive, "slipAtTop"), ReadFloat(drive, "bollardAccel"),
            ReadFloat(drive, "spinUpSeconds"), ReadFloat(drive, "governorBand"), ReadFloat(drive, "rudderGain"),
            ReadFloat(drive, "rudderMaxDeg"), ReadFloat(drive, "raceGain"),
            ReadFloat(drive, "rudderHeelLever")));
        sb.AppendLine(string.Format(
            "       Reff {0:F3} m  full revs {1:F2} rad/s  tauMax {2:E2} N m  thrustK {3:E2}   wheel R {4:F2}  axle ({5:F2}, {6:F2}, {7:F2})  design dip {8:F2}  rudder area {9:F1}",
            reff, drive.MaxRate, ReadFloat(drive, "tauMax"), ReadFloat(drive, "thrustK"),
            data.wheelRadius, data.wheelAxle.x, data.wheelAxle.y, data.wheelAxle.z, data.wheelDesignDip, data.rudderArea));
        sb.AppendLine(string.Format(
            "motor  MaxSpeed {0:F2}  HullLength {1:F2}  telegraph rate {2:F2} /s x crew labour {3:F2}  ExternalDrive {4}",
            motor.MaxSpeed, motor.HullLength, ReadFloat(motor, "sailTrimRate"), motor.OarPower01, motor.ExternalDrive));
        if (meter != null)
            sb.AppendLine(string.Format(
                "meter  ceilings heave {0:F1} m/s  pitch {1:F0}  roll {2:F0} deg/s  lateral {3:F1} m/s2",
                ReadFloat(meter, "heaveRateCeiling"), ReadFloat(meter, "pitchRateCeiling"),
                ReadFloat(meter, "rollRateCeiling"), ReadFloat(meter, "lateralAccelCeiling")));
        sb.AppendLine("signs  heel + = starboard side UP (she lists to port)   trim + = bow UP   yaw + = to starboard   level + = water above her marks");
        sb.AppendLine("note   her strips sample the RAW sea (registry handles are not hull-filtered); the ladder ships' probes feel cascade 1 at "
            + OceanPhysicsDriver.HullFilterNow.y.ToString("F2"));
    }

    static System.Reflection.FieldInfo FieldOf(object c, string field)
    {
        if (c == null) return null;
        return c.GetType().GetField(field,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public);
    }

    static float ReadFloat(object c, string field)
    {
        var f = FieldOf(c, field);
        return f != null && f.FieldType == typeof(float) ? (float)f.GetValue(c) : float.NaN;
    }

    static int ReadInt(object c, string field)
    {
        var f = FieldOf(c, field);
        return f != null && f.FieldType == typeof(int) ? (int)f.GetValue(c) : -1;
    }

    /// Solve for the GLOBAL Hs that lands the wanted LOCAL one: RegionField
    /// scales the sea by place. The calm end has a floor; what was reached is
    /// what gets reported.
    float ForceLocalHs(float want)
    {
        Vector2 at = new Vector2(spot.x, spot.z);
        float ask = want;
        for (int i = 0; i < 4; i++)
        {
            sea.ForceHs(ask);
            float got = sea.SeaHsAt(at);
            if (got < 1e-3f) break;
            ask *= want / got;
        }
        return sea.SeaHsAt(at);
    }

    float seaReached;

    /// Pin the sea, THEN the phase, in the order the traps demand.
    ///
    /// The envelope is a function of OceanTime as well as place, so the clock
    /// goes to ten seconds short of the pin first, the height is solved
    /// there, the rebuild is given nine seconds to land with the clock
    /// RUNNING (a paused clock freezes the spectrum), the height is solved
    /// again, and only then is the phase scrubbed to the instant itself.
    ///
    /// OceanTime is static and outlives a play session, so the first scrub
    /// can be BACKWARDS -- and SeaStateController throttles its rebuild on
    /// `Now - lastRebuildTime`, which a backward scrub turns negative for as
    /// long as it takes the clock to catch up. SurfProbe measured one sea
    /// twice that way. The throttle's stamp is put back to its initial value
    /// so the forced sea cannot be held off.
    IEnumerator SetSea(float wantHs, double pinT)
    {
        OceanTime.Paused = false;
        OceanTime.Scale = 1.0;
        double before = OceanTime.Now;
        OceanTime.Scrub(pinT - 10.0);
        var stamp = FieldOf(sea, "lastRebuildTime");
        if (stamp != null && stamp.FieldType == typeof(double)) stamp.SetValue(sea, -999.0);
        else if (before > pinT - 10.0)
            sb.AppendLine("!! could not reach SeaStateController.lastRebuildTime and the scrub was BACKWARDS ("
                + before.ToString("F0") + " -> " + (pinT - 10.0).ToString("F0")
                + "): the forced sea may not have reached the water. Check the measured Hs.");
        ForceLocalHs(wantHs);
        yield return new WaitForSeconds(9f);
        seaReached = ForceLocalHs(wantHs);
        yield return new WaitForSeconds(1f);
        OceanTime.Scrub(pinT);
        yield return new WaitForSeconds(1f);   // the readback ring catches up
        sb.AppendLine(string.Format(
            "sea    asked local Hs {0:F2}, envelope says {1:F2} here (global {2:F2}, severity {3:F3}); phase pinned at OceanTime {4:F0}{5}",
            wantHs, seaReached, sea.CurrentHs, sea.Severity01, pinT,
            Mathf.Abs(seaReached - wantHs) > 0.25f * wantHs + 0.05f
                ? "   !! NOT the sea that was asked for (the calm end has a floor)" : ""));
    }

    /// Transform AND rigidbody, seated at the sampled surface: her origin is
    /// her waterline, and she has no clamp to catch a 3 m drop.
    void Warp(float headingDeg, float back)
    {
        motor.AutopilotTarget = null;
        motor.Rudder = 0f;
        Quaternion q = Quaternion.Euler(0f, headingDeg, 0f);
        Vector3 dir = q * Vector3.forward;
        Vector3 p = new Vector3(spot.x, 0f, spot.z) - dir * back;
        float h = OceanSampler.SampleImmediate(p).height;
        if (float.IsNaN(h) || float.IsInfinity(h)) h = 0f;
        p.y = h;
        body.transform.SetPositionAndRotation(p, q);
        rb.position = p;
        rb.rotation = q;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    /// Her samples come through registry handles: half a second for them to
    /// be taken where she now is, and then until the hull says it is reading.
    IEnumerator AfterWarp()
    {
        yield return new WaitForSeconds(0.5f);
        float t = 0f;
        while (!body.Ready && t < 10f)
        {
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
        }
        if (!body.Ready)
            Fail("INSTRUMENT FAILURE: HullFormBody.Ready still false 10 s after a warp -- nothing after this was measured");
    }

    void HoldHeading(float headingDeg)
    {
        Vector3 dir = Quaternion.Euler(0f, headingDeg, 0f) * Vector3.forward;
        motor.AutopilotTarget = rb.position + dir * 50000f;
    }

    float lastTelegraphWait;
    IEnumerator WaitTelegraph(float target, float maxSeconds)
    {
        float t = 0f;
        while (Mathf.Abs(motor.Throttle - target) > 0.01f && t < maxSeconds)
        {
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
        }
        lastTelegraphWait = t;
    }

    // Attitude off the BODY, not the transform: with interpolation on the
    // transform is the drawn pose, up to a step behind.
    float Heel()
    {
        Vector3 r = rb.rotation * Vector3.right;
        return Mathf.Asin(Mathf.Clamp(r.y, -1f, 1f)) * Mathf.Rad2Deg;
    }

    float Trim()
    {
        Vector3 f = rb.rotation * Vector3.forward;
        return Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
    }

    Vector3 FlatForward()
    {
        Vector3 f = rb.rotation * Vector3.forward;
        f.y = 0f;
        return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
    }

    float Way() { return Vector3.Dot(rb.linearVelocity, FlatForward()); }
    float HeadingDeg() { Vector3 f = FlatForward(); return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; }
    float YawRate() { return rb.angularVelocity.y * Mathf.Rad2Deg; }

    float DriftDeg()
    {
        Vector3 v = rb.linearVelocity; v.y = 0f;
        if (v.magnitude < 0.5f) return 0f;
        return Vector3.SignedAngle(FlatForward(), v, Vector3.up);
    }

    bool GaugeValid()
    {
        if (gauge == null || gauge.sampledFrame == 0) return false;
        float h = gauge.sample.height;
        return !float.IsNaN(h) && !float.IsInfinity(h);
    }

    static float BearingOf(Vector2 run) { return Mathf.Atan2(run.x, run.y) * Mathf.Rad2Deg; }
    static string Verdict(bool ok) { return ok ? "PASS" : "FAIL"; }

    // ------------------------------------------------------------ FLOAT --

    IEnumerator RunFloat()
    {
        yield return SetSea(CalmHs, PinBase);
        motor.ThrottleOrder = 0f;
        Warp(swellBearing, 0f);
        yield return AfterWarp();
        if (failure != null) yield break;
        yield return new WaitForSeconds(10f);

        int stations = data.StationCount;
        var gaugeH = new List<float>();
        float t = 0f; int n = 0;
        float above = 0f, aboveGauge = 0f, levelMean = 0f, trim = 0f, heel = 0f, sub = 0f, subRaw = 0f;
        float dipW = 0f, aft = 0f, fore = 0f, deck = 0f, vy = 0f;
        int nGauge = 0;
        int aftIdx = 0, foreIdx = 0;
        for (int i = 0; i < stations; i++)
        {
            if (data.stations[i].z < data.stations[aftIdx].z) aftIdx = i;
            if (data.stations[i].z > data.stations[foreIdx].z) foreIdx = i;
        }
        while (t < 12f)
        {
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
            above += rb.position.y - body.MeanWaterHeight;
            if (GaugeValid()) { aboveGauge += rb.position.y - gauge.sample.height; gaugeH.Add(gauge.sample.height); nGauge++; }
            float lv = 0f;
            for (int i = 0; i < stations; i++)
                lv += 0.5f * (body.HalfStripLevel(i, 0) + body.HalfStripLevel(i, 1));
            levelMean += stations > 0 ? lv / stations : 0f;
            aft += 0.5f * (body.HalfStripLevel(aftIdx, 0) + body.HalfStripLevel(aftIdx, 1));
            fore += 0.5f * (body.HalfStripLevel(foreIdx, 0) + body.HalfStripLevel(foreIdx, 1));
            trim += Trim(); heel += Heel();
            sub += body.Submersion; subRaw += body.SubmersionRaw;
            dipW += drive.WheelDip;
            deck += body.MaxDeckImmersion;
            vy += rb.linearVelocity.y * rb.linearVelocity.y;
            n++;
        }
        if (n == 0) { Fail("INSTRUMENT FAILURE: no fixed steps in the FLOAT window"); yield break; }
        above /= n; levelMean /= n; trim /= n; heel /= n; sub /= n; subRaw /= n;
        dipW /= n; aft /= n; fore /= n; deck /= n;

        sb.AppendLine();
        sb.AppendLine("FLOAT  (at rest, 12 s mean over " + n + " steps after a 10 s settle; her origin IS the design waterline)");
        sb.AppendLine(string.Format(
            "  origin above the hull's own mean sampled surface {0,7:F1} cm   (negative = she sits deep)",
            above * 100f));
        if (nGauge > 0)
            sb.AppendLine(string.Format(
                "  origin above an independent point gauge at her origin {0,7:F1} cm   (one raw point, not her samples: the non-circular check)",
                aboveGauge / nGauge * 100f));
        else
            sb.AppendLine("  !! the independent gauge never sampled");
        sb.AppendLine(string.Format(
            "  mean half-strip level {0,6:F1} cm above her marks   aftmost station {1,6:F1} cm   foremost {2,6:F1} cm",
            levelMean * 100f, aft * 100f, fore * 100f));
        sb.AppendLine(string.Format(
            "  trim {0:F2} deg (bow up +)   heel {1:F2} deg   Submersion {2:F3} (raw {3:F3})   deck edge clears the water by {4:F2} m   heave-rate rms {5:F3} m/s",
            trim, heel, sub, subRaw, -deck, Mathf.Sqrt(vy / n)));
        sb.AppendLine(string.Format(
            "  stern wheel dip {0:F3} m   against wheelDesignDip {1:F3} m   (geometry says {2:F3} m with her on her marks)",
            dipW, data.wheelDesignDip, data.wheelRadius - data.wheelAxle.y));
        sb.AppendLine("  measured sea at her origin: 4 x rms = " + (4f * Std(gaugeH)).ToString("F2") + " m");

        float draftErrCm = Mathf.Abs(above) * 100f;
        bool dipOk = Mathf.Abs(dipW - data.wheelDesignDip) <= 0.1f;
        sb.AppendLine("  GATE draft within 3 cm of drawn: " + draftErrCm.ToString("F1") + " cm  " + Verdict(draftErrCm <= 3f)
            + "     GATE wheel dip = design +/- 0.10 m: " + Verdict(dipOk));

        float volD, wpD, kbD, bmD, lcbD, ilD;
        data.Rederive(out volD, out wpD, out kbD, out bmD, out lcbD, out ilD);
        float rho = ReadFloat(body, "waterDensity");
        sb.AppendLine();
        sb.AppendLine("  THE BOOK against HullFormData.Rederive (the station tables at level 0)");
        sb.AppendLine("                     book      tables     diff");
        BookLine("volume m3", data.volume, volD);
        BookLine("waterplane m2", data.waterplane, wpD);
        BookLine("KB m", data.kb, kbD);
        BookLine("BM m", data.bm, bmD);
        BookLine("LCB z m", data.lcbZ, lcbD);
        sb.AppendLine(string.Format("    waterplane I_L (tables) {0:E3} m4", ilD));
        sb.AppendLine(string.Format(
            "    GM book {0:F3}   KB+BM-KG off the tables {1:F3}   KG {2:F3}   mass book {3:F0}  rigidbody {4:F0}  rho x table volume {5:F0} kg (rho {6:F0})",
            data.gm, kbD + bmD - data.kg, data.kg, data.massKg, rb.mass, rho * volD, rho));
        sb.AppendLine(string.Format(
            "    rigidbody mass / (rho x table volume) = {0:F4}   -- 1.0000 floats her on her marks; each 1% is about {1:F1} cm of draft",
            rho * volD > 1f ? rb.mass / (rho * volD) : 0f,
            wpD > 1f ? 0.01f * volD / wpD * 100f : 0f));
    }

    void BookLine(string what, float book, float tables)
    {
        float scale = Mathf.Max(Mathf.Abs(book), 1e-4f);
        sb.AppendLine(string.Format("    {0,-14} {1,9:F3}  {2,9:F3}  {3,7:F2}%", what, book, tables,
            100f * (tables - book) / scale));
    }

    // ------------------------------------------------------------ DECAY --

    IEnumerator RunDecay()
    {
        yield return SetSea(CalmHs, PinBase);
        motor.ThrottleOrder = 0f;
        Warp(swellBearing, 0f);
        yield return AfterWarp();
        if (failure != null) yield break;
        yield return new WaitForSeconds(12f);

        // What the pinned calm does to her on its own: the floor every peak
        // has to clear, MEASURED, because her strips feel the raw chop.
        var r0 = new List<float>(); var p0 = new List<float>();
        float t = 0f;
        while (t < 6f)
        {
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
            r0.Add(Heel()); p0.Add(Trim());
        }
        float rollNoise = Std(r0), pitchNoise = Std(p0);
        sb.AppendLine();
        sb.AppendLine("DECAY  (pinned calm, no way on, telegraph at stop; a kick, then hands off)");
        sb.AppendLine(string.Format("  before the kicks the sea alone moves her: heel rms {0:F3} deg  trim rms {1:F3} deg",
            rollNoise, pitchNoise));

        yield return Kick(true, 8f, rollNoise);
        yield return new WaitForSeconds(6f);
        yield return Kick(false, 4f, pitchNoise);
    }

    /// First-peak height of x'' + 2 z w x' + w^2 x = 0 from a velocity kick
    /// v0, as a fraction of v0 / w.
    static float PeakFactor(float zeta)
    {
        if (float.IsNaN(zeta) || zeta < 0f) zeta = 0f;
        if (zeta >= 0.999f) return Mathf.Exp(-1f);
        float s = Mathf.Sqrt(1f - zeta * zeta);
        float phi = Mathf.Atan2(s, zeta);
        return Mathf.Exp(-zeta * phi / s);
    }

    IEnumerator Kick(bool roll, float targetDeg, float noiseRms)
    {
        Vector3 it = rb.inertiaTensor;
        float inertia = roll ? it.z : it.x;
        float stiffness = roll ? body.RollStiffness : body.PitchStiffness;
        float zetaSet = ReadFloat(body, roll ? "rollDampingRatio" : "pitchDampingRatio");
        float wn = stiffness > 0f && inertia > 0f ? Mathf.Sqrt(stiffness / inertia) : 1f;
        float rate = targetDeg * Mathf.Deg2Rad * wn / PeakFactor(zetaSet);

        // Bow UP is a negative rotation about +X; starboard side up is a
        // positive one about +Z. Both kicks are made positive in this
        // probe's own signs.
        float rest = roll ? Heel() : Trim();
        rb.AddRelativeTorque(roll ? Vector3.forward * (inertia * rate) : Vector3.right * (-inertia * rate),
            ForceMode.Impulse);

        var ts = new List<float>(); var xs = new List<float>();
        float t = 0f;
        while (t < 24f)
        {
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
            ts.Add(t); xs.Add((roll ? Heel() : Trim()) - rest);
        }

        float floor = Mathf.Max(0.25f, 3f * noiseRms);
        var peakT = new List<float>(); var peakA = new List<float>();
        for (int i = 2; i < xs.Count - 2; i++)
        {
            float a = Mathf.Abs(xs[i]);
            if (a < floor) continue;
            if (a >= Mathf.Abs(xs[i - 1]) && a >= Mathf.Abs(xs[i + 1])
                && a > Mathf.Abs(xs[i - 2]) && a > Mathf.Abs(xs[i + 2]))
            {
                if (peakA.Count > 0 && Mathf.Sign(xs[i]) == Mathf.Sign(peakA[peakA.Count - 1]))
                    continue;
                peakT.Add(ts[i]); peakA.Add(xs[i]);
            }
        }

        string name = roll ? "roll " : "pitch";
        sb.AppendLine();
        sb.AppendLine(string.Format(
            "  {0}  kick {1:F1} deg/s (sized for {2:F0} deg from the live stiffness {3:E2}, inertia {4:E2}, zeta {5:F2}); peak floor {6:F2} deg",
            name, rate * Mathf.Rad2Deg, targetDeg, stiffness, inertia, zetaSet, floor));
        if (peakA.Count == 0)
        {
            sb.AppendLine("  " + name + "  !! no peak above the floor -- the kick did not move her; nothing measured");
            yield break;
        }
        var line = new StringBuilder("  " + name + "  half-cycle peaks");
        for (int i = 0; i < peakA.Count && i < 8; i++)
            line.Append(string.Format(" {0:F2}@{1:F2}s", peakA[i], peakT[i]));
        line.Append(" deg");
        sb.AppendLine(line.ToString());

        float tUndamped = 2f * Mathf.PI / wn;
        if (peakA.Count >= 2)
        {
            float ratio = Mathf.Abs(peakA[1] / peakA[0]);
            float delta = -Mathf.Log(Mathf.Max(1e-4f, ratio));       // per HALF cycle
            float zeta = delta / Mathf.Sqrt(Mathf.PI * Mathf.PI + delta * delta);
            float period = 2f * (peakT[1] - peakT[0]);
            float natural = period * Mathf.Sqrt(Mathf.Max(1e-4f, 1f - zeta * zeta));
            sb.AppendLine(string.Format(
                "  {0}  period {1:F2} s as swung (undamped equivalent {2:F2} s; the tables predict {3:F2} s)   zeta {4:F2}   second peak is {5:F0}% of the first",
                name, period, natural, tUndamped, zeta, ratio * 100f));
            if (roll)
                sb.AppendLine("  GATE T_roll 6.5-8 s: " + period.ToString("F2") + " s  " + Verdict(period >= 6.5f && period <= 8f)
                    + "     GATE zeta 0.4-0.5: " + zeta.ToString("F2") + "  " + Verdict(zeta >= 0.4f && zeta <= 0.5f));
            else
                sb.AppendLine(string.Format(
                    "  GATE pitch no overshoot: she swung back {0:F2} deg past rest ({1:F0}% of the kick)  FAIL"
                    + "   -- the live pitchDampingRatio {2:F2} predicts {3:F0}% on its own",
                    Mathf.Abs(peakA[1]), ratio * 100f, zetaSet,
                    zetaSet < 0.999f ? 100f * Mathf.Exp(-Mathf.PI * zetaSet / Mathf.Sqrt(1f - zetaSet * zetaSet)) : 0f));
        }
        else
        {
            sb.AppendLine(string.Format(
                "  {0}  one peak only ({1:F2} deg at {2:F2} s): she did not swing back past the {3:F2} deg floor. The tables predict an undamped {4:F2} s.",
                name, peakA[0], peakT[0], floor, tUndamped));
            if (roll)
                sb.AppendLine("  GATE T_roll 6.5-8 s: not measurable  FAIL     GATE zeta 0.4-0.5: above ~0.6  FAIL");
            else
                sb.AppendLine("  GATE pitch no overshoot: no swing back above " + floor.ToString("F2") + " deg  PASS");
        }
    }

    // ------------------------------------------------------------ DRIVE --

    IEnumerator RunDrive()
    {
        yield return SetSea(CalmHs, PinBase);
        float heading = swellBearing;
        motor.ThrottleOrder = 0f;
        Warp(heading, ClearRadius - 50f);
        yield return AfterWarp();
        if (failure != null) yield break;
        yield return new WaitForSeconds(6f);
        yield return WaitTelegraph(0f, 20f);

        HoldHeading(heading);
        motor.ThrottleOrder = 1f;
        var ts = new List<float>(); var ways = new List<float>();
        var gaugeH = new List<float>();
        float t = 0f, telegraphFull = -1f;
        float topSum = 0f, wS = 0f, thS = 0f, surge = 0f, dipS = 0f, trim = 0f, ride = 0f;
        int n = 0;
        while (t < 40f)
        {
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
            float w = Way();
            ts.Add(t); ways.Add(w);
            if (GaugeValid()) gaugeH.Add(gauge.sample.height);
            if (telegraphFull < 0f && motor.Throttle >= 0.99f) telegraphFull = t;
            if (t >= 32f)
            {
                topSum += w;
                wS += drive.WheelRate;
                thS += drive.WheelThrustN;
                surge += body.DebugSurgeN;
                dipS += drive.WheelDip;
                trim += Trim(); ride += rb.position.y - body.MeanWaterHeight;
                n++;
            }
        }
        if (n == 0) { Fail("INSTRUMENT FAILURE: no fixed steps in the DRIVE window"); yield break; }
        float top = topSum / n;
        wS /= n; thS /= n; surge /= n; dipS /= n; trim /= n; ride /= n;
        float t50 = -1f, t90 = -1f;
        for (int i = 0; i < ways.Count; i++)
        {
            if (t50 < 0f && ways[i] >= 0.5f * top) t50 = ts[i];
            if (t90 < 0f && ways[i] >= 0.9f * top) { t90 = ts[i]; break; }
        }
        float endOfRun = ways[ways.Count - 1];

        // Coast: telegraph to stop. The governor then holds the wheels at
        // zero revs, so the floats are BRAKES, not idle.
        motor.ThrottleOrder = 0f;
        Vector3 from = rb.position;
        float coastT = 0f, telegraphStop = -1f;
        while (coastT < 120f && Way() > 2f)
        {
            yield return new WaitForFixedUpdate();
            coastT += Time.fixedDeltaTime;
            if (telegraphStop < 0f && Mathf.Abs(motor.Throttle) <= 0.01f) telegraphStop = coastT;
        }
        Vector3 d = rb.position - from; d.y = 0f;
        float carried = d.magnitude;
        bool coasted = Way() <= 2f;

        // Astern, helm amidships and the autopilot OFF: the rudder reverses
        // with sternway and a course-holder would be steering her backwards.
        motor.AutopilotTarget = null;
        motor.Rudder = 0f;
        motor.ThrottleOrder = -1f;
        float h0 = HeadingDeg();
        float asternT = 0f, stoppedAt = -1f, minWay = float.MaxValue;
        while (asternT < 15f)
        {
            yield return new WaitForFixedUpdate();
            asternT += Time.fixedDeltaTime;
            float w = Way();
            if (w < minWay) minWay = w;
            if (stoppedAt < 0f && w <= 0f) stoppedAt = asternT;
        }
        float asternWay = Way();
        float asternS = drive.WheelRate;
        float swung = Mathf.DeltaAngle(h0, HeadingDeg());
        motor.ThrottleOrder = 0f;

        float rimS = wS * reff;
        sb.AppendLine();
        sb.AppendLine("DRIVE  (pinned calm, from rest, course held by the autopilot; the telegraph's own ramp is included)");
        sb.AppendLine(string.Format(
            "  telegraph reached full ahead after {0:F2} s (it took {1:F2} s to come to stop before the run)",
            telegraphFull, lastTelegraphWait));
        sb.AppendLine(string.Format(
            "  top speed {0:F2} m/s (mean of the last 8 s; last step {1:F2})   authored topSpeed {2:F2}   to 50% in {3:F2} s   to 90% in {4:F2} s",
            top, endOfRun, drive.TopSpeed, t50, t90));
        sb.AppendLine(string.Format(
            "  stern wheel {0:F2} rad/s of {1:F2} full   rim {2:F2} m/s   slip {3:F3}   dip {4:F2} m",
            wS, drive.MaxRate, rimS,
            Mathf.Abs(rimS) > 0.01f ? 1f - top / rimS : 0f, dipS));
        sb.AppendLine(string.Format(
            "  thrust {0:F0} N   DebugSurgeN {1:F0} N   thrust + surge = {2:F0} N ({3:F1}% of thrust: FK, rudder and cross-flow are the rest)",
            thS, surge, thS + surge,
            Mathf.Abs(thS) > 1f ? 100f * (thS + surge) / thS : 0f));
        sb.AppendLine(string.Format(
            "  at speed: trim {0:F2} deg (bow up +)   origin {1:F1} cm above her mean sampled surface   sea 4 x rms {2:F2} m",
            trim, ride * 100f, 4f * Std(gaugeH)));
        sb.AppendLine(string.Format(
            "  telegraph to stop: {0} 2 m/s after {1:F1} s and {2:F0} m = {3:F1} L   (telegraph at stop after {4:F2} s; wheel then {5:F2} rad/s)",
            coasted ? "under" : "!! STILL ABOVE", coastT, carried, carried / data.lwl, telegraphStop,
            drive.WheelRate));
        sb.AppendLine(string.Format(
            "  full astern 15 s from there: way {0:F2} m/s at the end (least {1:F2}), stopped after {2:F1} s, wheel {3:F2} rad/s, head swung {4:F1} deg hands-off",
            asternWay, minWay, stoppedAt, asternS, swung));
        sb.AppendLine("  GATE top 15-16 m/s: " + top.ToString("F2") + "  " + Verdict(top >= 15f && top <= 16f)
            + "     GATE 0-90% about 8 s: " + t90.ToString("F1") + " s (read it)"
            + "     GATE astern works: " + Verdict(asternWay < -1f));
        // One machine line, so SWAY and STORM can quote a MEASURED calm speed.
        sb.AppendLine("TOP_SPEED_MEASURED " + top.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
    }

    // ------------------------------------------------------------- TURN --

    IEnumerator RunTurn()
    {
        yield return SetSea(CalmHs, PinBase);
        float heading = swellBearing;
        float L = data.lwl;

        // ---- (a) the pivot: from rest, telegraph HALF AHEAD, helm hard over
        //
        // The side-wheeler was pivoted with the telegraph at STOP, because
        // her two wheels turned against each other. One wheel on the
        // centreline cannot do that. What it does instead is drag a race past
        // the rudder, so the honest low-speed test for this ship is: ring her
        // ahead and put the helm over, and see how big a circle she needs
        // before she has any way on at all.
        motor.ThrottleOrder = 0f;
        Warp(heading, 0f);
        yield return AfterWarp();
        if (failure != null) yield break;
        yield return new WaitForSeconds(8f);
        yield return WaitTelegraph(0f, 20f);

        motor.AutopilotTarget = null;
        // Just enough to keep the wheel turning. Half ahead on a ship that
        // makes 15 m/s is not a pivot, it is a turning circle at 7 knots --
        // the first run of this test measured 3.94 L and was measuring the
        // wrong thing.
        motor.ThrottleOrder = 0.25f;
        motor.Rudder = 1f;
        var ts = new List<float>(); var yaws = new List<float>();
        var track = new List<Vector2>();
        float t = 0f, yawSum = 0f, wS = 0f, raceSum = 0f, waySum = 0f;
        int n = 0, step = 0, nRace = 0;
        Vector3 start = rb.position;
        float turned = 0f, lastHeading = HeadingDeg();
        while (t < 20f)
        {
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
            float yaw = YawRate();
            ts.Add(t); yaws.Add(yaw);
            float hd = HeadingDeg();
            turned += Mathf.DeltaAngle(lastHeading, hd); lastHeading = hd;
            if ((step++ % 5) == 0) track.Add(new Vector2(rb.position.x, rb.position.z));
            if (t >= 12f) { yawSum += yaw; wS += drive.WheelRate; n++; }
            // The race is measured in the FIRST seconds, not the last. Once
            // she is up to the speed the wheel is driving, the slip is small
            // by definition -- averaging over the settled part measured the
            // one moment when the race is guaranteed not to matter.
            if (t <= 4f) { raceSum += drive.RudderInflow; waySum += Way(); nRace++; }
        }
        motor.Rudder = 0f;
        if (n == 0) { Fail("INSTRUMENT FAILURE: no fixed steps in the pivot window"); yield break; }
        float yawRest = yawSum / n; wS /= n;
        raceSum /= Mathf.Max(1, nRace); waySum /= Mathf.Max(1, nRace);
        float span = 0f;
        for (int i = 0; i < track.Count; i++)
            for (int j = i + 1; j < track.Count; j++)
            {
                float dd = (track[i] - track[j]).magnitude;
                if (dd > span) span = dd;
            }
        Vector3 net = rb.position - start; net.y = 0f;
        float build63 = -1f;
        for (int i = 0; i < yaws.Count; i++)
            if (Mathf.Abs(yaws[i]) >= 0.63f * Mathf.Abs(yawRest)) { build63 = ts[i]; break; }
        bool biting = Mathf.Abs(raceSum) > Mathf.Abs(waySum) + 1f;

        sb.AppendLine();
        sb.AppendLine("TURN (a)  from rest, telegraph a quarter ahead, full starboard helm, 20 s"
            + "   (a single wheel has no differential: this is the race over the rudder, not a pivot)");
        sb.AppendLine(string.Format(
            "  yaw rate {0:F2} deg/s (mean of the last 8 s; 63% of it after {1:F2} s)   she came round {2:F0} deg   a full turn would take {3:F0} s",
            yawRest, build63, turned, Mathf.Abs(yawRest) > 0.01f ? 360f / Mathf.Abs(yawRest) : 0f));
        sb.AppendLine(string.Format(
            "  her origin's path is {0:F1} m across = {1:F2} L (LWL {2:F1}); net drift {3:F1} m; way {4:F2} m/s",
            span, span / L, L, net.magnitude, waySum / n));
        sb.AppendLine(string.Format(
            "  stern wheel {0:F2} rad/s of {1:F2} full   first 4 s: rudder inflow {2:F2} m/s against {3:F2} m/s of way   {4}",
            wS, drive.MaxRate, raceSum, waySum,
            biting ? "the race is doing the steering" : "!! the helm has only her way to bite on"));
        sb.AppendLine("  GATE comes round inside 2.0 L: " + (span / L).ToString("F2") + " L  "
            + Verdict(span / L < 2.0f && Mathf.Abs(yawRest) > 0.5f)
            + "     GATE the race feeds the rudder: " + Verdict(biting)
            + (yawRest < 0f ? "     !! starboard helm turned her to PORT" : ""));

        // ---- (b) the turn at speed ----
        yield return new WaitForSeconds(3f);
        Warp(heading, 300f);
        yield return AfterWarp();
        if (failure != null) yield break;
        motor.ThrottleOrder = 1f;
        HoldHeading(heading);
        float runT = 0f, v0Sum = 0f; int v0n = 0;
        while (runT < 22f)
        {
            yield return new WaitForFixedUpdate();
            runT += Time.fixedDeltaTime;
            if (runT >= 20f) { v0Sum += Way(); v0n++; }
        }
        float v0 = v0n > 0 ? v0Sum / v0n : Way();

        motor.AutopilotTarget = null;
        motor.Rudder = 1f;
        ts.Clear(); yaws.Clear();
        var fit = new List<Vector2>();
        t = 0f; n = 0;
        float vSum = 0f, heelSum = 0f, driftSum = 0f, roughSum = 0f;
        float heelPeak = 0f, heelPeakT = 0f, yawPeak = 0f, yawPeakT = 0f;
        yawSum = 0f; wS = 0f;
        const float SettledFrom = 28f;
        while (t < 40f)
        {
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
            float yaw = YawRate();
            float heel = Heel();
            ts.Add(t); yaws.Add(yaw);
            if (Mathf.Abs(heel) > Mathf.Abs(heelPeak)) { heelPeak = heel; heelPeakT = t; }
            if (Mathf.Abs(yaw) > Mathf.Abs(yawPeak)) { yawPeak = yaw; yawPeakT = t; }
            if (t >= 20f) fit.Add(new Vector2(rb.position.x, rb.position.z));
            if (t >= SettledFrom)
            {
                Vector3 v = rb.linearVelocity; v.y = 0f;
                vSum += v.magnitude; yawSum += yaw; heelSum += heel; driftSum += DriftDeg();
                wS += drive.WheelRate;
                if (meter != null) roughSum += meter.Roughness01;
                n++;
            }
        }
        if (n == 0) { Fail("INSTRUMENT FAILURE: no fixed steps in the settled-turn window"); yield break; }
        float vSteady = vSum / n, yawSteady = yawSum / n, heelSteady = heelSum / n, drift = driftSum / n;
        wS /= n;
        float t63 = -1f;
        for (int i = 0; i < yaws.Count; i++)
            if (Mathf.Abs(yaws[i]) >= 0.63f * Mathf.Abs(yawSteady)) { t63 = ts[i]; break; }
        float radiusRate = Mathf.Abs(yawSteady) > 1e-3f ? vSteady / (Mathf.Abs(yawSteady) * Mathf.Deg2Rad) : 0f;
        float radiusTrack = CircleRadius(fit);

        // Helm amidships: the LAST time she was still swinging, so a wave
        // nudging her back over the line is not read as the turn ending.
        motor.Rudder = 0f;
        float ct = 0f, last = 0f;
        while (ct < 15f)
        {
            yield return new WaitForFixedUpdate();
            ct += Time.fixedDeltaTime;
            if (Mathf.Abs(YawRate()) > 0.1f * Mathf.Abs(yawSteady)) last = ct;
        }
        motor.ThrottleOrder = 0f;

        float side = Mathf.Sign(yawSteady);
        sb.AppendLine();
        sb.AppendLine("TURN (b)  full ahead settled 22 s, then full starboard helm 40 s (settled = the last 12 s), then helm amidships");
        sb.AppendLine(string.Format(
            "  yaw rate reaches 63% of settled after {0:F2} s   settled {1:F2} deg/s   (peak {2:F2} deg/s at {3:F1} s -- a peak well above settled means the 63% time is flattered by her entry speed)",
            t63, yawSteady, yawPeak, yawPeakT));
        sb.AppendLine(string.Format(
            "  circle {0:F0} m across = {1:F2} L from speed / yaw rate;  {2:F0} m = {3:F2} L from a circle fitted to her track over the last 20 s;  full circle {4:F0} s",
            radiusRate * 2f, radiusRate * 2f / L, radiusTrack * 2f, radiusTrack * 2f / L,
            Mathf.Abs(yawSteady) > 1e-3f ? 360f / Mathf.Abs(yawSteady) : 0f));
        sb.AppendLine(string.Format(
            "  way {0:F2} -> {1:F2} m/s ({2:F0}% lost)   drift angle {3:F1} deg (+ = her track lies to starboard of her head)   stern wheel {4:F2} rad/s",
            v0, vSteady, v0 > 0.1f ? 100f * (1f - vSteady / v0) : 0f, drift, wS));
        sb.AppendLine(string.Format(
            "  heel steady {0:F2} deg = {1:F2} deg {2}   peak {3:F2} deg = {4:F2} deg {5} at {6:F1} s   v x omega {7:F2} m/s2   roughness {8:F3}",
            heelSteady, Mathf.Abs(heelSteady), heelSteady * side >= 0f ? "OUTWARD" : "INWARD",
            heelPeak, Mathf.Abs(heelPeak), heelPeak * side >= 0f ? "OUTWARD" : "INWARD", heelPeakT,
            vSteady * Mathf.Abs(yawSteady) * Mathf.Deg2Rad, roughSum / n));
        sb.AppendLine(string.Format("  helm amidships: yaw rate under 10% of settled after {0:F2} s{1}",
            last, last >= 14.9f ? "   !! never within the 15 s watched" : ""));
        float across = Mathf.Max(radiusRate, radiusTrack) * 2f / L;
        sb.AppendLine("  GATE circle <= 3.5 L: " + across.ToString("F2") + " L (the larger of the two)  " + Verdict(across > 0f && across <= 3.5f)
            + "     GATE heel <= 8 deg: peak " + Mathf.Abs(heelPeak).ToString("F1") + "  " + Verdict(Mathf.Abs(heelPeak) <= 8f)
            + "     GATE turn builds in about 0.5 s: " + t63.ToString("F2") + " s (read it)");
    }

    static double Det3(double a, double b, double c, double d, double e, double f, double g, double h, double i)
    {
        return a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
    }

    /// Kasa least-squares circle through a track, about its own centroid so
    /// the sums stay small kilometres from the origin. 0 if it cannot be fit.
    static float CircleRadius(List<Vector2> pts)
    {
        int n = pts.Count;
        if (n < 8) return 0f;
        double mx = 0, mz = 0;
        for (int i = 0; i < n; i++) { mx += pts[i].x; mz += pts[i].y; }
        mx /= n; mz /= n;
        double sxx = 0, sxz = 0, szz = 0, sx = 0, sz = 0, sxr = 0, szr = 0, sr = 0;
        for (int i = 0; i < n; i++)
        {
            double x = pts[i].x - mx, z = pts[i].y - mz, r = x * x + z * z;
            sxx += x * x; sxz += x * z; szz += z * z; sx += x; sz += z;
            sxr += x * r; szr += z * r; sr += r;
        }
        double det = Det3(sxx, sxz, sx, sxz, szz, sz, sx, sz, n);
        if (System.Math.Abs(det) < 1e-9) return 0f;
        double A = Det3(sxr, sxz, sx, szr, szz, sz, sr, sz, n) / det;
        double B = Det3(sxx, sxr, sx, sxz, szr, sz, sx, sr, n) / det;
        double C = Det3(sxx, sxz, sxr, sxz, szz, szr, sx, sz, sr) / det;
        double r2 = C + 0.25 * (A * A + B * B);
        return r2 > 0 ? (float)System.Math.Sqrt(r2) : 0f;
    }

    // ----------------------------------------------------- SWAY / STORM --

    /// The calm top speed DRIVE measured, if it has been run on this machine;
    /// otherwise the authored one off the live component, and says which.
    float CalmTop(out string source)
    {
        string path = PathFor("drive");
        try
        {
            if (System.IO.File.Exists(path))
            {
                string[] lines = System.IO.File.ReadAllLines(path);
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    if (!lines[i].StartsWith("TOP_SPEED_MEASURED")) continue;
                    float v;
                    if (float.TryParse(lines[i].Substring("TOP_SPEED_MEASURED".Length).Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out v) && v > 0.5f)
                    {
                        source = "MEASURED by the last DRIVE run (" + path + ", "
                            + System.IO.File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm") + ")";
                        return v;
                    }
                }
            }
        }
        catch (System.Exception) { }
        source = "AUTHORED PaddleDrive.topSpeed -- no DRIVE run on file, so this is not a measured calm speed";
        return drive.TopSpeed;
    }

    IEnumerator RunLegs(float hs, bool storm)
    {
        string source;
        float calmTop = CalmTop(out source);
        float freeboard = data.depth - data.draft;
        float[] offs = { 0f, 90f, 180f };
        string[] labels = { "with the swell", "beam-on       ", "into the swell" };
        string[] brief = { "down", "beam", "up  " };

        sb.AppendLine();
        sb.AppendLine((storm ? "STORM" : "SWAY") + "  (asked local Hs " + hs.ToString("F1")
            + ", full ahead, three legs of 45 s after a 12 s run-up, each from the rim of the clear disc through its centre)");
        sb.AppendLine("  calm top speed " + calmTop.ToString("F2") + " m/s -- " + source);
        if (storm)
            sb.AppendLine("  waist freeboard (depth - draft) " + freeboard.ToString("F2")
                + " m: green water below is in THOSE units, off MaxDeckImmersion (worst half-strip, water level over its own deck edge)");

        float worstRollRms = 0f, headPct = 0f;
        bool anyGreen = false;
        var table = new StringBuilder();
        for (int leg = 0; leg < 3; leg++)
        {
            // A later instant each leg: three different stretches of the same
            // pinned sea, the same three on every run.
            yield return SetSea(hs, PinBase + 60.0 * leg);
            float heading = swellBearing + offs[leg];
            Warp(heading, ClearRadius - 20f);
            yield return AfterWarp();
            if (failure != null) yield break;
            motor.ThrottleOrder = 1f;
            HoldHeading(heading);
            yield return new WaitForSeconds(12f);

            var rolls = new List<float>(); var pitches = new List<float>();
            var roughs = new List<float>(); var decks = new List<float>();
            var gaugeH = new List<float>();
            float t = 0f, way = 0f, vy2 = 0f, hsSum = 0f, offCourse = 0f;
            float dipSMin = float.MaxValue, dipSMax = float.MinValue;
            int n = 0, outS = 0, outAny = 0, green = 0, greenHalf = 0, greenOne = 0, flat = 0;
            float greenRun = 0f, greenRunMax = 0f, burialMax = 0f, subRawMax = 0f, wS = 0f;
            float shoalest = float.NegativeInfinity;
            var terrain = Island.TerrainHeight;
            while (t < 45f)
            {
                yield return new WaitForFixedUpdate();
                float dt = Time.fixedDeltaTime;
                t += dt;
                rolls.Add(Heel()); pitches.Add(Trim());
                if (meter != null) roughs.Add(meter.Roughness01);
                float deck = body.MaxDeckImmersion;
                decks.Add(deck);
                if (deck > 0f) { green++; greenRun += dt; if (greenRun > greenRunMax) greenRunMax = greenRun; }
                else greenRun = 0f;
                if (deck > 0.5f * freeboard) greenHalf++;
                if (deck > freeboard) greenOne++;
                if (body.BurialDepth > burialMax) burialMax = body.BurialDepth;
                if (body.SubmersionRaw > subRawMax) subRawMax = body.SubmersionRaw;
                if ((rb.rotation * Vector3.up).y < 0.5f) flat++;
                float ds = drive.WheelDip;
                if (ds < dipSMin) dipSMin = ds; if (ds > dipSMax) dipSMax = ds;
                if (ds <= 0f) { outS++; outAny++; }
                wS += drive.WheelRate;
                way += Way();
                vy2 += rb.linearVelocity.y * rb.linearVelocity.y;
                hsSum += sea.SeaHsAt(new Vector2(rb.position.x, rb.position.z));
                offCourse += Mathf.Abs(Mathf.DeltaAngle(HeadingDeg(), heading));
                if (GaugeValid()) gaugeH.Add(gauge.sample.height);
                if (terrain != null) shoalest = Mathf.Max(shoalest, terrain(rb.position.x, rb.position.z));
                n++;
            }
            // Stop her between legs: the next SetSea takes 11 s, and at full
            // ahead with nobody steering that is 170 m further out of a disc
            // that was only checked to its rim.
            motor.AutopilotTarget = null;
            motor.ThrottleOrder = 0f;
            if (n == 0) { Fail("INSTRUMENT FAILURE: no fixed steps in leg " + leg); yield break; }

            float rollRms = Std(rolls), meanWay = way / n, pct = calmTop > 0.1f ? 100f * meanWay / calmTop : 0f;
            float deckMax = MaxOf(decks);
            if (rollRms > worstRollRms) worstRollRms = rollRms;
            if (leg == 2) headPct = pct;
            if (green > 0) anyGreen = true;
            // An attitude of exactly zero in a seaway is the probe reporting
            // its own setup, not the sea (the StallProbe trap).
            if (rollRms < 1e-3f && Std(pitches) < 1e-3f)
                sb.AppendLine("  !! leg " + brief[leg] + ": roll and pitch are both dead flat -- she is not in the waves; this leg measured nothing");

            table.AppendLine(string.Format(
                "  {0}  hdg {1,5:F0}   roll {2,5:F2} / {3,5:F2} / {4,5:F2}   pitch {5,5:F2} / {6,5:F2} / {7,5:F2}   heave {8,5:F2} m/s   way {9,5:F2} = {10,3:F0}%   rough {11:F3} (p90 {12:F3})",
                labels[leg], Mathf.Repeat(heading, 360f),
                rollRms, Pct(Abs(rolls), 0.9f), MaxOf(Abs(rolls)),
                Std(pitches), Pct(Abs(pitches), 0.9f), MaxOf(Abs(pitches)),
                Mathf.Sqrt(vy2 / n), meanWay, pct, Mean(roughs), Pct(roughs, 0.9f)));
            if (!storm)
                table.AppendLine(string.Format(
                    "      deck: MaxDeckImmersion max {0:F2} m, over the edge on {1:F1}% of steps",
                    deckMax, 100f * green / n));
            else
                table.AppendLine(string.Format(
                    "      green water: worst {0:F2} freeboards ({1:F2} m), p90 {2:F2};  over the edge {3:F1}% of steps, past half a freeboard {4:F1}%, past a whole one {5:F1}%;  longest spell {6:F1} s;  BurialDepth max {7:F2} m;  SubmersionRaw max {8:F2};  steps past 60 deg of heel {9}",
                    deckMax / freeboard, deckMax, Pct(decks, 0.9f) / freeboard,
                    100f * green / n, 100f * greenHalf / n, 100f * greenOne / n, greenRunMax, burialMax, subRawMax, flat));
            table.AppendLine(string.Format(
                "      stern wheel: dip {0:F2}..{1:F2} m (design {2:F2});  fully out of the water {3:F1}% of steps;  mean revs {4:F2} of {5:F2}",
                dipSMin, dipSMax, data.wheelDesignDip,
                100f * outS / n, wS / n, drive.MaxRate));
            table.AppendLine(string.Format(
                "      sea: envelope says Hs {0:F2} along the leg;  4 x rms of a raw point gauge riding at her origin {1:F2} m (45 s of one point: +-25% at best);  off course {2:F1} deg mean;  seabed never above {3:F0} m{4}",
                hsSum / n, 4f * Std(gaugeH), offCourse / n, shoalest,
                shoalest > -Mathf.Max(20f, hs / 0.55f + 4f) ? "   !! SHOAL WATER -- the depth cap was in this leg" : ""));
        }
        motor.ThrottleOrder = 0f;

        sb.AppendLine();
        sb.AppendLine("  leg              heading   roll rms / p90 / max deg    pitch rms / p90 / max deg   heave-rate rms   mean way, % of calm   roughness mean");
        sb.Append(table.ToString());

        if (!storm)
        {
            sb.AppendLine();
            sb.AppendLine("  THE BRIG, as recorded by HandlingProbe sway (her probes are hull-filtered; the steamer's strips are not):");
            for (int leg = 0; leg < 3; leg++)
                sb.AppendLine(string.Format("  {0}            roll {1,5:F2} / {2,5:F2} / {3,5:F2}                                                              rough {4:F3}",
                    labels[leg], BrigRollRms[leg], BrigRollP90[leg], BrigRollMax[leg], BrigRough[leg]));
            sb.AppendLine();
            sb.AppendLine("  GATE roll rms < 2.5 deg at local Hs 3.5: worst leg " + worstRollRms.ToString("F2") + "  " + Verdict(worstRollRms < 2.5f)
                + "     GATE >= 80% of calm speed into a head sea: " + headPct.ToString("F0") + "%  " + Verdict(headPct >= 80f));
            sb.AppendLine("  (the green-water gate is written at severity 0.60 = global Hs " + sea.HsAt(0.60f).ToString("F2")
                + " m, not at this sea; here she " + (anyGreen ? "DID take water over the deck edge" : "stayed dry") + ")");
        }
    }

    // ------------------------------------------------------------ stats --

    static float Mean(List<float> a)
    {
        if (a.Count == 0) return 0f;
        float s = 0f;
        for (int i = 0; i < a.Count; i++) s += a[i];
        return s / a.Count;
    }

    /// RMS about the MEAN: a steady list or a steady trim is not sway.
    static float Std(List<float> a)
    {
        if (a.Count == 0) return 0f;
        float m = Mean(a), ss = 0f;
        for (int i = 0; i < a.Count; i++) ss += (a[i] - m) * (a[i] - m);
        return Mathf.Sqrt(ss / a.Count);
    }

    static List<float> Abs(List<float> a)
    {
        var r = new List<float>(a.Count);
        for (int i = 0; i < a.Count; i++) r.Add(Mathf.Abs(a[i]));
        return r;
    }

    static float MaxOf(List<float> a)
    {
        if (a.Count == 0) return 0f;
        float m = a[0];
        for (int i = 1; i < a.Count; i++) if (a[i] > m) m = a[i];
        return m;
    }

    static float Pct(List<float> a, float p)
    {
        if (a.Count == 0) return 0f;
        var c = a.ToArray();
        System.Array.Sort(c);
        return c[Mathf.Clamp(Mathf.RoundToInt(p * (c.Length - 1)), 0, c.Length - 1)];
    }
}
