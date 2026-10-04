using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **The one drawing of goods as loose units (2026-10-03: no longer the
    /// camp store).**
    ///
    /// Until 2026-10-03 this drew the camp's STORE: a ring of stacks round
    /// the fire (Kevin 2026-09-19, *"they will pile them close to the
    /// campfire"*), then, once a store hut stood, whatever its kit had no
    /// rack for stacked on the grass beside it -- the stone heaps, the fish
    /// pile and the crate beside the kitchen. Kevin, 2026-10-02: *"this
    /// issue of resources just stacking somewhere in the proximity of the
    /// structures is not okay"*, and on 2026-10-03 he approved the
    /// storage-slot containers: every stored good lives in an authored
    /// container slot (`StorageSlots`, `StorageSlotView`), capacity =
    /// visible slots. So the ring, the hut-side stacks, the move between
    /// them and the open crate are GONE. **Until the slot art is imported
    /// the store is invisible in the world** -- expected; the sheets still
    /// count it.
    ///
    /// What stays, because other things draw goods with it:
    /// <list type="bullet">
    /// <item>`DrawPile` -- a fallen hand's dropped load (`GroundLoadPiles`)
    /// and the deck load aboard.</item>
    /// <item>`BuildUnit` -- one carried unit (`CargoVisual`, a shoulder
    /// load, the home beach).</item>
    /// <item>`StoreBuildingOf` -- which store hut the store is kept in
    /// (raid alarm, raid walker, the crew's alarm run).</item>
    /// </list>
    /// No instance is created any more (`Outpost` used to `EnsureOn` one per
    /// camp); it stays a `MonoBehaviour` only so the class keeps its file
    /// and script GUID.
    public class CampPiles : MonoBehaviour
    {
        /// Most units drawn in one pile. Past a dozen a stack stops being
        /// countable, so it stops. Public because the deck load aboard
        /// (`Ship.ShipHold`) stops at the same dozen.
        public const int MaxDrawn = 12;

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        /// **The storage building the store is kept in**, or null while
        /// the store is the ring by the fire. The standing store hut nearest
        /// the ledger's own store point (`OutpostLedger.StoreAt`, the first
        /// raised row), so
        /// the hands, these stacks and the books agree on which one. Found
        /// by plan id among `Outpost.Built` rather than by an exact
        /// position match, so a trip booked before the hut stood, or a
        /// hut re-sited on load, still finds it.
        public static Building StoreBuildingOf(Outpost o)
        {
            if (o == null) return null;
            var l = o.Ledger;
            if (l == null || !l.HasStorageBuilding) return null;
            var built = o.Built;
            if (built == null) return null;
            bool anchored = l.StoreAt(out Vector3 want);
            string a = BuildPlans.Storage.id;
            Building best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < built.Count; i++)
            {
                var x = built[i];
                if (x == null || x.Id != a) continue;
                if (!anchored) return x;
                Vector3 d = x.transform.position - want;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = x; }
            }
            return best;
        }

        /// What a resource's pile looks like. Logs are cross-stacked
        /// (Timber/Boards); everything else used to be one generic heap of
        /// coloured cubes, which read as "a pile of something" and nothing
        /// more — Kevin, 2026-09-22: *"assets for stones and food seem to be
        /// missing, I can't see their gathered versions anywhere."* They
        /// were there, just unreadable as stone or food specifically, so
        /// each family now gets its own silhouette.
        ///
        /// **Boards are planks, not logs (Astra resource kit v1, Kevin
        /// approved 2026-09-30):** `Planks` is its own silhouette, flat golden
        /// boards laid course on course, no longer the timber cylinders.
        enum PileShape { Logs, Planks, Cairn, Courses, Bundle, Sacks, Heap }

        static PileShape ShapeFor(string resource)
        {
            if (resource == Res.Timber) return PileShape.Logs;
            if (resource == Res.Boards) return PileShape.Planks;
            if (resource == Res.Stone || resource == Res.Ore) return PileShape.Cairn;
            // **Brick is the opposite of a cairn and that is the whole
            // point**, 2026-09-22: rough stone lies where it was tipped,
            // brick is stacked by somebody who cut it square. A pile of
            // brick beside a pile of stone should read, at a glance and from
            // the air, as the same material after a day's work.
            if (resource == Res.Brick) return PileShape.Courses;
            // Arrows stand; nothing else in a camp does. A bundle upright in
            // the grass is the one silhouette here that is taller than it is
            // wide, so the quiver reads from the air without a label.
            if (resource == Res.Arrows) return PileShape.Bundle;
            if (Economy.FoodBook.IsFoodish(resource)) return PileShape.Sacks;
            return PileShape.Heap;
        }

        // --- the one drawing of a unit of cargo (2026-09-24) -----------------
        //
        // Kevin, phone playtest: *"the cargo gets HUGE on the ship. much
        // larger than it is on land."* The deck had its own brig-era meshes
        // (`CargoVisual`, a 1.5 m x 0.42 m log, a 1.1 m block of stone) at two
        // to three times the size of the pile by the fire. There is one
        // drawing now, and it is this one: `DrawPile` is what the ring by the
        // fire, the hut-side stacks and the deck load aboard all call, and
        // `BuildUnit` is one unit of it on its own (what `CargoVisual.Build`
        // hands out, for a crewman's shoulder or the home beach).

        /// **Draw `count` units of `resource` into `pile`**, clearing what was
        /// there -- the same shapes, sizes, materials and layout as the stacks
        /// by the fire, capped at `MaxDrawn`. The pile is drawn about its own
        /// origin, base on y = 0, three columns along its local x.
        ///
        /// `tidy` is for cargo lashed on a deck: the same units, stowed
        /// square. Every log lies along the pile's local z (fore and aft when
        /// the pile is unrotated in a ship's frame), and stone and rubble keep
        /// their shapes but lose their tumble -- yaw within +/-2 deg, no tilt.
        /// Ashore passes false and is drawn exactly as before.
        public static void DrawPile(Transform pile, string resource, int count, bool tidy)
        {
            if (pile == null) return;
            for (int i = pile.childCount - 1; i >= 0; i--)
                Destroy(pile.GetChild(i).gameObject);
            if (count <= 0 || string.IsNullOrEmpty(resource)) return;

            var mat = MatFor(resource, false);
            int n = Mathf.Min(count, MaxDrawn);
            var shape = ShapeFor(resource);
            int seed = Mathf.Abs(resource.GetHashCode());

            for (int i = 0; i < n; i++)
                DrawUnit(pile, resource, shape, mat, i, n, seed, tidy);
            // (The open crate a pile of six sacks used to earn is gone,
            // 2026-10-03: it was the "crate beside the kitchen" Kevin flagged,
            // and nobody drops a crate with an armful.)
        }

        /// **One unit of `resource`**, the size it is in the pile by the fire,
        /// under a new root named `Cargo_<resource>` parented to `parent`,
        /// centred on the root's vertical axis with its base at about y = 0.
        /// Drawn tidy (a log lies along the root's z). An arrow unit is a
        /// small bundle: one shaft alone is a stick.
        public static GameObject BuildUnit(string resource, Transform parent)
        {
            if (string.IsNullOrEmpty(resource)) resource = Res.Timber;
            var root = new GameObject($"Cargo_{resource}");
            root.transform.SetParent(parent, false);
            var body = new GameObject("Unit").transform;
            body.SetParent(root.transform, false);

            var mat = MatFor(resource, false);
            var shape = ShapeFor(resource);
            int seed = Mathf.Abs(resource.GetHashCode());
            if (shape == PileShape.Bundle)
                for (int i = 0; i < UnitBundle; i++)
                    DrawUnit(body, resource, shape, mat, i, UnitBundle, seed, true);
            else
                // Index 1 is the middle column of the bottom course, the unit
                // the pile layout already puts nearest its own centre.
                DrawUnit(body, resource, shape, mat, 1, 1, seed, true);

            // Whatever the layout's row/column offset left, take it off: the
            // caller places the root, and the unit should be ON it.
            if (shape != PileShape.Bundle && body.childCount > 0)
            {
                Vector3 p = body.GetChild(0).localPosition;
                body.localPosition = new Vector3(-p.x, 0f, -p.z);
            }
            return root;
        }

        /// Shafts in one arrow "unit" drawn on its own.
        const int UnitBundle = 4;

        /// The i-th unit of an n-unit pile.
        static void DrawUnit(Transform stack, string resource, PileShape shape, Material mat,
            int i, int n, int seed, bool tidy)
        {
            int row = i / 3, col = i % 3;
            switch (shape)
            {
                case PileShape.Logs:
                    BuildLog(stack, mat, row, col, tidy);
                    break;
                case PileShape.Planks:
                    // Astra resource kit v1: a flat board, not a log. The old
                    // log stands in only if the mesh did not load.
                    if (!BuildPlank(stack, i, row, col, tidy)) BuildLog(stack, mat, row, col, tidy, false);
                    break;
                case PileShape.Cairn:
                    BuildCairnBlock(stack, mat, resource, i, row, col, seed, tidy);
                    break;
                case PileShape.Courses:
                    BuildBrickCourse(stack, mat, row, col);
                    break;
                case PileShape.Bundle:
                    BuildShaft(stack, mat, i, n);
                    break;
                case PileShape.Sacks:
                    // Astra food kit v1 (2026-09-30): the actual food where
                    // there is a model, the old sack where there is not.
                    var food = FoodShapeFor(resource);
                    if (food != null) BuildFood(stack, food, i, n, row, col, tidy);
                    else BuildSack(stack, resource, i, row, col);
                    break;
                default:
                    BuildHeapCube(stack, mat, i, row, col, tidy);
                    break;
            }
        }

        static GameObject NewPrimitive(Transform parent, PrimitiveType type, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static void BuildLog(Transform stack, Material mat, int row, int col, bool tidy, bool tryKit = true)
        {
            // Cross-piled, the way timber is actually stacked. Tidy (a deck
            // load): every course the same way, along z, so a lashed stack of
            // timber runs fore and aft and nothing stands up out of it.
            bool across = tidy || row % 2 == 1;

            // Astra resource kit v1, Kevin approved 2026-09-30: the real log
            // (`Timber_Unit`, 1.6 m x 0.24 m, base origin, long axis along z),
            // same columns, same course height, same cross-piling -- an even
            // course lies along x (a quarter turn), an odd one along z. The
            // primitive below is the fallback if the mesh did not load.
            if (tryKit && ResourceKit.Spawn(Res.Timber, false, stack,
                    new Vector3(across ? (col - 1) * 0.32f : 0f,
                                0.01f + row * 0.26f,
                                across ? 0f : (col - 1) * 0.32f),
                    across ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f)) != null)
                return;

            var go = NewPrimitive(stack, PrimitiveType.Cylinder, mat);
            go.transform.localScale = new Vector3(0.24f, 0.8f, 0.24f);
            go.transform.localRotation = Quaternion.Euler(
                across ? 90f : 0f, across ? 0f : 90f, 0f);
            go.transform.localPosition = new Vector3(
                across ? (col - 1) * 0.32f : 0f,
                0.13f + row * 0.26f,
                across ? 0f : (col - 1) * 0.32f);
        }

        /// **One milled board of a pile** (Astra resource kit v1, Kevin approved
        /// 2026-09-30: boards used to share the log cylinder). `Boards_Unit` is
        /// 1.6 m x 0.25 m x 0.075 m, base origin, long axis along z. Three
        /// boards a course side by side, each course laid on the last, so a
        /// dozen is a low golden slab about 0.3 m high -- flat where the timber
        /// beside it is round. Ashore each board sits a hair off true (a yaw
        /// within +/-3.6 deg, a nudge along its length); tidy, lashed on a
        /// deck, they are laid square. False if the mesh did not load.
        static bool BuildPlank(Transform stack, int i, int row, int col, bool tidy)
        {
            float yaw = tidy ? 0f : ((i * 37) % 7 - 3) * 1.2f;
            float slide = tidy ? 0f : ((i * 13) % 5 - 2) * 0.03f;
            return ResourceKit.Spawn(Res.Boards, false, stack,
                new Vector3((col - 1) * 0.28f, row * 0.078f, slide),
                Quaternion.Euler(0f, yaw, 0f)) != null;
        }

        static void BuildHeapCube(Transform stack, Material mat, int i, int row, int col, bool tidy)
        {
            // Rubble with no shape of its own: a plain heap. Tidy: squared
            // up, within +/-2 deg.
            var go = NewPrimitive(stack, PrimitiveType.Cube, mat);
            go.transform.localScale = new Vector3(0.44f, 0.34f, 0.44f);
            go.transform.localRotation = Quaternion.Euler(0f, tidy ? (i * 37) % 5 - 2 : (i * 37) % 360, 0f);
            go.transform.localPosition = new Vector3(
                (col - 1) * 0.42f, 0.17f + row * 0.3f,
                ((i % 5) - 2) * 0.09f);
        }

        /// Stone/ore: irregular flattened blocks stacked lower and wider
        /// than the generic heap, the way a cairn of quarried rock actually
        /// sits — not masonry, not a grid of identical cubes.
        static void BuildCairnBlock(Transform stack, Material mat, string resource, int i, int row, int col,
            int seed, bool tidy)
        {
            // Astra resource kit v1, Kevin approved 2026-09-30: the real chunk
            // (`Stone_Unit` / `Ore_Unit`, about 0.49 x 0.43 x 0.31 m, base
            // origin), in the same columns, spread and rise as the cubes it
            // replaces. Ashore each is spun to any heading and given a slight
            // size variation so a cairn is not a grid; tidy, it keeps its
            // shape, yaw within +/-2 deg and no variation. No tilt: the unit
            // has a flat base. The cube below is the fallback.
            if (ResourceKit.Family(resource) != null)
            {
                float yawK = tidy ? (Hash01(seed + i * 7) - 0.5f) * 4f : Hash01(seed + i * 7) * 360f;
                float sizeK = tidy ? 1f : Mathf.Lerp(0.85f, 1.1f, Hash01(seed + i * 3));
                if (ResourceKit.Spawn(resource, false, stack,
                        new Vector3((col - 1) * 0.5f, row * 0.20f, ((i % 5) - 2) * 0.14f),
                        Quaternion.Euler(0f, yawK, 0f), sizeK) != null)
                    return;
            }

            var go = NewPrimitive(stack, PrimitiveType.Cube, mat);

            float sx = Mathf.Lerp(0.3f, 0.55f, Hash01(seed + i * 3));
            float sz = Mathf.Lerp(0.3f, 0.55f, Hash01(seed + i * 3 + 1));
            float sy = Mathf.Lerp(0.18f, 0.30f, Hash01(seed + i * 3 + 2));
            go.transform.localScale = new Vector3(sx, sy, sz);

            // Tidy (lashed on a deck): the same blocks, squared up -- yaw
            // within +/-2 deg and flat, so the load does not read as tipped.
            float yaw = tidy ? (Hash01(seed + i * 7) - 0.5f) * 4f : Hash01(seed + i * 7) * 360f;
            float tiltX = tidy ? 0f : (Hash01(seed + i * 11) - 0.5f) * 16f;
            float tiltZ = tidy ? 0f : (Hash01(seed + i * 13) - 0.5f) * 16f;
            go.transform.localRotation = Quaternion.Euler(tiltX, yaw, tiltZ);

            // Wider spread and a shallower rise than the heap: a cairn
            // spreads out before it stacks up.
            go.transform.localPosition = new Vector3(
                (col - 1) * 0.5f, 0.12f + row * 0.20f,
                ((i % 5) - 2) * 0.14f);
        }

        /// Brick: neat staggered courses. Flat cubes of one size laid in
        /// rows of three, every other row shifted half a brick along, the
        /// way a course is actually bonded -- so the silhouette is a low
        /// rectangular block with a stepped edge, which is nothing else in
        /// the camp. No jitter and no rotation at all: the irregularity is
        /// what says "rock", and a brick stack has to say the opposite.
        static void BuildBrickCourse(Transform stack, Material mat, int row, int col)
        {
            // Astra resource kit v1, Kevin approved 2026-09-30: the real brick
            // (`Brick_Unit`, 0.36 x 0.18 x 0.12 m, base origin, long side along
            // x), laid exactly as the cube was -- same bond, stagger and rise.
            // The cube below is the fallback.
            float shift = (row % 2 == 1) ? 0.19f : 0f;
            if (ResourceKit.Spawn(Res.Brick, false, stack,
                    new Vector3((col - 1) * 0.38f + shift, 0.01f + row * 0.13f, 0f),
                    Quaternion.identity) != null)
                return;

            var go = NewPrimitive(stack, PrimitiveType.Cube, mat);
            go.transform.localScale = new Vector3(0.36f, 0.12f, 0.18f);
            // Half a brick's shift on the odd courses, and a hair of gap
            // between bricks so the row reads as laid rather than as one
            // long slab.
            float stagger = (row % 2 == 1) ? 0.19f : 0f;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localPosition = new Vector3(
                (col - 1) * 0.38f + stagger,
                0.07f + row * 0.13f,
                0f);
        }

        /// Arrows: a standing bundle of thin shafts in a slight fan, bound
        /// at the waist. Capped at eight drawn however many the camp holds --
        /// past that a bundle stops being countable and starts being a bush,
        /// and the tile by the fire is where the number lives anyway.
        static void BuildShaft(Transform stack, Material mat, int i, int n)
        {
            if (i >= MaxBundle) return;
            var go = NewPrimitive(stack, PrimitiveType.Cylinder, mat);
            go.transform.localScale = new Vector3(0.04f, 0.9f, 0.04f);
            // The fan: each shaft leans a little further out than the last,
            // around a ring, so the bundle splays at the top and gathers at
            // the foot the way a sheaf actually stands.
            float a = i * Mathf.PI * 2f / Mathf.Max(1, Mathf.Min(n, MaxBundle));
            const float Lean = 7f;
            go.transform.localRotation = Quaternion.Euler(
                Mathf.Sin(a) * Lean, i * 31f, Mathf.Cos(a) * Lean);
            go.transform.localPosition = new Vector3(
                Mathf.Cos(a) * 0.07f, 0.9f, Mathf.Sin(a) * 0.07f);
        }

        /// Shafts drawn in one bundle, whatever the pile holds.
        const int MaxBundle = 8;

        // --- Astra food kit v1, Kevin approved 2026-09-30 -----------------------
        //
        // The seven raw ingredients are drawn as themselves, not as sacks:
        // `Resources/Kits/Food/<Name>_Unit` (metre scale, base origin, one mesh
        // on the shared GameColor material -- `FoodKitImport`). Cooked dishes,
        // flour, biscuit, generic Food and Game keep the sack: a grilled fish
        // drawn as a teal raw fish would be indistinguishable from the raw pile
        // beside it. Count, columns, rows, the MaxDrawn cap and the crate at
        // six are the sack rules unchanged; only the geometry differs.

        /// One food, ready to instance: a private copy of the unit mesh,
        /// lying down if it is tall and thin, re-centred on x/z with its base
        /// at y = 0, plus the shared kit material.
        sealed class FoodShape
        {
            public Mesh mesh;
            public Material mat;
            public Vector3 size;   // of the copy: x/z footprint, y height
            public bool elongated; // yawed loosely with alternate ends flipped, not spun
        }

        /// Cached once per resource; a null entry means "no model / failed to
        /// load", so the old sack is drawn and nothing is retried per frame.
        static readonly Dictionary<string, FoodShape> foodShapes = new Dictionary<string, FoodShape>();

        static bool HasFoodModel(string resource) =>
            resource == Res.Potato || resource == Res.Carrot || resource == Res.Onion
            || resource == Res.Wheat || resource == Res.Apple || resource == Res.Fish
            || resource == Res.Meat;

        static FoodShape FoodShapeFor(string resource)
        {
            if (!HasFoodModel(resource)) return null;
            if (foodShapes.TryGetValue(resource, out var cached) && (cached == null || cached.mesh != null))
                return cached;

            FoodShape fs = null;
            try
            {
                var prefab = Resources.Load<GameObject>("Kits/Food/" + resource + "_Unit");
                var mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>(true) : null;
                var src = mf != null ? mf.sharedMesh : null;
                if (src != null && src.isReadable)
                {
                    var rend = mf.GetComponent<MeshRenderer>();
                    fs = MakeFoodShape(src, mf.transform.localToWorldMatrix,
                        rend != null ? rend.sharedMaterial : null);
                }
                else Debug.LogWarning($"[CampPiles] Kits/Food/{resource}_Unit is missing or not readable; drawing sacks.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[CampPiles] food model {resource} failed ({e.Message}); drawing sacks.");
                fs = null;
            }
            foodShapes[resource] = fs;
            return fs;
        }

        static FoodShape MakeFoodShape(Mesh src, Matrix4x4 node, Material kitMat)
        {
            // Measure first: a tall thin food (carrot, wheat) lies down in a
            // pile, the way it is actually heaped.
            var srcVerts = src.vertices;
            var bounds = new Bounds(node.MultiplyPoint3x4(srcVerts[0]), Vector3.zero);
            for (int i = 1; i < srcVerts.Length; i++) bounds.Encapsulate(node.MultiplyPoint3x4(srcVerts[i]));
            bool lies = bounds.size.y > 2f * Mathf.Max(bounds.size.x, bounds.size.z);
            var m = (lies ? Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 90f)) : Matrix4x4.identity) * node;

            var verts = new Vector3[srcVerts.Length];
            var box = new Bounds(m.MultiplyPoint3x4(srcVerts[0]), Vector3.zero);
            for (int i = 0; i < verts.Length; i++)
            {
                verts[i] = m.MultiplyPoint3x4(srcVerts[i]);
                box.Encapsulate(verts[i]);
            }
            // A sanity fence (metres): a wrong axis or unit lands here, and
            // the pile draws sacks rather than a giant or invisible potato.
            float longest = Mathf.Max(box.size.x, Mathf.Max(box.size.y, box.size.z));
            if (longest < 0.03f || longest > 1.5f)
                throw new System.Exception($"unit is {longest:F3} m long");
            Vector3 shift = new Vector3(-box.center.x, -box.min.y, -box.center.z);
            for (int i = 0; i < verts.Length; i++) verts[i] += shift;

            var mesh = new Mesh { name = src.name + "_Pile", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = verts;
            var srcNormals = src.normals;
            if (srcNormals.Length == verts.Length)
            {
                var it = m.inverse.transpose;
                var nrm = new Vector3[verts.Length];
                for (int i = 0; i < nrm.Length; i++) nrm[i] = it.MultiplyVector(srcNormals[i]).normalized;
                mesh.normals = nrm;
            }
            var srcColors = src.colors;
            if (srcColors.Length == verts.Length) mesh.colors = srcColors;   // GameColor
            var srcUv = src.uv;
            if (srcUv.Length == verts.Length) mesh.uv = srcUv;
            var tris = src.triangles;
            if (m.determinant < 0f)
                for (int i = 0; i + 2 < tris.Length; i += 3) { int t = tris[i]; tris[i] = tris[i + 2]; tris[i + 2] = t; }
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            return new FoodShape
            {
                mesh = mesh,
                mat = kitMat,
                size = box.size,
                elongated = lies || Mathf.Max(box.size.x, box.size.z) > 1.4f * Mathf.Min(box.size.x, box.size.z),
            };
        }

        /// The i-th food of an n-unit pile: the sack layout (three columns
        /// along x, `row = i / 3`, a little depth stagger) with the pitch and
        /// layer height taken from the food's own size, so apples nest and
        /// fish lie side by side. A single unit sits on the pile's origin.
        static void BuildFood(Transform stack, FoodShape food, int i, int n, int row, int col, bool tidy)
        {
            var mat = food.mat != null ? food.mat : MatFor(Res.Food, false);
            var go = new GameObject("Food");
            go.transform.SetParent(stack, false);
            go.AddComponent<MeshFilter>().sharedMesh = food.mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;

            float pitch = Mathf.Max(0.2f, food.size.x + 0.06f);
            float layer = Mathf.Max(0.05f, food.size.y * 0.8f);   // rows nest a little
            Vector3 pos;
            if (n <= 1) pos = Vector3.zero;
            else pos = new Vector3(
                (col - 1) * pitch + (row % 2 == 1 ? pitch * 0.25f : -pitch * 0.25f),
                row * layer,
                ((i % 5) - 2) * 0.05f);
            go.transform.localPosition = pos;

            float yaw;
            if (tidy) yaw = ((i * 37) % 5) - 2f;   // lashed on a deck: square, +/-2 deg
            else if (food.elongated) yaw = ((i * 53) % 50) - 25f + ((i & 1) == 1 ? 180f : 0f);
            else yaw = (i * 53) % 360;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// Food/game: squashed sacks with a darker "band" (a second, smaller
        /// sphere sharing its centre) rather than a bare coloured cube.
        static void BuildSack(Transform stack, string resource, int i, int row, int col)
        {
            var main = MatFor(resource, false);
            var band = MatFor(resource, true);

            var go = NewPrimitive(stack, PrimitiveType.Sphere, main);
            go.transform.localScale = new Vector3(0.4f, 0.3f, 0.4f);
            go.transform.localRotation = Quaternion.Euler(0f, (i * 53) % 360, 0f);
            Vector3 pos = new Vector3(
                (col - 1) * 0.42f, 0.15f + row * 0.28f,
                ((i % 5) - 2) * 0.1f);
            go.transform.localPosition = pos;

            var bandGo = NewPrimitive(stack, PrimitiveType.Sphere, band);
            bandGo.transform.localScale = new Vector3(0.42f, 0.1f, 0.42f);
            bandGo.transform.localRotation = go.transform.localRotation;
            bandGo.transform.localPosition = pos;
        }

        /// A cheap deterministic 0..1 hash, so an irregular cairn looks the
        /// same every time it is rebuilt rather than re-jittering each
        /// resource tick.
        static float Hash01(int seed)
        {
            unchecked
            {
                int h = seed * 374761393;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7fffffff) / 2147483647f;
            }
        }

        static Material MatFor(string resource, bool darker)
        {
            string key = darker ? resource + "#dark" : resource;
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find(
                WorldArtStyle.Instance != null
                    ? "SeaSick/Environment Toon"
                    : "Universal Render Pipeline/Lit"));
            Color col = Res.Colour(resource);
            if (darker) col = new Color(col.r * 0.6f, col.g * 0.6f, col.b * 0.6f, col.a);
            m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            mats[key] = m;
            return m;
        }
    }
}
