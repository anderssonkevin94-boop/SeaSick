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
            c = WorldArtStyle.BuildingColour(key, c);
            key += WorldArtStyle.CacheSuffix;
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            mats[key] = m;
            return m;
        }

        internal static void ReleaseArtMaterials(string suffix)
        {
            var keys = new List<string>();
            foreach (var pair in mats)
                if (pair.Key.EndsWith(suffix, System.StringComparison.Ordinal)) keys.Add(pair.Key);
            foreach (var key in keys)
            {
                if (mats[key] != null) Object.Destroy(mats[key]);
                mats.Remove(key);
            }
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

            // **Every kind gets its `Building` component**, which is what the
            // village counts itself by. An earlier version returned here
            // before the one at the bottom of this method, so a campfire was
            // raised on the ground and then reported as a failure to raise
            // one -- and the caller's error message blamed the terrain. A
            // shape that leaves by a different door has to carry the same
            // things out with it.
            if (plan.kind == BuildKind.Fire)
            {
                Fire(root.transform, plan);
                root.AddComponent<Building>().Configure(plan);
                return root;
            }

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

        /// A ring of stones with a few logs leaning in, and a light.
        ///
        /// Deliberately NOT a hut. The campfire is the moment an island stops
        /// being scenery and starts being somewhere you have a stake in, and a
        /// shed says that far less than a fire does. The light matters more
        /// than the geometry: day and night are shipped, so a camp you left
        /// burning is visible from the water at night, which is the cheapest
        /// possible way to make an investment READ from the deck.
        static void Fire(Transform root, BuildPlan plan)
        {
            var stone = Mat("firestone", new Color(0.46f, 0.45f, 0.43f));
            var log = Mat("firelog", new Color(0.29f, 0.20f, 0.13f));
            var ember = Mat("ember", new Color(1f, 0.46f, 0.12f), 0.6f);

            float r = plan.footprint.x * 0.5f;

            // The ring. Eight stones, jittered off a circle so it reads as
            // gathered rather than laid out.
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f;
                float rr = r * (0.92f + 0.1f * Mathf.Sin(i * 2.3f));
                var b = Box(root, stone,
                    new Vector3(0.34f, 0.22f, 0.28f),
                    new Vector3(Mathf.Cos(a) * rr, 0.11f, Mathf.Sin(a) * rr));
                b.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + i * 11f, 0f);
            }

            // Three logs leaning into the middle.
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f + 0.4f;
                var b = Box(root, log,
                    new Vector3(0.17f, 0.17f, r * 1.5f),
                    new Vector3(Mathf.Cos(a) * r * 0.34f, 0.24f, Mathf.Sin(a) * r * 0.34f));
                b.transform.localRotation =
                    Quaternion.Euler(38f, -a * Mathf.Rad2Deg + 90f, 0f);
            }

            Box(root, ember, new Vector3(0.4f, 0.14f, 0.4f), new Vector3(0f, 0.1f, 0f));

            var lightGo = new GameObject("Firelight");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.62f, 0.28f);
            l.range = 14f;
            l.intensity = 2.2f;
            l.shadows = LightShadows.None;      // one more shadow caster per camp is not worth it
            lightGo.AddComponent<Campfire>();
        }
    }
}
