using UnityEngine;
using SeaSick.World.Life;

namespace SeaSick.World
{
    /// <summary>
    /// **Pout + floor (death/rescue phase 4, 2026-09-28)**, docs/PLAN-DEATH-
    /// RESCUE.md, "Neglect": nobody deserts. An angry hand (mood &lt; 0.5)
    /// walks to the fire and sulks for `LifeTuning.PoutSeconds`, then goes
    /// back to whatever he was doing -- `order`/`target` are never touched.
    ///
    /// **Watched-and-running only, same gate as `TickDowned`** (D2's own
    /// rule, extended here): `Outpost.Update` calls this right beside
    /// `TickDowned`/`DispatchRescuers`, guarded there against a paused menu
    /// and an offline catch-up run. An unwatched camp simply keeps last
    /// phase's slow-down (`WorkFactor`'s `StarvingWorkFloor`) and nothing
    /// more -- pouting itself never starts, ticks, or ends off-screen.
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

        /// Real seconds, watched-and-running only (`Outpost.Update`). Ticks
        /// every hand's cooldown, ends any pout whose clock ran out, then
        /// starts new ones for angry, off-cooldown, non-busy hands -- one
        /// at a time, re-checking the floor after each so a camp at exactly
        /// the floor's headcount never lets two hands sulk on the same
        /// frame.
        public void PoutTick(float realDeltaSeconds)
        {
            if (hands == null || realDeltaSeconds <= 0f) return;

            foreach (var h in hands)
            {
                if (h == null) continue;
                if (h.poutCooldown > 0f)
                    h.poutCooldown = Mathf.Max(0f, h.poutCooldown - realDeltaSeconds);
                if (h.pouting)
                {
                    h.poutLeft -= realDeltaSeconds;
                    if (h.poutLeft <= 0f) EndPout(h);
                }
            }

            int free = FreeHandCount();
            foreach (var h in hands)
            {
                if (h == null || h.Busy || h.poutCooldown > 0f || !h.Angry) continue;
                // Pouting him would take `free` down by one -- refuse if
                // that breaks the floor; he simply tries again next tick
                // (nothing here remembers the refusal, same as any other
                // "not right now" in this file).
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
            h.poutLeft = LifeTuning.PoutSeconds;
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
            h.poutCooldown = LifeTuning.PoutCooldownSeconds;
        }
    }
}
