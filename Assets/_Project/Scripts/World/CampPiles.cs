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
        /// countable anyway, so it grows in height and then stops.
        const int MaxDrawn = 12;

        readonly Dictionary<string, Transform> stacks = new Dictionary<string, Transform>();
        readonly Dictionary<string, int> drawn = new Dictionary<string, int>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

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

            // A stable order, so a stack does not hop round the fire when a
            // new resource appears. `stores` is append-only in practice, but
            // "in practice" is how a camp ends up rearranging itself on
            // arrival, so the angle is taken from the resource NAME.
            foreach (var s in l.stores)
            {
                if (s == null || string.IsNullOrEmpty(s.resource)) continue;
                if (drawn.TryGetValue(s.resource, out int was) && was == s.whole) continue;
                drawn[s.resource] = s.whole;
                Rebuild(s.resource, s.whole, fire);
            }
        }

        /// What a resource's pile looks like. Logs are cross-stacked
        /// (Timber/Boards); everything else used to be one generic heap of
        /// coloured cubes, which read as "a pile of something" and nothing
        /// more — Kevin, 2026-09-22: *"assets for stones and food seem to be
        /// missing, I can't see their gathered versions anywhere."* They
        /// were there, just unreadable as stone or food specifically, so
        /// each family now gets its own silhouette.
        enum PileShape { Logs, Cairn, Courses, Sacks, Heap }

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
            if (resource == Res.Food || resource == Res.Game) return PileShape.Sacks;
            return PileShape.Heap;
        }

        void Rebuild(string resource, int count, Vector3 fire)
        {
            if (!stacks.TryGetValue(resource, out var stack) || stack == null)
            {
                var go = new GameObject("Pile_" + resource);
                go.transform.SetParent(transform, false);
                stack = go.transform;
                stacks[resource] = stack;
            }

            // Angle from the name: the same resource lands in the same place
            // at every camp, which is what lets a player read a camp from the
            // air without looking anything up.
            float a = Mathf.Abs(resource.GetHashCode() % 360) * Mathf.Deg2Rad;
            Vector3 at = fire + new Vector3(Mathf.Cos(a) * Radius, 0f, Mathf.Sin(a) * Radius);
            at.y = outpost.GroundAt(at);
            stack.position = at;
            stack.rotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f);

            for (int i = stack.childCount - 1; i >= 0; i--)
                Destroy(stack.GetChild(i).gameObject);
            if (count <= 0) return;

            var mat = MatFor(resource, false);
            int n = Mathf.Min(count, MaxDrawn);
            var shape = ShapeFor(resource);
            int seed = Mathf.Abs(resource.GetHashCode());

            for (int i = 0; i < n; i++)
            {
                int row = i / 3, col = i % 3;

                switch (shape)
                {
                    case PileShape.Logs:
                        BuildLog(stack, mat, row, col);
                        break;
                    case PileShape.Cairn:
                        BuildCairnBlock(stack, mat, i, row, col, seed);
                        break;
                    case PileShape.Courses:
                        BuildBrickCourse(stack, mat, row, col);
                        break;
                    case PileShape.Sacks:
                        BuildSack(stack, resource, i, row, col);
                        break;
                    default:
                        BuildHeapCube(stack, mat, i, row, col);
                        break;
                }
            }

            // A pile of six or more sacks earns one open crate beside it —
            // enough goods that some of it travelled boxed, not carried.
            if (shape == PileShape.Sacks && n >= 6)
                BuildCrate(stack, resource, n);
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

        static void BuildLog(Transform stack, Material mat, int row, int col)
        {
            // Cross-piled, the way timber is actually stacked.
            var go = NewPrimitive(stack, PrimitiveType.Cylinder, mat);
            bool across = row % 2 == 1;
            go.transform.localScale = new Vector3(0.24f, 0.8f, 0.24f);
            go.transform.localRotation = Quaternion.Euler(
                across ? 90f : 0f, across ? 0f : 90f, 0f);
            go.transform.localPosition = new Vector3(
                across ? (col - 1) * 0.32f : 0f,
                0.13f + row * 0.26f,
                across ? 0f : (col - 1) * 0.32f);
        }

        static void BuildHeapCube(Transform stack, Material mat, int i, int row, int col)
        {
            // Rubble with no shape of its own: a plain heap.
            var go = NewPrimitive(stack, PrimitiveType.Cube, mat);
            go.transform.localScale = new Vector3(0.44f, 0.34f, 0.44f);
            go.transform.localRotation = Quaternion.Euler(0f, (i * 37) % 360, 0f);
            go.transform.localPosition = new Vector3(
                (col - 1) * 0.42f, 0.17f + row * 0.3f,
                ((i % 5) - 2) * 0.09f);
        }

        /// Stone/ore: irregular flattened blocks stacked lower and wider
        /// than the generic heap, the way a cairn of quarried rock actually
        /// sits — not masonry, not a grid of identical cubes.
        static void BuildCairnBlock(Transform stack, Material mat, int i, int row, int col, int seed)
        {
            var go = NewPrimitive(stack, PrimitiveType.Cube, mat);

            float sx = Mathf.Lerp(0.3f, 0.55f, Hash01(seed + i * 3));
            float sz = Mathf.Lerp(0.3f, 0.55f, Hash01(seed + i * 3 + 1));
            float sy = Mathf.Lerp(0.18f, 0.30f, Hash01(seed + i * 3 + 2));
            go.transform.localScale = new Vector3(sx, sy, sz);

            float yaw = Hash01(seed + i * 7) * 360f;
            float tiltX = (Hash01(seed + i * 11) - 0.5f) * 16f;
            float tiltZ = (Hash01(seed + i * 13) - 0.5f) * 16f;
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
