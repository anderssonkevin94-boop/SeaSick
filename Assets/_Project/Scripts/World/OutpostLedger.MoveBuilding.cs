using UnityEngine;

namespace SeaSick.World
{
    /// **A building the player moved (Kevin, 2026-09-30: "I want to be able
    /// to turn and move buildings even after they're built").**
    ///
    /// The books' half of `Outpost.MoveBuilt`. Everything the ledger keys to
    /// a building -- its station row, its stock, its level, its worker, the
    /// pinned goal -- is keyed by its `raised` INDEX (or by plan id + the
    /// ordinal among that plan's rows), never by where it stands, so moving
    /// the row in place keeps all of it. Where a building stands is read
    /// live off the row (`StoreAt`, `StationPlace`, `CentreAt`, `ShoreOf`)
    /// everywhere except in one place: a trip already under way copied its
    /// two ends into `haulFromX/Z` / `haulToX/Z` when it set off. That copy
    /// is what this file puts right.
    public partial class OutpostLedger
    {
        /// **Write a moved building's new spot into its own row.** The save
        /// restores from this row exactly (buildings never move on LOAD --
        /// Kevin's 2026-09-27 rule), so the player's move is what the next
        /// load stands. `at` must be the very x/z the building's root now
        /// stands at: `StationStockView` finds its station by an exact
        /// position match against these rows. False for a bad index.
        public bool MoveRaised(int raisedIndex, Vector3 at, float yaw)
        {
            if (raised == null || raisedIndex < 0 || raisedIndex >= raised.Count) return false;
            var r = raised[raisedIndex];
            if (r == null) return false;
            r.x = at.x;
            r.z = at.z;
            r.yaw = yaw;
            return true;
        }

        /// **Re-aim every trip whose store, station or shore end moved.**
        ///
        /// Each walked trip placed its ends once, at dispatch. After a move
        /// the end that was the moved building is re-read from the books
        /// (the row already stands at the new spot), and the leg he is on
        /// right now is re-measured from where he stands -- so a hauler
        /// walking to the old bench turns for the new one, a load already on
        /// his shoulder goes to the new rack, and nobody is left walking to
        /// an empty patch of grass. The pickup and the drop-off still happen
        /// exactly where the books say (docs/DELIVERY-ON-ARRIVAL.md); nothing
        /// is dropped, refunded or re-counted. A field, a site, the ship and
        /// a ground load cannot have moved with a building and are left as
        /// they are. Returns how many trips were re-aimed.
        public int ReaimTrips()
        {
            if (hands == null) return 0;
            int n = 0;
            foreach (var h in hands)
            {
                if (h == null || !h.Hauling || !h.haulPlaced || h.tripLeg == 0) continue;
                bool fromMoved = Reaim(h.haulFrom, h.haulFromStation, ref h.haulFromX, ref h.haulFromZ);
                bool toMoved = Reaim(h.haulTo, h.haulToStation, ref h.haulToX, ref h.haulToZ);
                if (!fromMoved && !toMoved) continue;
                n++;
                if (h.Leg == TripLeg.ToPickup && fromMoved)
                    h.legLeft = RouteMetres(HandAt(h), PickPoint(h));
                else if (h.Leg == TripLeg.ToDrop && toMoved)
                    h.legLeft = RouteMetres(HandAt(h), DropPoint(h));
            }
            return n;
        }

        /// One end of a trip, re-read if it is a place a building can carry
        /// away with it. True when it actually moved.
        bool Reaim(HaulPlace place, int station, ref float x, ref float z)
        {
            if (place != HaulPlace.Store && place != HaulPlace.Station && place != HaulPlace.Shore)
                return false;
            if (!PlaceOf(place, station, null, out var at)) return false;
            if (Mathf.Abs(at.x - x) < 0.05f && Mathf.Abs(at.z - z) < 0.05f) return false;
            x = at.x;
            z = at.z;
            return true;
        }
    }
}
