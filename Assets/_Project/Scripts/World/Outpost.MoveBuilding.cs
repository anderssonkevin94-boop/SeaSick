using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Move or turn a building that already stands (Kevin, 2026-09-30:
    /// "I want to be able to turn and move buildings even after they're
    /// built. Don't allow this for the walls or roads.").**
    ///
    /// Free and instant: the player picks the spot in the ordinary siting
    /// mode (`CampSiting.BeginMove`, the same ghost, bar and ✓ as a new
    /// building) and on ✓ the SAME building -- the same GameObject, the same
    /// `built` slot, the same `raised` row -- is stood at the new spot. It is
    /// moved in place rather than pulled down and raised again because half
    /// the camp holds the `Building` itself (a sawyer's `preferred` mill,
    /// the lookout's `towerOn`, the store piles' `hut`, every open sheet) and
    /// the ledger keys its stock, level, worker and pinned goal to the row's
    /// INDEX; all of that stays good only if nothing is replaced.
    ///
    /// **"Buildings never move" (2026-09-27) is about LOADING**: the game
    /// never relocates a saved building. The player's own move writes the
    /// new x/z/yaw into the row (`OutpostLedger.MoveRaised`), which is what
    /// the next load stands, exactly.
    ///
    /// **One test** for the ghost and the move, `CanMoveTo`, which is the
    /// new-building test (`TooFarFromTown` + `CanPlace`: footprint, slope,
    /// shore, water, overlap, walls, roads, the fishing hut's shore) with
    /// the building's own reserved disc left out -- plus one rule a new
    /// building does not have: the new plot must already be clear of trees
    /// and rocks, because an instant move has no CLEAR phase for the hands
    /// to work through.
    ///
    /// Not movable: a pier or a dry dock (the ship's berth, her home berth
    /// and the refit slip are registered from where they stand -- `Dock`,
    /// `Dock.Home`, the saved home berth, `DryDockSlip` -- and she may be
    /// lying at one), and a watchtower joined to the wall (it is part of the
    /// run). Walls, gates, roads and ladders are not `Building`s at all.
    public partial class Outpost
    {
        /// **The building whose own ground does not count against it.**
        /// Set for the life of a move session (`CampSiting`) and around the
        /// move itself; its reserved disc is skipped by `ClearOfReserved`.
        /// Same contract as `IgnoreSite`: set it, ask, clear it.
        public Building MovingBuilt
        {
            get => movingBuilt;
            set
            {
                movingBuilt = value;
                movingReservation = value != null && ReservationAt(value.transform.position, out var r)
                    ? r : (Vector4?)null;
            }
        }
        Building movingBuilt;
        Vector4? movingReservation;

        /// The disc `Raise` reserved for the building standing at `root`:
        /// its centre is the root's own x/z (only y differs, by the slope
        /// sink).
        bool ReservationAt(Vector3 root, out Vector4 res)
        {
            res = default;
            bool found = false;
            float best = 0.05f * 0.05f;
            foreach (var r in buildingReservations)
            {
                float dx = r.x - root.x, dz = r.z - root.z;
                float d = dx * dx + dz * dz;
                if (d <= best) { best = d; res = r; found = true; }
            }
            return found;
        }

        /// **Can this building be picked up at all?** The sheets ask it to
        /// decide whether to show Move. `why` is the sentence for a no.
        public bool CanMove(Building b, out string why)
        {
            why = "";
            if (b == null || ledger == null || ledger.raised == null) { why = "nothing to move"; return false; }
            int i = built.IndexOf(b);
            if (i < 0 || i >= ledger.raised.Count || ledger.raised[i] == null
                || ledger.raised[i].planId != b.Id)
            { why = "this building is not in the camp's books"; return false; }
            if (b.Id == BuildPlans.Palisade.id || b.Id == BuildPlans.Gate.id
                || b.Id == BuildPlans.Road.id || b.Id == BuildPlans.Ladder.id)
            { why = "walls, gates, roads and ladders stay where they were built"; return false; }
            if (b.Kind == BuildKind.Pier) { why = "a pier stays where it was built"; return false; }
            if (b.Kind == BuildKind.DryDock) { why = "the dry dock stays where it was built"; return false; }
            // Dug into its hill (2026-10-05): a new spot is a new mine.
            if (b.Kind == BuildKind.Mine) { why = "a mine stays dug into its hill"; return false; }
            if (IsWallTower(b)) { why = "a tower on the wall is part of the wall"; return false; }
            return true;
        }

        /// The plan a standing building was raised from, at the length its
        /// row recorded. Default when it is not in the books.
        public BuildPlan PlanOfBuilt(Building b)
        {
            int i = b != null ? built.IndexOf(b) : -1;
            if (ledger == null || ledger.raised == null || i < 0 || i >= ledger.raised.Count
                || ledger.raised[i] == null) return default;
            return PlanFor(ledger.raised[i].planId, ledger.raised[i].length);
        }

        /// The facing a standing building has now (its row's yaw).
        public float YawOfBuilt(Building b)
        {
            int i = b != null ? built.IndexOf(b) : -1;
            if (ledger == null || ledger.raised == null || i < 0 || i >= ledger.raised.Count
                || ledger.raised[i] == null) return b != null ? b.transform.eulerAngles.y : 0f;
            return ledger.raised[i].yaw;
        }

        /// **Would `b` stand at `at`, facing `yaw`?** The one test the move's
        /// ghost and the move itself both ask -- see the file header.
        public bool CanMoveTo(Building b, Vector3 at, float yaw, out string why)
            => CanMoveTo(b, at, yaw, out why, out _, out _);

        bool CanMoveTo(Building b, Vector3 at, float yaw, out string why, out float lo, out float hi)
        {
            lo = hi = 0f;
            if (!CanMove(b, out why)) return false;
            var plan = PlanOfBuilt(b);
            if (string.IsNullOrEmpty(plan.id)) { why = "this building is not in the camp's books"; return false; }

            var was = MovingBuilt;
            bool set = was != b;
            if (set) MovingBuilt = b;
            try
            {
                // The fire IS the camp's centre, so `TooFarFromTown` waves it
                // through; a moved fire must still be on ground the camp can
                // walk to, or every building would be cut off from it.
                if (plan.kind == BuildKind.Fire && hasCampCentre && !CampPath.Reachable(this, at))
                {
                    why = "no walkable path from the camp reaches this ground";
                    return false;
                }
                if (TooFarFromTown(plan, at, out why)) return false;
                // A free-standing tower is not snapped onto the wall by a
                // move (joining a run is a new tower's business), and a
                // tower that lands exactly on a wall node would be judged
                // by the wall tower's relaxed rules while standing apart.
                if (IsTowerPlan(plan) && OnWallNode(at)) { why = "the wall is in the way"; return false; }
                if (!CanPlace(plan, at, yaw, out why, out lo, out hi)) return false;
                CountObstructions(plan, at, yaw, out int trees, out int rocks);
                if (trees > 0 || rocks > 0)
                {
                    why = ObstructionWords(trees, rocks) + " in the way -- a building moves onto clear ground";
                    return false;
                }
                return true;
            }
            finally
            {
                if (set) MovingBuilt = was;
            }
        }

        static string ObstructionWords(int trees, int rocks)
        {
            string t = trees == 1 ? "1 tree" : trees + " trees";
            string r = rocks == 1 ? "1 rock" : rocks + " rocks";
            if (trees > 0 && rocks > 0) return t + " and " + r;
            return trees > 0 ? t : r;
        }

        /// **Stand `b` at `at`, facing `yaw`, now.** False (and nothing
        /// changed) when `CanMoveTo` says no. Everything that remembered
        /// where the building stood is told, in this order: the clearing
        /// books, the building, its row, its reserved disc, the camp centre
        /// (the fire), the farm's beds, the fishing hut's shore spot, trips
        /// already under way, the bodies, the store's piles.
        public bool MoveBuilt(Building b, Vector3 at, float yaw, out string why)
        {
            if (!CanMoveTo(b, at, yaw, out why, out float lo, out float hi)) return false;
            int idx = built.IndexOf(b);
            var row = ledger.raised[idx];
            var plan = PlanOfBuilt(b);

            Vector3 oldRoot = b.transform.position;
            Quaternion oldRot = b.transform.rotation;
            Vector3 oldAt = row.At;
            float oldYaw = row.yaw;
            Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
            Vector3 p = at;
            p.y = hi;

            // --- the clearing books (the CLEAR phase's registry) -----------
            // The old plot's stumps and broken rocks are the building's; the
            // new plot's (already felled, or `CanMoveTo` refused it) become
            // the building's. Exactly what a blueprint MOVE does with its own
            // plot (`Site` with a moving row): count the new plot as a site
            // would, then hand back what the old one leaves behind.
            SnapshotBuiltPlot(plan, oldAt, oldYaw, snapTrees, snapRocks);
            TakeFootprint(new PendingBuild
                { planId = plan.id, x = p.x, z = p.z, yaw = yaw, length = row.length });

            // --- the building, its row, its disc ------------------------------
            bool hadDisc = ReservationAt(oldRoot, out Vector4 oldDisc);
            BuildingFactory.Repose(b.transform, p, facing, hi - lo);
            // The row gets the root's own x/z, exactly: `StationStockView`
            // matches its station by an exact position compare.
            ledger.MoveRaised(idx, b.transform.position, yaw);
            if (hadDisc) { reserved.Remove(oldDisc); buildingReservations.Remove(oldDisc); }
            float len = plan.footprint.x, wid = plan.footprint.y;
            float halfDiag = 0.5f * Mathf.Sqrt(len * len + wid * wid);
            var disc = new Vector4(p.x, p.y, p.z, halfDiag + spacing * 0.5f);
            reserved.Add(disc);
            buildingReservations.Add(disc);
            if (movingBuilt == b) movingReservation = disc;
            Terrain.SceneryGround.ClearFootprintNear(p, facing, plan.footprint, 1f);

            // --- the fire is the camp's centre ----------------------------------
            // The ledger key, the path grid (re-centred when it moved far),
            // the fire ring the idle hands stand in, the warm-hut radius and
            // the store square before a storage hut all follow the centre.
            if (plan.kind == BuildKind.Fire)
            {
                SetCampCentre(p);
                ledger.SetCentre(p);
                // Its store cache chooses again round the new spot
                // (2026-10-03): the saved one is keyed to the old.
                PlaceFireCache();
            }

            // The registry re-reads every row now (its key only counts rows,
            // not where they stand), then the old plot's leftovers go back
            // on the camp's books -- the ones the new plot did not take.
            clearKey = int.MinValue;
            EnsureClearing(true);
            ReturnToCamp(snapTrees, snapRocks);

            // The fishing hut's cached stand-and-fish spot is keyed to the
            // row's position (`shoreForX/Z`), so it is found again here.
            SaveShoreSpots();
            footCache.Remove(b.GetInstanceID());

            // Trips already under way to or from it turn for the new spot;
            // the bodies walking them re-aim, and anyone standing on it
            // (the lookout on his deck, a sleeper inside) comes with it.
            ledger.ReaimTrips();
            CampWorker.BuildingMoved(this, b, oldRoot);
            // (The store piles that had to be re-laid here are retired,
            // 2026-10-03: `StorageSlotView` rides on the building itself.)
            if (Watched) { ArrangeHands(); PuppetsToWork(); }
            why = "";
            return true;
        }

        /// What of a standing building's plot is down right now: its felled
        /// trees and broken rocks, the ones the registry holds for it.
        void SnapshotBuiltPlot(BuildPlan plan, Vector3 at, float yaw,
            List<int> trees, List<ResourceNode> rocks)
        {
            trees.Clear(); rocks.Clear();
            if (plan.kind == BuildKind.Pier || plan.kind == BuildKind.DryDock) return;
            EnsureClearing(true);
            var shape = ClearShape.Rect(at, yaw, plan.footprint);
            var wood = WoodHere();
            if (wood != null)
                for (int i = 0; i < wood.TreeCount && i < siteTree.Length; i++)
                {
                    if (!siteTree[i]) continue;
                    var t = wood.TreeAt(i);
                    if (t.felled && shape.Contains(t.baseAt)) trees.Add(i);
                }
            GatherRocks(rockScratch);
            foreach (var n in rockScratch)
                if (n != null && n.Gathered && siteRocks.Contains(n) && shape.Contains(n.transform.position))
                    rocks.Add(n);
        }
    }
}
