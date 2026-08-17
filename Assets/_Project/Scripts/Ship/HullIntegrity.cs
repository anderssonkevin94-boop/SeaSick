using SeaSick.Crew;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.Ship
{
    /// Hull condition, running aground, and repairs. Islands are solid now:
    /// hit one and you lose speed, ride rougher, and terrify the crew.
    [RequireComponent(typeof(ShipMotor))]
    public class HullIntegrity : MonoBehaviour
    {
        [Header("Hull")]
        [Range(0f, 1f)] [SerializeField] float integrity = 1f;
        [SerializeField] float speedPenaltyAtWreck = 0.4f;   // -40% top speed at 0 hull
        [SerializeField] float wallowAtWreck = 0.25f;        // extra roughness at 0 hull

        [Header("Grounding")]
        [SerializeField] float hullMargin = 7f;      // ship half-length-ish clearance
        [SerializeField] float freeImpactSpeed = 2.5f; // gentle nudges cost nothing
        [SerializeField] float damagePerImpactSpeed = 0.035f;
        [SerializeField] float crewShock = 0.12f;      // sickness/anger jolt on a bad hit

        [Header("Repair")]
        [SerializeField] float repairRate = 0.05f;        // hull per second
        [SerializeField] float timberPerHullPoint = 12f;  // timber for a full rebuild

        public float Integrity01 => integrity;
        public float SpeedMultiplier => 1f - speedPenaltyAtWreck * (1f - integrity);
        public float Wallow01 => wallowAtWreck * (1f - integrity);
        public bool NeedsRepair => integrity < 0.995f;
        public float LastImpactTime { get; private set; } = -99f;
        public float LastImpactSpeed { get; private set; }

        ShipMotor motor;
        CrewAgent[] crew;

        void Start()
        {
            motor = GetComponent<ShipMotor>();
            crew = GetComponentsInChildren<CrewAgent>(true);
        }

        void Update()
        {
            var isle = Island.Nearest(transform.position);
            if (isle == null) return;

            Vector3 toShip = transform.position - isle.transform.position;
            toShip.y = 0f;
            float dist = toShip.magnitude;
            float solidRadius = isle.Radius + hullMargin;
            if (dist >= solidRadius || dist < 0.01f) return;

            // Aground: shove the hull back out to the shoreline.
            Vector3 outward = toShip / dist;
            Vector3 pos = transform.position;
            Vector3 fixedPos = isle.transform.position + outward * solidRadius;
            transform.position = new Vector3(fixedPos.x, pos.y, fixedPos.z);

            float closingSpeed = -Vector3.Dot(motor.Velocity, outward);
            motor.KillVelocityAlong(outward);

            if (closingSpeed > freeImpactSpeed && Time.time - LastImpactTime > 0.5f)
            {
                float excess = closingSpeed - freeImpactSpeed;
                integrity = Mathf.Clamp01(integrity - excess * damagePerImpactSpeed);
                LastImpactTime = Time.time;
                LastImpactSpeed = closingSpeed;

                // Everyone gets thrown across the deck.
                float shock = crewShock * Mathf.Clamp01(excess / 8f);
                foreach (var c in crew) if (c != null) c.Jolt(shock);
            }
        }

        /// Returns the timber consumed this frame (0 if nothing to do).
        public float RepairStep(float dt, float timberAvailable)
        {
            if (!NeedsRepair || timberAvailable <= 0f) return 0f;
            float hullWanted = repairRate * dt;
            float timberWanted = hullWanted * timberPerHullPoint;
            if (timberWanted > timberAvailable)
            {
                timberWanted = timberAvailable;
                hullWanted = timberWanted / timberPerHullPoint;
            }
            integrity = Mathf.Clamp01(integrity + hullWanted);
            return timberWanted;
        }

        public void FullRepair() => integrity = 1f;
    }
}
