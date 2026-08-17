using UnityEngine;

namespace SeaSick.Ship
{
    /// Measures how roughly the hull is moving — THE core number of the game.
    /// Roughness (0..1) will drive crew seasickness rate: smooth sailing keeps
    /// it low, slamming through chop with full canvas spikes it. Tune the
    /// normalization constants here until "sailing well" visibly pays off.
    public class SmoothnessMeter : MonoBehaviour
    {
        [Header("What counts as rough (normalization ceilings)")]
        // Ceilings are set so ordinary bobbing reads as CALM (~0.15) and only
        // real punishment — swell, slamming, hard turns — climbs toward 1.
        // Keeping ambient low is what makes good sailing feel rewarded.
        [SerializeField] float heaveRateCeiling = 2.7f;      // m/s of vertical motion
        [SerializeField] float pitchRateCeiling = 27f;       // deg/s
        [SerializeField] float rollRateCeiling = 30f;        // deg/s
        [SerializeField] float lateralAccelCeiling = 3.2f;   // m/s^2, centripetal (hard turns)

        [Header("Blend weights")]
        [SerializeField] float verticalWeight = 0.35f;
        [SerializeField] float pitchWeight = 0.25f;
        [SerializeField] float rollWeight = 0.20f;
        [SerializeField] float lateralWeight = 0.20f;
        [SerializeField] float gustWeight = 0.15f; // gusts are fast but bumpy

        [SerializeField] float smoothingHalflife = 1.2f; // seconds; EMA over the raw signal

        /// 0 = glassy calm, 1 = washing machine. Smoothed; safe to drive gameplay.
        public float Roughness01 { get; private set; }
        /// Convenience inverse for scoring/UI.
        public float Smoothness01 => 1f - Roughness01;

        ShipMotor motor;
        float prevY;
        float prevPitch;
        float prevRoll;
        float prevYaw;
        Vector3 prevPos;
        bool primed;

        void OnEnable() { primed = false; motor = GetComponent<ShipMotor>(); }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 pos = transform.position;
            float y = pos.y;
            float pitch = NormalizeAngle(transform.eulerAngles.x);
            float roll = NormalizeAngle(transform.eulerAngles.z);
            float yaw = transform.eulerAngles.y;

            if (!primed)
            {
                prevY = y; prevPitch = pitch; prevRoll = roll;
                prevYaw = yaw; prevPos = pos;
                primed = true;
                return;
            }

            // Heave rate, not acceleration: single derivative is numerically
            // stable, and sustained vertical motion is what churns stomachs.
            float velY = (y - prevY) / dt;
            float pitchRate = Mathf.DeltaAngle(prevPitch, pitch) / dt;
            float rollRate = Mathf.DeltaAngle(prevRoll, roll) / dt;

            // Centripetal feel of a turn: |v| * |yaw rate| — what standing crew
            // actually experience when the deck carves sideways under them.
            Vector3 horizontalDelta = pos - prevPos;
            horizontalDelta.y = 0f;
            float horizontalSpeed = horizontalDelta.magnitude / dt;
            float yawRateRad = Mathf.Abs(Mathf.DeltaAngle(prevYaw, yaw)) * Mathf.Deg2Rad / dt;
            float lateralAcc = horizontalSpeed * yawRateRad;

            prevY = y; prevPitch = pitch; prevRoll = roll;
            prevYaw = yaw; prevPos = pos;

            float gust = motor != null ? motor.GustFactor01 : 0f;
            float raw =
                verticalWeight * Mathf.Clamp01(Mathf.Abs(velY) / heaveRateCeiling) +
                pitchWeight * Mathf.Clamp01(Mathf.Abs(pitchRate) / pitchRateCeiling) +
                rollWeight * Mathf.Clamp01(Mathf.Abs(rollRate) / rollRateCeiling) +
                lateralWeight * Mathf.Clamp01(lateralAcc / lateralAccelCeiling) +
                gustWeight * gust;

            float blend = 1f - Mathf.Exp(-(0.6931f / smoothingHalflife) * dt);
            Roughness01 = Mathf.Lerp(Roughness01, Mathf.Clamp01(raw), blend);
        }

        static float NormalizeAngle(float degrees) =>
            degrees > 180f ? degrees - 360f : degrees;
    }
}
