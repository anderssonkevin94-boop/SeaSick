using UnityEngine;
using UnityEngine.InputSystem;
using SeaSick.Ocean;

/// Drive the sea's white water and the sharpness of its crests by hand, with
/// the numbers on screen.
///
/// Same reason `WaterClarityTuner` and `DockCamTuner` exist, and the same
/// lesson behind them: how much foam a breaking crest should throw, and how
/// pointed the tops of the waves should be, are not things to derive. Three
/// rounds of me picking a Jacobian threshold off arithmetic would produce
/// three defensible seas and probably none of them the one Kevin asked for.
///
/// There are FOUR separable things here and it matters that they can be judged
/// one at a time, because they fail in different directions:
///
///   FOLD      how readily the shader calls a patch of water "breaking".
///             `_FoamJThreshold` is where on the Jacobian the fold starts and
///             `_FoamSnap` is how wide the onset is. A wide onset is haze
///             lying on the water; a narrow one is an edge.
///   TRAIL     the persistent foam buffer — `foamThreshold` decides how much
///             fold it takes to LEAVE a mark, `foamHalflife` how long the mark
///             lasts, `foamInjection` how white it starts. This is the
///             difference between a sea that flashes white and a sea that
///             carries its own wake around.
///   SPUME     the particles thrown off a crest that folds. Their gate is the
///             ocean's own foam field, so if `peak foam` never reaches the
///             gate the sea is not folding as far as the SIMULATION is
///             concerned and the fix is TRAIL, not this.
///   SHARPNESS `crestSharpen.z` — choppiness on cascade 2, the 32 m near-field
///             chop. **This one is free.** The readback carries cascades 0 and
///             1 only (`DisplacementReadback.PhysicsCascades`), so cascade 2 is
///             invisible to the CPU sampler and sharpening it costs no Newton
///             iteration. x and y are NOT free — the budget is spent — and the
///             tuner deliberately does not offer them.
///
/// Foam values are written to the LIVE BLEND ONLY, every frame, and never to
/// the four sea-state assets.
///
/// The first version wrote them to the assets too, so that a re-lerp could not
/// revert a slider — and that quietly stamped one value over all four sea
/// states within a frame of the tuner waking up, flattening the authored
/// difference between a pond and a storm (calm injects 0.15, the storm 0.45,
/// and both became whatever the blend happened to hold). It was caught because
/// `CrestProbe` printed the live foamThreshold as 0.50 at BOTH Hs 14 and Hs
/// 55, which the anchors cannot produce. Re-applying to the blend every frame
/// solves the revert without touching what anybody authored: a rebuild
/// overwrites the blend and the next frame overwrites it back.
///
/// **TRAP, learnt the expensive way on the clarity pass: runtime material
/// edits do NOT survive leaving play mode.** Press O before you stop.
///
/// Controls (tick `active` in the Inspector first):
///   sliders     drag them
///   7           mute the fresh crest foam (the fold)
///   8           mute the trail (the persistent buffer)
///   9           mute the spume particles
///   O           print the block (console and /tmp/seasick-foam.txt)
///   Home        back to the values this session started with
///
/// The keys and the panel side are deliberately clear of `WaterClarityTuner`
/// (1/2/P/backspace, top left): both of these are usually on at once and two
/// dev overlays fighting over the same key is its own afternoon.
public class SeaFoamTuner : MonoBehaviour, SeaSick.UI.IDevTool
{
    // --- IDevTool: this tuner is opened from the settings drawer, and the
    // drawer is what decides where it draws. See SeaSick.UI.DevTools.
    public string ToolName => "Foam & crests";
    public string ToolBlurb => "how readily a crest breaks, how long the trail lasts, how sharp the tops are";
    public bool ToolActive { get => active; set => active = value; }
    void OnEnable() => SeaSick.UI.DevTools.Register(this);
    void OnDisable() => SeaSick.UI.DevTools.Unregister(this);

    [Tooltip("Off = the shipped sea, untouched. Tick to tune it by hand.")]
    [SerializeField] bool active = false;

    Material mat;
    StormSpray spray;
    bool seeded;

    // What the session started with, for backspace.
    float jWas, snapWas, gainWas, reliefWas, sparkleWas;
    float liftWas, floorWas, trailGainWas;
    float thrWas, halfWas, injWas, sharpWas, gateWas, rateWas, injSharpWas;
    bool foldMuted, trailMuted, spumeMuted;
    float breakersWere, breakClock, breakersPerSec;

