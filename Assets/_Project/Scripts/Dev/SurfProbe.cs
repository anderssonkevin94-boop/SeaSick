using System.Collections;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.Terrain;
using SeaSick.World;

/// **How much whitewater is there, as a function of how deep the water is?**
///
/// The shoreline should be the foamiest water in the world. It is currently
/// the least, and the reason is a sign error rather than a missing feature:
/// `Ocean.shader` multiplies `foamAmt` by `env`, and `env` near a beach IS the
/// depth cap (`breakFraction * depth / Hs`), which in 8 m of water under a
/// 62 m sea is 0.07. So the term that correctly lies the sea down also removes
/// the whitecaps, and shallow water ends up with LESS foam than deep.
///
/// This measures that curve instead of arguing about it. An ORTHOGRAPHIC
/// camera looks straight down across a real shoreline with `_SS_FoamOnly` on,
/// so every pixel is the shader's own `foamAmt` and every pixel maps to a
/// world position by arithmetic rather than by projection. Pixels are binned
/// by the DEPTH under them, read from the same shore grid the shader samples.
///
/// The gate is the shape of the curve, not its height: foam must RISE as the
/// water shoals. Anything else is the sign error still being there.
///
/// It reports the mean camera distance per depth bin as well, because depth
/// and distance-from-camera are correlated by construction on a shore
/// transect and the chop fades with distance -- so the confound is stated
/// rather than hidden. Before and after are shot from identical geometry, so
/// it cancels in the difference.
///
/// Built on the two lessons ShoreProbe and CalmWaterShot had to learn: THE
/// SHORE GRID FOLLOWS THE SHIP, so this finds a real shoreline and warps her
/// to it rather than measuring where she isn't; and it asserts the grid
/// actually covers the frame before believing a single number.
///
/// Play mode, Sea.unity. Writes /tmp/seasick-surf.txt, -surf-<hs>.png and
/// -surf-<hs>-shaded.png.
public class SurfProbe : MonoBehaviour
{
    const int ShotW = 768, ShotH = 512;
    const double PinT = 4200.0;
    /// Two sea states. Normal is the sea the home shelf actually gets; rough
    /// is what a squall brings. The surf zone's WIDTH should scale with them
    /// -- a bigger sea breaks further out -- so measuring one would not show
    /// whether the term is depth-driven or just a band painted at the beach.
    static readonly float[] StateHs = { 3.5f, 14f };

