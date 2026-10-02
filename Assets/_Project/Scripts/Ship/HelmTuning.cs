namespace SeaSick.Ship
{
    /// **Live knobs for the one-thumb helm.** Plain static fields so the
    /// on-phone tuning lab can edit them by reflection on these exact names,
    /// and `HelmInput` reads every one of them EVERY frame — nothing is
    /// cached at Start, so a slider moved on the phone is felt on the next
    /// frame.
    ///
    /// `directRudder` picks the mode of the floating stick:
    /// - true: stick X is the RUDDER (follows the thumb, springs back to
    ///   midships when it lifts) and stick Y is a LATCHED throttle lever
    ///   (drag up = more, drag down = less, the value set stays on release).
    /// - false: today's heading autopilot — the drag direction is a world
    ///   heading she holds after release, the drag distance is the throttle.
    public static class HelmTuning
    {
        /// true: stick X = rudder, stick Y = latched throttle. false: today's heading autopilot.
        public static bool directRudder = true;
        /// Rudder (0..1 of full) at stick X = one ring radius. Above 1 reaches full rudder inside the rim. Range 0.3..1.5.
        public static float rudderPerRim = 1.0f;
        /// Exponent on |stickX|: above 1 = finer near centre. Range 0.5..3.
        public static float rudderCurve = 1.4f;
        /// Rudder units/s the blade follows the thumb at. Range 1..12.
        /// 2026-10-02: 3.5 -> 5 (full helm in 0.2 s).
        public static float rudderMoveSpeed = 5f;
        /// Rudder units/s it springs back to midships at once the thumb lifts. Range 0.5..15.
        /// 2026-10-02: 2.5 -> 8 (midships from hard over in 0.125 s): the
        /// turn stops when the thumb lifts, `HandlingTuning.yawReleaseBrake`
        /// does the rest.
        public static float rudderReturnPerSec = 8f;
        /// Stick Y, in ring radii from the touch-down point, ignored before the throttle moves. Range 0..0.3.
        public static float throttleDeadZone = 0.10f;
        /// A tap (no drag) rings the telegraph to stop. Applies to both modes.
        public static bool tapStops = true;

        // --- heading hold (2026-10-02, direct mode; see `HeadingHold`) ---
        /// Once the helm is centred and the turn has died, hold that heading
        /// against waves and heel. Any rudder lets go; a stop tap does not.
        public static bool headingHold = true;
        /// Hold's yaw rate per degree off, deg/s per deg (1/s). ~1 is
        /// critically damped through the yaw lag; above ~1.5 it can overshoot.
        /// Range 0.1..2.
        public static float holdGain = 0.7f;
        /// Most yaw rate the hold ever asks for, deg/s: it nudges, it never
        /// throws her round. Range 0.5..10.
        public static float holdMaxDegPerSec = 4f;
        /// Yaw rate, deg/s, under which the dying turn counts as over and the
        /// heading is captured. Range 0.2..5.
        public static float holdCaptureDegPerSec = 1.5f;
        /// Blade (0..1 of full) beyond which the helm is steering and the
        /// hold lets go. 0.05 is a thumb ~0.12 ring radii off centre, above a
        /// tap's 0.10 dead zone, so a stop tap never breaks the hold.
        public const float HoldBreakRudder = 0.05f;
        /// s after the helm centres that the heading is captured even if the
        /// sea never lets the turn settle under `holdCaptureDegPerSec`.
        public const float HoldCaptureMaxSeconds = 1f;
        /// deg off the held heading past which the hold takes the new
        /// heading instead of hauling her back (a ram, a broach).
        public const float HoldLetGoDeg = 30f;
    }
}
