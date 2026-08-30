using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// Raises a building out of primitives, the way the props and the cargo
    /// are made. No prefabs: a plan is data (BuildPlan) and the geometry is
    /// derived from its footprint and ridge, so changing a size in the
    /// charter changes the building.
    public static class BuildingFactory
    {
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        static Material Mat(string key, Color c, float smoothness = 0.08f)
        {
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            mats[key] = m;
            return m;
        }

        /// `footing` is how far the base slab reaches BELOW the floor. The
        /// ground is never flattened -- the height field is a pure function
        /// of position with no per-building data in it -- so a building sits
        /// at its highest corner and the slab bridges the drop to its
        /// lowest. Without that it either floats at one corner or is buried
        /// at another, and on this terrain it is always one of the two.
        public static GameObject Raise(BuildPlan plan, Transform parent,
            Vector3 floorAt, Quaternion facing, float footing)
        {
            float len = plan.footprint.x;     // along the ridge
            float wid = plan.footprint.y;     // across it
            float ridge = plan.ridge;

            var root = new GameObject("Building_" + plan.id);
            root.transform.SetParent(parent, true);
            root.transform.SetPositionAndRotation(floorAt, facing);

            var timber = Mat("wall", new Color(0.42f, 0.31f, 0.20f));
            var dark = Mat("beam", new Color(0.25f, 0.18f, 0.12f));
            var thatch = Mat("thatch", new Color(0.34f, 0.33f, 0.22f));
            var stone = Mat("footing", new Color(0.44f, 0.44f, 0.42f));

            // Foundation. Deep enough to reach the low corner, and a little
            // wider than the walls so it reads as a footing rather than as
            // the building sinking.
            // A quarter-metre past the low corner, because the corners the
            // caller measured are the WALLS' corners and the slab is wider
            // than they are -- the ground under its own corners can be lower
            // still. Buried is invisible; floating is not.
            float slab = 0.6f + Mathf.Max(0f, footing);
            Box(root.transform, stone,
                new Vector3(len + 0.7f, slab, wid + 0.7f),
                new Vector3(0f, 0.35f - slab * 0.5f, 0f));

            float wallH = ridge * 0.55f;
            Box(root.transform, timber, new Vector3(len, wallH, wid),
                new Vector3(0f, 0.35f + wallH * 0.5f, 0f));

            // Corner posts, so the walls read as built rather than extruded.
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Box(root.transform, dark, new Vector3(0.32f, wallH + 0.3f, 0.32f),
                        new Vector3(sx * len * 0.5f, 0.35f + wallH * 0.5f, sz * wid * 0.5f));

            // Roof: two panels leaning on a ridge that runs along the length.
            float eave = 0.35f + wallH;
            float rise = Mathf.Max(0.6f, ridge - eave);
            float half = wid * 0.5f;
            float slope = Mathf.Sqrt(half * half + rise * rise);
            float pitch = Mathf.Atan2(rise, half) * Mathf.Rad2Deg;
            for (int s = -1; s <= 1; s += 2)
            {
                var panel = Box(root.transform, thatch,
                    new Vector3(len + 0.8f, 0.24f, slope * 1.06f),
                    new Vector3(0f, eave + rise * 0.5f, s * half * 0.5f));
                panel.transform.localRotation = Quaternion.Euler(s * pitch, 0f, 0f);
            }
            Box(root.transform, dark, new Vector3(len + 0.9f, 0.26f, 0.34f),
                new Vector3(0f, ridge, 0f));

            // Door in the gable end, facing whatever `facing` points at.
            Box(root.transform, dark, new Vector3(0.18f, 2.0f, 1.1f),
                new Vector3(-len * 0.5f - 0.05f, 0.35f + 1.0f, 0f));

            root.AddComponent<Building>().Configure(plan);
            return root;
        }

        static GameObject Box(Transform parent, Material mat, Vector3 size, Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localScale = size;
            go.transform.localPosition = at;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }
    }
}
