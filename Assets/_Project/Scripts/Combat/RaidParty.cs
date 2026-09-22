using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Combat
{
    /// The bodies a beached `EnemyShip` puts ashore. One party at a time --
    /// `Active` is a single slot, not a list, because only one raider can be
    /// beached at a camp at once (see `RaidDirector.Consider`).
    ///
    /// Owns its own GameObject rather than living on the ship: a sunk ship is
    /// destroyed the instant `HullIntegrity` says so, and men already
    /// standing on the sand have to keep existing for a few seconds after
    /// that to flee into the water.
    public class RaidParty : MonoBehaviour
    {
        public static RaidParty Active { get; private set; }

        const int PartySize = 3;
        const int MaxLoot = 8;
        const float MaxSeconds = 120f;
        const float Spacing = 1.5f;

        public World.Outpost Camp { get; private set; }
        public EnemyShip Ship { get; private set; }
        public int Stolen { get; private set; }
        public bool Landed { get; private set; }

        /// Walkers still standing -- destroyed ones drop out of the count on
        /// their own, so this is a live readout, not a decrementing tally.
        public int Ashore
        {
            get
            {
                int n = 0;
                for (int i = 0; i < walkers.Count; i++)
                    if (walkers[i] != null) n++;
                return n;
            }
        }

        readonly List<RaidWalker> walkers = new List<RaidWalker>();
        float elapsed;
        bool sunk;    // told every walker to flee already
        bool ended;   // Recall's outcome already reported

        /// Called once by `EnemyShip` the frame it grounds. Spawns the party
        /// spaced out along the beach so three bodies don't stack on one
        /// footprint.
        public static RaidParty Begin(EnemyShip ship)
        {
            if (ship == null || ship.Site.camp == null) return null;

            var go = new GameObject("RaidParty");
            var party = go.AddComponent<RaidParty>();
            party.Ship = ship;
            party.Camp = ship.Site.camp;
            party.Landed = true;

            var site = ship.Site;
            Vector3 shoreDir = site.shore - site.water;
            shoreDir.y = 0f;
            Vector3 tangent = shoreDir.sqrMagnitude > 0.01f
                ? Vector3.Cross(shoreDir.normalized, Vector3.up)
                : Vector3.right;

            // Kevin, 2026-09-22: a posted lookout with arrows looses a
            // volley as the party wades in -- two arrows drop one raider
            // before he reaches the beach, and a party of none never lands.
            int loosed = party.Camp.Ledger != null ? party.Camp.Ledger.LookoutVolley() : 0;
            int size = Mathf.Max(0, PartySize - loosed / 2);
            if (size == 0) { Destroy(go); return null; }

            for (int i = 0; i < size; i++)
            {
                var agent = Crew.BornVillager.Make("raider", null);
                if (agent == null) continue;

                var walker = agent.gameObject.AddComponent<RaidWalker>();
                walker.party = party;
                walker.camp = party.Camp;
                walker.site = site;

                float offset = (i - (size - 1) * 0.5f) * Spacing;
                Vector3 at = site.shore + tangent * offset;
                at.y = party.Camp.GroundAt(at);
                agent.transform.position = at;

                agent.gameObject.SetActive(true);
                party.walkers.Add(walker);
            }

            Active = party;
            return party;
        }

        /// A walker made it back to the ship with one unit. Called by
        /// `RaidWalker`, never by anything reading the ledger directly -- the
        /// ledger transfer already happened at `Taking`; this just counts it.
        public void Delivered(string res) => Stolen++;

        /// The ship is withdrawing under her own power (hurt, or this party
        /// hit its loot/time cap): call every walker home and record the
        /// outcome once.
        public void Recall()
        {
            if (ended)
            {
                for (int i = 0; i < walkers.Count; i++) walkers[i]?.Recall();
                return;
            }
            ended = true;
            Camp.Ledger.raids++;
            RaidDirector.ReportResult(Camp, Stolen > 0
                ? $"the raiders got away with {Stolen}"
                : "the raiders fled with nothing");
            for (int i = 0; i < walkers.Count; i++) walkers[i]?.Recall();
        }

        void Update()
        {
            elapsed += Time.deltaTime;

            if (Ship == null || !Ship.Alive)
            {
                if (!sunk)
                {
                    sunk = true;
                    if (!ended)
                    {
                        ended = true;
                        RaidDirector.ReportResult(Camp, "you sank them — the loot is back on the pile");
                    }
                    for (int i = 0; i < walkers.Count; i++) walkers[i]?.Flee();
                }
            }
            else if (Stolen >= MaxLoot || elapsed > MaxSeconds)
            {
                Ship.EndRaid();   // calls back into Recall()
            }

            walkers.RemoveAll(w => w == null);
            if (walkers.Count == 0)
            {
                Active = null;
                Destroy(gameObject);
            }
        }
    }
}
