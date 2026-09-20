using UnityEngine;
using SeaSick.CameraRig;

/// **Fly the god's-eye island view, and print the numbers that describe it.**
///
/// Same reason `DockCamTuner` exists and the same lesson behind it: how fast
/// a thrown island should coast, how steep the view should stand up as you
/// pull back, and how close you should be allowed to get are not things to
/// derive. Three rounds of me picking them off arithmetic would produce three
/// defensible cameras and probably none of them the one Kevin asked for —
/// which is what happened with the dock shot's tilt, and what one round of him
/// flying it settled.
///
/// So: he drives, this reports, and whatever he stops on becomes the shipped
/// numbers. **Dump C#** writes the whole block out as field initialisers that
/// paste straight over `IslandCam.Feel`.
///
/// Every value here is a plain static, not a serialized field, and that is
/// deliberate: `IslandCam` is `AddComponent`-ed at runtime, so it never
/// appears in a scene, no Inspector can shadow these, and nothing this writes
/// can be quietly reverted by a stale scene value. (The serialization trap has
/// eaten two days of this project so far, and `overviewTilt` once read 56 in a
/// running build whose source said 38.)
///
/// **TRAP, the same one every runtime tuner here has:** what you change lives
/// only in this play session. Press Dump before you stop.
///
/// It also sets `IslandCam.TunerAttached` while it is open. Without that the
/// rule "an open dev tool owns the keys" would switch off the very camera this
/// is here to tune, and every slider would move a picture that had stopped
/// responding to the mouse.
public class IslandCamTuner : MonoBehaviour, SeaSick.UI.IDevTool
{
    public string ToolName => "Island camera";
    public string ToolBlurb =>
        "the god's-eye island view: the tilt curve, the reach, the throw (only while she is lying at an island)";

    public bool ToolActive
    {
        get => active;
        set
        {
            active = value;
            // The one thing this tool must do besides draw: say that it is
            // here, so `IslandCam.Update` does not stand aside for it.
            IslandCam.TunerAttached = value;
        }
    }

    void OnEnable() => SeaSick.UI.DevTools.Register(this);
    void OnDisable()
    {
        SeaSick.UI.DevTools.Unregister(this);
        IslandCam.TunerAttached = false;
    }

    bool active;
    Vector2 scroll;
    bool seeded;
    string lastDump;

    // What the session started with, so `reset` means something.
    float[] was;

    IslandCam cam;
    ChaseCamera chase;

    IslandCam Cam()
    {
        if (cam == null) cam = Object.FindFirstObjectByType<IslandCam>();
        return cam;
    }

    ChaseCamera Chase()
    {
        if (chase == null) chase = Object.FindFirstObjectByType<ChaseCamera>();
        return chase;
    }

    // =========================================================================
    // The values, in one list.
    //
    // A list rather than twenty-odd hand-written slider lines, because the
    // dump and the sliders then cannot disagree about what the set IS -- a
    // tuner that prints a block missing the field you just moved is worse than
    // no tuner, and that is exactly the shape of mistake a second hand-written
    // copy makes.
    // =========================================================================

    class Knob
    {
        public string group, name, doc;
        public float lo, hi;
        public System.Func<float> get;
        public System.Action<float> set;
        public string format;
    }

    Knob[] knobs;

