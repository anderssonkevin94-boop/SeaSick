using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **The palisade kit on a wall segment (2026-09-23).**
    ///
    /// The kit is nine fixed pieces: 1 m runs A/B/C,
    /// 0.5 m and 0.25 m fillers, a square post, a 1 m breach, the gate and
    /// the broken gate. A segment is any length from 0.5 to 12 m at any
    /// angle, so this is the adapter between the two, and its whole policy
    /// is **tile, never stretch, never clip**:
    ///
    /// Since 2026-09-28 the seven wall pieces are the approved level 1 wall
    /// (`art-staging/wall-textured-v3`, `Art/WallL1`, put behind the same
    /// wrappers by `Dev/Editor/WallL1Import`: 0.66 m rope-bound posts,
    /// textured toon materials); the gate and the broken gate are still
    /// Astra's (`art-staging/palisade-astra-lvl1-v2`).
    ///
    /// - a run is filled with whole quarter-metres (1 m runs, then one
    ///   0.5, then one 0.25), the tiled span CENTRED on the segment, so
    ///   what does not fit (under 0.25 m) is split into the two ends and
    ///   hidden inside the posts' shafts (0.66 m);
    /// - one post per NODE, not per segment end (`WallChain`), turned to
    ///   the bisector of the runs that meet there;
    /// - an acute bend (under 45°) gives up a quarter-metre at that end so
    ///   the two runs poke into the post rather than through each other;
    /// - rails (the kit's "rear") always face the camp.
    ///
    /// **Resources/Palisade/*.prefab are wrappers**, not the raw FBX: the
    /// FBX comes in with the wall along its -X, rails on -Z and the root at
    /// the start end; each wrapper turns that to **+Z along the wall, +Y up,
    /// rails on -X**, root at the start (the post: its CENTRE at the root).
    /// Everything below is written in that frame and never looks at the
    /// FBX's axes again.
    ///
    /// **Batched, 2026-09-30.** A kit piece is one renderer with three or
    /// four materials, and a 22-segment ring stood as ~220 of them: ~730
    /// draw calls at camp view on the phone, about half the frame. A
    /// segment's pieces are now never instantiated: each state (Whole /
    /// Broken) is merged into ONE mesh with one submesh per material
    /// (`Batch`), and the chain's posts into one more (`BakePosts`), so a
    /// segment is at most four draws whatever its length. The mesh is made
    /// when the drawing is -- a segment raised, a neighbour changing its
    /// fit, a gate made -- and never on a breach or a mend, which stay a
    /// `SetActive` between the two pre-merged states. The gate module
    /// (`GateLeaves` animates it) and blueprint ghosts (tinted piece by
    /// piece) still instantiate their pieces. Merging, not GPU instancing:
    /// the toon shader is SRP-Batcher compatible, and the SRP Batcher only
    /// makes each draw cheaper (and wins over instancing for it), while
    /// the kit's 3 run variants, 2 fillers and 4 materials would split
    /// instancing into a dozen small groups. Colliders are untouched --
    /// the pieces never had any (`BuildingFactory.RaiseWall`'s one box).
    public static class WallVisual
    {
        /// The kit's stake pitch, and so the smallest piece.
        public const float Pitch = 0.25f;

        /// The gate module's length, built-in posts included.
        public const float GateSpan = 3f;

        /// Below this much room between a node and the end of the gate
        /// module, the gate's own post (0.2 m inside the module's end) is
        /// what stands on the node, and a standalone post would be a
        /// second post in the same place.
        const float GateCoversNode = 0.2f;

        /// An incident run closer than this (degrees, between the two runs
        /// leaving the node) makes a bend acute enough to trim.
        public const float AcuteTrim = 45f;

        /// **Metres a run gives up at a wall tower's node** (2026-09-27):
        /// Astra's tower is 2.28 m across its legs, so a run stopping 1 m
        /// short of the node ends just inside them rather than running its
        /// stakes through the tower's middle. Whole quarters, so the tiling
        /// is unchanged. A guess until Kevin sees it on the phone.
        public const float TowerTrim = 1.0f;

        /// Pieces are sunk this far below the lowest ground they span, the
        /// post a little further: the kit's stakes reach 0.08 m under their
        /// root, which is a shallow embed on a slope.
        const float Sink = 0.03f;
        const float PostSink = 0.05f;

        const float Eps = 0.001f;

        /// What a segment's drawing depends on besides its own two posts:
        /// the neighbours (the trims), the camp (the flip) and, for a
        /// drawing with no `WallChain` (a blueprint ghost), whether it
        /// carries its own posts.
        public struct Fit
        {
            public bool gate;
            /// Rails face local +X (the camp is on the right of a→b)
            /// rather than the kit's own -X.
            public bool flip;
            /// Give up a quarter-metre at this end (an acute bend).
            public bool trimA, trimB;
            /// Stand a post at each uncovered end, under the segment root.
            public bool ownPosts;
            /// A wall tower stands on this end's node (2026-09-27): the run
            /// stops `TowerTrim` short of it, at the tower's legs.
            public bool towerA, towerB;
            /// The segment's level (2026-10-03): 2 draws the stone-based
            /// level 2 kit. 0 and 1 are level 1 -- and add nothing to `Key`,
            /// so a level 1 drawing is keyed exactly as before.
            public int level;

            public int Key => (gate ? 1 : 0) | (flip ? 2 : 0) | (trimA ? 4 : 0)
                | (trimB ? 8 : 0) | (ownPosts ? 16 : 0) | (towerA ? 32 : 0) | (towerB ? 64 : 0)
                | (level >= 2 ? 128 : 0);
        }

        // --- the kit ---------------------------------------------------------

        static GameObject[] runs;
        static GameObject half, quarter, breach, post, gate, gateBroken;
        static bool loaded, ready;

        /// **One level's pieces (2026-10-03).** Level 1 is the fields above,
        /// loaded as always; level 2 (`Dev/Editor/WallL2Import`: stone base,
        /// squared oak, stone pillars, `Wall2_*` / `Gate_L2`) is loaded the
        /// first time a level 2 segment is drawn. Same frame, same snap
        /// contract, so every placement rule below serves both.
        sealed class KitSet
        {
            public GameObject[] runs;
            public GameObject half, quarter, breach, post, gate, gateBroken;
        }

        static KitSet kit1, kit2;
        static bool loaded2;

        /// The pieces for `level`. Level 2 falls back to level 1 (with one
        /// warning) when its wrappers have not been imported yet.
        static KitSet KitFor(int level)
        {
            if (!loaded) Load();
            if (level < 2) return kit1;
            if (!loaded2)
            {
                loaded2 = true;
                var k = new KitSet
                {
                    runs = new[] { Kit("Wall2_Run_1m_A"), Kit("Wall2_Run_1m_B"), Kit("Wall2_Run_1m_C") },
                    half = Kit("Wall2_Filler_050m"),
                    quarter = Kit("Wall2_Filler_025m"),
                    breach = Kit("Wall2_Breached_1m"),
                    post = Kit("Wall2_Post"),
                    gate = Kit("Gate_L2"),
                    gateBroken = Kit("Gate_Breached_L2"),
                };
                bool ok = k.runs[0] != null && k.runs[1] != null && k.runs[2] != null && k.half != null
                    && k.quarter != null && k.breach != null && k.post != null && k.gate != null
                    && k.gateBroken != null;
                if (ok) kit2 = k;
                else Debug.LogWarning("[Camp] level 2 wall kit missing under Resources/Palisade "
                    + "(run Dev/Editor/WallL2Import) -- level 2 walls are drawn with level 1's pieces.");
            }
            return kit2 ?? kit1;
        }

        /// The post piece's name at `level` -- what a chain's post marker is
        /// called, so a node whose level changed knows its marker is stale.
        public static string PostName(int level) => KitFor(level).post.name;

        /// False when any piece failed to load -- the factory then raises
        /// the old extruded wall, the same way `BuildingFactory.Dress`
        /// falls back for a building.
        public static bool KitReady
        {
            get
            {
                if (!loaded) Load();
                return ready;
            }
        }

        static void Load()
        {
            loaded = true;
            runs = new[]
            {
                Kit("Palisade_Run_1m_A"), Kit("Palisade_Run_1m_B"), Kit("Palisade_Run_1m_C"),
            };
            half = Kit("Palisade_Filler_050m");
            quarter = Kit("Palisade_Filler_025m");
            breach = Kit("Palisade_Breached_1m");
            post = Kit("Palisade_Post");
            gate = Kit("Gate_L1");
            gateBroken = Kit("Gate_Breached_3m");
            kit1 = new KitSet
            {
                runs = runs, half = half, quarter = quarter, breach = breach,
                post = post, gate = gate, gateBroken = gateBroken,
            };
            ready = runs[0] != null && runs[1] != null && runs[2] != null && half != null
                && quarter != null && breach != null && post != null && gate != null
                && gateBroken != null;
            if (!ready)
                Debug.LogWarning("[Camp] palisade kit missing under Resources/Palisade -- "
                    + "walls are raised out of primitives instead.");
        }

        static GameObject Kit(string name) => Resources.Load<GameObject>("Palisade/" + name);

        // --- the batch (2026-09-30) -----------------------------------------

        /// One kit piece's drawing, in its wrapper's frame: every submesh
        /// with its material, and the renderer settings the merged one
        /// copies. Read once per prefab.
        sealed class PieceMesh
        {
            public readonly List<(Mesh mesh, int sub, Material mat, Matrix4x4 m)> parts
                = new List<(Mesh, int, Material, Matrix4x4)>();
            public UnityEngine.Rendering.ShadowCastingMode shadows = UnityEngine.Rendering.ShadowCastingMode.On;
            public bool receive = true;
            public int layer;
            /// False when a mesh cannot be read on the CPU (a player build
            /// of an FBX imported without Read/Write): that piece is
            /// instantiated as before rather than merged into nothing.
            public bool mergeable = true;
        }

        /// Merge pieces at all. On in the game; `Dev/WallDrawCost` turns it
        /// off and redraws (`WallChain.RedrawAll`) for a same-frame A/B.
        internal static bool Merge = true;

        static readonly Dictionary<GameObject, PieceMesh> pieceMeshes = new Dictionary<GameObject, PieceMesh>();

        static PieceMesh MeshOf(GameObject prefab)
        {
            if (pieceMeshes.TryGetValue(prefab, out var pm)) return pm;
            pm = new PieceMesh();
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            bool first = true;
            foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                var f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null) continue;
                var mesh = f.sharedMesh;
                if (!mesh.isReadable) pm.mergeable = false;
                var mats = r.sharedMaterials;
                var m = toRoot * f.transform.localToWorldMatrix;
                for (int i = 0; i < mesh.subMeshCount && i < mats.Length; i++)
                    if (mats[i] != null) pm.parts.Add((mesh, i, mats[i], m));
                if (first)
                {
                    pm.shadows = r.shadowCastingMode;
                    pm.receive = r.receiveShadows;
                    pm.layer = r.gameObject.layer;
                    first = false;
                }
            }
            if (pm.parts.Count == 0) pm.mergeable = false;
            pieceMeshes[prefab] = pm;
            return pm;
        }

        /// **Pieces gathered for one holder, merged into one renderer with
        /// a submesh per material.** Everything is in the holder's frame.
        sealed class Batch
        {
            readonly Transform holder;
            readonly List<Material> order = new List<Material>();
            readonly Dictionary<Material, List<CombineInstance>> byMat
                = new Dictionary<Material, List<CombineInstance>>();
            PieceMesh look;

            public Batch(Transform holder) { this.holder = holder; }

            /// False when the piece cannot be merged; the caller then
            /// instantiates it.
            public bool Add(GameObject prefab, Matrix4x4 pose)
            {
                var pm = MeshOf(prefab);
                if (!pm.mergeable) return false;
                if (look == null) look = pm;
                foreach (var p in pm.parts)
                {
                    if (!byMat.TryGetValue(p.mat, out var list))
                    {
                        list = new List<CombineInstance>();
                        byMat[p.mat] = list;
                        order.Add(p.mat);
                    }
                    list.Add(new CombineInstance { mesh = p.mesh, subMeshIndex = p.sub, transform = pose * p.m });
                }
                return true;
            }

            /// Merge what was gathered into a "Batched" child of the holder.
            public void Finish(string name)
            {
                if (order.Count == 0) return;
                var perMat = new CombineInstance[order.Count];
                var temp = new Mesh[order.Count];
                int verts = 0;
                for (int i = 0; i < order.Count; i++)
                {
                    var list = byMat[order[i]];
                    foreach (var ci in list) verts += ci.mesh.vertexCount;   // an upper bound
                    temp[i] = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    temp[i].CombineMeshes(list.ToArray(), true, true, false);
                    perMat[i] = new CombineInstance { mesh = temp[i], transform = Matrix4x4.identity };
                }
                var mesh = new Mesh
                {
                    name = name,
                    indexFormat = verts > 65000
                        ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16,
                };
                mesh.CombineMeshes(perMat, false, false, false);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);   // drawn only from here on; no CPU copy kept
                foreach (var t in temp) Kill(t);

                var go = new GameObject("Batched");
                go.layer = look.layer;
                go.transform.SetParent(holder, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = order.ToArray();
                r.shadowCastingMode = look.shadows;
                r.receiveShadows = look.receive;
                go.AddComponent<WallBatchMesh>().mesh = mesh;
            }
        }

        /// **A post that is only a place (2026-09-30)**: the chain keeps
        /// one per node as before (`HasPostAt`, the turning), but the drawing
        /// is `BakePosts`'s one merged mesh. Falls back to a real post when
        /// the kit cannot be merged.
        public static GameObject PostMarker(Transform parent, Vector3 node, float yaw, int level = 1)
        {
            var post = KitFor(level).post;
            if (!Merge || !MeshOf(post).mergeable) return Post(parent, node, yaw, level);
            var go = new GameObject(post.name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(node + Vector3.down * PostSink,
                Quaternion.Euler(0f, yaw, 0f));
            return go;
        }

        /// **Every post of a chain as one mesh** under `postRoot`, replacing
        /// the last one. `WallChain.Refresh` calls it when a post came,
        /// went or turned.
        public static void BakePosts(Transform postRoot, IEnumerable<Transform> markers)
        {
            if (!loaded) Load();
            var old = postRoot.Find("Batched");
            if (old != null) { old.SetParent(null, false); Kill(old.gameObject); }
            if (!Merge || !MeshOf(post).mergeable) return;
            var batch = new Batch(postRoot);
            Matrix4x4 toRoot = postRoot.worldToLocalMatrix;
            // A marker is named after its post piece (`PostMarker`): level 2
            // nodes carry the stone pillar.
            var post2 = KitFor(2).post;
            foreach (var t in markers)
                if (t != null && t.GetComponent<Renderer>() == null && t.childCount == 0)
                    batch.Add(t.name == post2.name ? post2 : post, toRoot * t.localToWorldMatrix);
            batch.Finish("WallPosts batch");
        }

        // --- the fit ---------------------------------------------------------

        /// Which way the rails face: toward `centre`. The segment root is
        /// turned along a→b, so its +X is the right-hand side.
        public static bool Flip(Vector3 a, Vector3 b, Vector3 centre)
        {
            Vector3 run = Flat(b - a);
            if (run.sqrMagnitude < 1e-6f) return false;
            Vector3 right = Vector3.Cross(Vector3.up, run.normalized);
            return Vector3.Dot(Flat(centre - 0.5f * (a + b)), right) > 0f;
        }

        /// The fit of a drawing that has no chain to ask: a blueprint ghost.
        /// It carries its own posts and faces the camp it stands in, if any.
        public static Fit Loose(Vector3 a, Vector3 b, bool isGate, Outpost camp, int level = 1)
            => new Fit
            {
                gate = isGate,
                flip = camp != null && Flip(a, b, camp.CampCentre),
                ownPosts = true,
                level = level,
            };

        /// Does a gate of this length put its own post on its nodes?
        public static bool GateOnNode(float length) => (length - GateSpan) * 0.5f < GateCoversNode;

        // --- the drawing -----------------------------------------------------

        /// **Fill a segment's two states.** `root` is the segment's own
        /// object: at the midpoint, turned along a→b (`BuildingFactory.
        /// RaiseWall`). Both states are built now, so a breach and a mend
        /// stay a `SetActive` -- unless `withBroken` is false (a blueprint
        /// ghost, redrawn as the thumb drags, which is never breached), and
        /// then `broken` is null.
        public static void Build(Transform root, Vector3 a, Vector3 b, Fit fit,
            System.Func<Vector3, float> ground, out Transform whole, out Transform broken,
            bool withBroken = true)
        {
            if (!loaded) Load();
            whole = Holder(root, "Whole");
            broken = withBroken ? Holder(root, "Broken") : null;
            var k = KitFor(fit.level);
            var s = new Span(a, b, fit.flip, ground, k);
            // A standing segment is merged; a ghost (no broken state) is
            // not -- it is tinted piece by piece and redrawn as the thumb
            // drags.
            if (withBroken && Merge)
            {
                s.batches[whole] = new Batch(whole);
                s.batches[broken] = new Batch(broken);
            }
            float L = s.L;

            if (fit.gate)
            {
                // The gate centred; on a segment shorter than 3 m it
                // overhangs both nodes (a 2 m lattice step gives 2 and 2.83),
                // which is accepted -- its own posts then stand past the
                // nodes. Longer, the gap each side is run, starting AT the
                // node and pushed into the gate's own post by what does not
                // make a whole quarter: that post is 0.38 m deep and hides
                // it, where a gap beside it would be a hole.
                float side = (L - GateSpan) * 0.5f;
                s.Spawn(whole, k.gate, side, GateSpan).AddComponent<GateLeaves>().Fit();
                if (broken != null) s.Spawn(broken, k.gateBroken, side, GateSpan);
                if (side > Eps)
                {
                    foreach (var h in new[] { whole, broken })
                    {
                        if (h == null) continue;
                        int n = Mathf.CeilToInt(side / Pitch - Eps) - (fit.trimA ? 1 : 0);
                        s.Quarters(h, fit.trimA ? Pitch : 0f, n);
                        n = Mathf.CeilToInt(side / Pitch - Eps) - (fit.trimB ? 1 : 0);
                        s.Quarters(h, L - (fit.trimB ? Pitch : 0f) - n * Pitch, n);
                    }
                }
            }
            else
            {
                // A wall tower's end stops at the tower's legs; its own
                // trim replaces the acute-bend quarter there.
                float u0 = fit.towerA ? Mathf.Min(TowerTrim, 0.5f * L) : 0f;
                float u1 = fit.towerB ? Mathf.Max(u0, L - TowerTrim) : L;
                bool tA = fit.trimA && !fit.towerA, tB = fit.trimB && !fit.towerB;
                s.Tile(whole, u0, u1, tA, tB);

                // **Breached = the middle third gone**: the outer thirds
                // keep their stakes, the middle is stumps. There are no
                // breached fillers, so the middle is whole metres of stumps
                // centred in the third and bare ground either side -- it is
                // a hole, and a hole may have gaps in it.
                if (broken != null)
                {
                    float third = (u1 - u0) / 3f;
                    s.Tile(broken, u0, u0 + third, tA, false);
                    s.Metres(broken, k.breach, u0 + third, u0 + 2f * third);
                    s.Tile(broken, u0 + 2f * third, u1, false, tB);
                }
            }

            foreach (var batch in s.batches.Values) batch.Finish("Palisade batch");

            if (fit.ownPosts && !(fit.gate && GateOnNode(L)))
            {
                float yaw = s.Yaw;
                Post(root, a, yaw, fit.level);
                Post(root, b, yaw, fit.level);
            }
        }

        /// A post with its centre on `node`, turned to `yaw` (degrees).
        public static GameObject Post(Transform parent, Vector3 node, float yaw, int level = 1)
        {
            var post = KitFor(level).post;
            var go = Object.Instantiate(post, parent, false);
            go.name = post.name;
            go.transform.SetPositionAndRotation(node + Vector3.down * PostSink,
                Quaternion.Euler(0f, yaw, 0f));
            return go;
        }

        /// **The bisector, for a square post.** A square looks the same
        /// every 90°, so the angles are averaged in that symmetry (4θ):
        /// a straight run and a right-angled corner both meet a face
        /// square-on, and any other bend splits the difference so each run
        /// is equally far off a face. Where that is undecided (runs exactly
        /// 45° apart) the plain line bisector (2θ) decides. `outgoing` are
        /// the flat directions of the runs leaving the node.
        public static float NodeYaw(List<Vector3> outgoing)
        {
            if (outgoing == null || outgoing.Count == 0) return 0f;
            float c4 = 0f, s4 = 0f, c2 = 0f, s2 = 0f;
            for (int i = 0; i < outgoing.Count; i++)
            {
                float t = Mathf.Atan2(outgoing[i].x, outgoing[i].z);
                c4 += Mathf.Cos(4f * t); s4 += Mathf.Sin(4f * t);
                c2 += Mathf.Cos(2f * t); s2 += Mathf.Sin(2f * t);
            }
            if (c4 * c4 + s4 * s4 > 1e-4f) return Mathf.Atan2(s4, c4) * 0.25f * Mathf.Rad2Deg;
            if (c2 * c2 + s2 * s2 > 1e-4f) return Mathf.Atan2(s2, c2) * 0.5f * Mathf.Rad2Deg;
            return Mathf.Atan2(outgoing[0].x, outgoing[0].z) * Mathf.Rad2Deg;
        }

        static Transform Holder(Transform root, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            return go.transform;
        }

        internal static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// Destroy now in the editor (the preview), at the end of the frame
        /// in play.
        internal static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        /// One segment's line, in its root's frame: `u` is metres from post
        /// A along the run.
        class Span
        {
            readonly Vector3 a, b, dir;
            readonly bool flip;
            readonly System.Func<Vector3, float> ground;
            readonly float midY;
            readonly uint seed;
            readonly KitSet kit;
            public readonly float L;

            /// The holders whose pieces are merged rather than
            /// instantiated (2026-09-30); empty for a ghost.
            public readonly Dictionary<Transform, Batch> batches = new Dictionary<Transform, Batch>();

            public float Yaw => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

            public Span(Vector3 a, Vector3 b, bool flip, System.Func<Vector3, float> ground, KitSet kit)
            {
                this.kit = kit;
                this.a = a;
                this.b = b;
                this.flip = flip;
                this.ground = ground;
                Vector3 run = Flat(b - a);
                L = Mathf.Max(0.01f, run.magnitude);
                dir = run.sqrMagnitude > 1e-6f ? run.normalized : Vector3.forward;
                midY = 0.5f * (a.y + b.y);
                // Stable across rebuilds (the posts are the segment's
                // identity), different from the next segment along.
                unchecked
                {
                    seed = (uint)Mathf.RoundToInt(a.x * 10f) * 73856093u
                        ^ (uint)Mathf.RoundToInt(a.z * 10f) * 19349663u
                        ^ (uint)Mathf.RoundToInt(b.x * 10f) * 83492791u
                        ^ (uint)Mathf.RoundToInt(b.z * 10f) * 2654435761u;
                }
            }

            float GroundAt(float u)
            {
                float y = Mathf.Lerp(a.y, b.y, Mathf.Clamp01(u / L));
                if (ground == null) return y;
                return ground(new Vector3(a.x + dir.x * u, y, a.z + dir.z * u));
            }

            /// One piece over [u, u + len]. Its root is its START end, so a
            /// flipped piece (rails to the right) is turned round and rooted
            /// at the far end of its span instead. Into the holder's batch
            /// when it has one, else a piece of its own.
            public void Place(Transform holder, GameObject prefab, float u, float len)
            {
                Pose(u, len, out var pos, out var rot);
                if (holder != null && batches.TryGetValue(holder, out var batch)
                    && batch.Add(prefab, Matrix4x4.TRS(pos, rot, Vector3.one)))
                    return;
                Spawn(holder, prefab, u, len);
            }

            /// The same, always as its own object (the gate module).
            public GameObject Spawn(Transform holder, GameObject prefab, float u, float len)
            {
                var go = Object.Instantiate(prefab, holder, false);
                go.name = prefab.name;
                Pose(u, len, out var pos, out var rot);
                go.transform.localPosition = pos;
                go.transform.localRotation = rot;
                return go;
            }

            void Pose(float u, float len, out Vector3 pos, out Quaternion rot)
            {
                float y = Mathf.Min(GroundAt(u), Mathf.Min(GroundAt(u + 0.5f * len), GroundAt(u + len)))
                    - midY - Sink;
                float z = u - 0.5f * L;
                pos = new Vector3(0f, y, flip ? z + len : z);
                rot = flip ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            }

            /// Whole quarter-metres over [u0, u1], the tiled span centred in
            /// it; a trimmed end gives up one more quarter at that end.
            public void Tile(Transform holder, float u0, float u1, bool trimStart, bool trimEnd)
            {
                float len = u1 - u0;
                int n = Mathf.FloorToInt(len / Pitch + Eps);
                float start = u0 + 0.5f * (len - n * Pitch);
                if (trimStart) { n--; start += Pitch; }
                if (trimEnd) n--;
                Quarters(holder, start, n);
            }

            /// `n` quarter-metres from `u`: the 1 m runs (A/B/C by the
            /// segment's hash, so they vary along a wall), with the one 0.5
            /// and the one 0.25 in the middle of them rather than always at
            /// the same end of every segment.
            public void Quarters(Transform holder, float u, int n)
            {
                if (n <= 0) return;
                int metres = n / 4, rest = n % 4;
                int before = metres / 2;
                for (int i = 0; i < metres; i++)
                {
                    if (i == before) u = Fillers(holder, u, rest);
                    Place(holder, kit.runs[Variant(u)], u, 1f);
                    u += 1f;
                }
                if (metres == 0) Fillers(holder, u, rest);
            }

            float Fillers(Transform holder, float u, int rest)
            {
                if (rest >= 2) { Place(holder, kit.half, u, 0.5f); u += 0.5f; }
                if ((rest & 1) == 1) { Place(holder, kit.quarter, u, 0.25f); u += 0.25f; }
                return u;
            }

            /// Whole metres of `prefab` centred over [u0, u1] (the breach).
            public void Metres(Transform holder, GameObject prefab, float u0, float u1)
            {
                float len = u1 - u0;
                int n = Mathf.FloorToInt(len + Eps);
                float u = u0 + 0.5f * (len - n);
                for (int i = 0; i < n; i++) Place(holder, prefab, u + i, 1f);
            }

            /// By position along the segment, so a run standing in the same
            /// place in both states (a gate's sides) is the same run.
            int Variant(float u)
            {
                unchecked
                {
                    uint x = seed + (uint)Mathf.RoundToInt(u * 4f) * 0x9E3779B9u;
                    x ^= x >> 15; x *= 0x2C1B3C6Du; x ^= x >> 12; x *= 0x297A2D39u; x ^= x >> 15;
                    return (int)(x % 3u);
                }
            }
        }

        // --- the preview -----------------------------------------------------

        /// **The game's own path, off the game.** Raises a polyline of
        /// segments (`nodes[i]` → `nodes[i+1]`) through `BuildingFactory.
        /// RaiseWall` + `WallSegment.Configure` + `WallChain.Refresh` --
        /// exactly what `Outpost.RaiseWall` does -- under a chain that faces
        /// `campCentre`. For edit-mode checks; nothing in the game calls it.
        public static GameObject Preview(Transform parent, Vector3[] nodes, bool[] breached,
            bool[] gates, Vector3 campCentre)
        {
            var root = new GameObject("WallPreview");
            if (parent != null) root.transform.SetParent(parent, false);
            var chain = root.AddComponent<WallChain>();
            chain.PreviewCentre(campCentre);
            for (int i = 0; i + 1 < nodes.Length; i++)
            {
                bool isGate = gates != null && i < gates.Length && gates[i];
                bool isBroken = breached != null && i < breached.Length && breached[i];
                var go = BuildingFactory.RaiseWall(root.transform, nodes[i], nodes[i + 1], isGate,
                    out var whole, out var broken);
                var seg = go.AddComponent<WallSegment>();
                float hp = WallSegment.HpFor(Flat(nodes[i + 1] - nodes[i]).magnitude, isGate);
                seg.Configure(null, nodes[i], nodes[i + 1], isGate, isBroken ? 0f : hp, hp,
                    whole, broken);
            }
            chain.Refresh();
            return root;
        }
    }
}
