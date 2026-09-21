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
            m = new Material(Shader.Find(WorldArtStyle.Instance != null ? "SeaSick/Environment Toon" : "Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Ambient")) m.SetFloat("_Ambient", 0);
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

            // **The authored kit first, and the extrusion as the fallback.**
            //
            // `SettlementKitV1` is fifteen models on a vertex palette, scaled
            // to the crew's own height, with worker and entry markers already
            // in the prefabs. A plan that names one wears it; a plan that does
            // not is extruded exactly as before. Both leave by the same door
            // at the bottom of this method -- an earlier version returned
            // early for a campfire and so raised a fire and then reported a
            // failure to raise one, and the caller's error message blamed the
            // terrain. **A shape that leaves by a different door has to carry
            // the same things out with it.**
            if (Dress(root.transform, plan))
            {
                Footing(root.transform, plan, footing);
                // The kit's fire is a ring of stones and nothing else. The
                // LIGHT is the whole reason a camp reads from the water at
                // night, so it is added whatever the geometry came from.
                if (plan.kind == BuildKind.Fire) Firelight(root.transform);
                else Lamp(root.transform, plan);
                root.AddComponent<Building>().Configure(plan);
                return root;
            }

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

            Lamp(root.transform, plan);
            root.AddComponent<Building>().Configure(plan);
            return root;
        }

        /// **The blueprint: what it will look like, standing there not built.**
        ///
        /// Built by raising the real thing and then taking away everything
        /// that makes it real -- its `Building` component (or the village
        /// would count a plan as a shed), its light, its flicker. That is the
        /// point: a ghost assembled separately is a second description of the
        /// same building, and the two drift the first time a roof pitch
        /// changes. This one cannot be wrong about what it is previewing
        /// because it IS the preview.
        public static GameObject Ghost(BuildPlan plan, Transform parent,
            Vector3 floorAt, Quaternion facing, float footing, float alpha)
        {
            var root = Raise(plan, parent, floorAt, facing, footing);
            root.name = "Blueprint_" + plan.id;

            var b = root.GetComponent<Building>();
            if (b != null) Object.Destroy(b);
            foreach (var f in root.GetComponentsInChildren<Campfire>(true))
                Object.Destroy(f);
            // The light goes with its GameObject: an unlit fire that still
            // lights the trees is the one tell that would read as a bug.
            foreach (var l in root.GetComponentsInChildren<Light>(true))
                Object.Destroy(l.gameObject);

            Tint(root, alpha);
            return root;
        }

        /// Chalk: what a blueprint is drawn in unless somebody says otherwise.
        public static readonly Color GhostChalk = new Color(0.62f, 0.78f, 0.92f);
        /// What a ghost turns when it is standing somewhere it cannot be
        /// built. Red is the whole feedback — the sheet says why in words, but
        /// the colour is what the thumb is reading.
        public static readonly Color GhostRefused = new Color(0.92f, 0.36f, 0.30f);

        /// Re-tint a ghost in place, so a blueprint filling up does not have
        /// to be torn down and rebuilt every time a log arrives.
        public static void Tint(GameObject ghost, float alpha)
            => Tint(ghost, GhostChalk, alpha);

        public static void Tint(GameObject ghost, Color colour, float alpha)
        {
            var mat = GhostMat(colour, alpha);
            foreach (var r in ghost.GetComponentsInChildren<MeshRenderer>(true))
                r.sharedMaterial = mat;
        }

        static readonly Dictionary<int, Material> ghostMats = new Dictionary<int, Material>();

        /// One material per rounded colour, cached: a ghost dragged about the
        /// island re-tints every frame it crosses a boundary, and a material
        /// per change would leak one per frame.
        static Material GhostMat(Color colour, float alpha)
        {
            int key = Mathf.RoundToInt(Mathf.Clamp01(alpha) * 20f)
                | (Mathf.RoundToInt(colour.r * 15f) << 8)
                | (Mathf.RoundToInt(colour.g * 15f) << 12)
                | (Mathf.RoundToInt(colour.b * 15f) << 16);
            if (ghostMats.TryGetValue(key, out var m) && m != null) return m;

            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetFloat("_Surface", 1f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
            // Chalk, not the building's own colour. A blueprint that is
            // merely a pale version of the finished thing reads as a building
            // in fog; one that is plainly a drawing reads as a decision not
            // yet paid for.
            m.SetColor("_BaseColor", new Color(colour.r, colour.g, colour.b,
                Mathf.Clamp01(alpha)));
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            ghostMats[key] = m;
            return m;
        }

        /// Put the authored model on, if the plan names one and it loads.
        ///
        /// Quietly false when it does not: a camp that cannot be built because
        /// an art asset moved is a worse failure than a plainer shed, and the
        /// extrusion below is a complete building in its own right.
        static bool Dress(Transform root, BuildPlan plan)
        {
            if (string.IsNullOrEmpty(plan.prefab)) return false;
            var asset = Resources.Load<GameObject>(plan.prefab);
            if (asset == null)
            {
                if (!warned.Contains(plan.prefab))
                {
                    warned.Add(plan.prefab);
                    Debug.LogWarning($"[Camp] no model at Resources/{plan.prefab} -- "
                        + $"'{plan.label}' is raised out of primitives instead.");
                }
                return false;
            }

            var model = Object.Instantiate(asset, root);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            // Colliders on a building would put the crew's pathing and the
            // ship's grounding into a conversation nobody asked for. The
            // ground is what things stand on here.
            foreach (var c in model.GetComponentsInChildren<Collider>(true))
                Object.Destroy(c);
            return true;
        }

        static readonly HashSet<string> warned = new HashSet<string>();

        /// A slab under an authored model, for the same reason the extruded
        /// ones have one: the ground is never flattened, so a building sits at
        /// its highest corner and something has to bridge the drop to its
        /// lowest. Buried is invisible; floating is not.
        static void Footing(Transform root, BuildPlan plan, float footing)
        {
            if (footing <= 0.05f && plan.kind == BuildKind.Fire) return;
            var stone = Mat("footing", new Color(0.44f, 0.44f, 0.42f));
            float slab = 0.35f + Mathf.Max(0f, footing);
            Box(root, stone,
                new Vector3(plan.footprint.x + 0.5f, slab, plan.footprint.y + 0.5f),
                new Vector3(0f, 0.08f - slab * 0.5f, 0f));
        }

        /// The fire itself, separated from the stones so the authored ring and
        /// the extruded one light the island the same way.
        static void Firelight(Transform root)
        {
            var lightGo = new GameObject("Firelight");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.62f, 0.28f);
            l.range = 22f;
            l.intensity = 12f;
            l.shadows = LightShadows.None;   // one more shadow caster per camp is not worth it
            lightGo.AddComponent<Campfire>();
        }

        /// **A lit window.** Every building that is not the fire gets one
        /// warm lamp under its eaves (Kevin, 2026-09-21: "add light sources to
        /// the campfire and the buildings so I can see at night"). Smaller
        /// and steadier than the fire, and gated by the same night curve, so
        /// a camp at night is one big fire and a ring of small windows.
        /// The mobile pipeline lights four per object; a hut lit by its own
        /// lamp, the fire and two neighbours is within that.
        static void Lamp(Transform root, BuildPlan plan)
        {
            float len = plan.footprint.x, wid = plan.footprint.y;
            var lightGo = new GameObject("Lamp");
            lightGo.transform.SetParent(root, false);
            // Just outside the door wall, at lintel height, so the pool falls
            // on the ground in front rather than being swallowed by the walls.
            lightGo.transform.localPosition = new Vector3(0f, 1.7f, wid * 0.5f + 0.4f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.72f, 0.42f);
            l.range = Mathf.Clamp(8f + Mathf.Max(len, wid), 10f, 14f);
            l.intensity = 5f;
            l.shadows = LightShadows.None;
            var fire = lightGo.AddComponent<Campfire>();
            fire.flicker = 0.07f;
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

            Firelight(root);
        }
    }
}
