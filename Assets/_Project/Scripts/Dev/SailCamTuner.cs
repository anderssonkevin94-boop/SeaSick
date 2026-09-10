using UnityEngine;
using SeaSick.World;

/// Fly the SAILING camera by hand, with the numbers that matter drawn on
/// screen. The docked shot has had `DockCamTuner` since the village work; this
/// is the same thing for the shot you spend the game in, and it exists for the
/// same reason: three rounds of me measuring and guessing lost to one round of
/// Kevin flying it.
///
/// **What it reports is the DECK, not her bounding box.** The rig framing
/// scales to her oriented bounds, and her masts are about twice the hull's
/// height — so she can measure 62% of a portrait frame while the deck, which
/// is the thing you are trying to read, is a small strip inside it. Every
/// number here is about the part you actually look at.
///
/// Judged at 1080x2340, because the editor Game view is landscape and the game
/// is not.
///
/// Tick `active`, then:
///   right-drag   swing behind her / raise and lower the eye
///   scroll       in and out
///   I / K        look further ahead or nearer
///   U / O        lens
///   P            print the numbers to paste into ChaseCamera
///   backspace    back to the shipped rig
public class SailCamTuner : MonoBehaviour, SeaSick.UI.IDevTool
{
    // --- IDevTool: opened from the settings drawer, which decides where it
    // draws. See SeaSick.UI.DevTools.
    public string ToolName => "Sail camera";
    public string ToolBlurb => "the seat under way: distance astern, height, look-ahead and lens";
    public bool ToolActive { get => active; set => active = value; }
    void OnEnable() => SeaSick.UI.DevTools.Register(this);
    void OnDisable() => SeaSick.UI.DevTools.Unregister(this);

    [Tooltip("Off = the game's own rig, untouched. Tick to fly it.")]
    [SerializeField] bool active = false;

    [Header("Feel")]
    [SerializeField] float heightPerPixel = 0.10f;
    [SerializeField] float zoomPerNotch = 0.10f;
    [SerializeField] float aheadPerKey = 12f;

    SeaSick.CameraRig.ChaseCamera chase;
    SeaSick.Ship.ShipMotor motor;
    Camera cam;
    GUIStyle box;

    bool seeded;
    float dist, hgt, ahead, lookH, fov;

    void Awake()
    {
        chase = FindAnyObjectByType<SeaSick.CameraRig.ChaseCamera>();
        motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        cam = chase != null ? chase.GetComponent<Camera>() : Camera.main;
    }

    void Seed()
    {
        // Seed from what the rig is ACTUALLY using, so ticking the box does
        // not move the camera. A tuner that jumps on the first frame is a
        // tuner you cannot compare against.
        dist = 25f; hgt = 16.5f; ahead = 20f; lookH = 0.5f;
        fov = cam != null ? cam.fieldOfView : 58f;
        seeded = true;
    }

    void Update()
    {
        if (chase == null) return;
        // Only clear an override this tuner is actually holding.
        //
        // Clearing it unconditionally meant a switched-OFF tuner sitting in
        // the scene nulled the hook every frame, which silently defeated
        // anything else driving it: SailShots set ten different seats and
        // photographed the same one ten times, because this ran in between.
        // An idle tool must not touch shared state.
        if (!active)
        {
            if (seeded) { chase.SailOverride = null; seeded = false; }
            return;
        }
        if (!seeded) Seed();

        var m = UnityEngine.InputSystem.Mouse.current;
        var k = UnityEngine.InputSystem.Keyboard.current;
        if (m != null && m.rightButton.isPressed)
        {
            Vector2 d = m.delta.ReadValue();
            hgt = Mathf.Clamp(hgt + d.y * heightPerPixel, 1f, 90f);
            dist = Mathf.Clamp(dist - d.x * heightPerPixel, 4f, 160f);
        }
        if (m != null)
        {
            float scroll = m.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                dist = Mathf.Clamp(dist * (1f - Mathf.Sign(scroll) * zoomPerNotch), 4f, 160f);
        }
        if (k != null)
        {
            float dt = Time.unscaledDeltaTime;
            if (k.iKey.isPressed) ahead += aheadPerKey * dt;
            if (k.kKey.isPressed) ahead -= aheadPerKey * dt;
            if (k.uKey.isPressed) fov = Mathf.Clamp(fov - 8f * dt, 20f, 90f);
            if (k.oKey.isPressed) fov = Mathf.Clamp(fov + 8f * dt, 20f, 90f);
            if (k.pKey.wasPressedThisFrame) Dump();
            if (k.backspaceKey.wasPressedThisFrame) Seed();
        }

        chase.SailOverride = new SeaSick.CameraRig.ChaseCamera.SailShot
        {
            distance = dist, height = hgt, lookAhead = ahead, lookHeight = lookH, fov = fov,
        };
    }

