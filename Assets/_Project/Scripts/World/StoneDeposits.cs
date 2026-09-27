using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **A camp's stone deposits, 2026-09-24**: makes sure the island a camp
    /// stands on has kit rocks (`StoneDeposit`) for its Stone seam, and sizes
    /// the seam to them once.
    ///
    /// - Every Stone `ResourceNode` of the island is dressed as a deposit
    ///   (they dress themselves in `ResourceNode.Start` too, on every island;
    ///   this just makes sure a camp's are ready before the seam is sized).
    /// - An island with a Stone seam in its books and NO rock on the ground
    ///   -- the starting island: the populator's home branch places no props
    ///   while `Outpost.EnsureStoneStock` still books a seam, so gatherers
    ///   swung at thin air -- gets `HomeMin`..`HomeMax` deposits laid here,
    ///   on flat-ish ground in the band `Outpost.PlaceCampStone` uses (past
    ///   the clearing, so clear of the fire ring and the buildings), above
    ///   the beach and inside the shore. Seeded from the island's position
    ///   and the camp centre, both saved, so a reload lays the same rocks.
    /// - `OutpostLedger.SizeStoneToDeposits` gets the island's total units.
    ///
    /// - **The island's loose scenery rocks count (2026-09-27,
    ///   `SceneryStone`)**: stood as Stone nodes first, so they are in the
    ///   sum, and an island that has them gets nothing laid.
    ///
    /// Called from `Outpost.CatchUp`, right after `PlaceCampStone`.
    /// Idempotent and cheap after the first call.
    public static class StoneDeposits
    {
        public const int HomeMin = 4, HomeMax = 8;
        /// Metres kept between two laid deposits (a worker walks between).
        const float Spacing = 6f;
        /// Metres kept from any standing building's pivot.
        const float BuildingClear = 8f;
        /// Steepest ground a laid deposit sits on (rise over run, 2 m apart).
        const float MaxSlope = 0.3f;

        static readonly List<ResourceNode> scratch = new List<ResourceNode>();
        static readonly List<Vector3> laid = new List<Vector3>();

        public static void EnsureOn(Outpost o)
        {
            if (o == null || o.Ledger == null) return;
            var isle = o.Island;
            if (isle == null || o.Ledger.Stock(Res.Stone) == null) return;

            // The island's loose scenery rocks first (2026-09-27): they are
            // stone too, and an island that has them needs no rocks laid.
            // Not settled yet (the bake or the path grid is still coming) ->
            // the seam waits, so it is sized to every rock, not to half.
            bool settled = SceneryStone.Ensure(o);

            Collect(isle);
            if (settled && scratch.Count == 0 && o.HasGround) { Lay(o, isle); Collect(isle); }

            float units = 0f;
            for (int i = 0; i < scratch.Count; i++)
            {
                var n = scratch[i];
                var d = StoneDeposit.Dress(n);
                // A rock on a building plot is the plot's (see `GatherSync`).
                // Nor is a rock a gather party took by name: it left the
                // seam when it was taken (`OutpostLedger.GroundTaken`).
                if (d != null && !n.HeldBySite && !o.Ledger.Taken(n)) units += d.Units;
            }
            scratch.Clear();
            if (settled) o.Ledger.SizeStoneToDeposits(units);
        }

        static void Collect(Island isle)
        {
            scratch.Clear();
            var all = ResourceNode.All;
            for (int i = 0; i < all.Count; i++)
            {
                var n = all[i];
                if (n != null && n.Home == isle && n.Resource == Res.Stone) scratch.Add(n);
            }
        }

        /// Lay a seeded handful of deposits round the camp on an island that
        /// has none.
        static void Lay(Outpost o, Island isle)
        {
            Vector3 centre = o.CampCentre;
            Vector3 ip = isle.transform.position;
            int seed = unchecked(Mathf.RoundToInt(ip.x) * 73856093 ^ Mathf.RoundToInt(ip.z) * 19349663
                                 ^ Mathf.RoundToInt(centre.x) * 83492791 ^ Mathf.RoundToInt(centre.z) * 2654435);
            var rng = new System.Random(seed);
            int want = rng.Next(HomeMin, HomeMax + 1);

            // Same band `Outpost.PlaceCampStone` stands rock in, so that ring
            // never moves what this lays (and the rock is outside the clearing
            // a building goes into, and well off the fire).
            float near = Mathf.Max(o.ClearingRadius + 4f, 14f) + 1f;
            float far = near + 13f;
            float beach = o.MinGroundHeight + 0.8f;

            laid.Clear();
            for (int pass = 0; pass < 2 && laid.Count < want; pass++)
            {
                for (int tries = 0; tries < want * 40 && laid.Count < want; tries++)
                {
                    float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                    float r = Mathf.Lerp(near, far, (float)rng.NextDouble());
                    Vector3 p = centre + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                    if (!Fits(o, isle, p, beach, out float h)) continue;
                    p.y = h;
                    laid.Add(p);
                }
                far += 12f;     // a tight island: look a little further out
            }

            for (int i = 0; i < laid.Count; i++)
            {
                var go = new GameObject("StoneDeposit_" + i);
                go.transform.SetParent(isle.transform, true);
                go.transform.position = laid[i];
                // Position first: `Awake` takes `SpawnPos` from it.
                var node = go.AddComponent<ResourceNode>();
                node.Configure(Res.Stone, isle, 4);
                StoneDeposit.Dress(node);
            }
            laid.Clear();
        }

        static bool Fits(Outpost o, Island isle, Vector3 p, float beach, out float h)
        {
            h = o.GroundAt(p);
            if (h < beach) return false;
            // Inside the shore on its bearing, with a margin.
            Vector3 fromIsle = p - isle.transform.position;
            fromIsle.y = 0f;
            if (fromIsle.magnitude > isle.RadiusToward(p) * 0.85f) return false;
            // Flat-ish: every neighbour 2 m off within the slope.
            float lim = MaxSlope * 2f;
            if (Mathf.Abs(o.GroundAt(p + new Vector3(2f, 0f, 0f)) - h) > lim) return false;
            if (Mathf.Abs(o.GroundAt(p - new Vector3(2f, 0f, 0f)) - h) > lim) return false;
            if (Mathf.Abs(o.GroundAt(p + new Vector3(0f, 0f, 2f)) - h) > lim) return false;
            if (Mathf.Abs(o.GroundAt(p - new Vector3(0f, 0f, 2f)) - h) > lim) return false;
            for (int i = 0; i < laid.Count; i++)
                if (Flat(laid[i] - p) < Spacing) return false;
            var built = o.Built;
            if (built != null)
                for (int i = 0; i < built.Count; i++)
                    if (built[i] != null && Flat(built[i].transform.position - p) < BuildingClear) return false;
            return true;
        }

        static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }
    }
}
