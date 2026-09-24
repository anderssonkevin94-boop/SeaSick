using System.Collections.Generic;
using System.Text;
using SeaSick.Crew;
using SeaSick.Ship;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.Voyage
{
    /// The voyage: cast off from the pier, work the archipelago island by
    /// island, come back alongside before the crew break — and then spend
    /// what you landed on the village. Gathering happens through the anchor
    /// and shore-party system; this class owns the hold, the stores, the
    /// tally and the one moment the player is not steering. UI is dev-grade
    /// IMGUI until the loop is feel-approved.
    public class VoyageManager : MonoBehaviour
    {
        [SerializeField] ShipMotor ship;
        [SerializeField] Transform homePoint;
        [Tooltip("Fallback arrival radius, used only when there is no dock to come alongside.")]
        [SerializeField] float homeRadius = 60f;
        [Tooltip("How far off her berth she has to get before she counts as having sailed. She starts tied up, so leaving has to be an act, not a distance from an island centre.")]
        [SerializeField] float departureRange = 150f;
        [Tooltip("What home keeps with nothing built. Only used if the island never got an Outpost.")]
        [SerializeField] int fallbackStoreCapacity = 30;
        [Tooltip("The marked line — a full hold. NOT a hard cap: you may load past it.")]
        [SerializeField] int holdCapacity = 40;
        [Tooltip("How far past the line she'll physically take, as a multiple. The rest rides on deck.")]
        [SerializeField] float overloadLimit = 1.6f;

        enum Phase { AtSea, Home }
        Phase phase = Phase.AtSea;

        /// True while she is lying at the pier with the panel up. The anchor
        /// controller stands its own buttons and its spacebar down on this.
        public bool AtHome => phase == Phase.Home;

        public Transform HomePoint => homePoint;
        /// **The player's decision to be greedy.** Off, the shore party fills
        /// the hold to the marked line and stops. On, they keep loading and the
        /// surplus rides on deck, where it costs freeboard, handling, and
        /// eventually the guns. Nobody overloads by accident.
        public bool TakeDeckCargo { get; set; }

        /// Where the shore party stops — the line, or the physical limit.
        public bool HoldFull => TotalHeld >= (TakeDeckCargo ? MaxHold : holdCapacity);
        /// Genuinely nothing more will fit, on deck or below.
        public bool HoldStuffed => TotalHeld >= MaxHold;
        public int TotalHeld { get; private set; }
        public int HoldCapacity => holdCapacity;

        /// Set by the ship, not by the scene, once she has bays.
        ///
        /// `holdCapacity` was a hand-tuned 40 against a home that keeps 30 --
        /// a full voyage landing 8 short, which is the gap the first storehouse
        /// exists to close. A hull whose hold is DECIDED, bay by bay, has to
        /// drive this number instead, or the panel says one thing and the
        /// shore party does another. The floor keeps a ship with no hold bays
        /// able to carry something home rather than nothing at all.
        public void SetHoldCapacity(int cells, int minimum = 4)
        {
            int want = Mathf.Max(minimum, cells);
            if (want == holdCapacity) return;
            holdCapacity = want;
        }
        public int MaxHold => Mathf.RoundToInt(holdCapacity * overloadLimit);
        /// 1.0 is the marked line. Above that she's carrying deck cargo.
        public float HoldFill => holdCapacity > 0 ? (float)TotalHeld / holdCapacity : 0f;
        public bool Overloaded => TotalHeld > holdCapacity;

        readonly Dictionary<string, int> held = new Dictionary<string, int>();
        readonly Dictionary<string, int> banked = new Dictionary<string, int>();

        /// The hold and the stores, read-only, for the save. Every other
        /// reader asks by name (`AmountOf`, `Banked`); a save has to walk them.
        public IReadOnlyDictionary<string, int> HeldStores => held;
        public IReadOnlyDictionary<string, int> BankedStores => banked;

        /// **Put a saved hold and saved stores back**, and make the pictures
        /// agree: the stack at the stern and the piles on the beach are both
        /// rebuilt from the numbers, because neither re-syncs by itself.
        ///
        /// Runs AFTER `Start` and after the yard has applied the saved rung,
        /// so `BeginVoyage` has already had its one chance to wipe the hold
        /// and `SetHoldCapacity` has already been told the saved size.
        public void RestoreStores(IEnumerable<KeyValuePair<string, int>> hold,
                                  IEnumerable<KeyValuePair<string, int>> stores)
        {
            held.Clear();
            TotalHeld = 0;
            if (hold != null)
                foreach (var kv in hold)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0) continue;
                    held[kv.Key] = kv.Value;
                    TotalHeld += kv.Value;
                }
            if (ship != null)
            {
                ship.CargoLoad = HoldFill;
                var shipHold = ship.GetComponent<Ship.ShipHold>();
                if (shipHold != null)
                {
                    shipHold.ClearVisuals();
                    foreach (var kv in held)
                        for (int i = 0; i < kv.Value; i++) shipHold.AddVisual(kv.Key);
                }
            }

            banked.Clear();
            if (stores != null)
                foreach (var kv in stores)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0) continue;
                    banked[kv.Key] = kv.Value;
                }
            var pile = World.Stockpile.Instance;
            if (pile != null)
            {
                pile.Clear();
                foreach (var kv in banked)
                    for (int i = 0; i < kv.Value; i++) pile.Deposit(kv.Key);
            }
            panelVersion++;
        }

        CrewAgent[] crew;
        AnchorController anchor;
        bool hasLeftHome;
        /// She has been at her berth at least once. Guards the startup race
        /// below — see the note in Update.
        bool seenBerth;
        float voyageStartTime;
        int pukesAtStart;
        float completedTime;
        int completedPukes;
        float completedWorst;
        string completedHaul = "";
        int completedSpoiled;
        /// Per resource, in `ResOrder`: "4 timber · 2 boards". Fixed the
        /// moment she ties up, because the room that was short is the room
        /// there WAS, and building a storehouse afterwards must not rewrite
        /// the tally of the voyage that paid for it.
        string completedSpoiledDetail = "";
        string buildNote = "";

        // --- The home panel's strings ---------------------------------------
        //
        // IMGUI runs OnGUI once per EVENT — Layout, Repaint, and one more for
        // every mouse move — so the dozen interpolations this panel used to
        // make were made several times a frame, `BankSummary`'s StringBuilder
        // with them. See StatusHUD for the measurement; this is the same fix.
        //
        // None of it moves while the panel is up: the tally is fixed the
        // moment she ties up, and the stores only change when something is
        // built. So it is built when the CONTENT changes and not otherwise —
        // a version the two places that change it bump, plus the two numbers
        // themselves as a backstop against a path that forgets to.
        int panelVersion;
        long panelBuiltFor = long.MinValue;
        string tallyLine = "";
        string spoiledLine = "";
        string storesLine = "";
        /// True when ANY one pile is at its ceiling, which is what the warning
        /// colour means now that the ceiling is per resource. A total against a
        /// total could not say "the timber is full and the boards are not".
        bool storesFull;
        string[] planLabels = new string[0];
        string[] planBlurbs = new string[0];

        // --- one fixed order for every list of resources ---------------------
        //
        // **The order used to be the dictionary's**, which is to say the order
        // things happened to be inserted in, which is to say none at all. That
        // was invisible while timber was the only thing a hold ever carried
        // and became a silent bug the moment a camp could make boards: the old
        // landing shared ONE pool of room across every kind, so whichever
        // resource enumerated first ate the space and the rest spoiled on the
        // sand with no reason the player could see. Room is per resource now
        // (below), but the ORDER still has to be fixed, or the tally and the
        // stores line reshuffle themselves between voyages.
        //
        // Raw materials in the order the world unlocks them by ring, then the
        // made goods, then food. Anything a later pass invents lands after
        // these, sorted by name, so it is stable without being listed here.
        static readonly string[] ResOrder =
        {
            World.Res.Timber, World.Res.Stone, World.Res.Ore, World.Res.Spice,
            World.Res.Boards, World.Res.Tools, World.Res.Food, World.Res.Meals,
        };

        /// **Cheapest over the side first.** Jettisoning is a decision taken
        /// at the helm in seconds with the deck awash, so the game picks what
        /// goes: raw bulk before anything a camp spent a day making, and a
        /// crate of tools or a sack of spice is the last thing to swim.
        static readonly string[] DumpOrder =
        {
            World.Res.Timber, World.Res.Stone, World.Res.Food, World.Res.Meals,
            World.Res.Ore, World.Res.Boards, World.Res.Spice, World.Res.Tools,
        };

        /// Scratch for the two orderings below — reused, because both of them
        /// run while the home panel is up and IMGUI is drawing.
        readonly List<string> ordered = new List<string>();

        /// The keys of `d`, in `ResOrder`, with anything unlisted after them
        /// in name order. The returned list is the shared scratch: read it
        /// out before calling this again.
        List<string> InOrder(Dictionary<string, int> d)
        {
            ordered.Clear();
            foreach (var r in ResOrder) if (d.ContainsKey(r)) ordered.Add(r);
            int known = ordered.Count;
            foreach (var kv in d) if (!Listed(kv.Key)) ordered.Add(kv.Key);
            if (ordered.Count > known)
                ordered.Sort(known, ordered.Count - known, System.StringComparer.Ordinal);
            return ordered;
        }

        static bool Listed(string r)
        {
            foreach (var k in ResOrder) if (k == r) return true;
            return false;
        }

        GUIStyle centerLabel, cargoLabel;

        void Start()
        {
            if (ship == null) ship = FindFirstObjectByType<ShipMotor>();
            crew = ship != null ? ship.GetComponentsInChildren<CrewAgent>(true) : new CrewAgent[0];
            anchor = ship != null ? ship.GetComponent<AnchorController>() : null;
            BeginVoyage();
        }

        /// **Cast off.** The "set sail" button on the home panel and the
        /// spacebar both land here, and so does `Start`. Public so a probe can
        /// press the button the player presses rather than setting the phase
        /// behind the game's back — see `SinkProbe`.
        public void BeginVoyage()
        {
            // One button, one intention: ending the tally IS casting off.
            if (phase == Phase.Home && anchor != null) anchor.CastOff();
            phase = Phase.AtSea;
            buildNote = "";
            TakeDeckCargo = false;
            hasLeftHome = false;
            voyageStartTime = Time.time;
            pukesAtStart = TotalPukes();
            held.Clear();
            TotalHeld = 0;
            if (ship != null) ship.CargoLoad = 0f;
            // Casting off is a moment worth keeping. A no-op until the player
            // has chosen New or Continue, so the call from `Start` cannot
            // overwrite a save with a fresh world.
            Save.SaveGame.Autosave("cast off");
        }

        int TotalPukes()
        {
            int n = 0;
            foreach (var c in crew) if (c != null) n += c.PukeCount;
            return n;
        }

        /// Loot into the hold, from a shore party or salvaged from the sea.
        public void AddLoot(int amount, string resource)
        {
            if (phase == Phase.Home || amount <= 0) return;
            int room = MaxHold - TotalHeld;
            if (room <= 0) return;
            amount = Mathf.Min(amount, room);

            held.TryGetValue(resource, out int cur);
            held[resource] = cur + amount;
            TotalHeld += amount;
            ship.CargoLoad = HoldFill;
        }

        /// Barrels and spars out of the water. Timber, because that is what a
        /// wreck is made of — through `World.Res` rather than a literal, so a
        /// rename cannot leave salvage banking into a resource nothing else
        /// has heard of.
        public void AddSalvage(int amount) => AddLoot(amount, World.Res.Timber);

        public int AmountOf(string resource) => held.TryGetValue(resource, out int n) ? n : 0;

        /// Units of `resource` in the hold. The name the camp sheets use
        /// (`OutpostLedger.StoreCountOf` is its shore twin); same as `AmountOf`.
        public int HeldOf(string resource) =>
            !string.IsNullOrEmpty(resource) && held.TryGetValue(resource, out int n) ? n : 0;

        /// **Out of the hold, onto a villager's shoulder** (2026-09-24, camp
        /// transfer orders: `OutpostLedger.OrderTransfer`, bound through
        /// `World.ShipCargoSide`). Takes up to `amount` of `resource` and
        /// returns what it ACTUALLY removed -- the caller books exactly that,
        /// so a request for more than is aboard can neither create nor lose
        /// a unit. Unlike `TryConsume` it never refuses a partial take.
        /// The visible stack is the caller's (`ShipHold.RemoveVisual`).
        public int RemoveLoot(int amount, string resource)
        {
            if (amount <= 0 || string.IsNullOrEmpty(resource)) return 0;
            if (!held.TryGetValue(resource, out int have) || have <= 0) return 0;
            int take = Mathf.Min(have, amount);
            held[resource] = have - take;
            if (held[resource] <= 0) held.Remove(resource);
            TotalHeld -= take;
            if (ship != null) ship.CargoLoad = HoldFill;
            return take;
        }

        /// Over the side. The escape valve for a ship that is going under —
        /// costs you the payoff, buys back freeboard immediately. The player's
        /// decision, at the helm, in seconds.
        ///
        /// **What goes first is not "whatever the dictionary says".** It used
        /// to be, and with one cargo kind nobody could tell; with a hold
        /// carrying timber, boards and tools it meant a panicking captain
        /// might throw the forge's whole week over the side and keep the logs.
        /// `DumpOrder` is cheapest first, so the thing you lose is the thing
        /// you can cut more of.
        public int Jettison(int amount)
        {
            if (TotalHeld <= 0 || amount <= 0) return 0;
            int dumped = 0;
            foreach (var k in DumpOrder)
            {
                if (amount <= 0) break;
                if (!held.TryGetValue(k, out int have)) continue;
                int take = Mathf.Min(amount, have);
                held[k] = have - take;
                TotalHeld -= take;
                amount -= take;
                dumped += take;
                if (held[k] <= 0) held.Remove(k);
            }
            // Anything a later pass invented and did not list. Allocates, and
            // only on the path where the listed eight did not cover the load.
            if (amount > 0 && held.Count > 0)
            {
                var rest = new List<string>(held.Keys);
                rest.Sort(System.StringComparer.Ordinal);
                foreach (var k in rest)
                {
                    if (amount <= 0) break;
                    int take = Mathf.Min(amount, held[k]);
                    held[k] -= take;
                    TotalHeld -= take;
                    amount -= take;
                    dumped += take;
                    if (held[k] <= 0) held.Remove(k);
                }
            }
            ship.CargoLoad = HoldFill;

            // Take the visible stacks down with it, or the deck keeps looking
            // loaded after the weight has gone.
            var shipHold = ship != null ? ship.GetComponent<Ship.ShipHold>() : null;
            if (shipHold != null)
                for (int i = 0; i < dumped; i++) shipHold.RemoveVisual();

            return dumped;
        }

        /// Spend cargo (repairs burn timber). False if the hold can't cover it.
        public bool TryConsume(string resource, int amount)
        {
            if (!held.TryGetValue(resource, out int have) || have < amount) return false;
            held[resource] = have - amount;
            if (held[resource] <= 0) held.Remove(resource);
            TotalHeld -= amount;
            ship.CargoLoad = HoldFill;
            return true;
        }

        void Update()
        {
            if (ship == null || (homePoint == null && World.Dock.Home == null)) return;

            if (phase == Phase.AtSea)
            {
                // You have to actually leave before you can arrive — the ship
                // starts tied up, which otherwise completed a 0-second voyage
                // the instant the game began.
                //
                // Both halves are measured against the BERTH now, not against
                // the home island's centre. She starts at the end of a 46 m
                // pier on an island whose centre is a couple of hundred
                // metres inland, so a radius round that centre had her
                // "departed" before she had let go a line.
                var dock = World.Dock.Home;
                if (dock != null)
                {
                    bool berthed = AtBerth();
                    if (berthed) seenBerth = true;

                    // **She has to be AT her berth before she can leave it.**
                    // Without this the loop completed a voyage on the first
                    // frame of the game: the dock does not exist until the
                    // populator has found the island, and the ship is not
                    // moved onto her berth until the frame after that, so for
                    // the first frames she sits at her scene position — 483 m
                    // from a berth that does not exist yet — which reads
                    // exactly like a ship that has sailed. The three-second
                    // grace is for a dev spawn that never touches the pier at
                    // all (PlaytestStart puts her in the western storm).
                    bool settled = seenBerth || Time.time - voyageStartTime > 3f;
                    float off = dock.DistanceFrom(ship.transform.position);
                    if (!hasLeftHome && settled && !berthed && off > departureRange)
                        hasLeftHome = true;
                    if (hasLeftHome && berthed) CompleteVoyage();
                }
                else
                {
                    float home = Flat(ship.transform.position, homePoint.position);
                    if (!hasLeftHome && home > homeRadius * 1.25f) hasLeftHome = true;
                    if (hasLeftHome && home < homeRadius) CompleteVoyage();
                }
            }
            else
            {
                foreach (var c in crew) if (c != null) c.Rest();
                // Tap-anywhere had to go: the panel has buttons on it now,
                // and every one of them would also have set sail.
                if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                    BeginVoyage();
            }
        }

        void CompleteVoyage()
        {
            completedTime = Time.time - voyageStartTime;
            completedPukes = TotalPukes() - pukesAtStart;
            // Read the crew BEFORE Rest() wipes them, or the tally always
            // reports a healthy ship coming home.
            completedWorst = 0f;
            foreach (var c in crew)
                if (c != null && c.Sickness01 > completedWorst) completedWorst = c.Sickness01;

            // What home can KEEP is not what she can carry. The hold takes
            // 40 to the marked line and 64 with deck cargo; the open beach
            // keeps 30 until somebody builds somewhere to put it. The
            // surplus is not a penalty message, it is the reason the first
            // building exists — and you have to see it land short once
            // before the storehouse means anything.
            //
            // **N OF EACH, not N in total.** This used to pool one
            // `StoreCapacity - BankedTotal` across the whole hold and walk the
            // `held` dictionary in whatever order it felt like, so a hold with
            // thirty logs and twelve boards in it banked the logs, filled the
            // beach with them and left the boards — a week of a sawyer's work
            // — on the sand, with the panel saying only "12 left on the sand"
            // and no hint that the LOGS were what ate the room. Camps have
            // kept N of each since 2026-09-19 (`Outpost.KeepsOfEach`, Kevin:
            // *"crew on the island can gather resources up to 10 of each"*);
            // home is the same place under a different name and now keeps the
            // same way. It is also what makes carrying a second thing home
            // worth the passage instead of a competitor for the first's slots.
            int cap = StoreCapacity;
            var sb = new StringBuilder();
            var spoil = new StringBuilder();
            var landed = new List<string>();
            completedSpoiled = 0;
            foreach (var res in InOrder(held))
            {
                int got = held[res];
                // Room is asked PER RESOURCE, against what is already in that
                // pile — so a beach full of timber costs the timber nothing
                // it was not already going to lose, and costs the boards
                // nothing at all.
                int take = Mathf.Min(got, Mathf.Max(0, cap - Banked(res)));
                int lost = got - take;

                if (sb.Length > 0) sb.Append("   ");
                sb.Append($"+{got} {res.ToLowerInvariant()}");
                if (lost > 0) sb.Append($" ({take} kept)");

                if (lost > 0)
                {
                    completedSpoiled += lost;
                    if (spoil.Length > 0) spoil.Append(" · ");
                    spoil.Append($"{lost} {res.ToLowerInvariant()}");
                }
                if (take <= 0) continue;
                banked.TryGetValue(res, out int cur);
                banked[res] = cur + take;
                for (int i = 0; i < take; i++) landed.Add(res);
            }
            completedHaul = sb.Length > 0 ? sb.ToString() : "empty hold";
            completedSpoiledDetail = spoil.ToString();

            // Carry it ashore piece by piece so the pile visibly grows rather
            // than the haul evaporating into a number.
            if (landed.Count > 0) StartCoroutine(UnloadAshore(landed));

            held.Clear();
            TotalHeld = 0;
            ship.CargoLoad = 0f;
            foreach (var c in crew) if (c != null) c.Rest();
            buildNote = "";
            phase = Phase.Home;
            panelVersion++;   // the whole tally just changed; see RefreshPanelText
        }

        // --- The stores, and what they buy ----------------------------------

        /// **What home keeps OF EACH THING**, which grows as the village is
        /// built. Thirty of timber AND thirty of boards, not thirty between
        /// them — see the note in `CompleteVoyage`.
        ///
        /// The name is the old one and the number is unchanged: `Outpost`
        /// renamed the same quantity `KeepsOfEach` in 2026-09-19 and kept
        /// `StoreCapacity` as an alias. This reads the explicit name so that
        /// what it means is written down at the only place that uses it, and
        /// so nothing here has to be revisited if the alias ever goes.
        ///
        /// Every reader of this is asking a per-resource question — `LoopProbe`
        /// lands one kind and compares the spoilage against it; the panel
        /// quotes it beside each pile.
        public int StoreCapacity => World.Outpost.Home != null
            ? World.Outpost.Home.KeepsOfEach : fallbackStoreCapacity;

        /// Everything in every pile added up. **Not a capacity question** —
        /// there is no total ceiling any more — so this is a readout and a
        /// convenience for probes, never the thing room is measured against.
        public int BankedTotal
        {
            get { int n = 0; foreach (var kv in banked) n += kv.Value; return n; }
        }

        public int Banked(string resource) =>
            banked.TryGetValue(resource, out int n) ? n : 0;

        /// What the last homecoming could not keep, and which piles it was.
        /// Read-only, and read by `SinkProbe`: a gate that re-derived the
        /// spoilage from the loot it put in the hold would agree with itself
        /// whatever the panel said.
        public int Spoiled => completedSpoiled;
        public string SpoiledDetail => completedSpoiledDetail;

        /// Raise a building and pay for it out of the stores.
        ///
        /// Sited FIRST, paid for second: `Outpost.Raise` returns null when
        /// there is nowhere in the clearing left to stand it, and charging
        /// for a building that was never built is the one outcome the player
        /// can neither see nor undo.
        public bool TryBuild(World.BuildPlan plan)
        {
            var village = World.Outpost.Home;
            if (village == null) { buildNote = "nowhere to build"; return false; }
            if (Banked(plan.resource) < plan.cost)
            {
                buildNote = $"need {plan.cost} {plan.resource.ToLower()}";
                return false;
            }

            var raised = village.Raise(plan);
            if (raised == null) { buildNote = "no room left in the clearing"; return false; }

            SpendBanked(plan.resource, plan.cost);
            buildNote = $"{plan.label} raised — home keeps {StoreCapacity} of each";
            panelVersion++;   // "build" becomes "build another"; see RefreshPanelText
            return true;
        }

        /// Off the beach and into the building. The visible pile has to come
        /// down with the number, or the stores read as spent in the panel and
        /// untouched on the ground two metres away.
        ///
        /// Public since the yard began charging for rungs (`ShipPrices`): a
        /// hull is the second thing the stores buy, and it has to be paid for
        /// through the same call the buildings use or the pile and the number
        /// part company again. It does not check — `ShipPrices.TrySpend` and
        /// `TryBuild` check, and both clamp here anyway.
        public void SpendBanked(string resource, int amount)
        {
            banked.TryGetValue(resource, out int have);
            int take = Mathf.Min(have, amount);
            banked[resource] = have - take;
            if (banked[resource] <= 0) banked.Remove(resource);
            var pile = World.Stockpile.Instance;
            if (pile != null) pile.Withdraw(resource, take);
            panelVersion++;   // the stores moved; see RefreshPanelText
        }

        /// **The HOMECOMING's unload only** (2026-09-24). Home is not a camp
        /// ledger: the voyage banks the hold into `banked` + `Stockpile` in
        /// `CompleteVoyage`, and this only paces the visible pile. A hold
        /// emptied at a CAMP never comes here -- that is carried, armful by
        /// armful, by the camp's hands through transfer orders
        /// (`OutpostLedger.OrderTransfer(res, n, toShip: false)`, the ship end
        /// bound by `World.ShipCargoSide`). This instant version is kept for
        /// the one place with no camp and no hands: the home dock.
        System.Collections.IEnumerator UnloadAshore(List<string> units)
        {
            var pile = World.Stockpile.Instance;
            var shipHold = ship != null ? ship.GetComponent<Ship.ShipHold>() : null;
            foreach (var resource in units)
            {
                if (shipHold != null) shipHold.RemoveVisual();
                if (pile != null) pile.Deposit(resource);
                yield return new WaitForSeconds(0.18f);
            }
        }

        /// Alongside her own pier, which is the arrival. Falls back to a
        /// plain distance if the anchor controller is missing.
        bool AtBerth()
        {
            if (anchor != null) return anchor.AtHomeDock;
            var dock = World.Dock.Home;
            return dock != null && dock.DistanceFrom(ship.transform.position) < 25f;
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// Every line on the home panel, built at most once per thing that
        /// changes it — a voyage landing, or a building going up.
        ///
        /// It is checked here rather than pushed from `CompleteVoyage` so the
        /// panel cannot draw a stale line if a future path moves the stores
        /// without saying so: the version catches the changes we know about
        /// and the two totals catch the ones we do not.
        void RefreshPanelText()
        {
            int total = BankedTotal, cap = StoreCapacity;
            long key = SeaSick.UI.HudLabel.Key(panelVersion, total, cap);
            if (key == panelBuiltFor) return;
            panelBuiltFor = key;

            int m = Mathf.FloorToInt(completedTime / 60f);
            int sec = Mathf.FloorToInt(completedTime % 60f);
            // Trips to the rail alone is a misleading number now that the
            // meter never falls — a crew can be finished having puked twice,
            // or fine having puked once. Lead with how bad it got.
            string crewLine = completedPukes == 0
                ? "the crew kept it together"
                : $"worst {completedWorst:P0} sick   ·   {completedPukes}× to the rail";
            tallyLine = $"{m}:{sec:00}   ·   {crewLine}";

            // Named, because which pile overflowed is the whole information.
            // "12 left on the sand" told the player a number; "12 boards left
            // on the sand" tells them to build a store hut before the next
            // time the sawmill has been running.
            spoiledLine = completedSpoiled > 0
                ? $"{completedSpoiledDetail} left on the sand — nowhere to keep it"
                : "";

            // Stores against what they can BE, PER PILE — a bare total cannot
            // tell you the timber is full while the boards have room, and
            // which of them is full is the whole reason the buttons below it
            // exist.
            storesLine = $"stores — {BankSummary()}";
            storesFull = false;
            foreach (var kv in banked) if (kv.Value >= cap) { storesFull = true; break; }

            var plans = World.BuildPlans.All;
            if (planLabels.Length != plans.Length)
            {
                planLabels = new string[plans.Length];
                planBlurbs = new string[plans.Length];
            }
            var village = World.Outpost.Home;
            for (int i = 0; i < plans.Length; i++)
            {
                var plan = plans[i];
                int have = Banked(plan.resource);
                bool afford = have >= plan.cost;
                int already = village != null ? village.CountOf(plan.id) : 0;
                string res = plan.resource.ToLower();
                // "×2" read as "build two of them". The second one is
                // another one.
                planLabels[i] = already > 0
                    ? $"build another {plan.label} — {plan.cost} {res}"
                    : $"build {plan.label} — {plan.cost} {res}";
                planBlurbs[i] = afford
                    ? plan.blurb
                    : $"{plan.blurb}   ·   {have}/{plan.cost} {res}";
            }
        }

        void OnGUI()
        {
            // At sea the permanent HUD (StatusHUD) covers cargo and distance —
            // this class only draws the one moment she is tied up and the
            // player is not steering.
            if (phase != Phase.Home) return;
            RefreshPanelText();

            int u = SeaSick.UI.HudLayout.Unit;
            var plans = World.BuildPlans.All;
            float ph = u * (19f + (completedSpoiled > 0 ? 1.9f : 0f) + plans.Length * 4.4f);

            // Clear of the left tab rail, not centred over it. At 0.08 to 0.92
            // of screen width this panel covered the Yard tab and the open
            // Yard panel underneath it -- and the yard is the other half of
            // what you came home to do.
            var safe = SeaSick.UI.HudLayout.Safe;
            float pad = SeaSick.UI.HudLayout.Pad;
            float left = safe.x + pad + SeaSick.UI.HudLayout.RailWidth + SeaSick.UI.HudLayout.Gap;
            float room = safe.xMax - pad - left;
            float pw = Mathf.Min(room, u * 30f);
            var panel = new Rect(left + (room - pw) * 0.5f,
                                 Mathf.Max(safe.y + pad, safe.y + (safe.height - ph) * 0.42f),
                                 pw, ph);
            SeaSick.UI.UITheme.Rect(panel, SeaSick.UI.UITheme.PanelSolid);
            SeaSick.UI.UITheme.Rect(new Rect(panel.x, panel.y, panel.width, 2f), SeaSick.UI.UITheme.Sea);

            float y = panel.y + u * 1.4f;
            GUI.Label(new Rect(panel.x, y, panel.width, u * 2f), "VOYAGE COMPLETE", SeaSick.UI.UITheme.Title);
            y += u * 3.2f;
            GUI.Label(new Rect(panel.x, y, panel.width, u * 1.8f), completedHaul, SeaSick.UI.UITheme.Strong);
            y += u * 2.4f;

            if (completedSpoiled > 0)
            {
                var was = GUI.color;
                GUI.color = SeaSick.UI.UITheme.Warn;
                GUI.Label(new Rect(panel.x, y, panel.width, u * 1.6f),
                    spoiledLine, SeaSick.UI.UITheme.Small2Centered);
                GUI.color = was;
                y += u * 1.9f;
            }

            GUI.Label(new Rect(panel.x, y, panel.width, u * 1.6f),
                tallyLine, SeaSick.UI.UITheme.Small2Centered);
            y += u * 2.2f;

            // Stores against what they can BE — a bare number cannot tell you
            // the beach is full, and being full is the whole reason the
            // buttons below it exist. `storesFull` is "any one pile is at its
            // ceiling", worked out in RefreshPanelText; comparing two totals
            // here would never go amber once the piles were separate.
            var fullWas = GUI.color;
            if (storesFull) GUI.color = SeaSick.UI.UITheme.Warn;
            GUI.Label(new Rect(panel.x, y, panel.width, u * 1.6f),
                storesLine, SeaSick.UI.UITheme.Small2Centered);
            GUI.color = fullWas;
            y += u * 2.4f;

            SeaSick.UI.UITheme.Rect(new Rect(panel.x + u, y, panel.width - u * 2f, 1f),
                SeaSick.UI.UITheme.Track);
            y += u * 0.7f;

            float bw = Mathf.Min(panel.width - u * 2f, u * 20f);
            float bx = panel.center.x - bw * 0.5f;
            for (int i = 0; i < plans.Length; i++)
            {
                var plan = plans[i];
                var r = new Rect(bx, y, bw, u * 2.4f);
                SeaSick.UI.UIBlocker.Block(r);
                GUI.enabled = Banked(plan.resource) >= plan.cost;
                if (GUI.Button(r, planLabels[i], SeaSick.UI.UITheme.Button)) TryBuild(plan);
                GUI.enabled = true;
                y += u * 2.6f;

                GUI.Label(new Rect(panel.x, y, panel.width, u * 1.4f),
                    planBlurbs[i], SeaSick.UI.UITheme.Small2Centered);
                y += u * 1.8f;
            }

            if (!string.IsNullOrEmpty(buildNote))
                GUI.Label(new Rect(panel.x, y, panel.width, u * 1.6f), buildNote,
                    SeaSick.UI.UITheme.Small2Centered);

            var btn = new Rect(panel.center.x - u * 6f, panel.yMax - u * 3.4f, u * 12f, u * 2.4f);
            SeaSick.UI.UIBlocker.Block(btn);
            if (GUI.Button(btn, "set sail   (space)", SeaSick.UI.UITheme.Button)) BeginVoyage();
        }

        string HoldSummary()
        {
            var sb = new StringBuilder();
            foreach (var kv in held)
            {
                if (sb.Length > 0) sb.Append("  ");
                sb.Append($"{kv.Key} {kv.Value}");
            }
            return sb.ToString();
        }

        /// "timber 30/30 · boards 12/30" — every pile against its OWN ceiling,
        /// in `ResOrder`. Built inside `RefreshPanelText` and nowhere else.
        string BankSummary()
        {
            if (banked.Count == 0) return "nothing yet";
            int cap = StoreCapacity;
            var sb = new StringBuilder();
            foreach (var res in InOrder(banked))
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append($"{res.ToLowerInvariant()} {banked[res]}/{cap}");
            }
            return sb.ToString();
        }
    }
}
