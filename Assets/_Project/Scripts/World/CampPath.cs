using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **A walkable map of the ground around one camp, and A* over it.**
    ///
    /// Kevin, on the phone 2026-09-22: *"villager pathfinding seems strange.
    /// they walk in a straight line even when that means walking over a
    /// mountain."* Exactly right, and it was not a bug so much as an absence:
    /// `CampWorker.Walk` stepped straight at its target and took its Y from
    /// `Outpost.GroundAt`, so a hand sent to a tree on the far side of a
    /// ridge climbed the ridge at 2.6 m/s like a goat.
    ///
    /// **Why a height-field grid and NOT a runtime `NavMeshSurface` bake.**
    /// `com.unity.ai.navigation` is installed and the bake was the first
    /// thing tried on paper. Three facts in this project's own terrain code
    /// rule it out:
    ///
    ///  1. **The ground the villagers walk on is not the mesh.** Every point
    ///     a hand is ever placed at comes from `Outpost.GroundAt`, which is
    ///     the analytic field `Island.TerrainHeight(x, z)` — see
    ///     `Outpost.height`, and `Stand`, `ArrangeHands`, `Walk`. A NavMesh
    ///     baked off the drawn mesh would be a *second* opinion about where
    ///     the ground is, and the two disagreeing is precisely the fault the
    ///     GDD records for 2026-08-30 ("the crew were walking 30 metres above
    ///     the island"). This grid asks the same function the walk does, so
    ///     it cannot disagree with it.
    ///
    ///  2. **The collidable mesh is streamed, LOD'd and mostly absent.**
    ///     `TerrainStreamer` keeps `MeshCollider`s only inside
    ///     `settings.colliderRadius` chunks, enables and disables them as the
    ///     ring moves, rebuilds chunk meshes when the LOD target changes and
    ///     marks them dynamic. A surface baked from physics colliders would
    ///     be baked over a moving, re-meshing target and would need
    ///     re-baking every time a chunk changed LOD — on a phone, repeatedly,
    ///     for ground that is *analytically known anyway*.
    ///
    ///  3. **A camp is local.** Hands work within about a hundred metres of
    ///     the fire. The whole island never needs to be navigable; a bounded
    ///     patch around `CampCentre` does, which makes the map small enough
    ///     to build in one go and cheap enough to query many times a second.
    ///
    /// So: one grid per camp, built lazily the first time anybody asks for a
    /// route on that island, sized from the island and capped so the cost is
    /// bounded whatever the island's size. Build time is logged.
    ///
    /// **It lives on the island's own GameObject** (`For` adds it), so it is
    /// destroyed with the island and no static table has to be swept.
    ///
    /// **It never traps anybody.** A blocked start or goal snaps to the
    /// nearest walkable cell; a search that fails or runs past its expansion
    /// cap returns false, and the caller falls back to the old straight line.
    /// Standing still forever is not a state this can produce.
    [DisallowMultipleComponent]
    public class CampPath : MonoBehaviour
    {
        // --- tunables (runtime-added component: these statics ARE the dials) -

        /// Ground steeper than this is not walked up. 38° sits deliberately
        /// between the "sleek hills" the island style aims for (walkable) and
        /// the cliff shards and steep domes (not). Beaches and clearings are
        /// far below it.
        public static float MaxSlopeDegrees = 38f;

        /// Below this is sea, not beach. Generous downward so the waterline
        /// itself and the pier approach stay walkable.
        public static float SeaLevelY = -0.20f;

        /// How much a merely *awkward* slope costs relative to flat ground at
        /// the limit. Keeps routes on the gentle line rather than grazing
        /// every wall they are technically allowed to touch.
        public static float SlopePenalty = 3f;

        /// **Metres per cell, and now an EXACT number rather than a wish
        /// (2026-09-23).** It used to be "desired": the grid stretched to
        /// cover the island and the real cell size fell out of the
        /// division, so it was 3.1 m here and 4.7 m there. Walls cannot
        /// live on a grid like that -- `Outpost.WallPostStep` snaps a post
        /// to a 2 m step and the segment between two posts is rasterised
        /// into cells, so a cell that is not exactly 2 m puts the blocked
        /// cells somewhere the player did not draw the wall. So the cell is
        /// fixed and the EXTENT is what gives (see `Build`).
        public static float DesiredCell = 2f;

        /// Hard cap on the grid's side length in cells. This, not the island,
        /// is what bounds the build cost on a phone. 160 cells of 2 m is a
        /// 320 m square centred on the camp -- more than twice the hundred
        /// metres a hand's errands cover, and 25 600 cells is about 1.3 MB
        /// of arrays and ~25 k height samples to build.
        public static int MaxCells = 160;

        /// Ceiling on A* expansions per query. A route that needs more than
        /// this is a route across the whole island, and the straight-line
        /// fallback is a better answer than a frame spike.
        /// Raised with the cell size: the same hundred-metre walk is two
        /// and a half times as many cells at 2 m as it was at 5 m.
        public static int MaxExpansions = 9000;

        /// Plans allowed to start in one frame, across every camp. Villagers
        /// re-plan on independent timers, so this only ever bites when a
        /// whole camp is re-tasked at once.
        public static int PlansPerFrame = 2;

        public static bool LogBuild = true;

        // --- build cost, for the record ------------------------------------

        public static float LastBuildMs { get; private set; }
        public static int LastBuildCells { get; private set; }
        public static float TotalBuildMs { get; private set; }
        public static int BuildCount { get; private set; }

        /// Frame budget. Shared by every camp, reset on the frame number
        /// changing rather than in an `Update`, so it works without this
        /// component ever ticking.
        static int budgetFrame = -1;
        static int plansThisFrame;

        public static bool Budget()
        {
            int f = Time.frameCount;
            if (f != budgetFrame) { budgetFrame = f; plansThisFrame = 0; }
            if (plansThisFrame >= PlansPerFrame) return false;
            plansThisFrame++;
            return true;
        }

        // --- who is walking ---------------------------------------------------

        /// **Two kinds of feet, one map (2026-09-23).** A gate is open to
        /// the camp's own people and shut to a raiding party, which is the
        /// whole of decision D3 ("gates are automatic") -- so a cell is not
        /// simply blocked, it is blocked FOR SOMEBODY. Rocks and standing
        /// wall block both; a gate blocks only the raider.
        public enum Walker { Hand, Raider }

        const byte BlockHand = 1;
        const byte BlockRaider = 2;
        const byte BlockBoth = BlockHand | BlockRaider;

        static byte MaskFor(Walker who) => who == Walker.Hand ? BlockHand : BlockRaider;

        // --- the grid --------------------------------------------------------

        Outpost camp;
        bool built;

        int n;                  // cells per side
        float cell;             // metres per cell
        Vector2 origin;         // world XZ of cell (0,0)'s CENTRE
        float[] hs;             // ground height per cell
        bool[] open;            // walkable ground (slope and sea only)?
        /// Per-cell block flags -- rocks, walls, gates. Kept apart from
        /// `open` because `open` is a property of the GROUND and is only
        /// ever recomputed by a rebuild, while these are put up and knocked
        /// down all through a raid.
        byte[] block;
        float[] pen;            // 1 + SlopePenalty * (slope/limit)^2

        // A* scratch, allocated once and reused. `stamp` is what lets a query
        // skip clearing twelve thousand floats it will never read.
        float[] g;
        int[] came;
        int[] stamp;
        bool[] closed;
        int search;
        int[] heap;             // cell indices
        float[] heapF;
        int heapCount;

        readonly List<int> cells = new List<int>();

        /// Which block flag the CURRENT query cares about. Set at the top of
        /// every query and read by `Nearest`, `Clear` and `Search`; the
        /// searches are strictly one-at-a-time (single-threaded, no
        /// coroutine yields inside one) so a field is honest here and an
        /// argument threaded through five call sites would only be noise.
        byte mask = BlockHand;

        /// Can this walker stand in this cell? Ground first, then whatever
        /// has been put on top of it.
        bool Walk(int i) => open[i] && (block[i] & mask) == 0;

        /// The pathing map for this camp, added to the island on first ask.
        public static CampPath For(Outpost camp)
        {
            if (camp == null) return null;
            var p = camp.GetComponent<CampPath>();
            if (p == null)
            {
                p = camp.gameObject.AddComponent<CampPath>();
                p.camp = camp;
            }
            else if (p.camp == null) p.camp = camp;
            return p;
        }

        /// Throw the map away so the next ask rebuilds it. For a camp whose
        /// centre has moved a long way, or a dev reset.
        public void Invalidate() => built = false;

        void Build()
        {
            hs = null;

            // **Not built, rather than built empty.** An island that has not
            // been surveyed yet has no height field to read, and latching
            // `built` here would disable routing on it for the rest of the
            // session — the hands would silently go back to walking over
            // mountains the moment the camp was made. Try again next ask;
            // until then the straight line stands, which is today's
            // behaviour.
            if (camp == null || !camp.Sited) return;

            built = true;

            var watch = System.Diagnostics.Stopwatch.StartNew();

            Vector3 c = camp.CampCentre;

            // Cover the island, or a sensible patch of a big one. A hand's
            // errands are local; what is off the map falls back to the old
            // straight line, which is no worse than today.
            var isle = camp.Island;
            float want = isle != null ? isle.MaxRadius + 40f : 160f;
            want = Mathf.Clamp(want, 80f, 260f);

            // **The cell is fixed at 2 m and the EXTENT gives.** A wall post
            // snaps to a 2 m step (`Outpost.WallPostStep`) and the segment
            // between two posts is rasterised into these cells; a cell size
            // derived from the island's radius would put a wall's blocked
            // cells beside the wall rather than under it. So an island
            // bigger than `MaxCells * cell` across is simply not covered to
            // its shore -- the grid stays centred on the camp, as it always
            // was, and what falls off the edge falls back to the straight
            // line exactly as it did before.
            cell = Mathf.Max(0.5f, DesiredCell);
            n = Mathf.Clamp(Mathf.CeilToInt(2f * want / cell), 32, MaxCells);
            if ((n & 1) == 1) n++;                 // even, so the camp sits on a cell edge
            float half = 0.5f * n * cell;

            // **And the grid is aligned to the WORLD's 2 m lattice**, not to
            // the camp: `Outpost.SnapPost` rounds a world position to a
            // multiple of the step, so unless cell centres land on multiples
            // of the step too, a snapped post is half a cell off the cell it
            // is meant to be the centre of.
            origin = new Vector2(
                Mathf.Round((c.x - half + cell * 0.5f) / cell) * cell,
                Mathf.Round((c.z - half + cell * 0.5f) / cell) * cell);

            int count = n * n;
            hs = new float[count];
            open = new bool[count];
            block = new byte[count];
            pen = new float[count];

            // One height sample per cell. This is the whole cost of the map.
            for (int y = 0; y < n; y++)
            {
                float wz = origin.y + y * cell;
                int row = y * n;
                for (int x = 0; x < n; x++)
                    hs[row + x] = camp.GroundAt(new Vector3(origin.x + x * cell, 0f, wz));
            }

            // Slope from the neighbouring cells. Over a ~5 m span, a mean
            // 38° is a wall to a man 1.8 m tall, which is the test we want —
            // not the micro-roughness a finer difference would pick up.
            float limit = Mathf.Tan(Mathf.Clamp(MaxSlopeDegrees, 5f, 80f) * Mathf.Deg2Rad);
            for (int y = 0; y < n; y++)
            {
                int row = y * n;
                for (int x = 0; x < n; x++)
                {
                    int i = row + x;
                    float h = hs[i];
                    if (h <= SeaLevelY) { open[i] = false; pen[i] = 1f; continue; }

                    // **Measured over a two-cell stencil, 2026-09-23.** The
                    // comment above is the reason: a mean 38° over about
                    // five metres is what a man cannot walk up, and now
                    // that a cell is 2 m the immediate neighbour would be
                    // measuring the ground's roughness rather than its
                    // shape -- which would close cells all over a hillside
                    // the hands have been crossing happily for a month.
                    int r = Mathf.Max(1, Mathf.RoundToInt(2f / cell));
                    float span = r * cell;
                    float dx = 0f, dz = 0f;
                    if (x >= r) dx = Mathf.Max(dx, Mathf.Abs(h - hs[i - r]));
                    if (x < n - r) dx = Mathf.Max(dx, Mathf.Abs(h - hs[i + r]));
                    if (y >= r) dz = Mathf.Max(dz, Mathf.Abs(h - hs[i - r * n]));
                    if (y < n - r) dz = Mathf.Max(dz, Mathf.Abs(h - hs[i + r * n]));

                    float slope = Mathf.Max(dx, dz) / span;
                    open[i] = slope <= limit;
                    float k = slope / limit;
                    pen[i] = 1f + SlopePenalty * k * k;
                }
            }

            g = new float[count];
            came = new int[count];
            stamp = new int[count];
            closed = new bool[count];
            // **Sized for the pushes, not for the cells.** This heap does
            // not decrease-key — it re-pushes a cell when a cheaper way in
            // is found and drops the stale entry at pop time — so a cell can
            // sit in it up to once per incoming edge. An expansion pushes at
            // most 8, and expansions are capped, so `MaxExpansions * 8` is a
            // hard ceiling on how many entries can ever be live at once.
            // Sizing it `count` instead would silently drop entries on a
            // crowded search and quietly return a worse route.
            int slots = Mathf.Min(count, MaxExpansions) * 8 + 16;
            heap = new int[slots];
            heapF = new float[slots];
            search = 0;

            // Rocks first (they never move), then whatever wall is already
            // standing -- a grid rebuilt mid-raid must come back knowing
            // about the palisade it was built under.
            MarkRocks();
            ReMarkWalls();

            watch.Stop();
            LastBuildMs = (float)watch.Elapsed.TotalMilliseconds;
            LastBuildCells = count;
            TotalBuildMs += LastBuildMs;
            BuildCount++;

            if (LogBuild)
                Debug.Log($"[CampPath] {name}: {n}x{n} cells @ {cell:0.0} m " +
                          $"({half:0} m half-extent) built in {LastBuildMs:0.0} ms, " +
                          $"{Walkable():0.0}% walkable.");
        }

        float Walkable()
        {
            if (open == null || open.Length == 0) return 0f;
            int w = 0;
            for (int i = 0; i < open.Length; i++) if (open[i]) w++;
            return 100f * w / open.Length;
        }

        // --- the query -------------------------------------------------------

        /// Plan a walk. Fills `corners` with world points to head for in
        /// order, the last of which is `to` itself, and returns true.
        ///
        /// False means "no route worth having" — off the map, no ground, or
        /// the search gave up — and the caller should walk straight at the
        /// target, which is what it did before this file existed.
        public bool Plan(Vector3 from, Vector3 to, List<Vector3> corners)
            => Route(from, to, Walker.Hand, corners);

        /// **The same walk, for somebody in particular (2026-09-23).** A
        /// hand walks through its own camp's gates; a raider does not, and
        /// has to break a segment to get in. Everything else about the two
        /// searches is identical, which is the point -- a raider that
        /// pathed by a different rule from the hands would climb the
        /// mountains the hands learnt not to climb.
        public bool Route(Vector3 from, Vector3 to, Walker who, List<Vector3> corners)
        {
            if (corners == null) corners = new List<Vector3>();
            corners.Clear();
            if (!built) Build();
            if (hs == null) return false;

            mask = MaskFor(who);
            int a = Nearest(from), b = Nearest(to);
            if (a < 0 || b < 0) return false;
            if (a == b) { corners.Add(to); return true; }

            if (!Search(a, b)) return false;

            // Walk the come-from chain back, into `cells` forwards.
            cells.Clear();
            for (int i = b; i != a; i = came[i])
            {
                cells.Add(i);
                if (cells.Count > n * n) return false;   // paranoia; cannot loop
            }
            cells.Add(a);
            cells.Reverse();

            // String-pull: keep only the corners where the route actually has
            // to bend. Without this a hand zig-zags across cell centres and
            // reads like a man following a grid, which is worse-looking than
            // the straight line this replaces.
            //
            // **The look-ahead is WINDOWED, and that is a cost decision.**
            // Scanning to the end of the route from every corner is
            // O(len² ) line tests of O(len) each — on a hundred-cell walk
            // that is a million cell reads, on a phone, twice a frame. A
            // 24-cell window (about 120 m of route) is longer than any
            // single straight run a camp's ground actually offers, so it
            // costs nothing visible and bounds the work at a few tens of
            // thousands of reads.
            const int Window = 24;
            int at = 0;
            int last = cells.Count - 1;
            while (at < last)
            {
                int far = at + 1;
                int lookTo = Mathf.Min(last, at + Window);
                for (int j = lookTo; j > at + 1; j--)
                    if (Clear(cells[at], cells[j])) { far = j; break; }
                at = far;
                if (far < last) corners.Add(Centre(cells[far]));
            }

            // The real destination last, never a cell centre: arrival
            // tolerance and every "am I there" test upstream are about the
            // target, not about the map.
            corners.Add(to);
            return true;
        }

        bool Search(int start, int goal)
        {
            search++;
            heapCount = 0;

            stamp[start] = search;
            g[start] = 0f;
            came[start] = start;
            closed[start] = false;
            Push(start, Heuristic(start, goal));

            int expansions = 0;

            while (heapCount > 0)
            {
                int cur = Pop();
                if (cur == goal) return true;
                if (closed[cur]) continue;
                closed[cur] = true;

                if (++expansions > MaxExpansions) return false;

                int cx = cur % n, cy = cur / n;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + DX[d], ny = cy + DY[d];
                    if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                    int nb = ny * n + nx;
                    if (!Walk(nb)) continue;

                    // No squeezing through the gap between two blocked cells:
                    // a diagonal is only a step if both of its orthogonals are
                    // walkable too.
                    if (d >= 4)
                    {
                        if (!Walk(cy * n + nx) || !Walk(ny * n + cx)) continue;
                    }

                    float step = (d >= 4 ? 1.41421356f : 1f) * cell * pen[nb];
                    float ng = g[cur] + step;

                    if (stamp[nb] != search)
                    {
                        stamp[nb] = search;
                        closed[nb] = false;
                        g[nb] = ng;
                        came[nb] = cur;
                        Push(nb, ng + Heuristic(nb, goal));
                    }
                    else if (!closed[nb] && ng < g[nb])
                    {
                        g[nb] = ng;
                        came[nb] = cur;
                        Push(nb, ng + Heuristic(nb, goal));
                    }
                }
            }
            return false;
        }

        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// Octile distance. Admissible because `pen` is never below 1.
        float Heuristic(int i, int goal)
        {
            int dx = Mathf.Abs(i % n - goal % n);
            int dy = Mathf.Abs(i / n - goal / n);
            int lo = Mathf.Min(dx, dy);
            return cell * ((dx + dy - 2 * lo) + 1.41421356f * lo);
        }

        // --- grid helpers -----------------------------------------------------

        Vector3 Centre(int i)
        {
            int x = i % n, y = i / n;
            return new Vector3(origin.x + x * cell, hs[i], origin.y + y * cell);
        }

        int Index(Vector3 at)
        {
            int x = Mathf.RoundToInt((at.x - origin.x) / cell);
            int y = Mathf.RoundToInt((at.z - origin.y) / cell);
            if (x < 0 || y < 0 || x >= n || y >= n) return -1;
            return y * n + x;
        }

        /// The cell for a point, or the nearest walkable one within a few
        /// cells of it. This is the "target is off the mesh" fallback: a man
        /// sent to a spot on a cliff walks to the foot of the cliff rather
        /// than refusing to move.
        int Nearest(Vector3 at)
        {
            int i = Index(at);
            if (i < 0) return -1;
            if (Walk(i)) return i;

            int cx = i % n, cy = i / n;
            for (int r = 1; r <= 6; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    int y = cy + dy;
                    if (y < 0 || y >= n) continue;
                    int spanStep = (Mathf.Abs(dy) == r) ? 1 : 2 * r;
                    for (int dx = -r; dx <= r; dx += spanStep)
                    {
                        int x = cx + dx;
                        if (x < 0 || x >= n) continue;
                        int j = y * n + x;
                        if (Walk(j)) return j;
                    }
                }
            }
            return -1;
        }

        /// Is the straight line between two cells walkable the whole way? A
        /// supercover walk, so it cannot slip diagonally between two blocked
        /// cells the way a naive Bresenham does.
        bool Clear(int a, int b)
        {
            int x0 = a % n, y0 = a / n, x1 = b % n, y1 = b / n;
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int guard = dx + dy + 2;

            while (guard-- > 0)
            {
                if (!Walk(y0 * n + x0)) return false;
                if (x0 == x1 && y0 == y1) return true;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                else if (e2 < dx) { err += dx; y0 += sy; }
                // Both branches in one step would cut a corner, so they are
                // taken one at a time — that is the "supercover" part.
            }
            return false;
        }

        // --- walls, gates and rocks ------------------------------------------

        /// **Cheap reachability: can this walker get there at all?**
        ///
        /// A raid party's first question is not "which way in" but "is
        /// there a way in" -- if the camp is ringed and the gate is shut,
        /// the answer is no and the party goes and breaks a segment
        /// instead. Same A* as `Route`, stopped the moment the goal is
        /// popped and with no string-pull afterwards, because nobody is
        /// going to walk this one.
        public bool HasRoute(Vector3 from, Vector3 to, Walker who)
        {
            if (!built) Build();
            if (hs == null) return false;
            mask = MaskFor(who);
            int a = Nearest(from), b = Nearest(to);
            if (a < 0 || b < 0) return false;
            if (a == b) return true;
            return Search(a, b);
        }

        /// **Put a raised segment on the map, or take it off.**
        ///
        /// Rasterised as a supercover line between the two posts, one cell
        /// wide -- the same walk `Clear` does, so a segment can never be
        /// slipped through diagonally. A gate blocks the raider only; a
        /// palisade blocks everybody. Called on raise, on breach, on
        /// repair and on the swap from wall to gate; nothing else rebuilds
        /// the grid, which is the point (`docs/PLAN-fortress-harbour.md`
        /// Phase 1: "rebuild the affected cells on raise/cancel, not the
        /// whole grid").
        public void MarkWall(WallSegment seg, bool blocked)
        {
            if (seg == null) return;
            MarkLine(seg.A, seg.B, seg.IsGate ? BlockRaider : BlockBoth, blocked);
        }

        /// The same for a line the caller describes itself -- what a wall
        /// SITE uses to keep the ground it is drawn on to itself.
        public void MarkLine(Vector3 a, Vector3 b, byte flags, bool blocked)
        {
            if (!built) Build();
            if (hs == null) return;

            int ia = Index(a), ib = Index(b);
            if (ia < 0 || ib < 0) return;
            int x0 = ia % n, y0 = ia / n, x1 = ib % n, y1 = ib / n;
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int guard = dx + dy + 2;
            while (guard-- > 0)
            {
                int i = y0 * n + x0;
                if (blocked) block[i] |= flags; else block[i] = (byte)(block[i] & ~flags);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                else if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        /// Re-lay every standing segment. Called at the end of a build, so
        /// a grid thrown away and remade (a camp centre that moved, a dev
        /// reset) comes back knowing about the wall.
        void ReMarkWalls()
        {
            if (camp == null) return;
            var walls = camp.Walls;
            if (walls == null) return;
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                if (w == null || w.Breached) continue;
                MarkLine(w.A, w.B, w.IsGate ? BlockRaider : BlockBoth, true);
            }
        }

        /// **Rocks are obstacles now (D4, 2026-09-23).** Kevin's decision
        /// was "rocks become blocked cells for everyone; trees are felled
        /// at raise time as today", so this is the one place props reach
        /// the map.
        ///
        /// **What it can see, and what it cannot.** A stone or ore prop is
        /// a real `ResourceNode` GameObject with a position and a radius,
        /// so those are marked exactly. The boulders the island's scenery
        /// scatters are NOT objects at all -- `IslandScenery` bakes them
        /// straight into a cell's combined mesh (`SceneryKit`), and there
        /// is no index of them the way `SceneryWood` indexes trees. Rather
        /// than guess at them from the terrain's rock field (a second
        /// opinion about where the rocks are, which is the fault this whole
        /// file exists to avoid), `RockProbe` is left as the door: fill it
        /// in when the scenery grows an index and every camp's grid picks
        /// the rocks up on its next build.
        public static System.Func<float, float, bool> RockProbe;

        /// Metres of clearance marked round a rock prop, on top of its own
        /// radius. Half a cell, so a boulder that sits on a cell boundary
        /// closes both of the cells it is actually in and no more.
        public static float RockClearance = 1f;

        void MarkRocks()
        {
            var isle = camp != null ? camp.Island : null;
            var nodes = ResourceNode.All;
            for (int k = 0; k < nodes.Count; k++)
            {
                var node = nodes[k];
                if (node == null) continue;
                if (isle != null && node.Home != null && node.Home != isle) continue;
                // Timber is a tree and a tree is not an obstacle (D4); what
                // is left is stone and ore, which are rocks.
                if (node.Resource == Res.Timber) continue;
                MarkDisc(node.transform.position, RockClearance, BlockBoth);
            }

            if (RockProbe == null) return;
            for (int y = 0; y < n; y++)
            {
                float wz = origin.y + y * cell;
                int row = y * n;
                for (int x = 0; x < n; x++)
                    if (RockProbe(origin.x + x * cell, wz)) block[row + x] |= BlockBoth;
            }
        }

        void MarkDisc(Vector3 at, float radius, byte flags)
        {
            int cx = Mathf.RoundToInt((at.x - origin.x) / cell);
            int cy = Mathf.RoundToInt((at.z - origin.y) / cell);
            int r = Mathf.Max(0, Mathf.CeilToInt(radius / cell));
            for (int y = cy - r; y <= cy + r; y++)
            {
                if (y < 0 || y >= n) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= n) continue;
                    float dx = origin.x + x * cell - at.x;
                    float dz = origin.y + y * cell - at.z;
                    if (dx * dx + dz * dz > radius * radius) continue;
                    block[y * n + x] |= flags;
                }
            }
        }

        /// Metres per cell, once the grid is up. `Outpost.WallPostStep` is
        /// the number a post snaps to and this is the number the cells are;
        /// they are the same number by construction, and this is how a
        /// caller can check.
        public float CellSize => cell;

        // --- binary heap ------------------------------------------------------

        void Push(int cellIndex, float f)
        {
            if (heapCount + 1 >= heap.Length) return;   // cannot happen; cheap to say
            int i = ++heapCount;
            heap[i] = cellIndex;
            heapF[i] = f;
            while (i > 1)
            {
                int p = i >> 1;
                if (heapF[p] <= heapF[i]) break;
                Swap(p, i);
                i = p;
            }
        }

        int Pop()
        {
            int top = heap[1];
            heap[1] = heap[heapCount];
            heapF[1] = heapF[heapCount];
            heapCount--;

            int i = 1;
            while (true)
            {
                int l = i << 1, r = l + 1, s = i;
                if (l <= heapCount && heapF[l] < heapF[s]) s = l;
                if (r <= heapCount && heapF[r] < heapF[s]) s = r;
                if (s == i) break;
                Swap(s, i);
                i = s;
            }
            return top;
        }

        void Swap(int a, int b)
        {
            (heap[a], heap[b]) = (heap[b], heap[a]);
            (heapF[a], heapF[b]) = (heapF[b], heapF[a]);
        }
    }
}
