using UnityEngine;

namespace SeaSick.Ship
{
    /// **The pier lock's physics step** (2026-10-01, the catwalk). Runs after
    /// every other FixedUpdate on the hull -- the strip-theory body, the
    /// paddle, the motor -- so it can see the horizontal force they have
    /// queued for this step (`Rigidbody.GetAccumulatedForce`) and cancel it,
    /// then set her flat velocity to a calm servo toward the berth. Her
    /// vertical force and every torque but yaw are left alone: she heaves,
    /// rolls and pitches exactly as the sea makes her, she just does not
    /// travel or swing.
    ///
    /// Why not a plain velocity servo in `ShipMotor`: measured on Kevin's
    /// save in a 2.7 m sea, `HullFormBody`'s cross-flow drag drags her
    /// toward the wave orbital velocity at up to half a step's worth every
    /// step, and the servo lost -- 15 m of drift along the pier head. Why
    /// not a ConfigurableJoint: its locked axes are in the BODY's frame, so
    /// a locked X/Z tilts with her roll and turns heave into a slide.
    ///
    /// Added by `ShipMotor`; reads its `HoldStation` / `StationLock01` /
    /// `AnchorPoint` / `MooringHeading`.
    [DefaultExecutionOrder(10000)]
    [RequireComponent(typeof(Rigidbody))]
    public class StationKeeper : MonoBehaviour
    {
        /// How fast a position error is closed (1/s) and the speed cap: a
        /// first-order servo never overshoots, and the cap keeps a large
        /// error (a load set her down a metre off) a slow walk in.
        const float Rate = 2.5f;
        const float MaxSpeed = 0.8f;
        /// The servo part's acceleration cap, so the hand-over is not a kick.
        const float MaxAccel = 4f;
        const float YawRate = 2f;
        const float MaxYawSpeed = 0.35f;   // rad/s
        const float MaxYawAccel = 2f;      // rad/s^2

        ShipMotor motor;
        Rigidbody rb;

        void Awake()
        {
            motor = GetComponent<ShipMotor>();
            rb = GetComponent<Rigidbody>();
        }

        void FixedUpdate()
        {
            if (motor == null || rb == null || rb.isKinematic) return;
            float w = motor.StationLock01;
            if (w <= 0f || !motor.Anchored) return;
            float dt = Time.fixedDeltaTime;

            // --- position: hold her flat centre of mass on the berth ------
            // Where her CoM lies when her origin is on the berth, at her
            // current heading (yaw only -- roll must not move the target).
            Quaternion yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            Vector3 comTarget = motor.AnchorPoint + Flat(yaw * rb.centerOfMass);
            Vector3 err = Flat(comTarget - rb.worldCenterOfMass);
            Vector3 want = Vector3.ClampMagnitude(err * Rate, MaxSpeed);
            Vector3 servo = Vector3.ClampMagnitude(want - Flat(rb.linearVelocity), MaxAccel * dt);
            // What the rest of the hull is about to do to her flat velocity
            // this step, undone.
            Vector3 queued = Flat(rb.GetAccumulatedForce()) * (dt / rb.mass);
            rb.AddForce((servo - queued) * w, ForceMode.VelocityChange);

            // --- heading: hold her on the berth heading --------------------
            if (motor.MooringHeading.HasValue)
            {
                float errDeg = Mathf.DeltaAngle(transform.eulerAngles.y, motor.MooringHeading.Value);
                float wantYaw = Mathf.Clamp(errDeg * Mathf.Deg2Rad * YawRate, -MaxYawSpeed, MaxYawSpeed);
                float dYaw = Mathf.Clamp(wantYaw - rb.angularVelocity.y, -MaxYawAccel * dt, MaxYawAccel * dt);
                // The yaw the queued torques would add this step, undone.
                Vector3 torque = rb.GetAccumulatedTorque();
                Quaternion frame = rb.rotation * rb.inertiaTensorRotation;
                Vector3 local = Quaternion.Inverse(frame) * torque;
                Vector3 I = rb.inertiaTensor;
                Vector3 alphaLocal = new Vector3(
                    I.x > 1e-6f ? local.x / I.x : 0f,
                    I.y > 1e-6f ? local.y / I.y : 0f,
                    I.z > 1e-6f ? local.z / I.z : 0f);
                float queuedYaw = (frame * alphaLocal).y * dt;
                rb.AddTorque(Vector3.up * ((dYaw - queuedYaw) * w), ForceMode.VelocityChange);
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
