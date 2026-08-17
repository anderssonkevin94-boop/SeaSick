using SeaSick.Crew;
using SeaSick.Ship;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.Voyage
{
    /// The core loop: sail out -> anchor & gather -> sail home heavy -> tally.
    /// UI is dev-grade IMGUI for now; real portrait UI comes after the loop
    /// is proven. Mutiny/turn-back arrives in milestone 5.
    public class VoyageManager : MonoBehaviour
    {
        [SerializeField] ShipMotor ship;
        [SerializeField] Transform homePoint;
        [SerializeField] Transform destinationPoint;
        [SerializeField] string destinationName = "Windward Isle";
        [SerializeField] float anchorRadius = 40f;
        [SerializeField] float homeRadius = 45f;
        [SerializeField] float gatherDuration = 6f;
        [SerializeField] int lootPerTrip = 12;

        enum Phase { Outbound, Gathering, ReturnLeg, Tally }
        Phase phase = Phase.Outbound;

        CrewAgent crew;
        float gatherT;
        float voyageStartTime;
        int pukesAtStart;
        int lootHeld;
        int lootBanked;
        float completedTime;
        int completedPukes;

        GUIStyle centerLabel;

        void Start()
        {
            if (ship == null) ship = FindFirstObjectByType<ShipMotor>();
            crew = ship != null ? ship.GetComponentInChildren<CrewAgent>() : null;
            BeginVoyage();
        }

        void BeginVoyage()
        {
            phase = Phase.Outbound;
            voyageStartTime = Time.time;
            pukesAtStart = crew != null ? crew.PukeCount : 0;
            lootHeld = 0;
        }

        void Update()
        {
            if (ship == null) return;
            Vector3 shipPos = ship.transform.position;

            switch (phase)
            {
                case Phase.Outbound:
                    if (Flat(shipPos, destinationPoint.position) < anchorRadius)
                    {
                        phase = Phase.Gathering;
                        gatherT = 0f;
                    }
                    break;

                case Phase.Gathering:
                    gatherT += Time.deltaTime;
                    if (gatherT >= gatherDuration)
                    {
                        lootHeld = lootPerTrip;
                        ship.CargoLoad01 = 1f;
                        phase = Phase.ReturnLeg;
                    }
                    break;

                case Phase.ReturnLeg:
                    if (Flat(shipPos, homePoint.position) < homeRadius)
                    {
                        completedTime = Time.time - voyageStartTime;
                        completedPukes = (crew != null ? crew.PukeCount : 0) - pukesAtStart;
                        lootBanked += lootHeld;
                        ship.CargoLoad01 = 0f;
                        if (crew != null) crew.Rest();
                        phase = Phase.Tally;
                    }
                    break;

                case Phase.Tally:
                    if (crew != null) crew.Rest(); // docked: no sickness at home
                    bool tap = Pointer.current != null && Pointer.current.press.wasPressedThisFrame;
                    bool key = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
                    if (tap || key) BeginVoyage();
                    break;
            }
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        void OnGUI()
        {
            if (centerLabel == null)
                centerLabel = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };

            float w = Screen.width;
            float h = Screen.height;
            var strip = new Rect(0f, h * 0.86f, w, 34f);

            switch (phase)
            {
                case Phase.Outbound:
                {
                    float d = Flat(ship.transform.position, destinationPoint.position);
                    Banner(strip, $"→ {destinationName}   {d:F0} m");
                    break;
                }
                case Phase.Gathering:
                {
                    Banner(strip, $"Gathering timber…");
                    var bar = new Rect(w * 0.25f, strip.y - 14f, w * 0.5f, 10f);
                    GUI.color = new Color(0f, 0f, 0f, 0.4f);
                    GUI.DrawTexture(bar, Texture2D.whiteTexture);
                    GUI.color = new Color(0.95f, 0.8f, 0.3f);
                    GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * (gatherT / gatherDuration), bar.height),
                        Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    break;
                }
                case Phase.ReturnLeg:
                {
                    float d = Flat(ship.transform.position, homePoint.position);
                    Banner(strip, $"→ Home   {d:F0} m   (cargo full: +{lootHeld} timber)");
                    break;
                }
                case Phase.Tally:
                {
                    var panel = new Rect(w * 0.12f, h * 0.32f, w * 0.76f, h * 0.3f);
                    GUI.color = new Color(0.05f, 0.08f, 0.12f, 0.88f);
                    GUI.DrawTexture(panel, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    int m = Mathf.FloorToInt(completedTime / 60f);
                    int s = Mathf.FloorToInt(completedTime % 60f);
                    string bo = completedPukes == 0
                        ? "Bo kept it together!"
                        : $"Bo puked {completedPukes}×";
                    GUI.Label(panel, $"VOYAGE COMPLETE\n\n+{lootHeld} timber  (stock: {lootBanked})\ntime {m}:{s:00}   ·   {bo}\n\ntap / space to set sail", centerLabel);
                    break;
                }
            }
        }

        void Banner(Rect r, string text)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(r, text, centerLabel);
        }
    }
}
