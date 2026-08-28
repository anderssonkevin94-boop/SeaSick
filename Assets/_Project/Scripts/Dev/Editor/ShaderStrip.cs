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

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("ShaderStrip: not in play mode"); return; }
        var old = FindAnyObjectByType<ShaderStrip>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("ShaderStrip").AddComponent<ShaderStrip>();
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
