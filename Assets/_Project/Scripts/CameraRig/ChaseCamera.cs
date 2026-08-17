using UnityEngine;

namespace SeaSick.CameraRig
{
    /// Portrait chase camera: follows the ship's yaw only (never inherits
    /// pitch/roll — the horizon stays stable while the ship rocks), looking
    /// past the ship toward the horizon. No manual control by design.
    public class ChaseCamera : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] float distance = 27f;
        [SerializeField] float height = 14f;
        [SerializeField] float lookAhead = 28f;
        [SerializeField] float lookHeight = 2.5f;
        [SerializeField] float positionResponse = 2.2f;
        [SerializeField] float rotationResponse = 3f;

        [SerializeField] float fovBase = 55f;
        [SerializeField] float fovSpeedBoost = 7f;

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

            // Speed reads in the lens: FOV opens up as the ship accelerates.
            if (cam != null && motor != null)
            {
                float s01 = Mathf.Clamp01(motor.CurrentSpeed / motor.MaxSpeed);
                float targetFov = fovBase + fovSpeedBoost * s01 * s01;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-2f * dt));
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
                // to watch — and back off enough to hold both in frame.
                anchor = Vector3.Lerp(poi, shipFlat, 0.38f);
                float back = distance * 0.85f + separation * 0.8f;
                desired = anchor + axis * back + Vector3.up * (height + separation * 0.4f);
                lookPoint = anchor + Vector3.up * lookHeight;
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
            Quaternion desiredRot = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, desiredRot, 1f - Mathf.Exp(-rotationResponse * dt));
        }
    }
}
