using UnityEngine;
using SeaSick.Combat;

namespace SeaSick.World
{
    /// <summary>
    /// **The alarm's own bodies** (death/rescue phase 10, 2026-09-28):
    /// walking to the store for a spear, and running to hide (a hut door,
    /// or a crouch on the camp's far side) -- `Combat.RaidAlarm` decides
    /// WHO gets which role; this is the walk, the same division
    /// `CampWorker.Rescue`/`Defend` already keep between the books and the
    /// mime.
    ///
    /// **Priority**: checked after the rescue/pout checks, ahead of
    /// `TickDefend` -- a hand mid-fetch or mid-hide is not fighting yet.
    /// Once he is (a spear in hand, or a hunter's own), `TickDefend` picks
    /// him up on its own the very next frame off `OutpostHand.raidSpear` /
    /// `huntArmed` -- this file never sets `defending` itself.
    /// </summary>
    public partial class CampWorker
    {
        const float FetchArriveMetres = 1.0f;
        const float HideArriveMetres = 0.8f;
        /// A store-bound spear can never stand out here forever (Kevin's
        /// phone: a hand left holding a spear with nobody ticking him
        /// again). `TickReturnSpear` books it in on its own the moment
        /// `Walk`/`Near` call him arrived; this is only the backstop for
        /// whatever they miss.
        const float ReturnSpearGiveUpSeconds = 20f;

        /// Renderers switched off while this body is inside a hut -- not
        /// saved (nothing about an alarm role is), so a reload mid-raid just
        /// reads as "not hidden" until the next frame re-derives it.
        bool bodyHidden;
        float returnSpearStuck;

        /// This body has been taken off its job by the alarm (any role:
        /// fetching, hiding, crouching, carrying a spear home) and has not
        /// been handed back yet. Not saved, same as the roles' own picture.
        bool alarmTook;

        /// **Off the job for an emergency, cleanly (2026-10-03).** The alarm,
        /// a defence or a rescue takes a hand off whatever he was doing; the
        /// BOOKS already dropped his load where he stood
        /// (`OutpostLedger.DropCarriedLoadNow`) and cancelled the trip. This
        /// is the body agreeing with them, the frame he is taken:
        ///
        /// - **His tree / rock / clearing / beast go back** (`ReleaseClaim`).
        ///   A hider used to keep his trunk, so the camp's claim table had a
        ///   "cutter" sitting in a hut, and that trunk could not go to anyone
        ///   else for the whole raid.
        /// - **The trip picture is forgotten** (`ForgetTrip`), and the
        ///   one-frame "trip just ended" edge (`wasHauling`) with it: left
        ///   set, the first free frame after the raid read it as his trip
        ///   finishing and walked him to the old drop-off to set down a load
        ///   the books had already dropped on the ground. Kevin's rule: a
        ///   villager never carries anything the ledger did not hand him.
        /// - **Nothing in his arms** (`Drop`) and **no carcass on his
        ///   shoulders** (`HunterProps.PutDown`): the carcass is the ground
        ///   load now. A hunter caught out kept it on his back through the
        ///   whole fight, because the fight keeps driving his spear prop.
        ///
        /// Idempotent: the roles hand a body between each other (fetch ->
        /// defend -> carry the spear home) and each calls this.
        void StepOffJob()
        {
            ReleaseClaim();
            ForgetTrip();
            wasHauling = false;
            Drop();
            DropHunterLoad();
        }

        /// **Back to the same job, from where he stands (2026-10-03).** The
        /// order was never touched (the alarm, a fight and a rescue are not
        /// orders), so all this does is clear the picture: `Resting` is where
        /// every order's own loop decides what happens next -- a cutter asks
        /// the camp for a tree, a hauler picks up his next trip, a sawyer
        /// walks back to his bench. Without it a hider came out still
        /// `Working`, and a cutter stood at the hut door "chopping" his old
        /// tree until `chopPatience` ran out. Left alone while the Hand has
        /// him or he is in the air/down: those own the phase.
        void BackOnJob()
        {
            Drop();
            ClearRoute();
            if (phase == Phase.Held || phase == Phase.Flying || phase == Phase.Landing
                || phase == Phase.Downed) return;
            if (phase != Phase.Resting) wait = 0f;
            phase = Phase.Resting;
        }

