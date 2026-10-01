using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **The lookout climbs onto the platform (Kevin, 2026-09-27: "make the
    /// lookouts climb up onto the tower platform").**
    ///
    /// Astra's watchtower (`Resources/Settlement/watchtower_astra`, from
    /// art-staging/watchtower-astra-lvl1-v2) carries three reference empties
    /// under its `Watchtower` node -- `Ladder_Bottom` (the rails' foot, on the
    /// ground on the plan's `front` side), `Ladder_Top` (where the rails meet
    /// the deck, 4.61 m up) and `Lookout_Anchor` (the centre of the deck's
    /// walking surface). They are read off the raised model in world space,
    /// so the tower's yaw, the wall-tower squaring (`WallTowerYaw`) and the
    /// slope sink all come along for free. A tower without them (the extruded
    /// fallback shed) has no climb: the lookout stands at its door as before.
    ///
    /// The climb itself is `LadderClimb` walking a one-flight
    /// `LadderLayout.Shape` built here (`TowerClimbShape`): ground to the
    /// rails' foot, up the rails rung by rung at `ClimbSecondsPerMetre`,
    /// across the deck to the stand point -- the cliff ladders' pose and
    /// timing exactly.
    public partial class Outpost
    {
        /// Metres out from the rails' foot the lookout walks to before he
        /// climbs (the climb covers the last of it).
        const float TowerFootOut = 0.7f;

        /// **The lookout's corner (2026-10-01, the reworked v15 `Lookout`
        /// clip):** he stands in a BACK corner of the deck, `LookoutCornerOut`
        /// m either way from `Lookout_Anchor` in the tower's own frame (+Z =
        /// the ladder side): back = away from the ladder, and `LookoutCornerSide`
        /// picks the right-hand back corner (the ladder is central, so either
        /// side clears it equally). The old stand point (anchor + 0.25 m) was
        /// inside the gun carriage; `WatchtowerGun` now steps its pivot off
        /// this corner and keeps a blind wedge over him.
        public const float LookoutCornerOut = 0.44f;
        public const float LookoutCornerSide = 1f;

        /// The deck's centre (`Lookout_Anchor`), the lookout's corner and the
        /// flat unit direction from the centre out over that corner (he faces
        /// it: over the corner post). False for a model without the marks.
        /// The frame comes from the marks themselves (ladder top -> +Z), so
        /// the tower's yaw, wall-tower squaring and Move/Turn come along.
        public static bool LookoutCorner(Building b, out Vector3 centre, out Vector3 corner, out Vector3 outward)
        {
            centre = corner = outward = Vector3.zero;
            if (!TowerMarks(b, out _, out _, out corner)) return false;
            var m = towerMarks[b.GetInstanceID()];
            centre = m.anchor.position;
            outward = corner - centre;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-6f) return false;
            outward.Normalize();
            return true;
        }

        /// **The tower's three marks in world space**, or false for a model
        /// without them. `stand` is the lookout's back corner of the deck
        /// (`LookoutCorner`), clear of the ladder hole and the gun.
        public static bool TowerMarks(Building b, out Vector3 bottom, out Vector3 top, out Vector3 stand)
        {
            bottom = top = stand = Vector3.zero;
            if (b == null) return false;
            int key = b.GetInstanceID();
            if (!towerMarks.TryGetValue(key, out var m) || m.bottom == null || m.top == null || m.anchor == null)
            {
                m = default;
                foreach (var t in b.GetComponentsInChildren<Transform>(true))
                {
                    switch (BuildingFactory.Stem(t.name))
                    {
                        case "Ladder_Bottom": m.bottom = t; break;
                        case "Ladder_Top": m.top = t; break;
                        case "Lookout_Anchor": m.anchor = t; break;
                    }
                }
                if (m.bottom == null || m.top == null || m.anchor == null) return false;
                towerMarks[key] = m;
            }
            bottom = m.bottom.position;
            top = m.top.position;
            stand = m.anchor.position;
            // The tower's own +Z (anchor -> ladder) and +X, flat.
            Vector3 front = top - stand;
            front.y = 0f;
            if (front.sqrMagnitude < 1e-4f) front = b.transform.forward;
            front.y = 0f;
            front.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, front);
            stand += (right * LookoutCornerSide - front) * LookoutCornerOut;
            return true;
        }

        struct TowerMarkSet { public Transform bottom, top, anchor; }
        static readonly Dictionary<int, TowerMarkSet> towerMarks = new Dictionary<int, TowerMarkSet>();

        /// **Where the lookout stands on the ground to start his climb**: a
        /// stride out from the rails' foot, on open ground. On a wall tower
        /// the spot must also keep `DoorWallClear` off every wall line (the
        /// 2026-09-27 stand-spot fix: a spot beside the palisade pinned him
        /// in the corner the runs make) -- tried a stride and two further
        /// out, then false (the caller falls back to `WallTowerDoor`, and the
        /// lookout simply does not climb that tower). Cached a second.
        public bool TowerLadderFoot(Building b, out Vector3 foot)
        {
            foot = Vector3.zero;
            if (!TowerMarks(b, out Vector3 bottom, out _, out _)) return false;
            int key = b.GetInstanceID();
            if (footCache.TryGetValue(key, out var hit)
                && Time.time < hit.until && hit.walls == walls.Count)
            {
                foot = hit.at;
                return hit.ok;
            }

            Vector3 outward = bottom - b.transform.position;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-4f) outward = b.transform.forward;
            outward.y = 0f;
            outward.Normalize();

            bool wallTower = IsWallTower(b);
            var map = GetComponent<CampPath>();
            bool ok = false;
            for (int step = 0; step < 3 && !ok; step++)
            {
                Vector3 p = bottom + outward * (TowerFootOut + step * 0.6f);
                if (wallTower && !DoorClearOfWalls(p)) continue;
                if (map != null && map.Built && !map.WalkableAt(p, CampPath.Walker.Hand)) continue;
                p.y = GroundAt(p);
                foot = p;
                ok = true;
            }
            footCache[key] = (foot, ok, Time.time + 1f, walls.Count);
            return ok;
        }

        readonly Dictionary<int, (Vector3 at, bool ok, float until, int walls)> footCache =
            new Dictionary<int, (Vector3, bool, float, int)>();

        /// **The climb from `foot` to the deck** as a ladder shape
        /// `LadderClimb` walks: ground to the rails' foot, one flight up the
        /// rails, across the deck to the stand point. Null without marks.
        /// `run` points from the ladder into the tower, so the climber faces
        /// the rails and hangs `LadderClimb.Standoff` outside them; the last
        /// point is offset by the same so he ends exactly on `stand`.
        public LadderLayout.Shape TowerClimbShape(Building b, Vector3 foot)
        {
            if (!TowerMarks(b, out Vector3 bottom, out Vector3 top, out Vector3 stand)) return null;
            Vector3 run = b.transform.position - bottom;
            run.y = 0f;
            if (run.sqrMagnitude < 1e-4f) run = -b.transform.forward;
            run.y = 0f;
            run.Normalize();
            // The rails start on the real ground: a tower sunk into a slope
            // or on stilts puts the marker a little off it either way.
            Vector3 rail = bottom;
            rail.y = GroundAt(rail);
            var s = new LadderLayout.Shape
            {
                foot = foot,
                top = stand,
                run = run,
                side = Vector3.Cross(Vector3.up, run).normalized,
            };
            s.path.Add(foot);
            s.path.Add(rail);
            s.path.Add(top);
            s.path.Add(stand + run * LadderClimb.Standoff);
            s.legs.Add(LadderLayout.Leg.Ground);
            s.legs.Add(LadderLayout.Leg.Climb);
            s.legs.Add(LadderLayout.Leg.Deck);
            return s;
        }
    }
}
