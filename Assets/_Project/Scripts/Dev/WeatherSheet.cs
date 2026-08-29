using System.Collections;
using System.Reflection;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;

/// **Contact sheets for the two things the 2026-08-28 realism pass changed
/// about how the sea READS, neither of which has ever been looked at.**
///
/// Same discipline as ShaderStrip, and for the same reason: the sea drifts, so
/// you cannot hold two states side by side in your head. Everything that would
/// confound the comparison is pinned -- one wave phase, one seed, one camera,
/// one sun, one sea state, the ship held still and IN FRAME for scale.
///
///   axes.png    Two rows.
///               TOP: the wind sea turned to 0 / 90 / 180 / 270 degrees off
///               the swell, with the swell left exactly where the anchor
///               authored it. Four relationships, one sea.
///               BOTTOM: the SHIPPED axis rule (SeaStateController.ApplyAxes,
///               never a copy of it) evaluated at t0, +30 s, +60 s, +90 s of
///               game time -- BUT RENDERED AT THE PINNED WAVE PHASE. That is
///               the whole trick: the axes take a time argument while the wave
///               phase comes from OceanTime, so these four tiles differ by
///               WHAT THE AXES LAYER DOES IN 30 SECONDS and by nothing else --
///               the wave phase, the sea's size, the camera and the sun are all
///               pinned. Whatever moves between them is exactly what a player
///               sees over half a minute. (The axes layer turns the wind fast,
///               the swell slowly and swings gamma with the wind, so all three
///               move; the wind is simply the one that moves far enough to
///               see.)
///
///   patches.png Two rows.
///               TOP: the patch field forced UNIFORM at its low end (slick),
///               at 1 (the authored sea), and at its high end (ruffle), from
///               the deck. Only the multiplier differs.
///               BOTTOM: the SHIPPED field, which varies in space, seen from
///               140 m up so several hundred metres of water is in frame --
///               which is the only way to find out whether a ruffle patch
///               reads as a BAND.
///
/// Both sheets come with numbers, because "does it read as X" is not a
/// question a picture answers on its own. Every tile is scored for mean
/// luminance (is it darker?) and horizontal grain (is it rougher?) over the
/// water only, and every row gets a pairwise |difference| matrix -- if two
/// tiles score the same against each other they are the same picture, whatever
/// the theory says they should be.
///
/// Plain C# for Coplay, and NOT in Dev/Editor -- a MonoBehaviour in an Editor
/// folder cannot be AddComponent-ed. Run in play mode in Sea.unity.
///
/// EXECUTION ORDER 10000 IS LOAD-BEARING. The first version pinned only the
/// ship's X and Z and let ChaseCamera do what it liked, and the control tile
/// -- the same sea drawn twice -- came back at 0.051 against a 0.084 signal.
/// The hull was still settling on the frozen surface, the camera was still
/// easing after it, and the wake ripples and the spray run off Time.deltaTime
/// and not off OceanTime, so none of them stop when the ocean does. Every
/// number on the sheet was that noise. The ship is frozen kinematic, the
/// camera transform is reasserted AFTER ChaseCamera's own LateUpdate, and the
/// ripple sim and the spray are switched off for the run.
[DefaultExecutionOrder(10000)]
public class WeatherSheet : MonoBehaviour
{
    const int TileW = 512, TileH = 340;
    const double PinT = 4200.0;      // one arbitrary but FIXED instant
    // Two sea states, because the first run showed they answer differently.
    // Rough (Hs 14) is the middle of the ocean's range and the sea most of a
    // voyage is spent in -- and it is largely under whitecaps, which is itself
    // part of the answer. Lively (Hs 4.5) is the shelf sea near home, where
    // the chop is the water rather than a texture on top of a roller, and it
    // is the state in which the grain is actually legible.
    const float RoughHs = 14f, LivelyHs = 4.5f;
    float sheetHs = RoughHs;
    // The band of each tile the numbers are taken over: the lower part is
    // water, and the middle column is the ship.
    const float WaterTop = 0.55f, ShipLo = 0.20f, ShipHi = 0.80f;

    static bool running;             // execute_script times out and the script
                                     // usually ran anyway; two instances of a
                                     // probe silently corrupt a run.
    bool patchMode;

    public static void Axes() => Spawn(false, RoughHs, "");
    public static void AxesLively() => Spawn(false, LivelyHs, "-lively");
    public static void Patches() => Spawn(true, RoughHs, "");
    public static void PatchesLively() => Spawn(true, LivelyHs, "-lively");

    static void Spawn(bool patches, float hs, string suffix)
    {
        if (!Application.isPlaying) { Debug.LogError("WeatherSheet: not in play mode"); return; }
        if (running) { Debug.LogError("WeatherSheet: already running"); return; }
        running = true;
        var go = new GameObject("WeatherSheet");
        var c = go.AddComponent<WeatherSheet>();
        c.patchMode = patches; c.sheetHs = hs; c.suffix = suffix;
    }

    string suffix = "";

    bool pin; Vector3 pinAt; Quaternion pinRot; ShipMotor motor;
    Transform pinCam; Vector3 camPos; Quaternion camRot;

