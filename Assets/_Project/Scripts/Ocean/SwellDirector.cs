using SeaSick.Ship;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.Ocean
{
    /// Launches swell fronts that sweep across the archipelago. The player gets
    /// a long, honest warning — enough time to reach shelter IF they haven't
    /// overreached. Being caught out in one is a fast track to mutiny.
    [RequireComponent(typeof(WaveField))]
    public class SwellDirector : MonoBehaviour
    {
        [SerializeField] Vector2 intervalRange = new Vector2(58f, 88f);
        [SerializeField] float firstSwellDelay = 40f;
        [SerializeField] float spawnDistance = 470f;
        [SerializeField] float frontSpeed = 9.5f;
        [SerializeField] float halfWidth = 115f;
        [SerializeField] float steepness = 0.62f;
        [SerializeField] float wavelength = 72f;
        [SerializeField] float clearBeyond = 520f; // front has swept well past

        WaveField field;
        ShipMotor ship;
        float nextAt;
        GUIStyle style, subStyle;

        public float ShipIntensity { get; private set; }
        public float SecondsToImpact { get; private set; } = -1f;

        void Start()
        {
            field = GetComponent<WaveField>();
            ship = FindFirstObjectByType<ShipMotor>();
            nextAt = Time.time + firstSwellDelay;
        }

        void Update()
        {
            float t = Time.time;

            if (!field.SwellActive && t >= nextAt) Launch();

            if (field.SwellActive && ship != null)
            {
                Vector2 p = new Vector2(ship.transform.position.x, ship.transform.position.z);
                ShipIntensity = field.SwellIntensity(p, t);
                float approach = field.SwellApproachDistance(p, t);
                SecondsToImpact = approach > 0f ? approach / field.SwellSpeed : -1f;

                // Retire the front once it's swept far past the player.
                if (approach < -clearBeyond)
                {
                    field.ClearSwell();
                    ShipIntensity = 0f;
                    SecondsToImpact = -1f;
                    nextAt = t + Random.Range(intervalRange.x, intervalRange.y);
                }
            }
            else if (!field.SwellActive)
            {
                ShipIntensity = 0f;
                SecondsToImpact = -1f;
            }
        }

        void Launch()
        {
            Vector3 shipPos = ship != null ? ship.transform.position : Vector3.zero;
            Vector2 p = new Vector2(shipPos.x, shipPos.z);
            // Come at the player from a random quarter so routes stay honest.
            float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector2 from = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang));
            Vector2 dir = -from;
            Vector2 origin = p + from * spawnDistance;
            field.LaunchSwell(origin, dir, frontSpeed, halfWidth, steepness, wavelength);
        }

        void OnGUI()
        {
            if (!field.SwellActive || ship == null) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                };
                subStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14, alignment = TextAnchor.MiddleCenter,
                };
            }

            bool inside = ShipIntensity > 0.15f;
            var r = new Rect(0f, Screen.height * 0.29f, Screen.width, 30f);
            var r2 = new Rect(0f, Screen.height * 0.29f + 28f, Screen.width, 22f);

            GUI.color = inside
                ? new Color(0.65f, 0.06f, 0.04f, 0.82f)
                : new Color(0.92f, 0.55f, 0.08f, 0.72f);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 50f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var shelter = Island.Nearest(ship.transform.position);
            string shelterText = shelter != null
                ? $"nearest shelter {Island.FlatDistance(ship.transform.position, shelter.transform.position):F0} m"
                : "no shelter in range";

            if (inside)
            {
                GUI.Label(r, "CAUGHT IN THE SWELL — make for land!", style);
                GUI.Label(r2, shelterText, subStyle);
            }
            else if (SecondsToImpact > 0f)
            {
                GUI.Label(r, $"BIG SWELL INCOMING — {SecondsToImpact:F0}s", style);
                GUI.Label(r2, shelterText, subStyle);
            }
            else
            {
                GUI.Label(r, "the swell is passing…", style);
            }
        }
    }
}
