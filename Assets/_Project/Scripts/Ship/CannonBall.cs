using SeaSick.Combat;
using SeaSick.Ocean;
using UnityEngine;

namespace SeaSick.Ship
{
    /// A round shot in flight. Ballistic arc, then it either bites a target or
    /// hits the sea and throws a splash — which stamps straight into the wake
    /// buffer, so the disturbance stays on the water afterwards.
    public class CannonBall : MonoBehaviour
    {
        const float Radius = 0.21f;
        const float Damage = 1f;

        static Material ironMat;
        static Material tracerMat;

        Vector3 velocity;
        float life;
        IHittable owner;   // never bites the hand that fired it

        public static CannonBall Spawn(Vector3 position, Vector3 velocity,
            float assistWindow = 0f, float assistCap = 0f, IHittable owner = null)
        {
            if (assistCap > 0f)
                velocity = LayBetter(position, velocity, assistWindow, assistCap, owner);


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

            Tracer(go);

            var ball = go.AddComponent<CannonBall>();
            ball.velocity = velocity;
            ball.owner = owner;
            GunneryStats.RecordShot();
            return ball;
        }

        /// A thin smoke trail behind the shot. This is how the fall of shot
        /// stays readable now that the camera deliberately does not chase it:
        /// you follow the trail down to the water instead of being shown.
        static void Tracer(GameObject go)
        {
            if (tracerMat == null)
            {
                tracerMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                tracerMat.SetFloat("_Surface", 1f);
                tracerMat.SetOverrideTag("RenderType", "Transparent");
                tracerMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                tracerMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                tracerMat.SetInt("_ZWrite", 0);
                tracerMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                tracerMat.renderQueue = 3000;
                tracerMat.SetColor("_BaseColor", Color.white);
            }

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.5f;
            trail.startWidth = 0.42f;
            trail.endWidth = 0.02f;
            trail.numCapVertices = 2;
            trail.sharedMaterial = tracerMat;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;

            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.97f, 0.90f), 0f),
                        new GradientColorKey(new Color(0.78f, 0.78f, 0.80f), 1f) },
                new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = grad;
        }

        /// Nudge the aim so a near miss lands. Applied once, at the muzzle — the
        /// shot stays a clean parabola and nothing bends in flight, so what you
        /// see is a gun crew laying slightly better rather than a ball that
        /// chases people.
        ///
        /// The miss is judged at the target's own range, against the launch
        /// angle that would pass through it. Measuring it at the point where
        /// the ball reaches the water instead is wrong twice over: the beast
        /// sits above the waterline and the shot is meant to pass through it on
        /// the way down, so a perfectly good shot reads as a 20m miss and the
        /// assist never engages.
        ///
        /// The correction is capped as metres of movement at the target's
        /// range, so a long shot bends no further than a short one.
        static Vector3 LayBetter(Vector3 from, Vector3 velocity, float window, float cap,
            IHittable owner)
        {
            float g = Mathf.Abs(Physics.gravity.y);
            float speed = velocity.magnitude;
            if (g < 0.01f || speed < 0.01f) return velocity;

            Vector3 dir = velocity / speed;

            IHittable best = null;
            Vector3 bestIdeal = Vector3.zero;
            float bestMiss = float.MaxValue;
            float bestRange = 0f;

            foreach (var h in HitTargets.All)
            {
                if (h == null || !h.Alive || ReferenceEquals(h, owner)) continue;

                Vector3 to = h.HitCentre - from;
                Vector3 flat = new Vector3(to.x, 0f, to.z);
                float range = flat.magnitude;
                if (range < 1f) continue;

                if (!SolveLaunch(range, to.y, speed, g, flat / range, out Vector3 ideal)) continue;

                // Small-angle: the linear miss at the target's range.
                float miss = Vector3.Angle(dir, ideal) * Mathf.Deg2Rad * range;
                if (miss < bestMiss)
                {
                    bestMiss = miss;
                    best = h;
                    bestIdeal = ideal;
                    bestRange = range;
                }
            }

            if (best == null || bestMiss > window) return velocity;

            float step = Mathf.Min(Vector3.Angle(dir, bestIdeal) * Mathf.Deg2Rad, cap / bestRange);
            return Vector3.RotateTowards(dir, bestIdeal, step, 0f) * speed;
        }

        /// Lay a shot on a point and pull the trigger. Raiders aim at where
        /// the player will be; the player's own guns deliberately do not aim
        /// at anything, because "aiming is the tiller" is the whole premise.
        public static CannonBall FireAt(Vector3 from, Vector3 target, float speed,
            float spreadDeg, IHittable owner)
        {
            Vector3 to = target - from;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            float range = flat.magnitude;
            float g = Mathf.Abs(Physics.gravity.y);

            Vector3 dir;
            if (range < 1f || !SolveLaunch(range, to.y, speed, g, flat / range, out dir))
                dir = to.sqrMagnitude > 1e-4f ? to.normalized : Vector3.forward;

            // Scatter, so a raider is a threat rather than a sniper.
            if (spreadDeg > 0f)
            {
                dir = Quaternion.AngleAxis(Random.Range(-spreadDeg, spreadDeg), Vector3.up) * dir;
                Vector3 pitchAxis = Vector3.Cross(Vector3.up, dir);
                if (pitchAxis.sqrMagnitude > 1e-4f)
                    dir = Quaternion.AngleAxis(
                        Random.Range(-spreadDeg, spreadDeg) * 0.4f, pitchAxis.normalized) * dir;
            }

            return Spawn(from, dir * speed, 0f, 0f, owner);
        }

        /// The launch direction that carries `speed` through a point `range`
        /// away and `height` above the muzzle. Takes the flat of the two
        /// solutions — the arcing one would lob the shot over the mast.
        /// False when the point simply cannot be reached.
        static bool SolveLaunch(float range, float height, float speed, float g,
            Vector3 flatDir, out Vector3 dir)
        {
            dir = Vector3.zero;
            float v2 = speed * speed;
            float disc = v2 * v2 - g * (g * range * range + 2f * height * v2);
            if (disc < 0f) return false;

            float theta = Mathf.Atan((v2 - Mathf.Sqrt(disc)) / (g * range));
            dir = (flatDir * Mathf.Cos(theta) + Vector3.up * Mathf.Sin(theta)).normalized;
            return true;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 from = transform.position;
            velocity += Physics.gravity * dt;
            Vector3 to = from + velocity * dt;
            transform.position = to;

            life += dt;
            if (life > 12f) { GunneryStats.RecordMiss(); Destroy(gameObject); return; }

            // Targets before the sea: a shot crosses a monster well above the
            // waterline, and at 42 m/s it covers most of a metre per frame, so
            // the test has to be swept or fast shots tunnel clean through.
            var target = HitTargets.SweepFirst(from, to, Radius, out Vector3 hitPoint, owner);
            if (target != null && target.TakeHit(hitPoint, Damage))
            {
                Impact.Burst(hitPoint);
                GunneryStats.RecordHit();
                Destroy(gameObject);
                return;
            }

            float surface = Ocean2.OceanSampler.Ready
                ? Ocean2.OceanSampler.SampleImmediate(to).height
                : 0f;

            if (to.y <= surface)
            {
                Splash(new Vector3(to.x, surface, to.z));
                GunneryStats.RecordMiss();
                Destroy(gameObject);
            }
        }

        void Splash(Vector3 at)
        {
            // The ripple sim knows how to hold a mark on the water.
            Ocean2.DynamicWaterSim.Splash(at, 7f, 1.3f);

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
