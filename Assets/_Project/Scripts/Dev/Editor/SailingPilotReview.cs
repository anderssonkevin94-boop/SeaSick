using System;
using System.IO;
using SeaSick.Ship;
using UnityEditor;
using UnityEngine;

public static class SailingPilotReview
{
    [MenuItem("SeaSick/Sailing Prototype/Validate Rules")]
    public static void Validate()
    {
        int passed = 0;
        void Check(bool value, string label)
        {
            if (!value) throw new Exception("Sailing prototype: " + label);
            passed++;
        }
        Vector3 start = Vector3.zero, end = new Vector3(100, 0, 0);
        Check(SailingCourse.Clear(start, end, 3, 2, (x,z) => -30), "deep-water course");
        Check(!SailingCourse.Clear(start, end, 3, 2, null), "unknown terrain rejects");
        Check(!SailingCourse.Clear(start, end, 3, 2, (x,z) => -1), "shallow water rejects");
        Check(!SailingCourse.Clear(start, end, 3, 2, (x,z) => x > 45 && x < 55 ? 4 : -30), "island between endpoints");
        Check(!SailingCourse.Clear(start, end, 3, 2, (x,z) => z > 2 ? 4 : -30), "hull clearance");
        Check(!SailingCourse.Clear(start, new Vector3(900,0,0), 3, 2, (x,z) => -30), "range limit");
        Check(!SailingCourse.Clear(start, new Vector3(float.NaN,0,0), 3, 2, (x,z) => -30), "invalid destination");
        Check(!SailingCourse.Clear(start, end, 3, 2, (x,z) => float.NaN), "invalid terrain");
        Check(SailingCourse.SpeedOrder(100, 5, 0, 15, 0) > 0, "cruise order");
        Check(SailingCourse.SpeedOrder(5, 5, 0, 15, 0) == 0, "arrival stops");
        Check(SailingCourse.SpeedOrder(12, 5, 12, 15, 0) == 0, "early coasting");
        Check(SailingCourse.SpeedOrder(100, 5, 0, 15, 150)
            < SailingCourse.SpeedOrder(100, 5, 0, 15, 0), "sharp turn slows");
        Check(SailingCourse.SpeedOrder(700, 5, 0, 15, 0) <= .65f, "no burn autopilot");
        Check(SeaSick.Combat.CombatLock.ScreenHullDistance(new Vector2(5,3), Vector2.zero, new Vector2(10,0)) == 3f, "hull side picking");
        Check(SeaSick.Combat.CombatLock.ScreenHullDistance(new Vector2(13,0), Vector2.zero, new Vector2(10,0)) == 3f, "hull end picking");
        Check(SeaSick.Combat.CombatLock.ScreenHullDistance(new Vector2(0,3), Vector2.zero, Vector2.zero) == 3f, "end-on hull picking");
        string result = "PASS: " + passed + " sailing-course checks. " + DateTime.UtcNow.ToString("O");
        File.WriteAllText("/tmp/seasick-sailing-rules.txt", result);
        Debug.Log(result);
    }