    IEnumerator Start()
    {
        var sb = new StringBuilder();
        string stem = "/tmp/seasick-weather-" + (patchMode ? "patches" : "axes") + suffix;
        string outTxt = stem + ".txt";
        System.IO.File.WriteAllText(outTxt, "WeatherSheet: did not finish\n");

        motor = FindAnyObjectByType<ShipMotor>();
        var helm = FindAnyObjectByType<HelmInput>();
        if (helm != null) helm.enabled = false;
        var ctrl = SeaStateController.Instance;
        var cam = Camera.main;
        var ocean = OceanRenderer.Instance;
        if (motor == null || ctrl == null || cam == null || ocean == null || !OceanSampler.Ready)
        { Finish(sb, outTxt, "ABORT: no ship / controller / camera / renderer / sampler"); yield break; }
        if (motor != null) { motor.ThrottleOrder = 0f; motor.Rudder = 0f; }

        var rb = motor.GetComponent<Rigidbody>();

        // Everything that animates off Time.deltaTime rather than OceanTime
        // keeps running when the ocean is frozen, and each of them redraws
        // differently every frame. They are the whole reason a control tile
        // that must be zero came back at 0.05.
        var ripples = FindAnyObjectByType<DynamicWaterSim>();
        var spray = FindAnyObjectByType<StormSpray>();
        bool ripplesWere = ripples != null && ripples.enabled;
        bool sprayWas = spray != null && spray.enabled;
        if (ripples != null) ripples.enabled = false;
        if (spray != null) spray.enabled = false;
        // Disabling the ripple sim FREEZES its buffer, it does not clear it,
        // and the shader keeps sampling the stale texture -- which showed up
        // from overhead as a grey square of dead foam sitting on the water
        // around the ship, in every tile. Unbind the rect instead; SampleSim
        // guards on rect.z and returns nothing. DynamicWaterSim re-pushes this
        // every frame once it is switched back on.
        Vector4 simRectWas = Shader.GetGlobalVector("_Ocean_SimRect");
        Shader.SetGlobalVector("_Ocean_SimRect", Vector4.zero);

        Vector2 shipXZ = new Vector2(motor.transform.position.x, motor.transform.position.z);

        // Pin the sea, then freeze the clock. ORDER MATTERS and is not
        // obvious: SeaStateController throttles its rebuild on
        // OceanTime.Now - lastRebuildTime, so with the clock already paused
        // that difference never grows and the spectrum silently never updates.
        // Unpause, force, let it land, then scrub and freeze.
        OceanTime.Paused = false;
        ctrl.ForceHs(sheetHs);
        yield return new WaitForSeconds(3f);
        OceanTime.Scrub(PinT);
        OceanTime.Paused = true;
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        // Let her settle on the frozen surface and the camera ease in behind
        // her, THEN nail both down. Freezing before they have settled just
        // pins a ship mid-fall.
        yield return new WaitForSeconds(3f);
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.isKinematic = true; }
        pinAt = motor.transform.position;
        pinRot = motor.transform.rotation;
        shipXZ = new Vector2(pinAt.x, pinAt.z);
        pinCam = cam.transform; camPos = pinCam.position; camRot = pinCam.rotation;
        pin = true;
        yield return null; yield return null;

        // SeaStateController is left ENABLED rather than disabled the way
        // DivergenceProbe does it, because in Sea.unity disabling it nulls
        // Instance and SkyDirector then reads storminess 0 and eases the sky
        // toward calm underneath the sheet. Forced + paused it settles after
        // one rebuild and returns early forever after (severity is pinned and
        // the axis offsets are a pure function of a frozen time), so it never
        // touches the spectrum again. Not trusted, though -- every tile
        // asserts the renderer is still holding OUR settings object, so a
        // stomp shows up in the report instead of quietly producing a sheet of
        // the wrong sea.
        var scratch = ScriptableObject.CreateInstance<OceanSpectrumSettings>();
        float sev = ctrl.SeverityForHs(sheetHs);

        Shader.SetGlobalVector("_SS_LayerOff", Vector4.zero);
        var rt = new RenderTexture(TileW, TileH, 24, RenderTextureFormat.ARGB32);
        var tile = new Texture2D(TileW, TileH, TextureFormat.RGB24, false);

        sb.AppendLine(patchMode
            ? "WeatherSheet -- patches: does a ruffle read as rougher water, or as noise?"
            : "WeatherSheet -- axes: what does the wind turning against the swell look like?");
        sb.AppendLine($"sea forced to Hs {sheetHs} m ({ctrl.CurrentStateName}), " +
                      $"wave phase pinned at OceanTime {PinT}, ship held at " +
                      $"({pinAt.x:F0}, {pinAt.z:F0}), tile {TileW}x{TileH}");
        sb.AppendLine();

        if (patchMode) yield return PatchSheet(sb, stem, ctrl, sev, scratch, ocean, cam, rt, tile, shipXZ);
        else           yield return AxesSheet(sb, stem, ctrl, sev, scratch, ocean, cam, rt, tile, shipXZ);

