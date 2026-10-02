using UnityEngine;
using SeaSick.World.Life;

namespace SeaSick.World
{
    /// <summary>
    /// **Pout + floor (death/rescue phase 4, 2026-09-28; grief only since
    /// 2026-10-02; RETIRED later on 2026-10-02 -- nobody pouts at all,
    /// see `PoutStep`. The history below is how it used to work.)**,
    /// docs/PLAN-DEATH-RESCUE.md, "Neglect": nobody deserts.
    ///
    /// Kevin, 2026-10-02: *"pouting only happens when someone dies. its too
    /// harsh of a punishment that occurs too often. when someone dies they
    /// pout for half a day."* So low mood no longer starts a pout (it still
    /// slows a hand, `WorkFactor`'s `StarvingWorkFloor`); a death at the camp
    /// (`Die`) marks every survivor `griefPending`, and `PoutStep` walks
    /// them to the fire for half a sky day each, as the floor allows --
    /// `order`/`target` are never touched, he goes back to what he was doing.
    ///
    /// **Game time, from `Step`** (was real seconds, watched only): a pout
    /// runs out off screen and in a time-away catch-up too, so nobody comes
    /// back to a camp still sulking over something hours old.
    /// </summary>
    public partial class OutpostLedger
    {
        /// **The floor** (`LifeTuning.MinHandsFloor`): pouting may never
        /// drop the number of hands who are NOT downed/recovering/dragged/
        /// rescuing/pouting below this. Shared by the tick and by the dev
        /// panel's "force a pout now" button, so a manual test can never
        /// do what the real thing would refuse to.
        static bool FloorHolds(int freeAfter) => freeAfter >= LifeTuning.MinHandsFloor;

        int FreeHandCount()
        {
            int n = 0;
            if (hands != null) foreach (var h in hands) if (h != null && !h.Busy) n++;
            return n;
        }

        /// Half a sky day (Kevin's "half a day"), in game seconds -- read
        /// off `TimeOfDay.DayLength`, so it follows the day length.
        public static float GriefPoutSeconds => 0.5f * TimeOfDay.DayLength;

        /// **Pouting is retired (2026-10-02, second pass).** Kevin: *"we
        /// need to rework the villagers quitting or being too unhappy to
        /// work. the micro management is too much ... see it as age of
        /// empires style. you assign someone somewhere, thats what they
        /// do."* Nobody starts a pout any more, not even after a death; this
        /// only ENDS one that an older build or an old save left running
        /// and drops any grief still owed, so a loaded camp is back at work
        /// on its first tick. Runs every quantum, even a zero-length one
        /// (no `workDays` gate), so it cannot wait on the clock.
        void PoutStep(float workDays)
        {
            if (hands == null) return;
            foreach (var h in hands)
            {
                if (h == null) continue;
                h.griefPending = false;
                if (h.pouting) EndPout(h);
                else h.poutCooldown = 0f;
            }
        }


        /// **Dev-only (`LifeDevPanel`'s "Pout" button).** Pouting is retired
        /// (2026-10-02, see `PoutStep`), so this never starts one and always
        /// returns false. Kept so the dev panel still compiles.
        public bool ForcePout(OutpostHand h) => false;

        void StartPout(OutpostHand h)
        {
            h.pouting = true;
            h.griefPending = false;
            h.poutLeft = GriefPoutSeconds;
            // **Nothing vanishes.** A load already in his arms when the
            // pout starts: if he had reached the drop (walking the last leg
            // home, or standing at a full store waiting for room) the trip
            // is finished right now, over the store's ceiling if it has to
            // be (`force`, same as a hand leaving the camp's books
            // altogether -- `RemoveHand`); otherwise it is put down where
            // he stands as a ground load for somebody else to collect
            // (`DropCarriedLoad`, phase 2 -- also what a merely-PLANNED
            // trip collapses to, since the source never gave it up).
            if (h.Hauling)
            {
                if (h.haulPicked && h.Leg == TripLeg.AtDrop) DepositHaul(h, true);
                else DropCarriedLoad(h);
            }
            Life.Lives.Log(h.name, Life.LifeEvents.Pouted, CampLabel);
        }

        void EndPout(OutpostHand h)
        {
            h.pouting = false;
            h.poutLeft = 0f;
            h.poutCooldown = 0f;
        }
    }
}
