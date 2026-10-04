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

        /// **Where a mine would go if the player points HERE.** The nearest
        /// foot of a face within `MineSnapReach` of the tap: up the ground's
        /// own gradient first, then every `MineBearings` bearing; a tap ON
        /// the face walks back down it to the foot. The heading is the
        /// face's downhill direction (its gradient a pace up the face, on a
        /// 1.5 m stencil so a ripple does not swing it), and the pivot is the
        /// foot pushed `MineBuryMetres` into the hill at the foot's height.
        /// False with `MineNeedsCliff` and the tap itself when there is no
        /// face in reach -- the ghost stays red where the player pointed.
        public bool SnapMine(Vector3 picked, out Vector3 pivot, out float yaw, out string why)
        {
            pivot = picked;
            yaw = 0f;
            why = "";
            if (!Sited || height == null) { why = "this ground was never surveyed"; return false; }
            pivot.y = height(picked.x, picked.z);

            Vector3 up = -Downhill(picked, 2f);
            Vector3 foot = picked, uphill = Vector3.zero;
            float best = float.MaxValue;

            // A tap on the face itself: down it to the foot.
            if (up.sqrMagnitude > 0.5f && MineSteepAhead(picked, up))
            {
                Vector3 p = picked;
                for (float t = 0f; t <= BuildPlans.MineSnapReach; t += MineStep)
                {
                    p = picked - up * t;
                    if (!MineSteepAhead(p, up)) break;
                }
                foot = p; uphill = up; best = 0f;
            }
            else
            {
                for (int k = -1; k < MineBearings; k++)
                {
                    Vector3 dir;
                    if (k < 0) { if (up.sqrMagnitude < 0.5f) continue; dir = up; }
                    else
                    {
                        float a = k * Mathf.PI * 2f / MineBearings;
                        dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    }
                    for (float t = 0f; t <= BuildPlans.MineSnapReach && t < best; t += MineStep)
                    {
                        Vector3 p = picked + dir * t;
                        if (!MineSteepAhead(p, dir)) continue;
                        // The gradient's own bearing wins a near-tie: it is
                        // the face the player is looking at.
                        float score = k < 0 ? t - 0.75f : t;
                        if (score < best) { best = score; foot = p; uphill = dir; }
                        break;
                    }
                }
            }
            if (best == float.MaxValue) { why = MineNeedsCliff; return false; }

            // Square to the face: its gradient a pace up it.
            Vector3 onFace = foot + uphill * 1.5f;
            Vector3 faceUp = -Downhill(onFace, 1.5f);
            if (faceUp.sqrMagnitude > 0.5f && Vector3.Dot(faceUp, uphill) > 0.3f) uphill = faceUp;

            Vector3 outward = -uphill;
            yaw = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg;
            pivot = foot + uphill * BuildPlans.MineBuryMetres;
            pivot.y = height(foot.x, foot.z);
            return true;
        }

        /// **Can a mine stand with its lip at `at`, facing `yaw`?** Four
        /// rules, the first that fails says why:
        /// <list type="number">
        /// <item>the foot (the lip pulled back out of the bury) is above the
        ///   island's building floor;</item>
        /// <item>behind it the ground rises steeper than `Walkability.Grade
        ///   (Man)` over `MineCliffProbe` -- at the middle of the doorway and
        ///   at least one of its two sides -- else `MineNeedsCliff`;</item>
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

            float run = BuildPlans.MineCliffProbe;
            float need = Walkability.Grade(Walkability.Feet.Man) * run;
            float side = plan.footprint.x * 0.3f;
            bool mid = false;
            int sides = 0;
            for (int s = -1; s <= 1; s++)
            {
                Vector3 b = foot + right * (s * side);
                float hb = height(b.x, b.z);
                Vector3 q = b - fwd * run;
                bool steep = height(q.x, q.z) - hb >= need;
                if (s == 0) mid = steep;
                else if (steep) sides++;
            }
            if (!mid || sides < 1) { why = MineNeedsCliff; return false; }

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
            float halfDiag = 0.5f * Mathf.Sqrt(plan.footprint.x * plan.footprint.x + depth * depth);
            if (!Clear(apron, halfDiag, out string blocked)) { why = blocked; return false; }
            return true;
        }
    }
}
