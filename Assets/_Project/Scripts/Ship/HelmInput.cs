using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.Ship
{
    /// One-thumb helm: hold anywhere in the lower steering zone; horizontal
    /// position of the thumb maps to absolute tiller position. Release and the
    /// rudder eases back to center. Keyboard A/D works in the editor.
    [RequireComponent(typeof(ShipMotor))]
    public class HelmInput : MonoBehaviour
    {
        [SerializeField, Range(0.1f, 1f)] float steerZoneHeight = 0.45f;
        [SerializeField] float engageSpeed = 3.5f;   // rudder units/s while steering
        [SerializeField] float recenterSpeed = 1.2f; // rudder units/s on release
        [SerializeField, Range(-1f, 1f)] float testRudder = 0f; // editor/testing override

        ShipMotor motor;
        float rudder;

        void Awake() { motor = GetComponent<ShipMotor>(); }

        void Update()
        {
            float target = 0f;
            bool steering = false;

            var pointer = Pointer.current;
            if (pointer != null && pointer.press.isPressed)
            {
                Vector2 p = pointer.position.ReadValue();
                if (p.y < Screen.height * steerZoneHeight)
                {
                    // Slight overdrive (x2.2) so full rudder doesn't need the screen edge.
                    target = Mathf.Clamp((p.x / Screen.width - 0.5f) * 2.2f, -1f, 1f);
                    steering = true;
                }
            }

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.isPressed) { target = -1f; steering = true; }
                else if (kb.dKey.isPressed) { target = 1f; steering = true; }
            }

            if (!Mathf.Approximately(testRudder, 0f)) { target = testRudder; steering = true; }

            rudder = Mathf.MoveTowards(
                rudder, steering ? target : 0f,
                (steering ? engageSpeed : recenterSpeed) * Time.deltaTime);
            motor.Rudder = rudder;
        }
    }
}
