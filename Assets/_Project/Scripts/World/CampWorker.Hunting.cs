using UnityEngine;

namespace SeaSick.World
{
    /// **The hunt, walked (2026-09-27, docs/DELIVERY-ON-ARRIVAL.md).**
    ///
    /// Kevin: *"it should be them walking up to an animal, jabbing it with
    /// its spear, picking it up and walking back. There should be no
    /// arbitrary timer."* The body IS the hunter's walker while the camp is
    /// watched; the ledger's hunt trip (`OutpostLedger.Hunting`) only says
    /// which leg he is on and books the two events:
    /// - **No trip = home.** The books start one only with a spear, a beast
    ///   nobody else is on and room for meat or hide.
    /// - **`ToPickup`: walk up to the beast** he claimed (the real one; it
    ///   grazes, he follows), to arm's length, then `BodyArrived`.
    /// - **`AtPickup`: the jab** (`OutpostLedger.JabSeconds`, a pose). Its end
    ///   is the kill: the herd loses one in the books and
    ///   `Outpost.SyncHunting` drops the claimed beast.
    /// - **`ToDrop`: he shoulders the animal's own body**
    ///   (`HunterProps.Shoulder`) and walks it to the store; on arrival
    ///   `BodyArrived` puts 4 Food and 1 Hide in the store, and he sets the
    ///   carcass down.
    public partial class CampWorker
    {
        /// Seconds of stoop at the store as the carcass goes down.
        const float SetDownSeconds = 0.8f;
        /// Roughly the middle of a goat's flank above its feet, metres.
        const float FlankHeight = 0.35f;
        /// Longest he waits by a killed beast for the scene to drop it.
        const float FallWaitSeconds = 3f;

        HunterProps hunterProps;
        float setDownLeft = -1f;
        float fallWait;

        void TickHunting(OutpostHand r, float dt)
        {
            var ledger = camp != null ? camp.Ledger : null;
            string spear = ledger != null ? ledger.SpearInHand() : null;
            if (hunterProps == null) hunterProps = HunterProps.On(gameObject);
            var props = hunterProps;
            var view = ledger != null ? ledger.HaulOf(r) : default;
            bool trip = r.HuntTrip && view.active;

            // --- the carcass just went into the store: set it down --------
            // (Also a carcass left on his shoulders by a trip that ended
            // some other way.) Runs before the next trip's walk, which the
            // books may already have planned in the same frame.
            if (setDownLeft >= 0f || (!trip && props.HasCarcass))
            {
                props.Drive(spear, HunterProps.Pose.Upright);
                if (setDownLeft < 0f) setDownLeft = SetDownSeconds;
                acting?.Set(VillagerActing.Mode.Bend);
                setDownLeft -= dt;
                if (setDownLeft > 0f) return;
                props.PutDown();
                setDownLeft = -1f;
                Drop();
                phase = Phase.Resting;
                wait = 0f;
                return;
            }
            setDownLeft = -1f;

            // --- no hunt in the books: home ------------------------------
            if (!trip)
            {
                Unclaim();
                props.Drive(spear, HunterProps.Pose.Upright);
                acting?.Set(VillagerActing.Mode.None);
                phase = Phase.Resting;
                if (Walk(home, dt)) FaceRest(dt, 0f);
                return;
            }

            // --- carrying it home ----------------------------------------
            if (view.leg == TripLeg.ToDrop || view.leg == TripLeg.AtDrop)
            {
                // The beast he jabbed goes over in the scene a moment after
                // the books kill it; wait for it (briefly) and lift it.
                if (quarry != null && !props.HasCarcass)
                {
                    Vector3 at = quarry.transform.position;
                    if (!quarry.Down && fallWait < FallWaitSeconds)
                    {
                        fallWait += dt;
                        Face(at - transform.position, dt);
                        props.Drive(spear, HunterProps.Pose.Thrust, at + Vector3.up * FlankHeight * 0.5f);
                        acting?.Set(VillagerActing.Mode.Bend);
                        return;
                    }
                    if (quarry.Down) props.Shoulder(quarry);
                    quarry = null;           // not Unclaim: it is a carcass now
                }
                fallWait = 0f;
                phase = Phase.Coming;
                props.Drive(spear, HunterProps.Pose.Upright);
                acting?.Set(VillagerActing.Mode.None);
                carrying = Res.Food;
                dropAt = Dropoff(r, Res.Food);
                if (!Walk(dropAt, dt)) return;
                // The drop-off event: meat and hide into the store now; he
                // stoops and the carcass goes down.
                ledger.BodyArrived(r);
                setDownLeft = SetDownSeconds;
                return;
            }
            fallWait = 0f;

            // --- walking up to it / the jab --------------------------------
            if (quarry == null || quarry.Dead)
            {
                if (!ClaimQuarry())
                {
                    // No beast drawn here (the herd is not loaded, or every
                    // one has a man on it): walk to where the books last saw
                    // the herd, and count that as reaching it.
                    phase = Phase.Going;
                    props.Drive(spear, HunterProps.Pose.Upright);
                    acting?.Set(VillagerActing.Mode.None);
                    if (view.leg == TripLeg.ToPickup)
                    {
                        Vector3 p = view.fromAt;
                        p.y = camp.GroundAt(p);
                        if (Walk(p, dt)) ledger.BodyArrived(r);
                    }
                    else ledger.BodyWorked(r, dt * ClockRate());
                    return;
                }
            }

            Vector3 beast = quarry.transform.position;
            if (view.leg == TripLeg.ToPickup)
            {
                phase = Phase.Going;
                props.Drive(spear, HunterProps.Pose.Upright);
                acting?.Set(VillagerActing.Mode.None);
                if (!Near(beast, HuntReach + 0.35f)) { Walk(StandOffFrom(beast, HuntReach), dt); return; }
                r.walkingIn = false;
                ledger.BodyArrived(r);
                return;
            }

            // AtPickup: the jab.
            phase = Phase.Working;
            r.walkingIn = false;
            Face(beast - transform.position, dt);
            props.Drive(spear, HunterProps.Pose.Thrust, beast + Vector3.up * FlankHeight);
            acting?.Set(VillagerActing.Mode.Bend);
            ledger.BodyWorked(r, dt * ClockRate());
        }

        /// A point `off` metres from a beast, on the side he is standing on.
        Vector3 StandOffFrom(Vector3 beast, float off)
        {
            Vector3 away = transform.position - beast;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            Vector3 p = beast + away.normalized * off;
            p.y = camp != null ? camp.GroundAt(p) : p.y;
            return p;
        }
    }
}
