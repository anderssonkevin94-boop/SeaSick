using UnityEngine;
using SeaSick.Crew;
using SeaSick.Voyage;
using SeaSick.World.Life;

namespace SeaSick.Ship.Overboard
{
    /// **The lashing meter** (phase 6, docs/PLAN-DEATH-RESCUE.md "Man
    /// overboard", last bullet: "Cargo goes overboard too"). Same shape as
    /// `CrewAgent.TrackGrip`, but per SHIP rather than per hand, drained by a
    /// HIGHER roughness threshold than a hand's own grip (a lashed hold
    /// rides out more than a queasy stomach does), and never touched until
    /// the scripted first man-overboard is done — a smooth first voyage
    /// should never lose a crate before it has even taught the player the
    /// swimmer rescue.
    ///
    /// Ticked from `CrewRoster.Update`, one call per ship per frame, the
    /// same place `FirstOverboard.Tick` already runs.
    public static class CargoLashing
    {
        static float lashing01 = 1f;

        public static void Tick(ShipMotor motor, AnchorController anchor, float dt)
        {
            // Nothing can slide before the scripted first swimmer has run
            // his course, and never off-live (catch-up/paused) — the same
            // guard every overboard system checks.
            if (!FirstOverboard.Done || motor == null || !Sailing.IsLive(anchor))
            {
                lashing01 = 1f;
                return;
            }

            var meter = motor.GetComponent<SmoothnessMeter>();
            var hull = motor.GetComponent<HullIntegrity>();

            float rough = meter != null ? meter.Roughness01 : 0f;
            float roughDrain = Mathf.Max(0f, rough - OverboardTuning.LashCalmRoughness)
                * OverboardTuning.LashDrainPerRoughness;

            float heel = Mathf.Abs(NormalizeDeg(motor.transform.eulerAngles.z));
            float heelDrain = Mathf.Max(0f, heel - OverboardTuning.LashHeelFreeDeg)
                * OverboardTuning.LashDrainPerHeelDeg;

            float slamDrain = 0f;
            if (hull != null && Time.time - hull.LastImpactTime < 0.5f)
                slamDrain = OverboardTuning.LashSlamDrainFlat
                    * Mathf.Clamp01(hull.LastImpactSpeed / Mathf.Max(0.1f, OverboardTuning.SlamSpeedForFullHit));

            float drain = roughDrain + heelDrain + slamDrain;
            if (drain > 0f)
            {
                float stormMul = 1f + OverboardTuning.StormDrainMultiplierExtra * Sailing.Storminess01;
                lashing01 = Mathf.Clamp01(lashing01 - drain * stormMul * dt);
            }
            else
            {
                lashing01 = Mathf.Clamp01(lashing01 + OverboardTuning.LashRefillPerSecond * dt);
            }

            if (lashing01 <= 0f)
            {
                lashing01 = 1f;
                TrySlideCargo(motor, heel >= 0f ? NormalizeDeg(motor.transform.eulerAngles.z) : 0f);
            }
        }

        static float NormalizeDeg(float degrees) => degrees > 180f ? degrees - 360f : degrees;

        /// **Dev panel (phase 6)**: skip the meter and the "first man
        /// overboard done" gate and slide one crate right now — for testing
        /// the pickup without waiting on real weather, same reasoning as
        /// `CrewAgent.DebugDropOverboardNear`.
        public static void DebugForceSlide(ShipMotor motor)
        {
            if (motor == null) return;
            lashing01 = 1f;
            TrySlideCargo(motor, NormalizeDeg(motor.transform.eulerAngles.z));
        }

        /// One crate over the side — weighted by count, so the resource the
        /// hold is fullest of is the one likeliest to slide, same reasoning
        /// as a real deck load.
        static void TrySlideCargo(ShipMotor motor, float heelDeg)
        {
            var voyage = Object.FindFirstObjectByType<VoyageManager>();
            if (voyage == null || voyage.TotalHeld <= 0) return;

            string resource = PickWeighted(voyage);
            if (string.IsNullOrEmpty(resource)) return;

            int have = voyage.HeldOf(resource);
            int amount = Mathf.Min(OverboardTuning.CrateUnits, have);
            if (amount <= 0) return;

            int taken = voyage.RemoveLoot(amount, resource);
            if (taken <= 0) return;

            var shipHold = motor.GetComponent<ShipHold>();
            if (shipHold != null)
                for (int i = 0; i < taken; i++) shipHold.RemoveVisual(resource);

            // The low side — whichever way she's leaning right now.
            float side = heelDeg >= 0f ? 1f : -1f;
            float halfBeam = OverboardTuning.HullHalfBeamMetres;
            float halfLen = Mathf.Max(1f, motor.HullLength * 0.5f - 1.5f);
            float z = Random.Range(-halfLen, halfLen);
            Vector3 local = new Vector3(halfBeam * side, 0.3f, z);
            Vector3 worldPos = motor.transform.TransformPoint(local);
            if (Ocean.OceanSampler.Ready) worldPos.y = Ocean.OceanSampler.SampleImmediate(worldPos).height;

            FloatingCargo.Spawn(resource, taken, motor.transform, worldPos);

            Ocean.DynamicWaterSim.Splash(worldPos, 3f, 0.6f);
            Banner.Show("Cargo overboard — " + taken + " " + resource.ToLowerInvariant());
        }

        static string PickWeighted(VoyageManager voyage)
        {
            // Every kind actually in the hold (the roll is random anyway, so
            // the dictionary's order does not matter) -- a fixed list here
            // would silently miss any resource added later.
            int total = 0;
            foreach (var kv in voyage.HeldStores) if (kv.Value > 0) total += kv.Value;
            if (total <= 0) return null;
            int roll = Random.Range(0, total);
            int running = 0;
            foreach (var kv in voyage.HeldStores)
            {
                if (kv.Value <= 0) continue;
                running += kv.Value;
                if (roll < running) return kv.Key;
            }
            return null;
        }
    }
}
