using System.Collections;
using System.Reflection;
using UnityEngine;

/// Parks the ship in the storm and turns on StormSpray's per-second budget log.
public class SprayDebug : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SprayDebug: not in play mode"); return; }
        var old = FindAnyObjectByType<SprayDebug>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("SprayDebug").AddComponent<SprayDebug>();
    }

    SeaSick.Ship.ShipMotor motor;
    Vector3 hold;

    IEnumerator Start()
    {
        motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        Vector3 home = voyage != null && voyage.HomePoint != null
            ? voyage.HomePoint.position : Vector3.zero;
        if (motor == null) yield break;

        hold = home + new Vector3(-1500f, 0f, 0f);
        motor.transform.position = new Vector3(hold.x, motor.transform.position.y, hold.z);

        var spray = motor.GetComponent<SeaSick.Ocean.StormSpray>();
        int count = motor.GetComponents<SeaSick.Ocean.StormSpray>().Length;
        Debug.Log($"SPRAYDBG components on ship: {count}");
        if (spray != null)
        {
            var f = typeof(SeaSick.Ocean.StormSpray)
                .GetField("logDiagnostics", BindingFlags.Instance | BindingFlags.NonPublic);
            f?.SetValue(spray, true);
            var rate = typeof(SeaSick.Ocean.StormSpray)
                .GetField("spindriftRate", BindingFlags.Instance | BindingFlags.NonPublic);
            Debug.Log($"SPRAYDBG live spindriftRate = {rate?.GetValue(spray)}");
        }
        yield return new WaitForSeconds(9f);
        Debug.Log("SPRAYDBG done");
    }

    void LateUpdate()
    {
        if (motor == null) return;
        Vector3 p = motor.transform.position;
        motor.transform.position = new Vector3(hold.x, p.y, hold.z);
    }
}
