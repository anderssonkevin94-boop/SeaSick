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
                // Placed again on his first frame: `CrewAgent.Start` writes
                // his deck post into localPosition, and with no parent that
                // is a point beside the WORLD ORIGIN -- every party landed
                // there, under the sea, hundreds of metres from the camp.
                walker.landAt = at;
                // A camp body, not a deck hand: no sea-sickness, no station
                // pose written over the direction he is walking.
                agent.Puppeted = true;

                agent.gameObject.SetActive(true);
                party.walkers.Add(walker);
            }

            Active = party;
            return party;
        }

        // --- the wall the party is breaking ------------------------------------

        /// The one segment this party is working on. A party decides this
        /// ONCE, at the first walker to find himself shut out, and every
        /// other walker is handed the same answer -- four men on one stretch
        /// of palisade (through in ~6 s) rather than four men on four
        /// stretches (through in ~25 s, four holes, no drama).
        World.WallSegment target;

        /// The segment to break, chosen on first ask and held until it is
        /// breached or destroyed. `from` is the asking walker's position; the
        /// first asker is effectively the party leader, which is the right
        /// answer because he is the man who hit the wall first.
        ///
        /// Null means "nothing worth breaking is reachable" -- no walls, or
        /// the only way in is through something the raider cannot even walk
        /// up to. The caller then falls back to ordinary walking, which falls
        /// back to a straight line: no raider ever stands still on this.
        public World.WallSegment BreachSegment(Vector3 from)
        {
            if (target != null && !target.Breached) return target;
            target = Choose(from);
            return target;
        }

        /// A walker got through. Drop the shared target so a later block --
        /// a rebuilt wall, a second ring -- is chosen fresh.
        public void BreachOpened(World.WallSegment seg)
        {
            if (target == seg) target = null;
        }

        World.WallSegment Choose(Vector3 from)
        {
            if (Camp == null) return null;
            var walls = Camp.Walls;
            if (walls == null || walls.Count == 0) return null;

            var map = World.CampPath.For(Camp);

            World.WallSegment bestReachable = null, bestAny = null;
            float dReachable = float.MaxValue, dAny = float.MaxValue;

            for (int i = 0; i < walls.Count; i++)
            {
                var seg = walls[i];
                if (seg == null || seg.Breached) continue;

                Vector3 outside = RaidWalker.OutsidePoint(seg, Camp);
                float dx = outside.x - from.x, dz = outside.z - from.z;
                float d = dx * dx + dz * dz;

                if (d < dAny) { dAny = d; bestAny = seg; }

                // "Nearest by route-able distance", approximated the way the
                // brief allows: nearest by straight distance AMONG the
                // segments whose outside point he can actually stand at. A
                // segment on the far side of a cliff is closer on a ruler and
                // useless in fact.
                if (map != null && !map.HasRoute(from, outside, World.CampPath.Walker.Raider)) continue;
                if (d < dReachable) { dReachable = d; bestReachable = seg; }
            }

            // A gate is a wall for this purpose (D2/D3: they never climb, and
            // a shut gate is just a cheaper thing to smash) -- it is in the
            // same list and wins on its own lower `MaxHp` once he is swinging.
            return bestReachable != null ? bestReachable : bestAny;
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