    void Build()
    {
        if (knobs != null) return;
        knobs = new[]
        {
            K("THE GROUND", "clearance (m)", 1f, 40f,
              () => IslandCam.Feel.clearance, v => IslandCam.Feel.clearance = v,
              "air between the lens and the real height field"),
            K("THE GROUND", "reach from the ship (m)", 60f, 900f,
              () => IslandCam.Feel.reachFromShip, v => IslandCam.Feel.reachFromShip = v,
              "how far the middle of the frame may be walked from her"),
            K("THE GROUND", "near zoom needs (m of ground)", 10f, 200f,
              () => IslandCam.Feel.nearGround, v => IslandCam.Feel.nearGround = v,
              "below this the terrain must be at full detail"),
            K("THE GROUND", "...within (m of the ship)", 64f, 600f,
              () => IslandCam.Feel.nearGroundReach, v => IslandCam.Feel.nearGroundReach = v,
              "and full detail only exists this far from her"),

            K("THE TILT CURVE", "low: ground (m)", 4f, 80f,
              () => IslandCam.Feel.tiltLowGround, v => IslandCam.Feel.tiltLowGround = v, ""),
            K("THE TILT CURVE", "low: tilt (deg)", 8f, 60f,
              () => IslandCam.Feel.tiltLowDeg, v => IslandCam.Feel.tiltLowDeg = v,
              "close in you are looking AT something; a steep angle is a roof"),
            K("THE TILT CURVE", "mid: ground (m)", 60f, 300f,
              () => IslandCam.Feel.tiltMidGround, v => IslandCam.Feel.tiltMidGround = v, ""),
            K("THE TILT CURVE", "mid: tilt (deg)", 12f, 70f,
              () => IslandCam.Feel.tiltMidDeg, v => IslandCam.Feel.tiltMidDeg = v,
              "THE SHIPPED SHOT: 165 m must give 32 deg or an untouched view moves"),
            K("THE TILT CURVE", "far: ground (m)", 200f, 900f,
              () => IslandCam.Feel.tiltHighGround, v => IslandCam.Feel.tiltHighGround = v, ""),
            K("THE TILT CURVE", "far: tilt (deg)", 20f, 85f,
              () => IslandCam.Feel.tiltHighDeg, v => IslandCam.Feel.tiltHighDeg = v,
              "far out you are reading a plan; a shallow angle is all horizon"),
            K("THE TILT CURVE", "hand may tilt from (deg)", 5f, 40f,
              () => IslandCam.Feel.minTiltDeg, v => IslandCam.Feel.minTiltDeg = v, ""),
            K("THE TILT CURVE", "...to (deg)", 40f, 89f,
              () => IslandCam.Feel.maxTiltDeg, v => IslandCam.Feel.maxTiltDeg = v, ""),
            K("THE TILT CURVE", "terrain may force (deg)", 40f, 89f,
              () => IslandCam.Feel.yieldMaxTiltDeg, v => IslandCam.Feel.yieldMaxTiltDeg = v,
              "standing up over a hill keeps the same ground in frame; backing off does not"),

            K("THROWING THE LAND", "throw window (s)", 0.02f, 0.3f,
              () => IslandCam.Feel.flingSample, v => IslandCam.Feel.flingSample = v,
              "how much pointer history the throw is averaged over", "F3"),
            K("THROWING THE LAND", "decay (e-folds/s)", 0.5f, 12f,
              () => IslandCam.Feel.flingDecay, v => IslandCam.Feel.flingDecay = v, ""),
            K("THROWING THE LAND", "cut after (s)", 0.3f, 4f,
              () => IslandCam.Feel.flingSeconds, v => IslandCam.Feel.flingSeconds = v,
              "so 'it stops' is a fact and not an asymptote", "F2"),
            K("THROWING THE LAND", "stop under (m/s)", 0.05f, 4f,
              () => IslandCam.Feel.flingStop, v => IslandCam.Feel.flingStop = v, "", "F2"),
            K("THROWING THE LAND", "swing gain", 0f, 1.5f,
              () => IslandCam.Feel.orbitFlingGain, v => IslandCam.Feel.orbitFlingGain = v,
              "a swing that keeps going is disorienting in a way a pan is not", "F2"),
            K("THROWING THE LAND", "swing stops under (deg/s)", 0.5f, 20f,
              () => IslandCam.Feel.orbitFlingStop, v => IslandCam.Feel.orbitFlingStop = v, "", "F1"),

            K("THE GESTURES", "grab step cap (x ground)", 0.5f, 8f,
              () => IslandCam.Feel.grabStepGrounds, v => IslandCam.Feel.grabStepGrounds = v,
              "one bad near-horizon frame must not throw the island off screen", "F2"),
            K("THE GESTURES", "believe rays steeper than", 0.005f, 0.3f,
              () => IslandCam.Feel.minRayDown, v => IslandCam.Feel.minRayDown = v,
              "0.03 is about 1.7 deg below the horizontal", "F3"),
            K("THE GESTURES", "fly-to lands at (m of ground)", 12f, 200f,
              () => IslandCam.Feel.flyToGround, v => IslandCam.Feel.flyToGround = v, ""),
            K("THE GESTURES", "key swing (deg/s)", 10f, 200f,
              () => IslandCam.Feel.orbitKeyDegPerSecond,
              v => IslandCam.Feel.orbitKeyDegPerSecond = v, ""),
            K("THE GESTURES", "key tilt (deg/s)", 5f, 120f,
              () => IslandCam.Feel.tiltKeyDegPerSecond,
              v => IslandCam.Feel.tiltKeyDegPerSecond = v, ""),
            K("THE GESTURES", "pick radius (x screen height)", 0.02f, 0.2f,
              () => IslandCam.Feel.pickRadius, v => IslandCam.Feel.pickRadius = v,
              "a FRACTION, never pixels: a phone and a desk window do not have the same ones",
              "F3"),
        };

        was = new float[knobs.Length];
        for (int i = 0; i < knobs.Length; i++) was[i] = knobs[i].get();
        seeded = true;
    }

    static Knob K(string group, string name, float lo, float hi,
                  System.Func<float> get, System.Action<float> set,
                  string doc, string format = "F1")
        => new Knob
        {
            group = group, name = name, lo = lo, hi = hi,
            get = get, set = set, doc = doc, format = format,
        };

