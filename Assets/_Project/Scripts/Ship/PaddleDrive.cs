using UnityEngine;

namespace SeaSick.Ship
{
    /// The paddle steamer's moving parts: two wheels that turn her, and the
    /// helm wheel in the helmsman's hands.
    ///
    /// The wheels are differential. Both turn together for way, and the helm
    /// adds an opposite term — outer wheel faster, inner slower and reversing
    /// at full helm — so at zero throttle with the helm hard over they
    /// counter-rotate and she spins on the spot. This boat has no rudder, so
    /// the wheels and the helm wheel are the only visible steering cue and
    /// they have to read honestly.
    ///
    /// The yaw model in ShipMotor stays authoritative (it is tuned, and
    /// handling should not regress to make a visual honest). What this adds is
    /// a low-speed yaw assist that exists only while the wheels are actually
    /// differential, which is what makes spinning on the spot a real
    /// manoeuvre rather than an animation.
    [RequireComponent(typeof(ShipMotor))]
    public class PaddleDrive : MonoBehaviour
    {
        [Header("Wheels")]
        [SerializeField] Transform portWheel;
        [SerializeField] Transform starboardWheel;
        [Tooltip("Wheel radius in metres — sets how fast they turn for a given speed through the water.")]
        [SerializeField] float wheelRadius = 1.27f;
        [Tooltip("Spin axis in each wheel's own local space.")]
        [SerializeField] Vector3 wheelAxis = Vector3.forward;
        [Tooltip("Flip if the wheels appear to drive her backwards.")]
        [SerializeField] bool invertWheels;

        [Header("Differential")]
        [Tooltip("Rate difference between the wheels at full helm, as a fraction of the wheel rate at full ahead.")]
        [SerializeField, Range(0f, 1.5f)] float differential = 0.85f;
        [Tooltip("Extra yaw (deg/s) at full helm with the throttle shut — the spin-on-the-spot manoeuvre.")]
        [SerializeField] float spinAssist = 12f;
        [Tooltip("Wheel rate (rad/s) at full throttle standing still — the slip that gets her moving.")]
        [SerializeField] float stallRate = 3.2f;

        [Header("Helm wheel")]
        [SerializeField] Transform helmWheel;
        [Tooltip("Spin axis in the helm wheel's own local space.")]
        [SerializeField] Vector3 helmAxis = Vector3.right;
        [Tooltip("Wheel rotation at full helm, degrees. 450 = two and a half turns lock to lock.")]
        [SerializeField] float helmVisualAngle = 450f;
        [Tooltip("How fast the wheel follows the helm order. Lower feels heavier in the hands.")]
        [SerializeField] float helmFollowRate = 3.5f;

        ShipMotor motor;
        Rigidbody rb;
        float portAngle, stbdAngle, helmAngle;

        /// Wheel rates in rad/s, for spray, audio and anything else that wants
        /// to know how hard she is being driven.
        public float PortRate { get; private set; }
        public float StarboardRate { get; private set; }
        /// How hard the wheels are working against each other, 0..1.
        public float Differential01 { get; private set; }
        /// Exposed so probes read the real radius instead of a copy that goes
        /// stale the moment the boat is rescaled.
        public float WheelRadius { get { return wheelRadius; } }

        void Awake()
        {
            motor = GetComponent<ShipMotor>();
            rb = GetComponent<Rigidbody>();
        }

        void Update()
        {
            if (motor == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float helm = Mathf.Clamp(motor.Rudder, -1f, 1f);

            // Common rate: the faster of rolling with the water and what the
            // throttle is asking for, so the wheels slip while she builds way
            // instead of pretending she is already at speed.
            float forwardWay = Vector3.Dot(
                rb != null ? rb.linearVelocity : Vector3.zero, transform.forward);
            float rollRate = forwardWay / Mathf.Max(0.05f, wheelRadius);
            float demand = motor.Anchored ? 0f : motor.SailSetting * stallRate;
            float common = Mathf.Abs(demand) > Mathf.Abs(rollRate) ? demand : rollRate;

            // Differential: to turn to starboard the PORT wheel drives harder.
            float diff = helm * differential * stallRate;
            PortRate = common + diff;
            StarboardRate = common - diff;
            Differential01 = Mathf.Clamp01(Mathf.Abs(diff) / Mathf.Max(0.01f, stallRate));

            float sign = invertWheels ? -1f : 1f;
            portAngle += PortRate * sign * Mathf.Rad2Deg * dt;
            stbdAngle += StarboardRate * sign * Mathf.Rad2Deg * dt;
            portAngle = Mathf.Repeat(portAngle, 360f);
            stbdAngle = Mathf.Repeat(stbdAngle, 360f);

            if (portWheel != null) portWheel.localRotation = Quaternion.AngleAxis(portAngle, wheelAxis);
            if (starboardWheel != null) starboardWheel.localRotation = Quaternion.AngleAxis(stbdAngle, wheelAxis);

            // The helm wheel lags the order, so it reads as something being
            // turned by hand rather than a dial snapping to a value.
            float target = -helm * helmVisualAngle;
            helmAngle = Mathf.Lerp(helmAngle, target, 1f - Mathf.Exp(-helmFollowRate * dt));
            if (helmWheel != null) helmWheel.localRotation = Quaternion.AngleAxis(helmAngle, helmAxis);
        }

        void FixedUpdate()
        {
            // Spin on the spot: only while the wheels really are opposed and
            // there is little way on. At speed the tuned turn rate rules.
            if (rb == null || motor == null || motor.Anchored) return;
            float slow = 1f - Mathf.Clamp01(motor.CurrentSpeed / 3f);
            if (slow <= 0f || Differential01 <= 0f) return;
            float assist = Mathf.Sign(motor.Rudder) * spinAssist * Differential01 * slow;
            var av = rb.angularVelocity;
            av.y += assist * Mathf.Deg2Rad * Time.fixedDeltaTime;
            rb.angularVelocity = av;
        }
    }
}
