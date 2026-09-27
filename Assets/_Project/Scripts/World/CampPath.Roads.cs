using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Roads on the map (2026-09-27).** Kevin: *"give the villagers a
    /// slight speed boost when using it, and have them use it as much as
    /// possible within reason -- if they'd get there faster using the road,
    /// they use the road. They can start/stop using the road wherever they
    /// want."*
    ///
    /// So a road is not a route of its own; it is CHEAPER GROUND. Every
    /// player road segment (`OutpostLedger.builtRoads`) is rastered onto a
    /// road layer (the same supercover a wall is laid with), a step between
    /// two road cells costs `1 / CampRoads.SpeedMultiplier` of a step on
    /// grass, and the A* does the rest: it takes the road exactly when the
    /// road is quicker, and joins and leaves it at whatever cell is best.
    /// The body walks `SpeedMultiplier` faster on a road cell
    /// (`CampWorker.Walk`) and the invisible walker's leg is metered in the
    /// same road-discounted metres (`EffectiveMetres`), so a watched and an
    /// unwatched camp agree.
    ///
    /// The heuristic is left as it was (octile, grass-priced), which makes
    /// it inadmissible by at most the multiplier: a route may be up to that
    /// much worse than the best one in the worst case, and a road far off
    /// to the side may go unnoticed. That is the "within reason", and it
    /// keeps the search as cheap as it was.
    public partial class CampPath
    {
        bool[] road;
        bool anyRoad;
        readonly List<int> roadRaster = new List<int>();

        /// **Re-lay the road layer from the ledger.** Called by a map build
        /// and by `Outpost.Roads` whenever a segment is raised, torn down or
        /// loaded. A few hundred cell writes.
        public void RelayRoads()
        {
            // Never builds the map itself (a load would pay for it early):
            // a build lays this layer as its last step.
            if (!built || hs == null) return;
            int count = n * n;
            if (road == null || road.Length != count) road = new bool[count];
            else System.Array.Clear(road, 0, road.Length);
            anyRoad = false;
            var list = camp != null && camp.Ledger != null ? camp.Ledger.builtRoads : null;
            if (list == null) return;
            foreach (var r in list)
            {
                if (r == null) continue;
                int a = Index(r.A), b = Index(r.B);
                if (a < 0 || b < 0) continue;
                Raster(a, b, roadRaster);
                foreach (int i in roadRaster) road[i] = true;
                anyRoad = true;
            }
        }

        /// Is this point on a road cell? Never builds the map (asked per
        /// walker per frame): false until it exists.
        public bool OnRoad(Vector3 at)
        {
            if (!anyRoad || road == null || hs == null) return false;
            int i = Index(at);
            return i >= 0 && i < road.Length && road[i];
        }

        bool RoadCell(int i) => anyRoad && road != null && road[i];

        /// The price factor for one grid step: a step along the road (both
        /// cells road) is the multiplier cheaper.
        float RoadCost(int from, int to)
            => RoadCell(from) && RoadCell(to) ? 1f / CampRoads.SpeedMultiplier : 1f;

        // --- the string-pull keeps the road ------------------------------------

        /// Set once per `Route`, over `cells`: is there any road on it at all?
        bool routeTouchesRoad;

        void RoadPrefix()
        {
            routeTouchesRoad = false;
            if (!anyRoad) return;
            for (int k = 0; k < cells.Count; k++)
                if (road[cells[k]]) { routeTouchesRoad = true; return; }
        }

        /// **May the string-pull replace cells[at..j] with a straight line?**
        /// Without this a pull would cut every road corner off -- the line
        /// is walkable, so the old rule took it and the hand walked the
        /// grass beside the road he was routed along. Now the straight line
        /// is priced the same way the route was (road-discounted metres,
        /// slope ignored as the old pull ignored it) and taken only if it is
        /// no slower than the cells it replaces. A route that never touches
        /// a road pulls exactly as before.
        bool KeepsRoad(int at, int j)
        {
            if (!routeTouchesRoad) return true;
            float inv = 1f / CampRoads.SpeedMultiplier;
            float sub = 0f;
            for (int k = at; k < j; k++)
            {
                int p = cells[k], q = cells[k + 1];
                bool diag = p % n != q % n && p / n != q / n;
                float len = (diag ? 1.41421356f : 1f) * cell;
                if (Hop(p, q)) len = 0f;             // a ladder link: never pulled across anyway
                sub += len * (road[p] && road[q] ? inv : 1f);
            }
            Raster(cells[at], cells[j], roadRaster);
            int onRoad = 0;
            foreach (int i in roadRaster) if (road[i]) onRoad++;
            float frac = roadRaster.Count > 0 ? onRoad / (float)roadRaster.Count : 0f;
            Vector2 a = new Vector2(cells[at] % n, cells[at] / n), b = new Vector2(cells[j] % n, cells[j] / n);
            float line = Vector2.Distance(a, b) * cell * (frac * inv + (1f - frac));
            return line <= sub + 0.05f;
        }

        /// **Road-discounted metres along a route**: `from`, then `corners`,
        /// each leg sampled every metre and a road metre counted as
        /// 1/`SpeedMultiplier`. What the invisible walker meters its leg in
        /// (`Outpost.WalkedMetres`), so he takes as long as a body would.
        public float EffectiveMetres(Vector3 from, List<Vector3> corners)
        {
            if (!anyRoad) return RouteMetres(from, corners);
            float inv = 1f / CampRoads.SpeedMultiplier;
            float m = 0f;
            Vector3 p = from;
            for (int i = 0; i < corners.Count; i++)
            {
                Vector3 q = corners[i];
                float dx = q.x - p.x, dz = q.z - p.z;
                float len = Mathf.Sqrt(dx * dx + dz * dz);
                int steps = Mathf.Max(1, Mathf.CeilToInt(len));
                float piece = len / steps;
                for (int s = 0; s < steps; s++)
                {
                    float t = (s + 0.5f) / steps;
                    m += OnRoad(new Vector3(p.x + dx * t, 0f, p.z + dz * t)) ? piece * inv : piece;
                }
                p = q;
            }
            return m;
        }
    }
}
