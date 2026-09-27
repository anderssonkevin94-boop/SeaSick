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

        public static void Tick(CrewRoster roster, AnchorController anchor, float dt)
        {
            if (Done || triggered) return;
            if (!Sailing.IsLive(anchor)) { sailingSeconds = 0f; return; }

            var meter = roster != null ? roster.GetComponent<SmoothnessMeter>() : null;
            float rough = meter != null ? meter.Roughness01 : 0f;
            if (rough > OverboardTuning.FirstTimeMaxRoughness) { sailingSeconds = 0f; return; }

            sailingSeconds += dt;
            if (sailingSeconds < OverboardTuning.FirstTimeSailSeconds) return;

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
