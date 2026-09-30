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

        [Header("Bows (2026-09-30, docs/GDD.md \"Bows\")")]
        [Tooltip("Metres a defender with a bow shoots a raider from -- from behind the wall, or wherever he stands. Spear hands still close to jabReach.")]
        public float bowRange = 18f;
        [Tooltip("Metres a posted tower lookout with the camp's bow shoots from: higher up, farther.")]
        public float lookoutBowRange = 26f;
        [Tooltip("Real seconds between one archer's shots. Each shot spends one arrow.")]
        public float bowShotSeconds = 1.5f;
        [Tooltip("Chance one arrow hits (0..1). With bowDamage 1.5 an arrow is worth ~1 hp, so ~3 arrows drop a 3 hp raider -- in line with the landing volley's 2 arrows a raider.")]
        [Range(0f, 1f)] public float bowHitChance = 0.7f;
        [Tooltip("Damage one arrow that hits does (stone jab = 1, iron = 1.5).")]
        public float bowDamage = 1.5f;
        [Tooltip("Metres a second an arrow flies (show only; the hit lands when it arrives).")]
        public float arrowSpeed = 30f;

        [Header("Bows at sea (2026-09-30)")]
        [Tooltip("Metres from the ship the crew's bows reach an enemy hull.")]
        public float shipBowRange = 40f;
        [Tooltip("Real seconds between the crew's volleys. Each archer spends one arrow from the hold per volley.")]
        public float shipVolleySeconds = 3f;
        [Tooltip("Chance one arrow at sea hits the enemy hull (0..1).")]
        [Range(0f, 1f)] public float shipBowHitChance = 0.6f;
        [Tooltip("Hull damage one arrow hit does, as a share of one round shot (1 = a cannonball). Arrows add up: at 0.1, ten hits = one ball.")]
        public float shipArrowHullDamage = 0.1f;

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

        public static float BowRange => Active != null ? Active.bowRange : 18f;
        public static float LookoutBowRange => Active != null ? Active.lookoutBowRange : 26f;
        public static float BowShotSeconds => Mathf.Max(0.2f, Active != null ? Active.bowShotSeconds : 1.5f);
        public static float BowHitChance => Active != null ? Active.bowHitChance : 0.7f;
        public static float BowDamage => Active != null ? Active.bowDamage : 1.5f;
        public static float ArrowSpeed => Mathf.Max(5f, Active != null ? Active.arrowSpeed : 30f);
        public static float ShipBowRange => Active != null ? Active.shipBowRange : 40f;
        public static float ShipVolleySeconds => Mathf.Max(0.5f, Active != null ? Active.shipVolleySeconds : 3f);
        public static float ShipBowHitChance => Active != null ? Active.shipBowHitChance : 0.6f;
        public static float ShipArrowHullDamage => Active != null ? Active.shipArrowHullDamage : 0.1f;

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
