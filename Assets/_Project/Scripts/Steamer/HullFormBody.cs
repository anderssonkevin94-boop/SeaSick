using SeaSick.Ocean;
using UnityEngine;

namespace SeaSick.Steamer
{
    /// Strip buoyancy off the hull's own section tables (docs/steamer-spec.md §4).
    ///
    /// The ladder ships float on a dozen probe spheres and a stack of terms
    /// that exist to cover for them -- reserve, plow, dynamic lift, a burial
    /// clamp. This hull has none of those. Each of her stations is two
    /// half-strips whose displaced volume is READ from the real section at the
    /// water level sampled beside it, so flare is the reserve, the waterplane
    /// is the heave spring, and BM -- hence GM, hence her roll period -- falls
    /// out of the geometry instead of being tuned toward. Tilting each strip's
    /// lift by the slope the hull herself spans gives the Froude-Krylov force:
    /// surfing, wave surge and sway are emergent and already length-averaged,
    /// because a 34 m hull with 13 stations cannot see a 5 m wave as a slope.
    ///
    /// What is NOT geometry is said out loud and kept small: a heave damper
    /// per strip, a top-up to reach the roll and pitch damping ratios, the
    /// cross-flow (sway) drag that is her keel, a surge resistance curve and
    /// a yaw damper.
    ///
    /// Sampling goes through `OceanProbeRegistry` (the no-per-object-sampling
    /// rule): `HullFormProbeFeeder` (-100) writes positions, the driver (-90)
    /// samples, this (-80) reads the same step's answer.
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(-80)]
    public class HullFormBody : MonoBehaviour
    {
        // xs = hb / sqrt(3). A wall-sided half-strip of waterplane hb*dz has a
        // transverse second moment of hb^3 dz / 3 about the centreline; a
        // point spring of the same area at xs gives hb dz xs^2. Equal when
        // xs^2 = hb^2 / 3, so the point model's BM is the section's BM.
        const float SampleOffsetFactor = 0.57735f;
        const float MinSampleOffset = 0.3f;
        // sample.velocity is a finite difference of two readback slots and
        // is clamped at the source; clamped again here because a handle is
        // not a hull probe and this is the number quadratic drag squares.
        const float MaxOrbitalSpeed = 8f;
        // Below this the hull is on her beam ends and "the slope she spans"
        // has no horizontal baseline left to be measured over.
        const float MinUprightForSlope = 0.2f;

        [SerializeField] float waterDensity = 1025f;

        [Header("Damping ratios (derived into coefficients at Configure)")]
        [Tooltip("Heave damping ratio. c_h = 2 zeta sqrt(rho g Aw m), shared out over the half-strips by waterplane and applied where each one samples -- so the same dampers also give some roll and pitch damping, which is accounted for below.")]
        [SerializeField, Range(0f, 1.5f)] float heaveDampingRatio = 0.45f;
        [Tooltip("Target roll damping ratio against K = m g GM. The strip dampers' own share (sum c arm^2) is subtracted first and only the remainder is added as a hull-frame torque. 0.45 is one visible overshoot: paddle boxes and bilge keels, not a racing dinghy.")]
        [SerializeField, Range(0f, 1.5f)] float rollDampingRatio = 0.45f;
        [Tooltip("Target pitch damping ratio against the strip model's own longitudinal stiffness. She noses into a sea once, not three times.")]
        [SerializeField, Range(0f, 1.5f)] float pitchDampingRatio = 0.8f;

        [Header("Froude-Krylov")]
        [Tooltip("Ceiling on the surface gradient a strip is allowed to feel, rise over run. 0.6 is a 31 degree face -- past that the sea is breaking and the pressure field under it is no longer hydrostatic anyway.")]
        [SerializeField, Range(0.1f, 1f)] float maxSlope = 0.6f;
        [Tooltip("How much of the SHORT-wave transverse slope reaches her roll, 0..1. The long-wave slope (read off the outriggers) is always felt in full.")]
        [SerializeField, Range(0f, 1f)] float shortWaveRollFeel = 0.5f;
        [Tooltip("Outrigger sample points, metres abeam of the centreline. About half the everyday wind-sea wavelength, so chop cancels across the pair and swell does not.")]
        [SerializeField, Range(6f, 30f)] float outriggerOffset = 12f;
        [Tooltip("Seconds of EMA on the published FkSurgeAccel / FkSwayAccel. The forces themselves are applied raw; this only smooths what the motor and camera read.")]
        [SerializeField, Range(0.02f, 2f)] float fkSmoothing = 0.4f;

        [Header("Cross-flow (sway) -- her keel")]
        [Tooltip("Depth of the lateral force's line of action, as a fraction of draft below the waterline. 0.5 = the centre of lateral resistance of a plain rectangle of underbody. Deeper heels her outward harder in a turn.")]
        [SerializeField, Range(0f, 1f)] float lateralForceDepth = 0.3f;
        [Tooltip("Bluff-body cross-flow drag coefficient: what stops her being pushed sideways.")]
        [SerializeField, Range(0f, 3f)] float crossflowCd = 1.1f;
        [Tooltip("Lift slope of the hull as a low-aspect foil: side force per unit of (way x leeway). This is the term that makes her track -- it grows with speed, so she is loose when stopped and stiff at full ahead.")]
        [SerializeField, Range(0f, 3f)] float crossflowLift = 0.6f;
        /// The hull's side LIFT is a slender-body force: it answers her
        /// sideslip as a whole, and it lands forward of midships. Computed
        /// per station from the LOCAL sway it became a z^2 yaw damper worth
        /// ~9e7 N m s at full ahead, eight times the rudder: measured circle
        /// 9.3 lengths. So it reads the sway at the centre of mass, and this
        /// is how far its weight leans toward the bow (0 = even).
        [SerializeField, Range(0f, 1f)] float liftBowBias = 0.1f;
        /// Yaw damping that grows with way, as a fraction of yaw inertia per
        /// m/s: the part of the old per-station lift that was worth keeping,
        /// as a dial.
        ///
        /// 0.027 (was 0.12). See `yawDampingFactor` for why the pair came
        /// down together; this one carries most of what is left, because a
        /// damper that grows with way is the one that keeps the circle the
        /// same size in METRES whatever the telegraph says.
        [SerializeField, Range(0f, 0.5f)] float yawDampingPerSpeed = 0.027f;
        [Tooltip("Linear term, m/s. Only matters in the last few cm/s, where the quadratic term has nothing left and she would otherwise drift forever.")]
        [SerializeField, Range(0f, 1f)] float crossflowLinear = 0.15f;
        [Tooltip("Lateral-area multiplier on the aft stations: skeg and deadwood. More area aft than forward is directional stability -- without it she is a weathervane pointing the wrong way.")]
        [SerializeField, Range(1f, 3f)] float skegFactor = 1.7f;
        [Tooltip("How many of the aftmost stations get the skeg factor.")]
        [SerializeField, Range(0, 6)] int skegStations = 3;

        [Header("Surge resistance  R = -m (a1 u + a2 u|u|)")]
        [Tooltip("a1, 1/s. Per unit MASS on purpose: PaddleDrive solves its thrust constant against these two, so top speed survives the generator moving the displacement.")]
        [SerializeField, Range(0f, 0.5f)] float surgeLinear = 0.05f;
        [Tooltip("a2, 1/m.")]
        [SerializeField, Range(0f, 0.05f)] float surgeQuadratic = 0.006f;

        [Header("Yaw")]
        [Tooltip("c_yaw = this x I_yaw, i.e. a 1/s decay rate on yaw, on top of what the cross-flow strips already give at their arms. 0.03 (was 0.4): a SPEED-INDEPENDENT yaw damper is the term that made her turn wider the slower she went, which is backwards. What is left is only enough that a drifting hull does not spin on.")]
        [SerializeField, Range(0f, 2f)] float yawDampingFactor = 0.03f;

        [Tooltip("How much of the cross-flow strips' resistance to ROTATION she keeps, 0..1. The strips' sway force is untouched at any value -- only the part of their yaw moment that comes from the hull turning under them is scaled. Flat-plate cross-flow drag read off the whole wetted profile at every station badly overpredicts N_r on a slender hull: measured 671 kN m s/rad against a real launch's ~200, which is why her yaw rate used to arrive complete in 0.14 s. 0.35 puts N_r' at about 0.003, where a real hull is.")]
        [SerializeField, Range(0f, 1f)] float crossflowYawScale = 0.35f;

        [Tooltip("Yaw added mass, as a multiple of the solid-body yaw inertia. The water a turning hull has to shove sideways turns with her; for a slender hull it is worth 0.5 to 1.0 of her own I_zz. Without it she has a real ship's yaw damping and a model boat's yaw inertia, so her head answers the helm instantly -- which is exactly the whiplash Kevin felt. 1.6 puts her yaw time constant at about L/V, where a real hull's is.")]
        [SerializeField, Range(1f, 3f)] float yawAddedInertia = 1.6f;

        [Header("Published wetness")]
        [Tooltip("Metres of green water over the lowest deck edge before BurialDepth starts counting. There is no burial clamp on this hull -- this only sets where Bilge and HullIntegrity start charging her for it.")]
        [SerializeField, Range(0f, 3f)] float greenWaterThreshold = 1f;

        // ---- runtime state; everything below is sized once in Configure ----
        // NonSerialized matters: a domain reload mid-play keeps serializable
        // private fields and drops the rest, so without it this would come
        // back "configured" with null handles. With it, the whole of the
        // runtime state is rebuilt from the JSON on the next step.
        [System.NonSerialized] bool configured;
        [System.NonSerialized] bool autoLoadTried;
        [System.NonSerialized] bool everSampled;
        HullFormData data;
        Rigidbody rb;
        BuoyantBody legacyBody;

        int stationCount;
        float[] xs;                 // per station: lateral offset of sample point and force arm
        float[] halfShare;          // per station: ONE half-strip's share of the waterplane
        float[] skeg;               // per station: lateral-area multiplier
        float[] stationHeight;      // per station: mean sampled surface height
        float[] stationLevel;       // per station: mean hull-local water level
        Vector3[] stationVelocity;  // per station: mean orbital velocity (no ambient)
        // per half-strip, index = station * 2 + side (0 port, 1 starboard)
        OceanProbeRegistry.Handle[] handles;
        Vector3[] sampleLocal;
        Vector3[] sampleWorld;
        float[] height;
        float[] level;
        Vector3[] waterVelocity;
        Vector3[] forcePoint;       // gizmos only
        Vector3[] forceVector;      // gizmos only

        float heaveDamping;         // c_h, N s/m, whole hull
        float extraRollDamping;     // N m s/rad about hull z, beyond the strips' own
        float extraPitchDamping;    // N m s/rad about hull x, beyond the strips' own
        float yawDamping;           // N m s/rad about hull y
        float lateralForceY;

        public HullFormData Data => data;
        public Rigidbody Body => rb;
        /// Sampler ready AND every handle sampled at least once. Until then
        /// she hangs at her marks with gravity cancelled and nothing applied.
        public bool Ready { get; private set; }
        /// a1 and a2 of the surge resistance; PaddleDrive solves against them.
        public float SurgeLinear { get => surgeLinear; set => surgeLinear = Mathf.Max(0f, value); }
        public float SurgeQuadratic { get => surgeQuadratic; set => surgeQuadratic = Mathf.Max(0f, value); }
        /// Immersed volume / design volume, clamped 0..1.
        public float Submersion { get; private set; }
        /// The same ratio unclamped -- > 1 is how deep she is pressed.
        public float SubmersionRaw { get; private set; }
        public float MeanWaterHeight { get; private set; }
        /// max over half-strips of (water level - deck edge), metres. Negative
        /// is freeboard left; positive is green water over the rail.
        public float MaxDeckImmersion { get; private set; }
        /// Metres past `greenWaterThreshold`, >= 0. The BuoyantBody.BurialDepth
        /// equivalent, minus the clamp it used to report on.
        public float BurialDepth { get; private set; }
        /// FK horizontal force / mass along the flattened keel and athwart
        /// (+ = ahead, + = to starboard), m/s^2, EMA over `fkSmoothing`.
        public float FkSurgeAccel { get; private set; }
        public float FkSwayAccel { get; private set; }
        /// The book displacement, m^3 (mass = rho x this, so she floats on her marks).
        public float DesignVolume => data != null ? data.volume : 0f;
        /// N m/rad. Roll is m g GM off the tables; pitch is what the strips deliver about the CoM.
        public float RollStiffness { get; private set; }
        public float PitchStiffness { get; private set; }
        /// Macroscopic water motion (current, drift) added to the orbital
        /// velocity as the reference for every drag term -- the hull is
        /// carried by it, not shoved through it.
        public Vector3 AmbientFlow { get; set; }

        /// Probe read surface, this step. Vertical buoyancy (N, up +),
        /// cross-flow total (N, starboard +), surge resistance (N, ahead +).
        public float DebugBuoyancyN { get; private set; }
        public float DebugCrossflowN { get; private set; }
        public float DebugSurgeN { get; private set; }
        /// Unsmoothed FK force this step, world space, N.
        public Vector3 DebugFkForce { get; private set; }
        /// The strips' own roll/pitch damping and the torque top-up, N m s/rad.
        public float DebugStripRollDamping { get; private set; }
        public float DebugStripPitchDamping { get; private set; }
        public float DebugExtraRollDamping => extraRollDamping;
        public float DebugExtraPitchDamping => extraPitchDamping;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        /// Fit the rigidbody and every derived coefficient to a hull form.
        /// Safe to call once straight after AddComponent, and again if the
        /// data changes (it re-registers its handles rather than leaking them).
        public void Configure(HullFormData hullForm)
        {
            string why = null;
            if (hullForm == null || !hullForm.Validate(out why))
            {
                Debug.LogError("[Steamer] HullFormBody.Configure: no usable hull form"
                    + (hullForm == null ? "." : " (" + why + ")."), this);
                return;
            }
            UnregisterHandles();
            configured = false;
            data = hullForm;
            if (rb == null) rb = GetComponent<Rigidbody>();
            legacyBody = GetComponent<BuoyantBody>();

            // ---- rigidbody, per spec §4 ----
            // mass = rho x volume EXACTLY is what floats her on her marks; a
            // file that forgot it gets the identity rather than a stone.
            float mass = data.massKg > 1f ? data.massKg : waterDensity * data.volume;
            rb.mass = mass;
            rb.useGravity = true;
            rb.centerOfMass = data.com;
            // A colliderless body defaults to unit inertia and spins like a
            // coin. Gyradii, not a box: the roll gyradius is the design's
            // handle on T_roll (wheels and boxes 5-6 m out park it between
            // the wind sea and the swell) and a box cannot express that.
            // (PhysX rejects a zero inertia component outright, so a file
            // missing a gyradius falls back to the spec's seed value.)
            float kPitch = data.gyradiusPitch > 0.1f ? data.gyradiusPitch : 0.25f * data.lwl;
            float kYaw = data.gyradiusYaw > 0.1f ? data.gyradiusYaw : 0.26f * data.lwl;
            float kRoll = data.gyradiusRoll > 0.1f ? data.gyradiusRoll : 5.4f;
            // Yaw carries its added mass in the tensor rather than as a
            // torque: PhysX integrates the rotation with it, so the whole
            // response -- the helm, a wave's yaw moment, a collision -- gets
            // the right time constant, and every coefficient derived from
            // `inertiaTensor.y` below scales with it for free. Roll and pitch
            // keep their own added mass in their damping ratios, as before.
            rb.inertiaTensor = new Vector3(mass * kPitch * kPitch,
                mass * kYaw * kYaw * Mathf.Max(1f, yawAddedInertia),
                mass * kRoll * kRoll);
            rb.inertiaTensorRotation = Quaternion.identity;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.05f;

            // ---- the book, checked against the tables it came from ----
            data.Rederive(out float volD, out float wpD, out float kbD, out float bmD,
                          out float lcbD, out float _);
            WarnIfOff("volume", data.volume, volD);
            WarnIfOff("waterplane", data.waterplane, wpD);
            WarnIfOff("kb", data.kb, kbD);
            WarnIfOff("bm", data.bm, bmD);
            // LCB sits near zero by design, so a percentage of it means nothing:
            // judged against the length instead, 0.2 % L.
            if (Mathf.Abs(lcbD - data.lcbZ) > 0.002f * Mathf.Max(1f, data.lwl))
                Debug.LogWarning($"[Steamer] hullform lcbZ: book {data.lcbZ:F3} m, tables {lcbD:F3} m.", this);

            // ---- arrays ----
            stationCount = data.stations.Length;
            int halves = stationCount * 2;
            xs = new float[stationCount];
            halfShare = new float[stationCount];
            skeg = new float[stationCount];
            stationHeight = new float[stationCount];
            stationLevel = new float[stationCount];
            stationVelocity = new Vector3[stationCount];
            // Two more than the half-strips: the outriggers (see
            // `shortWaveRollFeel`), which ride at the end of the same arrays
            // so registering, feeding and readiness need no second path.
            handles = new OceanProbeRegistry.Handle[halves + 2];
            sampleLocal = new Vector3[halves + 2];
            sampleWorld = new Vector3[halves + 2];
            sampleLocal[halves] = new Vector3(outriggerOffset, 0f, 0f);
            sampleLocal[halves + 1] = new Vector3(-outriggerOffset, 0f, 0f);
            height = new float[halves];
            level = new float[halves];
            waterVelocity = new Vector3[halves];
            forcePoint = new Vector3[halves];
            forceVector = new Vector3[halves];

            // The waterplane the STRIPS deliver, not the book's: shares must
            // sum to one over the dampers that actually exist.
            float waterplane = Mathf.Max(1e-3f, wpD);
            float g = Physics.gravity.magnitude;
            float stripRoll = 0f, stripPitch = 0f, longitudinalAboutCom = 0f;
            for (int i = 0; i < stationCount; i++)
            {
                var s = data.stations[i];
                float hb = 0f > s.keelY ? data.HalfBreadthAt(i, 0f) : 0f;
                xs[i] = Mathf.Max(MinSampleOffset, SampleOffsetFactor * hb);
                halfShare[i] = hb * s.dz / waterplane;
                sampleLocal[i * 2] = new Vector3(-xs[i], 0f, s.z);
                sampleLocal[i * 2 + 1] = new Vector3(xs[i], 0f, s.z);

                // "Aft three" by POSITION, not by index: the spec orders the
                // stations aft to bow, but a rank costs nothing at init and
                // survives a generator that writes them the other way.
                int aftOfMe = 0;
                for (int j = 0; j < stationCount; j++)
                    if (data.stations[j].z < s.z) aftOfMe++;
                skeg[i] = aftOfMe < skegStations ? skegFactor : 1f;

                float arm = s.z - data.com.z;
                stripRoll += 2f * halfShare[i] * xs[i] * xs[i];
                stripPitch += 2f * halfShare[i] * arm * arm;
                longitudinalAboutCom += 2f * hb * s.dz * arm * arm;
            }

            // ---- damping: c = 2 zeta sqrt(K I), per axis ----
            heaveDamping = 2f * heaveDampingRatio * Mathf.Sqrt(waterDensity * g * waterplane * mass);
            stripRoll *= heaveDamping;
            stripPitch *= heaveDamping;

            // GM off the tables, with the JSON's CoM: this is the stiffness
            // the strips will really deliver, which is what the damping has
            // to be a ratio OF. The book's gm is the fallback, not the source.
            float gmTables = kbD + bmD - data.kg;
            float gm = gmTables > 0.05f ? gmTables : Mathf.Max(0.05f, data.gm);
            RollStiffness = mass * g * gm;
            // rho g I_L is the waterplane's spring about the CoM; the second
            // term is the same B-below-G loss that turns BM into GM, and is
            // a percent or so of the first on any hull longer than she is deep.
            PitchStiffness = Mathf.Max(0f,
                waterDensity * g * longitudinalAboutCom - mass * g * (data.kg - kbD));

            Vector3 inertia = rb.inertiaTensor;
            float rollTarget = 2f * rollDampingRatio * Mathf.Sqrt(RollStiffness * inertia.z);
            float pitchTarget = 2f * pitchDampingRatio * Mathf.Sqrt(PitchStiffness * inertia.x);
            // Never negative: if the strips alone already over-damp an axis,
            // the honest answer is a lower heave ratio, not an anti-damper.
            extraRollDamping = Mathf.Max(0f, rollTarget - stripRoll);
            extraPitchDamping = Mathf.Max(0f, pitchTarget - stripPitch);
            yawDamping = yawDampingFactor * inertia.y;
            DebugStripRollDamping = stripRoll;
            DebugStripPitchDamping = stripPitch;

            lateralForceY = -lateralForceDepth * data.draft;

            if (GetComponent<HullFormProbeFeeder>() == null)
                gameObject.AddComponent<HullFormProbeFeeder>();

            configured = true;
            everSampled = false;
            Ready = false;
            if (isActiveAndEnabled) RegisterHandles();
        }

        void WarnIfOff(string what, float book, float tables)
        {
            float scale = Mathf.Max(Mathf.Abs(book), 1e-4f);
            if (Mathf.Abs(book - tables) / scale > 0.02f)
                Debug.LogWarning($"[Steamer] hullform {what}: book {book:F3}, re-derived from the station tables {tables:F3} (> 2 % apart). The strips float her on the tables, not the book.", this);
        }

        // Register in OnEnable, unregister in OnDisable: the registry is a
        // static list, and with domain reload off it outlives the play
        // session -- a handle left behind is sampled forever by every later run.
        void OnEnable()
        {
            if (configured) RegisterHandles();
        }

        void OnDisable()
        {
            UnregisterHandles();
            Ready = false;
        }

        void RegisterHandles()
        {
            if (handles == null) return;
            if (rb == null) rb = GetComponent<Rigidbody>();
            Vector3 pos = rb.position;
            Quaternion rot = rb.rotation;
            for (int k = 0; k < handles.Length; k++)
            {
                if (handles[k] != null) continue;
                sampleWorld[k] = pos + rot * sampleLocal[k];
                handles[k] = OceanProbeRegistry.Register(sampleWorld[k]);
            }
            everSampled = false;
        }

        void UnregisterHandles()
        {
            if (handles == null) return;
            for (int k = 0; k < handles.Length; k++)
            {
                OceanProbeRegistry.Unregister(handles[k]);
                handles[k] = null;
            }
            everSampled = false;
        }

        /// Called by HullFormProbeFeeder at -100, before the driver samples.
        ///
        /// The pose is the RIGIDBODY's, not the transform's. With interpolation
        /// on, the transform is the rendered pose -- up to a step behind the
        /// body -- while every force below is applied about the body's real
        /// centre of mass; sampling and arming off the transform would shift
        /// every force arm by that lag, along the direction of travel.
        /// (Assumes the ship root is unscaled, as every physics root here is.)
        bool TryHeight(int k, out float surface)
        {
            surface = 0f;
            var h = handles[k];
            if (h == null) return false;
            OceanSample s = h.sample;
            float nn = s.normal.x * s.normal.x + s.normal.y * s.normal.y + s.normal.z * s.normal.z;
            if (nn <= 1e-6f || float.IsNaN(s.height) || float.IsInfinity(s.height)) return false;
            surface = s.height;
            return true;
        }

        internal void WriteProbePositions()
        {
            if (!configured || handles == null || rb == null) return;
            Vector3 pos = rb.position;
            Quaternion rot = rb.rotation;
            for (int k = 0; k < handles.Length; k++)
            {
                if (handles[k] == null) continue;
                sampleWorld[k] = pos + rot * sampleLocal[k];
                handles[k].position = sampleWorld[k];
            }
        }

        bool AllSampled()
        {
            if (everSampled) return true;
            if (handles == null) return false;
            for (int k = 0; k < handles.Length; k++)
                if (handles[k] == null || handles[k].sampledFrame == 0) return false;
            everSampled = true;
            return true;
        }

        void FixedUpdate()
        {
            if (!configured)
            {
                // Dropped into a scene by hand, or back from a domain reload
                // with the runtime state gone: fit herself, once.
                if (autoLoadTried) return;
                autoLoadTried = true;
                var loaded = HullFormData.Load();
                if (loaded != null) Configure(loaded);
                if (!configured) return;
            }

            Ready = OceanSampler.Ready && AllSampled();
            if (!Ready)
            {
                // Spec §4.11. The sea does not exist yet, so neither does her
                // buoyancy; cancel gravity and she hangs at her marks instead
                // of falling through the surface on frame one and arriving at
                // the first real sample already four metres under.
                rb.AddForce(-Physics.gravity * rb.mass);
                return;
            }

            Step(Time.fixedDeltaTime);
        }

        void Step(float dt)
        {
            float g = Physics.gravity.magnitude;
            float rhoG = waterDensity * g;
            float mass = rb.mass;

            Vector3 pos = rb.position;
            Quaternion rot = rb.rotation;
            Vector3 up = rot * Vector3.up;
            Vector3 fwd = rot * Vector3.forward;
            Vector3 right = rot * Vector3.right;
            // Hull-local level from a world height difference. Floored at 0.5
            // so a knockdown cannot turn a metre of water into ten of "level".
            float upY = Mathf.Max(up.y, 0.5f);

            // Drag axes are the FLATTENED hull axes, as on the ladder ships:
            // resolved in the pitched frame, forward way projects onto local
            // Y and the hull becomes a diving plane.
            Vector3 fwdFlat = new Vector3(fwd.x, 0f, fwd.z);
            fwdFlat = fwdFlat.sqrMagnitude > 1e-4f ? fwdFlat.normalized : Vector3.forward;
            Vector3 rightFlat = new Vector3(fwdFlat.z, 0f, -fwdFlat.x);

            // ---- pass 1: read the samples ----
            float heightSum = 0f;
            Vector3 velocitySum = Vector3.zero;
            for (int i = 0; i < stationCount; i++)
            {
                for (int side = 0; side < 2; side++)
                {
                    int k = i * 2 + side;
                    // Recomputed rather than trusted from the feeder: same
                    // pose, same answer, and no dependence on it having run.
                    Vector3 p = pos + rot * sampleLocal[k];
                    sampleWorld[k] = p;

                    var h = handles[k];
                    float surface = p.y;            // fallback: on her marks at this strip
                    Vector3 v = Vector3.zero;
                    if (h != null)
                    {
                        OceanSample s = h.sample;
                        // A zero normal is a default(OceanSample): the driver
                        // has not written this one. NaN is a readback hiccup.
                        float nn = s.normal.x * s.normal.x + s.normal.y * s.normal.y + s.normal.z * s.normal.z;
                        if (nn > 1e-6f && !float.IsNaN(s.height) && !float.IsInfinity(s.height))
                        {
                            surface = s.height;
                            v = new Vector3(s.velocity.x, s.velocity.y, s.velocity.z);
                            float sq = v.sqrMagnitude;
                            if (float.IsNaN(sq) || float.IsInfinity(sq)) v = Vector3.zero;
                            else if (sq > MaxOrbitalSpeed * MaxOrbitalSpeed) v *= MaxOrbitalSpeed / Mathf.Sqrt(sq);
                        }
                    }
                    height[k] = surface;
                    level[k] = (surface - p.y) / upY;
                    waterVelocity[k] = v;
                    heightSum += surface;
                    velocitySum += v;
                }
                stationHeight[i] = 0.5f * (height[i * 2] + height[i * 2 + 1]);
                stationLevel[i] = 0.5f * (level[i * 2] + level[i * 2 + 1]);
                stationVelocity[i] = 0.5f * (waterVelocity[i * 2] + waterVelocity[i * 2 + 1]);
            }
            int halves = stationCount * 2;
            Vector3 meanOrbital = velocitySum / halves;

            // ---- how much of the sea's TRANSVERSE slope she feels ----
            // Port and starboard samples sit 2 xs (about 4.4 m) apart and read
            // the raw surface, so a 15-30 m chop tips that pair as if it were
            // the whole sea's slope. A real hull feels it through her depth
            // (pressure dies as exp(-k z)) and across her beam: about 0.6 of
            // a 24 m wave, 0.4 of a 14 m one, all of a swell. Measured raw at
            // local Hs 3.5: roll rms 5.2 deg, worse than the brig. So the
            // slope is split. The LONG part is read off two outriggers a
            // wavelength of chop apart, where the chop cancels and the swell
            // does not, and is felt in full -- she must still lie to the face
            // of a roller. What is left is chop, and is felt at
            // `shortWaveRollFeel`. Only the WATER is filtered: her own tilt
            // reaches `level` through sampleWorld.y untouched, so the
            // hydrostatic stiffness (BM, GM) is exactly what it was.
            if (shortWaveRollFeel < 1f && TryHeight(halves, out float outS) && TryHeight(halves + 1, out float outP))
            {
                float slopeLong = (outS - outP) / (2f * outriggerOffset);
                for (int i = 0; i < stationCount; i++)
                {
                    int kp = i * 2, ks = kp + 1;
                    float half = 0.5f * (height[ks] - height[kp]);
                    float longHalf = xs[i] * slopeLong;
                    float felt = longHalf + shortWaveRollFeel * (half - longHalf);
                    height[ks] = stationHeight[i] + felt;
                    height[kp] = stationHeight[i] - felt;
                    level[ks] = (height[ks] - sampleWorld[ks].y) / upY;
                    level[kp] = (height[kp] - sampleWorld[kp].y) / upY;
                }
            }

            // ---- pass 2: buoyancy + FK + heave damping, per half-strip ----
            float immersed = 0f, buoyancyUp = 0f, deckWorst = float.MinValue;
            Vector3 fk = Vector3.zero;
            bool slopeUsable = up.y > MinUprightForSlope;
            for (int i = 0; i < stationCount; i++)
            {
                var st = data.stations[i];

                // The slope is the hull's OWN: differences of her own samples
                // over her own baselines, never sample.normal -- that carries
                // sub-metre chop which a 34 m hull cannot feel, and at |g| up
                // to 0.6 it would be the largest force on her.
                //
                // Transverse is port->starboard over 2 xs along `right`;
                // longitudinal is a central difference of station means along
                // `fwd` (one-sided at the ends). Both baselines are tilted
                // with the hull, so the two directional slopes are solved for
                // the world gradient rather than assumed orthogonal:
                //   grad . right_xz = st,  grad . fwd_xz = sl,
                // whose determinant is exactly up.y.
                Vector3 grad = Vector3.zero;
                if (slopeUsable)
                {
                    int a = i > 0 ? i - 1 : i;
                    int b = i < stationCount - 1 ? i + 1 : i;
                    float run = data.stations[b].z - data.stations[a].z;
                    float sl = Mathf.Abs(run) > 1e-3f ? (stationHeight[b] - stationHeight[a]) / run : 0f;
                    float sTrans = (height[i * 2 + 1] - height[i * 2]) / (2f * xs[i]);
                    float gx = (fwd.z * sTrans - right.z * sl) / up.y;
                    float gz = (right.x * sl - fwd.x * sTrans) / up.y;
                    float mag = Mathf.Sqrt(gx * gx + gz * gz);
                    if (mag > maxSlope) { float c = maxSlope / mag; gx *= c; gz *= c; }
                    if (!float.IsNaN(gx) && !float.IsNaN(gz)) grad = new Vector3(gx, 0f, gz);
                }

                for (int side = 0; side < 2; side++)
                {
                    int k = i * 2 + side;
                    float yw = level[k];
                    float over = yw - st.deckY;
                    if (over > deckWorst) deckWorst = over;

                    float volume = 0.5f * data.AreaAt(i, yw) * st.dz;
                    forcePoint[k] = sampleWorld[k];
                    forceVector[k] = Vector3.zero;
                    if (volume <= 0f) continue;
                    immersed += volume;

                    // Lift acts through the immersed section's centroid, at
                    // the half-strip's arm. The table stops growing at the
                    // deck edge: water on deck adds nothing, and the flare
                    // below it is all the reserve she has.
                    float yc = data.CentroidYAt(i, yw);
                    Vector3 at = pos + rot * new Vector3(sampleLocal[k].x, yc, st.z);
                    float lift = rhoG * volume;
                    Vector3 f = new Vector3(-lift * grad.x, lift, -lift * grad.z);
                    rb.AddForceAtPosition(f, at);
                    buoyancyUp += lift;
                    fk.x += f.x; fk.z += f.z;

                    // Heave damper, at the waterline point, against the
                    // water's OWN vertical motion -- so she is carried up a
                    // swell rather than damped against it.
                    float vRelY = rb.GetPointVelocity(sampleWorld[k]).y - (AmbientFlow.y + waterVelocity[k].y);
                    float damp = -heaveDamping * halfShare[i] * vRelY;
                    rb.AddForceAtPosition(new Vector3(0f, damp, 0f), sampleWorld[k]);

                    forcePoint[k] = at;
                    forceVector[k] = f;
                }
            }

            float designVolume = Mathf.Max(1e-3f, data.volume);
            SubmersionRaw = immersed / designVolume;
            Submersion = Mathf.Clamp01(SubmersionRaw);

            // ---- pass 3: cross-flow per station ----
            float crossTotal = 0f, yawGiveBack = 0f;
            float invStations = 1f / stationCount;
            Vector3 vRelCom = rb.linearVelocity - (AmbientFlow + meanOrbital);
            float u0 = Vector3.Dot(vRelCom, fwdFlat);
            float v0 = Vector3.Dot(vRelCom, rightFlat);
            float invHalfLength = 2f / Mathf.Max(1f, data.lwl);
            for (int i = 0; i < stationCount; i++)
            {
                var st = data.stations[i];
                // Lateral area is the wetted PROFILE: from the local keel to
                // the local water level, and no deeper than the hull is --
                // a buried bow does not grow more keel.
                float wetDepth = Mathf.Clamp(stationLevel[i] - st.keelY, 0f, Mathf.Max(0f, st.deckY - st.keelY));
                if (wetDepth <= 0f) continue;
                float area = wetDepth * st.dz * skeg[i];

                Vector3 at = pos + rot * new Vector3(0f, lateralForceY, st.z);
                Vector3 vRel = rb.GetPointVelocity(at) - (AmbientFlow + stationVelocity[i]);
                float v = Vector3.Dot(vRel, rightFlat);
                float liftWeight = 1f + liftBowBias * st.z * invHalfLength;
                float f = -waterDensity * area *
                    (0.5f * crossflowCd * v * Mathf.Abs(v)
                     + crossflowLift * liftWeight * Mathf.Abs(u0) * v0 + crossflowLinear * v);
                // The ROTATIONAL part of this station's force, separated so
                // it can be scaled without touching her keel grip: what the
                // strip would make if the hull were sliding bodily (v0) is
                // kept whole, and the difference -- everything that exists
                // only because she is turning under it -- is what
                // `crossflowYawScale` weighs. The force she feels is `f`
                // either way; only the moment arm's share changes.
                float fStraight = -waterDensity * area *
                    (0.5f * crossflowCd * v0 * Mathf.Abs(v0)
                     + crossflowLift * liftWeight * Mathf.Abs(u0) * v0 + crossflowLinear * v0);
                // Explicit drag can only ever take speed OFF. Half of what
                // would stop this station's share of the hull dead in one
                // step is far above anything the terms reach in a seaway
                // (~90 kN at full ahead against ~500 kN per m/s here) and is
                // the bound that keeps a velocity glitch from becoming a kick.
                float cap = 0.5f * mass * invStations * Mathf.Max(Mathf.Abs(v), Mathf.Abs(v0)) / Mathf.Max(dt, 1e-4f);
                f = Mathf.Clamp(f, -cap, cap);
                fStraight = Mathf.Clamp(fStraight, -cap, cap);
                rb.AddForceAtPosition(rightFlat * f, at);
                // `AddForceAtPosition` has just charged her the FULL yaw
                // moment of this strip. Give back the part of the rotational
                // share she is not keeping; the force itself is untouched.
                yawGiveBack += (st.z - data.com.z) * (f - fStraight);
                crossTotal += f;
            }
            if (crossflowYawScale < 1f)
                rb.AddTorque(rot * new Vector3(0f,
                    (crossflowYawScale - 1f) * yawGiveBack, 0f));

            // ---- surge resistance, at the CoM, along the flattened keel ----
            // Against the LENGTH-AVERAGED orbital velocity: she is 34 m long
            // and rides in the mean of what is under her, not in whatever the
            // water is doing at one point.
            float uHull = Vector3.Dot(rb.linearVelocity - (AmbientFlow + meanOrbital), fwdFlat);
            float surge = -mass * (surgeLinear * uHull + surgeQuadratic * uHull * Mathf.Abs(uHull)) * Submersion;
            rb.AddForce(fwdFlat * surge);

            // ---- hull-frame damping torques ----
            // Roll and pitch get only the REMAINDER the strip dampers leave;
            // yaw gets c_yaw on top of the cross-flow strips. All scaled by
            // submersion: a hull thrown clear of the sea gets no air brakes.
            Vector3 spin = Quaternion.Inverse(rot) * rb.angularVelocity;
            Vector3 torque = new Vector3(
                -extraPitchDamping * spin.x,
                -(yawDamping + yawDampingPerSpeed * rb.inertiaTensor.y * Mathf.Abs(u0)) * spin.y,
                -extraRollDamping * spin.z) * Submersion;
            rb.AddTorque(rot * torque);

            // ---- publish ----
            MeanWaterHeight = heightSum / halves;
            MaxDeckImmersion = deckWorst > float.MinValue ? deckWorst : 0f;
            BurialDepth = Mathf.Max(0f, MaxDeckImmersion - greenWaterThreshold);
            float blend = 1f - Mathf.Exp(-dt / Mathf.Max(0.02f, fkSmoothing));
            FkSurgeAccel += (Vector3.Dot(fk, fwdFlat) / mass - FkSurgeAccel) * blend;
            FkSwayAccel += (Vector3.Dot(fk, rightFlat) / mass - FkSwayAccel) * blend;
            DebugBuoyancyN = buoyancyUp;
            DebugCrossflowN = crossTotal;
            DebugSurgeN = surge;
            DebugFkForce = fk;

            // Bilge, Breakers, HullIntegrity and ShipMotor read the hull's
            // wetness off BuoyantBody. It is disabled on this ship (it floats
            // on nothing of theirs) and stays as the mailbox.
            if (legacyBody != null)
                legacyBody.PublishExternal(Submersion, MeanWaterHeight, MaxDeckImmersion, BurialDepth);
        }

        /// Last computed hull-local water level at a half-strip, metres above
        /// the design waterline. side 0 = port, 1 = starboard.
        public float HalfStripLevel(int station, int side)
        {
            if (level == null || station < 0 || station >= stationCount) return 0f;
            return level[station * 2 + (side != 0 ? 1 : 0)];
        }

        /// AmbientFlow + the orbital velocity under the nearest station. For
        /// PaddleDrive: a wheel works against the water it is actually in,
        /// and the hull has already paid for sampling it.
        public Vector3 WaterVelocityNear(Vector3 worldPos)
        {
            if (!configured || rb == null || stationVelocity == null) return AmbientFlow;
            float z = Vector3.Dot(worldPos - rb.position, rb.rotation * Vector3.forward);
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < stationCount; i++)
            {
                float d = Mathf.Abs(data.stations[i].z - z);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return AmbientFlow + stationVelocity[best];
        }

        void OnDrawGizmosSelected()
        {
            if (!configured || data == null || sampleLocal == null) return;
            // One half-strip's share of her weight draws as 1.5 m, so the
            // lift arrows read as a hydrostatic curve along the hull.
            float unit = rb != null ? rb.mass * Physics.gravity.magnitude / Mathf.Max(1, stationCount * 2) : 1f;
            float scale = 1.5f / Mathf.Max(1f, unit);
            for (int i = 0; i < stationCount; i++)
            {
                var st = data.stations[i];
                float span = Mathf.Max(0.01f, st.deckY - st.keelY);
                for (int side = 0; side < 2; side++)
                {
                    int k = i * 2 + side;
                    Vector3 p = Application.isPlaying ? sampleWorld[k] : transform.TransformPoint(sampleLocal[k]);
                    // dry keel = yellow, to the deck edge = blue, over it = red.
                    float wet = (level[k] - st.keelY) / span;
                    Gizmos.color = wet > 1f ? Color.red
                        : Color.Lerp(new Color(1f, 0.9f, 0.2f), new Color(0.1f, 0.4f, 1f), Mathf.Clamp01(wet));
                    Gizmos.DrawSphere(p, 0.18f);
                    if (!Application.isPlaying) continue;
                    // Force arm (sample point -> where the lift acts), then the lift.
                    Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
                    Gizmos.DrawLine(p, forcePoint[k]);
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(forcePoint[k], forcePoint[k] + forceVector[k] * scale);
                }
                if (Application.isPlaying)
                {
                    Gizmos.color = new Color(1f, 0.4f, 0.9f);
                    Gizmos.DrawWireCube(rb.position + rb.rotation * new Vector3(0f, lateralForceY, st.z),
                        new Vector3(0.15f, 0.15f, 0.15f));
                }
            }
            if (rb != null)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawWireSphere(rb.worldCenterOfMass, 0.3f);
            }
        }
    }
}
