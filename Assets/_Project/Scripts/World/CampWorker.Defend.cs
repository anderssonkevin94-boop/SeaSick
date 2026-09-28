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
            // **Phase 10:** a hand who fetched his own spear out of the
            // store (`Combat.RaidAlarm`/`TickFetchSpear`) is armed the same
            // as the phase-9 dev flag or a hunter caught out with his own.
            bool armed = r.huntArmed || r.armedDefender || !string.IsNullOrEmpty(r.raidSpear);
            if (!raidLive || !armed) return StopDefending(r, false);

            if (!r.defending)
            {
                r.defending = true;
                // **Per-hand spear (phase 10):** a hand armed from the store
                // fights with the exact unit he took, iron or stone,
                // whatever the pile does afterwards -- only the phase-9 dev
                // "Arm" flag and a hunter with nothing here yet fall back to
                // the camp-wide snapshot.
                r.defendSpear = !string.IsNullOrEmpty(r.raidSpear) ? r.raidSpear : ledger.SpearInHand();
                party.MarkDefender(r.name);
                ledger.DropCarriedLoadNow(r);
                Drop();
                defendJabClock = 0f;
            }

            RaidWalker foe = NearestRaider(party);
            if (foe == null)
            {
                // **Phase 10, "defend near home + gather point":** nobody to
                // fight yet -- hold the gate/breach nearest the raiders (or,
                // walled or not, the fire-front line) rather than drifting
                // back to the ordinary job the moment `NearestRaider` comes
                // up empty for a frame.
                Vector3 gather = GatherPoint(camp, party);
                var waitProps = HunterProps.On(gameObject);
                if (!Near(gather, 1.2f))
                {
                    phase = Phase.Going;
                    acting?.Set(VillagerActing.Mode.None);
                    waitProps.Drive(r.defendSpear, HunterProps.Pose.Upright);
                    Walk(gather, dt);
                }
                else
                {
                    phase = Phase.Resting;
                    acting?.Set(VillagerActing.Mode.None);
                    waitProps.Drive(r.defendSpear, HunterProps.Pose.Upright);
                    Face(camp.CampCentre - transform.position, dt);
                }
                return true;
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

        /// **Where an armed defender waits with nobody to fight yet**
        /// (phase 10). Walled: the gate, or the breach, nearest the
        /// raiders' own centre -- the same posts and segments
        /// `RaidWalker.Barred`/`RaidParty.Choose` already read off
        /// `Outpost.Walls`. No walls at all: a point toward the raiders,
        /// `RaidFightTuning.GatherRadiusNoWalls` out from the fire -- short
        /// of the 30 m chase line, so he is waiting at the obvious front
        /// door rather than standing at the fire itself.
        static Vector3 GatherPoint(Outpost camp, RaidParty party)
        {
            Vector3 centre = camp.CampCentre;
            Vector3 raidersAt = RaidersCentroid(party) ?? centre + Vector3.forward * 10f;

            var walls = camp.Walls;
            if (walls != null && walls.Count > 0)
            {
                WallSegment best = null;
                float bestD = float.MaxValue;
                for (int i = 0; i < walls.Count; i++)
                {
                    var w = walls[i];
                    if (w == null) continue;
                    if (!w.IsGate && !w.Breached) continue;   // only a way in counts
                    float d = (w.Midpoint - raidersAt).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = w; }
                }
                if (best != null)
                {
                    // The INSIDE point: `RaidWalker.OutsidePoint` gives the
                    // raiders' own approach side, so reflect it through the
                    // segment's midpoint to stand on the camp's side of it.
                    Vector3 outside = RaidWalker.OutsidePoint(best, camp);
                    Vector3 inside = best.Midpoint * 2f - outside;
                    inside.y = camp.GroundAt(inside);
                    return inside;
                }
            }

            Vector3 toward = raidersAt - centre;
            toward.y = 0f;
            toward = toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward;
            Vector3 at = centre + toward * RaidFightTuning.GatherRadiusNoWalls;
            at.y = camp.GroundAt(at);
            return at;
        }

        static Vector3? RaidersCentroid(RaidParty party)
        {
            if (party == null) return null;
            var list = party.Walkers;
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var w = list[i];
                if (w == null || w.Dead) continue;
                sum += w.transform.position;
                n++;
            }
            return n > 0 ? sum / n : (Vector3?)null;
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
