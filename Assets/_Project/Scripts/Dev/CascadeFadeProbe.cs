using System.Collections;
using System.Reflection;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using SeaSick.Ocean;

/// **How the sea thins with distance.** The clipmap has to stop drawing a
/// cascade before the ring's vertex density stops resolving it, and the shape
/// of that retreat is the single most visible thing about the far water: done
/// per ring it is a camera-locked line on the sea, done per metre it is waves
/// getting smaller.
///
/// Measures it rather than judging it by eye, in two independent ways:
///
/// 1. **Analytic.** Reads the per-cascade RMS straight off the displacement
///    textures (so the numbers are metres of real sea, not weights), then walks
///    outward a metre at a time asking the SHIPPED rule what weight a vertex at
///    that distance gets. Reports surface RMS against distance and, the number
///    that matters, the largest single-metre DROP in it — a step function has
///    one huge one, a ramp has none.
/// 2. **Photographic.** One sea-level frame over open water with the phase
///    pinned, scored for high-frequency contrast row by row. Each row is a
///    known ground distance (flat-water geometry off the camera height), so the
///    profile is directly comparable to the analytic curve and shows what the
///    player actually sees.
///
/// The rule is never duplicated here. If `OceanClipmap` exposes a static
/// `WeightsAt(float)` the probe calls it; otherwise it reads each ring's
/// `_Ocean_CascadeWeights` back off the live MaterialPropertyBlock and works
/// out which ring covers each distance. So this file measures the old scheme
/// and the new one without being edited between the two runs.
///
/// Play mode, OceanLab (no terrain, so the region envelope is out of the way).
/// `Execute` runs the PC tier, `ExecuteMobile` the phone one; each writes
/// /tmp/seasick-cascadefade-<tier>.txt and -<tier>.png.
public class CascadeFadeProbe : MonoBehaviour
{
    const int ShotW = 900, ShotH = 1500;
    // High enough to stay out of the water. A 12 m eye is a fair masthead on a
    // 3 m sea and is UNDER the surface half the time on the 68 m storm this
    // probe runs, which shows up as a near field of flat colour and a contrast
    // profile that reads zero where the detail is densest.
    const float EyeHeight = 40f;
    const float ShotFov = 40f;       // vertical
    const float ShotPitch = 12f;     // degrees down from horizontal
    const double PinnedTime = 500.0;
    // The overhead A/B. The clipmap's rings are SQUARES, so a per-ring weight
    // step is a square drawn on the water — look straight down and it is not a
    // subtle thing. Shot over a rough sea rather than the storm: what steps is
    // the chop, and on the 68 m storm the chop is 0.28 m of RMS against 17 m
    // of swell, which is not what the picture is about.
    const float TopAltitude = 250f;
    const float TopFov = 60f;

    /// Which quality asset to rebuild the stack around. Play mode enters the
    /// BUILD TARGET'S default tier, which is not the one being tuned, and the
    /// two tiers fade the sea completely differently — 7 rings and a 2600 m
    /// displacement fade against 5 rings and 350 m. Naming the tier is the
    /// whole point of the run, so it is an argument, never a guess.
    string tierAsset = "Ocean/OceanQuality_PC";
    string outFile = "/tmp/seasick-cascadefade.txt";
    string shotFile = "/tmp/seasick-cascadefade.png";
    string topFile = "/tmp/seasick-cascadefade-top.png";

    public static void Execute() => Spawn("Ocean/OceanQuality_PC", "pc");
    public static void ExecuteMobile() => Spawn("Ocean/OceanQuality_Mobile", "mobile");

    static void Spawn(string asset, string tag)
    {
        if (!Application.isPlaying) { Debug.LogError("CascadeFadeProbe: not in play mode"); return; }
        var p = new GameObject("CascadeFadeProbe").AddComponent<CascadeFadeProbe>();
        p.tierAsset = asset;
        p.outFile = $"/tmp/seasick-cascadefade-{tag}.txt";
        p.shotFile = $"/tmp/seasick-cascadefade-{tag}.png";
        p.topFile = $"/tmp/seasick-cascadefade-{tag}-top.png";
    }

