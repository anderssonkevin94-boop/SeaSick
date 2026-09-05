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
        [Tooltip("What home keeps with nothing built. Only used if the island never got a Village.")]
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
        string buildNote = "";

        GUIStyle centerLabel, cargoLabel;

        void Start()
        {
            if (ship == null) ship = FindFirstObjectByType<ShipMotor>();
            crew = ship != null ? ship.GetComponentsInChildren<CrewAgent>(true) : new CrewAgent[0];
            anchor = ship != null ? ship.GetComponent<AnchorController>() : null;
            BeginVoyage();
        }

        void BeginVoyage()
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

        public void AddSalvage(int amount) => AddLoot(amount, "Timber");

        public int AmountOf(string resource) => held.TryGetValue(resource, out int n) ? n : 0;

        /// Over the side. The escape valve for a ship that is going under —
        /// costs you the payoff, buys back freeboard immediately. The player's
        /// decision, at the helm, in seconds.
        public int Jettison(int amount)
        {
            if (TotalHeld <= 0 || amount <= 0) return 0;
            int dumped = 0;
            var keys = new List<string>(held.Keys);
            foreach (var k in keys)
            {
                if (amount <= 0) break;
                int take = Mathf.Min(amount, held[k]);
                held[k] -= take;
                TotalHeld -= take;
                amount -= take;
                dumped += take;
                if (held[k] <= 0) held.Remove(k);
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
            int room = Mathf.Max(0, StoreCapacity - BankedTotal);
            var sb = new StringBuilder();
            var landed = new List<string>();
            completedSpoiled = 0;
            foreach (var kv in held)
            {
                if (sb.Length > 0) sb.Append("   ");
                sb.Append($"+{kv.Value} {kv.Key}");
                int take = Mathf.Min(kv.Value, room);
                room -= take;
                completedSpoiled += kv.Value - take;
                if (take <= 0) continue;
                banked.TryGetValue(kv.Key, out int cur);
                banked[kv.Key] = cur + take;
                for (int i = 0; i < take; i++) landed.Add(kv.Key);
            }
            completedHaul = sb.Length > 0 ? sb.ToString() : "empty hold";

            // Carry it ashore piece by piece so the pile visibly grows rather
            // than the haul evaporating into a number.
            if (landed.Count > 0) StartCoroutine(UnloadAshore(landed));

            held.Clear();
            TotalHeld = 0;
            ship.CargoLoad = 0f;
            foreach (var c in crew) if (c != null) c.Rest();
            buildNote = "";
            phase = Phase.Home;
        }

        // --- The stores, and what they buy ----------------------------------

        /// Everything home can keep, which grows as the village is built.
        public int StoreCapacity => World.Village.Home != null
            ? World.Village.Home.StoreCapacity : fallbackStoreCapacity;

        public int BankedTotal
        {
            get { int n = 0; foreach (var kv in banked) n += kv.Value; return n; }
        }

        public int Banked(string resource) =>
            banked.TryGetValue(resource, out int n) ? n : 0;

        /// Raise a building and pay for it out of the stores.
        ///
        /// Sited FIRST, paid for second: `Village.Raise` returns null when
        /// there is nowhere in the clearing left to stand it, and charging
        /// for a building that was never built is the one outcome the player
        /// can neither see nor undo.
        public bool TryBuild(World.BuildPlan plan)
        {
            var village = World.Village.Home;
            if (village == null) { buildNote = "nowhere to build"; return false; }
            if (Banked(plan.resource) < plan.cost)
            {
                buildNote = $"need {plan.cost} {plan.resource.ToLower()}";
                return false;
            }

            var raised = village.Raise(plan);
            if (raised == null) { buildNote = "no room left in the clearing"; return false; }

            SpendBanked(plan.resource, plan.cost);
            buildNote = $"{plan.label} raised — home keeps {StoreCapacity}";
            return true;
        }

        /// Off the beach and into the building. The visible pile has to come
        /// down with the number, or the stores read as spent in the panel and
        /// untouched on the ground two metres away.
        void SpendBanked(string resource, int amount)
        {
            banked.TryGetValue(resource, out int have);
            int take = Mathf.Min(have, amount);
            banked[resource] = have - take;
            if (banked[resource] <= 0) banked.Remove(resource);
            var pile = World.Stockpile.Instance;
            if (pile != null) pile.Withdraw(resource, take);
        }

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

        void OnGUI()
        {
            // At sea the permanent HUD (StatusHUD) covers cargo and distance —
            // this class only draws the one moment she is tied up and the
            // player is not steering.
            if (phase != Phase.Home) return;

            int u = SeaSick.UI.UITheme.Unit;
            float w = Screen.width, h = Screen.height;
            var plans = World.BuildPlans.All;
            float ph = u * (19f + (completedSpoiled > 0 ? 1.9f : 0f) + plans.Length * 4.4f);
            var panel = new Rect(w * 0.08f, Mathf.Max(u * 2f, (h - ph) * 0.42f), w * 0.84f, ph);
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
                    $"{completedSpoiled} left on the sand — nowhere to keep it",
                    SeaSick.UI.UITheme.Small2Centered);
                GUI.color = was;
                y += u * 1.9f;
            }

            int m = Mathf.FloorToInt(completedTime / 60f);
            int sec = Mathf.FloorToInt(completedTime % 60f);
            // Trips to the rail alone is a misleading number now that the
            // meter never falls — a crew can be finished having puked twice,
            // or fine having puked once. Lead with how bad it got.
            string crewLine = completedPukes == 0
                ? "the crew kept it together"
                : $"worst {completedWorst:P0} sick   ·   {completedPukes}× to the rail";
            GUI.Label(new Rect(panel.x, y, panel.width, u * 1.6f),
                $"{m}:{sec:00}   ·   {crewLine}", SeaSick.UI.UITheme.Small2Centered);
            y += u * 2.2f;

            // Stores against what they can BE — a bare number cannot tell you
            // the beach is full, and being full is the whole reason the
            // buttons below it exist.
            int total = BankedTotal, cap = StoreCapacity;
            var fullWas = GUI.color;
            if (total >= cap) GUI.color = SeaSick.UI.UITheme.Warn;
            GUI.Label(new Rect(panel.x, y, panel.width, u * 1.6f),
                $"stores {total} / {cap}   ·   {BankSummary()}", SeaSick.UI.UITheme.Small2Centered);
            GUI.color = fullWas;
            y += u * 2.4f;

            SeaSick.UI.UITheme.Rect(new Rect(panel.x + u, y, panel.width - u * 2f, 1f),
                SeaSick.UI.UITheme.Track);
            y += u * 0.7f;

            float bw = Mathf.Min(panel.width - u * 2f, u * 20f);
            float bx = panel.center.x - bw * 0.5f;
            var village = World.Village.Home;
            foreach (var plan in plans)
            {
                int have = Banked(plan.resource);
                bool afford = have >= plan.cost;
                int already = village != null ? village.CountOf(plan.id) : 0;

                var r = new Rect(bx, y, bw, u * 2.4f);
                SeaSick.UI.UIBlocker.Block(r);
                GUI.enabled = afford;
                // "×2" read as "build two of them". The second one is
                // another one.
                string label = already > 0
                    ? $"build another {plan.label} — {plan.cost} {plan.resource.ToLower()}"
                    : $"build {plan.label} — {plan.cost} {plan.resource.ToLower()}";
                if (GUI.Button(r, label, SeaSick.UI.UITheme.Button)) TryBuild(plan);
                GUI.enabled = true;
                y += u * 2.6f;

                GUI.Label(new Rect(panel.x, y, panel.width, u * 1.4f),
                    afford ? plan.blurb : $"{plan.blurb}   ·   {have}/{plan.cost} {plan.resource.ToLower()}",
                    SeaSick.UI.UITheme.Small2Centered);
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

        string BankSummary()
        {
            if (banked.Count == 0) return "nothing yet";
            var sb = new StringBuilder();
            foreach (var kv in banked)
            {
                if (sb.Length > 0) sb.Append("   ");
                sb.Append($"{kv.Key} {kv.Value}");
            }
            return sb.ToString();
        }
    }
}
