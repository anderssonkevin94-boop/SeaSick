using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Roads the player lays (2026-09-27).** Kevin: *"I'd rather place it
    /// myself, give the villagers a slight speed boost when using it, and
    /// have them use it as much as possible within reason."*
    ///
    /// Sited tap-to-tap like a wall (`UI/RoadSiting`), one queue row per
    /// segment -- `planId == BuildPlans.Road.id`, `postA`/`postB` its ends,
    /// `x/z` its middle (where the stone is carried and the builder stands)
    /// -- priced in stone by length (`BuildPlans.RoadCost`), stocked by
    /// carriers who deliver on arrival and hammered like anything else,
    /// then raised here into `ledger.builtRoads`. What a standing road DOES
    /// is `CampPath.Roads` (cheaper to route over, faster to walk on); how
    /// it looks is `CampRoads`.
    public partial class Outpost
    {
        readonly List<RoadSegment> roadSegments = new List<RoadSegment>();

        /// Standing road segments at this camp.
        public IReadOnlyList<RoadSegment> Roads => roadSegments;

        /// Is this queue row a road segment?
        public static bool IsRoadRow(PendingBuild row)
            => row != null && !row.isWall && row.planId == BuildPlans.Road.id;

        /// Longest single site; a longer drag is cut into pieces this long.
        public const float MaxRoadSegment = 16f;
        /// Shortest segment worth a site.
        public const float MinRoadSegment = 1.5f;
        /// Sampling step along a segment for the ground test.
        const float RoadProbeStep = 0.5f;

        /// **Can a road run from `a` to `b`?** Refuses only nonsense: off
        /// the camp's map or the drawable sheet, into the sea, up ground a
        /// hand cannot stand on (cliff, rock, standing wall -- a gate is
        /// fine), through a building, or on top of a road already there.
        public bool CanPlaceRoad(Vector3 a, Vector3 b, out string why)
        {
            if (ledger == null || !Sited) { why = "this ground was never surveyed"; return false; }
            Vector3 run = b - a; run.y = 0f;
            float len = run.magnitude;
            if (len < MinRoadSegment) { why = "too short for a road"; return false; }
            if (len > MaxRoadSegment + 0.01f) { why = "too long for one piece"; return false; }
            if (!CampRoads.InReach(this, a) || !CampRoads.InReach(this, b))
            { why = "too far from the fire"; return false; }

            var map = CampPath.For(this);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / RoadProbeStep));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = a + run * (i / (float)steps);
                if (map != null && !map.OnMap(p)) { why = "too far from the camp"; return false; }
                if (GroundAt(p) < CampPath.SeaLevelY + 0.05f) { why = "that's in the water"; return false; }
                if (map != null && !map.RoadGround(p)) { why = "nobody can walk there — too steep, rock or wall"; return false; }
                // **Never through a building, by the ribbon's width
                // (2026-09-27).** The centre line alone let the ribbon's
                // edge lie over a footprint. Tapered at the tips like the
                // drawing, so a road can still end at a door.
                float fromEnd = Mathf.Min(i, steps - i) * (len / steps);
                float margin = Mathf.Min(CampRoads.HalfWidth, CampRoads.TipHalfWidth + fromEnd);
                var hit = BuildingOn(p, margin);
                if (hit != null) { why = $"it runs through the {hit.Label}"; return false; }
                // Nor across the fire's store cache (2026-10-03).
                if (OnFireCache(p, margin)) { why = "it runs through the fire's store"; return false; }
                if (BuildingSiteOn(p, margin, out BuildPlan site)) { why = $"it runs through the {site.label} going up there"; return false; }
            }

            // The same segment twice is nonsense; crossing one is a crossroads.
            if (ledger.builtRoads != null)
                foreach (var r in ledger.builtRoads)
                    if (r != null && SameRoad(r.A, r.B, a, b))
                    { why = "there's a road here already"; return false; }
            if (ledger.sites != null)
                foreach (var row in ledger.sites)
                    if (IsRoadRow(row) && SameRoad(row.postA, row.postB, a, b))
                    { why = "a road is already on order here"; return false; }
            why = "";
            return true;
        }

        static bool SameRoad(Vector3 p, Vector3 q, Vector3 a, Vector3 b)
        {
            const float Near = 1.2f;
            return (Flat(p, a) < Near && Flat(q, b) < Near) || (Flat(p, b) < Near && Flat(q, a) < Near)
                   || (Flat(0.5f * (p + q), 0.5f * (a + b)) < Near && Mathf.Abs(Flat(p, q) - Flat(a, b)) < 2f * Near);
        }

        static float Flat(Vector3 p, Vector3 q)
            => Mathf.Sqrt((p.x - q.x) * (p.x - q.x) + (p.z - q.z) * (p.z - q.z));

        /// The queued building (not a wall, ladder or road) whose footprint,
        /// grown by `margin`, covers `p` -- true with its plan.
        bool BuildingSiteOn(Vector3 p, float margin, out BuildPlan hit)
        {
            hit = default;
            if (ledger == null || ledger.sites == null) return false;
            foreach (var row in ledger.sites)
            {
                if (row == null || string.IsNullOrEmpty(row.planId)) continue;
                if (row.isWall || IsRoadRow(row) || IsLadderRow(row)) continue;
                var plan = PlanFor(row.planId, row.length);
                Vector2 f = plan.footprint;
                if (f.x <= 0f || f.y <= 0f) continue;
                Quaternion q = Quaternion.Euler(0f, row.yaw, 0f);
                Vector3 d = p - new Vector3(row.x, 0f, row.z); d.y = 0f;
                if (Mathf.Abs(Vector3.Dot(d, q * Vector3.right)) <= 0.5f * f.x + margin
                    && Mathf.Abs(Vector3.Dot(d, q * Vector3.forward)) <= 0.5f * f.y + margin)
                { hit = plan; return true; }
            }
            return false;
        }

        /// The standing building (not a wall) whose footprint, grown by
        /// `margin`, covers `p`, or null.
        Building BuildingOn(Vector3 p, float margin = 0.1f)
        {
            for (int k = 0; k < built.Count; k++)
            {
                var bd = built[k];
                if (bd == null || bd is WallSegment) continue;
                Vector2 f = bd.Footprint;
                if (f.x <= 0f || f.y <= 0f) continue;
                Transform t = bd.transform;
                Vector3 d = p - t.position; d.y = 0f;
                Vector3 r = t.right; r.y = 0f; r.Normalize();
                Vector3 fw = t.forward; fw.y = 0f; fw.Normalize();
                if (Mathf.Abs(Vector3.Dot(d, r)) <= 0.5f * f.x + margin
                    && Mathf.Abs(Vector3.Dot(d, fw)) <= 0.5f * f.y + margin)
                    return bd;
            }
            return null;
        }

        /// **A road end near `p`** (standing or on order) within `reach`, for
        /// the siting tool to snap a new run onto -- so the drawing joins
        /// the two into one line. False when there is none.
        public bool RoadEndNear(Vector3 p, float reach, out Vector3 end)
        {
            Vector3 bestAt = p;
            float best = reach;
            bool found = false;
            void Try(Vector3 q)
            {
                float d = Flat(p, q);
                if (d <= best) { best = d; bestAt = q; found = true; }
            }
            if (ledger != null && ledger.builtRoads != null)
                foreach (var r in ledger.builtRoads)
                    if (r != null) { Try(r.A); Try(r.B); }
            if (ledger != null && ledger.sites != null)
                foreach (var row in ledger.sites)
                    if (IsRoadRow(row)) { Try(row.postA); Try(row.postB); }
            end = found ? new Vector3(bestAt.x, GroundAt(bestAt), bestAt.z) : p;
            return found;
        }

        /// **Queue a segment.** Null with a reason if refused. Priced by
        /// length in stone (`BuildPlans.RoadCost`), no timber.
        public PendingBuild SiteRoad(Vector3 a, Vector3 b, out string why)
        {
            if (ledger == null) { why = "this ground was never surveyed"; return null; }
            ledger.MigratePending();
            if (!CanPlaceRoad(a, b, out why)) return null;
            Vector3 run = b - a;
            run.y = 0f;
            Vector3 mid = 0.5f * (a + b);
            var row = new PendingBuild
            {
                planId = BuildPlans.Road.id,
                postA = new Vector3(a.x, 0f, a.z),
                postB = new Vector3(b.x, 0f, b.z),
                x = mid.x,
                z = mid.z,
                yaw = run.sqrMagnitude > 1e-4f
                    ? Quaternion.LookRotation(run.normalized, Vector3.up).eulerAngles.y : 0f,
                length = 0f,
                needed = 0,
                stoneNeeded = BuildPlans.RoadCost(run.magnitude),
                phased = true,
            };
            // **Claim the line's trees and rocks (2026-09-27), same as a
            // wall.** Without this a road drew straight over standing
            // scenery -- nothing ever owned it, so nothing ever felled it,
            // and `ShapeOf` now knows a road row is a line (`IsRoadRow`).
            // Before the row joins the queue, so the registry is still the
            // one without it (`TakeFootprint`'s own rule).
            TakeFootprint(row);
            ledger.sites.Add(row);
            if (ledger.EnlistFree() > 0 && Watched) PuppetsToWork();
            EnsureBlueprints();
            if (ledger.ReadyToRaise) FinishReady();
            why = "";
            return row;
        }

        /// The blueprint for a queued segment (`EnsureBlueprints`).
        BuildSite DrawRoadSite(PendingBuild row)
        {
            var site = BuildSite.PlaceRoad(this, BuildPlans.Road, row.postA, row.postB);
            site.Bind(row);
            site.Refresh(row);
            return site;
        }

        /// Lay a paid, built row (`RaiseRow`). Never refuses.
        RoadSegment RaiseRoad(PendingBuild row)
        {
            if (!IsRoadRow(row)) return null;
            var br = new BuiltRoad { ax = row.postA.x, az = row.postA.z, bx = row.postB.x, bz = row.postB.z };
            if (ledger.builtRoads == null) ledger.builtRoads = new List<BuiltRoad>();
            ledger.builtRoads.Add(br);
            var seg = StandRoad(br);
            RoadsChanged();
            return seg;
        }

        /// The tappable object for a record. The drawing is the camp's.
        RoadSegment StandRoad(BuiltRoad br)
        {
            if (br == null || !Sited) return null;
            var go = new GameObject("Road");
            go.transform.SetParent(transform, true);
            var seg = go.AddComponent<RoadSegment>();
            seg.Configure(this, br);
            roadSegments.Add(seg);
            return seg;
        }

        /// Called by `RoadSegment.TearDown`.
        public void ForgetRoad(RoadSegment s)
        {
            if (s == null) return;
            roadSegments.Remove(s);
            if (ledger != null && ledger.builtRoads != null && s.Row != null)
                ledger.builtRoads.Remove(s.Row);
            RoadsChanged();
        }

        /// The map and the drawing follow the ledger.
        void RoadsChanged()
        {
            var map = CampPath.For(this);
            if (map != null) map.RelayRoads();
            var draw = CampRoads.For(this);
            if (draw != null) draw.MarkDirty();
        }

        /// `Adopt`: drop the standing segments (a reload replaces them).
        void ClearRoads()
        {
            foreach (var s in roadSegments) if (s != null) Destroy(s.gameObject);
            roadSegments.Clear();
        }

        /// `Adopt`: every saved segment back down.
        void StandSavedRoads()
        {
            if (ledger == null) return;
            if (ledger.builtRoads == null) ledger.builtRoads = new List<BuiltRoad>();
            ledger.builtRoads.RemoveAll(r => r == null);
            foreach (var r in ledger.builtRoads) StandRoad(r);
            RoadsChanged();
        }
    }
}
