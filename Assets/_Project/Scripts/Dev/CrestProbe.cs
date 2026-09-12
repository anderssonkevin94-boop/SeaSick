using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.World;

/// **Does the open sea break, and are its tops sharp?**
///
/// Two questions the existing probes cannot answer, for the same structural
/// reason in both cases: `WaveSizeProbe`, `DivergenceProbe` and every other
/// measurement of "the sea" reads the CPU sampler, and the CPU sampler carries
/// **cascades 0 and 1 only** (`DisplacementReadback.PhysicsCascades`). The
/// near-field chop the eye reads as sharpness lives in cascade 2, and the foam
/// is computed entirely on the GPU. Anything measured through the sampler is
/// therefore blind to both by construction — and would report "no change"
/// after a change that is plainly visible on screen.
///
/// So this measures the two SHIPPED ARTEFACTS instead:
///
///   SHARPNESS  the cascade-2 displacement and derivative textures, read back
///              from the GPU. Steepness, and the fraction of the band that is
///              actually FOLDING (J = (1+Dxx)(1+Dzz) - Dxz^2 below 1).
///   FOAM       the shader's own `foamAmt`, off the framebuffer with
///              `_SS_FoamOnly` on and the camera culled to the ocean layer, so
///              every counted pixel is water and nothing else.
///
/// The before/after for the foam comes off ONE FRAME via `_SS_FoamOldJ`, not
/// from two runs: the sea moves, and two runs of identical code have been
/// measured 50.8% and 24.5% apart on the same patch of water. Same reason the
/// wave phase is pinned with `OceanTime.Scrub` before anything is counted.
///
/// Play mode, Sea.unity. Writes /tmp/seasick-crest.txt and the PNGs.
public class CrestProbe : MonoBehaviour
{
    const int ShotW = 768, ShotH = 512;
    const double PinT = 5100.0;
    /// Rough and storm. Calm is not interesting here — nothing breaks in it,
    /// and it should not.
    static readonly float[] StateHs = { 14f, 55f };
    /// The sharpen sweep. 1.0 is the sea before this pass; 1.35 is what
    /// `SetupSeaFoam` ships; 1.8 is there to show which way the curve runs.
    static readonly float[] Sharpen = { 1.0f, 1.35f, 1.8f };

