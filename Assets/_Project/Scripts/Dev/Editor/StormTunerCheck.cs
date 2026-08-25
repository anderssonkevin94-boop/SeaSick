using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;

/// Proves the Storm Tuner's two claims before anyone trusts a slider.
///
/// 1. CHAOS CANNOT CHANGE THE WAVE HEIGHT. That is the whole reason the knobs
///    are worth having over the raw fields, and it is a claim about arithmetic
///    (variance adds, heights do not), so it gets checked rather than asserted.
/// 2. The knobs round-trip: Read(Apply(k)) == k, so dragging a slider, closing
///    the window and reopening it does not quietly move the sea.
///
/// Then it renders the top-down preview to a PNG through exactly the path the
/// window uses, because "the window is empty" and "the camera is wrong" look
/// identical from a screenshot of the editor.
///
/// Play mode, Sea.unity. Writes Temp/storm-tuner-check.txt and
/// /tmp/seasick-topdown-{900,300}.png.
public class StormTunerCheck : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("StormTunerCheck: not in play mode"); return; }
        new GameObject("StormTunerCheck").AddComponent<StormTunerCheck>();
    }

    IEnumerator Start()
    {
        var sb = new StringBuilder();
        sb.AppendLine("StormTunerCheck");
        sb.AppendLine();

        var stormy = Resources.Load<OceanSpectrumSettings>("Ocean/SeaState_Stormy");
        if (stormy == null) { Debug.LogError("no SeaState_Stormy"); yield break; }

        // --- 1. what the shipped storm reads as ---------------------------
        var k = StormShape.Read(stormy);
        sb.AppendLine("shipped storm reads as:");
        sb.AppendLine($"  height {k.heightHs:F1} m   wavelength {k.wavelength:F0} m"
            + $"   chaos {k.chaos:F2}   texture x{k.texture:F1}   sharpness {k.choppiness:F2}");
        sb.AppendLine($"  estimated face {StormShape.FaceSlopeDeg(k):F1} deg"
            + "   (WaveSizeProbe measured median 15.4)");
        sb.AppendLine();

        // --- 2. does chaos hold the height? -------------------------------
        // The claim: sweeping chaos across its whole range moves energy
        // between the two trains and leaves total Hs untouched.
        var scratch = ScriptableObject.CreateInstance<OceanSpectrumSettings>();
        scratch.CopyFrom(stormy);

        sb.AppendLine("chaos sweep at a fixed height knob of 68.4 m:");
        sb.AppendLine("  chaos   main_Hs   cross_Hs   total_Hs   cross_angle   face_deg");
        float worstDrift = 0f;
        for (float c = 0f; c <= 1.001f; c += 0.25f)
        {
            var kk = k; kk.chaos = c; kk.heightHs = 68.4f;
            StormShape.Apply(scratch, kk);
            float total = 4f * Mathf.Sqrt(
                Sq(scratch.swellHeight * 0.25f) + Sq(scratch.swell2Height * 0.25f));
            worstDrift = Mathf.Max(worstDrift, Mathf.Abs(total - 68.4f));
            sb.AppendLine($"  {c,5:F2}   {scratch.swellHeight,7:F1}   {scratch.swell2Height,8:F1}"
                + $"   {total,8:F2}   {scratch.swell2DirectionDeg - scratch.swellDirectionDeg,11:F0}"
                + $"   {StormShape.FaceSlopeDeg(kk),8:F1}");
        }
        sb.AppendLine($"  worst height drift across the sweep: {worstDrift:F4} m  "
            + (worstDrift < 0.01f ? "PASS — chaos is height-neutral" : "FAIL"));
        sb.AppendLine();

        // --- 3. round trip -------------------------------------------------
        var probe = new StormShape.Knobs
        { heightHs = 42f, wavelength = 310f, chaos = 0.7f, texture = 11f, choppiness = 0.9f };
        StormShape.Apply(scratch, probe);
        var back = StormShape.Read(scratch);
        bool rt = Mathf.Abs(back.heightHs - probe.heightHs) < 0.05f
               && Mathf.Abs(back.wavelength - probe.wavelength) < 0.5f
               && Mathf.Abs(back.chaos - probe.chaos) < 0.01f
               && Mathf.Abs(back.texture - probe.texture) < 0.01f;
        sb.AppendLine("round trip Read(Apply(k)):");
        sb.AppendLine($"  in  height {probe.heightHs:F2} wl {probe.wavelength:F1} chaos {probe.chaos:F3} tex {probe.texture:F2}");
        sb.AppendLine($"  out height {back.heightHs:F2} wl {back.wavelength:F1} chaos {back.chaos:F3} tex {back.texture:F2}");
        sb.AppendLine("  " + (rt ? "PASS" : "FAIL"));
        sb.AppendLine();
        Object.DestroyImmediate(scratch);

        // --- 4. the top-down camera path ----------------------------------
        var motor = FindAnyObjectByType<ShipMotor>();
        var ctrl = SeaStateController.Instance;
        // Force only -- do NOT disable. OnDisable nulls SeaStateController.Instance,
        // which is what SkyDirector reads to drive _SS_Storminess, so disabling
        // the controller switches the storm colours and the storm fog off. The
        // first run of this check did exactly that and produced two beautiful
        // fair-weather-turquoise photographs of a severity-1.0 storm.
        if (ctrl != null) ctrl.ForceSeverity(1f);
        yield return new WaitForSeconds(8f);

        if (motor == null) { sb.AppendLine("no ShipMotor — skipped the preview render"); }
        else
        {
            // Frozen, exactly as the window's Freeze button does it, so both
            // framings are the same instant of the same sea.
            OceanTime.Paused = true;
            yield return new WaitForSeconds(0.5f);

            foreach (float view in new[] { 900f, 300f })
            {
                string path = $"/tmp/seasick-topdown-{view:F0}.png";
                Shoot(motor.transform.position, view, 300f, 900, 900, path);
                sb.AppendLine($"lit    {view:F0} m across -> {path}"
                    + $"   ({view / 24.2f:F1} boat lengths wide)"
                    + $"   [_SS_Storminess {Shader.GetGlobalFloat("_SS_Storminess"):F2}]");
                yield return null;

                string hpath = $"/tmp/seasick-height-{view:F0}.png";
                sb.AppendLine("height " + ShootHeight(motor.transform.position, view, hpath));
                yield return null;
            }
            OceanTime.Paused = false;
        }

        if (ctrl != null) ctrl.ReleaseForce();

        Directory.CreateDirectory("Temp");
        File.WriteAllText("Temp/storm-tuner-check.txt", sb.ToString());
        Debug.Log("StormTunerCheck:\n" + sb);
        Destroy(gameObject);
    }

    /// The window's render path: an orthographic camera straight down, never
    /// tagged MainCamera and never left enabled, so the ocean clipmap keeps
    /// following the real camera instead of being dragged around by this one.
    static void Shoot(Vector3 shipPos, float viewMetres, float altitude, int w, int h, string path)
    {
        var go = new GameObject("TopDownShot") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = viewMetres * 0.5f * (h / (float)w);
        cam.transform.position = new Vector3(shipPos.x, shipPos.y + altitude, shipPos.z);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.nearClipPlane = 1f;
        cam.farClipPlane = altitude + 1200f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.cullingMask = ~0;
        cam.enabled = false;

        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR);
        rt.Create();
        cam.targetTexture = rt;

        // Fog OFF. The storm's fog ends at 430 m and an overhead camera is
        // always further away than that, so the fog-respecting version of this
        // render came back a single flat black square.
        // ...and the ambient lifted, because a storm from directly above is
        // very nearly black (body colours 0.06-0.10, Fresnel at its 0.02 floor
        // when you look straight down). Correct, and useless to tune against.
        bool fogWas = RenderSettings.fog;
        float endWas = RenderSettings.fogEndDistance;
        Color skyWas = RenderSettings.ambientSkyColor;
        Color eqWas = RenderSettings.ambientEquatorColor;
        Color grWas = RenderSettings.ambientGroundColor;
        const float Exposure = 4f;
        RenderSettings.fog = false;
        RenderSettings.fogEndDistance = 100000f;
        RenderSettings.ambientSkyColor = skyWas * Exposure;
        RenderSettings.ambientEquatorColor = eqWas * Exposure;
        RenderSettings.ambientGroundColor = grWas * Exposure;
        cam.Render();
        RenderSettings.fog = fogWas;
        RenderSettings.fogEndDistance = endWas;
        RenderSettings.ambientSkyColor = skyWas;
        RenderSettings.ambientEquatorColor = eqWas;
        RenderSettings.ambientGroundColor = grWas;

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());

        Object.DestroyImmediate(tex);
        cam.targetTexture = null;
        rt.Release();
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
    }

    /// Mirrors StormTuner.SampleHeightMap so the check exercises the same path
    /// the window draws. If these two ever disagree the window is lying.
    static string ShootHeight(Vector3 shipPos, float viewMetres, string path)
    {
        const int N = 80;
        if (!OceanSampler.Ready) return "sampler not ready";

        var hs = new float[N * N];
        float min = float.MaxValue, max = float.MinValue, sum = 0f, sumSq = 0f;
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                float u = i / (float)(N - 1) - 0.5f;
                float v = j / (float)(N - 1) - 0.5f;
                float hh = OceanSampler.SampleImmediate(
                    new Vector3(shipPos.x + u * viewMetres, 0f, shipPos.z + v * viewMetres)).height;
                hs[j * N + i] = hh;
                min = Mathf.Min(min, hh); max = Mathf.Max(max, hh);
                sum += hh; sumSq += hh * hh;
            }

        float mean = sum / hs.Length;
        float rms = Mathf.Sqrt(Mathf.Max(0f, sumSq / hs.Length - mean * mean));
        float span = Mathf.Max(2f * rms, 0.05f);

        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color32[hs.Length];
        for (int k = 0; k < hs.Length; k++)
        {
            float t = Mathf.Clamp01(0.5f + (hs[k] - mean) / (2f * span));
            Color c = t < 0.5f
                ? Color.Lerp(new Color(0.03f, 0.06f, 0.16f), new Color(0.10f, 0.42f, 0.50f), t * 2f)
                : Color.Lerp(new Color(0.10f, 0.42f, 0.50f), Color.white, (t - 0.5f) * 2f);
            px[k] = c;
        }
        tex.SetPixels32(px);
        tex.Apply(false);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        return $"{viewMetres:F0} m across -> {path}   trough {min:F0} m, crest {max:F0} m, "
             + $"Hs(4xRMS) {4f * rms:F0} m";
    }

    static float Sq(float x) => x * x;
}
