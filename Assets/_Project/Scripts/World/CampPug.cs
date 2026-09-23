using UnityEngine;

namespace SeaSick.World
{
    /// **The camp companion, 2026-09-23.** Kevin: "keep her by the fire."
    /// Purely a visual, like `CampPiles` beside the store -- nothing in the
    /// ledger ever knows she exists, so she goes up through the same
    /// `Outpost.AfterRaised` hook the farm's beds use (`PlantFarmBeds`),
    /// fired once, the moment a campfire is actually RAISED (never for a
    /// ghost -- `BuildingFactory.Ghost` never reaches `AfterRaised` at all,
    /// it is built by `BuildSite`/`CampSiting` instead).
    ///
    /// Static pose, no animation, parented under the fire's own building
    /// root: when the fire goes (`Outpost.Demolish` destroys the building
    /// GameObject) she goes with it, with no teardown code needed here.
    public static class CampPug
    {
        const string PrefabPath = "AstraPlaytest/CampPug";
        /// Metres out from the fire.
        const float Offset = 1.6f;

        [RuntimeInitializeOnLoadMethod]
        static void Hook() => Outpost.SpawnCampPug = SpawnFor;

        static void SpawnFor(Building fire)
        {
            if (fire == null) return;
            var outpost = fire.GetComponentInParent<Outpost>();
            // Watched only -- she is a look, not a fact the game keeps, so
            // there is nothing to catch up on a camp nobody is standing at.
            if (outpost == null || !outpost.Watched) return;
            if (fire.transform.Find("CampPug") != null) return;   // once

            var asset = Resources.Load<GameObject>(PrefabPath);
            if (asset == null) return;

            Vector3 away = AwaySide(outpost, fire.transform.position);
            Vector3 at = fire.transform.position + away * Offset;
            if (Island.TerrainHeight != null) at.y = Island.TerrainHeight(at.x, at.z);

            var pug = Object.Instantiate(asset, at,
                Quaternion.LookRotation(-away, Vector3.up), fire.transform);
            pug.name = "CampPug";
        }

        /// Away from the nearest raised pier if the camp has one -- the pier
        /// is the water side, so the far side of the fire from it is dry
        /// land. No pier yet: a fixed direction (the outpost's own facing),
        /// which is at least stable rather than arbitrary per frame.
        static Vector3 AwaySide(Outpost outpost, Vector3 fireAt)
        {
            foreach (var b in outpost.Built)
            {
                if (b == null || b.GetComponent<Pier>() == null) continue;
                Vector3 d = fireAt - b.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f) return d.normalized;
            }
            return outpost.transform.forward;
        }
    }
}
