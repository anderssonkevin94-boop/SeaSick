using System;
using UnityEngine;

namespace SeaSick.Ship.SeaLife
{
    /// <summary>
    /// **"Things to find at sea" knobs** (2026-09-28) — flotsam, message
    /// bottles, fish shoals and dolphins, the sea between islands feeling
    /// alive rather than empty water to cross. Same load pattern as
    /// `OverboardTuning`: an asset at
    /// `Assets/_Project/Settings/Resources/SeaLifeTuning.asset`, loaded with
    /// `Resources.Load` so the iOS build carries it with no scene reference.
    /// **Missing asset = the code's own defaults** below.
    ///
    /// All numbers: first pass, tune by play.
    /// </summary>
    [CreateAssetMenu(menuName = "SeaSick/Sea Life Tuning", fileName = "SeaLifeTuning")]
    public class SeaLifeTuning : ScriptableObject
    {
        [Header("Flotsam")]
        [Tooltip("Average real seconds of live sailing between flotsam spawns (jittered +/-30%). Only spawns if fewer than 2 pieces already exist.")]
        public float flotsamEverySeconds = 150f;
        [Tooltip("Real seconds a piece of flotsam has before it quietly sinks.")]
        public float flotsamLifeSeconds = 300f;
        [Tooltip("Metres ahead of the bow, nearest, a new piece of flotsam spawns.")]
        public float flotsamAheadMin = 150f;
        [Tooltip("Metres ahead of the bow, farthest, a new piece of flotsam spawns.")]
        public float flotsamAheadMax = 300f;
        [Tooltip("Half-angle off the bow (degrees) flotsam can spawn within.")]
        public float flotsamConeDeg = 40f;
        [Tooltip("Fewest units of the random resource a wreckage cluster holds.")]
        public int flotsamUnitsMin = 2;
        [Tooltip("Most units of the random resource a wreckage cluster holds.")]
        public int flotsamUnitsMax = 5;
        [Tooltip("Metres astern a piece of flotsam can drift before it is culled quietly.")]
        public float flotsamCullAsternMetres = 600f;
        [Tooltip("Metres/second flotsam drifts.")]
        public float flotsamDriftSpeed = 0.3f;

        [Header("Message in a bottle")]
        [Tooltip("Chance, per flotsam spawn roll, that it's a bottle instead of a wreckage cluster.")]
        [Range(0f, 1f)] public float bottleChance = 0.2f;

        [Header("Fish shoal")]
        [Tooltip("Average real seconds of live sailing between shoal spawns.")]
        public float shoalEverySeconds = 200f;
        [Tooltip("Metres off the bow, nearest, a shoal spawns.")]
        public float shoalAheadMin = 120f;
        [Tooltip("Metres off the bow, farthest, a shoal spawns.")]
        public float shoalAheadMax = 250f;
        [Tooltip("Ship must be within this many metres of the shoal's centre to fish it.")]
        public float shoalRadius = 25f;
        [Tooltip("Ship must be slower than this (m/s) to fish.")]
        public float shoalMaxSpeed = 3f;
        [Tooltip("Real seconds between each +1 food while fishing.")]
        public float fishSeconds = 4f;
        [Tooltip("Most food a single shoal yields before it's fished out.")]
        public int shoalFood = 6;
        [Tooltip("Real seconds a shoal lingers before it disperses even if not fished out.")]
        public float shoalTimeoutSeconds = 240f;
        [Tooltip("How many gulls circle a shoal.")]
        public int shoalBirdsMin = 4;
        public int shoalBirdsMax = 6;
        [Tooltip("Gull circling radius, metres.")]
        public float shoalBirdRadiusMin = 6f;
        public float shoalBirdRadiusMax = 10f;

