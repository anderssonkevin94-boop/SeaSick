using System.Collections;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;

/// Two contact sheets of the water shader, because the reason it will not
/// assemble in your head is that you have only ever seen it in a drifting sea
/// where you cannot hold two states side by side.
///
///   states.png  — calm / rough / heavy / mountainous, SAME wave phase, same
///                 seed, same camera, same sun. Only the spectrum differs.
///   layers.png  — the same wave with the shading stacked up one term at a
///                 time: body colour, + subsurface, + sky and glitter, + foam.
///
/// Everything that makes a comparison honest is pinned. OceanTime is scrubbed
/// to one instant so every tile is the same water; the sea state is forced;
/// the ship is held so the chase camera frames identically. The ship stays IN
/// frame on purpose — a 1.6 m sea and a 65 m sea look identical without
/// something of known size in the picture.
///
/// Rendered through the MAIN camera into a RenderTexture, which is also what
/// drops the HUD (IMGUI never reaches a RenderTexture).
///
/// Plain C# for Coplay. Run in play mode in Sea.unity.
/// Writes /tmp/seasick-shader-states.png and /tmp/seasick-shader-layers.png.
public class ShaderStrip : MonoBehaviour
{
    const int TileW = 512, TileH = 340;
    const double PinT = 4200.0;   // one arbitrary but FIXED instant

    static readonly float[] StateHs = { 1.6f, 14f, 30f, 65f };
    // Suppression masks: subsurface, sky reflection, sun glitter, foam.
    static readonly Vector4[] LayerOff =
    {
        new Vector4(1, 1, 1, 1),   // body colour alone
        new Vector4(0, 1, 1, 1),   // + subsurface
        new Vector4(0, 0, 0, 1),   // + sky reflection and glitter
        new Vector4(0, 0, 0, 0),   // + foam: the shipped shader
    };

    /// Sun elevations, degrees above the horizon. The scene ships at 50, which
    /// a chase camera looking at the skyline never sees -- it shows about 25
    /// degrees of sky, so the sun disc has always been above the frame. A long
    /// glitter track on water is a LOW-sun phenomenon besides: at 50 degrees
    /// the specular is a compact patch beside the boat however it is tuned.
    static readonly float[] SunElevations = { 8f, 16f, 28f, 50f };

    /// Times of day, 0..1 (0.25 = sunrise, 0.5 = noon, 0.75 = sunset). Chosen
    /// to straddle every transition the palette blend has: full night, the
    /// twilight band on both sides of the horizon crossing, and noon. Dawn and
    /// dusk are both here on purpose -- they share one authored palette and
    /// the sun's own arc is what makes them differ, so a sheet where they look
    /// identical means the arc is not being read.
    static readonly float[] DayTimes = { 0.92f, 0.24f, 0.29f, 0.50f, 0.755f, 0.80f };
    static readonly string[] DayNames = { "night", "dawn", "sunrise", "noon", "sunset", "dusk" };

