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
        [SerializeField] float maxPlowDecel = 4f;
        // Dynamic (planing) lift. Reserve buoyancy resists burial with DEPTH;
        // this resists it with SPEED, which is what a real hull does — water a
        // moving bow deflects pushes back, and on angled bow sections most of
        // that push is upward. It matters because the previous answer to
        // burial was plow drag, a BRAKE, so going faster cost speed to stay
        // dry. Lift inverts that: faster means more lift means drier, and she
        // keeps the speed. Measured at maxSpeed 40 in severity-1.0 head seas:
        // without it 23.0% of the run had the deck under with 6.47 m over it
        // and the rails swamped 38.5% of the time; with it, 0.0% and 0.0%.
        [Tooltip("Lift coefficient in 1/2 rho v^2 A Cl on the area proxy pi*r^2. Acts only where the hull is driven PAST its static waterline, so it is exactly zero at rest and the calm float equilibrium is untouched.")]
        [SerializeField] float dynamicLiftCoeff = 0.12f;
        [Tooltip("Metres of bury past full probe submersion at which dynamic lift reaches full strength.")]
        [SerializeField] float dynamicLiftDepth = 1f;
        [Tooltip("Ceiling on total dynamic lift, m/s^2 — bounded so a fast bow is held up, never thrown clear. The failure mode this guards is porpoising.")]
        [SerializeField] float maxDynamicLiftAccel = 10f;
        [Tooltip("Weight applied to lift on probes AFT of midships. Planing lift is generated where the hull meets oncoming flow, not where flow leaves it. Applying it at every immersed probe was measured and reverted: with the stern deep and the bow up, lift at the sternpost pitches her nose DOWN and the deck got wetter than with no lift at all (deckOverMax -1.00 m -> +0.75 m).")]
        [SerializeField, Range(0f, 1f)] float dynamicLiftAftWeight = 0f;
        [Tooltip("Fraction of reserve LIFT kept while the probe rises relative to the water (ramp over 1 m/s). Only the lift bleeds — reserve damping and plow drag stay at full strength, so this is an asymmetric shock absorber: full catch on the way in, no spring-return pogo on the way out (symmetric lift measured 6-9 s calm settle vs 1.75 s baseline).")]
        [SerializeField, Range(0f, 1f)] float reserveUpwardKeep = 0.3f;
        [Header("Burial clamp — the floor under everything else")]
        [Tooltip("Metres of green water over the deepest rail probe past which the hull is treated as BURIED and pulled back out. Buoyancy is a force and forces lose races: on the worst waves this sea produces, the surface accelerates downward at 0.8 g, and no floating body can follow that -- the crest simply passes over her. Reserve buoyancy, plow drag and dynamic lift all push the right way and can still be outrun. This is the bound that cannot be.")]
        [SerializeField] float burialDepth = 1.2f;
        [Tooltip("Seconds to pull her back to the burial depth once past it. Fast enough that she never swims, slow enough that it reads as the sea letting go of her rather than a hand lifting her out.")]
        [SerializeField] float burialRecovery = 0.45f;
        [Tooltip("Ceiling on the clamp's acceleration, m/s^2. Bounded so a freak wave cannot fire her out of the water — the failure this guards against is a hull popping up like a beach ball, which reads far worse than the burial did.")]
        [SerializeField] float maxBurialAccel = 14f;

        [Tooltip("Explicit inertia box (m) — a colliderless Rigidbody defaults to unit inertia and spins like a coin.")]
        [SerializeField] Vector3 inertiaBoxDims = new Vector3(4.4f, 3f, 13f);
        [SerializeField] Vector3 centreOfMass = new Vector3(0f, -0.6f, 0f);

        Rigidbody rb;
        BuoyancyProbeSet probeSet;
        // Plow is clamped against a total, so it is applied in a second pass.
        // Preallocated: this runs every FixedUpdate.
        Vector3[] plowScratch;
        Vector3[] plowAt;
        Vector3[] liftScratch;
        float probeHalfLength;
        // Filled by FillQueries, read back by ApplyForces a few lines later
        // in the same physics step (OceanPhysicsDriver always calls both,
        // FillQueries first, with no transform-moving work in between) --
        // so ApplyForces reuses these instead of calling TransformPoint on
        // every probe a second time.
        Vector3[] probeWorldCache;

        /// Share-weighted submersion this step, 0..1.
        public float Submersion { get; private set; }
        /// Mean water height over the probes this step.
        public float MeanWaterHeight { get; private set; }
        /// Net wave force applied this step (buoyancy horizontal + drag), N.
        public Vector3 WaveForce { get; private set; }
        /// Deepest green water over any rail probe this step, metres (can be < 0).
        public float MaxRailImmersion { get; private set; }
        /// True while the burial clamp is holding her out of the sea.
        public bool Buried { get; private set; }
        /// Metres past the burial threshold this step. Bilge reads it: going
        /// under should cost more water than a wetting, and the green-water
        /// ingress alone is capped and cannot express the difference.
        public float BurialDepth { get; private set; }
        /// Clamp acceleration applied this step, m/s^2. Diagnostic.
        public float DebugBurialAccel { get; private set; }
        /// Debug decomposition of this step's drag (world space, N).
        public Vector3 DebugDragForward { get; private set; }
        public Vector3 DebugDragLateral { get; private set; }
        public Vector3 DebugDragVertical { get; private set; }
        /// The plow-drag share of DebugDragForward this step (world space, N),
        /// and the deepest reserve any probe reached. Diagnostic only.
        public Vector3 DebugPlowForward { get; private set; }
        public float DebugMaxReserve { get; private set; }
        /// Dynamic (planing) lift actually applied this step, N.
        public Vector3 DebugDynamicLift { get; private set; }
        /// Live tuning surface for A/B probes — set plow to 0 to isolate it.
        public float PlowDragFactor { get => plowDragFactor; set => plowDragFactor = value; }
        public float ReservePerMetre { get => reservePerMetre; set => reservePerMetre = value; }
        public float PlowOnset { get => plowOnset; set => plowOnset = value; }
        public float MaxPlowDecel { get => maxPlowDecel; set => maxPlowDecel = value; }
        public float DynamicLiftCoeff { get => dynamicLiftCoeff; set => dynamicLiftCoeff = value; }
        /// Extra metres the body should sit below its light waterline (cargo,
        /// bilge water). Lowers the float equilibrium without touching mass.
        public float SeatOffset { get; set; }
        /// Macroscopic water motion (drift/current) added to the orbital
        /// velocity as the drag reference — the hull is carried, not shoved.
        public Vector3 AmbientFlow { get; set; }
        /// Displaced volume at full submersion, m^3 — the rig's capacity.
        /// Read by the float gate to check a layout against the hull's book.
        public float TotalVolume => totalVolume;
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

        /// Re-fit this body to a different hull, at runtime.
        ///
        /// Exists because the modular ladder swaps hulls while the game is
        /// running and every one of these numbers is a property of the HULL,
        /// not of the ship object. The rules are the fleet's own, unchanged:
        /// displaced volume from the hull's stations, damping scaled by the
        /// mass ratio against what the coefficients were tuned at, and angular
        /// damping additionally by (LOA/tuned)^2.
        ///
        /// It re-does the rigidbody work `Awake` does, because the inertia
        /// tensor is derived from `rb.mass` and a hull swap changes the mass by
        /// up to 360x across the ladder. Setting the fields alone leaves a
        /// three-decker turning on a skiff's inertia.
        public void ConfigureForHull(float displacedVolume, float floatRatio,
                                     float dampingScale, float loaScale,
                                     Vector3 boxDims, Vector3 com)
        {
            totalVolume = displacedVolume / Mathf.Max(0.01f, floatRatio);
            linearDrag = 28000f * dampingScale;
            quadraticDrag = 3000f * dampingScale;
            angularDragTorque = 30000f * dampingScale * loaScale * loaScale;
            inertiaBoxDims = boxDims;
            centreOfMass = com;

            if (rb == null) rb = GetComponent<Rigidbody>();
            if (rb == null) return;
            rb.centerOfMass = centreOfMass;
            var d = inertiaBoxDims;
            float m = rb.mass / 12f;
            rb.inertiaTensor = new Vector3(
                m * (d.y * d.y + d.z * d.z),
                m * (d.x * d.x + d.z * d.z),
                m * (d.x * d.x + d.y * d.y));
            rb.inertiaTensorRotation = Quaternion.identity;
        }

        /// The half of `ConfigureForHull` that was missing: every threshold
        /// that decides when the SEA is allowed to have her is authored in
        /// metres, and a metre is a different fraction of every hull on the
        /// ladder. The documented trap — "ship tuning authored as absolute
        /// metres does not survive a change of hull" — except these five were
        /// never on the list, so the burial clamp only engaged once green
        /// water stood 1.2 m over the rail: a wetting on the three-decker's
        /// 7 m of freeboard, and a metre PAST fully swallowed on the skiff's
        /// 0.9. Measured on the Long boat before this: 20.61 m of water over
        /// her deck at the worst instant of the standing burial gate.
        ///
        /// Everything scales DOWN from the brig and never up: the constants
        /// were tuned at her scale and the hulls above her pass the gates at
        /// those values, so a bigger hull keeps them (relative to her size
        /// they are already generous) and a smaller hull gets them shrunk to
        /// mean the same thing they meant on the brig. All five are zero at
        /// the calm float equilibrium, so statics are untouched by
        /// construction.
        ///
        /// Values are computed from named constants, never by scaling the
        /// serialized fields — those may be stale (the serialization trap),
        /// and scaling them would compound on every re-apply.
        public void ConfigureWaveResponse(float freeboard, float draft, float speedScale)
        {
            // The brig — rung 12, the hull Kevin art-directed and the gates ran on.
            const float TunedFreeboard = 2.70f;
            float fb = Mathf.Max(0.3f, freeboard);
            // HALF her freeboard of green water over the deck edge, and no
            // more — the sea may sweep her deck, never swallow her. The cap
            // keeps the top of the ladder at what it already passes with.
            // Half, not the third first tried: at 0.3 x freeboard the clamp
            // held the Long boat INSIDE the crests instead of letting her
            // knife through them, the crests swept her way off (1.8 m/s
            // against 7.9 unclamped), and a boat with no way is a boat the
            // waves wash over — swallowed time went UP, 13.9% -> 21.3%.
            burialDepth = Mathf.Clamp(0.50f * fb, 0.25f, 1.00f);
            // The slam penalty at speeds she can actually reach: 4.5 m/s is
            // cruising for the brig and flat out for the skiff, so a fixed
            // floor turned the penalty off on exactly the hulls that bury
            // easiest.
            plowSpeedFloor = 4.5f * Mathf.Min(1f, speedScale);
            // The brake must out-muscle the sail by the same MARGIN on every
            // hull. Propulsion is a rate-limited servo whose authority grows
            // as 1/sqrt(L) on smaller hulls (ShipMotor.accelScale), so the
            // brig's 4.0 ceiling against her 2.6 of sail left 1.4 m/s^2 of
            // net brake — and the Long boat's 3.3 of sail against the same
            // 4.0 left 0.7. This alone was measured doing nothing (plow's
            // mean was 0.24 m/s^2 while she speared — the term barely
            // fires); the knob that actually slowed her is the hull-relative
            // head-sea rule in ShipMotor. Kept because the margin argument
            // stands for the slams plow DOES catch.
            maxPlowDecel = 4f * Mathf.Clamp(1f / Mathf.Max(0.1f, speedScale), 1f, 2f);
            //
            // NOT scaled, all three measured at severity 0.60 on the Long
            // boat, one knob at a time:
            //   `reservePerMetre` — reserve arrives with MATCHING DAMPING by
            //   design, and 3x the rate glued her to the falling back of
            //   every wave: swallowed time went 2.8% -> 8.4%.
            //   `dynamicLiftDepth` / `plowOnset` — both shape the PITCH
            //   COUPLE, not the height she rides at; shrinking the lift ramp
            //   to 0.41 m multiplied bow lift ~2.4x at shallow bury, which
            //   reproduced the stern-under failure the area fix had just
            //   cured (poopDeckUnder 40.5% vs 46.8%, draft 16.7 m vs 21.95).
            //   More bow lift pitches the STERN in; the deck gets wetter.
        }

        void OnEnable() => OceanPhysicsDriver.Register(this);
        void OnDisable() => OceanPhysicsDriver.Unregister(this);

        public void FillQueries(System.Span<Vector3> dst)
        {
            var probes = probeSet.Probes;
            if (probeWorldCache == null || probeWorldCache.Length != probes.Length)
                probeWorldCache = new Vector3[probes.Length];
            for (int i = 0; i < probes.Length; i++)
            {
                Vector3 w = transform.TransformPoint(probes[i].localPosition);
                probeWorldCache[i] = w;
                dst[i] = w;
            }
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
            Vector3 liftTotal = Vector3.zero;

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
                liftScratch = new Vector3[probes.Length];
                probeHalfLength = 0f;
                for (int i = 0; i < probes.Length; i++)
                {
                    float az = Mathf.Abs(probes[i].localPosition.z);
                    if (az > probeHalfLength) probeHalfLength = az;
                }
            }
            // Cleared every step: a probe that leaves the water skips the body
            // of the loop, and a stale plow vector would keep braking her.
            for (int i = 0; i < probes.Length; i++)
            {
                plowScratch[i] = Vector3.zero;
                liftScratch[i] = Vector3.zero;
            }

            for (int i = 0; i < probes.Length; i++)
            {
                Vector3 world = probeWorldCache[i];
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

                // Dynamic lift, weighted forward: full at the stem, zero at
                // midships, none aft. Deferred to pass 2 like plow, because it
                // is capped against a total.
                if (over > 0f && forwardWay > 0f)
                {
                    float fore = probeHalfLength > 0.01f
                        ? probes[i].localPosition.z / probeHalfLength : 0f;
                    float foreWeight = Mathf.Clamp01(
                        fore >= 0f ? fore : -fore * dynamicLiftAftWeight);
                    if (foreWeight > 0f)
                    {
                        float liftDepth = Mathf.Clamp01(
                            over / Mathf.Max(0.01f, dynamicLiftDepth));
                        // The probe's own WATERPLANE area, not a disc cut from
                        // its ramp. `radius` is the band a probe takes to go
                        // from dry to wet; it was standing in for "how much
                        // hull is presenting itself to the flow" only because
                        // nothing better was to hand. Now there is:
                        // capacity/radius is the area whose immersion makes
                        // this probe's lift, which is an area in the same
                        // sense a waterplane is.
                        //
                        // Measured, and it is not cosmetic. `HydrostaticLayout`
                        // gives the keel group a ramp twice as wide as the old
                        // rig's -- forced, because the ramp has to equal twice
                        // its own depth for the probe to be exactly submerged
                        // and exactly at the reserve onset. Squared, that was
                        // 3x the bow-up lift, all of it forward, and she sailed
                        // her stern under: poop deck submerged 46.8% of a
                        // 60 s storm run against 10.9%, mean draft 21.95 m
                        // against 0.41.
                        float area = totalVolume * probes[i].volumeShare
                                     / Mathf.Max(0.01f, probes[i].radius);
                        float lift = 0.5f * waterDensity * forwardWay * forwardWay
                                     * area * dynamicLiftCoeff * liftDepth * foreWeight;
                        liftScratch[i] = Vector3.up * lift;
                        liftTotal += liftScratch[i];
                    }
                }

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

            // Dynamic lift, capped as a total and applied at the probes so a
            // buried bow gets a bow-UP couple — the opposite sign to plow's.
            float liftMax = maxDynamicLiftAccel * rb.mass;
            float liftMag = liftTotal.magnitude;
            float liftScale = liftMag > liftMax && liftMag > 1e-3f
                ? liftMax / liftMag : 1f;
            if (liftMag > 1e-3f)
            {
                for (int i = 0; i < probes.Length; i++)
                {
                    if (liftScratch[i].sqrMagnitude <= 0f) continue;
                    rb.AddForceAtPosition(liftScratch[i] * liftScale, plowAt[i]);
                }
            }
            DebugDynamicLift = liftTotal * liftScale;

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

            ApplyBurialClamp(Time.fixedDeltaTime);
        }

        /// EASE, THEN CLAMP. The pattern this project already had to learn once,
        /// for the vertical lag: bound the error in metres against the boat's
        /// own freeboard, so it cannot be swallowed whatever the sea does.
        ///
        /// Everything above this line is a FORCE, and a force can be outrun. At
        /// 0.8 g of downward surface acceleration -- which the worst waves in
        /// this sea genuinely reach -- buoyancy is not merely losing, it is
        /// physically incapable of keeping up, because the water is falling away
        /// nearly as fast as gravity pulls her down into it. No amount of
        /// reserve buoyancy fixes that; it is a race that cannot be won.
        ///
        /// So this is not physics and does not pretend to be. It is a bound,
        /// deliberately: past `burialDepth` she is pulled back toward it on a
        /// fixed time constant, capped, and acting ONLY on the way down. Above
        /// the threshold it contributes exactly nothing, so the float
        /// equilibrium, the gates and every measured value are untouched.
        void ApplyBurialClamp(float dt)
        {
            Buried = false;
            BurialDepth = 0f;
            if (dt <= 0f || rb == null) return;

            float over = MaxRailImmersion - burialDepth;
            if (over <= 0f) return;

            Buried = true;
            BurialDepth = over;

            // Only ever upward, and only while she is still going down or
            // rising too slowly to clear it. Pushing on a hull that is already
            // on her way out is what turns a clamp into a trampoline.
            float want = over / Mathf.Max(burialRecovery, 0.05f);
            float have = rb.linearVelocity.y;
            if (have >= want) return;

            float accel = Mathf.Min((want - have) / Mathf.Max(dt, 1e-4f), maxBurialAccel);
            rb.AddForce(Vector3.up * (accel * rb.mass), ForceMode.Force);
            DebugBurialAccel = accel;
        }
    }
}
