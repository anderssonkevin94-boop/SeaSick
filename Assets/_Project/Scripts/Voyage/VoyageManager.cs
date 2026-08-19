using System.Collections.Generic;
using System.Text;
using SeaSick.Crew;
using SeaSick.Ship;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.Voyage
{
    /// The voyage: leave home, work the archipelago island by island, come
    /// back before the crew break. Gathering now happens through the anchor
    /// and shore-party system — this class owns the hold, the banking, and
    /// the tally. UI is dev-grade IMGUI until the loop is feel-approved.
    public class VoyageManager : MonoBehaviour
    {
        [SerializeField] ShipMotor ship;
        [SerializeField] Transform homePoint;
        [SerializeField] float homeRadius = 60f;
        [Tooltip("The marked line — a full hold. NOT a hard cap: you may load past it.")]
        [SerializeField] int holdCapacity = 40;
        [Tooltip("How far past the line she'll physically take, as a multiple. The rest rides on deck.")]
        [SerializeField] float overloadLimit = 1.6f;

        enum Phase { AtSea, Tally }
        Phase phase = Phase.AtSea;

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
        public int MaxHold => Mathf.RoundToInt(holdCapacity * overloadLimit);
        /// 1.0 is the marked line. Above that she's carrying deck cargo.
        public float HoldFill => holdCapacity > 0 ? (float)TotalHeld / holdCapacity : 0f;
        public bool Overloaded => TotalHeld > holdCapacity;

        readonly Dictionary<string, int> held = new Dictionary<string, int>();
        readonly Dictionary<string, int> banked = new Dictionary<string, int>();

        CrewAgent[] crew;
        bool hasLeftHome;
        float voyageStartTime;
        int pukesAtStart;
        float completedTime;
        int completedPukes;
        float completedWorst;
        string completedHaul = "";

        GUIStyle centerLabel, cargoLabel;

        void Start()
        {
            if (ship == null) ship = FindFirstObjectByType<ShipMotor>();
            crew = ship != null ? ship.GetComponentsInChildren<CrewAgent>(true) : new CrewAgent[0];
            BeginVoyage();
        }

        void BeginVoyage()
        {
            phase = Phase.AtSea;
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
            if (phase == Phase.Tally || amount <= 0) return;
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
            if (ship == null || homePoint == null) return;

            if (phase == Phase.AtSea)
            {
                float home = Flat(ship.transform.position, homePoint.position);
                // You have to actually leave before you can arrive — the ship
                // starts inside the arrival radius, which otherwise completed
                // a 0-second voyage the instant the game began.
                if (!hasLeftHome && home > homeRadius * 1.25f) hasLeftHome = true;
                if (hasLeftHome && home < homeRadius) CompleteVoyage();
            }
            else
            {
                foreach (var c in crew) if (c != null) c.Rest();
                bool tap = Pointer.current != null && Pointer.current.press.wasPressedThisFrame;
                bool key = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
                if (tap || key) BeginVoyage();
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

            var sb = new StringBuilder();
            var landed = new List<string>();
            foreach (var kv in held)
            {
                if (sb.Length > 0) sb.Append("   ");
                sb.Append($"+{kv.Value} {kv.Key}");
                banked.TryGetValue(kv.Key, out int cur);
                banked[kv.Key] = cur + kv.Value;
                for (int i = 0; i < kv.Value; i++) landed.Add(kv.Key);
            }
            completedHaul = sb.Length > 0 ? sb.ToString() : "empty hold";

            // Carry it ashore piece by piece so the pile visibly grows rather
            // than the haul evaporating into a number.
            if (landed.Count > 0) StartCoroutine(UnloadAshore(landed));

            held.Clear();
            TotalHeld = 0;
            ship.CargoLoad = 0f;
            foreach (var c in crew) if (c != null) c.Rest();
            phase = Phase.Tally;
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

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        void OnGUI()
        {
            // At sea the permanent HUD (StatusHUD) covers cargo and distance —
            // this class only draws the end-of-voyage moment.
            if (phase != Phase.Tally) return;

            int u = SeaSick.UI.UITheme.Unit;
            float w = Screen.width, h = Screen.height;
            var panel = new Rect(w * 0.08f, h * 0.30f, w * 0.84f, u * 16f);
            SeaSick.UI.UITheme.Rect(panel, SeaSick.UI.UITheme.PanelSolid);
            SeaSick.UI.UITheme.Rect(new Rect(panel.x, panel.y, panel.width, 2f), SeaSick.UI.UITheme.Sea);

            float y = panel.y + u * 1.4f;
            GUI.Label(new Rect(panel.x, y, panel.width, u * 2f), "VOYAGE COMPLETE", SeaSick.UI.UITheme.Title);
            y += u * 3.2f;
            GUI.Label(new Rect(panel.x, y, panel.width, u * 1.8f), completedHaul, SeaSick.UI.UITheme.Strong);
            y += u * 2.6f;

            int m = Mathf.FloorToInt(completedTime / 60f);
            int s = Mathf.FloorToInt(completedTime % 60f);
            // Trips to the rail alone is a misleading number now that the
            // meter never falls — a crew can be finished having puked twice,
            // or fine having puked once. Lead with how bad it got.
            string crewLine = completedPukes == 0
                ? "the crew kept it together"
                : $"worst {completedWorst:P0} sick   ·   {completedPukes}× to the rail";
            GUI.Label(new Rect(panel.x, y, panel.width, u * 1.6f),
                $"{m}:{s:00}   ·   {crewLine}", SeaSick.UI.UITheme.Small2Centered);
            y += u * 2.2f;
            GUI.Label(new Rect(panel.x, y, panel.width, u * 1.6f),
                $"stores — {BankSummary()}", SeaSick.UI.UITheme.Small2Centered);

            var btn = new Rect(panel.center.x - u * 6f, panel.yMax - u * 3.4f, u * 12f, u * 2.4f);
            SeaSick.UI.UIBlocker.Block(btn);
            if (GUI.Button(btn, "set sail", SeaSick.UI.UITheme.Button)) BeginVoyage();
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
