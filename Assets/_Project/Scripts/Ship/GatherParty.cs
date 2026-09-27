using System.Collections.Generic;
using SeaSick.Crew;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.Ship
{
    /// **The gather party (2026-09-27).** Kevin: *"yes go ahead with the
    /// gather party."* Anchored off an island with no camp, the ship sends a
    /// few hands ashore to fetch ONE raw good -- so much of it, or until the
    /// hold is full -- and they carry it straight into the hold. Nothing
    /// stays behind: no fire, no pile, no upkeep, no outpost (`HasCamp`
    /// stays false; this never touches the island's ledger).
    ///
    /// **The trip.** Each hand walks from the landing to the nearest free
    /// source of the good that is reachable from the landing (a straight,
    /// walkable line: `LineWalkable`, `Walkability.MayStep` every
    /// `LineStep` metres -- there is no camp path grid on an island without
    /// a camp), works it at Kevin's dials (wood 5 s a log, stone 8 s, ore
    /// 10 s, per unit), carries the whole armful back over the plank and
    /// into the hold. Trip time is the walk itself: the body walks it.
    ///
    /// **Stops** when the amount is in, the hold is full, nothing reachable
    /// is left, raiders come near, or the player recalls. Recall: everyone
    /// walks back; a load in hand is delivered when he comes aboard.
    ///
    /// **Rules chosen.** Sailing: the ship is `Ashore` while any hand is
    /// out, and `Ashore` offers no cast-off (the prompt says "N hands ashore
    /// -- recall first"). Save: nothing of the party is saved -- the save
    /// writes the ship's crew by name and the hold, so a party ashore loads
    /// as recalled, every hand aboard, loads in hand dropped (at most one
    /// armful each). Raids: raiders within `RaidAlarmMetres` of any hand, or
    /// a raid landing on this island, and the party drops tools and runs for
    /// the ship (recall). Unwatched: a party only exists while she lies at
    /// the island, which is always watched -- there is no offline stepping.
    /// Food: there is no ship ration logic in the code yet, so a party eats
    /// nothing extra.
    ///
    /// **What persists.** Sources are taken the way the plain shore party
    /// takes them: a felled tree stays felled in the island's mesh, a loose
    /// rock is hidden (`SceneryRocks.SetHidden`, no remnant), a kit deposit
    /// shows its remnant -- for as long as the island stays built this
    /// session. Not across save/load: booking a party's take into the
    /// survey ledger would have `GatherSync` hide a SECOND, different set of
    /// rocks to match the books (it draws the gathered set nearest the
    /// clearing first), so that is a follow-up, not a guess.
    public class GatherParty : MonoBehaviour
    {
        // --- Kevin's playtest dials (GDD, trips) --------------------------
        public const float WoodSecondsPerUnit = 5f;
        public const float StoneSecondsPerUnit = 8f;
        public const float OreSecondsPerUnit = 10f;
        public const float OtherSecondsPerUnit = 5f;

        /// How far from the landing a party will go, metres. A guess.
        public const float Reach = 80f;
        /// Most units one hand carries in one trip.
        public const int MaxArmful = 8;
        /// Most loose rocks stood as party sources at once.
        public const int MaxRockNodes = 60;
        /// Raiders this close to any hand and the party runs.
        public const float RaidAlarmMetres = 30f;
        /// Sample spacing of the walkable-line test, metres.
        public const float LineStep = 1.5f;

        /// "Fill hold" as a target.
        public const int FillHold = -1;

        public struct Option
        {
            public string resource;
            public int sources;
            public int units;
        }

        AnchorController anchor;
        VoyageManager voyage;
        ShipHold hold;
        Gangway gangway;

        readonly List<CrewAgent> hands = new List<CrewAgent>();
        readonly List<ResourceNode> partyRocks = new List<ResourceNode>();
        Island island;
        Vector3 landing;
        float nextRaidLook;

        /// A party is out (someone has not come back yet).
        public bool Out { get; private set; }
        public bool Recalling { get; private set; }
        public string Resource { get; private set; }
        /// Units wanted, or `FillHold`.
        public int Target { get; private set; }
        public int DeliveredUnits { get; private set; }
        public int Trips { get; private set; }
        public int SourcesTaken { get; private set; }
        /// Why the party stopped (or is stopping); empty while it works.
        public string StopReason { get; private set; } = "";
        public Island Island => island;

        /// The last party's numbers, for `GatherPartyCheck`.
        public static int LastDelivered, LastTrips, LastSourcesTaken;
        public static string LastStop = "";

        public static GatherParty For(AnchorController a)
        {
            if (a == null) return null;
            var p = a.GetComponent<GatherParty>();
            return p != null ? p : a.gameObject.AddComponent<GatherParty>();
        }

        void Awake()
        {
            anchor = GetComponent<AnchorController>();
            voyage = GetComponent<VoyageManager>();
            if (voyage == null) voyage = FindFirstObjectByType<VoyageManager>();
            hold = GetComponentInChildren<ShipHold>();
            gangway = GetComponentInChildren<Gangway>();
        }

        // --- reading ------------------------------------------------------

        /// Hands out of the ship right now.
        public int Ashore
        {
            get
            {
                int n = 0;
                foreach (var h in hands) if (h != null && !h.IsAboard) n++;
                return n;
            }
        }

        /// Units in the party's arms right now.
        public int InHand
        {
            get
            {
                int n = 0;
                foreach (var h in hands) if (h != null && h.Party == this) n += h.CarriedUnits;
                return n;
            }
        }

        public string TargetLabel => Target == FillHold ? "hold" : Target.ToString();

        /// "Stone 7/20 · 3 ashore".
        public string StatusLine
            => $"{Resource} {DeliveredUnits}/{TargetLabel}  ·  {Ashore} ashore"
               + (Recalling ? "  ·  coming back" : "");

        /// Units the hold will still take.
        public int Room => CampLoading.RoomAboard(voyage);

        /// Hands aboard who could go (ours, on station). The ship needs no
        /// minimum sailing crew while at anchor, so every one of them.
        public static List<CrewAgent> Available(AnchorController a)
        {
            var list = new List<CrewAgent>();
            if (a == null) return list;
            foreach (var c in a.GetComponentsInChildren<CrewAgent>(false))
                if (c != null && c.Available && c.transform.IsChildOf(a.transform)) list.Add(c);
            return list;
        }

        /// What this island offers a party from `from`: every gatherable raw
        /// good with at least one reachable source, and how much.
        public static List<Option> Survey(Island isle, Vector3 from, GatherParty party)
        {
            var result = new List<Option>();
            if (isle == null) return result;
            if (party != null) party.StandSources(isle, from);
            var by = new Dictionary<string, Option>();
            foreach (var n in ResourceNode.All)
            {
                if (!Usable(n, isle, party)) continue;
                if (party != null ? !party.InReach(n, from) : !InReachRaw(n, from)) continue;
                by.TryGetValue(n.Resource, out var o);
                o.resource = n.Resource;
                o.sources++;
                o.units += UnitsIn(n);
                by[n.Resource] = o;
            }
            foreach (var r in Order)
                if (by.TryGetValue(r, out var o)) result.Add(o);
            foreach (var kv in by)
                if (System.Array.IndexOf(Order, kv.Key) < 0) result.Add(kv.Value);
            return result;
        }

        static readonly string[] Order = { Res.Stone, Res.Timber, Res.Ore, Res.Spice, Res.Food };

        // --- sending and recalling ----------------------------------------

        /// Send `count` hands for `amount` of `res` (or `FillHold`). False
        /// with the reason in `why`.
        public bool Send(string res, int amount, int count, out string why)
        {
            why = "";
            if (anchor == null) { why = "no ship"; return false; }
            if (Out) { why = "a party is already out"; return false; }
            if (!anchor.CanSendParty(out why)) return false;
            if (string.IsNullOrEmpty(res) || !Res.IsGatherable(res)) { why = "pick what to fetch"; return false; }
            if (Room <= 0) { why = "the hold is full"; return false; }
            island = anchor.CurrentIsland;
            if (Combat.EnemyShip.CountAt(island) > 0) { why = "raiders on this island"; return false; }

            landing = anchor.PartyLanding();
            StandSources(island, landing);
            var pick = Available(anchor);
            if (pick.Count == 0) { why = "no hands aboard"; return false; }
            count = Mathf.Clamp(count, 1, pick.Count);
            if (!HasSource(res)) { why = $"no {res.ToLowerInvariant()} within reach of the landing"; return false; }

            Resource = res;
            Target = amount <= 0 ? FillHold : amount;
            DeliveredUnits = 0;
            Trips = 0;
            SourcesTaken = 0;
            StopReason = "";
            Recalling = false;
            Out = true;
            hands.Clear();
            for (int i = 0; i < count; i++) hands.Add(pick[i]);
            anchor.PutPartyAshore(this, hands, landing);
            return true;
        }

        /// Everyone back to the ship. Loads in hand are delivered when they
        /// come aboard.
        public void Recall(string why)
        {
            if (!Out) return;
            if (string.IsNullOrEmpty(StopReason)) StopReason = why ?? "recalled";
            Recalling = true;
            foreach (var h in hands) if (h != null && h.Party == this) h.ReturnAboard();
        }

        // --- the crew's side (CrewAgent calls these) ----------------------

        /// Units still wanted, counting what is already in arms.
        int Wanted
        {
            get
            {
                int room = Room - InHand;
                if (Target == FillHold) return Mathf.Max(0, room);
                return Mathf.Max(0, Mathf.Min(room, Target - DeliveredUnits - InHand));
            }
        }

        /// The next source for `who`, claimed for him, or null (go home).
        public ResourceNode NextSource(CrewAgent who)
        {
            if (!Out || Recalling || who == null) return null;
            if (Wanted <= 0) { StopFor(); return null; }

            ResourceNode best = null, bestFits = null;
            float bestSq = float.MaxValue, fitsSq = float.MaxValue;
            int room = Room - InHand;
            Vector3 at = who.transform.position;
            foreach (var n in ResourceNode.All)
            {
                if (!Usable(n, island, this) || n.Resource != Resource || n.Claim.Held) continue;
                if (!InReach(n, landing)) continue;
                float sq = (n.transform.position - at).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = n; }
                // Prefer a source whose whole yield fits what the hold
                // still takes, so a rock is never broken for half of it.
                if (UnitsIn(n) <= room && sq < fitsSq) { fitsSq = sq; bestFits = n; }
            }
            var pick = bestFits != null ? bestFits : best;
            if (pick == null)
            {
                // Nothing left for this hand. The party stops once no one
                // is still working.
                if (!AnyoneWorking(who)) StopFor("nothing reachable left");
                return null;
            }
            return pick.TryClaim(who) ? pick : null;
        }

        /// The armful he takes from `n`: its whole yield, no more than the
        /// hold still takes and `MaxArmful`.
        public int ArmfulAt(ResourceNode n)
        {
            int units = UnitsIn(n);
            int room = Mathf.Max(1, Room - InHand);
            return Mathf.Clamp(Mathf.Min(units, room), 1, MaxArmful);
        }

        public float WorkSeconds(string res, int units)
        {
            float per = res == Res.Timber ? WoodSecondsPerUnit
                : res == Res.Stone ? StoneSecondsPerUnit
                : res == Res.Ore ? OreSecondsPerUnit
                : OtherSecondsPerUnit;
            return per * Mathf.Max(1, units);
        }

        /// A source came out of the ground.
        public void Cut(CrewAgent who, ResourceNode n, string res, int units)
        {
            SourcesTaken++;
        }

        /// A load reached the hold. Only what actually went in counts.
        public void Delivered(CrewAgent who, string res, int units)
        {
            if (voyage == null || units <= 0) return;
            int room = Room;
            int n = Mathf.Min(units, room);
            int before = voyage.TotalHeld;
            if (n > 0) voyage.AddLoot(n, res);
            int got = Mathf.Max(0, voyage.TotalHeld - before);
            if (hold != null) for (int i = 0; i < got; i++) hold.AddVisual(res);
            if (res == Resource) DeliveredUnits += got;
            Trips++;
            if (got < units) Debug.Log($"GatherParty: {units - got} {res} over the side -- the hold is full");
            if (Out && !Recalling && Wanted <= 0) StopFor();
        }

        /// He is back aboard (off the party).
        public void Boarded(CrewAgent who) { }

        void StopFor(string why = null)
        {
            if (!Out || Recalling) return;
            if (why == null)
                why = Target != FillHold && DeliveredUnits + InHand >= Target
                    ? "done" : "the hold is full";
            Recall(why);
        }

        bool AnyoneWorking(CrewAgent except)
        {
            foreach (var h in hands)
                if (h != null && h != except && h.Party == this && (h.CarriedUnits > 0 || h.StateName == "Chopping" || h.StateName == "ToNode"))
                    return true;
            return false;
        }

        // --- the frame ----------------------------------------------------

        void Update()
        {
            if (!Out) return;

            // She left, or the island went away under us: nobody can be
            // stranded, so hands still ashore are recalled.
            if (!Recalling && (anchor == null || anchor.CurrentIsland != island)) Recall("the ship moved");

            if (!Recalling && Time.time >= nextRaidLook)
            {
                nextRaidLook = Time.time + 1f;
                if (RaidersNear()) Recall("raiders! running for the ship");
            }

            // Done when everyone is back aboard.
            bool anyOut = false;
            foreach (var h in hands)
                if (h != null && h.gameObject.activeInHierarchy && h.Party == this) { anyOut = true; break; }
            if (!anyOut) Finish();
        }

        void Finish()
        {
            LastDelivered = DeliveredUnits;
            LastTrips = Trips;
            LastSourcesTaken = SourcesTaken;
            LastStop = string.IsNullOrEmpty(StopReason) ? "done" : StopReason;
            Out = false;
            Recalling = false;
            hands.Clear();
            ClearRocks();
            Debug.Log($"GatherParty: back aboard -- {DeliveredUnits} {Resource} in {Trips} trips, "
                + $"{SourcesTaken} sources taken ({LastStop})");
        }

        bool RaidersNear()
        {
            var raid = Combat.RaidParty.Active;
            bool raidHere = raid != null && raid.Landed && raid.Camp != null && raid.Camp.Island == island;
            if (raidHere) return true;
            if (raid == null && Combat.EnemyShip.CountAt(island) == 0) return false;
            var walkers = FindObjectsByType<Combat.RaidWalker>(FindObjectsSortMode.None);
            float r2 = RaidAlarmMetres * RaidAlarmMetres;
            foreach (var w in walkers)
            {
                if (w == null) continue;
                foreach (var h in hands)
                {
                    if (h == null || h.IsAboard) continue;
                    Vector3 d = w.transform.position - h.transform.position; d.y = 0f;
                    if (d.sqrMagnitude < r2) return true;
                }
            }
            return false;
        }

        void OnDisable()
        {
            if (Out) Recall("the ship went away");
        }

        // --- sources ------------------------------------------------------

        bool HasSource(string res)
        {
            foreach (var n in ResourceNode.All)
                if (Usable(n, island, this) && n.Resource == res && InReach(n, landing)) return true;
            return false;
        }

        /// Free to work: on this island (or one of the party's own rock
        /// nodes), standing, and a raw good.
        static bool Usable(ResourceNode n, Island isle, GatherParty party)
        {
            if (n == null || n.Harvested || !n.isActiveAndEnabled) return false;
            if (!Res.IsGatherable(n.Resource)) return false;
            if (n.Home == isle) return true;
            return party != null && party.partyRocks.Contains(n);
        }

        static int UnitsIn(ResourceNode n)
        {
            if (n == null) return 0;
            if (n.Deposit != null) return Mathf.Max(1, n.Deposit.Units);
            // One tree is one log (GDD 2026-08-18); a kit prop of ore or
            // spice is one prop's worth (`GatherSync`).
            return n.Resource == Res.Timber ? 1 : n.UnitsPerProp;
        }

        /// Within reach of the landing and a walkable straight line from it.
        /// Cached per node for the party's life.
        readonly Dictionary<ResourceNode, bool> reachCache = new Dictionary<ResourceNode, bool>();
        bool InReachCached(ResourceNode n, Vector3 from)
        {
            if (reachCache.TryGetValue(n, out bool ok)) return ok;
            ok = InReachRaw(n, from);
            reachCache[n] = ok;
            return ok;
        }

        bool InReach(ResourceNode n, Vector3 from) => InReachCached(n, from);

        static bool InReachRaw(ResourceNode n, Vector3 from)
        {
            Vector3 p = n.transform.position;
            Vector3 d = p - from; d.y = 0f;
            if (d.sqrMagnitude > Reach * Reach) return false;
            float stop = Mathf.Max(1.9f, n.StandOff + 0.6f);
            float len = d.magnitude;
            if (len <= stop) return true;
            return LineWalkable(from, from + d * ((len - stop) / len));
        }

        /// Can a man walk the straight line from `a` to `b`? No water (past
        /// the first few metres, where the landing meets the sea) and no
        /// step steeper than `Walkability` allows (downhill off a ledge is
        /// allowed, as it is everywhere else).
        public static bool LineWalkable(Vector3 a, Vector3 b)
        {
            var h = Island.TerrainHeight;
            if (h == null) return true;
            Vector3 d = b - a; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.01f) return true;
            Vector3 dir = d / len;
            Vector3 prev = new Vector3(a.x, h(a.x, a.z), a.z);
            int steps = Mathf.CeilToInt(len / LineStep);
            for (int i = 1; i <= steps; i++)
            {
                float t = Mathf.Min(len, i * LineStep);
                float x = a.x + dir.x * t, z = a.z + dir.z * t;
                float y = h(x, z);
                if (t > 6f && y < 0.2f) return false;              // water inland
                var next = new Vector3(x, y, z);
                if (!Walkability.MayStep(h, prev, next, Walkability.Feet.Man)) return false;
                prev = next;
            }
            return true;
        }

        /// Timber: materialise tree nodes round the landing (as the shore
        /// party does). Stone: when no camp stood nodes on the island's loose
        /// rocks, stand party-only nodes on the ones in reach -- `Home` null,
        /// so the camp systems (`GatherSync`, `CampWorker`) never see them.
        void StandSources(Island isle, Vector3 from)
        {
            if (isle == null) return;
            if (island != isle) { ClearRocks(); reachCache.Clear(); }
            island = isle;
            if ((from - landing).sqrMagnitude > 4f) reachCache.Clear();
            landing = from;

            var wood = isle.GetComponentInChildren<Terrain.SceneryWood>();
            if (wood != null) wood.Populate(from, Reach);

            if (partyRocks.Count > 0) return;
            var rocks = Terrain.SceneryRocks.On(isle);
            if (rocks == null || rocks.Materialized) return;   // a camp's nodes stand there
            var picks = new List<(float d2, int i)>();
            var h = Island.TerrainHeight;
            for (int i = 0; i < rocks.Count; i++)
            {
                if (rocks.IsHidden(i)) continue;
                var r = rocks.RockAt(i);
                Vector3 d = r.at - from; d.y = 0f;
                if (d.sqrMagnitude > Reach * Reach) continue;
                if (h != null && h(r.at.x, r.at.z) < 0.5f) continue;   // surf and wet sand stay scenery
                picks.Add((d.sqrMagnitude, i));
            }
            picks.Sort((x, y) => x.d2 != y.d2 ? x.d2.CompareTo(y.d2) : x.i.CompareTo(y.i));
            foreach (var (_, i) in picks)
            {
                if (partyRocks.Count >= MaxRockNodes) break;
                var r = rocks.RockAt(i);
                var go = new GameObject("PartyRock_" + i);
                go.transform.SetParent(rocks.transform, false);
                go.transform.position = r.at;
                var node = go.AddComponent<ResourceNode>();
                node.Configure(Res.Stone, null, 4);
                if (StoneDeposit.DressScenery(node, rocks, i) == null) { Destroy(go); continue; }
                if (!InReachRaw(node, from)) { Destroy(go); continue; }
                partyRocks.Add(node);
            }
        }

        /// Take the party's own rock nodes away. A rock already worked stays
        /// hidden (the deposit hid it and nothing shows it again).
        void ClearRocks()
        {
            foreach (var n in partyRocks) if (n != null) Destroy(n.gameObject);
            partyRocks.Clear();
            reachCache.Clear();
        }
    }
}
