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
                if (plan.beds > 0) NameBeds(root.transform);
                // **No slab under an authored model (Kevin, 2026-09-24):**
                // *"all the buildings on these concrete looking slabs in game
                // really breaks the immersion. ensure the buildings are
                // standing flat on the ground, some clipping is okay."* The
                // caller hands us the HIGHEST corner; the model is let down
                // most of the way to the lowest, so the downhill edge floats
                // by at most a fifth of the drop (a few centimetres on the
                // plots we allow) and the uphill side clips into the slope,
                // which he accepted. The extruded fallbacks below still pour
                // their own footing; they are what a missing model gets.
                root.transform.position -= new Vector3(0f, Mathf.Max(0f, footing) * SinkIntoSlope, 0f);
                // The kit's fire is a ring of stones and nothing else. The
                // LIGHT is the whole reason a camp reads from the water at
                // night, so it is added whatever the geometry came from.
                if (plan.kind == BuildKind.Fire) Firelight(root.transform);
                else Lamp(root.transform, plan);
                root.AddComponent<Building>().Configure(plan);
                Arm(root, plan);
                return root;
            }

            if (plan.kind == BuildKind.Fire)
            {
                Fire(root.transform, plan);
                root.AddComponent<Building>().Configure(plan);
                return root;
            }

            if (plan.kind == BuildKind.Pier)
            {
                PierDeck(root.transform, plan);
                // The lamp on the landward post: the pool falls on the beach
                // where the crew step off, and reads from the water as the
                // one light that is at the shore rather than up the hill.
                Lamp(root.transform, plan,
                    new Vector3(-len * 0.5f + 0.6f, 2.1f, wid * 0.5f + 0.2f));
                root.AddComponent<Building>().Configure(plan);
                root.AddComponent<Pier>().Configure(plan);
                return root;
            }

            if (plan.kind == BuildKind.Fletcher)
            {
                FletcherShop(root.transform, plan, footing);
                Lamp(root.transform, plan);
                root.AddComponent<Building>().Configure(plan);
                Arm(root, plan);
                return root;
            }

            if (plan.kind == BuildKind.Quarry)
            {
                QuarryYard(root.transform, plan, footing);
                Lamp(root.transform, plan);
                root.AddComponent<Building>().Configure(plan);
                Arm(root, plan);
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
            Arm(root, plan);
            return root;
        }

        /// A watchtower carries a gun and can be shot at. Both exits of
        /// `Raise` come through here, for the same reason they share the
        /// `Building` line above.
        static void Arm(GameObject root, BuildPlan plan)
        {
            if (plan.id == OutpostLedger.WatchtowerId)
                root.AddComponent<Combat.WatchtowerGun>();
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
            // A ghost pier is not a berth. It never registered (only
            // `Outpost.Raise` does that), so this is tidiness, not safety.
            var pier = root.GetComponent<Pier>();
            if (pier != null) Object.Destroy(pier);
            foreach (var f in root.GetComponentsInChildren<Campfire>(true))
                Object.Destroy(f);
            // A ghost tower has no gun and cannot be shot: `WatchtowerGun`
            // registers itself as a target in OnEnable, so it goes now.
            foreach (var g in root.GetComponentsInChildren<Combat.WatchtowerGun>(true))
                Object.DestroyImmediate(g);
            // The light goes with its GameObject: an unlit fire that still
            // lights the trees is the one tell that would read as a bug.
            foreach (var l in root.GetComponentsInChildren<Light>(true))
                Object.Destroy(l.gameObject);

            Tint(root, alpha);
            return root;
        }

        // --- the wall (Phase 1, 2026-09-23) ----------------------------------

        /// **A palisade segment, or a gate, between two posts.**
        ///
        /// Since 2026-09-23 the drawing is Astra's palisade kit, tiled along
        /// the run by `WallVisual` (quarter-metre pieces, never stretched),
        /// with the posts on the NODES owned by the camp's `WallChain` -- a
        /// segment raised under an `Outpost` has no posts of its own. A
        /// drawing with no chain (a blueprint ghost) stands its own two.
        /// Should the kit fail to load, the old extruded wall below is
        /// raised instead, posts and all.
        ///
        /// Both states are built at once and one of them is switched off:
        /// a segment is broken and mended several times in a raid, and
        /// rebuilding the mesh each time is work done in the frame the
        /// player is least able to spare it.
        ///
        /// `a` and `b` are the posts at ground height. The object is placed
        /// at the midpoint and turned along the run, so everything hung off
        /// its transform (the sheet's anchor, a hauler's target) is about
        /// the middle of the segment.
        public static GameObject RaiseWall(Transform parent, Vector3 a, Vector3 b,
            bool gate, out Transform whole, out Transform broken, bool withBroken = true)
        {
            Vector3 mid = 0.5f * (a + b);
            Vector3 run = b - a;
            run.y = 0f;
            float len = Mathf.Max(0.5f, run.magnitude);
            Quaternion facing = run.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(run.normalized, Vector3.up)
                : Quaternion.identity;

            var root = new GameObject(gate ? "Gate" : "Palisade");
            root.transform.SetParent(parent, true);
            root.transform.SetPositionAndRotation(mid, facing);
            float postH = gate ? BuildPlans.GateHeight : BuildPlans.PalisadeHeight;

            if (WallVisual.KitReady)
            {
                var chain = WallChain.Of(parent, create: true);
                var camp = parent != null ? parent.GetComponentInParent<Outpost>() : null;
                var fit = chain != null ? chain.FitFor(a, b, gate, null)
                    : WallVisual.Loose(a, b, gate, camp);
                WallVisual.Build(root.transform, a, b, fit,
                    camp != null ? camp.GroundAt : (System.Func<Vector3, float>)null,
                    out whole, out broken, withBroken);
            }
            else WallBoxes(root.transform, a, b, gate, len, postH, out whole, out broken);

            // **One collider for the whole segment**, on the root: the
            // pieces have none (they are drawings), and a tap has to land
            // on the SEGMENT to open its sheet -- which is how a gate is
            // placed (D5: "tap a built segment -> its sheet"). Sized to the
            // run and kept whether or not the middle is standing: a breach
            // is still a thing you can tap to see how the repair is going.
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, postH * 0.5f, 0f);
            box.size = new Vector3(1.2f, postH, len);

            return root;
        }

        /// **The extruded wall: the fallback when the kit will not load.**
        /// A stretched box for the run and a post at each end -- cheap, and
        /// a fence that reads as a fence from the deck at fifty metres.
        static void WallBoxes(Transform root, Vector3 a, Vector3 b, bool gate, float len,
            float postH, out Transform whole, out Transform broken)
        {
            Vector3 mid = 0.5f * (a + b);
            float railH = BuildPlans.PalisadeHeight;
            float drop = 0.35f;        // buried, so a segment on a slope has no daylight under it

            var wholeGo = new GameObject("Whole");
            wholeGo.transform.SetParent(root, false);
            var brokenGo = new GameObject("Broken");
            brokenGo.transform.SetParent(root, false);
            whole = wholeGo.transform;
            broken = brokenGo.transform;

            // Local Z runs along the segment; the post positions are the
            // two ends of it, lifted to the local floor. The posts stand in
            // BOTH states: a breached wall still has its posts in the
            // ground, which is what makes the gap read as a hole.
            float half = len * 0.5f;
            float aUp = a.y - mid.y, bUp = b.y - mid.y;

            foreach (var holder in new[] { whole, broken })
            {
                Box(holder, "Post_A", new Vector3(0f, aUp + postH * 0.5f - drop, -half),
                    new Vector3(0.34f, postH + drop, 0.34f), PostMat);
                Box(holder, "Post_B", new Vector3(0f, bUp + postH * 0.5f - drop, half),
                    new Vector3(0.34f, postH + drop, 0.34f), PostMat);
            }

            if (gate)
            {
                // A gate is the gap plus a lintel across the top of it. The
                // "whole" one has the lintel; the broken one has the posts
                // and nothing over them.
                float lintelY = Mathf.Max(aUp, bUp) + postH - 0.25f;
                Box(whole, "Lintel", new Vector3(0f, lintelY, 0f),
                    new Vector3(0.28f, 0.42f, len), PostMat);
            }
            else
            {
                float midUp = 0.5f * (aUp + bUp);
                Box(whole, "Run", new Vector3(0f, midUp + railH * 0.5f - drop, 0f),
                    new Vector3(0.28f, railH + drop, len), WallMat);

                // **Broken = the middle third missing.** Two stubs, each a
                // third of the run, left standing against their own post.
                float third = len / 3f;
                Box(broken, "Stub_A",
                    new Vector3(0f, midUp + railH * 0.42f - drop, -half + third * 0.5f),
                    new Vector3(0.28f, railH * 0.84f + drop, third), WallMat);
                Box(broken, "Stub_B",
                    new Vector3(0f, midUp + railH * 0.42f - drop, half - third * 0.5f),
                    new Vector3(0.28f, railH * 0.84f + drop, third), WallMat);
            }
        }

        /// The same drawing, translucent: what a wall SITE stands as while
        /// it is being stocked. Built through `RaiseWall` for the same
        /// reason `Ghost` is built through `Raise` -- a ghost assembled
        /// separately is a second description of the same thing.
        public static GameObject WallGhost(Transform parent, Vector3 a, Vector3 b,
            bool gate, float alpha)
        {
            // No broken state: a drawing is never breached, and the siting
            // tool redraws this as the thumb drags.
            var root = RaiseWall(parent, a, b, gate, out _, out var broken, withBroken: false);
            root.name = gate ? "Blueprint_gate" : "Blueprint_palisade";
            if (broken != null) Object.Destroy(broken.gameObject);
            // A drawing is not a thing you bump into, and the SITE's own
            // collider is what the tap has to find.
            var box = root.GetComponent<BoxCollider>();
            if (box != null) Object.Destroy(box);
            Tint(root, alpha);
            return root;
        }

        static Material WallMat => Mat("PalisadeRun", new Color(0.46f, 0.36f, 0.24f));
        static Material PostMat => Mat("PalisadePost", new Color(0.38f, 0.29f, 0.19f));

        static void Box(Transform parent, string name, Vector3 at, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
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
        ///
        /// **2026-09-23: `SeaSick/BlueprintGhost`, not URP Lit.** A ghost
        /// standing behind a tree or in the grass used to vanish there --
        /// URP Lit's ghost material had `_ZWrite 0` but no way to touch
        /// `ZTest`, so it still lost the depth test to whatever foliage had
        /// already written the depth buffer in front of it. Checked in
        /// `Library/PackageCache/com.unity.render-pipelines.universal@.../
        /// Shaders/Lit.shader` and `Unlit.shader` (package 17.4.0): neither
        /// shader's forward pass carries a `[_ZTest]` token, so
        /// `mat.SetInt("_ZTest", (int)CompareFunction.Always)` on either one
        /// is a silent no-op -- there is no property for it to set. The only
        /// way to draw over scenery is a shader that says `ZTest Always`
        /// itself, so that one line is the whole of `BlueprintGhost.shader`
        /// (`Assets/_Project/Art/Shaders/World/`): unlit, one colour
        /// property, no lighting maths -- cheaper on the phone's GPU than
        /// the Lit ghost ever was, not just equally cheap.
        static Material GhostMat(Color colour, float alpha)
        {
            int key = Mathf.RoundToInt(Mathf.Clamp01(alpha) * 20f)
                | (Mathf.RoundToInt(colour.r * 15f) << 8)
                | (Mathf.RoundToInt(colour.g * 15f) << 12)
                | (Mathf.RoundToInt(colour.b * 15f) << 16);
            if (ghostMats.TryGetValue(key, out var m) && m != null) return m;

            m = new Material(Shader.Find("SeaSick/BlueprintGhost"));
            // Chalk, not the building's own colour. A blueprint that is
            // merely a pale version of the finished thing reads as a building
            // in fog; one that is plainly a drawing reads as a decision not
            // yet paid for.
            m.SetColor("_BaseColor", new Color(colour.r, colour.g, colour.b,
                Mathf.Clamp01(alpha)));
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
            // **Storage visibility (Kevin, 2026-09-23): a production
            // building always shows the true amount it holds.** Finds its
            // own station and toggles the kit's slot children; self-disables
            // on a model with none (the blacksmith/kitchen kit, anything
            // extruded).
            root.gameObject.AddComponent<StationStockView>();
            // Astra's non-station buildings (2026-09-23): each of these
            // looks for its own kit's slot names and self-disables (never
            // even ticks `Update`, see each view's `Start`) on a model with
            // none of them, exactly like `StationStockView` above -- so
            // every kit building carries all four and only the one whose
            // names match ever does anything.
            root.gameObject.AddComponent<StoreStockView>();
            root.gameObject.AddComponent<FarmBedView>();
            root.gameObject.AddComponent<CampfireStateView>();
            root.gameObject.AddComponent<ShelterStateView>();
            return true;
        }

        static readonly HashSet<string> warned = new HashSet<string>();

        // --- the farm's beds, 2026-09-21 ------------------------------------

        /// What a kit farm's crop modules are called once it is raised:
        /// `Bed_00`, `Bed_01`, ... in the kit's own slot order.
        public const string BedPrefix = "Bed_";
        /// What its planting markers are called: `BedSlot_00`, ... -- the
        /// empty at the centre of each bed, where a planted crop goes.
        public const string BedSlotPrefix = "BedSlot_";

        /// **The kit's beds ARE separable.** `farm_01/02/03` come out of
        /// Blender as one structure mesh plus one crop module per bed
        /// (`farm_01_Crop_00` ...), each with a `Plant_slot_NN` empty on it,
        /// so the crop can be hidden, swapped or replanted one bed at a time.
        /// The FBX keeps Blender's `.006` suffixes, so the match is on the
        /// stem. Renamed rather than tagged: a name survives a prefab
        /// variant, a save and a `Find`, and the kit's next export.
        static void NameBeds(Transform root)
        {
            var crops = new List<Transform>();
            var slots = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Contains("_Crop_")) crops.Add(t);
                else if (Stem(t.name).StartsWith("Plant_slot_", System.StringComparison.Ordinal)) slots.Add(t);
            }
            crops.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            slots.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            for (int i = 0; i < crops.Count; i++) crops[i].name = BedPrefix + i.ToString("00");
            for (int i = 0; i < slots.Count; i++) slots[i].name = BedSlotPrefix + i.ToString("00");
        }

        /// Visible to `StationStockView` too: the FBX import can add
        /// `.001` suffixes, so anything matching a kit slot name has to
        /// match on the stem the same way the bed naming below does.
        internal static string Stem(string name)
        {
            int dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        /// The crop modules of a raised farm, in slot order. Four on
        /// `farm_01`, six on `farm_02`, eight on `farm_03`; empty for
        /// anything that is not a farm or was extruded.
        public static List<Transform> BedsOf(Transform building) => Named(building, BedPrefix);

        /// The planting markers of a raised farm, in slot order.
        public static List<Transform> BedSlotsOf(Transform building) => Named(building, BedSlotPrefix);

        static List<Transform> Named(Transform building, string prefix)
        {
            var found = new List<Transform>();
            if (building == null) return found;
            foreach (var t in building.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(prefix, System.StringComparison.Ordinal)) found.Add(t);
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found;
        }

        /// A slab under an authored model, for the same reason the extruded
        /// ones have one: the ground is never flattened, so a building sits at
        /// its highest corner and something has to bridge the drop to its
        /// lowest. Buried is invisible; floating is not.
        /// How much of the plot's high-to-low drop an authored model is let
        /// down by (1 = sit on the lowest corner, 0 = the old highest-corner
        /// perch). Kit and extruded buildings are unaffected.
        const float SinkIntoSlope = 0.8f;

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
            l.intensity = 4f;                // the shaders fade linearly over the range; 4 is a bright pool
            l.shadows = LightShadows.None;   // one more shadow caster per camp is not worth it
            lightGo.AddComponent<Campfire>();

            // The flame itself: an unlit ember the light cannot be seen
            // without. From the deck at night the point light is a pool on
            // the shore; this is the orange dot in the middle of it.
            var flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(flame.GetComponent<Collider>());
            flame.name = "Flame";
            flame.transform.SetParent(root, false);
            flame.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            flame.transform.localScale = new Vector3(0.55f, 0.7f, 0.55f);
            var fm = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            fm.color = new Color(1f, 0.55f, 0.15f);
            flame.GetComponent<MeshRenderer>().sharedMaterial = fm;
        }

        /// **A lit window.** Every building that is not the fire gets one
        /// warm lamp under its eaves (Kevin, 2026-09-21: "add light sources to
        /// the campfire and the buildings so I can see at night"). Smaller
        /// and steadier than the fire, and gated by the same night curve, so
        /// a camp at night is one big fire and a ring of small windows.
        /// The mobile pipeline lights four per object; a hut lit by its own
        /// lamp, the fire and two neighbours is within that.
        static void Lamp(Transform root, BuildPlan plan, Vector3? at = null)
        {
            float len = plan.footprint.x, wid = plan.footprint.y;
            var lightGo = new GameObject("Lamp");
            lightGo.transform.SetParent(root, false);
            // Just outside the door wall, at lintel height, so the pool falls
            // on the ground in front rather than being swallowed by the walls.
            lightGo.transform.localPosition = at ?? new Vector3(0f, 1.7f, wid * 0.5f + 0.4f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.72f, 0.42f);
            l.range = Mathf.Clamp(8f + Mathf.Max(len, wid), 10f, 14f);
            l.intensity = 1.6f;              // linear fade in the shaders; a window, not a second fire
            l.shadows = LightShadows.None;
            var fire = lightGo.AddComponent<Campfire>();
            fire.flicker = 0.07f;
        }

        /// **Planks on posts.** Local +X runs land to sea, the deck is at
        /// local y = 0 -- which `Outpost.CanPlacePier` puts at
        /// `BuildPlans.PierDeck` above mean water, NOT above the ground: the
        /// ground under a pier is beach at one end and sea bed at the
        /// other. Each post is cut to the ground under it, off the world
        /// height field, so the same pier stands on any beach; the sea bed
        /// is clamped at `PostDeepest` because a post to a 30 m bottom is a
        /// draw call for nothing anybody sees.
        ///
        /// Nothing here has a collider (see `Dress`): the ship's grounding
        /// reads the height field, and the dock the pier registers is what
        /// stops her, not the planks.
        ///
        /// **Astra's pier kit v4 first, these boxes as the fallback
        /// (2026-09-23)**, the way `Dress` puts a building's kit on before
        /// the extrusion: `PierVisual` lays her modules, caps, shafts, ramp
        /// and lantern under a `PierKit` child and leaves the root, its
        /// length and its `Pier` exactly as they were. If any of her prefabs
        /// is missing from `Resources/Pier/` or fails its contract check it
        /// places nothing, and the boxes below go up instead.
        static void PierDeck(Transform root, BuildPlan plan)
        {
            if (PierVisual.Build(root, plan, Island.TerrainHeight)) return;

            float len = plan.footprint.x, wid = plan.footprint.y;
            var plank = Mat("plank", new Color(0.50f, 0.38f, 0.24f));
            var post = Mat("post", new Color(0.22f, 0.16f, 0.11f));
            var rail = Mat("beam", new Color(0.25f, 0.18f, 0.12f));

            // The deck, as planks: one box a plank wide every plank, with a
            // finger of gap, so it reads as laid rather than poured.
            const float PlankW = 0.5f, Gap = 0.06f;
            int planks = Mathf.Max(1, Mathf.RoundToInt(len / PlankW));
            float pw = len / planks;
            for (int i = 0; i < planks; i++)
            {
                float x = -len * 0.5f + (i + 0.5f) * pw;
                Box(root, plank, new Vector3(pw - Gap, 0.14f, wid), new Vector3(x, -0.07f, 0f));
            }
            // Two stringers under the planks, running the length.
            for (int s = -1; s <= 1; s += 2)
                Box(root, rail, new Vector3(len, 0.2f, 0.22f),
                    new Vector3(0f, -0.24f, s * (wid * 0.5f - 0.3f)));

            // Posts every three metres each side, from the deck down to
            // whatever is under them. Height is read in WORLD space at each
            // post's own footprint, so the pier's rotation does not matter.
            var ground = Island.TerrainHeight;
            float deckY = root.position.y;
            for (float x = -len * 0.5f + 0.4f; x <= len * 0.5f - 0.2f + 1e-3f; x += 3f)
                for (int s = -1; s <= 1; s += 2)
                {
                    var local = new Vector3(x, 0f, s * (wid * 0.5f - 0.25f));
                    Vector3 w = root.TransformPoint(local);
                    float g = ground != null ? ground(w.x, w.z) : 0f;
                    g = Mathf.Max(g, PostDeepest);
                    float top = 0.25f;                     // proud of the deck, as a bollard
                    float bottom = g - deckY - 0.3f;       // driven a little into the ground
                    float h = top - bottom;
                    Box(root, post, new Vector3(0.3f, h, 0.3f),
                        new Vector3(local.x, bottom + h * 0.5f, local.z));
                }

            // A short ramp at the land end, down toward the beach. Its foot
            // is at the ground under the land end, so it meets the sand
            // however high the deck stands above it there.
            Vector3 landW = root.TransformPoint(new Vector3(-len * 0.5f, 0f, 0f));
            float landG = ground != null ? ground(landW.x, landW.z) : 0f;
            float drop = Mathf.Clamp(deckY - landG, 0.3f, 2.5f);
            const float Run = 2.4f;
            float slope = Mathf.Sqrt(Run * Run + drop * drop);
            var ramp = Box(root, plank, new Vector3(slope, 0.14f, wid * 0.8f),
                new Vector3(-len * 0.5f - Run * 0.5f, -drop * 0.5f - 0.07f, 0f));
            ramp.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(drop, Run) * Mathf.Rad2Deg);
        }

        /// World y a pier post stops at: deeper than this and it is
        /// invisible under the water anyway.
        public const float PostDeepest = -6f;

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

        /// **The fletcher's, 2026-09-22: a bench, a bundle and a butt.**
        ///
        /// A small timber hut -- the plainest building in the camp, because a
        /// fletcher needs a roof and a flat surface and nothing else -- with
        /// a lean-to working bench along the open side, a bundle of five
        /// shafts leaning by the door, and a straw target butt standing three
        /// metres off in the clear.
        ///
        /// **The butt is what carries the building.** Everything else here
        /// could be a shed; a round straw disc on a post, out in the open
        /// where nothing else in the camp puts anything, says at a glance
        /// what is made inside. It is the only prop this factory places
        /// outside its own footprint, which is exactly why it reads.
        ///
        /// Local -X is the door, as everywhere else, so `Outpost.Raise`
        /// turning it toward the clearing puts the bench, the bundle and the
        /// butt on the village side.
        static void FletcherShop(Transform root, BuildPlan plan, float footing)
        {
            float len = plan.footprint.x, wid = plan.footprint.y;
            float ridge = plan.ridge;

            var timber = Mat("wall", new Color(0.42f, 0.31f, 0.20f));
            var beam = Mat("beam", new Color(0.25f, 0.18f, 0.12f));
            var thatch = Mat("thatch", new Color(0.34f, 0.33f, 0.22f));
            var stone = Mat("footing", new Color(0.44f, 0.44f, 0.42f));
            var straw = Mat("butt", new Color(0.78f, 0.70f, 0.44f));
            var shaft = Mat("shaft", Res.Colour(Res.Arrows));

            // Slab, walls, posts and roof: the plain hut, unchanged, because
            // the fletcher's IS a plain hut and the character is in what
            // stands beside it.
            float slab = 0.6f + Mathf.Max(0f, footing);
            Box(root, stone, new Vector3(len + 0.7f, slab, wid + 0.7f),
                new Vector3(0f, 0.35f - slab * 0.5f, 0f));

            float wallH = ridge * 0.55f;
            Box(root, timber, new Vector3(len, wallH, wid),
                new Vector3(0f, 0.35f + wallH * 0.5f, 0f));
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Box(root, beam, new Vector3(0.3f, wallH + 0.3f, 0.3f),
                        new Vector3(sx * len * 0.5f, 0.35f + wallH * 0.5f, sz * wid * 0.5f));

            float eave = 0.35f + wallH;
            float rise = Mathf.Max(0.6f, ridge - eave);
            float half = wid * 0.5f;
            float slope = Mathf.Sqrt(half * half + rise * rise);
            float pitch = Mathf.Atan2(rise, half) * Mathf.Rad2Deg;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                var panel = Box(root, thatch,
                    new Vector3(len + 0.8f, 0.24f, slope * 1.06f),
                    new Vector3(0f, eave + rise * 0.5f, sgn * half * 0.5f));
                panel.transform.localRotation = Quaternion.Euler(sgn * pitch, 0f, 0f);
            }
            Box(root, beam, new Vector3(len + 0.9f, 0.26f, 0.34f),
                new Vector3(0f, ridge, 0f));
            Box(root, beam, new Vector3(0.18f, 2.0f, 1.1f),
                new Vector3(-len * 0.5f - 0.05f, 0.35f + 1.0f, 0f));

            // --- the lean-to bench -------------------------------------------
            //
            // A worktop on two legs against the door wall, under a single
            // sloped board propped off the eave: the shelter a man works
            // under when the bench will not fit indoors.
            const float BenchTop = 0.95f;
            float bx = -len * 0.5f - 0.75f;
            Box(root, timber, new Vector3(1.3f, 0.12f, wid * 0.62f),
                new Vector3(bx, 0.35f + BenchTop, 0f));
            for (int sz = -1; sz <= 1; sz += 2)
                Box(root, beam, new Vector3(0.14f, BenchTop, 0.14f),
                    new Vector3(bx, 0.35f + BenchTop * 0.5f, sz * wid * 0.24f));
            var lean = Box(root, thatch, new Vector3(1.9f, 0.14f, wid * 0.7f),
                new Vector3(bx - 0.15f, 0.35f + wallH * 0.92f, 0f));
            lean.transform.localRotation = Quaternion.Euler(0f, 0f, 22f);

            // --- the bundle of shafts by the door ----------------------------
            //
            // Five thin cylinders standing in a slight fan, leaning on the
            // door post. The same shape `CampPiles` gives an Arrows pile, so
            // the shop and its output rhyme the way the quarry and its brick
            // do.
            for (int i = 0; i < 5; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                var c = go.GetComponent<Collider>();
                if (c != null) Object.Destroy(c);
                go.transform.SetParent(root, false);
                go.transform.localScale = new Vector3(0.05f, 0.55f, 0.05f);
                go.transform.localRotation = Quaternion.Euler(
                    (i - 2) * 3.5f, i * 26f, 9f + (i - 2) * 4f);
                go.transform.localPosition = new Vector3(
                    -len * 0.5f + 0.18f, 0.35f + 0.5f,
                    wid * 0.28f + (i - 2) * 0.055f);
                go.GetComponent<MeshRenderer>().sharedMaterial = shaft;
            }

            // --- the target butt, three metres off the door ------------------
            //
            // A post, a flattened cylinder of straw on it, and a dark boss at
            // the centre so the disc reads as a TARGET rather than as a
            // wheel leaning against nothing.
            Vector3 butt = new Vector3(-len * 0.5f - ButtStandoff, 0f, -wid * 0.22f);
            Box(root, beam, new Vector3(0.16f, 1.25f, 0.16f),
                butt + new Vector3(0f, 0.35f + 0.625f, 0f));
            Box(root, beam, new Vector3(0.5f, 0.12f, 0.5f),
                butt + new Vector3(0f, 0.35f + 0.06f, 0f));

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var dc = disc.GetComponent<Collider>();
            if (dc != null) Object.Destroy(dc);
            disc.transform.SetParent(root, false);
            disc.transform.localScale = new Vector3(0.95f, 0.1f, 0.95f);
            // Cylinders stand on end; a butt faces the shooter, so it is
            // tipped onto its rim with its face toward the door.
            disc.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            disc.transform.localPosition = butt + new Vector3(0f, 0.35f + 1.35f, 0f);
            disc.GetComponent<MeshRenderer>().sharedMaterial = straw;

            var boss = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var bc = boss.GetComponent<Collider>();
            if (bc != null) Object.Destroy(bc);
            boss.transform.SetParent(root, false);
            boss.transform.localScale = new Vector3(0.34f, 0.13f, 0.34f);
            boss.transform.localRotation = disc.transform.localRotation;
            boss.transform.localPosition = disc.transform.localPosition
                + new Vector3(0.03f, 0f, 0f);
            boss.GetComponent<MeshRenderer>().sharedMaterial =
                Mat("buttboss", new Color(0.55f, 0.24f, 0.20f));
        }

        /// Metres from the door wall to the target butt. Three, as asked:
        /// far enough to be plainly a thing you shoot AT, close enough that
        /// `Outpost.Raise`'s clearance check on the footprint still leaves
        /// room for it (the spiral reserves `halfDiag + spacing`, which on a
        /// hut-sized plan is comfortably past this).
        public const float ButtStandoff = 3f;

        /// **A quarry, 2026-09-22: a yard, not a room.**
        ///
        /// Kevin asked for a building that turns rough stone into brick, and
        /// the one thing it must not look like is another shed with a
        /// different sign on it. So the silhouette carries the job: a LOW
        /// open-fronted stone shed (three walls, the fourth side left open
        /// onto the work, held up on two posts), a CUT FACE beside it -- a
        /// few flattened blocks stepped back the way rock is taken off a
        /// bench -- and a STACK OF BRICK at the front, which is the only
        /// thing in the camp that is both squared and stacked.
        ///
        /// Read from the air that is: grey mass, a notch of shadow where the
        /// front should be, a stepped face, and a small neat orange block.
        /// The brick stack is the same courses `CampPiles` lays the Brick
        /// pile in, deliberately -- the building and its output rhyme.
        ///
        /// The open side is local -X, which is where the extruded hut puts
        /// its door, so `Outpost.Raise` turning the building's -X toward the
        /// middle of the clearing faces the working front at the village
        /// exactly as it faces a hut's door at it. Nothing about siting,
        /// footing or the corner test changes.
        static void QuarryYard(Transform root, BuildPlan plan, float footing)
        {
            float len = plan.footprint.x;    // along the ridge, open at -X
            float wid = plan.footprint.y;
            float ridge = plan.ridge;

            var rock = Mat("quarrystone", new Color(0.52f, 0.51f, 0.48f));
            var cut = Mat("quarrycut", new Color(0.60f, 0.59f, 0.55f));
            var beam = Mat("beam", new Color(0.25f, 0.18f, 0.12f));
            var roof = Mat("thatch", new Color(0.34f, 0.33f, 0.22f));
            var stone = Mat("footing", new Color(0.44f, 0.44f, 0.42f));
            var brick = Mat("brick", Res.Colour(Res.Brick));

            // The slab, exactly as the hut gets one and for the same reason:
            // the ground is never flattened.
            float slab = 0.6f + Mathf.Max(0f, footing);
            Box(root, stone, new Vector3(len + 0.7f, slab, wid + 0.7f),
                new Vector3(0f, 0.35f - slab * 0.5f, 0f));

            // **Three walls.** Low -- a shed to keep rain off cut stone, not
            // a room to stand up in -- and thick, because they are rubble.
            float wallH = ridge * 0.52f;
            const float Thick = 0.55f;
            float wallY = 0.35f + wallH * 0.5f;

            // Back wall, the closed gable at +X.
            Box(root, rock, new Vector3(Thick, wallH, wid),
                new Vector3(len * 0.5f - Thick * 0.5f, wallY, 0f));
            // The two long sides, stopping short of the open front so the
            // opening reads as an opening rather than as a missing wall.
            float sideLen = len * 0.72f;
            for (int s = -1; s <= 1; s += 2)
                Box(root, rock, new Vector3(sideLen, wallH, Thick),
                    new Vector3(len * 0.5f - sideLen * 0.5f, wallY,
                        s * (wid * 0.5f - Thick * 0.5f)));

            // Two posts holding the open front up. Without them the roof
            // floats over the opening and the whole thing reads as ruined.
            for (int s = -1; s <= 1; s += 2)
                Box(root, beam, new Vector3(0.3f, wallH + 0.25f, 0.3f),
                    new Vector3(-len * 0.5f + 0.35f, 0.35f + (wallH + 0.25f) * 0.5f,
                        s * (wid * 0.5f - 0.35f)));

            // **The gloom inside**, which is what actually makes the front
            // read as OPEN. Without it you see the back wall straight
            // through the opening in the same grey as the front, and the
            // whole thing reads as a fourth wall with two posts stuck on it.
            // A dark box filling the interior is a shadow the toon shader
            // will not draw for us.
            //
            // It starts half a metre off the floor rather than at it: the
            // brick stacked inside is the one warm thing in the silhouette
            // and a gloom that reached the ground would swallow it.
            var gloom = Mat("quarrygloom", new Color(0.16f, 0.15f, 0.14f));
            const float GloomFloor = 0.45f;
            float gloomH = Mathf.Max(0.3f, wallH * 0.94f - GloomFloor);
            Box(root, gloom,
                new Vector3(len - Thick - 0.4f, gloomH, wid - Thick * 2f - 0.1f),
                new Vector3(-Thick * 0.25f, 0.35f + GloomFloor + gloomH * 0.5f, 0f));

            // A low roof, two shallow panels on a ridge along the length --
            // the hut's roof at half the rise, which is what makes it read
            // as a shed from the side.
            float eave = 0.35f + wallH;
            float rise = Mathf.Max(0.35f, ridge - eave);
            float half = wid * 0.5f;
            float slope = Mathf.Sqrt(half * half + rise * rise);
            float pitch = Mathf.Atan2(rise, half) * Mathf.Rad2Deg;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                var panel = Box(root, roof,
                    new Vector3(len + 0.7f, 0.2f, slope * 1.06f),
                    new Vector3(0f, eave + rise * 0.5f, sgn * half * 0.5f));
                panel.transform.localRotation = Quaternion.Euler(sgn * pitch, 0f, 0f);
            }
            Box(root, beam, new Vector3(len + 0.8f, 0.22f, 0.3f),
                new Vector3(0f, ridge, 0f));

            // --- the cut face ------------------------------------------------
            //
            // Five flattened blocks stepping back and up along one flank,
            // the way a bench is worked: the lowest is the widest and the
            // ones above it are set back, so the profile is a stair and not
            // a heap. Deterministic, no jitter -- this is rock that has been
            // CUT, and a jittered version of it is the cairn beside the fire.
            float faceZ = wid * 0.5f + 1.5f;
            for (int i = 0; i < 5; i++)
            {
                float h = 0.55f - i * 0.07f;
                float w = 2.6f - i * 0.35f;
                var b = Box(root, cut, new Vector3(w, h, 1.5f - i * 0.18f),
                    new Vector3(len * 0.12f + i * 0.22f,
                        0.2f + i * (h * 0.82f),
                        faceZ + i * 0.28f));
                // A degree or two off square: cut, but cut by hand.
                b.transform.localRotation = Quaternion.Euler(0f, 3f * i, 1.5f);
            }
            // Two loose blocks at the foot of the face, just prised off.
            for (int i = 0; i < 2; i++)
            {
                var b = Box(root, rock, new Vector3(0.7f, 0.42f, 0.6f),
                    new Vector3(len * 0.12f - 1.7f - i * 0.95f, 0.21f,
                        faceZ - 0.4f + i * 0.5f));
                b.transform.localRotation = Quaternion.Euler(0f, 24f + i * 41f, 0f);
            }

            // --- the brick, stacked at the front -----------------------------
            //
            // Six of them, two courses of three, the odd course offset half a
            // brick -- the same bond `CampPiles.BuildBrickCourse` lays the
            // pile in, at the same size, so the goods by the fire and the
            // goods at the yard are plainly the same thing.
            const float BrickL = 0.36f, BrickH = 0.12f, BrickW = 0.18f;
            // **On the slab, just inside the open front**, not out on the
            // grass: the ground is never flattened, so anything set down
            // outside the footing either floats at one corner or is buried
            // at the other, and a six-brick stack is far too small to
            // survive either. Inside the opening it stands on a known floor,
            // and the gloom behind it is what the orange reads against.
            Vector3 stackAt = new Vector3(-len * 0.5f + 1.0f, 0.35f, -wid * 0.26f);
            for (int i = 0; i < 6; i++)
            {
                int row = i / 3, col = i % 3;
                Box(root, brick, new Vector3(BrickL, BrickH, BrickW),
                    stackAt + new Vector3(
                        (col - 1) * 0.38f + (row % 2 == 1 ? 0.19f : 0f),
                        BrickH * 0.5f + row * (BrickH + 0.01f),
                        0f));
            }
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
