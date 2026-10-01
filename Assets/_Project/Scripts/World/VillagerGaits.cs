using UnityEngine;

namespace SeaSick.World
{
    /// **How fast each walk really walks** (2026-10-01, Kevin: villagers
    /// "just slide around ... playing an animation and gliding. slow their
    /// speed if you have to but i want to see them actually walk. if they
    /// turn, they turn their body there.")
    ///
    /// The v15 walks are authored IN PLACE: the stance foot slides backward
    /// under the hips at the clip's own ground speed. A body that moves at
    /// exactly that speed, played at rate 1, keeps the foot planted on the
    /// ground; one moving at 2.6 m/s with the clip capped at 2.2x skated.
    /// So the gait sets the speed, never the other way round: urgency picks
    /// WHICH walk (stroll, errand, tired, carry, run, scared), and each walk
    /// moves at its own clip's speed times a fixed playback rate.
    ///
    /// The speeds are MEASURED off the imported clips (`foot.L`/`foot.R`
    /// backward speed while planted, DeckhandVisual at its 1.31 prefab
    /// scale, 240 samples a loop), not copied from clips.json -- they agree
    /// with it to 2 cm/s, and Carry (0.35), HuntWalk (0.42) and Gangway
    /// (0.21), which it does not list, were wrong in code before.
    public static class VillagerGaits
    {
        // m/s at playback rate 1, game size.
        public const float WalkClip = 0.58f, BriskClip = 0.86f, TiredClip = 0.23f, DeckClip = 0.33f,
            RunClip = 2.04f, ScaredClip = 2.30f, CarryClip = 0.35f, StalkClip = 0.42f,
            SickClip = 0.22f, GangwayClip = 0.21f;

        /// **Every walk's playback rate** (2026-10-01, Kevin chose "quicker
        /// steps, 1.5x"): each clip plays this much faster AND the body moves
        /// this much faster than the clip's own ground speed, so the foot
        /// stays planted -- only the cadence changes.
        public static float Cadence = 1.5f;
        /// The gangway's balancing shuffle keeps its authored pace (quickened
        /// it reads as a scurry along the plank).
        public static float GangwayRate = 1f;
        /// Running tops out here, m/s (Run at 1.5x would be 3.06, RunScared
        /// 3.45: a sprint, not a camp's run).
        public static float RunCap = 3.0f;

        /// The rate window. Above `RateMax` (a walk on a road: 1.5 x 1.3) a
        /// body moving faster than its walk skates rather than scurries;
        /// below `RateMin` it is the start/stop ramp.
        public static float RateMin = 0.2f, RateMax = 2.0f;

        /// Body turn rate, degrees a second: walking, and standing.
        public static float TurnMoving = 330f, TurnInPlace = 480f;
        /// Off-heading at which he stops to turn rather than walk on (deg),
        /// and below which he walks at full speed.
        public static float SharpDeg = 60f, StraightDeg = 20f;
        /// Seconds from standing to full speed (and back).
        public static float RampSeconds = 0.3f;

        /// The speed a clip of ground speed `clip` (m/s at rate 1) is walked at.
        public static float Cruise(float clip) => Mathf.Min(clip * Cadence, Mathf.Max(RunCap, clip));
        public static float Carry => Cruise(CarryClip);

        /// **The books' walking speed** (`OutpostLedger.WalkMetresPerSecond`):
        /// a trip is out brisk and back carrying, so the harmonic mean of
        /// the two -- what a round trip really averages.
        public static float Books => 2f / (1f / Cruise(BriskClip) + 1f / Carry);
    }

    /// **One body's stride**: turns the body toward where it is going at a
    /// body's rate and moves it only along its OWN forward, easing up from
    /// standing and down into a stop. Plain C#, one per walker.
    public sealed class Stride
    {
        /// Ground speed now, m/s.
        public float Speed;
        int lastFrame = -10;

        /// Stood still (arrived, blocked, or doing something else).
        public void Stop() => Speed = 0f;

        /// Turn the body (yaw only, about world up) toward `want` and return
        /// this frame's step along its new forward. `cruise` = the walk's
        /// speed, `distLeft` = metres left to the stopping point (brakes
        /// into it), `steerDeg` = a bias to the heading (passing someone).
        public Vector3 Step(Transform body, Vector3 want, float cruise, float distLeft, float dt, float steerDeg = 0f)
            => Step(body, want, cruise, distLeft, dt, steerDeg, false);

        /// The same in the body's PARENT frame (a hand on a ship's deck):
        /// `wantLocal` and the returned step are parent-local, and only the
        /// local yaw is written, so he still rolls with the deck.
        public Vector3 StepLocal(Transform body, Vector3 wantLocal, float cruise, float distLeft, float dt)
            => Step(body, wantLocal, cruise, distLeft, dt, 0f, true);

        Vector3 Step(Transform body, Vector3 want, float cruise, float distLeft, float dt, float steerDeg, bool local)
        {
            // A body not stepped last frame stood still in between.
            int f = Time.frameCount;
            if (f - lastFrame > 1) Speed = 0f;
            lastFrame = f;

            want.y = 0f;
            Vector3 fwd = local ? body.localRotation * Vector3.forward : body.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            float off = 0f;
            if (want.sqrMagnitude > 1e-8f)
                off = Mathf.DeltaAngle(yaw, Mathf.Atan2(want.x, want.z) * Mathf.Rad2Deg + steerDeg);

            float turnRate = Speed < 0.3f * cruise ? VillagerGaits.TurnInPlace : VillagerGaits.TurnMoving;
            float turn = Mathf.Clamp(off, -turnRate * dt, turnRate * dt);
            yaw += turn;
            off -= turn;
            if (local) body.localRotation = Quaternion.Euler(0f, yaw, 0f);
            else body.rotation = Quaternion.Euler(0f, yaw, 0f);

            // Full speed within `StraightDeg` of the heading, none past
            // `SharpDeg`: a sharp corner is turned on the spot, then walked.
            float a = Mathf.Abs(off);
            float k = a <= VillagerGaits.StraightDeg ? 1f
                : a >= VillagerGaits.SharpDeg ? 0f
                : 1f - (a - VillagerGaits.StraightDeg) / (VillagerGaits.SharpDeg - VillagerGaits.StraightDeg);
            float accel = cruise / Mathf.Max(0.05f, VillagerGaits.RampSeconds);
            float target = cruise * k;
            // Brake into the stop: v^2 = 2 a d, never below a fifth of the
            // walk so the last pace is still a pace.
            target = Mathf.Min(target, Mathf.Max(0.2f * cruise, Mathf.Sqrt(2f * accel * Mathf.Max(0f, distLeft))));
            if (want.sqrMagnitude <= 1e-8f) target = 0f;
            Speed = Mathf.MoveTowards(Speed, target, accel * dt);
            float y = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(y), 0f, Mathf.Cos(y)) * (Speed * dt);
        }
    }
}
