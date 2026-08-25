using System.Collections;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.World;

/// What the sea LOOKS like from the deck, at the scale of the boat. Numbers
/// cannot settle "does it read as a mountain": the old sea measured 16.6 m of
/// amplitude and looked like a sheet. Five shots of ONE pinned instant of the
/// storm sea in the western deep, all with the ship in frame for scale:
///   0 astern at sea level, 1 from the deepest trough looking at the highest
///   crest, 2 from that crest looking down at her, 3 beam-on low, 4 high
///   three-quarter so the wavelength pattern is visible.
///
/// The phase is pinned (OceanTime.Scrub + Paused) so re-running after a change
/// shoots the SAME water -- tint and shape comparisons are worthless while the
/// waves move.
///
/// Play mode, Sea.unity. Writes /tmp/seasick-wave-0..4.png and -waveshot.txt.
public class WaveShot : MonoBehaviour
{
    const float PatchHalf = 800f;
    const float DeepEnough = -9f;
    const float PinnedTime = 137.0f;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("WaveShot: not in play mode"); return; }
        new GameObject("WaveShot").AddComponent<WaveShot>();
    }

    static bool DeepEverywhere(Vector3 centre, float half)
    {
        var h = Island.TerrainHeight;
        if (h == null) return false;
        for (int i = -2; i <= 2; i++)
            for (int j = -2; j <= 2; j++)
                if (h(centre.x + i * half * 0.5f, centre.z + j * half * 0.5f) > DeepEnough) return false;
        return true;
    }

    IEnumerator Start()
    {
        var sb = new StringBuilder();

        // Same tier forcing as WaveSizeProbe: play mode enters the iOS default
        // (Mobile), which is not the config being tuned.
        var ocean = OceanRenderer.Instance;
        var pc = Resources.Load<OceanQuality>("Ocean/OceanQuality_PC");
        string tier = "unchanged";
        if (ocean != null && pc != null)
        {
            OceanQuality.Override(pc);
            ocean.enabled = false;
            ocean.enabled = true;
            var cl = FindAnyObjectByType<OceanClipmap>();
            if (cl != null) cl.Build();
            tier = "OceanQuality_PC";
            yield return null;
        }
        var q = OceanQuality.Active;

        var motor = FindAnyObjectByType<ShipMotor>();
        var helm = FindAnyObjectByType<HelmInput>();
        if (helm != null) helm.enabled = false;
        var sea = SeaStateController.Instance;
        var rb = motor != null ? motor.GetComponent<Rigidbody>() : null;

        Vector3 spot = Vector3.zero;
        bool found = false;
        for (float dist = 2000f; dist <= 20000f && !found; dist += 250f)
            for (int b = 0; b < 16 && !found; b++)
            {
                float ang = b / 16f * Mathf.PI * 2f;
                Vector3 c = new Vector3(Mathf.Sin(ang) * dist, 0f, Mathf.Cos(ang) * dist);
                if (DeepEverywhere(c, PatchHalf)) { spot = c; found = true; }
            }
        if (!found) { Debug.LogError("WaveShot: no deep-water patch found"); yield break; }

        if (rb != null)
        {
            rb.position = new Vector3(spot.x, 2f, spot.z);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        if (motor != null) { motor.SailOrder = 0f; motor.Rudder = 0f; }
        if (sea != null) sea.ForceSeverity(1f);
        yield return new WaitForSeconds(16f);   // let the storm spectrum land

        // Pin the phase so a re-run shoots the same water.
        OceanTime.Scrub(PinnedTime);
        OceanTime.Paused = true;
        for (int f = 0; f < 8; f++) yield return null;

        // Find the highest crest and deepest trough near her.
        int side = 81;
        float step = 15f;
        var qa = new NativeArray<float3>(side * side, Allocator.TempJob);
        var ra = new NativeArray<OceanSample>(side * side, Allocator.TempJob);
        for (int i = 0; i < side; i++)
            for (int j = 0; j < side; j++)
                qa[i * side + j] = new float3(
                    spot.x + (i - side / 2) * step, 0f, spot.z + (j - side / 2) * step);
        OceanSampler.SampleBatch(qa, ra, default).Complete();
        Vector3 crest = spot, trough = spot;
        float hiY = -9999f, loY = 9999f;
        for (int i = 0; i < side * side; i++)
        {
            float h = ra[i].height;
            if (h > hiY) { hiY = h; crest = new Vector3(qa[i].x, h, qa[i].z); }
            if (h < loY) { loY = h; trough = new Vector3(qa[i].x, h, qa[i].z); }
        }
        qa.Dispose(); ra.Dispose();

        Vector3 ship = motor != null ? motor.transform.position : spot;
        float shipY = OceanSampler.SampleImmediate(ship).height;

        var main = Camera.main;
        var cam = new GameObject("WaveShotCam").AddComponent<Camera>();
        if (main != null) cam.CopyFrom(main);
        cam.depth = 100f;
        cam.farClipPlane = 6000f;

        Vector3 toCrest = crest - ship; toCrest.y = 0f;
        if (toCrest.sqrMagnitude < 1f) toCrest = Vector3.forward;
        toCrest.Normalize();
        Vector3 beam = new Vector3(-toCrest.z, 0f, toCrest.x);

        Vector3[] from =
        {
            ship - toCrest * 70f + Vector3.up * (shipY + 3f),
            new Vector3(trough.x, trough.y + 3f, trough.z),
            new Vector3(crest.x, crest.y + 6f, crest.z),
            ship + beam * 60f + Vector3.up * (shipY + 2.5f),
            ship - toCrest * 400f + Vector3.up * (shipY + 250f),
        };
        Vector3[] at = { ship, crest, ship, ship, ship };
        string[] label =
        {
            "astern at sea level, ship in frame",
            "from the deepest trough, looking at the highest crest",
            "from the highest crest, looking down at her",
            "beam on, 2.5 m above the water",
            "high three-quarter, wavelength pattern",
        };

        sb.AppendLine("WaveShot -- the storm sea at the scale of the boat");
        sb.AppendLine("ocean quality FORCED to " + tier + ": patch0 "
            + q.patchSizes[0].ToString("F0") + " m, rings " + q.clipmapRings
            + ", displacement fade " + q.displacementFadeDistance.ToString("F0") + " m");
        sb.AppendLine("spot " + spot.ToString("F0") + "   severity forced 1.00   phase pinned at t="
            + PinnedTime.ToString("F0"));
        sb.AppendLine("boat 24.2 m long, sitting at y=" + shipY.ToString("F2"));
        sb.AppendLine("highest crest " + hiY.ToString("F2") + " m at " + crest.ToString("F0"));
        sb.AppendLine("deepest trough " + loY.ToString("F2") + " m at " + trough.ToString("F0"));
        sb.AppendLine("crest-to-trough across the 1200 m search: " + (hiY - loY).ToString("F2") + " m");
        sb.AppendLine("NOTE: the editor Game view is LANDSCAPE; the shipping target is portrait.");
        sb.AppendLine();

        for (int i = 0; i < from.Length; i++)
        {
            cam.transform.position = from[i];
            cam.transform.LookAt(at[i]);
            for (int f = 0; f < 3; f++) yield return null;
            ScreenCapture.CaptureScreenshot("/tmp/seasick-wave-" + i + ".png");
            for (int f = 0; f < 3; f++) yield return null;
            sb.AppendLine("shot " + i + ": " + label[i]);
            sb.AppendLine("   camera " + from[i].ToString("F0") + " looking at " + at[i].ToString("F0"));
        }

        Destroy(cam.gameObject);
        OceanTime.Paused = false;
        if (sea != null) sea.ReleaseForce();
        System.IO.File.WriteAllText("/tmp/seasick-waveshot.txt", sb.ToString());
        Debug.Log("WaveShot:\n" + sb);
        Destroy(gameObject);
    }
}
