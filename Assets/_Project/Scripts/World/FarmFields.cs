using UnityEngine;

namespace SeaSick.World
{
    /// The seam between a raised farm and the wheat: a farm that stands
    /// plants its beds beside the building and gives the ledger a field
    /// to harvest from, so a farmhand has standing wheat to take and the
    /// player can see it grow back.
    public static class FarmFields
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Wire()
        {
            Outpost.PlantFarmBeds = b =>
            {
                if (b == null) return;
                var plan = BuildPlans.Farm;
                if (plan.beds <= 0) return;
                var isle = Island.Nearest(b.transform.position);
                if (isle == null) return;

                // The kit's own bed slots when it has them; a square patch
                // off the building's front otherwise (the shipped farm_01
                // wears four crop modules, the plan says six).
                var slots = BuildingFactory.BedSlotsOf(b.transform);
                Vector3 at = slots.Count > 0
                    ? slots[slots.Count / 2].position
                    : b.transform.position + b.transform.forward * (plan.footprint.y * 0.5f + 3f);
                float yaw = b.transform.eulerAngles.y;
                Terrain.SceneryCrops.Plant(isle, at, yaw, plan.beds);

                var o = Outpost.Of(isle);
                if (o != null && o.Ledger != null) o.Ledger.AddField(plan);
            };
        }
    }
}
