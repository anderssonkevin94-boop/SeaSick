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
        [SerializeField] int holdCapacity = 40;

        enum Phase { AtSea, Tally }
        Phase phase = Phase.AtSea;

        public Transform HomePoint => homePoint;
        public bool HoldFull => TotalHeld >= holdCapacity;
        public int TotalHeld { get; private set; }

        readonly Dictionary<string, int> held = new Dictionary<string, int>();
        readonly Dictionary<string, int> banked = new Dictionary<string, int>();

        CrewAgent[] crew;
        float voyageStartTime;
        int pukesAtStart;
        float completedTime;
        int completedPukes;
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
            voyageStartTime = Time.time;
            pukesAtStart = TotalPukes();
            held.Clear();
            TotalHeld = 0;
            if (ship != null) ship.CargoLoad01 = 0f;
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
            int room = holdCapacity - TotalHeld;
            if (room <= 0) return;
            amount = Mathf.Min(amount, room);

            held.TryGetValue(resource, out int cur);
            held[resource] = cur + amount;
            TotalHeld += amount;
            ship.CargoLoad01 = Mathf.Clamp01((float)TotalHeld / holdCapacity);
        }

        public void AddSalvage(int amount) => AddLoot(amount, "Timber");

        public int AmountOf(string resource) => held.TryGetValue(resource, out int n) ? n : 0;

        /// Spend cargo (repairs burn timber). False if the hold can't cover it.
        public bool TryConsume(string resource, int amount)
        {
            if (!held.TryGetValue(resource, out int have) || have < amount) return false;
            held[resource] = have - amount;
            if (held[resource] <= 0) held.Remove(resource);
            TotalHeld -= amount;
            ship.CargoLoad01 = Mathf.Clamp01((float)TotalHeld / holdCapacity);
            return true;
        }

        /// Mutinous crew throwing loot overboard (stage 4). Lightening the
        /// ship also restores speed — they really do get home faster.
        public void DitchCargo(int amount)
        {
            if (TotalHeld <= 0) return;
            var keys = new List<string>(held.Keys);
            foreach (var k in keys)
            {
                if (amount <= 0) break;
                int take = Mathf.Min(amount, held[k]);
                held[k] -= take;
                TotalHeld -= take;
                amount -= take;
                if (held[k] <= 0) held.Remove(k);
            }
            ship.CargoLoad01 = Mathf.Clamp01((float)TotalHeld / holdCapacity);
        }

        void Update()
        {
            if (ship == null || homePoint == null) return;

            if (phase == Phase.AtSea)
            {
                if (Flat(ship.transform.position, homePoint.position) < homeRadius)
                    CompleteVoyage();
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

            var sb = new StringBuilder();
            foreach (var kv in held)
            {
                if (sb.Length > 0) sb.Append("   ");
                sb.Append($"+{kv.Value} {kv.Key}");
                banked.TryGetValue(kv.Key, out int cur);
                banked[kv.Key] = cur + kv.Value;
            }
            completedHaul = sb.Length > 0 ? sb.ToString() : "empty hold";

            held.Clear();
            TotalHeld = 0;
            ship.CargoLoad01 = 0f;
            foreach (var c in crew) if (c != null) c.Rest();
            phase = Phase.Tally;
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        void OnGUI()
        {
            if (centerLabel == null)
            {
                centerLabel = new GUIStyle(GUI.skin.label)
                { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                cargoLabel = new GUIStyle(GUI.skin.label)
                { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }

            float w = Screen.width, h = Screen.height;

            if (phase == Phase.AtSea)
            {
                var strip = new Rect(0f, h * 0.90f, w, 26f);
                GUI.color = new Color(0f, 0f, 0f, 0.45f);
                GUI.DrawTexture(strip, Texture2D.whiteTexture);
                GUI.color = Color.white;
                float home = Flat(ship.transform.position, homePoint.position);
                string hold = TotalHeld > 0 ? HoldSummary() : "hold empty";
                GUI.Label(strip, $"{hold}   ({TotalHeld}/{holdCapacity})   ·   home {home:F0} m", cargoLabel);
                return;
            }

            var panel = new Rect(w * 0.1f, h * 0.3f, w * 0.8f, h * 0.32f);
            GUI.color = new Color(0.05f, 0.08f, 0.12f, 0.9f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            int m = Mathf.FloorToInt(completedTime / 60f);
            int s = Mathf.FloorToInt(completedTime % 60f);
            string crewLine = completedPukes == 0
                ? "the crew kept it together!"
                : $"the crew puked {completedPukes}×";
            GUI.Label(panel,
                $"VOYAGE COMPLETE\n\n{completedHaul}\n\nstores: {BankSummary()}\ntime {m}:{s:00}   ·   {crewLine}\n\ntap / space to set sail",
                centerLabel);
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
