using System;
using UnityEngine;

namespace SeaSick.Ocean
{
    /// <summary>
    /// **"Weather you can see coming" knobs** (2026-09-28, GDD §2/§6). Same
    /// load pattern as `OverboardTuning`: an asset at
    /// `Assets/_Project/Settings/Resources/SquallTuning.asset`, loaded with
    /// `Resources.Load` so the iOS build carries it with no scene reference.
    /// **Missing asset = the code's own defaults** below.
    ///
    /// All numbers: first pass, tune by play.
    /// </summary>
    [CreateAssetMenu(menuName = "SeaSick/Squall Tuning", fileName = "SquallTuning")]
    public class SquallTuning : ScriptableObject
    {
        [Header("Spawning")]
        [Tooltip("Roughly how often a new squall is born while sailing live, seconds. Jittered 0.7-1.3x so it never reads as a metronome.")]
        public float everySeconds = 240f;
        [Tooltip("Nearest a squall can be born, metres from the ship.")]
        public float minSpawnDistance = 700f;
        [Tooltip("Farthest a squall can be born, metres from the ship.")]
        public float maxSpawnDistance = 1100f;
        [Tooltip("Never spawn within this many metres of an island an outpost (camp) sits on.")]
        public float campClearance = 150f;

        [Header("The disc")]
        [Tooltip("Radius of the squall's bad-weather disc, metres.")]
        public float radius = 260f;
        [Tooltip("How fast the squall drifts across the water, m/s.")]
        public float driftSpeed = 3f;
        [Tooltip("How long a squall lives before it dissipates, seconds.")]
        public float lifeSeconds = 300f;

        [Header("Inside the squall")]
        [Tooltip("SeaStateController severity (0..1, same scale as SevNormal/SevRough) the sea is blended toward at the squall's dead centre. 0.85 sits between rough (0.70) and full storm (1.0).")]
        [Range(0f, 1f)] public float insideSeverity = 0.85f;
        [Tooltip("Ship speed, m/s, at or above which the squall's own storminess boost applies in full. Below it the boost is eased down -- riding it out SLOW is meant to be genuinely gentler, on top of whatever SmoothnessMeter already reads off actual hull motion.")]
        public float speedForFullBoost = 8f;
        [Tooltip("The boost multiplier at a dead stop, 0..1. Never zero -- the sea itself is still rough inside a squall, a becalmed ship just isn't adding slamming on top of it.")]
        [Range(0f, 1f)] public float minSpeedFactor = 0.6f;

        // --- loading -----------------------------------------------------

        public const string ResourcePath = "SquallTuning";

        static SquallTuning active;
        static bool looked;

        public static SquallTuning Active
        {
            get
            {
                if (looked) return active;
                looked = true;
                try { active = Resources.Load<SquallTuning>(ResourcePath); }
                catch (Exception) { active = null; }
                return active;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { looked = false; active = null; }

        static SquallTuning D => Active;

        public static float EverySeconds => D != null ? D.everySeconds : 240f;
        public static float MinSpawnDistance => D != null ? D.minSpawnDistance : 700f;
        public static float MaxSpawnDistance => D != null ? D.maxSpawnDistance : 1100f;
        public static float CampClearance => D != null ? D.campClearance : 150f;
        public static float Radius => D != null ? D.radius : 260f;
        public static float DriftSpeed => D != null ? D.driftSpeed : 3f;
        public static float LifeSeconds => D != null ? D.lifeSeconds : 300f;
        public static float InsideSeverity => D != null ? D.insideSeverity : 0.85f;
        public static float SpeedForFullBoost => D != null ? D.speedForFullBoost : 8f;
        public static float MinSpeedFactor => D != null ? D.minSpeedFactor : 0.6f;
    }
}
