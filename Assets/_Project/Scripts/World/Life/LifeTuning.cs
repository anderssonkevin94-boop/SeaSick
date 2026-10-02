using System;
using UnityEngine;

namespace SeaSick.World.Life
{
    /// <summary>
    /// **Death/rescue phase 1 knobs** (docs/PLAN-DEATH-RESCUE.md, "Deaths").
    /// Same load pattern as `SeaSick.World.Economy.EconomyTuning`: an asset
    /// at `Assets/_Project/Settings/Resources/LifeTuning.asset`, loaded with
    /// `Resources.Load` so the iOS build carries it with no scene reference.
    /// **Missing asset = the code's own defaults** below, so nothing breaks
    /// if it is deleted or fails to load.
    ///
    /// All numbers: first pass, tune by play.
    /// </summary>
    [CreateAssetMenu(menuName = "SeaSick/Life Tuning", fileName = "LifeTuning")]
    public class LifeTuning : ScriptableObject
    {
        [Header("Downed")]
        [Tooltip("Real seconds a downed hand has before he dies. Lenient starting value (docs: \"~3 min\").")]
        public float downedSeconds = 180f;

        [Header("Life log")]
        [Tooltip("Events kept per person before the oldest (lowest count / least recent) are dropped. Repeats of the same kind+camp+other increment count instead of appending, so this is a generous ceiling, not a normal length.")]
        public int maxEventsPerLife = 40;

        [Header("Rescue / drag (phase 2)")]
        [Tooltip("Metres a second a rescuer drags a downed hand. Slower than the ordinary walk (2.6): he is hauling a body, not himself.")]
        public float dragSpeed = 1.2f;

        [Tooltip("Game-days a recovering hand spends laid down before he stands back up. Starting value: about 2 real minutes at the default 180 s day (2 min = 120 s = 120/180 days).")]
        public float recoverDays = 120f / 180f;

        [Tooltip("Chance a kill (or the jab, on a miss) goes wrong and downs the hunter instead -- watched camps only, never in catch-up. Iron halves it.")]
        public float huntAccidentChance = 0.03f;

        [Header("Pout + floor (phase 4)")]
        [Tooltip("UNUSED since 2026-10-02: a pout is grief over a death and lasts half a sky day (OutpostLedger.GriefPoutSeconds); low mood no longer starts one.")]
        public float poutSeconds = 300f;

        [Tooltip("UNUSED since 2026-10-02: grief pouts have no cooldown (one per death).")]
        public float poutCooldownSeconds = 600f;

        [Tooltip("A camp never lets pouting drop the number of hands who are NOT downed/recovering/dragged/rescuing/pouting below this. A hand who would break the floor simply does not start pouting; he tries again next tick. **Also the floor for any future neglect outcome** (docs: \"a camp never drops below a minimum number of hands\") -- this one field is meant to gate whatever comes after pouting too, not just this phase.")]
        public int minHandsFloor = 2;

        // --- loading -----------------------------------------------------

        public const string ResourcePath = "LifeTuning";

        static LifeTuning active;
        static bool looked;

        /// The asset, or null when there is none (the code defaults then
        /// stand). Loaded once; safe to call from edit-mode tools.
        public static LifeTuning Active
        {
            get
            {
                if (looked) return active;
                looked = true;
                try { active = Resources.Load<LifeTuning>(ResourcePath); }
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

        public static float DownedSeconds => Active != null ? Active.downedSeconds : 180f;
        public static int MaxEventsPerLife => Active != null ? Active.maxEventsPerLife : 40;
        public static float DragSpeed => Active != null ? Active.dragSpeed : 1.2f;
        public static float RecoverDays => Active != null ? Active.recoverDays : 120f / 180f;
        public static float HuntAccidentChance => Active != null ? Active.huntAccidentChance : 0.03f;
        public static float PoutSeconds => Active != null ? Active.poutSeconds : 300f;
        public static float PoutCooldownSeconds => Active != null ? Active.poutCooldownSeconds : 600f;
        public static int MinHandsFloor => Active != null ? Active.minHandsFloor : 2;
    }
}
