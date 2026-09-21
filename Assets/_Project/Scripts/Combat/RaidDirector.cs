using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Combat
{
    /// **The clock for a raid Kevin is there to see.** The other raid --
    /// `OutpostLedger.Raid`, banked while nobody is watching -- is a number
    /// that changes between visits. This one is a ship you can see coming and
    /// sink, so it needs a clock of its own: stand at a camp with raiders
    /// offshore and something worth taking, and 25 unwatched-adjacent
    /// seconds later one of them beaches.
    ///
    /// One raid per visit, never a second one stacked on top of the first --
    /// `raidedThisVisit` resets only when the camp is forgotten (the ship
    /// sails away, `Outpost.Watched` drops).
    public static class RaidDirector
    {
        const float SecondsToRaid = 25f;

        class State
        {
            public float watchedFor;
            public bool raidedThisVisit;
            public bool warned;
            public string lastResult;
            public float lastResultAt = -999f;
        }

        static readonly Dictionary<World.Outpost, State> states = new Dictionary<World.Outpost, State>();

        /// How long a reported outcome ("the raiders got away with 5") stays
        /// on the banner after the party is gone.
        const float ResultShowSeconds = 8f;

        /// Called every frame `Outpost.Update` runs while the camp is watched
        /// and has a fire lit. Ticks the clock and, once, launches a raider.
        public static void Consider(World.Outpost camp, float dt)
        {
            if (camp == null) return;
            var st = StateFor(camp);
            st.watchedFor += dt;

            if (st.raidedThisVisit || camp.Ledger == null || camp.Ledger.Total <= 0) return;
            if (st.watchedFor < SecondsToRaid) return;
            if (RaidParty.Active != null) return;

            var ship = EnemyShip.IdleAt(camp.Island);
            if (ship == null) return;
            if (!camp.ShoreNear(camp.CampCentre, out Vector3 shore, out Vector3 water)) return;

            ship.BeginRaid(new RaidSite { camp = camp, water = water, shore = shore });
            st.raidedThisVisit = true;
            // A manned lookout means the camp saw them coming, whatever
            // happens next -- see `RaidBanner`'s "the lookout" line.
            st.warned = camp.Ledger.Guard >= 1f;
        }

        /// The ship has sailed off -- forget this visit so the next one gets
        /// its own clock and its own chance at a raid.
        public static void Forget(World.Outpost camp)
        {
            if (camp != null) states.Remove(camp);
        }

        /// The raider currently beaching or beached at this camp, or null.
        public static EnemyShip Incoming(World.Outpost camp)
        {
            if (camp == null) return null;
            foreach (var r in EnemyShip.All)
                if (r != null && r.Alive && r.Raiding && r.Site.camp == camp) return r;
            return null;
        }

        /// Did a manned watchtower spot this visit's raider coming?
        public static bool WarnedOf(World.Outpost camp) =>
            states.TryGetValue(camp, out var st) && st.warned;

        /// `RaidParty` calls this once, when a raid ends, so the banner has
        /// something to say after the ship and the party are both gone.
        public static void ReportResult(World.Outpost camp, string text)
        {
            if (camp == null) return;
            var st = StateFor(camp);
            st.lastResult = text;
            st.lastResultAt = Time.unscaledTime;
        }

        /// The last raid's outcome, for as long as `RaidBanner` should still
        /// be showing it -- null once that window has passed.
        public static string LastResult(World.Outpost camp)
        {
            if (!states.TryGetValue(camp, out var st) || string.IsNullOrEmpty(st.lastResult)) return null;
            return Time.unscaledTime - st.lastResultAt <= ResultShowSeconds ? st.lastResult : null;
        }

        static State StateFor(World.Outpost camp)
        {
            if (!states.TryGetValue(camp, out var st))
            {
                st = new State();
                states[camp] = st;
            }
            return st;
        }
    }
}
