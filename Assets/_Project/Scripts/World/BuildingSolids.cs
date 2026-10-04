using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **What part of a standing building a villager cannot walk through
    /// (2026-10-01).** Kevin: *"buildings don't have a collider and villagers
    /// walk straight through them. give them hitboxes that make sense while
    /// still allowing workers to get to and from their stations / collecting
    /// / drop off points."*
    ///
    /// Not colliders (`BuildingFactory.Dress` strips those on purpose, and
    /// nobody here walks by physics): a few flat boxes per building in its
    /// own frame, read by `CampPath` for its grid (`RelayBuildings`) and its
    /// per-step guard (`Obstructs`). A kit model's boxes are baked from its
    /// real benches, racks, hearths and posts (`Dev/Editor/BuildingSolidsBake`
    /// -> `BuildingSolids.Baked.cs`), so the lanes between them -- the mill's
    /// rear gate, the forge's front, the kitchen's back -- stay open. A
    /// building with no baked model (the extruded storehouse) blocks its
    /// footprint less 0.2 m a side; its spots are all outside the footprint
    /// (`CampWorker.EdgeBeyond`, `WorkSpot`). Piers and dry docks are walked
    /// on, never round. Nothing here is saved.
    public static partial class BuildingSolids
    {
        /// Fallback inset from an unbaked footprint's edge, metres.
        const float FootprintInset = 0.2f;

        /// The marker names a worker walks to at a building: where `CampPath`
        /// keeps a lane open from (`Lanes`).
        public static readonly string[] AccessStems =
            { "Input_Pickup", "Output_Dropoff", "Worker_Stand", "Worker_Approach", "Entry", "Entrance_Anchor", "DropSpot" };

        /// This building's boxes in its own frame (x = centre x, y = centre
        /// z, z = half x, w = half z) into `into`; how many.
        public static int LocalBoxes(Building b, List<Vector4> into)
        {
            into.Clear();
            if (b == null) return 0;
            if (b.Kind == BuildKind.Pier || b.Kind == BuildKind.DryDock) return 0;
            var plan = BuildPlans.Named(b.Id);
            // **The mine (2026-10-05)**: its footprint is centred on the lip,
            // half in the hill and half the working apron. Only the shaft
            // head behind the lip is solid; the apron, the container's front
            // and the mouth stand stay walkable.
            if (b.Kind == BuildKind.Mine)
            {
                float hw = Mathf.Max(0.4f, plan.footprint.x * 0.5f - FootprintInset);
                float d = BuildPlans.MineHeadDepth;
                into.Add(new Vector4(0f, -0.3f - d * 0.5f, hw, d * 0.5f));
                return into.Count;
            }
            // The model it wears NOW: a level 2 swap reads its own boxes.
            string model = !string.IsNullOrEmpty(b.ModelPrefab) ? b.ModelPrefab : plan.prefab;
            if (!string.IsNullOrEmpty(model) && Baked.TryGetValue(model, out var f)
                && b.transform.Find("Model") != null)
            {
                for (int i = 0; i + 3 < f.Length; i += 4)
                    into.Add(new Vector4(f[i], f[i + 1], f[i + 2], f[i + 3]));
                AddFireCache(b, into);
                return into.Count;
            }
            Vector2 fp = b.Footprint;
            if (fp.x <= 0f || fp.y <= 0f) fp = plan.footprint;
            if (fp.x <= 0f || fp.y <= 0f) return 0;
            into.Add(new Vector4(0f, 0f,
                Mathf.Max(0.2f, fp.x * 0.5f - FootprintInset), Mathf.Max(0.2f, fp.y * 0.5f - FootprintInset)));
            AddFireCache(b, into);
            return into.Count;
        }

        /// **The fire cache's groundsheet and tripod (2026-10-03)**, in the
        /// campfire's frame, ONLY where `Outpost.PlaceFireCache` actually
        /// stood one (its child is there): one box, so walkers go round the
        /// goods. The cache may be turned against the fire (it faces the
        /// fire from wherever it was clear), and a box here is axis-aligned
        /// in the fire's frame, so it is the turned footprint's bounding box
        /// -- never bigger than the disc that spot was tested clear with.
        static void AddFireCache(Building b, List<Vector4> into)
        {
            if (b.Id != BuildPlans.Campfire.id) return;
            var c = b.transform.Find(BuildingFactory.FireCacheChild);
            if (c == null) return;
            Vector4 k = BuildingFactory.FireCacheBox;
            var fire = b.transform;
            Vector3 centre = fire.InverseTransformPoint(c.TransformPoint(new Vector3(k.x, 0f, k.y)));
            Vector3 ax = fire.InverseTransformDirection(c.right), az = fire.InverseTransformDirection(c.forward);
            float hx = Mathf.Abs(ax.x) * k.z + Mathf.Abs(az.x) * k.w;
            float hz = Mathf.Abs(ax.z) * k.z + Mathf.Abs(az.z) * k.w;
            into.Add(new Vector4(centre.x, centre.z, hx, hz));
        }

        /// Is this baked? (For checks.)
        public static bool IsBaked(string prefab) => prefab != null && Baked.ContainsKey(prefab);
    }
}
