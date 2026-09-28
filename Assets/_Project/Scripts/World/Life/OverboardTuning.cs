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

        [Header("The rescue (phase 5b)")]
        [Tooltip("Metres from the hull's rail/side (not its centre) within which \"Throw line\" appears.")]
        public float throwReachMetres = 9f;
        [Tooltip("Ship speed (m/s) above which she's too fast to throw a line, even in reach.")]
        public float throwMaxSpeed = 1.8f;
        [Tooltip("Half the hull's beam, metres -- used to find the nearest point on her SIDE rather than her centre when judging reach and where a haul happens.")]
        public float hullHalfBeamMetres = 3.5f;
        [Tooltip("Real seconds a haul takes once a crew member reaches the rail.")]
        public float haulSeconds = 4f;
        [Tooltip("Metres/second the swimmer is pulled toward the rail during a haul.")]
        public float haulPullSpeed = 2.5f;
        [Tooltip("A haul this far past throwReachMetres (as a multiple) slips the line and cancels the haul.")]
        public float haulSlipMultiple = 1.6f;
        [Tooltip("Real seconds the rescuer stays off station after a successful haul, on top of the swimmer's own rescueOffStationSeconds.")]
        public float rescuerRecoverSeconds = 6f;

        [Header("Cargo overboard (phase 6)")]
        [Tooltip("SmoothnessMeter.Roughness01 below this drains the LASHING meter nothing at all -- higher than calmRoughness, because a well-lashed hold rides out more than a queasy hand's grip does.")]
        public float lashCalmRoughness = 0.40f;
        [Tooltip("Lashing lost per second, per unit of roughness above lashCalmRoughness.")]
        public float lashDrainPerRoughness = 0.35f;
        [Tooltip("Degrees of heel below this cost the lashing nothing.")]
        public float lashHeelFreeDeg = 16f;
        [Tooltip("Lashing lost per second, per degree of heel above the free band.")]
        public float lashDrainPerHeelDeg = 0.01f;
        [Tooltip("Flat lashing hit from a hard slam (HullIntegrity.LastImpactTime within the last 0.5s), scaled by impact speed / slamSpeedForFullHit (shared with grip).")]
        public float lashSlamDrainFlat = 0.5f;
        [Tooltip("Lashing refilled per second whenever nothing above is draining.")]
        public float lashRefillPerSecond = 0.06f;
        [Tooltip("Units of the picked resource that slide off when the lashing meter empties (or fewer, if less than this is held).")]
        public int crateUnits = 4;
        [Tooltip("Real seconds a floating crate has before it sinks for good.")]
        public float floatSeconds = 120f;

        [Header("Shipyard modules (phase 8)")]
        [Tooltip("Grip-drain multiplier per bulwarks module fitted (stacks multiplicatively, capped at bulwarksMaxStacks).")]
        public float bulwarksDrainMultiplier = 0.6f;
        [Tooltip("Most bulwarks modules that stack their grip-drain reduction.")]
        public int bulwarksMaxStacks = 2;
        [Tooltip("Grip-drain multiplier while safety lines are fitted (almost nobody goes over).")]
        public float safetyLinesDrainMultiplier = 0.15f;
        [Tooltip("Station work-rate multiplier for every hand while safety lines are fitted (the trade-off: slower sails/oars/guns).")]
        public float safetyLinesWorkRateMultiplier = 0.85f;
        [Tooltip("Extra throw reach, metres, while a lifebuoy rack is fitted.")]
        public float lifebuoyReachBonusMetres = 6f;
        [Tooltip("Swim-timer seconds added ONCE, the moment \"Throw line\" is pressed on a swimmer, while a lifebuoy rack is fitted.")]
        public float lifebuoyTimerBonusSeconds = 45f;
        [Tooltip("Haul-time multiplier while a scramble net is fitted (faster hauls, swimmer or cargo).")]
        public float scrambleNetHaulMultiplier = 0.5f;
        [Tooltip("Extra real seconds on the rail-hold warning while a lookout is fitted (spots trouble sooner).")]
        public float lookoutWarnSecondsBonus = 0.8f;
        [Tooltip("Extra seconds on a swimmer's timer, always, while a lookout is fitted (spotted going in).")]
        public float lookoutSwimSecondsBonus = 30f;
        [Tooltip("Ship speed (m/s) below which the jolly boat will launch for a swimmer.")]
        public float jollyBoatMaxShipSpeed = 3f;
        [Tooltip("Jolly boat rowing speed, metres/second, out and back.")]
        public float jollyBoatRowSpeed = 2.5f;
        [Tooltip("Fewest OTHER hull sections (middles) the ship must have for a jolly boat to be launchable -- \"big ships only\".")]
        public int jollyBoatMinMiddleSections = 2;

        [Header("Scripted first time")]
        [Tooltip("Real seconds under way (calm-ish water) before the scripted first man-overboard fires.")]
        public float firstTimeSailSeconds = 90f;
        [Tooltip("Roughness01 ceiling for \"calm-ish\" while waiting for the scripted first time.")]
        public float firstTimeMaxRoughness = 0.45f;
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
        public static float ThrowReachMetres => D != null ? D.throwReachMetres : 9f;
        public static float ThrowMaxSpeed => D != null ? D.throwMaxSpeed : 1.8f;
        public static float HullHalfBeamMetres => D != null ? D.hullHalfBeamMetres : 3.5f;
        public static float HaulSeconds => D != null ? D.haulSeconds : 4f;
        public static float HaulPullSpeed => D != null ? D.haulPullSpeed : 2.5f;
        public static float HaulSlipMultiple => D != null ? D.haulSlipMultiple : 1.6f;
        public static float RescuerRecoverSeconds => D != null ? D.rescuerRecoverSeconds : 6f;
        public static float LashCalmRoughness => D != null ? D.lashCalmRoughness : 0.40f;
        public static float LashDrainPerRoughness => D != null ? D.lashDrainPerRoughness : 0.35f;
        public static float LashHeelFreeDeg => D != null ? D.lashHeelFreeDeg : 16f;
        public static float LashDrainPerHeelDeg => D != null ? D.lashDrainPerHeelDeg : 0.01f;
        public static float LashSlamDrainFlat => D != null ? D.lashSlamDrainFlat : 0.5f;
        public static float LashRefillPerSecond => D != null ? D.lashRefillPerSecond : 0.06f;
        public static int CrateUnits => D != null ? D.crateUnits : 4;
        public static float FloatSeconds => D != null ? D.floatSeconds : 120f;
        public static float BulwarksDrainMultiplier => D != null ? D.bulwarksDrainMultiplier : 0.6f;
        public static int BulwarksMaxStacks => D != null ? D.bulwarksMaxStacks : 2;
        public static float SafetyLinesDrainMultiplier => D != null ? D.safetyLinesDrainMultiplier : 0.15f;
        public static float SafetyLinesWorkRateMultiplier => D != null ? D.safetyLinesWorkRateMultiplier : 0.85f;
        public static float LifebuoyReachBonusMetres => D != null ? D.lifebuoyReachBonusMetres : 6f;
        public static float LifebuoyTimerBonusSeconds => D != null ? D.lifebuoyTimerBonusSeconds : 45f;
        public static float ScrambleNetHaulMultiplier => D != null ? D.scrambleNetHaulMultiplier : 0.5f;
        public static float LookoutWarnSecondsBonus => D != null ? D.lookoutWarnSecondsBonus : 0.8f;
        public static float LookoutSwimSecondsBonus => D != null ? D.lookoutSwimSecondsBonus : 30f;
        public static float JollyBoatMaxShipSpeed => D != null ? D.jollyBoatMaxShipSpeed : 3f;
        public static float JollyBoatRowSpeed => D != null ? D.jollyBoatRowSpeed : 2.5f;
        public static int JollyBoatMinMiddleSections => D != null ? D.jollyBoatMinMiddleSections : 2;
        public static float FirstTimeSailSeconds => D != null ? D.firstTimeSailSeconds : 90f;
        public static float FirstTimeMaxRoughness => D != null ? D.firstTimeMaxRoughness : 0.45f;
        public static float FirstTimeSwimSeconds => D != null ? D.firstTimeSwimSeconds : 240f;
    }
}
