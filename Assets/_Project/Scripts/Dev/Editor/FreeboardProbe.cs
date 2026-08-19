using System.Collections;
using System.Globalization;
using System.Text;
using SeaSick.Ship;
using UnityEngine;

/// Measures the MARGIN, not the outcome. Green water kept reading exactly zero
/// at every load, which tells us nothing about how close it came — so this
/// reports the signed gap between the sea and the rail: how far the water got
/// from coming aboard, in metres. That sets railHeight from data instead of
/// from a guess.
public class FreeboardProbe : MonoBehaviour
{
    public const string OutPath = "/tmp/seasick-freeboard-probe.txt";

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("FreeboardProbe: not in play mode"); return; }
        new GameObject("FreeboardProbe").AddComponent<FreeboardProbe>();
    }

    StringBuilder log = new StringBuilder();
    ShipMotor motor;
    SmoothnessMeter meter;
    Vector3 hold;
    bool pinned;

    void Line(string s)
    {
        log.AppendLine(s);
        System.IO.File.WriteAllText(OutPath, log.ToString());
    }

    void LateUpdate()
    {
        if (!pinned || motor == null) return;
        Vector3 p = motor.transform.position;
        motor.transform.position = new Vector3(hold.x, p.y, hold.z);
    }

    IEnumerator Start()
    {
        motor = FindAnyObjectByType<ShipMotor>();
        meter = motor != null ? motor.GetComponent<SmoothnessMeter>() : null;
        if (motor == null) { Line("FAIL: no ShipMotor"); yield break; }

        motor.transform.position = new Vector3(0f, motor.transform.position.y, -520f);
        hold = motor.transform.position;
        pinned = true;
        yield return new WaitForSeconds(2f);

        Line("How close does the sea get to the rail? Negative = dry by that many metres.");
        Line("railHeight is 2.35m; sink is subtracted from it by load.");
        Line("");
        Line("load\tsink\tpeakGap\tmeanGap\tpeakRoll\tpeakWave\trough");

        foreach (float load in new[] { 0f, 1f, 1.6f })
        {
            motor.CargoLoad = load;
            yield return new WaitForSeconds(1.5f);

            float peakGap = float.NegativeInfinity, sumGap = 0f;
            float peakRoll = 0f, peakWave = 0f;
            int n = 0;
            float t0 = Time.time;
            var field = SeaSick.Ocean.WaveField.Instance;

            while (Time.time - t0 < 25f)
            {
                // Lowest rail this frame, against the water directly under it.
                float best = float.NegativeInfinity;
                for (int i = -1; i <= 1; i += 2)
                {
                    Vector3 rail = motor.transform.TransformPoint(new Vector3(i * 3.9f, 2.35f, 3.5f));
                    float h = field != null ? field.SampleHeightFast(new Vector2(rail.x, rail.z), Time.time) : 0f;
                    float gap = h - rail.y;
                    if (gap > best) best = gap;
                    float waveRel = h - motor.transform.position.y;
                    if (waveRel > peakWave) peakWave = waveRel;
                }
                if (best > peakGap) peakGap = best;
                sumGap += best; n++;

                float roll = Mathf.Abs(NormalizeAngle(motor.transform.eulerAngles.z));
                if (roll > peakRoll) peakRoll = roll;
                yield return null;
            }

            Line($"{load:F2}\t{motor.SinkDepth:F2}\t{peakGap:F2}\t{(sumGap / Mathf.Max(1, n)):F2}\t" +
                 $"{peakRoll:F1}°\t{peakWave:F2}\t{(meter != null ? meter.Roughness01 : 0f):F2}");
        }

        Line("");
        Line("=== forced heel: a broadside is the biggest roll in the game ===");
        motor.CargoLoad = 1f;
        yield return new WaitForSeconds(1.5f);
        float bestFired = float.NegativeInfinity, rollFired = 0f;
        for (int shot = 0; shot < 3; shot++)
        {
            motor.AddRecoilRoll(28f);
            float t1 = Time.time;
            while (Time.time - t1 < 3f)
            {
                for (int i = -1; i <= 1; i += 2)
                {
                    Vector3 rail = motor.transform.TransformPoint(new Vector3(i * 3.9f, 2.35f, 3.5f));
                    var f2 = SeaSick.Ocean.WaveField.Instance;
                    float h = f2 != null ? f2.SampleHeightFast(new Vector2(rail.x, rail.z), Time.time) : 0f;
                    if (h - rail.y > bestFired) bestFired = h - rail.y;
                }
                float roll = Mathf.Abs(NormalizeAngle(motor.transform.eulerAngles.z));
                if (roll > rollFired) rollFired = roll;
                yield return null;
            }
        }
        Line($"laden + broadside recoil: peak roll {rollFired:F1}°, closest the sea got: {bestFired:F2} m");

        Line("");
        Line("DONE");
    }

    static float NormalizeAngle(float d) => d > 180f ? d - 360f : d;
}
