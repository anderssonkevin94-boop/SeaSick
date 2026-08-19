using SeaSick.Ocean;
using UnityEngine;

namespace SeaSick.Ship
{
    /// A round shot in flight. Ballistic arc, then it hits the sea and throws a
    /// splash — which stamps straight into the wake buffer, so the disturbance
    /// stays on the water afterwards.
    public class CannonBall : MonoBehaviour
    {
        static Material ironMat;

        Vector3 velocity;
        float life;

        public static CannonBall Spawn(Vector3 position, Vector3 velocity)
        {
            if (ironMat == null)
            {
                ironMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                ironMat.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.14f));
                ironMat.SetFloat("_Smoothness", 0.5f);
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "CannonBall";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.localScale = Vector3.one * 0.42f;
            go.transform.position = position;
            go.GetComponent<MeshRenderer>().sharedMaterial = ironMat;

            var ball = go.AddComponent<CannonBall>();
            ball.velocity = velocity;
            return ball;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            velocity += Physics.gravity * dt;
            transform.position += velocity * dt;

            life += dt;
            if (life > 12f) { Destroy(gameObject); return; }

            var waves = WaveField.Instance;
            float surface = waves != null
                ? waves.SampleHeightFast(new Vector2(transform.position.x, transform.position.z), Time.time)
                : 0f;

            if (transform.position.y <= surface)
            {
                Splash(new Vector3(transform.position.x, surface, transform.position.z));
                Destroy(gameObject);
            }
        }

        void Splash(Vector3 at)
        {
            // The wake buffer already knows how to hold a mark on the water.
            WakeTexture.Splash(at, 7f, 1.3f);

            var go = new GameObject("Splash");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 12f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.gravityModifier = 2.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            main.playOnAwake = false;
            main.startColor = Color.white;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.4f;
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));
            mat.SetColor("_BaseColor", new Color(0.96f, 0.99f, 1f));
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

            ps.Emit(45);
            Destroy(go, 2.5f);
        }
    }
}
