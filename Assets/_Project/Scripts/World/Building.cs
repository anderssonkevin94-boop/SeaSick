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
        [SerializeField] BuildKind kind;
        [SerializeField] Vector2 footprint;

        public string Id => id;
        public BuildKind Kind => kind;
        public string Label => label;
        public int StoreCapacity => storeCapacity;

        /// `BuildPlan.footprint` (x along the ridge, z across it), kept
        /// here so a groundcover re-clear after a re-dress -- see
        /// `TerrainWorldPopulator.Redress` -- does not need the plan table
        /// again, just the buildings already standing.
        public Vector2 Footprint => footprint;

        /// **Which model it wears now (2026-10-01)** -- the Resources path of
        /// its `Model` child: the plan's own prefab, or a level's
        /// `BuildingLevelLook.prefabOverride` once `BuildingFactory.ShowLevel`
        /// has swapped it. Null for an extruded building. `BuildingSolids`
        /// reads its boxes by this. Not saved: the level is, and the load
        /// swaps the model again.
        public string ModelPrefab { get; internal set; }

        /// Bumped on every model swap, so anything holding the old model's
        /// marks (a lookout on the deck, a cached stand) can tell.
        public int ModelRevision { get; internal set; }

        public void Configure(BuildPlan plan)
        {
            id = plan.id;
            label = plan.label;
            storeCapacity = plan.storeCapacity;
            kind = plan.kind;
            footprint = plan.footprint;
        }
    }
}
