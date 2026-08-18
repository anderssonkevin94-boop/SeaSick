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

        void Start()
        {
            motor = GetComponent<ShipMotor>();

            // Solid, lit foam. Translucent billboards read as grey squares over
            // dark water; opaque chunks that catch the sun read as real spray
            // and thrown water. These shrink away instead of fading out.
            var solid = new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));
            solid.SetColor("_BaseColor", new Color(0.97f, 0.99f, 1f, 1f));
            solid.SetFloat("_Smoothness", 0.35f);

            // The long wake still fades, so it dissolves into the sea rather
            // than popping out of existence behind you.
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.5f));
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
            go.transform.SetParent(transform, false);
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

        void Update()
        {
            float s01 = Mathf.Clamp01(motor.CurrentSpeed / motor.MaxSpeed);
            // Spray kicks in hard when the bow drops onto a wave face.
            float slam = Mathf.Clamp01(-motor.SurfAccel / 2.5f);
            SetRate(bowSpray, sprayFullRate * (Mathf.Pow(s01, 1.4f) + slam * 0.8f));
            SetRate(wake, wakeFullRate * s01);
            SetRate(shoulderPort, shoulderRate * s01);
            SetRate(shoulderStar, shoulderRate * s01);
            SetRate(sternWash, wakeFullRate * 0.8f * s01);
        }

        static void SetRate(ParticleSystem ps, float rate)
        {
            var e = ps.emission;
            e.rateOverTime = rate;
        }
    }
}
