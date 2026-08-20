using System.Collections;
using UnityEngine;

/// Captures the screen INCLUDING the IMGUI HUD. Rendering a camera to a
/// RenderTexture does not include IMGUI, so the HUD can only be reviewed via
/// ScreenCapture.
public class HudShot : MonoBehaviour
{
    public const string OutPath = "/tmp/seasick-hud.png";

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("HudShot: not in play mode"); return; }
        new GameObject("HudShot").AddComponent<HudShot>();
    }

    IEnumerator Start()
    {
        // Get her out into water worth looking at, and load her so the
        // contextual panels have something to say.
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        if (motor != null && voyage != null && voyage.HomePoint != null)
        {
            Vector3 home = voyage.HomePoint.position;
            motor.transform.position = new Vector3(home.x + 620f, motor.transform.position.y, home.z + 240f);
            for (int i = 0; i < 26; i++) voyage.AddLoot(1, "Timber");
        }

        // Let the chase camera ease in — it lags a warp badly.
        yield return new WaitForSeconds(3.5f);

        if (System.IO.File.Exists(OutPath)) System.IO.File.Delete(OutPath);
        ScreenCapture.CaptureScreenshot(OutPath);
        Debug.Log("HudShot: captured " + OutPath);
    }
}
