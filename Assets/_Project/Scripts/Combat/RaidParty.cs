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

        const float MaxSeconds = 120f;
        const float Spacing = 1.5f;

        public World.Outpost Camp { get; private set; }
        public EnemyShip Ship { get; private set; }
        public int Stolen { get; private set; }
        public bool Landed { get; private set; }

        /// **Raids grow with the camp (phase 12).** Computed once, at
        /// `Begin`, off the camp's hands and stores -- never mid-raid, and
        /// never for a camp nobody is watching (`Begin` only ever runs off
        /// a landing, which only ever happens watched). Loot scales with it,
        /// capped separately.
        int maxLoot;

        /// Walkers still standing -- destroyed ones drop out of the count on
        /// their own, so this is a live readout, not a decrementing tally.
        public int Ashore
        {
            get
            {
                int n = 0;
                for (int i = 0; i < walkers.Count; i++)
                    // **Phase 9:** a killed raider leaves the live count
                    // immediately, even though his body lingers a few
                    // seconds to fade (`RaidWalker.Dead`).
                    if (walkers[i] != null && !walkers[i].Dead) n++;
                return n;
            }
        }

        readonly List<RaidWalker> walkers = new List<RaidWalker>();
        float elapsed;
        bool sunk;    // told every walker to flee already
        bool ended;   // Recall's outcome already reported

        /// The live raiders (some may be `Dead`, fading, on their way out
        /// of `walkers` on their own) -- `World.CampWorker.TickDefend` reads
        /// this to find something to fight. Read-only: only this class
        /// spawns or removes a walker.
        public IReadOnlyList<RaidWalker> Walkers => walkers;

        /// How many bodies actually landed (before any died) -- the morale
        /// threshold (docs: "half the party is down") is measured against
        /// this, not the live `Ashore` count, so a corpse still fading
        /// counts toward it exactly once.
        int landedCount;

        /// Raiders killed this landing (death/rescue phase 9). Read by
        /// nothing outside this file; drives `MoraleCheck`.
        int killedCount;

        /// **Half the party is down.** Set once; every remaining raider is
        /// told to `Flee` the moment it flips, and `RaidBanner` reads it to
        /// swap its usual scoreline for "the raiders are running".
        public bool MoraleBroken { get; private set; }

        /// Names of every hand who defended at all during this landing,
        /// win or lose -- logged once each as `DefendedCamp` when the raid
        /// ends (`Recall`). `World.CampWorker.TickDefend` adds a name the
        /// first frame that hand starts fighting.
        readonly HashSet<string> defendersThisRaid = new HashSet<string>();
        public void MarkDefender(string handName)
        {
            if (!string.IsNullOrEmpty(handName)) defendersThisRaid.Add(handName);
        }

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

            // **Raids grow with the camp (phase 12).** basePartySize, plus
            // one raider per handsPerExtraRaider hands living here, plus one
            // per wealthPerExtraRaider whole units sitting in the stores (at
            // most maxWealthRaiders of those -- 2026-09-28 smoke: a 5-hand
            // camp with 188 units drew 7 raiders at 40 per extra) --
            // clamped to [basePartySize, maxPartySize]. Computed here, once,
            // off the camp as it stands the instant the party lands --
            // never mid-raid, never for an unwatched camp (this only ever
            // runs off a real landing).
            var ledger = party.Camp.Ledger;
            int hands = ledger?.hands != null ? ledger.hands.Count : 0;
            int wealth = ledger != null ? ledger.Total : 0;
            int basePartySize = RaidFightTuning.BasePartySize;
            int partySize = Mathf.Clamp(
                basePartySize
                    + hands / Mathf.Max(1, RaidFightTuning.HandsPerExtraRaider)
                    + Mathf.Min(RaidFightTuning.MaxWealthRaiders,
                        Mathf.FloorToInt(wealth / Mathf.Max(1f, RaidFightTuning.WealthPerExtraRaider))),
                basePartySize, RaidFightTuning.MaxPartySize);
            party.maxLoot = Mathf.Min(RaidFightTuning.MaxLootCap,
                Mathf.RoundToInt(RaidFightTuning.MaxLootBase * partySize / (float)Mathf.Max(1, basePartySize)));

            // Kevin, 2026-09-22: a posted lookout with arrows looses a
            // volley as the party wades in -- two arrows drop one raider
            // before he reaches the beach, and a party of none never lands.
            int loosed = 0; // Visible arrows replace the old invisible landing kills.
            int size = Mathf.Max(0, partySize - loosed / 2);
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

            party.landedCount = party.walkers.Count;

            // **The alarm** (death/rescue phase 10, 2026-09-28): the party
            // is ashore, so every hand at the camp stops what it was doing
            // -- drops its load, arms up from the store or runs to hide.
            // The first villager to see a raider raises the shared alarm.
            // **Phase 9 safety net:** a raid-fight tally is never saved
            // (`OutpostHand.raidHitsTaken`), so it should already read 0 --
            // this only guards a hand who was mid-tally when the LAST raid
            // ended some other way (ship sunk, camp went unwatched) than a
            // clean `Recall`.
            if (party.Camp.Ledger?.hands != null)
                foreach (var h in party.Camp.Ledger.hands)
                    if (h != null) { h.raidHitsTaken = 0; h.combatDamage = 0f; }

            Active = party;
            return party;
        }

        /// **A raider fell** (`RaidWalker.Killed`, phase 9). Counts toward
        /// morale; at half the landed party down, every raider still
        /// standing runs for the ship the same way a sunk ship sends them
        /// (`Flee`, never `Recall` -- this was NOT a clean withdrawal).
        public void RaiderKilled()
        {
            killedCount++;
            if (MoraleBroken || landedCount <= 0) return;
            if (killedCount >= Mathf.CeilToInt(landedCount * 0.5f))
            {
                MoraleBroken = true;
                for (int i = 0; i < walkers.Count; i++) walkers[i]?.Flee();
            }
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

        World.WallSegment Choose(Vector3 from) => NearestBreachable(Camp, from, out _);

        /// **The wall a raider standing at `from` would break, and what that
        /// would cost him.** Shared by a party (`Choose`, one target for the
        /// whole landing party) and by `World.Outpost.BestLanding` (scoring a
        /// candidate beach point before anybody has landed at all -- there is
        /// no party yet, so this has to work off the camp alone).
        ///
        /// "Nearest by route-able distance", approximated the way the brief
        /// allows: nearest by straight distance AMONG the segments whose
        /// outside point he can actually stand at. A segment on the far side
        /// of a cliff is closer on a ruler and useless in fact.
        public static World.WallSegment NearestBreachable(World.Outpost camp, Vector3 from, out float cost)
        {
            cost = float.MaxValue;
            if (camp == null) return null;
            var walls = camp.Walls;
            if (walls == null || walls.Count == 0) return null;

            var map = World.CampPath.For(camp);

            World.WallSegment bestReachable = null, bestAny = null;
            float dReachable = float.MaxValue, dAny = float.MaxValue;
            Vector3 outsideAny = from, outsideReachable = from;

            for (int i = 0; i < walls.Count; i++)
            {
                var seg = walls[i];
                if (seg == null || seg.Breached) continue;

                Vector3 outside = RaidWalker.OutsidePoint(seg, camp);
                float dx = outside.x - from.x, dz = outside.z - from.z;
                float d = dx * dx + dz * dz;

                if (d < dAny) { dAny = d; bestAny = seg; outsideAny = outside; }

                // A gate is a wall for this purpose (D2/D3: they never climb,
                // and a shut gate is just a cheaper thing to smash) -- it is
                // in the same list and wins on its own lower `MaxHp` once he
                // is swinging.
                if (map != null && !map.HasRoute(from, outside, World.CampPath.Walker.Raider)) continue;
                if (d < dReachable) { dReachable = d; bestReachable = seg; outsideReachable = outside; }
            }

            var chosen = bestReachable != null ? bestReachable : bestAny;
            Vector3 outsideChosen = bestReachable != null ? outsideReachable : outsideAny;
            if (chosen != null)
                cost = Vector3.Distance(from, outsideChosen) + RaidFightTuning.BreachCostMetres;
            return chosen;
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
            // **Lived through a raid** (death/rescue phase 1, 2026-09-27):
            // every hand still on the roster when the raiders withdraw gets
            // the event -- a downed one included, since he made it, just
            // not unhurt.
            // (Logged in `LogDefenders`, which both endings call.)
            RaidDirector.ReportResult(Camp, (MoraleBroken
                ? "the raiders broke and ran"
                : Stolen > 0
                    ? $"the raiders got away with {Stolen}"
                    : "the raiders fled with nothing") + "\nAll clear.");
            LogDefenders();
            RaidAlarm.End(Camp);   // death/rescue phase 12: all clear
            for (int i = 0; i < walkers.Count; i++) walkers[i]?.Recall();
        }

        /// **Phase 9:** every hand who fought at all this landing gets
        /// `DefendedCamp` once, however the raid ended (a clean withdrawal
        /// via `Recall`, or the ship sinking below). Guarded so a raid that
        /// somehow touches both endings only logs once.
        bool defendersLogged;
        void LogDefenders()
        {
            if (defendersLogged || Camp?.Ledger == null) return;
            defendersLogged = true;
            if (Camp.Ledger.hands != null)
                foreach (var h in Camp.Ledger.hands)
                    if (h != null)
                        World.Life.Lives.Log(h.name, World.Life.LifeEvents.SurvivedRaid, Camp.Ledger.CampLabel);
            foreach (var name in defendersThisRaid)
                World.Life.Lives.Log(name, World.Life.LifeEvents.DefendedCamp, Camp.Ledger.CampLabel);
        }

        float nextDetection;
        void Update()
        {
            if (Time.time >= nextDetection && !ended) { nextDetection=Time.time+.25f; VillageDefense.Detect(this); }
            elapsed += Time.deltaTime;

            if (Ship == null || !Ship.Alive)
            {
                if (!sunk)
                {
                    sunk = true;
                    if (!ended)
                    {
                        ended = true;
                        RaidDirector.ReportResult(Camp, "you sank them — the loot is back on the pile\nAll clear.");
                        LogDefenders();
                        RaidAlarm.End(Camp);   // death/rescue phase 12: all clear
                    }
                    for (int i = 0; i < walkers.Count; i++) walkers[i]?.Flee();
                }
            }
            else if (Stolen >= maxLoot || elapsed > MaxSeconds || MoraleBroken)
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
