using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.World;

/// **Can you see the bottom, and does it taper?**
///
/// The water went from opaque to murk-with-depth, and that is two separate
/// terms that fail at opposite ends (see the shoal/murk note in
/// `Ocean.shader`): a SHOAL tint driven by the vertical depth, which is what
/// reads at a grazing angle from a deck, and a MURK extinction driven by the
/// view-ray column, which is what actually shows you the sand. A single
/// pretty screenshot cannot tell you which of the two you are looking at,
/// so this shoots the same water four ways at one wave phase:
///
///   0  shipped — both terms
///   1  murk off  (`_SS_RefractOff`) — the shoal tint alone
///   2  shoal off (strength 0)       — seeing through, alone
///   3  neither                      — the water as it was before
///
/// The camera looks DOWN the depth gradient from just off the beach, so one
/// frame contains everything from swash to open water, and the sheet prints
/// the measured depth along that line so the taper can be read in metres
/// rather than admired.
public class ShoalShot : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("ShoalShot: not in play mode"); return; }
        var old = FindAnyObjectByType<ShoalShot>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("ShoalShot").AddComponent<ShoalShot>();
    }

    IEnumerator Start()
    {
        // `_SS_RefractOff` -- the whole "murk off" half of this sheet -- lives
        // behind the ocean shader's `_SEASICK_DEBUG` variant now, so without
        // the keyword the four shots would be four photographs of the same
        // water. OnDestroy takes it off again; the abort below leaves this
        // object alive, so it takes it off itself.
        SeaDebugKeyword.Acquire(this);
        var sb = new StringBuilder("=== ShoalShot ===\n");
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var streamer = FindAnyObjectByType<SeaSick.Terrain.TerrainStreamer>();
        var sea = SeaSick.Ocean.SeaStateController.Instance;
        if (Island.All.Count == 0)
        {
            Debug.LogError("ShoalShot: no islands");
            SeaDebugKeyword.Release(this);
            yield break;
        }

        // Calm and midday: this is a look at the WATER, and a storm sea or a
        // low sun would be judging something else.
        TimeOfDay.SetTime01(0.42f);
        TimeOfDay.Paused = true;
        if (sea != null) sea.ForceHs(0.6f);

        Vector3 shipAt = motor != null ? motor.transform.position : Vector3.zero;
        Island target = null; float best = float.MaxValue;
        foreach (var isl in Island.All)
        {
            float d = Vector3.Distance(isl.transform.position, shipAt);
            if (d < best) { best = d; target = isl; }
        }
        Vector3 c = target.transform.position;
        float r = Mathf.Max(target.Radius, 60f);

        // Seaward bearing, searched not assumed — the same trap IslandLook
        // documents: the ship starts at her home mooring, which is an island,
        // so "the direction of the ship" points into a hill.
        Vector3 seaward = Vector3.forward; float bestOpen = float.MinValue;
        for (int b = 0; b < 24; b++)
        {
            float ang = b / 24f * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            float open = 0f;
            for (float d = r + 40f; d <= r + 400f; d += 30f)
            {
                float g = Ground(c + dir * d);
                open += Mathf.Min(g, 0f);
                if (g > -1f) open -= 400f;
            }
            if (open > bestOpen) { bestOpen = open; seaward = dir; }
        }

        // Walk out from the island centre to find the waterline, then report
        // the depth profile the shot is going to contain.
        float shore = r;
        for (float d = r * 0.5f; d < r + 400f; d += 2f)
            if (Ground(c + seaward * d) <= 0f) { shore = d; break; }
        sb.AppendLine($"island {c:F0} r {r:F0}, waterline at {shore:F0} m out on {seaward:F2}");
        sb.AppendLine("  depth along the shot, metres out from the waterline:");
        var prof = new StringBuilder("   ");
        for (float d = 0f; d <= 160f; d += 20f)
            prof.Append($"{d,4:F0}m:{-Ground(c + seaward * (shore + d)),5:F1}  ");
        sb.AppendLine(prof.ToString());

        var main = Camera.main;
        var cam = new GameObject("ShoalCam").AddComponent<Camera>();
        if (main != null) cam.CopyFrom(main);
        cam.depth = 100f;
        cam.farClipPlane = 6000f;
        var streamerTarget = streamer != null ? streamer.target : null;
        if (streamer != null) streamer.target = cam.transform;

        // Over the back of the beach, looking DOWN the depth gradient: swash
        // at the bottom of frame, open water at the top, every metre of the
        // taper in between.
        //
        // The pitch is the whole experiment and the first version got it
        // wrong. 11 m up looking 55 m out is 11 degrees below horizontal, and
        // at 11 degrees the view ray crosses five metres of water for every
        // metre of DEPTH -- so nothing was visible through anything and the
        // sheet could only ever show the shoal tint. 24 m up looking 34 m out
        // is 35 degrees, where the ray crosses 1.7 m per metre of depth,
        // which is roughly what the eye does from a deck looking over the
        // rail at water it is about to sail into.
        cam.transform.position = c + seaward * (shore - 10f) + Vector3.up * 24f;
        cam.transform.LookAt(c + seaward * (shore + 24f));

        float waited = 0f;
        yield return new WaitForSeconds(3f);
        while (streamer != null && streamer.PendingCount > 0 && waited < 40f)
        { waited += 0.5f; yield return new WaitForSeconds(0.5f); }
        yield return new WaitForSeconds(1f);

        var mat = FindOceanMaterial();
        float shoalWas = mat != null ? mat.GetFloat("_ShoalStrength") : 0f;
        string[] names = { "shipped", "murk-off", "shoal-off", "neither" };
        for (int i = 0; i < 4; i++)
        {
            Shader.SetGlobalFloat("_SS_RefractOff", (i == 1 || i == 3) ? 1f : 0f);
            if (mat != null)
                mat.SetFloat("_ShoalStrength", (i == 2 || i == 3) ? 0f : shoalWas);
            yield return null;
            string path = $"/tmp/seasick-shoal-{names[i]}.png";
            Shoot(cam, path);
            sb.AppendLine($"  {names[i],-10} -> {path}");
        }

        // Put everything back. A forced sea and a global dev flag both
        // survive leaving play mode here (domain reload is off), so a probe
        // that walks away leaves every later run looking at the wrong thing.
        // The `_SEASICK_DEBUG` keyword is exactly such a flag; OnDestroy below
        // clears it, so it is cleared on this path too.
        Shader.SetGlobalFloat("_SS_RefractOff", 0f);
        if (mat != null) mat.SetFloat("_ShoalStrength", shoalWas);
        if (streamer != null) streamer.target = streamerTarget;
        if (sea != null) sea.ReleaseForce();
        TimeOfDay.Paused = false;
        Destroy(cam.gameObject);
        System.IO.File.WriteAllText("/tmp/seasick-shoal.txt", sb.ToString());
        Debug.Log(sb.ToString());
        Destroy(gameObject);
    }

    void OnDestroy() { SeaDebugKeyword.Release(this); }

    static Material FindOceanMaterial()
    {
        foreach (var mr in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (mr.sharedMaterial != null && mr.sharedMaterial.shader != null
                && mr.sharedMaterial.shader.name == "SeaSick/Ocean")
                return mr.sharedMaterial;
        return Resources.Load<Material>("OceanSurface");
    }

    static float Ground(Vector3 p)
        => Island.TerrainHeight != null ? Island.TerrainHeight(p.x, p.z) : -999f;

    static void Shoot(Camera cam, string path)
    {
        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = cam.targetTexture;
        var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0f, 0f, W, H), 0, 0);
        tex.Apply();
        cam.targetTexture = prev;
        RenderTexture.active = prevActive;
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(tex);
        rt.Release();
        Destroy(rt);
    }
}
