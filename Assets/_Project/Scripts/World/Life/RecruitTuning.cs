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
    }
}
