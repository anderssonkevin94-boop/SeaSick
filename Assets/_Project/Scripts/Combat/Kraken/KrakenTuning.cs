namespace SeaSick.Combat
{
    /// **The kraken's knobs, live** (GDD §6 "The Kraken", build step 1).
    ///
    /// Plain static fields so the feel lab writes them by reflection on these
    /// exact names (`FeelLab.TypeFullNames`), and `Kraken` / `KrakenArms` read
    /// them EVERY frame, never cache them -- a slider moved on the phone is
    /// seen on the next frame, the same contract as `JuiceTuning`.
    ///
    /// Step 1 is harmless: everything here is look and motion. Reach, windup,
    /// damage and the spawn odds arrive with steps 2-4, in this same class.
    /// All numbers: first pass at prefab scale 49 (~65 m across), tune by play.
    public static class KrakenTuning
    {
        // ------------------------------------------------------------ lifecycle
        /// Seconds from deep to surfaced. The arms finish rising with it.        1..12
        public static float riseSeconds = 4.5f;
        /// Seconds from surfaced back to deep, after which it is destroyed.      1..12
        public static float sinkSeconds = 5f;
        /// Seconds it idles at the surface before sinking on its own.            5..180
        public static float surfacedSeconds = 45f;
        /// How far under its surfaced height it starts and ends, metres. Deep
        /// enough that the raised arm tips (~29 m over the root) are under too. 15..60
        public static float sinkDepth = 34f;
        /// The root's height against the sampled sea, metres. -1.75 is where
        /// the approved P1 preview stood: head and towering arms out, the
        /// trailing arm roots just under, no slivers of arm hovering.        -6..3
        public static float waterlineOffset = -1.75f;
        /// How far off the ship it surfaces, metres. Outside the ~40 m arm
        /// reach, so nothing of it ever lands on the hull.                    45..120
        public static float spawnDistance = 55f;
        /// How fast it turns to keep facing the ship, degrees a second.         0..30
        public static float turnDegPerSec = 6f;

        // ------------------------------------------------------------- the arms
        /// Peak curl of the travelling wave, degrees per bone. Always held
        /// inside the rig's 50 deg/bone budget on top of the pose's own curl.   0..25
        public static float swayAmplitudeDeg = 7f;
        /// Waves per second running base to tip.                                0.05..1
        public static float swaySpeed = 0.22f;
        /// Length of one wave, in bones (an arm is 10).                         3..30
        public static float swayWavelength = 11f;
        /// Side sway about the bone's local Z, degrees per bone.                0..15
        public static float sideSwayDeg = 3f;
        /// The mantle's slow breathing, degrees.                                0..6
        public static float breathDeg = 1.6f;

        // ---------------------------------------------------------- water marks
        /// The breach's one big splash in the ripple sim: radius m, strength.
        public static float breachSplashRadius = 24f;
        public static float breachSplashStrength = 3.2f;
        /// Radius of each foam stamp where an arm cuts the water, metres.       1..12
        public static float foamRadius = 4.5f;
        /// Foam laid per second at each of those stamps.                        0..4
        public static float foam = 1.2f;

        // ---------------------------------------------------------------- camera
        /// Widen the chase camera to frame ship and kraken while it is up.
        /// Never overrides a combat lock or a shore party's framing.
        public static bool frameCamera = true;
    }
}
