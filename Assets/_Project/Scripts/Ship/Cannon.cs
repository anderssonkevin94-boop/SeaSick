using UnityEngine;

namespace SeaSick.Ship
{
    /// One gun on the rail. Built from primitives to match the rest of the
    /// low-poly art (the supplied cannon FBX is an empty file), so the mesh can
    /// be swapped for a real model later without touching the behaviour.
    public class Cannon : MonoBehaviour
    {
        [SerializeField] float reloadTime = 3.2f;
        [SerializeField] float recoilDistance = 0.55f;
        [SerializeField] float recoilReturn = 3.5f;
        [SerializeField] float muzzleSpeed = 42f;
        [SerializeField] float elevationDeg = 9f;

        Transform barrelPivot;
        Transform barrel;
        ParticleSystem smoke;
        float recoil;
        float readyAt;

        public bool Ready => Time.time >= readyAt;
        public float ReloadFraction => Mathf.Clamp01(1f - (readyAt - Time.time) / reloadTime);
        /// Straight out of the muzzle, angled up a touch.
        public Vector3 MuzzlePoint => barrel != null
            ? barrel.position + barrel.forward * 0.9f
            : transform.position;
        public Vector3 FireDirection => barrelPivot != null
            ? barrelPivot.forward : transform.forward;
        public float MuzzleSpeed => muzzleSpeed;

        /// How far the shot carries from the muzzle down to flat water.
        /// Computed from the live tuning rather than written down, so it stays
        /// honest when the numbers move.
        public float FlatRange
        {
            get
            {
                float g = Mathf.Abs(Physics.gravity.y);
                float rad = elevationDeg * Mathf.Deg2Rad;
                float vy = muzzleSpeed * Mathf.Sin(rad);
                float vx = muzzleSpeed * Mathf.Cos(rad);
                float h = Mathf.Max(0.5f, MuzzlePoint.y);
                return vx * (vy + Mathf.Sqrt(vy * vy + 2f * g * h)) / g;
            }
        }

        public void Build(Material wood, Material iron)
        {
            // Carriage
            var carriage = Prim(PrimitiveType.Cube, transform, new Vector3(0.85f, 0.32f, 1.0f), wood);
            carriage.transform.localPosition = new Vector3(0f, 0.16f, -0.1f);

            for (int i = 0; i < 4; i++)
            {
                var wheel = Prim(PrimitiveType.Cylinder, transform, new Vector3(0.34f, 0.07f, 0.34f), iron);
                wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                wheel.transform.localPosition = new Vector3(
                    i < 2 ? -0.42f : 0.42f, 0.14f, i % 2 == 0 ? 0.28f : -0.42f);
            }

            // Barrel on its own pivot so it can recoil and be elevated.
            var pivot = new GameObject("BarrelPivot");
            pivot.transform.SetParent(transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            pivot.transform.localRotation = Quaternion.Euler(-elevationDeg, 0f, 0f);
            barrelPivot = pivot.transform;

            var tube = new GameObject("Barrel");
            tube.transform.SetParent(pivot.transform, false);
            barrel = tube.transform;

            var main = Prim(PrimitiveType.Cylinder, tube.transform, new Vector3(0.26f, 0.62f, 0.26f), iron);
            main.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            main.transform.localPosition = new Vector3(0f, 0f, 0.35f);

            var breech = Prim(PrimitiveType.Sphere, tube.transform, Vector3.one * 0.33f, iron);
            breech.transform.localPosition = new Vector3(0f, 0f, -0.28f);

            var muzzle = Prim(PrimitiveType.Cylinder, tube.transform, new Vector3(0.31f, 0.09f, 0.31f), iron);
            muzzle.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            muzzle.transform.localPosition = new Vector3(0f, 0f, 0.94f);

            smoke = BuildSmoke();
        }

        ParticleSystem BuildSmoke()
        {
            var go = new GameObject("MuzzleSmoke");
            go.transform.SetParent(barrel, false);
            go.transform.localPosition = new Vector3(0f, 0f, 1.0f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.6f);
            main.gravityModifier = -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 16f;
            shape.radius = 0.15f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.96f, 0.85f), 0f),
                        new GradientColorKey(new Color(0.62f, 0.62f, 0.64f), 0.3f),
                        new GradientColorKey(new Color(0.5f, 0.5f, 0.52f), 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 1.9f)));

            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
            mat.SetColor("_BaseColor", Color.white);
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            return ps;
        }

        static GameObject Prim(PrimitiveType type, Transform parent, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// `carriedVelocity` is the ship's own motion. Without it a broadside
        /// fired at speed lands astern of where it was aimed — at 21 m/s over a
        /// 1.6s flight that is most of the gun's range in lead error.
        public bool Fire(Vector3 carriedVelocity = default)
        {
            if (!Ready) return false;
            readyAt = Time.time + reloadTime;
            recoil = recoilDistance;
            if (smoke != null) smoke.Emit(28);

            CannonBall.Spawn(MuzzlePoint, FireDirection * muzzleSpeed + carriedVelocity);
            return true;
        }

        void Update()
        {
            if (barrel == null) return;
            recoil = Mathf.MoveTowards(recoil, 0f, recoilReturn * Time.deltaTime);
            barrel.localPosition = new Vector3(0f, 0f, -recoil);
        }
    }
}
