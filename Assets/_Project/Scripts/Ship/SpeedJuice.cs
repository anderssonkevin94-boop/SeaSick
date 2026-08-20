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

        ShipMotor motor;
        ParticleSystem bowSpray;
        ParticleSystem wake;
        ParticleSystem shoulderPort;
        ParticleSystem shoulderStar;
        ParticleSystem sternWash;
        ParticleSystem beamPort;
        ParticleSystem beamStar;

        // Impact thresholds are RELATIVE to the sea she is in.
        //
        // As absolutes these meant "a hard knock" in the water they were tuned
        // in, and "every other frame" once the western deep started throwing
        // 40m of heave — the same mistake the spindrift crest threshold made.
        // A burst should mark an unusual blow, not the ambient state.
        [Header("Wave impacts")]
        [SerializeField] float beamImpactThreshold = 1.5f;
        [SerializeField] float beamImpactCooldown = 0.45f;
        [SerializeField] float slamThreshold = 1.9f;
        [SerializeField] float slamCooldown = 0.5f;
        [Tooltip("How much rougher the sea has to hit before a burst counts, " +
                 "scaled by the sea it is standing in.")]
        [SerializeField] float seaThresholdScale = 3.5f;

        float lastBeamImpact = -99f;
        float lastSlam = -99f;
        float prevSurf;
        Transform emitterRoot;

        void Start()
        {
            motor = GetComponent<ShipMotor>();

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
            solid.SetTexture("_BaseMap", SeaSick.Ocean2.FoamTexture.SoftPuff());
            solid.SetFloat("_AlphaClip", 1f);
            solid.SetFloat("_Cutoff", 0.45f);
            solid.EnableKeyword("_ALPHATEST_ON");

            // The long wake still fades, so it dissolves into the sea rather
            // than popping out of existence behind you.
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.5f));
            mat.SetTexture("_BaseMap", SeaSick.Ocean2.FoamTexture.SoftPuff());
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;

            bowSpray = MakeSystem("BowSpray", new Vector3(0f, 0.4f, 9.2f), solid, size: 0.5f,
                speed: 5.5f, spreadAngle: 62f, lifetime: 1.0f, gravity: 1.3f, solidFoam: true);
            wake = MakeSystem("WakeFoam", new Vector3(0f, 0.15f, -8.6f), mat, size: 0.8f,
                speed: 1.0f, spreadAngle: 42f, lifetime: 5.5f, gravity: 0f, solidFoam: false);

            // Foam where the hull actually parts the water, thrown out along
            // the shoulders rather than straight back. Solid — this is the
            // water being displaced, and it should look like it has mass.
            shoulderPort = MakeSystem("ShoulderPort", new Vector3(-2.4f, 0.15f, 5.5f), solid,
                size: 1f, speed: 2.4f, spreadAngle: 34f, lifetime: 2.6f, gravity: 0.25f, solidFoam: true);
            shoulderStar = MakeSystem("ShoulderStar", new Vector3(2.4f, 0.15f, 5.5f), solid,
                size: 1f, speed: 2.4f, spreadAngle: 34f, lifetime: 2.6f, gravity: 0.25f, solidFoam: true);
            shoulderPort.transform.localRotation = Quaternion.Euler(-8f, -118f, 0f);
            shoulderStar.transform.localRotation = Quaternion.Euler(-8f, 118f, 0f);

            // Seas breaking against the beam — burst-emitted on impact rather
            // than streamed, thrown up and outboard.
            beamPort = MakeSystem("BeamSprayPort", new Vector3(-3.2f, 0.8f, 0.5f), solid,
                size: 0.7f, speed: 7f, spreadAngle: 40f, lifetime: 1.3f, gravity: 1.6f, solidFoam: true);
            beamStar = MakeSystem("BeamSprayStar", new Vector3(3.2f, 0.8f, 0.5f), solid,
                size: 0.7f, speed: 7f, spreadAngle: 40f, lifetime: 1.3f, gravity: 1.6f, solidFoam: true);
            beamPort.transform.localRotation = Quaternion.Euler(-52f, -90f, 0f);
            beamStar.transform.localRotation = Quaternion.Euler(-52f, 90f, 0f);

            // Churn right under the transom — solid, close in, short-lived.
            sternWash = MakeSystem("SternWash", new Vector3(0f, 0.1f, -9.5f), solid,
                size: 0.7f, speed: 1.6f, spreadAngle: 55f, lifetime: 1.8f, gravity: 0.15f,
                solidFoam: true);
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

        void Update()
        {
            HoldAtWaterline();
            float s01 = Mathf.Clamp01(motor.CurrentSpeed / motor.MaxSpeed);
            // Spray kicks in hard when the bow drops onto a wave face.
            float slam = Mathf.Clamp01(-motor.SurfAccel / 2.5f);
            SetRate(bowSpray, sprayFullRate * (Mathf.Pow(s01, 1.4f) + slam * 0.8f));
            SetRate(wake, wakeFullRate * s01);
            SetRate(shoulderPort, shoulderRate * s01);
            SetRate(shoulderStar, shoulderRate * s01);
            SetRate(sternWash, wakeFullRate * 0.8f * s01);

            WaveImpacts(s01);
        }

        /// Bursts of spray where the sea actually strikes the hull — off the
        /// beam when a wave shoulders into the side, off the bow when the stem
        /// drops into a trough. Each also stamps foam into the wake buffer, so
        /// the mark stays on the water after the spray itself has gone.
        void WaveImpacts(float speed01)
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

                Vector3 at = transform.position + transform.right * (lateral > 0f ? 3.5f : -3.5f);
                Ocean2.DynamicWaterSim.Splash(at, 6f, 0.5f + force);
            }

            // Bow slam: the surf pull reversing hard as the stem drops.
            float dSurf = (motor.SurfAccel - prevSurf) / Mathf.Max(0.0001f, Time.deltaTime);
            prevSurf = motor.SurfAccel;
            if (dSurf < -slamThreshold * 4f * bar && speed01 > 0.25f
                && Time.time - lastSlam > slamCooldown)
            {
                lastSlam = Time.time;
                if (bowSpray != null) bowSpray.Emit(Mathf.RoundToInt(Mathf.Lerp(14f, 50f, speed01)));
                Ocean2.DynamicWaterSim.Splash(transform.position + transform.forward * 9f, 7f, 0.8f);
            }
        }

        static void SetRate(ParticleSystem ps, float rate)
        {
            var e = ps.emission;
            e.rateOverTime = rate;
        }
    }
}
