using System.Collections.Generic;
using SeaSick.Crew;
using SeaSick.Ship;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.Voyage
{
    /// The voyage: cast off from the pier, work the archipelago island by
    /// island, come back alongside before the crew break — and then spend
    /// what you landed on the village. Gathering happens through the anchor
    /// and shore-party system; this class owns the hold, the stores and the
    /// homecoming bookkeeping. It has no UI of its own (2026-09-26, Kevin:
    /// *"voyage complete still shows up. I don't want that one there at
    /// all. It doesn't serve a purpose for the game."* — the old "VOYAGE
    /// COMPLETE" IMGUI panel, its tally strings and its build buttons are
    /// gone; building still happens through `TryBuild`, called from
    /// whatever sheet the village UI puts it on).
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

        /// True while she is lying at the pier, voyage closed, waiting to
        /// cast off again. The anchor controller stands its own buttons and
        /// its spacebar down on this.
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
        /// **RETIRED 2026-10-04 -- a save's old home bank, waiting to be
        /// repaired into the home camp's store.** Until then home docking
        /// banked the hold here (plus crate piles on the beach), a number the
        /// home CAMP never read: Kevin's ore and stone "vanished when I
        /// docked". Nothing adds to it any more. It is filled only by
        /// `RestoreStores` from an old save's `banked` rows and emptied by
        /// `RepairLegacyBank` (`HomeBankRepair`) into `Outpost.Home`'s ledger
        /// on load / homecoming. While there is no home camp to take it
        /// (a home pier gone), it is carried through saves untouched and
        /// nothing reads or spends it.
        readonly Dictionary<string, int> banked = new Dictionary<string, int>();

        /// The hold, read-only, for the save. Every other reader asks by
        /// name (`AmountOf`, `HeldOf`); a save has to walk it.
        public IReadOnlyDictionary<string, int> HeldStores => held;
        /// The UNREPAIRED old home bank (see `banked`), for the save to carry
        /// until a home camp can take it. Empty in any game since 2026-10-04.
        public IReadOnlyDictionary<string, int> BankedStores => banked;

        /// **Put a saved hold back**, and make the picture agree: the stack
        /// at the stern is rebuilt from the numbers, because it does not
        /// re-sync by itself. `legacyBank` is an old save's `banked` rows,
        /// kept only until `RepairLegacyBank` moves them into the home camp.
        ///
        /// Runs AFTER `Start` and after the yard has applied the saved rung,
        /// so `SetHoldCapacity` has already been told the saved size.
        public void RestoreStores(IEnumerable<KeyValuePair<string, int>> hold,
                                  IEnumerable<KeyValuePair<string, int>> legacyBank)
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
            if (legacyBank != null)
                foreach (var kv in legacyBank)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0) continue;
                    banked.TryGetValue(kv.Key, out int cur);
                    banked[kv.Key] = cur + kv.Value;
                }
            // No crate piles on the beach any more (no ground piles, Kevin
            // 2026-10-02): take down any an earlier session drew.
            ClearHomePile();
        }

        /// **The one-time repair** (2026-10-04): the old home bank goes into
        /// the home camp's store and is cleared. Idempotent -- with nothing
        /// left in `banked` it changes nothing. A no-op while there is no
        /// home camp; the bank waits. Returns the units moved.
        public int RepairLegacyBank()
        {
            var home = World.Outpost.Home;
            int moved = HomeBankRepair.Apply(banked, home != null ? home.Ledger : null);
            if (moved > 0)
            {
                ClearHomePile();
                Debug.Log("VoyageManager: repaired the old home bank -- " + moved
                    + " units moved into " + home.name + "'s store");
            }
            return moved;
        }

        /// The retired `Stockpile` crates on the home island, taken down.
        static void ClearHomePile()
        {
            var isle = World.Island.Home;
            var pile = isle != null ? World.Stockpile.Of(isle) : null;
            if (pile != null) pile.Clear();
        }

        CrewAgent[] crew;
        AnchorController anchor;
        bool hasLeftHome;
        /// She has been at her berth at least once. Guards the startup race
        /// below — see the note in Update.
        bool seenBerth;
        float voyageStartTime;
        int completedSpoiled;
        /// Per resource, in `ResOrder`: "4 timber · 2 boards". Fixed the
        /// moment she ties up, because the room that was short is the room
        /// there WAS, and building a storehouse afterwards must not rewrite
        /// the tally of the voyage that paid for it. Read by `SinkProbe`
        /// through `Spoiled`/`SpoiledDetail` — kept even though the panel
        /// that used to print it is gone (2026-09-26).
        string completedSpoiledDetail = "";

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

        /// Rest the hands who are ON her. `crew` is a cache, and a hand left
        /// at a camp is still in it: resting him snapped his camp body onto a
        /// deck that was not there, every frame she lay at home (2026-09-27).
        void RestAboard()
        {
            if (crew == null || ship == null) return;
            var hull = ship.transform;
            foreach (var c in crew)
                if (c != null && c.transform.IsChildOf(hull)) c.Rest();
        }

        void Start()
        {
            if (ship == null) ship = FindFirstObjectByType<ShipMotor>();
            crew = ship != null ? ship.GetComponentsInChildren<CrewAgent>(true) : new CrewAgent[0];
            anchor = ship != null ? ship.GetComponent<AnchorController>() : null;
            BeginVoyage();
        }

        /// **Cast off.** The spacebar while at home lands here, and so does
        /// `Start`. Public so a probe can trigger the same path the player
        /// does rather than setting the phase behind the game's back — see
        /// `SinkProbe`.
        public void BeginVoyage() => BeginVoyage(castOff: true);

        void BeginVoyage(bool castOff)
        {
            if (castOff && phase == Phase.Home && anchor != null) anchor.CastOff();
            phase = Phase.AtSea;
            TakeDeckCargo = false;
            hasLeftHome = false;
            voyageStartTime = Time.time;
            // **The hold is NOT emptied here any more** (2026-10-04). Home
            // is a camp: whatever its hands have not carried ashore yet is
            // still aboard when she casts off, and leaving the pier must
            // never wipe it. (It used to be a no-op -- `CompleteVoyage`
            // had already banked and cleared the hold -- and that banking is
            // what lost Kevin's ore.)
            if (ship != null) ship.CargoLoad = HoldFill;
            // Casting off is a moment worth keeping. A no-op until the player
            // has chosen New or Continue, so the call from `Start` cannot
            // overwrite a save with a fresh world.
            Save.SaveGame.Autosave("cast off");
        }

        /// Loot into the hold, from a shore party or salvaged from the sea.
        public void AddLoot(int amount, string resource)
        {
            // Open at home too since 2026-10-04: home is a camp, and its
            // hands load her through `ShipCargoSide.Give` like any camp's.
            if (amount <= 0) return;
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

        /// **Room for `amount` more units under the same ceiling `AddLoot`
        /// clamps to** (`MaxHold`). `ReturnCargo` stays unclamped for its own
        /// callers; the bow harpoon asks this first and keeps a load at the
        /// rail until it is true (Kevin 2026-10-03: the hold is finite).
        public bool CargoFits(int amount) => amount <= 0 || TotalHeld + amount <= MaxHold;

        /// **Cargo overboard, recovered or returned unsalvaged on save**
        /// (phase 6). Unlike `AddLoot`, never clamped to `MaxHold` — these
        /// units already counted as aboard a moment before they went over
        /// the side (or before the save that pulled them back out of the
        /// sea), so a hold that happens to be full right now must not cost
        /// them. The visible deck stack is the caller's, same as `AddLoot`.
        public void ReturnCargo(int amount, string resource)
        {
            if (amount <= 0 || string.IsNullOrEmpty(resource)) return;
            held.TryGetValue(resource, out int cur);
            held[resource] = cur + amount;
            TotalHeld += amount;
            if (ship != null) ship.CargoLoad = HoldFill;
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
            // No home berth, no voyage loop: a voyage is home-to-home, and a
            // new game has no home until the player makes one (2026-09-29).
            // The old fallback -- "home" = back within 165 m of HomePoint --
            // went with the pre-made home island.
            if (ship == null || World.Dock.Home == null) return;

            // **A load is not a voyage** (2026-09-26, Kevin: "completed
            // voyage! set sail or build a store house" after every Continue).
            // Mid-restore she is warped off the harbour to where the save
            // left her -- hundreds of metres, which read as "has left" --
            // and then berthed at her saved home pier, which read as
            // "arrived". Nothing the restore does to her pose counts; the
            // first frame after it judges her from wherever she ended up.
            if (Save.SaveGame.Restoring) return;

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
            }
            else
            {
                // **Left home by another door (2026-10-03, Kevin: "I can't
                // always land at a new island").** Casting off from the Ship
                // sheet or the thumb bar goes through `AnchorController
                // .CastOff` and never reached `BeginVoyage`, so the voyage
                // stayed "at home": no island offered landing and the crew
                // were rested every frame at sea, until a reload. Whatever
                // took her off the pier -- weighing, under way -- the voyage
                // has begun. `castOff: false`, because she is already going
                // (calling `CastOff` again from here is harmless today, but
                // `BeginVoyage` -> `CastOff` -> back here must never become a
                // loop).
                if (anchor != null && !anchor.AtHomeDock)
                {
                    BeginVoyage(castOff: false);
                    return;
                }
                RestAboard();
                // Spacebar is the only way to cast off now that there is no
                // panel button for it (2026-09-26). Not while the shipyard is
                // open (2026-09-24, ShipyardUiProbe:
                // Space here cast her off from under an open refit screen).
                if (!SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked
                    && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                    BeginVoyage();
            }
        }

        /// **Home is a camp like any other** (2026-10-04). The hold stays
        /// aboard; the home camp's hands (its runners, once it has any) carry
        /// it ashore armful by armful into the home camp's store through the
        /// ordinary transfer path (`OutpostLedger.OrderTransfer`, ship end
        /// `World.ShipCargoSide`) -- a good counts only when a villager has
        /// delivered it. Homecoming places one "all of it, ashore" order per
        /// kind aboard, which the player can stop or reverse on the Backpack
        /// sheet like any order. Until 2026-10-04 this banked the hold into
        /// a home-only number (`banked`) the home camp never read, drew crate
        /// piles on the beach and emptied the hold -- Kevin's ore and stone
        /// "vanished when I docked".
        void CompleteVoyage()
        {
            completedSpoiled = 0;
            completedSpoiledDetail = "";
            // Re-read: hands left at camps since Start are not aboard, and
            // hands the yard cloned since are.
            crew = ship != null ? ship.GetComponentsInChildren<CrewAgent>(true) : new CrewAgent[0];
            RestAboard();
            phase = Phase.Home;
            OrderHomeUnload();
            // An old save's bank, if a home camp has appeared since the load.
            RepairLegacyBank();
        }

        /// One TransferAll ship -> store order per CARGO kind aboard, on the home
        /// camp (the repair stock of Timber stays aboard, `HomeUnload`). Refused (she is not alongside after all, no camp) = nothing
        /// moves and nothing is lost: the cargo stays aboard and the
        /// Backpack sheet can order it later.
        void OrderHomeUnload()
        {
            var home = World.Outpost.Home;
            var l = home != null ? home.Ledger : null;
            if (l == null || held.Count == 0) return;
            World.ShipCargoSide.BindTo(home);
            // Cargo only: the repair stock of Timber stays aboard (`HomeUnload`).
            var kinds = new List<string>(InOrder(held));
            HomeUnload.OrderAll(kinds, held, l);
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

        /// **The home camp's store** (2026-10-04): what the yard and the home
        /// buildings are paid from. `SpendableOf` -- store, racks, finished
        /// benches; never a station's bay -- the same gate every camp cost
        /// reads, and exactly what `SpendFromHome` can take. 0 with no home
        /// camp: a new game has no home pier, no yard and nothing to pay.
        public int HomeStoreOf(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return 0;
            var home = World.Outpost.Home;
            var l = home != null ? home.Ledger : null;
            return l != null ? l.SpendableOf(resource) : 0;
        }

        /// Everything in the home camp's store, all kinds. A readout for
        /// probes, never a capacity question.
        public int HomeStoreTotal
        {
            get
            {
                var home = World.Outpost.Home;
                return home != null && home.Ledger != null ? home.Ledger.Total : 0;
            }
        }

        /// Old names, kept so the probes compile: both read the HOME CAMP
        /// store now (`HomeStoreOf`), not the retired `banked`.
        public int Banked(string resource) => HomeStoreOf(resource);
        public int BankedTotal => HomeStoreTotal;

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
            if (village == null) return false;
            if (HomeStoreOf(plan.resource) < plan.cost) return false;

            var raised = village.Raise(plan);
            if (raised == null) return false;

            SpendFromHome(plan.resource, plan.cost);
            return true;
        }

        /// **Pay out of the home camp's store** (2026-10-04; was the retired
        /// `banked`). `OutpostLedger.Take` -- store first, then racks and
        /// finished benches, never a bay -- settled to now first, as every
        /// other spend of a camp's goods is. It does not check:
        /// `ShipPrices.TrySpend` and `TryBuild` check, and `Take` clamps to
        /// what is there. Returns what was taken.
        public int SpendFromHome(string resource, int amount)
        {
            if (string.IsNullOrEmpty(resource) || amount <= 0) return 0;
            var home = World.Outpost.Home;
            if (home == null || home.Ledger == null) return 0;
            home.CatchUp();
            return home.Ledger.Take(resource, amount);
        }

        /// Old name, kept so the probes compile. See `SpendFromHome`.
        public void SpendBanked(string resource, int amount) => SpendFromHome(resource, amount);

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

    }
}
