using SeaSick.World;
using SeaSick.World.Economy;

namespace SeaSick.UI.Sheets
{
    /// **"Set as goal", 2026-09-27.** Kevin: *"let's try it."* The one
    /// door every "Set as goal" button goes through -- the station page's
    /// upgrade card today, the build list's cards next. The goal lives in
    /// the camp's ledger (`OutpostLedger.pinnedGoal`, saved with it); the
    /// camp overview and the Next card above the thumb bar ("YOUR GOAL") read it through
    /// `GoalChain.ForCamp`.
    public static class GoalPin
    {
        /// Bumped on every change, so a view can re-key without polling.
        public static int Version { get; private set; }

        /// Pin a goal on `camp`. An unset goal clears.
        public static void Set(Outpost camp, PinnedGoal goal)
        {
            var l = camp != null ? camp.Ledger : null;
            if (l == null) return;
            l.pinnedGoal = goal != null && goal.IsSet ? goal : new PinnedGoal();
            Version++;
        }

        /// Pin THIS building's next level (`raised[raisedIndex]`).
        public static void SetUpgrade(Outpost camp, int raisedIndex, string planId)
        {
            var l = camp != null ? camp.Ledger : null;
            if (l == null) return;
            l.PinUpgrade(raisedIndex, planId);
            Version++;
        }

        /// Pin the next copy of `planId` (a build-list card).
        public static void SetBuild(Outpost camp, string planId)
        {
            var l = camp != null ? camp.Ledger : null;
            if (l == null) return;
            l.PinBuild(planId);
            Version++;
        }

        /// Back to the camp's standing goal, the fire's next level.
        public static void Clear(Outpost camp)
        {
            var l = camp != null ? camp.Ledger : null;
            if (l == null) return;
            l.ClearGoal();
            Version++;
        }

        public static PinnedGoal Get(Outpost camp) => camp != null && camp.Ledger != null ? camp.Ledger.ActiveGoal : null;

        public static bool IsUpgradePinned(Outpost camp, int raisedIndex, string planId)
        {
            var g = Get(camp);
            return g != null && g.kind == PinnedGoal.Upgrade && g.planId == planId
                   && g.ordinal == camp.Ledger.OrdinalOfRaised(raisedIndex, planId);
        }

        public static bool IsBuildPinned(Outpost camp, string planId)
        {
            var g = Get(camp);
            return g != null && g.kind == PinnedGoal.Build && g.planId == planId;
        }

        /// The chain the camp is working toward right now.
        public static GoalChain Chain(Outpost camp) =>
            GoalChain.ForCamp(camp != null ? camp.Ledger : null);
    }
}
