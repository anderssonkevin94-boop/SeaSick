using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;

/// Why does the HUD say "calm" in a storm? Walks the chain that produces the
/// sea-state name: the controller's global severity, the region envelope at
/// the ship, the local severity that follows from them, and what ShipMotor
/// ended up believing. One of those links is lying.
public class SeaStateCheck : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SeaStateCheck: not in play mode"); return; }
        new GameObject("SeaStateCheck").AddComponent<SeaStateCheck>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        ShipMotor motor = FindAnyObjectByType<ShipMotor>();
        SeaStateController sea = SeaStateController.Instance;
        RegionField region = RegionField.Instance;
        Rigidbody rb = motor != null ? motor.GetComponent<Rigidbody>() : null;

        Vector3 spot = new Vector3(-3200f, 2f, 0f);
        if (rb != null) { rb.position = spot; rb.linearVelocity = Vector3.zero; }
        yield return new WaitForSeconds(6f);

        if (sea != null) sea.ForceSeverity(1f);
        yield return new WaitForSeconds(6f);

        Vector3 p = motor != null ? motor.transform.position : spot;
        Vector2 p2 = new Vector2(p.x, p.z);

        sb.AppendLine("ship at " + p.ToString("F0"));
        sb.AppendLine("SeaStateController.Severity01      " +
            (sea != null ? sea.Severity01.ToString("F3") : "no controller"));
        sb.AppendLine("SeaStateController.SeaSeverityAt() " +
            (sea != null ? sea.SeaSeverityAt(p2).ToString("F3") : "-"));
        sb.AppendLine("ShipMotor.SeaSeverity01            " +
            (motor != null ? motor.SeaSeverity01.ToString("F3") : "-"));
        sb.AppendLine("ShipMotor.SeaStateName             " +
            (motor != null ? motor.SeaStateName : "-"));
        sb.AppendLine("ShipMotor.SeaResistance01          " +
            (motor != null ? motor.SeaResistance01.ToString("F3") : "-"));

        if (sea != null) sea.ReleaseForce();
        System.IO.File.WriteAllText("/tmp/seasick-seastate.txt", sb.ToString());
        Debug.Log("SeaStateCheck:\n" + sb);
        Destroy(gameObject);
    }
}
