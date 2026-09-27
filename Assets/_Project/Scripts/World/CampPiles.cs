using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **What the camp has gathered, stacked on the ground beside the fire.**
    ///
    /// Kevin, 2026-09-19: *"they will pile them close to the campfire."* So
    /// the stores are not a number in a sheet — they are a thing you fly over
    /// and read. Four stacks beside a fire is a working camp; one stack and
    /// three empty spaces is a camp that has run out of something.
    ///
    /// Drawn FROM the ledger and owning nothing, exactly like `BuildSite` and
    /// the parked crew. A camp three kilometres astern has no piles in the
    /// scene and has lost nothing by it.
    public class CampPiles : MonoBehaviour
    {
        Outpost outpost;

        /// Where the ring of stacks sits, metres from the fire. Outside the
        /// crew's own ring (`Outpost.FireRingRadius`, 2.9 m) so that people
        /// and goods do not stand in each other.
        const float Radius = 5.2f;

        /// Most units drawn in one stack. The ceiling starts at ten and a
        /// store hut takes it to thirty; past a dozen the stack stops being
        /// countable anyway, so it grows in height and then stops. Public
        /// because the deck load aboard (`Ship.ShipHold`) is drawn with the
        /// same piles and stops at the same dozen.
        public const int MaxDrawn = 12;

        readonly Dictionary<string, Transform> stacks = new Dictionary<string, Transform>();
        readonly Dictionary<string, int> drawn = new Dictionary<string, int>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        // --- the store hut (2026-09-24) -------------------------------------
        //
        // Kevin, phone playtest: *"the initial storage is by the campfire.
        // but as soon as a storage hut is built all resources are to be
        // moved from around the fireplace into the storage hut."* Once a
        // Storage/Storehouse stands (`OutpostLedger.HasStorageBuilding`) the
        // ring by the fire is not drawn any more. The hut draws what its
        // kit has racks for (`StoreStockView`: timber, boards, food); what it
        // has no rack for (stone, ore, brick, arrows, ...) is stacked on the
        // ground beside the hut, on its fire-facing side, either side of the
        // door the hands walk to -- so nothing the camp owns goes unseen.
        //
        // **The move.** On the raise itself (not on a load that finds the
        // hut already standing) the ring does not blink out: each stack
        // drains over `MoveSeconds` while the same units appear at the hut
        // (`StillByFire` is subtracted from what the hut and its stacks
        // show). The ledger is untouched throughout: there is one store, and
        // this is only where it is drawn.

        /// The hut the store is drawn at; null = the ring by the fire.
        Building hut;
        /// The ring has been drawn at least once with no hut standing, so a
        /// hut appearing is a raise worth watching the goods move for.
        bool drewRing;
        /// Units of each resource still drawn by the fire mid-move.
        readonly Dictionary<string, int> byFire = new Dictionary<string, int>();
        readonly List<string> moveKeys = new List<string>();
        float moveClock;
        /// Roughly how long the whole move takes, whatever the stack sizes.
        const float MoveSeconds = 5f;
        /// One tick of the move: every stack by the fire gives up a share.
        const float MoveTick = 0.25f;

        readonly Dictionary<string, Transform> hutStacks = new Dictionary<string, Transform>();
        readonly Dictionary<string, int> hutDrawn = new Dictionary<string, int>();
        /// First-seen order of the goods stacked beside the hut: the slot a
        /// resource gets, stable for the session.
        readonly List<string> hutOrder = new List<string>();
        StoreStockView hutView;

        /// Metres between the hut-side stacks, and their distance out from
        /// the wall. The middle slot is left empty: that is the door the
        /// hands walk to (`CampWorker.StoreSpot`, `EdgeBeyond` + 0.6 m).
        const float HutSlotSpacing = 1.4f, HutStandOff = 1.1f;

        /// **The storage building the store is kept in**, or null while
        /// the store is the ring by the fire. The standing Storage or
        /// Storehouse nearest the ledger's own store point
        /// (`OutpostLedger.StoreAt`, the first raised row of either), so
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
            string a = BuildPlans.Storage.id, b = BuildPlans.Storehouse.id;
            Building best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < built.Count; i++)
            {
                var x = built[i];
                if (x == null || (x.Id != a && x.Id != b)) continue;
                if (!anchored) return x;
                Vector3 d = x.transform.position - want;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = x; }
            }
            return best;
        }

        /// Units of `resource` still drawn by the fire while the store moves
        /// into a newly raised hut; 0 otherwise. What the hut's own display
        /// subtracts so the same unit is never drawn in both places.
        public int StillByFire(string resource)
        {
            if (hut == null || string.IsNullOrEmpty(resource)) return 0;
            return byFire.TryGetValue(resource, out int n) ? n : 0;
        }

        public static CampPiles EnsureOn(Outpost owner)
        {
            if (owner == null) return null;
            var found = owner.GetComponentInChildren<CampPiles>(true);
            if (found != null) return found;

            var go = new GameObject("CampPiles");
            go.transform.SetParent(owner.transform, false);
            var piles = go.AddComponent<CampPiles>();
            piles.outpost = owner;
            return piles;
        }

        void LateUpdate()
        {
            if (outpost == null || outpost.Ledger == null) return;
            // Nothing is kept anywhere until the fire is lit, so there is
            // nothing to draw and no place to draw it round.
            if (!outpost.HasCamp) return;
            Refresh();
        }

        /// Bring the stacks up to what the ledger says. Cheap when nothing has
        /// changed: a stack is rebuilt only when its whole-unit count moves.
        public void Refresh()
        {
            var l = outpost.Ledger;
            Vector3 fire = outpost.CampCentre;

            var standing = StoreBuildingOf(outpost);
            // The second test catches a hut pulled down: Unity's `==` calls a
            // destroyed hut equal to null, so `standing != hut` alone never
            // notices and its side stacks would be left standing.
            if (standing != hut || (hut == null && hutStacks.Count > 0)) Rehome(standing);

            if (hut != null) { StepMove(Time.deltaTime); DrawAtHut(l, fire); return; }

            drewRing = true;
            // A stable order, so a stack does not hop round the fire when a
            // new resource appears. `stores` is append-only in practice, but
            // "in practice" is how a camp ends up rearranging itself on
            // arrival, so the angle is taken from the resource NAME.
            foreach (var s in l.stores)
            {
                if (s == null || string.IsNullOrEmpty(s.resource)) continue;
                // What the pile physically holds: booked-out units stay on
                // it until the man sent for them lifts them (2026-09-24,
                // `OutpostLedger.OnStorePile`).
                SetRing(s.resource, l.OnStorePile(s.resource), fire);
            }
        }

        /// The store changed buildings: a hut raised (start the move), a
        /// hut pulled down (straight back to the ring), or a different hut
        /// taking over (just redraw there).
        void Rehome(Building standing)
        {
            bool raised = hut == null && standing != null;
            hut = standing;
            hutView = hut != null ? hut.GetComponent<StoreStockView>() : null;
            moveClock = 0f;
            byFire.Clear();
            if (raised && drewRing)
                foreach (var kv in drawn)
                    if (kv.Value > 0) byFire[kv.Key] = kv.Value;
            // Every hut-side stack is redrawn at the new spot (or cleared).
            foreach (var t in hutStacks.Values)
                if (t != null) Destroy(t.gameObject);
            hutStacks.Clear();
            hutDrawn.Clear();
        }

        /// One step of the move: each stack still by the fire gives up a
        /// share, sized so the whole ring is empty in about `MoveSeconds`.
        void StepMove(float dt)
        {
            if (byFire.Count == 0) return;
            moveClock += dt;
            if (moveClock < MoveTick) return;
            moveClock = 0f;
            moveKeys.Clear();
            moveKeys.AddRange(byFire.Keys);
            foreach (var k in moveKeys)
            {
                int n = byFire[k];
                // A drawn stack tops out at `MaxDrawn`, so 40 logs by the
                // fire read as 12 and leave as 12: the undrawn surplus goes
                // on the first tick.
                if (n > MaxDrawn) n = MaxDrawn;
                int step = Mathf.Max(1, Mathf.CeilToInt(MaxDrawn * MoveTick / MoveSeconds));
                n -= step;
                if (n <= 0) byFire.Remove(k); else byFire[k] = n;
            }
        }

        void DrawAtHut(OutpostLedger l, Vector3 fire)
        {
            foreach (var s in l.stores)
            {
                if (s == null || string.IsNullOrEmpty(s.resource)) continue;
                int whole = l.OnStorePile(s.resource);
                int atFire = Mathf.Min(StillByFire(s.resource), whole);
                SetRing(s.resource, atFire, fire);
                int atHut = Mathf.Max(0, whole - atFire);
                if (hutView != null && hutView.Shows(s.resource)) atHut = 0;
                SetHutSide(s.resource, atHut, fire);
            }
        }

        void SetRing(string resource, int count, Vector3 fire)
        {
            if (drawn.TryGetValue(resource, out int was) && was == count) return;
            drawn[resource] = count;
            // Angle from the name: the same resource lands in the same place
            // at every camp, which is what lets a player read a camp from the
            // air without looking anything up.
            float a = Mathf.Abs(resource.GetHashCode() % 360) * Mathf.Deg2Rad;
            Vector3 at = fire + new Vector3(Mathf.Cos(a) * Radius, 0f, Mathf.Sin(a) * Radius);
            at.y = outpost.GroundAt(at);
            Rebuild(stacks, "Pile_", resource, count, at, Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f));
        }

        void SetHutSide(string resource, int count, Vector3 fire)
        {
            if (hutDrawn.TryGetValue(resource, out int was) && was == count) return;
            hutDrawn[resource] = count;
            if (count <= 0 && !hutStacks.ContainsKey(resource)) return;
            if (!hutOrder.Contains(resource)) hutOrder.Add(resource);
            Vector3 at = HutSlot(hutOrder.IndexOf(resource), fire, out Quaternion facing);
            Rebuild(hutStacks, "HutPile_", resource, count, at, facing);
        }

        /// Slot `i` beside the hut: just outside its footprint on the side
        /// facing the fire (the side the hands come to), alternating right
        /// and left of the door, the door itself left clear.
        Vector3 HutSlot(int i, Vector3 fire, out Quaternion facing)
        {
            Vector3 c = hut.transform.position;
            Vector3 d = fire - c;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) { d = hut.transform.forward; d.y = 0f; }
            d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            Vector2 fp = hut.Footprint;
            if (fp.x <= 0f || fp.y <= 0f) fp = BuildPlans.Named(hut.Id).footprint;
            float hx = Mathf.Max(0.5f, fp.x * 0.5f), hz = Mathf.Max(0.5f, fp.y * 0.5f);
            Vector3 local = Quaternion.Euler(0f, -hut.transform.eulerAngles.y, 0f) * d;
            float ax = Mathf.Abs(local.x), az = Mathf.Abs(local.z);
            float reach = Mathf.Min(ax > 1e-4f ? hx / ax : float.MaxValue,
                                    az > 1e-4f ? hz / az : float.MaxValue);
            Vector3 side = new Vector3(-d.z, 0f, d.x);
            int k = i / 2 + 1;
            float lateral = (i % 2 == 0 ? 1f : -1f) * k * HutSlotSpacing;
            Vector3 at = c + d * (reach + HutStandOff) + side * lateral;
            at.y = outpost.GroundAt(at);
            facing = Quaternion.LookRotation(side, Vector3.up);
            return at;
        }

        /// What a resource's pile looks like. Logs are cross-stacked
        /// (Timber/Boards); everything else used to be one generic heap of
        /// coloured cubes, which read as "a pile of something" and nothing
        /// more — Kevin, 2026-09-22: *"assets for stones and food seem to be
        /// missing, I can't see their gathered versions anywhere."* They
        /// were there, just unreadable as stone or food specifically, so
        /// each family now gets its own silhouette.
        enum PileShape { Logs, Cairn, Courses, Bundle, Sacks, Heap }

        static PileShape ShapeFor(string resource)
        {
            if (resource == Res.Timber || resource == Res.Boards) return PileShape.Logs;
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

        void Rebuild(Dictionary<string, Transform> into, string prefix, string resource, int count,
            Vector3 at, Quaternion facing)
        {
            if (!into.TryGetValue(resource, out var stack) || stack == null)
            {
                var go = new GameObject(prefix + resource);
                go.transform.SetParent(transform, false);
                stack = go.transform;
                into[resource] = stack;
            }

            stack.position = at;
            stack.rotation = facing;
            DrawPile(stack, resource, count, false);
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

            // A pile of six or more sacks earns one open crate beside it —
            // enough goods that some of it travelled boxed, not carried.
            if (shape == PileShape.Sacks && n >= 6)
                BuildCrate(pile, resource, n);
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
                case PileShape.Cairn:
                    BuildCairnBlock(stack, mat, i, row, col, seed, tidy);
                    break;
                case PileShape.Courses:
                    BuildBrickCourse(stack, mat, row, col);
                    break;
                case PileShape.Bundle:
                    BuildShaft(stack, mat, i, n);
                    break;
                case PileShape.Sacks:
                    BuildSack(stack, resource, i, row, col);
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

        static void BuildLog(Transform stack, Material mat, int row, int col, bool tidy)
        {
            // Cross-piled, the way timber is actually stacked. Tidy (a deck
            // load): every course the same way, along z, so a lashed stack of
            // timber runs fore and aft and nothing stands up out of it.
            var go = NewPrimitive(stack, PrimitiveType.Cylinder, mat);
            bool across = tidy || row % 2 == 1;
            go.transform.localScale = new Vector3(0.24f, 0.8f, 0.24f);
            go.transform.localRotation = Quaternion.Euler(
                across ? 90f : 0f, across ? 0f : 90f, 0f);
            go.transform.localPosition = new Vector3(
                across ? (col - 1) * 0.32f : 0f,
                0.13f + row * 0.26f,
                across ? 0f : (col - 1) * 0.32f);
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
        static void BuildCairnBlock(Transform stack, Material mat, int i, int row, int col, int seed,
            bool tidy)
        {
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

        /// One open crate: a box with a smaller, darker box let into the top
        /// to read as an open interior rather than a solid block.
        static void BuildCrate(Transform stack, string resource, int afterIndex)
        {
            var wood = MatFor(Res.Boards, false);
            var dark = MatFor(resource, true);

            int row = afterIndex / 3, col = afterIndex % 3;
            Vector3 at = new Vector3((col - 1) * 0.42f + 0.5f, 0.2f + row * 0.1f, 0.35f);

            var body = NewPrimitive(stack, PrimitiveType.Cube, wood);
            body.transform.localScale = new Vector3(0.5f, 0.4f, 0.5f);
            body.transform.localPosition = at;

            var inset = NewPrimitive(stack, PrimitiveType.Cube, dark);
            inset.transform.localScale = new Vector3(0.36f, 0.22f, 0.36f);
            inset.transform.localPosition = at + new Vector3(0f, 0.12f, 0f);
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
