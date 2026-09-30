using UnityEngine;
using SeaSick.Crew;
using SeaSick.World.Life;

namespace SeaSick.Ship.Overboard
{
    /// **The scripted first man-overboard** (build brief item 8). Ticked
    /// once a frame from `CrewRoster.Update` (one roster == one ship's worth
    /// of bookkeeping, the natural place for "how long have WE been sailing
    /// calmly" to live).
    ///
    /// Until `Done`, nobody's grip is even allowed to reach a warning
    /// (`CrewAgent.TrackRailSafety` checks `Blocks`) except the one agent
    /// this class has chosen and force-triggered. Saved as
    /// `SaveData.firstOverboardDone` (see `SaveGame`).
    public static class FirstOverboard
    {
        public static bool Done;
        static bool triggered;
        static CrewAgent chosen;
        static float sailingSeconds;

        /// True while `agent` must not be allowed to organically warn/fall.
        /// The chosen agent, once forced, is exempt so his own sequence can
        /// run to its warning and fall like anybody else's.
        public static bool Blocks(CrewAgent agent) => !Done && !(triggered && chosen == agent);

        /// **2026-09-30 Kevin: the first man overboard comes from the first
        /// collision or enemy hit that throws a hand over**, not from sailing
        /// for 90 s. `CrewAgent.ThrownOverboard` asks here; the first caller
        /// gets true (a long swim timer, `Done` set once he is rescued), and
        /// nobody after that until the run is over.
        public static bool ClaimScripted()
        {
            if (Done || triggered) return false;
            triggered = true;
            return true;
        }

        public static void Tick(CrewRoster roster, AnchorController anchor, float dt)
        {
            if (Done || triggered) return;
            // Timed trigger is off by default (`OverboardTuning.firstTimeByTimer`).
            if (!OverboardTuning.FirstTimeByTimer) return;
            // Total time under way, NOT reset by a rough patch or an anchor
            // stop: Kevin, 2026-09-28, *"i sailed around for over 90 seconds
            // and no one fell off"* -- ordinary sailing sits at roughness
            // 0.2-0.37 and every crossing of the calm line used to zero the
            // clock, so it never got there. Now the clock only counts, and
            // the calm check just picks the MOMENT once the time is served.
            if (!Sailing.IsLive(anchor)) return;
            sailingSeconds += dt;
            if (sailingSeconds < OverboardTuning.FirstTimeSailSeconds) return;

            var meter = roster != null ? roster.GetComponent<SmoothnessMeter>() : null;
            float rough = meter != null ? meter.Roughness01 : 0f;
            // Wait for a calm moment, but not forever: a minute past the
            // mark it happens anyway (the long first-time timer makes up
            // for the rougher water).
            if (rough > OverboardTuning.FirstTimeMaxRoughness
                && sailingSeconds < OverboardTuning.FirstTimeSailSeconds + 60f) return;

            var all = roster != null ? roster.All : System.Array.Empty<CrewAgent>();
            CrewAgent worst = null;
            foreach (var c in all)
            {
                if (c == null || !c.Available) continue;
                if (worst == null || c.Sickness01 > worst.Sickness01) worst = c;
            }
            if (worst == null) return;

            chosen = worst;
            triggered = true;
            chosen.ForceOverboardSequence(scripted: true);
        }
    }
}