        // Put everything back. A stuck pause, a stuck force or a ship left
        // kinematic outlives the probe and the next thing to run measures a
        // frozen sea from a boat that cannot float.
        OceanTime.Paused = false;
        ctrl.ReleaseForce();
        pin = false;
        if (rb != null) rb.isKinematic = false;
        if (ripples != null) ripples.enabled = ripplesWere;
        if (spray != null) spray.enabled = sprayWas;
        Shader.SetGlobalVector("_Ocean_SimRect", simRectWas);
        Destroy(rt); Destroy(tile); Destroy(scratch);
        Finish(sb, outTxt, null);
    }

    // ---- sheet 1: the two axes -------------------------------------------

    IEnumerator AxesSheet(StringBuilder sb, string stem, SeaStateController ctrl, float sev,
                          OceanSpectrumSettings scratch, OceanRenderer ocean,
                          Camera cam, RenderTexture rt, Texture2D tile, Vector2 p)
    {
        const int Cols = 6;
        var sheet = new Texture2D(TileW * Cols, TileH * 2, TextureFormat.RGB24, false);
        var top = new Color[Cols][]; var bot = new Color[Cols][];
        var topName = new string[Cols]; var botName = new string[Cols];

        // ---- row A: the wind turned against a fixed swell ------------------
        float[] rel = { 0f, 45f, 90f, 135f, 180f, 270f };
        string[] relName = { "wind WITH swell", "wind +45", "wind ACROSS (+90)",
                             "wind +135", "wind AGAINST (180)", "wind ACROSS (-90)" };
        sb.AppendLine("ROW A -- the wind sea turned against a swell left where the anchor put it.");
        sb.AppendLine("  The swell (and its second train) are untouched in every tile; only");
        sb.AppendLine("  windDirectionDeg moves. This is the relationship the split exists to make.");
        for (int i = 0; i < Cols; i++)
        {
            ctrl.BlendAt(sev, scratch);
            scratch.windDirectionDeg = scratch.swellDirectionDeg + rel[i];
            yield return Push(ocean, scratch);
            Capture(cam, rt, tile);
            top[i] = tile.GetPixels(); topName[i] = relName[i];
            sb.AppendLine($"  A{i} {relName[i],-20} wind {Norm(scratch.windDirectionDeg),6:F1} deg  " +
                          $"swell {Norm(scratch.swellDirectionDeg),6:F1}  " +
                          $"cross {Mathf.Abs(Mathf.DeltaAngle(scratch.swellDirectionDeg, scratch.swell2DirectionDeg)),5:F1}  " +
                          $"{Held(ocean, scratch)}");
        }
        sb.AppendLine();

        // ---- row B: the shipped rule, four moments, ONE wave phase ---------
        // The axes take a time argument; the wave phase comes from OceanTime.
        // So the first four tiles are the same water with the weather at four
        // different moments, which is the only honest way to ask "does the
        // grain visibly rotate".
        //
        // THE LAST TWO TILES ARE THE CONTROLS, and without them the sheet says
        // nothing. Four pictures that differ tell you the pictures differ;
        // they cannot tell you whether that is the wind turning or simply two
        // draws of the same sea. So:
        //   B4 repeats B0 exactly -- it must come out at zero, or the
        //      measurement is noise and every other number here is worthless;
        //   B5 holds the WEATHER at B0 and advances only the WAVE PHASE by the
        //      same 30 s -- the picture-change from the sea merely moving,
        //      which is the yardstick every other difference has to beat.
        double[] dt = { 0.0, 30.0, 60.0, 90.0 };
        sb.AppendLine("ROW B -- the SHIPPED rule (ApplyAxes) at +0 / +30 / +60 / +90 s of game time,");
        sb.AppendLine("  rendered at ONE pinned wave phase. Everything the axes layer moves in");
        sb.AppendLine("  30 s moves here -- wind fast, swell slowly, gamma with the wind -- and");
        sb.AppendLine("  nothing else does. windTurnPeriod is the knob under the fast one.");
        sb.AppendLine("  B4 and B5 are CONTROLS: B4 is B0 drawn again (must be 0), B5 is B0's");
        sb.AppendLine("  weather with only the WAVE PHASE moved on 30 s (the sea just moving).");
        float prevWind = 0f;
        for (int i = 0; i < 4; i++)
        {
            ctrl.BlendAt(sev, scratch);
            ctrl.ApplyAxes(scratch, p, PinT + dt[i]);
            yield return Push(ocean, scratch);
            Capture(cam, rt, tile);
            bot[i] = tile.GetPixels(); botName[i] = $"+{dt[i]:F0} s (weather)";
            float w = Norm(scratch.windDirectionDeg);
            string turned = i == 0 ? "" : $"  turned {Mathf.Abs(Mathf.DeltaAngle(prevWind, w)),5:F1} deg in 30 s";
            sb.AppendLine($"  B{i} +{dt[i],3:F0} s  wind {w,6:F1} deg  " +
                          $"swell {Norm(scratch.swellDirectionDeg),6:F1}  " +
                          $"wind-against-swell {Mathf.Abs(Mathf.DeltaAngle(scratch.windDirectionDeg, scratch.swellDirectionDeg)),5:F1}" +
                          turned + $"  {Held(ocean, scratch)}");
            prevWind = w;
        }

        // B4: the same sea drawn a second time.
        ctrl.BlendAt(sev, scratch);
        ctrl.ApplyAxes(scratch, p, PinT);
        yield return Push(ocean, scratch);
        Capture(cam, rt, tile);
        bot[4] = tile.GetPixels(); botName[4] = "CONTROL B0 redrawn";
        sb.AppendLine("  B4 control: B0's weather drawn again at the same phase");

        // B5: B0's weather, 30 s of wave motion.
        OceanTime.Scrub(PinT + 30.0);
        yield return null; yield return null; yield return null;
        Capture(cam, rt, tile);
        bot[5] = tile.GetPixels(); botName[5] = "CONTROL waves +30 s";
        sb.AppendLine("  B5 control: B0's weather, wave phase advanced 30 s (weather held still)");
        OceanTime.Scrub(PinT);
        yield return null; yield return null;
        sb.AppendLine();

        for (int i = 0; i < Cols; i++)
        {
            sheet.SetPixels(i * TileW, TileH, TileW, TileH, top[i]);   // row A on top
            sheet.SetPixels(i * TileW, 0, TileW, TileH, bot[i]);
        }
        sheet.Apply();
        System.IO.File.WriteAllBytes(stem + ".png", sheet.EncodeToPNG());
        Destroy(sheet);

        Score(sb, "ROW A (wind vs swell)", topName, top);
        Score(sb, "ROW B (the shipped weather 30 s apart, plus two controls)", botName, bot);

        // How fast the wind actually turns here, measured rather than taken
        // from the period. 42 deg/min is a claim about a knob; this is the
        // distribution the player is in.
        sb.AppendLine("HOW FAST THE WIND ACTUALLY TURNS (the shipped rule, at this place):");
        float worst = 0f, sum = 0f; int n = 0;
        float last = Norm(WindAt(ctrl, scratch, sev, p, PinT));
        for (int k = 1; k <= 20; k++)          // 10 minutes at 30 s
        {
            float w = Norm(WindAt(ctrl, scratch, sev, p, PinT + k * 30.0));
            float d = Mathf.Abs(Mathf.DeltaAngle(last, w));
            worst = Mathf.Max(worst, d); sum += d; n++; last = w;
        }
        sb.AppendLine($"  over 10 min of game time: mean {sum / n * 2f:F1} deg/min, " +
                      $"worst 30 s step {worst:F1} deg (= {worst * 2f:F1} deg/min)");
        sb.AppendLine($"  a day is 180 s, so the wind turns {sum / n * 6f:F0} deg in a game day");
        sb.AppendLine();
        // ---- row C: the same wind rotation seen from STRAIGHT ABOVE --------
        // The grain-angle column above is measured in SCREEN space from a
        // camera 13 m over the water, where perspective foreshortens every
        // wave into a horizontal band and the measure saturates near vertical
        // whatever the sea is doing. That is fine for "did the picture change"
        // and useless for "did the grain turn". An orthographic camera looking
        // straight down has no foreshortening at all, so there the measured
        // orientation IS the wave direction -- which separates the two answers
        // that matter: whether the chop rotates in the WORLD (it must; the
        // spectrum says so) and whether that rotation is legible from the DECK
        // (a different question, and the one actually being asked).
        var down = new GameObject("WeatherSheetTopCam").AddComponent<Camera>();
        down.CopyFrom(cam);
        down.depth = cam.depth + 10f;
        down.orthographic = true;
        down.orthographicSize = 60f;      // a 120 m square: chop and mid, not swell
        down.transform.position = new Vector3(pinAt.x, 220f, pinAt.z);
        down.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        var tops = new Texture2D(TileW * 4, TileH * 2, TextureFormat.RGB24, false);
        var topd = new Color[4][]; var topdName = new string[4];
        var nofoam = new Color[4][]; var nofoamName = new string[4];
        sb.AppendLine("ROW C -- the same wind rotation from an ORTHOGRAPHIC camera straight");
        sb.AppendLine("  overhead, 180 x 120 m. No perspective, so the measured grain angle IS");
        sb.AppendLine("  the wave direction. This is what separates 'the chop rotates in the");
        sb.AppendLine("  world' from 'you can see it rotate from the deck'.");
        sb.AppendLine("  THE SECOND HALF HAS FOAM SUPPRESSED (_SS_LayerOff.w), because foam is");
        sb.AppendLine("  isotropic torn noise with far more contrast than the water under it,");
        sb.AppendLine("  and if it dominates the gradient field then the first half is measuring");
        sb.AppendLine("  the foam and not the sea. Which of the two moves with the wind is the");
        sb.AppendLine("  whole answer: if only the foam-free half does, the chop IS turning and");
        sb.AppendLine("  the whitecaps are what hide it.");
        float[] relC = { 0f, 90f, 180f, 270f };
        for (int i = 0; i < 4; i++)
        {
            ctrl.BlendAt(sev, scratch);
            scratch.windDirectionDeg = scratch.swellDirectionDeg + relC[i];
            yield return Push(ocean, scratch);

            Shader.SetGlobalVector("_SS_LayerOff", Vector4.zero);
            yield return null; yield return null;
            Capture(down, rt, tile);
            topd[i] = tile.GetPixels(); topdName[i] = $"wind {relC[i]:F0} off swell";

            // Foam off. Suppressing the foam ALSO takes the fresnel sky mix
            // off its (1 - foamAmt) factor, so this is not a pure subtraction
            // -- it is "the water with the whitecaps taken away", which is
            // exactly the picture the question is about.
            Shader.SetGlobalVector("_SS_LayerOff", new Vector4(0, 0, 0, 1));
            yield return null; yield return null;
            Capture(down, rt, tile);
            nofoam[i] = tile.GetPixels(); nofoamName[i] = $"wind {relC[i]:F0}, NO FOAM";
        }
        Shader.SetGlobalVector("_SS_LayerOff", Vector4.zero);
        Destroy(down.gameObject);
        for (int i = 0; i < 4; i++)
        {
            tops.SetPixels(i * TileW, TileH, TileW, TileH, topd[i]);
            tops.SetPixels(i * TileW, 0, TileW, TileH, nofoam[i]);
        }
        tops.Apply();
        System.IO.File.WriteAllBytes(stem + "-topdown.png", tops.EncodeToPNG());
        Destroy(tops);
        ScoreFull(sb, "ROW C (wind rotation from straight above, shipped shader)", topdName, topd);
        ScoreFull(sb, "ROW C (the same, FOAM SUPPRESSED)", nofoamName, nofoam);

        sb.AppendLine($"tiles: {stem}.png, {Cols} wide x 2 high, row A on TOP");
        sb.AppendLine($"       {stem}-topdown.png, 4 wide x 2, overhead; TOP row shipped, BOTTOM row foam off");
    }

    static float WindAt(SeaStateController ctrl, OceanSpectrumSettings s, float sev, Vector2 p, double t)
    {
        ctrl.BlendAt(sev, s);
        ctrl.ApplyAxes(s, p, t);
        return s.windDirectionDeg;
    }

    // ---- sheet 2: the patch field ----------------------------------------

    IEnumerator PatchSheet(StringBuilder sb, string stem, SeaStateController ctrl, float sev,
                           OceanSpectrumSettings scratch, OceanRenderer ocean,
                           Camera cam, RenderTexture rt, Texture2D tile, Vector2 p)
    {
        const int Cols = 3;
        var wf = WeatherField.Instance;
        if (wf == null) { sb.AppendLine("ABORT: no WeatherField in the scene"); yield break; }

        // The shipped ranges, read off the live component rather than
        // hardcoded -- a duplicated constant is a gate that stops gating.
        var fLo = typeof(WeatherField).GetField("patchRangeLo", BindingFlags.NonPublic | BindingFlags.Instance);
        var fHi = typeof(WeatherField).GetField("patchRangeHi", BindingFlags.NonPublic | BindingFlags.Instance);
        if (fLo == null || fHi == null) { sb.AppendLine("ABORT: WeatherField.patchRangeLo/Hi not found"); yield break; }
        Vector3 shipLo = (Vector3)fLo.GetValue(wf), shipHi = (Vector3)fHi.GetValue(wf);

        // Only the sea state matters for the spectrum here; the patch field is
        // an ENVELOPE and changing it must not touch the spectrum at all.
        ctrl.BlendAt(sev, scratch);
        ctrl.ApplyAxes(scratch, p, PinT);
        yield return Push(ocean, scratch);

        var sheet = new Texture2D(TileW * Cols, TileH * 2, TextureFormat.RGB24, false);
        var top = new Color[Cols][]; var bot = new Color[Cols][];
        var topName = new string[Cols]; var botName = new string[Cols];
        var fields = new float[Cols][];

        // ---- row A: the field forced UNIFORM at each end -------------------
        // lo == hi makes the field's own value irrelevant, so the whole frame
        // is one kind of water and the three tiles differ by the multiplier
        // and by nothing else. No rebake is needed and none is wanted: the
        // bias solve only sets the field's MEAN, and a constant field has no
        // mean to get wrong.
        Vector3[] force = { shipLo, Vector3.one, shipHi };
        string[] forceName = { "slick (field low end)", "the authored sea (x1)", "ruffle (field high end)" };
        sb.AppendLine("ROW A -- the patch field forced UNIFORM, from the deck.");
        sb.AppendLine($"  shipped range: swell {shipLo.x:F2}..{shipHi.x:F2}  " +
                      $"mid {shipLo.y:F2}..{shipHi.y:F2}  chop {shipLo.z:F2}..{shipHi.z:F2}");
        for (int i = 0; i < Cols; i++)
        {
            fLo.SetValue(wf, force[i]); fHi.SetValue(wf, force[i]);
            yield return null; yield return null; yield return null;  // LateUpdate pulls, Publish pushes, then a frame to draw
            Capture(cam, rt, tile);
            top[i] = tile.GetPixels(); topName[i] = forceName[i];
            float3 env = RegionField.Instance.Params.EvaluateCascades(
                new float2(p.x, p.y), RegionField.Instance.Islands,
                RegionField.Instance.Shore, RegionField.Instance.Weather);
            sb.AppendLine($"  A{i} {forceName[i],-24} patch x({force[i].x:F2}, {force[i].y:F2}, {force[i].z:F2})" +
                          $"  envelope here swell {env.x:F3} mid {env.y:F3} chop {env.z:F3}");
        }
        sb.AppendLine();

        // ---- row B: the shipped field from straight above, against itself --
        // The first version of this row used a pitched camera 140 m up and it
        // was worthless: the horizon sat mid-frame, most of the picture was
        // sky and fog, and "is there a band" came down to squinting. An
        // ORTHOGRAPHIC camera looking straight down fixes both halves of the
        // problem. There is no perspective, so every pixel maps to a known
        // world position by arithmetic -- which means the water can be scored
        // AGAINST THE FIELD THAT MADE IT rather than against an impression.
        // Pixels are binned by the field's own 0..1 value at their position,
        // and if the ruffle end of the field is not brighter and grainier than
        // the slick end then there is no band in the picture whatever the
        // envelope says.
        fLo.SetValue(wf, shipLo); fHi.SetValue(wf, shipHi);
        const float OrthoSize = 250f;               // 500 m tall, 750 m wide
        var down = new GameObject("WeatherSheetTopCam").AddComponent<Camera>();
        down.CopyFrom(cam);
        down.depth = cam.depth + 10f;
        down.orthographic = true;
        down.orthographicSize = OrthoSize;
        down.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        float aspect = TileW / (float)TileH;

        double[] times = { PinT, PinT + 600.0, PinT + 1200.0 };
        sb.AppendLine("ROW B -- the SHIPPED field from straight above, 750 x 500 m, orthographic.");
        sb.AppendLine("  Patches drift, so the only way to move the field is to move the clock;");
        sb.AppendLine("  these three tiles are therefore different water as well as a different");
        sb.AppendLine("  field, and each is asked on its own whether a band is visible in it.");
        sb.AppendLine("  Scrubbing the clock hands the spectrum back to SeaStateController (its");
        sb.AppendLine("  rebuild throttle and its axis test both wake up), which is fine and");
        sb.AppendLine("  wanted here: the shipped rule at that moment is exactly what should be");
        sb.AppendLine("  on screen. It is only pinned in row A, where the sea must not move.");
        sb.AppendLine("  BOTTOM HALF OF THE PNG IS THE FIELD ITSELF over the same footprint,");
        sb.AppendLine("  bright = ruffle, so the water can be laid against what asked for it.");
        for (int i = 0; i < Cols; i++)
        {
            OceanTime.Scrub(times[i]);
            down.transform.position = new Vector3(pinAt.x, 400f, pinAt.z);
            // Let the controller rebuild at the scrubbed time and the sim land.
            yield return new WaitForSeconds(1.5f);
            Capture(down, rt, tile);
            bot[i] = tile.GetPixels(); botName[i] = $"t+{times[i] - PinT:F0} s";

            // The field over the same footprint, sampled exactly where each
            // pixel looks. Ortho + straight down means this is arithmetic, not
            // a projection: camera up is +Z and camera right is +X.
            var field = new float[TileW * TileH];
            for (int y = 0; y < TileH; y++)
                for (int x = 0; x < TileW; x++)
                {
                    float u = (x + 0.5f) / TileW * 2f - 1f;
                    float v = (y + 0.5f) / TileH * 2f - 1f;
                    field[y * TileW + x] = wf.Patch01(new float2(
                        pinAt.x + u * OrthoSize * aspect, pinAt.z + v * OrthoSize));
                }
            fields[i] = field;

            float lo = 1f, hi = 0f;
            for (int k = 0; k < field.Length; k++) { lo = Mathf.Min(lo, field[k]); hi = Mathf.Max(hi, field[k]); }
            sb.AppendLine($"  B{i} t+{times[i] - PinT,4:F0} s  field {lo:F2}..{hi:F2} across the frame " +
                          $"(span {hi - lo:F2})  -> chop x{Mathf.Lerp(shipLo.z, shipHi.z, lo):F2}" +
                          $"..x{Mathf.Lerp(shipLo.z, shipHi.z, hi):F2}");
        }
        Destroy(down.gameObject);
        OceanTime.Scrub(PinT);
        sb.AppendLine();

        // Three bands in the same columns: the deck tiles, the water from
        // above, and the field that asked for it.
        var big = new Texture2D(TileW * Cols, TileH * 3, TextureFormat.RGB24, false);
        var fieldPx = new Color[TileW * TileH];
        for (int i = 0; i < Cols; i++)
        {
            big.SetPixels(i * TileW, TileH * 2, TileW, TileH, top[i]);
            big.SetPixels(i * TileW, TileH, TileW, TileH, bot[i]);
            for (int k = 0; k < fieldPx.Length; k++)
            {
                float f = fields[i] != null ? fields[i][k] : 0f;
                fieldPx[k] = new Color(f, f, f);
            }
            big.SetPixels(i * TileW, 0, TileW, TileH, fieldPx);
        }
        big.Apply();
        System.IO.File.WriteAllBytes(stem + ".png", big.EncodeToPNG());
        Destroy(big); Destroy(sheet);

        Score(sb, "ROW A (uniform slick / authored / ruffle, from the deck)", topName, top);
        ScoreFull(sb, "ROW B (the shipped field from straight above)", botName, bot);

        // The test a picture cannot give you: is the water actually brighter
        // and grainier WHERE THE FIELD SAYS IT IS ROUGH? Every overhead pixel
        // binned by the field's own value at that exact spot.
        sb.AppendLine("DOES THE BAND LAND WHERE THE FIELD PUT IT?");
        for (int i = 0; i < Cols; i++)
        {
            if (fields[i] == null) continue;
            sb.AppendLine($"  B{i} t+{times[i] - PinT:F0} s:");
            sb.AppendLine("     field band        pixels   mean lum   grain (HF)");
            var lum = Luminance(bot[i]);
            float bLo = 0f, bHi = 0f, gLo = 0f, gHi = 0f;
            for (int b = 0; b < 5; b++)
            {
                float f0 = b / 5f, f1 = (b + 1) / 5f;
                double sl = 0, sg = 0; int n = 0;
                for (int y = 0; y < TileH; y++)
                    for (int x = 0; x < TileW - 1; x++)
                    {
                        int k = y * TileW + x;
                        float f = fields[i][k];
                        if (f < f0 || f >= f1) continue;
                        sl += lum[k]; sg += Mathf.Abs(lum[k + 1] - lum[k]); n++;
                    }
                string tag = b == 0 ? "slick" : b == 4 ? "ruffle" : "";
                if (n == 0) { sb.AppendLine($"     {f0:F1}-{f1:F1} {tag,-7}        0         --           --"); continue; }
                float ml = (float)(sl / n), mg = (float)(sg / n);
                if (bLo == 0f) { bLo = ml; gLo = mg; }
                bHi = ml; gHi = mg;
                sb.AppendLine($"     {f0:F1}-{f1:F1} {tag,-7} {n,8}   {ml,8:F4}   {mg,10:F5}");
            }
            sb.AppendLine($"     band contrast, ruffle bin - slick bin: lum {bHi - bLo,+7:F4}  grain {gHi - gLo,+8:F5}");

            // AND THE COMPETING EXPLANATION, which has to be ruled out before
            // any of the above means anything. The chop fades with DISTANCE
            // FROM THE CAMERA (the same realism pass shipped that too), so an
            // overhead frame 750 m across has a disc of textured water in the
            // middle and smooth water at the edges whatever the patch field is
            // doing. If grain tracks distance harder than it tracks the field,
            // the band is not what is being seen.
            sb.AppendLine("     the same pixels binned by DISTANCE FROM THE CAMERA instead:");
            sb.AppendLine("       distance      pixels   mean lum   grain (HF)   chop weight");
            for (int d = 0; d < 5; d++)
            {
                float d0 = d * 75f, d1 = (d + 1) * 75f;
                double sl = 0, sg = 0; int n = 0;
                for (int y = 0; y < TileH; y++)
                    for (int x = 0; x < TileW - 1; x++)
                    {
                        float u = (x + 0.5f) / TileW * 2f - 1f;
                        float v = (y + 0.5f) / TileH * 2f - 1f;
                        float dist = Mathf.Sqrt(Mathf.Pow(u * OrthoSize * aspect, 2f)
                                              + Mathf.Pow(v * OrthoSize, 2f));
                        if (dist < d0 || dist >= d1) continue;
                        int k = y * TileW + x;
                        sl += lum[k]; sg += Mathf.Abs(lum[k + 1] - lum[k]); n++;
                    }
                float w = SeaSick.Ocean.OceanClipmap.WeightsAt(0.5f * (d0 + d1)).z;
                if (n == 0) { sb.AppendLine($"       {d0,4:F0}-{d1,4:F0} m         0         --           --      {w,8:F3}"); continue; }
                sb.AppendLine($"       {d0,4:F0}-{d1,4:F0} m  {n,8}   {sl / n,8:F4}   {sg / n,10:F5}      {w,8:F3}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("THE CHOP'S OWN DRAW DISTANCE, from the shipped rule (OceanClipmap.WeightsAt):");
        sb.AppendLine("  distance    swell     mid    chop");
        foreach (float d in new[] { 0f, 64f, 128f, 192f, 256f, 384f, 512f, 1024f })
        {
            Vector4 w = SeaSick.Ocean.OceanClipmap.WeightsAt(d);
            sb.AppendLine($"  {d,6:F0} m   {w.x,6:F3}  {w.y,6:F3}  {w.z,6:F3}");
        }
        sb.AppendLine("  A patch feature is 128, 256 or 512 m across (tileMetres / 8, /16, /32).");
        sb.AppendLine("  Compare the two: whether a ruffle can be seen AS A BAND depends on");
        sb.AppendLine("  whether the chop still exists at the distance the band's far edge is at.");
        sb.AppendLine();
        sb.AppendLine($"tiles: {stem}.png, {Cols} wide x 3 high -- TOP the deck tiles, MIDDLE the");
        sb.AppendLine("       water from above, BOTTOM the patch field over the same footprint");
    }

    // ---- the numbers ------------------------------------------------------

    /// Mean luminance and horizontal grain over the WATER only, plus the
    /// pairwise difference between every pair of tiles in the row. "Does it
    /// read as darker and rougher" is two numbers; "are these the same
    /// picture" is the third.
    static void Score(StringBuilder sb, string title, string[] names, Color[][] tiles)
    {
        sb.AppendLine(title + ":");
        sb.AppendLine("  tile                          mean lum   grain (HF)   grain angle  coherence");
        var lum = new float[tiles.Length][];
        for (int i = 0; i < tiles.Length; i++)
        {
            lum[i] = Luminance(tiles[i]);
            Vector2 o = Orient(lum[i]);
            sb.AppendLine($"  {names[i],-28}  {Mean(lum[i]),8:F4}   {Grain(lum[i]),8:F5}   {o.x,8:F1} deg  {o.y,7:F3}");
        }
        sb.AppendLine("  |difference| between tiles (0 = the same picture):");
        sb.Append("      ");
        for (int j = 0; j < tiles.Length; j++) sb.Append($"{j,9}");
        sb.AppendLine();
        for (int i = 0; i < tiles.Length; i++)
        {
            sb.Append($"    {i} ");
            for (int j = 0; j < tiles.Length; j++)
                sb.Append(i == j ? "        -" : $"{Diff(lum[i], lum[j]),9:F4}");
            sb.AppendLine();
        }
        sb.AppendLine();
    }

    /// The same scores over the WHOLE tile. The overhead view is water edge to
    /// edge -- there is no sky to keep out and the ship is a few pixels in the
    /// middle -- so masking it the way the deck tiles are masked would throw
    /// away most of the picture.
    static void ScoreFull(StringBuilder sb, string title, string[] names, Color[][] tiles)
    {
        sb.AppendLine(title + ":");
        sb.AppendLine("  tile                          mean lum   grain (HF)   grain angle  coherence");
        var lum = new float[tiles.Length][];
        for (int i = 0; i < tiles.Length; i++)
        {
            lum[i] = Luminance(tiles[i]);
            Vector2 o = OrientAll(lum[i]);
            sb.AppendLine($"  {names[i],-28}  {MeanAll(lum[i]),8:F4}   {GrainAll(lum[i]),8:F5}   {o.x,8:F1} deg  {o.y,7:F3}");
        }
        sb.AppendLine();
    }

    static float MeanAll(float[] l)
    {
        double s = 0; for (int i = 0; i < l.Length; i++) s += l[i];
        return (float)(s / l.Length);
    }

    static float GrainAll(float[] l)
    {
        double s = 0; int n = 0;
        for (int y = 0; y < TileH; y++)
            for (int x = 0; x < TileW - 1; x++) { s += Mathf.Abs(l[y * TileW + x + 1] - l[y * TileW + x]); n++; }
        return (float)(s / n);
    }

    static Vector2 OrientAll(float[] l)
    {
        double jxx = 0, jyy = 0, jxy = 0;
        for (int y = 1; y < TileH - 1; y++)
            for (int x = 1; x < TileW - 1; x++)
            {
                int i = y * TileW + x;
                float gx = 0.5f * (l[i + 1] - l[i - 1]);
                float gy = 0.5f * (l[i + TileW] - l[i - TileW]);
                jxx += gx * gx; jyy += gy * gy; jxy += gx * gy;
            }
        return Tensor(jxx, jyy, jxy);
    }

    static float[] Luminance(Color[] px)
    {
        var l = new float[px.Length];
        for (int i = 0; i < px.Length; i++)
            l[i] = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;
        return l;
    }

    /// Iterate the water band, skipping the column the ship stands in.
    static void ForWater(System.Action<int, int> f)
    {
        int yTop = Mathf.RoundToInt(TileH * WaterTop);
        int xLo = Mathf.RoundToInt(TileW * ShipLo), xHi = Mathf.RoundToInt(TileW * ShipHi);
        for (int y = 0; y < yTop; y++)
            for (int x = 0; x < TileW - 1; x++)
                if (x < xLo || x > xHi) f(x, y);
    }

    static float Mean(float[] l)
    {
        double s = 0; int n = 0;
        ForWater((x, y) => { s += l[y * TileW + x]; n++; });
        return n == 0 ? 0f : (float)(s / n);
    }

    /// Mean absolute neighbour difference: how much fine detail the water
    /// carries. A ruffle should raise this; a slick should lower it.
    static float Grain(float[] l)
    {
        double s = 0; int n = 0;
        ForWater((x, y) => { s += Mathf.Abs(l[y * TileW + x + 1] - l[y * TileW + x]); n++; });
        return n == 0 ? 0f : (float)(s / n);
    }

    /// The dominant orientation of the fine structure on the water, in screen
    /// degrees, from the structure tensor of the luminance gradient. This is
    /// the number that says whether the grain ROTATED as opposed to merely
    /// being redrawn: two pictures of unrelated water score the same mean and
    /// the same HF energy, but only a genuine rotation moves this. Screen
    /// space, so perspective foreshortens it -- read the CHANGE between tiles,
    /// never the absolute value.
    static Vector2 Orient(float[] l)
    {
        double jxx = 0, jyy = 0, jxy = 0;
        ForWater((x, y) =>
        {
            if (x < 1 || x > TileW - 2 || y < 1 || y > TileH - 2) return;
            int i = y * TileW + x;
            // CENTRAL differences, and this is not a refinement. Forward
            // differences (l[i+1] - l[i] and l[i+W] - l[i]) share the -l[i]
            // term, so for any noisy image E[gx*gy] picks up the variance of a
            // single pixel and comes out POSITIVE whatever the picture is. The
            // first version of this measure did exactly that and reported a
            // rock-steady 43-48 degrees for every sea it was shown, including
            // ones rotated 90 degrees against each other. Central differences
            // share no term and have no such bias.
            float gx = 0.5f * (l[i + 1] - l[i - 1]);
            float gy = 0.5f * (l[i + TileW] - l[i - TileW]);
            jxx += gx * gx; jyy += gy * gy; jxy += gx * gy;
        });
        return Tensor(jxx, jyy, jxy);
    }

    /// Orientation of the dominant GRADIENT in degrees (x) and how directional
    /// the field is at all, 0..1 (y). Doubled-angle form, so it does not care
    /// which end of an axis a gradient points along. THE COHERENCE IS NOT
    /// DECORATION: on an isotropic field the angle is the arctangent of two
    /// numbers that are both noise, and it will happily report a confident
    /// value that means nothing. Below about 0.05 the angle should be ignored.
    static Vector2 Tensor(double jxx, double jyy, double jxy)
    {
        double trace = jxx + jyy;
        double aniso = System.Math.Sqrt((jxx - jyy) * (jxx - jyy) + 4.0 * jxy * jxy);
        float ang = 0.5f * Mathf.Atan2((float)(2.0 * jxy), (float)(jxx - jyy)) * Mathf.Rad2Deg;
        return new Vector2(ang, trace > 1e-12 ? (float)(aniso / trace) : 0f);
    }

    static float Diff(float[] a, float[] b)
    {
        double s = 0; int n = 0;
        ForWater((x, y) => { s += Mathf.Abs(a[y * TileW + x] - b[y * TileW + x]); n++; });
        return n == 0 ? 0f : (float)(s / n);
    }

    // ---- plumbing ---------------------------------------------------------

    /// Hand the renderer a spectrum and give it time to rebuild h0, re-run the
    /// FFT and land in the textures. OceanRenderer runs at -100 and this
    /// coroutine runs after everything, so a change made now is picked up on
    /// the NEXT frame's StepSimulation, not this one.
    static IEnumerator Push(OceanRenderer ocean, OceanSpectrumSettings s)
    {
        ocean.SetSettings(s);
        ocean.MarkSpectrumDirty();
        for (int i = 0; i < 4; i++) yield return null;
    }

    /// Did SeaStateController stomp on us? Reported per tile rather than
    /// assumed, because a sheet of the wrong sea looks exactly like a sheet of
    /// the right one.
    static string Held(OceanRenderer ocean, OceanSpectrumSettings s) =>
        ReferenceEquals(ocean.Settings, s) ? "" : "*** STOMPED: the renderer is not holding our settings ***";

    static float Norm(float deg) { float d = deg % 360f; return d < 0f ? d + 360f : d; }

    static void Capture(Camera cam, RenderTexture rt, Texture2D tile)
    {
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        tile.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tile.Apply();
        cam.targetTexture = prevTarget;
        RenderTexture.active = prevActive;
    }

    void Finish(StringBuilder sb, string path, string err)
    {
        running = false;
        pin = false;
        if (err != null) sb.AppendLine(err);
        System.IO.File.WriteAllText(path, sb.ToString());
        Debug.Log("WeatherSheet:\n" + sb);
        Destroy(gameObject);
    }

    /// Runs at order 10000, i.e. after ChaseCamera's own LateUpdate, so the
    /// last word on where the camera is belongs to this sheet.
    void LateUpdate()
    {
        if (!pin) return;
        if (motor != null)
        {
            motor.transform.position = pinAt;
            motor.transform.rotation = pinRot;
        }
        if (pinCam != null) { pinCam.position = camPos; pinCam.rotation = camRot; }
    }
}