    /// How tall something is on a 1080x2340 phone, measured through the live
    /// camera at the PHONE's aspect rather than the editor's.
    float FracOfHeight(Vector3 a, Vector3 b)
    {
        if (cam == null) return 0f;
        float was = cam.aspect;
        cam.aspect = SeaSick.CameraRig.ChaseCamera.PortraitAspect;
        Vector3 va = cam.WorldToViewportPoint(a), vb = cam.WorldToViewportPoint(b);
        cam.aspect = was;
        if (va.z <= 0f || vb.z <= 0f) return 0f;
        return Vector2.Distance(new Vector2(va.x, va.y), new Vector2(vb.x, vb.y));
    }

    string Report()
    {
        const float Px = 2340f;
        float loa = motor != null ? motor.HullLength : WorldScale.ShipLength;
        string deck = "no ship";
        string beam = "";
        if (motor != null)
        {
            Vector3 p = motor.transform.position, f = motor.transform.forward, r = motor.transform.right;
            float fl = FracOfHeight(p - f * (loa * 0.5f), p + f * (loa * 0.5f));
            float fb = FracOfHeight(p - r * (loa * 0.16f), p + r * (loa * 0.16f));
            deck = $"deck {loa:F0} m long: {fl * 100f:F0}% of frame height ({fl * Px:F0} px)";
            beam = $"\nher beam across the frame: {fb * 100f:F0}% ({fb * Px:F0} px)";
        }

        float crewPx = 0f;
        var crew = FindObjectsByType<SeaSick.Crew.CrewAgent>(FindObjectsSortMode.None);
        foreach (var c in crew)
        {
            if (!c.IsAboard) continue;
            crewPx = FracOfHeight(c.transform.position, c.transform.position + Vector3.up * WorldScale.Person) * Px;
            break;
        }

        float tilt = Mathf.Atan2(hgt - lookH, dist + ahead) * Mathf.Rad2Deg;
        bool horizon = tilt < fov * 0.5f;

        return $"distance {dist:F0} m   height {hgt:F0} m   lookAhead {ahead:F0} m   lens {fov:F0}°\n"
             + $"eye sits {tilt:F0}° above her — horizon {(horizon ? "IN frame" : "OFF the top")}\n"
             + deck + beam + "\n"
             + $"a crew member on deck: {crewPx:F0} px"
             + (crewPx > 0f && crewPx < 40f ? "  (under 40 px — you cannot read a pose)" : "")
             + $"\nframing scale x{chase.FramingScale:F2} (per-hull)   judged at 1080x2340";
    }

    void Dump()
    {
        string s = Report()
            + $"\n\npaste into ChaseCamera:  distance {dist:F1}   height {hgt:F1}"
            + $"   lookAhead {ahead:F1}   fovBase {fov:F1}";
        System.IO.File.WriteAllText("/tmp/seasick-sailcam.txt", s);
        Debug.Log("SailCamTuner — framing saved:\n" + s);
    }

    /// The readout, inside the settings drawer's rect. It used to be a
    /// `GUI.Box` at `8, Screen.height * 0.28f` — on the Yard and Home tabs, and on the
    /// other camera tuner's readout, which drew 20 pixels away.
    public void DrawTool(Rect body)
    {
        if (!seeded) return;
        if (box == null)
            box = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = Mathf.Max(11, Screen.height / 62),
                padding = new RectOffset(10, 10, 8, 8),
                richText = false,
            };
        box.normal.textColor = Color.white;

        string text = Report()
            + "\n\nright-drag in/out + up/down   scroll zoom   I/K look ahead"
            + "\nU/O lens   P save these numbers   backspace reset";
        var size = box.CalcSize(new GUIContent(text));
        GUI.Box(new Rect(body.x, body.y,
                         Mathf.Min(size.x + 6f, body.width),
                         Mathf.Min(size.y + 4f, body.height)), text, box);
    }
}