    void Seed()
    {
        if (seeded) return;
        mat = FindOceanMaterial();
        spray = FindAnyObjectByType<StormSpray>();
        if (mat == null) return;
        jWas = mat.GetFloat("_FoamJThreshold");
        liftWas = mat.GetFloat("_FoamBandLift");
        floorWas = mat.GetFloat("_FoamTrailFloor");
        trailGainWas = mat.GetFloat("_FoamTrailGain");
        snapWas = mat.GetFloat("_FoamSnap");
        gainWas = mat.GetFloat("_FoamCrestGain");
        reliefWas = mat.GetFloat("_FoamRelief");
        sparkleWas = mat.GetFloat("_FoamSparkle");
        var live = Live();
        if (live != null)
        {
            thrWas = live.foamThreshold;
            halfWas = live.foamHalflife;
            injWas = live.foamInjection;
            sharpWas = live.crestSharpen.z;
            injSharpWas = live.foamInjectSharpness;
        }
        if (spray != null) { gateWas = spray.FoamGateFraction; rateWas = spray.BreakerRate; }
        thr = thrWas; half = halfWas; inj = injWas; sharp = sharpWas;
        injSharp = injSharpWas;
        seeded = true;
    }

    static OceanSpectrumSettings Live() =>
        OceanRenderer.Instance != null ? OceanRenderer.Instance.Settings : null;

    void Update()
    {
        if (!active) return;
        Seed();
        if (mat == null) return;

        // Breaking crests per second, measured rather than assumed — the
        // number that says whether the gate is reachable at all in this sea.
        if (spray != null)
        {
            breakClock += Time.deltaTime;
            if (breakClock >= 0.5f)
            {
                breakersPerSec = (spray.Breakers - breakersWere) / breakClock;
                breakersWere = spray.Breakers;
                breakClock = 0f;
            }
        }

        var k = Keyboard.current;
        if (k == null) return;
        if (k.digit7Key.wasPressedThisFrame) foldMuted = !foldMuted;
        if (k.digit8Key.wasPressedThisFrame) { trailMuted = !trailMuted; touched = true; }
        if (k.digit9Key.wasPressedThisFrame) spumeMuted = !spumeMuted;
        if (k.oKey.wasPressedThisFrame) Print();
        if (k.homeKey.wasPressedThisFrame) Restore();
    }

    // The values the sliders hold. Seeded from the blend once, then owned by
    // the tuner — reading them back off the blend each frame would let a
    // rebuild drag the sliders around under the cursor.
    float thr, half, inj, sharp, injSharp;

    /// Re-applied every frame to the LIVE BLEND, and to nothing else. See the
    /// note at the top of the file for why the anchors are off limits.
    // Nothing is pushed until a slider has actually MOVED.
    //
    // Without this an always-on tuner is not neutral: it seeds from whatever
    // sea state happened to be blended when it woke up and then re-asserts
    // those four numbers every frame for the rest of the session, so the foam
    // silently stops following the weather. A dev overlay that is on by
    // default has to be inert by default.
    bool touched;

    void PushSpectrum()
    {
        if (!touched) return;
        var live = Live();
        if (live == null) return;
        live.foamThreshold = thr;
        live.foamHalflife = half;
        live.foamInjection = trailMuted ? 0f : inj;
        live.foamInjectSharpness = injSharp;
        var cs = live.crestSharpen; cs.z = sharp; live.crestSharpen = cs;
    }

    // Both, because script order is not guaranteed: whichever of these runs
    // after the controller's rebuild is the one that wins, and a dev tool
    // should not depend on knowing which.
    void LateUpdate() { if (active && seeded) PushSpectrum(); }

