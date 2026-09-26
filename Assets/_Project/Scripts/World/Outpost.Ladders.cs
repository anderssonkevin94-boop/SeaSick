using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Ladders up the cliffs (Kevin, 2026-09-27: "is there any way to
    /// implement a ladder-platform-ladder-platform structure to ascend the
    /// mountains? it could be fun to use those in a build.")**
    ///
    /// Sited like a wall: two points, the FOOT (walkable ground below a
    /// cliff) and the TOP (walkable ground above), `UI/LadderSiting`.
    /// Queued as an ordinary build row -- `planId == BuildPlans.Ladder.id`,
    /// `postA` the foot, `postB` the top, `x/z` the FOOT (where the hauler
    /// and the builder walk to, so the builders have to reach the bottom)
    /// -- stocked and hammered like anything else, then raised here.
    ///
    /// A standing chain is a LINK in the camp's map (`CampPath.RelayLinks`):
    /// hands and raiders route over it when it is genuinely shorter, and
    /// what stands on a plateau becomes reachable. Animals never ask the
    /// camp's map, so they cannot use one. A ladder round the end of a wall
    /// is a way in for a raid -- intended; the player defends it.
    public partial class Outpost
    {
        readonly List<Ladder> ladders = new List<Ladder>();

        /// Standing ladder chains at this camp. `CampPath` and `LadderClimb`
        /// read this.
        public IReadOnlyList<Ladder> Ladders => ladders;

        /// Is this queue row a ladder chain? (Not `isWall`: none of the wall
        /// rules -- post snapping, the band, the gate -- apply.)
        public static bool IsLadderRow(PendingBuild row)
            => row != null && !row.isWall && row.planId == BuildPlans.Ladder.id;

        /// Metres within which two chains' feet or tops count as the same spot.
        const float LadderSpacing = 2.5f;

        /// **Can a chain stand from `foot` to `top`?** The shape rules
        /// (`LadderLayout.Valid`), then the camp's: on the camp's map, the
        /// foot reachable by the hands (they have to build it from there),
        /// and not on top of another chain. `foot`/`top` come back in order
        /// (lower first) with heights.
        public bool CanPlaceLadder(ref Vector3 foot, ref Vector3 top, out string why)
        {
            if (ledger == null || !Sited) { why = "this ground was never surveyed"; return false; }
            if (!ledger.PlanUnlocked(BuildPlans.Palisade.id))
            { why = ledger.PlanLockReason(BuildPlans.Palisade.id); return false; }
            if (!LadderLayout.Valid(GroundAt, ref foot, ref top, out why)) return false;

            var map = CampPath.For(this);
            if (map != null && (!map.OnMap(foot) || !map.OnMap(top)))
            { why = "too far from the camp"; return false; }
            if (map != null && !map.Reachable(foot))
            { why = "the hands can't reach the foot"; return false; }
            if (map != null && map.Reachable(top))
            {
                // Already on the fire's ground: only worth it if the walk
                // round is long. (One ground-only search; the siting tool
                // asks only when an end moves.)
                float walk = map.GroundRouteMetres(foot, top);
                if (walk >= 0f && walk < 3f * LadderLayout.Flat(foot, top) + 10f)
                { why = "not a cliff — there's a walk up close by"; return false; }
            }

            foreach (var l in ladders)
                if (l != null && (LadderLayout.Flat(l.Foot, foot) < LadderSpacing
                                  || LadderLayout.Flat(l.Top, top) < LadderSpacing))
                { why = "a ladder already stands here"; return false; }
            if (ledger.sites != null)
                foreach (var r in ledger.sites)
                    if (IsLadderRow(r) && (LadderLayout.Flat(r.postA, foot) < LadderSpacing
                                           || LadderLayout.Flat(r.postB, top) < LadderSpacing))
                    { why = "a ladder is already on order here"; return false; }
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                if (w == null) continue;
                if (w.FlatDistanceTo(foot) < 1.2f || w.FlatDistanceTo(top) < 1.2f)
                { why = "too close to the wall"; return false; }
            }
            why = "";
            return true;
        }

        /// **Queue a chain.** Null with a reason if refused. Priced by the
        /// rise (`BuildPlans.LadderCost`), no stone.
        public PendingBuild SiteLadder(Vector3 foot, Vector3 top, out string why)
        {
            if (ledger == null) { why = "this ground was never surveyed"; return null; }
            ledger.MigratePending();
            if (!CanPlaceLadder(ref foot, ref top, out why)) return null;
            float rise = top.y - foot.y;
            Vector3 run = top - foot;
            run.y = 0f;
            var row = new PendingBuild
            {
                planId = BuildPlans.Ladder.id,
                postA = foot,
                postB = top,
                x = foot.x,
                z = foot.z,
                yaw = run.sqrMagnitude > 1e-4f
                    ? Quaternion.LookRotation(run.normalized, Vector3.up).eulerAngles.y : 0f,
                length = 0f,
                needed = BuildPlans.LadderCost(rise),
                phased = true,
            };
            ledger.sites.Add(row);
            if (ledger.EnlistFree() > 0 && Watched) PuppetsToWork();
            EnsureBlueprints();
            if (ledger.ReadyToRaise) FinishReady();
            why = "";
            return row;
        }

        /// The blueprint for a queued chain (`EnsureBlueprints`).
        BuildSite DrawLadderSite(PendingBuild row)
        {
            var shape = LadderLayout.Plan(GroundAt, row.postA, row.postB);
            var site = BuildSite.PlaceLadder(this, BuildPlans.Ladder, shape);
            site.Bind(row);
            site.Refresh(row);
            return site;
        }

        /// Stand a paid, built row up (`RaiseRow`). Never refuses: the shape
        /// is re-laid from the ground as it is.
        Ladder RaiseLadder(PendingBuild row)
        {
            if (!IsLadderRow(row)) return null;
            var bl = new BuiltLadder { fx = row.postA.x, fz = row.postA.z, tx = row.postB.x, tz = row.postB.z };
            if (ledger.builtLadders == null) ledger.builtLadders = new List<BuiltLadder>();
            ledger.builtLadders.Add(bl);
            return StandLadder(bl);
        }

        /// The chain from a record: drawn, listed, linked on the map.
        Ladder StandLadder(BuiltLadder bl)
        {
            if (bl == null || !Sited) return null;
            Vector3 foot = bl.Foot, top = bl.Top;
            foot.y = GroundAt(foot);
            top.y = GroundAt(top);
            if (top.y < foot.y) { var t = foot; foot = top; top = t; }
            var shape = LadderLayout.Plan(GroundAt, foot, top);
            var go = LadderLayout.Draw(transform, shape, "Ladder", true);
            var ladder = go.AddComponent<Ladder>();
            ladder.Configure(this, shape, bl);
            ladders.Add(ladder);
            var map = CampPath.For(this);
            if (map != null) map.RelayLinks();
            return ladder;
        }

        /// Called by `Ladder.TearDown`.
        public void ForgetLadder(Ladder l)
        {
            if (l == null) return;
            ladders.Remove(l);
            if (ledger != null && ledger.builtLadders != null && l.Row != null)
                ledger.builtLadders.Remove(l.Row);
            var map = CampPath.For(this);
            if (map != null) map.RelayLinks(l);
        }

        /// `Adopt`: drop the standing chains (a reload replaces them).
        void ClearLadders()
        {
            foreach (var l in ladders) if (l != null) Destroy(l.gameObject);
            ladders.Clear();
        }

        /// `Adopt`: every saved chain back up.
        void StandSavedLadders()
        {
            if (ledger == null) return;
            if (ledger.builtLadders == null) ledger.builtLadders = new List<BuiltLadder>();
            ledger.builtLadders.RemoveAll(l => l == null);
            foreach (var l in ledger.builtLadders) StandLadder(l);
        }

        /// **The chain whose one end a walker at `here` stands by and whose
        /// other end `aim` lies toward** -- the leg a route takes over a
        /// link. `up` says which way. Null when there is none. A few flat
        /// distances per chain; a camp has a handful.
        public Ladder LadderLeg(Vector3 here, Vector3 aim, float reach, out bool up)
        {
            up = true;
            if (ladders.Count == 0) return null;
            float aimY = GroundAt(aim);
            for (int i = 0; i < ladders.Count; i++)
            {
                var l = ladders[i];
                if (l == null || l.Shape == null) continue;
                Vector3 f = l.Foot, t = l.Top;
                bool atFoot = LadderLayout.Flat(here, f) <= reach && Mathf.Abs(here.y - f.y) < 1.6f;
                bool atTop = LadderLayout.Flat(here, t) <= reach && Mathf.Abs(here.y - t.y) < 1.6f;
                if (atFoot && Mathf.Abs(aimY - t.y) < Mathf.Abs(aimY - f.y)
                    && LadderLayout.Flat(aim, t) < LadderLayout.Flat(aim, f) + 1f
                    && LadderLayout.Flat(aim, t) <= LadderLayout.MaxRun + 4f)
                { up = true; return l; }
                if (atTop && Mathf.Abs(aimY - f.y) < Mathf.Abs(aimY - t.y)
                    && LadderLayout.Flat(aim, f) < LadderLayout.Flat(aim, t) + 1f
                    && LadderLayout.Flat(aim, f) <= LadderLayout.MaxRun + 4f)
                { up = false; return l; }
            }
            return null;
        }
    }
}
