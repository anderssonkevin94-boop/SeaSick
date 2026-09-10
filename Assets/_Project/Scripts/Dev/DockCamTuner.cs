using UnityEngine;
using UnityEngine.InputSystem;
using SeaSick.World;

/// Fly the docked overview by hand, and print the numbers that describe it.
///
/// This exists because composition is not something to measure -- it is
/// something to look at and judge, and the judging is Kevin's. Three rounds
/// of me picking a tilt and a framing radius from arithmetic produced three
/// views that were each defensible and none of them what he wanted. So:
/// he drives, the rig reports, and whatever he stops on becomes the shipped
/// numbers.
///
/// The readout is drawn ON SCREEN deliberately. A screenshot then carries
/// every number I need to reproduce the shot exactly, instead of a picture I
/// have to reverse-engineer an angle from.
///
/// Controls, while lying at the dock:
///   right-mouse drag  swing around / raise and lower the angle
///   scroll wheel      closer and further
///   I / K             push the framing point away / pull it back
///   J / L             slide it left / right
///   U / O             widen / narrow the lens
///   P                 print these numbers (also written to /tmp)
///   backspace         back to the shipped framing
public class DockCamTuner : MonoBehaviour
{
    /// **Kept deliberately, switched off.** The framing it produced is
    /// shipped in ChaseCamera (tilt 32, fov 36, 165 m of ground up the
    /// frame); this stays in the scene so the next question about an angle
    /// can be answered the same way instead of by me guessing again. Tick
    /// `active` in the Inspector to take the camera back.
    [Tooltip("Off = the game's own framing, untouched. Tick to fly the docked camera by hand.")]
    [SerializeField] bool active = false;

    [Header("Feel")]
    [SerializeField] float orbitPerPixel = 0.25f;
    [SerializeField] float tiltPerPixel = 0.18f;
    [SerializeField] float zoomPerNotch = 0.08f;
    [SerializeField] float panSpeed = 34f;

    SeaSick.CameraRig.ChaseCamera chase;
    SeaSick.Ship.AnchorController anchor;
    Camera cam;

    bool seeded;
    float azimuth;      // degrees, where the camera sits around the centre
    float tilt = 38f;
    float span = 200f;
    float fov = 60f;
    Vector3 centre;
    GUIStyle box;

    void Awake()
    {
        chase = FindAnyObjectByType<SeaSick.CameraRig.ChaseCamera>();
        anchor = FindAnyObjectByType<SeaSick.Ship.AnchorController>();
        cam = chase != null ? chase.GetComponent<Camera>() : null;
    }

    /// True only while she is lying at the dock, which is the only time the
    /// overview is up and so the only time there is anything to tune.
    bool Docked => anchor != null && anchor.CurrentDock != null
        && anchor.CurrentState == SeaSick.Ship.AnchorController.State.Anchored;

