using SeaSick.CameraRig;
using SeaSick.UI;
using SeaSick.UI.Sheets;
using UnityEngine;
using UnityEngine.InputSystem;
using ET = UnityEngine.InputSystem.EnhancedTouch;

namespace SeaSick.Ship
{
    /// Experimental command layer. HelmInput calls Tick instead of writing its
    /// own orders; the motor, buoyancy and propulsion remain the only physics owners.
    [DisallowMultipleComponent]
    public sealed class SailingPilot : MonoBehaviour
    {
        [SerializeField] bool experimental = true;
        public bool Experimental => experimental;
        public Vector3? Destination { get; private set; }
        public string Status { get; private set; } = "Stopped";
        public static SailingPilot Instance { get; private set; }
        static bool Running => Instance != null && Instance.isActiveAndEnabled
            && Instance.owner != null && Instance.owner.enabled && Instance.experimental;
        public static bool OwnsWorldInput => Running && !IslandCam.Engaged
            && Instance.motor != null && !Instance.motor.Anchored && Time.timeScale > 0f;
        public static bool BlocksZoom => Running && (Instance.manual || Instance.blockPinch);
        public static Vector3 ViewOffset => OwnsWorldInput ? Instance.pan : Vector3.zero;

        ShipMotor motor;
        HelmInput owner;
        Rigidbody body;
        Vector3 pan;
        Rect panel, stopRect, centerRect, modeRect, helmRect;
        const int NoFinger = int.MinValue;
        const int MouseFinger = -100;
        int finger = NoFinger;
        Vector2 down, previous;
        bool moved, manual, suppress, blockPinch, holdHeading, keyboardDriving;
        float heading, throttle, startThrottle, rudder, nextSafety;
        float warningUntil;
        Camera gestureCamera;
        GUIStyle buttonStyle, statusStyle, helmStyle;
        int styleSize;

        void Awake()
        {
            motor = GetComponent<ShipMotor>();
            owner = GetComponent<HelmInput>();
            body = GetComponent<Rigidbody>();
            Instance = this;
        }

        void OnDisable()
        {
            Stop();
            if (Instance == this) Instance = null;
        }
        void OnEnable() => Instance = this;
        void OnApplicationFocus(bool focused) { if (!focused && Running) Stop(); }
        void OnApplicationPause(bool paused) { if (paused && Running) Stop(); }

        public void SetExperimental(bool value)
        {
            if (experimental == value) return;
            owner.AllStop();
            experimental = value;
            motor.AutopilotTarget = null;
            motor.ThrottleOrder = motor.Rudder = 0f;
            motor.Rowing = false;
            Status = "Stopped";
            suppress = true;
        }

        public void Stop()
        {
            Destination = null;
            throttle = rudder = 0f;
            holdHeading = false;
            finger = NoFinger;
            manual = false;
            suppress = true;
            pan = Vector3.zero;
            if (motor != null && experimental)
            {
                motor.AutopilotTarget = null;
                motor.ThrottleOrder = motor.Rudder = 0f;
                motor.Rowing = false;
            }
            Status = "Stopped";
        }

        bool Busy => IslandCam.Engaged || motor.Anchored || Time.timeScale <= 0f
            || CampSiting.Placing || (Hand.Instance != null && Hand.Instance.Holding);

        public void Tick()
        {
            if (motor == null) return;
            Layout();
            if (Busy)
            {
                Stop();
                Status = motor.Anchored ? "Anchored" : "Stopped";
                return;
            }
            var combat = GetComponent<SeaSick.Combat.CombatLock>();
            if (combat != null && combat.Locked != null) pan = Vector3.zero;
            ReadGesture();
            bool keyboard = ReadKeyboard();
            if (Destination.HasValue && !keyboard && !manual) FollowCourse();
            if (!manual && !keyboard)
            {
                float targetRudder = 0f;
                if (holdHeading)
                {
                    float rate = body != null ? body.angularVelocity.y * Mathf.Rad2Deg : 0f;
                    targetRudder = Mathf.Clamp(Mathf.DeltaAngle(motor.Heading, heading) * .04f - rate * .02f, -1f, 1f);
                    if (Vector3.Dot(motor.Velocity, transform.forward) < -.05f) targetRudder = -targetRudder;
                }
                rudder = Mathf.MoveTowards(rudder, targetRudder, 3.5f * Time.deltaTime);
            }
            motor.AutopilotTarget = null;
            motor.Rudder = rudder;
            motor.ThrottleOrder = throttle;
        }

