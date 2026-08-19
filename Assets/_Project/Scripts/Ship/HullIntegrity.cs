using SeaSick.Combat;
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
        [SerializeField] float reefDamageScale = 1f; // a full-speed reef hit costs ~37% hull
        // Another hull gives where rock does not, so ramming costs less than
        // a reef — but it still costs, and it still stops you dead.
        [SerializeField] float ramDamageScale = 0.55f;
        [SerializeField] float enemyHullRadius = 9f;

        [Header("Under fire")]
        // ~12 hits to wreck a hull. Deliberately slow: the damage is not what
        // ends a voyage — the wallow it adds and the crew it terrifies are,
        // and those compound long before the timber runs out.
        [SerializeField] float damagePerShot = 0.085f;
        [SerializeField] float shotShock = 0.10f;

        [Header("Repair")]
        [SerializeField] float repairRate = 0.05f;        // hull per second
        [SerializeField] float timberPerHullPoint = 12f;  // timber for a full rebuild

        public float Integrity01 => integrity;
        public float SpeedMultiplier => 1f - speedPenaltyAtWreck * (1f - integrity);
        public float Wallow01 => wallowAtWreck * (1f - integrity);
        public bool NeedsRepair => integrity < 0.995f;
        public float LastImpactTime { get; private set; } = -99f;
        public float LastImpactSpeed { get; private set; }
        public float LastShotTime { get; private set; } = -99f;

        ShipMotor motor;
        CrewAgent[] crew;

        void Start()
        {
            motor = GetComponent<ShipMotor>();
            crew = GetComponentsInChildren<CrewAgent>(true);
        }

        void Update()
        {
            // Whichever obstacle we're actually inside — island or reef.
            var isle = Island.Nearest(transform.position);
            var reef = Reef.Nearest(transform.position);

            Vector3 obstaclePos = Vector3.zero;
            float obstacleRadius = 0f;
            float damageScale = 1f;
            bool intruding = false;

            // Islands aren't circles any more — use the shoreline distance on
            // the bearing the ship is actually approaching from.
            if (isle != null)
            {
                float shoreRadius = isle.RadiusToward(transform.position);
                if (Intrudes(isle.transform.position, shoreRadius))
                {
                    obstaclePos = isle.transform.position;
                    obstacleRadius = shoreRadius;
                    intruding = true;
                }
            }
            if (!intruding && reef != null && Intrudes(reef.transform.position, reef.Radius))
            {
                obstaclePos = reef.transform.position;
                obstacleRadius = reef.Radius;
                damageScale = reefDamageScale; // jagged rock bites harder
                intruding = true;
            }
            // Enemy hulls are solid as well. Without this the player sails
            // clean through a raider, which reads as the raider having no
            // hit box at all.
            if (!intruding)
                foreach (var raider in EnemyShip.All)
                {
                    if (raider == null || !raider.Alive) continue;
                    if (!Intrudes(raider.transform.position, enemyHullRadius)) continue;
                    obstaclePos = raider.transform.position;
                    obstacleRadius = enemyHullRadius;
                    damageScale = ramDamageScale;
                    intruding = true;
                    break;
                }

            if (!intruding) return;

            Vector3 toShip = transform.position - obstaclePos;
            toShip.y = 0f;
            float dist = toShip.magnitude;
            float solidRadius = obstacleRadius + hullMargin;
            if (dist < 0.01f) return;

            // Aground: shove the hull back out to the shoreline.
            Vector3 outward = toShip / dist;
            Vector3 pos = transform.position;
            Vector3 fixedPos = obstaclePos + outward * solidRadius;
            transform.position = new Vector3(fixedPos.x, pos.y, fixedPos.z);

            float closingSpeed = -Vector3.Dot(motor.Velocity, outward);
            motor.KillVelocityAlong(outward);

            if (closingSpeed > freeImpactSpeed && Time.time - LastImpactTime > 0.5f)
            {
                float excess = closingSpeed - freeImpactSpeed;
                integrity = Mathf.Clamp01(integrity - excess * damagePerImpactSpeed * damageScale);
                LastImpactTime = Time.time;
                LastImpactSpeed = closingSpeed;

                // Everyone gets thrown across the deck.
                float shock = crewShock * Mathf.Clamp01(excess / 8f);
                foreach (var c in crew) if (c != null) c.Jolt(shock);
            }
        }

        bool Intrudes(Vector3 centre, float radius)
        {
            Vector3 d = transform.position - centre;
            d.y = 0f;
            return d.sqrMagnitude < (radius + hullMargin) * (radius + hullMargin);
        }

        /// A round shot comes aboard. This is the whole point of arming the
        /// raiders: the damage lands on HullIntegrity, which already costs
        /// speed and adds wallow, and the shock lands on the crew, who already
        /// get sicker and angrier for it. A fight therefore spends the voyage
        /// clock rather than running beside it.
        public void TakeShot(Vector3 point, float amount)
        {
            integrity = Mathf.Clamp01(integrity - damagePerShot * Mathf.Max(0.01f, amount));
            LastShotTime = Time.time;
            LastImpactTime = Time.time;

            if (crew == null) crew = GetComponentsInChildren<CrewAgent>(true);
            foreach (var c in crew) if (c != null) c.Jolt(shotShock);

            Splinters(point);
        }

        /// Timber off the rail where the shot went in.
        static void Splinters(Vector3 at)
        {
            var go = new GameObject("HullHit");
            go.transform.position = at;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 12f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            main.gravityModifier = 2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            main.playOnAwake = false;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.62f, 0.48f, 0.30f), new Color(0.30f, 0.24f, 0.18f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;

            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial =
                new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));

            ps.Emit(28);
            Destroy(go, 2.5f);
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
