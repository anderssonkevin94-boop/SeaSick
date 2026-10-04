using UnityEngine;

namespace SeaSick.Ship
{
    /// **How many hammocks she has, and whether one is free** (2026-10-04).
    ///
    /// Kevin's phone, docking at home: "No free berth · no berth aboard —
    /// she carries 5" on a steamer whose refit gave her 10 bunks.
    /// `Outpost.BerthRefusal` read the ladder brig's `Shipyard.Berths`
    /// (quarters cells x 1). On the steamer that yard is stood down and never
    /// gets a cell, so it said 0, the floor made it 1, and every carry-aboard
    /// and castaway pickup was refused with anyone at all aboard.
    ///
    /// The steamer's berths are her modular plan's `capacity.crewStations`
    /// (the shipyard report's "Crew berths"); the untouched standard steamer
    /// has the reference ship's. The rung count is for the ladder brig only.
    /// Pure arithmetic so `CastawayFixSelfTest` can run it outside the editor.
    public static class CrewBerths
    {
        /// The modular steamer's berths: the plan she is drawn from, or the
        /// reference ship's when she is the untouched standard steamer.
        public static int OfPlan(SeaSick.Ship.Modular.ShipyardPlan plan) =>
            plan != null ? Mathf.Max(1, plan.capacity.crewStations)
                         : SeaSick.Ship.Modular.ShipyardPlanner.ReferenceCrewStations;

        /// The ladder brig's berths: quarters cells, never fewer than the
        /// helmsman's one.
        public static int OfRung(int rungBerths) => Mathf.Max(1, rungBerths);

        /// Null when a hammock is free; otherwise the sentence the Hand's
        /// prompt and the castaway card show. Says the CAPACITY, because that
        /// is the number the player can change (buy bunks) and the number
        /// that can be checked against the top bar's head count.
        public static string Refusal(int aboard, int berths) =>
            aboard >= berths ? "no berth aboard — all " + berths + " taken" : null;
    }
}
