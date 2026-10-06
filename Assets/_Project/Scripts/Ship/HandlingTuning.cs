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
    /// **Re-based 2026-10-02** (Kevin: "I don't feel like I can get the boat
    /// where I want to get it"): tighter, quicker turns that STOP when the
    /// helm centres (`yawTauBuild`, `yawTauRelease`, `turnCircleLengths`, new
    /// `yawReleaseBrake`), a pivot from a standstill (`pivotTurnDegPerSec`)
    /// and the heading hold (`HeadingHold`, knobs in `HelmTuning`).
    public static class HandlingTuning
    {
        /// s, first-order lag for the yaw rate to BUILD toward what the helm
        /// asks.                                                 range 0.1..1.5
        /// 2026-10-02: 0.45 -> 0.30 (Kevin: "I don't feel like I can get the
        /// boat where I want to get it").
        public static float yawTauBuild = 0.30f;
        /// s, lag for the yaw rate to die when the rudder centres (or
        /// reverses: the rate has to pass through zero first).     range 0.1..2
        /// 2026-10-02: 0.60 -> 0.25; with `yawReleaseBrake` on top she stops
        /// turning ~0.3 s after the blade centres instead of carrying ~12 deg.
        public static float yawTauRelease = 0.25f;
        /// deg/s^2 of COUNTER-YAW while the turn eases (helm centred, eased
        /// or reversed), on top of the release lag: a constant brake, so the
        /// last of the swing dies in finite time instead of creeping out of
        /// an exponential tail. From the peak rate, 40 with a 0.25 s lag stops
        /// her in ~0.3 s having carried ~3 deg.                      range 0..120
        public static float yawReleaseBrake = 40f;
        /// Turn RADIUS at top speed, in hull lengths, before the flat-out
        /// falloff below: peak rate = topSpeed / (this x L). Was the constant
        /// ShipMotor.TurnCircleLengths = 2.2 (a 4.4 L circle).       range 0.8..6
        /// 2026-09-29: 1.6 -> 3.5. Peak yaw measured ~34-43 deg/s; target is
        /// ~15-20 deg/s. The rudder's response time (yawTau*) is unchanged.
        /// 2026-10-02: 3.5 -> 2.3, peak ~15 -> ~23 deg/s on the launch. With
        /// `TurnRate01` the rate holds through mid speeds, so slowing down
        /// tightens the circle (1.15 L radius at half speed) and flat out
        /// opens it (2.9 L).
        public static float turnCircleLengths = 2.3f;
        /// Fraction of the peak turn rate available at zero speed.     range 0..1
        /// 2026-09-29: 0.4 -> 0.2, so she does not pivot on the spot.
        public static float turnRateAtRest01 = 0.2f;
        /// deg/s she can PIVOT with no way on and the engine stopped, helm
        /// hard over: the wheel kicked against the blade, so she can be
        /// pointed at a dock or a target from a standstill. A floor under the
        /// speed curve, faded out by `PivotFadeSpeed01` of top speed, scaled
        /// by the helm like any other turn. 0 = the old "no water on the
        /// blade, no turn".                                         range 0..15
        public static float pivotTurnDegPerSec = 12f;
        /// Fraction of speed lost at the peak yaw rate:
        /// speed x (1 - this x (yawRate / peak)^2).                 range 0..0.6
        public static float turnSpeedBleed = 0.25f;
        /// Heel INTO the turn, degrees, at the peak yaw rate AND top speed;
        /// scales with (yawRate / peak) x (speed / top). Was a fixed torque,
        /// ShipMotor.turnHeel = 5 (measured 24.6 deg steady on the brig).
        ///                                                          range 0..20
        public static float turnHeelDegrees = 6f;
        /// **Captain alone can sail** (Kevin, 2026-09-30): the fraction of full
        /// ahead/astern the telegraph can reach with NO hands aboard, and the
        /// least the oars and the throttle ramp ever scale to when the crew
        /// are sick or at the rail. Hands lift the ceiling to full, as before;
        /// nothing ever drops below this.                          range 0.1..1
        public static float captainAloneThrottle = 0.35f;
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
        /// Keep docking assistance through slow travel, handing over by the
        /// normal turn curve's peak. The old quarter-speed fade left a weak
        /// steering band just as the boat began moving (2026-10-06).
        public const float PivotFadeSpeed01 = PeakAtSpeed01;

        /// rad/s the pivot floor gives at `speed01` of top speed: all of
        /// `pivotTurnDegPerSec` at rest, nothing by `PivotFadeSpeed01`.
        public static float PivotRate(float speed01) =>
            Mathf.Max(0f, pivotTurnDegPerSec) * Mathf.Deg2Rad
            * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(speed01) / PivotFadeSpeed01));

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
            return Mathf.Max(0.02f, Building(command, current) ? yawTauBuild : yawTauRelease);
        }

        static bool Building(float command, float current) =>
            command * current >= 0f && Mathf.Abs(command) > Mathf.Abs(current);

        /// One step of the yaw rate (rad/s) toward `command`: the first-order
        /// lag `YawTau` picks, and while easing, `yawReleaseBrake` on top --
        /// a constant counter-yaw that never carries her past the command.
        /// `tauScale` stretches both (the ladder's long hulls, a broach's
        /// rudder loss); 1 for the steamer.
        public static float YawStep(float current, float command, float dt, float tauScale)
        {
            float k = Mathf.Max(0.05f, tauScale);
            float next = current + (command - current) * (1f - Mathf.Exp(-dt / (YawTau(command, current) * k)));
            if (!Building(command, current))
                next = Mathf.MoveTowards(next, command, Mathf.Max(0f, yawReleaseBrake) * Mathf.Deg2Rad * dt / k);
            return next;
        }
    }

    /// **Heading hold** (2026-10-02, `HelmTuning.headingHold`). Once the
    /// player's helm is centred and the turn has died, the heading she is on
    /// is captured and held by a gentle yaw-RATE command proportional to the
    /// error, so waves and heel stop wandering her off it. It feeds the same
    /// lagged rate reference the helm does (`HandlingTuning.YawStep`), so it
    /// is a first-order loop through a ~0.3 s lag: holdGain 0.7/s gives a
    /// damping ratio of ~1 -- no hunting. Any real helm lets it go at once.
    /// Owned per ship by whichever drive turns her (`PaddleDrive`,
    /// `ShipMotor`); a plain struct, no allocation.
    public struct HeadingHold
    {
        /// Holding `HeadingDeg` right now.
        public bool Active;
        public float HeadingDeg;
        float calm;

        public void Reset() { Active = false; calm = 0f; }

        /// The hold's yaw-rate command this step, rad/s (+ to starboard), or
        /// 0 while it is off or waiting for the turn to die. `allowed` is
        /// the helm's say (`ShipMotor.HoldAllowed`, written by `HelmInput`); `helm` the blade, which
        /// still has to be (near) midships; `yawRate` the rate the capture
        /// waits on, rad/s.
        public float Step(bool allowed, float helm, float headingDeg, float yawRate, float dt)
        {
            if (!allowed || !HelmTuning.headingHold || Mathf.Abs(helm) > HelmTuning.HoldBreakRudder)
            {
                Reset();
                return 0f;
            }
            if (!Active)
            {
                // Capture once the turn has died -- or after a second anyway,
                // so a sea that never lets the rate settle still gets held.
                calm += dt;
                if (Mathf.Abs(yawRate) * Mathf.Rad2Deg > Mathf.Max(0.05f, HelmTuning.holdCaptureDegPerSec)
                    && calm < HelmTuning.HoldCaptureMaxSeconds)
                    return 0f;
                Active = true;
                HeadingDeg = headingDeg;
            }
            float err = Mathf.DeltaAngle(headingDeg, HeadingDeg);
            // Knocked well off it (a ram, a broach, a grounding): take the new
            // heading rather than wrestling her all the way back.
            if (Mathf.Abs(err) > HelmTuning.HoldLetGoDeg) { HeadingDeg = headingDeg; err = 0f; }
            float max = Mathf.Max(0f, HelmTuning.holdMaxDegPerSec);
            return Mathf.Clamp(err * Mathf.Max(0f, HelmTuning.holdGain), -max, max) * Mathf.Deg2Rad;
        }
    }
}
