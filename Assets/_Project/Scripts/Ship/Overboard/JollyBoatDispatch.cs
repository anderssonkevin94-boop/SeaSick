using SeaSick.Crew;
using SeaSick.World.Life;
using UnityEngine;

namespace SeaSick.Ship.Overboard
{
    /// **Decides when the jolly boat launches** (phase 8 shipyard module).
    /// Ticked from `CrewRoster.Update` — one call per ship per frame, same
    /// spirit as `FirstOverboard.Tick`/`CargoLashing.Tick` right next to it.
    /// Only one boat out at a time (this project has one player ship; see
    /// `OverboardModules`'s own note on that).
    public static class JollyBoatDispatch
    {
        static JollyBoat active;

        /// `roster.transform` is the hull; `motor` (may be null) gives the
        /// ship's own speed for the "slow enough to launch" gate.
        public static void Tick(CrewRoster roster, ShipMotor motor, float dt)
        {
            if (roster == null || active != null) return;
            if (motor != null && motor.CurrentSpeed > OverboardTuning.JollyBoatMaxShipSpeed) return;
            if (!OverboardModules.JollyBoatAvailable()) return;

            var target = PickTarget(roster.transform);
            if (target == null) return;

            var hand = PickAvailableHand(roster);
            if (hand == null || !hand.BeginJollyBoatDuty()) return;

            active = JollyBoat.Launch(roster.transform, hand, target);
        }

        /// Called by `JollyBoat.Finish` once she's back alongside.
        public static void Cleared(JollyBoat boat)
        {
            if (active == boat) active = null;
        }

        /// People before cargo (same rule `RescueHud`/`IOverboardTarget`
        /// already keep) — a swimmer belonging to THIS hull, not already
        /// mid-haul or mid-tow.
        static IOverboardTarget PickTarget(Transform hull)
        {
            foreach (var s in Swimmer.All)
                if (s != null && !s.Resolved && !s.BeingHauled && s.Hull == hull) return s;
            foreach (var c in FloatingCargo.All)
                if (c != null && !c.Resolved && !c.BeingHauled && c.Hull == hull) return c;
            return null;
        }

        static CrewAgent PickAvailableHand(CrewRoster roster)
        {
            foreach (var c in roster.All) if (c != null && c.Available) return c;
            return null;
        }
    }
}
