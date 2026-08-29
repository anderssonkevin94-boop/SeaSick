using SeaSick.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.Ship
{
    /// The helm. WASD at a desk, one thumb on a phone.
    ///
    /// W and S drive: hold for ahead or astern, release and the telegraph
    /// takes back over. A and D steer. The sail steps are gone -- she is a
    /// paddle steamer, windDriven has been off in the scene the whole time,
    /// and "furled / half / full" was a sailing rig's vocabulary bolted onto
    /// an engine that PaddleDrive was already reading as a throttle. The
    /// telegraph underneath is the same control for a thumb, and it now has
    /// an astern notch, which canvas could never have.
    ///
    /// Touch: hold anywhere in the lower steering zone; horizontal position
    /// maps to absolute tiller position, and releasing eases the rudder back
    /// to centre.
    [RequireComponent(typeof(ShipMotor))]
    public class HelmInput : MonoBehaviour
    {
        [SerializeField, Range(0.1f, 1f)] float steerZoneHeight = 0.45f;
        [SerializeField] float engageSpeed = 3.5f;   // rudder units/s while steering
        [SerializeField] float recenterSpeed = 1.2f; // rudder units/s on release
        [SerializeField, Range(-1f, 1f)] float testRudder = 0f; // editor/testing override

        // The engine telegraph, for thumbs. One astern notch, because
        // backing a paddle wheel is a manoeuvre, not a way to travel.
        static readonly float[] Orders = { -1f, 0f, 0.35f, 0.7f, 1f };
        static readonly string[] OrderNames = { "full astern", "stop", "slow ahead", "half ahead", "full ahead" };
        const int StopOrder = 1;

        ShipMotor motor;
        float rudder;
        int order = StopOrder;

        void Awake() { motor = GetComponent<ShipMotor>(); }

        void Update()
        {
            float target = 0f;
            bool steering = false;
            float? drive = null;

            var pointer = Pointer.current;
            if (pointer != null && pointer.press.isPressed)
            {
                Vector2 p = pointer.position.ReadValue();
                if (p.y < Screen.height * steerZoneHeight && !UIBlocker.Blocked(p))
                {
                    // Slight overdrive (x2.2) so full rudder doesn't need the screen edge.
                    target = Mathf.Clamp((p.x / Screen.width - 0.5f) * 2.2f, -1f, 1f);
                    steering = true;
                }
            }

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) { target = -1f; steering = true; }
                else if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) { target = 1f; steering = true; }

                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) drive = 1f;
                else if (kb.sKey.isPressed || kb.downArrowKey.isPressed) drive = -1f;
                // R, not space — space already dismisses the voyage tally.
                if (kb.rKey.wasPressedThisFrame) motor.Rowing = !motor.Rowing;
            }

            if (!Mathf.Approximately(testRudder, 0f)) { target = testRudder; steering = true; }

            rudder = Mathf.MoveTowards(
                rudder, steering ? target : 0f,
                (steering ? engageSpeed : recenterSpeed) * Time.deltaTime);
            motor.Rudder = rudder;
            // A held key OVERRIDES the telegraph and hands it straight back
            // on release, so the two never fight: the keys are for driving,
            // the telegraph is for setting her going and leaving her there.
            // The engine's ramp in ShipMotor does the smoothing, and the
            // crew's condition sets how fast it ramps -- an order is still
            // only as good as whoever is below to answer it.
            motor.ThrottleOrder = drive ?? Orders[order];
        }

        /// What she is being asked to do RIGHT NOW, which is the held key if
        /// there is one -- the panel must never read "stop" while a finger on
        /// W has her making way.
        string Label()
        {
            float o = motor.ThrottleOrder;
            if (Mathf.Approximately(o, Orders[order])) return OrderNames[order];
            if (o > 0.5f) return "full ahead";
            if (o > 0.05f) return "ahead";
            if (o < -0.5f) return "full astern";
            if (o < -0.05f) return "astern";
            return "stop";
        }

        void StepOrder(int delta) => order = Mathf.Clamp(order + delta, 0, Orders.Length - 1);

        void OnGUI()
        {
            int u = UITheme.Unit;
            float pad = u * 0.7f;

            // --- Point of sail: the readout that teaches the whole system ---
            float panelW = u * 10f;
            float panelH = u * 4.6f;
            float px = Screen.width - panelW - pad;
            float py = Screen.height - panelH - pad;
            UITheme.Rect(new Rect(px, py, panelW, panelH), UITheme.Panel);

            // What the water is doing, and how much of it you're taking on the
            // bow. No point of sail, no no-go: this reads "the sea is heavy and
            // you're driving into it", which is the only heading cost left.
            float strain = motor.HeadSea01 * motor.SeaSeverity01;
            var nameColour = strain > 0.4f ? UITheme.Warn : UITheme.Text;
            // GUI.contentColor tints the text without building a style. A
            // per-frame `new GUIStyle` allocates AND invalidates IMGUI's
            // cached text mesh for everything drawn with it.
            var prevContent = GUI.contentColor;
            GUI.contentColor = nameColour;
            GUI.Label(new Rect(px, py + u * 0.2f, panelW, u * 1.6f),
                motor.SeaStateName, UITheme.Small2Centered);
            GUI.contentColor = prevContent;

            var effRect = new Rect(px + u * 0.6f, py + u * 1.9f, panelW - u * 1.2f, u * 0.5f);
            UITheme.Bar(effRect, motor.SeaResistance01, UITheme.Ramp(1f - motor.SeaResistance01));

            // --- Sail control ---
            float bw = (panelW - u * 1.8f) * 0.5f;
            var less = new Rect(px + u * 0.6f, py + u * 2.7f, bw, u * 1.6f);
            var more = new Rect(less.xMax + u * 0.6f, less.y, bw, u * 1.6f);
            UIBlocker.Block(less);
            UIBlocker.Block(more);
            if (GUI.Button(less, "▼", UITheme.Button)) StepOrder(-1);
            if (GUI.Button(more, "▲", UITheme.Button)) StepOrder(1);
            // The order is what you asked for; the bar under it is what the
            // crew have actually managed. When they're sick the two disagree,
            // and that gap IS the mechanic — it has to be on screen.
            GUI.Label(new Rect(px, py + u * 2.9f, panelW, u * 1.2f),
                motor.ThrottleMoving ? Label() + " …" : Label(),
                UITheme.Small2Centered);
            // Astern fills the same bar backwards from a centre mark, so
            // which way she is being driven reads without being read.
            var barRect = new Rect(px + u * 0.6f, py + u * 4.25f, panelW - u * 1.2f, u * 0.22f);
            float t = Mathf.Clamp(motor.Throttle, -1f, 1f);
            float mid = barRect.x + barRect.width * 0.32f;
            if (t >= 0f)
                UITheme.Bar(new Rect(mid, barRect.y, (barRect.xMax - mid) * t, barRect.height),
                    1f, motor.ThrottleMoving ? UITheme.Warn : UITheme.Sea);
            else
                UITheme.Bar(new Rect(mid + (mid - barRect.x) * t, barRect.y,
                    (mid - barRect.x) * -t, barRect.height), 1f, UITheme.Warn);

            // Oars: wind-independent, but pure labour — a crew at the rail
            // can't pull, so this stops being the guaranteed way home.
            float oars = motor.OarPower01;
            var row = new Rect(px, py - u * 2.1f, panelW, u * 1.8f);
            UIBlocker.Block(row);
            var rowStyle = motor.Rowing ? UITheme.ButtonPressed : UITheme.Button;
            string oarLabel = oars < 0.02f ? "—  no one at the oars"
                : motor.Rowing ? "◉  rowing" : "◎  man the oars";
            GUI.enabled = oars >= 0.02f;
            if (GUI.Button(row, oarLabel, rowStyle)) motor.Rowing = !motor.Rowing;
            GUI.enabled = true;
        }
    }
}
