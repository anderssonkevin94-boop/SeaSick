using UnityEngine;

namespace SeaSick.World
{
    /// **A building moved under the bodies (Kevin, 2026-09-30: "I want to
    /// be able to turn and move buildings even after they're built").**
    ///
    /// `Outpost.MoveBuilt` stands the same building somewhere else in one
    /// frame. Most of what a body does re-reads the building live (its work
    /// spot, its markers, the pad gate, the route to a target that moved), so
    /// it simply walks to the new place. What a body had COPIED is put right
    /// here: the two ends of the trip it is miming (resolved once per trip),
    /// a lookout standing on the tower's deck, a sleeper hidden inside the
    /// hut, and a hand lying by the fire.
    public partial class CampWorker
    {
        /// Tell every body of `camp` that `b` was moved from `oldRoot`.
        public static void BuildingMoved(Outpost camp, Building b, Vector3 oldRoot)
        {
            if (camp == null || b == null) return;
            for (int i = Bodies.Count - 1; i >= 0; i--)
            {
                var w = Bodies[i];
                if (w != null && w.camp == camp) w.OnBuildingMoved(b, oldRoot);
            }
        }

        void OnBuildingMoved(Building b, Vector3 oldRoot)
        {
            // **On the tower's deck, he rides it**; half-way up or down its
            // ladder he is let off on the ground and climbs the tower where
            // it stands now (the ordinary shift walk does that).
            if (towerOn == b)
            {
                if (tower == TowerState.Top && Outpost.TowerMarks(b, out _, out _, out Vector3 deck))
                    transform.position = deck;
                else
                {
                    OffTower();
                    Vector3 p = transform.position;
                    p.y = Ground(p);
                    transform.position = p;
                }
            }

            // **Asleep inside this hut**: the hidden body is at the old door;
            // put it at the new one, so he wakes where the hut is.
            if (asleep && row != null && row.sleepHutId == b.GetInstanceID())
                transform.position = Grounded(WorkSpot(camp, b));

            // **Lying in the ring round the fire**: the ring moved with it.
            if (lyingByFire && b.Kind == BuildKind.Fire)
            {
                Vector3 p = transform.position + (b.transform.position - oldRoot);
                p.y = Ground(p);
                transform.position = p;
            }

            // **The trip he is miming**: its store/station/shore end was
            // re-read by the books (`OutpostLedger.ReaimTrips`, which runs
            // first); re-aim the spots the body walks to and faces.
            if (row != null && camp != null && camp.Ledger != null
                && row.Hauling && row.haulSerial == mimedTrip)
            {
                var view = camp.Ledger.HaulOf(row);
                if (view.active) AimTripMime(row, view);
            }

            ClearRoute();
        }
    }
}
