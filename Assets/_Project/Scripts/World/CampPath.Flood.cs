using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **One walk-distance flood from a landing, for the landing party
    /// (Kevin, 2026-10-03: "the landing party reaches the whole island,
    /// nothing moves, far = longer trip").** Until then a party only worked
    /// sources within 80 m of the landing along a straight walkable line
    /// (`GatherParty.Reach`), so the Ore outcrop -- which `PlaceCampStone`
    /// rings round the surveyed clearing, inland -- never showed. Now any
    /// source the hands can WALK to is offered, at its walked distance.
    ///
    /// **Why a flood and not one A* per source.** The sheet asks about every
    /// source on the island at once (up to ~110: 48 tree nodes, 60 party
    /// rocks, the kit nodes), and an A* across the island is up to
    /// `MaxExpansions` (9 000) cells each -- hundreds of thousands of
    /// expansions on the frame the sheet opens. One Dijkstra from the
    /// landing over this same grid answers all of them: every reachable
    /// cell's walked metres and the cell it was reached from, so a source's
    /// distance is an array read and its route a walk back up `floodCame`.
    /// Cost: one pass over the walkable land of the landing's region (a
    /// 640 m island is at most `MaxCells`^2 = 102 400 cells), once per
    /// landing, cached by the party until it moves. NOT measured on the
    /// phone yet (no editor this session) -- the same order of work as the
    /// grid's own build, which logs ~10-40 ms.
    ///
    /// Same rules as `Route` for a hand: `Walk` (ground, rocks, walls,
    /// buildings), no diagonal squeeze, slope penalty `pen` orders the
    /// search; ladders and roads are left out (a camp-less island has none,
    /// and leaving them out can only make a route longer, never wrong).
    /// What it reports is real METRES walked, not the penalised cost.
    public partial class CampPath
    {
        float[] floodCost;      // penalised cost, the search's order
        float[] floodMetres;    // metres actually walked; +inf = not reached
        int[] floodCame;        // the cell this one was reached from; -1 = start
        int floodStart = -1;
        int[] fHeap = new int[0];
        float[] fHeapK = new float[0];
        int fHeapCount;

        /// Flood the walkable ground from `from` (the landing). False when
        /// the map cannot say (not built, no ground near `from`): the caller
        /// falls back to straight lines.
        public bool FloodFrom(Vector3 from)
        {
            floodStart = -1;
            if (!built) Build();
            if (hs == null) return false;
            mask = MaskFor(Walker.Hand);
            SyncSolids();
            int start = Near(from, 6);
            if (start < 0) return false;

            int count = n * n;
            if (floodCost == null || floodCost.Length != count)
            {
                floodCost = new float[count];
                floodMetres = new float[count];
                floodCame = new int[count];
            }
            for (int i = 0; i < count; i++) { floodCost[i] = float.PositiveInfinity; floodMetres[i] = float.PositiveInfinity; }

            fHeapCount = 0;
            floodCost[start] = 0f;
            floodMetres[start] = 0f;
            floodCame[start] = -1;
            FPush(start, 0f);
            while (fHeapCount > 0)
            {
                FPop(out int cur, out float k);
                if (k > floodCost[cur]) continue;          // a stale entry (no decrease-key)
                int cx = cur % n, cy = cur / n;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + DX[d], ny = cy + DY[d];
                    if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                    int nb = ny * n + nx;
                    if (!Walk(nb)) continue;
                    if (d >= 4 && (!Walk(cy * n + nx) || !Walk(ny * n + cx))) continue;
                    float metres = (d >= 4 ? 1.41421356f : 1f) * cell;
                    float nc = floodCost[cur] + metres * pen[nb];
                    if (nc >= floodCost[nb]) continue;
                    floodCost[nb] = nc;
                    floodMetres[nb] = floodMetres[cur] + metres;
                    floodCame[nb] = cur;
                    FPush(nb, nc);
                }
            }
            floodStart = start;
            return true;
        }

        /// Metres walked from the flood's start to stand by `to` (within
        /// `ReachCells` of it, or `standOff` metres when that is more: a big
        /// outcrop's own cells are blocked rock (`MarkRocks`), so it is
        /// worked from its edge), or false if the flood never got there.
        public bool FloodReach(Vector3 to, float standOff, out float metres)
        {
            metres = 0f;
            int c = FloodCell(to, standOff);
            if (c < 0) return false;
            Vector3 at = Centre(c);
            float dx = at.x - to.x, dz = at.z - to.z;
            metres = floodMetres[c] + Mathf.Sqrt(dx * dx + dz * dz);
            return true;
        }

        /// The walk from the flood's start to `to` as corners to head for in
        /// order, the last being `to` itself. Only the cells where the
        /// direction turns are kept. False if the flood never got there.
        public bool FloodRoute(Vector3 to, float standOff, List<Vector3> corners)
        {
            corners.Clear();
            int c = FloodCell(to, standOff);
            if (c < 0) return false;
            cells.Clear();
            for (int i = c; i >= 0 && cells.Count <= n * n; i = floodCame[i]) cells.Add(i);
            cells.Reverse();                                   // start .. target cell
            int lastDx = int.MinValue, lastDy = int.MinValue;
            for (int k = 1; k < cells.Count; k++)
            {
                int a = cells[k - 1], b = cells[k];
                int dx = b % n - a % n, dy = b / n - a / n;
                if (k > 1 && (dx != lastDx || dy != lastDy)) corners.Add(Centre(a));
                lastDx = dx; lastDy = dy;
            }
            corners.Add(to);
            return true;
        }

        /// The reached cell within reach of `to` (see `FloodReach`) with the
        /// shortest walk to it, or -1.
        int FloodCell(Vector3 to, float standOff)
        {
            if (floodStart < 0 || floodMetres == null) return -1;
            int i = Index(to);
            if (i < 0) return -1;
            int r = Mathf.Max(ReachCells, Mathf.CeilToInt(standOff / cell) + 1);
            int cx = i % n, cy = i / n, best = -1;
            float bestM = float.PositiveInfinity;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= n || y >= n) continue;
                    int j = y * n + x;
                    float m = floodMetres[j];
                    if (float.IsPositiveInfinity(m)) continue;
                    m += Mathf.Sqrt(dx * dx + dy * dy) * cell;
                    if (m < bestM) { bestM = m; best = j; }
                }
            return best;
        }

        // A plain binary min-heap that grows; the flood re-pushes instead of
        // decreasing a key, so it can hold a cell more than once.
        void FPush(int i, float k)
        {
            if (fHeapCount == fHeap.Length)
            {
                int size = Mathf.Max(1024, fHeap.Length * 2);
                System.Array.Resize(ref fHeap, size);
                System.Array.Resize(ref fHeapK, size);
            }
            int at = fHeapCount++;
            while (at > 0)
            {
                int up = (at - 1) >> 1;
                if (fHeapK[up] <= k) break;
                fHeap[at] = fHeap[up]; fHeapK[at] = fHeapK[up];
                at = up;
            }
            fHeap[at] = i; fHeapK[at] = k;
        }

        void FPop(out int i, out float k)
        {
            i = fHeap[0]; k = fHeapK[0];
            int lastI = fHeap[--fHeapCount];
            float lastK = fHeapK[fHeapCount];
            int at = 0;
            while (true)
            {
                int l = at * 2 + 1;
                if (l >= fHeapCount) break;
                int r = l + 1;
                int m = r < fHeapCount && fHeapK[r] < fHeapK[l] ? r : l;
                if (fHeapK[m] >= lastK) break;
                fHeap[at] = fHeap[m]; fHeapK[at] = fHeapK[m];
                at = m;
            }
            if (fHeapCount > 0) { fHeap[at] = lastI; fHeapK[at] = lastK; }
        }
    }
}
