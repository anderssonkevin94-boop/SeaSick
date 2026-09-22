using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.World;

/// HOW DOES SHE HANDLE. Every other ship probe asks whether she survives the
/// water; none of them asks what she is like to steer, and nothing at all has
/// read `SmoothnessMeter.Roughness01` since it was hand-verified on 08-17.
/// This is the instrument for the helm and for the sway:
///
///   DECAY    flat water, a roll kick and a pitch kick: period and damping
///            ratio from the first two half-cycle peaks. The number that says
///            whether she settles after a wave or goes on rocking.
///   STRAIGHT the everyday sea on three headings about the swell (down /
///            beam / up): roll and pitch RMS, p90, max, way made, how she
///            rides, and the roughness the crew's stomachs are charged.
///   TURN     full helm from a straight run: how long the turn takes to
///            build, what it settles at, the circle in hull lengths, what it
///            costs in speed, drift, heel -- then helm amidships, and how long
///            she takes to stop swinging.
///   DRIVE    time to 90% of her top speed, and how far she carries her way.
///
/// Three launchers so one acceptance run fits one play session:
/// `Sway` (decay + straight), `Helm` (turn + drive), `Controls` (rungs 0 and
/// 19, and the brig beam-on at severity 0.75). Play mode, Sea.unity. Writes
/// /tmp/seasick-handling-<mode>.txt.
///
/// The helm is driven by disabling `HelmInput` and writing `ShipMotor.Rudder`
/// -- HelmInput reasserts both orders every frame -- so the input ramp is NOT
/// in these numbers. They are the hull's response to a step.
public class HandlingProbe : MonoBehaviour
{
    static string mode = "sway";

    public static void Sway() => Launch("sway");
    public static void Helm() => Launch("helm");
    public static void Controls() => Launch("controls");
    public static void Trace() => Launch("trace");
    public static void Execute() => Launch("sway");

    static void Launch(string m)
    {
        if (!Application.isPlaying) { Debug.LogError("HandlingProbe: play mode only"); return; }
        var old = FindAnyObjectByType<HandlingProbe>();
        if (old != null) Destroy(old.gameObject);
        mode = m;
        new GameObject("HandlingProbe").AddComponent<HandlingProbe>();
    }

    // Waves are untouched below 8 m of water and capped at 0.55 x depth, so
    // 12 m is still clear of the bottom for the everyday sea; the heavy-water
    // leg wants 25.
    static readonly float[] DepthLadder = { -60f, -40f, -25f, -12f };
    const float CalmHs = 0.15f;
    const float ClearRadius = 480f;

    ShipMotor motor;
    Rigidbody rb;
    BuoyantBody buoyant;
    SmoothnessMeter meter;
    SeaStateController sea;
    Shipyard yard;
    Vector3 spot;
    StringBuilder sb;

