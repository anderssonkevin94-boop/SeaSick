using System.Collections.Generic;
using UnityEngine;
using SeaSick.World;

namespace SeaSick.Terrain
{
    /// Harvest index for both standalone storybook tree instances and legacy
    /// welded scenery. Each standalone tree owns its prefab instance; felling
    /// disables only that instance. Its meshes remain shared and untouched.
    ///
    /// Legacy batching history:
    ///
    /// Kevin: *"most of the trees are cosmetic and dont even get cut down."*
    /// Measured, he was being generous: 1346 scenery trees against 26
    /// harvestable props on one island — **1.9 %**.
    ///
    /// The scenery is several hundred trees welded into one mesh per cell
    /// precisely so it costs one draw call, and that is worth keeping: six
    /// hundred GameObjects each carrying a trunk and two canopies is eighteen
    /// hundred renderers per island. So the mesh stays, and this indexes it —
    /// every tree's base position and the exact range of vertices it owns in
    /// BOTH level-of-detail meshes, recorded as it is built. Felling one
    /// collapses its vertices onto its own base, which turns every triangle
    /// it owns into a degenerate and leaves the index buffers untouched.
    ///
    /// Nodes are materialised ON DEMAND around a landing party rather than up
    /// front: at 1300 trees an island and fifteen islands, a GameObject per
    /// tree would be twenty thousand of them for a crew of five who can reach
    /// a few dozen.
    ///
    /// **The economy is untouched.** How much timber an island yields is
    /// `Island.Remaining`, set from the prop count and decremented by the
    /// crew; this only changes which trees they walk to and whether the one
    /// they cut actually falls over. Raising the yield to match the visible
    /// wood would be changing the economy to fix a visual problem, which is
    /// the mistake `IslandScenery` was written to avoid in the first place.
    public class SceneryWood : MonoBehaviour
    {
        public struct Tree
        {
            public Vector3 baseAt;
            public int cell;
            public int vertStart, vertCount;       // in the cell's LOD0 mesh
            public int lod1Start, lod1Count;       // in the cell's LOD1 mesh (0 if none)
            public bool felled;
            public GameObject instance;

            /// Where this tree's vertices stood before it came down, so it can
            /// be stood back up. Kevin, 2026-09-22: the wood regrows away from
            /// camp, and `Fell` writes the base position over the only copy of
            /// the shape there was -- a few dozen vectors per FELLED tree is
            /// the whole cost of being able to undo that, and a tree that was
            /// never cut pays nothing.
            public Vector3[] standing0, standing1;

            /// Did the OUTPOST'S BOOKS take this one down? Only those can come
            /// back. A shore party chopping a `ResourceNode` fells whatever
            /// trunk it walked to (`NodeHarvested`), which no ledger counted
            /// and no ledger can reproduce -- standing one of those up because
            /// the camp's ring happened to reach past it would be inventing a
            /// tree out of a number that never described it.
            public bool ledgerFelled;
        }

        /// One welded mesh pair and the renderers that draw it. `v0`/`v1`
        /// are cached so felling does not re-fetch tens of thousands of
        /// vectors from the mesh every time.
        public class Cell
        {
            public Mesh lod0, lod1;
            public Vector3[] v0, v1;
            public MeshRenderer r0, r1;
            public Vector3 centre;
            public float radius;
        }

        Tree[] trees;
        List<Cell> cells;
        Island island;
        readonly Dictionary<int, ResourceNode> live = new Dictionary<int, ResourceNode>();

        public int TreeCount => trees != null ? trees.Length : 0;
        public IReadOnlyList<Cell> Cells => cells;
        public Tree TreeAt(int i) => trees[i];

        public void Configure(List<Cell> cells, List<Tree> index, Island isle)
        {
            this.cells = cells;
            trees = index.ToArray();
            island = isle;
            foreach (var c in cells)
            {
                c.v0 = c.lod0 != null ? c.lod0.vertices : null;
                c.v1 = c.lod1 != null ? c.lod1.vertices : null;
            }
        }

