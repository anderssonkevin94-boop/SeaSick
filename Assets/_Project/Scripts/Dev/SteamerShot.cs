using System.Collections;
using UnityEngine;

/// Look shots of the stern-wheel steamer in Sea.unity, in the game's own
/// light, with her guns and her hands where the game put them.
///
/// The renders the Blender generator makes are Workbench with a studio light
/// and no sea; they say whether the MESH is right and nothing about whether
/// she reads as a ship in this game. That is a different question and it is
/// the one Kevin answers, so it gets its own camera: five looks with the
/// weather pinned calm enough to see her.
///
/// Play mode, Sea.unity, steamer selected BEFORE Play. Launched through
/// `RunSteamerProbe.Shot()`.
///
/// Lives in `Scripts/Dev/`, NOT `Scripts/Dev/Editor/`, and that is not a
/// filing preference: an editor-assembly MonoBehaviour cannot be added to a
/// GameObject at runtime at all ("Can't add script behaviour ... because it
/// is an editor script"), and every shot tool here works by adding itself to
/// a new GameObject. `IslandShot` is still sitting in the Editor folder with
/// the same bug waiting in it.
public class SteamerShot : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SteamerShot: not in play mode"); return; }
        var old = FindAnyObjectByType<SteamerShot>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("SteamerShot").AddComponent<SteamerShot>();
    }

    IEnumerator Start()
    {
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        if (motor == null) { Debug.LogError("SteamerShot: no ShipMotor"); yield break; }
        var ship = motor.transform;
        var sea = FindAnyObjectByType<SeaSick.Ocean.SeaStateController>();
        if (sea != null) sea.ForceHs(0.6f);

        var main = Camera.main;
        var cam = new GameObject("SteamerShotCam").AddComponent<Camera>();
        if (main != null) cam.CopyFrom(main);
        cam.depth = 100f;
        cam.farClipPlane = 4000f;

        // Let her settle on her marks before anything is photographed: the
        // first second of a play session is the hull finding the surface.
        yield return new WaitForSeconds(6f);

        // name, eye in SHIP-LOCAL metres, what to look at in ship-local
        var shots = new (string name, Vector3 eye, Vector3 at)[]
        {
            ("stern",   new Vector3( 14f,  9f, -30f), new Vector3(0f, 1.5f, -8f)),
            ("bow",     new Vector3(-16f,  8f,  28f), new Vector3(0f, 1.5f,  4f)),
            ("beam",    new Vector3( 34f,  4f,  -1f), new Vector3(0f, 2.0f, -1f)),
            ("chase",   new Vector3(  0f, 11f, -34f), new Vector3(0f, 2.0f,  6f)),
            ("deck",    new Vector3(  0f,  6f, -15f), new Vector3(0f, 1.5f,  6f)),
        };
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < shots.Length; i++)
        {
            cam.transform.position = ship.TransformPoint(shots[i].eye);
            cam.transform.LookAt(ship.TransformPoint(shots[i].at));
            yield return new WaitForSeconds(1.5f);
            string path = "/tmp/seasick-steamer-shot-" + shots[i].name + ".png";
            ScreenCapture.CaptureScreenshot(path);
            sb.AppendLine(shots[i].name + " -> " + path);
            yield return new WaitForSeconds(1.5f);
        }
        Destroy(cam.gameObject);
        var battery = motor.GetComponent<SeaSick.Ship.CannonBattery>();
        var hands = motor.GetComponentsInChildren<SeaSick.Crew.CrewAgent>(false);
        sb.AppendLine("guns per side " + (battery != null ? battery.GunsPerSide : -1)
            + "   hands on deck " + hands.Length);
        System.IO.File.WriteAllText("/tmp/seasick-steamer-shot.txt", sb + "DONE\n");
        Debug.Log("SteamerShot done\n" + sb);
    }
}
