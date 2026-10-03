namespace SeaSick.Ship
{
    /// **Live knobs for the boost's punch** (2026-10-03, DREDGE step 3,
    /// Kevin: "a little more punch in the boost"; docs/PLAN-dredge-controls.md
    /// §1.1 Haste). Plain static fields so the on-phone FeelLab can edit them
    /// by reflection on these exact names; every reader reads them EVERY
    /// frame, nothing is cached at Start.
    ///
    /// The punch fires on the boost's ENGAGE (`HelmInput.BoostEngagedAt`:
    /// `Boosting` going false -> true, after it has been off for at least
    /// `reengageGuardSeconds`). Boost itself stays free: none of this costs
    /// anything.
    ///
    /// Nausea budget (GDD §6, sailing smoothness): every MOVING camera term is
    /// short -- the FOV kick peaks in ~0.15 s and settles, the engage shake is
    /// gone in ~0.25 s -- and what lasts while boosting is a steady (not
    /// moving) wider lens plus a small, position-only rumble. `boostFovCapDeg`
    /// caps the lens term whatever the sliders say.
    public static class BoostTuning
    {
        /// Master multiplier on EVERY amount below (FOV, shake, rumble, surge,
        /// chug). 0 = no punch at all, 1 = as tuned. Range 0..2.
        public static float punch = 1f;
        /// Camera effects on/off (DREDGE's "Haste VFX" toggle): false keeps the
        /// lens, shake and rumble still; haptic, surge and sound stay.
        public static bool cameraFx = true;

        // --- lens (ChaseCamera, on top of the speed juice FOV) ---
        /// Degrees the lens opens at the engage peak. Range 0..12.
        public static float engageFovKickDeg = 6f;
        /// s to reach (~95% of) the kick. Range 0.05..0.5.
        public static float engageFovInSeconds = 0.15f;
        /// s for the kick to settle down to the sustain width. Range 0.1..2.
        public static float engageFovSettleSeconds = 0.5f;
        /// Degrees the lens stays open while boosting. Range 0..8.
        public static float sustainFovDeg = 3f;
        /// s for the lens to close again once boost ends. Range 0.1..2.
        public static float fovOutSeconds = 0.6f;
        /// Hard cap on the boost lens term, degrees, after `punch`. Range 2..15.
        public static float boostFovCapDeg = 10f;

        // --- shake (ChaseCamera.Shake, the kraken's scale: 1 = a hard slam) ---
        /// The engage jolt, in `ChaseCamera.Shake` units (it squares the
        /// amount and decays in ~0.25 s; 0.6 = ~0.2 m / ~0.4 deg peak).
        /// Range 0..1.2.
        public static float engageShake = 0.6f;
        /// The light sustained rumble while boosting, LINEAR in Shake's
        /// metres/degrees (0.12 = ~7 cm, ~0.1 deg pitch/yaw, no roll).
        /// Range 0..0.4.
        public static float rumbleAmp = 0.12f;
        /// Rumble noise rate, Hz-ish (the kraken shake runs at 23). Lower is
        /// a slower wobble -- keep it a buzz, not a sway. Range 2..20.
        public static float rumbleHz = 9f;
        /// s for the rumble to fade in and out. Range 0.05..1.
        public static float rumbleFadeSeconds = 0.25f;

        // --- the rest ---
        /// One haptic tick on engage (iOS device only; also gated on
        /// `JuiceTuning.overboardHapticsOn`, the native-haptics switch).
        public static bool hapticOnEngage = true;
        /// Extra forward push at the engage, m/s^2 (x mass). Never past the
        /// ordered (burn) speed: it fades out as she reaches it. Range 0..4.
        public static float surgeAccel = 1.5f;
        /// s the surge decays over (linear-to-zero envelope). Range 0.1..2.
        public static float surgeSeconds = 0.8f;
        /// Extra chug pitch at the engage, riding the surge envelope (the
        /// engine "chugs up"). Range 0..0.6.
        public static float engageChugPitch = 0.18f;
        /// s boost must have been off before it can fire the punch again, so
        /// feathering the stick doesn't machine-gun kicks. Range 0..2.
        public static float reengageGuardSeconds = 0.5f;
    }
}
