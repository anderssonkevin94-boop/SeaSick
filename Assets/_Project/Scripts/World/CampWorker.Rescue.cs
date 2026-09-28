using UnityEngine;

namespace SeaSick.World
{
    /// <summary>
    /// **The rescuer's own walk (death/rescue phase 2, 2026-09-27).**
    ///
    /// docs/PLAN-DEATH-RESCUE.md, "Deaths": "a nearby free hand runs over
    /// and drags them back to a hut." Who goes is the ledger's call
    /// (`OutpostLedger.DispatchRescuers`, plain data); this file is the
    /// rescuer's own body walking it, the same division `CampWorker`
    /// already keeps between the books and the mime everywhere else.
    ///
    /// **Watched-only, on purpose** (D2): this only runs inside `Update`,
    /// which only runs for a body that exists, which only happens while the
    /// camp is watched. There is no headless equivalent -- a drag simply
    /// freezes the moment the camp stops being watched and picks up again,
    /// from the same two positions, the next time a body is here to walk it
    /// (`OutpostHand.reached`/`dragged` are saved, so a reload mid-drag
    /// restores exactly where it left off and `DispatchRescuers` sends
    /// somebody the moment the camp is watched again if the old rescuer's
    /// body is gone).
    public partial class CampWorker
    {
        /// The row this body draws, for code outside this file that needs
        /// to find ONE particular hand's body (the rescuer looking for the
        /// downed hand's own `CampWorker`, to drag it along).
        public OutpostHand HandRow => Row;

        /// A pace behind the rescuer, where a dragged body slides.
        const float DragTrail = 1.1f;

        /// **This body is rescuing somebody**, at whatever stage: walking
        /// to him, or dragging him home. True = handled this frame (the
        /// caller returns without running the ordinary order dispatch).
        bool TickRescue(OutpostHand r, float dt)
        {
            if (r == null || string.IsNullOrEmpty(r.rescuing)) return false;
            var ledger = camp != null ? camp.Ledger : null;
            if (ledger == null) { r.rescuing = ""; return false; }
            var down = ledger.Hand(r.rescuing);
            if (down == null || !down.downed)
            {
                // He died before we got there, was revived, or the row is
                // simply gone: nothing more for this rescuer to do.
                r.rescuing = "";
                r.draggingNow = false;
                return false;
            }

            // **A sleeping rescuer wakes for it (2026-09-28).** `TickRescue`
            // runs ahead of `TickRoutine`, so a hand the ledger picked while
            // he was already down for the night used to drag the body
            // straight through his own sleep pose and just stand there once
            // he arrived -- `asleep`/`lyingByFire` never got cleared, so
            // `TickSleep`'s "held pose, nothing more to do" kept swallowing
            // him afterward. Wake him the same way the player's own order
            // or dawn would; the routine sends him back to bed normally
            // once this rescue ends.
            if (asleep || lyingByFire) WakeBody(r);

            acting?.Set(VillagerActing.Mode.None);

            if (!down.reached)
            {
                r.draggingNow = false;
                phase = Phase.Going;
                Vector3 target = ledger.HandAt(down);
                if (!Walk(target, dt)) return true;
                // **Reached: the downed timer is stopped for good** (the
                // ledger's own `TickDowned` skips a hand once `reached`).
                down.reached = true;
                down.dragged = true;
                return true;
            }

            // Dragging: walk together to the hut door (or the fire).
            // **Routed, not a straight `MoveTowards` (2026-09-28).** Kevin
            // saw pairs dragged straight through palisades, up cliffs and
            // into buildings -- the same wall/slope guards every other
            // errand gets (`CampPath`/`Walkability`) now carry the rescuer
            // too, the downed body just riding a pace behind him.
            r.draggingNow = true;
            Vector3 goal = RescueGoal(out bool atHut);
            phase = Phase.Coming;
            if (!Walk(goal, dt))
            {
                ledger.BodyAt(r, transform.position);
                DragDownedBodyAlong(down, ledger);
                return true;
            }

            // Arrived: lay him down to recover.
            ledger.BodyAt(r, transform.position);
            down.downed = false;
            down.reached = false;
            down.dragged = false;
            down.recovering = true;
            down.recoverLeft = Life.LifeTuning.RecoverDays;
            down.recoverAtHut = atHut;
            ledger.BodyAt(down, goal);
            Life.Lives.Log(r.name, Life.LifeEvents.DraggedOther, ledger.CampLabel, down.name);
            r.rescuing = "";
            r.draggingNow = false;
            phase = Phase.Resting;
            wait = RestSeconds;
            return true;
        }

        /// Slide the downed body a pace behind the rescuer, snapped to the
        /// ground -- and tell the ledger where he is, so a save mid-drag
        /// (or the camp going unwatched) restores him close to where he
        /// really was, not back at the spot he first went down.
        void DragDownedBodyAlong(OutpostHand down, OutpostLedger ledger)
        {
            Vector3 behind = transform.position - transform.forward * DragTrail;
            behind.y = camp.GroundAt(behind);
            ledger.BodyAt(down, behind);
            foreach (var b in Bodies)
                if (b != null && b.HandRow == down)
                {
                    b.transform.position = behind;
                    break;
                }
        }

        /// The hut door nearest the camp centre, or the fire if no hut
        /// stands. Astra's markers via `WorkSpot`, the same call every
        /// other errand to a building uses.
        Vector3 RescueGoal(out bool atHut)
        {
            var built = camp.Built;
            if (built != null)
                for (int i = 0; i < built.Count; i++)
                {
                    var b = built[i];
                    if (b != null && b.Id == BuildPlans.Hut.id)
                    {
                        atHut = true;
                        return WorkSpot(camp, b);
                    }
                }
            atHut = false;
            return camp.CampCentre;
        }
    }
}
