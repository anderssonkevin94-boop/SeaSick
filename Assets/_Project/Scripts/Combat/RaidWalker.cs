using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Combat
{
    /// One body of a landing party: walks to whatever pile the camp has the
    /// most of, takes one unit off it, carries it back to the ship, repeats
    /// — the mirror image of `World.CampWorker`, and it borrows that file's
    /// `Walk`/pile-spot math on purpose so a raider crosses the same ground
    /// the same way a hand does.
    ///
    /// Since 2026-09-23 that borrowing includes the *routing*: a raider asks
    /// `World.CampPath` for corners exactly the way `CampWorker.Walk` does,
    /// but as `World.CampPath.Walker.Raider`, for whom walls AND gates are solid
    /// (PLAN-fortress-harbour D2 "raiders never climb", D3 "gates are closed
    /// to raiders"). When that map says there is no way in at all, he does
    /// the only thing left: he picks a segment and breaks it. See `Breach`.
    ///
    /// Owns no state the camp needs to agree with: everything it touches
    /// (`OutpostLedger.Take`/`Add`) is a real transfer at the moment it
    /// happens, so killing the ship or the party mid-carry never has to
    /// reconcile a promise against the books — whatever a walker is holding
    /// when he stops existing is exactly what the ledger already paid out.
    public class RaidWalker : MonoBehaviour
    {
        public enum Phase { ToPile, Taking, ToShip, Fleeing, Recalled }

        // Set by RaidParty right after spawning, before this body is active.
        public RaidParty party;
        public World.Outpost camp;
        public RaidSite site;

        public Phase phase = Phase.ToPile;

        /// Where `RaidParty` put him on the sand. Re-applied on his first
        /// frame, because `CrewAgent.Start` (which runs after the party
        /// placed him, before this Update) snaps a crew body to its deck post
        /// -- ship-local coordinates that, for a parentless raider, are a spot
        /// beside the world origin. Kevin, 2026-09-26: *"I never see the
        /// raiders walking on land"* -- they were walking along the sea bed
        /// from (0, 0) towards the camp.
        public Vector3 landAt;
        bool landed;

        const float Speed = 2.6f;
        const float TakeSeconds = 0.8f;
        const float PileRadius = 5.2f;
        const float PileStandOff = 0.9f;

        // --- breaching dials --------------------------------------------------

        /// **Seconds for ONE raider to break a fresh segment.** The damage a
        /// raider does is `seg.MaxHp / breachSoloSeconds` per second, so the
        /// number that matters is this one and the wall's own HP is read off
        /// the segment rather than assumed here — a stone wall with four
        /// times a palisade's HP takes four times as long from the same dial,
        /// and a gate (cheap, 4 timber) is softer for free because its own
        /// `MaxHp` is lower.
        ///
        /// 25 s solo is the shape Phase 1 asked for: one man hacking at a
        /// palisade is a long, visible, answerable thing; the whole party of
        /// four concentrating on the SAME segment (see
        /// `RaidParty.BreachSegment`) shares the work and is through in
        /// 25/4 ≈ 6 s. A camp with a manned tower covering the wall gets
        /// those six seconds to do something about it.
        [SerializeField] float breachSoloSeconds = 25f;

        /// Close enough to the segment's line to be swinging at it.
        const float BreachReach = 2f;

        /// How far outside a segment a raider stands to work on it.
        const float BreachStandOff = 1.5f;

        /// How often the "can I even get there?" question is re-asked. Cheap
        /// (a whole A* with the raider mask), so not every frame; short
        /// enough that the frame a hole opens, he notices within half a
        /// second and pours through.
        const float ReachCheckSeconds = 0.5f;

        string carrying;      // resource on the shoulder, or the one being taken
        float takeTimer;
        float fleeTimer;

        // breaching state
        World.WallSegment breaking;   // the segment this body is working on, or null
        bool blocked;                 // last reach check said "no way in"
        float reachTimer;
        Vector3 reachedFor;           // the goal the last reach check was about

        /// True while he is hacking at a wall rather than walking. Read by
        /// nothing yet; here because "why is that man standing still?" is the
        /// first question anybody debugging a raid asks.
        public bool IsBreaching => breaking != null;

        /// The ship is sunk or has broken off: drop what you're holding back
        /// on the pile (a raid that failed shouldn't quietly keep the loot)
        /// and run for the water.
        public void Flee()
        {
            if (phase == Phase.Fleeing || phase == Phase.Recalled) return;
            DropCarried();
            StopBreaking();
            phase = Phase.Fleeing;
            fleeTimer = 0f;
        }

        /// The ship is withdrawing under its own power: walk back and board.
        /// Unlike `Flee`, whatever is on the shoulder still counts as taken
        /// -- the raid succeeded at carrying it, even if it didn't get all
        /// the way to the hull yet.
        public void Recall()
        {
            if (phase == Phase.Fleeing || phase == Phase.Recalled) return;
            StopBreaking();
            phase = Phase.Recalled;
        }

        void DropCarried()
        {
            if (string.IsNullOrEmpty(carrying)) return;
            camp.Ledger.Add(carrying, 1);
            carrying = null;
            Act(World.VillagerActing.Mode.None, null);
        }

        // A walker only exists once he's on the sand (see RaidParty.Begin),
        // so "enabled" already means "ashore" -- RaiderMarkerField owns the
        // rest of the little red triangle over him.
        void OnEnable() => RaiderMarkerField.Enter(this);
        void OnDisable() => RaiderMarkerField.Leave(this);

        void Update()
        {
            if (!landed)
            {
                landed = true;
                transform.position = landAt;
            }

            float dt = Time.deltaTime;
            switch (phase)
            {
                case Phase.ToPile:
                {
                    if (string.IsNullOrEmpty(carrying) && !PickTarget()) { phase = Phase.Recalled; break; }
                    Vector3 goal = PileSpot(carrying);
                    if (Barred(goal, dt)) { TickBreach(dt); break; }
                    if (Walk(goal, dt)) { phase = Phase.Taking; takeTimer = 0f; }
                    break;
                }

                case Phase.Taking:
                    takeTimer += dt;
                    if (takeTimer < TakeSeconds) break;
                    if (camp.Ledger.Take(carrying, 1) == 1)
                    {
                        Act(World.VillagerActing.Mode.Carry, carrying);
                        phase = Phase.ToShip;
                    }
                    else
                    {
                        // Somebody -- or another raider -- emptied the pile
                        // while he waited. Pick again rather than steal air.
                        carrying = null;
                        phase = PickTarget() ? Phase.ToPile : Phase.Recalled;
                    }
                    break;

                case Phase.ToShip:
                    // Loot in hand and the hole behind him repaired: he breaks
                    // his way back out rather than standing in the village
                    // holding a log forever.
                    if (Barred(site.shore, dt)) { TickBreach(dt); break; }
                    if (Walk(site.shore, dt))
                    {
                        party.Delivered(carrying);
                        carrying = null;
                        Act(World.VillagerActing.Mode.None, null);
                        phase = Phase.ToPile;
                    }
                    break;

                // Fleeing and Recalled deliberately never breach: a man who
                // has been told to leave leaves, and `Walk`'s straight-line
                // fallback guarantees he can (PLAN Phase 1, note 6 -- no
                // raider stands forever).
                case Phase.Fleeing:
                    if (Walk(site.water, dt))
                    {
                        fleeTimer += dt;
                        if (fleeTimer >= 3f) Destroy(gameObject);
                    }
                    break;

                case Phase.Recalled:
                    if (Walk(site.shore, dt))
                    {
                        if (!string.IsNullOrEmpty(carrying)) party.Delivered(carrying);
                        Destroy(gameObject);
                    }
                    break;
            }
        }

        /// First resource `CampLoading.BestFirst` has any of. False when the
        /// camp is empty and there is nothing left worth walking to.
        bool PickTarget()
        {
            var order = World.CampLoading.BestFirst;
            for (int i = 0; i < order.Length; i++)
            {
                if (camp.Ledger.CountOf(order[i]) > 0) { carrying = order[i]; return true; }
            }
            carrying = null;
            return false;
        }

        /// Same formula as `CampWorker.PileSpot` -- a raider walks to the
        /// same stack a hand would, because it's the same pile.
        Vector3 PileSpot(string resource)
        {
            // **The hut, once one stands** (2026-09-24): the goods moved off
            // the fire ring into the Storage/Storehouse (`CampPiles`), so the
            // raider goes where the goods are -- a stride outside the hut on
            // his own side of it.
            var hut = World.CampPiles.StoreBuildingOf(camp);
            if (hut != null)
            {
                Vector3 face = hut.transform.position;
                Vector3 dir = transform.position - face; dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
                Vector3 h = face + dir.normalized * 2.4f;
                h.y = camp.GroundAt(h);
                return h;
            }
            float a = Mathf.Abs(resource.GetHashCode() % 360) * Mathf.Deg2Rad;
            var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 at = camp.CampCentre + outward * (PileRadius - PileStandOff);
            at.y = camp.GroundAt(at);
            return at;
        }

        void Act(World.VillagerActing.Mode mode, string res) =>
            World.VillagerActing.On(GetComponent<Crew.CrewAgent>())?.Set(mode, res);

        // --- barred, and breaking through --------------------------------------

        /// **Is there no way to `goal` at all?** Asked at most twice a second
        /// and only from the phases that are allowed to answer it with an axe.
        ///
        /// A `true` here is a strong claim -- the raider mask blocks walls and
        /// gates, so it means the village is *sealed* -- and it is the only
        /// thing that puts a man on a wall. A false, including "there is no
        /// map for this island", leaves the old behaviour exactly as it was.
        bool Barred(Vector3 goal, float dt)
        {
            var map = World.CampPath.For(camp);
            if (map == null) { blocked = false; StopBreaking(); return false; }

            reachTimer += dt;
            bool stale = reachTimer >= ReachCheckSeconds
                || Vector3.SqrMagnitude(new Vector3(goal.x - reachedFor.x, 0f, goal.z - reachedFor.z)) > 6.25f;

            // A segment somebody else finished (or the camp rebuilt) is not a
            // reason to keep swinging: re-ask now.
            if (breaking != null && breaking.Breached) stale = true;

            if (stale)
            {
                reachTimer = 0f;
                reachedFor = goal;
                blocked = !map.HasRoute(transform.position, goal, World.CampPath.Walker.Raider);
                if (!blocked) StopBreaking();
            }

            if (!blocked) return false;

            // Sealed -- but only if the party can actually name a segment to
            // break. If it cannot (no walls at all, nothing reachable), fall
            // back to walking, which falls back to a straight line.
            var seg = party != null ? party.BreachSegment(transform.position) : null;
            if (seg == null) { StopBreaking(); return false; }

            breaking = seg;
            return true;
        }

        /// Walk to a point 1.5 m outside the chosen segment, then swing. The
        /// whole party is handed the SAME segment by `RaidParty`, so four men
        /// pile onto one stretch of palisade instead of each opening his own
        /// private door.
        void TickBreach(float dt)
        {
            var seg = breaking;
            if (seg == null || seg.Breached) { StopBreaking(); return; }

            Vector3 here = transform.position;
            if (DistanceToSegment(here, seg) > BreachReach)
            {
                Act(World.VillagerActing.Mode.None, null);
                Walk(OutsidePoint(seg), dt);
                return;
            }

            // In reach: face the wall and work.
            Vector3 face = Nearest(here, seg) - here;
            Face(face, dt);
            Act(World.VillagerActing.Mode.Hammer, null);
            seg.Damage(Mathf.Max(1f, seg.MaxHp) / Mathf.Max(1f, breachSoloSeconds) * dt);

            if (seg.Breached)
            {
                // The hole is open. Drop the route on the floor so the next
                // frame plans through it rather than following corners that
                // were drawn around a wall that is no longer there.
                StopBreaking();
                blocked = false;
                reachTimer = ReachCheckSeconds;   // re-ask immediately
                ClearRoute();
                party?.BreachOpened(seg);
            }
        }

        void StopBreaking()
        {
            if (breaking == null) return;
            breaking = null;
            ClearRoute();
            // Put the shoulder load back on if he was carrying when he was
            // stopped; otherwise stand normal.
            Act(string.IsNullOrEmpty(carrying) ? World.VillagerActing.Mode.None
                                               : World.VillagerActing.Mode.Carry, carrying);
        }

        /// A point `BreachStandOff` metres on the OUTSIDE of the segment --
        /// outside meaning the side away from the camp centre, which for a
        /// ring wall is the side a landing party is already on.
        Vector3 OutsidePoint(World.WallSegment seg) => OutsidePoint(seg, camp);

        public static Vector3 OutsidePoint(World.WallSegment seg, World.Outpost camp)
        {
            Vector3 a = seg.A, b = seg.B;
            Vector3 mid = (a + b) * 0.5f;
            Vector3 along = b - a;
            along.y = 0f;
            Vector3 normal = new Vector3(-along.z, 0f, along.x);
            if (normal.sqrMagnitude < 0.0001f) normal = Vector3.forward;
            normal.Normalize();

            Vector3 outward = mid - camp.CampCentre;
            outward.y = 0f;
            if (Vector3.Dot(normal, outward) < 0f) normal = -normal;

            Vector3 at = mid + normal * BreachStandOff;
            at.y = camp.GroundAt(at);
            return at;
        }

        public static Vector3 Nearest(Vector3 p, World.WallSegment seg)
        {
            Vector3 a = seg.A, b = seg.B;
            Vector3 ab = b - a;
            ab.y = 0f;
            Vector3 ap = p - a;
            ap.y = 0f;
            float len2 = ab.sqrMagnitude;
            if (len2 < 0.0001f) return a;
            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / len2);
            return a + ab * t;
        }

        public static float DistanceToSegment(Vector3 p, World.WallSegment seg)
        {
            Vector3 n = Nearest(p, seg);
            float dx = n.x - p.x, dz = n.z - p.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // --- moving and facing, copied from CampWorker.Walk/Face ------------
        //
        // Same shape as `CampWorker`, deliberately duplicated rather than
        // called: that file's Walk is bound to a hand's errand state, and the
        // one difference that matters here -- `Walker.Raider` instead of
        // `Walker.Hand` -- is the whole point of the raid.

        readonly List<Vector3> route = new List<Vector3>();
        int routeAt;
        Vector3 routeFor;
        bool hasRoute;
        float routeAge;

        const float RePlanSeconds = 1.2f;
        const float RePlanMoved = 2.5f;
        const float CornerReach = 1.4f;

        void ClearRoute()
        {
            route.Clear();
            routeAt = 0;
            hasRoute = false;
        }

        bool Walk(Vector3 to, float dt)
        {
            Vector3 here = transform.position;
            Vector3 d = to - here;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist < 0.35f) { ClearRoute(); return true; }

            Vector3 aim = NextCorner(here, to, dist, dt);

            Vector3 leg = aim - here;
            leg.y = 0f;
            float legLen = leg.magnitude;
            if (legLen < 0.0001f) return false;

            Vector3 step = leg / legLen * Mathf.Min(Speed * dt, legLen);
            Vector3 next = here + step;
            // **Raiders climb no better than the hands (2026-09-27).** Same
            // backstop as `CampWorker.Walk`, same numbers (`World.Walkability`):
            // the straight-line fallback used to carry a party up a cliff
            // face. Refused, he waits for a re-plan; close to the goal, or
            // after a couple of seconds, that is as far as he gets.
            if (!World.Walkability.MayStep(camp, here, next, World.Walkability.Feet.Man))
            {
                routeAge = Mathf.Max(routeAge, RePlanSeconds - 0.5f);
                slopeStuck += dt;
                if (dist < 4f || slopeStuck > 2f) { slopeStuck = 0f; ClearRoute(); return true; }
                Face(leg, dt);
                return false;
            }
            slopeStuck = 0f;
            next.y = camp.GroundAt(next);
            transform.position = next;
            Face(leg, dt);
            return false;
        }

        float slopeStuck;

        Vector3 NextCorner(Vector3 here, Vector3 to, float dist, float dt)
        {
            if (dist < 6f) { ClearRoute(); return to; }

            routeAge += dt;

            bool stale = !hasRoute
                || routeAt >= route.Count
                || routeAge >= RePlanSeconds
                || Vector3.SqrMagnitude(new Vector3(to.x - routeFor.x, 0f, to.z - routeFor.z))
                       > RePlanMoved * RePlanMoved;

            if (stale && World.CampPath.Budget())
            {
                var map = World.CampPath.For(camp);
                routeAge = 0f;
                routeFor = to;
                routeAt = 0;
                hasRoute = map != null
                    && map.Route(here, to, World.CampPath.Walker.Raider, route)
                    && route.Count > 0;
                if (!hasRoute) route.Clear();
            }

            if (!hasRoute || routeAt >= route.Count) return to;

            while (routeAt < route.Count - 1)
            {
                Vector3 c = route[routeAt];
                float dx = c.x - here.x, dz = c.z - here.z;
                if (dx * dx + dz * dz > CornerReach * CornerReach) break;
                routeAt++;
            }

            return routeAt >= route.Count - 1 ? to : route[routeAt];
        }

        void Face(Vector3 dir, float dt)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir.normalized, Vector3.up),
                1f - Mathf.Exp(-8f * dt));
        }
    }
}
