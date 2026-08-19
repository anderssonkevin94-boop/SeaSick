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

        // Roll goes straight into elevation for a gun pointing over the beam,
        // and measured under sail it swung the barrel between 1° and 15.6° —
        // landing the shot anywhere from 30m to 102m. That is not a difficulty,
        // it is a lottery the player cannot see. The crew lay the gun against
        // the roll; at less than 1 they lay it imperfectly, so a heavy sea
        // still costs accuracy as a gradient rather than a coin toss.
        [Range(0f, 1f)] [SerializeField] float rollStabilisation = 0.75f;

        // Guns could be trained with handspikes, so they do. This widens "the
        // side bears" from a knife-edge into a zone without bending any
        // physics — the ball still flies exactly where the barrel points.
        [SerializeField] float maxTraverseDeg = 18f;
        [SerializeField] float traverseSpeed = 55f;

        // Last-mile help, applied once at the muzzle rather than as steering in
        // flight: the shot stays a clean parabola, the crew just laid it a
        // little better. Only rescues shots already close.
        [SerializeField] float aimAssistWindow = 8f;
        [SerializeField] float aimAssistCap = 2.5f;

        Transform barrelPivot;
        Transform barrel;
        ParticleSystem smoke;
        Quaternion restLocalRotation = Quaternion.identity;
        SeaSick.Combat.IHittable owner;
        float trainYaw;
        float recoil;
        float reloadLeft;

        /// Who is working this gun, as a rate. The battery sets it from the
        /// assigned crew member every frame: 1 for a healthy gunner, less for
        /// a queasy one, 0 when they are at the rail — at which point the
        /// reload simply stops where it is and waits for them.
        public float ReloadScale { get; set; } = 1f;
        /// False when nobody is on the gun. A loaded gun with no one behind it
        /// is still a silent gun.
        public bool Manned { get; set; } = true;

        public bool Ready => Manned && reloadLeft <= 0f;
        public float ReloadFraction => Mathf.Clamp01(1f - reloadLeft / reloadTime);
        /// Straight out of the muzzle, angled up a touch.
        public Vector3 MuzzlePoint => barrel != null
            ? barrel.position + barrel.forward * 0.9f
            : transform.position;
        public Vector3 FireDirection => AimRotation() * Vector3.forward;
        public float MuzzleSpeed => muzzleSpeed;
        public float TraverseDeg => trainYaw;

        /// Where the barrel actually points, in world space.
        ///
        /// Derived rather than read off the transform so firing and drawing can
        /// never disagree: Fire() runs in Update and the visual is set in
        /// LateUpdate, so reading barrelPivot.forward at fire time would use
        /// last frame's attitude.
        Quaternion AimRotation()
        {
            // What the hull hands us, roll and all.
            Quaternion raw = transform.rotation * Quaternion.Euler(-elevationDeg, 0f, 0f);

            Vector3 flat = transform.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f) return raw;

            // Same bearing, but pitched off the horizon instead of off the deck.
            Quaternion level = Quaternion.LookRotation(flat.normalized, Vector3.up)
                               * Quaternion.Euler(-elevationDeg, 0f, 0f);

            return Quaternion.Slerp(raw, level, rollStabilisation);
        }

        /// Swing the carriage toward a target, within the traverse limit.
        /// Pass null to let it drift back to its rest bearing.
        public void TrainOn(Vector3? worldTarget, float dt)
        {
            float desired = 0f;

            if (worldTarget.HasValue && transform.parent != null)
            {
                Vector3 toTarget = worldTarget.Value - transform.position;
                toTarget.y = 0f;

                Vector3 rest = transform.parent.rotation * (restLocalRotation * Vector3.forward);
                rest.y = 0f;

                if (toTarget.sqrMagnitude > 0.01f && rest.sqrMagnitude > 1e-6f)
                    desired = Mathf.Clamp(
                        Vector3.SignedAngle(rest.normalized, toTarget.normalized, Vector3.up),
                        -maxTraverseDeg, maxTraverseDeg);
            }

            trainYaw = Mathf.MoveTowards(trainYaw, desired, traverseSpeed * dt);

            // Traverse about *world* up, not the gun's own up. Its own up is
            // the mast, and the mast leans with the roll — swinging the gun
            // sideways around a leaning axis also swings it in elevation.
            // Measured: that drove the barrel to -10.4° and put broadsides
            // straight into the sea. Stabilisation happens to mask it, which
            // is worse than it sounding, because it silently couples two
            // settings that should be independent.
            if (transform.parent == null)
            {
                transform.localRotation = restLocalRotation * Quaternion.Euler(0f, trainYaw, 0f);
                return;
            }

            transform.rotation = Quaternion.AngleAxis(trainYaw, Vector3.up)
                                 * (transform.parent.rotation * restLocalRotation);
        }

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
            // The battery has already set our rest bearing; traverse works
            // relative to it.
            restLocalRotation = transform.localRotation;

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
            reloadLeft = reloadTime;
            recoil = recoilDistance;
            if (smoke != null) smoke.Emit(28);

            // Resolved lazily, and by interface rather than by type, so the
            // guns work on anything hittable that carries them.
            if (owner == null)
                foreach (var mb in GetComponentsInParent<MonoBehaviour>())
                    if (mb is SeaSick.Combat.IHittable h) { owner = h; break; }

            CannonBall.Spawn(MuzzlePoint, FireDirection * muzzleSpeed + carriedVelocity,
                aimAssistWindow, aimAssistCap, owner);
            return true;
        }

        /// Kick and smoke without spawning a shot, for guns whose ball is
        /// fired by something else. Raiders aim as a ship rather than as a
        /// battery of independently-laid guns, so their guns are worked for
        /// the look of the thing while EnemyShip does the ballistics.
        public void RecoilOnly()
        {
            recoil = recoilDistance;
            if (smoke != null) smoke.Emit(28);
        }

        void Update()
        {
            // Reload is worked, not waited out: it only runs down while there
            // is someone on the gun to run it down.
            if (reloadLeft > 0f)
                reloadLeft = Mathf.Max(0f, reloadLeft - Time.deltaTime * Mathf.Max(0f, ReloadScale));

            if (barrel == null) return;
            recoil = Mathf.MoveTowards(recoil, 0f, recoilReturn * Time.deltaTime);
            barrel.localPosition = new Vector3(0f, 0f, -recoil);
        }

        /// Draw the barrel where it will actually shoot. After the hull has
        /// settled for the frame, so the laying is against the real attitude.
        void LateUpdate()
        {
            if (barrelPivot != null) barrelPivot.rotation = AimRotation();
        }
    }
}
