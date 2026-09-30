using System;
using UnityEngine;

namespace SeaSick.World.Life
{
    /// <summary>
    /// **Recruits-at-sea knobs** (docs/PLAN-DEATH-RESCUE.md, "Recruits at
    /// sea" + "Ferrying"; phase 7 build brief). Same load pattern as
    /// `OverboardTuning`/`LifeTuning`: an asset at
    /// `Assets/_Project/Settings/Resources/RecruitTuning.asset`, loaded with
    /// `Resources.Load` so the iOS build carries it with no scene reference.
    /// **Missing asset = the code's own defaults** below.
    ///
    /// All numbers: first pass, tune by play.
    /// </summary>
    [CreateAssetMenu(menuName = "SeaSick/Recruit Tuning", fileName = "RecruitTuning")]
    public class RecruitTuning : ScriptableObject
    {
        [Header("Visibility")]
        [Tooltip("Metres from the ship within which a castaway (washed-ashore crew or a stranger) is spawned as a visible figure, and within which a stranger is first rolled for on a camp-less island.")]
        public float castawayShowMetres = 250f;

        [Header("Strangers")]
        [Tooltip("Chance (0..1) that a camp-less island, the first time the ship comes within castawayShowMetres of it, turns out to have a stranger castaway waiting.")]
        public float strangerChance = 0.25f;
        [Tooltip("Cap on strangers alive (created, not yet picked up) at once across the whole world.")]
        public int maxStrangers = 3;

        [Header("Pickup")]
        [Tooltip("Metres from a castaway's saved beach spot within which \"Take <name> aboard\" appears.")]
        public float pickupMetres = 45f;
        [Tooltip("Real seconds the pickup takes once the button is pressed, while the ship stays in range and slow -- a small progress fill, same spirit as the rescue haul.")]
        public float pickupSeconds = 3f;

        /// **Castaways at sea** (Kevin, 2026-09-30: *"Please add so people
        /// can be found floating in the water."*). A stranger clinging to a
        /// board, spawned by `Voyage.CastawaySpawner` and pulled aboard with
        /// a bottom button. First-pass numbers, tune by play.
        [Header("Castaways at sea")]
        [Tooltip("Real seconds of live sailing (not paused, not away, not at anchor, not in a fight) between two castaways in the water, before jitter.")]
        public float seaCastawayEverySeconds = 600f;
        [Tooltip("± fraction of seaCastawayEverySeconds each wait is jittered by (0.25 = 450..750 s at 600).")]
        public float seaCastawayJitter = 0.25f;
        [Tooltip("Seconds a castaway stays in the water before drifting out of reach and being gone. No death, no penalty.")]
        public float seaCastawayLifeSeconds = 240f;
        [Tooltip("Nearest the castaway spawns ahead of the ship, metres.")]
        public float seaCastawayAheadMin = 110f;
        [Tooltip("Furthest the castaway spawns ahead of the ship, metres.")]
        public float seaCastawayAheadMax = 200f;
        [Tooltip("Half-angle either side of the bow the castaway can spawn in, degrees (60 = ahead or off either bow).")]
        public float seaCastawayConeDeg = 60f;
        [Tooltip("Seabed must be at least this deep under the spawn spot, metres. Keeps them out of shallows and off reefs.")]
        public float seaCastawayMinDepth = 4f;
        [Tooltip("Metres from the hull's side within which the Pull aboard button appears.")]
        public float seaCastawayPullMetres = 12f;
        [Tooltip("The ship must be slower than this (m/s) for the Pull aboard button; faster shows \"Slow down\".")]
        public float seaCastawayPullMaxSpeed = 3f;
        [Tooltip("Drift speed on the current, m/s.")]
        public float seaCastawayDriftSpeed = 0.4f;

        // --- loading -----------------------------------------------------

        public const string ResourcePath = "RecruitTuning";

        static RecruitTuning active;
        static bool looked;

        public static RecruitTuning Active
        {
            get
            {
                if (looked) return active;
                looked = true;
                try { active = Resources.Load<RecruitTuning>(ResourcePath); }
                catch (Exception) { active = null; }
                return active;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { looked = false; active = null; }

        static RecruitTuning D => Active;

        public static float CastawayShowMetres => D != null ? D.castawayShowMetres : 250f;
        public static float StrangerChance => D != null ? D.strangerChance : 0.25f;
        public static int MaxStrangers => D != null ? D.maxStrangers : 3;
        public static float PickupMetres => D != null ? D.pickupMetres : 45f;
        public static float PickupSeconds => D != null ? D.pickupSeconds : 3f;

        public static float SeaCastawayEverySeconds => D != null ? D.seaCastawayEverySeconds : 600f;
        public static float SeaCastawayJitter => D != null ? D.seaCastawayJitter : 0.25f;
        public static float SeaCastawayLifeSeconds => D != null ? D.seaCastawayLifeSeconds : 240f;
        public static float SeaCastawayAheadMin => D != null ? D.seaCastawayAheadMin : 110f;
        public static float SeaCastawayAheadMax => D != null ? D.seaCastawayAheadMax : 200f;
        public static float SeaCastawayConeDeg => D != null ? D.seaCastawayConeDeg : 60f;
        public static float SeaCastawayMinDepth => D != null ? D.seaCastawayMinDepth : 4f;
        public static float SeaCastawayPullMetres => D != null ? D.seaCastawayPullMetres : 12f;
        public static float SeaCastawayPullMaxSpeed => D != null ? D.seaCastawayPullMaxSpeed : 3f;
        public static float SeaCastawayDriftSpeed => D != null ? D.seaCastawayDriftSpeed : 0.4f;
    }
}
