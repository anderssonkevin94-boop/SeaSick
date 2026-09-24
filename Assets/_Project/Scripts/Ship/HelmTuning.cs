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
        public static float rudderMoveSpeed = 3.5f;
        /// Rudder units/s it springs back to midships at once the thumb lifts. Range 0.5..8.
        public static float rudderReturnPerSec = 2.5f;
        /// Stick Y, in ring radii from the touch-down point, ignored before the throttle moves. Range 0..0.3.
        public static float throttleDeadZone = 0.10f;
        /// A tap (no drag) rings the telegraph to stop. Applies to both modes.
        public static bool tapStops = true;
    }
}
