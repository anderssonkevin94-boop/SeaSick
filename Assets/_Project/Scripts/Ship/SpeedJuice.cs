using UnityEngine;

namespace SeaSick.Ship
{
    /// Perceptual speed: bow spray and a foam wake that scale with how fast
    /// the hull is actually moving. Built entirely in code — no prefab setup.
    [RequireComponent(typeof(ShipMotor))]
    public class SpeedJuice : MonoBehaviour
    {
        [SerializeField] float sprayFullRate = 55f; // particles/s at max speed
        [SerializeField] float wakeFullRate = 30f;

        ShipMotor motor;
        ParticleSystem bowSpray;
        ParticleSystem wake;
        ParticleSystem shoulderPort;
        ParticleSystem shoulderStar;

        void Start()
        {
            motor = GetComponent<ShipMotor>();

            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.45f));
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;

            bowSpray = MakeSystem("BowSpray", new Vector3(0f, 0.4f, 9.2f), mat, size: 0.45f,
                speed: 5.5f, spreadAngle: 62f, lifetime: 1.0f, gravity: 1.3f);
            wake = MakeSystem("WakeFoam", new Vector3(0f, 0.15f, -8.6f), mat, size: 0.8f,
                speed: 1.0f, spreadAngle: 42f, lifetime: 5.5f, gravity: 0f);

            // Foam where the hull actually parts the water, thrown out along
            // the shoulders rather than straight back.
            shoulderPort = MakeSystem("ShoulderPort", new Vector3(-2.4f, 0.15f, 5.5f), mat,
                size: 0.9f, speed: 2.2f, spreadAngle: 34f, lifetime: 3.5f, gravity: 0f);
            shoulderStar = MakeSystem("ShoulderStar", new Vector3(2.4f, 0.15f, 5.5f), mat,
                size: 0.9f, speed: 2.2f, spreadAngle: 34f, lifetime: 3.5f, gravity: 0f);
            shoulderPort.transform.localRotation = Quaternion.Euler(-8f, -118f, 0f);
            shoulderStar.transform.localRotation = Quaternion.Euler(-8f, 118f, 0f);
        }

        ParticleSystem MakeSystem(string name, Vector3 localPos, Material mat,
            float size, float speed, float spreadAngle, float lifetime, float gravity)
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
            main.startColor = new Color(1f, 1f, 1f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = spreadAngle;
            shape.radius = 0.3f;

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = grad;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;

            return ps;
        }

        void Update()
        {
            float s01 = Mathf.Clamp01(motor.CurrentSpeed / motor.MaxSpeed);
            // Spray kicks in hard when the bow drops onto a wave face.
            float slam = Mathf.Clamp01(-motor.SurfAccel / 2.5f);
            SetRate(bowSpray, sprayFullRate * (Mathf.Pow(s01, 1.6f) + slam * 0.6f));
            SetRate(wake, wakeFullRate * s01);
            SetRate(shoulderPort, wakeFullRate * 0.7f * s01);
            SetRate(shoulderStar, wakeFullRate * 0.7f * s01);
        }

        static void SetRate(ParticleSystem ps, float rate)
        {
            var e = ps.emission;
            e.rateOverTime = rate;
        }
    }
}
