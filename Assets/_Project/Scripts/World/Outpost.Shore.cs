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
    }
}
