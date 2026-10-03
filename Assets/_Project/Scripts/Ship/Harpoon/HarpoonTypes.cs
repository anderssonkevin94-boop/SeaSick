using SeaSick.Crew;

namespace SeaSick.Ship.Harpoon
{
    /// Where the gun is in its cycle.
    /// Ready → Flying → (bite) Hooked → Ready, (miss) Returning → Ready,
    /// (snap or cut) Reloading → Ready.
    public enum HarpoonState { Ready, Flying, Hooked, Returning, Reloading }

    /// How the line reads at a glance: sag, straight, or creaking red.
    public enum TensionBand { Slack, Taut, Strained }

    /// **Who works the gun this moment.** `hand` is null for the captain.
    /// `workRate` scales the wind-up and the reel; `accuracy01` scales the
    /// lead error (1 = dead on). `atGun` false = the hand is still walking to
    /// the bow; the gun waits a little, then the captain fires it.
    public struct HarpoonCrew
    {
        public CrewAgent hand;
        public bool captain;
        public bool atGun;
        public float workRate;
        public float accuracy01;

        /// The fallback when nobody is free: slower, never crewless.
        public static HarpoonCrew Captain => new HarpoonCrew
        {
            hand = null,
            captain = true,
            atGun = true,
            workRate = HarpoonTuning.captainWorkRate,
            accuracy01 = 1f,
        };
    }

    /// **The crew seam** (PLAN-harpoon §5, §8.7). The villager side
    /// implements this and assigns `HarpoonGun.CrewSource`. `Man` is asked
    /// every frame while the gun has work (`HarpoonGun.Demand`); `Release`
    /// once it has been idle for `HarpoonTuning.releaseIdleSeconds`.
    public interface IHarpoonCrewSource
    {
        HarpoonCrew Man(HarpoonGun gun);
        void Release(HarpoonGun gun);
    }
}
