using UnityEngine;

namespace SeaSick.Ship
{
    /// Perceptual speed: bow spray and a foam wake that scale with how fast
    /// the hull is actually moving. Built entirely in code — no prefab setup.
    [RequireComponent(typeof(ShipMotor))]
    public class SpeedJuice : MonoBehaviour
    {
        [SerializeField] float sprayFullRate = 130f; // particles/s at max speed
        [SerializeField] float wakeFullRate = 85f;
        [SerializeField] float shoulderRate = 75f;

        // Speed and turn you can SEE (2026-09-24, "slow, uneventful, not
        // responsive"). The bow spray used to top out at 12% of its rate at
        // full speed -- sixteen puffs a second, which at the phone's framing
        // is nothing -- so half ahead and full ahead looked the same. These
        // are the shares of the full rates that speed and turning now reach;
        // `JuiceTuning.sprayScale` multiplies all of it, live.
        [Header("Speed and turn spray")]
        [Tooltip("Share of sprayFullRate the bow throws at top speed (was 0.12).")]
        [SerializeField] float bowSpeedShare = 0.30f;
        [Tooltip("Extra share of sprayFullRate the bow throws in a full-rate turn.")]
        [SerializeField] float bowTurnShare = 0.10f;
        [Tooltip("Share of shoulderRate thrown off the OUTSIDE shoulder in a full-rate turn at top speed.")]
        [SerializeField] float outsideShoulderShare = 0.55f;
        [Tooltip("Turn fraction below which the shoulders stay silent, so a straight course keeps its clean water.")]
        [Range(0f, 0.8f)] [SerializeField] float shoulderTurnGate = 0.25f;
        // Hard caps for the phone. At sprayScale 3 in a hard turn these are
        // what is alive at once, not what is asked for.
        const int BowSprayCap = 260;
        const int ShoulderCap = 180;

        SurfaceWake surfaceWake;
        ShipMotor motor;
        Rigidbody rb;
        ParticleSystem bowSpray;
        ParticleSystem wake;
        ParticleSystem shoulderPort;
        ParticleSystem shoulderStar;
        ParticleSystem sternWash;
        ParticleSystem beamPort;
        ParticleSystem beamStar;
        ParticleSystem wakeLinePort;
        ParticleSystem wakeLineStar;

        // Impact thresholds are RELATIVE to the sea she is in.
        //
        // As absolutes these meant "a hard knock" in the water they were tuned
        // in, and "every other frame" once the western deep started throwing
        // 40m of heave — the same mistake the spindrift crest threshold made.
        // A burst should mark an unusual blow, not the ambient state.
        [Header("Wave impacts")]
        [SerializeField] float beamImpactThreshold = 1.5f;
        [SerializeField] float beamImpactCooldown = 0.45f;
        [Tooltip("How much rougher the sea has to hit before a burst counts, " +
                 "scaled by the sea it is standing in.")]
        [SerializeField] float seaThresholdScale = 3.5f;

        // The bow entering the water is the loudest splash the ship makes, and
        // the honest driver for it is the VERTICAL CLOSING SPEED between the
        // stem and the surface — the metres per second at which the gap shuts.
        // The bow dropping into a trough and a wave face rushing up into a
        // held bow are the same event by this measure, which is what the eye
        // reads as "water hitting the boat". A burst only fires when the stem
        // is within a bandwidth of the surface (real contact, not a slam in
        // mid-air) and the closing speed clears a sea-relative bar.
        [Header("Bow entry splash")]
        [Tooltip("Vertical closing speed (m/s) at which the bow-entry splash reaches full size.")]
        [SerializeField] float entryFullSpeed = 8f;
        [Tooltip("Closing speed (m/s) below which no burst fires, before the sea-relative rise.")]
        [SerializeField] float entryThreshold = 2.2f;
        [SerializeField] float entryCooldown = 0.14f;
        [Tooltip("Metres of surface either side of the stem counted as contact.")]
        [SerializeField] float entryContactBand = 1.4f;

