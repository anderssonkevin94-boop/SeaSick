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

        void Start()
        {
            motor = GetComponent<ShipMotor>();

            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.75f));
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;

            bowSpray = MakeSystem("BowSpray", new Vector3(0f, 0.4f, 9.2f), mat, size: 0.35f,
                speed: 4.5f, spreadAngle: 55f, lifetime: 0.8f, gravity: 1.1f);
            wake = MakeSystem("WakeFoam", new Vector3(0f, 0.15f, -8.6f), mat, size: 0.8f,
                speed: 1.2f, spreadAngle: 30f, lifetime: 2.2f, gravity: 0f);
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
            main.startColor = new Color(1f, 1f, 1f, 0.8f);
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
            SetRate(bowSpray, sprayFullRate * Mathf.Pow(s01, 1.6f));
            SetRate(wake, wakeFullRate * s01);
        }

        static void SetRate(ParticleSystem ps, float rate)
        {
            var e = ps.emission;
            e.rateOverTime = rate;
        }
    }
}
