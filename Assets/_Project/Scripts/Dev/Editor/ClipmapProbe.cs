using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean2;

/// Clipmap acceptance: (a) vertex swim — with OceanTime frozen, re-anchoring
/// every ring must not change the rendered surface except in the thin bands
/// where ring assignment itself moved (vertices always land on a fixed world
/// lattice; if they don't, this probe lights up); (b) altitude tiling shots
/// for the no-visible-repetition check. Run in play mode in OceanLab.
/// Writes /tmp/seasick-clipmap.txt and /tmp/seasick-tiling-*.png.
public class ClipmapProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("ClipmapProbe: not in play mode"); return; }
        new GameObject("ClipmapProbe").AddComponent<ClipmapProbe>();
    }

    IEnumerator Start()
    {
        var sb = new StringBuilder();
        var cam = Camera.main;
        var clip = FindAnyObjectByType<OceanClipmap>();
        var ocean = OceanRenderer.Instance;
        if (cam == null || clip == null || ocean == null) yield break;

        // Storm sea, frozen phase — worst case for boundary artifacts.
        var storm = ScriptableObject.CreateInstance<OceanSpectrumSettings>();
        storm.windSpeed = 22f; storm.fetchKm = 200f; storm.choppiness = 1f;
        ocean.SetSettings(storm);
        yield return null;
        OceanTime.Scrub(60.0);
        OceanTime.Paused = true;
        ocean.StepSimulation();
        yield return null;

        var follow = new GameObject("SwimFollow").transform;
        follow.position = Vector3.zero;
        clip.FollowOverride = follow;
        cam.transform.SetPositionAndRotation(new Vector3(0f, 16f, -30f),
            Quaternion.Euler(14f, 0f, 0f));
        yield return null; yield return null;

        var texA = Capture(cam);
        follow.position = new Vector3(37.3f, 0f, 11.7f);
        yield return null; yield return null;
        var texB = Capture(cam);

        int w = texA.width, h = texA.height;
        var pa = texA.GetPixels32();
        var pb = texB.GetPixels32();
        long sumDiff = 0; int over8 = 0;
        for (int i = 0; i < pa.Length; i++)
        {
            int d = Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g)
                  + Mathf.Abs(pa[i].b - pb[i].b);
            sumDiff += d;
            if (d > 24) over8++; // >8/255 avg per channel
        }
        float meanDiff = sumDiff / (float)(pa.Length * 3);
        float pctOver = over8 * 100f / pa.Length;
        bool swimPass = meanDiff < 2f && pctOver < 2f;
        sb.AppendLine($"swim: meanDiff={meanDiff:F2}/255 pixelsOver8={pctOver:F2}% -> {(swimPass ? "PASS" : "FAIL")}");

        // Altitude tiling shots (lab-only view; gameplay camera never sees
        // this). The distance fade would hide any tiling, so it is pushed out
        // past the clipmap extent for these frames only.
        OceanTime.Paused = false;
        var prevQ = OceanQuality.Active;
        var farQ = ScriptableObject.CreateInstance<OceanQuality>();
        farQ.displacementFadeDistance = 50000f;
        OceanQuality.Override(farQ);
        foreach (var (alt, name) in new[] { (2000f, "2km"), (5000f, "5km") })
        {
            cam.transform.SetPositionAndRotation(new Vector3(0f, alt, 0f),
                Quaternion.Euler(90f, 0f, 0f));
            yield return null; yield return null;
            ScreenCapture.CaptureScreenshot($"/tmp/seasick-tiling-{name}.png");
            yield return null; yield return null;
        }
        OceanQuality.Override(prevQ);
        Destroy(farQ);

        System.IO.File.WriteAllText("/tmp/seasick-clipmap.txt", sb.ToString());
        Debug.Log("ClipmapProbe: " + sb);
        Destroy(texA); Destroy(texB); Destroy(follow.gameObject);
        Destroy(gameObject);
    }

    static Texture2D Capture(Camera cam)
    {
        var rt = RenderTexture.GetTemporary(960, 540, 24);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prev;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }
}