    bool sunMode;
    bool dayMode;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("ShaderStrip: not in play mode"); return; }
        var old = FindAnyObjectByType<ShaderStrip>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("ShaderStrip").AddComponent<ShaderStrip>();
    }

    /// Just the sun-elevation sheet.
    public static void SunAngles()
    {
        if (!Application.isPlaying) { Debug.LogError("ShaderStrip: not in play mode"); return; }
        var old = FindAnyObjectByType<ShaderStrip>();
        if (old != null) Destroy(old.gameObject);
        var go = new GameObject("ShaderStrip");
        var c = go.AddComponent<ShaderStrip>();
        c.sunMode = true;
    }

    /// The time-of-day sheet: one pinned sea, one pinned wave phase, one
    /// camera, and only the hour changing.
    public static void DaySheet()
    {
        if (!Application.isPlaying) { Debug.LogError("ShaderStrip: not in play mode"); return; }
        var old = FindAnyObjectByType<ShaderStrip>();
        if (old != null) Destroy(old.gameObject);
        var go = new GameObject("ShaderStrip");
        var c = go.AddComponent<ShaderStrip>();
        c.dayMode = true;
    }

    bool pin; Vector3 pinAt; ShipMotor motor;

    IEnumerator Start()
    {
        motor = FindAnyObjectByType<ShipMotor>();
        var ctrl = SeaStateController.Instance;
        var cam = Camera.main;
        if (motor == null || ctrl == null || cam == null || !OceanSampler.Ready)
        {
            Debug.LogError("ShaderStrip: no ship / controller / camera / sampler");
            yield break;
        }

        // Make the sky snap instead of easing over its 3 s time constant, so
        // each tile does not cost ten seconds of waiting.
        var sky = FindAnyObjectByType<SeaSick.World.SkyDirector>();
        var responseField = sky != null
            ? typeof(SeaSick.World.SkyDirector).GetField("response",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            : null;
        object savedResponse = null;
        if (responseField != null)
        {
            savedResponse = responseField.GetValue(sky);
            responseField.SetValue(sky, 25f);
        }

        pinAt = motor.transform.position; pin = true;
        var rb = motor.GetComponent<Rigidbody>();

        var rt = new RenderTexture(TileW, TileH, 24, RenderTextureFormat.ARGB32);
        var tile = new Texture2D(TileW, TileH, TextureFormat.RGB24, false);

        // SkyDirector owns the directional light now: it reasserts the
        // light's rotation from TimeOfDay every LateUpdate. Writing
        // light.rotation therefore lasts exactly one frame, which is why the
        // hour is driven through the OWNER's pinTime field rather than by
        // aiming the light -- the same trap that made a sail-trim probe report
        // 0.1 s trims and a camera probe measure nothing.
        var pinField = sky != null
            ? typeof(SeaSick.World.SkyDirector).GetField("pinTime",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            : null;
        object savedPin = pinField != null ? pinField.GetValue(sky) : null;

        if (dayMode)
        {
            if (pinField == null)
            {
                Debug.LogError("ShaderStrip: no SkyDirector.pinTime -- cannot pin the hour");
                yield break;
            }

            var days = new Texture2D(TileW * DayTimes.Length, TileH, TextureFormat.RGB24, false);

            // A moderate sea. The point of this sheet is the LIGHT, and a 65 m
            // storm would bury it under foam and its own grey lid.
            yield return SetSea(ctrl, 3.5f, rb);

            // Pin the DAY, not just the hour. pinTime goes through SetTime01,
            // which preserves the day number -- and the moon's bearing and its
            // phase both hang off `day / synodicDays`. At a 180 s day a play
            // session rolls the counter over in three minutes, so two runs of
            // this sheet put the moon in different places and gave it a
            // different phase, and the dawn tile lost its moon between one run
            // and the next with nothing reporting a change. Same rule as
            // pinning the sea state and the wave phase: pin everything the
            // picture depends on, or the sheet measures when you pressed the
            // button.
            SeaSick.World.TimeOfDay.Scrub(0.0);

            for (int i = 0; i < DayTimes.Length; i++)
            {
                pinField.SetValue(sky, DayTimes[i]);
                // SkyDirector applies on LateUpdate and the palette blend is
                // snapped (response forced to 25 above), but the ambient and
                // the fog land a frame later than the skybox.
                yield return null; yield return null; yield return null;
                Capture(cam, rt, tile);
                days.SetPixels(i * TileW, 0, TileW, TileH, tile.GetPixels());
                Debug.Log($"ShaderStrip: day tile {i} '{DayNames[i]}' t={DayTimes[i]:F3} "
                        + $"night01={sky.Night01:F2} sunY={sky.SunDirection.y:F3} "
                        + $"moonY={sky.MoonDirection.y:F3} sunIsUp={sky.SunIsUp} "
                        + $"day={SeaSick.World.TimeOfDay.Day} phase={SeaSick.World.TimeOfDay.MoonPhase01(SeaSick.World.TimeOfDay.Day, 29.5f):F2}");
            }
            days.Apply();
            System.IO.File.WriteAllBytes("/tmp/seasick-shader-day.png", days.EncodeToPNG());

            pinField.SetValue(sky, savedPin);
            OceanTime.Paused = false;
            ctrl.ReleaseForce();
            if (responseField != null && savedResponse != null)
                responseField.SetValue(sky, savedResponse);
            pin = false;
            Destroy(rt); Destroy(tile); Destroy(days);
            Debug.Log("ShaderStrip: wrote /tmp/seasick-shader-day.png");
            Destroy(gameObject);
            yield break;
        }

        if (sunMode)
        {
            // Put the sun on the CAMERA's azimuth rather than turning the boat
            // to face it -- "can you see the sun" is not a question you can
            // answer with the camera pointed somewhere else, and the chase
            // camera is not ours to aim. Same azimuth for all four tiles, so
            // only elevation differs.
            var suns = new Texture2D(TileW * SunElevations.Length, TileH, TextureFormat.RGB24, false);
            var light = FindSunTransform();
            Quaternion savedSun = light != null ? light.rotation : Quaternion.identity;
            // +180, and it is not a fudge: a directional light TRAVELS along
            // its forward vector, so the sun sits at the OPPOSITE azimuth to
            // the light's yaw. Setting the yaw to the camera's heading puts
            // the sun squarely behind the camera, which is exactly the sheet
            // this first produced -- four identical tiles and no sun in any of
            // them.
            float azimuth = cam.transform.eulerAngles.y + 180f;

            yield return SetSea(ctrl, 3.5f, rb);      // a calm-ish sea: glitter reads best on it
            for (int i = 0; i < SunElevations.Length; i++)
            {
                if (light != null) light.rotation = Quaternion.Euler(SunElevations[i], azimuth, 0f);
                yield return null; yield return null; yield return null;
                Capture(cam, rt, tile);
                suns.SetPixels(i * TileW, 0, TileW, TileH, tile.GetPixels());
                Debug.Log($"ShaderStrip: sun tile {i} at {SunElevations[i]} deg");
            }
            suns.Apply();
            System.IO.File.WriteAllBytes("/tmp/seasick-shader-suns.png", suns.EncodeToPNG());
            if (light != null) light.rotation = savedSun;   // put the scene back

            OceanTime.Paused = false;
            ctrl.ReleaseForce();
            if (responseField != null && savedResponse != null)
                responseField.SetValue(sky, savedResponse);
            pin = false;
            Destroy(rt); Destroy(tile); Destroy(suns);
            Debug.Log("ShaderStrip: wrote /tmp/seasick-shader-suns.png");
            Destroy(gameObject);
            yield break;
        }

        var states = new Texture2D(TileW * StateHs.Length, TileH, TextureFormat.RGB24, false);
        var layers = new Texture2D(TileW * LayerOff.Length, TileH, TextureFormat.RGB24, false);

        // --- sea states, shipped shader ------------------------------------
        Shader.SetGlobalVector("_SS_LayerOff", Vector4.zero);
        for (int i = 0; i < StateHs.Length; i++)
        {
            yield return SetSea(ctrl, StateHs[i], rb);
            Capture(cam, rt, tile);
            states.SetPixels(i * TileW, 0, TileW, TileH, tile.GetPixels());
            Debug.Log($"ShaderStrip: state tile {i} at Hs {ctrl.CurrentHs:F1} ({ctrl.CurrentStateName})");
        }
        states.Apply();
        System.IO.File.WriteAllBytes("/tmp/seasick-shader-states.png", states.EncodeToPNG());

        // --- shading layers, one sea ---------------------------------------
        // Heavy rather than the storm: every term is doing something at 30 m,
        // and at 65 the foam covers enough of the frame to hide the rest.
        yield return SetSea(ctrl, 30f, rb);
        for (int i = 0; i < LayerOff.Length; i++)
        {
            Shader.SetGlobalVector("_SS_LayerOff", LayerOff[i]);
            yield return null; yield return null;
            Capture(cam, rt, tile);
            layers.SetPixels(i * TileW, 0, TileW, TileH, tile.GetPixels());
        }
        layers.Apply();
        System.IO.File.WriteAllBytes("/tmp/seasick-shader-layers.png", layers.EncodeToPNG());

        // --- put everything back -------------------------------------------
        // Probes leave static state pinned across play sessions unless they
        // are explicit about it, and a stuck _SS_LayerOff would ship an ocean
        // with its reflections turned off.
        Shader.SetGlobalVector("_SS_LayerOff", Vector4.zero);
        OceanTime.Paused = false;
        ctrl.ReleaseForce();
        if (responseField != null && savedResponse != null)
            responseField.SetValue(sky, savedResponse);
        pin = false;

        Destroy(rt); Destroy(tile); Destroy(states); Destroy(layers);
        Debug.Log("ShaderStrip: wrote /tmp/seasick-shader-states.png and -layers.png");
        Destroy(gameObject);
    }

    /// Force a sea state and then freeze it at the pinned instant.
    ///
    /// The order matters and is not obvious: OceanTime must be RUNNING while
    /// the state changes, because SeaStateController throttles its spectrum
    /// rebuild on `OceanTime.Now - lastRebuildTime` — with the clock paused
    /// that difference stays zero forever and the spectrum silently never
    /// updates. Unpause, change, let it land, then scrub and freeze.
    IEnumerator SetSea(SeaStateController ctrl, float hs, Rigidbody rb)
    {
        OceanTime.Paused = false;
        ctrl.ForceHs(hs);
        yield return new WaitForSeconds(2.5f);        // rebuild + readback + camera ease
        OceanTime.Scrub(PinT);
        OceanTime.Paused = true;
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        yield return new WaitForSeconds(1.5f);        // let her settle on the frozen surface
    }

    static Transform FindSunTransform()
    {
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) return l.transform;
        return null;
    }

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

    void LateUpdate()
    {
        if (!pin || motor == null) return;
        var p = motor.transform.position;
        motor.transform.position = new Vector3(pinAt.x, p.y, pinAt.z);
    }
}
