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
    /// The scenery is several hundred trees welded into one mesh precisely so
    /// it costs one draw call, and that is worth keeping: six hundred
    /// GameObjects each carrying a trunk and two canopies is eighteen hundred
    /// renderers per island. So the mesh stays, and this indexes it — every
    /// tree's base position and the exact range of vertices it owns, recorded
    /// as it is built. Felling one collapses its vertices onto its own base,
    /// which turns every triangle it owns into a degenerate and leaves the
    /// index buffer untouched.
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
            public int vertStart, vertCount;
            public bool felled;
        }

        Tree[] trees;
        Mesh mesh;
        Vector3[] verts;          // cached, so felling does not re-fetch 90k vectors
        Island island;
        readonly Dictionary<int, ResourceNode> live = new Dictionary<int, ResourceNode>();

        public int TreeCount => trees != null ? trees.Length : 0;

        public void Configure(Mesh m, List<Tree> index, Island isle)
        {
            mesh = m;
            trees = index.ToArray();
            island = isle;
            verts = mesh.vertices;
        }

        /// Drop the tree: every vertex it owns onto its own base, so each of
        /// its triangles collapses to a point and draws nothing.
        public void Fell(int i)
        {
            if (trees == null || i < 0 || i >= trees.Length || trees[i].felled) return;
            var t = trees[i];
            for (int v = t.vertStart; v < t.vertStart + t.vertCount && v < verts.Length; v++)
                verts[v] = t.baseAt;
            trees[i].felled = true;
            mesh.SetVertices(verts);
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

        /// Called by the node when the crew finish it.
        public void NodeHarvested(int i)
        {
            Fell(i);
            live.Remove(i);
        }
    }
}