    void Update()
    {
        if (!active || chase == null || !Docked) { if (chase != null) chase.OverviewOverride = null; return; }

        // Start from wherever the game had put it, so the first nudge is a
        // nudge and not a jump.
        if (!seeded && chase.Overview.HasValue)
        {
            var o = chase.Overview.Value;
            centre = o.centre;
            Vector3 f = o.from; f.y = 0f;
            azimuth = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            tilt = chase.CurrentTilt > 0.01f ? chase.CurrentTilt : 38f;
            span = chase.CurrentSpan > 0.01f ? chase.CurrentSpan : 200f;
            fov = cam != null ? cam.fieldOfView : 60f;
            seeded = true;
        }
        if (!seeded) return;

        var m = Mouse.current;
        var k = Keyboard.current;

        if (m != null && m.rightButton.isPressed)
        {
            Vector2 d = m.delta.ReadValue();
            azimuth -= d.x * orbitPerPixel;
            tilt = Mathf.Clamp(tilt + d.y * tiltPerPixel, 8f, 89f);
        }
        if (m != null)
        {
            float scroll = m.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                span = Mathf.Clamp(span * (1f - Mathf.Sign(scroll) * zoomPerNotch), 30f, 1200f);
        }

        if (k != null)
        {
            // Pan in the camera's own frame, so "left" is left on screen.
            float rad = azimuth * Mathf.Deg2Rad;
            Vector3 back = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            Vector3 right = new Vector3(back.z, 0f, -back.x);
            float dt = Time.unscaledDeltaTime * panSpeed;
            if (k.iKey.isPressed) centre -= back * dt;
            if (k.kKey.isPressed) centre += back * dt;
            if (k.jKey.isPressed) centre -= right * dt;
            if (k.lKey.isPressed) centre += right * dt;
            if (k.uKey.isPressed) fov = Mathf.Clamp(fov + 20f * Time.unscaledDeltaTime, 25f, 90f);
            if (k.oKey.isPressed) fov = Mathf.Clamp(fov - 20f * Time.unscaledDeltaTime, 25f, 90f);
            if (k.pKey.wasPressedThisFrame) Dump();
            if (k.backspaceKey.wasPressedThisFrame) { seeded = false; chase.OverviewOverride = null; return; }
        }

        centre.y = 0f;
        float a = azimuth * Mathf.Deg2Rad;
        chase.OverviewOverride = new SeaSick.CameraRig.ChaseCamera.IslandShot
        {
            centre = centre,
            radius = 1f,                       // unused: span is explicit
            from = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)),
            tiltDeg = tilt,
            span = span,
        };
        if (cam != null) cam.fieldOfView = fov;
    }

    string Report()
    {
        float t = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        float personFrac = WorldScale.Person / Mathf.Max(0.01f, 2f * span * t);
        float groundTall = 2f * span * t;
        var village = Settlement.Home;
        var dock = Dock.Home;
        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();

        string shipSeen = "?";
        if (cam != null && motor != null)
        {
            // Frustum at the PHONE's aspect, not the editor Game view's.
            // Read off the live camera, this readout says "fully in shot"
            // about a landscape window while the ship is off the side of the
            // portrait frame the game ships in -- which is precisely the
            // failure this tuner exists to prevent somebody eyeballing.
            float wasAspect = cam.aspect;
            cam.aspect = SeaSick.CameraRig.ChaseCamera.PortraitAspect;
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            cam.aspect = wasAspect;
            int seen = 0;
            for (float u = -0.5f; u <= 0.5f; u += 1f)
                for (float v = -0.5f; v <= 0.5f; v += 1f)
                {
                    Vector3 p = motor.transform.position
                              + motor.transform.forward * (u * WorldScale.ShipLength)
                              + motor.transform.right * (v * 8.44f);
                    if (GeometryUtility.TestPlanesAABB(planes, new Bounds(p, Vector3.one * 2f))) seen++;
                }
            shipSeen = seen == 4 ? "fully in shot" : seen == 0 ? "NOT VISIBLE" : $"clipped ({seen}/4)";
        }

        return $"tilt {tilt:F0}°   span {span:F0} m   fov {fov:F0}°   azimuth {azimuth:F0}°\n"
             + $"centre ({centre.x:F0}, {centre.z:F0})"
             + (village != null ? $"   village centre ({village.Centre.x:F0}, {village.Centre.z:F0})" : "")
             + (dock != null ? $"   berth ({dock.Berth.x:F0}, {dock.Berth.z:F0})" : "") + "\n"
             + $"ground in frame {groundTall:F0} m tall\n"
             + $"a crew member is {personFrac * 100f:F2}% of screen height "
             + $"= {personFrac * 2340f:F0} px on a phone\n"
             + $"hut {personFrac * 2340f * WorldScale.Hut / WorldScale.Person:F0} px   "
             + $"longhouse {personFrac * 2340f * WorldScale.Longhouse / WorldScale.Person:F0} px   "
             + $"tower {personFrac * 2340f * WorldScale.WatchTower / WorldScale.Person:F0} px\n"
             + $"the ship: {shipSeen}"
             + (village != null ? $"\nsettlement holds ~{village.Capacity()} buildings on 15 m plots" : "");
    }

    void Dump()
    {
        string s = Report();
        System.IO.File.WriteAllText("/tmp/seasick-camtune.txt", s);
        Debug.Log("DockCamTuner — framing saved:\n" + s);
    }

    void OnGUI()
    {
        if (!active || !Docked || !seeded) return;
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
            + "\n\nright-drag swing/tilt   scroll zoom   IJKL pan   U/O lens"
            + "\nP save these numbers   backspace reset";
        var size = box.CalcSize(new GUIContent(text));
        // Top-left, clear of the compass and the minimap.
        GUI.Box(new Rect(8f, Screen.height * 0.30f, size.x + 6f, size.y + 4f), text, box);
    }
}
