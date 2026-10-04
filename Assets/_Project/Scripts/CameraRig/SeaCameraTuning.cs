namespace SeaSick.CameraRig
{
    /// **Live knobs for the DREDGE-style sea camera** (2026-10-03,
    /// docs/PLAN-dredge-controls.md phase 2). Plain static fields so the
    /// on-phone tuning lab can edit them by reflection on these exact names;
    /// `SeaCameraInput` and `ChaseCamera` read every one EVERY frame, nothing
    /// is cached at Start.
    ///
    /// The look is RATE control, like DREDGE's right stick: the stick (or the
    /// mouse, turned into stick tilt) sets how fast the view turns, never
    /// where it points, so it can't be flicked round. The offset it builds
    /// stays until a recenter.
    public static class SeaCameraTuning
    {
        // --- the camera stick (upper zone, touch) ---
        /// Full-tilt yaw rate, degrees/s, before `SeaCameraPrefs.Sensitivity`.
        /// Range 30..300.
        public static float maxYawDegPerSec = 120f;
        /// Full-tilt pitch rate, degrees/s, before sensitivity. Range 15..180.
        public static float maxPitchDegPerSec = 60f;
        /// Each axis, fraction of the ring radius, ignored before it turns
        /// anything (a resting thumb, or a tap, looks nowhere). Range 0..0.3.
        public static float deadZone = 0.10f;
        /// Ring radius, fraction of the screen's shorter side: one radius of
        /// push = full rate. Range 0.06..0.25.
        public static float ringRadiusFrac = 0.12f;

        // --- the mouse (desktop) ---
        /// Degrees per pixel of mouse drag, before sensitivity; capped at the
        /// full-tilt rates above, so a fling turns no faster than the stick.
        /// Range 0.05..1.
        public static float mouseDegPerPixel = 0.25f;

        // --- the offset ---
        /// Lowest the player may tilt the view, degrees off the tuned pitch
        /// (negative = flatter, toward the horizon). Never under the water:
        /// the seat's elevation is floored at a few degrees whatever this
        /// says. Range -30..0.
        public static float pitchMinDeg = -12f;
        /// Highest, degrees off the tuned pitch (positive = looking further
        /// down on her). Never past vertical: capped at 80 degrees of
        /// elevation. Range 0..50.
        public static float pitchMaxDeg = 30f;
        /// How far the camera backs off as the view tilts down toward her
        /// (Kevin 2026-10-04, like DREDGE: looking straight down zooms out a
        /// bit): the seat's distance multiplier at `pitchMaxDeg`, 1 = none.
        /// Range 1..2.
        public static float topDownZoom = 1.4f;
        /// Shape of that zoom over the tilt: 1 = even, above 1 = it grows
        /// mostly in the last part of the tilt. Range 0.5..3.
        public static float topDownZoomCurve = 1.5f;
        /// s for a recenter to ease both offsets home (and, detached, the
        /// base back behind her). Range 0.1..1.5.
        public static float recenterSeconds = 0.4f;
        /// s of follow lag on the look offset itself: the drawn offset
        /// springs after the one the input built, so a stick release doesn't
        /// stop the view dead. 0 = none. The heading-follow lag stays the V2
        /// yaw spring's (`JuiceTuning.camYawLagSeconds`). Range 0..0.5.
        public static float followSeconds = 0.12f;
        /// s between the two taps of a recenter double-tap. Range 0.15..0.5.
        public static float doubleTapSeconds = 0.3f;
    }
}
