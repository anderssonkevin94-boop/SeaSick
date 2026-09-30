using System.Collections.Generic;
using SeaSick.Voyage;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// **The hold, seen on deck: barrels, sacks and crates on the coaster
    /// (Astra ship cargo, Kevin approved 2026-09-29; in the game 2026-09-30).**
    ///
    /// The coaster's hold is abstract (`ShipHold.SetAbstractStorage`: the
    /// hands set each unit down at the storage hatch and it is gone), so a
    /// full ship looked empty. This stands a FEW chunky props in fixed deck
    /// sockets and fills them from `VoyageManager`'s hold:
    ///
    /// - **Sockets** (`Plan`) are laid per hull section, so they follow the
    ///   modular layout: two pairs against the bulwarks on each section --
    ///   the stern's aft deck either side of the helm's fore-and-aft lane,
    ///   each middle's ends clear of its gun bay, the bow's foredeck forward
    ///   of the guns plus one prop on the centreline where the prow closes
    ///   in (further forward the sloped bow planking swallows a prop). A
    ///   candidate is dropped unless the deck is flat under its whole
    ///   footprint and it keeps clear of every gun (plus room for the
    ///   gunner), wall and doorway the walk graph knows. Ladders and the
    ///   storage hatch are on the centreline: side sockets stand 1.3 m+
    ///   outboard, the centre one 0.7 m forward of the hatch.
    /// - **How many**: `ceil(fill x sockets)` of them, fill = held / the
    ///   marked line (0 = a bare deck, the line = every socket). Split by
    ///   container in proportion to what is aboard (`ShipCargoKit.KindOf`,
    ///   largest remainder, every kind aboard shows at least once), crates
    ///   first, then sacks, then barrels, bow sockets before stern.
    /// - **Deck cargo** (past the line, `VoyageManager.Overloaded`) stacks a
    ///   second, smaller prop on the filled sockets, in proportion to how far
    ///   past the line she is: at the physical limit every socket is doubled.
    /// - **No colliders.** Crew walk the ship's own graph, not physics (their
    ///   capsules are triggers), so every socket is handed to
    ///   `CoasterNavigation.Build` as a reserved box whether or not a prop
    ///   stands there: routes never change as the hold fills and nobody
    ///   walks through a barrel. Loose colliders on the moving rigidbody
    ///   would only add to the hull compound.
    ///
    /// Polled four times a second; props are re-made only when the picture
    /// changes. Lives on the `ModularShipView`, so a refit takes it along.
    public sealed class ShipCargoDisplay : MonoBehaviour
    {
        /// One place on deck, in the view's frame.
        public struct Socket
        {
            public Vector3 pos;
            /// -1 port (x < 0), +1 starboard, 0 on the centreline.
            public int side;
            public string section;
        }

        /// Footprint half-size a socket reserves (a large crate turned fore
        /// and aft is 0.32 x 0.41 m; a large barrel 0.33 m round).
        static readonly Vector3 Half = new Vector3(0.34f, 0f, 0.42f);
        const float GunMargin = 0.15f;
        const float PollSeconds = 0.25f;

        readonly List<Socket> sockets = new List<Socket>();
        GameObject[] baseProps, topProps;
        string[] baseNames, topNames;
        VoyageManager voyage;
        Transform root;
        float nextPoll;
        string lastKey;

        public IReadOnlyList<Socket> Sockets => sockets;
        /// Props standing on deck now (base + stacked), for checks.
        public int Shown { get; private set; }

        /// **The sockets for this assembly**, ship-local, in fill order, from
        /// the walk graph's floors and obstacles (build it once first).
        public static List<Vector3> Plan(ModularShipView view, AssemblyResult asm, CoasterNavigation nav,
            Transform ship, out List<int> sides, out List<string> sections)
        {
            var pts = new List<Vector3>();
            sides = new List<int>();
            sections = new List<string>();
            var single = new List<(Vector3 p, int side, string key)>();
            var fittings = Fittings(view, ship);
            var hulls = asm.placed.FindAll(p => ModuleKind.IsHull(p.kind));
            // Bow first (by the storage hatch), then middles fore to aft, the stern last.
            hulls.Sort((a, b) => Order(a).CompareTo(Order(b)));
            foreach (var p in hulls)
            {
                Transform host = null;
                foreach (Transform t in view.transform) if (t.name.StartsWith(p.instanceKey + " (")) { host = t; break; }
                if (host == null) continue;
                bool raised = CoasterFamily.Raised(p.moduleId);
                float deck; (float x, float z)[] cands;
                if (p.kind == ModuleKind.Stern)
                {
                    // Forward of the helm, either side of the ladder; the aft
                    // pair loses its port place to the funnel.
                    deck = raised ? 3.68f : 1.48f;
                    cands = new[] { (1.78f, 2.78f), (1.78f, 1.90f) };
                }
                else if (p.kind == ModuleKind.Bow)
                {
                    // One pair forward of the guns, one prop on the centreline
                    // where the prow closes in (x = 0: a single socket).
                    deck = raised ? 3.08f : 1.055f;
                    cands = new[] { (1.35f, 2.50f), (0f, 3.15f) };
                }
                else
                {
                    deck = raised ? 3.08f : 0.88f;
                    cands = new[] { (1.75f, 0.45f), (1.75f, 2.55f) };
                }
                foreach (var (cx, cz) in cands)
                {
                    var got = new List<(Vector3 p, int side, string key)>();
                    foreach (int side in cx == 0f ? new[] { 0 } : new[] { -1, 1 })
                    {
                        var local = ship.InverseTransformPoint(host.TransformPoint(new Vector3(side * cx, 0f, cz)));
                        if (Fits(nav, fittings, local.x, local.z, deck, out float y))
                            got.Add((new Vector3(local.x, y, local.z), side, p.instanceKey));
                    }
                    // Matched pairs fill first; a centre socket or a place whose
                    // mirror is taken by a fitting fills last.
                    if (got.Count == 2) foreach (var g in got) { pts.Add(g.p); sides.Add(g.side); sections.Add(g.key); }
                    else single.AddRange(got);
                }
            }
            foreach (var g in single) { pts.Add(g.p); sides.Add(g.side); sections.Add(g.key); }
            return pts;
        }

        /// Ship-local boxes of the small fittings standing on the decks
        /// (funnel, helm, lanterns, ladders, landing posts, gun art): anything
        /// under 2 m across. Hull shells, floors and bulwarks are larger.
        static List<Bounds> Fittings(ModularShipView view, Transform ship)
        {
            var list = new List<Bounds>();
            foreach (var mf in view.GetComponentsInChildren<MeshFilter>(true))
            {
                // Not our own props (a re-install finds the old ones still there).
                if (mf.sharedMesh == null || (mf.transform.parent != null && mf.transform.parent.name == "CargoDisplay")) continue;
                var mb = mf.sharedMesh.bounds;
                var m = ship.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var b = new Bounds(m.MultiplyPoint3x4(mb.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                    b.Encapsulate(m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))));
                if (b.size.x < 2f && b.size.z < 2f) list.Add(b);
            }
            return list;
        }

        static int Order(PlacedModule p) =>
            p.kind == ModuleKind.Bow ? -1000 : p.kind == ModuleKind.Stern ? 1000 : -Mathf.RoundToInt(p.positionM.z * 10f);

        /// Flat deck under the whole footprint near `deck`, clear of every
        /// deck fitting, and of every gun (plus the gunner's margin), wall
        /// and doorway the walk graph avoids.
        static bool Fits(CoasterNavigation nav, List<Bounds> fittings, float x, float z, float deck, out float y)
        {
            if (!nav.FloorY(x, z, deck, 0.3f, out y)) return false;
            for (int i = 0; i < 4; i++)
            {
                float cx = x + ((i & 1) == 0 ? -Half.x : Half.x), cz = z + ((i & 2) == 0 ? -Half.z : Half.z);
                if (!nav.FloorY(cx, cz, y, 0.08f, out _)) return false;
            }
            var mine = Reserve(new Vector3(x, y, z));
            var body = new Bounds(new Vector3(x, y + 0.55f, z), new Vector3(Half.x * 2f, 0.9f, Half.z * 2f));
            foreach (var f in fittings) if (f.Intersects(body)) return false;
            foreach (var ob in nav.Obstacles)
            {
                var b = ob; b.Expand(new Vector3(GunMargin * 2f, 0f, GunMargin * 2f));
                if (b.Intersects(mine)) return false;
            }
            return true;
        }

        /// The ship-local box a socket keeps the walk graph out of.
        public static Bounds Reserve(Vector3 socket) =>
            new Bounds(socket + Vector3.up * 0.55f, new Vector3(Half.x * 2f, 1.1f, Half.z * 2f));

        /// Attach to `view` (or reset one already there) with these sockets.
        public static ShipCargoDisplay Install(ModularShipView view, Transform ship,
            List<Vector3> shipLocal, List<int> sides, List<string> sections)
        {
            var d = view.GetComponent<ShipCargoDisplay>();
            if (d == null) d = view.gameObject.AddComponent<ShipCargoDisplay>();
            d.Setup(ship, shipLocal, sides, sections);
            return d;
        }

        void Setup(Transform ship, List<Vector3> shipLocal, List<int> sides, List<string> sections)
        {
            if (root != null) Destroy(root.gameObject);
            root = new GameObject("CargoDisplay").transform;
            root.SetParent(transform, false);
            sockets.Clear();
            for (int i = 0; i < shipLocal.Count; i++)
                sockets.Add(new Socket
                {
                    pos = transform.InverseTransformPoint(ship.TransformPoint(shipLocal[i])),
                    side = sides[i],
                    section = sections[i],
                });
            baseProps = new GameObject[sockets.Count];
            topProps = new GameObject[sockets.Count];
            baseNames = new string[sockets.Count];
            topNames = new string[sockets.Count];
            voyage = FindAnyObjectByType<VoyageManager>();
            lastKey = null;
            nextPoll = 0f;
            Shown = 0;
        }

        readonly int[] units = new int[3];
        readonly int[] props = new int[3];
        readonly ShipCargoKit.Kind[] want = new ShipCargoKit.Kind[64];

        void Update()
        {
            if (Time.unscaledTime < nextPoll || sockets.Count == 0) return;
            nextPoll = Time.unscaledTime + PollSeconds;
            if (voyage == null) voyage = FindAnyObjectByType<VoyageManager>();
            if (voyage == null) return;
            Refresh();
        }

        /// Recount the hold and redraw what changed. Public for checks.
        public void Refresh()
        {
            int n = Mathf.Min(sockets.Count, want.Length);
            units[0] = units[1] = units[2] = 0;
            int total = 0;
            foreach (var kv in voyage.HeldStores)
            {
                if (kv.Value <= 0) continue;
                units[(int)ShipCargoKit.KindOf(kv.Key)] += kv.Value;
                total += kv.Value;
            }
            int line = Mathf.Max(1, voyage.HoldCapacity);
            int filled = total <= 0 ? 0 : Mathf.Clamp(Mathf.CeilToInt(Mathf.Min(total, line) / (float)line * n), 1, n);
            int over = Mathf.Max(0, total - line), room = Mathf.Max(1, voyage.MaxHold - line);
            int stacked = over <= 0 ? 0 : Mathf.Clamp(Mathf.CeilToInt(over / (float)room * filled), 1, filled);

            string key = filled + "/" + stacked + "/" + units[0] + "/" + units[1] + "/" + units[2];
            if (key == lastKey) return;
            lastKey = key;

            Split(filled, total);
            int k = 0;
            for (int c = 0; c < 3; c++) for (int j = 0; j < props[c]; j++) want[k++] = (ShipCargoKit.Kind)c;

            int shown = 0;
            for (int i = 0; i < sockets.Count; i++)
            {
                string b = null, t = null;
                if (i < filled)
                {
                    // Large and small alternate by pair, so port and starboard match.
                    bool big = (i / 2) % 2 == 0;
                    b = ShipCargoKit.ModelName(want[i], big);
                    if (i < stacked) t = ShipCargoKit.ModelName(want[i], false);
                }
                shown += Place(ref baseProps[i], ref baseNames[i], b, i, 0f, false);
                float lift = 0f;
                if (t != null)
                {
                    var under = ShipCargoKit.Get(b);
                    lift = under != null ? under.height : 0f;
                    if (want[i] == ShipCargoKit.Kind.Sack) lift -= 0.08f;   // a sack slumps into what it sits on
                }
                shown += Place(ref topProps[i], ref topNames[i], t, i, lift, true);
            }
            Shown = shown;
        }

        /// `filled` props shared out by units per kind: largest remainder,
        /// every kind aboard at least one while there are props to give.
        void Split(int filled, int total)
        {
            props[0] = props[1] = props[2] = 0;
            if (filled <= 0 || total <= 0) return;
            int given = 0;
            for (int c = 0; c < 3; c++)
                if (units[c] > 0 && given < filled) { props[c] = 1; given++; }
            while (given < filled)
            {
                int best = 0; float gap = float.MinValue;
                for (int c = 0; c < 3; c++)
                {
                    if (units[c] <= 0) continue;
                    float g = units[c] / (float)total * filled - props[c];
                    if (g > gap) { gap = g; best = c; }
                }
                props[best]++; given++;
            }
        }

        int Place(ref GameObject go, ref string current, string name, int i, float lift, bool top)
        {
            if (name == current) return go != null ? 1 : 0;
            if (go != null) Destroy(go);
            go = null;
            current = name;
            if (name == null) return 0;
            var m = ShipCargoKit.Get(name);
            if (m == null) return 0;
            var s = sockets[i];
            go = new GameObject((top ? "Deck_" : "Hold_") + name);
            var t = go.transform;
            t.SetParent(root, false);
            t.localPosition = s.pos + Vector3.up * lift;
            // Crates and sacks lie fore and aft along the bulwark, face inboard;
            // barrels and sacks get a few degrees so a row is not a grid.
            bool round = name.Contains("Barrel") || name.Contains("Sack");
            float yaw = (s.side > 0 ? -90f : s.side < 0 ? 90f : 0f) + (round ? ((i * 37 + (top ? 19 : 0)) % 17 - 8) : 0f);
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = m.mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m.mat;
            return 1;
        }
    }
}
