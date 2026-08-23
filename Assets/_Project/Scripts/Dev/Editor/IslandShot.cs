using System.Collections;
using UnityEngine;
using SeaSick.Terrain;

/// Sea.unity look check for the procedural islands. A dedicated camera (depth
/// above the chase cam) shoots the home island from the sea, from the beach,
/// and from above, while the ship stays put so the streamer is centred on it.
public class IslandShot : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("IslandShot: not in play mode"); return; }
        new GameObject("IslandShot").AddComponent<IslandShot>();
    }

    IEnumerator Start()
    {
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var st = FindAnyObjectByType<TerrainStreamer>();
        var main = Camera.main;
        var cam = new GameObject("ShotCam").AddComponent<Camera>();
        if (main != null) { cam.CopyFrom(main); }
        cam.depth = 100f;
        cam.farClipPlane = 4000f;
        var sb = new System.Text.StringBuilder();
        Vector3 shore = new Vector3(0f, 0f, -215f);
        Vector3[] from = { new Vector3(0f, 12f, 140f), new Vector3(-40f, 6f, -150f), new Vector3(0f, 700f, -150f), new Vector3(900f, 120f, 300f) };
        Vector3[] at = { shore, shore + new Vector3(40f, 15f, -120f), shore + new Vector3(0f, 0f, -200f), shore };
        for (int i = 0; i < from.Length; i++)
        {
            if (motor != null)
            {
                // She sails herself; keep the streamer centred on home for the shots.
                var rb = motor.GetComponent<Rigidbody>();
                motor.transform.position = new Vector3(0f, motor.transform.position.y, 40f);
                if (rb != null) rb.linearVelocity = Vector3.zero;
            }
            cam.transform.position = from[i];
            cam.transform.LookAt(at[i]);
            yield return new WaitForSeconds(i == 0 ? 6f : 3f);
            sb.AppendLine("shot " + i + " from " + from[i] + ": loaded=" + (st != null ? st.LoadedCount : -1) + " pending=" + (st != null ? st.PendingCount : -1)
                + " shipPos=" + (motor != null ? motor.transform.position.ToString("F0") : "?"));
            ScreenCapture.CaptureScreenshot("/tmp/seasick-island-" + i + ".png");
            yield return null;
        }
        Destroy(cam.gameObject);
        System.IO.File.WriteAllText("/tmp/seasick-island.txt", sb.ToString());
        Debug.Log("IslandShot done\n" + sb);
    }
}
