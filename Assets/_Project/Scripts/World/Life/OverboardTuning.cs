using System;
using UnityEngine;

namespace SeaSick.World.Life
{
    /// <summary>
    /// **Man overboard knobs** (docs/PLAN-DEATH-RESCUE.md, "Man overboard";
    /// phase 5a build brief). Same load pattern as `LifeTuning`: an asset at
    /// `Assets/_Project/Settings/Resources/OverboardTuning.asset`, loaded
    /// with `Resources.Load` so the iOS build carries it with no scene
    /// reference. **Missing asset = the code's own defaults** below.
    ///
    /// All numbers: first pass, tune by play.
    /// </summary>
    [CreateAssetMenu(menuName = "SeaSick/Overboard Tuning", fileName = "OverboardTuning")]
    public class OverboardTuning : ScriptableObject
    {
        [Header("Grip drain")]
        [Tooltip("SmoothnessMeter.Roughness01 below this drains nothing at all -- calm water is zero risk.")]
        public float calmRoughness = 0.15f;
        [Tooltip("Grip lost per second, per unit of roughness above the calm threshold.")]
        public float drainPerRoughness = 0.55f;
        [Tooltip("Degrees of heel (hull roll) below this cost nothing.")]
        public float heelFreeDeg = 10f;
        [Tooltip("Grip lost per second, per degree of heel above the free band.")]
        public float drainPerHeelDeg = 0.012f;
        [Tooltip("Flat grip hit from a hard slam (HullIntegrity.LastImpactTime within the last 0.5s), scaled by impact speed / slamSpeedForFullHit.")]
        public float slamDrainFlat = 0.35f;
        [Tooltip("Impact speed (m/s) that counts as a full-severity slam for grip purposes.")]
        public float slamSpeedForFullHit = 6f;
        [Tooltip("Grip refilled per second whenever nothing above is draining -- the 'smooth sailing = safe' promise.")]
        public float refillPerSecond = 0.10f;

        [Header("Multipliers")]
        [Tooltip("Extra drain multiplier at full storm (SeaStateController.Storminess01 == 1). 0 = no change, 1 = drains twice as fast.")]
        public float stormDrainMultiplierExtra = 0.9f;
        [Tooltip("Drain multiplier at night (TimeOfDay.Hour outside 6..20).")]
        public float nightDrainMultiplier = 1.3f;
        [Tooltip("Drain multiplier at full sea legs (LifeRecord.seaLegs == 1). Full stomach still floors it at 40%.")]
        public float seaLegsMinMultiplier = 0.4f;

        [Header("The warning")]
        [Tooltip("Grip level that grabs the rail and shouts \"Hold on!\".")]
        public float warnGrip = 0.18f;
        [Tooltip("Real seconds held at the rail once warned before the fall/save is decided.")]
        public float warnSeconds = 1.0f;

        [Header("The swimmer")]
        [Tooltip("Real seconds a swimmer has in calm water before the timer runs out.")]
        public float calmSwimSeconds = 150f;
        [Tooltip("Seconds shaved off the swim timer at full storm.")]
        public float stormSwimSecondsOff = 60f;
        [Tooltip("Seconds shaved off the swim timer at night.")]
        public float nightSwimSecondsOff = 30f;
        [Tooltip("Metres/second the swimmer drifts (wind/current stand-in).")]
        public float driftSpeed = 0.35f;
        [Tooltip("Metres from the nearest shore that counts as \"washed ashore\" rather than lost at sea when the timer runs out.")]
        public float washAshoreMetres = 40f;

        [Header("Outcomes")]
        [Tooltip("Sickness01 spike on rescue (soaked and shaken).")]
        public float rescueSicknessSpike = 0.9f;
        [Tooltip("Real seconds a rescued hand stays off their station.")]
        public float rescueOffStationSeconds = 30f;
        [Tooltip("Sea-legs points gained per rescue (LifeRecord.seaLegs, 0..1).")]
        public float rescueSeaLegsGain = 0.08f;

        [Header("Scripted first time")]
        [Tooltip("Real seconds under way (calm-ish water) before the scripted first man-overboard fires.")]
        public float firstTimeSailSeconds = 90f;
        [Tooltip("Roughness01 ceiling for \"calm-ish\" while waiting for the scripted first time.")]
        public float firstTimeMaxRoughness = 0.35f;
        [Tooltip("Swim timer for the scripted first swimmer -- long, so Kevin learns the rescue before a storm tests him.")]
        public float firstTimeSwimSeconds = 240f;

        // --- loading -----------------------------------------------------

        public const string ResourcePath = "OverboardTuning";

        static OverboardTuning active;
        static bool looked;

        public static OverboardTuning Active
        {
            get
            {
                if (looked) return active;
                looked = true;
                try { active = Resources.Load<OverboardTuning>(ResourcePath); }
                catch (Exception) { active = null; }
                return active;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { looked = false; active = null; }

        static OverboardTuning D => Active;

        public static float CalmRoughness => D != null ? D.calmRoughness : 0.15f;
        public static float DrainPerRoughness => D != null ? D.drainPerRoughness : 0.55f;
        public static float HeelFreeDeg => D != null ? D.heelFreeDeg : 10f;
        public static float DrainPerHeelDeg => D != null ? D.drainPerHeelDeg : 0.012f;
        public static float SlamDrainFlat => D != null ? D.slamDrainFlat : 0.35f;
        public static float SlamSpeedForFullHit => D != null ? D.slamSpeedForFullHit : 6f;
        public static float RefillPerSecond => D != null ? D.refillPerSecond : 0.10f;
        public static float StormDrainMultiplierExtra => D != null ? D.stormDrainMultiplierExtra : 0.9f;
        public static float NightDrainMultiplier => D != null ? D.nightDrainMultiplier : 1.3f;
        public static float SeaLegsMinMultiplier => D != null ? D.seaLegsMinMultiplier : 0.4f;
        public static float WarnGrip => D != null ? D.warnGrip : 0.18f;
        public static float WarnSeconds => D != null ? D.warnSeconds : 1.0f;
        public static float CalmSwimSeconds => D != null ? D.calmSwimSeconds : 150f;
        public static float StormSwimSecondsOff => D != null ? D.stormSwimSecondsOff : 60f;
        public static float NightSwimSecondsOff => D != null ? D.nightSwimSecondsOff : 30f;
        public static float DriftSpeed => D != null ? D.driftSpeed : 0.35f;
        public static float WashAshoreMetres => D != null ? D.washAshoreMetres : 40f;
        public static float RescueSicknessSpike => D != null ? D.rescueSicknessSpike : 0.9f;
        public static float RescueOffStationSeconds => D != null ? D.rescueOffStationSeconds : 30f;
        public static float RescueSeaLegsGain => D != null ? D.rescueSeaLegsGain : 0.08f;
        public static float FirstTimeSailSeconds => D != null ? D.firstTimeSailSeconds : 90f;
        public static float FirstTimeMaxRoughness => D != null ? D.firstTimeMaxRoughness : 0.35f;
        public static float FirstTimeSwimSeconds => D != null ? D.firstTimeSwimSeconds : 240f;
    }
}
