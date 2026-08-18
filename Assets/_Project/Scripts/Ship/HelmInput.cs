using SeaSick.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.Ship
{
    /// One-thumb helm: hold anywhere in the lower steering zone; horizontal
    /// position of the thumb maps to absolute tiller position. Release and the
    /// rudder eases back to center. Sail is set in three readable steps —
    /// furled, half, full — which reads at a glance and suits a thumb.
    [RequireComponent(typeof(ShipMotor))]
    public class HelmInput : MonoBehaviour
    {
        [SerializeField, Range(0.1f, 1f)] float steerZoneHeight = 0.45f;
        [SerializeField] float engageSpeed = 3.5f;   // rudder units/s while steering
        [SerializeField] float recenterSpeed = 1.2f; // rudder units/s on release
        [SerializeField, Range(-1f, 1f)] float testRudder = 0f; // editor/testing override

        static readonly float[] SailSteps = { 0f, 0.5f, 1f };
        static readonly string[] SailNames = { "furled", "half", "full" };

        ShipMotor motor;
        float rudder;
        int sailStep = 2;

        void Awake() { motor = GetComponent<ShipMotor>(); }

        void Update()
        {
            float target = 0f;
            bool steering = false;

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

                if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) StepSail(1);
                else if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) StepSail(-1);
                // R, not space — space already dismisses the voyage tally.
                if (kb.rKey.wasPressedThisFrame) motor.Rowing = !motor.Rowing;
            }

            if (!Mathf.Approximately(testRudder, 0f)) { target = testRudder; steering = true; }

            rudder = Mathf.MoveTowards(
                rudder, steering ? target : 0f,
                (steering ? engageSpeed : recenterSpeed) * Time.deltaTime);
            motor.Rudder = rudder;
            motor.SailSetting = SailSteps[sailStep];
        }

        void StepSail(int delta) => sailStep = Mathf.Clamp(sailStep + delta, 0, SailSteps.Length - 1);

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

            bool bad = motor.WindAngleDeg < ShipMotor.NoGoDegrees;
            var nameColour = bad ? UITheme.Bad : UITheme.Text;
            var nameStyle = new GUIStyle(UITheme.Small2Centered) { normal = { textColor = nameColour } };
            GUI.Label(new Rect(px, py + u * 0.2f, panelW, u * 1.6f), motor.PointOfSailName, nameStyle);

            // Efficiency of the current heading — this is what tacking chases.
            var effRect = new Rect(px + u * 0.6f, py + u * 1.9f, panelW - u * 1.2f, u * 0.5f);
            UITheme.Bar(effRect, motor.PolarEfficiency,
                bad ? UITheme.Bad : UITheme.Ramp(1f - motor.PolarEfficiency));

            // --- Sail control ---
            float bw = (panelW - u * 1.8f) * 0.5f;
            var less = new Rect(px + u * 0.6f, py + u * 2.7f, bw, u * 1.6f);
            var more = new Rect(less.xMax + u * 0.6f, less.y, bw, u * 1.6f);
            UIBlocker.Block(less);
            UIBlocker.Block(more);
            if (GUI.Button(less, "▼", UITheme.Button)) StepSail(-1);
            if (GUI.Button(more, "▲", UITheme.Button)) StepSail(1);
            GUI.Label(new Rect(px, py + u * 2.9f, panelW, u * 1.2f),
                SailNames[sailStep], UITheme.Small2Centered);

            // Oars: always available, always works, wears the crew out.
            var row = new Rect(px, py - u * 2.1f, panelW, u * 1.8f);
            UIBlocker.Block(row);
            var rowStyle = new GUIStyle(UITheme.Button);
            if (motor.Rowing) rowStyle.normal = rowStyle.active;
            if (GUI.Button(row, motor.Rowing ? "◉  rowing" : "◎  man the oars", rowStyle))
                motor.Rowing = !motor.Rowing;
        }
    }
}
