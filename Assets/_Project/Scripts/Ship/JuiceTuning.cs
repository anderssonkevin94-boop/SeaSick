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
    /// "sailing feels a lot better"): values he changed were baked here; the
    /// FeelLab still loads his saved PlayerPrefs over them on his phone.
    /// **2026-09-29 calm-down:** camFovBoostDeg, camDropMeters and the coaster
    /// camera's turn orbit / yaw follow were turned down (Kevin: "too rough
    /// around the edges and almost too much movement"), so those no longer
    /// match his 09-24 bake; his saved PlayerPrefs still win on his phone.
    public static class JuiceTuning
    {
        /// Extra vertical FOV at top speed, degrees.                 0..25
        /// (2026-09-29: 9.8 -> 4; the speed dolly is the one speed effect now.)
        public static float camFovBoostDeg = 4f;
        /// The chase camera sinks this far toward the water at top speed. 0..5
        /// (2026-09-29: 1.07 -> 0, off; it added to the sense of movement.)
        public static float camDropMeters = 0f;
        /// Far end of the speed dolly (seat multiplier at the burn notch, upright). 1..1.5
        /// The near end (lying still) stays ChaseCamera.portraitStoppedPull.
        public static float camDollyMax = 1.15f;
        /// Coaster sailing camera: yaw follow time constant, seconds. 0 = locked on her. 0..1.5
        /// Higher = the world pans less in a turn and she drifts off centre.
        public static float camYawLagSeconds = 0.45f;
        /// Coaster sailing camera: most the aim may trail the direct-at-ship yaw, degrees. 0..20
        /// Portrait half-FOV is ~17-20, so 12 keeps her on screen.
        public static float camMaxOffCentreDeg = 12f;
        /// Coaster sailing camera: side swing of the seat in a sustained turn, degrees. 0..30
        /// (2026-09-29: was a hard-coded 25 in SailingTurnOrbit.) The seat goes to the
        /// OUTSIDE of the turn, which turns the view (through her) toward the inside.
        public static float camTurnOrbitDeg = 8f;
        /// **Sailing camera V2 (2026-10-02)**, Kevin: "when I turn I'm often
        /// turning blind" and "when I engage the enemy I have a real hard time
        /// controlling the ship". Off = the camera exactly as it was before,
        /// for an A/B on the phone. The knobs below only act while it is on.
        public static bool camStyleV2 = true;
        /// Upright cruise pitch, degrees down to the look point (was an implicit ~15.7
        /// on the coaster). Capped so the horizon stays under 84% of the screen. 10..26
        public static float camPitchDeg = 20f;
        /// Extra pitch when she needs to see (slow, hard turn, raider or land near). 0..12
        public static float camRisePitchDeg = 6f;
        /// ...and how much further back the seat sits then, as a fraction. 0..0.6
        public static float camRiseBackFraction = 0.2f;
        /// Time constant of that rise, seconds; it settles back 2.5x slower so a
        /// chattering helm cannot pump the camera up and down.             0.1..2
        public static float camRiseSeconds = 0.5f;
        /// The aim leans this far INTO a turn, degrees, keyed mostly off the rudder
        /// so it moves before she does.                                    0..20
        public static float camTurnLeadDeg = 12f;
        /// Smoothing on that lean, seconds.                                 0..1.5
        public static float camLeadSeconds = 0.5f;
        /// Locked on: most the seat may swing off dead astern, degrees. Under 90, so her
        /// bow never points at the lens and the stick never reads mirrored; 30 lost a
        /// raider on the beam off a portrait frame (smoke test 2026-10-02).  0..85
        public static float camLockSwingDeg = 70f;
        /// Locked on: farthest astern the seat backs off to hold both ships, metres. 25..80
        public static float camLockMaxBack = 45f;
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
        /// Man-overboard haptics (phase 5a, `Ship.Haptics`), through the
        /// native `SeaSickHaptics.mm` plugin -- NOT `Handheld.Vibrate`, so
        /// this is safe to leave on despite `hapticsOn` staying off above.
        public static bool  overboardHapticsOn = true;

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
