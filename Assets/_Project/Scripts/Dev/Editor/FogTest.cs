using System.Collections;
using System.Reflection;
using UnityEngine;

/// Shoots the same storm sea at several fog distances.
///
/// The dominant waves out west are 200-250m long. Storm fog was set to
/// 128-547m in the sky pass, so less than one wavelength is visible — which
/// would make a genuinely huge sea read as a flat sheet no matter how big it
/// is. This settles how much of "the sea looks flat" is the sea and how much
/// is the weather hiding it.
[DefaultExecutionOrder(800)]
public class FogTest : MonoBehaviour
{
    SeaSick.Ship.ShipMotor motor;
    Vector3 hold;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("FogTest: not in play mode"); return; }
        var old = FindAnyObjectByType<FogTest>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("FogTest").AddComponent<FogTest>();
    }

    static void SetPrivate(object o, string field, object v)
        => o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(o, v);

    IEnumerator Start()
    {
        motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var sky = SeaSick.World.SkyDirector.Instance;
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        Vector3 home = voyage != null && voyage.HomePoint != null
            ? voyage.HomePoint.position : Vector3.zero;
        if (motor == null || sky == null) { Debug.LogError("FogTest: missing"); yield break; }

        hold = home + new Vector3(-1500f, 0f, 0f);
        motor.transform.position = new Vector3(hold.x, motor.transform.position.y, hold.z);
        yield return new WaitForSeconds(5f);

        var cases = new[]
        {
            (70f, 430f, "asis"),
            (200f, 1100f, "open"),
            (400f, 2000f, "wide"),
        };
        foreach (var (near, far, name) in cases)
        {
            SetPrivate(sky, "stormFogStart", near);
            SetPrivate(sky, "stormFogEnd", far);
            yield return new WaitForSeconds(2.5f);
            string path = $"/tmp/seasick-fog-{name}.png";
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"FOGTEST {name}: {RenderSettings.fogStartDistance:F0}..{RenderSettings.fogEndDistance:F0}");
            yield return new WaitForSeconds(2f);
        }
        Debug.Log("FOGTEST done");
    }

    void LateUpdate()
    {
        if (motor == null) return;
        Vector3 p = motor.transform.position;
        motor.transform.position = new Vector3(hold.x, p.y, hold.z);
    }
}
