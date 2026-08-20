using System.Collections;
using UnityEngine;

/// Settles what the dark region in the lower half of the frame actually is.
///
/// Paints the ocean's storm deep colour bright magenta. If the dark area turns
/// magenta it IS the ocean, drawn at near-normal incidence, and the problem is
/// shading. If it stays dark the ocean is not being drawn there at all, and the
/// problem is geometry or culling. No amount of reasoning separates those two;
/// one photograph does.
[DefaultExecutionOrder(800)]
public class VoidTest : MonoBehaviour
{
    SeaSick.Ship.ShipMotor motor;
    Vector3 hold;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("VoidTest: not in play mode"); return; }
        var old = FindAnyObjectByType<VoidTest>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("VoidTest").AddComponent<VoidTest>();
    }

    IEnumerator Start()
    {
        motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var rend = FindAnyObjectByType<SeaSick.Ocean.OceanRenderer>();
        var mr = rend != null ? rend.GetComponent<MeshRenderer>() : null;
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        Vector3 home = voyage != null && voyage.HomePoint != null
            ? voyage.HomePoint.position : Vector3.zero;
        if (motor == null || mr == null) { Debug.LogError("VoidTest: missing"); yield break; }

        hold = home + new Vector3(-1500f, 0f, 0f);
        motor.transform.position = new Vector3(hold.x, motor.transform.position.y, hold.z);
        yield return new WaitForSeconds(5f);

        // Controlled: the ship is pinned, so every shot below is the same
        // patch of sea from the same camera. Tint one term at a time and see
        // which one owns the dark region. Comparing shots from separate runs
        // is worthless here — the sea and the camera both move.
        var m = mr.material;
        var deep = m.GetColor("_StormDeep");
        var shoal = m.GetColor("_StormShallow");
        var crest = m.GetColor("_StormCrest");

        Shoot("/tmp/seasick-term-none.png");
        yield return new WaitForSeconds(2f);

        m.SetColor("_StormDeep", new Color(1f, 0f, 0f));
        yield return new WaitForSeconds(1.5f);
        Shoot("/tmp/seasick-term-deep.png");
        yield return new WaitForSeconds(1.5f);
        m.SetColor("_StormDeep", deep);

        m.SetColor("_StormShallow", new Color(0f, 1f, 0f));
        yield return new WaitForSeconds(1.5f);
        Shoot("/tmp/seasick-term-shallow.png");
        yield return new WaitForSeconds(1.5f);
        m.SetColor("_StormShallow", shoal);

        m.SetColor("_StormCrest", new Color(0f, 0f, 1f));
        yield return new WaitForSeconds(1.5f);
        Shoot("/tmp/seasick-term-crest.png");
        yield return new WaitForSeconds(1.5f);
        m.SetColor("_StormCrest", crest);

        Debug.Log("VOIDTEST: red=deep, green=shallow(grazing), blue=foam.");
    }

    void Shoot(string path)
    {
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
    }

    void LateUpdate()
    {
        if (motor == null) return;
        Vector3 p = motor.transform.position;
        motor.transform.position = new Vector3(hold.x, p.y, hold.z);
    }
}
