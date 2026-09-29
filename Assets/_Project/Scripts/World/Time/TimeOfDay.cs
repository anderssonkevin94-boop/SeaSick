using UnityEngine;

namespace SeaSick.World
{
    /// The clock the sky reads. Deliberately built like OceanTime: a static
    /// that one owner advances once per frame, and that can be paused and
    /// scrubbed.
    ///
    /// That is not tidiness, it is a hard requirement. Every visual test in
    /// this project — ShaderStrip, SpawnShot, StormShot, any A/B of the water
    /// — is only comparable if the lighting is identical between frames, and
    /// the moment the sun moves on its own, two screenshots of the same sea
    /// stop being two screenshots of the same thing. The ocean already learnt
    /// this once; the sky does not get to learn it again.
    ///
    /// 0.00 = midnight, 0.25 = sunrise, 0.50 = noon, 0.75 = sunset.
    public static class TimeOfDay
    {
        /// Seconds since the start of day 0. Double for the same reason
        /// OceanTime is: a float loses resolution over a long session.
        public static double Seconds { get; private set; }

        /// **Real seconds in one whole SKY day** -- the sun, the "Day N"
        /// counter, and what villagers eat and how their mood drifts per day.
        /// One source of truth: `Economy.EconomyFeel.dayLengthSeconds` (480 s
        /// since 2026-09-29, Kevin: "the food production cant keep up with
        /// how fast the day cycles go"; 180 before). The setter writes that
        /// dial. A change, from here or from the FEEL panel mid-game, is
        /// re-anchored so the sun and the day counter do not jump
        /// (`Reanchor`), and `Seconds` never moves.
        ///
        /// **Production does NOT run on this.** Every rate the ledger books
        /// "per day" is per `WorkDaySeconds` (a fixed 180 real seconds), so
        /// crops, benches, hammering, walking and gathering keep their real
        /// time at any day length.
        public static float DayLength
        {
            get
            {
                float d = Economy.EconomyFeel.dayLengthSeconds;
                if (!(d > 1f)) d = 1f;
                if (d != appliedDayLength) Reanchor(d);
                return d;
            }
            set
            {
                float v = Mathf.Max(1f, value);
                if (v != appliedDayLength) Reanchor(v);
                Economy.EconomyFeel.dayLengthSeconds = v;
            }
        }

        /// **The ledger's day: 180 real seconds, fixed.** Every "per day"
        /// production rate (`TimberPerHandPerDay`, recipe `ratePerDay`,
        /// `QuantumDays`, crop `GrowDays`, build hand-days ...) was priced
        /// against a 180 s day, and stays priced against it: the sky day
        /// got longer on 2026-09-29, the work did not get slower. Saves
        /// store ledger progress in these days, so this must never change.
        public const float WorkDaySeconds = 180f;

        /// Sky days per work day: what a daily NEED (eating, mood drift)
        /// multiplies a ledger step by, so hunger follows the sun and not the
        /// work clock. 1 at a 180 s day, 0.375 at 480 s.
        public static float SkyDaysPerWorkDay => WorkDaySeconds / DayLength;

        /// The day length before it moved to a FEEL dial. A save written
        /// before `SaveData.calendarDays` existed was at this length, which
        /// is what its day number and time of day are recovered with.
        public const float LegacyDayLength = 180f;

        // The calendar is anchored, not `Seconds / DayLength`: at
        // `anchorSeconds` the calendar read `anchorDays` (fractional days),
        // and it runs at the current `DayLength` from there. A day-length
        // change re-anchors at the present instant, so `Time01` and `Day`
        // are continuous; `Seconds` (every timer and ledger stamp in the
        // game hangs off it) is untouched.
        static double anchorSeconds, anchorDays;
        static float appliedDayLength = -1f;   // -1: nothing applied yet, anchor (0, 0)

        static void Reanchor(float newLength)
        {
            if (appliedDayLength > 0f)
            {
                anchorDays += (Seconds - anchorSeconds) / appliedDayLength;
                anchorSeconds = Seconds;
            }
            appliedDayLength = newLength;
        }

        /// Fractional calendar days at an absolute clock position.
        public static double DaysAt(double seconds)
        {
            float dl = DayLength;   // re-anchors first if the dial moved
            return anchorDays + (seconds - anchorSeconds) / dl;
        }

        /// Fractional calendar days now (day index + time of day).
        public static double CalendarDays => DaysAt(Seconds);

