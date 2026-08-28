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

        /// Real seconds in one whole day. 180 while we are testing, so a full
        /// cycle fits inside a play session; the shipped target is 1440 (a
        /// minute an hour).
        public static float DayLength { get; set; } = 180f;

        public static bool Paused { get; set; }
        public static double Scale { get; set; } = 1.0;

        /// Whole days elapsed — the counter the settlement will eventually
        /// spend. Nothing consumes it yet; it exists so that when the Days
        /// pressure is built it does not need a new clock.
        public static int Day => DayLength > 0f ? (int)(Seconds / DayLength) : 0;

        /// 0..1 through the current day.
        public static float Time01
        {
            get
            {
                if (DayLength <= 0f) return 0.5f;
                double f = Seconds / DayLength;
                return (float)(f - System.Math.Floor(f));
            }
        }

        /// Advanced exactly once per frame by the owner (SkyDirector).
        public static void Advance(float deltaTime)
        {
            if (!Paused) Seconds += deltaTime * Scale;
        }

        /// Jump to an absolute time in seconds.
        public static void Scrub(double seconds) => Seconds = seconds;

        /// Jump to a time of day, keeping the day number. What probes and the
        /// contact sheets use.
        public static void SetTime01(float t)
        {
            int d = Day;
            Seconds = d * (double)DayLength + Mathf.Repeat(t, 1f) * DayLength;
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
