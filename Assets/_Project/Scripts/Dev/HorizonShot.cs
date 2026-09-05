using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.Terrain;
using SeaSick.World;

/// Stands off the home island at several distances and photographs it with the
/// distant-land field on and off, so "can you see where you are heading?" has
/// a picture instead of an opinion.
///
/// Run in play mode in Sea.unity: `RunProbe.Horizon()`.
/// Writes /tmp/seasick-horizon-<distance>-<on|off>.png and a text log.
public class HorizonShot : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("HorizonShot: not in play mode"); return; }
        var old = FindAnyObjectByType<HorizonShot>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("HorizonShot").AddComponent<HorizonShot>();
    }

    static readonly float[] Distances = { 1800f, 3500f, 6000f };

    IEnumerator Start()
    {
        var sb = new StringBuilder("=== HorizonShot ===\n");
        var motor = FindAnyObjectByType<ShipMotor>();
        var voyage = FindAnyObjectByType<SeaSick.Voyage.VoyageManager>();
        var field = FindAnyObjectByType<HorizonField>();
        var sea = SeaStateController.Instance;
        if (motor == null || sea == null)
        {
            Debug.LogError("HorizonShot: no ship / sea");
            yield break;
        }
        sb.AppendLine(field != null ? "HorizonField present" : "NO HorizonField in scene");

        Vector3 home = voyage != null && voyage.HomePoint != null
            ? voyage.HomePoint.position : Vector3.zero;
        home.y = 0f;

        TimeOfDay.SetTime01(0.34f);
        TimeOfDay.Paused = true;
        sea.ForceHs(2.2f);

        var cam = new GameObject("HorizonCam").AddComponent<Camera>();
        var main = Camera.main;
        if (main != null) cam.CopyFrom(main);
        cam.depth = 100f;
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, 8600f);
        cam.fieldOfView = 42f;                 // a little long, the way you actually look for land
        sb.AppendLine($"camera far clip {cam.farClipPlane:F0} m, fov {cam.fieldOfView:F0}");

        var rb = motor.GetComponent<Rigidbody>();
        // Centre the field on the camera the pictures are taken from, not on
        // the chase camera, which is somewhere else entirely while the ship is
        // being teleported around.
        if (field != null) field.Target = cam.transform;

        var tset = Resources.FindObjectsOfTypeAll<TerrainSettings>();
        TerrainSettings ts = tset != null && tset.Length > 0 ? tset[0] : null;
        var streamerForSettings = FindAnyObjectByType<TerrainStreamer>();
        if (ts != null)
        {
            probeParams = TerrainParams.From(ts);
            probeLut = TerrainCurveLut.Bake(ts.profileCurve, Unity.Collections.Allocator.Persistent);
            sb.AppendLine($"terrain: viewRadius {ts.viewRadius} x {ts.chunkSize:F0} m = "
                          + $"{ts.viewRadius * ts.chunkSize:F0} m of real chunks");
        }
        else sb.AppendLine("NO TerrainSettings found — profile will be zeros");

        foreach (float d in Distances)
        {
            // Stand off to the east and look back at home.
            Vector3 at = home + new Vector3(d, 0f, 0f);
            motor.transform.position = new Vector3(at.x, motor.transform.position.y, at.z);
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

            cam.transform.position = new Vector3(at.x, 14f, at.z);
            cam.transform.rotation = Quaternion.LookRotation(
                new Vector3(-1f, -0.035f, 0f).normalized, Vector3.up);

            sb.AppendLine(Profile(cam, d));

            for (int pass = 0; pass < 2; pass++)
            {
                bool on = pass == 0;
                if (field != null)
                {
                    field.enabled = on;
                    if (on) field.Rebuild();
                }
                // Wait on the CONDITION, not on a guess. A fixed sleep here is
                // what photographed the field mid-rebuild and produced three
                // runs of arguing with a picture that was showing a flat disc.
                float t0 = Time.time;
                while (on && field != null && !field.Ready && Time.time - t0 < 15f)
                    yield return null;
                yield return new WaitForSeconds(0.6f);
                if (on && field != null) sb.AppendLine("  " + field.Describe());
                Shoot(cam, $"/tmp/seasick-horizon-{d:F0}-{(on ? "on" : "off")}.png");
                yield return null;
            }
            if (field != null) field.enabled = true;
            sb.AppendLine($"  {d:F0} m: shot on + off");
        }

        if (probeLut.IsCreated) probeLut.Dispose();
        Destroy(cam.gameObject);
        TimeOfDay.Paused = false;
        sea.ReleaseForce();
        System.IO.File.WriteAllText("/tmp/seasick-horizon.txt", sb.ToString());
        Debug.Log(sb.ToString());
        Destroy(gameObject);
    }

    /// What is actually in front of the camera, in metres — so "that grey
    /// mountain" stops being a guess. Reports the ground along the line of
    /// sight and the angle each sample subtends, because a hill that fills
    /// half the frame is either tall or near and the picture cannot say which.
    static string Profile(Camera cam, float standOff)
    {
        var sb = new StringBuilder($"  profile at {standOff:F0} m stand-off, "
                                   + $"cam y {cam.transform.position.y:F1}, "
                                   + $"fog {ColorUtility.ToHtmlStringRGB(RenderSettings.fogColor)}, "
                                   + $"fogEnd {RenderSettings.fogEndDistance:F0}");
        sb.AppendLine("     dist   ground   subtends   half-fov is 21 deg");
        Vector3 o = cam.transform.position;
        Vector3 f = cam.transform.forward; f.y = 0f; f.Normalize();
        for (float d = 200f; d <= 8000f; d += d < 2000f ? 200f : 800f)
        {
            Vector3 p = o + f * d;
            float h = SeaSick.Terrain.TerrainHeight.Height(
                new Unity.Mathematics.float2(p.x, p.z), probeParams, probeLut);
            float ang = Mathf.Atan2(Mathf.Max(0f, h) - o.y, d) * Mathf.Rad2Deg;
            sb.AppendLine($"    {d,5:F0}  {h,7:F1}  {ang,8:F1}");
        }
        return sb.ToString();
    }

    static SeaSick.Terrain.TerrainParams probeParams;
    static Unity.Collections.NativeArray<float> probeLut;

    static void Shoot(Camera cam, string path)
    {
        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = cam.targetTexture; var prevActive = RenderTexture.active;
        cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0f, 0f, W, H), 0, 0); tex.Apply();
        cam.targetTexture = prev; RenderTexture.active = prevActive;
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(tex); rt.Release(); Destroy(rt);
    }
}