    // =========================================================================
    // Drawing
    // =========================================================================

    GUIStyle head, small, note;

    void Styles()
    {
        if (head != null) return;
        head = new GUIStyle(GUI.skin.label)
        {
            richText = true,
            fontStyle = FontStyle.Bold,
            fontSize = SeaSick.UI.UITheme.Unit,
        };
        small = new GUIStyle(GUI.skin.label)
        {
            richText = true,
            fontSize = Mathf.Max(9, SeaSick.UI.UITheme.Unit - 2),
            wordWrap = false,
        };
        note = new GUIStyle(small) { fontStyle = FontStyle.Italic };
        note.normal.textColor = SeaSick.UI.UITheme.TextDim;
    }

    public void DrawTool(Rect body)
    {
        Build();
        Styles();

        var c = Cam();
        if (c == null || c.Subject == null)
        {
            GUI.Label(body, "she is not lying at an island — nothing to fly",
                      SeaSick.UI.UITheme.Small);
            return;
        }

        GUILayout.BeginArea(body);

        // --- what the view is doing right now --------------------------------
        //
        // On screen, and not in the console, for the reason DockCamTuner's
        // readout is: a screenshot then carries every number needed to
        // reproduce the shot, instead of a picture to reverse-engineer.
        GUILayout.Label(Readout(c), small);

        GUILayout.Space(SeaSick.UI.HudLayout.Gap);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("dump C#")) Dump();
        if (GUILayout.Button("reset")) Reset();
        if (GUILayout.Button("home")) c.Home();
        GUILayout.EndHorizontal();

        GUILayout.Space(SeaSick.UI.HudLayout.Gap);
        scroll = GUILayout.BeginScrollView(scroll);

        string group = null;
        foreach (var k in knobs)
        {
            if (k.group != group)
            {
                group = k.group;
                GUILayout.Space(SeaSick.UI.HudLayout.Gap);
                GUILayout.Label(group, head);
                if (group == "THE TILT CURVE") GUILayout.Label(TiltLine(), note);
            }
            Row(k);
            if (!string.IsNullOrEmpty(k.doc)) GUILayout.Label("   " + k.doc, note);
        }