    /// Drawn inside the settings drawer's rect. It used to take a fixed
    /// 440x560 box out of `Screen.width − 452, 12` — which at the shipping
    /// portrait aspect is exactly where the minimap, the wind row and the ship
    /// panel are.
    public void DrawTool(Rect body)
    {
        Seed();
        if (mat == null) return;
        var rich = new GUIStyle(GUI.skin.label) { richText = true };

        GUILayout.BeginArea(body);
        GUILayout.Label("<b>SEA FOAM &amp; CREST</b>   7 fold  8 trail  9 spume  O print  Home reset", rich);

        GUILayout.Space(6);
        GUILayout.Label($"FOLD — when the shader calls it breaking{(foldMuted ? "   [MUTED]" : "")}");
        mat.SetFloat("_FoamJThreshold", Row("J threshold", mat.GetFloat("_FoamJThreshold"), 0.2f, 1f));
        mat.SetFloat("_FoamSnap", Row("snap (J width)", mat.GetFloat("_FoamSnap"), 0.02f, 0.6f));
        float gain = Row("fresh crest gain", foldMuted ? gainWas : mat.GetFloat("_FoamCrestGain"), 0f, 3f);
        if (foldMuted) gainWas = gain;
        mat.SetFloat("_FoamCrestGain", foldMuted ? 0f : gain);
        mat.SetFloat("_FoamRelief", Row("relief (bump)", mat.GetFloat("_FoamRelief"), 0f, 3f));
        mat.SetFloat("_FoamSparkle", Row("wet sparkle", mat.GetFloat("_FoamSparkle"), 0f, 2f));
        mat.SetFloat("_FoamNoiseScale", Row("tear scale", mat.GetFloat("_FoamNoiseScale"), 0.02f, 0.6f));
        // What the foam is allowed to SEE. Measured at 0.09% coverage before
        // this existed: the fold lives in cascade 2 and the shader was
        // weighting it by the clipmap, which is near zero past a few tens of
        // metres. Turn this to 0 to watch the fold foam disappear entirely.
        mat.SetFloat("_FoamBandLift", Row("short-band lift", mat.GetFloat("_FoamBandLift"), 0f, 1f));
        mat.SetFloat("_FoamLiftFar", Row("lift fades by (m)", mat.GetFloat("_FoamLiftFar"), 60f, 1200f));

        GUILayout.Space(8);
        GUILayout.Label($"TRAIL — the mark a fold leaves behind{(trailMuted ? "   [MUTED]" : "")}");
        thr = Row("inject below J", thr, 0.1f, 0.95f);
        half = Row("half-life (s)", half, 0.5f, 20f);
        inj = Row("injection", inj, 0f, 1.5f);
        // How SELECTIVE the marking is. Low fills the buffer everywhere and
        // reads as milk; high marks only water that really folded.
        injSharp = Row("inject selectivity", injSharp, 1f, 16f);
        mat.SetFloat("_FoamTrailFloor", Row("trail floor", mat.GetFloat("_FoamTrailFloor"), 0f, 0.5f));
        mat.SetFloat("_FoamTrailGain", Row("trail contrast", mat.GetFloat("_FoamTrailGain"), 0.2f, 4f));
        PushSpectrum();

        GUILayout.Space(8);
        GUILayout.Label($"SPUME — water thrown off a folding crest{(spumeMuted ? "   [MUTED]" : "")}");
        if (spray != null)
        {
            spray.FoamGateFraction =
                Row("gate (x peak foam)", spray.FoamGateFraction, 0.1f, 0.95f);
            float r = Row("crests/s", spumeMuted ? rateWas : spray.BreakerRate, 0f, 30f);
            if (spumeMuted) rateWas = r;
            spray.BreakerRate = spumeMuted ? 0f : r;
            spray.SpumePerCrest = Mathf.RoundToInt(Row("per crest", spray.SpumePerCrest, 1f, 40f));
            // The honest readout: the gate is into the ocean's own foam field,
            // so if the peak never reaches it, no slider on THIS row will
            // produce a particle — the fix is one section up.
            string verdict = spray.PeakFoam >= spray.FoamGate
                ? "<color=#8f8>reachable</color>"
                : "<color=#f88>gate never reached — raise TRAIL's J, not this</color>";
            GUILayout.Label($"peak foam <b>{spray.PeakFoam:F2}</b> vs gate {spray.FoamGate:F2}  {verdict}", rich);
            GUILayout.Label($"crests {breakersPerSec:F1}/s   spume alive {spray.SpumeAlive}"
                          + $"   spindrift {spray.SpindriftAlive}");
        }
        else GUILayout.Label("<color=#f88>no StormSpray in the scene</color>", rich);

        GUILayout.Space(8);
        GUILayout.Label("SHARPNESS — cascade 2 only (the 32 m near chop)");
        sharp = Row("crest sharpen z", sharp, 0.5f, 2.5f);
        PushSpectrum();
        GUILayout.Label("<i>free: cascade 2 is not in the physics readback. "
                      + "0 and 1 cost a Newton step and are not offered.</i>", rich);

        var ssc = SeaStateController.Instance;
        if (ssc != null)
            GUILayout.Label($"sea: <b>{ssc.CurrentHs:F1} m</b> Hs   {ssc.CurrentStateName}", rich);
        GUILayout.EndArea();
    }

