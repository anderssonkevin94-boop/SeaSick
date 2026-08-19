using System.Collections;
using System.Reflection;
using System.Text;
using SeaSick.Crew;
using SeaSick.Ship;
using UnityEngine;

/// Measures crewed sail trim. The first attempt at this drove
/// motor.SailOrder directly and measured nothing, because HelmInput reasserts
/// SailOrder from its own sailStep every frame — the same trap as
/// HelmInput→sail, MutinyController→autopilot and CombatLock→LockTarget.
/// Drive the OWNER's state and let it propagate.
public class TrimProbe : MonoBehaviour
{
    public const string OutPath = "/tmp/seasick-trim-probe.txt";

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("TrimProbe: not in play mode"); return; }
        new GameObject("TrimProbe").AddComponent<TrimProbe>();
    }

    StringBuilder log = new StringBuilder();
    ShipMotor motor;
    CrewRoster roster;
    HelmInput helm;
    FieldInfo stepField, sickField;

    void Line(string s)
    {
        log.AppendLine(s);
        System.IO.File.WriteAllText(OutPath, log.ToString());
    }

    IEnumerator Start()
    {
        motor = FindAnyObjectByType<ShipMotor>();
        roster = motor != null ? motor.GetComponent<CrewRoster>() : null;
        helm = motor != null ? motor.GetComponent<HelmInput>() : null;
        stepField = typeof(HelmInput).GetField("sailStep", BindingFlags.Instance | BindingFlags.NonPublic);
        sickField = typeof(CrewAgent).GetField("<Sickness01>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (motor == null || roster == null || helm == null || stepField == null || sickField == null)
        {
            Line($"FAIL motor={motor != null} roster={roster != null} helm={helm != null} " +
                 $"step={stepField != null} sick={sickField != null}");
            yield break;
        }

        Line("=== sail trim vs. how much crew is actually working ===");
        Line("Driving HelmInput.sailStep (the owner), not motor.SailOrder.");
        Line("");

        yield return Case(0.05f, "fresh crew");
        yield return Case(0.60f, "queasy");
        yield return Case(0.90f, "heaving");

        Line("");
        Line("=== every hand at 1.0 — wait for them to give up ===");
        SetAll(1f);
        float t0 = Time.time;
        while (Time.time - t0 < 30f && roster.BrokenCount < roster.CrewCount)
            yield return new WaitForSeconds(1f);
        Line($"after {Time.time - t0:F0}s: broken {roster.BrokenCount}/{roster.CrewCount}  " +
             $"able {roster.AbleCount}  labour {roster.Labour01:F2}");

        float held = motor.SailSetting;
        stepField.SetValue(helm, 0);
        yield return new WaitForSeconds(8f);
        Line($"ordered furled, 8s later sail is {motor.SailSetting:F3} (was {held:F3}) — expect unchanged");
        Line($"speed {motor.CurrentSpeed:F1} m/s, rudder still answers: heading {motor.Heading:F0}°");

        Line("");
        Line("DONE");
    }

    void SetAll(float v)
    {
        foreach (var c in roster.All) sickField.SetValue(c, v);
    }

    IEnumerator Case(float sickness, string label)
    {
        // Settle them at a known sickness and back at their posts.
        SetAll(0f);
        stepField.SetValue(helm, 2);
        float settle = Time.time;
        while (Time.time - settle < 4f) yield return null;
        SetAll(sickness);
        yield return new WaitForSeconds(0.5f);

        float labour = roster.Labour01;
        int able = roster.AbleCount;
        float from = motor.SailSetting;
        stepField.SetValue(helm, 0);           // order: furled
        // HelmInput propagates sailStep -> SailOrder in ITS Update, which may
        // not have run yet this frame. Checking Trimming immediately reads
        // "already there" and reports a 0.0s trim.
        float armed = Time.time;
        while (!motor.Trimming && Time.time - armed < 1f) yield return null;
        float start = Time.time;
        while (motor.Trimming && Time.time - start < 30f) yield return null;
        Line($"{label,-12} sick {sickness:F2}  labour {labour:F2}  able {able}/{roster.CrewCount}  " +
             $"| full→furled {from:F2}→{motor.SailSetting:F2} in {Time.time - start:F1}s");
    }
}
