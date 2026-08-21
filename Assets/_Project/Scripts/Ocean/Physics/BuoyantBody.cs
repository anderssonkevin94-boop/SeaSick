using UnityEngine;

namespace SeaSick.Ocean
{
    /// Probe-driven rigidbody buoyancy. Per physics step (fed by
    /// OceanPhysicsDriver from the shared SampleBatch): each probe applies an
    /// upward force proportional to its submersion and a linear+quadratic drag
    /// against the water's orbital velocity, at its position — heave, pitch,
    /// roll, surf and broach all emerge from the probe layout. Angular drag
    /// scales with submersion so a hull settles instead of ringing.
    [RequireComponent(typeof(Rigidbody), typeof(BuoyancyProbeSet))]
    public class BuoyantBody : MonoBehaviour
    {
        [SerializeField] float waterDensity = 1025f;
        [Tooltip("Displaced volume at full submersion, m^3. Sets total buoyant capacity; the body floats where share-weighted submersion x this x rho x g = weight.")]
        [SerializeField] float totalVolume = 6.5f;
        [SerializeField] float linearDrag = 28000f;
        [SerializeField] float quadraticDrag = 3000f;
        // A hull's water drag is wildly anisotropic: the full coefficient
        // vertically is what couples her to the swell, but applied fore-aft it
        // parks the ship (measured: 1.6 m/s under full sail). Forward motion
        // belongs to the propulsion servo; the keel owns most of lateral.
        [SerializeField] float forwardDragFactor = 0.005f;
        [SerializeField] float lateralDragFactor = 0.25f;
        [SerializeField] float angularDragTorque = 30000f;
        // The old model saturated at full probe submersion: driven any deeper,
        // the hull gained ZERO extra lift (net reserve ~6.5 m/s^2 on the
        // sloop), so charging a mountainous face buried her to the sails.
        // Reserve models the sealed hull above the waterline: past full
        // submersion the restoring force keeps growing with depth, so the
        // deck shoulders out of a wave instead of plowing under. Zero effect
        // at the normal float equilibrium.
        [Tooltip("Extra buoyant capacity per metre past full probe submersion, x that probe's capacity. Linear from the onset on purpose: the shallow band (0-0.5 m over) is what raises her ride height in a storm, and that dynamic freeboard — not the deep response — is what keeps the deck dry (softening the onset measured 4-12% of the run with rails under vs 0%).")]
        [SerializeField] float reservePerMetre = 1.5f;
        [Tooltip("Cap on the reserve multiple, x probe capacity.")]
        [SerializeField] float maxReserve = 2.5f;
        [Tooltip("Extra forward drag factor per unit of PLOW reserve. A deeply buried bow is plowing a wall of water: no plausible lift out-muscles 4 t of way, but shedding the way lets the lift win. This is the wave-slam feel.")]
        [SerializeField] float plowDragFactor = 0.15f;
        [Tooltip("Metres of bury past full probe submersion before plow drag starts — its own onset, separate from the lift's. The lift must ramp from zero (that band IS the storm freeboard), but a keel-line probe is ~96% submerged just floating: the stem sits 4 cm from full submersion at rest, so a shared onset put the brake on in flat water (measured: reserve active on 38% of steps at anchor in a calm) and cost 95% of her distance made good in a lively sea.")]
        [SerializeField] float plowOnset = 0.6f;
        [Tooltip("Forward way (m/s through the water) below which plow drag stops acting; it fades in over the same span again above it. Plow exists to stop a hull CHARGING into a wall of water, and that is a fast-ship problem — but keyed on depth alone it kept pulling once she was already stopped, and in mountainous seas that pinned her at 1.4 m/s making 11 m in 40 s (against 381 m with plow off). The floor lets a slam take her from 20 down to single figures and no further, which is the cost the design wants without the handbrake it did not.")]
        [SerializeField] float plowSpeedFloor = 4.5f;
        [Tooltip("Ceiling on total plow deceleration, m/s^2. Propulsion is a rate-limited servo (ShipMotor.acceleration, 2.6 m/s^2), so an uncapped brake wins outright and never gives the sail a way back — and because plow is applied at the stem, below and forward of the CoM, it pitches her bow-down into more bury and runs away. At 4.0 a slam sheds the intended ~2 m/s over half a second and she works back up.")]
        [SerializeField] float maxPlowDecel = 6f;
        [Tooltip("Fraction of reserve LIFT kept while the probe rises relative to the water (ramp over 1 m/s). Only the lift bleeds — reserve damping and plow drag stay at full strength, so this is an asymmetric shock absorber: full catch on the way in, no spring-return pogo on the way out (symmetric lift measured 6-9 s calm settle vs 1.75 s baseline).")]
        [SerializeField, Range(0f, 1f)] float reserveUpwardKeep = 0.3f;
        [Tooltip("Explicit inertia box (m) — a colliderless Rigidbody defaults to unit inertia and spins like a coin.")]
        [SerializeField] Vector3 inertiaBoxDims = new Vector3(4.4f, 3f, 13f);
        [SerializeField] Vector3 centreOfMass = new Vector3(0f, -0.6f, 0f);

