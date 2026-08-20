using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;

/// "Sometimes my boat is all the way over the water and sometimes all the way
/// under."
///
/// The GPU never inverts anything — it displaces rest positions forward, so
/// what you SEE is the true Gerstner surface. The CPU has to solve the inverse
/// to find the height at a fixed world XZ, and that is what the hull seats on.
/// If the solver is wrong, the ship sits at the wrong height relative to the
/// water that is drawn, and nothing about the wave field itself is at fault.
///
/// SampleHeight does three undamped fixed-point steps, which only converge
/// while the total steepness stays under 1 — and Displace multiplies by
/// RegionScale, which is 1.5 out west, on top of a sea state reaching 1.15.
/// This measures the error against a properly converged solution.
[DefaultExecutionOrder(700)]
public class SeatingProbe : MonoBehaviour
{
    public const string OutPath = "/tmp/seasick-seating.txt";

    SeaSick.Ship.ShipMotor motor;
    SeaSick.Ocean.WaveField field;
    static FieldInfo seaField;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SeatingProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<SeatingProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("SeatingProbe").AddComponent<SeatingProbe>();
    }

    float pinned = 1f;
    void LateUpdate()
    {
        if (seaField != null && field != null) seaField.SetValue(field, pinned);
        if (motor != null) motor.transform.position =
            new Vector3(hold.x, motor.transform.position.y, hold.z);
    }
    Vector3 hold;

    /// Damped fixed point. The damping is what makes it converge at steepness
    /// the plain iteration cannot handle.
    static float TrueHeight(SeaSick.Ocean.WaveField f, Vector2 q, float t, out float residual)
    {
        Vector2 p = q;
        for (int i = 0; i < 120; i++)
        {
            Vector3 d = f.Displace(p, t);
            Vector2 err = p + new Vector2(d.x, d.z) - q;
            p -= err * 0.5f;
        }
        Vector3 fin = f.Displace(p, t);
        residual = (p + new Vector2(fin.x, fin.z) - q).magnitude;
        return fin.y;
    }

    IEnumerator Start()
    {
        motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        field = SeaSick.Ocean.WaveField.Instance;
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        Vector3 home = voyage != null && voyage.HomePoint != null
            ? voyage.HomePoint.position : Vector3.zero;
        if (motor == null || field == null) { Debug.LogError("SeatingProbe: missing"); yield break; }

        seaField = typeof(SeaSick.Ocean.WaveField).GetField(
            "<SeaState01>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        var sb = new StringBuilder();
        sb.AppendLine("Error in the hull's seating solver, against a converged solution.");
        sb.AppendLine("The GPU draws the true surface; only the CPU has to invert.");
        sb.AppendLine();

        foreach (var (west, sea, label) in new[]
        {
            (160f, 1.0f, "home shelf, sea 1.00"),
            (1500f, 0.85f, "western deep, sea 0.85"),
            (1500f, 1.15f, "western deep, sea 1.15 (rough ceiling)"),
        })
        {
            pinned = sea;
            hold = home + new Vector3(-west, 0f, 0f);
            motor.transform.position = new Vector3(hold.x, motor.transform.position.y, hold.z);
            yield return new WaitForSeconds(2.5f);

            Vector2 at = new Vector2(hold.x, hold.z);
            float region = field.RegionScale(at);
            float storm = field.StormAmount01(at);

            float errMax = 0f, errSum = 0f, resMax = 0f, shipGapMin = 9999f, shipGapMax = -9999f;
            int n = 0;
            float t0 = Time.time;
            while (Time.time - t0 < 8f)
            {
                Vector2 q = new Vector2(motor.transform.position.x, motor.transform.position.z);
                float t = Time.time;
                float h3 = field.SampleHeight(q, t);
                float hTrue = TrueHeight(field, q, t, out float res);

                float e = Mathf.Abs(h3 - hTrue);
                errSum += e; n++;
                if (e > errMax) errMax = e;
                if (res > resMax) resMax = res;

                float gap = motor.transform.position.y - hTrue;
                if (gap < shipGapMin) shipGapMin = gap;
                if (gap > shipGapMax) shipGapMax = gap;
                yield return null;
            }

            sb.AppendLine($"-- {label}   region {region:F2}  storm {storm:F2}");
            sb.AppendLine($"   solver error vs converged:  mean {(n > 0 ? errSum / n : 0f):F2} m   " +
                          $"MAX {errMax:F2} m");
            sb.AppendLine($"   converged residual:         {resMax:F3} m  (how well the truth solved)");
            sb.AppendLine($"   hull above true surface:    {shipGapMin:F2} .. {shipGapMax:F2} m  " +
                          $"(should be a steady small offset)");
            sb.AppendLine();
        }

        System.IO.File.WriteAllText(OutPath, sb.ToString());
        Debug.Log("SeatingProbe done\n" + sb);
    }
}
