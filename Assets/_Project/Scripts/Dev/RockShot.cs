using System.Collections;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Terrain;
using SeaSick.World;

/// Crank the rock knobs the way the tuner would, rebuild, and photograph the
/// rockiest island. Two things at once: it proves the live-rebuild path the
/// tuner depends on, and it is the first time the rock field is looked at
/// rather than measured. A field that reports 13% rock and shows none would
/// be exactly the kind of disagreement this project keeps getting caught by.
public class RockShot : MonoBehaviour
{
    public static void Execute() => new GameObject("RockShot").AddComponent<RockShot>();

    IEnumerator Start()
    {
        var streamer = FindFirstObjectByType<TerrainStreamer>();
        var ship = FindFirstObjectByType<SeaSick.Ship.ShipMotor>();
        var anchor = ship != null ? ship.GetComponent<SeaSick.Ship.AnchorController>() : null;
        if (streamer == null || streamer.settings == null || ship == null)
        { Debug.LogError("RockShot: missing pieces"); yield break; }
        var ts = streamer.settings;

        // The rockiest island in the world, by its own character field.
        var prm0 = TerrainParams.From(ts);
        Island best = null; float bestRock = -1f;
        foreach (var i in Island.All)
        {
            if (i == null) continue;
            float r = TerrainHeight.Rock01(new float2(i.transform.position.x, i.transform.position.z), prm0);
            if (r > bestRock) { bestRock = r; best = i; }
        }
        Debug.Log($"RockShot: {best.name}, rockiness {bestRock:F2}");

        // Pinned mid-morning. An unpinned shot came back a black rectangle
        // once already; the day/night cycle is short enough to roll over
        // inside one probe.
        SeaSick.World.TimeOfDay.SetTime01(0.36f);
        if (anchor != null) anchor.CastOff();
        yield return null;

        float bestAng = 0f, bestH = -1f;
        for (int a = 0; a < 48; a++)
        {
            float ang = a / 48f * Mathf.PI * 2f;
            Vector3 p = best.transform.position
                + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * best.RadiusAt(ang) * 0.5f;
            float h = Island.TerrainHeight(p.x, p.z);
            if (h > bestH) { bestH = h; bestAng = ang; }
        }
        Vector3 dir = new Vector3(Mathf.Sin(bestAng), 0f, Mathf.Cos(bestAng));
        Vector3 at = best.transform.position + dir * (best.RadiusAt(bestAng) + 300f);
        at.y = ship.transform.position.y;
        var rb = ship.GetComponent<Rigidbody>();
        ship.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-dir));
        if (rb != null) { rb.position = at; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        ship.AnchorPoint = at;
        yield return new WaitForSeconds(6f);
        SeaSick.World.TimeOfDay.SetTime01(0.36f);

        Shoot("/tmp/rock-before.png", ship, best, bestH);

        // Now turn it up, exactly as the tuner's arrow keys would.
        ts.rockRelief = 60f;
        ts.rockThresholdHard = 0.24f;
        ts.rockThresholdSoft = 0.55f;
        streamer.MarkDirty();
        Debug.Log("RockShot: cranked rock and marked the streamer dirty");
        yield return new WaitForSeconds(8f);
        SeaSick.World.TimeOfDay.SetTime01(0.36f);

        Shoot("/tmp/rock-after.png", ship, best, bestH);
        Destroy(gameObject);
    }

    static void Shoot(string path, SeaSick.Ship.ShipMotor ship, Island isle, float peak)
    {
        // Camera.main is null whenever the chase rig is inactive or
        // untagged, which is exactly what happens after a probe has warped
        // the ship about. Copying from it then throws, and the shot comes
        // back as an empty blue frame with a NullReferenceException nobody
        // reads. Fall back to any camera in the scene.
        var src = Camera.main;
        if (src == null) foreach (var c in Camera.allCameras) { src = c; break; }
        if (src == null) { Debug.LogError("RockShot: no camera to copy from"); return; }

        var go = new GameObject("RockCam");
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(src);
        cam.fieldOfView = 40f;
        cam.farClipPlane = 8000f;
        Vector3 look = isle.transform.position; look.y = peak * 0.45f;
        Vector3 eye = ship.transform.position + Vector3.up * 90f;
        go.transform.position = eye;
        go.transform.LookAt(look);

        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
        cam.aspect = (float)W / H;
        cam.ResetProjectionMatrix();
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null;
        Object.Destroy(go);
    }
}
