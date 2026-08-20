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
        [Tooltip("Explicit inertia box (m) — a colliderless Rigidbody defaults to unit inertia and spins like a coin.")]
        [SerializeField] Vector3 inertiaBoxDims = new Vector3(4.4f, 3f, 13f);
        [SerializeField] Vector3 centreOfMass = new Vector3(0f, -0.6f, 0f);

        Rigidbody rb;
        BuoyancyProbeSet probeSet;

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

            for (int i = 0; i < probes.Length; i++)
            {
                Vector3 world = transform.TransformPoint(probes[i].localPosition);
                float sub = Mathf.Clamp01(
                    (samples[i].height - (world.y + SeatOffset)) / probes[i].radius + 0.5f);
                subSum += sub * probes[i].volumeShare;
                hSum += samples[i].height;
                if (probes[i].isRail)
                    railWorst = Mathf.Max(railWorst, samples[i].height - world.y);

                if (sub <= 0f) continue;

                Vector3 buoy = Vector3.up *
                    (waterDensity * g * totalVolume * probes[i].volumeShare * sub);

                Vector3 vWater = AmbientFlow + new Vector3(
                    samples[i].velocity.x, samples[i].velocity.y, samples[i].velocity.z);
                Vector3 vRel = rb.GetPointVelocity(world) - vWater;
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
                float coeff = -(linearDrag + quadraticDrag * vRel.magnitude)
                              * (probes[i].volumeShare * sub);
                Vector3 drag = coeff * shaped;
                dbgVert += coeff * (Vector3.up * vRel.y);
                dbgFwd += coeff * (fwdFlat * (Vector3.Dot(horiz, fwdFlat) * forwardDragFactor));
                dbgLat += coeff * (rightFlat * (Vector3.Dot(horiz, rightFlat) * lateralDragFactor));
                // No single probe may out-shove its own buoyant capacity by
                // much — keeps any velocity transient from launching the hull.
                float dragCap = 2f * waterDensity * g * totalVolume * probes[i].volumeShare;
                drag = Vector3.ClampMagnitude(drag, dragCap);

                rb.AddForceAtPosition(buoy + drag, world);
                waveForce += buoy + drag;
            }

            Submersion = subSum;
            MeanWaterHeight = hSum / probes.Length;
            MaxRailImmersion = railWorst > float.MinValue ? railWorst : 0f;
            DebugDragForward = dbgFwd;
            DebugDragLateral = dbgLat;
            DebugDragVertical = dbgVert;
            WaveForce = waveForce - Vector3.up * Vector3.Dot(waveForce, Vector3.up);

            // Submersion-scaled angular damping: a hull in the water settles,
            // a hull thrown clear of it doesn't get magic air brakes.
            rb.AddTorque(-rb.angularVelocity * (angularDragTorque * Mathf.Clamp01(subSum)));
        }
    }
}
