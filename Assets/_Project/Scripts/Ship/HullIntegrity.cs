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
        [SerializeField] float crewShock = 0.06f;      // permanent sickness jolt on a bad hit
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
        [SerializeField] float shotShock = 0.05f;

        [Header("Repair")]
        [SerializeField] float repairRate = 0.05f;        // hull per second
        [SerializeField] float timberPerHullPoint = 12f;  // timber for a full rebuild

        public float Integrity01 => integrity;
        public float SpeedMultiplier => 1f - speedPenaltyAtWreck * (1f - integrity);
        /// Broken timber and water aboard both make her lie over and roll.
        /// The bilge's share is capped inside Bilge itself, so a flooded ship
        /// is crippled rather than doomed.
        public float Wallow01
        {
            get
            {
                if (bilge == null) bilge = GetComponent<Bilge>();
                float water = bilge != null ? bilge.WallowFromWater : 0f;
                return wallowAtWreck * (1f - integrity) + water;
            }
        }

        Bilge bilge;
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
            var reef = Reef.Nearest(transform.position);

            Vector3 obstaclePos = Vector3.zero;
            float obstacleRadius = 0f;
            float damageScale = 1f;
            bool intruding = false;

            // **Land is the height field, not a radius.**
            //
            // This used to be `Island.RadiusToward` — a 46-sector radial
            // profile that was the hull's ONLY collision boundary. It cannot
            // describe a bay or a lobe: measured against the real waterline
            // on 360 bearings, it was out by a mean of 1–23 m and a worst of
            // **934 m out to sea** on the biggest island, with stretches of
            // real land **690 m inside it** that you could sail straight
            // through. Invisible walls and phantom water.
            //
            // The height field has no such problem: it is what the coastline
            // is drawn FROM, and it agrees with the rendered mesh to 7 mm.
            // The push-off direction comes from its gradient — downhill is
            // deeper water — which needs no centre and so does not care what
            // shape the island is.
            //
            // Skipped while she is anchored: deliberately putting her ashore
            // is not running aground, and the shore party moors her 11 m off
            // a beach where the water is inches deep.
            if (!motor.Anchored && GroundedOnLand(out Vector3 landOut, out Vector3 landFix))
            {
                Aground(landOut, landFix, 1f);
                return;
            }

            if (reef != null && Intrudes(reef.transform.position, reef.Radius))
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

            Aground(toShip / dist, obstaclePos + (toShip / dist) * solidRadius, damageScale);
        }

        static float Ground(Vector3 p)
            => Island.TerrainHeight != null ? Island.TerrainHeight(p.x, p.z) : -999f;

        /// Is there ground under her, and which way is deep water?
        ///
        /// `outward` is straight downhill on the height field. `fixedPos` is
        /// the first place along it with enough water under the keel, found
        /// by walking out — a single step would leave her still aground on
        /// anything steeper than the step size.
        bool GroundedOnLand(out Vector3 outward, out Vector3 fixedPos)
        {
            outward = Vector3.zero;
            fixedPos = transform.position;
            if (Island.TerrainHeight == null) return false;

            Vector3 p = transform.position;
            float need = -groundingDraft;
            if (Ground(p) <= need) return false;

            const float E = 4f;
            float gx = Ground(p + Vector3.right * E) - Ground(p - Vector3.right * E);
            float gz = Ground(p + Vector3.forward * E) - Ground(p - Vector3.forward * E);
            Vector3 down = new Vector3(-gx, 0f, -gz);
            if (down.sqrMagnitude < 1e-6f)
            {
                // Flat shallows: no gradient to follow, so fall back to away
                // from the nearest island's centre.
                var isle = Island.Nearest(p);
                down = isle != null ? p - isle.transform.position : Vector3.forward;
                down.y = 0f;
                if (down.sqrMagnitude < 1e-6f) down = Vector3.forward;
            }
            outward = down.normalized;

            Vector3 q = p;
            for (int i = 0; i < 24; i++)
            {
                q += outward * 4f;
                if (Ground(q) <= need) break;
            }
            fixedPos = q;
            return true;
        }

        /// Shove her clear, kill the closing speed, and bill the hull for it.
        void Aground(Vector3 outward, Vector3 fixedPos, float damageScale)
        {
            Vector3 pos = transform.position;
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

        /// Metres of water she needs under her before she touches.
        ///
        /// **Deliberately tiny — it is a waterline, not a draft.** At a
        /// realistic 1.0 m the depth contour she is stopped at sits a mean of
        /// 55 m off the home island's visible shore and 798 m off it at
        /// worst, because these islands have broad shallow aprons. That is
        /// honest water and a dishonest game: the landing prompt only reaches
        /// 30 m past the shoreline, so she would be walled out of her own
        /// beaches on most bearings — the same symptom as the bug this
        /// replaced, arrived at from the opposite direction.
        ///
        /// The defect actually being fixed is sailing THROUGH LAND and
        /// hitting walls in deep water. Stopping her essentially at the
        /// waterline fixes both and leaves the shallows navigable, which is
        /// what a shallow-draught paddle steamer should do anyway.
        ///
        /// **A const, not a `[SerializeField]`.** As a serialized field it
        /// never took the value written here: the scene has no entry for it,
        /// yet the component kept reporting the old 1.0 through recompiles
        /// AND through fresh play sessions, because Unity restores a
        /// component's serialized state over a changed initializer. That is
        /// the same trap `SetupStormSky.ResetToCodeDefaults` exists for, and
        /// it cost two byte-identical measurement runs that I nearly
        /// believed. Nothing needs to tune this per scene, so the safest
        /// thing is for it not to be tunable per scene.
        const float groundingDraft = 0.3f;

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
        /// Direct hull loss as a fraction, for things that are not shot: a
        /// mountain sea, rocks. **`TakeShot`'s `amount` is a multiplier on
        /// `damagePerShot`, not a fraction** — passing 0.30 there costs about
        /// 2.5% hull, not 30%, which is exactly the trap this method exists to
        /// avoid. No crew jolt: the caller owns that, or it gets applied twice.
        public void Batter(Vector3 point, float fraction)
        {
            integrity = Mathf.Clamp01(integrity - Mathf.Max(0f, fraction));
            LastImpactTime = Time.time;
            Splinters(point);
        }

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
