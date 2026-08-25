using UnityEngine;

namespace SeaSick.Ship
{
    /// A lantern hanging on its chain. Driven as a real damped pendulum off
    /// the hull's own acceleration and attitude rather than a sine wave, so it
    /// lurches when she lurches, hangs steady in a calm, and swings across
    /// rather than along when she rolls. In a game whose first pillar is how
    /// the sea feels, the small hanging things are worth doing properly —
    /// they are the readout the player watches without noticing.
    ///
    /// The pivot is the chain's top; the lantern is the child that swings.
    public class LanternSwing : MonoBehaviour
    {
        [Tooltip("Pendulum length, metres. Longer swings slower.")]
        [SerializeField] float chainLength = 0.55f;
        [Tooltip("How quickly a swing dies out. 0 = never, 1 = dead.")]
        [SerializeField, Range(0f, 1f)] float damping = 0.14f;
        [Tooltip("Largest swing either way, degrees — the chain does not wrap.")]
        [SerializeField] float maxAngle = 34f;
        [Tooltip("How hard the hull's acceleration drives it.")]
        [SerializeField] float drive = 1f;

        Rigidbody hull;
        Transform hullT;
        Vector3 lastHullVel;
        Quaternion rest = Quaternion.identity;

        // Two independent pendulums: across the ship, and fore and aft.
        float angleX, velX;   // swing fore/aft (rotation about local X)
        float angleZ, velZ;   // swing athwartships (rotation about local Z)

        void Start()
        {
            hull = GetComponentInParent<Rigidbody>();
            hullT = hull != null ? hull.transform : transform.root;
            // Swing FROM the rest pose. Writing localRotation outright wiped
            // the pivot's orientation and left both lanterns hanging sideways.
            rest = transform.localRotation;
            if (hull != null) lastHullVel = hull.linearVelocity;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || hullT == null) return;

            // The force the lantern feels is gravity plus the hull's own
            // acceleration, expressed in the hull's frame.
            Vector3 accelWorld = Vector3.zero;
            if (hull != null)
            {
                accelWorld = (hull.linearVelocity - lastHullVel) / dt;
                lastHullVel = hull.linearVelocity;
            }
            Vector3 apparent = Vector3.down * Physics.gravity.magnitude - accelWorld * drive;
            Vector3 local = hullT.InverseTransformDirection(apparent);

            // Where the chain wants to point, in the hull's frame. When she
            // heels, "down" is no longer down the mast — that tilt is the
            // whole effect and it comes out of this transform for free.
            float restX = Mathf.Atan2(local.z, -local.y) * Mathf.Rad2Deg;
            float restZ = -Mathf.Atan2(local.x, -local.y) * Mathf.Rad2Deg;

            float omega = Mathf.Sqrt(Physics.gravity.magnitude / Mathf.Max(0.05f, chainLength));
            Step(ref angleX, ref velX, restX, omega, dt);
            Step(ref angleZ, ref velZ, restZ, omega, dt);

            transform.localRotation = rest * Quaternion.Euler(angleX, 0f, angleZ);
        }

        void Step(ref float angle, ref float vel, float rest, float omega, float dt)
        {
            float accel = -omega * omega * (angle - rest) - 2f * damping * omega * vel;
            vel += accel * dt;
            angle += vel * dt;
            if (angle > maxAngle) { angle = maxAngle; vel = Mathf.Min(vel, 0f); }
            else if (angle < -maxAngle) { angle = -maxAngle; vel = Mathf.Max(vel, 0f); }
        }
    }
}