    [MenuItem("SeaSick/Sailing Prototype/Check Runtime")]
    public static void CheckRuntime()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Enter Play mode first.");
        SeaSick.Save.GameBoot.Skip();
        var pilot = SailingPilot.Instance;
        if (pilot == null) throw new Exception("No runtime SailingPilot.");
        var motor = pilot.GetComponent<ShipMotor>();
        pilot.Stop();
        pilot.SetExperimental(false);
        if (pilot.Experimental || SailingPilot.OwnsWorldInput) throw new Exception("Classic switch failed.");
        pilot.SetExperimental(true);
        if (!pilot.Experimental || pilot.Destination.HasValue || motor.ThrottleOrder != 0)
            throw new Exception("Prototype switch retained an order.");
        string result = "PASS: runtime attachment, classic/prototype switch and stop. "
            + "Anchored=" + motor.Anchored + ", ExternalDrive=" + motor.ExternalDrive;
        File.WriteAllText("/tmp/seasick-sailing-runtime.txt", result);
        Debug.Log(result);
    }

    static double trialStart;
    static Vector3 trialGoal;
    static bool ordered;
    static float nextSample;
    static readonly System.Text.StringBuilder trace = new System.Text.StringBuilder();

    [MenuItem("SeaSick/Sailing Prototype/Run Save-Safe Water Trial")]
    public static void WaterTrial()
    {
        CheckRuntime();
        var pilot = SailingPilot.Instance;
        var anchor = pilot.GetComponent<AnchorController>();
        if (anchor != null) anchor.CastOff();
        var motor = pilot.GetComponent<ShipMotor>();
        if (motor.Anchored) throw new Exception("Recall crew and cast off before this trial.");
        var height = SeaSick.CameraRig.GroundPick.Height;
        if (height == null) throw new Exception("Terrain not ready.");
        Vector3 from = pilot.transform.position;
        bool found = false;
        for (int radius = 100; radius <= 1600 && !found; radius += 100)
        for (int angle = 0; angle < 360 && !found; angle += 30)
        {
            Vector3 p = from + Quaternion.Euler(0,angle,0) * Vector3.forward * radius;
            Vector3 q = p + new Vector3(70f, 0f, 70f);
            if (!SailingCourse.Clear(p,q,20,15,height)) continue;
            p.y = SeaSick.Ocean.OceanSampler.Ready
                ? SeaSick.Ocean.OceanSampler.SampleImmediate(p).height : 0f;
            var body = pilot.GetComponent<Rigidbody>();
            body.position = p;
            body.rotation = Quaternion.identity;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            trialGoal = q;
            found = true;
        }
        if (!found) throw new Exception("No deep-water trial corridor found.");
        trace.Clear();
        trace.AppendLine("Save suppressed. Ship relocated in Play mode only. t,distance,speed,throttle,status");
        trialStart = EditorApplication.timeSinceStartup;
        ordered = false;
        nextSample = 0f;
        EditorApplication.update -= SampleTrial;
        EditorApplication.update += SampleTrial;
    }

    static void SampleTrial()
    {
        var pilot = SailingPilot.Instance;
        if (!EditorApplication.isPlaying || pilot == null)
        { EditorApplication.update -= SampleTrial; return; }
        float t = (float)(EditorApplication.timeSinceStartup - trialStart);
        if (!ordered && t > 4)
        {
            ordered = true;
            trace.AppendLine("Destination accepted=" + pilot.TrySetDestination(trialGoal));
        }
        var motor = pilot.GetComponent<ShipMotor>();
        if (t >= nextSample)
        {
            Vector3 d = trialGoal - pilot.transform.position; d.y = 0;
            trace.AppendLine($"{t:F1},{d.magnitude:F1},{motor.CurrentSpeed:F2},{motor.ThrottleOrder:F2},{pilot.Status}");
            nextSample = t + 2;
            File.WriteAllText("/tmp/seasick-sailing-trial.txt",trace.ToString());
        }
        if (t > 65)
        {
            pilot.Stop();
            ScreenCapture.CaptureScreenshot("/tmp/seasick-sailing-portrait.png");
            EditorApplication.update -= SampleTrial;
        }
    }

    [MenuItem("SeaSick/Sailing Prototype/Capture Game View")]
    public static void Capture()
    {
        ScreenCapture.CaptureScreenshot("/tmp/seasick-sailing-portrait.png");
    }

    [MenuItem("SeaSick/Sailing Prototype/Read Control State")]
    public static void ReadState()
    {
        var p = SailingPilot.Instance;
        if (p == null) return;
        var m = p.GetComponent<ShipMotor>();
        File.WriteAllText("/tmp/seasick-sailing-input.txt",
            $"prototype={p.Experimental} status={p.Status} target={p.Destination} pan={SailingPilot.ViewOffset} throttle={m.ThrottleOrder} speed={m.CurrentSpeed}");
    }
}
