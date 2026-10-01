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
    ///
    /// **Except through a wall (2026-09-24).** Kevin, on the phone: *"villagers
    /// and animals walk through the walls that I've built."* The straight-line
    /// fallback is only taken where the straight line does not cross a wall
    /// (`Crosses`); where it would, the walker waits and asks again, and
    /// every step any walker takes is checked against the walls as lines
    /// (`Blocks`, `BlocksAnimal`) -- so no fallback, short hop or cut corner
    /// can carry a body through a palisade.
    [DisallowMultipleComponent]
    public partial class CampPath : MonoBehaviour
    {
        // --- tunables (runtime-added component: these statics ARE the dials) -

        /// Ground steeper than this is not walked up. **Now the one shared
        /// number (2026-09-27), `Walkability.ManMaxDegrees` (33°)**: it was
        /// 38° here and tested on four axes only, and the animals and every
        /// fallback walk had caps of their own or none (see `Walkability`).
        /// Still between the "sleek hills" the island style aims for
        /// (walkable) and the cliff shards and steep domes (not). Beaches and
        /// clearings are far below it.
        public static float MaxSlopeDegrees
        {
            get => Walkability.ManMaxDegrees;
            set => Walkability.ManMaxDegrees = value;
        }

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

        /// **Hard cap on the grid's side length in cells.** This, not the
        /// island, is what bounds the build cost on a phone.
        ///
        /// **Raised 160 -> 320, 2026-09-27** (Kevin: doing away with the 40 m
        /// building radius -- "the whole island should be built if you want
        /// it to"): `Reachable` and `Walkability` now have to answer for any
        /// spot on the island the player might site at, not just the
        /// hundred-odd metres round the fire the old ring allowed. 320 cells
        /// of 2 m is a 640 m square centred on the camp (half-extent 320 m),
        /// which covers every island short of an exceptionally large one --
        /// `Build` still asks for `Island.MaxRadius` first and only hits this
        /// ceiling on the biggest islands, where the far shore simply falls
        /// back to the old "no map, no refusal" behaviour rather than being
        /// tested. 102 400 cells is ~2.9 MB of arrays (~10 float/int/byte
        /// arrays at 4/1 bytes each) and ~102 k height samples to build --
        /// four times the old 25 600/1.3 MB, still a one-shot lazy build, not
        /// a per-frame cost.
        public static int MaxCells = 320;

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

        /// Set by `Save.AwayProgress` for the length of a time-away catch-up:
        /// an invisible walker's leg (`Outpost.WalkedMetres`) is planned only
        /// on a grid that is already up, never builds one mid-chunk.
        public static bool NoBuildForRoutes;

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
        /// **Walls and gates, on a layer of their own (2026-09-24).** They
        /// used to share `block` with the rocks, so a segment could only
        /// be OR'd on and AND'd off -- and two segments share the cell
        /// their common post stands in, so tearing down, breaching or
        /// gating one segment cleared its NEIGHBOUR's post cell too. This
        /// layer is instead re-laid whole from `camp.Walls` on every change
        /// (`RelayWalls`): a few hundred cell writes, and it cannot drift.
        byte[] wall;
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
        bool Walk(int i) => open[i] && ((block[i] | wall[i] | bldg[i]) & mask) == 0;

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

            // **Cover the whole island (2026-09-27), not a disc round the
            // fire.** Until this decision a camp's ground map only had to
            // answer for the hundred-odd metres the 40 m build ring and a
            // hand's errands both stayed inside of, so the old upper clamp
            // (260 m half-extent) quietly cut a big island off at the knees.
            // Now any point on the island can be sited at, and `Reachable`
            // has to have an opinion about it -- so the target is the
            // island's OWN measured radius, floored for a tiny islet's own
            // errand range and otherwise uncapped here; `MaxCells` below is
            // the one place cost is actually bounded. What still falls off
            // the edge (an exceptionally large island) falls back to the old
            // "no map, no refusal" behaviour, same as it always did.
            var isle = camp.Island;
            float want = isle != null ? isle.MaxRadius + 40f : 160f;
            want = Mathf.Max(want, 80f);

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
            wall = new byte[count];
            bldg = new byte[count];
            pen = new float[count];

            // One height sample per cell. This is the whole cost of the map.
            for (int y = 0; y < n; y++)
            {
                float wz = origin.y + y * cell;
                int row = y * n;
                for (int x = 0; x < n; x++)
                    hs[row + x] = camp.GroundAt(new Vector3(origin.x + x * cell, 0f, wz));
            }

            // **Slope: the steepest rise to ANY of the eight neighbours
            // (2026-09-27, `Walkability.SteepestRise`).** It was the larger
            // of the two AXIS differences, which reads a slope running
            // diagonally to the grid at 1/sqrt(2) of its real steepness --
            // a 45 degree flank came out as 35 and stayed open. The span is
            // one 2 m cell, which is also the largest step a man takes
            // between two cells he is routed through, so "no rise over
            // 1.3 m between neighbouring cells" is the same statement.
            float limit = Mathf.Tan(Mathf.Clamp(MaxSlopeDegrees, 5f, 80f) * Mathf.Deg2Rad);
            slopeClosed = 0;
            for (int y = 0; y < n; y++)
            {
                int row = y * n;
                for (int x = 0; x < n; x++)
                {
                    int i = row + x;
                    float h = hs[i];
                    if (h <= SeaLevelY) { open[i] = false; pen[i] = 1f; continue; }

                    float slope = Walkability.SteepestRise(hs, n, x, y, cell);
                    open[i] = slope <= limit;
                    if (!open[i]) slopeClosed++;
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
            RelayWalls(null);
            LayLinks(null);
            RelayRoads();
            // Buildings (2026-10-01, `CampPath.Solids`): their own layer,
            // with the lanes to their markers carved back open. Not in
            // `LabelGround`: like a wall, a building is not the ground.
            solidFrame = -1;
            SyncSolids();
            RelayBuildings();
            LabelGround();

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

        // --- ground reachability (2026-09-27) ---------------------------------

        /// Land cells closed by slope alone, at the last build.
        int slopeClosed;
        /// Which piece of connected walkable ground each cell is on (ground
        /// and rocks only, NOT walls: a wall is the player's to open with a
        /// gate, a cliff is not). 0 = not walkable.
        int[] region;
        int campRegion;

        /// **Flood the walkable ground once, at build.** 25 600 cells, a
        /// single pass with a flat stack -- well under a millisecond -- and
        /// it turns "can the hands get to that tree at all" from an A*
        /// search into an array read, which is what lets every target pick
        /// skip the ones up a cliff (`Reachable`).
        void LabelGround()
        {
            int count = n * n;
            region = new int[count];
            var stack = new int[count];
            int label = 0;
            for (int s = 0; s < count; s++)
            {
                if (region[s] != 0 || !GroundOpen(s)) continue;
                label++;
                int top = 0;
                stack[top++] = s;
                region[s] = label;
                while (top > 0)
                {
                    int cur = stack[--top];
                    int cx = cur % n, cy = cur / n;
                    for (int d = 0; d < 8; d++)
                    {
                        int nx = cx + DX[d], ny = cy + DY[d];
                        if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                        int nb = ny * n + nx;
                        if (region[nb] != 0 || !GroundOpen(nb)) continue;
                        // Same corner rule as the search.
                        if (d >= 4 && (!GroundOpen(cy * n + nx) || !GroundOpen(ny * n + cx))) continue;
                        region[nb] = label;
                        stack[top++] = nb;
                    }
                    // A ladder joins the ground at its two ends (2026-09-27).
                    if (hasLink != null && hasLink[cur])
                        for (int e = 0; e < links.Count; e++)
                        {
                            if (links[e].from != cur) continue;
                            int nb = links[e].to;
                            if (region[nb] != 0 || !GroundOpen(nb)) continue;
                            region[nb] = label;
                            stack[top++] = nb;
                        }
                }
            }
            int c = Near(camp != null ? camp.CampCentre : Vector3.zero, 6);
            campRegion = c >= 0 ? region[c] : 0;
        }

        bool GroundOpen(int i) => open[i] && (block[i] & BlockHand) == 0;

        /// The cell for a point if its ground is open, else the nearest open
        /// one within `radius` cells; -1 if none or off the map.
        int Near(Vector3 at, int radius)
        {
            int i = Index(at);
            if (i < 0) return -1;
            if (GroundOpen(i)) return i;
            int cx = i % n, cy = i / n, best = -1;
            float bestD = float.MaxValue;
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= n || y >= n) continue;
                    int j = y * n + x;
                    if (!GroundOpen(j)) continue;
                    float d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = j; }
                }
            return best;
        }

        /// Cells a target may stand off the walkable ground and still count
        /// as reached: a tree or a rock at the foot of a slope is worked
        /// from the foot. Two cells (4 m) -- `CampWorker.Walk` arrives
        /// within `SlopeArrive` of a target it cannot climb to.
        public const int ReachCells = 2;

        /// **Can the camp's people walk to this point from the fire, over
        /// the ground?** Ignores walls (a gate fixes those) and is true
        /// whenever the map cannot say -- off the grid, not built, no camp
        /// cell -- so nothing that worked before this existed is refused
        /// for want of an answer.
        public bool Reachable(Vector3 at)
        {
            if (!built) Build();
            if (hs == null || region == null || campRegion == 0) return true;
            if (Index(at) < 0) return true;
            int c = Near(at, ReachCells);
            return c >= 0 && region[c] == campRegion;
        }

        /// Shortcut: true when the camp has no map to ask.
        public static bool Reachable(Outpost camp, Vector3 at)
        {
            var map = camp != null ? For(camp) : null;
            return map == null || map.Reachable(at);
        }

        // --- read-outs for Dev/Editor/SlopeCheck -----------------------------

        public bool Built => built && hs != null;
        public int Side => n;
        public Vector2 Origin => origin;
        /// % of LAND cells (above `SeaLevelY`) closed by slope.
        public float SlopeClosedPercent
        {
            get
            {
                if (hs == null) return 0f;
                int land = 0;
                for (int i = 0; i < hs.Length; i++) if (hs[i] > SeaLevelY) land++;
                return land > 0 ? 100f * slopeClosed / land : 0f;
            }
        }
        /// 0 sea, 1 closed by slope, 2 rock, 3 wall, 4 open on the fire's
        /// ground, 5 open but cut off from the fire.
        public int CellKind(int x, int y)
        {
            int i = y * n + x;
            if (hs[i] <= SeaLevelY) return 0;
            if (!open[i]) return 1;
            if ((block[i] & BlockHand) != 0) return 2;
            if ((wall[i] & BlockHand) != 0) return 3;
            return region != null && campRegion != 0 && region[i] == campRegion ? 4 : 5;
        }
        public float HeightAtCell(int x, int y) => hs[y * n + x];

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
            => Route(from, to, who, corners, true);

        static float Flat2(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }

        /// `ladders` false: over the ground only (a worn road does not run
        /// up a ladder, `CampRoads`).
        public bool Route(Vector3 from, Vector3 to, Walker who, List<Vector3> corners, bool ladders)
        {
            if (corners == null) corners = new List<Vector3>();
            corners.Clear();
            if (!built) Build();
            if (hs == null) return false;

            mask = MaskFor(who);
            SyncSolids();
            // **In and out of a building by its lane (2026-10-01).** A walk
            // that starts or ends at a building's marker (a stand, a pickup)
            // plans from / to the lane's outside end and walks the lane as
            // its first / last leg -- the lane is the way between the
            // benches, the grid is too coarse to know it.
            bool laneA = LaneAt(from, 0.6f, out Vector3 exitA, out Vector3 viaA, out bool hasViaA);
            bool laneB = LaneAt(to, 0.5f, out Vector3 exitB, out Vector3 viaB, out bool hasViaB);
            // **Already at the stand's own approach (2026-10-01, the level 2
            // sawmill).** An approach inside a building's closed cells has a
            // lane of its own, so a walk from it to the stand it serves (or
            // from that stand back to it) read as "leave by the approach's
            // lane, then come back in": out to its exit, back to it -- and a
            // re-plan near the approach sent him out again. Finch circled
            // there for good. The lane between the two IS the walk.
            if ((laneB && hasViaB && Flat2(viaB, from) < 0.6f * 0.6f)
                || (laneA && hasViaA && Flat2(viaA, to) < 0.5f * 0.5f))
            {
                corners.Add(to);
                return true;
            }
            int a = Nearest(laneA ? exitA : from, who), b = Nearest(laneB ? exitB : to, who);
            if (a < 0 || b < 0) return false;
            // Out by the approach first (a stand's lane, `ViaApproach`),
            // unless he is leaving it for the approach itself.
            if (laneA && hasViaA && (viaA - to).sqrMagnitude > 0.09f) corners.Add(viaA);
            if (laneA && (exitA - (hasViaA ? viaA : from)).sqrMagnitude > 0.0001f) corners.Add(exitA);
            if (a == b)
            {
                if (laneB) corners.Add(exitB);
                if (laneB && hasViaB && (viaB - from).sqrMagnitude > 0.09f) corners.Add(viaB);
                corners.Add(to);
                return true;
            }

            useLinks = ladders;
            bool found = Search(a, b);
            useLinks = true;
            if (!found) return false;

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
            RoadPrefix();
            const int Window = 24;
            int at = 0;
            int last = cells.Count - 1;
            while (at < last)
            {
                int far = at + 1;
                // **Never pull a corner across a ladder (2026-09-27).** Both
                // ends of a link hop stay corners, so the walker arrives at
                // the foot and `LadderClimb` sees the leg up to the top.
                if (!Hop(cells[at], cells[at + 1]))
                {
                    int lookTo = Mathf.Min(last, at + Window);
                    for (int k = at + 1; k < lookTo; k++)
                        if (Hop(cells[k], cells[k + 1])) { lookTo = k; break; }
                    for (int j = lookTo; j > at + 1; j--)
                        if (Clear(cells[at], cells[j]) && KeepsRoad(at, j)
                            && SolidLineClear(Centre(cells[at]), Centre(cells[j]))) { far = j; break; }
                }
                at = far;
                if (far < last) corners.Add(Centre(cells[far]));
            }

            // The real destination last, never a cell centre: arrival
            // tolerance and every "am I there" test upstream are about the
            // target, not about the map. Down its lane, if it has one.
            if (laneB) corners.Add(exitB);
            if (laneB && hasViaB) corners.Add(viaB);
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

                    float step = (d >= 4 ? 1.41421356f : 1f) * cell * pen[nb] * RoadCost(cur, nb);
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

                // **Ladders (2026-09-27): an edge off the grid**, priced at
                // the climb's time so a route takes it only when it is
                // genuinely shorter. Hands and raiders alike; animals never
                // search this map.
                if (!useLinks || hasLink == null || !hasLink[cur]) continue;
                for (int e = 0; e < links.Count; e++)
                {
                    var l = links[e];
                    if (l.from != cur) continue;
                    int nb = l.to;
                    if (!Walk(nb)) continue;
                    float ng = g[cur] + l.cost;
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

        /// Octile distance. Admissible because `pen` is never below 1 --
        /// **and, with any road on the camp (2026-09-27), scaled by
        /// `1 / CampRoads.SpeedMultiplier`.** Grass-priced octile is
        /// inadmissible the moment a road exists: `RoadCost` can price a
        /// step as low as `1/SpeedMultiplier`, so the true cheapest path can
        /// run below what the plain heuristic promises, and A* is only
        /// guaranteed to find the optimum when the heuristic never
        /// overestimates. Undiscounted, a road bowed out to the side of the
        /// straight line reads as strictly worse until the search stumbles
        /// onto it, and a search that never stumbles onto it returns the
        /// grass route instead -- exactly the "faster road off to the side
        /// may go unnoticed" gap `CampPath.Roads` used to accept on purpose.
        /// Scaling by the multiplier keeps it a true lower bound (no step is
        /// ever cheaper than `cell / SpeedMultiplier`), so the search is
        /// exact again.
        ///
        /// **The cost: a camp with roads searches less selectively.** The
        /// discounted heuristic is uniformly weaker (by exactly
        /// `SpeedMultiplier`, ~1.3x at today's tuning), so a query on a
        /// roaded camp expands more cells for the same route than the old
        /// grass heuristic did -- `MaxExpansions` (9000) is unchanged and
        /// this is still well inside it for a camp-sized grid, but it is a
        /// real, camp-wide slowdown, not a free correctness fix. A camp with
        /// no roads (`anyRoad` false) is untouched: the multiplier is 1.
        float Heuristic(int i, int goal)
        {
            int dx = Mathf.Abs(i % n - goal % n);
            int dy = Mathf.Abs(i / n - goal / n);
            int lo = Mathf.Min(dx, dy);
            float h = cell * ((dx + dy - 2 * lo) + 1.41421356f * lo);
            return anyRoad ? h / CampRoads.SpeedMultiplier : h;
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
        ///
        /// **On the point's own side of any wall (2026-09-24).** A point in
        /// a blocked cell is usually a point beside a wall -- a hand
        /// standing where the palisade just went up, a site a pace off the
        /// line -- and the old ring scan took the first walkable cell in
        /// scan order, the far side of the wall as often as the near one.
        /// The route then started (or ended) across the palisade and its
        /// first (or last) straight leg walked through it. Now a candidate
        /// must be reachable from the point without crossing a wall, and
        /// the closest such one in the ring wins. A point INSIDE a wall's
        /// thickness is exempt (`WallEmbedded`), so nobody is trapped.
        int Nearest(Vector3 at, Walker who = Walker.Hand)
        {
            int i = Index(at);
            if (i < 0) return -1;
            if (Walk(i)) return i;

            bool gatesOpen = who == Walker.Hand;
            int cx = i % n, cy = i / n;
            for (int r = 1; r <= 6; r++)
            {
                int best = -1;
                float bestD = float.MaxValue;
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
                        if (!Walk(j)) continue;
                        float ddx = origin.x + x * cell - at.x, ddz = origin.y + y * cell - at.z;
                        float d = ddx * ddx + ddz * ddz;
                        if (d >= bestD) continue;
                        if (camp != null && WallsBlock(camp.Walls, at, Centre(j), gatesOpen, 0f, out _))
                            continue;
                        best = j;
                        bestD = d;
                    }
                }
                if (best >= 0) return best;
            }
            return -1;
        }

        /// Is the straight line between two cells walkable the whole way?
        ///
        /// **A TRUE supercover now (2026-09-24)**, the same walk `Raster`
        /// lays a wall with: every cell the line between the two centres
        /// passes through, and at an exact corner both cells it grazes. The
        /// old one-step-at-a-time Bresenham was 4-connected but not a
        /// supercover: on a shallow diagonal it skipped cells the line
        /// really passes through, so a wall laid with it had gaps under its
        /// own drawing and a leg tested with it could clip a blocked corner.
        bool Clear(int a, int b)
        {
            int x = a % n, y = a / n, x1 = b % n, y1 = b / n;
            int ddx = x1 - x, ddy = y1 - y;
            int nx = Mathf.Abs(ddx), ny = Mathf.Abs(ddy);
            int sx = ddx > 0 ? 1 : -1, sy = ddy > 0 ? 1 : -1;
            if (!Walk(y * n + x)) return false;
            for (int ix = 0, iy = 0; ix < nx || iy < ny;)
            {
                int decision = (1 + 2 * ix) * ny - (1 + 2 * iy) * nx;
                if (decision == 0)
                {
                    // Through a corner exactly: both cells it touches count.
                    if (!Walk(y * n + x + sx) || !Walk((y + sy) * n + x)) return false;
                    x += sx; y += sy; ix++; iy++;
                }
                else if (decision < 0) { x += sx; ix++; }
                else { y += sy; iy++; }
                if (!Walk(y * n + x)) return false;
            }
            return true;
        }

        /// The supercover of the line between two cells, into `into`. The
        /// same walk as `Clear`, so a wall's cells and a route's line test
        /// agree cell for cell.
        void Raster(int a, int b, List<int> into)
        {
            into.Clear();
            int x = a % n, y = a / n, x1 = b % n, y1 = b / n;
            int ddx = x1 - x, ddy = y1 - y;
            int nx = Mathf.Abs(ddx), ny = Mathf.Abs(ddy);
            int sx = ddx > 0 ? 1 : -1, sy = ddy > 0 ? 1 : -1;
            into.Add(y * n + x);
            for (int ix = 0, iy = 0; ix < nx || iy < ny;)
            {
                int decision = (1 + 2 * ix) * ny - (1 + 2 * iy) * nx;
                if (decision == 0)
                {
                    into.Add(y * n + x + sx);
                    into.Add((y + sy) * n + x);
                    x += sx; y += sy; ix++; iy++;
                }
                else if (decision < 0) { x += sx; ix++; }
                else { y += sy; iy++; }
                into.Add(y * n + x);
            }
        }

        readonly List<int> rasterCells = new List<int>();

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
            useLinks = true;
            if (!built) Build();
            if (hs == null) return false;
            mask = MaskFor(who);
            SyncSolids();
            if (LaneAt(from, 0.6f, out Vector3 ea)) from = ea;
            if (LaneAt(to, 0.5f, out Vector3 eb)) to = eb;
            int a = Nearest(from, who), b = Nearest(to, who);
            if (a < 0 || b < 0) return false;
            if (a == b) return true;
            return Search(a, b);
        }

        /// **Put a raised segment on the map, or take it off.**
        ///
        /// Rasterised as a supercover line between the two posts, one cell
        /// wide -- the same walk `Clear` does, so a segment can never be
        /// slipped through diagonally. A gate's 3 m module blocks the raider
        /// only; a palisade -- including the palisade either side of a
        /// gate's module -- blocks everybody. Called on raise, on breach, on
        /// repair and on the swap from wall to gate; nothing else rebuilds
        /// the grid, which is the point (`docs/PLAN-fortress-harbour.md`
        /// Phase 1: "rebuild the affected cells on raise/cancel, not the
        /// whole grid").
        ///
        /// **It re-lays the whole wall layer rather than toggling one line
        /// (2026-09-24).** Toggling cleared the shared post cell of the
        /// neighbouring segment on every tear-down, breach or gate swap; and
        /// a gate marked as one raider-only line opened the WHOLE segment to
        /// the hands -- a 12 m "gate" is a 3 m gate module with 4.5 m of
        /// palisade either side (`WallVisual.Build`), and the hands walked
        /// straight through that palisade. `RelayWalls` has neither fault.
        /// `blocked == false` means "this one is coming off": it is left out
        /// of the re-lay although it is still in `camp.Walls` (every caller
        /// clears a segment before forgetting or changing it).
        public void MarkWall(WallSegment seg, bool blocked)
        {
            if (seg == null) return;
            if (!built) Build();
            if (hs == null) return;
            RelayWalls(blocked ? null : seg);
        }

        /// Can this walker stand in the cell under `at`? For checks
        /// (`WallTowerCheck`). False off the map.
        public bool WalkableAt(Vector3 at, Walker who)
        {
            if (!built) Build();
            if (hs == null) return false;
            int i = Index(at);
            if (i < 0) return false;
            mask = MaskFor(who);
            SyncSolids();
            return Walk(i);
        }

        /// **Re-lay the wall layer as it stands** -- a wall tower came or
        /// went (`Outpost.TouchWallTowers`), which changes no segment.
        public void RelayWallLayer()
        {
            if (!built) Build();
            if (hs == null) return;
            RelayWalls(null);
        }

        /// The same for a line the caller describes itself -- what a wall
        /// SITE uses to keep the ground it is drawn on to itself. Goes on
        /// the rock layer, so a wall re-lay never wipes it.
        public void MarkLine(Vector3 a, Vector3 b, byte flags, bool blocked)
        {
            if (!built) Build();
            if (hs == null) return;

            int ia = Index(a), ib = Index(b);
            if (ia < 0 || ib < 0) return;
            Raster(ia, ib, rasterCells);
            for (int k = 0; k < rasterCells.Count; k++)
            {
                int i = rasterCells[k];
                if (blocked) block[i] |= flags; else block[i] = (byte)(block[i] & ~flags);
            }
        }

        /// Half the opening a gate segment gives the hands, metres from its
        /// midpoint: the gate module (`WallVisual.GateSpan`) plus a hair, so
        /// a 2 m or 2.83 m gate -- whose module overhangs both its posts --
        /// opens the post cells it stands over.
        static float GateOpenHalf => WallVisual.GateSpan * 0.5f + 0.1f;

        /// **Bumped every time the wall layer is re-laid (2026-09-30)** --
        /// a build, a raise, a breach, a gate swap. A cache of "can a hand
        /// get from here to there" (the fishing hut's shore spot,
        /// `Outpost.SaveShoreSpots`) keys on it and re-asks only when it
        /// moves: one int compare a tick, never a search.
        public int WallRevision { get; private set; }

        /// **Lay every standing segment, from scratch.** Palisade: both
        /// flags on every cell of its supercover. Gate: the raider flag on
        /// every cell and the hand flag on the cells outside the gate module
        /// (the palisade either side of it); then, in a second pass so the
        /// order of the list cannot matter, the hand flag comes OFF the
        /// module's cells, including a post cell a neighbouring palisade put
        /// it on. A grid rebuilt from nothing and a grid marked one change
        /// at a time now come out identical.
        void RelayWalls(WallSegment except)
        {
            if (wall == null) return;
            WallRevision++;
            System.Array.Clear(wall, 0, wall.Length);
            if (camp == null) return;
            var walls = camp.Walls;
            if (walls == null) return;
            float open2 = GateOpenHalf * GateOpenHalf;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int k = 0; k < walls.Count; k++)
                {
                    var w = walls[k];
                    if (w == null || w == except || w.Breached) continue;
                    if (pass == 1 && !w.IsGate) continue;
                    int ia = Index(w.A), ib = Index(w.B);
                    if (ia < 0 || ib < 0) continue;
                    Raster(ia, ib, rasterCells);

                    float mx = 0.5f * (w.A.x + w.B.x), mz = 0.5f * (w.A.z + w.B.z);
                    for (int c = 0; c < rasterCells.Count; c++)
                    {
                        int i = rasterCells[c];
                        if (!w.IsGate) { wall[i] |= BlockBoth; continue; }
                        float dx = origin.x + (i % n) * cell - mx;
                        float dz = origin.y + (i / n) * cell - mz;
                        bool inModule = dx * dx + dz * dz <= open2;
                        if (pass == 0)
                        {
                            wall[i] |= BlockRaider;
                            if (!inModule) wall[i] |= BlockHand;
                        }
                        else if (inModule) wall[i] = (byte)(wall[i] & ~BlockHand);
                    }
                }
            }

            // **Wall towers are wall (2026-09-27).** The node a tower stands
            // on blocks everybody while the tower stands, whatever the runs
            // either side of it are doing -- a breach beside a tower is a
            // hole in the run, not round the tower.
            camp.WallTowerNodes(towerNodes);
            for (int k = 0; k < towerNodes.Count; k++)
            {
                int i = Index(towerNodes[k]);
                if (i >= 0) wall[i] |= BlockBoth;
            }
        }

        readonly List<Vector3> towerNodes = new List<Vector3>();

        // --- walls as LINES: the continuous test ------------------------------

        /// **Metres a walker keeps off a wall.** The grid says which way to
        /// go; this is what stops a body actually entering the palisade on
        /// a corner cut, a short straight hop, or a plan that failed
        /// (`CampWorker.Walk`, `Animal.Step`). About the half-depth of the
        /// kit's stakes plus a shoulder.
        public static float WallClearance = 0.35f;

        /// Closer than this to a wall line, a walker is IN the wall -- only
        /// possible by being put there (the Hand, a segment raised over a
        /// man, a builder's stand spot on a line that runs toward the fire).
        /// Such a walker ignores that segment so he can always walk out.
        public const float WallEmbedded = 0.15f;

        /// Does a step from `from` to `to` pass through a wall of this camp,
        /// or come within `clearance` of one while closing on it? `along`
        /// is the blocking wall's direction, for sliding. Gates are open to
        /// a `Hand` (their 3 m module only) and shut to anybody else. No
        /// allocation: a `for` over the camp's own list.
        public static bool Blocks(Outpost camp, Vector3 from, Vector3 to, Walker who,
            float clearance, out Vector3 along)
        {
            along = default;
            if (camp == null) return false;
            return WallsBlock(camp.Walls, from, to, who == Walker.Hand, clearance, out along);
        }

        /// Does the straight line cross a wall of this camp? `Blocks` with
        /// no clearance: "can I just walk straight there".
        public static bool Crosses(Outpost camp, Vector3 from, Vector3 to, Walker who)
            => Blocks(camp, from, to, who, 0f, out _);

        /// **The same for an animal, against every camp's walls.** A beast
        /// is not the camp's: gates are shut to it, as to a raider. Called
        /// per animal per frame, so it is a loop over a handful of camps and
        /// their segments with a box reject before any real arithmetic, and
        /// a camp with no wall costs one `Count` read.
        public static bool BlocksAnimal(Vector3 from, Vector3 to, float clearance)
        {
            var all = Outpost.All;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null) continue;
                var walls = o.Walls;
                if (walls == null || walls.Count == 0) continue;
                if (WallsBlock(walls, from, to, false, clearance, out _)) return true;
            }
            return false;
        }

        static bool WallsBlock(IReadOnlyList<WallSegment> walls, Vector3 p, Vector3 q,
            bool gatesOpen, float r, out Vector3 along)
        {
            along = default;
            if (walls == null) return false;
            // The list gates are looked up in, for a walker they open to.
            var gates = gatesOpen ? walls : null;
            float h = WallVisual.GateSpan * 0.5f;
            for (int k = 0; k < walls.Count; k++)
            {
                var w = walls[k];
                if (w == null || w.Breached) continue;
                Vector3 a = w.A, b = w.B;
                float dx = b.x - a.x, dz = b.z - a.z;
                float len = Mathf.Sqrt(dx * dx + dz * dz);
                if (len < 1e-4f) continue;
                float ux = dx / len, uz = dz / len;

                if (w.IsGate && gatesOpen)
                {
                    // Only the palisade either side of the module stands.
                    if (len <= 2f * h) continue;
                    float mx = 0.5f * (a.x + b.x), mz = 0.5f * (a.z + b.z);
                    if (PieceBlocks(p, q, a.x, a.z, mx - ux * h, mz - uz * h, r, gates)
                        || PieceBlocks(p, q, mx + ux * h, mz + uz * h, b.x, b.z, r, gates))
                    { along = new Vector3(ux, 0f, uz); return true; }
                    continue;
                }

                if (PieceBlocks(p, q, a.x, a.z, b.x, b.z, r, gates))
                { along = new Vector3(ux, 0f, uz); return true; }
            }
            return false;
        }

        /// **Is this point in a gate's opening?** A gate's 3 m module is
        /// wider than a 2 m segment and overhangs its posts, so the way
        /// through a short gate runs over the very post cells the palisade
        /// either side ends on (`RelayWalls` opens them for the same
        /// reason). A crossing -- or a close pass -- inside the opening is
        /// the gate, not the palisade. Only reached when a step is already
        /// against a wall, so the second loop is rare.
        static bool InGate(IReadOnlyList<WallSegment> walls, float x, float z)
        {
            float o2 = GateOpenHalf * GateOpenHalf;
            for (int k = 0; k < walls.Count; k++)
            {
                var w = walls[k];
                if (w == null || !w.IsGate || w.Breached) continue;
                float dx = 0.5f * (w.A.x + w.B.x) - x, dz = 0.5f * (w.A.z + w.B.z) - z;
                if (dx * dx + dz * dz <= o2) return true;
            }
            return false;
        }

        /// One straight piece of wall against one step, flat. `gates`, when
        /// not null, is the list whose gate openings let this walker by.
        static bool PieceBlocks(Vector3 p, Vector3 q, float sx, float sz, float ex, float ez,
            float r, IReadOnlyList<WallSegment> gates)
        {
            // Box reject first: almost every piece is nowhere near the step.
            float lox = (sx < ex ? sx : ex) - r, hix = (sx > ex ? sx : ex) + r;
            float loz = (sz < ez ? sz : ez) - r, hiz = (sz > ez ? sz : ez) + r;
            if ((p.x > q.x ? p.x : q.x) < lox || (p.x < q.x ? p.x : q.x) > hix) return false;
            if ((p.z > q.z ? p.z : q.z) < loz || (p.z < q.z ? p.z : q.z) > hiz) return false;

            float d0 = PointSegDist(p.x, p.z, sx, sz, ex, ez);
            if (d0 < WallEmbedded) return false;       // in it already: let him out

            // Crossing the line outright.
            float c1 = Cross2(sx, sz, ex, ez, p.x, p.z), c2 = Cross2(sx, sz, ex, ez, q.x, q.z);
            float c3 = Cross2(p.x, p.z, q.x, q.z, sx, sz), c4 = Cross2(p.x, p.z, q.x, q.z, ex, ez);
            if ((c1 > 0f) != (c2 > 0f) && (c3 > 0f) != (c4 > 0f))
            {
                if (gates == null) return true;
                float t = c1 / (c1 - c2);                // where p->q meets the line
                return !InGate(gates, p.x + (q.x - p.x) * t, p.z + (q.z - p.z) * t);
            }

            if (r <= 0f) return false;
            float d1 = PointSegDist(q.x, q.z, sx, sz, ex, ez);
            if (!(d1 < r && d1 < d0)) return false;
            return gates == null || !InGate(gates, q.x, q.z);
        }

        static float Cross2(float ox, float oz, float px, float pz, float qx, float qz)
            => (px - ox) * (qz - oz) - (pz - oz) * (qx - ox);

        static float PointSegDist(float x, float z, float sx, float sz, float ex, float ez)
        {
            float dx = ex - sx, dz = ez - sz;
            float l2 = dx * dx + dz * dz;
            float t = l2 < 1e-8f ? 0f : Mathf.Clamp01(((x - sx) * dx + (z - sz) * dz) / l2);
            float ox = sx + t * dx - x, oz = sz + t * dz - z;
            return Mathf.Sqrt(ox * ox + oz * oz);
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

        /// **Can a worn road lie here? (2026-09-26, `CampRoads`).** Open
        /// ground a hand may stand on: not sea, not too steep, no rock, no
        /// standing wall. A gate is open to hands, so a road runs through
        /// it. False off the map or before it is built.
        public bool RoadGround(Vector3 at)
        {
            if (!built) Build();
            if (hs == null) return false;
            int i = Index(at);
            return i >= 0 && open[i] && ((block[i] | wall[i]) & BlockHand) == 0;
        }

        // --- ladders: links off the grid (2026-09-27) -------------------------

        /// One direction of a ladder chain on the map.
        struct Link { public int from, to; public float cost; }

        readonly List<Link> links = new List<Link>();
        /// Per cell: does any link start here? (So the search pays one array
        /// read per expansion, not a list walk.)
        bool[] hasLink;
        /// Set per query by `Route` (roads ask without).
        bool useLinks = true;

        /// Metres of walking one second of climbing is worth, for the link
        /// cost: about a hand's walking pace.
        public static float LinkMetresPerSecond = 2.4f;

        /// Links on the map now (both directions count, so 2 per chain).
        public int LinkCount => links.Count;

        /// **Re-lay every ladder's link from `camp.Ladders`, and re-flood
        /// the ground** (a plateau joined by a ladder is on the fire's ground
        /// now, so `Reachable` says yes to what stands on it). `except` is a
        /// chain coming down that is still in the list. A ladder's ends snap
        /// to the nearest open cell within a few; one that finds none adds
        /// nothing.
        public void RelayLinks(Ladder except = null)
        {
            if (!built) Build();
            if (hs == null) return;
            LayLinks(except);
            LabelGround();
        }

        void LayLinks(Ladder except)
        {
            links.Clear();
            if (hasLink == null || hasLink.Length != n * n) hasLink = new bool[n * n];
            else System.Array.Clear(hasLink, 0, hasLink.Length);
            if (camp == null) return;
            var list = camp.Ladders;
            if (list == null) return;
            for (int k = 0; k < list.Count; k++)
            {
                var l = list[k];
                if (l == null || l == except || l.Shape == null) continue;
                int a = NearLevel(l.Foot, 3), b = NearLevel(l.Top, 3);
                if (a < 0 || b < 0 || a == b) continue;
                // Never below the octile distance, so the heuristic stays
                // admissible (and a chain is never "free").
                float cost = Mathf.Max(l.ClimbSeconds * LinkMetresPerSecond, Heuristic(a, b) + cell);
                links.Add(new Link { from = a, to = b, cost = cost });
                links.Add(new Link { from = b, to = a, cost = cost });
                hasLink[a] = true;
                hasLink[b] = true;
            }
        }

        /// **The open cell for a ladder's end, on the end's own LEVEL.** A
        /// top standing a metre or two back from the lip can have its own
        /// cell closed by the edge, and the nearest open cell in plain
        /// distance may then be at the FOOT of the cliff -- a link from the
        /// foot to the foot. So the nearest open cell within 1.5 m of the
        /// end's height wins; plain `Near` only if there is none.
        int NearLevel(Vector3 at, int radius)
        {
            int i = Index(at);
            if (i < 0) return -1;
            int cx = i % n, cy = i / n, best = -1;
            float bestD = float.MaxValue;
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= n || y >= n) continue;
                    int j = y * n + x;
                    if (!GroundOpen(j) || Mathf.Abs(hs[j] - at.y) > 1.5f) continue;
                    float ox = origin.x + x * cell - at.x, oz = origin.y + y * cell - at.z;
                    float d = ox * ox + oz * oz;
                    if (d < bestD) { bestD = d; best = j; }
                }
            return best >= 0 ? best : Near(at, radius);
        }

        /// Two consecutive route cells that are not grid neighbours: a link.
        bool Hop(int a, int b)
        {
            int dx = Mathf.Abs(a % n - b % n), dy = Mathf.Abs(a / n - b / n);
            return dx > 1 || dy > 1;
        }

        /// Is there a link between the cells under these two points (either
        /// way)? For checks.
        public bool LinkBetween(Vector3 p, Vector3 q)
        {
            if (!built) Build();
            if (hs == null) return false;
            int a = NearLevel(p, 3), b = NearLevel(q, 3);
            for (int e = 0; e < links.Count; e++)
                if (links[e].from == a && links[e].to == b) return true;
            return false;
        }

        /// Is this point on the map at all?
        public bool OnMap(Vector3 at)
        {
            if (!built) Build();
            return hs != null && Index(at) >= 0;
        }

        /// **Metres of the hands' route over the ground ONLY (no ladders)**,
        /// or -1 when there is none. Corner to corner, flat. For the ladder
        /// siting rule "not a cliff -- there's a walk up close by".
        public float GroundRouteMetres(Vector3 from, Vector3 to)
        {
            if (!Route(from, to, Walker.Hand, scratchRoute, false)) return -1f;
            return RouteMetres(from, scratchRoute);
        }

        /// Flat metres along `from` then `corners`.
        public static float RouteMetres(Vector3 from, List<Vector3> corners)
        {
            float m = 0f;
            Vector3 p = from;
            for (int i = 0; i < corners.Count; i++)
            {
                Vector3 q = corners[i];
                float dx = q.x - p.x, dz = q.z - p.z;
                m += Mathf.Sqrt(dx * dx + dz * dz);
                p = q;
            }
            return m;
        }

        readonly List<Vector3> scratchRoute = new List<Vector3>();

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