        Rigidbody rb;
        BuoyancyProbeSet probeSet;
        // Plow is clamped against a total, so it is applied in a second pass.
        // Preallocated: this runs every FixedUpdate.
        Vector3[] plowScratch;
        Vector3[] plowAt;

        /// Share-weighted submersion this step, 0..1.
        public float Submersion { get; private set; }
        /// Mean water height over the probes this step.
        public float MeanWaterHeight { get; private set; }
        /// Net wave force applied this step (buoyancy horizontal + drag), N.
        public Vector3 WaveForce { get; private set; }
        /// Deepest green water over any rail probe this step, metres (can be < 0).
        public float MaxRailImmersion { get; private set; }
        /// Debug decomposition of this step's drag (world space, N).
        public Vector3 DebugDragForward { get; private set; }
        public Vector3 DebugDragLateral { get; private set; }
        public Vector3 DebugDragVertical { get; private set; }
        /// The plow-drag share of DebugDragForward this step (world space, N),
        /// and the deepest reserve any probe reached. Diagnostic only.
        public Vector3 DebugPlowForward { get; private set; }
        public float DebugMaxReserve { get; private set; }
        /// Live tuning surface for A/B probes — set plow to 0 to isolate it.
        public float PlowDragFactor { get => plowDragFactor; set => plowDragFactor = value; }
        public float ReservePerMetre { get => reservePerMetre; set => reservePerMetre = value; }
        public float PlowOnset { get => plowOnset; set => plowOnset = value; }
        public float MaxPlowDecel { get => maxPlowDecel; set => maxPlowDecel = value; }
        /// Extra metres the body should sit below its light waterline (cargo,
        /// bilge water). Lowers the float equilibrium without touching mass.
        public float SeatOffset { get; set; }
        /// Macroscopic water motion (drift/current) added to the orbital
        /// velocity as the drag reference — the hull is carried, not shoved.
        public Vector3 AmbientFlow { get; set; }
        public Rigidbody Body => rb;
        public BuoyancyProbeSet Probes => probeSet;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            probeSet = GetComponent<BuoyancyProbeSet>();
            rb.useGravity = true;
            rb.centerOfMass = centreOfMass;
            var d = inertiaBoxDims;
            float m = rb.mass / 12f;
            rb.inertiaTensor = new Vector3(
                m * (d.y * d.y + d.z * d.z),
                m * (d.x * d.x + d.z * d.z),
                m * (d.x * d.x + d.y * d.y));
            rb.inertiaTensorRotation = Quaternion.identity;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        void OnEnable() => OceanPhysicsDriver.Register(this);
        void OnDisable() => OceanPhysicsDriver.Unregister(this);

        public void FillQueries(System.Span<Vector3> dst)
        {
            var probes = probeSet.Probes;
            for (int i = 0; i < probes.Length; i++)
                dst[i] = transform.TransformPoint(probes[i].localPosition);
        }