        /// Drop the tree: every vertex it owns onto its own base, in both
        /// meshes, so each of its triangles collapses to a point and draws
        /// nothing at either distance.
        public void Fell(int i)
        {
            if (trees == null || i < 0 || i >= trees.Length || trees[i].felled) return;
            var t = trees[i];
            var c = cells[t.cell];
            if (t.instance != null)
            {
                t.instance.SetActive(false);
                trees[i].felled = true;
                return;
            }
            if (c.v0 != null)
            {
                if (trees[i].standing0 == null) trees[i].standing0 = Snapshot(c.v0, t.vertStart, t.vertCount);
                for (int v = t.vertStart; v < t.vertStart + t.vertCount && v < c.v0.Length; v++)
                    c.v0[v] = t.baseAt;
                c.lod0.SetVertices(c.v0);
            }
            if (c.v1 != null && t.lod1Count > 0)
            {
                if (trees[i].standing1 == null) trees[i].standing1 = Snapshot(c.v1, t.lod1Start, t.lod1Count);
                for (int v = t.lod1Start; v < t.lod1Start + t.lod1Count && v < c.v1.Length; v++)
                    c.v1[v] = t.baseAt;
                c.lod1.SetVertices(c.v1);
            }
            trees[i].felled = true;
        }

        /// **Fell it for the outpost's books**, which is the only kind of
        /// felling that can ever be undone. See `Tree.ledgerFelled`.
        public void FellForLedger(int i)
        {
            if (trees == null || i < 0 || i >= trees.Length) return;
            bool wasDown = trees[i].felled;
            Fell(i);
            // A tree the shore party already took stays theirs: the camp did
            // not cut it and must not be able to grow it back.
            if (!wasDown && trees[i].felled) trees[i].ledgerFelled = true;
        }

        /// **Stand a felled tree back up**, exactly where it was.
        ///
        /// Kevin, 2026-09-22: *"they should re-grow further away from camp, to
        /// help the camp not get overgrown."* WHICH trees come back, and in
        /// what order, is the outpost's business (`Outpost.DrawWood`) -- this
        /// is only the undo of `Fell`, and it refuses when there is no
        /// snapshot to put back rather than leaving a tree half-restored.
        public void Restand(int i)
        {
            if (trees == null || i < 0 || i >= trees.Length) return;
            if (!trees[i].felled || !trees[i].ledgerFelled) return;
            var t = trees[i];
            if (t.instance != null)
            {
                // The node that harvested it is gone; a fresh one is made by
                // `Populate` when a party comes near. Leaving the old one on
                // the object would stand a spent node up with the tree.
                var stale = t.instance.GetComponent<ResourceNode>();
                if (stale != null && !live.ContainsKey(i)) Destroy(stale);
                t.instance.SetActive(true);
                trees[i].felled = false;
                trees[i].ledgerFelled = false;
                return;
            }
            var c = cells[t.cell];
            if (t.vertCount > 0 && (c.v0 == null || t.standing0 == null)) return;
            if (c.v0 != null && t.standing0 != null)
            {
                for (int k = 0; k < t.standing0.Length && t.vertStart + k < c.v0.Length; k++)
                    c.v0[t.vertStart + k] = t.standing0[k];
                c.lod0.SetVertices(c.v0);
            }
            if (c.v1 != null && t.standing1 != null)
            {
                for (int k = 0; k < t.standing1.Length && t.lod1Start + k < c.v1.Length; k++)
                    c.v1[t.lod1Start + k] = t.standing1[k];
                c.lod1.SetVertices(c.v1);
            }
            trees[i].felled = false;
            trees[i].ledgerFelled = false;
        }

        static Vector3[] Snapshot(Vector3[] src, int start, int count)
        {
            if (src == null || count <= 0 || start < 0 || start >= src.Length) return null;
            int n = Mathf.Min(count, src.Length - start);
            var snap = new Vector3[n];
            System.Array.Copy(src, start, snap, 0, n);
            return snap;
        }

