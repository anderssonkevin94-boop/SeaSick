using System.Collections;
using System.Reflection;
using System.Text;
using SeaSick.Ship;
using UnityEngine;

/// The one claim that matters after removing the polar: speed must not depend
/// on heading, except when the water is genuinely big.
public class HeadingProbe : MonoBehaviour
{
    public const string OutPath = "/tmp/seasick-heading-probe.txt";

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("HeadingProbe: not in play mode"); return; }
        new GameObject("HeadingProbe").AddComponent<HeadingProbe>();
    }

    StringBuilder log = new StringBuilder();
    ShipMotor motor;
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
        if (motor == null) { Line("FAIL: no ShipMotor"); yield break; }

        motor.transform.position = new Vector3(0f, motor.transform.position.y, -520f);
        hold = motor.transform.position;
        pinned = true;
        motor.CargoLoad = 0f;
        yield return new WaitForSeconds(2f);

        var headingField = typeof(ShipMotor).GetField("heading",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (headingField == null) { Line("FAIL: no heading field"); yield break; }

        var field = SeaSick.Ocean.WaveField.Instance;
        Line($"sea severity here: {(field != null ? field.SeaSeverity01(new Vector2(0f, -520f), Time.time) : -1f):F2}");
        Line("");
        Line("bearing\tseaAngle\thead01\tresist\tspeed");

        float min = 999f, max = -999f;
        for (int deg = 0; deg < 360; deg += 45)
        {
            headingField.SetValue(motor, (float)deg);
            // Let way build on the new heading before sampling.
            yield return new WaitForSeconds(9f);

            float sp = motor.CurrentSpeed;
            if (sp < min) min = sp;
            if (sp > max) max = sp;
            Line($"{deg}°\t{motor.SeaAngleDeg:F0}°\t{motor.HeadSea01:F2}\t" +
                 $"{motor.SeaResistance01:F2}\t{sp:F1}");
        }

        Line("");
        Line($"spread across all headings: {min:F1} — {max:F1} m/s  ({(max - min):F1} m/s)");
        Line(max > 0.01f
            ? $"slowest heading is {(min / max):P0} of the fastest"
            : "no way made at all");
        Line("");
        Line("DONE");
    }
}