    IEnumerator Start()
    {
        sb = new StringBuilder();
        motor = FindAnyObjectByType<ShipMotor>();
        var helm = FindAnyObjectByType<HelmInput>();
        sea = SeaStateController.Instance;
        if (motor == null || sea == null) { Debug.LogError("HandlingProbe: no ship or no sea"); yield break; }
        rb = motor.GetComponent<Rigidbody>();
        buoyant = motor.GetComponent<BuoyantBody>();
        meter = motor.GetComponent<SmoothnessMeter>();
        yard = motor.GetComponent<Shipyard>();
        if (helm != null) helm.enabled = false;

        // Every voyage starts tied up at home, and the anchor is a SPRING to
        // the drop point: warp her 11 km with it still down and it hauls her
        // back at 190 m/s and throws her 300 m into the air. The first
        // baseline measured exactly that and called it a roll of 141 degrees.
        var anchor = motor.GetComponent<AnchorController>();
        if (anchor != null) anchor.CastOff();
        motor.Anchored = false;
        motor.MooringHeading = null;
        while (!OceanSampler.Ready) yield return null;

        sb.AppendLine("=== HandlingProbe: " + mode + " ===");
        yield return FindDeepWater();
        if (!foundWater) { Debug.LogError("HandlingProbe: no deep water"); yield break; }
        Header();

        if (mode == "trace")
        {
            // What the warp itself does to her, twice a second: the first
            // baseline came back with a 141 degree roll in a flat calm.
            // The leg that made 1.2 m/s at full ahead: running with the swell,
            // which here is bow-on to the wind sea. Twice a second, every force
            // that could be holding her, as an acceleration.
            float hsT = ForceLocalHs(sea.HsAt(SeaStateController.SevNormal));
            float headingT = swellBearing;
            sb.AppendLine("local Hs " + hsT.ToString("F2") + "  heading " + headingT.ToString("F0")
                + "  swell runs " + BearingOf(sea.SwellDirection).ToString("F0")
                + "  wind sea runs " + BearingOf(sea.WindDirection).ToString("F0"));
            Warp(headingT, 400f);
            motor.ThrottleOrder = 1f;
            HoldHeading(headingT);
            float m = rb.mass;
            for (int i = 0; i < 80; i++)
            {
                Vector3 f = motor.transform.forward; f.y = 0f; f.Normalize();
                sb.AppendLine(string.Format(
                    "t {0,4:F1} way {1,5:F1} side {2,5:F1} roll {3,6:F1} pitch {4,5:F1} offCrs {5,5:F1} | surf {6,5:F2} latWave {7,5:F2} "
                    + "plow {8,5:F2} dragF {9,5:F2} dragLat {10,5:F2} | rail {11,5:F2} sub {12:F2} buried {13} broach {14:F2} resist {15:F2} thr {16:F2}",
                    i * 0.5f, Way, Vector3.Dot(rb.linearVelocity, motor.transform.right), Roll, Pitch,
                    Mathf.DeltaAngle(motor.Heading, headingT),
                    motor.SurfAccel, motor.LateralWaveAccel,
                    Vector3.Dot(buoyant.DebugPlowForward, f) / m, Vector3.Dot(buoyant.DebugDragForward, f) / m,
                    buoyant.DebugDragLateral.magnitude / m,
                    buoyant.MaxRailImmersion, buoyant.Submersion, buoyant.Buried ? 1 : 0,
                    motor.Broach01, motor.SeaResistance01, motor.Throttle));
                yield return new WaitForSeconds(0.5f);
            }
        }
        else if (mode == "sway")
        {
            yield return Decay();
            yield return Straights(SeaStateController.SevNormal);
        }
        else if (mode == "helm")
        {
            yield return Turn(1f, CalmHs, "calm, full ahead");
            yield return Turn(0.5f, CalmHs, "calm, half ahead");
            yield return TurnInSea(SeaStateController.SevNormal);
            yield return Drive();
        }
        else
        {
            int home = yard != null ? yard.NodeIndex : -1;
            foreach (int node in new[] { 0, 19 })
            {
                if (yard == null) break;
                yard.Apply(node);
                yield return new WaitForSeconds(1f);
                sb.AppendLine();
                sb.AppendLine("##### rung " + node + "  " + yard.Node.label);
                Header();
                yield return Decay();
                yield return Turn(1f, CalmHs, "calm, full ahead");
            }
            if (yard != null && home >= 0) { yard.Apply(home); yield return new WaitForSeconds(1f); }
            sb.AppendLine();
            sb.AppendLine("##### rung " + home + " in heavy water");
            yield return Straight(90f, "beam", 0.75f, true);
        }

        FlatForHull(false);
        motor.AutopilotTarget = null;
        motor.Rudder = 0f;
        motor.ThrottleOrder = 0f;
        sea.ReleaseForce();
        if (helm != null) helm.enabled = true;
        System.IO.File.WriteAllText("/tmp/seasick-handling-" + mode + ".txt", sb.ToString());
        Debug.Log("HandlingProbe\n" + sb);
        Destroy(gameObject);
    }

    // ------------------------------------------------------------ setup --