        bool ReadKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            if (kb.escapeKey.wasPressedThisFrame) Stop();
            if (kb.rKey.wasPressedThisFrame) motor.Rowing = !motor.Rowing;
            float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1 : 0)
                - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1 : 0);
            float y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1 : 0)
                - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1 : 0);
            if (x == 0f && y == 0f)
            {
                if (keyboardDriving) throttle = 0f;
                keyboardDriving = false;
                return false;
            }
            keyboardDriving = true;
            Destination = null;
            heading = motor.Heading;
            holdHeading = true;
            rudder = x;
            throttle = y;
            Status = "Manual";
            return true;
        }

        public bool TrySetDestination(Vector3 point)
        {
            if (!experimental || Busy) return false;
            if (!SailingCourse.Clear(transform.position, point, Clearance, Draft,
                GroundPick.Height))
            {
                Status = "No clear-water course";
                warningUntil = Time.unscaledTime + 3f;
                return false;
            }
            point.y = 0f;
            Destination = point;
            motor.Rowing = false;
            pan = Vector3.zero;
            nextSafety = 0f;
            warningUntil = 0f;
            holdHeading = true;
            Status = "Underway";
            return true;
        }

        float Clearance => Mathf.Max(2f, motor.HullLength * .20f);
        float Draft => Mathf.Max(2f, motor.HullLength * .12f);
        float Arrival => Mathf.Max(3f, motor.HullLength * .22f);

        void FollowCourse()
        {
            Vector3 delta = Destination.Value - transform.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance <= Arrival)
            {
                Destination = null;
                throttle = 0f;
                heading = motor.Heading;
                Status = "Arrived";
                return;
            }
            heading = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            float error = Mathf.DeltaAngle(motor.Heading, heading);
            throttle = SailingCourse.SpeedOrder(distance, Arrival, motor.CurrentSpeed,
                motor.MaxSpeed, error);
            if (Time.unscaledTime >= nextSafety)
            {
                nextSafety = Time.unscaledTime + .5f;
                Vector3 travel = motor.Velocity;
                travel.y = 0f;
                float stopping = travel.sqrMagnitude / (2f * .65f) + motor.HullLength;
                Vector3 look = transform.position + travel.normalized *
                    Mathf.Min(SailingCourse.MaxRange, Mathf.Max(Clearance * 2f, stopping));
                if (!SailingCourse.Clear(transform.position, look, Clearance, Draft, GroundPick.Height))
                {
                    Stop();
                    Status = "Course blocked - stopping";
                    warningUntil = Time.unscaledTime + 3f;
                    return;
                }
            }
            if (Time.unscaledTime >= warningUntil)
                Status = throttle < .05f ? "Slowing" : "Underway";
        }

        void ReadGesture()
        {
            var touches = ET.Touch.activeTouches;
            int count = 0;
            ET.Touch touch = default;
            foreach (var t in touches)
            {
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended
                    || t.phase == UnityEngine.InputSystem.TouchPhase.Canceled) continue;
                count++;
                touch = t;
            }
            if (count > 1)
            {
                blockPinch |= manual;
                EndGesture(false);
                suppress = true;
                return;
            }
            if (count == 1)
            {
                if (suppress) return;
                if (finger != NoFinger && finger != touch.touchId)
                { EndGesture(false); suppress = true; return; }
                Feed(touch.touchId, touch.screenPosition,
                    touch.phase == UnityEngine.InputSystem.TouchPhase.Began);
                return;
            }
            if (finger != NoFinger && finger != MouseFinger)
            {
                bool ended = false;
                foreach (var t in touches)
                    if (t.touchId == finger && t.phase == UnityEngine.InputSystem.TouchPhase.Ended)
                    { RecordRelease(t.screenPosition); ended = true; }
                EndGesture(ended);
            }
            // Real-touch frames must not fall through to the synthesized mouse.
            if (touches.Count > 0) return;
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                if (!suppress) Feed(MouseFinger, mouse.position.ReadValue(), mouse.leftButton.wasPressedThisFrame);
            }
            else
            {
                if (finger == MouseFinger)
                {
                    if (mouse != null) RecordRelease(mouse.position.ReadValue());
                    EndGesture(mouse != null && mouse.leftButton.wasReleasedThisFrame);
                }
                suppress = blockPinch = false;
            }
        }

        void Feed(int id, Vector2 point, bool began)
        {
            if (began && finger == NoFinger)
            {
                Vector2 gui = new Vector2(point.x, Screen.height - point.y);
                bool onHelm = helmRect.Contains(gui) && !Sheets.IsOpen;
                if (!onHelm && (panel.Contains(gui) || UIBlocker.Blocked(point))) return;
                finger = id;
                down = previous = point;
                gestureCamera = Camera.main;
                moved = false;
                manual = onHelm;
                if (manual)
                {
                    Destination = null;
                    holdHeading = false;
                    startThrottle = throttle;
                    pan = Vector3.zero;
                    Status = "Manual";
                }
                return;
            }
            if (id != finger) return;
            float slop = Mathf.Max(12f, Mathf.Min(Screen.width, Screen.height) * .018f);
            moved |= (point - down).sqrMagnitude > slop * slop;
            if (manual)
            {
                float radius = Mathf.Min(Screen.width, Screen.height) * .14f;
                rudder = Mathf.Clamp((point.x - down.x) / radius, -1f, 1f);
                throttle = Mathf.Clamp(startThrottle + (point.y - down.y) / radius, -1f, 1f);
            }
            else if (moved)
            {
                if (WaterPoint(gestureCamera, previous, out var a)
                    && WaterPoint(gestureCamera, point, out var b))
                    pan = Vector3.ClampMagnitude(pan + a - b, 150f);
            }
            previous = point;
        }

        void EndGesture(bool released)
        {
            if (finger == NoFinger) return;
            bool tap = released && !manual && !moved;
            if (manual)
            {
                heading = motor.Heading;
                holdHeading = true;
                rudder = 0f;
            }
            finger = NoFinger;
            manual = false;
            if (!tap || UIBlocker.Blocked(previous)) return;
            if (WorldPicker.TrySelect(previous)) return;
            // Dismissing a sheet must not also launch the ship.
            if (Sheets.IsOpen) { Sheets.Close(); return; }
            var combat = GetComponent<SeaSick.Combat.CombatLock>();
            if (combat != null)
            {
                var result = combat.TryTap(gestureCamera, previous);
                if (result != SeaSick.Combat.CombatLock.TapResult.Miss)
                {
                    if (result == SeaSick.Combat.CombatLock.TapResult.OutOfRange)
                    {
                        Status = "Target out of range";
                        warningUntil = Time.unscaledTime + 3f;
                    }
                    else
                    {
                        Stop();
                        Status = result == SeaSick.Combat.CombatLock.TapResult.Engaged
                            ? "Target locked" : "Target released";
                    }
                    return;
                }
            }
            if (WaterPoint(gestureCamera, previous, out var hit))
            {
                Ray ray = gestureCamera.ScreenPointToRay(previous);
                if (GroundPick.Along(ray, out var ground) && ground.y > 0f
                    && Vector3.Distance(ray.origin, ground) < Vector3.Distance(ray.origin, hit))
                    return;
                TrySetDestination(hit);
            }
        }

        void RecordRelease(Vector2 point)
        {
            float slop = Mathf.Max(12f, Mathf.Min(Screen.width, Screen.height) * .018f);
            moved |= (point - down).sqrMagnitude > slop * slop;
            previous = point;
        }

        static bool WaterPoint(Camera camera, Vector2 screen, out Vector3 point)
        {
            point = default;
            if (camera == null) return false;
            Ray ray = camera.ScreenPointToRay(screen);
            if (ray.direction.y > -.025f) return false;
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance)
                || distance > 2000f) return false;
            point = ray.GetPoint(distance);
            return true;
        }

        void Layout()
        {
            float u = HudLayout.Unit;
            float h = Mathf.Max(u * 2.5f, Mathf.Min(Screen.width, Screen.height) * .105f);
            float gap = u * .3f;
            float width = Mathf.Min(h * 4.5f + gap * 2f, HudLayout.Safe.width - u * 2f);
            panel = HudLayout.Place(experimental ? HudLayout.Slot.Helm : HudLayout.Slot.HelmActions,
                width, experimental ? h * 2.8f + gap : h);
            float cell = (width - gap * 2f) / 3f;
            stopRect = new Rect(panel.x, panel.y, cell, h);
            centerRect = new Rect(panel.x + cell + gap, panel.y, cell, h);
            modeRect = new Rect(panel.x + 2f * (cell + gap), panel.y, cell, h);
            helmRect = new Rect(panel.x, panel.y + h + gap, width, h * 1.35f);
        }

        void OnGUI()
        {
            if (owner == null || !owner.enabled || IslandCam.Engaged) return;
            Layout();
            int size = Mathf.RoundToInt(stopRect.height * .29f);
            if (buttonStyle == null || size != styleSize)
            {
                styleSize = size;
                buttonStyle = new GUIStyle(UITheme.Button) { fontSize = size };
                statusStyle = new GUIStyle(UITheme.Small2Centered) { fontSize = Mathf.RoundToInt(size * .78f) };
                helmStyle = new GUIStyle(UITheme.Strong) { fontSize = size };
            }
            UIBlocker.Block(panel);
            if (!experimental)
            {
                if (GUI.Button(panel, "Try tap sailing", buttonStyle)) SetExperimental(true);
                return;
            }
            if (GUI.Button(stopRect, "Stop", buttonStyle)) Stop();
            if (GUI.Button(centerRect, "Center", buttonStyle)) pan = Vector3.zero;
            if (GUI.Button(modeRect, "Classic", buttonStyle)) SetExperimental(false);
            UITheme.Rect(helmRect, manual ? UITheme.Sea : UITheme.PanelSolid);
            GUI.Label(helmRect, "Helm", helmStyle);
            GUI.Label(new Rect(panel.x, helmRect.yMax, panel.width, stopRect.height * .45f), Status, statusStyle);
            DrawCourse();
        }

        void DrawCourse()
        {
            if (!Destination.HasValue || Event.current.type != EventType.Repaint) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 goal = Destination.Value;
            goal.y = transform.position.y;
            Vector3 screen = cam.WorldToScreenPoint(goal);
            if (screen.z <= 0f) return;
            Vector2 p = new Vector2(screen.x, Screen.height - screen.y);
            float r = HudLayout.Unit * .55f;
            UITheme.Rect(new Rect(p.x-r, p.y-2, r*2, 4), UITheme.Sea);
            UITheme.Rect(new Rect(p.x-2, p.y-r, 4, r*2), UITheme.Sea);
            for (int i = 1; i < 20; i++)
            {
                Vector3 s = cam.WorldToScreenPoint(Vector3.Lerp(transform.position, goal, i / 20f));
                if (s.z <= 0f) continue;
                Vector2 at = new Vector2(s.x, s.y);
                if (UIBlocker.Blocked(at)) continue;
                UITheme.Rect(new Rect(s.x-2, Screen.height-s.y-2, 4, 4), UITheme.Sea);
            }
        }
    }
}
