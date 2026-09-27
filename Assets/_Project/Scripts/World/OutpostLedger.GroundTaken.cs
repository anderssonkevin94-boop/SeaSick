using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **What was taken off this island's ground BY NAME (2026-09-27).**
    ///
    /// The gather party (`Ship.GatherParty`) works an island with no camp and
    /// takes the source nearest its LANDING, not the one nearest a camp, so
    /// its takes cannot be a prefix of the camp's nearest-the-camp order the
    /// way a camp's gathering is (`GatherSync`, `Outpost.SyncFelling`). They
    /// are recorded here instead, one entry per source, in the island's own
    /// ledger (the survey `Outpost`'s on a camp-less island -- the same
    /// ledger a camp made there later keeps):
    ///
    /// - `takenRocks`: indices into the island's deterministic loose-rock
    ///   index (`Terrain.SceneryRocks`),
    /// - `takenTrees`: indices into the island's scenery wood
    ///   (`Terrain.SceneryWood`),
    /// - `takenPropX/Z`: every other source (a kit stone deposit, an ore or
    ///   spice prop) by the decimetre XZ it was made at
    ///   (`ResourceNode.SpawnPos`, seeded, so the same on every load).
    ///
    /// **How it reconciles with the camp's count (the one rule).** A named
    /// take moves the source OUT of the seam: the stock's `standing` AND its
    /// `standingMax` go down by the source's units (`BookGroundTake`), so the
    /// camp's derived "taken" (`standingMax - standing`) does not change and
    /// `GatherSync` / `SyncFelling` hide nothing extra for it; and every
    /// order they build leaves named sources out (`Taken`), so the camp's
    /// prefix can never land on one. The named set is drawn gone by
    /// `GroundTaken.Apply` on every `CatchUp`. So: named takes are hidden by
    /// name, camp takes by count over what is left, and neither can hide
    /// what the other took or show what either took.
    ///
    /// Saved with the ledger (`SaveGame` keeps a camp-less ledger that has
    /// any). Empty in an old save -- `JsonUtility` leaves a field its JSON
    /// does not mention at its constructed value -- so an old save loads
    /// exactly as before.
    public partial class OutpostLedger
    {
        public List<int> takenRocks = new List<int>();
        public List<int> takenTrees = new List<int>();
        public List<int> takenPropX = new List<int>();
        public List<int> takenPropZ = new List<int>();

        [System.NonSerialized] HashSet<int> rockSet, treeSet;
        [System.NonSerialized] HashSet<long> propSet;

        /// Anything taken by name on this island.
        public bool HasGroundTaken =>
            (takenRocks != null && takenRocks.Count > 0)
            || (takenTrees != null && takenTrees.Count > 0)
            || (takenPropX != null && takenPropX.Count > 0);

        /// Entries in the named set, all kinds. Bumps whenever one is added,
        /// so a cached order can tell it is stale.
        public int GroundTakenCount =>
            (takenRocks != null ? takenRocks.Count : 0)
            + (takenTrees != null ? takenTrees.Count : 0)
            + (takenPropX != null ? takenPropX.Count : 0);

        void EnsureTakenSets()
        {
            if (takenRocks == null) takenRocks = new List<int>();
            if (takenTrees == null) takenTrees = new List<int>();
            if (takenPropX == null) takenPropX = new List<int>();
            if (takenPropZ == null) takenPropZ = new List<int>();
            if (rockSet == null || rockSet.Count != takenRocks.Count) rockSet = new HashSet<int>(takenRocks);
            if (treeSet == null || treeSet.Count != takenTrees.Count) treeSet = new HashSet<int>(takenTrees);
            int props = Mathf.Min(takenPropX.Count, takenPropZ.Count);
            if (propSet == null || propSet.Count != props)
            {
                propSet = new HashSet<long>();
                for (int i = 0; i < props; i++) propSet.Add(PropKey(takenPropX[i], takenPropZ[i]));
            }
        }

        static long PropKey(int x, int z) => ((long)x << 32) ^ (uint)z;
        static int Dm(float v) => Mathf.RoundToInt(v * 10f);

        public bool RockTaken(int i) { EnsureTakenSets(); return rockSet.Contains(i); }
        public bool TreeTaken(int i) { EnsureTakenSets(); return treeSet.Contains(i); }
        public bool PropTaken(Vector3 at) { EnsureTakenSets(); return propSet.Contains(PropKey(Dm(at.x), Dm(at.z))); }

        /// Is this source one the named set took? By rock index, tree index,
        /// or where it was made -- whichever kind it is.
        public bool Taken(ResourceNode n)
        {
            if (n == null || !HasGroundTaken) return false;
            var d = n.Deposit;
            if (d != null && d.IsScenery) return RockTaken(d.SceneryIndex);
            if (n.TreeIndex >= 0) return TreeTaken(n.TreeIndex);
            return PropTaken(n.SpawnPos);
        }

        /// **Book one source out of the ground by name** and take its units
        /// out of the seam (standing and ceiling together, see the class
        /// note). `units` is the whole source -- a rock broken for less than
        /// its yield is still gone. Returns the units debited. Idempotent per
        /// source.
        public float BookGroundTake(ResourceNode n, int units)
        {
            if (n == null) return 0f;
            EnsureTakenSets();
            if (Taken(n)) return 0f;
            var d = n.Deposit;
            if (d != null && d.IsScenery) { takenRocks.Add(d.SceneryIndex); rockSet.Add(d.SceneryIndex); }
            else if (n.TreeIndex >= 0) { takenTrees.Add(n.TreeIndex); treeSet.Add(n.TreeIndex); }
            else
            {
                int x = Dm(n.SpawnPos.x), z = Dm(n.SpawnPos.z);
                takenPropX.Add(x); takenPropZ.Add(z); propSet.Add(PropKey(x, z));
            }

            var s = Stock(n.Resource);
            if (s == null || units <= 0) return 0f;
            float take = Mathf.Min(units, Mathf.Max(0f, s.standing));
            s.standing -= take;
            s.standingMax = Mathf.Max(s.standing, s.standingMax - take);
            return take;
        }
    }
}
