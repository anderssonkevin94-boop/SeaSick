using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **The mine is sited by the hill, not by the thumb (2026-10-05).**
    /// Kevin's spec: a mine stands ONLY against a mountain or plateau side --
    /// a slope steeper than a man may walk behind it, flat walkable ground
    /// the camp can reach in front -- and its ghost turns itself to face out
    /// of the slope "exactly like the Pier/dock ghost". So it has a snap
    /// (`SnapMine`, the pier's `SnapPier` in shape: the pick in, a pivot, a
    /// yaw and a reason out) and its own test (`CanPlaceMine`, asked by
    /// `CanPlace` of the snap's answer, of a blueprint and of a saved row
    /// alike, so a green ghost is a mine `Raise` will stand).
    ///
    /// **Frame.** The pivot is the entrance's front lip at ground level, +Z
    /// out of the hill (the art's contract). It is put at the FOOT of the
    /// face pushed `BuildPlans.MineBuryMetres` into it, and stood at the
    /// foot's height -- so the art's back, which extends behind the pivot,
    /// is buried in the hillside ("part of the backside of the asset should
    /// clip into the mountain side so it looks more connected").
    public partial class Outpost
    {
        /// What the ghost says when the ground has no face to dig into.
        public const string MineNeedsCliff = "needs a cliff face to dig into";

        /// Bearings tried round the tap for the nearest foot of a face.
        const int MineBearings = 16;
        const float MineStep = 0.5f;

        /// Is the ground at `p` rising, along `uphill`, steeper than a man
        /// may walk over `BuildPlans.MineCliffProbe`?
        bool MineSteepAhead(Vector3 p, Vector3 uphill)
        {
            float run = BuildPlans.MineCliffProbe;
            Vector3 q = p + uphill * run;
            return (height(q.x, q.z) - height(p.x, p.z)) / run >= Walkability.Grade(Walkability.Feet.Man);
        }

        /// What the ghost says when there is a face but too little hill to
        /// sink the mine's back into across its width (a spur, a lone knoll).
        public const string MineNeedsHillBehind = "too little hill behind it to dig into";

        /// A site the snap may offer: lip, heading, and its distance from
        /// the tap (the gradient's own bearing a little nearer).
        struct MineSite { public Vector3 pivot; public float yaw, dist; }
        readonly List<MineSite> mineSites = new List<MineSite>(24);

        // The snap's answer for the last tap, kept while the thumb is still
        // and the walls are unchanged: the ghost asks every frame.
        Vector3 mineSnapTap = new Vector3(float.NaN, 0f, 0f), mineSnapPivot;
        float mineSnapYaw;
        string mineSnapWhy;
        bool mineSnapOk;
        int mineSnapRev;

        int MineRev()
        {
            var map = CampPath.For(this);
            return unchecked((map != null ? map.WallRevision * 65599 : 0) + built.Count);
        }

        /// From `from` along `dir`, the first point within `MineSnapReach`
        /// where the ground ahead rises steeper than a man may walk.
        bool MineFaceAlong(Vector3 from, Vector3 dir, out Vector3 foot, out float t)
        {
            for (t = 0f; t <= BuildPlans.MineSnapReach; t += MineStep)
            {
                foot = from + dir * t;
                if (MineSteepAhead(foot, dir)) return true;
            }
            foot = from;
            return false;
        }

        /// A face found at `foot` going up `uphill`: squared to the face,
        /// walked up to where the hill starts rising (`MineFootRise`), the
        /// lip pushed `MineBuryMetres` in and stood at the foot's height.
        void AddMineSite(Vector3 foot, Vector3 uphill, Vector3 picked, float bias)
        {
            Vector3 onFace = foot + uphill * 1.5f;
            Vector3 faceUp = -Downhill(onFace, 1.5f);
            if (faceUp.sqrMagnitude > 0.5f && Vector3.Dot(faceUp, uphill) > 0.3f) uphill = faceUp;
            float h0 = height(foot.x, foot.z);
            for (float s = 0.25f; s <= BuildPlans.MineCliffProbe; s += 0.25f)
            {
                Vector3 q = foot + uphill * 0.25f;
                if (height(q.x, q.z) - h0 > BuildPlans.MineFootRise) break;
                foot = q;
            }
            Vector3 outward = -uphill;
            var site = new MineSite { yaw = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg };
            site.pivot = foot + uphill * BuildPlans.MineBuryMetres;
            site.pivot.y = height(foot.x, foot.z);
            Vector3 d = site.pivot - picked;
            d.y = 0f;
            site.dist = d.magnitude + bias;
            foreach (var o in mineSites)
                if ((o.pivot - site.pivot).sqrMagnitude < 0.09f && Mathf.Abs(Mathf.DeltaAngle(o.yaw, site.yaw)) < 5f)
                    return;
            mineSites.Add(site);
        }

        /// **Where a mine would go if the player points HERE.** Every foot
        /// of a face within `MineSnapReach` of the tap -- up the ground's own
        /// gradient, every `MineBearings` bearing, and the gradient's site
        /// slid along the face -- nearest first, and the first that
        /// `CanPlace` accepts is the answer (2026-10-05 fix round: the snap
        /// only ever offers a site placement will stand, so dragging along a
        /// good face no longer flickers green/red). None accepted: false,
        /// with the nearest site and its refusal (the ghost stays red THERE,
        /// saying why); no face at all: `MineNeedsCliff` at the tap.
        public bool SnapMine(Vector3 picked, out Vector3 pivot, out float yaw, out string why)
        {
            pivot = picked;
            yaw = 0f;
            why = "";
            if (!Sited || height == null) { why = "this ground was never surveyed"; return false; }
            pivot.y = height(picked.x, picked.z);

            int rev = MineRev();
            Vector3 moved = picked - mineSnapTap;
            moved.y = 0f;
            if (moved.sqrMagnitude < 0.0225f && rev == mineSnapRev)
            {
                pivot = mineSnapPivot; yaw = mineSnapYaw; why = mineSnapWhy;
                return mineSnapOk;
            }

            mineSites.Clear();
            Vector3 up = -Downhill(picked, 2f);
            bool hasUp = up.sqrMagnitude > 0.5f;
            Vector3 from = picked;
            // A tap on the face itself: down it to the foot first.
            if (hasUp && MineSteepAhead(picked, up))
            {
                Vector3 p = picked;
                for (float t = 0f; t <= BuildPlans.MineSnapReach; t += MineStep)
                {
                    p = picked - up * t;
                    if (!MineSteepAhead(p, up)) break;
                }
                from = p;
                AddMineSite(p, up, picked, -0.75f);
            }
            for (int k = -1; k < MineBearings; k++)
            {
                Vector3 dir;
                if (k < 0) { if (!hasUp) continue; dir = up; }
                else
                {
                    float a = k * Mathf.PI * 2f / MineBearings;
                    dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                }
                if (MineFaceAlong(from, dir, out Vector3 foot, out _))
                    AddMineSite(foot, dir, picked, k < 0 ? -0.75f : 0f);
            }
            // Slid along the face: a stretch too narrow right here may be
            // wide enough a few metres over.
            if (mineSites.Count > 0)
            {
                MineSite near = mineSites[0];
                foreach (var o in mineSites) if (o.dist < near.dist) near = o;
                var q = Quaternion.Euler(0f, near.yaw, 0f);
                Vector3 fwd = q * Vector3.forward, right = q * Vector3.right;
                // 0.75 m steps to 3 m each way (2026-10-05 re-verify: 1.5 m
                // steps left gaps where a tap between two good sites went red).
                for (int s = -4; s <= 4; s++)
                {
                    if (s == 0) continue;
                    Vector3 start = near.pivot + right * (s * 0.75f) + fwd * BuildPlans.MineCliffProbe;
                    if (MineFaceAlong(start, -fwd, out Vector3 foot, out _))
                        AddMineSite(foot, -fwd, picked, 0f);
                }
            }

            bool ok = false;
            if (mineSites.Count == 0) why = MineNeedsCliff;
            else
            {
                mineSites.Sort((a, b) => a.dist.CompareTo(b.dist));
                string first = null;
                for (int i = 0; i < mineSites.Count && !ok; i++)
                {
                    var c = mineSites[i];
                    if (CanPlace(BuildPlans.Mine, c.pivot, c.yaw, out string w))
                    {
                        ok = true; pivot = c.pivot; yaw = c.yaw; why = "";
                    }
                    else if (first == null) { first = w; pivot = c.pivot; yaw = c.yaw; }
                }
                if (!ok) why = first;
            }
            mineSnapTap = picked; mineSnapRev = rev;
            mineSnapPivot = pivot; mineSnapYaw = yaw; mineSnapWhy = why; mineSnapOk = ok;
            return ok;
        }

        // The ghost asks every frame: one route search per new spot (or wall
        // layer), not one a frame -- a refused search floods the grid. Since
        // the snap tries several sites, the last few answers are kept.
        struct MineWalkAnswer { public Vector3 from, to; public bool ok; }
        readonly List<MineWalkAnswer> mineWalks = new List<MineWalkAnswer>(32);
        int mineWalkRev = int.MinValue;

        /// Can a hand walk from the mouth stand to the drop spot? The grid's
        /// route when it is built, else the fire's region (`Reachable`).
        bool MineDropWalk(Vector3 stand, Vector3 drop)
        {
            var map = CampPath.For(this);
            if (map == null) return true;
            if (!map.Built) return CampPath.Reachable(this, drop);
            if (mineWalkRev != map.WallRevision) { mineWalks.Clear(); mineWalkRev = map.WallRevision; }
            foreach (var w in mineWalks)
                if ((stand - w.from).sqrMagnitude < 0.01f && (drop - w.to).sqrMagnitude < 0.01f) return w.ok;
            bool ok = map.HasRoute(stand, drop, CampPath.Walker.Hand);
            if (mineWalks.Count >= 32) mineWalks.RemoveAt(0);
            mineWalks.Add(new MineWalkAnswer { from = stand, to = drop, ok = ok });
            return ok;
        }

        // The ghost's store note, kept per spot (one long route search).
        Vector3 mineNoteAt = new Vector3(float.NaN, 0f, 0f);
        float mineNoteYaw;
        int mineNoteRev;
        string mineNote;

        /// **The ghost's non-blocking note (2026-10-05 fix round):** a mine
        /// may stand where the store's runners cannot walk (Kevin's only
        /// cliffs were 160 m out, past his palisade) -- it is placed, as any
        /// station is, but the ghost says so: `MissingWords.WalledOffFromStore`
        /// when a wall is the cause, `NoWayFromStore` otherwise, null when
        /// the store's door routes to the container's drop spot (or there is
        /// no grid to ask). The same question `SaveStationReach` asks of the
        /// standing mine, which then raises "Mine · walled off from the store".
        public string MineStoreNote(Vector3 at, float yaw)
        {
            var map = CampPath.For(this);
            if (map == null || !map.Built) return null;
            int rev = MineRev();
            Vector3 d = at - mineNoteAt;
            d.y = 0f;
            if (d.sqrMagnitude < 0.25f && Mathf.Abs(Mathf.DeltaAngle(yaw, mineNoteYaw)) < 10f && rev == mineNoteRev)
                return mineNote;
            mineNoteAt = at; mineNoteYaw = yaw; mineNoteRev = rev;
            var store = CampPiles.StoreBuildingOf(this);
            Vector3 from = store != null ? CampWorker.WorkSpot(this, store) : CampCentre;
            Vector3 drop = at + Quaternion.Euler(0f, yaw, 0f) * BuildingFactory.MineMarkLocal("DropSpot");
            drop.y = height(drop.x, drop.z);
            if (map.HasRoute(from, CampPath.FreeSpot(this, drop), CampPath.Walker.Hand)) mineNote = null;
            else mineNote = walls.Count > 0 && map.Reachable(drop)
                ? Economy.MissingWords.WalledOffFromStore : Economy.MissingWords.NoWayFromStore;
            return mineNote;
        }

        /// The hill test of `CanPlaceMine` (see there), from the lip's foot
        /// `foot` at height `h0`. `face`: the middle at least has a steep
        /// face behind it (so a refusal is about the width or the height).
        public bool MineHillBehind(Vector3 foot, Vector3 fwd, Vector3 right, float h0, out bool face)
        {
            face = false;
            float run = BuildPlans.MineCliffProbe;
            float need = Walkability.Grade(Walkability.Feet.Man) * run;
            float w = BuildPlans.MineBackHalfWidth;
            for (int i = 0; i < 3; i++)
            {
                int s = i == 0 ? 0 : i == 1 ? -1 : 1;     // the middle first
                Vector3 b = foot + right * (s * w);
                Vector3 q = b - fwd * run;
                if (height(q.x, q.z) - height(b.x, b.z) < need) return false;
                if (s == 0) face = true;
                Vector3 n = b - fwd * BuildPlans.MineBackNearDepth;
                if (height(n.x, n.z) - h0 < BuildPlans.MineBackNearRise) return false;
                Vector3 f = b - fwd * BuildPlans.MineBackFarDepth;
                if (height(f.x, f.z) - h0 < BuildPlans.MineBackFarRise) return false;
            }
            return true;
        }

        /// **Can a mine stand with its lip at `at`, facing `yaw`?** Four
        /// rules, the first that fails says why:
        /// <list type="number">
        /// <item>the foot (the lip pulled back out of the bury) is above the
        ///   island's building floor;</item>
        /// <item>behind it the hill fills the art's back across its width
        ///   (`MineHillBehind`: steep, and high enough at two depths, at the
        ///   middle and both sides) -- else `MineNeedsCliff` /
        ///   `MineNeedsHillBehind`;</item>
        /// <item>the apron in front (`MineApronDepth` deep, the footprint's
        ///   width) passes the ordinary corner test and is standable ground
        ///   the camp can walk to (`CampPath.Reachable`);</item>
        /// <item>the apron is clear of every other building and reservation.</item>
        /// </list>
        /// `lo` = `hi` = the foot's height: the root stands there, no footing.
        bool CanPlaceMine(BuildPlan plan, Vector3 at, float yaw, out string why, out float lo, out float hi)
        {
            why = "";
            var facing = Quaternion.Euler(0f, yaw, 0f);
            Vector3 fwd = facing * Vector3.forward, right = facing * Vector3.right;
            Vector3 foot = at + fwd * BuildPlans.MineBuryMetres;
            float h0 = height(foot.x, foot.z);
            lo = hi = h0;
            if (h0 < minHeight) { why = "the mouth would be down on the beach"; return false; }

            // **The hill fills the art's back across its whole width
            // (2026-10-05 fix round).** At the middle and both sides
            // (`MineBackHalfWidth`): steeper than a man may walk over
            // `MineCliffProbe` behind the lip line, and standing at least
            // `MineBackNearRise` / `MineBackFarRise` above the lip at
            // `MineBackNearDepth` / `MineBackFarDepth` back -- so the rock
            // mass reads as sunk into the hill, never stood in front of a
            // spur with a flank on the grass. The middle first: no face there
            // at all is `MineNeedsCliff`; a face too narrow or too low is
            // `MineNeedsHillBehind`.
            if (!MineHillBehind(foot, fwd, right, h0, out bool face))
            {
                why = face ? MineNeedsHillBehind : MineNeedsCliff;
                return false;
            }

            float depth = BuildPlans.MineApronDepth;
            Vector3 apron = foot + fwd * (depth * 0.5f + 0.2f);
            if (!Corners(apron, facing, plan.footprint.x, depth, false, out _, out _, out string footing))
            {
                why = footing;
                return false;
            }
            if (!Walkability.Standable(height, apron.x, apron.z, Walkability.Feet.Man))
            {
                why = "the ground in front of the mouth is too steep to work";
                return false;
            }
            if (!CampPath.Reachable(this, apron))
            {
                why = "the camp cannot walk to the mine's mouth";
                return false;
            }
            // **The drop walk (2026-10-05):** from the mouth stand out to the
            // model's own `DropSpot` (read off the prefab), on ground a man
            // stands on, by a route the camp's grid can walk.
            Vector3 stand = at + fwd * BuildPlans.MineMouthStand;
            Vector3 drop = at + facing * BuildingFactory.MineMarkLocal("DropSpot");
            drop.y = 0f;
            if (!Walkability.Standable(height, drop.x, drop.z, Walkability.Feet.Man))
            {
                why = "the container's spot is on ground too steep to stand on";
                return false;
            }
            if (!MineDropWalk(stand, drop))
            {
                why = "no walk from the mouth to the container";
                return false;
            }
            float halfDiag = 0.5f * Mathf.Sqrt(plan.footprint.x * plan.footprint.x + depth * depth);
            if (!Clear(apron, halfDiag, out string blocked)) { why = blocked; return false; }
            return true;
        }
    }
}
