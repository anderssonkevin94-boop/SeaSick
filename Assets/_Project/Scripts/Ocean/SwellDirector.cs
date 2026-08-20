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
            if (!inside && SecondsToImpact <= 0f) return;
            // A mountain sea owns this slot when there is one — it is the far
            // more urgent thing, and two plates stacked is clutter.
            if (MountainSeaDirector.WarningShowing) return;

            var shelter = Island.Nearest(ship.transform.position);
            string shelterText = shelter != null
                ? $"shelter {Island.FlatDistance(ship.transform.position, shelter.transform.position):F0} m"
                : "no shelter";
            string head = inside ? "in the swell" : $"swell  {SecondsToImpact:F0}s";

            // A small plate tucked under the compass, not a band across the
            // world. You read it the way you read the rest of the instruments.
            int u = SeaSick.UI.UITheme.Unit;
            float w = u * 11f;
            float x = (Screen.width - w) * 0.5f;
            float y = u * 5.2f;
            SeaSick.UI.UITheme.Rect(new Rect(x, y, w, u * 3.1f), SeaSick.UI.UITheme.PanelSolid);
            SeaSick.UI.UITheme.Rect(new Rect(x, y, w, 2f),
                inside ? SeaSick.UI.UITheme.Bad : SeaSick.UI.UITheme.Warn);
            GUI.Label(new Rect(x, y + u * 0.25f, w, u * 1.5f), head, SeaSick.UI.UITheme.Small2Centered);
            GUI.Label(new Rect(x, y + u * 1.6f, w, u * 1.4f), shelterText, SeaSick.UI.UITheme.Small);
        }
    }
}