        GUILayout.Space(SeaSick.UI.HudLayout.Gap);
        GUILayout.Label("what you change lives only in this play session — "
                      + "dump before you stop", note);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void Row(Knob k)
    {
        float v = k.get();
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{k.name}  <b>{v.ToString(k.format)}</b>",
                        small, GUILayout.Width(Mathf.Min(230f, Screen.width * 0.28f)));
        float after = GUILayout.HorizontalSlider(v, k.lo, k.hi);
        GUILayout.EndHorizontal();
        if (!Mathf.Approximately(after, v)) k.set(after);
    }

    /// The curve, as the four numbers anybody would actually check it by.
    static string TiltLine()
    {
        return $"   8 m {IslandCam.AutoTilt(8f):F1}°   "
             + $"40 m {IslandCam.AutoTilt(40f):F1}°   "
             + $"<b>165 m {IslandCam.AutoTilt(165f):F1}°</b>   "
             + $"300 m {IslandCam.AutoTilt(300f):F1}°   "
             + $"520 m {IslandCam.AutoTilt(520f):F1}°";
    }

    string Readout(IslandCam c)
    {
        var ch = Chase();
        string pose = "—";
        float overTerrain = -1f;
        if (c.VirtualPose(out Vector3 seat, out _, out float fov))
        {
            var h = GroundPick.Height;
            overTerrain = h != null ? seat.y - h(seat.x, seat.z) : -1f;
            pose = $"lens ({seat.x:F0}, {seat.y:F0}, {seat.z:F0})  lens {fov:F0}°";
        }

        Vector3 p = c.Pivot;
        float fromShip = SeaSick.World.Island.FlatDistance(p, transform.position);

        return $"<b>{c.Ground:F0} m of ground</b>   tilt {c.TiltNow:F1}°   "
             + $"bearing {c.AzimuthDeg:F0}°   bias {c.TiltBiasDeg:+0.0;-0.0;0.0}°\n"
             + $"{pose}"
             + (overTerrain >= 0f ? $"   <b>{overTerrain:F1} m</b> over the ground" : "")
             + $"\nmiddle of the frame ({p.x:F0}, {p.z:F0})   "
             + $"{fromShip:F0} m from her (reach {IslandCam.Feel.reachFromShip:F0})\n"
             + $"{(c.HandsOn ? "hands on" : "composed")}"
             + $"   {(c.Grabbing ? "GRABBING" : "")}"
             + $"   blend {(ch != null ? ch.OverviewLevel : 0f):F3}"
             + $"   {(c.Ready ? "" : "<color=#f88>not ready</color>")}"
             + (string.IsNullOrEmpty(lastDump) ? "" : "\n" + lastDump);
    }

    // =========================================================================
    // Dump and reset
    // =========================================================================

    void Reset()
    {
        for (int i = 0; i < knobs.Length; i++) knobs[i].set(was[i]);
        lastDump = "back to what this session started with";
    }

    /// The whole block, as C# that pastes over `IslandCam.Feel`.
    ///
    /// Written as INITIALISERS rather than as a table of values, because the
    /// journey from "Kevin liked this" to "the game ships it" should be a
    /// paste and not a transcription. Every value that has moved is marked.
    void Dump()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// IslandCam.Feel — flown by hand, "
                    + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        sb.AppendLine("public static float clearance = "
                    + F(IslandCam.Feel.clearance) + ";");
        sb.AppendLine("public static float reachFromShip = "
                    + F(IslandCam.Feel.reachFromShip) + ";");
        sb.AppendLine("public static float nearGround = "
                    + F(IslandCam.Feel.nearGround) + ";");
        sb.AppendLine("public static float nearGroundReach = "
                    + F(IslandCam.Feel.nearGroundReach) + ";");
        sb.AppendLine("public static float tiltLowGround = "
                    + F(IslandCam.Feel.tiltLowGround) + ", tiltLowDeg = "
                    + F(IslandCam.Feel.tiltLowDeg) + ";");
        sb.AppendLine("public static float tiltMidGround = "
                    + F(IslandCam.Feel.tiltMidGround) + ", tiltMidDeg = "
                    + F(IslandCam.Feel.tiltMidDeg) + ";");
        sb.AppendLine("public static float tiltHighGround = "
                    + F(IslandCam.Feel.tiltHighGround) + ", tiltHighDeg = "
                    + F(IslandCam.Feel.tiltHighDeg) + ";");
        sb.AppendLine("public static float minTiltDeg = "
                    + F(IslandCam.Feel.minTiltDeg) + ", maxTiltDeg = "
                    + F(IslandCam.Feel.maxTiltDeg) + ";");
        sb.AppendLine("public static float yieldMaxTiltDeg = "
                    + F(IslandCam.Feel.yieldMaxTiltDeg) + ";");
        sb.AppendLine("public static float flingSample = "
                    + F(IslandCam.Feel.flingSample) + ";");
        sb.AppendLine("public static float flingDecay = "
                    + F(IslandCam.Feel.flingDecay) + ";");
        sb.AppendLine("public static float flingSeconds = "
                    + F(IslandCam.Feel.flingSeconds) + ";");
        sb.AppendLine("public static float flingStop = "
                    + F(IslandCam.Feel.flingStop) + ";");
        sb.AppendLine("public static float orbitFlingGain = "
                    + F(IslandCam.Feel.orbitFlingGain) + ";");
        sb.AppendLine("public static float orbitFlingStop = "
                    + F(IslandCam.Feel.orbitFlingStop) + ";");
        sb.AppendLine("public static float grabStepGrounds = "
                    + F(IslandCam.Feel.grabStepGrounds) + ";");
        sb.AppendLine("public static float minRayDown = "
                    + F(IslandCam.Feel.minRayDown) + ";");
        sb.AppendLine("public static float flyToGround = "
                    + F(IslandCam.Feel.flyToGround) + ";");
        sb.AppendLine("public static float orbitKeyDegPerSecond = "
                    + F(IslandCam.Feel.orbitKeyDegPerSecond) + ";");
        sb.AppendLine("public static float tiltKeyDegPerSecond = "
                    + F(IslandCam.Feel.tiltKeyDegPerSecond) + ";");
        sb.AppendLine("public static float pickRadius = "
                    + F(IslandCam.Feel.pickRadius) + ";");
        sb.AppendLine();
        sb.AppendLine("// the curve it makes:");
        sb.AppendLine("// " + TiltLine().Replace("<b>", "").Replace("</b>", "").Trim());

        int moved = 0;
        for (int i = 0; i < knobs.Length; i++)
            if (!Mathf.Approximately(knobs[i].get(), was[i])) moved++;

        string text = sb.ToString();
        Debug.Log("IslandCamTuner — " + moved + " value(s) moved:\n" + text);
        try
        {
            var path = System.IO.Path.Combine(
                Application.dataPath, "../Logs/IslandCamFeel.txt");
            System.IO.File.WriteAllText(path, text);
            lastDump = $"dumped — {moved} moved — Logs/IslandCamFeel.txt";
        }
        catch { lastDump = $"dumped to the console — {moved} moved"; }
    }

    /// Enough digits to reproduce the feel, and a trailing `f` so it compiles.
    static string F(float v)
        => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "f";
}
