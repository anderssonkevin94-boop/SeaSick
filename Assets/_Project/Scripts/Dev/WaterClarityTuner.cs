using UnityEngine;
using UnityEngine.InputSystem;
using SeaSick.World;

/// Drive the water's clarity by hand, with the numbers on screen.
///
/// Same reason `DockCamTuner` exists: how much of the bottom you should be
/// able to see is not a thing to measure, it is a thing to look at and judge,
/// and the judging is Kevin's. Three rounds of me picking an extinction rate
/// from arithmetic would produce three defensible seas and probably none of
/// them the one he asked for.
///
/// The two terms are separate and it matters that they can be judged
/// separately -- see the shoal/murk note in `Ocean.shader`:
///
///   SHOAL  the colour water takes over a bottom, from the VERTICAL depth.
///          Survives a grazing angle, so it is what tells you where the
///          shallow water is from a deck.
///   MURK   actually seeing the bottom, from the length of the water column
///          along the VIEW RAY. Dies at a grazing angle, by physics.
///
/// The readout prints the one number that connects them: at the angle you
/// are actually looking, how deep the water can be and still show you its
/// floor. That is the number the question "can I see how deep it is" is
/// really about, and it is not the same as the murk slider.
///
/// Edits go to the SHARED material, so whatever you stop on is what the
/// project keeps -- no copying numbers back by hand. `P` also prints them.
///
/// Controls (tick `active` in the Inspector first):
///   sliders           drag them
///   1 / 2             mute the murk / the shoal, to see one at a time
///   P                 print the block (console and /tmp/seasick-clarity.txt)
///   backspace         back to the values this session started with
public class WaterClarityTuner : MonoBehaviour, SeaSick.UI.IDevTool
{
    // --- IDevTool: this tuner is opened from the settings drawer, and the
    // drawer is what decides where it draws. See SeaSick.UI.DevTools.
    public string ToolName => "Water clarity";
    public string ToolBlurb => "how far down you can see, and the colour the water takes over a bottom";
    public bool ToolActive { get => active; set => active = value; }
    void OnEnable() => SeaSick.UI.DevTools.Register(this);

    [Tooltip("Off = the shipped water, untouched. Tick to tune it by hand.")]
    [SerializeField] bool active = false;

    Material mat;
    Camera cam;
    bool seeded;
    float murkWas, refrWas, shoalDWas, shoalSWas;
    Color murkXWas, shoalCWas;
    bool murkMuted, shoalMuted;

    void Seed()
    {
        if (seeded) return;
        mat = FindOceanMaterial();
        if (mat == null) return;
        murkWas = mat.GetFloat("_MurkDepth");
        refrWas = mat.GetFloat("_RefractStrength");
        shoalDWas = mat.GetFloat("_ShoalDepth");
        shoalSWas = mat.GetFloat("_ShoalStrength");
        murkXWas = mat.GetColor("_MurkExtinction");
        shoalCWas = mat.GetColor("_ShoalColor");
        seeded = true;
    }

    // Whether THIS tuner currently has the ocean's debug variant switched on.
    // Tracked rather than set every frame: a keyword change is a shader
    // variant switch, not a uniform write, and is not something to do 60
    // times a second for no reason.
    bool debugKeyword;

    /// `_SS_RefractOff` is only declared in the ocean shader's `_SEASICK_DEBUG`
    /// variant now -- the shipped sea does not carry the six dev uniforms at
    /// all. Setting the global without the keyword does nothing, and does it
    /// SILENTLY: the 1 key would simply look broken. So the keyword follows
    /// the tick box.
    ///
    /// This is a single global switch and several dev tools want it, so the
    /// keyword is owner-refcounted through `SeaDebugKeyword` rather than set
    /// bare: a probe finishing (or another tuner ticking off) while this one
    /// is still active used to be last-writer-wins and could turn the
    /// keyword off out from under this tuner. Acquire/Release below only
    /// touch this tuner's own membership in that set, so the 1 key cannot go
    /// quiet because some other tool finished.
    void SetDebugKeyword(bool on)
    {
        if (on == debugKeyword) return;
        debugKeyword = on;
        if (on) SeaDebugKeyword.Acquire(this);
        else SeaDebugKeyword.Release(this);
    }

    void Update()
    {
        SetDebugKeyword(active);
        if (!active) return;
        Seed();
        if (mat == null) return;
        // Camera.main does a scene-wide FindObjectWithTag under the hood;
        // this tool's camera doesn't change scene to scene, so look it up
        // once and keep it.
        if (cam == null) cam = Camera.main;
        var k = Keyboard.current;
        if (k == null) return;
        if (k.digit1Key.wasPressedThisFrame) murkMuted = !murkMuted;
        if (k.digit2Key.wasPressedThisFrame) shoalMuted = !shoalMuted;
        if (k.pKey.wasPressedThisFrame) Print();
        if (k.backspaceKey.wasPressedThisFrame) Restore();
        Shader.SetGlobalFloat("_SS_RefractOff", murkMuted ? 1f : 0f);
    }

    void OnDisable()
    {
        SeaSick.UI.DevTools.Unregister(this);
        Shader.SetGlobalFloat("_SS_RefractOff", 0f);
        SetDebugKeyword(false);
    }

