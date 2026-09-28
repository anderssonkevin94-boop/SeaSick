using System;
using UnityEngine;

namespace SeaSick.Combat
{
    /// <summary>
    /// **Village-defence phase 9 knobs** (docs/PLAN-DEATH-RESCUE.md, "Village
    /// defence in raids": the fight only -- raiders get health, defenders
    /// jab, raiders hit back, morale breaks at half the party down). Same
    /// load pattern as `World.Life.LifeTuning`: an asset at
    /// `Assets/_Project/Settings/Resources/RaidFightTuning.asset`, loaded
    /// with `Resources.Load` so the iOS build carries it with no scene
    /// reference. **Missing asset = the code's own defaults** below, so
    /// nothing breaks if it is deleted or never created.
    ///
    /// All numbers: first pass, tune by play.
    /// </summary>
    [CreateAssetMenu(menuName = "SeaSick/Raid Fight Tuning", fileName = "RaidFightTuning")]
    public class RaidFightTuning : ScriptableObject
    {
        [Header("Raider health")]
        [Tooltip("Hits a raider can take before he falls. Docs: ~3 jabs with a stone spear, ~2 with iron.")]
        public float raiderHp = 3f;
        [Tooltip("Damage one stone-spear jab does.")]
        public float stoneDamage = 1f;
        [Tooltip("Damage one iron-spear jab does.")]
        public float ironDamage = 1.5f;
        [Tooltip("Real seconds a killed raider's body lies flat before it fades/sinks away.")]
        public float corpseFadeSeconds = 8f;

        [Header("Defenders")]
        [Tooltip("Real seconds between one defender's jabs.")]
        public float jabSeconds = 0.8f;
        [Tooltip("Metres a defender must close to before he can jab.")]
        public float jabReach = 1.6f;
        [Tooltip("Metres from the fire a defender will still chase a raider when the camp has no walls. Inside a wall, the wall itself (routing that treats walls/gates as solid to a raider) is the test instead.")]
        public float defendRadiusNoWalls = 30f;
        [Tooltip("Hits a raider must land on this hand to knock him down. He goes down the ordinary way (OutpostLedger.Down) once reached.")]
        public int hitsToDown = 3;
        [Tooltip("Death/rescue phase 10: how far outside the fire an armed defender waits, toward the raiders, when the camp has no walls and no raider has come inside yet. Metres from the fire, well short of defendRadiusNoWalls.")]
        public float gatherRadiusNoWalls = 8f;
        [Tooltip("Death/rescue phase 10: metres an unarmed hand with no hut runs from the raiders' centre before crouching, kept inside the defend perimeter.")]
        public float crouchDistance = 10f;

        [Header("Raids grow with the camp (phase 12)")]
        [Tooltip("Party size with nobody home to raise it: the floor, and the minimum a raid is ever clamped down to.")]
        public int basePartySize = 3;
        [Tooltip("One extra raider per this many hands living at the camp (floored).")]
        public int handsPerExtraRaider = 4;
        [Tooltip("One extra raider per this many whole units sitting in the camp's stores (floored) -- OutpostLedger.Total, every resource together.")]
        public float wealthPerExtraRaider = 200f;
        [Tooltip("At most this many extra raiders from stored wealth (the rest comes from the number of hands). Starting value.")]
        public int maxWealthRaiders = 1;
        [Tooltip("Party size never grows past this, however rich or crowded the camp gets.")]
        public int maxPartySize = 7;
        [Tooltip("Loot cap for a base-size (basePartySize) party -- scales with the party, capped at maxLootCap.")]
        public int maxLootBase = 8;
        [Tooltip("The loot cap never grows past this, however large the party.")]
        public int maxLootCap = 16;

        [Header("Raiders fighting back")]
        [Tooltip("Real seconds between one raider's strikes once he is fighting rather than stealing.")]
        public float raiderHitSeconds = 1.2f;
        [Tooltip("Metres a raider must close to before he can strike.")]
        public float raiderReach = 1.6f;

        [Header("Landing + routing (shortest reasonable route)")]
        [Tooltip("Metres-equivalent cost of breaking a wall segment, added to the walk to reach it, when comparing a breach against an open route. Kevin: raiders should take the shortest reasonable route in, and if a breach is nearer than walking round, that's the route.")]
        public float breachCostMetres = 40f;
        [Tooltip("Landing-site candidates sampled round the shoreline (evenly spaced bearings) once per raid, before the ship sails in. More is costlier at raid-start only, never per frame.")]
        public int landingCandidates = 36;

        // --- loading -----------------------------------------------------

        public const string ResourcePath = "RaidFightTuning";

        static RaidFightTuning active;
        static bool looked;

        /// The asset, or null when there is none (the code defaults then
        /// stand). Loaded once; safe to call from edit-mode tools.
        public static RaidFightTuning Active
        {
            get
            {
                if (looked) return active;
                looked = true;
                try { active = Resources.Load<RaidFightTuning>(ResourcePath); }
                catch (Exception) { active = null; }
                return active;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            // Domain reload may be off in the editor: forget the last run.
            looked = false; active = null;
        }

        public static float RaiderHp => Active != null ? Active.raiderHp : 3f;
        public static float StoneDamage => Active != null ? Active.stoneDamage : 1f;
        public static float IronDamage => Active != null ? Active.ironDamage : 1.5f;
        public static float CorpseFadeSeconds => Active != null ? Active.corpseFadeSeconds : 8f;
        public static float JabSeconds => Active != null ? Active.jabSeconds : 0.8f;
        public static float JabReach => Active != null ? Active.jabReach : 1.6f;
        public static float DefendRadiusNoWalls => Active != null ? Active.defendRadiusNoWalls : 30f;
        public static int HitsToDown => Active != null ? Active.hitsToDown : 3;
        public static float GatherRadiusNoWalls => Active != null ? Active.gatherRadiusNoWalls : 8f;
        public static float CrouchDistance => Active != null ? Active.crouchDistance : 10f;
        public static float RaiderHitSeconds => Active != null ? Active.raiderHitSeconds : 1.2f;
        public static float RaiderReach => Active != null ? Active.raiderReach : 1.6f;

        public static float BreachCostMetres => Active != null ? Active.breachCostMetres : 40f;
        public static int LandingCandidates => Active != null ? Active.landingCandidates : 36;

        public static int BasePartySize => Active != null ? Active.basePartySize : 3;
        public static int HandsPerExtraRaider => Active != null ? Active.handsPerExtraRaider : 4;
        public static float WealthPerExtraRaider => Active != null ? Active.wealthPerExtraRaider : 200f;
        public static int MaxWealthRaiders => Active != null ? Active.maxWealthRaiders : 1;
        public static int MaxPartySize => Active != null ? Active.maxPartySize : 7;
        public static int MaxLootBase => Active != null ? Active.maxLootBase : 8;
        public static int MaxLootCap => Active != null ? Active.maxLootCap : 16;
    }
}
