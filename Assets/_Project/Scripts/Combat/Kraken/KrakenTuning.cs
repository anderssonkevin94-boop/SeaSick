namespace SeaSick.Combat
{
    /// **The kraken's knobs, live** (GDD §6 "The Kraken", build step 1).
    ///
    /// Plain static fields so the feel lab writes them by reflection on these
    /// exact names (`FeelLab.TypeFullNames`), and `Kraken` / `KrakenArms` read
    /// them EVERY frame, never cache them -- a slider moved on the phone is
    /// seen on the next frame, the same contract as `JuiceTuning`.
    ///
    /// Step 1 is look and motion; step 2 (the swat) adds reach, windup, ring,
    /// damage. Health and the spawn odds arrive with steps 3-4, in this class.
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
        /// Where the ship sits on the screen while framed, fraction of the
        /// height from the bottom (the lower third).                            0.15..0.5
        public static float camShip01 = 0.27f;
        /// The same on a wide (desktop) screen, where the combat row sits in
        /// the bottom centre: 0.27 put her stern behind the Lock button
        /// (step-3 desktop capture).                                            0.15..0.6
        public static float camShip01Desk = 0.40f;
        /// Highest the kraken's top may reach, fraction from the bottom, when
        /// the HUD's top bar is not up to measure against (it is read live
        /// when it is).                                                          0.6..0.95
        public static float camTop01 = 0.84f;
        /// The seat's height seen from the ship, degrees over the horizon.      8..45
        public static float camElevDeg = 18f;
        /// Most the seat swings off her stern toward the kraken, degrees, so
        /// left on the stick stays left on the screen.                         0..120
        public static float camSwingDeg = 65f;
        /// Farthest the seat backs off to fit it all, metres.                   60..250
        public static float camMaxBack = 150f;
        /// Camera shake on a hit / a near miss / any slam the ship can feel.    0..1.5
        public static float shakeHit = 0.9f;
        public static float shakeMiss = 0.35f;

        // ------------------------------------------------------- the swat (step 2)
        /// Metres from the kraken's body inside which it attacks.             20..60
        public static float reach = 40f;
        /// Mean seconds between swats (each start jittered +-25 %).          2..12
        public static float swatInterval = 5f;
        /// Seconds the arm coils over the ring before it slams: the tell.
        /// 2.6 (+ the 0.32 slam = 2.9 s from ring to impact) measured
        /// dodgeable from a stop (burn), half and full ahead (hard over) with
        /// the 9 m bootstrap ship, and never by holding course
        /// (`KrakenDodgeProbe`, 2026-10-03).                                  1..4
        public static float windupSeconds = 2.6f;
        /// Seconds of the slam itself, coil to water.                          0.15..1
        public static float slamSeconds = 0.32f;
        /// Radius of the foam ring the arm lands on, metres. A hit is the
        /// ring's edge touching her hull footprint, so this is the margin she
        /// must clear by.                                                     2..14
        public static float ringRadius = 4.5f;
        /// How much of the ship's velocity the ring leads by: 1 = exactly
        /// where she will be at the slam if she holds course and speed.        0..1.5
        public static float ringLead = 1f;
        /// Seconds into the fight after which two arms may come at once.       0..120
        public static float twoArmAfter = 30f;
        /// Chance per swat of a second arm once that time has passed.          0..1
        public static float twoArmChance = 0.35f;
        /// How fast it drifts toward the ship while fighting, m/s (her half
        /// ahead is ~4-5 m/s), and how close it comes.                         0..4
        public static float driftSpeed = 1.2f;
        public static float driftStopAt = 24f;

        /// Hits that take a fresh hull to its floor (integrity 0 = crippled:
        /// x0.60 top speed, full wallow; nothing sinks her). Each hit costs
        /// 1/hitsToCripple of the hull.                                        1..8
        public static float hitsToCripple = 3f;
        /// Knockdown on a hit: degrees of heel and seconds the sea takes to
        /// right her.                                                          0..40
        public static float knockdownDeg = 22f;
        public static float knockdownSeconds = 2.6f;
        /// Closing speed handed to the overboard roll on a hit, m/s (the
        /// overboard table's own min..full speeds turn it into a chance;
        /// 0 = nobody goes over).                                              0..12
        public static float overboardSpeed = 7f;
        /// Permanent crew sickness jolt on a hit (HullIntegrity's shot is 0.05). 0..0.2
        public static float crewJolt = 0.06f;
        /// Roll kick a near miss gives her, deg/s, fading with distance out to
        /// `missRockRange` metres from the ring's edge.                       0..40
        public static float missRoll = 14f;
        public static float missRockRange = 18f;
        // ------------------------------------------------- fight back (step 3)
        /// Body hits (one cannonball = 1) that drive it off.                   3..40
        public static float hitPoints = 12f;
        /// What a hit on a raised arm costs it, as a share of a body hit (the
        /// arm hit's real reward is the cancelled swat).                       0..1
        public static float armHitDamage = 0.35f;
        /// The head's hit sphere, metres (the mantle is ~16 m tall).           4..16
        public static float bodyHitRadius = 9f;
        /// A raised arm's hit capsule radius, metres.                          1..6
        public static float armHitRadius = 3f;
        /// Clear water that counts as escaped, metres from its body, held for
        /// `escapeSeconds`; then it sinks away with no loot.                  60..250
        public static float escapeDistance = 120f;
        public static float escapeSeconds = 4f;
        /// Safety: a fight still going after this many seconds ends as an
        /// escape (0 = never).                                                 0..900
        public static float maxFightSeconds = 300f;

        /// The ripple sim splash at the slam: radius m, strength.
        public static float slamSplashRadius = 9f;
        public static float slamSplashStrength = 2.4f;
    }
}
