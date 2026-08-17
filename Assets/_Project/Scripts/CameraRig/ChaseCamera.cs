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

        public Transform Target { get => target; set => target = value; }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;

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
