namespace SeaSick.Ship
{
    /// **Live knobs for the heading hold** (`HeadingHold`). Plain static
    /// fields so the on-phone tuning lab can edit them by reflection on these
    /// exact names; read every frame, nothing is cached at Start.
    ///
    /// The stick's own knobs (zone, dead zone, curves, boost) moved to
    /// `SailControlTuning` with the DREDGE-style helm (2026-10-03); the hold
    /// is kept as a pure physics stabiliser for Kevin to judge in play
    /// (docs/PLAN-dredge-controls.md §3.4).
    public static class HelmTuning
    {
        /// Once the helm is centred and the turn has died, hold that heading
        /// against waves and heel. Any turn input lets go.
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
        /// hold lets go.
        public const float HoldBreakRudder = 0.05f;
        /// s after the helm centres that the heading is captured even if the
        /// sea never lets the turn settle under `holdCaptureDegPerSec`.
        public const float HoldCaptureMaxSeconds = 1f;
        /// deg off the held heading past which the hold takes the new
        /// heading instead of hauling her back (a ram, a broach).
        public const float HoldLetGoDeg = 30f;
    }
}