        /// Trees whose LOD0 vertex run has collapsed to one point, counted
        /// off the MESH rather than off the bookkeeping that felled them --
        /// the probes want the shipped artefact, not the flag.
        public int FelledInMesh()
        {
            if (trees == null) return 0;
            int n = 0;
            for (int i = 0; i < trees.Length; i++)
            {
                var t = trees[i];
                if (t.instance != null) { if (!t.instance.activeSelf) n++; continue; }
                if (t.felled && t.vertCount == 0) { n++; continue; }
                var c = cells[t.cell];
                var v = c.lod0 != null ? c.lod0.vertices : null;
                if (v == null || t.vertCount < 2) continue;
                bool collapsed = true;
                for (int k = t.vertStart + 1; k < t.vertStart + t.vertCount && k < v.Length; k++)
                    if ((v[k] - v[t.vertStart]).sqrMagnitude > 1e-6f) { collapsed = false; break; }
                if (collapsed) n++;
            }
            return n;
        }

        /// Stand up harvest nodes on the real trees near a landing party.
        ///
        /// Returns how many are now available. Capped: the crew cannot work
        /// more than a few at a time and every one is a GameObject.
        public int Populate(Vector3 near, float radius, int max = 48)
        {
            if (trees == null || island == null) return 0;
            float r2 = radius * radius;

            // Nearest first, so a party that lands on a wooded shore gets the
            // trees in front of it rather than whichever the array happened
            // to hold first.
            var candidates = new List<(float d2, int i)>();
            for (int i = 0; i < trees.Length; i++)
            {
                if (trees[i].felled || live.ContainsKey(i)) continue;
                float d2 = (trees[i].baseAt - near).sqrMagnitude;
                if (d2 <= r2) candidates.Add((d2, i));
            }
            candidates.Sort((a, b) => a.d2.CompareTo(b.d2));

            int made = 0;
            foreach (var (_, i) in candidates)
            {
                if (live.Count >= max) break;
                var go = trees[i].instance != null ? trees[i].instance : new GameObject("Tree_" + i);
                go.transform.SetParent(transform, true);
                go.transform.position = trees[i].baseAt;
                var node = go.AddComponent<ResourceNode>();
                node.ConfigureScenery(island, this, i);
                live[i] = node;
                made++;
            }
            return made;
        }

        /// Fell everything standing inside a circle, and say how many came
        /// down.
        ///
        /// This is how a camp gets its clearing. At home the village's ground
        /// is handed to the scenery bake as a keep-out BEFORE the trees go in,
        /// because the wood is several hundred trees welded into one mesh --
        /// but on any other island the trees are already standing when the
        /// player decides to build, so there is no keep-out to hand anyone.
        /// The wood comes down at runtime instead, through the same path the
        /// crew fell it by. Which is the better story anyway: **making camp
        /// fells the wood it stands on, and you keep the logs.**
        ///
        /// `skip` (2026-09-23): trees the caller does not want touched --
        /// `Outpost` passes the ones standing on another building's plot,
        /// which only that plot's clearing may take down.
        public int FellWithin(Vector3 at, float radius, System.Func<int, bool> skip = null)
        {
            if (trees == null) return 0;
            float r2 = radius * radius;
            int n = 0;
            for (int i = 0; i < trees.Length; i++)
            {
                if (trees[i].felled) continue;
                if (skip != null && skip(i)) continue;
                Vector3 d = trees[i].baseAt - at;
                d.y = 0f;                       // a disc on the map, not a sphere
                if (d.sqrMagnitude > r2) continue;
                // Through the live node if there is one, so a tree a crewman
                // has claimed does not leave a node standing on a stump.
                if (live.TryGetValue(i, out var node) && node != null) live.Remove(i);
                // The clearing is booked against `ledger.treesFelled` by the
                // caller, so it is the books' wood like any other.
                FellForLedger(i);
                n++;
            }
            return n;
        }

        /// Called by the node when the crew finish it.
        public void NodeHarvested(int i)
        {
            Fell(i);
            live.Remove(i);
        }
    }
}
