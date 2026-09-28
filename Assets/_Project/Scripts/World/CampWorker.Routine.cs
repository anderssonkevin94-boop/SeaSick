using UnityEngine;
using SeaSick.World.Life;

namespace SeaSick.World
{
    /// <summary>
    /// **Villagers with a day (2026-09-28).** The body side of the evening/
    /// sleep routine (`docs` none yet -- Kevin's brief, this session): a
    /// hand not on the player's own order (`OutpostHand.orderOverride`)
    /// walks to the fire at dusk, then to a hut with a free bed (or lies by
    /// the fire with none standing) once the camp turns in, and comes back
    /// out at dawn. Deciding WHO is awake is a pure function of the clock
    /// (`Life.CampLifeTuning.PhaseAtHour`), the same division every other
    /// file in this family keeps: the economy-neutral part (no output
    /// off-hours, scaled-up output while awake) lives in `OutpostLedger.
    /// WorkFactor`/`DayNightWorkScale` and runs for watched and unwatched
    /// camps alike; this file is only the watched body's mime of it, same
    /// shape as `CampWorker.Pout.cs`.
    ///
    /// **Priority**: checked last, right where the ordinary order dispatch
    /// (`Update`'s `switch (r.order)`) would otherwise run -- a haul or a
    /// tower shift already in progress finishes on its own leg first (the
    /// design rule: nothing is dropped for the evening), and the rescue/
    /// pout/alarm/defend checks earlier in `Update` all still outrank this,
    /// so a live raid or a downed friend always wins over the routine.
    /// </summary>
    public partial class CampWorker
    {
        const float SleepArriveMetres = 1.0f;

        bool asleep;
        /// Lying by the fire (no hut), the downed pose without the downed
        /// state -- calm, not hurt.
        bool lyingByFire;

        /// True = handled this frame (same contract as `TickPout`/
        /// `TickAlarmRole`): the caller returns without running the
        /// ordinary order dispatch.
        bool TickRoutine(OutpostHand r, float dt)
        {
            if (r == null) return false;

            // **The player's order wins.** He dragged a job onto this hand
            // during the evening/night on purpose; leave him to it until
            // `OutpostLedger.Step` clears the flag at the next dawn.
            if (r.orderOverride)
            {
                if (asleep || lyingByFire) WakeBody(r);
                return false;
            }

            var routinePhase = CampLifeTuning.PhaseAtHour(TimeOfDay.Hour);
            if (routinePhase == CampLifeTuning.RoutinePhase.Awake)
            {
                if (asleep || lyingByFire) WakeBody(r);
                return false;
            }
            if (routinePhase == CampLifeTuning.RoutinePhase.Evening) return TickEvening(r, dt);
            return TickSleep(r, dt);
        }

        // --- evening at the fire --------------------------------------------

        bool TickEvening(OutpostHand r, float dt)
        {
            if (asleep || lyingByFire) WakeBody(r);   // safety: clock ran backwards on a dev scrub

            Vector3 spot = FireRingSpot(r);
            if (!Near(spot, HideArriveMetres))
            {
                phase = Phase.Going;
                acting?.Set(VillagerActing.Mode.None);
                Walk(spot, dt);
                return true;
            }

            phase = Phase.Resting;
            Face(camp.CampCentre - transform.position, dt);
            acting?.Set(VillagerActing.Mode.None);
            // A fed camp sings; a hungry one just stands there. Which hands
            // sing is hashed off the name so it does not flicker frame to
            // frame or hand to hand at random.
            bool fed = camp.Ledger != null && !camp.Ledger.Hungry;
            if (fed && SingsAtFire(r)) Sway(dt);
            return true;
        }

        /// Roughly a third of a fed camp's ring sways gently ("singing").
        static bool SingsAtFire(OutpostHand r)
        {
            int hash = string.IsNullOrEmpty(r.name) ? 0 : r.name.GetHashCode();
            return (Mathf.Abs(hash) % 3) == 0;
        }