    /// Deep water she can actually SAIL in. The coarse 5x5 grid every other
    /// probe uses has 400 m between its points, and the first spot it passed
    /// had a pinnacle rising to 15 m under the surface 200 m from the centre:
    /// she ran aground on it at 12 m/s, fourteen seconds into the leg, and
    /// the probe reported a ship that could not make way.
    ///
    /// The sea floor is a 180 m abyss studded with rocks and islets every few
    /// hundred metres -- one random point in six is shallower than 12 m -- so
    /// there is NO clear kilometre anywhere, on any grid fine enough to mean
    /// it. What there is, is clear DISCS. So every leg starts on the rim and
    /// sails THROUGH the centre (`Warp`'s second argument), and a disc of
    /// `ClearRadius` checked on a 40 m grid is all the water a run needs.
    bool foundWater;
    /// Read ONCE. The swell turns on its own clock, and legs laid off a
    /// bearing that moved would leave the corridors that were checked.
    float swellBearing;
    IEnumerator FindDeepWater()
    {
        foundWater = false;
        var h = Island.TerrainHeight;
        if (h == null) yield break;

        var offsets = new List<Vector2>();
        for (float x = -ClearRadius; x <= ClearRadius; x += 40f)
            for (float z = -ClearRadius; z <= ClearRadius; z += 40f)
                if (x * x + z * z <= ClearRadius * ClearRadius) offsets.Add(new Vector2(x, z));
        float swell = swellBearing = BearingOf(sea.SwellDirection);

        for (int d = 0; d < DepthLadder.Length; d++)
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
                    spot = c; foundWater = true;
                    sb.AppendLine("clear water at " + spot.ToString("F0") + ": a " + ClearRadius.ToString("F0")
                        + " m disc, shallowest seabed " + shallowest.ToString("F0") + " m on a 40 m grid; swell runs "
                        + swell.ToString("F0") + " deg");
                    yield break;
                }
        }
    }

    /// What she IS, read off the live components: every number below is a
    /// report on these, and a probe that assumed them would be describing the
    /// code it was written against.
    void Header()
    {
        Vector3 it = rb.inertiaTensor;
        sb.AppendLine(string.Format(
            "hull L {0:F1} m  mass {1:F0} kg  GM {2:F2}  inertia pitch/yaw/roll {3:E2} {4:E2} {5:E2}",
            motor.HullLength, rb.mass, yard != null && yard.Load != null ? yard.Load.GMm : 0f,
            it.x, it.y, it.z));
        sb.AppendLine(string.Format(
            "motor  maxSpeed {0:F2}  accel {1:F2}  maxTurn {2:F1} deg/s  keelGrip {3:F2}  turnHeel {4:F2}"
            + "  rollLimit {5:F0}  pitchLimit {6:F0}",
            motor.MaxSpeed, motor.AccelerationNow, motor.MaxTurnRate,
            ReadFloat(motor, "keelGrip"), ReadFloat(motor, "turnHeel"),
            ReadFloat(motor, "rollLimit"), ReadFloat(motor, "pitchLimit")));
        sb.AppendLine(string.Format(
            "water  angularDragTorque {0:E2}  linearDrag {1:E2}  quadraticDrag {2:E2}   crew labour {3:F2}",
            ReadFloat(buoyant, "angularDragTorque"), ReadFloat(buoyant, "linearDrag"),
            ReadFloat(buoyant, "quadraticDrag"), motor.OarPower01));
        sb.AppendLine(string.Format(
            "damping  target zeta roll {0:F2} pitch {1:F2}   stiffness roll {2:E2} pitch {3:E2} N m/rad",
            ReadFloat(buoyant, "rollDampingRatio"), ReadFloat(buoyant, "pitchDampingRatio"),
            buoyant.RollStiffness, buoyant.PitchStiffness));
        if (meter != null)
            sb.AppendLine(string.Format(
                "meter  ceilings heave {0:F1} m/s  pitch {1:F0}  roll {2:F0} deg/s  lateral {3:F1} m/s2",
                ReadFloat(meter, "heaveRateCeiling"), ReadFloat(meter, "pitchRateCeiling"),
                ReadFloat(meter, "rollRateCeiling"), ReadFloat(meter, "lateralAccelCeiling")));
    }

    static float ReadFloat(Object c, string field)
    {
        if (c == null) return float.NaN;
        var f = c.GetType().GetField(field,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public);
        return f != null && f.FieldType == typeof(float) ? (float)f.GetValue(c) : float.NaN;
    }

    /// Pin the sea by the height of the water SHE is in. `ForceHs` sets the
    /// global figure and `RegionField` multiplies it by place -- about 2.9x
    /// in the open water this probe has to go to for depth -- so a forced
    /// "everyday" sea out here was a gale. Solve for the global value that
    /// lands the local one, and report what was actually reached: the calm
    /// end has a floor.
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

    /// FLAT water as the hull feels it. The sea has a floor -- severity 0 is
    /// still Hs 0.43, times the region -- and the first decay test was run
    /// in a 1.24 m sea that rolled her 20 degrees on its own, which is a
    /// wave-response measurement wearing a decay test's name. The physics
    /// driver already owns a per-band "how much of this does a hull feel";
    /// zero both bands and the kick is the only thing that moves her. The
    /// drawn sea is untouched, and so is the surf term, which reads the
    /// surface itself -- small at this height, and it has no roll in it.
    float feel0 = float.NaN, feel1 = float.NaN;
    void FlatForHull(bool flat)
    {
        var drivers = FindObjectsByType<OceanPhysicsDriver>(FindObjectsInactive.Include);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public;
        var f0 = typeof(OceanPhysicsDriver).GetField("hullFeelsCascade0", flags);
        var f1 = typeof(OceanPhysicsDriver).GetField("hullFeelsCascade1", flags);
        if (drivers.Length == 0 || f0 == null || f1 == null)
        {
            sb.AppendLine("!! FlatForHull could not reach the driver: drivers " + drivers.Length
                + " f0 " + (f0 != null) + " f1 " + (f1 != null) + " -- the 'flat' legs below are NOT flat");
            return;
        }
        if (float.IsNaN(feel0)) { feel0 = (float)f0.GetValue(drivers[0]); feel1 = (float)f1.GetValue(drivers[0]); }
        foreach (var d in drivers)
        {
            f0.SetValue(d, flat ? 0f : feel0);
            f1.SetValue(d, flat ? 0f : feel1);
        }
        if (mode == "trace")
            sb.AppendLine("FlatForHull(" + flat + "): " + drivers.Length + " driver(s), was "
                + feel0.ToString("F2") + "/" + feel1.ToString("F2") + ", now "
                + ((float)f0.GetValue(drivers[0])).ToString("F2") + "/" + ((float)f1.GetValue(drivers[0])).ToString("F2"));
    }

    /// `back` metres astern of the centre, so the leg runs through it.
    void Warp(float headingDeg, float back = 0f)
    {
        motor.AutopilotTarget = null;
        motor.Rudder = 0f;
        Vector3 dir = Quaternion.Euler(0f, headingDeg, 0f) * Vector3.forward;
        rb.position = new Vector3(spot.x, 3f, spot.z) - dir * back;
        rb.rotation = Quaternion.Euler(0f, headingDeg, 0f);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    void HoldHeading(float headingDeg)
    {
        Vector3 dir = Quaternion.Euler(0f, headingDeg, 0f) * Vector3.forward;
        motor.AutopilotTarget = rb.position + dir * 50000f;
    }

    static float Signed(float deg) => deg > 180f ? deg - 360f : deg;
    float Roll => Signed(motor.transform.eulerAngles.z);
    float Pitch => Signed(motor.transform.eulerAngles.x);
    float Way => Vector3.Dot(rb.linearVelocity, motor.transform.forward);

    static float BearingOf(Vector2 run) => Mathf.Atan2(run.x, run.y) * Mathf.Rad2Deg;

    // ------------------------------------------------------------ decay --

    IEnumerator Decay()
    {
        ForceLocalHs(CalmHs);
        FlatForHull(true);
        motor.ThrottleOrder = 0f;
        Warp(0f);
        yield return new WaitForSeconds(10f);
        sb.AppendLine();
        sb.AppendLine("DECAY  (water flat as the hull feels it, no way on; a kick, then hands off)");
        yield return Kick(true);
        yield return new WaitForSeconds(4f);
        yield return Kick(false);
        FlatForHull(false);
    }

    IEnumerator Kick(bool roll)
    {
        float rate = (roll ? 10f : 6f) * Mathf.Deg2Rad;
        Vector3 it = rb.inertiaTensor;
        float rest = roll ? Roll : Pitch;
        rb.AddRelativeTorque(roll ? Vector3.forward * (it.z * rate) : Vector3.right * (it.x * rate),
            ForceMode.Impulse);

        var ts = new List<float>(); var xs = new List<float>();
        float t = 0f;
        while (t < 22f)
        {
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
            ts.Add(t); xs.Add((roll ? Roll : Pitch) - rest);
        }

        // Half-cycle peaks: local extrema of alternating sign, above a floor
        // that the residual chop cannot reach.
        const float Floor = 0.35f;
        var peakT = new List<float>(); var peakA = new List<float>();
        for (int i = 2; i < xs.Count - 2; i++)
        {
            float a = Mathf.Abs(xs[i]);
            if (a < Floor) continue;
            if (a >= Mathf.Abs(xs[i - 1]) && a >= Mathf.Abs(xs[i + 1])
                && a > Mathf.Abs(xs[i - 2]) && a > Mathf.Abs(xs[i + 2]))
            {
                if (peakA.Count > 0 && Mathf.Sign(xs[i]) == Mathf.Sign(peakA[peakA.Count - 1]))
                    continue;
                peakT.Add(ts[i]); peakA.Add(xs[i]);
            }
        }

        string name = roll ? "roll " : "pitch";
        if (peakA.Count == 0) { sb.AppendLine("  " + name + "  no peak above the floor"); yield break; }
        var line = new StringBuilder("  " + name + "  peaks");
        for (int i = 0; i < peakA.Count && i < 8; i++) line.Append(" " + peakA[i].ToString("F2"));
        line.Append(" deg   (" + peakA.Count + " half-cycles above " + Floor.ToString("F2") + ")");
        sb.AppendLine(line.ToString());
        if (peakA.Count >= 2)
        {
            float ratio = Mathf.Abs(peakA[1] / peakA[0]);
            float delta = -Mathf.Log(Mathf.Max(1e-4f, ratio));       // per HALF cycle
            float zeta = delta / Mathf.Sqrt(Mathf.PI * Mathf.PI + delta * delta);
            float period = 2f * (peakT[1] - peakT[0]);
            sb.AppendLine(string.Format("  {0}  period {1:F2} s   zeta {2:F2}   (second peak is {3:F0}% of the first)",
                name, period, zeta, ratio * 100f));
        }
        else
            sb.AppendLine("  " + name + "  one peak only: she did not swing back past the floor (zeta > ~0.6)");
    }

    // --------------------------------------------------------- straight --

    IEnumerator Straights(float severity)
    {
        sb.AppendLine();
        sb.AppendLine("STRAIGHT  (local Hs " + sea.HsAt(severity).ToString("F2") + " = severity "
            + severity.ToString("F2") + " as home waters have it, full ahead, 40 s each after a 14 s run-up)");
        sb.AppendLine("  heading   off-swell off-wind   roll rms/p90/max      pitch rms/p90/max     way    ride    rough mean/p90");
        yield return Straight(0f, "down", severity, false);
        yield return Straight(90f, "beam", severity, false);
        yield return Straight(180f, "up  ", severity, false);
    }

    /// `offSwell` is degrees between her heading and the way the swell RUNS:
    /// 0 is running with it, 180 is bow-on.
    IEnumerator Straight(float offSwell, string label, float severity, bool header)
    {
        float hsHere = ForceLocalHs(sea.HsAt(severity));
        if (header)
        {
            sb.AppendLine("STRAIGHT  (local Hs " + hsHere.ToString("F2") + " = severity " + severity.ToString("F2") + ")");
            sb.AppendLine("  heading   off-swell off-wind   roll rms/p90/max      pitch rms/p90/max     way    ride    rough mean/p90");
        }
        float heading = swellBearing + offSwell;
        Warp(heading, 400f);
        motor.ThrottleOrder = 1f;
        HoldHeading(heading);
        yield return new WaitForSeconds(14f);

        var rolls = new List<float>(); var pitches = new List<float>();
        var roughs = new List<float>();
        float way = 0f, ride = 0f, offCourse = 0f, resist = 0f, broach = 0f; int n = 0;
        float shoalest = float.NegativeInfinity;
        float t = 0f;
        while (t < 40f)
        {
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
            rolls.Add(Roll); pitches.Add(Pitch);
            offCourse += Mathf.Abs(Mathf.DeltaAngle(motor.Heading, heading));
            resist += motor.SeaResistance01;
            broach = Mathf.Max(broach, motor.Broach01);
            shoalest = Mathf.Max(shoalest, Island.TerrainHeight(rb.position.x, rb.position.z));
            if (meter != null) roughs.Add(meter.Roughness01);
            way += Way; ride += rb.position.y - buoyant.MeanWaterHeight; n++;
        }
        float offWind = Mathf.Abs(Mathf.DeltaAngle(heading, BearingOf(sea.WindDirection)));
        sb.AppendLine(string.Format("  {0}      {1,6:F0} {2,8:F0}     {3}     {4}   {5,5:F1}  {6,6:F2}    {7:F3} / {8:F3}",
            label, offSwell, offWind, Dist(rolls), Dist(pitches), way / n, ride / n,
            Mean(roughs), Pct(roughs, 0.9f)));
        sb.AppendLine(string.Format(
            "            off course {0:F1} deg mean   sea resistance {1:F2}   worst broach {2:F2}   engine {3:F2}   Hs here {4:F2}   seabed never above {5:F0} m{6}",
            offCourse / n, resist / n, broach, motor.Throttle, motor.SeaHs, shoalest,
            shoalest > -20f ? "   !! SHOAL WATER -- this leg measured the bottom" : ""));
        motor.AutopilotTarget = null;
    }

    // ------------------------------------------------------------- turn --

    class TurnResult
    {
        public float v0, vSteady, yawSteady, build90, coast10, drift, heelSteady, heelPeak, rough, lateral;
    }

    IEnumerator Turn(float throttle, float hs, string label)
    {
        ForceLocalHs(hs);
        FlatForHull(true);
        label += ", water flat as the hull feels it";
        bool first = label.StartsWith("calm, full");
        var r = new TurnResult();
        yield return TurnCore(throttle, r);
        FlatForHull(false);
        if (first || mode == "controls")
        {
            sb.AppendLine();
            sb.AppendLine("TURN  (full helm from a straight run, 30 s, then helm amidships)");
        }
        Report(label, r);
    }

    IEnumerator TurnInSea(float severity)
    {
        float hs = ForceLocalHs(sea.HsAt(severity));
        var r = new TurnResult();
        yield return TurnCore(1f, r);
        Report("severity " + severity.ToString("F2") + " (local Hs " + hs.ToString("F2") + "), full ahead", r);
    }

    void Report(string label, TurnResult r)
    {
        float radius = r.yawSteady > 1e-3f ? r.vSteady / (r.yawSteady * Mathf.Deg2Rad) : 0f;
        sb.AppendLine("  " + label);
        sb.AppendLine(string.Format(
            "    builds to 90% in {0:F2} s   steady {1:F1} deg/s   circle {2:F0} m across = {3:F1} L   full circle {4:F0} s",
            r.build90, r.yawSteady, radius * 2f, radius * 2f / motor.HullLength,
            r.yawSteady > 1e-3f ? 360f / r.yawSteady : 0f));
        sb.AppendLine(string.Format(
            "    way {0:F1} -> {1:F1} m/s ({2:F0}% lost)   drift {3:F1} deg   heel steady {4:F1} peak {5:F1} deg",
            r.v0, r.vSteady, r.v0 > 0.1f ? 100f * (1f - r.vSteady / r.v0) : 0f,
            r.drift, r.heelSteady, r.heelPeak));
        sb.AppendLine(string.Format(
            "    v*omega {0:F2} m/s2   roughness {1:F3}   helm amidships: swing under 10% after {2:F2} s",
            r.lateral, r.rough, r.coast10));
    }

    IEnumerator TurnCore(float throttle, TurnResult r)
    {
        float heading = swellBearing;
        Warp(heading, 300f);
        motor.ThrottleOrder = throttle;
        HoldHeading(heading);
        yield return new WaitForSeconds(16f);
        r.v0 = Way;

        motor.AutopilotTarget = null;
        motor.Rudder = 1f;
        var ts = new List<float>(); var yaws = new List<float>();
        float t = 0f, vSum = 0f, yawSum = 0f, driftSum = 0f, heelSum = 0f, roughSum = 0f; int n = 0;
        while (t < 30f)
        {
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
            float yaw = rb.angularVelocity.y * Mathf.Rad2Deg;
            ts.Add(t); yaws.Add(yaw);
            r.heelPeak = Mathf.Max(r.heelPeak, Mathf.Abs(Roll));
            if (t >= 20f)
            {
                vSum += rb.linearVelocity.magnitude; yawSum += Mathf.Abs(yaw);
                driftSum += Mathf.Abs(motor.DriftAngleDeg); heelSum += Roll;
                if (meter != null) roughSum += meter.Roughness01;
                n++;
            }
        }
        r.vSteady = vSum / n; r.yawSteady = yawSum / n; r.drift = driftSum / n;
        r.heelSteady = heelSum / n; r.rough = roughSum / n;
        r.lateral = r.vSteady * r.yawSteady * Mathf.Deg2Rad;
        r.build90 = -1f;
        for (int i = 0; i < yaws.Count; i++)
            if (Mathf.Abs(yaws[i]) >= 0.9f * r.yawSteady) { r.build90 = ts[i]; break; }

        motor.Rudder = 0f;
        t = 0f; r.coast10 = -1f;
        float last = 0f;
        while (t < 8f)
        {
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
            // The LAST time she was still swinging, so a wave that nudges her
            // back over the line is not mistaken for the turn having ended.
            if (Mathf.Abs(rb.angularVelocity.y) * Mathf.Rad2Deg > 0.1f * r.yawSteady) last = t;
        }
        r.coast10 = last;
    }

    // ------------------------------------------------------------ drive --

    IEnumerator Drive()
    {
        ForceLocalHs(CalmHs);
        FlatForHull(true);
        float heading = swellBearing;
        Warp(heading, 430f);
        motor.ThrottleOrder = 0f;
        yield return new WaitForSeconds(6f);

        HoldHeading(heading);
        motor.ThrottleOrder = 1f;
        float t = 0f, t50 = -1f, t90 = -1f;
        while (t < 40f)
        {
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
            float w = Way;
            if (t50 < 0f && w >= 0.5f * motor.MaxSpeed) t50 = t;
            if (t90 < 0f && w >= 0.9f * motor.MaxSpeed) { t90 = t; }
            if (t90 > 0f && t > t90 + 4f) break;
        }
        float top = Way;

        motor.ThrottleOrder = 0f;
        Vector3 from = rb.position; t = 0f;
        while (t < 90f && Way > 1f)
        {
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
        }
        float carried = Vector3.Distance(from, rb.position);
        sb.AppendLine();
        FlatForHull(false);
        sb.AppendLine("DRIVE  (water flat as the hull feels it, from a standstill; the telegraph's own ramp is included)");
        sb.AppendLine(string.Format("  to 50% of {0:F1} m/s in {1:F1} s   to 90% in {2:F1} s   reached {3:F1} m/s",
            motor.MaxSpeed, t50, t90, top));
        sb.AppendLine(string.Format("  throttle shut: under 1 m/s after {0:F1} s and {1:F0} m = {2:F1} L",
            t, carried, carried / motor.HullLength));
        motor.AutopilotTarget = null;
    }

    // ------------------------------------------------------------ stats --

    static float Mean(List<float> a)
    {
        if (a.Count == 0) return 0f;
        float s = 0f; for (int i = 0; i < a.Count; i++) s += a[i];
        return s / a.Count;
    }

    static float Pct(List<float> a, float p)
    {
        if (a.Count == 0) return 0f;
        var c = a.ToArray(); System.Array.Sort(c);
        return c[Mathf.Clamp(Mathf.RoundToInt(p * (c.Length - 1)), 0, c.Length - 1)];
    }

    /// RMS about the MEAN (a steady list is not sway), then p90 and max of the
    /// absolute angle, which is what the eye is actually shown.
    static string Dist(List<float> a)
    {
        float m = Mean(a), ss = 0f;
        var abs = new List<float>(a.Count);
        for (int i = 0; i < a.Count; i++) { ss += (a[i] - m) * (a[i] - m); abs.Add(Mathf.Abs(a[i])); }
        float rms = a.Count > 0 ? Mathf.Sqrt(ss / a.Count) : 0f;
        return string.Format("{0,5:F2} {1,5:F2} {2,5:F2}", rms, Pct(abs, 0.9f), Pct(abs, 1f));
    }
}
