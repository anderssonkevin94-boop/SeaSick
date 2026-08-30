using UnityEngine;

namespace SeaSick.World
{
    /// A structure that has actually been raised. Carries its plan's effect
    /// so the village can total up what it has without a second ledger --
    /// the buildings on the ground ARE the record of what was built.
    public class Building : MonoBehaviour
    {
        [SerializeField] string id;
        [SerializeField] string label;
        [SerializeField] int storeCapacity;

        public string Id => id;
        public string Label => label;
        public int StoreCapacity => storeCapacity;

        public void Configure(BuildPlan plan)
        {
            id = plan.id;
            label = plan.label;
            storeCapacity = plan.storeCapacity;
        }
    }
}
