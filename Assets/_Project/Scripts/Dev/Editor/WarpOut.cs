using SeaSick.Ship;
using UnityEngine;

/// Parks the ship at a given distance from home and holds it there, so the
/// water around it can be looked at. The C# wave field and the ocean shader
/// are two implementations of one formula — if they disagree the hull will
/// visibly float above or sink into the surface.
public class WarpOut : MonoBehaviour
{
    public static float Distance = 150f;
    Vector3 hold;
    ShipMotor motor;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("WarpOut: not in play mode"); return; }
        var existing = FindAnyObjectByType<WarpOut>();
        if (existing != null) Destroy(existing.gameObject);
        new GameObject("WarpOut").AddComponent<WarpOut>();
    }

    void Start()
    {
        motor = FindAnyObjectByType<ShipMotor>();
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        Vector3 home = voyage != null && voyage.HomePoint != null
            ? voyage.HomePoint.position : Vector3.zero;
        if (motor == null) return;
        hold = home + new Vector3(Distance, 0f, 0f);
        motor.transform.position = new Vector3(hold.x, motor.transform.position.y, hold.z);
        Debug.Log($"WarpOut: ship parked {Distance}m from home");
    }

    void LateUpdate()
    {
        if (motor == null) return;
        Vector3 p = motor.transform.position;
        motor.transform.position = new Vector3(hold.x, p.y, hold.z);
    }
}
