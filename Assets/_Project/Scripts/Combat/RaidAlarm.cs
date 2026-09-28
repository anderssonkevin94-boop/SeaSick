using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Combat
{
    /// <summary>
    /// **The alarm, arming and hiding** (death/rescue phase 10, 2026-09-28).
    /// docs/PLAN-DEATH-RESCUE.md, "Village defence in raids": the moment a
    /// raid party lands, every hand not already out of the ordinary dispatch
    /// for some other reason (down, recovering, being dragged, rescuing,
    /// pouting) and not the posted tower lookout either arms up from the
    /// store -- closest to it first, iron before stone -- or runs to hide.
    /// A hunter already armed (`huntArmed`) needs no role here: he goes
    /// straight to `World.CampWorker.TickDefend` on his own, same as phase 9.
    ///
    /// This file owns the ROSTER (who got which role) and the store-spear
    /// bookkeeping at the alarm's two edges (declared, cleared).
    /// `World.CampWorker`'s own `TickAlarmRole`/`TickDefend` walk the bodies
    /// through it frame to frame. Driven from `RaidParty`'s own lifecycle
    /// (`Begin`, `Recall`, the sunk branch of `Update`) rather than polled,
    /// so there is exactly one "the alarm just went up/down" edge per raid.
    /// </summary>
    public static class RaidAlarm
    {
        class State
        {
            public bool active;
            public bool hideAllOverride;
        }

        static readonly Dictionary<World.Outpost, State> states = new Dictionary<World.Outpost, State>();

        static State StateFor(World.Outpost camp)
        {
            if (!states.TryGetValue(camp, out var st)) { st = new State(); states[camp] = st; }
            return st;
        }

        public static bool IsActive(World.Outpost camp) =>
            camp != null && states.TryGetValue(camp, out var st) && st.active;

        /// **Everyone stops.** Called once by `RaidParty.Begin` the instant
        /// a party lands.
        public static void Begin(World.Outpost camp)
        {
            var ledger = camp != null ? camp.Ledger : null;
            if (ledger == null || ledger.hands == null) return;
            var st = StateFor(camp);
            st.active = true;
            st.hideAllOverride = false;

            int storeSpears = ledger.StoreCountOf(World.Res.IronSpear) + ledger.StoreCountOf(World.Res.Spear);

            var eligible = new List<World.OutpostHand>();
            foreach (var h in ledger.hands)
            {
                if (h == null) continue;
                if (h.downed || h.recovering || h.dragged || h.pouting
                    || !string.IsNullOrEmpty(h.rescuing)) continue;   // keep doing that
                if (IsPostedLookout(h)) { h.raidLookout = true; continue; }   // stays put, keeps shooting
                if (h.huntArmed) continue;   // straight to the fight, own spear
                eligible.Add(h);
            }

            // Closest to the store first -- "first come first served".
            Vector3 storeAt = StorePoint(camp);
            eligible.Sort((a, b) =>
                (ledger.HandAt(a) - storeAt).sqrMagnitude.CompareTo((ledger.HandAt(b) - storeAt).sqrMagnitude));

            for (int i = 0; i < eligible.Count; i++)
            {
                var h = eligible[i];
                h.alarmed = true;
                ledger.DropCarriedLoadNow(h);
                if (i < storeSpears) h.fetchingSpear = true;
                else AssignHide(camp, h);
            }
        }

        /// **All clear** (minimal, phase 12 will replace it). Called by
        /// `RaidParty` at every ending it has (`Recall`, the sunk branch of
        /// `Update`) -- idempotent, so touching it from more than one of
        /// those in the same raid is harmless.
        public static void End(World.Outpost camp)
        {
            if (camp == null || !states.TryGetValue(camp, out var st) || !st.active) return;
            st.active = false;
            st.hideAllOverride = false;

            var ledger = camp.Ledger;
            if (ledger == null || ledger.hands == null) return;

            foreach (var h in ledger.hands)
            {
                if (h == null) continue;
                h.raidLookout = false;
                if (!h.alarmed && string.IsNullOrEmpty(h.raidSpear)
                    && !h.fetchingSpear && !h.hidingHut && !h.hidingCrouch) continue;

                // **Nothing vanishes**: a store spear still in a hand's arms
                // (fetched, or fighting with it) goes straight back into the
                // store count -- TODO phase 12, carried back on foot instead.
                if (!string.IsNullOrEmpty(h.raidSpear))
                {
                    ledger.Add(h.raidSpear, 1);
                    h.raidSpear = null;
                }
                h.alarmed = false;
                h.fetchingSpear = false;
                h.hidingHut = false;
                h.hidingCrouch = false;
                // `defending`/`defendSpear` are `World.CampWorker.TickDefend`'s
                // own to clear (`StopDefending`, the next frame it sees the
                // raid is no longer live) -- untouched here, same division
                // phase 9 already kept.
            }
        }

        /// **Phase 11 hook** (exposed now, no button on it yet): send
        /// everyone -- armed hands included, lookouts excepted -- into
        /// hiding, or call off the hide and send the armed back out.
        public static void HideAll(World.Outpost camp, bool hide)
        {
            var ledger = camp != null ? camp.Ledger : null;
            if (ledger == null || ledger.hands == null || !states.TryGetValue(camp, out var st)) return;
            st.hideAllOverride = hide;

            if (hide)
            {
                foreach (var h in ledger.hands)
                {
                    if (h == null || h.raidLookout) continue;
                    if (h.downed || h.recovering || h.dragged || h.pouting
                        || !string.IsNullOrEmpty(h.rescuing)) continue;
                    if (h.hidingHut || h.hidingCrouch) continue;

                    if (h.defending) { h.defending = false; h.defendSpear = null; }
                    h.fetchingSpear = false;
                    if (!string.IsNullOrEmpty(h.raidSpear) && !h.huntArmed)
                    {
                        ledger.Add(h.raidSpear, 1);
                        h.raidSpear = null;
                    }
                    h.alarmed = true;
                    AssignHide(camp, h);
                }
            }
            else
            {
                // Send the armed back out: the store spears left decide who
                // re-arms, same "closest first" spirit as `Begin` without
                // re-sorting the whole roster for a dev-only hook.
                int available = ledger.StoreCountOf(World.Res.IronSpear) + ledger.StoreCountOf(World.Res.Spear);
                foreach (var h in ledger.hands)
                {
                    if (h == null || !(h.hidingHut || h.hidingCrouch)) continue;
                    h.hidingHut = false;
                    h.hidingCrouch = false;
                    if (available > 0) { h.fetchingSpear = true; available--; }
                    else AssignHide(camp, h);
                }
            }
        }

        /// For the banner (phase 11): defending, hiding, spears still
        /// sitting in the store.
        public static (int defending, int hiding, int spearsLeftInStore) Counts(World.Outpost camp)
        {
            var ledger = camp != null ? camp.Ledger : null;
            if (ledger == null || ledger.hands == null) return (0, 0, 0);
            int def = 0, hide = 0;
            foreach (var h in ledger.hands)
            {
                if (h == null) continue;
                if (h.defending) def++;
                if (h.hidingHut || h.hidingCrouch) hide++;
            }
            int store = ledger.StoreCountOf(World.Res.IronSpear) + ledger.StoreCountOf(World.Res.Spear);
            return (def, hide, store);
        }

        /// **Save-during-raid** (docs: "nothing vanishes"): any store spear
        /// still out in a hand's arms goes back into the count BEFORE the
        /// ledger is captured, the same door `Ship.Overboard.FloatingCargo`
        /// uses for cargo riding the sea when `SaveGame.Capture` runs.
        public static void ReturnAllHeldSpearsForSave()
        {
            foreach (var camp in World.Outpost.All)
            {
                var ledger = camp != null ? camp.Ledger : null;
                if (ledger == null || ledger.hands == null) continue;
                foreach (var h in ledger.hands)
                {
                    if (h == null || string.IsNullOrEmpty(h.raidSpear)) continue;
                    ledger.Add(h.raidSpear, 1);
                    h.raidSpear = null;
                }
            }
        }

        static bool IsPostedLookout(World.OutpostHand h) =>
            h.order == World.OutpostOrder.Work && h.target == World.OutpostLedger.WatchtowerId;

        static Vector3 StorePoint(World.Outpost camp)
        {
            var b = World.CampPiles.StoreBuildingOf(camp);
            return b != null ? b.transform.position : camp.CampCentre;
        }

        /// Send a hand to hide: nearest hut if one stands, else crouch.
        /// `World.CampWorker.TickAlarmRole` is what actually walks him
        /// there; this only decides which.
        public static void AssignHide(World.Outpost camp, World.OutpostHand h)
        {
            Vector3 from = camp.Ledger.HandAt(h);
            if (NearestHut(camp, from) != null) h.hidingHut = true;
            else h.hidingCrouch = true;
        }

        /// The nearest standing `BuildPlans.Hut` to `from`, or null with none
        /// raised. Also used by `World.CampWorker` to walk a hiding hand to
        /// its door and by the hut label to know whom to count.
        public static World.Building NearestHut(World.Outpost camp, Vector3 from)
        {
            var built = camp != null ? camp.Built : null;
            if (built == null) return null;
            World.Building best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < built.Count; i++)
            {
                var b = built[i];
                if (b == null || b.Id != World.BuildPlans.Hut.id) continue;
                float d = (b.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = b; }
            }
            return best;
        }
    }
}
