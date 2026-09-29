using UnityEngine;

namespace SeaSick.Ship
{
    /// How the ship ANSWERS: live knobs for the helm-feel lab (2026-09-24).
    ///
    /// Plain static fields, read every physics step by the code that actually
    /// turns and drives her -- `ShipMotor` for the ladder (sail) hulls and
    /// `PaddleDrive` for the steamer, which bypasses the motor's servo
    /// (`ShipMotor.ExternalDrive`). Nothing caches them, so a slider moved on
    /// the phone is felt on the next step. The lab edits them by REFLECTION on
    /// these exact field names; do not rename them.
    ///
    /// The two helpers below are the shared maths, so both drives mean the
    /// same thing by "turn rate at this speed" and "which lag applies".
    /// **Defaults = Kevin's FeelLab tuning, 2026-09-24** (phone, SAVE+LOG JSON pasted in chat after
    /// "sailing feels a lot better"): values he changed are baked here; the
    /// FeelLab still loads his saved PlayerPrefs over them on his phone.
    /// **Re-based 2026-09-29** (Kevin: "too rough around the edges and almost
    /// too much movement"): `turnCircleLengths`, `turnRateAtRest01` and
    /// `topSpeedScale` are no longer his 09-24 bake; the FeelLab drops their
    /// saved values once (`FeelLab.RebasedKeys`) so the new defaults show.
    public static class HandlingTuning
    {
        /// s, first-order lag for the yaw rate to BUILD toward what the helm
        /// asks.                                                 range 0.1..1.5
        public static float yawTauBuild = 0.45f;
        /// s, lag for the yaw rate to die when the rudder centres (or
        /// reverses: the rate has to pass through zero first).     range 0.1..2
        public static float yawTauRelease = 0.60f;
        /// Turn RADIUS at top speed, in hull lengths, before the flat-out
        /// falloff below: peak rate = topSpeed / (this x L). Was the constant
        /// ShipMotor.TurnCircleLengths = 2.2 (a 4.4 L circle).       range 0.8..6
        /// 2026-09-29: 1.6 -> 3.5. Peak yaw measured ~34-43 deg/s; target is
        /// ~15-20 deg/s. The rudder's response time (yawTau*) is unchanged.
        public static float turnCircleLengths = 3.5f;
        /// Fraction of the peak turn rate available at zero speed.     range 0..1
        /// 2026-09-29: 0.4 -> 0.2, so she does not pivot on the spot.
        public static float turnRateAtRest01 = 0.2f;
        /// Fraction of speed lost at the peak yaw rate:
        /// speed x (1 - this x (yawRate / peak)^2).                 range 0..0.6
        public static float turnSpeedBleed = 0.25f;
        /// Heel INTO the turn, degrees, at the peak yaw rate AND top speed;
        /// scales with (yawRate / peak) x (speed / top). Was a fixed torque,
        /// ShipMotor.turnHeel = 5 (measured 24.6 deg steady on the brig).
        ///                                                          range 0..20
        public static float turnHeelDegrees = 6f;
        /// Multiplies acceleration (propulsive side only).          range 0.3..3
        public static float accelScale = 2.15f;
        /// Multiplies top speed.                                  range 0.5..1.5
        /// 2026-09-29: 1.5 -> 1.15 (about 15 -> 11.5 m/s); burn/overdrive
        /// still adds its burst on top.
        public static float topSpeedScale = 1.15f;
        /// Multiplies what slows her while the telegraph is at stop
        /// (> 1 stops sooner).                                      range 0.3..3
        public static float coastDownScale = 1f;
        /// Written through to `PaddleDrive.Responsiveness` (0 = the pure
        /// strip-theory steamer, which also switches her yaw servo off).
        ///                                                          range 0..1.5
        public static float paddleResponsiveness = 1f;

        // --- capsize safety net (2026-09-29), ExternalDrive hulls only ---
        // The steamer/modular hulls skip ShipMotor's soft attitude limits and
        // their strip buoyancy is small-angle, so once past ~60-70 deg she
        // stayed inverted. Applied in `ShipMotor.ExternalRollGuard`.
        /// deg of heel before the extra righting spring starts. Normal turn
        /// heel and wave roll stay well inside it.                 range 20..60
        public static float capsizeSoftRollDeg = 30f;
        /// Extra righting spring past `capsizeSoftRollDeg`, as a multiple of
        /// the hull's own RollStiffness (m g GM), per radian past it. range 0..6
        public static float capsizeSoftRollStiffness = 1.5f;
        /// Extra roll damping past the limit, as a damping ratio of that
        /// spring (fades in over the first 10 deg past it).         range 0..3
        public static float capsizeSoftRollDamping = 0.8f;
        /// deg of heel (or hull up.y < 0.25) that counts as capsized. range 45..120
        public static float capsizeRecoverRollDeg = 75f;
        /// s she must stay capsized before she is righted.           range 0.5..6
        public static float capsizeRecoverSeconds = 2f;
        /// s the righting ease takes (spin zeroed, rotation eased to upright).
        ///                                                          range 0.2..3
        public static float capsizeRightingSeconds = 1f;

        /// Where the turn-rate curve peaks, as a fraction of top speed, and
        /// what is left of it flat out. From the 2026-09-18 helm design: 0.4x
        /// at rest, peak by half speed, 0.8x flat out -- a hull turns hardest
        /// at moderate way, where the rudder bites and the hull is not yet
        /// fighting it.
        public const float PeakAtSpeed01 = 0.5f;
        public const float FlatOutTurn01 = 0.8f;
        /// |ThrottleOrder| below this counts as "telegraph at stop" for
        /// `coastDownScale`.
        public const float CoastOrder = 0.02f;

        /// Available turn rate as a fraction of the peak, at `speed01` of top
        /// speed. `rest01` is the share at zero speed (normally
        /// `turnRateAtRest01`; the steamer gates it by her rudder inflow).
        public static float TurnRate01(float speed01, float rest01)
        {
            float s = Mathf.Clamp01(speed01);
            if (s < PeakAtSpeed01)
                return Mathf.Lerp(Mathf.Clamp01(rest01), 1f, Mathf.SmoothStep(0f, 1f, s / PeakAtSpeed01));
            return Mathf.Lerp(1f, FlatOutTurn01, (s - PeakAtSpeed01) / (1f - PeakAtSpeed01));
        }

        /// The lag to use this step: BUILD while the command is further from
        /// zero than the rate on the same side, RELEASE otherwise (helm
        /// easing, centred, or reversed -- a reversal has to unwind first).
        public static float YawTau(float command, float current)
        {
            bool building = command * current >= 0f && Mathf.Abs(command) > Mathf.Abs(current);
            return Mathf.Max(0.02f, building ? yawTauBuild : yawTauRelease);
        }
    }
}
