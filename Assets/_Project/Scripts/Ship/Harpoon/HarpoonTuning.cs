namespace SeaSick.Ship.Harpoon
{
    /// **Live knobs for the bow harpoon** (docs/PLAN-harpoon.md, phase 1).
    /// Plain static fields so the on-phone FeelLab edits them by reflection
    /// on these exact names; `HarpoonGun` and `HarpoonLine` read them every
    /// frame, nothing is cached.
    ///
    /// Line units: force is in "load units" (1 = what it takes to drag a
    /// crate, mass 1, through the water at 1 m/s), so the same numbers hold
    /// for a bottle and a heavy load.
    public static class HarpoonTuning
    {
        // --- reach (Kevin 2026-10-04: 90° total, 35 m at L1) ---
        /// m from the bow a target may be. Range 10..60.
        public static float range = 35f;
        /// Degrees either side of the bow. Range 15..90.
        public static float arcHalfDeg = 45f;

        // --- the shot ---
        /// s from the tap to the barb leaving, at work rate 1 (the captain
        /// is slower: this ÷ his work rate). Range 0..1.5.
        public static float windupSeconds = 0.35f;
        /// m/s the barb flies at; the lead is solved for it. Range 15..90.
        public static float barbSpeed = 45f;
        /// m of rise at the middle of a full-range shot (shorter shots arc
        /// less). Range 0..6.
        public static float barbArcMetres = 1.6f;
        /// m the barb may land from the hook point and still bite. Range 0.5..4.
        public static float biteRadius = 1.5f;
        /// m of lead error at accuracy 0, scaled by (1 − accuracy). Range 0..6.
        public static float leadErrorMetres = 3f;
        /// s to reel an empty line back after a miss (no reload). Range 0.5..5.
        public static float missReelSeconds = 2f;
        /// s the gun is down after a snap or a cut (Kevin 2026-10-04: 5 s).
        /// Range 1..15.
        public static float reloadSeconds = 5f;

        // --- the winch (spring-damper on the line, never a step) ---
        /// m/s the winch hauls at, work rate 1, mass 1 (÷ √mass). Range 0.5..8.
        public static float reelSpeed = 3f;
        /// s for the winch to come up to (or down from) its speed. Range 0.05..2.
        public static float reelEaseSeconds = 0.5f;
        /// Line stiffness, load units per metre of stretch. Range 2..40.
        public static float reelSpring = 8f;
        /// Damping as a share of critical (1 = no bounce). Range 0.2..2.
        public static float reelDamper = 1f;
        /// Water drag on the load, per unit mass, per m/s. Range 0.2..4.
        public static float waterDrag = 1f;
        /// m from the bow at which the load is at the rail and comes aboard.
        /// Range 0.5..5.
        public static float railMetres = 2f;

        // --- tension ---
        /// Line force (load units) that reads as full strain, 1.0. Range 3..30.
        public static float snapForce = 10f;
        /// Tension01 from which the winch stalls (fully at strainBand): you
        /// ease off, it takes up again. Range 0.3..0.95.
        public static float winchStallStart = 0.6f;
        /// Tension01 above which the line reads Taut. Range 0.05..0.6.
        public static float tautBand = 0.25f;
        /// Tension01 above which the line reads Strained (creaks, glows).
        /// Range 0.4..0.95.
        public static float strainBand = 0.7f;
        /// Tension01 that, held, snaps the line. Range 0.7..1.
        public static float snapTension = 0.95f;
        /// s above snapTension before it goes. Range 0.2..4.
        public static float snapHoldSeconds = 1.2f;
        /// s smoothing on the read tension (display and snap). Range 0..0.5.
        public static float tensionSmoothSeconds = 0.12f;

        // --- the pull on the ship (phase 1: salvage, never a tow) ---
        /// m/s² on the ship per load unit of line force. At the default a
        /// crate at full strain is 0.3 m/s²: felt, never a tow. Range 0..0.1.
        public static float pullScale = 0.03f;
        /// rad/s of yaw toward the load per m/s² of side pull, on the
        /// servo-driven hulls (the steamer gets it from the force's lever
        /// arm). Range 0..0.3.
        public static float servoPullYaw = 0.08f;

        // --- crew ---
        /// The captain's work rate when nobody mans the gun. Range 0.2..1.
        public static float captainWorkRate = 0.6f;
        /// s the gun waits for a hand walking to the bow before the captain
        /// fires it. Range 0..5.
        public static float crewWalkWaitSeconds = 2f;
        /// s with no Demand before the hand is released. Range 0.2..5.
        public static float releaseIdleSeconds = 1f;

        // --- the mount ---
        /// Degrees/s the swivel turns toward its target. Range 30..720.
        public static float swivelDegPerSec = 160f;
        /// Revolutions/s of the winch drum per m/s of line hauled. Range 0..3.
        public static float drumTurnsPerMetre = 0.8f;
    }
}
