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
            if (!Near(spot, HideArriveMetres) && !Walk(spot, dt))
            {
                phase = Phase.Going;
                acting?.Set(VillagerActing.Mode.None);
                return true;
            }

            phase = Phase.Resting;
            Face(camp.CampCentre - transform.position, dt);
            // **Supper at the ring (2026-10-02).** The books served him at
            // the bell; here he eats it, a bowl or two, then sits the
            // evening out. Late arrivals eat on arrival; one still walking
            // at bedtime skips the picture -- the meal was counted anyway.
            if (TrySupperBite(r)) return true;
            acting?.Set(VillagerActing.Mode.None);
            // A camp that ate its fill together sings; an under-fed one just
            // stands there. Which hands sing is hashed off the name so it
            // does not flicker frame to frame or hand to hand at random.
            bool fed = camp.Ledger != null && camp.Ledger.fedTogether;
            if (fed && SingsAtFire(r)) Sway(dt);
            return true;
        }

        /// Bowls this body still has to eat at tonight's supper.
        int supperBites;

        /// **One bowl of tonight's supper**, if the ledger served him one he
        /// has not eaten yet (`OutpostHand.SupperWaiting`): the same Eat
        /// mime a meal trip used (`StartEat`, played out by `TickDelivery`),
        /// once per serving up to two. True = a bowl just started.
        bool TrySupperBite(OutpostHand r)
        {
            var l = camp.Ledger;
            if (l == null || !r.SupperWaiting(l.supperDay)) { supperBites = 0; return false; }
            if (supperBites <= 0) supperBites = Mathf.Clamp(r.supperServings, 1, 2);
            supperBites--;
            if (supperBites <= 0) r.supperMimedDay = r.supperOn;
            StartEat(camp.CampCentre, string.IsNullOrEmpty(r.lastMeal) ? Res.Meals : r.lastMeal);
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
            // Not inside a building that stands on the ring (2026-10-01).
            if (CampPath.PushOut(camp, at, out Vector3 outside)) at = outside;
            at.y = camp.GroundAt(at);
            return at;
        }

        // --- sleep -----------------------------------------------------------

        // **Tonight's bed, reserved (2026-10-02).** Kevin on the phone: the
        // whole camp walked to the nearest hut, found it full, walked on to
        // the next, and the last few wandered back to the fire. A bed used
        // to count as taken only once its sleeper had ARRIVED
        // (`sleepHutId`), and every walker re-picked every frame. Now a
        // hand picks once, the moment he turns in, and that bed counts as
        // taken from then on (`BedsTaken`), so nobody is sent to a hut that
        // is already spoken for -- and a hand with no bed left knows it at
        // once and goes straight to the fire.
        //
        // Picked per hand, nearest to HIM, not dealt out camp-wide: at
        // bedtime the routine hands are all standing in the fire ring
        // (`TickEvening`), so who asks first barely matters, and a camp-wide
        // deal would have to hold beds for hands still on a haul or a raid
        // role further up `Update` -- the per-body priority chain decides
        // when each one is free, not the clock.
        //
        // **Released without anybody calling it**: a walker's reservation
        // only counts while `TickSleep` ran for him this frame or last
        // (`bedTickFrame`), so an alarm, a rescue, a pout, a haul, the Hand
        // picking him up, a death or the player's own order all drop it on
        // their own, and he picks again from wherever he is once he turns
        // in again. A sleeper's bed is held for as long as he is `asleep`;
        // `WakeBody` (dawn, alarm, rescue, override) lets it go. Body-only,
        // never saved: a reload just picks again.

        /// Metres of walk a hand will add to sleep in last night's hut
        /// again. Mild: Kevin is fine with a different tent each night.
        const float LastBedPreferMetres = 3f;

        /// Tonight's choice has been made (hut or fire).
        bool bedChosen;
        /// ...and it was a hut (`bedHut`); false = the fire. Kept apart
        /// from `bedHut == null` so a hut demolished under him reads as
        /// "pick again", not as "chose the fire".
        bool bedInHut;
        Building bedHut;
        /// `Time.frameCount` of the last `TickSleep` for this body -- what
        /// keeps a walker's reservation alive (see above).
        int bedTickFrame = -10;
        /// Where he slept last night, for `LastBedPreferMetres`.
        Building lastBedHut;

        bool TickSleep(OutpostHand r, float dt)
        {
            // Not ticked last frame while still walking: something above in
            // `Update` had him, and his old pick may be across camp from
            // where he is now (and no longer held) -- choose again.
            int now = Time.frameCount;
            if (bedChosen && !asleep && !lyingByFire && now - bedTickFrame > 1) bedChosen = false;
            bedTickFrame = now;

            if (asleep)
            {
                // The hut came down under him: out he comes, and turns in
                // again next frame like anyone else.
                if (bedInHut && bedHut == null) WakeBody(r);
                // Otherwise a held pose; nothing more to do until dawn
                // (`TickRoutine` above) or `orderOverride` pulls him out.
                return true;
            }
            if (lyingByFire) return true;

            if (!bedChosen || (bedInHut && bedHut == null)) ChooseBed();
            Vector3 goal = bedInHut ? WorkSpot(camp, bedHut) : camp.CampCentre;
            if (!Near(goal, SleepArriveMetres) && !Walk(goal, dt))
            {
                phase = Phase.Going;
                acting?.Set(VillagerActing.Mode.None);
                return true;
            }

            r.sleepHutId = bedInHut ? bedHut.GetInstanceID() : 0;
            acting?.Set(VillagerActing.Mode.None);
            if (bedInHut)
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

        void ChooseBed()
        {
            bedChosen = true;
            bedHut = FindBedHut();
            bedInHut = bedHut != null;
            if(bedHut!=null && Row!=null) { Row.hasHomeHut=true; Row.homeHutX=bedHut.transform.position.x; Row.homeHutZ=bedHut.transform.position.z; }
        }

        /// **The nearest hut with a bed nobody has spoken for**, measured
        /// from this hand (straight line, flat; last night's hut a little
        /// closer, `LastBedPreferMetres`). Beds per hut are the plan's
        /// `houses` plus its level's bonus, the same sum `OutpostLedger.
        /// HousingCapacity` counts. Null with no hut standing or every one
        /// spoken for; the caller lies by the fire instead.
        Building FindBedHut()
        {
            var built = camp != null ? camp.Built : null;
            if (built == null) return null;
            Vector3 at = transform.position;
            Building best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < built.Count; i++)
            {
                var b = built[i];
                if (b == null || b.Id != BuildPlans.Hut.id) continue;
                int capacity = BuildPlans.Hut.houses
                    + Economy.Techs.HousesBonus(BuildPlans.Hut.id, camp.LevelOfBuilding(b));
                if (capacity <= 0 || BedsTaken(b) >= capacity) continue;

                Vector3 d = b.transform.position - at;
                d.y = 0f;
                float score = d.magnitude;
                if (b == lastBedHut) score -= LastBedPreferMetres;
                if (score < bestScore) { bestScore = score; best = b; }
            }
            return best;
        }

        /// Beds in `hut` other bodies of this camp hold tonight: asleep in
        /// it, or on their way there and still turning in (ticked this
        /// frame or last -- see the note above `LastBedPreferMetres`).
        int BedsTaken(Building hut)
        {
            int now = Time.frameCount, n = 0;
            for (int i = 0; i < Bodies.Count; i++)
            {
                var w = Bodies[i];
                if (w == null || w == this || w.camp != camp || !w.bedInHut || w.bedHut != hut) continue;
                if (w.asleep || now - w.bedTickFrame <= 1) n++;
            }
            return n;
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
            // Tonight's bed goes back (see `TickSleep`); remembered for a
            // mild pull back to it tomorrow night.
            if (bedInHut && bedHut != null) lastBedHut = bedHut;
            bedChosen = false;
            bedInHut = false;
            bedHut = null;
            phase = Phase.Resting;
            wait = 0f;
        }
    }
}
