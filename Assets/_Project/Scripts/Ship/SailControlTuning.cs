namespace SeaSick.Ship
{
    /// **Live knobs for the DREDGE-style sea stick** (2026-10-03,
    /// docs/PLAN-dredge-controls.md phase 1). Plain static fields so the
    /// on-phone tuning lab can edit them by reflection on these exact names;
    /// `HelmInput` and `SeaStick` read every one EVERY frame, nothing is
    /// cached at Start.
    ///
    /// The stick is boat-relative: Y = speed (up ahead, down astern), X =
    /// turn. Nothing latches: let go and both orders are zero.
    public static class SailControlTuning
    {
        // --- the gesture (SeaStick) ---
        /// Top of the boat zone, fraction of screen height from the BOTTOM.
        /// A touch that begins above it never starts the stick (the upper
        /// part is the camera's, phase 2). Range 0.3..0.8.
        public static float zoneTopFrac = 0.55f;
        /// Ring radius, fraction of the screen's shorter side: one radius of
        /// push = full order. Range 0.08..0.25.
        public static float ringRadiusFrac = 0.09f;
        /// Each axis, fraction of the ring radius, ignored before it orders
        /// anything (a thumb resting still sails nowhere). Range 0..0.3.
        public static float deadZone = 0.10f;
        /// s a lifted thumb keeps the stick: a touch back down within this
        /// (near the same spot) carries on the same stick. Range 0..0.3.
        public static float liftGraceSeconds = 0.10f;
        /// "Near the same spot" for the lift grace, in ring radii from where
        /// the thumb lifted. Range 0.3..2.
        public static float liftGraceRadius = 1.0f;

        // --- what the stick means (HelmInput) ---
        /// Exponent on the turn axis: above 1 = finer near centre. Range 0.5..3.
        public static float turnCurve = 1.15f;
        /// Exponent on the speed axis: 1 = speed straight in proportion to
        /// the push (DREDGE). Range 0.5..3.
        public static float throttleCurve = 1.0f;
        /// Full astern as a share of full ahead's order: she backs slowly.
        /// Range 0.1..1.
        public static float reverseCap = 0.5f;
        /// Rudder units/s the blade follows the stick at, both ways: takes
        /// the jitter out of a thumb without making her lazy. Range 1..20.
        public static float rudderSlewPerSec = 6f;
        /// s for a held W/S/A/D to ramp from nothing to full (and back on
        /// release), so the keys read analog too. Range 0..1.
        public static float keyRampSeconds = 0.25f;

        // --- boost (the Haste-style button, Shift at a desk) ---
        /// Boost disarms after this long with no push ahead... Range 0.5..5.
        public static float boostIdleSeconds = 1.5f;
        /// ...once she is also slower than this, m/s (near stopped; a swell
        /// alone drifts her up to ~1.5).
        /// Range 0..3.
        public static float boostStopSpeed = 1.5f;

        // --- steer toward (man overboard) ---
        /// Throttle the swimmer steer sails at while the player isn't
        /// pushing (release no longer holds a speed). Range 0.1..1.
        public static float steerTowardThrottle = 0.5f;
        /// ...easing down to this inside `steerTowardSlowHulls` hull
        /// lengths, so she comes up on them slowly. Range 0.05..1.
        public static float steerTowardSlowThrottle = 0.25f;
        /// Hull lengths out at which the steer starts easing off. Range 0.5..6.
        public static float steerTowardSlowHulls = 2.5f;
    }
}
