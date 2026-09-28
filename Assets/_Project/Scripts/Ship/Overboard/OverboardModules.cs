using System.Collections.Generic;
using SeaSick.Ship.Modular;
using SeaSick.World.Life;
using UnityEngine;

namespace SeaSick.Ship.Overboard
{
    /// **Overboard shipyard modules** (phase 8, docs/PLAN-DEATH-RESCUE.md
    /// "Shipyard modules"; shipyard-slots.md: player-filled slots, modules
    /// built free + instant). One place that answers "how many of catalog
    /// module X does the player's live ship have fitted right now" for the
    /// man-overboard/cargo-overboard gameplay code (`CrewAgent`, `Swimmer`,
    /// `RescueHud`) — so that code never touches `ShipyardService` or
    /// `SlotModel` directly.
    ///
    /// Reads through `SeaSick.Ship.Modular.SlotModel.FittedCounts`, which
    /// only understands the LIVE ship (`ShipyardService.Player.Current`) —
    /// this project has exactly one player ship at a time (see
    /// `RescueHud`'s own note: "cheap either way with one ship"). The counts
    /// are cached and only recomputed when the ship is actually refitted
    /// (`ShipyardService.Refitted`), because `ShipConfiguration.Clone()` is
    /// a JSON round-trip and this is read from crew Update loops.
    public static class OverboardModules
    {
        static ShipyardService trackedService;
        static Dictionary<string, int> counts = new Dictionary<string, int>();
        static bool dirty = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (trackedService != null) trackedService.Refitted -= OnRefitted;
            trackedService = null;
            counts = new Dictionary<string, int>();
            dirty = true;
        }

        static void OnRefitted(ShipConfiguration cfg) => dirty = true;

        static void Refresh()
        {
            var live = ShipyardService.Player;
            if (live != trackedService)
            {
                if (trackedService != null) trackedService.Refitted -= OnRefitted;
                trackedService = live;
                if (trackedService != null) trackedService.Refitted += OnRefitted;
                dirty = true;
            }
            if (!dirty) return;
            dirty = false;
            counts = trackedService != null ? SlotModel.FittedCounts(trackedService.Current) : new Dictionary<string, int>();
        }

        /// How many of catalog module `moduleId` (e.g. `ModuleCatalog.Lookout`)
        /// are fitted on the player's live ship right now.
        public static int Count(string moduleId)
        {
            Refresh();
            return counts.TryGetValue(moduleId, out var n) ? n : 0;
        }

        public static bool Has(string moduleId) => Count(moduleId) > 0;

        // ---- effect readouts (docs/PLAN-DEATH-RESCUE.md "Shipyard modules") ----

        /// Grip-drain multiplier from bulwarks + safety lines together
        /// (`CrewAgent.TrackGrip`). Bulwarks stack up to `bulwarksMaxStacks`;
        /// safety lines are `onePerShip` so this is 0 or 1.
        public static float GripDrainMultiplier()
        {
            int bulwarks = Mathf.Min(Count(ModuleCatalog.Bulwarks), OverboardTuning.BulwarksMaxStacks);
            float mul = Mathf.Pow(OverboardTuning.BulwarksDrainMultiplier, bulwarks);
            if (Has(ModuleCatalog.SafetyLines)) mul *= OverboardTuning.SafetyLinesDrainMultiplier;
            return mul;
        }

        /// Station work-rate multiplier (`CrewAgent.WorkRate01`) -- safety
        /// lines slow every hand's sails/oars/guns work while clipped on.
        public static float StationWorkRateMultiplier() =>
            Has(ModuleCatalog.SafetyLines) ? OverboardTuning.SafetyLinesWorkRateMultiplier : 1f;

        /// Extra throw reach, metres, from a fitted lifebuoy rack (`RescueHud`).
        public static float ThrowReachBonusMetres() =>
            Has(ModuleCatalog.LifebuoyRack) ? OverboardTuning.LifebuoyReachBonusMetres : 0f;

        /// Swim-timer seconds the buoy buys, ONCE, the moment "Throw line"
        /// is pressed (`CrewAgent.StartHaul`) -- 0 if no lifebuoy rack is fitted.
        public static float ThrowLifebuoyBonusSeconds() =>
            Has(ModuleCatalog.LifebuoyRack) ? OverboardTuning.LifebuoyTimerBonusSeconds : 0f;

        /// Haul-time multiplier from a fitted scramble net (`CrewAgent`'s
        /// haul -- shared by swimmers and floating cargo alike).
        public static float HaulTimeMultiplier() =>
            Has(ModuleCatalog.ScrambleNet) ? OverboardTuning.ScrambleNetHaulMultiplier : 1f;

        /// Extra real seconds on the rail-hold warning from a fitted lookout
        /// (`CrewAgent.TrackRailSafety`).
        public static float WarnSecondsBonus() =>
            Has(ModuleCatalog.Lookout) ? OverboardTuning.LookoutWarnSecondsBonus : 0f;

        /// Extra seconds on every swimmer's timer, always, from a fitted
        /// lookout (`Swimmer.Spawn`).
        public static float SwimSecondsBonus() =>
            Has(ModuleCatalog.Lookout) ? OverboardTuning.LookoutSwimSecondsBonus : 0f;

        /// A jolly boat is fitted AND the ship is big enough to carry one
        /// ("big ships only" -- gated here by middle-section count, on top
        /// of the module's own high `dockLevel`, since dock-level
        /// enforcement is still data-only -- see `ShipyardService.DockLevel`).
        public static bool JollyBoatAvailable()
        {
            if (!Has(ModuleCatalog.JollyBoat)) return false;
            var svc = ShipyardService.Player;
            var cfg = svc != null ? svc.Current : null;
            return cfg != null && cfg.middleIds != null
                && cfg.middleIds.Count >= OverboardTuning.JollyBoatMinMiddleSections;
        }
    }
}