        // The diverging bow wake — the V a moving hull leaves. Laid down in
        // world space so each puff stays on the water where it was dropped and
        // the ship sails out from under it: the trail IS the accumulation of
        // what she emitted a few seconds ago. Rate rises with speed; below a
        // crawl there is no wake to leave.
        [Header("Bow wake trail")]
        [SerializeField] float wakeLineFullRate = 110f;
        [Tooltip("Kelvin half-angle: the wake's arms open this far off her track.")]
        [SerializeField] float wakeHalfAngleDeg = 19.5f;

        float lastBeamImpact = -99f;
        float lastEntry = -99f;
        Transform emitterRoot;

        // --- the rig is HER size, not a typed coordinate ---------------------
        //
        // Every emitter below used to sit at a hard number off the paddle
        // steamer -- bow at z = 9.2, beam at x = +-3.2, stern at z = -9.5 --
        // and nothing resized them when the ladder started swapping hulls
        // underneath. That is the same fault `HullWaterClip` had and it fails
        // the same way at both ends: on the skiff (9.0 x 2.9 m) the bow emitter
        // is 4.7 m PAST her stem, throwing spray off open water ahead of the
        // boat; on the ship of the line (46 x 13 m) z = 9.2 is amidships, x =
        // 3.2 is well inboard of a 6.5 m half-beam and y = 0.8 is six metres
        // below her rail -- so the burst is born INSIDE the hull and comes up
        // through the deck. Which is exactly what Kevin sees in heavy water.
        //
        // Fractions, not metres. They are chosen to reproduce the steamer's
        // rig at the steamer's size and to sit OUTBOARD of the planking on any
        // hull: `HullWaterClip` holds the sea out to 0.41 x beam and 0.40 x
        // length, so anything at or beyond those fractions is over the water.
        const float BowZ = 0.40f;      // x length, from amidships
        const float ShoulderZ = 0.24f;
        const float ShoulderX = 0.42f; // x beam -- just outboard of the clip
        const float BeamX = 0.48f;
        const float SternZ = -0.42f;
        const float WakeZ = -0.38f;
        const float WakeLineZ = 0.22f;
        const float WakeLineX = 0.40f;
        // Where in her freeboard the spray leaves her. The waterline is y = 0
        // in this frame (the same local units `HydrostaticLayout` and the clip
        // ellipse use), so these are fractions of rail height above it and a
        // burst can never start below the surface however she is loaded.
        const float LowY = 0.10f, WashY = 0.06f, BeamY = 0.45f;

        float hullLength = 24.2f;   // the steamer, so an unconfigured rig is
        float hullBeam = 8.5f;      // the rig this file shipped with
        float hullRailY = 1.9f;
        float sizeScale = 1f;

        /// Called by `Shipyard.Refit` for every rung of the ladder. Sizes the
        /// whole foam rig to the hull standing in it -- the fleet hulls and
        /// anything else that never comes through the yard keep the defaults.
        public void ConfigureForHull(float length, float beam, float railY)
        {
            hullLength = Mathf.Max(2f, length);
            hullBeam = Mathf.Max(1f, beam);
            hullRailY = Mathf.Max(0.3f, railY);
            // Particle SIZE cannot scale with her length or a three-decker
            // throws cotton wool and a skiff disappears in it. Spray breaks up
            // into droplets at a scale set by the water, not by the boat, so
            // it grows far slower than she does: the square root puts a 46 m
            // hull's spray at 1.4x the steamer's, not 1.9x.
            sizeScale = Mathf.Sqrt(hullLength / 24.2f);
            if (emitterRoot != null) ApplyRig();
            if (surfaceWake != null) surfaceWake.Configure(hullLength, hullBeam);
        }

