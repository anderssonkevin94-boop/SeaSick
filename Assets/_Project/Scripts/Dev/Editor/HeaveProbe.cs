using System.Collections;
using System.Text;
using UnityEngine;

/// Is the ship actually being thrown 20m up and down out in the storm, or does
/// the water only look big in a grid sample? Settles whether a flat-looking
/// screenshot is a physics problem or a camera one.
public class HeaveProbe : MonoBehaviour
{
    public const string OutPath = "/tmp/seasick-heave.txt";

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("HeaveProbe: not in play mode"); return; }
        new GameObject("HeaveProbe").AddComponent<HeaveProbe>();
    }

    StringBuilder log = new StringBuilder();
    void Line(string s) { log.AppendLine(s); System.IO.File.WriteAllText(OutPath, log.ToString()); }

    IEnumerator Start()
    {
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        var field = SeaSick.Ocean.WaveField.Instance;
        if (motor == null || field == null) { Line("FAIL"); yield break; }

        Vector3 home = voyage != null && voyage.HomePoint != null
            ? voyage.HomePoint.position : Vector3.zero;

        Line("How far the hull is actually thrown, sailing free for 25s each.");
        Line("");
        Line("westOf\tshipYmin\tshipYmax\tHEAVE\tpitch\troll");

        foreach (float west in new[] { 300f, 1600f })
        {
            motor.transform.position = new Vector3(home.x - west, motor.transform.position.y, home.z);
            yield return new WaitForSeconds(3f);

            float lo = 9999f, hi = -9999f, pk = 0f, rl = 0f;
            float t0 = Time.time;
            while (Time.time - t0 < 25f)
            {
                float y = motor.transform.position.y;
                if (y < lo) lo = y;
                if (y > hi) hi = y;
                pk = Mathf.Max(pk, Mathf.Abs(Norm(motor.transform.eulerAngles.x)));
                rl = Mathf.Max(rl, Mathf.Abs(Norm(motor.transform.eulerAngles.z)));
                yield return null;
            }
            Line($"{west:F0}\t{lo:F1}\t{hi:F1}\t{(hi - lo):F1}\t{pk:F0}°\t{rl:F0}°");
        }

        Line("");
        Line("A 21m ship in a 25m sea should be heaving well over 10m.");
        Line("DONE");
    }

    static float Norm(float d) => d > 180f ? d - 360f : d;
}