    static bool running;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SurfProbe: not in play mode"); return; }
        if (running) { Debug.LogError("SurfProbe: already running"); return; }
        running = true;
        new GameObject("SurfProbe").AddComponent<SurfProbe>();
    }

    // Depth bins, metres. Land and "outside the grid" are reported separately:
    // outside the grid the depth is a 1e9 sentinel that must never be averaged
    // into anything.
    static readonly float[] Bins = { 0f, 1f, 2f, 4f, 8f, 16f, 32f, 64f, 128f, 1e8f };

    IEnumerator Start()
    {
        var sb = new StringBuilder();
        System.IO.File.WriteAllText("/tmp/seasick-surf.txt", "SurfProbe: did not finish\n");

        var field = FindAnyObjectByType<TerrainShoreField>();
        var s = field != null ? field.settings : null;
        var motor = FindAnyObjectByType<ShipMotor>();
        var helm = FindAnyObjectByType<HelmInput>();
        var sea = SeaStateController.Instance;
        var region = RegionField.Instance;
        var cam = Camera.main;
        if (helm != null) helm.enabled = false;
        if (motor != null) { motor.ThrottleOrder = 0f; motor.Rudder = 0f; }
        if (field == null || s == null || motor == null || sea == null || region == null || cam == null)
        { Finish(sb, "ABORT: no shore field / ship / controller / region / camera"); yield break; }
        var rb = motor.GetComponent<Rigidbody>();

        float wait = 0f;
        while ((!field.Ready || Island.All.Count == 0) && wait < 25f) { wait += Time.deltaTime; yield return null; }
        if (!field.Ready) { Finish(sb, "ABORT: shore grid never built"); yield break; }
        if (Island.All.Count == 0) { Finish(sb, "ABORT: no islands generated"); yield break; }
        yield return new WaitForSeconds(3f);

        // FIND a shoreline. Nearest island to the ship, walking in along the
        // bearing to her -- the only one whose depths stream in without a
        // voyage.
        Vector3 shipPos = motor.transform.position;
        Island isle = null; float best = float.MaxValue;
        foreach (var i in Island.All)
        {
            if (i == null) continue;
            float d = Vector2.Distance(new Vector2(i.transform.position.x, i.transform.position.z),
                                       new Vector2(shipPos.x, shipPos.z)) - i.MaxRadius;
            if (d < best) { best = d; isle = i; }
        }
        Vector2 centre = new Vector2(isle.transform.position.x, isle.transform.position.z);
        Vector2 outward = (new Vector2(shipPos.x, shipPos.z) - centre).normalized;

        // PERSISTENT, not Temp: this yields between baking the curve and using
        // it, and a Temp allocation is valid for exactly one frame.
        var prm = TerrainParams.From(s);
        NativeArray<float> lut = TerrainCurveLut.Bake(s.profileCurve, Allocator.Persistent);
        Vector2 shoreline = Vector2.zero; bool foundShore = false;
        for (float r = isle.MaxRadius + 400f; r > 5f; r -= 4f)
        {
            Vector2 q = centre + outward * r;
            if (TerrainHeight.Height(new float2(q.x, q.y), prm, lut) > -0.5f) { shoreline = q; foundShore = true; break; }
        }
        lut.Dispose();
        if (!foundShore) { Finish(sb, $"ABORT: no shoreline on '{isle.name}' along the bearing to the ship"); yield break; }

        // Park her a little way off the beach so the grid centres on the water
        // being measured, not on the hilltop behind it.
        Vector2 park = shoreline + outward * 180f;
        if (rb != null)
        {
            rb.position = new Vector3(park.x, 2f, park.y);
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        }
        yield return new WaitForSeconds(5f);

        sb.AppendLine("SurfProbe -- how much whitewater, against how deep the water is");
        sb.AppendLine($"island '{isle.name}' at ({centre.x:F0}, {centre.y:F0}); shoreline " +
                      $"({shoreline.x:F0}, {shoreline.y:F0}); ship parked at ({park.x:F0}, {park.y:F0})");
        sb.AppendLine($"shore grid {region.ShoreN} texels");
        sb.AppendLine();

        // The frame: straight down, centred a little offshore of the beach so
        // the whole transect from dry land to deep water is in one picture.
        const float OrthoSize = 200f;                       // 400 m tall, 600 m wide
        Vector2 look = shoreline + outward * 140f;
        var down = new GameObject("SurfProbeCam").AddComponent<Camera>();
        down.CopyFrom(cam);
        down.depth = cam.depth + 10f;
        down.orthographic = true;
        down.orthographicSize = OrthoSize;
        down.transform.position = new Vector3(look.x, 500f, look.y);
        down.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        float aspect = ShotW / (float)ShotH;

        // DRAW ONLY THE OCEAN. The first run of this probe read the beach and
        // called it foam: in 0-1 m of water the displaced surface dips below
        // the seabed and the terrain shows through, and terrain knows nothing
        // about _SS_FoamOnly, so its sand came back as 0.09 mean / 0.85 peak
        // in the shallowest bin -- the one bin the gate turns on. Cull to the
        // ocean's own layer and clear to BLUE, which _SS_FoamOnly can never
        // produce (it writes foamAmt to all three channels, so every ocean
        // pixel is exactly grey). Anything not grey is not water and is not
        // counted.
        int oceanLayer = -1;
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            var m = r.sharedMaterial;
            if (m != null && m.shader != null && m.shader.name == "SeaSick/Ocean")
            { oceanLayer = r.gameObject.layer; break; }
        }
        if (oceanLayer < 0) { Finish(sb, "ABORT: found no renderer using SeaSick/Ocean -- cannot isolate the water"); yield break; }
        int fullMask = down.cullingMask;
        var fullClear = down.clearFlags;
        sb.AppendLine($"camera culled to the ocean layer ({LayerMask.LayerToName(oceanLayer)}), " +
                      "cleared to blue; only exactly-grey pixels are counted as water");

        // ASSERT THE GRID COVERS THE FRAME before believing anything. Outside
        // it ShoreWetDepth returns a 1e9 sentinel and the sea is left
        // untouched, which looks exactly like a broken depth coupling.
        var rp = region.Params;
        int outside = 0, probes = 0;
        for (int a = -4; a <= 4; a++)
            for (int b = -4; b <= 4; b++)
            {
                Vector2 q = look + new Vector2(a * OrthoSize * aspect / 4f, b * OrthoSize / 4f);
                probes++;
                if (rp.ShoreWetDepth(new float2(q.x, q.y), region.Shore).z > 1e8f) outside++;
            }
        sb.AppendLine($"grid coverage of the frame: {probes - outside}/{probes} sample points inside" +
                      (outside > probes / 3 ? "   *** most of the frame has no seabed; every number below is about nothing ***" : ""));
        sb.AppendLine();

        // Pin the hour at noon and make the sky snap rather than ease over
        // its 3 s time constant. The look shots are the whole point of the
        // exercise being visual, and the first run of them came back at NIGHT
        // -- a 180 s day means "whenever the probe happened to start" is a
        // coin toss. Driven through SkyDirector's OWN pinTime by reflection,
        // because it reasserts the light's rotation from TimeOfDay every
        // LateUpdate, so writing the light lasts exactly one frame.
        var sky = FindAnyObjectByType<SkyDirector>();
        var pinField = sky != null ? typeof(SkyDirector).GetField("pinTime",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) : null;
        var respField = sky != null ? typeof(SkyDirector).GetField("response",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) : null;
        object savedPin = pinField != null ? pinField.GetValue(sky) : null;
        object savedResp = respField != null ? respField.GetValue(sky) : null;
        if (pinField != null) pinField.SetValue(sky, 0.5f);
        if (respField != null) respField.SetValue(sky, 25f);
        sb.AppendLine(pinField != null ? "hour pinned to noon for the look shots"
                                       : "*** could not pin the hour -- look shots are at whatever time it is ***");
        sb.AppendLine();

        // A deck-level look at the same beach, because the complaint is
        // visual and an orthographic plan view flatters a surf zone: from
        // 500 m up a band of white is obviously a band, and the question is
        // what it looks like from a boat.
        var eye = new GameObject("SurfProbeEyeCam").AddComponent<Camera>();
        eye.CopyFrom(cam);
        eye.depth = cam.depth + 11f;
        eye.transform.position = new Vector3(park.x, 9f, park.y);
        eye.transform.rotation = Quaternion.LookRotation(
            new Vector3(shoreline.x, 3f, shoreline.y) - eye.transform.position, Vector3.up);

        var rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32);
        var shot = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);

        // Depth per pixel, once: the geometry is the same for every sea state,
        // and this is 400k ShoreWetDepth calls.
        var depth = new float[ShotW * ShotH];
        var dist = new float[ShotW * ShotH];
        for (int y = 0; y < ShotH; y++)
            for (int x = 0; x < ShotW; x++)
            {
                float u = (x + 0.5f) / ShotW * 2f - 1f;
                float v = (y + 0.5f) / ShotH * 2f - 1f;
                float wx = look.x + u * OrthoSize * aspect, wz = look.y + v * OrthoSize;
                depth[y * ShotW + x] = rp.ShoreWetDepth(new float2(wx, wz), region.Shore).z;
                dist[y * ShotW + x] = Vector2.Distance(new Vector2(wx, wz), look);
            }

        float lastNominal = -1f;
        for (int pass = 0; pass < StateHs.Length; pass++)
        {
            float hs = StateHs[pass];
            // EACH PASS PINS AT A LATER INSTANT, and that is not cosmetic.
            // SeaStateController throttles its rebuild on
            // `OceanTime.Now - lastRebuildTime`, so a probe that pins every
            // pass at the SAME instant scrubs the clock BACKWARDS relative to
            // the rebuild it just caused -- the difference goes negative and
            // the controller returns early for the rest of the run. The second
            // sea state then silently never arrives and the probe measures the
            // first one twice, reporting it under the second one's name. It
            // did exactly that, and only "declared nominalHs 4" under a
            // heading that said Hs 14 gave it away. Same family as the
            // readback ring that ranked slots by timestamp and wedged on any
            // backward scrub; probes scrub backwards constantly.
            double pinT = PinT + pass * 600.0;
            OceanTime.Scrub(pinT);
            OceanTime.Paused = false;
            sea.ForceHs(hs);
            yield return new WaitForSeconds(3f);
            OceanTime.Scrub(pinT);
            OceanTime.Paused = true;
            yield return new WaitForSeconds(1.5f);

            // AND CHECK IT ARRIVED. A gate that fails green is worse than no
            // gate: the numbers above were produced by a run where this had
            // not arrived, and they looked perfectly plausible.
            float nominal = OceanRenderer.Instance.Settings != null
                ? OceanRenderer.Instance.Settings.nominalHs : 0f;
            float measured = 4f * RmsAround(new Vector3(park.x, 0f, park.y), 60);
            bool arrived = Mathf.Abs(nominal - lastNominal) > 0.01f;
            if (!arrived)
                sb.AppendLine($"*** THE SPECTRUM DID NOT REBUILD for Hs {hs:F1}: declared nominalHs is " +
                              $"still {nominal:F1}. Everything below is the PREVIOUS sea. ***");
            lastNominal = nominal;

            Shader.SetGlobalFloat("_SS_FoamOnly", 1f);
            down.cullingMask = 1 << oceanLayer;
            down.clearFlags = CameraClearFlags.SolidColor;
            down.backgroundColor = new Color(0f, 0f, 1f);

            // The BEFORE and the AFTER, one wave phase apart from each other
            // by nothing at all. _SS_SurfOff takes the new term out of the
            // shipped shader, so both halves of the comparison come off the
            // same frame of the same sea instead of two builds an hour apart.
            Shader.SetGlobalFloat("_SS_SurfOff", 1f);
            yield return null; yield return null;
            Capture(down, rt, shot);
            var plain = shot.GetPixels();
            System.IO.File.WriteAllBytes($"/tmp/seasick-surf-{hs:F1}-nosurf.png", shot.EncodeToPNG());

            Shader.SetGlobalFloat("_SS_SurfOff", 0f);
            yield return null; yield return null;
            Capture(down, rt, shot);
            var foam = shot.GetPixels();
            System.IO.File.WriteAllBytes($"/tmp/seasick-surf-{hs:F1}.png", shot.EncodeToPNG());

            // The look shot gets the whole scene back -- the beach is the
            // point of the picture even though it must not be in the numbers.
            Shader.SetGlobalFloat("_SS_FoamOnly", 0f);
            down.cullingMask = fullMask;
            down.clearFlags = fullClear;
            yield return null; yield return null;
            Capture(down, rt, shot);
            System.IO.File.WriteAllBytes($"/tmp/seasick-surf-{hs:F1}-shaded.png", shot.EncodeToPNG());
            Capture(eye, rt, shot);
            System.IO.File.WriteAllBytes($"/tmp/seasick-surf-{hs:F1}-deck.png", shot.EncodeToPNG());

            sb.AppendLine($"=== Hs {hs:F1} m ({sea.CurrentStateName}), declared nominalHs {nominal:F0}, " +
                          $"measured 4xRMS {measured:F1} m at the ship ===");
            sb.AppendLine("  water depth        pixels   foam BEFORE   foam AFTER   max after   mean dist");
            float shallowest = -1f, deepest = -1f, shallowBefore = -1f, deepBefore = -1f;
            for (int b = 0; b + 1 < Bins.Length; b++)
            {
                double sf = 0, sp = 0, sd = 0; float mx = 0f; int n = 0;
                for (int k = 0; k < foam.Length; k++)
                {
                    float d = depth[k];
                    if (d > 1e8f || d < Bins[b] || d >= Bins[b + 1]) continue;
                    if (!IsWater(foam[k]) || !IsWater(plain[k])) continue;
                    float f = foam[k].r; sf += f; sp += plain[k].r; sd += dist[k]; mx = Mathf.Max(mx, f); n++;
                }
                string label = $"  {Bins[b],5:F0}-{(Bins[b + 1] > 1e7f ? "  deep" : Bins[b + 1].ToString("F0").PadLeft(6))} m";
                if (n == 0) { sb.AppendLine(label + "        0            --           --          --"); continue; }
                float mf = (float)(sf / n), mp = (float)(sp / n);
                if (b == 0) { shallowest = mf; shallowBefore = mp; }
                if (Bins[b + 1] > 1e7f) { deepest = mf; deepBefore = mp; }
                sb.AppendLine(label + $" {n,8}   {mp,11:F4}   {mf,10:F4}  {mx,10:F4}  {sd / n,8:F0} m");
            }
            // Land, and water the grid does not know about, both stated so a
            // reader can see they were kept out rather than averaged in.
            int land = 0, off = 0, notWater = 0;
            for (int k = 0; k < foam.Length; k++)
            {
                if (!IsWater(foam[k])) notWater++;
                if (depth[k] > 1e8f) off++; else if (depth[k] <= 0f) land++;
            }
            sb.AppendLine($"  (land {land} px by depth, outside the grid {off} px, " +
                          $"no ocean drawn {notWater} px -- all excluded above)");
            sb.AppendLine();
            sb.AppendLine($"  THE SHAPE, before: shallowest bin {shallowBefore:F4} vs deepest {deepBefore:F4}  ->  " +
                          (shallowBefore > deepBefore ? "foam RISES into the shallows"
                                                      : $"foam FALLS into the shallows by {(deepBefore / Mathf.Max(shallowBefore, 1e-4f)):F1}x (the sign error)"));
            sb.AppendLine($"  THE SHAPE, after:  shallowest bin {shallowest:F4} vs deepest {deepest:F4}  ->  " +
                          (shallowest > deepest ? $"foam RISES into the shallows by {(shallowest / Mathf.Max(deepest, 1e-4f)):F1}x"
                                                : "foam FALLS into the shallows (the sign error)"));
            sb.AppendLine("  (framebuffer values, so sRGB-encoded -- a monotone transform of foamAmt,");
            sb.AppendLine("   which is all the SHAPE of the curve needs; do not read them as amounts)");
            sb.AppendLine();
        }

        Shader.SetGlobalFloat("_SS_FoamOnly", 0f);
        Shader.SetGlobalFloat("_SS_SurfOff", 0f);
        OceanTime.Paused = false;
        sea.ReleaseForce();
        if (pinField != null) pinField.SetValue(sky, savedPin);
        if (respField != null) respField.SetValue(sky, savedResp);
        Destroy(down.gameObject); Destroy(eye.gameObject); Destroy(rt); Destroy(shot);
        Finish(sb, null);
    }

    /// Ocean under _SS_FoamOnly writes foamAmt into all three channels, so
    /// water is exactly grey and the blue clear colour never is.
    static bool IsWater(Color c) =>
        Mathf.Abs(c.r - c.b) < 0.004f && Mathf.Abs(c.r - c.g) < 0.004f;

    /// Surface RMS on a ring grid about a point. Sampling one point over time
    /// would measure the swell PERIOD as much as its height.
    static float RmsAround(Vector3 centre, int n)
    {
        if (!OceanSampler.Ready) return 0f;
        float sum = 0f, sumSq = 0f; int count = 0;
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            for (int r = 1; r <= 5; r++)
            {
                Vector3 q = centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (r * 12f);
                float h = OceanSampler.SampleImmediate(q).height;
                sum += h; sumSq += h * h; count++;
            }
        }
        if (count == 0) return 0f;
        float mean = sum / count;
        return Mathf.Sqrt(Mathf.Max(0f, sumSq / count - mean * mean));
    }

    static void Capture(Camera cam, RenderTexture rt, Texture2D shot)
    {
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        shot.Apply();
        cam.targetTexture = prevTarget;
        RenderTexture.active = prevActive;
    }

    void Finish(StringBuilder sb, string err)
    {
        running = false;
        Shader.SetGlobalFloat("_SS_FoamOnly", 0f);
        Shader.SetGlobalFloat("_SS_SurfOff", 0f);
        OceanTime.Paused = false;
        if (err != null) sb.AppendLine(err);
        System.IO.File.WriteAllText("/tmp/seasick-surf.txt", sb.ToString());
        Debug.Log("SurfProbe:\n" + sb);
        Destroy(gameObject);
    }
}
