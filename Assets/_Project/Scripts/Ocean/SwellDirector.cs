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
        [SerializeField] Vector2 intervalRange = new Vector2(70f, 110f);
        [SerializeField] float firstSwellDelay = 45f;
        // Warning is now short and the band is wide: the storm arrives soon
        // after you're told, then stays with you for the best part of a minute.
        [SerializeField] float spawnDistance = 360f;
        [SerializeField] float frontSpeed = 7.5f;
        [SerializeField] float halfWidth = 250f;
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

            bool inside = ShipIntensity > 0.15f;
            var shelter = Island.Nearest(ship.transform.position);
            string shelterText = shelter != null
                ? $"nearest shelter {Island.FlatDistance(ship.transform.position, shelter.transform.position):F0} m"
                : "no shelter in range";

            // Sits below the navigation tape, which owns the top of the screen.
            if (inside)
                SeaSick.UI.UITheme.Banner(0.20f, "CAUGHT IN THE SWELL",
                    new Color(0.62f, 0.08f, 0.06f, 0.80f), shelterText);
            else if (SecondsToImpact > 0f)
                SeaSick.UI.UITheme.Banner(0.20f, $"BIG SWELL  ·  {SecondsToImpact:F0}s",
                    new Color(0.80f, 0.46f, 0.06f, 0.72f), shelterText);
        }
    }
}
