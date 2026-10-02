using UnityEngine;
using SeaSick.World.Life;

namespace SeaSick.World
{
    /// <summary>
    /// **Pout + floor (death/rescue phase 4, 2026-09-28; grief only since
    /// 2026-10-02)**, docs/PLAN-DEATH-RESCUE.md, "Neglect": nobody deserts.
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

        /// **The pout clock, one quantum of game time** (`Step`, 2026-10-02).
        /// Ends any pout whose time ran out, then starts the grief pouts
        /// that are owed -- one at a time, re-checking the floor after each,
        /// so a camp at exactly the floor's headcount never lets two hands
        /// mourn at once; the rest wait their turn.
        void PoutStep(float workDays)
        {
            if (hands == null || workDays <= 0f) return;
            float secs = workDays * TimeOfDay.WorkDaySeconds;
            foreach (var h in hands)
            {
                if (h == null) continue;
                h.poutCooldown = 0f;
                if (h.pouting)
                {
                    h.poutLeft -= secs;
                    if (h.poutLeft <= 0f) EndPout(h);
                }
            }
            int free = FreeHandCount();
            foreach (var h in hands)
            {
                if (h == null || !h.griefPending || h.Busy) continue;
                if (!FloorHolds(free - 1)) continue;
                StartPout(h);
                free--;
            }
        }


        /// **Dev-only (`LifeDevPanel`'s "Pout" button): force a pout now,
        /// cooldown ignored, floor still respected.** Returns false (does
        /// nothing) when the floor would break or the hand is not free to
        /// pout at all (already busy, or not on this roster).
        public bool ForcePout(OutpostHand h)
        {
            if (h == null || hands == null || !hands.Contains(h) || h.Busy) return false;
            if (!FloorHolds(FreeHandCount() - 1)) return false;
            StartPout(h);
            return true;
        }

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
