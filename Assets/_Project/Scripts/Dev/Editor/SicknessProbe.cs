using System.Collections;
using System.Globalization;
using System.Text;
using SeaSick.Crew;
using SeaSick.Ship;
using UnityEngine;

/// Play-mode probe for the reworked sickness/labour system.
/// Writes results to a file because each execute_script call compiles a fresh
/// assembly and cannot find a component created by a previous one.
public class SicknessProbe : MonoBehaviour
{
    public const string OutPath = "/tmp/seasick-sickness-probe.txt";

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SicknessProbe: not in play mode"); return; }
        var go = new GameObject("SicknessProbe");
        go.AddComponent<SicknessProbe>();
    }

    StringBuilder log = new StringBuilder();
    ShipMotor motor;
    CrewRoster roster;
    SmoothnessMeter meter;
    CannonBattery battery;

    void Line(string s)
    {
        log.AppendLine(s);
        System.IO.File.WriteAllText(OutPath, log.ToString());
    }

    IEnumerator Start()
    {
        motor = FindAnyObjectByType<ShipMotor>();
        roster = motor != null ? motor.GetComponent<CrewRoster>() : null;
        meter = motor != null ? motor.GetComponent<SmoothnessMeter>() : null;
        battery = motor != null ? motor.GetComponent<CannonBattery>() : null;

        if (motor == null || roster == null)
        {
            Line("FAIL: motor or roster missing");
            yield break;
        }

        Line("=== PHASE A: natural accumulation, 30s, must be monotonic ===");
        Line("t\trough\tsick0\tsick1\tsick2\tsick3\tsick4\tlabour\table");
        var crew = roster.All;
        float[] prev = new float[crew.Length];
        int drops = 0;
        float t0 = Time.time;
        while (Time.time - t0 < 30f)
        {
            var sb = new StringBuilder();
            sb.Append((Time.time - t0).ToString("F0", CultureInfo.InvariantCulture)).Append('\t');
            sb.Append(meter != null ? meter.Roughness01.ToString("F3", CultureInfo.InvariantCulture) : "-").Append('\t');
            for (int i = 0; i < crew.Length; i++)
            {
                float s = crew[i].Sickness01;
                if (s < prev[i] - 0.0001f) drops++;
                prev[i] = s;
                sb.Append(s.ToString("F3", CultureInfo.InvariantCulture)).Append('\t');
            }
            sb.Append(roster.Labour01.ToString("F2", CultureInfo.InvariantCulture)).Append('\t');
            sb.Append(roster.AbleCount);
            Line(sb.ToString());
            yield return new WaitForSeconds(3f);
        }
        Line($"monotonic check: {drops} decrease(s) observed at sea (expect 0)");

        Line("");
        Line("=== PHASE B: healthy crew trims full -> furled ===");
        yield return Trim(0f, "furled");
        yield return Trim(1f, "full");

        Line("");
        Line("=== PHASE C: force every hand to 0.90 (heaving), retest trim ===");
        SetAll(0.90f);
        yield return new WaitForSeconds(1f);
        Line($"labour {roster.Labour01:F2}  able {roster.AbleCount}/{roster.CrewCount}");
        yield return Trim(0f, "furled");

        Line("");
        Line("=== PHASE D: force every hand to 1.0 (broken) ===");
        SetAll(1f);
        // Give them time to walk to the rail and slump.
        float wait = Time.time;
        while (Time.time - wait < 22f)
        {
            if (roster.AbleCount == 0) break;
            yield return new WaitForSeconds(1f);
        }
        Line($"after {Time.time - wait:F0}s: able {roster.AbleCount}/{roster.CrewCount}  broken {roster.BrokenCount}  labour {roster.Labour01:F2}");
        Line($"oar power {motor.OarPower01:F2} (expect ~0)");
        if (battery != null)
            Line($"guns ready — port {battery.PortReady}/{battery.GunsPerSide}  stbd {battery.StarboardReady}/{battery.GunsPerSide} (expect 0/2 both)");

        float held = motor.SailSetting;
        motor.SailOrder = 1f;
        yield return new WaitForSeconds(6f);
        Line($"sail held at {motor.SailSetting:F3} after ordering full for 6s (was {held:F3}) — expect no movement");
        Line($"ship still makes {motor.CurrentSpeed:F1} m/s — expect > 0, never stranded");

        Line("");
        Line("=== PHASE E: shore recovery is the only cure ===");
        float before = roster.WorstSickness01;
        yield return new WaitForSeconds(4f);
        Line($"worst sickness {before:F3} -> {roster.WorstSickness01:F3} at sea (expect no fall)");

        Line("");
        Line("DONE");
    }

    void SetAll(float v)
    {
        // Sickness01 has a private setter, so drive the backing field. Setting
        // the OWNER's state and letting it propagate, per the standing lesson
        // about probes that write state something else reasserts.
        var f = typeof(CrewAgent).GetField("<Sickness01>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (f == null) { Line("FAIL: could not reach Sickness01 backing field"); return; }
        foreach (var c in roster.All) f.SetValue(c, v);
    }

    IEnumerator Trim(float order, string label)
    {
        motor.SailOrder = order;
        float start = Time.time;
        float from = motor.SailSetting;
        while (motor.Trimming && Time.time - start < 25f) yield return null;
        Line($"trim to {label}: {from:F2} -> {motor.SailSetting:F2} in {Time.time - start:F1}s  (labour {roster.Labour01:F2})");
    }
}