        /// Time of day (0..1) at an absolute clock position -- what the
        /// ledger's steps read for their own simulated instant.
        public static float Time01At(double seconds)
        {
            double f = DaysAt(seconds);
            return (float)(f - System.Math.Floor(f));
        }

        /// Set the clock AND the calendar: `seconds` on the clock reads
        /// `calendarDays` on the calendar. What a boot and a save load use.
        public static void SetClock(double seconds, double calendarDays)
        {
            float dl = DayLength;
            Seconds = seconds;
            anchorSeconds = seconds;
            anchorDays = System.Math.Max(0.0, calendarDays);
            appliedDayLength = dl;
        }

        public static bool Paused { get; set; }
        public static double Scale { get; set; } = 1.0;

        /// Whole days elapsed — the counter the settlement will eventually
        /// spend. Nothing consumes it yet; it exists so that when the Days
        /// pressure is built it does not need a new clock.
        public static int Day => (int)System.Math.Floor(System.Math.Max(0.0, CalendarDays));

        /// 0..1 through the current day.
        public static float Time01 => Time01At(Seconds);

        /// Advanced exactly once per frame by the owner (SkyDirector).
        public static void Advance(float deltaTime)
        {
            if (!Paused) Seconds += deltaTime * Scale;
        }

        /// Jump to an absolute time in seconds. The calendar keeps its
        /// anchor, so a relative jump of one `DayLength` is one day.
        public static void Scrub(double seconds) => Seconds = seconds;

        /// Jump to a time of day, keeping the day number. What probes and the
        /// contact sheets use.
        public static void SetTime01(float t)
        {
            int d = Day;
            float dl = DayLength;
            Seconds = anchorSeconds + (d + (double)Mathf.Repeat(t, 1f) - anchorDays) * dl;
        }

        /// Local hour, for anything that wants to say the time out loud.
        public static float Hour => Time01 * 24f;

        // ---- where the sun and moon are ------------------------------------

        /// Unit vector TOWARD the sun, in world space, at a time of day.
        ///
        /// North is +Z in this world and east is +X, so the arc is built
        /// directly in world vectors rather than through azimuth/elevation
        /// angles — there is no sign convention left to get wrong, and each of
        /// the four cardinal instants can be read straight off the formula:
        ///
        ///   t = 0.25  ->  ( 1,  0,  0)   due east, on the horizon: sunrise
        ///   t = 0.50  ->  ( 0,  c, -s)   highest, bearing south
        ///   t = 0.75  ->  (-1,  0,  0)   due west, on the horizon: sunset
        ///   t = 0.00  ->  ( 0, -c,  s)   below the world: midnight
        ///
        /// `latitude` tilts the whole arc toward the south, so the sun peaks
        /// at (90 - latitude) degrees instead of passing straight overhead —
        /// which is what gives long shadows and a sun that stays in frame for
        /// a camera looking at the skyline.
        public static Vector3 SunDirection(float t, float latitudeDeg)
        {
            float theta = (t - 0.25f) * 2f * Mathf.PI;
            float lat = latitudeDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
            return new Vector3(c, s * Mathf.Cos(lat), -s * Mathf.Sin(lat));
        }

        /// Unit vector toward the moon. Roughly opposite the sun, but running
        /// slow: a synodic month of days means it rises about fifty minutes
        /// later each day, so it is not simply "the sun at night" and the
        /// night sky is different from one voyage to the next.
        public static Vector3 MoonDirection(float t, float latitudeDeg, int day, float synodicDays)
        {
            float lag = synodicDays > 0.01f ? day / synodicDays : 0f;
            return SunDirection(t + 0.5f + lag, latitudeDeg);
        }

        /// 0 = new moon, 1 = full. Drives how much light it gives and how
        /// bright the halo is drawn.
        ///
        /// The sign matters and was wrong once. MoonDirection puts the moon at
        /// `t + 0.5 + lag`, so at lag = 0 the moon is OPPOSITE the sun — which
        /// is a FULL moon, not a new one. The shader draws the terminator from
        /// the two direction vectors, so a phase that disagreed with them gave
        /// a fully lit disc lighting the world like a new moon, with nothing
        /// reporting a problem. +cos, so lag 0 -> 1 (full) and lag 0.5 -> 0
        /// (new, moon between us and the sun).
        public static float MoonPhase01(int day, float synodicDays)
        {
            float lag = synodicDays > 0.01f ? day / synodicDays : 0f;
            return 0.5f + 0.5f * Mathf.Cos(lag * 2f * Mathf.PI);
        }
    }
}