        /// True = handled this frame.
        bool TickAlarmRole(OutpostHand r, float dt)
        {
            bool hasRole = r.fetchingSpear || r.hidingHut || r.hidingCrouch || r.returningSpear;
            if (!hasRole)
            {
                // The alarm let go of him (raid over, or `Combat.RaidAlarm`
                // moved him straight from hiding into `HideAll`'s reverse) --
                // if he was hidden, he is seen again wherever he was left,
                // which for a hut is the door (docs: "hiders come out, body
                // shown at the hut door").
                //
                // **Except a hand asleep in his hut (2026-09-28).** `TickSleep`
                // sets `bodyHidden` too (same flag, same hut-door renderer
                // switch) and this branch used to run the very next frame --
                // BEFORE `TickRoutine` -- and reveal him again, so a sleeper
                // flashed hidden for one frame and then stood visible at the
                // door all night. `asleep` alone owns his body while he has
                // no alarm role; `TickRoutine`/`WakeBody` is what reveals him,
                // at dawn or if the alarm sends him a role (below, next
                // frame `hasRole` is true and `TickHiding`/`TickFetchSpear`
                // take it from there).
                if (bodyHidden && !asleep) RevealBody(r);
                returnSpearStuck = 0f;
                // The alarm is done with him: back to the same job, clean
                // (2026-10-03, see `BackOnJob`).
                if (alarmTook) { alarmTook = false; BackOnJob(); }
                return false;
            }
            if (camp == null || camp.Ledger == null)
            {
                r.fetchingSpear = r.hidingHut = r.hidingCrouch = r.returningSpear = false;
                if (bodyHidden && !asleep) RevealBody(r);
                if (alarmTook) { alarmTook = false; BackOnJob(); }
                return false;
            }

            // **The frame the alarm takes him (2026-10-03):** off his job
            // cleanly -- tree let go, trip picture forgotten, carcass down.
            // See `StepOffJob`.
            if (!alarmTook) { alarmTook = true; StepOffJob(); }

            // **The alarm wakes a sleeper (2026-09-28).** A role is his whole
            // night now: left `asleep`, the reveal guard above would keep him
            // invisible wherever the role left him once the raid is over.
            // Awake, the routine sends him back to bed like anyone else.
            if (asleep || lyingByFire) WakeBody(r);

            if (r.fetchingSpear) return TickFetchSpear(r, dt);
            if (r.returningSpear) return TickReturnSpear(r, dt);
            return TickHiding(r, dt);
        }

        /// **All clear, on foot (death/rescue phase 12).** Walk the exact
        /// spear he is holding (`OutpostHand.raidSpear`) back to the store
        /// and book it there, worn fraction and all, on arrival
        /// (`Combat.RaidAlarm.SettleReturn`). The unwatched fallback --
        /// the camp stops being watched mid-walk -- is `World.CampWorker.
        /// Remove`'s own job, not this tick's: a body that vanishes never
        /// gets another frame here to finish the trip.
        bool TickReturnSpear(OutpostHand r, float dt)
        {
            if (string.IsNullOrEmpty(r.raidSpear)) { r.returningSpear = false; return false; }

            var storeB = CampPiles.StoreBuildingOf(camp);
            Vector3 goal = storeB != null ? WorkSpot(camp, storeB) : camp.CampCentre;

            // **A hand can never stand here forever.** `Walk` returning
            // true (its own, looser arrival -- stall guard included) is
            // arrived, same as `Near`; only asking `Near` left a hand who
            // Walk gave up escaping toward stuck outside the tighter
            // radius with nothing left to tick him (`ResetStall` holds him
            // still once Walk itself is done trying).
            if (!Near(goal, FetchArriveMetres) && !Walk(goal, dt))
            {
                returnSpearStuck += dt;
                if (returnSpearStuck < ReturnSpearGiveUpSeconds)
                {
                    phase = Phase.Going;
                    acting?.Set(VillagerActing.Mode.None);
                    return true;
                }
                // Backstop: book it in from wherever he is stuck rather than
                // hold a spear-carrier forever.
            }
            returnSpearStuck = 0f;

            RaidAlarm.SettleReturn(camp, r);
            phase = Phase.Resting;
            wait = RestSeconds;
            acting?.Set(VillagerActing.Mode.None);
            return true;
        }

        /// Walk to the store and take one spear out of it, iron first -- or,
        /// with the spears gone, a bow (2026-09-30).
        /// `Combat.RaidAlarm.Begin` already checked one was there when it
        /// sent him, but a save/dev tool/another fetcher can have emptied it
        /// since -- gone by the time he arrives, and he hides instead of
        /// standing there holding air.
        bool TickFetchSpear(OutpostHand r, float dt)
        {
            var ledger = camp.Ledger;
            var storeB = CampPiles.StoreBuildingOf(camp);
            Vector3 goal = storeB != null ? WorkSpot(camp, storeB) : camp.CampCentre;

            if (!Near(goal, FetchArriveMetres) && !Walk(goal, dt))
            {
                phase = Phase.Going;
                acting?.Set(VillagerActing.Mode.None);
                return true;
            }

            // **Bows (2026-09-30):** spears first -- they are the melee
            // line -- then a bow, if the camp has arrows for it and it is not
            // one of the bows kept for the tower lookouts
            // (`RaidAlarm.SpareBows`).
            string got = ledger.TakeFromStore(Res.IronSpear, 1) == 1 ? Res.IronSpear
                : ledger.TakeFromStore(Res.Spear, 1) == 1 ? Res.Spear
                : RaidAlarm.SpareBows(camp) > 0 && ledger.TakeFromStore(Res.Bow, 1) == 1 ? Res.Bow
                : null;
            r.fetchingSpear = false;
            if (got != null) { r.raidSpear = got; r.raidSpearWear = 0f; }
            else RaidAlarm.AssignHide(camp, r);   // gone by the time he got there

            phase = Phase.Resting;
            wait = RestSeconds;
            acting?.Set(VillagerActing.Mode.None);
            return true;
        }