    static float SmoothStep(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / Mathf.Max(b - a, 1e-6f));
        return t * t * (3f - 2f * t);
    }

    IEnumerator Start()
    {
        var sb = new StringBuilder();
        // A probe that dies silently leaves nothing to read; write the reason.
        System.IO.File.WriteAllText(outFile, "CascadeFadeProbe: did not finish\n");

        var cam = Camera.main;
        var clip = FindAnyObjectByType<OceanClipmap>();
        var ocean = OceanRenderer.Instance;
        if (cam == null || clip == null || ocean == null)
        {
            sb.AppendLine($"ABORT cam={cam != null} clipmap={clip != null} ocean={ocean != null}");
            System.IO.File.WriteAllText(outFile, sb.ToString());
            yield break;
        }

        // Force the tier and rebuild the stack around it — a new CascadeSet
        // from OnEnable and a new set of rings — then say which one loudly.
        var tier = Resources.Load<OceanQuality>(tierAsset);
        if (tier == null)
        {
            sb.AppendLine($"ABORT: Resources/{tierAsset} not found");
            System.IO.File.WriteAllText(outFile, sb.ToString());
            yield break;
        }
        OceanQuality.Override(tier);
        ocean.enabled = false;
        ocean.enabled = true;
        clip.Build();
        yield return null;
        var q = OceanQuality.Active;
        sb.AppendLine($"quality tier: {(q != null ? q.name : "<none>")}  fft={q?.fftSize}  " +
                      $"rings={q?.clipmapRings}  innerCell={q?.innerCellSize}  " +
                      $"fadeDist={q?.displacementFadeDistance}");
        sb.AppendLine($"patches: {string.Join(", ", q.patchSizes)}");

        // Shipped storm, loaded not retyped. A hand-copied duplicate of the
        // sea state is a gate that silently stops gating.
        var sea = FindAnyObjectByType<SeaStateController>();
        if (sea != null) sea.enabled = false;
        var storm = Resources.Load<OceanSpectrumSettings>("Ocean/SeaState_Stormy");
        if (storm == null)
        {
            sb.AppendLine("ABORT: Resources/Ocean/SeaState_Stormy not found");
            System.IO.File.WriteAllText(outFile, sb.ToString());
            yield break;
        }
        // Pausing OceanTime freezes the SPECTRUM too, so land the state first
        // and only then scrub and freeze.
        OceanTime.Paused = false;
        ocean.SetSettings(storm);
        for (int i = 0; i < 6; i++) yield return null;
        OceanTime.Scrub(PinnedTime);
        OceanTime.Paused = true;
        ocean.StepSimulation();
        yield return null;
        yield return null;

        // ---- per-cascade RMS, straight off the displacement textures --------
        var cas = ocean.Cascades;
        int n = cas.N;
        var sigmaY = new float[3];
        var sigmaXZ = new float[3];
        var reqs = new AsyncGPUReadbackRequest[3];
        for (int c = 0; c < 3; c++)
            reqs[c] = AsyncGPUReadback.Request(cas.Displacement, 0, 0, n, 0, n, c, 1,
                TextureFormat.RGBAHalf);
        AsyncGPUReadback.WaitAllRequests();
        for (int c = 0; c < 3; c++)
        {
            if (reqs[c].hasError) { sb.AppendLine($"ABORT: readback error cascade {c}"); break; }
            var d = reqs[c].GetData<half4>();
            double sy = 0, sxz = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float y = d[i].y;
                float x = d[i].x, z = d[i].z;
                sy += (double)y * y;
                sxz += (double)(x * x + z * z);
            }
            sigmaY[c] = Mathf.Sqrt((float)(sy / d.Length));
            sigmaXZ[c] = Mathf.Sqrt((float)(sxz / d.Length));
        }
        sb.AppendLine();
        sb.AppendLine("per-cascade RMS off the displacement texture (metres, envelope 1):");
        for (int c = 0; c < 3; c++)
            sb.AppendLine($"  cascade {c} (patch {q.patchSizes[c]} m): vertical {sigmaY[c]:F4}  horizontal {sigmaXZ[c]:F4}");
        float fullRms = Mathf.Sqrt(sigmaY[0] * sigmaY[0] + sigmaY[1] * sigmaY[1] + sigmaY[2] * sigmaY[2]);
        sb.AppendLine($"  all three, unfaded: {fullRms:F4}  (Hs = 4 x RMS = {4f * fullRms:F2} m)");

        // ---- which rule is live --------------------------------------------
        var weightsAt = typeof(OceanClipmap).GetMethod("WeightsAt",
            BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(float) }, null);
        bool perDistance = weightsAt != null;
        sb.AppendLine();
        sb.AppendLine(perDistance
            ? "RULE: per-DISTANCE (OceanClipmap.WeightsAt)"
            : "RULE: per-RING (_Ocean_CascadeWeights off each ring's MaterialPropertyBlock)");

        // Ring geometry, for the per-ring rule and for the record either way.
        int cellsAcross = 128;
        var f = typeof(OceanClipmap).GetField("cellsAcross",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null) cellsAcross = (int)f.GetValue(clip);
        int rings = clip.RingCount;
        var ringCell = new float[rings];
        var ringOuter = new float[rings];
        var ringInner = new float[rings];
        var ringW = new Vector4[rings];
        var mpb = new MaterialPropertyBlock();
        for (int r = 0; r < rings; r++)
        {
            float cell = q.innerCellSize * (1 << r);
            ringCell[r] = cell;
            ringOuter[r] = r == 0 ? (cellsAcross + 4) * cell * 0.5f : cellsAcross * cell * 0.5f;
            ringInner[r] = r == 0 ? 0f : ringOuter[r] * 0.5f - 3f * cell;
            var t = clip.transform.Find($"Ring{r}");
            var mr = t != null ? t.GetComponent<MeshRenderer>() : null;
            if (mr != null) { mr.GetPropertyBlock(mpb); ringW[r] = mpb.GetVector("_Ocean_CascadeWeights"); }
        }
        sb.AppendLine();
        sb.AppendLine("rings (cellsAcross " + cellsAcross + "):");
        sb.AppendLine("  ring  cell     covers        c0(swell)  c1(mid)  c2(chop)");
        for (int r = 0; r < rings; r++)
            sb.AppendLine($"  {r,4}  {ringCell[r],6:F2}  {ringInner[r],6:F0}-{ringOuter[r],-6:F0}  " +
                          $"{ringW[r].x,9:F3}  {ringW[r].y,7:F3}  {ringW[r].z,7:F3}");

        // ---- the envelope at these positions, so it is not silently in the mix
        float envHere = 1f;
        var rf = RegionField.Instance;
        if (rf != null) envHere = rf.Evaluate(new Vector2(200f, 0f));
        sb.AppendLine($"\nregion envelope at the sample line: {envHere:F3} " +
                      (Mathf.Abs(envHere - 1f) < 0.02f ? "(neutral, out of the way)" : "(NOT neutral)"));

        // ---- the sweep ------------------------------------------------------
        float fadeEnd = q.displacementFadeDistance;
        float fadeStart = fadeEnd * 0.6f;
        int maxD = 3000;
        var wc = new float[maxD + 1, 3];
        var rms = new float[maxD + 1];
        for (int d = 0; d <= maxD; d++)
        {
            Vector4 w;
            if (perDistance) w = (Vector4)weightsAt.Invoke(null, new object[] { (float)d });
            else
            {
                // The finest ring whose annulus covers this distance is the one
                // drawn nearest the camera there.
                int pick = rings - 1;
                for (int r = 0; r < rings; r++)
                    if (d <= ringOuter[r] && (r == 0 || d >= ringInner[r])) { pick = r; break; }
                w = ringW[pick];
            }
            float fade = 1f - SmoothStep(fadeStart, fadeEnd, d);
            double v = 0;
            for (int c = 0; c < 3; c++)
            {
                float ww = Mathf.Max(0f, w[c]) * fade * envHere;
                wc[d, c] = ww;
                v += (double)(ww * sigmaY[c]) * (ww * sigmaY[c]);
            }
            rms[d] = Mathf.Sqrt((float)v);
        }

        sb.AppendLine();
        sb.AppendLine("surface RMS against distance (metres of vertical displacement):");
        sb.AppendLine("   dist   w:swell   w:mid   w:chop     RMS     chop RMS alone");
        foreach (int d in new[] { 16, 32, 48, 64, 96, 128, 160, 192, 224, 256, 320, 384,
                                  448, 512, 640, 768, 896, 1024, 1280, 1536, 2048, 2600 })
            sb.AppendLine($"  {d,5}   {wc[d, 0],7:F3} {wc[d, 1],7:F3} {wc[d, 2],7:F3}   " +
                          $"{rms[d],7:F3}   {wc[d, 2] * sigmaY[2],7:F4}");

        // The number that says "line" or "ramp": the biggest one-metre fall.
        sb.AppendLine();
        sb.AppendLine("biggest single-metre fall (a step function has one, a ramp has none):");
        ReportStep(sb, "total RMS", rms, maxD);
        for (int c = 0; c < 3; c++)
        {
            var band = new float[maxD + 1];
            for (int d = 0; d <= maxD; d++) band[d] = wc[d, c] * sigmaY[c];
            ReportStep(sb, $"cascade {c} RMS", band, maxD);
        }

        sb.AppendLine();
        sb.AppendLine("where each cascade's weight crosses:");
        for (int c = 0; c < 3; c++)
        {
            int d90 = -1, d50 = -1, d10 = -1, d0 = -1;
            for (int d = 0; d <= maxD; d++)
            {
                float w = wc[d, c];
                if (d90 < 0 && w < 0.90f * wc[0, c]) d90 = d;
                if (d50 < 0 && w < 0.50f * wc[0, c]) d50 = d;
                if (d10 < 0 && w < 0.10f * wc[0, c]) d10 = d;
                if (d0 < 0 && w <= 0.001f) d0 = d;
            }
            sb.AppendLine($"  cascade {c}: 90% at {d90} m, 50% at {d50} m, 10% at {d10} m, gone at {d0} m");
        }

        // ---- the photograph --------------------------------------------------
        // Sea level over open water, phase pinned, one frame. Every row is a
        // known ground distance on flat water, so the contrast profile down the
        // image is a profile against distance — the same axis as the table
        // above, arrived at without going through the rule at all.
        var follow = new GameObject("CascadeFadeFollow").transform;
        follow.position = Vector3.zero;
        clip.FollowOverride = follow;
        float prevFov = cam.fieldOfView;
        cam.transform.SetPositionAndRotation(new Vector3(0f, EyeHeight, 0f),
            Quaternion.Euler(ShotPitch, 0f, 0f));
        cam.fieldOfView = ShotFov;
        yield return null;
        yield return null;

        var rt = RenderTexture.GetTemporary(ShotW, ShotH, 24);
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;
        RenderTexture.active = rt;
        var tex = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        cam.fieldOfView = prevFov;
        System.IO.File.WriteAllBytes(shotFile, tex.EncodeToPNG());

        var px = tex.GetPixels();
        float halfTan = Mathf.Tan(ShotFov * 0.5f * Mathf.Deg2Rad);
        // Distance bands, geometric — the eye reads distance as a ratio too.
        var edges = new float[] { 48, 68, 96, 136, 192, 272, 384, 543, 768, 1086, 1536, 2172, 3072 };
        var accum = new double[edges.Length - 1];
        var count = new int[edges.Length - 1];
        for (int y = 0; y < ShotH; y++)
        {
            // Angle above the camera axis for this row, then depression below
            // the horizon, then where that ray meets flat water.
            float up = Mathf.Atan(((y + 0.5f) / ShotH * 2f - 1f) * halfTan) * Mathf.Rad2Deg;
            float dep = ShotPitch - up;
            if (dep <= 0.05f) continue;           // sky, or so near the horizon it is noise
            float dist = EyeHeight / Mathf.Tan(dep * Mathf.Deg2Rad);
            if (dist < edges[0] || dist >= edges[edges.Length - 1]) continue;
            int bin = 0;
            while (bin < accum.Length - 1 && dist >= edges[bin + 1]) bin++;
            double row = 0;
            int rowN = 0;
            for (int x = 0; x < ShotW - 1; x++)
            {
                var a = px[y * ShotW + x];
                var b = px[y * ShotW + x + 1];
                float la = a.r * 0.2126f + a.g * 0.7152f + a.b * 0.0722f;
                float lb = b.r * 0.2126f + b.g * 0.7152f + b.b * 0.0722f;
                row += Mathf.Abs(lb - la);
                rowN++;
            }
            accum[bin] += row / Mathf.Max(rowN, 1);
            count[bin]++;
        }
        sb.AppendLine();
        sb.AppendLine($"photographed detail: mean |dLuminance| between neighbouring pixels,");
        sb.AppendLine($"binned by ground distance (eye {EyeHeight} m, vfov {ShotFov}, pitch {ShotPitch} down,");
        sb.AppendLine($"{ShotW}x{ShotH}, phase pinned at t={PinnedTime}). Higher = more visible texture.");
        sb.AppendLine("     band (m)     rows   contrast   model RMS");
        for (int i = 0; i < accum.Length; i++)
        {
            if (count[i] == 0) continue;
            int mid = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(edges[i] * edges[i + 1])), 0, maxD);
            sb.AppendLine($"  {edges[i],6:F0}-{edges[i + 1],-6:F0} {count[i],6}   " +
                          $"{accum[i] / count[i],8:F5}   {rms[mid],8:F3}");
        }

        // ---- the overhead A/B ------------------------------------------------
        var rough = Resources.Load<OceanSpectrumSettings>("Ocean/SeaState_Rough");
        if (rough != null)
        {
            OceanTime.Paused = false;
            ocean.SetSettings(rough);
            for (int i = 0; i < 6; i++) yield return null;
            OceanTime.Scrub(PinnedTime);
            OceanTime.Paused = true;
            ocean.StepSimulation();
            cam.transform.SetPositionAndRotation(new Vector3(0f, TopAltitude, 0f),
                Quaternion.Euler(90f, 0f, 0f));
            cam.fieldOfView = TopFov;
            yield return null;
            yield return null;
            var rt2 = RenderTexture.GetTemporary(ShotW, ShotW, 24);
            cam.targetTexture = rt2;
            cam.Render();
            cam.targetTexture = prevTarget;
            RenderTexture.active = rt2;
            var top = new Texture2D(ShotW, ShotW, TextureFormat.RGB24, false);
            top.ReadPixels(new Rect(0, 0, ShotW, ShotW), 0, 0);
            top.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt2);
            cam.fieldOfView = prevFov;
            System.IO.File.WriteAllBytes(topFile, top.EncodeToPNG());
            float halfGround = TopAltitude * Mathf.Tan(TopFov * 0.5f * Mathf.Deg2Rad);
            sb.AppendLine();
            sb.AppendLine($"overhead A/B at {topFile}: {ShotW}x{ShotW} straight down from " +
                          $"{TopAltitude} m, vfov {TopFov}, sea state {rough.name}, phase pinned. " +
                          $"Covers +-{halfGround:F0} m, {ShotW / (halfGround * 2f):F2} px per metre. " +
                          $"The clipmap's rings are squares, so a per-ring weight step draws one.");
            Destroy(top);
        }

        System.IO.File.WriteAllText(outFile, sb.ToString());
        Debug.Log("CascadeFadeProbe: wrote " + outFile);
        Destroy(tex);
        Destroy(follow.gameObject);
        Destroy(gameObject);
    }

    static void ReportStep(StringBuilder sb, string label, float[] v, int maxD)
    {
        float worst = 0f; int at = 0;
        for (int d = 1; d <= maxD; d++)
        {
            float drop = v[d - 1] - v[d];
            if (drop > worst) { worst = drop; at = d; }
        }
        float rel = v[Mathf.Max(at - 1, 0)] > 1e-6f ? worst / v[at - 1] : 0f;
        sb.AppendLine($"  {label,-16} {worst:F4} m in one metre at {at} m " +
                      $"({rel * 100f:F1}% of the value just inside it)");
    }
}