        [Header("Dolphins (reward for smooth sailing)")]
        [Tooltip("Ship speed (m/s) that counts as \"sailing\" for the dolphin gate.")]
        public float dolphinMinSpeed = 4f;
        [Tooltip("SmoothnessMeter.Roughness01 must stay below this the whole time.")]
        public float dolphinMaxRoughness01 = 0.2f;
        [Tooltip("Continuous seconds of smooth, fast sailing before dolphins show up.")]
        public float dolphinBuildupSeconds = 20f;
        [Tooltip("How long the pod stays once it arrives.")]
        public float dolphinDurationSeconds = 20f;
        [Tooltip("Real seconds before dolphins can appear again.")]
        public float dolphinCooldownSeconds = 180f;
        [Tooltip("Fewest / most dolphins in a pod.")]
        public int dolphinCountMin = 2;
        public int dolphinCountMax = 3;
        [Tooltip("Crew sickness decay multiplier while dolphins are at the bow (Sickness01 easing, not rising).")]
        public float dolphinSicknessDecayMultiplier = 1.5f;

        [Header("Spawn safety")]
        [Tooltip("Nothing spawns within this many metres of an island's shore, or inside a camp's harbour.")]
        public float minDistanceFromShoreMetres = 60f;

        // --- loading -----------------------------------------------------

        public const string ResourcePath = "SeaLifeTuning";

        static SeaLifeTuning active;
        static bool looked;

        public static SeaLifeTuning Active
        {
            get
            {
                if (looked) return active;
                looked = true;
                try { active = Resources.Load<SeaLifeTuning>(ResourcePath); }
                catch (Exception) { active = null; }
                return active;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { looked = false; active = null; }

        static SeaLifeTuning D => Active;

        public static float FlotsamEverySeconds => D != null ? D.flotsamEverySeconds : 150f;
        public static float FlotsamLifeSeconds => D != null ? D.flotsamLifeSeconds : 300f;
        public static float FlotsamAheadMin => D != null ? D.flotsamAheadMin : 150f;
        public static float FlotsamAheadMax => D != null ? D.flotsamAheadMax : 300f;
        public static float FlotsamConeDeg => D != null ? D.flotsamConeDeg : 40f;
        public static int FlotsamUnitsMin => D != null ? D.flotsamUnitsMin : 2;
        public static int FlotsamUnitsMax => D != null ? D.flotsamUnitsMax : 5;
        public static float FlotsamCullAsternMetres => D != null ? D.flotsamCullAsternMetres : 600f;
        public static float FlotsamDriftSpeed => D != null ? D.flotsamDriftSpeed : 0.3f;
        public static float BottleChance => D != null ? D.bottleChance : 0.2f;
        public static float ShoalEverySeconds => D != null ? D.shoalEverySeconds : 200f;
        public static float ShoalAheadMin => D != null ? D.shoalAheadMin : 120f;
        public static float ShoalAheadMax => D != null ? D.shoalAheadMax : 250f;
        public static float ShoalRadius => D != null ? D.shoalRadius : 25f;
        public static float ShoalMaxSpeed => D != null ? D.shoalMaxSpeed : 3f;
        public static float FishSeconds => D != null ? D.fishSeconds : 4f;
        public static int ShoalFood => D != null ? D.shoalFood : 6;
        public static float ShoalTimeoutSeconds => D != null ? D.shoalTimeoutSeconds : 240f;
        public static int ShoalBirdsMin => D != null ? D.shoalBirdsMin : 4;
        public static int ShoalBirdsMax => D != null ? D.shoalBirdsMax : 6;
        public static float ShoalBirdRadiusMin => D != null ? D.shoalBirdRadiusMin : 6f;
        public static float ShoalBirdRadiusMax => D != null ? D.shoalBirdRadiusMax : 10f;
        public static float DolphinMinSpeed => D != null ? D.dolphinMinSpeed : 4f;
        public static float DolphinMaxRoughness01 => D != null ? D.dolphinMaxRoughness01 : 0.2f;
        public static float DolphinBuildupSeconds => D != null ? D.dolphinBuildupSeconds : 20f;
        public static float DolphinDurationSeconds => D != null ? D.dolphinDurationSeconds : 20f;
        public static float DolphinCooldownSeconds => D != null ? D.dolphinCooldownSeconds : 180f;
        public static int DolphinCountMin => D != null ? D.dolphinCountMin : 2;
        public static int DolphinCountMax => D != null ? D.dolphinCountMax : 3;
        public static float DolphinSicknessDecayMultiplier => D != null ? D.dolphinSicknessDecayMultiplier : 1.5f;
        public static float MinDistanceFromShoreMetres => D != null ? D.minDistanceFromShoreMetres : 60f;
    }
}