        /// Called by OceanPhysicsDriver with this body's slice of the batch.
        public void ApplyForces(System.ReadOnlySpan<OceanSample> samples)
        {
            var probes = probeSet.Probes;
            float g = Physics.gravity.magnitude;
            float subSum = 0f, hSum = 0f;
            float railWorst = float.MinValue;
            Vector3 waveForce = Vector3.zero;
            Vector3 dbgFwd = Vector3.zero, dbgLat = Vector3.zero, dbgVert = Vector3.zero;
            float reserveWorst = 0f;
            Vector3 plowTotal = Vector3.zero;

            // Plow fades out as she loses way: see plowSpeedFloor.
            Vector3 hullFwd = transform.forward;
            hullFwd.y = 0f;
            hullFwd = hullFwd.sqrMagnitude > 1e-4f ? hullFwd.normalized : Vector3.forward;
            float forwardWay = Vector3.Dot(rb.linearVelocity - AmbientFlow, hullFwd);
            float wayFactor = Mathf.Clamp01(
                (forwardWay - plowSpeedFloor) / Mathf.Max(0.01f, plowSpeedFloor));

            if (plowScratch == null || plowScratch.Length != probes.Length)
            {
                plowScratch = new Vector3[probes.Length];
                plowAt = new Vector3[probes.Length];
            }
            // Cleared every step: a probe that leaves the water skips the body
            // of the loop, and a stale plow vector would keep braking her.
            for (int i = 0; i < probes.Length; i++) plowScratch[i] = Vector3.zero;

            for (int i = 0; i < probes.Length; i++)
            {
                Vector3 world = transform.TransformPoint(probes[i].localPosition);
                float depth = samples[i].height - (world.y + SeatOffset);
                float sub = Mathf.Clamp01(depth / probes[i].radius + 0.5f);
                subSum += sub * probes[i].volumeShare;
                hSum += samples[i].height;
                if (probes[i].isRail)
                    railWorst = Mathf.Max(railWorst, samples[i].height - world.y);

                if (sub <= 0f) continue;

                Vector3 vWater = AmbientFlow + new Vector3(
                    samples[i].velocity.x, samples[i].velocity.y, samples[i].velocity.z);
                Vector3 vRel = rb.GetPointVelocity(world) - vWater;

                // Metres buried past full submersion buy reserve. Linear
                // from the onset on purpose: the shallow band is what raises
                // her storm ride height, and every onset softening tried
                // (deadband, quadratic, knee) put the rails under 4-13% of
                // the head-seas gate vs 0% for this shape. Lift and damping
                // bleed on upward relative motion (keeping damping raw
                // measured 8.7 s calm settle). Plow drag reads the SAME depth
                // through its own, later onset below — it wants "the bow is
                // buried in a wall of water", not "she is floating".
                float over = Mathf.Max(0f, depth - probes[i].radius * 0.5f);
                float reserve = Mathf.Min(maxReserve, reservePerMetre * over);
                float reserveLift = reserve *
                    Mathf.Lerp(1f, reserveUpwardKeep, Mathf.Clamp01(vRel.y));
                float effSub = sub + reserveLift;
                // Plow has its own, later onset. Lift must ramp from the first
                // millimetre past full submersion or the rails go under, but a
                // keel-line probe is already ~96% submerged at her float
                // equilibrium, so sharing that onset made the brake permanent.
                // Gating plow on VERTICAL closing speed was tried and reverted:
                // it switched the term off almost entirely (0.0 kN through a
                // whole storm run), she charged the faces at 10 m/s and
                // pitchpoled — plow is what stops that. The physical term is
                // about driving HORIZONTALLY into a wall of water, which the
                // forward-velocity factor below already carries.
                float overPlow = Mathf.Max(0f, over - plowOnset);
                float plowReserve = Mathf.Min(maxReserve, reservePerMetre * overPlow);

                Vector3 buoy = Vector3.up * (waterDensity * g * totalVolume
                    * probes[i].volumeShare * (sub + reserveLift));
                // Anisotropy on WORLD vertical + flattened hull axes. Doing it
                // in the ship frame turns pitch into a diving plane: forward
                // speed projects onto local Y, gets the full heave coefficient,
                // and drags the hull under (measured: 0.88 submersion under
                // full sail, speed capped at 7 m/s and sinking).
                Vector3 fwdFlat = transform.forward; fwdFlat.y = 0f;
                fwdFlat = fwdFlat.sqrMagnitude > 1e-4f ? fwdFlat.normalized : Vector3.forward;
                Vector3 rightFlat = new Vector3(fwdFlat.z, 0f, -fwdFlat.x);
                Vector3 horiz = new Vector3(vRel.x, 0f, vRel.z);
                Vector3 shaped = Vector3.up * vRel.y
                    + fwdFlat * (Vector3.Dot(horiz, fwdFlat) * forwardDragFactor)
                    + rightFlat * (Vector3.Dot(horiz, rightFlat) * lateralDragFactor);
                // Drag scales with effSub too: the reserve lift arrives with
                // matching damping, so a buried bow rises instead of ringing.
                float coeff = -(linearDrag + quadraticDrag * vRel.magnitude)
                              * (probes[i].volumeShare * effSub);
                Vector3 drag = coeff * shaped;
                dbgVert += coeff * (Vector3.up * vRel.y);
                dbgFwd += coeff * (fwdFlat * (Vector3.Dot(horiz, fwdFlat) * forwardDragFactor));
                dbgLat += coeff * (rightFlat * (Vector3.Dot(horiz, rightFlat) * lateralDragFactor));
                if (reserve > reserveWorst) reserveWorst = reserve;
                // No single probe may out-shove its own buoyant capacity by
                // much — keeps any velocity transient from launching the hull.
                float dragCap = 2f * waterDensity * g * totalVolume
                    * probes[i].volumeShare * (1f + reserve);
                drag = Vector3.ClampMagnitude(drag, dragCap);

                // Plow held back for pass 2: it is clamped against a TOTAL, so
                // no single probe can decide it.
                plowScratch[i] = coeff *
                    (fwdFlat * (Vector3.Dot(horiz, fwdFlat)
                        * (plowDragFactor * plowReserve * wayFactor)));
                plowAt[i] = world;
                plowTotal += plowScratch[i];

                rb.AddForceAtPosition(buoy + drag, world);
                waveForce += buoy + drag;
            }

            // Pass 2 — plow drag, clamped as a total. Scaling every probe by
            // the same factor keeps the bow-down couple's SHAPE (a slam still
            // pitches her) while bounding it, which is what stops the
            // bury -> more plow -> more bury runaway.
            float plowMax = maxPlowDecel * rb.mass;
            float plowMag = plowTotal.magnitude;
            float plowScale = plowMag > plowMax && plowMag > 1e-3f
                ? plowMax / plowMag : 1f;
            if (plowMag > 1e-3f)
            {
                for (int i = 0; i < probes.Length; i++)
                {
                    if (plowScratch[i].sqrMagnitude <= 0f) continue;
                    Vector3 f = plowScratch[i] * plowScale;
                    rb.AddForceAtPosition(f, plowAt[i]);
                    waveForce += f;
                }
            }
            DebugPlowForward = plowTotal * plowScale;

            Submersion = subSum;
            MeanWaterHeight = hSum / probes.Length;
            MaxRailImmersion = railWorst > float.MinValue ? railWorst : 0f;
            DebugDragForward = dbgFwd;
            DebugDragLateral = dbgLat;
            DebugDragVertical = dbgVert;
            DebugMaxReserve = reserveWorst;
            WaveForce = waveForce - Vector3.up * Vector3.Dot(waveForce, Vector3.up);

            // Submersion-scaled angular damping: a hull in the water settles,
            // a hull thrown clear of it doesn't get magic air brakes.
            rb.AddTorque(-rb.angularVelocity * (angularDragTorque * Mathf.Clamp01(subSum)));
        }
    }
}
