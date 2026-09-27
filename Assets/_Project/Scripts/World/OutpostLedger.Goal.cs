namespace SeaSick.World
{
    /// **A goal the player pinned, 2026-09-27.** Kevin, on goals beyond the
    /// campfire: *"let's try it."* One per camp: the next level of ONE
    /// building (by plan and ordinal, the same key `LevelOf(planId,
    /// ordinal)` reads), or the next copy of a plan. Empty = the camp's
    /// standing goal, the fire's next level.
    ///
    /// Saved with the ledger. An old save has no `pinnedGoal` in its JSON,
    /// and `JsonUtility` leaves the field at its constructed value -- an
    /// empty goal -- so an old save loads exactly as before.
    [System.Serializable]
    public class PinnedGoal
    {
        public const string Upgrade = "upgrade";
        public const string Build = "build";

        /// `Upgrade`, `Build`, or empty for none.
        public string kind;
        public string planId;
        /// Upgrade: which copy of `planId`. Build: unused.
        public int ordinal;
        /// Upgrade: the level it is done at. Build: the copies held when it
        /// was pinned -- done once there are more.
        public int target;

        public bool IsSet => !string.IsNullOrEmpty(kind) && !string.IsNullOrEmpty(planId);

        public bool Same(PinnedGoal o) =>
            o != null && o.kind == kind && o.planId == planId && (kind != Upgrade || o.ordinal == ordinal);
    }

    public partial class OutpostLedger
    {
        public PinnedGoal pinnedGoal = new PinnedGoal();

        /// The pinned goal, or null when none is set. A goal already met or
        /// no longer possible (its building gone) reads as none -- and is
        /// dropped, so the camp falls back to the fire's next level.
        public PinnedGoal ActiveGoal
        {
            get
            {
                var g = pinnedGoal;
                if (g == null || !g.IsSet) return null;
                bool done;
                if (g.kind == PinnedGoal.Upgrade)
                {
                    int i = RaisedIndexOf(g.planId, g.ordinal);
                    done = i < 0 || LevelAtRaised(i, g.planId) >= g.target;
                }
                else if (g.kind == PinnedGoal.Build) done = CopiesHeld(g.planId) > g.target;
                else done = true;
                if (!done) return g;
                pinnedGoal = new PinnedGoal();
                return null;
            }
        }

        /// Pin the next level of the building on `raised[raisedIndex]`.
        public void PinUpgrade(int raisedIndex, string planId)
        {
            int ord = OrdinalOfRaised(raisedIndex, planId);
            if (ord < 0) return;
            pinnedGoal = new PinnedGoal
            {
                kind = PinnedGoal.Upgrade, planId = planId, ordinal = ord,
                target = LevelAtRaised(raisedIndex, planId) + 1,
            };
        }

        /// Pin the next copy of `planId`.
        public void PinBuild(string planId)
        {
            if (string.IsNullOrEmpty(planId)) return;
            pinnedGoal = new PinnedGoal { kind = PinnedGoal.Build, planId = planId, target = CopiesHeld(planId) };
        }

        public void ClearGoal() => pinnedGoal = new PinnedGoal();

        /// Which copy of its plan `raised[raisedIndex]` is (0-based), or -1.
        public int OrdinalOfRaised(int raisedIndex, string planId)
        {
            if (raised == null || raisedIndex < 0 || raisedIndex >= raised.Count) return -1;
            if (raised[raisedIndex] == null || raised[raisedIndex].planId != planId) return -1;
            int k = 0;
            for (int i = 0; i < raisedIndex; i++)
                if (raised[i] != null && raised[i].planId == planId) k++;
            return k;
        }
    }
}
