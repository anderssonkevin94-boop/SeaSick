using UnityEngine;

namespace SeaSick.Combat
{
    /// One body of a landing party: walks to whatever pile the camp has the
    /// most of, takes one unit off it, carries it back to the ship, repeats
    /// — the mirror image of `World.CampWorker`, and it borrows that file's
    /// `Walk`/pile-spot math on purpose so a raider crosses the same ground
    /// the same way a hand does.
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

        const float Speed = 2.6f;
        const float TakeSeconds = 0.8f;
        const float PileRadius = 5.2f;
        const float PileStandOff = 0.9f;

        string carrying;      // resource on the shoulder, or the one being taken
        float takeTimer;
        float fleeTimer;

        /// The ship is sunk or has broken off: drop what you're holding back
        /// on the pile (a raid that failed shouldn't quietly keep the loot)
        /// and run for the water.
        public void Flee()
        {
            if (phase == Phase.Fleeing || phase == Phase.Recalled) return;
            DropCarried();
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
            phase = Phase.Recalled;
        }

        void DropCarried()
        {
            if (string.IsNullOrEmpty(carrying)) return;
            camp.Ledger.Add(carrying, 1);
            carrying = null;
            Act(World.VillagerActing.Mode.None, null);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            switch (phase)
            {
                case Phase.ToPile:
                    if (string.IsNullOrEmpty(carrying) && !PickTarget()) { phase = Phase.Recalled; break; }
                    if (Walk(PileSpot(carrying), dt)) { phase = Phase.Taking; takeTimer = 0f; }
                    break;

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
                    if (Walk(site.shore, dt))
                    {
                        party.Delivered(carrying);
                        carrying = null;
                        Act(World.VillagerActing.Mode.None, null);
                        phase = Phase.ToPile;
                    }
                    break;

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
            float a = Mathf.Abs(resource.GetHashCode() % 360) * Mathf.Deg2Rad;
            var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 at = camp.CampCentre + outward * (PileRadius - PileStandOff);
            at.y = camp.GroundAt(at);
            return at;
        }

        void Act(World.VillagerActing.Mode mode, string res) =>
            World.VillagerActing.On(GetComponent<Crew.CrewAgent>())?.Set(mode, res);

        // --- moving and facing, copied from CampWorker.Walk/Face ------------

        bool Walk(Vector3 to, float dt)
        {
            Vector3 here = transform.position;
            Vector3 d = to - here;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist < 0.35f) return true;

            Vector3 step = d / dist * Mathf.Min(Speed * dt, dist);
            Vector3 next = here + step;
            next.y = camp.GroundAt(next);
            transform.position = next;
            Face(d, dt);
            return false;
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
