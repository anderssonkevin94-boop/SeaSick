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

        /// A lane's outside end keeps this far off every box.
        const float LaneExitClear = 0.3f;

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

        /// `via`: a stand's way in through its own building's approach
        /// marker (`hasVia`), walked between `exit` and `at`.
        struct Lane { public Vector3 at, exit, via; public bool hasVia; }

        readonly List<Box> boxes = new List<Box>();
        readonly List<Group> groups = new List<Group>();
        readonly List<Lane> lanes = new List<Lane>();
        readonly List<Vector3> accessPts = new List<Vector3>();
        /// Per access point: its building's group, and whether it is the
        /// stand / the approach marker (`ViaApproach`).
        readonly List<int> accessGroup = new List<int>();
        readonly List<byte> accessKind = new List<byte>();
        readonly List<Vector4> localScratch = new List<Vector4>();
        readonly List<Vector4> markerScratch = new List<Vector4>();
        readonly List<int> markerGroup = new List<int>();
        /// Markers too close to a box to stand at, and their free spots.
        readonly List<Vector3> freeAt = new List<Vector3>();
        readonly List<Vector3> freeTo = new List<Vector3>();
        /// Every access marker, as read (`RebuildSolids`): the free spots
        /// are worked out again from these when a wall changes.
        readonly List<Vector3> markerAll = new List<Vector3>();
        /// The `WallRevision` the free spots (marker cache and ring) were
        /// worked out under.
        int freeWallRev = -1;

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
                // The model itself, not just "has one": a level 2 swap
                // (`BuildingFactory.ShowLevel`) re-reads the boxes and relays.
                var model = t.Find("Model");
                sig = sig * 31 + (model != null ? model.GetInstanceID() : 0);
                // The fire's store cache (2026-10-03): stood, moved or
                // taken off after the fire itself, and it carries a box.
                var cache = t.Find(BuildingFactory.FireCacheChild);
                sig = sig * 31 + (cache != null ? cache.GetInstanceID() : 0);
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
            accessGroup.Clear();
            accessKind.Clear();
            freeAt.Clear();
            freeTo.Clear();
            markerAll.Clear();
            markerScratch.Clear();
            markerGroup.Clear();
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

                // Access markers: the spots a worker walks to here. Read
                // after every box is down (below): a marker can sit in a
                // building raised later in the list.
                foreach (var m in b.GetComponentsInChildren<Transform>(true))
                {
                    string stem = BuildingFactory.Stem(m.name);
                    if (System.Array.IndexOf(BuildingSolids.AccessStems, stem) < 0) continue;
                    markerScratch.Add(new Vector4(m.position.x, m.position.y, m.position.z,
                        stem == "Worker_Stand" ? 1 : stem == "Worker_Approach" ? 2 : 0));
                    markerGroup.Add(groups.Count - 1);
                }
            }

            // One inside a box (a shell's unused `Entry`) is not a spot. Every
            // marker too close to a box or a wall to stand at (a stand 5 cm
            // into its own bench, a pickup 7 cm off the counter, a spot 30 cm
            // off the palisade) keeps its free spot (`FreeSpot`), worked out
            // once (`RefreshFreeSpots`), not per route.
            for (int i = 0; i < markerScratch.Count; i++)
            {
                var m = markerScratch[i];
                var p = new Vector3(m.x, m.y, m.z);
                markerAll.Add(p);
                if (DistToSolids(p.x, p.z) < 0f) continue;
                accessPts.Add(p);
                accessGroup.Add(markerGroup[i]);
                accessKind.Add((byte)m.w);
            }
            RefreshFreeSpots();
            markerScratch.Clear();
            markerGroup.Clear();
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

        /// Outward unit direction of a box's distance at a point outside it
        /// (world, flat): off the face he is beside, or out of the corner's
        /// rounding.
        static void Grad(in Box b, float x, float z, out float gx, out float gz)
        {
            Local(b, x, z, out float lx, out float lz);
            float cx = Mathf.Clamp(lx, -b.hx, b.hx), cz = Mathf.Clamp(lz, -b.hz, b.hz);
            float ox = lx - cx, oz = lz - cz;
            float n = Mathf.Sqrt(ox * ox + oz * oz);
            if (n < 1e-6f)
            {
                // On the face itself: its normal.
                if (Mathf.Abs(lx) - b.hx > Mathf.Abs(lz) - b.hz) { ox = lx >= 0f ? 1f : -1f; oz = 0f; }
                else { ox = 0f; oz = lz >= 0f ? 1f : -1f; }
            }
            else { ox /= n; oz /= n; }
            gx = ox * b.ux + oz * b.vx;
            gz = ox * b.uz + oz * b.vz;
        }

        /// **True distance from a flat segment to a box** (round corners,
        /// like `SignedDist`), for a segment that does not cross it: the
        /// nearer of its two ends, or of the box's four corners to it.
        /// `SegHits` with a margin grows the box with SQUARE corners, so a
        /// man 0.155 m off a bench's corner read as inside the 0.15 m
        /// clearance and every step he could take was refused (the
        /// kitchen's pickup, 2026-10-04).
        static float SegDist(in Box b, float px, float pz, float qx, float qz)
        {
            Local(b, px, pz, out float ax, out float az);
            Local(b, qx, qz, out float bx, out float bz);
            float m = Mathf.Min(LocalDist(b, ax, az), LocalDist(b, bx, bz));
            float dx = bx - ax, dz = bz - az, ll = dx * dx + dz * dz;
            if (ll < 1e-12f) return m;
            for (int c = 0; c < 4; c++)
            {
                float cx = (c & 1) == 0 ? b.hx : -b.hx, cz = (c & 2) == 0 ? b.hz : -b.hz;
                float t = Mathf.Clamp01(((cx - ax) * dx + (cz - az) * dz) / ll);
                float ex = ax + t * dx - cx, ez = az + t * dz - cz;
                m = Mathf.Min(m, Mathf.Sqrt(ex * ex + ez * ez));
            }
            return m;
        }

        static float LocalDist(in Box b, float lx, float lz)
        {
            float ex = Mathf.Abs(lx) - b.hx, ez = Mathf.Abs(lz) - b.hz;
            if (ex <= 0f && ez <= 0f) return Mathf.Max(ex, ez);
            float ox = ex > 0f ? ex : 0f, oz = ez > 0f ? ez : 0f;
            return Mathf.Sqrt(ox * ox + oz * oz);
        }

        /// The step guard, for one camp. See `Obstructs`.
        bool SolidStepBlocks(Vector3 p, Vector3 q, Vector3 goal, float r, out Vector3 along)
            => SolidStepBlocks(p, q, goal, r, out along, out _, out _);

        /// `faceU`/`faceV`: the blocking box's two face directions (both
        /// tangents a man at its corner may slide along).
        bool SolidStepBlocks(Vector3 p, Vector3 q, Vector3 goal, float r, out Vector3 along,
            out Vector3 faceU, out Vector3 faceV)
        {
            along = default;
            faceU = default;
            faceV = default;
            SyncSolids();
            if (groups.Count == 0) return false;
            float mx = 0.5f * (p.x + q.x), mz = 0.5f * (p.z + q.z);
            float sx = q.x - p.x, sz = q.z - p.z;
            float len = Mathf.Sqrt(sx * sx + sz * sz);
            float half = 0.5f * len;
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
                    else if (d0 >= r)
                        hit = SegHits(b, p.x, p.z, q.x, q.z, r) && SegDist(b, p.x, p.z, q.x, q.z) < r - 1e-4f;
                    else
                    {
                        // **Already within the clearance: refused if it comes
                        // any closer, anywhere along it (2026-10-04).** The
                        // distance to a box along a straight line is convex,
                        // so the line comes closer than `d0` somewhere iff it
                        // starts by closing in. The old endpoint test passed
                        // a long leg that grazed the bench a man stood
                        // beside and came away again (`NextCorner`'s 6 m
                        // shortcut walked him through it).
                        Grad(b, p.x, p.z, out float gx, out float gz);
                        hit = len > 1e-6f && (gx * sx + gz * sz) / len < -0.02f;
                    }
                    if (!hit) continue;
                    // Slide along the box where he stands: the face's tangent,
                    // or round the corner's rounding -- never into the
                    // neighbouring box the old `ex > ez` face pick chose.
                    Grad(b, p.x, p.z, out float tx, out float tz);
                    along = new Vector3(-tz, 0f, tx);
                    faceU = new Vector3(b.ux, 0f, b.uz);
                    faceV = new Vector3(b.vx, 0f, b.vz);
                    return true;
                }
            }
            return false;
        }

        /// Does the flat segment pass INTO a box (no clearance)? Boxes `p` is
        /// inside of do not count. The escape's test: in clutter every
        /// stride closes on something, and only going through one is wrong.
        bool SolidPenetrates(Vector3 p, Vector3 q)
        {
            SyncSolids();
            float mx = 0.5f * (p.x + q.x), mz = 0.5f * (p.z + q.z);
            float half = 0.5f * Mathf.Sqrt((q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z));
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var g = groups[gi];
                float dx = mx - g.cx, dz = mz - g.cz;
                float reach = g.rad + half;
                if (dx * dx + dz * dz > reach * reach) continue;
                for (int k = g.first; k < g.first + g.count; k++)
                {
                    var b = boxes[k];
                    if (SignedDist(b, p.x, p.z) < 0f) continue;
                    if (SegHits(b, p.x, p.z, q.x, q.z, 0f)) return true;
                }
            }
            return false;
        }

        // --- free ground beside a marker (2026-10-04) ------------------------

        /// Metres past `SolidClearance` a free spot keeps off every box, and
        /// past `WallClearance` off every wall.
        const float FreeMargin = 0.05f;

        /// **The nearest point a body can actually stand at** near `p`:
        /// `p` itself when it is `SolidClearance + FreeMargin` off every
        /// box and `WallClearance + FreeMargin` off every wall, else the
        /// closest point that is, on `p`'s side of every wall and reachable
        /// from `p` without going through a box or a wall. Markers drawn
        /// into or against their own bench (the grain mill's stand 5 cm
        /// inside, the kitchen's stand 7 cm off its counter), against the
        /// palisade (the watchtower's output spot, 30 cm off it) and every
        /// fallback spot (`EdgeBeyond`, a bay a pace off a wall) are planned
        /// to this, so the last leg ends where the step guard lets a man
        /// stand. Markers are cached (`RefreshFreeSpots`); anything else
        /// costs a distance read unless it is in the clearance. The caches
        /// follow the buildings (`SolidRevision`) and the walls
        /// (`WallRevision`).
        public Vector3 FreeSpot(Vector3 p)
        {
            SyncSolids();
            var walls = camp != null ? camp.Walls : null;
            if (!TooClose(walls, p)) return p;
            if (freeWallRev != WallRevision) RefreshFreeSpots();
            for (int i = 0; i < freeAt.Count; i++)
                if (Flat2(freeAt[i], p) < 1e-4f) return freeTo[i];
            // Anything else in the clearance (an `EdgeBeyond` spot against
            // a neighbour's bench) is asked for every frame its errand
            // walks: the last few answers are kept.
            for (int i = 0; i < FreeRing; i++)
                if (freeQRev[i] == SolidRevision && freeQWall[i] == WallRevision
                    && Flat2(freeQAt[i], p) < 1e-4f) return freeQTo[i];
            Vector3 f = FindFreeSpot(p);
            freeQAt[freeQNext] = p;
            freeQTo[freeQNext] = f;
            freeQRev[freeQNext] = SolidRevision;
            freeQWall[freeQNext] = WallRevision;
            freeQNext = (freeQNext + 1) % FreeRing;
            return f;
        }

        const int FreeRing = 8;
        readonly Vector3[] freeQAt = new Vector3[FreeRing], freeQTo = new Vector3[FreeRing];
        readonly int[] freeQRev = { -1, -1, -1, -1, -1, -1, -1, -1 };
        readonly int[] freeQWall = { -1, -1, -1, -1, -1, -1, -1, -1 };
        int freeQNext;

        public static Vector3 FreeSpot(Outpost camp, Vector3 p)
        {
            var map = camp != null ? For(camp) : null;
            return map != null ? map.FreeSpot(p) : p;
        }

        /// **A roomier spot beside a cramped one** (2026-10-04, Kevin: the
        /// kitchen runner "walks into this, steps back and walks in again";
        /// `CampWorker.RoomySpot` for a hauler's bay spot). On rings 0.5 / 0.7 / 0.9 m round
        /// `target`, 16 bearings each: a spot well clear of every box
        /// (`AltClearance`), reachable from the camp, within arm's reach of
        /// the target (no box and no standing wall on the line between them)
        /// and more than 0.5 m from `failedAt` (pass a far point to skip that). The `nth` such spot,
        /// nearest ring first and the roomiest of a ring first, so a second
        /// try is a different spot.
        public static bool AltSpot(Outpost camp, Vector3 target, Vector3 failedAt, int nth, out Vector3 alt)
            => AltSpot(camp, target, failedAt, nth, out alt, null);

        /// The same, never closer to `outsideOf` (a building's middle) than
        /// `target` is: the spot beside a bay stays on the yard side and never
        /// steps in through a door (the store hut's open front, 2026-10-04).
        public static bool AltSpot(Outpost camp, Vector3 target, Vector3 failedAt, int nth, out Vector3 alt, Vector3? outsideOf)
        {
            alt = target;
            var map = camp != null ? For(camp) : null;
            if (map == null) return false;
            var found = new List<(float r, float clear, Vector3 p)>();
            for (int ring = 0; ring < AltRings.Length; ring++)
            {
                float r = AltRings[ring];
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    var c = new Vector3(target.x + Mathf.Cos(a) * r, target.y, target.z + Mathf.Sin(a) * r);
                    float clear = map.SolidDistance(c);
                    if (clear < AltClearance) continue;
                    if (FlatLen(c - failedAt) < 0.5f) continue;
                    if (outsideOf.HasValue && FlatLen(c - outsideOf.Value) < FlatLen(target - outsideOf.Value) - 0.05f) continue;
                    if (!map.Reachable(c)) continue;
                    if (SolidBetween(camp, c, target, target) || Crosses(camp, c, target, Walker.Hand)) continue;
                    found.Add((r, clear, c));
                }
            }
            if (found.Count == 0) return false;
            found.Sort((x, y) => x.r != y.r ? x.r.CompareTo(y.r) : y.clear.CompareTo(x.clear));
            if (nth >= found.Count) return false;
            alt = found[nth].p;
            return true;
        }

        // Inside a metre: arm's reach of a bay, and inside `CampWorker`'s
        // `Near(home, 1f)` so a gatherer coming home still counts as in.
        static readonly float[] AltRings = { 0.5f, 0.7f, 0.9f };
        /// Metres an alternative spot keeps off every box: more than a free
        /// spot's, so the last step in has room.
        const float AltClearance = 0.45f;
        static float FlatLen(Vector3 d) { d.y = 0f; return d.magnitude; }

        /// Is `p` in the clearance of a box or a wall?
        bool TooClose(IReadOnlyList<WallSegment> walls, Vector3 p)
            => DistToSolids(p.x, p.z) < SolidClearance + FreeMargin
               || NearWall(walls, p.x, p.z, WallClearance + FreeMargin);

        /// The free spot of every marker in a clearance, again: the markers
        /// are the same, the walls (or the boxes) are not.
        void RefreshFreeSpots()
        {
            freeWallRev = WallRevision;
            freeAt.Clear();
            freeTo.Clear();
            var walls = camp != null ? camp.Walls : null;
            for (int i = 0; i < markerAll.Count; i++)
            {
                var p = markerAll[i];
                if (!TooClose(walls, p)) continue;
                freeAt.Add(p);
                freeTo.Add(FindFreeSpot(p));
            }
        }

        /// Is a standing wall of the list closer than `r` to the point? Every
        /// segment counts as it stands for a body that is not a hand: a gate
        /// is shut to him (`WallsBlock`'s notion), a breached piece is gone.
        /// A box reject first, as `PieceBlocks` does.
        static bool NearWall(IReadOnlyList<WallSegment> walls, float x, float z, float r)
        {
            if (walls == null) return false;
            for (int k = 0; k < walls.Count; k++)
            {
                var w = walls[k];
                if (w == null || w.Breached) continue;
                Vector3 a = w.A, b = w.B;
                if (x < (a.x < b.x ? a.x : b.x) - r || x > (a.x > b.x ? a.x : b.x) + r) continue;
                if (z < (a.z < b.z ? a.z : b.z) - r || z > (a.z > b.z ? a.z : b.z) + r) continue;
                if (PointSegDist(x, z, a.x, a.z, b.x, b.z) < r) return true;
            }
            return false;
        }

        /// Does the straight line from `p` to `c` cross a standing wall,
        /// gates shut? `WallsBlock` lets a walker already within
        /// `WallEmbedded` of a wall out through it; a spot must not be put
        /// on the far side of one that way, so this has no such grace.
        static bool CrossesWall(IReadOnlyList<WallSegment> walls, Vector3 p, Vector3 c)
        {
            if (walls == null) return false;
            for (int k = 0; k < walls.Count; k++)
            {
                var w = walls[k];
                if (w == null || w.Breached) continue;
                Vector3 a = w.A, b = w.B;
                float c1 = Cross2(a.x, a.z, b.x, b.z, p.x, p.z), c2 = Cross2(a.x, a.z, b.x, b.z, c.x, c.z);
                float c3 = Cross2(p.x, p.z, c.x, c.z, a.x, a.z), c4 = Cross2(p.x, p.z, c.x, c.z, b.x, b.z);
                if ((c1 > 0f) != (c2 > 0f) && (c3 > 0f) != (c4 > 0f)) return true;
            }
            return false;
        }

        /// Rings of 16 every 10 cm out to 1.5 m; the first ring with a spot
        /// wins, and in it the one furthest off the boxes (to 2 m), a spot
        /// he can walk to without closing on any box first. A spot keeps
        /// `WallClearance + FreeMargin` off every wall and stays on `p`'s
        /// side of them.
        Vector3 FindFreeSpot(Vector3 p)
        {
            Vector3 best = p;
            var walls = camp != null ? camp.Walls : null;
            for (int ring = 1; ring <= 15; ring++)
            {
                float rr = 0.1f * ring, bestScore = float.MinValue;
                for (int h = 0; h < 16; h++)
                {
                    float a = h * Mathf.PI / 8f;
                    var c = new Vector3(p.x + Mathf.Sin(a) * rr, p.y, p.z + Mathf.Cos(a) * rr);
                    float dc = DistToSolids(c.x, c.z);
                    if (dc < SolidClearance + FreeMargin) continue;
                    if (NearWall(walls, c.x, c.z, WallClearance + FreeMargin)) continue;
                    if (SolidPenetrates(p, c)) continue;
                    if (CrossesWall(walls, p, c)) continue;
                    float score = Mathf.Min(dc, 2f) + (SolidStepBlocks(p, c, FarGoal, SolidClearance, out _) ? 0f : 1f);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = c;
                }
                if (bestScore > float.MinValue)
                {
                    if (camp != null) best.y = camp.GroundAt(best);
                    return best;
                }
            }
            return p;
        }

        static readonly Vector3 FarGoal = new Vector3(1e9f, 0f, 1e9f);

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

        /// `Obstructs`, also handing back the blocking box's two face
        /// directions (zero when a wall blocked): the slide candidates at a
        /// box's corner (`CampWorker.Walk`).
        public static bool Obstructs(Outpost camp, Vector3 from, Vector3 to, Vector3 goal,
            Walker who, out Vector3 along, out Vector3 faceU, out Vector3 faceV)
        {
            faceU = default;
            faceV = default;
            if (Blocks(camp, from, to, who, WallClearance, out along)) return true;
            if (camp == null) return false;
            var map = For(camp);
            return map != null && map.SolidStepBlocks(from, to, goal, SolidClearance, out along, out faceU, out faceV);
        }

        /// A stride that goes INTO a box or across a wall (no clearance):
        /// what an escape out of a wedge may not do (`CampWorker.PickEscape`).
        public static bool Penetrates(Outpost camp, Vector3 from, Vector3 to)
        {
            if (camp == null) return false;
            if (Crosses(camp, from, to, Walker.Hand)) return true;
            var map = For(camp);
            return map != null && map.SolidPenetrates(from, to);
        }

        /// Signed distance to the nearest building box (big when none).
        public static float SolidDistance(Outpost camp, Vector3 p)
        {
            var map = camp != null ? For(camp) : null;
            return map != null ? map.SolidDistance(p) : 1e9f;
        }

        /// **A spot worth coming back to** (`CampWorker`'s breadcrumb): a
        /// hand-walkable cell, 0.3 m+ off every box.
        public static bool CrumbSpot(Outpost camp, Vector3 p)
        {
            var map = camp != null ? For(camp) : null;
            if (map == null || !map.built || map.hs == null) return false;
            if (map.SolidDistance(p) < 0.3f) return false;
            int i = map.Index(p);
            if (i < 0) return false;
            map.mask = BlockHand;
            return map.Walk(i);
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
                        float dq = DistToSolids(qx, qz);
                        bool inSolid = dq < SolidClearance;
                        cost += inSolid ? 10f : 1f;
                        if (cost >= bestCost) break;
                        // **The exit is somewhere to stand (2026-10-04)**: a
                        // comfortable 0.3 m off every box, not the first
                        // point 0.15 m off one.
                        if (inSolid || dq < LaneExitClear) continue;
                        int c = Index(new Vector3(qx, 0f, qz));
                        if (c < 0) break;
                        if (bldg[c] == 0 && open[c])
                        {
                            // And a lane the step guard will walk: one that
                            // closes on a box on its way out costs extra.
                            var q = new Vector3(qx, p.y, qz);
                            if (SolidStepBlocks(p, q, FarGoal, SolidClearance, out _)) cost += 20f;
                            if (cost < bestCost)
                            {
                                bestCost = cost;
                                bestExit = q;
                            }
                            break;
                        }
                    }
                }
                if (bestCost == float.MaxValue) continue;
                if (camp != null) bestExit.y = camp.GroundAt(bestExit);
                lanes.Add(new Lane { at = p, exit = bestExit });
            }
            ViaApproach();
        }

        /// **A stand is entered by its building's approach (2026-10-01).**
        /// The shortest-way-out lane is right for a pickup on a building's
        /// edge, wrong for a cook's stand tucked behind the counter: the
        /// kitchen's stand is 7 cm off the counter box and its cheapest
        /// heading ran out through the counter, so the route brought him
        /// round to the far side and the step guard (rightly) would not let
        /// him through -- he paced up and down 2 m short. Where the same
        /// building has a `Worker_Approach` the stand can see without
        /// passing through a box, the stand's lane runs stand -> approach ->
        /// the approach's own way out, which is the walk the building was
        /// drawn for.
        void ViaApproach()
        {
            for (int i = 0; i < lanes.Count; i++)
            {
                int si = accessPts.IndexOf(lanes[i].at);
                if (si < 0 || accessKind[si] != 1) continue;
                for (int j = 0; j < accessPts.Count; j++)
                {
                    if (accessKind[j] != 2 || accessGroup[j] != accessGroup[si]) continue;
                    Vector3 st = accessPts[si], ap = accessPts[j];
                    float len = Mathf.Sqrt((ap.x - st.x) * (ap.x - st.x) + (ap.z - st.z) * (ap.z - st.z));
                    if (len > 3f || len < 0.05f) continue;
                    bool clear = true;
                    for (int k = 1; k <= 8 && clear; k++)
                    {
                        float t = k / 8f;
                        if (DistToSolids(Mathf.Lerp(st.x, ap.x, t), Mathf.Lerp(st.z, ap.z, t)) < 0f) clear = false;
                    }
                    if (!clear) continue;
                    // The approach's own way out, or the approach itself
                    // when it already stands on open ground.
                    Vector3 exit = ap;
                    for (int k = 0; k < lanes.Count; k++)
                        if ((lanes[k].at - ap).sqrMagnitude < 1e-6f) { exit = lanes[k].exit; break; }
                    var l = lanes[i];
                    l.via = ap; l.hasVia = true; l.exit = exit;
                    lanes[i] = l;
                    break;
                }
            }
        }

        /// The lane whose marker is within `within` of `p`, if any.
        bool LaneAt(Vector3 p, float within, out Vector3 exit) => LaneAt(p, within, out exit, out _, out _);

        bool LaneAt(Vector3 p, float within, out Vector3 exit, out Vector3 via, out bool hasVia)
        {
            exit = p;
            via = p;
            hasVia = false;
            float best = within * within;
            bool found = false;
            for (int i = 0; i < lanes.Count; i++)
            {
                float dx = lanes[i].at.x - p.x, dz = lanes[i].at.z - p.z;
                float d = dx * dx + dz * dz;
                if (d > best) continue;
                best = d;
                exit = lanes[i].exit;
                via = lanes[i].via;
                hasVia = lanes[i].hasVia;
                found = true;
            }
            // A marker already outside every building's cells needs no lane --
            // unless the short way out is itself refused (2026-10-04).
            if (found && !hasVia && (exit - p).sqrMagnitude < 0.09f && SolidLineClear(p, exit)) return false;
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
