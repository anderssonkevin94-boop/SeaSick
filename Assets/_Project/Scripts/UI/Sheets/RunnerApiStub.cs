// TEMPORARY stub for the runner API; the coordinator deletes this file when World/OutpostLedger.Runners.cs lands.
using System.Collections.Generic;

namespace SeaSick.World
{
    public partial class OutpostLedger
    {
        /// A runner is a Work hand posted at the Storage building.
        public static bool IsRunner(OutpostHand h) =>
            h != null && h.order == OutpostOrder.Work && h.target == BuildPlans.Storage.id;

        /// Total runner capacity over the raised storage copies.
        public int RunnerSlots() => CountBuilt(BuildPlans.Storage.id) * 2;

        public int RunnerCount()
        {
            int n = 0;
            if (hands != null) foreach (var h in hands) if (IsRunner(h)) n++;
            return n;
        }

        public int IdleCount()
        {
            int n = 0;
            foreach (var _ in IdleHands()) n++;
            return n;
        }

        /// Hands with no job at all, in the ledger's order.
        public IEnumerable<OutpostHand> IdleHands()
        {
            if (hands == null) yield break;
            foreach (var h in hands)
                if (h != null && !h.Busy && !Reserve(h) && h.order == OutpostOrder.Idle) yield return h;
        }

        public bool RunnerWaiting(OutpostHand h) => IsRunner(h) && !h.Hauling;
    }
}
