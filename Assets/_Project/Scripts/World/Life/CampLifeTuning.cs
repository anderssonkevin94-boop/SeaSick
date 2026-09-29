using System;
using UnityEngine;

namespace SeaSick.World.Life
{
    /// <summary>
    /// **Villagers with a day (2026-09-28).** Every knob for the evening/
    /// sleep/speech routine, same load pattern as `LifeTuning`: an asset at
    /// `Assets/_Project/Settings/Resources/CampLifeTuning.asset`, loaded
    /// with `Resources.Load` so the iOS build carries it with no scene
    /// reference. **Missing asset = the code's own defaults** below.
    ///
    /// All numbers: first pass, tune by play.
    /// </summary>
    [CreateAssetMenu(menuName = "SeaSick/Camp Life Tuning", fileName = "CampLifeTuning")]
    public class CampLifeTuning : ScriptableObject
    {
        // Kevin, 2026-09-29, after the sky day went to 8 min: "villagers sleep
        // through the night, its too much dead game play" -- he picked short
        // nights: evening 22-23, asleep 23-01 (3 h off, was 21-04 = 7 h).
        // AwakeWorkScale keeps the day's output the same.
        [Header("The day (local hour, 0..24)")]
        [Tooltip("Hands not on an urgent job start walking to the fire from here.")]
        public float eveningStartHour = 22f;
        [Tooltip("Hands with a hut go inside; the rest lie by the fire.")]
        public float sleepHour = 23f;
        [Tooltip("Hands come out of their huts and resume the ordinary dispatch (idle-hand ladder etc.).")]
        public float wakeHour = 1f;

        [Header("Output neutrality")]
        [Tooltip("A whole day's production is unchanged: awake hours are scaled up by 24/awakeHours so the same per-day total comes out of fewer working hours. Read this, never hand-set it.")]
        public float AwakeHours => Mathf.Max(0.01f, eveningStartHour - wakeHour);

        [Header("Evening at the fire")]
        [Tooltip("Metres the ring around the campfire is drawn at.")]
        public float fireRingRadius = 3.2f;
        [Tooltip("Degrees a hand's sway rocks through when singing (fed camp only).")]
        public float swayDegrees = 6f;
        [Tooltip("Sway cycles a real second.")]
        public float swayHz = 0.35f;

        [Header("Sleep")]
        [Tooltip("Metres from a hut's door/the fire that counts as arrived.")]
        public float bedArriveMetres = 1.0f;

        [Header("Speech lines (watched camp only)")]
        [Tooltip("Real seconds a line stays up before it fades.")]
        public float lineSeconds = 3.5f;
        [Tooltip("Real seconds between one camp's lines, on average.")]
        public float lineEverySeconds = 30f;
        [Tooltip("+/- real seconds of jitter added to lineEverySeconds so camps do not all talk on the same beat.")]
        public float lineJitterSeconds = 10f;
        [Tooltip("World-space height above a villager's head the line is drawn at.")]
        public float lineHeight = 2.1f;
        [Tooltip("Line text size (world units, TextMesh characterSize).")]
        public float lineCharSize = 0.14f;

        // --- loading -----------------------------------------------------

        public const string ResourcePath = "CampLifeTuning";

        static CampLifeTuning active;
        static bool looked;

        public static CampLifeTuning Active
        {
            get
            {
                if (looked) return active;
                looked = true;
                try { active = Resources.Load<CampLifeTuning>(ResourcePath); }
                catch (Exception) { active = null; }
                return active;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { looked = false; active = null; }

        public static float EveningStartHour => Active != null ? Active.eveningStartHour : 22f;
        public static float SleepHour => Active != null ? Active.sleepHour : 23f;
        public static float WakeHour => Active != null ? Active.wakeHour : 1f;
        public static float FireRingRadius => Active != null ? Active.fireRingRadius : 3.2f;
        public static float SwayDegrees => Active != null ? Active.swayDegrees : 6f;
        public static float SwayHz => Active != null ? Active.swayHz : 0.35f;
        public static float BedArriveMetres => Active != null ? Active.bedArriveMetres : 1.0f;
        public static float LineSeconds => Active != null ? Active.lineSeconds : 3.5f;
        public static float LineEverySeconds => Active != null ? Active.lineEverySeconds : 30f;
        public static float LineJitterSeconds => Active != null ? Active.lineJitterSeconds : 10f;
        public static float LineHeight => Active != null ? Active.lineHeight : 2.1f;
        public static float LineCharSize => Active != null ? Active.lineCharSize : 0.14f;

        /// Hand-days-per-day work scale during the awake window, so a
        /// shorter working day still produces a full day's output.
        /// `24 / (eveningStartHour - wakeHour)`.
        public static float AwakeWorkScale
        {
            get
            {
                float hours = Mathf.Max(0.01f, EveningStartHour - WakeHour);
                return 24f / hours;
            }
        }

        public enum RoutinePhase { Awake, Evening, Sleep }

        /// **The routine phase for a local hour, 0..24.** Assumes
        /// `wakeHour < eveningStartHour < sleepHour` (true of the defaults;
        /// no midnight wrap to handle for the sensible ranges this ever
        /// ships with), except sleep itself always wraps past midnight back
        /// round to `wakeHour`.
        public static RoutinePhase PhaseAtHour(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            float wake = WakeHour, evening = EveningStartHour, sleep = SleepHour;
            if (hour >= wake && hour < evening) return RoutinePhase.Awake;
            if (hour >= evening && hour < sleep) return RoutinePhase.Evening;
            return RoutinePhase.Sleep; // sleep..24 and 0..wake
        }

        public static bool IsAwakeHour(float hour) => PhaseAtHour(hour) == RoutinePhase.Awake;
    }
}
