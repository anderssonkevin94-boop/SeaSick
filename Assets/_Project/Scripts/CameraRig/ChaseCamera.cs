using UnityEngine;

namespace SeaSick.CameraRig
{
    /// Portrait chase camera: follows the ship's yaw only (never inherits
    /// pitch/roll — the horizon stays stable while the ship rocks), looking
    /// past the ship toward the horizon. No manual control by design.
    public class ChaseCamera : MonoBehaviour
    {
        [SerializeField] Transform target;
        // Three-quarter view: tilt = (height - lookHeight) / (distance +
        // lookAhead). Vertical FOV is 60, so the frame spans tilt +/- 30
        // degrees — the horizon is only visible while tilt stays under 30.
        // These give ~22 degrees, which keeps a band of sky at the top while
        // still looking down onto the deck.
        [SerializeField] float distance = 25f;
        [SerializeField] float height = 19f;
        [SerializeField] float lookAhead = 20f;
        [SerializeField] float lookHeight = 0.5f;
        [SerializeField] float positionResponse = 2.2f;
        [SerializeField] float rotationResponse = 3f;

        // FOV motion is the main cause of simulator sickness in a chase cam.
        // Keep the total swing small (a few degrees) and ease it slowly.
        [SerializeField] float fovBase = 58f;
        [SerializeField] float fovSpeedBoost = 4f;
        [Tooltip("Extra FOV kick while surfing down a wave face. Keep tiny.")]
        [SerializeField] float fovSurfPunch = 1.5f;
        [SerializeField] float fovResponse = 1.1f;
        [Tooltip("Never let the lens dip under the water surface.")]
        [SerializeField] float minHeightAboveWater = 2.6f;
        [Tooltip("Keeps the camera above island terrain instead of inside it.")]
        [SerializeField] float terrainClearance = 8f;

        public Transform Target { get => target; set => target = value; }

        /// When set (e.g. a shore party), the camera backs off and frames both
        /// the ship and this point so the player can watch the crew work.
        public Vector3? PointOfInterest { get; set; }

        Camera cam;
        SeaSick.Ship.ShipMotor motor;

        void Start()
        {
            cam = GetComponent<Camera>();
            if (target != null) motor = target.GetComponent<SeaSick.Ship.ShipMotor>();
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;

            // Speed reads in the lens: FOV opens with speed and punches when
            // the hull drops onto a wave face.
            if (cam != null && motor != null)
            {
                float s01 = Mathf.Clamp01(motor.CurrentSpeed / motor.MaxSpeed);
                float targetFov = fovBase + fovSpeedBoost * s01 * s01
                                  + fovSurfPunch * motor.SurfBoost01;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-fovResponse * dt));
            }

            Vector3 shipFlat = new Vector3(target.position.x, 0f, target.position.z);
            Vector3 anchor, desired, lookPoint;

            if (PointOfInterest.HasValue)
            {
                // Frame ship + shore party: sit on the far side of the ship
                // looking past it at the island, pulled back by their spread.
                Vector3 poi = PointOfInterest.Value;
                poi.y = 0f;
                Vector3 axis = shipFlat - poi;
                float separation = axis.magnitude;
                axis = separation < 0.5f ? -target.forward : axis / separation;

                // Bias toward the shore party — they're what the player wants
                // to watch. Framing keys off their spread rather than the
                // chase-cam height, which is deliberately high and would
                // otherwise leave the crew as specks.
                anchor = Vector3.Lerp(poi, shipFlat, 0.38f);
                float back = 18f + separation * 0.55f;
                desired = anchor + axis * back + Vector3.up * (14f + separation * 0.35f);
                lookPoint = anchor + Vector3.up * 1.5f;
            }
            else
            {
                Vector3 flatForward = target.forward;
                flatForward.y = 0f;
                flatForward = flatForward.sqrMagnitude < 0.001f ? Vector3.forward : flatForward.normalized;

                anchor = shipFlat;
                desired = anchor - flatForward * distance + Vector3.up * height;
                lookPoint = anchor + flatForward * lookAhead + Vector3.up * lookHeight;
            }

            transform.position = Vector3.Lerp(
                transform.position, desired, 1f - Mathf.Exp(-positionResponse * dt));

            // A low camera sells speed, but it must never end up underwater.
            var waves = SeaSick.Ocean.WaveField.Instance;
            if (waves != null)
            {
                Vector3 cp = transform.position;
                float surface = waves.SampleHeight(new Vector2(cp.x, cp.z), Time.time);
                if (cp.y < surface + minHeightAboveWater)
                    transform.position = new Vector3(cp.x, surface + minHeightAboveWater, cp.z);
            }

            // Islands are mountains now, and the camera sits well behind the
            // ship — close in to a shore it would otherwise end up inside the
            // hill, filling the screen with green.
            var isle = SeaSick.World.Island.Nearest(transform.position);
            if (isle != null)
            {
                Vector3 cp = transform.position;
                Vector3 d = cp - isle.transform.position;
                d.y = 0f;
                float dist = d.magnitude;
                float ang = Mathf.Atan2(d.x, d.z);
                if (dist < isle.RadiusAt(ang))
                {
                    float ground = isle.SurfacePoint(ang, dist).y;
                    if (cp.y < ground + terrainClearance)
                        transform.position = new Vector3(cp.x, ground + terrainClearance, cp.z);
                }
            }
            Quaternion desiredRot = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, desiredRot, 1f - Mathf.Exp(-rotationResponse * dt));
        }
    }
}