    static bool running;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("CrestProbe: not in play mode"); return; }
        if (running) { Debug.LogError("CrestProbe: already running"); return; }
        running = true;
        new GameObject("CrestProbe").AddComponent<CrestProbe>();
    }

    IEnumerator Start()
    {
        // The `_SS_*` dev uniforms this probe drives are no longer in the
        // shipped ocean shader: they live behind the `_SEASICK_DEBUG` variant,
        // so the sea does not pay for six constant loads and an eleven-way
        // ternary chain per pixel on the chance a probe reads them one
        // afternoon. Setting the globals without the keyword FAILS SILENTLY --
        // the variant that reads them is simply not the one running, and the
        // probe would photograph an ordinary sea and report it as foam. It
        // goes on here and comes off in Finish, which every exit path below
        // goes through, including all four aborts.
        Shader.EnableKeyword("_SEASICK_DEBUG");
        var sb = new StringBuilder();
        System.IO.File.WriteAllText("/tmp/seasick-crest.txt", "CrestProbe: did not finish\n");

        // The foam tuner re-applies its own sliders to the live blend every
        // frame, which would quietly undo every step of the sweeps below. A
        // probe and a tuner cannot both own the same value.
        var tuner = FindAnyObjectByType<SeaFoamTuner>();
        bool tunerWas = tuner != null && tuner.enabled;
        if (tuner != null) tuner.enabled = false;

        var motor = FindAnyObjectByType<ShipMotor>();
        var helm = FindAnyObjectByType<HelmInput>();
        var sea = SeaStateController.Instance;
        var ocean = OceanRenderer.Instance;
        var spray = FindAnyObjectByType<StormSpray>();
        var cam = Camera.main;
        if (helm != null) helm.enabled = false;
        if (motor != null) { motor.ThrottleOrder = 0f; motor.Rudder = 0f; }
        if (motor == null || sea == null || ocean == null || cam == null)
        { Finish(sb, "ABORT: no ship / controller / ocean / camera"); yield break; }
        var rb = motor.GetComponent<Rigidbody>();

        yield return new WaitForSeconds(3f);

        // DEEP WATER, asserted rather than assumed. The depth limit caps the
        // envelope at breakFraction * depth / Hs, so a probe that measures the
        // storm over a shelf measures a sea that was quietly held down — and
        // 1500 m due west, the obvious spawn, is 40 m ABOVE sea level.
        Vector3 park = motor.transform.position;
        float depth = DepthAt(park);
        if (depth < 150f)
        {
            bool found = false;
            for (int a = 0; a < 24 && !found; a++)
                for (int r = 2000; r <= 6000 && !found; r += 1000)
                {
                    float ang = a / 24f * Mathf.PI * 2f;
                    var q = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                    if (DepthAt(q) > 150f) { park = q; depth = DepthAt(q); found = true; }
                }
            if (!found) { Finish(sb, "ABORT: found no water deeper than 150 m to measure in"); yield break; }
            if (rb != null)
            {
                rb.position = new Vector3(park.x, 2f, park.z);
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            }
            yield return new WaitForSeconds(5f);
        }

        sb.AppendLine("CrestProbe — does the open sea break, and are its tops sharp?");
        sb.AppendLine($"measured at ({park.x:F0}, {park.z:F0}) in {depth:F0} m of water");
        sb.AppendLine();

        // The ocean's own layer, so a foam count can never include sky, spray
        // or a distant island — the mistake SurfProbe made once and reported
        // beach sand as whitewater.
        int oceanLayer = -1;
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            var m = r.sharedMaterial;
            if (m != null && m.shader != null && m.shader.name == "SeaSick/Ocean")
            { oceanLayer = r.gameObject.layer; break; }
        }
        if (oceanLayer < 0) { Finish(sb, "ABORT: no renderer using SeaSick/Ocean"); yield break; }

        PinNoon();

        // A deck-height eye, because that is where the complaint comes from.
        var eye = new GameObject("CrestProbeEye").AddComponent<Camera>();
        eye.CopyFrom(cam);
        eye.depth = cam.depth + 12f;
        eye.transform.position = new Vector3(park.x, 12f, park.z);
        eye.transform.rotation = Quaternion.Euler(8f, 40f, 0f);
        var rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32);
        var shot = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);
        int fullMask = eye.cullingMask;
        var fullClear = eye.clearFlags;

        SnapshotAnchors();
        float shippedSharpen = ocean.Settings != null ? ocean.Settings.crestSharpen.z : 1f;
        float lastNominal = -1f;

        for (int pass = 0; pass < StateHs.Length; pass++)
        {
            float hs = StateHs[pass];
            // Each pass pins LATER, never at the same instant: the controller
            // throttles its rebuild on OceanTime.Now - lastRebuildTime, so a
            // backward scrub makes that difference negative and it returns
            // early for the rest of the run. The second sea state then never
            // arrives and the probe measures the first one twice.
            double pinT = PinT + pass * 700.0;
            OceanTime.Scrub(pinT);
            OceanTime.Paused = false;
            sea.ForceHs(hs);
            yield return new WaitForSeconds(3f);

            float nominal = ocean.Settings != null ? ocean.Settings.nominalHs : 0f;
            bool arrived = Mathf.Abs(nominal - lastNominal) > 0.01f;
            lastNominal = nominal;
            sb.AppendLine($"=== sea forced to Hs {hs:F0} m (declared nominalHs {nominal:F1}) ===");
            if (!arrived)
                sb.AppendLine("*** THE SPECTRUM DID NOT REBUILD — everything below is the PREVIOUS sea ***");

            // ---- SHARPNESS: the cascade-2 texture itself ------------------
            sb.AppendLine("  cascade 2 (the 32 m near-field chop), read off the GPU:");
            sb.AppendLine("    sharpen | RMS slope | median slope | folding texels | max |Dxz|");
            foreach (float k in Sharpen)
            {
                SetSharpen(ocean, k);
                // Lambda is applied in ResolveOutputs every step, so the
                // texture is current the frame after — no spectrum rebuild is
                // involved and none is waited for.
                OceanTime.Scrub(pinT);
                yield return null; yield return null; yield return null;
                var st = ReadCascade2();
                sb.AppendLine($"    {k,7:F2} | {st.rmsSlope,9:F3} | {st.medSlope,12:F3} | "
                            + $"{st.foldPct,13:F2}% | {st.maxCross,8:F3}");
            }
            SetSharpen(ocean, shippedSharpen);
            OceanTime.Scrub(pinT);
            OceanTime.Paused = true;
            yield return new WaitForSeconds(0.5f);

            // ---- FOAM: the shader's own number, one frame, both ways -------
            eye.cullingMask = 1 << oceanLayer;
            eye.clearFlags = CameraClearFlags.SolidColor;
            eye.backgroundColor = new Color(0f, 0f, 1f);   // never grey, so never counted
            Shader.SetGlobalFloat("_SS_FoamOnly", 1f);

            Shader.SetGlobalFloat("_SS_FoamOldJ", 1f);
            yield return null; yield return null;
            Capture(eye, rt, shot);
            var before = FoamStats(shot);
            System.IO.File.WriteAllBytes($"/tmp/seasick-crest-{hs:F0}-foam-before.png", shot.EncodeToPNG());

            Shader.SetGlobalFloat("_SS_FoamOldJ", 0f);
            yield return null; yield return null;
            Capture(eye, rt, shot);
            var after = FoamStats(shot);
            System.IO.File.WriteAllBytes($"/tmp/seasick-crest-{hs:F0}-foam-after.png", shot.EncodeToPNG());

            sb.AppendLine($"  foam on the water, {before.water} water pixels of {ShotW * ShotH}:");
            sb.AppendLine($"    before (no cross term, 0.25 onset): mean {before.mean:F4}  "
                        + $">0.1 {before.pct10:F2}%  >0.5 {before.pct50:F2}%");
            sb.AppendLine($"    after  (full J, {ShotDesc()}):        mean {after.mean:F4}  "
                        + $">0.1 {after.pct10:F2}%  >0.5 {after.pct50:F2}%");
            sb.AppendLine($"    ratio: mean x{SafeRatio(after.mean, before.mean):F2}, "
                        + $"bright (>0.5) x{SafeRatio(after.pct50, before.pct50):F2}");

            // THE THREE-WAY SPLIT. `breaking` measured at zero for the whole
            // open sea, which could be a wrong Jacobian, an envelope that is
            // not 1 out here, or a cascade weight that never lets the short
            // band in. One coverage number cannot say which, and the project
            // has paid for that confusion before (DivergenceProbe spent a
            // fortnight being read as a parity drift when it was two
            // instrument faults). So each input goes to the screen in turn.
            string[] chNames = { "j", "breaking", "env", "wJ.z", "turb", "fresh", "residual",
                                 "fade", "wC.x", "wC.z", "dist/1000" };
            var q = OceanQuality.Active;
            sb.AppendLine($"    _Ocean_FadeParams = {Shader.GetGlobalVector("_Ocean_FadeParams")}"
                        + $"   displacementFadeDistance = "
                        + $"{(q != null ? q.displacementFadeDistance : -1f):F0}"
                        + $"   innerCellSize = {(q != null ? q.innerCellSize : -1f):F2}"
                        + $"   rings = {(q != null ? q.clipmapRings : -1)}");
            sb.AppendLine("    the foam's inputs, mean over the water in this frame:");
            for (int ch = 1; ch <= 11; ch++)
            {
                Shader.SetGlobalFloat("_SS_FoamChannel", ch);
                yield return null; yield return null;
                Capture(eye, rt, shot);
                var st = FoamStats(shot);
                sb.AppendLine($"      {chNames[ch - 1],-9} mean {st.mean:F4}  >0.1 {st.pct10,6:F2}%  "
                            + $">0.5 {st.pct50,6:F2}%");
                if (ch == 1)
                    System.IO.File.WriteAllBytes($"/tmp/seasick-crest-{hs:F0}-J.png", shot.EncodeToPNG());
            }
            Shader.SetGlobalFloat("_SS_FoamChannel", 0f);

            // WHERE DOES THE BUFFER STOP BEING A WASH? Sharpening the
            // injection ramp made it WORSE (p50 0.32 -> 0.417, p95 0.420 --
            // a constant field to three decimals), which says the combined
            // Jacobian is below the threshold nearly everywhere and no amount
            // of ramp shape will discriminate. The threshold itself is the
            // knob, and the only honest way to find it is to walk it and watch
            // the buffer's SPREAD: a wash has p95 == p50, a pattern does not.
            //
            // Run on a short half-life so each step settles in seconds instead
            // of the best part of a minute.
            {
                float halfWas = ocean.Settings.foamHalflife;
                float thrWas = ocean.Settings.foamThreshold;
                ocean.Settings.foamHalflife = 1.0f;
                OceanTime.Paused = false;
                sb.AppendLine("    foamThreshold walk (half-life 1 s, buffer at steady state):");
                sb.AppendLine("      thr  | buffer p50 | p95  | p95-p50 | screen foam mean");
                foreach (float t in new[] { 0.45f, 0.30f, 0.20f, 0.12f, 0.06f, 0.02f })
                {
                    ocean.Settings.foamThreshold = t;
                    yield return new WaitForSeconds(4f);
                    var ts = ReadTurb();
                    OceanTime.Scrub(pinT);
                    OceanTime.Paused = true;
                    yield return null; yield return null;
                    Capture(eye, rt, shot);
                    var fs = FoamStats(shot);
                    OceanTime.Paused = false;
                    sb.AppendLine($"      {t,4:F2} | {ts.p50,10:F3} | {ts.p95,4:F3} | "
                                + $"{ts.p95 - ts.p50,7:F3} | {fs.mean,16:F3}");
                }
                ocean.Settings.foamHalflife = halfWas;
                ocean.Settings.foamThreshold = thrWas;
                yield return new WaitForSeconds(3f);
                OceanTime.Scrub(pinT);
                OceanTime.Paused = true;
                yield return null;
            }

            // READ THE BUFFER BEFORE TOUCHING IT. The first version of this
            // read it after the drain below and reported an empty buffer under
            // the heading "as shipped" -- a number that flatly contradicted the
            // 35 % coverage measured off the same frame, which is how it was
            // caught. FoamAccumulate also steps on OceanTime, so a paused
            // clock never refills it.
            var turbOn = ReadTurb();
            sb.AppendLine($"    turbulence buffer as shipped: mean {turbOn.mean:F3} "
                        + $"p50 {turbOn.p50:F3} p95 {turbOn.p95:F3}   "
                        + $"(foamThreshold {(ocean.Settings != null ? ocean.Settings.foamThreshold : 0f):F2}, "
                        + $"injection {(ocean.Settings != null ? ocean.Settings.foamInjection : 0f):F2}, "
                        + $"halflife {(ocean.Settings != null ? ocean.Settings.foamHalflife : 0f):F1} s)");
            if (turbOn.p50 > 0.25f)
                sb.AppendLine("      *** the buffer is SATURATED: half the open sea carries foam, "
                            + "so a breaking crest has nothing to stand out against ***");

            // WHICH FOAM IS IT? The instant fold and the persistent buffer are
            // added together and a single coverage number cannot tell them
            // apart — which matters enormously, because they want opposite
            // fixes. Muting the buffer's injection isolates the fold.
            SetInjection(ocean, 0f);
            // The buffer decays by half-life, so it has to be given time to
            // actually empty before it is read; muting and shooting the next
            // frame would report the same saturated buffer.
            OceanTime.Paused = false;
            yield return new WaitForSeconds(Mathf.Clamp(
                (ocean.Settings != null ? ocean.Settings.foamHalflife : 4f) * 5f, 5f, 30f));
            OceanTime.Scrub(pinT);
            OceanTime.Paused = true;
            yield return null; yield return null;
            Capture(eye, rt, shot);
            var foldOnly = FoamStats(shot);
            System.IO.File.WriteAllBytes($"/tmp/seasick-crest-{hs:F0}-foam-foldonly.png", shot.EncodeToPNG());
            var turbOff = ReadTurb();
            RestoreAnchors();
            if (ocean.Settings != null)
                ocean.Settings.foamInjection = injWasPer[3];
            sb.AppendLine($"    fold alone (buffer muted and drained): mean {foldOnly.mean:F4}  "
                        + $">0.1 {foldOnly.pct10:F2}%  >0.5 {foldOnly.pct50:F2}%");
            sb.AppendLine($"    => the persistent BUFFER is {100f * (1f - SafeRatio(foldOnly.mean, after.mean)):F0}% "
                        + "of all the foam on screen");
            sb.AppendLine($"    turbulence buffer with injection off: mean {turbOff.mean:F3} "
                        + $"p50 {turbOff.p50:F3} p95 {turbOff.p95:F3}");

            // ---- the look shots -------------------------------------------
            Shader.SetGlobalFloat("_SS_FoamOnly", 0f);
            eye.cullingMask = fullMask;
            eye.clearFlags = fullClear;
            yield return null; yield return null;
            Capture(eye, rt, shot);
            System.IO.File.WriteAllBytes($"/tmp/seasick-crest-{hs:F0}-shaded.png", shot.EncodeToPNG());

            SetSharpen(ocean, 1f);
            OceanTime.Scrub(pinT);
            yield return null; yield return null; yield return null;
            Capture(eye, rt, shot);
            System.IO.File.WriteAllBytes($"/tmp/seasick-crest-{hs:F0}-flat.png", shot.EncodeToPNG());
            SetSharpen(ocean, shippedSharpen);

            // ---- the particles --------------------------------------------
            // Unpaused: spume is emitted from a moving sea, and a paused one
            // emits the same crest for ever.
            OceanTime.Paused = false;
            if (spray != null)
            {
                // Let the buffer refill after the drain: the spume gate reads
                // the same foam field, so counting crests against an empty one
                // would measure the probe rather than the sea.
                yield return new WaitForSeconds(6f);
                int wasBreaks = spray.Breakers;
                float t0 = Time.time;
                yield return new WaitForSeconds(8f);
                float dt = Mathf.Max(0.01f, Time.time - t0);
                sb.AppendLine($"  spume over {dt:F1} s: peak foam {spray.PeakFoam:F3} vs gate "
                            + $"{spray.FoamGate:F2}, {(spray.Breakers - wasBreaks) / dt:F1} crests/s, "
                            + $"{spray.SpumeAlive} alive, spindrift {spray.SpindriftAlive}");
                if (spray.PeakFoam < spray.FoamGate)
                    sb.AppendLine("    *** the gate is never reached: the SIMULATION is not folding "
                                + "this sea. Raise foamThreshold on the sea states, not the gate. ***");
            }
            else sb.AppendLine("  no StormSpray in the scene — no crest spume at all");
            sb.AppendLine();
        }

        Shader.SetGlobalFloat("_SS_FoamOldJ", 0f);
        Shader.SetGlobalFloat("_SS_FoamOnly", 0f);
        Shader.SetGlobalFloat("_SS_FoamChannel", 0f);
        OceanTime.Paused = false;
        RestoreAnchors();
        sea.ReleaseForce();
        if (tuner != null) tuner.enabled = tunerWas;
        if (eye != null) Destroy(eye.gameObject);
        Finish(sb, "/tmp/seasick-crest-*.png written");
    }

    string ShotDesc() => "snapped onset";

    static float SafeRatio(float a, float b) => b > 1e-6f ? a / b : -1f;

    static readonly string[] Anchors =
        { "SeaState_Calm", "SeaState_Normal", "SeaState_Rough", "SeaState_Stormy" };

    /// The four anchors, snapshotted so they can be put back EXACTLY as they
    /// were rather than as one flattened value.
    ///
    /// The first version restored all four to whatever the blend happened to
    /// hold, which stamped one injection over a range the assets author from
    /// 0.15 to 0.45 — so the second sea state in the same run was measured
    /// against a spectrum the first pass had quietly rewritten. Assets on disk
    /// survived (runtime edits do not persist), which is the only reason it
    /// cost nothing.
    float[] injWasPer, sharpWasPer;

    void SnapshotAnchors()
    {
        injWasPer = new float[Anchors.Length];
        sharpWasPer = new float[Anchors.Length];
        for (int i = 0; i < Anchors.Length; i++)
        {
            var a = Resources.Load<OceanSpectrumSettings>("Ocean/" + Anchors[i]);
            if (a == null) continue;
            injWasPer[i] = a.foamInjection;
            sharpWasPer[i] = a.crestSharpen.z;
        }
    }

    void RestoreAnchors()
    {
        if (injWasPer == null) return;
        for (int i = 0; i < Anchors.Length; i++)
        {
            var a = Resources.Load<OceanSpectrumSettings>("Ocean/" + Anchors[i]);
            if (a == null) continue;
            a.foamInjection = injWasPer[i];
            var cs = a.crestSharpen; cs.z = sharpWasPer[i]; a.crestSharpen = cs;
        }
    }

    static void SetSharpen(OceanRenderer ocean, float z)
    {
        // The live blend AND the anchors: the blend is re-lerped whenever the
        // weather moves, and a value written only to it would be silently
        // reverted partway through a sweep. Put back by RestoreAnchors.
        if (ocean.Settings != null)
        {
            var cs = ocean.Settings.crestSharpen; cs.z = z; ocean.Settings.crestSharpen = cs;
        }
        foreach (var name in Anchors)
        {
            var a = Resources.Load<OceanSpectrumSettings>("Ocean/" + name);
            if (a == null) continue;
            var cs = a.crestSharpen; cs.z = z; a.crestSharpen = cs;
        }
    }

    static void SetInjection(OceanRenderer ocean, float v)
    {
        if (ocean.Settings != null) ocean.Settings.foamInjection = v;
        foreach (var name in Anchors)
        {
            var a = Resources.Load<OceanSpectrumSettings>("Ocean/" + name);
            if (a != null) a.foamInjection = v;
        }
    }

    struct TurbStats { public float mean, p50, p95; }

    /// The persistent foam buffer itself — slice 0 of the turbulence array,
    /// which is the number the shader multiplies into `residual`. Reading it
    /// is the difference between "there is a lot of foam" and knowing WHICH
    /// of the two foams it is.
    static TurbStats ReadTurb()
    {
        var st = new TurbStats();
        var turb = Shader.GetGlobalTexture("_Ocean_Turbulence") as RenderTexture;
        if (turb == null) return st;
        var px = ReadSlice(turb, 0);
        if (px == null || px.Length == 0) return st;
        var v = new float[px.Length];
        double sum = 0;
        for (int i = 0; i < px.Length; i++) { v[i] = px[i].r; sum += v[i]; }
        System.Array.Sort(v);
        st.mean = (float)(sum / v.Length);
        st.p50 = v[v.Length / 2];
        st.p95 = v[(int)(v.Length * 0.95f)];
        return st;
    }

    struct Cascade2Stats
    {
        public float rmsSlope, medSlope, foldPct, maxCross;
    }

    /// Reads the cascade-2 slices straight off the GPU and describes them.
    /// This is the shipped artefact — the texture the vertex shader actually
    /// samples — and not a re-run of the arithmetic that produced it.
    static Cascade2Stats ReadCascade2()
    {
        var st = new Cascade2Stats();
        var disp = Shader.GetGlobalTexture("_Ocean_Displacement") as RenderTexture;
        var deriv = Shader.GetGlobalTexture("_Ocean_Derivatives") as RenderTexture;
        if (disp == null || deriv == null) return st;

        var d = ReadSlice(disp, 2);
        var v = ReadSlice(deriv, 2);
        if (d == null || v == null) return st;

        int n = d.Length;
        var slopes = new float[n];
        double sumSq = 0; int folding = 0; float maxCross = 0f;
        for (int i = 0; i < n; i++)
        {
            float sx = v[i].r, sz = v[i].g;      // surface slope
            float dxx = v[i].b, dzz = v[i].a;    // lambda * dDx/dx, lambda * dDz/dz
            float dxzTerm = d[i].a;              // lambda * dDx/dz
            float s = Mathf.Sqrt(sx * sx + sz * sz);
            slopes[i] = s;
            sumSq += s * s;
            float j = (1f + dxx) * (1f + dzz) - dxzTerm * dxzTerm;
            if (j < 0.7f) folding++;
            if (Mathf.Abs(dxzTerm) > maxCross) maxCross = Mathf.Abs(dxzTerm);
        }
        System.Array.Sort(slopes);
        st.rmsSlope = Mathf.Sqrt((float)(sumSq / n));
        st.medSlope = slopes[n / 2];
        st.foldPct = 100f * folding / n;
        st.maxCross = maxCross;
        return st;
    }

    static Color[] ReadSlice(RenderTexture src, int slice)
    {
        var prev = RenderTexture.active;
        var tmp = RenderTexture.GetTemporary(src.width, src.height, 0, src.format);
        Graphics.CopyTexture(src, slice, 0, tmp, 0, 0);
        RenderTexture.active = tmp;
        var tex = new Texture2D(src.width, src.height, TextureFormat.RGBAFloat, false);
        tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(tmp);
        var px = tex.GetPixels();
        Destroy(tex);
        return px;
    }

    struct Foam { public float mean, pct10, pct50; public int water; }

    /// `_SS_FoamOnly` writes foamAmt to all three channels, so a water pixel is
    /// EXACTLY grey. The clear colour is blue, which the shader cannot produce.
    /// Anything not grey is not water and is not counted — the test that kept
    /// beach sand out of SurfProbe's numbers.
    static Foam FoamStats(Texture2D shot)
    {
        var px = shot.GetPixels();
        var f = new Foam();
        double sum = 0; int over10 = 0, over50 = 0;
        foreach (var c in px)
        {
            if (Mathf.Abs(c.r - c.g) > 0.004f || Mathf.Abs(c.g - c.b) > 0.004f) continue;
            f.water++;
            sum += c.r;
            if (c.r > 0.1f) over10++;
            if (c.r > 0.5f) over50++;
        }
        if (f.water == 0) return f;
        f.mean = (float)(sum / f.water);
        f.pct10 = 100f * over10 / f.water;
        f.pct50 = 100f * over50 / f.water;
        return f;
    }

    static void Capture(Camera c, RenderTexture rt, Texture2D shot)
    {
        var prev = c.targetTexture;
        c.targetTexture = rt;
        c.Render();
        c.targetTexture = prev;
        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        shot.Apply();
        RenderTexture.active = prevActive;
    }

    static float DepthAt(Vector3 p)
    {
        var region = RegionField.Instance;
        if (region == null) return 0f;
        float d = region.Params.ShoreWetDepth(
            new Unity.Mathematics.float2(p.x, p.z), region.Shore).z;
        // Outside the shore grid the depth is a 1e9 sentinel: that is open
        // ocean, which sits on the -180 m floor.
        return d > 1e8f ? 180f : d;
    }

    /// Noon, and snapped rather than eased — the look shots are the point and
    /// a 180 s day makes "whenever the probe started" a coin toss.
    static void PinNoon()
    {
        var sky = FindAnyObjectByType<SkyDirector>();
        if (sky == null) return;
        var f = typeof(SkyDirector).GetField("pinTime",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var r = typeof(SkyDirector).GetField("response",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f != null) f.SetValue(sky, 0.5f);
        if (r != null) r.SetValue(sky, 25f);
    }

    static void Finish(StringBuilder sb, string tail)
    {
        // Off again, on every path. A stuck global keyword outlives play mode
        // exactly the way a stuck global float does, and leaving it on would
        // quietly ship the debug variant of the ocean to whoever hit play next.
        Shader.DisableKeyword("_SEASICK_DEBUG");
        sb.AppendLine(tail);
        System.IO.File.WriteAllText("/tmp/seasick-crest.txt", sb.ToString());
        Debug.Log("CrestProbe:\n" + sb);
        running = false;
    }
}
