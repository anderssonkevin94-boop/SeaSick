using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Watchtowers on the wall (Kevin, 2026-09-27: "yes to the watchtower
    /// plan, go ahead").**
    ///
    /// A watchtower sited within a few metres of a wall snaps onto a wall
    /// NODE (a post on the 2 m lattice: a segment's end, or a lattice point
    /// inside a standing segment, which is then split in two there) and
    /// becomes part of the wall:
    ///
    /// - the post at that node comes down and the runs either side stop at
    ///   the tower's legs (`WallChain`, `WallVisual.Fit.towerA/B`);
    /// - the node's cell blocks everybody in `CampPath`, like wall, and
    ///   stays blocked while the tower stands even if a run beside it is
    ///   breached (the runs still END at the node, so the line never has a
    ///   gap there -- a destroyed tower leaves a post, not a hole);
    /// - the campfire reach (`TownRadius`) does not apply to it -- walls can
    ///   reach further than the camp;
    /// - the lookout's door is on the camp side of the wall (`WallTowerDoor`).
    ///
    /// **Nothing is saved for it.** A wall tower is a watchtower whose
    /// position is a node some wall run ends at; that is derived, on load as
    /// at any other time, so old saves load unchanged and a wall later drawn
    /// to a free-standing tower makes it a wall tower the same way. Cost, the
    /// cannon (`WatchtowerGun`) and raider targeting are the watchtower's own
    /// and unchanged.
    public partial class Outpost
    {
        /// Metres from a wall node or wall line within which a watchtower
        /// being sited snaps onto the wall.
        public const float WallTowerSnap = 3f;

        /// **How far the furthest wall post stands from the town centre,
        /// metres. 0 with no wall at all.**
        ///
        /// A wall has no reach limit (`CanPlaceWall` never asks
        /// `TooFarFromTown`), so Kevin can and does walk a run well past
        /// `TownRadius` -- and 2026-09-27, wanted a watchtower along ANY
        /// side of it. The siting ghost's own reach test is exempt for a
        /// tower on the wall already (`TooFarFromTown`, `CanPlace`), but the
        /// CAMERA the player points with is not: it was left on whatever it
        /// was last zoomed to (`CampSiting.DropTheViewOn`, 35 m above the
        /// last thing sited), so a wall run beyond that is off screen and
        /// the thumb cannot drag or tap anywhere near it. `CampSiting` reads
        /// this to widen `IslandCam`'s pan reach for exactly the life of a
        /// siting session, so "any side of the wall" is also reachable, not
        /// only placeable.
        public float WallExtentFromCentre()
        {
            float best = 0f;
            Vector3 c = CampCentre;
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                if (w == null) continue;
                best = Mathf.Max(best, Island.FlatDistance(w.A, c), Island.FlatDistance(w.B, c));
            }
            if (ledger != null && ledger.sites != null)
                foreach (var row in ledger.sites)
                {
                    if (row == null || !row.isWall) continue;
                    best = Mathf.Max(best, Island.FlatDistance(row.postA, c),
                        Island.FlatDistance(row.postB, c));
                }
            return best;
        }

        /// Set only for the length of one `CanPlace` of a tower on the wall:
        /// the wall runs that pass through this node do not refuse it.
        Vector3? wallTowerNode;

        static bool IsTowerPlan(BuildPlan plan) => plan.id == OutpostLedger.WatchtowerId;

        /// `ClearOfPlans`' exemption: this wall line is the one the tower
        /// being tested joins.
        bool TowerJoins(Vector3 a, Vector3 b)
            => wallTowerNode.HasValue && WallSegment.FlatDistance(a, b, wallTowerNode.Value) < 0.3f;

        /// `CanPlaceWall`'s exemption: a run may END on a watchtower (built
        /// or queued) -- which is what makes it a wall tower.
        static bool TowerEndsRun(string planId, Vector3 towerAt, Vector3 a, Vector3 b)
            => planId == OutpostLedger.WatchtowerId && (SamePost(a, towerAt) || SamePost(b, towerAt));

        static bool OnLattice(Vector3 p)
        {
            float rx = Mathf.Round(p.x / WallPostStep) * WallPostStep;
            float rz = Mathf.Round(p.z / WallPostStep) * WallPostStep;
            return Mathf.Abs(p.x - rx) < 0.15f && Mathf.Abs(p.z - rz) < 0.15f;
        }

        /// Is `at` a wall node a tower may stand on? Exactly on it, flat.
        public bool OnWallNode(Vector3 at)
            => OnLattice(at) && FindWallNode(at, 0.3f, out _);

        /// **Where a watchtower dragged to `p` would snap onto the wall**:
        /// the nearest eligible node within `within` metres, or false.
        ///
        /// Eligible: the end of any standing or queued wall run that is not
        /// a gate's (a gate's module overhangs its posts), and the lattice
        /// points inside a STANDING plain segment with nothing queued on its
        /// posts (it is split there when the tower is sited, `JoinTowerToWall`).
        /// A queued run is only snapped to at its ends -- splitting a row that
        /// may already have logs carried to it is not worth the books.
        public bool FindWallNode(Vector3 p, float within, out Vector3 node)
        {
            Vector3 pick = p;
            float best = within * within;
            bool found = false;
            var gateEnds = GateEnds();

            void Try(Vector3 q)
            {
                for (int g = 0; g < gateEnds.Count; g++) if (SamePost(q, gateEnds[g])) return;
                float dx = q.x - p.x, dz = q.z - p.z, d2 = dx * dx + dz * dz;
                if (d2 >= best) return;
                best = d2;
                pick = q;
                found = true;
            }

            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                if (w == null || w.IsGate) continue;
                // Cheap reject: nothing on this line is near `p`.
                if (w.FlatDistanceTo(p) > within + 0.01f) continue;
                Try(w.A);
                Try(w.B);
                if (QueuedOn(w.A, w.B)) continue;
                foreach (var q in InteriorNodes(w.A, w.B)) Try(q);
            }
            if (ledger != null && ledger.sites != null)
                foreach (var row in ledger.sites)
                {
                    if (row == null || !row.isWall || row.planId == BuildPlans.Gate.id) continue;
                    Try(row.postA);
                    Try(row.postB);
                }
            node = found ? SnapPost(pick) : p;
            return found;
        }

        /// The posts of every gate, standing or queued.
        List<Vector3> GateEnds()
        {
            var ends = new List<Vector3>();
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                if (w == null || !w.IsGate) continue;
                ends.Add(w.A); ends.Add(w.B);
            }
            if (ledger != null && ledger.sites != null)
                foreach (var row in ledger.sites)
                    if (row != null && row.isWall && row.planId == BuildPlans.Gate.id)
                    { ends.Add(row.postA); ends.Add(row.postB); }
            return ends;
        }

        /// Is a gate or a repair queued on exactly these posts?
        bool QueuedOn(Vector3 a, Vector3 b)
        {
            if (ledger == null || ledger.sites == null) return false;
            foreach (var row in ledger.sites)
                if (row != null && row.isWall
                    && ((SamePost(row.postA, a) && SamePost(row.postB, b))
                        || (SamePost(row.postA, b) && SamePost(row.postB, a)))) return true;
            return false;
        }

        /// The 2 m lattice points strictly inside a->b (both ends on the
        /// lattice): gcd(dx, dz) - 1 of them, evenly spaced.
        IEnumerable<Vector3> InteriorNodes(Vector3 a, Vector3 b)
        {
            int dx = Mathf.RoundToInt((b.x - a.x) / WallPostStep);
            int dz = Mathf.RoundToInt((b.z - a.z) / WallPostStep);
            int g = Gcd(Mathf.Abs(dx), Mathf.Abs(dz));
            for (int k = 1; k < g; k++)
                yield return SnapPost(a + (b - a) * ((float)k / g));
        }

        static int Gcd(int x, int y)
        {
            while (y != 0) { int t = x % y; x = y; y = t; }
            return x;
        }

        /// **Make the wall meet the tower at `node`.** A standing plain
        /// segment that runs THROUGH the node is split into two there
        /// (hit points shared by length, breached stays breached). Called by
        /// `CampSiting` once the tower is sited; true if anything was split.
        public bool JoinTowerToWall(Vector3 node)
        {
            node = SnapPost(node);
            bool split = false;
            for (int i = walls.Count - 1; i >= 0; i--)
            {
                var w = walls[i];
                if (w == null || w.IsGate) continue;
                if (SamePost(w.A, node) || SamePost(w.B, node)) continue;
                if (w.FlatDistanceTo(node) > 0.1f) continue;
                if (QueuedOn(w.A, w.B)) continue;
                SplitWall(w, node);
                split = true;
            }
            if (split) TouchWallTowers();
            return split;
        }

        void SplitWall(WallSegment seg, Vector3 node)
        {
            float frac = seg.MaxHp > 0f ? Mathf.Clamp01(seg.Hp / seg.MaxHp) : 1f;
            Vector3 a = seg.A, b = seg.B;
            var map = CampPath.For(this);
            if (map != null) map.MarkWall(seg, false);
            ForgetWall(seg);
            // Out of the chain NOW: a dying segment lying along both halves
            // would read as an acute neighbour and trim their ends.
            var chain = WallChain.Of(transform);
            if (chain != null) chain.Remove(seg);
            Destroy(seg.gameObject);
            foreach (var (p, q) in new[] { (a, node), (node, b) })
            {
                float len = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(q.x, q.z));
                float max = WallSegment.HpFor(len, false);
                StandWall(new BuiltWall
                {
                    ax = p.x, az = p.z, bx = q.x, bz = q.z,
                    isGate = false, hp = max * frac, maxHp = max,
                });
            }
        }

        /// The flat direction of the first standing or queued run at
        /// `node`, either way round (a line has no inherent direction) --
        /// at either END, or PARTWAY ALONG a standing plain segment that has
        /// not been split there yet (siting happens before `JoinTowerToWall`
        /// runs, so a node in the middle of a run matches nothing at either
        /// end: without the distance-to-line test below, `WallTowerYaw`
        /// fell back to toward-the-fire for every mid-span tower). False
        /// with `Vector3.forward` if nothing does.
        bool WallRunDirAt(Vector3 node, out Vector3 dir)
        {
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                if (w == null || w.IsGate) continue;
                if (SamePost(w.A, node)) { dir = WallVisual.Flat(w.B - w.A).normalized; return true; }
                if (SamePost(w.B, node)) { dir = WallVisual.Flat(w.A - w.B).normalized; return true; }
                if (w.FlatDistanceTo(node) < 0.3f) { dir = WallVisual.Flat(w.B - w.A).normalized; return true; }
            }
            if (ledger != null && ledger.sites != null)
                foreach (var row in ledger.sites)
                {
                    if (row == null || !row.isWall) continue;
                    if (SamePost(row.postA, node))
                    { dir = WallVisual.Flat(row.postB - row.postA).normalized; return true; }
                    if (SamePost(row.postB, node))
                    { dir = WallVisual.Flat(row.postA - row.postB).normalized; return true; }
                    if (WallSegment.FlatDistance(row.postA, row.postB, node) < 0.3f)
                    { dir = WallVisual.Flat(row.postB - row.postA).normalized; return true; }
                }
            dir = Vector3.forward;
            return false;
        }

        /// **Which way a tower joined to `node` should face while it is
        /// being sited (Kevin, 2026-09-27): square across the wall, not
        /// diagonal to it** -- the plan's `front` turned perpendicular to
        /// the run, toward the camp side, the same side `WallTowerDoor`
        /// puts the lookout on. Falls back to the ordinary toward-the-fire
        /// facing where the node has no run yet (nothing to square to).
        public float WallTowerYaw(Vector3 node)
        {
            if (!WallRunDirAt(node, out Vector3 along)) return AutoYaw(node);
            Vector3 perp = Vector3.Cross(Vector3.up, along).normalized;
            Vector3 toFire = CampCentre - node;
            toFire.y = 0f;
            if (Vector3.Dot(perp, toFire) < 0f) perp = -perp;
            return Mathf.Atan2(perp.x, perp.z) * Mathf.Rad2Deg;
        }

        /// The built watchtower standing on this node, or null.
        public Building WallTowerAt(Vector3 node)
        {
            for (int i = 0; i < built.Count; i++)
            {
                var b = built[i];
                if (b == null || b.Id != OutpostLedger.WatchtowerId) continue;
                if (SamePost(b.transform.position, node) && WallEndsAt(node)) return b;
            }
            return null;
        }

        /// Is this building a watchtower some wall run ends at?
        public bool IsWallTower(Building b)
            => b != null && b.Id == OutpostLedger.WatchtowerId
               && OnLattice(b.transform.position) && WallEndsAt(b.transform.position);

        /// Does any standing or queued wall run end on this node?
        bool WallEndsAt(Vector3 node)
        {
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                if (w != null && (SamePost(w.A, node) || SamePost(w.B, node))) return true;
            }
            if (ledger != null && ledger.sites != null)
                foreach (var row in ledger.sites)
                    if (row != null && row.isWall
                        && (SamePost(row.postA, node) || SamePost(row.postB, node))) return true;
            return false;
        }

        /// The nodes of every built wall tower, for `CampPath`'s wall layer.
        public void WallTowerNodes(List<Vector3> into)
        {
            into.Clear();
            for (int i = 0; i < built.Count; i++)
                if (IsWallTower(built[i])) into.Add(built[i].transform.position);
        }

        /// **A tower came or went: re-settle the wall.** The chain redraws
        /// the posts and the runs' ends next `LateUpdate`; the path grid
        /// re-lays its wall layer now.
        public void TouchWallTowers()
        {
            var chain = WallChain.Of(transform);
            if (chain != null) chain.MarkDirty();
            var map = GetComponent<CampPath>();
            if (map != null) map.RelayWallLayer();
        }

        /// **Where the lookout stands at a wall tower: on the camp side.**
        /// The direction nearest "toward the fire" that is at least 30° off
        /// every run leaving the node and on the fire's side of each, just
        /// outside the footprint -- so the man is never put on the wall line
        /// or outside the ring. Falls back to toward-the-fire.
        public Vector3 WallTowerDoor(Building b)
        {
            Vector3 node = b.transform.position;
            Vector3 toFire = CampCentre - node;
            toFire.y = 0f;
            if (toFire.sqrMagnitude < 0.01f) toFire = Vector3.forward;
            toFire.Normalize();

            var runs = new List<Vector3>(4);
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                if (w == null) continue;
                if (SamePost(w.A, node)) runs.Add(WallVisual.Flat(w.B - w.A).normalized);
                else if (SamePost(w.B, node)) runs.Add(WallVisual.Flat(w.A - w.B).normalized);
            }

            var plan = BuildPlans.Named(b.Id);
            float reach = 0.6f + 0.5f * Mathf.Max(plan.footprint.x, plan.footprint.y);
            Vector3 bestDir = toFire;
            float bestAngle = float.MaxValue;
            const int Dirs = 32;
            for (int k = 0; k < Dirs; k++)
            {
                float t = k * Mathf.PI * 2f / Dirs;
                var d = new Vector3(Mathf.Sin(t), 0f, Mathf.Cos(t));
                bool ok = true;
                for (int r = 0; r < runs.Count && ok; r++)
                {
                    if (Vector3.Angle(d, runs[r]) < 30f) ok = false;
                    float sideFire = Vector3.Cross(runs[r], toFire).y;
                    float sideDoor = Vector3.Cross(runs[r], d).y;
                    if (Mathf.Abs(sideFire) > 0.05f && sideFire * sideDoor < 0f) ok = false;
                }
                if (!ok) continue;
                float ang = Vector3.Angle(d, toFire);
                if (ang < bestAngle) { bestAngle = ang; bestDir = d; }
            }
            Vector3 spot = node + bestDir * reach;
            spot.y = GroundAt(spot);
            return spot;
        }
    }
}
