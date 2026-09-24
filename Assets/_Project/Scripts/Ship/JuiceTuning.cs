using UnityEngine;

namespace SeaSick.Ship
{
    /// **The speed and turn feedback knobs, live.**
    ///
    /// Plain static fields so the helm feel lab can write them by reflection
    /// on these exact names, and every consumer (`ChaseCamera`, `SpeedJuice`,
    /// `SurfaceWake`, `PaddleSound`) reads them EVERY frame, never caches them
    /// at Start -- a slider moved on the phone is felt on the next frame.
    ///
    /// None of this changes how the ship moves. It is the feedback half: what
    /// the screen, the speaker and the hand are told about what she is doing.
    /// **Defaults = Kevin's FeelLab tuning, 2026-09-24** (phone, SAVE+LOG JSON pasted in chat after
    /// "sailing feels a lot better"): values he changed are baked here; the
    /// FeelLab still loads his saved PlayerPrefs over them on his phone.
    public static class JuiceTuning
    {
        /// Extra vertical FOV at top speed, degrees.                 0..25
        public static float camFovBoostDeg = 9.8f;
        /// The chase camera sinks this far toward the water at top speed. 0..5
        public static float camDropMeters = 1.07f;
        /// Camera roll (deg) per deg/s of yaw rate, INTO the turn.     0..0.5
        public static float camLeanPerYawDeg = 0.028f;
        /// Smoothing time on the three camera effects above, seconds. 0 = instant. 0..1
        public static float camLagSeconds = 1.0f;
        /// Bow and turn spray intensity multiplier.                  0..3
        public static float sprayScale = 3.0f;
        /// Surface wake intensity multiplier.                        0..3
        public static float wakeScale = 0.65f;
        /// Paddle/engine pitch rises by this fraction at top speed.  0..1
        public static float soundPitchRange = 0.45f;
        /// Telegraph notch / burn haptics on iOS and Android.
        public static bool  hapticsOn         = false;  // OFF 2026-09-24: Handheld.Vibrate at launch deadlocked iOS 26's audio daemon (audiomxd XPC timeout, phone froze); a Taptic plugin later
        public static bool  soundOn           = true;   // ON by Kevin's FeelLab JSON 2026-09-24 (haptics stay off: the launch freeze was Handheld.Vibrate); read once when SpeedJuice sets up, so a live toggle needs a relaunch

        // ---------------------------------------------------------- signals
        //
        // The two numbers every consumer keys off, worked out in ONE place so
        // the camera, the spray, the wake and the engine note can never
        // disagree about how fast she is going or how hard she is turning.

        static ShipMotor paddleFor;
        static SeaSick.Steamer.PaddleDrive paddle;
        static float nextPaddleLook;

        /// Her top speed, m/s. The paddle steamer's is the wheel's
        /// (`PaddleDrive.TopSpeed`, Froude-scaled by the bootstrap); every
        /// other hull's is the motor's. The motor's number is a generic
        /// length-scaled estimate and reads the steamer ~10% slow.
        public static float TopSpeed(ShipMotor motor)
        {
            if (motor == null) return 1f;
            if (paddleFor != motor || (paddle == null && Time.unscaledTime >= nextPaddleLook))
            {
                paddleFor = motor;
                paddle = motor.GetComponent<SeaSick.Steamer.PaddleDrive>();
                // The bootstrap may add the drive after the first frame; look
                // again now and then rather than every frame.
                nextPaddleLook = Time.unscaledTime + 1f;
            }
            float top = paddle != null && paddle.isActiveAndEnabled ? paddle.TopSpeed : motor.MaxSpeed;
            return Mathf.Max(0.5f, top);
        }

        /// 0 at rest, 1 at top speed. Clamped: the burn tier and surfing do
        /// not push the effects past what top speed shows.
        public static float Speed01(ShipMotor motor)
        {
            if (motor == null) return 0f;
            return Mathf.Clamp01(motor.CurrentSpeed / TopSpeed(motor));
        }

        /// Yaw rate, deg/s, +ve to starboard. From the rigidbody when she has
        /// one; 0 otherwise.
        public static float YawRateDeg(Rigidbody rb)
        {
            if (rb == null) return 0f;
            return Vector3.Dot(rb.angularVelocity, Vector3.up) * Mathf.Rad2Deg;
        }

        /// |yaw rate| as a fraction of her rated peak turn rate. The reference
        /// is floored so a hull with a tiny rated rate cannot read every
        /// wave-induced wobble as a hard turn.
        public static float Turn01(ShipMotor motor, float yawRateDeg)
        {
            float peak = motor != null ? Mathf.Max(8f, motor.MaxTurnRate) : 15f;
            return Mathf.Clamp01(Mathf.Abs(yawRateDeg) / peak);
        }

        /// Exponential ease with a time constant in seconds; 0 = instant.
        public static float Ease(float current, float target, float tauSeconds, float dt)
        {
            if (tauSeconds <= 1e-4f) return target;
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-dt / tauSeconds));
        }
    }
}
