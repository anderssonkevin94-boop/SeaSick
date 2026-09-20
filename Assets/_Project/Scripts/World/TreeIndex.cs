using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Which standing tree is nearest this point**, without walking the
    /// whole wood to find out.
    ///
    /// `SceneryWood` keeps its trees in one flat array, and the only question
    /// anybody used to ask of it was `CampWorker`'s — once per trip per hand,
    /// so a linear scan over a few hundred trees was honest. The Hand asks the
    /// same question every frame the cursor moves, which is a different budget.
    ///
    /// A lazy grid over XZ, built once per wood from `TreeAt`. **Positions are
    /// cached; whether a tree is felled is not** — that is read live from the
    /// wood on every query, because felling is driven by the ledger and a
    /// cached answer would be a second source of truth about the same stump.
    ///
    /// Owns nothing and changes nothing: `SceneryWood` is not touched.
    public sealed class TreeIndex
    {
        /// Metres to a cell. About the spacing of a camp's clearing, so a
        /// typical query reads four to nine cells.
        public const float CellSize = 8f;

        static readonly Dictionary<Terrain.SceneryWood, TreeIndex> indices
            = new Dictionary<Terrain.SceneryWood, TreeIndex>();

        readonly Terrain.SceneryWood wood;
        readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        readonly int builtFor;

        /// The index for this wood, built on first ask. Rebuilt if the wood
        /// has been reconfigured with a different number of trees.
        public static TreeIndex For(Terrain.SceneryWood wood)
        {
            if (wood == null) return null;
            if (indices.TryGetValue(wood, out var ix) && ix.builtFor == wood.TreeCount) return ix;
            Prune();
            ix = new TreeIndex(wood);
            indices[wood] = ix;
            return ix;
        }

        /// The wood standing on this outpost's island, indexed. Null if the
        /// island has no welded wood (a sandbank, or scenery not yet built).
        public static TreeIndex For(Outpost outpost)
        {
            if (outpost == null) return null;
            return For(outpost.GetComponentInChildren<Terrain.SceneryWood>());
        }

        TreeIndex(Terrain.SceneryWood wood)
        {
            this.wood = wood;
            builtFor = wood.TreeCount;
            for (int i = 0; i < builtFor; i++)
            {
                Vector3 at = wood.TreeAt(i).baseAt;
                long key = Key(CellOf(at.x), CellOf(at.z));
                if (!cells.TryGetValue(key, out var list))
                    cells[key] = list = new List<int>(8);
                list.Add(i);
            }
        }

        public Terrain.SceneryWood Wood => wood;

        /// Index of the nearest tree still standing within `maxDistance` of
        /// `at` (flat distance), or -1. Walks outward ring by ring and stops
        /// as soon as no unvisited ring could hold anything nearer.
        public int NearestStanding(Vector3 at, float maxDistance)
        {
            if (wood == null || wood.TreeCount != builtFor) return -1;
            int cx = CellOf(at.x), cz = CellOf(at.z);
            int rings = Mathf.CeilToInt(maxDistance / CellSize) + 1;
            float best = maxDistance * maxDistance;
            int found = -1;

            for (int r = 0; r <= rings; r++)
            {
                // Everything in ring r is at least (r-1) cells away.
                float ringNear = Mathf.Max(0, r - 1) * CellSize;
                if (ringNear * ringNear > best) break;

                for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                    if (!cells.TryGetValue(Key(cx + dx, cz + dz), out var list)) continue;
                    for (int k = 0; k < list.Count; k++)
                    {
                        var t = wood.TreeAt(list[k]);
                        if (t.felled) continue;
                        float ddx = t.baseAt.x - at.x, ddz = t.baseAt.z - at.z;
                        float m = ddx * ddx + ddz * ddz;
                        if (m < best) { best = m; found = list[k]; }
                    }
                }
            }
            return found;
        }

        /// As `NearestStanding`, but hands back where it stands.
        public bool NearestStanding(Vector3 at, float maxDistance, out Vector3 baseAt)
        {
            int i = NearestStanding(at, maxDistance);
            baseAt = i >= 0 ? wood.TreeAt(i).baseAt : Vector3.zero;
            return i >= 0;
        }

        static int CellOf(float v) => Mathf.FloorToInt(v / CellSize);
        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        // Woods die with their islands as the terrain streams out.
        static readonly List<Terrain.SceneryWood> dead = new List<Terrain.SceneryWood>();
        static void Prune()
        {
            dead.Clear();
            foreach (var kv in indices) if (kv.Key == null) dead.Add(kv.Key);
            foreach (var k in dead) indices.Remove(k);
        }
    }
}
