using System.Collections;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;

/// What the player actually sees when they press play, with nothing moved.
///
/// Every other shot script in here warps to a hardcoded coordinate and forces
/// a severity, which is right for testing the ocean and wrong for testing the
/// SPAWN: it would photograph a place PlaytestStart never chose and a weather
/// PlaytestStart never produced. This one touches nothing except the throttle
/// and photographs where the game put her.
///
/// Play mode, Sea.unity. Writes /tmp/seasick-spawn-0..2.png and logs the
/// conditions alongside, so the picture and the numbers come from one run.
public class SpawnShot : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SpawnShot: not in play mode"); return; }
        new GameObject("SpawnShot").AddComponent<SpawnShot>();
    }

    IEnumerator Start()
    {
        var motor = FindAnyObjectByType<ShipMotor>();
        var sea = SeaStateController.Instance;

        // Let the terrain stream, the FFT spin up and the buoyancy settle.
        // A shot taken before that is a photograph of a loading screen.
        yield return new WaitForSeconds(6f);

        if (motor != null) { motor.ThrottleOrder = 1f; motor.Rudder = 0f; }

        for (int i = 0; i < 3; i++)
        {
            yield return new WaitForSeconds(4f);

            Vector3 p = motor != null ? motor.transform.position : Vector3.zero;
            float surface = OceanSampler.Ready ? OceanSampler.SampleImmediate(p).height : 0f;
            float env = RegionField.Instance != null
                ? RegionField.Instance.Evaluate(new Vector2(p.x, p.z)) : -1f;

            Debug.Log($"SpawnShot[{i}] pos ({p.x:F0}, {p.y:F1}, {p.z:F0})  " +
                      $"speed {(motor != null ? motor.CurrentSpeed : 0f):F1} m/s  " +
                      $"severity {(sea != null ? sea.Severity01 : -1f):F2}  " +
                      $"envelope {env:F2}  surface {surface:F1} m  " +
                      $"deck-over-water {(p.y - surface):F1} m");

            ScreenCapture.CaptureScreenshot($"/tmp/seasick-spawn-{i}.png");
        }

        yield return new WaitForSeconds(2f);
        Debug.Log("SpawnShot: done");
        Destroy(gameObject);
    }
}
