using UnityEngine;
using SeaSick.Combat;

namespace SeaSick.World
{
    /// <summary>
    /// **Village defence, the fight only (death/rescue phase 9, 2026-09-28).**
    /// docs/PLAN-DEATH-RESCUE.md, "Village defence in raids": an armed hand
    /// near the camp during a live raid drops what he's carrying, walks to
    /// the nearest raider inside `RaidWalker.InsideDefendPerimeter`, and
    /// jabs him every `RaidFightTuning.JabSeconds`, reusing the hunting jab
    /// pose (`HunterProps`/`VillagerActing.Mode.Bend`).
    ///
    /// **Armed, for now** (phase 9's own scope -- the phase-10 alarm/arming
    /// flow is what actually routes hands here on its own): a hunter
    /// currently out (`OutpostHand.huntArmed`) or a hand the dev panel
    /// flagged (`OutpostHand.armedDefender`). Both are read, never written,
    /// by this file.
    ///
    /// Priority mirrors `TickRescue`/`TickPout`: checked ahead of the
    /// ordinary order dispatch (and ahead of hauling, so a hand mid-carry
    /// drops his load the same way a rescuer does) but behind nothing else
    /// that already outranks them.
    /// </summary>
    public partial class CampWorker
    {
        float defendJabClock;

        /// True = handled this frame.
        bool TickDefend(OutpostHand r, float dt)
        {
            var ledger = camp != null ? camp.Ledger : null;
            if (ledger == null) return StopDefending(r, false);

            var party = RaidParty.Active;
            bool raidLive = party != null && party.Camp == camp;
            bool armed = r.huntArmed || r.armedDefender;
            if (!raidLive || !armed) return StopDefending(r, false);

            RaidWalker foe = NearestRaider(party);
            if (foe == null) return StopDefending(r, false);

            if (!r.defending)
            {
                r.defending = true;
                r.defendSpear = ledger.SpearInHand();
                party.MarkDefender(r.name);
                ledger.DropCarriedLoadNow(r);
                Drop();
                defendJabClock = 0f;
            }

            Vector3 foePos = foe.transform.position;
            float reach = RaidFightTuning.JabReach;
            var props = HunterProps.On(gameObject);
            string spear = r.defendSpear;

            if (!Near(foePos, reach))
            {
                phase = Phase.Going;
                acting?.Set(VillagerActing.Mode.None);
                props.Drive(spear, HunterProps.Pose.Upright);
                Walk(foePos, dt);
                return true;
            }

            phase = Phase.Working;
            Face(foePos - transform.position, dt);
            acting?.Set(VillagerActing.Mode.Bend);
            props.Drive(spear, HunterProps.Pose.Thrust, foePos + Vector3.up * 1.0f);

            defendJabClock += dt;
            if (defendJabClock >= RaidFightTuning.JabSeconds)
            {
                defendJabClock = 0f;
                float dmg = spear == Res.IronSpear ? RaidFightTuning.IronDamage : RaidFightTuning.StoneDamage;
                foe.TakeHit(dmg, r);
            }
            return true;
        }

        /// Drop the flag (and the pose) if it was set; always returns
        /// false, so `TickDefend`'s callers read it as "not handled".
        bool StopDefending(OutpostHand r, bool _)
        {
            if (r != null && r.defending)
            {
                r.defending = false;
                r.defendSpear = null;
                phase = Phase.Resting;
                acting?.Set(VillagerActing.Mode.None);
            }
            return false;
        }

        /// The nearest live raider from this party who is inside the
        /// defend perimeter and not already fled/recalled/dead. Null if
        /// there is nothing left to fight (raid over, or every raider is
        /// running for the boat, or out past the wall/30 m line).
        RaidWalker NearestRaider(RaidParty party)
        {
            var list = party.Walkers;
            RaidWalker best = null;
            float bestD = float.MaxValue;
            Vector3 here = transform.position;
            for (int i = 0; i < list.Count; i++)
            {
                var w = list[i];
                if (w == null || w.Dead) continue;
                if (w.phase == RaidWalker.Phase.Fleeing || w.phase == RaidWalker.Phase.Recalled) continue;
                if (!RaidWalker.InsideDefendPerimeter(camp, w.transform.position)) continue;
                float d = (w.transform.position - here).sqrMagnitude;
                if (d < bestD) { bestD = d; best = w; }
            }
            return best;
        }
    }
}
