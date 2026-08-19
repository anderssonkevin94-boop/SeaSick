using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using SeaSick.Crew;
using SeaSick.Ship;
using SeaSick.Voyage;
using UnityEngine;

/// Play-mode probe for milestone 1: weight, freeboard, green water, bailing.
/// Writes to a file — each execute_script call compiles a fresh assembly and
/// cannot find a component created by a previous one.
public class LoadProbe : MonoBehaviour
{
    public const string OutPath = "/tmp/seasick-load-probe.txt";

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("LoadProbe: not in play mode"); return; }
        new GameObject("LoadProbe").AddComponent<LoadProbe>();
    }

    StringBuilder log = new StringBuilder();
    ShipMotor motor;
    Bilge bilge;
    CrewRoster roster;
    SmoothnessMeter meter;
    CannonBattery battery;
    VoyageManager voyage;

    void Line(string s)
    {
        log.AppendLine(s);
        System.IO.File.WriteAllText(OutPath, log.ToString());
    }

    // The last run measured an aground ship: unsteered, she sailed into an
    // island in the first few seconds and every "top speed" after that was 0.6.
    // Hold her in open water and let her sail on the spot.
    Vector3 hold;
    bool pinned;

    void LateUpdate()
    {
        if (!pinned || motor == null) return;
        Vector3 p = motor.transform.position;
        motor.transform.position = new Vector3(hold.x, p.y, hold.z);
    }

    IEnumerator Start()
    {
        motor = FindAnyObjectByType<ShipMotor>();
        bilge = motor != null ? motor.GetComponent<Bilge>() : null;
        roster = motor != null ? motor.GetComponent<CrewRoster>() : null;
        meter = motor != null ? motor.GetComponent<SmoothnessMeter>() : null;
        battery = motor != null ? motor.GetComponent<CannonBattery>() : null;
        voyage = FindAnyObjectByType<VoyageManager>();

        if (motor == null || bilge == null || roster == null)
        {
            Line($"FAIL motor={motor != null} bilge={bilge != null} roster={roster != null}");
            yield break;
        }

        // Somewhere with sea room: well clear of home and of the island ring.
        motor.transform.position = new Vector3(0f, motor.transform.position.y, -520f);
        hold = motor.transform.position;
        pinned = true;
        yield return new WaitForSeconds(2f);
        Line($"pinned in open water at {hold.x:F0},{hold.z:F0}");
        Line("");

        Line("=== freeboard: does she visibly settle as you load her? ===");
        Line("load\tsink(m)\ttopSpeed\tturn");
        foreach (float load in new[] { 0f, 0.5f, 1f, 1.3f, 1.6f })
        {
            motor.CargoLoad = load;
            yield return new WaitForSeconds(2.5f);
            Line($"{load:F2}\t{motor.SinkDepth:F3}\t{motor.CurrentSpeed:F1}\t—");
        }

        Line("");
        Line("=== green water: how much comes aboard, by load ===");
        Line("(30s each, in whatever sea we happen to have)");
        Line("load\trough\tpeakImm\tbilge\tbailers\tportGuns");
        foreach (float load in new[] { 0f, 0.6f, 1f, 1.6f })
        {
            motor.CargoLoad = load;
            bilge.Dry();
            float t0 = Time.time;
            float peakImmersion = 0f;
            while (Time.time - t0 < 30f)
            {
                if (motor.RailImmersion > peakImmersion) peakImmersion = motor.RailImmersion;
                yield return null;
            }
            Line($"{load:F2}\t{(meter != null ? meter.Roughness01 : 0f):F3}\t{peakImmersion:F3}\t" +
                 $"{bilge.Bilge01:F3}\t{bilge.Bailers}\t{(battery != null ? battery.PortReady : -1)}");
        }

        Line("");
        Line("=== the spiral: force her half swamped and see if she recovers ===");
        motor.CargoLoad = 1f;
        SetBilge(0.85f);
        yield return new WaitForSeconds(1f);
        Line($"at 0.85 bilge: sink {motor.SinkDepth:F3}  bailers {bilge.Bailers}/{roster.CrewCount}  " +
             $"guns {battery?.PortReady}/{battery?.GunsPerSide}+{battery?.StarboardReady}");
        float start = Time.time;
        while (bilge.Bilge01 > 0.06f && Time.time - start < 90f)
            yield return new WaitForSeconds(1f);
        Line($"after {Time.time - start:F0}s bailing: bilge {bilge.Bilge01:F3} " +
             (bilge.Bilge01 <= 0.06f ? "— RECOVERED" : "— STILL FLOODED (spiral may be unwinnable)"));

        Line("");
        Line("=== jettison: does dumping cargo buy freeboard back? ===");
        if (voyage != null)
        {
            for (int i = 0; i < 48; i++) voyage.AddLoot(1, "Timber");
            yield return new WaitForSeconds(0.5f);
            Line($"loaded to {voyage.TotalHeld}/{voyage.HoldCapacity} (max {voyage.MaxHold})  " +
                 $"fill {voyage.HoldFill:F2}  overloaded={voyage.Overloaded}  sink {motor.SinkDepth:F3}");
            int dumped = voyage.Jettison(20);
            yield return new WaitForSeconds(0.5f);
            Line($"jettisoned {dumped}: now {voyage.TotalHeld}  fill {voyage.HoldFill:F2}  sink {motor.SinkDepth:F3}");
        }
        else Line("(no VoyageManager)");

        Line("");
        Line("=== sickness is cosmetic: does anyone leave a post for it? ===");
        int offPost = 0;
        float watch = Time.time;
        while (Time.time - watch < 8f)
        {
            foreach (var c in roster.All)
                if (c != null && c.IsAboard && !c.Available && !c.IsBailing) offPost++;
            yield return new WaitForSeconds(0.5f);
        }
        Line($"non-bailing off-post samples over 8s: {offPost} (expect 0)");
        Line($"crew sickness now: {roster.WorstSickness01:F2} worst, {roster.AverageSickness01:F2} avg");

        Line("");
        Line("DONE");
    }

    void SetBilge(float v)
    {
        var f = typeof(Bilge).GetField("<Bilge01>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (f == null) { Line("FAIL: could not reach Bilge01 backing field"); return; }
        f.SetValue(bilge, v);
    }
}