    float Row(string label, float v, float lo, float hi)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label} {v:F2}", GUILayout.Width(180));
        float after = GUILayout.HorizontalSlider(v, lo, hi);
        GUILayout.EndHorizontal();
        if (!Mathf.Approximately(after, v)) touched = true;
        return after;
    }

    void Restore()
    {
        if (mat == null) return;
        mat.SetFloat("_FoamJThreshold", jWas);
        mat.SetFloat("_FoamSnap", snapWas);
        mat.SetFloat("_FoamCrestGain", gainWas);
        mat.SetFloat("_FoamRelief", reliefWas);
        mat.SetFloat("_FoamSparkle", sparkleWas);
        mat.SetFloat("_FoamBandLift", liftWas);
        mat.SetFloat("_FoamTrailFloor", floorWas);
        mat.SetFloat("_FoamTrailGain", trailGainWas);
        thr = thrWas; half = halfWas; inj = injWas; sharp = sharpWas;
        injSharp = injSharpWas;
        foldMuted = trailMuted = spumeMuted = false;
        // Push the restore even if nothing else has been touched, then go
        // quiet again so the weather owns these numbers once more.
        touched = true;
        PushSpectrum();
        touched = false;
        if (spray != null) { spray.FoamGateFraction = gateWas; spray.BreakerRate = rateWas; }
    }

    /// Printed in the shader's own Properties syntax for the material half and
    /// as plain field values for the asset half, so anything worth keeping can
    /// be pasted into the defaults rather than living only in a material and a
    /// ScriptableObject that somebody might revert.
    void Print()
    {
        var ssc = SeaStateController.Instance;
        string t =
            $"_FoamJThreshold (\"Foam Jacobian Threshold\", Range(0, 1)) = {mat.GetFloat("_FoamJThreshold"):F2}\n"
          + $"_FoamSnap (\"Foam — snap (J width of the onset)\", Range(0.02, 0.6)) = {mat.GetFloat("_FoamSnap"):F3}\n"
          + $"_FoamCrestGain (\"Foam — fresh crest gain\", Range(0, 3)) = {mat.GetFloat("_FoamCrestGain"):F2}\n"
          + $"_FoamRelief (\"Foam — relief (bump on the whitecap)\", Range(0, 3)) = {mat.GetFloat("_FoamRelief"):F2}\n"
          + $"_FoamSparkle (\"Foam — wet sparkle\", Range(0, 2)) = {mat.GetFloat("_FoamSparkle"):F2}\n"
          + $"_FoamNoiseScale (\"Foam Noise Scale\", Float) = {mat.GetFloat("_FoamNoiseScale"):F3}\n"
          + $"_FoamBandLift (\"Foam — short-band lift\", Range(0, 1)) = {mat.GetFloat("_FoamBandLift"):F2}\n"
          + $"_FoamLiftFar (\"Foam — lift fades out by (m)\", Float) = {mat.GetFloat("_FoamLiftFar"):F0}\n"
          + $"_FoamTrailFloor (\"Foam — trail floor\", Range(0, 0.5)) = {mat.GetFloat("_FoamTrailFloor"):F3}\n"
          + $"_FoamTrailGain (\"Foam — trail contrast\", Range(0.2, 4)) = {mat.GetFloat("_FoamTrailGain"):F2}\n"
          + "-- the BLENDED sea state these were judged against. They are one\n"
          + "   point on a four-anchor blend, so decide which anchors to move:\n"
          + $"   (judged at Hs {(ssc != null ? ssc.CurrentHs : 0f):F1}, {(ssc != null ? ssc.CurrentStateName : "?")})\n"
          + $"foamThreshold: {thr:F3}\n"
          + $"foamHalflife: {half:F2}\n"
          + $"foamInjection: {inj:F3}\n"
          + $"foamInjectSharpness: {injSharp:F2}\n"
          + $"crestSharpen.z: {sharp:F3}\n"
          + "-- StormSpray --\n"
          + $"foamGateFraction: {(spray != null ? spray.FoamGateFraction : 0f):F3}\n"
          + $"breakerRate: {(spray != null ? spray.BreakerRate : 0f):F2}\n"
          + $"spumePerCrest: {(spray != null ? spray.SpumePerCrest : 0)}\n";
        System.IO.File.WriteAllText("/tmp/seasick-foam.txt", t);
        Debug.Log("SeaFoamTuner — paste these into the shader defaults and the assets:\n" + t);
    }

    static Material FindOceanMaterial()
    {
        foreach (var mr in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (mr.sharedMaterial != null && mr.sharedMaterial.shader != null
                && mr.sharedMaterial.shader.name == "SeaSick/Ocean")
                return mr.sharedMaterial;
        return null;
    }
}