        void Sway(float dt)
        {
            float t = Time.time * CampLifeTuning.SwayHz * Mathf.PI * 2f;
            float deg = Mathf.Sin(t) * CampLifeTuning.SwayDegrees;
            var e = transform.eulerAngles;
            transform.rotation = Quaternion.Euler(e.x, e.y, deg);
        }

        /// A loose ring around the fire, spread by a hash of the hand's
        /// name so the same camp does not redraw its ring every frame.
        Vector3 FireRingSpot(OutpostHand r)
        {
            Vector3 centre = camp.CampCentre;
            int hash = string.IsNullOrEmpty(r.name) ? 0 : r.name.GetHashCode();
            float deg = Mathf.Abs(hash) % 360;
            Vector3 dir = Quaternion.AngleAxis(deg, Vector3.up) * Vector3.forward;
            Vector3 at = centre + dir * CampLifeTuning.FireRingRadius;
            at.y = camp.GroundAt(at);
            return at;
        }

        // --- sleep -----------------------------------------------------------

        bool TickSleep(OutpostHand r, float dt)
        {
            if (asleep)
            {
                // Held pose; nothing more to do until dawn (`TickRoutine`
                // above) or `orderOverride` pulls him back out.
                return true;
            }
            if (lyingByFire) return true;

            var hut = FindBedHut(r);
            Vector3 goal = hut != null ? WorkSpot(camp, hut) : camp.CampCentre;
            if (!Near(goal, SleepArriveMetres))
            {
                phase = Phase.Going;
                acting?.Set(VillagerActing.Mode.None);
                Walk(goal, dt);
                return true;
            }

            r.sleepHutId = hut != null ? hut.GetInstanceID() : 0;
            acting?.Set(VillagerActing.Mode.None);
            if (hut != null)
            {
                asleep = true;
                if (!bodyHidden) HideBody(r);
            }
            else
            {
                lyingByFire = true;
                LieDown();
            }
            phase = Phase.Resting;
            return true;
        }

        /// **The nearest hut with a free bed**, first-come-first-served by
        /// distance -- `BuildPlan.houses` beds a hut, occupancy counted off
        /// every OTHER hand's own `sleepHutId` (no separate roster to keep
        /// in sync). Null with no hut standing, or every one already full;
        /// the caller lies by the fire instead.
        Building FindBedHut(OutpostHand r)
        {
            var built = camp != null ? camp.Built : null;
            var hands = camp?.Ledger?.hands;
            if (built == null) return null;
            Building best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < built.Count; i++)
            {
                var b = built[i];
                if (b == null || b.Id != BuildPlans.Hut.id) continue;
                int capacity = BuildPlans.Hut.houses;
                if (capacity <= 0) continue;

                int occupied = 0;
                if (hands != null)
                    for (int j = 0; j < hands.Count; j++)
                    {
                        var hh = hands[j];
                        if (hh != null && hh != r && hh.sleepHutId == b.GetInstanceID()) occupied++;
                    }
                if (occupied >= capacity) continue;

                float d = (b.transform.position - transform.position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = b; }
            }
            return best;
        }

        /// Lying pose, calm -- the downed pose (flat, snapped to the
        /// ground) without any of the downed STATE.
        void LieDown()
        {
            Vector3 p = transform.position;
            p.y = camp.GroundAt(p);
            transform.position = p;
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 90f);
        }

        void StandUp()
        {
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }

        /// Dawn (or the player's own order) reclaims this hand: shown again
        /// wherever he was left -- a hut's door, same as the alarm's own
        /// hiders -- and handed back to the ordinary dispatch this same
        /// frame.
        void WakeBody(OutpostHand r)
        {
            if (asleep)
            {
                asleep = false;
                if (bodyHidden) RevealBody(r);
            }
            if (lyingByFire)
            {
                lyingByFire = false;
                StandUp();
            }
            r.sleepHutId = 0;
            phase = Phase.Resting;
            wait = 0f;
        }
    }
}