    /// Drawn inside the settings drawer's rect. It used to take a fixed
    /// 430x470 box out of the top left, which is where the crew pips are —
    /// and where `IslandTuner` and `DevHUD` also drew.
    public void DrawTool(Rect body)
    {
        Seed();
        if (mat == null) return;

        GUILayout.BeginArea(body);
        GUILayout.Label("<b>WATER CLARITY</b>   1 murk  2 shoal  P print  ⌫ reset",
            new GUIStyle(GUI.skin.label) { richText = true });

        GUILayout.Space(6);
        GUILayout.Label($"MURK — seeing the bottom{(murkMuted ? "   [MUTED]" : "")}");
        float murk = Row("see down (m)", mat.GetFloat("_MurkDepth"), 0.5f, 40f);
        mat.SetFloat("_MurkDepth", murk);
        Color ext = mat.GetColor("_MurkExtinction");
        ext.r = Row("  absorbs red", ext.r, 0.2f, 6f);
        ext.g = Row("  absorbs green", ext.g, 0.2f, 6f);
        ext.b = Row("  absorbs blue", ext.b, 0.2f, 6f);
        mat.SetColor("_MurkExtinction", ext);
        mat.SetFloat("_RefractStrength", Row("refraction", mat.GetFloat("_RefractStrength"), 0f, 2f));

        GUILayout.Space(8);
        GUILayout.Label($"SHOAL — the colour of water over a bottom{(shoalMuted ? "   [MUTED]" : "")}");
        mat.SetFloat("_ShoalDepth", Row("fades out by (m)", mat.GetFloat("_ShoalDepth"), 1f, 60f));
        float ss = Row("strength", shoalMuted ? shoalSWas : mat.GetFloat("_ShoalStrength"), 0f, 1f);
        mat.SetFloat("_ShoalStrength", shoalMuted ? 0f : ss);
        if (shoalMuted) shoalSWas = ss;
        Color sc = mat.GetColor("_ShoalColor");
        sc.r = Row("  red", sc.r, 0f, 1f);
        sc.g = Row("  green", sc.g, 0f, 1f);
        sc.b = Row("  blue", sc.b, 0f, 1f);
        mat.SetColor("_ShoalColor", sc);

        // The number the question is actually about. A view ray at pitch
        // theta crosses depth/sin(theta) metres of water, so the depth you
        // can see to is the murk range times sin(theta) -- which is why
        // looking along the water shows you nothing and looking down it
        // shows you sand, at the same setting.
        GUILayout.Space(8);
        float pitch = cam != null
            ? Mathf.Max(1f, -Mathf.Asin(Mathf.Clamp(cam.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg)
            : 30f;
        float seeTo = murk * Mathf.Sin(pitch * Mathf.Deg2Rad);
        GUILayout.Label($"looking down {pitch:F0}°  →  bottom visible in water up to "
                      + $"<b>{seeTo:F1} m</b> deep", new GUIStyle(GUI.skin.label) { richText = true });

        var ship = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        if (ship != null && Island.TerrainHeight != null)
        {
            var p = ship.transform.position;
            float d = -Island.TerrainHeight(p.x, p.z);
            GUILayout.Label($"water under her: <b>{d:F1} m</b>"
                + (d < seeTo ? "  — you should see the bottom here" : "  — too deep to see through"),
                new GUIStyle(GUI.skin.label) { richText = true });
        }
        GUILayout.EndArea();
    }

    static float Row(string label, float v, float lo, float hi)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label} {v:F2}", GUILayout.Width(190));
        v = GUILayout.HorizontalSlider(v, lo, hi);
        GUILayout.EndHorizontal();
        return v;
    }

    void Restore()
    {
        if (mat == null) return;
        mat.SetFloat("_MurkDepth", murkWas);
        mat.SetFloat("_RefractStrength", refrWas);
        mat.SetFloat("_ShoalDepth", shoalDWas);
        mat.SetFloat("_ShoalStrength", shoalSWas);
        mat.SetColor("_MurkExtinction", murkXWas);
        mat.SetColor("_ShoalColor", shoalCWas);
        murkMuted = shoalMuted = false;
        Shader.SetGlobalFloat("_SS_RefractOff", 0f);
    }

    /// Printed in the shader's own Properties syntax, so a value that is
    /// worth keeping can be pasted into the defaults rather than living only
    /// in a material somebody might revert.
    void Print()
    {
        Color e = mat.GetColor("_MurkExtinction"), s = mat.GetColor("_ShoalColor");
        string t =
            $"_MurkDepth (\"Murk — metres you can see down\", Range(0.5, 40)) = {mat.GetFloat("_MurkDepth"):F2}\n"
          + $"_MurkExtinction (\"Murk — per-channel absorption rate\", Color) = ({e.r:F2}, {e.g:F2}, {e.b:F2}, 1)\n"
          + $"_RefractStrength (\"Refraction (m at the surface)\", Range(0, 2)) = {mat.GetFloat("_RefractStrength"):F2}\n"
          + $"_ShoalColor (\"Shoal — colour of water over a bottom\", Color) = ({s.r:F2}, {s.g:F2}, {s.b:F2}, 1)\n"
          + $"_ShoalDepth (\"Shoal — depth it fades out by (m)\", Range(1, 60)) = {mat.GetFloat("_ShoalDepth"):F1}\n"
          + $"_ShoalStrength (\"Shoal strength\", Range(0, 1)) = {mat.GetFloat("_ShoalStrength"):F2}\n";
        System.IO.File.WriteAllText("/tmp/seasick-clarity.txt", t);
        Debug.Log("WaterClarityTuner — paste into Ocean.shader Properties:\n" + t);
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
