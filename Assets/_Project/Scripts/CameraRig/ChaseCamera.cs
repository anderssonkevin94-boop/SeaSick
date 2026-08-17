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

            Vector3 flatForward = target.forward;
            flatForward.y = 0f;
            flatForward = flatForward.sqrMagnitude < 0.001f ? Vector3.forward : flatForward.normalized;

            Vector3 anchor = new Vector3(target.position.x, 0f, target.position.z);
            Vector3 desired = anchor - flatForward * distance + Vector3.up * height;
            transform.position = Vector3.Lerp(
                transform.position, desired, 1f - Mathf.Exp(-positionResponse * dt));

            Vector3 lookPoint = anchor + flatForward * lookAhead + Vector3.up * lookHeight;
            Quaternion desiredRot = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, desiredRot, 1f - Mathf.Exp(-rotationResponse * dt));
        }
    }
}
