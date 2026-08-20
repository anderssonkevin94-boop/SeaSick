using System.Collections;
using UnityEngine;

/// Parks the ship deep in the western storm and photographs the water.
public class StormShot : MonoBehaviour
{
    public const string OutPath = "/tmp/seasick-storm.png";
    public static float West = 1500f;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("StormShot: not in play mode"); return; }
        var old = FindAnyObjectByType<StormShot>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("StormShot").AddComponent<StormShot>();
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

        hold = home + new Vector3(-West, 0f, 0f);
        motor.transform.position = new Vector3(hold.x, motor.transform.position.y, hold.z);

        var field = SeaSick.Ocean.WaveField.Instance;
        if (field != null)
        {
            Vector2 p = new Vector2(hold.x, hold.z);
            Debug.Log($"STORMSHOT: {West}m west — storm {field.StormAmount01(p):F2}  " +
                      $"region {field.RegionScale(p):F2}  seaState {field.SeaState01:F2}");
        }

        yield return new WaitForSeconds(4f);
        if (System.IO.File.Exists(OutPath)) System.IO.File.Delete(OutPath);
        ScreenCapture.CaptureScreenshot(OutPath);
        Debug.Log("StormShot: captured");
    }

    void LateUpdate()
    {
        if (motor == null) return;
        Vector3 p = motor.transform.position;
        motor.transform.position = new Vector3(hold.x, p.y, hold.z);
    }
}
