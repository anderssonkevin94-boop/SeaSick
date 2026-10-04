using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **The fishing hut's shore spot (Kevin, 2026-09-30):** *"I want the
    /// fisherman to walk to the closest water when he fishes."*
    ///
    /// The ledger has no terrain, so the scene finds the spot and writes it
    /// on the station row (`OutpostLedger.SetShore`), where it is saved --
    /// the fisher's catch trips (`OutpostLedger.Fishing`) walk there, body
    /// or invisible walker alike.
    ///
    /// **Finding it**: rays out from the hut every 360/`ShoreBearings`
    /// degrees, sampled every `ShoreStep` m on the terrain's own height
    /// field (water at 0, the field `FishingHutShore` reads); the stand spot
    /// on a ray is the last point at least `ShoreStandHeight` above water,
    /// outside the hut's footprint, before the ground goes under. Nearest
    /// first. With a path grid to ask (`CampPath`: the camp is watched, or
    /// its grid is already built) a spot must be a walkable cell with a
    /// route from the hut -- walls and gates included -- tried nearest first
    /// up to `ShoreRouteTries` searches; the first that passes is it. None:
    /// the station is marked "no way to the water" with the reason, and the
    /// fisher stalls on it (he still carries his box home) rather than
    /// re-plan. With no grid, the nearest spot is taken on the ground alone
    /// (an unwatched camp; checked properly the next time it is watched).
    ///
    /// **Cached**: once per hut, again only when the hut row changes or the
    /// wall layer is re-laid (`CampPath.WallRevision` -- a raise, breach or
    /// gate). One int compare per catch-up otherwise.
    public partial class Outpost
    {
        /// How far above mean water the fisher's feet stand, metres: dry
        /// sand, a pace up from the waterline. **Provisional.**
        const float ShoreStandHeight = 0.15f;
        const int ShoreBearings = 32;
        const float ShoreStep = 0.5f;
        /// Route searches spent on one hut per re-check (each a bounded A*;
        /// a failed one in a closed ring floods up to `CampPath.MaxExpansions`).
        const int ShoreRouteTries = 6;
        /// Metres a spot may be pulled back inland to land on a walkable cell.
        const float ShoreBackoffStep = 0.75f;
        const int ShoreBackoffSteps = 4;

        struct ShoreCandidate
        {
            public float dist;
            public Vector3 stand, water, dir;
        }

        static readonly List<ShoreCandidate> shoreScratch = new List<ShoreCandidate>();

        /// Once per catch-up, before the tick: every fishing hut's spot is
        /// current. Cheap when nothing changed.
        void SaveShoreSpots()
        {
            if (ledger == null || height == null || ledger.stations == null) return;
            CampPath map = null;
            for (int i = 0; i < ledger.stations.Count; i++)
            {
                var s = ledger.stations[i];
                if (!OutpostLedger.FishesAtShore(s)) continue;
                var row = ledger.StationRow(i);
                if (row == null) continue;
                if (map == null) map = CampPath.For(this);
                bool canRoute = map != null && (map.Built || Watched);
                bool routed = canRoute && map.Built;
                bool samePos = Mathf.Abs(s.shoreForX - row.x) < 0.5f && Mathf.Abs(s.shoreForZ - row.z) < 0.5f;
                if (s.shore != 0 && samePos && (!routed || s.shoreRev == map.WallRevision)) continue;

                bool found = FindShore(row, canRoute ? map : null, out var stand, out var water, out string why);
                int rev = map != null && map.Built ? map.WallRevision : int.MinValue;
                ledger.SetShore(i, found, stand, water, why, row.At, rev);
            }
        }

        bool FindShore(BuiltBuilding row, CampPath map, out Vector3 stand, out Vector3 water, out string why)
        {
            stand = water = row.At;
            why = null;
            var plan = BuildPlans.Named(row.planId);
            float len = plan.footprint.x, wid = plan.footprint.y;
            var inv = Quaternion.Inverse(Quaternion.Euler(0f, row.yaw, 0f));
            Vector3 at = row.At;
            float reach = 0.5f * Mathf.Sqrt(len * len + wid * wid) + BuildPlans.FishingHutReach + 2f;

            shoreScratch.Clear();
            for (int k = 0; k < ShoreBearings; k++)
            {
                float a = k * Mathf.PI * 2f / ShoreBearings;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                bool hasDry = false;
                Vector3 lastDry = at;
                for (float t = 1f; t <= reach; t += ShoreStep)
                {
                    Vector3 p = at + dir * t;
                    float g = height(p.x, p.z);
                    if (g < 0f)
                    {
                        if (hasDry)
                            shoreScratch.Add(new ShoreCandidate
                            {
                                dist = Vector3.Distance(lastDry, at),
                                stand = lastDry, water = at + dir * (t + 1.5f), dir = dir,
                            });
                        break;
                    }
                    Vector3 local = inv * (p - at);
                    bool inside = Mathf.Abs(local.x) <= len * 0.5f + 0.6f && Mathf.Abs(local.z) <= wid * 0.5f + 0.6f;
                    if (!inside && g >= ShoreStandHeight) { lastDry = p; hasDry = true; }
                }
            }
            if (shoreScratch.Count == 0)
            {
                why = "no water within reach of the hut";
                return false;
            }
            shoreScratch.Sort((x, y) => x.dist.CompareTo(y.dist));

            if (map == null)
            {
                stand = shoreScratch[0].stand;
                water = shoreScratch[0].water;
                return true;
            }

            int tries = 0;
            bool walled = false;
            foreach (var c in shoreScratch)
            {
                // Pulled back inland a pace at a time onto a cell a hand can
                // stand in (the grid is 2 m cells; the waterline is finer).
                Vector3 p = c.stand;
                bool ok = false;
                for (int b = 0; b <= ShoreBackoffSteps; b++)
                {
                    p = c.stand - c.dir * (b * ShoreBackoffStep);
                    if (map.WalkableAt(p, CampPath.Walker.Hand)) { ok = true; break; }
                }
                if (!ok) continue;
                if (tries++ >= ShoreRouteTries) break;
                if (map.HasRoute(at, p, CampPath.Walker.Hand))
                {
                    stand = p;
                    water = c.water;
                    return true;
                }
                if (CampPath.Crosses(this, at, p, CampPath.Walker.Hand)) walled = true;
            }
            why = walled ? "walled off from the water — needs a gate" : "no way down to the water";
            return false;
        }

        // --- the store's way to each station (2026-10-05) --------------------
        //
        // **Kevin's Day 853 save: the fishing hut and pier outside the
        // palisade, below a cliff band, nothing walkable joining them to the
        // inside.** Runners were booked trips there over and over and stood
        // at the store hut with no route (the store front runner loop). The
        // ledger has no terrain, so -- as for the shore spot above -- the
        // scene asks the path grid, once per catch-up, whether the store
        // hut's door routes to each station's bays, and writes the answer on
        // the station row (`OutpostLedger.SetStationReach`); every haul rung
        // then skips a cut-off station (`OutpostLedger.StationReachable`).
        //
        // **Cached**: a station is asked again only when the wall layer is
        // re-laid (`CampPath.WallRevision`: a raise, breach or gate) or a
        // building is raised, moved, swapped or taken down
        // (`CampPath.SolidRevision`), plus every `ReachRecheckSeconds` as a
        // safety net. One int compare per station otherwise.
        //
        // **No grid, no answer**: an unwatched camp whose grid was never
        // built (and a time-away chunk, `CampPath.NoBuildForRoutes`) asks
        // nothing; what the last look said stays on the rows, and a station
        // nobody has looked at is reachable -- the old behaviour.

        /// Real seconds between safety re-checks of every station.
        const float ReachRecheckSeconds = 15f;
        float nextReachCheckAt;
        readonly List<Vector3> reachTry = new List<Vector3>(2);

        /// Once per catch-up, before the tick: every station's "can the
        /// store walk there" is current. Cheap when nothing changed.
        void SaveStationReach()
        {
            if (ledger == null || ledger.stations == null || ledger.stations.Count == 0) return;
            var map = CampPath.For(this);
            if (map == null || !(map.Built || Watched)) return;
            if (!map.Built && CampPath.NoBuildForRoutes) return;
            // From the store hut's door (the runners' post); no hut, the fire.
            var store = CampPiles.StoreBuildingOf(this);
            Vector3 from = store != null ? CampWorker.WorkSpot(this, store) : CampCentre;
            // A watched camp's first ask builds the grid; an island not
            // surveyed yet has none, and says nothing.
            if (!map.Built) map.HasRoute(from, from, CampPath.Walker.Hand);
            if (!map.Built) return;
            int rev = unchecked(map.WallRevision * 65599 + map.SolidRevisionNow());
            bool recheck = Time.unscaledTime >= nextReachCheckAt;
            if (recheck) nextReachCheckAt = Time.unscaledTime + ReachRecheckSeconds;
            for (int i = 0; i < ledger.stations.Count; i++)
            {
                var s = ledger.stations[i];
                if (s == null || s.removed) continue;
                if (s.reachRev == rev && !recheck) continue;
                bool ok = StationRoutes(map, i, from, out bool walled);
                ledger.SetStationReach(i, ok, walled, rev);
            }
        }

        /// **Does the store's door route to station `i`?** Tried to its
        /// bay marker (`Output_Dropoff`, else `Input_Pickup`) and to its
        /// work spot, free spots as a walk plans them; any one routing is
        /// enough (a false "cut off" would starve a station, so the test
        /// leans to yes). A station not standing yet: yes. `walled`: no
        /// route, but the ground alone joins them (`CampPath.Reachable`
        /// ignores walls) and the camp has walls -- a gate would fix it.
        bool StationRoutes(CampPath map, int i, Vector3 from, out bool walled)
        {
            walled = false;
            var row = ledger.StationRow(i);
            if (row == null) return true;
            Building b = null;
            float bestSq = 0.25f;
            Vector3 at = row.At;
            foreach (var x in built)
            {
                if (x == null || x.Id != row.planId) continue;
                Vector3 d = x.transform.position - at;
                d.y = 0f;
                if (d.sqrMagnitude < bestSq) { bestSq = d.sqrMagnitude; b = x; }
            }
            reachTry.Clear();
            if (b != null)
            {
                CampWorker.BayMarks(b, out var outAt, out bool hasOut, out var inAt, out bool hasIn);
                if (hasOut) reachTry.Add(outAt);
                else if (hasIn) reachTry.Add(inAt);
                reachTry.Add(CampWorker.WorkSpot(this, b));
            }
            else reachTry.Add(at);
            foreach (var p in reachTry)
                if (map.HasRoute(from, CampPath.FreeSpot(this, p), CampPath.Walker.Hand)) return true;
            walled = walls.Count > 0 && map.Reachable(reachTry[0]);
            return false;
        }
    }
}