        bool TickHiding(OutpostHand r, float dt) => r.hidingHut ? TickHideInHut(r, dt) : TickCrouch(r, dt);

        /// Run to the nearest hut's door and disappear inside. No hut left
        /// standing (torn down, burnt, since he was sent) -- crouch instead.
        bool TickHideInHut(OutpostHand r, float dt)
        {
            var hut = RaidAlarm.NearestHut(camp, transform.position);
            if (hut == null) { r.hidingHut = false; r.hidingCrouch = true; return TickCrouch(r, dt); }

            Vector3 door = WorkSpot(camp, hut);
            if (!Near(door, HideArriveMetres))
            {
                if (bodyHidden) RevealBody(r);   // still on his way in: seen walking
                if (!Walk(door, dt))
                {
                    phase = Phase.Going;
                    acting?.Set(VillagerActing.Mode.None);
                    return true;
                }
            }

            // Inside: standing still, not at work (2026-10-03). Was
            // `Working`, which -- with the tree claim he also kept -- read to
            // the felling as a man swinging at his trunk from inside a hut.
            phase = Phase.Resting;
            acting?.Set(VillagerActing.Mode.None);
            if (!bodyHidden) HideBody(r);
            return true;
        }

        /// No hut stands: run to the far side of the camp from the raiders
        /// and crouch (`VillagerActing.Mode.Bend`, the same pose a defender
        /// jabs from) -- raiders never target a hand who is not `defending`,
        /// so nothing more is needed to keep him safe.
        bool TickCrouch(OutpostHand r, float dt)
        {
            Vector3 spot = CrouchSpot(r);
            if (!Near(spot, HideArriveMetres))
            {
                if (bodyHidden) RevealBody(r);
                if (!Walk(spot, dt))
                {
                    phase = Phase.Going;
                    acting?.Set(VillagerActing.Mode.None);
                    return true;
                }
            }

            // `Working` only so the spacing pass treats him as planted (a
            // crouch is not shoved about); his claims went at `StepOffJob`,
            // so it can no longer read as felling or hunting (2026-10-03),
            // and `BackOnJob` clears it when the raid lets him go.
            phase = Phase.Working;
            Face(camp.CampCentre - transform.position, dt);
            acting?.Set(VillagerActing.Mode.Bend);
            return true;
        }

        /// Away from the raiders' centre, kept inside the defend perimeter --
        /// a straight radial push off the fire, `RaidFightTuning.CrouchDistance`
        /// out, nudged a little per hand (hashed off his name) so several
        /// hiders spread out instead of stacking on one spot.
        Vector3 CrouchSpot(OutpostHand r)
        {
            Vector3 centre = camp.CampCentre;
            Vector3 away = Vector3.forward;
            var party = RaidParty.Active;
            var centroid = party != null && party.Camp == camp ? RaidersCentroid(party) : null;
            if (centroid.HasValue)
            {
                Vector3 fromRaiders = centre - centroid.Value;
                fromRaiders.y = 0f;
                if (fromRaiders.sqrMagnitude > 0.01f) away = fromRaiders;
            }
            away.Normalize();

            int hash = string.IsNullOrEmpty(r.name) ? 0 : r.name.GetHashCode();
            float spreadDeg = ((Mathf.Abs(hash) % 9) - 4) * 8f;   // -32..+32 degrees
            Vector3 dir = Quaternion.AngleAxis(spreadDeg, Vector3.up) * away;

            // **Reachable, not just clear ground (2026-09-28).** The radial
            // push can land past a wall, over a cliff edge, or into the sea
            // on an oddly-shaped camp -- `CampPath.Reachable` is the same
            // "same walkable region as the fire" test the route planner
            // itself trusts. Short of that, fall back toward the fire in
            // steps rather than send him somewhere `Walk` can never land.
            Vector3 at = centre;
            for (float frac = 1f; frac >= 0.24f; frac -= 0.25f)
            {
                Vector3 candidate = centre + dir * (RaidFightTuning.CrouchDistance * frac);
                candidate.y = camp.GroundAt(candidate);
                if (CampPath.Reachable(camp, candidate)) { at = candidate; break; }
            }
            at.y = camp.GroundAt(at);
            return at;
        }

        void HideBody(OutpostHand r)
        {
            bodyHidden = true;
            if (r != null) r.hiddenInHut = true;
            SetRenderersEnabled(false);
        }

        void RevealBody(OutpostHand r)
        {
            bodyHidden = false;
            if (r != null) r.hiddenInHut = false;
            SetRenderersEnabled(true);
        }

        void SetRenderersEnabled(bool on)
        {
            var rends = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
                if (rends[i] != null) rends[i].enabled = on;
        }
    }
}
