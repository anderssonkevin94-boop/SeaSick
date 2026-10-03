using System.Collections.Generic;
using SeaSick.Crew;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.Ship
{
    /// **The landing party (2026-09-30; was the gather party, 2026-09-27).**
    /// Kevin, on the phone at Island_3: *"what if i want to hunt, or
    /// explore? ... this whole interaction needs to be re worked."* Anchored
    /// off an island with no camp, the ship sends chosen hands ashore on ONE
    /// of two orders (`Order`), picked on `UI.Sheets.LandingPartySheet`.
    /// **2026-10-03, Kevin: "remove the Explore card" -- Gather and Hunt
    /// stay.** Explore was a 120 s walk inland (a fanned stop per hand, a
    /// breadcrumb trail home, a boar-bite roll) whose whole yield was
    /// opening the fog of war; the fog went the same day and the trip was
    /// left with the risk and "Nothing new found". Its walk, trail, danger
    /// roll, dials (`ExploreSeconds/Step/Reach`, `BoarDangerMetres`,
    /// `HurtChance*`) and report line are deleted. What Gather and Hunt
    /// still share stays: the hurt-never-dead hand (`HurtName`, the
    /// `Kill` accident roll, `Boarded` rest) and the recall walk home. A
    /// saved party never exists (the save writes the ship's crew by name
    /// and the hold), so an old save made mid-Explore simply loads with
    /// every hand aboard; nothing in the save names an order.
    ///
    /// * **Gather** -- the 2026-09-27 trip, unchanged in its dials and its
    ///   booking (below). (2026-09-30 to 2026-10-03 it was limited to
    ///   sources on revealed ground; with the fog gone, any source in reach.)
    /// * **Hunt** -- each armed hunter stalks the nearest beast of
    ///   the chosen kind (`Animal.Hunted`, so it does not bolt), jabs or
    ///   shoots it from `SpearReach`/`BowReach`, shoulders the carcass
    ///   (`HunterProps`) and carries `Res.MeatPerAnimal` Meat plus
    ///   `Techs.HuntDrops` Hide into the hold. One beast per hunter
    ///   (`BeastsPerHunter`). A bow kill spends one arrow from the hold;
    ///   spears and bows are carried, not spent (no fractional wear in the
    ///   hold). The kill rolls the camp's own `LifeTuning.HuntAccidentChance`
    ///   (iron halves it) and an accident is hurt-not-dead: he still carries it
    ///   home, then comes aboard off station for `HurtRestSeconds`
    ///   (`CrewAgent.ApplyRescueAftermath`, the ship's twin of the
    ///   drag-to-hut recovery), logged `Downed`.
    ///
    /// **Weapons (`DealWeapons`).** A hand is armed by what the HOLD
    /// carries: iron spears, then stone spears, then bows while there are
    /// arrows, dealt to the chosen hands first in crew order.
    ///
    /// **The report.** When the last hand is aboard, `LastReport` says what
    /// they brought ("6 timber aboard", "2 goats · 6 meat aboard") and
    /// a toast shows it for 4.5 s (`PartyReportToast`, 2026-09-30), or the
    /// sheet's "now" card for `ReportSeconds` while it is open.
    ///
    /// --- 2026-09-27 (the gather trip, unchanged) ---------------------------
    ///
    /// **The trip.** Each hand walks from the landing to the free source of
    /// the good with the shortest WALK from the landing, works it at Kevin's
    /// dials (wood 5 s a log, stone 8 s, ore 10 s, per unit), carries the
    /// whole armful back over the plank and into the hold. Trip time is the
    /// walk itself: the body walks it.
    ///
    /// **The whole island (Kevin, 2026-10-03: "the landing party reaches
    /// the whole island, nothing moves, far = longer trip").** Until then
    /// a source had to stand within `Reach` (80 m) of the landing on a
    /// straight walkable line, so the Ore outcrop -- ringed inland round
    /// the surveyed clearing by `Outpost.PlaceCampStone` -- was never
    /// offered. The cap is gone and nothing is moved to the party: a
    /// source counts when a hand can walk to it, by the first of
    ///  1. a straight walkable line (`LineWalkable`: `Walkability.MayStep`
    ///     every `LineStep` m, no water inland) -- walked as it is;
    ///  2. a route over the island's own walk grid (`CampPath`, the camps'
    ///     A* map, which a surveyed camp-less island has too): ONE flood
    ///     from the landing (`CampPath.FloodFrom`) gives every source's
    ///     walked metres, and the hand walks its corners out and back
    ///     (`RouteTo`, `CrewAgent`'s retrace);
    /// else not at all (a source up a cliff, or no survey and no straight
    /// line). A far source is simply a longer trip: the tile shows "far ·
    /// m:ss" past `FarMetres` (`TripSeconds`). Hunting never had the cap
    /// (a hunter walks straight at the nearest beast of the kind).
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
    /// **What persists (2026-09-27).** Every source a hand cuts is booked
    /// BY NAME into the island's ledger (`OutpostLedger.BookGroundTake`: a
    /// loose rock by its index, a tree by its index, a kit deposit or an
    /// ore/spice prop by where it was made), and its whole yield leaves the
    /// island's stock -- `standing` and `standingMax` together, so the
    /// camp's count-derived picture (`GatherSync`, nearest the camp first)
    /// hides nothing extra, and every camp order leaves the named sources
    /// out. `GroundTaken.Apply` draws the set gone on every `CatchUp`, so it
    /// survives save/load (`SaveGame` keeps a camp-less ledger that has
    /// named takes) and an island rebuilt from scratch; a camp made there
    /// later starts from the reduced stock and never sees those sources.
    /// A party hunt takes one animal off the ledger's `Game` stock the same
    /// way, when the island has a ledger.
    public class GatherParty : MonoBehaviour
    {
        // --- Kevin's playtest dials (GDD, trips) --------------------------
        public const float WoodSecondsPerUnit = 5f;
        public const float StoneSecondsPerUnit = 8f;
        public const float OreSecondsPerUnit = 10f;
        public const float OtherSecondsPerUnit = 5f;

        /// A source further than this WALK from the landing is "far" on the
        /// sheet (its tile shows the trip time). The old `Reach` cap's 80 m:
        /// a party used to go no further (2026-10-03: now it goes anywhere
        /// it can walk).
        public const float FarMetres = 80f;
        /// Radius trees are stood up round the landing for a party -- the
        /// whole island (`SceneryWood.Populate` still stands the 48 nearest).
        const float WholeIsland = 2000f;
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

        // --- the landing party's dials (2026-09-30, PROVISIONAL, unplayed) --
        // (The explore dials and the boar-encounter chances went with the
        // Explore order, 2026-10-03; a hunt's own risk is
        // `LifeTuning.HuntAccidentChance`.)
        /// Seconds a hurt hand is off station once aboard (never death).
        public const float HurtRestSeconds = 120f;
        /// Sickness a hurt hand comes back with (shaken).
        public const float HurtSicknessSpike = 0.1f;
        /// Reach a hunter strikes from, metres.
        public const float SpearReach = 2.0f;
        public const float BowReach = 12f;
        /// The jab / the draw, seconds at the beast.
        public const float JabSeconds = 1.2f;
        /// The flop before he lifts it, seconds.
        public const float FlopSeconds = 0.6f;
        /// Beasts one hunter brings home per trip.
        public const int BeastsPerHunter = 1;
        /// Seconds the result toast stays up.
        public const float ReportSeconds = 8f;

        public enum Order { Gather, Hunt }

        public struct Option
        {
            public string resource;
            public int sources;
            public int units;
            /// Metres walked from the landing to the NEAREST source of it.
            public float nearestMetres;
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
        /// What the party was sent to do.
        public Order Mode { get; private set; }
        public string Resource { get; private set; }
        /// Units wanted, or `FillHold`.
        public int Target { get; private set; }
        public int DeliveredUnits { get; private set; }
        public int Trips { get; private set; }
        public int SourcesTaken { get; private set; }
        /// Why the party stopped (or is stopping); empty while it works.
        public string StopReason { get; private set; } = "";
        public Island Island => island;
        /// The hands who went, in the order they were picked.
        public IReadOnlyList<CrewAgent> Hands => hands;
        /// The beast a hunt was sent for.
        public Animal.Kind HuntKind { get; private set; }
        /// Beasts killed this trip, and what came aboard from them.
        public int Kills { get; private set; }
        public int MeatAboard { get; private set; }
        public int HideAboard { get; private set; }
        /// A hand hurt this trip, or null.
        public string HurtName { get; private set; }

        /// The last party's numbers, for `GatherPartyCheck`.
        public static int LastDelivered, LastTrips, LastSourcesTaken;
        public static string LastStop = "";
        /// What the last party brought or found, and when (unscaled).
        public static string LastReport { get; private set; } = "";
        public static float LastReportAt { get; private set; } = -999f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            LastReport = "";
            LastReportAt = -999f;
        }

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

        // --- per hand -------------------------------------------------------

        enum Phase { Out, Stalk, Jab, Flop, Carry, Done }

        sealed class Job
        {
            public Phase phase;
            public string weapon;
            public Animal target;
            public bool hurt;
            public int kills;
        }

        readonly Dictionary<CrewAgent, Job> jobs = new Dictionary<CrewAgent, Job>();
        FaunaLod fauna;
        string hurtCause;

        Job JobOf(CrewAgent who) => who != null && jobs.TryGetValue(who, out var j) ? j : null;

        /// The weapon a hand in this party carries, or null.
        public string WeaponOf(CrewAgent who) => JobOf(who)?.weapon;

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

        /// "Stone 7/20 · 3 ashore" (gather), "Hunting goats · 0/1 · 1
        /// ashore".
        public string StatusLine
        {
            get
            {
                string tail = $"  ·  {Ashore} ashore" + (Recalling ? "  ·  coming back" : "");
                switch (Mode)
                {
                    case Order.Hunt:
                        return $"Hunting {HerdWord(HuntKind, 2)} {Kills}/{Mathf.Max(1, HunterCount) * BeastsPerHunter}" + tail;
                    default:
                        return $"{Resource} {DeliveredUnits}/{TargetLabel}" + tail;
                }
            }
        }

        int HunterCount
        {
            get
            {
                int n = 0;
                foreach (var h in hands) if (JobOf(h)?.weapon != null) n++;
                return n;
            }
        }

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

        /// Every hand of the ship's company still under her (on station or
        /// not), for the sheet's WHO GOES row -- a busy one is shown, locked.
        public static List<CrewAgent> Company(AnchorController a)
        {
            var list = new List<CrewAgent>();
            if (a == null) return list;
            foreach (var c in a.GetComponentsInChildren<CrewAgent>(false))
                if (c != null && c.transform.IsChildOf(a.transform)) list.Add(c);
            return list;
        }

        /// **Who carries what (2026-09-30).** Deals the HOLD's weapons to
        /// `order` (chosen hands first): iron spears, then stone spears, then
        /// bows while any arrow is aboard. `into[hand]` = `Res.IronSpear`,
        /// `Res.Spear`, `Res.Bow` or null (unarmed).
        public static void DealWeapons(VoyageManager v, IList<CrewAgent> order, Dictionary<CrewAgent, string> into)
        {
            into.Clear();
            if (order == null) return;
            int iron = v != null ? v.HeldOf(Res.IronSpear) : 0;
            int stone = v != null ? v.HeldOf(Res.Spear) : 0;
            int bows = v != null && v.HeldOf(Res.Arrows) > 0 ? v.HeldOf(Res.Bow) : 0;
            foreach (var c in order)
            {
                if (c == null || into.ContainsKey(c)) continue;
                string w = null;
                if (iron > 0) { iron--; w = Res.IronSpear; }
                else if (stone > 0) { stone--; w = Res.Spear; }
                else if (bows > 0) { bows--; w = Res.Bow; }
                into[c] = w;
            }
        }

        /// What this island offers a party from `from`: every gatherable raw
        /// good with at least one reachable source, and how much (on revealed
        /// ground only until the fog went, 2026-10-03).
        public static List<Option> Survey(Island isle, Vector3 from, GatherParty party)
        {
            var result = new List<Option>();
            if (isle == null) return result;
            if (party != null) party.StandSources(isle, from);
            var by = new Dictionary<string, Option>();
            foreach (var n in ResourceNode.All)
            {
                if (!Usable(n, isle, party)) continue;
                float metres;
                if (party != null) { var w = party.WalkOf(n); if (!w.ok) continue; metres = w.metres; }
                else if (!InReachRaw(n, from, out metres)) continue;
                bool first = !by.TryGetValue(n.Resource, out var o);
                o.resource = n.Resource;
                o.sources++;
                o.units += UnitsIn(n);
                o.nearestMetres = first ? metres : Mathf.Min(o.nearestMetres, metres);
                by[n.Resource] = o;
            }
            foreach (var r in Order_)
                if (by.TryGetValue(r, out var o)) result.Add(o);
            foreach (var kv in by)
                if (System.Array.IndexOf(Order_, kv.Key) < 0) result.Add(kv.Value);
            return result;
        }

        static readonly string[] Order_ = { Res.Stone, Res.Timber, Res.Ore, Res.Spice, Res.Food };

        /// "goat"/"goats", "boar".
        public static string HerdWord(Animal.Kind k, int n) =>
            k == Animal.Kind.Goat ? (n == 1 ? "goat" : "goats") : "boar";

        // --- sending and recalling ----------------------------------------

        /// Send `count` hands for `amount` of `res` (or `FillHold`). False
        /// with the reason in `why`. (The 2026-09-27 entry, kept for
        /// `GatherPartyCheck`; the sheet picks hands by name.)
        public bool Send(string res, int amount, int count, out string why)
        {
            var pick = Available(anchor);
            count = Mathf.Clamp(count, 1, Mathf.Max(1, pick.Count));
            if (pick.Count > count) pick.RemoveRange(count, pick.Count - count);
            return SendGather(res, amount, pick, out why);
        }

        /// Gather: `who` fetch `amount` of `res` (or `FillHold`).
        public bool SendGather(string res, int amount, List<CrewAgent> who, out string why)
        {
            if (!Ready(who, out why)) return false;
            if (string.IsNullOrEmpty(res) || !Res.IsGatherable(res)) { why = "pick what to fetch"; return false; }
            if (Room <= 0) { why = "the hold is full"; return false; }
            StandSources(island, landing);
            if (!HasSource(res)) { why = $"no {res.ToLowerInvariant()} the hands can walk to"; return false; }

            Resource = res;
            Target = amount <= 0 ? FillHold : amount;
            Begin(Order.Gather, who);
            return true;
        }

        /// Hunt: the ARMED hands of `who` each bring home one `kind`.
        public bool SendHunt(Animal.Kind kind, List<CrewAgent> who, out string why)
        {
            if (!Ready(who, out why)) return false;
            if (Room <= 0) { why = "the hold is full"; return false; }
            var deal = new Dictionary<CrewAgent, string>();
            DealWeapons(voyage, who, deal);
            var armed = new List<CrewAgent>();
            foreach (var c in who) if (deal.TryGetValue(c, out var w) && w != null) armed.Add(c);
            if (armed.Count == 0) { why = "nobody is armed"; return false; }
            FindFauna();
            if (NearestBeast(kind, landing, null) == null) { why = $"no {HerdWord(kind, 2)} on this island"; return false; }
            HuntKind = kind;
            Resource = Res.Meat;
            Target = 0;
            Begin(Order.Hunt, armed);
            return true;
        }

        bool Ready(List<CrewAgent> who, out string why)
        {
            why = "";
            if (anchor == null) { why = "no ship"; return false; }
            if (Out) { why = "a party is already out"; return false; }
            if (!anchor.CanSendParty(out why)) return false;
            island = anchor.CurrentIsland;
            if (Combat.EnemyShip.CountAt(island) > 0) { why = "raiders on this island"; return false; }
            // The island's books are where the take is written; the survey
            // runs while the camera rises, so this is a moment at most.
            if (Outpost.Surveying(island)) { why = "still looking the island over"; return false; }
            if (who == null || who.Count == 0) { why = "pick who goes"; return false; }
            foreach (var c in who)
                if (c == null || !c.Available) { why = $"{(c != null ? c.DisplayName : "somebody")} is busy"; return false; }
            landing = anchor.PartyLanding();
            return true;
        }

        void Begin(Order order, List<CrewAgent> who)
        {
            Mode = order;
            DeliveredUnits = 0;
            Trips = 0;
            SourcesTaken = 0;
            BookedSources = 0;
            Kills = 0;
            MeatAboard = 0;
            HideAboard = 0;
            StopReason = "";
            HurtName = null;
            hurtCause = null;
            Recalling = false;
            Out = true;
            FindFauna();

            hands.Clear();
            jobs.Clear();
            var deal = new Dictionary<CrewAgent, string>();
            DealWeapons(voyage, who, deal);
            foreach (var c in who)
            {
                if (c == null || hands.Contains(c)) continue;
                hands.Add(c);
                deal.TryGetValue(c, out var w);
                jobs[c] = new Job
                {
                    weapon = w,
                    phase = order == Order.Hunt ? Phase.Stalk : Phase.Out,
                };
            }
            anchor.PutPartyAshore(this, hands, landing);
        }

        /// Everyone back to the ship. Loads in hand are delivered when they
        /// come aboard.
        public void Recall(string why)
        {
            if (!Out) return;
            if (string.IsNullOrEmpty(StopReason)) StopReason = why ?? "recalled";
            Recalling = true;
            foreach (var h in hands)
            {
                if (h == null || h.Party != this) continue;
                var j = JobOf(h);
                if (j != null && j.target != null && !j.target.Dead) { j.target.Hunted = false; j.target = null; }
                h.ReturnAboard();
            }
        }

        // --- the crew's side (CrewAgent calls these) ----------------------

        /// A gather party works sources (`NextSource`); a hunt party walks
        /// errands (`NextErrand`).
        public bool Gathers => Mode == Order.Gather;

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

            // Shortest WALK from the landing (2026-10-03): every trip starts
            // at the ship, and on the whole island a source close as the
            // crow flies can be a long way round.
            ResourceNode best = null, bestFits = null;
            float bestSq = float.MaxValue, fitsSq = float.MaxValue;
            int room = Room - InHand;
            foreach (var n in ResourceNode.All)
            {
                if (!Usable(n, island, this) || n.Resource != Resource || n.Claim.Held) continue;
                var w = WalkOf(n);
                if (!w.ok) continue;
                float sq = w.metres;
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

        /// **The next place a hunter walks to**, or false (go
        /// aboard by the plank).
        public bool NextErrand(CrewAgent who, out Vector3 at)
        {
            at = default;
            var j = JobOf(who);
            if (!Out || j == null) return false;
            if (Mode == Order.Hunt)
            {
                if (Recalling || j.weapon == null || j.kills >= BeastsPerHunter || Room <= 0) return false;
                if (j.target == null || j.target.Dead || j.target.Shouldered)
                {
                    j.target = NearestBeast(HuntKind, who.transform.position, who);
                    if (j.target == null)
                    {
                        if (!AnyoneHunting(who)) StopFor($"no {HerdWord(HuntKind, 2)} left in sight");
                        return false;
                    }
                    j.target.Hunted = true;
                }
                j.phase = Phase.Stalk;
                at = j.target.transform.position;
                return true;
            }
            return false;
        }

        /// How short of the errand's point he stops.
        public float ErrandStandOff(CrewAgent who)
        {
            var j = JobOf(who);
            if (j != null && Mode == Order.Hunt && j.phase == Phase.Stalk)
                return j.weapon == Res.Bow ? BowReach : SpearReach;
            return 1.2f;
        }

        /// He got there. Seconds to stand (then `ErrandDone`), or 0 to ask
        /// `NextErrand` again at once.
        public float ErrandReached(CrewAgent who)
        {
            var j = JobOf(who);
            if (j == null) return 0f;
            if (Mode == Order.Hunt && j.phase == Phase.Stalk && j.target != null && !j.target.Dead)
            {
                j.phase = Phase.Jab;
                return JabSeconds;
            }
            return 0f;
        }

        /// The stand at the errand is over. Seconds to stand again, or 0.
        public float ErrandDone(CrewAgent who)
        {
            var j = JobOf(who);
            if (j == null || Mode != Order.Hunt) return 0f;
            if (j.phase == Phase.Jab)
            {
                if (j.target == null || j.target.Dead || Recalling) { j.phase = Phase.Stalk; return 0f; }
                Kill(who, j);
                j.phase = Phase.Flop;
                return FlopSeconds;
            }
            if (j.phase == Phase.Flop)
            {
                var a = j.target;
                if (a != null) HunterProps.On(who.gameObject).Shoulder(a);
                j.target = null;
                j.phase = Phase.Carry;
                int meat = Mathf.Max(1, Mathf.RoundToInt(Res.MeatPerAnimal));
                who.PartyCarry(Res.Meat, meat, a == null);
            }
            return 0f;
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

        /// A source came out of the ground: booked by name into the island's
        /// ledger with its WHOLE yield (a rock broken for less than it holds
        /// is still gone), before the node is harvested.
        public void Cut(CrewAgent who, ResourceNode n, string res, int units)
        {
            SourcesTaken++;
            var o = Outpost.Of(island);
            if (o != null && o.Ledger != null && n != null)
            {
                o.Ledger.BookGroundTake(n, UnitsIn(n));
                BookedSources++;
            }
            else Debug.LogWarning($"GatherParty: no ledger on {(island != null ? island.name : "?")} -- this take will not persist");
        }

        /// Sources booked into the island's ledger this party (for the check).
        public int BookedSources { get; private set; }

        /// A load reached the hold. Only what actually went in counts.
        public void Delivered(CrewAgent who, string res, int units)
        {
            if (voyage == null || units <= 0) return;
            int got = Load(res, units);
            if (res == Resource) DeliveredUnits += got;
            Trips++;
            if (got < units) Debug.Log($"GatherParty: {units - got} {res} over the side -- the hold is full");

            var j = JobOf(who);
            if (Mode == Order.Hunt && j != null && j.phase == Phase.Carry)
            {
                MeatAboard += got;
                foreach (var drop in World.Economy.Techs.HuntDrops)
                    if (drop.n > 0) HideAboard += Load(drop.res, drop.n);
                HunterProps.On(who.gameObject).PutDown();
                j.phase = Phase.Done;
            }
            if (Mode == Order.Gather && Out && !Recalling && Wanted <= 0) StopFor();
        }

        int Load(string res, int units)
        {
            int n = Mathf.Min(units, Room);
            int before = voyage.TotalHeld;
            if (n > 0) voyage.AddLoot(n, res);
            int got = Mathf.Max(0, voyage.TotalHeld - before);
            if (hold != null) for (int i = 0; i < got; i++) hold.AddVisual(res);
            return got;
        }

        /// He is back aboard (off the party). A hurt hand rests: off station
        /// for `HurtRestSeconds`, never dead.
        public void Boarded(CrewAgent who)
        {
            var j = JobOf(who);
            if (j == null || !j.hurt) return;
            who.ApplyRescueAftermath(HurtSicknessSpike, HurtRestSeconds);
            World.Life.Lives.Log(who.DisplayName, World.Life.LifeEvents.Downed,
                island != null ? island.name : "", hurtCause);
        }

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

        bool AnyoneHunting(CrewAgent except)
        {
            foreach (var h in hands)
            {
                if (h == null || h == except || h.Party != this) continue;
                var j = JobOf(h);
                if (j != null && (j.phase == Phase.Jab || j.phase == Phase.Flop || j.phase == Phase.Carry
                                  || (j.phase == Phase.Stalk && j.target != null)))
                    return true;
            }
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

            DriveHunters();

            // Done when everyone is back aboard.
            bool anyOut = false;
            foreach (var h in hands)
                if (h != null && h.gameObject.activeInHierarchy && h.Party == this) { anyOut = true; break; }
            if (!anyOut) Finish();
        }

        /// Spears and bows drawn in hand, the beast followed, the carcass
        /// kept on his shoulders (`HunterProps` drops anything not driven
        /// this frame).
        void DriveHunters()
        {
            // A gather party works with its tools, not its spears.
            if (Mode == Order.Gather) return;
            foreach (var h in hands)
            {
                if (h == null || h.Party != this || !h.IsAshore) continue;
                var j = JobOf(h);
                if (j == null) continue;
                if (Mode == Order.Hunt && j.phase == Phase.Stalk && j.target != null && !j.target.Dead)
                    h.RetargetErrand(j.target.transform.position);
                bool carrying = Mode == Order.Hunt && j.phase == Phase.Carry;
                if (j.weapon == null && !carrying) continue;
                var props = HunterProps.On(h.gameObject);
                var pose = j.phase == Phase.Jab ? HunterProps.Pose.Thrust : HunterProps.Pose.Upright;
                Vector3 aim = j.target != null ? j.target.transform.position : h.transform.position + h.transform.forward;
                props.Drive(j.weapon, pose, aim);
            }
        }

        void Finish()
        {
            LastDelivered = DeliveredUnits;
            LastTrips = Trips;
            LastSourcesTaken = SourcesTaken;
            LastStop = string.IsNullOrEmpty(StopReason) ? "done" : StopReason;
            foreach (var kv in jobs)
                if (kv.Value.target != null && !kv.Value.target.Dead) kv.Value.target.Hunted = false;
            Report();
            Out = false;
            Recalling = false;
            hands.Clear();
            jobs.Clear();
            ClearRocks();
            Debug.Log($"GatherParty: back aboard ({Mode}) -- {DeliveredUnits} {Resource} in {Trips} trips, "
                + $"{SourcesTaken} sources taken, {Kills} kills ({LastStop})");
        }

        // --- the report ---------------------------------------------------

        /// "6 timber aboard" / "2 goats · 6 meat and 2 hide aboard" / "Bo was
        /// hurt by a boar -- resting aboard".
        void Report()
        {
            var parts = new List<string>();
            // 2026-09-30 to 2026-10-03 the report led with what the trip had
            // newly FOUND (resources, herds, finds on ground it opened). The
            // fog and the Explore order are gone, so there is no news line.
            if (Mode == Order.Gather && DeliveredUnits > 0)
                parts.Add($"{DeliveredUnits} {World.Economy.ResDefs.Label(Resource).ToLowerInvariant()} aboard");
            if (Mode == Order.Hunt)
            {
                parts.Add(Kills > 0
                    ? $"{Kills} {HerdWord(HuntKind, Kills)} · {MeatAboard} meat" + (HideAboard > 0 ? $" and {HideAboard} hide" : "") + " aboard"
                    : "No kill");
            }
            if (HurtName != null) parts.Add($"{HurtName} was hurt by {hurtCause}, resting aboard");
            else if (StopReason.StartsWith("raiders")) parts.Add("ran from raiders");

            LastReport = string.Join(" · ", parts);
            LastReportAt = Time.unscaledTime;
        }

        /// True while the last report is fresh enough to toast.
        public static bool ReportFresh => !string.IsNullOrEmpty(LastReport)
                                          && Time.unscaledTime - LastReportAt < ReportSeconds;

        public static void DismissReport() => LastReportAt = -999f;

        // The result toast is `SeaSick.UI.Sheets.PartyReportToast` (UI
        // Toolkit, the Next card's look) since 2026-09-30; the IMGUI label
        // that stood here drew as a blurry stretched ellipse on the phone.

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

        // --- hunt -----------------------------------------------------------

        void FindFauna()
        {
            if (fauna != null && fauna.Island == island) return;
            fauna = null;
            foreach (var f in FindObjectsByType<FaunaLod>(FindObjectsSortMode.None))
                if (f != null && f.Island == island) { fauna = f; break; }
        }

        /// The nearest live, unclaimed beast of `kind` (any on the island
        /// since the fog went, 2026-10-03).
        Animal NearestBeast(Animal.Kind kind, Vector3 from, CrewAgent who)
        {
            if (fauna == null) return null;
            Animal best = null;
            float bestSq = float.MaxValue;
            foreach (var a in fauna.Animals)
            {
                if (a == null || a.Dead || a.Hunted || a.kind != kind) continue;
                float sq = (a.transform.position - from).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = a; }
            }
            return best;
        }

        /// **The kill.** The beast dies where it stands (it waits for its
        /// hunter, `Animal.AwaitingHunter`); a bow spends one arrow from the
        /// hold; the island's books lose one head of game; the hunting
        /// accident is rolled -- hurt, never dead.
        void Kill(CrewAgent who, Job j)
        {
            var a = j.target;
            if (a == null) return;
            a.Hunted = true;   // `Die` keeps the carcass for a claimed beast
            a.Die();
            j.kills++;
            Kills++;
            if (j.weapon == Res.Bow && voyage != null && voyage.RemoveLoot(1, Res.Arrows) > 0 && hold != null)
                hold.RemoveVisual(Res.Arrows);
            var o = Outpost.Of(island);
            var stock = o != null && o.Ledger != null ? o.Ledger.Stock(Res.Game) : null;
            if (stock != null) stock.standing = Mathf.Max(0f, stock.standing - 1f);
            World.Life.Lives.Log(who.DisplayName, World.Life.LifeEvents.HuntingKill, island != null ? island.name : "");
            float chance = World.Life.LifeTuning.HuntAccidentChance * (j.weapon == Res.IronSpear ? 0.5f : 1f);
            if (HurtName == null && Random.value < chance)
            {
                // He still carries it home; the others come back with him.
                j.hurt = true;
                HurtName = who.DisplayName;
                hurtCause = a.kind == Animal.Kind.Boar ? "the boar" : "a hunting fall";
            }
        }

        // --- sources ------------------------------------------------------

        bool HasSource(string res)
        {
            foreach (var n in ResourceNode.All)
                if (Usable(n, island, this) && n.Resource == res && WalkOf(n).ok) return true;
            return false;
        }

        /// Free to work: on this island (or one of the party's own rock
        /// nodes), standing, and a raw good. (2026-09-30 to 2026-10-03 also
        /// only on revealed ground; the fog of war is gone.)
        static bool Usable(ResourceNode n, Island isle, GatherParty party)
        {
            if (n == null || n.Harvested || !n.isActiveAndEnabled) return false;
            if (!Res.IsGatherable(n.Resource)) return false;
            if (TakenByName(n, isle)) return false;
            bool ours = n.Home == isle || (party != null && party.partyRocks.Contains(n));
            return ours;
        }

        /// Booked gone by an earlier party (`OutpostLedger.GroundTaken`).
        static bool TakenByName(ResourceNode n, Island isle)
        {
            var o = Outpost.Of(isle);
            return o != null && o.Ledger != null && o.Ledger.Taken(n);
        }

        static int UnitsIn(ResourceNode n)
        {
            if (n == null) return 0;
            if (n.Deposit != null) return Mathf.Max(1, n.Deposit.Units);
            // One tree is one log (GDD 2026-08-18); a kit prop of ore or
            // spice is one prop's worth (`GatherSync`).
            return n.Resource == Res.Timber ? 1 : n.UnitsPerProp;
        }

        /// **How a hand gets from the landing to a source (2026-10-03).**
        /// `ok` false: he cannot. `metres`: the walk one way. `routed`: over
        /// the walk grid's corners (`RouteTo`) rather than a straight line.
        public struct Walk { public bool ok, routed; public float metres; }

        /// Per node, for the party's life at this landing (cleared when the
        /// landing or the island changes, `StandSources`).
        readonly Dictionary<ResourceNode, Walk> reachCache = new Dictionary<ResourceNode, Walk>();
        CampPath walkMap;
        bool floodTried, floodOk;

        /// The walk from the landing to `n`, cached. See the class notes:
        /// straight line first, then the flood over the island's walk grid.
        public Walk WalkOf(ResourceNode n)
        {
            if (n == null) return default;
            if (reachCache.TryGetValue(n, out var w)) return w;
            w = default;
            if (InReachRaw(n, landing, out float straight)) { w.ok = true; w.metres = straight; }
            else
            {
                if (!floodTried)
                {
                    floodTried = true;
                    var o = Outpost.Of(island);
                    walkMap = o != null && o.Sited ? CampPath.For(o) : null;
                    floodOk = walkMap != null && walkMap.FloodFrom(landing);
                }
                if (floodOk && walkMap.FloodReach(n.transform.position, n.StandOff + 0.6f, out float m))
                {
                    w.ok = true; w.routed = true; w.metres = m;
                }
            }
            reachCache[n] = w;
            return w;
        }

        /// The way from the landing to `n` as points to walk in order, the
        /// last being the source itself: just the source for a straight
        /// walk, the grid's corners for a routed one. False if unreachable.
        public bool RouteTo(ResourceNode n, List<Vector3> into)
        {
            into.Clear();
            var w = WalkOf(n);
            if (!w.ok) return false;
            if (!w.routed || walkMap == null || !walkMap.FloodRoute(n.transform.position, n.StandOff + 0.6f, into))
            {
                into.Clear();
                into.Add(n.transform.position);
            }
            return true;
        }

        /// Seconds one trip to a source `metres` away takes: out and back at
        /// the crew's shore pace, plus working one armful. For the sheet's
        /// "far" tag; the trip itself is timed by the body walking it.
        public float TripSeconds(string res, float metres, int units)
        {
            float pace = 0f;
            foreach (var c in Company(anchor)) { if (c != null) { pace = c.ShoreWalkSpeed; break; } }
            if (pace <= 0.1f) pace = 6.5f;
            return 2f * metres / pace + WorkSeconds(res, Mathf.Clamp(units, 1, MaxArmful));
        }

        /// A walkable straight line from `from` to where a hand stands to
        /// work `n` (no distance cap since 2026-10-03), and its length.
        static bool InReachRaw(ResourceNode n, Vector3 from, out float metres)
        {
            Vector3 p = n.transform.position;
            Vector3 d = p - from; d.y = 0f;
            float stop = Mathf.Max(1.9f, n.StandOff + 0.6f);
            float len = d.magnitude;
            metres = len;
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
        /// rocks, stand party-only nodes on the ones a hand can walk to --
        /// `Home` null, so the camp systems (`GatherSync`, `CampWorker`)
        /// never see them. Both nearest the landing first and capped (48
        /// trees, `MaxRockNodes` rocks), but no longer within 80 m: the
        /// whole island (2026-10-03).
        void StandSources(Island isle, Vector3 from)
        {
            if (isle == null) return;
            if (island != isle) { ClearRocks(); ForgetWalks(); }
            island = isle;
            if ((from - landing).sqrMagnitude > 4f) ForgetWalks();
            landing = from;

            var wood = isle.GetComponentInChildren<Terrain.SceneryWood>();
            if (wood != null) wood.Populate(from, WholeIsland);

            if (partyRocks.Count > 0) return;
            var rocks = Terrain.SceneryRocks.On(isle);
            if (rocks == null || rocks.Materialized) return;   // a camp's nodes stand there
            var picks = new List<(float d2, int i)>();
            var h = Island.TerrainHeight;
            var books = Outpost.Of(isle) != null ? Outpost.Of(isle).Ledger : null;
            for (int i = 0; i < rocks.Count; i++)
            {
                if (rocks.IsHidden(i)) continue;
                if (books != null && books.RockTaken(i)) continue;
                var r = rocks.RockAt(i);
                Vector3 d = r.at - from; d.y = 0f;
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
                if (!WalkOf(node).ok) { Destroy(go); continue; }
                partyRocks.Add(node);
            }
        }

        /// Take the party's own rock nodes away. A rock already worked stays
        /// hidden (the deposit hid it and nothing shows it again).
        void ClearRocks()
        {
            foreach (var n in partyRocks) if (n != null) Destroy(n.gameObject);
            partyRocks.Clear();
            ForgetWalks();
        }

        /// The landing moved or the island changed: every cached walk and
        /// the flood are stale.
        void ForgetWalks()
        {
            reachCache.Clear();
            floodTried = false;
            floodOk = false;
            walkMap = null;
        }
    }
}
