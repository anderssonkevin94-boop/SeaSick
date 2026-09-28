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

        /// Renderers switched off while this body is inside a hut -- not
        /// saved (nothing about an alarm role is), so a reload mid-raid just
        /// reads as "not hidden" until the next frame re-derives it.
        bool bodyHidden;

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
                if (bodyHidden) RevealBody(r);
                return false;
            }
            if (camp == null || camp.Ledger == null)
            {
                r.fetchingSpear = r.hidingHut = r.hidingCrouch = r.returningSpear = false;
                if (bodyHidden) RevealBody(r);
                return false;
            }

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

            if (!Near(goal, FetchArriveMetres))
            {
                phase = Phase.Going;
                acting?.Set(VillagerActing.Mode.None);
                Walk(goal, dt);
                return true;
            }

            RaidAlarm.SettleReturn(camp, r);
            phase = Phase.Resting;
            wait = RestSeconds;
            acting?.Set(VillagerActing.Mode.None);
            return true;
        }

        /// Walk to the store and take one spear out of it, iron first.
        /// `Combat.RaidAlarm.Begin` already checked one was there when it
        /// sent him, but a save/dev tool/another fetcher can have emptied it
        /// since -- gone by the time he arrives, and he hides instead of
        /// standing there holding air.
        bool TickFetchSpear(OutpostHand r, float dt)
        {
            var ledger = camp.Ledger;
            var storeB = CampPiles.StoreBuildingOf(camp);
            Vector3 goal = storeB != null ? WorkSpot(camp, storeB) : camp.CampCentre;

            if (!Near(goal, FetchArriveMetres))
            {
                phase = Phase.Going;
                acting?.Set(VillagerActing.Mode.None);
                Walk(goal, dt);
                return true;
            }

            string got = ledger.TakeFromStore(Res.IronSpear, 1) == 1 ? Res.IronSpear
                : ledger.TakeFromStore(Res.Spear, 1) == 1 ? Res.Spear
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
                phase = Phase.Going;
                acting?.Set(VillagerActing.Mode.None);
                if (bodyHidden) RevealBody(r);   // still on his way in: seen walking
                Walk(door, dt);
                return true;
            }

            phase = Phase.Working;
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
                phase = Phase.Going;
                acting?.Set(VillagerActing.Mode.None);
                if (bodyHidden) RevealBody(r);
                Walk(spot, dt);
                return true;
            }

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

            Vector3 at = centre + dir * RaidFightTuning.CrouchDistance;
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