        void Start()
        {
            motor = GetComponent<ShipMotor>();
            rb = GetComponent<Rigidbody>();
            surfaceWake = GetComponent<SurfaceWake>();
            if (!surfaceWake) surfaceWake = gameObject.AddComponent<SurfaceWake>();
            surfaceWake.Configure(hullLength, hullBeam);
            // The engine note rides with the same rig: SpeedJuice is only
            // ever on the player's ShipMotor (raiders are EnemyShip, not
            // ShipMotor), so this is the one place that means "her".
            if (!GetComponent<PaddleSound>()) gameObject.AddComponent<PaddleSound>();

            // Every emitter hangs off this rather than off the hull directly,
            // so the whole rig can be held at the waterline as she settles.
            var rootGo = new GameObject("FoamEmitters");
            rootGo.transform.SetParent(transform, false);
            emitterRoot = rootGo.transform;

            // Solid, lit foam. Translucent billboards read as grey squares over
            // dark water; opaque chunks that catch the sun read as real spray
            // and thrown water. These shrink away instead of fading out.
            var solid = new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));
            solid.SetColor("_BaseColor", new Color(0.97f, 0.99f, 1f, 1f));
            solid.SetFloat("_Smoothness", 0.35f);
            // Opaque cannot fade, but it CAN be clipped to a disc. Without
            // this the bursts are square, which went unnoticed while they were
            // small, bright and lit by a strong sun — and filled the screen
            // with grey cardboard the moment the storm sea started throwing
            // them in numbers under a dark sky.
            solid.SetTexture("_BaseMap", SeaSick.Ocean.FoamTexture.SoftPuff());
            solid.SetFloat("_AlphaClip", 1f);
            solid.SetFloat("_Cutoff", 0.45f);
            solid.EnableKeyword("_ALPHATEST_ON");

