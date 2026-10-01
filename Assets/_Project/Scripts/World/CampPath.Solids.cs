using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Buildings are obstacles (2026-10-01).** Kevin: *"buildings don't have
    /// a collider and villagers walk straight through them. give them
    /// hitboxes that make sense while still allowing workers to get to and
    /// from their stations / collecting / drop off points."*
    ///
    /// Each standing building contributes a few flat boxes
    /// (`BuildingSolids`: its benches, racks, hearths, posts, a hut's
    /// walls), and they reach the walkers two ways, exactly like the walls:
    ///
    ///  - **the grid** (`bldg`, a layer of its own, re-laid whole on any
    ///    change): a cell is closed to everybody when its centre is within
    ///    `SolidGridInflate` of a box, which shuts a building's whole
    ///    footprint (a 2 m cell cannot tell the gap between two benches from
    ///    a bench) -- then every access marker (`Worker_Stand`,
    ///    `Input_Pickup`, ...) gets a LANE: the straightest way out to an
    ///    open cell that crosses the fewest boxes, measured on the boxes
    ///    themselves, and `Route` plans from / to the lane's outside end and
    ///    walks the lane as its first / last leg. So a sawyer leaves the
    ///    mill by its rear gate and a hauler reaches the kitchen's pickup
    ///    round the counter, not through it, and no marker is ever sealed;
    ///  - **the step guard** (`Obstructs`, from `CampWorker.Walk`): the
    ///    boxes themselves, as boxes, `SolidClearance` off -- slide along the
    ///    face, never through. A box the walker is already inside is ignored
    ///    (so a building raised over a man never traps him; `PushOut` then
    ///    stands him outside it), and so is a box his errand's own spot is
    ///    inside of (never seals a marker).
    ///
    /// Re-laid whenever the camp's buildings change (`SyncSolids`: a cheap
    /// signature over `camp.Built`, read at most once a frame), so a raise,
    /// a move, a turn or a demolish needs no hook. Nothing here is saved,
    /// and buildings never move for it -- only bodies do.
    public partial class CampPath
    {
        /// Metres a walker's centre keeps off a building's box.
        public static float SolidClearance = 0.15f;

        /// A grid cell is closed when its centre is this close to a box:
        /// half a cell, so no box, however small, slips between centres.
        public static float SolidGridInflate = 1.0f;

        struct Box
        {
            public float cx, cz;      // centre, world
            public float ux, uz;      // building's local X, flat, world
            public float vx, vz;      // building's local Z, flat, world
            public float hx, hz;      // half extents
        }

        struct Group { public float cx, cz, rad; public int first, count; }

        struct Lane { public Vector3 at, exit; }

        readonly List<Box> boxes = new List<Box>();
        readonly List<Group> groups = new List<Group>();
        readonly List<Lane> lanes = new List<Lane>();
        readonly List<Vector3> accessPts = new List<Vector3>();
        readonly List<Vector4> localScratch = new List<Vector4>();

        /// Per-cell: blocked by a building (both walkers).
        byte[] bldg;

        int solidFrame = -1;
        long solidSig = long.MinValue;
        bool solidsReady;

        /// Bumped every time the buildings' boxes change. A body compares
        /// it to decide whether to check it is standing inside one.
        public int SolidRevision { get; private set; }

        /// Boxes standing now (for checks).
        public int SolidCount { get { SyncSolids(); return boxes.Count; } }
        /// Lanes kept open now (for checks).
        public int LaneCount => lanes.Count;

        /// Force a re-read now (an edit-mode probe, or a caller that just
        /// moved a building and asks in the same frame).
        public void ResyncSolids() { solidFrame = -1; solidSig = long.MinValue; SyncSolids(); }

        /// The revision, synced first.
        public int SolidRevisionNow() { SyncSolids(); return SolidRevision; }

        void SyncSolids()
        {
            int f = Time.frameCount;
            if (solidsReady && f == solidFrame) return;
            solidFrame = f;
            if (camp == null) return;
            long sig = 17;
            var list = camp.Built;
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                if (b == null) { sig = sig * 31 + 7; continue; }
                var t = b.transform;
                Vector3 p = t.position;
                sig = sig * 31 + b.GetInstanceID();
                sig = sig * 31 + Mathf.RoundToInt(p.x * 50f);
                sig = sig * 31 + Mathf.RoundToInt(p.z * 50f);
                sig = sig * 31 + Mathf.RoundToInt(t.eulerAngles.y * 10f);
                sig = sig * 31 + (t.Find("Model") != null ? 1 : 0);
            }
            if (solidsReady && sig == solidSig) return;
            solidSig = sig;
            solidsReady = true;
            RebuildSolids();
            SolidRevision++;
            if (built && hs != null) RelayBuildings();
        }

        void RebuildSolids()
        {
            boxes.Clear();
            groups.Clear();
            accessPts.Clear();
            var list = camp.Built;
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                if (b == null) continue;
                if (BuildingSolids.LocalBoxes(b, localScratch) == 0) continue;
                var t = b.transform;
                Vector3 r = t.right; r.y = 0f;
                Vector3 fw = t.forward; fw.y = 0f;
                if (r.sqrMagnitude < 1e-6f || fw.sqrMagnitude < 1e-6f) continue;
                r.Normalize(); fw.Normalize();
                Vector3 s = t.lossyScale;
                float sx = Mathf.Abs(s.x), sz = Mathf.Abs(s.z);
                Vector3 o = t.position;
                var g = new Group { cx = o.x, cz = o.z, first = boxes.Count };
                float rad = 0f;
                for (int k = 0; k < localScratch.Count; k++)
                {
                    var lb = localScratch[k];
                    float lx = lb.x * sx, lz = lb.y * sz;
                    var bx = new Box
                    {
                        cx = o.x + r.x * lx + fw.x * lz,
                        cz = o.z + r.z * lx + fw.z * lz,
                        ux = r.x, uz = r.z, vx = fw.x, vz = fw.z,
                        hx = lb.z * sx, hz = lb.w * sz,
                    };
                    boxes.Add(bx);
                    float dx = bx.cx - o.x, dz = bx.cz - o.z;
                    rad = Mathf.Max(rad, Mathf.Sqrt(dx * dx + dz * dz) + Mathf.Sqrt(bx.hx * bx.hx + bx.hz * bx.hz));
                }
                g.rad = rad;
                g.count = boxes.Count - g.first;
                groups.Add(g);

                // Access markers: the spots a worker walks to here. One
                // inside a box (a shell's unused `Entry`) is not a spot.
                foreach (var m in b.GetComponentsInChildren<Transform>(true))
                {
                    if (System.Array.IndexOf(BuildingSolids.AccessStems, BuildingFactory.Stem(m.name)) < 0) continue;
                    Vector3 p = m.position;
                    if (DistToSolids(p.x, p.z) < 0f) continue;
                    accessPts.Add(p);
                }
            }
        }

        // --- geometry ------------------------------------------------------

        static void Local(in Box b, float x, float z, out float lx, out float lz)
        {
            float dx = x - b.cx, dz = z - b.cz;
            lx = dx * b.ux + dz * b.uz;
            lz = dx * b.vx + dz * b.vz;
        }

        /// Signed flat distance from a point to a box: negative inside.
        static float SignedDist(in Box b, float x, float z)
        {
            Local(b, x, z, out float lx, out float lz);
            float ex = Mathf.Abs(lx) - b.hx, ez = Mathf.Abs(lz) - b.hz;
            if (ex <= 0f && ez <= 0f) return Mathf.Max(ex, ez);
            float ox = ex > 0f ? ex : 0f, oz = ez > 0f ? ez : 0f;
            return Mathf.Sqrt(ox * ox + oz * oz);
        }

        /// Does the flat segment p-q touch the box grown by `r`? (Slab test
        /// in the box's frame; square corners.)
        static bool SegHits(in Box b, float px, float pz, float qx, float qz, float r)
        {
            Local(b, px, pz, out float ax, out float az);
            Local(b, qx, qz, out float bx, out float bz);
            float hx = b.hx + r, hz = b.hz + r;
            float t0 = 0f, t1 = 1f;
            float dx = bx - ax, dz = bz - az;
            if (!Slab(ax, dx, hx, ref t0, ref t1)) return false;
            if (!Slab(az, dz, hz, ref t0, ref t1)) return false;
            return t0 <= t1;
        }

        static bool Slab(float a, float d, float h, ref float t0, ref float t1)
        {
            if (Mathf.Abs(d) < 1e-7f) return a >= -h && a <= h;
            float i0 = (-h - a) / d, i1 = (h - a) / d;
            if (i0 > i1) { float tmp = i0; i0 = i1; i1 = tmp; }
            if (i0 > t0) t0 = i0;
            if (i1 < t1) t1 = i1;
            return t0 <= t1;
        }

        /// Nearest box's signed distance (big when none).
        float DistToSolids(float x, float z)
        {
            float best = 1e9f;
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var g = groups[gi];
                float dx = x - g.cx, dz = z - g.cz;
                float far = Mathf.Sqrt(dx * dx + dz * dz) - g.rad;
                if (far > best) continue;
                for (int k = g.first; k < g.first + g.count; k++)
                {
                    var bx = boxes[k];
                    float d = SignedDist(bx, x, z);
                    if (d < best) best = d;
                }
            }
            return best;
        }

        /// The step guard, for one camp. See `Obstructs`.
        bool SolidStepBlocks(Vector3 p, Vector3 q, Vector3 goal, float r, out Vector3 along)
        {
            along = default;
            SyncSolids();
            if (groups.Count == 0) return false;
            float mx = 0.5f * (p.x + q.x), mz = 0.5f * (p.z + q.z);
            float half = 0.5f * Mathf.Sqrt((q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z));
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var g = groups[gi];
                float dx = mx - g.cx, dz = mz - g.cz;
                float reach = g.rad + r + half;
                if (dx * dx + dz * dz > reach * reach) continue;
                for (int k = g.first; k < g.first + g.count; k++)
                {
                    var b = boxes[k];
                    float d0 = SignedDist(b, p.x, p.z);
                    if (d0 < 0f) continue;                                   // inside: let him out
                    if (SignedDist(b, goal.x, goal.z) < 0.05f) continue;     // his spot is in it
                    bool hit;
                    if (SegHits(b, p.x, p.z, q.x, q.z, 0f)) hit = true;      // straight through
                    else if (d0 >= r) hit = SegHits(b, p.x, p.z, q.x, q.z, r);
                    else
                    {
                        // Already within the clearance: only closing in is refused.
                        float d1 = SignedDist(b, q.x, q.z);
                        hit = d1 < d0 && d1 < r;
                    }
                    if (!hit) continue;
                    // Slide along the face he is outside of.
                    Local(b, p.x, p.z, out float lx, out float lz);
                    float ex = Mathf.Abs(lx) - b.hx, ez = Mathf.Abs(lz) - b.hz;
                    along = ex > ez ? new Vector3(b.vx, 0f, b.vz) : new Vector3(b.ux, 0f, b.uz);
                    return true;
                }
            }
            return false;
        }

        // --- the public face ---------------------------------------------

        /// **The step guard: walls, then buildings.** Does a step from
        /// `from` to `to` go through (or close in on) a wall or a building's
        /// box? `goal` is where the walker's errand ends: a box that spot is
        /// inside of does not block him (never seals a marker). `along` is
        /// the direction to slide. Use this where a body steps; `Crosses`
        /// stays walls-only for "is there a wall between" questions.
        public static bool Obstructs(Outpost camp, Vector3 from, Vector3 to, Vector3 goal,
            Walker who, out Vector3 along)
        {
            if (Blocks(camp, from, to, who, WallClearance, out along)) return true;
            if (camp == null) return false;
            var map = For(camp);
            return map != null && map.SolidStepBlocks(from, to, goal, SolidClearance, out along);
        }

        /// Is there a building's box on the straight line from `from` to
        /// `to`? Boxes `goal` is inside of, or `from` is inside of, do not
        /// count. Walls are `Crosses`.
        public static bool SolidBetween(Outpost camp, Vector3 from, Vector3 to, Vector3 goal)
        {
            if (camp == null) return false;
            var map = For(camp);
            return map != null && map.SolidStepBlocks(from, to, goal, SolidClearance, out _);
        }

        /// **Stand a body that is inside a building's box just outside it**
        /// -- a building raised or moved over him, a Hand drop, a save. The
        /// nearest face, plus a little, and never into another box (a few
        /// tries). The building never moves. False when he was not inside.
        public static bool PushOut(Outpost camp, Vector3 at, out Vector3 to)
        {
            to = at;
            if (camp == null) return false;
            var map = For(camp);
            return map != null && map.PushOutOf(at, out to);
        }

        bool PushOutOf(Vector3 at, out Vector3 to)
        {
            to = at;
            SyncSolids();
            bool moved = false;
            for (int pass = 0; pass < 4; pass++)
            {
                int inside = -1;
                float worst = 0f;
                for (int k = 0; k < boxes.Count; k++)
                {
                    float d = SignedDist(boxes[k], to.x, to.z);
                    if (d < worst) { worst = d; inside = k; }
                }
                if (inside < 0) break;
                var b = boxes[inside];
                Local(b, to.x, to.z, out float lx, out float lz);
                float ox = b.hx - Mathf.Abs(lx), oz = b.hz - Mathf.Abs(lz);
                float m = SolidClearance + 0.1f;
                if (ox < oz) lx = Mathf.Sign(lx == 0f ? 1f : lx) * (b.hx + m);
                else lz = Mathf.Sign(lz == 0f ? 1f : lz) * (b.hz + m);
                to.x = b.cx + b.ux * lx + b.vx * lz;
                to.z = b.cz + b.uz * lx + b.vz * lz;
                moved = true;
            }
            return moved;
        }

        // --- the grid layer ------------------------------------------------

        /// **Lay every building on the grid, then carve the lanes.** Cheap:
        /// a clear of one byte array, a few hundred cells marked, a short
        /// march per access marker. Bumps `WallRevision` (the shore-spot
        /// cache keys on it).
        void RelayBuildings()
        {
            if (hs == null) return;
            if (bldg == null || bldg.Length != n * n) bldg = new byte[n * n];
            else System.Array.Clear(bldg, 0, bldg.Length);
            lanes.Clear();
            WallRevision++;
            float inf = SolidGridInflate;
            for (int k = 0; k < boxes.Count; k++)
            {
                var b = boxes[k];
                float ex = Mathf.Abs(b.ux) * b.hx + Mathf.Abs(b.vx) * b.hz + inf;
                float ez = Mathf.Abs(b.uz) * b.hx + Mathf.Abs(b.vz) * b.hz + inf;
                int x0 = Mathf.FloorToInt((b.cx - ex - origin.x) / cell), x1 = Mathf.CeilToInt((b.cx + ex - origin.x) / cell);
                int y0 = Mathf.FloorToInt((b.cz - ez - origin.y) / cell), y1 = Mathf.CeilToInt((b.cz + ez - origin.y) / cell);
                for (int y = Mathf.Max(0, y0); y <= Mathf.Min(n - 1, y1); y++)
                    for (int x = Mathf.Max(0, x0); x <= Mathf.Min(n - 1, x1); x++)
                        if (SignedDist(b, origin.x + x * cell, origin.y + y * cell) <= inf)
                            bldg[y * n + x] = BlockBoth;
            }

            // Lanes: from each access marker, of 16 headings the one that
            // reaches an open, unbuilt cell crossing the fewest boxes, then
            // the shortest. (Nothing is carved: a route plans to the lane's
            // outside end, and the lane itself is walked on the boxes.)
            for (int a = 0; a < accessPts.Count; a++)
            {
                Vector3 p = accessPts[a];
                int pc = Index(p);
                if (pc < 0) continue;
                float bestCost = float.MaxValue;
                Vector3 bestExit = p;
                for (int h = 0; h < 16; h++)
                {
                    float ang = h * Mathf.PI / 8f;
                    float dx = Mathf.Sin(ang), dz = Mathf.Cos(ang);
                    float cost = 0f;
                    for (int s = 1; s <= 48; s++)
                    {
                        float qx = p.x + dx * 0.25f * s, qz = p.z + dz * 0.25f * s;
                        bool inSolid = DistToSolids(qx, qz) < SolidClearance;
                        cost += inSolid ? 10f : 1f;
                        if (cost >= bestCost) break;
                        if (inSolid) continue;
                        int c = Index(new Vector3(qx, 0f, qz));
                        if (c < 0) break;
                        if (bldg[c] == 0 && open[c])
                        {
                            bestCost = cost;
                            bestExit = new Vector3(qx, p.y, qz);
                            break;
                        }
                    }
                }
                if (bestCost == float.MaxValue) continue;
                if (camp != null) bestExit.y = camp.GroundAt(bestExit);
                lanes.Add(new Lane { at = p, exit = bestExit });
            }
        }

        /// The lane whose marker is within `within` of `p`, if any.
        bool LaneAt(Vector3 p, float within, out Vector3 exit)
        {
            exit = p;
            float best = within * within;
            bool found = false;
            for (int i = 0; i < lanes.Count; i++)
            {
                float dx = lanes[i].at.x - p.x, dz = lanes[i].at.z - p.z;
                float d = dx * dx + dz * dz;
                if (d > best) continue;
                best = d;
                exit = lanes[i].exit;
                found = true;
            }
            // A marker already outside every building's cells needs no lane.
            if (found && (exit - p).sqrMagnitude < 0.09f) return false;
            return found;
        }

        /// The straight leg between two route points stays off every box.
        bool SolidLineClear(Vector3 a, Vector3 b)
            => groups.Count == 0 || !SolidStepBlocks(a, b, new Vector3(1e9f, 0f, 1e9f), SolidClearance, out _);

        /// Debug read-out: 6 = cell closed by a building (else `CellKind`).
        public bool BuildingCell(int x, int y) => bldg != null && bldg[y * n + x] != 0;

        /// Debug read-out: the lanes, marker -> exit.
        public void LanesInto(List<Vector3> into)
        {
            into.Clear();
            for (int i = 0; i < lanes.Count; i++) { into.Add(lanes[i].at); into.Add(lanes[i].exit); }
        }

        /// Debug read-out: every box's four corners, flat.
        public void BoxCornersInto(List<Vector3> into)
        {
            SyncSolids();
            into.Clear();
            for (int k = 0; k < boxes.Count; k++)
            {
                var b = boxes[k];
                for (int c = 0; c < 4; c++)
                {
                    float sx = (c == 0 || c == 3) ? -1f : 1f, sz = c < 2 ? -1f : 1f;
                    float lx = sx * b.hx, lz = sz * b.hz;
                    into.Add(new Vector3(b.cx + b.ux * lx + b.vx * lz, 0f, b.cz + b.uz * lx + b.vz * lz));
                }
            }
        }

        /// Signed distance from a point to the nearest box (for checks).
        public float SolidDistance(Vector3 p) { SyncSolids(); return DistToSolids(p.x, p.z); }
    }
}
