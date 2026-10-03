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
    /// **Berry bushes (2026-10-03).** `takenBeds` holds the indices into
    /// the island's crop index (`Terrain.SceneryCrops`) a party picked, and
    /// `takenBedDay` the game day (`TimeOfDay.Seconds / WorkDaySeconds`) each
    /// stands again: unlike a tree or a rock a bush regrows, on the same
    /// clock as the camp's field (`Res.RegrowPerDay(Food)`, twenty days).
    /// The same one rule: picking one takes its yield (`SceneryCrops.YieldOf`,
    /// 0.5 for a bush) out of the Food stock's standing AND ceiling
    /// (`BookBedTake`), and the bed is `held` out of the field's units, so
    /// `Outpost.ReconcileCrops` and `SyncHarvest` count over what is left;
    /// the day passing gives both back (`ExpireBeds`, from
    /// `GroundTaken.Apply`).
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
        public List<int> takenBeds = new List<int>();
        public List<float> takenBedDay = new List<float>();

        [System.NonSerialized] HashSet<int> rockSet, treeSet, bedSet;
        [System.NonSerialized] HashSet<long> propSet;

        /// Anything taken by name on this island.
        public bool HasGroundTaken =>
            (takenRocks != null && takenRocks.Count > 0)
            || (takenTrees != null && takenTrees.Count > 0)
            || (takenPropX != null && takenPropX.Count > 0)
            || (takenBeds != null && takenBeds.Count > 0);

        /// Entries in the named set, all kinds. Bumps whenever one is added,
        /// so a cached order can tell it is stale.
        public int GroundTakenCount =>
            (takenRocks != null ? takenRocks.Count : 0)
            + (takenTrees != null ? takenTrees.Count : 0)
            + (takenPropX != null ? takenPropX.Count : 0)
            + (takenBeds != null ? takenBeds.Count : 0);

        void EnsureTakenSets()
        {
            if (takenRocks == null) takenRocks = new List<int>();
            if (takenTrees == null) takenTrees = new List<int>();
            if (takenPropX == null) takenPropX = new List<int>();
            if (takenPropZ == null) takenPropZ = new List<int>();
            if (takenBeds == null) takenBeds = new List<int>();
            if (takenBedDay == null) takenBedDay = new List<float>();
            while (takenBedDay.Count < takenBeds.Count) takenBedDay.Add(0f);   // a hand-edited save: due now
            if (rockSet == null || rockSet.Count != takenRocks.Count) rockSet = new HashSet<int>(takenRocks);
            if (bedSet == null || bedSet.Count != takenBeds.Count) bedSet = new HashSet<int>(takenBeds);
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
        public bool BedTaken(int i) { EnsureTakenSets(); return bedSet.Contains(i); }
        public bool PropTaken(Vector3 at) { EnsureTakenSets(); return propSet.Contains(PropKey(Dm(at.x), Dm(at.z))); }

        /// Is this source one the named set took? By rock index, tree index,
        /// or where it was made -- whichever kind it is.
        public bool Taken(ResourceNode n)
        {
            if (n == null || !HasGroundTaken) return false;
            var d = n.Deposit;
            if (d != null && d.IsScenery) return RockTaken(d.SceneryIndex);
            if (n.TreeIndex >= 0) return TreeTaken(n.TreeIndex);
            if (n.BedIndex >= 0) return BedTaken(n.BedIndex);
            return PropTaken(n.SpawnPos);
        }

        /// **Book one berry bush picked by name** (`takenBeds`), standing
        /// again on game day `regrowDay`, and take its `yield` out of the
        /// Food stock's standing and ceiling together (see the class note).
        /// Idempotent per bed. Returns the units debited.
        public float BookBedTake(int bed, float yield, float regrowDay)
        {
            if (bed < 0) return 0f;
            EnsureTakenSets();
            if (bedSet.Contains(bed)) return 0f;
            takenBeds.Add(bed); takenBedDay.Add(regrowDay); bedSet.Add(bed);
            var s = Stock(Res.Food);
            if (s == null || yield <= 0f) return 0f;
            float take = Mathf.Min(yield, Mathf.Max(0f, s.standing));
            s.standing -= take;
            s.standingMax = Mathf.Max(s.standing, s.standingMax - yield);
            return take;
        }

        /// The picked bushes whose regrow day has come (`today` in game
        /// days): out of the named set, into `into`, and their `yieldOf`
        /// given back to the Food stock's ceiling and standing. Returns how
        /// many came back.
        public int ExpireBeds(double today, System.Func<int, float> yieldOf, List<int> into)
        {
            if (takenBeds == null || takenBeds.Count == 0) return 0;
            EnsureTakenSets();
            var s = Stock(Res.Food);
            int n = 0;
            for (int k = takenBeds.Count - 1; k >= 0; k--)
            {
                if (today < takenBedDay[k]) continue;
                int bed = takenBeds[k];
                takenBeds.RemoveAt(k); takenBedDay.RemoveAt(k); bedSet.Remove(bed);
                into?.Add(bed);
                n++;
                float y = yieldOf != null ? yieldOf(bed) : 0f;
                if (s != null && y > 0f) { s.standingMax += y; s.standing = Mathf.Min(s.standingMax, s.standing + y); }
            }
            return n;
        }

        /// **Book one source out of the ground by name** and take its units
        /// out of the seam (standing and ceiling together, see the class
        /// note). `units` is the whole source -- a rock broken for less than
        /// its yield is still gone. Returns the units debited. Idempotent per
        /// source.
        public float BookGroundTake(ResourceNode n, int units)
        {
            if (n == null || n.BedIndex >= 0) return 0f;   // a bush books through `BookBedTake`
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
