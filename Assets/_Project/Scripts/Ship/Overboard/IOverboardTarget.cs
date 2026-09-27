using UnityEngine;

namespace SeaSick.Ship.Overboard
{
    /// **Anything that can be lost over the side and hauled back in**
    /// (phase 6, docs/PLAN-DEATH-RESCUE.md "Man overboard", last bullet:
    /// "Cargo goes overboard too... the same pickup... brings it back").
    /// `Swimmer` (5a/5b) and `FloatingCargo` (6) both implement this so
    /// `RescueHud` (ring, edge arrow, tap-to-steer, Throw line) and
    /// `CrewAgent`'s haul (`StartHaul`/`TickHaul`/`EndHaul`) work on either
    /// one without caring which.
    public interface IOverboardTarget
    {
        /// The GameObject's own transform — what `HelmInput.SteerToward`
        /// points the autopilot at, and what a Unity "fake null" check
        /// (`!= null` on a destroyed object) still works through.
        Transform Transform { get; }

        Vector3 WorldPosition { get; }

        /// 0..1, for the countdown ring / edge-arrow arc. A target with no
        /// clock of its own (there isn't one yet) can just return 1.
        float TimeLeft01 { get; }

        /// Name for the tap-to-steer readout ("steering to Anna") and the
        /// Throw line button ("Throw line to Anna" / "Throw line to the
        /// timber").
        string Label { get; }

        /// True once this target's fate is decided (rescued, sunk, washed
        /// ashore, lost) — `RescueHud`/`CrewAgent` drop it the same frame.
        bool Resolved { get; }

        /// **The ship's own hull**, for `RescueHud`'s reach check and
        /// `CrewAgent.StartHaul`'s "which side is she on".
        Transform Hull { get; }

        /// **Set by `CrewAgent.TickHaul`** every frame while a haul holds
        /// this target — pulls it toward `HaulAnchor` instead of drifting on
        /// its own.
        bool BeingHauled { get; set; }
        Vector3 HaulAnchor { get; set; }

        /// People first, then cargo, when several targets are in reach at
        /// once (build brief item 1) — lower sorts first.
        int RescuePriority { get; }

        /// The nearest point on the hull's SIDE (rail), not its centre —
        /// same shape as `Swimmer.NearestHullSide`, used for the reach check
        /// and for where the haul happens.
        Vector3 NearestHullSide();

        /// **Called once `CrewAgent.TickHaul` finishes hauling this
        /// target in.** `Swimmer.Rescue` and `FloatingCargo.Recover` both
        /// implement the outcome; the haul itself doesn't care which.
        void OnHauled(string rescuerName);
    }
}
