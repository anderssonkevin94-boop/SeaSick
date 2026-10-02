using UnityEngine;
using SeaSick.World.Life;

namespace SeaSick.World
{
    /// <summary>
    /// **The pouting hand's own walk (death/rescue phase 4, 2026-09-28).**
    ///
    /// docs/PLAN-DEATH-RESCUE.md, "Neglect": a grieving hand walks to the
    /// fire and stands there (since 2026-10-02 only after a death at the
    /// camp, half a sky day -- Kevin: *"pouting only happens when someone
    /// dies"*). Deciding WHO pouts and for how long is the ledger's job
    /// (`OutpostLedger.PoutStep`, plain data, game time); this file is the
    /// body walking it, same division `CampWorker.Rescue.cs` keeps for the
    /// drag.
    ///
    /// **Watched-only, same as the drag** (D2): this only runs inside
    /// `Update`, which only exists for a body that is here. Off screen the
    /// pout is books only and its clock still runs (`Step`).
    /// </summary>
    public partial class CampWorker
    {
        /// **This body is pouting at the fire.** True = handled this frame
        /// (the caller returns without running the ordinary order
        /// dispatch), the same contract `TickRescue` keeps.
        bool TickPout(OutpostHand r, float dt)
        {
            if (r == null || !r.pouting) return false;

            // **No meal to walk any more (2026-10-02).** He used to leave
            // the pout for a store-and-back meal trip; since supper is
            // served at the fire he eats where he sulks: a bowl being eaten
            // plays out here (this branch outranks `TickDelivery`).
            if (eatLeft > 0f) { TickDelivery(dt); return true; }

            // **At the ring, not in the flames (2026-09-30):** every pouter
            // used to walk to the fire's own centre and stand stacked inside
            // it. His own spot on the ring, as in the evening.
            Vector3 fire = FireRingSpot(r);
            Face(camp.CampCentre - transform.position, dt);

            // **No fitting `VillagerActing` mode for "arms crossed, sulking"
            // exists yet** (the set is Chop/Saw/Hammer/Hoe/Stir/Carry/
            // Dangle/Land/Bend -- none of them read as a pout), so this is
            // the "plain idle facing the fire" the plan allows for instead.
            // A later pass can swap this for a real pose without touching
            // anything here.
            acting?.Set(VillagerActing.Mode.None);

            if (!Near(fire, HideArriveMetres) && !Walk(fire, dt)) { phase = Phase.Going; return true; }

            phase = Phase.Resting;
            // Sulking, but not skipping supper: at the ring after the bell
            // he eats what the books served him, like the rest.
            if (CampLifeTuning.PhaseAtHour(TimeOfDay.Hour) == CampLifeTuning.RoutinePhase.Evening)
                TrySupperBite(r);
            return true;
        }
    }
}
