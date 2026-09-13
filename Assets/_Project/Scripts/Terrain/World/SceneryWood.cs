using System.Collections.Generic;
using UnityEngine;
using SeaSick.World;

namespace SeaSick.Terrain
{
    /// **Makes the baked wood cuttable.**
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
            if (c.v0 != null)
            {
                for (int v = t.vertStart; v < t.vertStart + t.vertCount && v < c.v0.Length; v++)
                    c.v0[v] = t.baseAt;
                c.lod0.SetVertices(c.v0);
            }
            if (c.v1 != null && t.lod1Count > 0)
            {
                for (int v = t.lod1Start; v < t.lod1Start + t.lod1Count && v < c.v1.Length; v++)
                    c.v1[v] = t.baseAt;
                c.lod1.SetVertices(c.v1);
            }
            trees[i].felled = true;
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
                var go = new GameObject("Tree_" + i);
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
        public int FellWithin(Vector3 at, float radius)
        {
            if (trees == null) return 0;
            float r2 = radius * radius;
            int n = 0;
            for (int i = 0; i < trees.Length; i++)
            {
                if (trees[i].felled) continue;
                Vector3 d = trees[i].baseAt - at;
                d.y = 0f;                       // a disc on the map, not a sphere
                if (d.sqrMagnitude > r2) continue;
                // Through the live node if there is one, so a tree a crewman
                // has claimed does not leave a node standing on a stump.
                if (live.TryGetValue(i, out var node) && node != null) live.Remove(i);
                Fell(i);
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
