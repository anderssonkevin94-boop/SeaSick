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
    /// of three orders (`Order`), picked on `UI.Sheets.LandingPartySheet`:
    ///
    /// * **Explore** -- the party walks into the fog for `ExploreSeconds`
    ///   of walking, each hand along his own fanned bearing, every stop a
    ///   walkable straight line (`LineWalkable`) that leads into the most
    ///   unrevealed ground (`IslandFog.IsRevealed`). Every hand ashore, on
    ///   any order, reveals `RevealRadius` round himself
    ///   (`IslandFog.RevealAround`). They come home the way they went (a
    ///   breadcrumb trail per hand), never in a straight line over a cliff.
    ///   **Mild risk:** the first time an explorer comes within
    ///   `BoarDangerMetres` of a live boar it is rolled once --
    ///   `HurtChanceUnarmed` for a party with no weapon among them,
    ///   `HurtChanceArmed` when one of them carries a spear or bow. A hurt
    ///   hand is never killed: the party turns for home and he comes aboard
    ///   off station for `HurtRestSeconds` (`CrewAgent.ApplyRescueAftermath`,
    ///   the ship's twin of the drag-to-hut recovery), logged `Downed`.
    /// * **Gather** -- the 2026-09-27 trip, unchanged in its dials and its
    ///   booking (below), now limited to sources on REVEALED ground.
    /// * **Hunt** -- each armed hunter stalks the nearest revealed beast of
    ///   the chosen kind (`Animal.Hunted`, so it does not bolt), jabs or
    ///   shoots it from `SpearReach`/`BowReach`, shoulders the carcass
    ///   (`HunterProps`) and carries `Res.MeatPerAnimal` Meat plus
    ///   `Techs.HuntDrops` Hide into the hold. One beast per hunter
    ///   (`BeastsPerHunter`). A bow kill spends one arrow from the hold;
    ///   spears and bows are carried, not spent (no fractional wear in the
    ///   hold). The kill rolls the camp's own `LifeTuning.HuntAccidentChance`
    ///   (iron halves it) and an accident is the same hurt-not-dead as above.
    ///
    /// **Weapons (`DealWeapons`).** A hand is armed by what the HOLD
    /// carries: iron spears, then stone spears, then bows while there are
    /// arrows, dealt to the chosen hands first in crew order.
    ///
    /// **The report.** When the last hand is aboard, `LastReport` says what
    /// they brought or found ("Found ore and a cairn · 6 timber aboard") and
    /// a toast shows it for `ReportSeconds` (`OnGUI`, the game's ice toast
    /// card), or the sheet's "now" card while it is open.
    ///
    /// --- 2026-09-27 (the gather trip, unchanged) ---------------------------
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

        // --- the landing party's dials (2026-09-30, PROVISIONAL, unplayed) --
        /// Seconds of walking an explore trip lasts before it turns home.
        public const float ExploreSeconds = 120f;
        /// Metres of fog a hand ashore clears round himself.
        public const float RevealRadius = 25f;
        /// How far one explore stop is from the last, metres.
        public const float ExploreStep = 22f;
        /// A live boar this close to an explorer is an encounter (rolled once).
        public const float BoarDangerMetres = 18f;
        /// Chance an encounter hurts somebody: nobody in the party armed / armed.
        public const float HurtChanceUnarmed = 0.35f;
        public const float HurtChanceArmed = 0.05f;
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

        public enum Order { Gather, Explore, Hunt }

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
        /// Seconds since the party left (explore counts its trip on it).
        public float Elapsed => Out ? Time.time - startedAt : 0f;
        /// 0..1 of an explore trip's walking time.
        public float Explore01 => Mathf.Clamp01(Elapsed / ExploreSeconds);
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

        enum Phase { Out, Home, Stalk, Jab, Flop, Carry, Done }

        sealed class Job
        {
            public Phase phase;
            public string weapon;
            public Animal target;
            public bool hurt;
            public int kills;
            public Vector3 heading;
            /// Explore stops reached, oldest first: the way home, reversed.
            public readonly List<Vector3> trail = new List<Vector3>();
        }

        readonly Dictionary<CrewAgent, Job> jobs = new Dictionary<CrewAgent, Job>();
        float startedAt, nextReveal, nextDanger;
        readonly HashSet<Animal> metBoars = new HashSet<Animal>();
        FaunaLod fauna;
        string wardedOffBy;
        string hurtCause;

        // found-before snapshot, for the report
        readonly Dictionary<string, int> foundBefore = new Dictionary<string, int>();
        readonly HashSet<Animal.Kind> herdsBefore = new HashSet<Animal.Kind>();
        int findsBefore;

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

        /// "Stone 7/20 · 3 ashore" (gather), "Exploring · 1:12 left · 2
        /// ashore", "Hunting goats · 0/1 · 1 ashore".
        public string StatusLine
        {
            get
            {
                string tail = $"  ·  {Ashore} ashore" + (Recalling ? "  ·  coming back" : "");
                switch (Mode)
                {
                    case Order.Explore:
                    {
                        int left = Mathf.CeilToInt(Mathf.Max(0f, ExploreSeconds - Elapsed));
                        return (Recalling ? "Exploring" : $"Exploring · {left / 60}:{left % 60:00} left") + tail;
                    }
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
        /// good with at least one reachable source on revealed ground, and
        /// how much.
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
            if (!HasSource(res)) { why = $"no {res.ToLowerInvariant()} within reach of the landing"; return false; }

            Resource = res;
            Target = amount <= 0 ? FillHold : amount;
            Begin(Order.Gather, who);
            return true;
        }

        /// Explore: `who` walk into the fog for `ExploreSeconds`.
        public bool SendExplore(List<CrewAgent> who, out string why)
        {
            if (!Ready(who, out why)) return false;
            Resource = null;
            Target = 0;
            Begin(Order.Explore, who);
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
            if (NearestBeast(kind, landing, null) == null) { why = $"no {HerdWord(kind, 2)} found yet"; return false; }
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
            wardedOffBy = null;
            Recalling = false;
            Out = true;
            startedAt = Time.time;
            nextReveal = 0f;
            nextDanger = Time.time + 1f;
            metBoars.Clear();
            FindFauna();
            Snapshot();

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
        /// come aboard; explorers walk their own trail home.
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
                if (Mode == Order.Explore && j != null && h.OnErrand && j.trail.Count > 0)
                {
                    j.phase = Phase.Home;
                    h.RetargetErrand(PopHome(j, h.transform.position));
                    continue;
                }
                h.ReturnAboard();
            }
        }

        // --- the crew's side (CrewAgent calls these) ----------------------

        /// A gather party works sources (`NextSource`); explore and hunt
        /// parties walk errands (`NextErrand`).
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

        /// **The next place an explorer or hunter walks to**, or false (go
        /// aboard by the plank).
        public bool NextErrand(CrewAgent who, out Vector3 at)
        {
            at = default;
            var j = JobOf(who);
            if (!Out || j == null) return false;
            if (Mode == Order.Explore)
            {
                if (Recalling || j.hurt || Elapsed >= ExploreSeconds) j.phase = Phase.Home;
                if (j.phase == Phase.Home)
                {
                    if (j.trail.Count == 0) return false;
                    at = PopHome(j, who.transform.position);
                    return true;
                }
                if (NextExploreStop(who, j, out at)) return true;
                // Walled in on every side: turn for home.
                j.phase = Phase.Home;
                if (j.trail.Count == 0) return false;
                at = PopHome(j, who.transform.position);
                return true;
            }
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
            if (Mode == Order.Explore)
            {
                if (j.phase == Phase.Out) j.trail.Add(who.transform.position);
                return 0f;
            }
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

            // Everybody ashore clears the fog round himself.
            if (Time.time >= nextReveal)
            {
                nextReveal = Time.time + 0.3f;
                var fog = IslandFog.For(island);
                if (fog != null)
                    foreach (var h in hands)
                        if (h != null && h.Party == this && h.IsAshore) fog.RevealAround(h.transform.position, RevealRadius);
            }

            if (Mode == Order.Explore && !Recalling && Time.time >= nextDanger)
            {
                nextDanger = Time.time + 0.5f;
                LookForTrouble();
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

        /// **Something could bite.** The first time an explorer comes within
        /// `BoarDangerMetres` of a live boar, one roll: `HurtChanceArmed`
        /// when any hand of the party carries a weapon (they ward it off),
        /// else `HurtChanceUnarmed`. At most one hand hurt a trip; the party
        /// then turns for home.
        void LookForTrouble()
        {
            if (fauna == null || HurtName != null) return;
            bool armed = false;
            foreach (var h in hands) if (JobOf(h)?.weapon != null) { armed = true; break; }
            float r2 = BoarDangerMetres * BoarDangerMetres;
            foreach (var a in fauna.Animals)
            {
                if (a == null || a.Dead || a.kind != Animal.Kind.Boar || metBoars.Contains(a)) continue;
                foreach (var h in hands)
                {
                    if (h == null || h.Party != this || !h.IsAshore) continue;
                    Vector3 d = a.transform.position - h.transform.position; d.y = 0f;
                    if (d.sqrMagnitude > r2) continue;
                    metBoars.Add(a);
                    if (Random.value < (armed ? HurtChanceArmed : HurtChanceUnarmed))
                    {
                        Hurt(h, "a boar");
                        return;
                    }
                    if (armed && wardedOffBy == null)
                    {
                        foreach (var g in hands)
                            if (JobOf(g)?.weapon != null) { wardedOffBy = g.DisplayName; break; }
                    }
                    break;
                }
            }
        }

        void Hurt(CrewAgent who, string cause)
        {
            var j = JobOf(who);
            if (j == null || HurtName != null) return;
            j.hurt = true;
            HurtName = who.DisplayName;
            hurtCause = cause;
            Recall($"{HurtName} was hurt by {cause}");
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

        void Snapshot()
        {
            foundBefore.Clear();
            herdsBefore.Clear();
            findsBefore = 0;
            var fog = IslandFog.For(island);
            if (fog == null) return;
            fog.FoundResources(foundBefore);
            var herds = new List<Animal>();
            fog.FoundHerds(herds);
            foreach (var a in herds) if (a != null) herdsBefore.Add(a.kind);
            var finds = new List<IslandFind>();
            fog.FoundIslandFinds(finds);
            findsBefore = finds.Count;
        }

        /// "Found ore and a cairn · 6 timber aboard" / "2 goats · 6 meat and
        /// 2 hide aboard" / "Bo was hurt by a boar -- resting aboard".
        void Report()
        {
            var parts = new List<string>();
            var fog = IslandFog.For(island);
            var news = new List<string>();
            if (fog != null)
            {
                var now = new Dictionary<string, int>();
                fog.FoundResources(now);
                foreach (var kv in now)
                    if (kv.Value > 0 && (!foundBefore.TryGetValue(kv.Key, out int was) || was <= 0))
                        news.Add(World.Economy.ResDefs.Label(kv.Key).ToLowerInvariant());
                var herds = new List<Animal>();
                fog.FoundHerds(herds);
                var kinds = new HashSet<Animal.Kind>();
                foreach (var a in herds) if (a != null) kinds.Add(a.kind);
                foreach (var k in kinds) if (!herdsBefore.Contains(k)) news.Add(HerdWord(k, 2));
                var finds = new List<IslandFind>();
                fog.FoundIslandFinds(finds);
                for (int i = findsBefore; i < finds.Count; i++)
                    if (finds[i] != null)
                        news.Add(finds[i].Kind == IslandFind.FindKind.Cairn ? "a cairn" : "a cache");
            }
            if (news.Count > 0) parts.Add("Found " + JoinAnd(news));
            else if (Mode == Order.Explore) parts.Add("Nothing new found");

            if (Mode == Order.Gather && DeliveredUnits > 0)
                parts.Add($"{DeliveredUnits} {World.Economy.ResDefs.Label(Resource).ToLowerInvariant()} aboard");
            if (Mode == Order.Hunt)
            {
                parts.Add(Kills > 0
                    ? $"{Kills} {HerdWord(HuntKind, Kills)} · {MeatAboard} meat" + (HideAboard > 0 ? $" and {HideAboard} hide" : "") + " aboard"
                    : "No kill");
            }
            if (Mode == Order.Explore && fog != null)
                parts.Add($"{Mathf.RoundToInt(fog.Revealed01 * 100f)}% explored");
            if (HurtName != null) parts.Add($"{HurtName} was hurt by {hurtCause}, resting aboard");
            else if (wardedOffBy != null) parts.Add($"{wardedOffBy} drove off a boar");
            else if (StopReason.StartsWith("raiders")) parts.Add("ran from raiders");

            LastReport = string.Join(" · ", parts);
            LastReportAt = Time.unscaledTime;
        }

        static string JoinAnd(List<string> items)
        {
            if (items.Count == 1) return items[0];
            return string.Join(", ", items.GetRange(0, items.Count - 1)) + " and " + items[items.Count - 1];
        }

        /// True while the last report is fresh enough to toast.
        public static bool ReportFresh => !string.IsNullOrEmpty(LastReport)
                                          && Time.unscaledTime - LastReportAt < ReportSeconds;

        public static void DismissReport() => LastReportAt = -999f;

        readonly SeaSick.UI.HudLabel reportLabel = new SeaSick.UI.HudLabel();
        static GUIStyle reportStyle;

        /// The result toast: the game's ice "news" card (`ReturnSummary`'s
        /// look), top of the screen, tap to dismiss. The sheet shows the
        /// same line in its own card while it is open.
        void OnGUI()
        {
            if (!ReportFresh) return;
            if (SeaSick.UI.ModularYard.ShipyardModal.IsOpen
                || SeaSick.UI.Menus.GameMenus.Current != SeaSick.UI.Menus.GameMenus.Mode.None) return;
            if (SeaSick.UI.Sheets.LandingPartySheet.IsOpen) return;
            if (reportLabel.Changed(LastReport.GetHashCode())) reportLabel.Set(LastReport);
            if (reportStyle == null)
                reportStyle = new GUIStyle(SeaSick.UI.UITheme.Toast) { wordWrap = true, alignment = TextAnchor.MiddleLeft };
            float width = Mathf.Min(SeaSick.UI.HudLayout.Safe.width - SeaSick.UI.HudLayout.Unit * 2f,
                                    SeaSick.UI.HudLayout.Unit * 30f);
            float pad = SeaSick.UI.HudLayout.Pad;
            float height = reportStyle.CalcHeight(reportLabel.Content, width - pad * 2f) + SeaSick.UI.HudLayout.Unit * 1.2f;
            var rect = SeaSick.UI.HudLayout.ToastRow(height, width);
            SeaSick.UI.UITheme.ToastCard(rect, SeaSick.UI.UITheme.LedgerIce);
            GUI.Label(new Rect(rect.x + pad, rect.y, rect.width - pad * 2f, rect.height), reportLabel.Content, reportStyle);
            SeaSick.UI.UIBlocker.Block(rect);
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                DismissReport();
                Event.current.Use();
            }
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

        // --- explore --------------------------------------------------------

        /// **The next stop into the fog.** Sixteen bearings round his last
        /// heading at a full and a short step; a stop must be dry land and a
        /// walkable straight line from where he stands. Scored by the fog it
        /// looks into (five samples, 10 m apart), then by keeping on the way
        /// he was going, less crowding the other hands' stops.
        bool NextExploreStop(CrewAgent who, Job j, out Vector3 at)
        {
            at = default;
            var h = Island.TerrainHeight;
            var fog = IslandFog.For(island);
            Vector3 from = who.transform.position;
            if (j.heading.sqrMagnitude < 0.01f)
            {
                Vector3 inland = (island != null ? island.transform.position : from) - landing;
                inland.y = 0f;
                if (inland.sqrMagnitude < 1f) inland = anchor != null ? anchor.transform.forward : Vector3.forward;
                int i = hands.IndexOf(who), n = hands.Count;
                float fan = n > 1 ? Mathf.Lerp(-55f, 55f, i / (float)(n - 1)) : 0f;
                j.heading = Quaternion.Euler(0f, fan, 0f) * inland.normalized;
            }

            float bestScore = float.MinValue;
            bool found = false;
            Vector3 best = default;
            for (int k = 0; k < 16; k++)
            {
                float ang = k * 22.5f;
                Vector3 dir = Quaternion.Euler(0f, ang, 0f) * j.heading;
                for (int s = 0; s < 2; s++)
                {
                    Vector3 p = from + dir * (s == 0 ? ExploreStep : ExploreStep * 0.55f);
                    if (h != null)
                    {
                        p.y = h(p.x, p.z);
                        if (p.y < 0.6f) continue;   // the sea, the surf, wet sand
                    }
                    if (!LineWalkable(from, p)) continue;
                    float score = Mathf.Cos(ang * Mathf.Deg2Rad) * 2f;
                    if (fog != null) score += Unrevealed(fog, p) * 3f;
                    foreach (var kv in jobs)
                    {
                        if (kv.Key == who || kv.Value.trail.Count == 0) continue;
                        Vector3 o = kv.Value.trail[kv.Value.trail.Count - 1] - p; o.y = 0f;
                        float d = o.magnitude;
                        if (d < 15f) score -= (15f - d) / 5f;
                    }
                    score += Random.value * 0.5f;
                    if (score > bestScore) { bestScore = score; best = p; found = true; }
                }
            }
            if (!found) return false;
            Vector3 head = best - from; head.y = 0f;
            if (head.sqrMagnitude > 0.01f) j.heading = head.normalized;
            at = best;
            return true;
        }

        static int Unrevealed(IslandFog fog, Vector3 p)
        {
            int n = 0;
            if (!fog.IsRevealed(p)) n++;
            if (!fog.IsRevealed(p + new Vector3(10f, 0f, 0f))) n++;
            if (!fog.IsRevealed(p + new Vector3(-10f, 0f, 0f))) n++;
            if (!fog.IsRevealed(p + new Vector3(0f, 0f, 10f))) n++;
            if (!fog.IsRevealed(p + new Vector3(0f, 0f, -10f))) n++;
            return n;
        }

        /// The next stop home: the newest trail point more than 3 m off.
        static Vector3 PopHome(Job j, Vector3 from)
        {
            while (j.trail.Count > 0)
            {
                Vector3 p = j.trail[j.trail.Count - 1];
                j.trail.RemoveAt(j.trail.Count - 1);
                Vector3 d = p - from; d.y = 0f;
                if (d.sqrMagnitude > 9f || j.trail.Count == 0) return p;
            }
            return from;
        }

        // --- hunt -----------------------------------------------------------

        void FindFauna()
        {
            if (fauna != null && fauna.Island == island) return;
            fauna = null;
            foreach (var f in FindObjectsByType<FaunaLod>(FindObjectsSortMode.None))
                if (f != null && f.Island == island) { fauna = f; break; }
        }

        /// The nearest live, unclaimed beast of `kind` on revealed ground.
        Animal NearestBeast(Animal.Kind kind, Vector3 from, CrewAgent who)
        {
            if (fauna == null) return null;
            var fog = IslandFog.For(island);
            Animal best = null;
            float bestSq = float.MaxValue;
            foreach (var a in fauna.Animals)
            {
                if (a == null || a.Dead || a.Hunted || a.kind != kind) continue;
                if (fog != null && !fog.IsRevealed(a.transform.position)) continue;
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
                if (Usable(n, island, this) && n.Resource == res && InReach(n, landing)) return true;
            return false;
        }

        /// Free to work: on this island (or one of the party's own rock
        /// nodes), standing, a raw good, and on REVEALED ground (2026-09-30:
        /// a party works only what has been seen).
        static bool Usable(ResourceNode n, Island isle, GatherParty party)
        {
            if (n == null || n.Harvested || !n.isActiveAndEnabled) return false;
            if (!Res.IsGatherable(n.Resource)) return false;
            if (TakenByName(n, isle)) return false;
            bool ours = n.Home == isle || (party != null && party.partyRocks.Contains(n));
            if (!ours) return false;
            var fog = IslandFog.For(isle);
            return fog == null || fog.IsRevealed(n.transform.position);
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
            var books = Outpost.Of(isle) != null ? Outpost.Of(isle).Ledger : null;
            for (int i = 0; i < rocks.Count; i++)
            {
                if (rocks.IsHidden(i)) continue;
                if (books != null && books.RockTaken(i)) continue;
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