            // The long wake still fades, so it dissolves into the sea rather
            // than popping out of existence behind you.
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.5f));
            mat.SetTexture("_BaseMap", SeaSick.Ocean.FoamTexture.SoftPuff());
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;

            // Positions are placed by `ApplyRig` at the end of Start, off the
            // hull she is actually wearing; Vector3.zero here is a placeholder.
            bowSpray = MakeSystem("BowSpray", Vector3.zero, solid, size: 0.5f,
                speed: 5.5f, spreadAngle: 62f, lifetime: 1.0f, gravity: 1.3f, solidFoam: true);
            wake = MakeSystem("WakeFoam", Vector3.zero, mat, size: 0.8f,
                speed: 1.0f, spreadAngle: 42f, lifetime: 5.5f, gravity: 0f, solidFoam: false);

            // Foam where the hull actually parts the water, thrown out along
            // the shoulders rather than straight back. Solid — this is the
            // water being displaced, and it should look like it has mass.
            shoulderPort = MakeSystem("ShoulderPort", Vector3.zero, solid,
                size: 1f, speed: 2.4f, spreadAngle: 34f, lifetime: 2.6f, gravity: 0.25f, solidFoam: true);
            shoulderStar = MakeSystem("ShoulderStar", Vector3.zero, solid,
                size: 1f, speed: 2.4f, spreadAngle: 34f, lifetime: 2.6f, gravity: 0.25f, solidFoam: true);
            shoulderPort.transform.localRotation = Quaternion.Euler(-8f, -118f, 0f);
            shoulderStar.transform.localRotation = Quaternion.Euler(-8f, 118f, 0f);

            // Seas breaking against the beam — burst-emitted on impact rather
            // than streamed, thrown up and outboard.
            beamPort = MakeSystem("BeamSprayPort", Vector3.zero, solid,
                size: 0.7f, speed: 7f, spreadAngle: 40f, lifetime: 1.3f, gravity: 1.6f, solidFoam: true);
            beamStar = MakeSystem("BeamSprayStar", Vector3.zero, solid,
                size: 0.7f, speed: 7f, spreadAngle: 40f, lifetime: 1.3f, gravity: 1.6f, solidFoam: true);

            // Churn right under the transom — solid, close in, short-lived.
            sternWash = MakeSystem("SternWash", Vector3.zero, solid,
                size: 0.7f, speed: 1.6f, spreadAngle: 55f, lifetime: 1.8f, gravity: 0.15f,
                solidFoam: true);

            // The diverging wake arms, emitted from the bow shoulders where the
            // hull first parts the water. Translucent so they dissolve into the
            // sea rather than popping out; long-lived so the V reaches far
            // astern; nearly no forward speed and a low outward drift, so once
            // laid down each puff essentially holds its water while the ship
            // pulls ahead. Yaw them off the stern line by the Kelvin half-angle
            // and let them widen over life into a spreading crest.
            wakeLinePort = MakeSystem("WakeLinePort", Vector3.zero, mat,
                size: 1.7f, speed: 0.7f, spreadAngle: 14f, lifetime: 6.5f, gravity: 0f,
                solidFoam: false);
            wakeLineStar = MakeSystem("WakeLineStar", Vector3.zero, mat,
                size: 1.7f, speed: 0.7f, spreadAngle: 14f, lifetime: 6.5f, gravity: 0f,
                solidFoam: false);
            // Point each arm aft and outboard by the wake half-angle (180 deg is
            // dead astern; open it outward from there).
            wakeLinePort.transform.localRotation =
                Quaternion.Euler(0f, 180f + wakeHalfAngleDeg, 0f);
            wakeLineStar.transform.localRotation =
                Quaternion.Euler(0f, 180f - wakeHalfAngleDeg, 0f);
            // The wake arm is a crest, not a burst of spume: it grows a little
            // as it spreads, then fades, rather than shrinking away.
            WidenOverLife(wakeLinePort);
            WidenOverLife(wakeLineStar);

            // Bursts keep their 1200 headroom elsewhere; the three emitters
            // that now stream with speed and turn are capped for the phone.
            SetCap(bowSpray, BowSprayCap);
            SetCap(shoulderPort, ShoulderCap);
            SetCap(shoulderStar, ShoulderCap);

            ApplyRig();
        }

        static void SetCap(ParticleSystem ps, int max)
        {
            if (ps == null) return;
            var main = ps.main;
            main.maxParticles = max;
        }

        /// Put every emitter where THIS hull's water is. Re-run whenever she
        /// changes size, which on the ladder is every upgrade.
        void ApplyRig()
        {
            float L = hullLength, B = hullBeam, R = hullRailY;
            Place(bowSpray, new Vector3(0f, R * LowY, L * BowZ), 0.5f);
            Place(wake, new Vector3(0f, R * WashY, L * WakeZ), 0.8f);
            Place(shoulderPort, new Vector3(-B * ShoulderX, R * LowY, L * ShoulderZ), 1f);
            Place(shoulderStar, new Vector3(B * ShoulderX, R * LowY, L * ShoulderZ), 1f);
            Place(beamPort, new Vector3(-B * BeamX, R * BeamY, L * 0.02f), 0.7f);
            Place(beamStar, new Vector3(B * BeamX, R * BeamY, L * 0.02f), 0.7f);
            Place(sternWash, new Vector3(0f, R * WashY, L * SternZ), 0.7f);
            Place(wakeLinePort, new Vector3(-B * WakeLineX, R * WashY, L * WakeLineZ), 1.7f);
            Place(wakeLineStar, new Vector3(B * WakeLineX, R * WashY, L * WakeLineZ), 1.7f);
        }

        void Place(ParticleSystem ps, Vector3 localPos, float baseSize)
        {
            if (ps == null) return;
            ps.transform.localPosition = localPos;
            var main = ps.main;
            float sz = baseSize * sizeScale;
            main.startSize = new ParticleSystem.MinMaxCurve(sz * 0.5f, sz);
        }

        /// A crest that broadens as it drifts, for the wake arms — the opposite
        /// of the solid foam's shrink-to-nothing.
        static void WidenOverLife(ParticleSystem ps)
        {
            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            var curve = new AnimationCurve(
                new Keyframe(0f, 0.6f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.15f));
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        ParticleSystem MakeSystem(string name, Vector3 localPos, Material mat,
            float size, float speed, float spreadAngle, float lifetime, float gravity,
            bool solidFoam)
        {
            var go = new GameObject(name);
            go.transform.SetParent(emitterRoot != null ? emitterRoot : transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            main.startLifetime = lifetime;
            main.gravityModifier = gravity;
            main.startColor = solidFoam ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1200;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = spreadAngle;
            shape.radius = 0.3f;

            if (solidFoam)
            {
                // Opaque foam can't fade, so it shrinks out of existence —
                // which also reads as spray breaking up.
                var sizeOverLife = ps.sizeOverLifetime;
                sizeOverLife.enabled = true;
                var curve = new AnimationCurve(
                    new Keyframe(0f, 0.55f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f));
                sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, curve);

                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
            }
            else
            {
                var colorOverLife = ps.colorOverLifetime;
                colorOverLife.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
                colorOverLife.color = grad;
            }

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;

            return ps;
        }

        /// Every emitter is pinned at a fixed height on the hull, which was
        /// fine while the hull always floated at the same depth. Cargo sinks
        /// her up to ~0.9m now, which dragged the foam emitters under the
        /// surface — a loaded ship grew a flat grey sheet through her waist.
        /// Lift the whole rig by however far she has settled.
        void HoldAtWaterline()
        {
            if (emitterRoot == null) return;
            float sink = motor != null ? motor.SinkDepth : 0f;
            var p = emitterRoot.localPosition;
            emitterRoot.localPosition = new Vector3(p.x, sink, p.z);
        }

        /// The beam bursts are aimed about the WORLD vertical, not about her
        /// deck.
        ///
        /// They used to be `Euler(-52, +-90, 0)` in ship space, which is "up
        /// and outboard" only while she is upright. A sea that hits hard enough
        /// to fire one is a sea that has her over: at 30 degrees of heel that
        /// cone points across her own deck, and in a storm she is rarely
        /// anywhere else — which is the other half of the spray-through-the-
        /// deck complaint, and the half that no amount of moving the emitter
        /// fixes. Water thrown up off the beam goes UP, whatever the boat is
        /// doing underneath it.
        void AimBeamSpray()
        {
            AimOutboard(beamStar, transform.right);
            AimOutboard(beamPort, -transform.right);
        }

        static void AimOutboard(ParticleSystem ps, Vector3 outboard)
        {
            if (ps == null) return;
            Vector3 flat = new Vector3(outboard.x, 0f, outboard.z);
            if (flat.sqrMagnitude < 1e-4f) return;   // beam-on to vertical: hold
            flat.Normalize();
            // Slerp between two perpendicular unit vectors is linear in angle,
            // so 0.58 is 52 degrees above the horizon — the elevation the old
            // ship-space Euler was asking for, now measured from the sea.
            Vector3 dir = Vector3.Slerp(flat, Vector3.up, 0.58f);
            ps.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        void Update()
        {
            HoldAtWaterline();
            AimBeamSpray();
            // Read every frame: the lab moves these live.
            float spray = Mathf.Max(0f, JuiceTuning.sprayScale);
            float s01 = JuiceTuning.Speed01(motor);
            float yaw = JuiceTuning.YawRateDeg(rb);
            float turn01 = JuiceTuning.Turn01(motor, yaw);
            // Spray kicks in hard when the bow drops onto a wave face.
            float slam = Mathf.Clamp01(-motor.SurfAccel / 2.5f);
            // A turn only throws water if she is moving through it.
            float turnSpray = turn01 * Mathf.Sqrt(s01);
            SetRate(bowSpray, sprayFullRate * spray
                * (Mathf.Pow(s01, 1.4f) * bowSpeedShare + turnSpray * bowTurnShare + slam * 0.35f));
            // Continuous foam is the water-following SurfaceWake mesh.
            // Keep these legacy emitters silent; impact spray remains airborne.
            SetRate(wake, 0);
            SetRate(sternWash, 0);

            // ...except the OUTSIDE shoulder in a real turn. Turning to
            // starboard she skids: the water comes at her from ahead and to
            // port, and the port bow is the one that shoulders it aside.
            // Gated so a straight course stays as clean as it was.
            float bite = Mathf.InverseLerp(shoulderTurnGate, 1f, turn01) * Mathf.Pow(s01, 0.8f);
            float shoulder = shoulderRate * outsideShoulderShare * spray * bite;
            SetRate(shoulderPort, yaw > 0f ? shoulder : 0f);
            SetRate(shoulderStar, yaw < 0f ? shoulder : 0f);

            // The wake arms need real way before there is a wake at all, and
            // they lengthen with speed — a fast hull throws a longer, denser V.
            SetRate(wakeLinePort, 0);
            SetRate(wakeLineStar, 0);

            BowEntry();
            WaveImpacts();
        }

        /// The bow entering the water — driven by how fast the surface and the
        /// stem are closing, so a hard drop into a trough and a face flung up
        /// into the bow both read as one splash that scales with the blow.
        void BowEntry()
        {
            if (rb == null || !SeaSick.Ocean.OceanSampler.Ready) return;

            // Her stem, not the steamer's. On the skiff the old number was
            // 4.7 m ahead of the boat, so this test was reading a patch of
            // open water and splashing there.
            Vector3 stem = transform.TransformPoint(
                new Vector3(0f, hullRailY * LowY, hullLength * BowZ));
            var sample = SeaSick.Ocean.OceanSampler.SampleImmediate(stem);
            // Only while the stem is actually at the water, not slamming in air.
            if (Mathf.Abs(stem.y - sample.height) > entryContactBand) return;

            // Vertical closing speed: how fast the gap between hull and surface
            // is shutting. Positive when the bow drives down or the water leaps
            // up to meet it.
            float hullVelY = rb.GetPointVelocity(stem).y;
            float closing = sample.velocity.y - hullVelY;
            // The bar rises with the sea, the way the beam and slam bars do —
            // in a big sea every entry is violent and a burst must mark an
            // unusual one, not the ambient state.
            float bar = entryThreshold * (1f + motor.SeaSeverity01 * seaThresholdScale);
            if (closing < bar || Time.time - lastEntry < entryCooldown) return;

            lastEntry = Time.time;
            float force = Mathf.Clamp01((closing - bar) / Mathf.Max(0.5f, entryFullSpeed));
            if (bowSpray != null)
                bowSpray.Emit(Mathf.RoundToInt(Mathf.Lerp(8f, 55f, force)));
            // The water itself is displaced where she lands — a real dent and
            // ring of foam the surface shader composites, sized by the blow.
            // The dent she leaves is the size of the boat that made it.
            Ocean.DynamicWaterSim.Splash(
                new Vector3(stem.x, sample.height, stem.z),
                Mathf.Clamp(hullBeam * 0.7f, 2.5f, 14f), 0.5f + 1.1f * force);
        }

        /// Spray off the BEAM when a wave shoulders into the side — thrown up
        /// and outboard, and stamped into the wake buffer so the mark stays on
        /// the water after the spray itself has gone. The bow entry is handled
        /// by its own closing-speed test in `BowEntry`.
        void WaveImpacts()
        {
            // In a big sea everything is a heavy blow, so the bar has to rise
            // with the water or the effect becomes the weather.
            float bar = 1f + motor.SeaSeverity01 * seaThresholdScale;
            float beamBar = beamImpactThreshold * bar;

            float lateral = motor.LateralWaveAccel;
            if (Mathf.Abs(lateral) > beamBar
                && Time.time - lastBeamImpact > beamImpactCooldown)
            {
                lastBeamImpact = Time.time;
                float force = Mathf.Clamp01((Mathf.Abs(lateral) - beamBar) / (2.5f * bar));
                var side = lateral > 0f ? beamStar : beamPort;
                if (side != null) side.Emit(Mathf.RoundToInt(Mathf.Lerp(10f, 45f, force)));

                Vector3 at = transform.position
                           + transform.right * (hullBeam * 0.5f * (lateral > 0f ? 1f : -1f));
                Ocean.DynamicWaterSim.Splash(
                    at, Mathf.Clamp(hullBeam * 0.7f, 2.5f, 14f), 0.5f + force);
            }
        }

        static void SetRate(ParticleSystem ps, float rate)
        {
            var e = ps.emission;
            e.rateOverTime = rate;
        }
    }
}
