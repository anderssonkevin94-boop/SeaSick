using System.Collections.Generic;
using SeaSick.Ocean;
using UnityEngine;

namespace SeaSick.Combat
{
    /// A serpent holding station on the surface. It does nothing back — on
    /// purpose. With nothing manoeuvring and nothing shooting at you, every
    /// miss is the gunnery's fault and not the target's, which is the only way
    /// to tell whether the broadsides actually work.
    ///
    /// It does ride the swell rather than sitting at a fixed height, so the
    /// guns still have to cope with a target that rises and falls, and it
    /// stamps the wake buffer so it reads as a thing in the water.
    ///
    /// Built from primitives like the islands, reefs, props and the guns.
    public class SeaMonster : MonoBehaviour, IHittable
    {
        public static readonly List<SeaMonster> All = new List<SeaMonster>();

        [SerializeField] float hitRadius = 4.2f;
        [SerializeField] int hitPoints = 6;
        [SerializeField] float sinkTime = 3.5f;

        int damage;
        float diedAt = -1f;
        float bobSeed;
        float writhe;          // hit reaction, decays back to rest
        float lastHitAt = -99f;
        float restY;

        readonly List<Renderer> skin = new List<Renderer>();
        readonly List<Color> skinColor = new List<Color>();
        MaterialPropertyBlock mpb;
        Transform body;
        bool flashClear = true;

        public Vector3 HitCentre => transform.position + Vector3.up * 1.4f;
        public float HitRadius => hitRadius;
        /// Coils astern, head forward — a long low body, not a ball.
        public Vector3 HitAxis => transform.forward * 4.5f;
        public bool Alive => diedAt < 0f;
        public int HitPoints => hitPoints;
        public int DamageTaken => damage;
        public float Health01 => Mathf.Clamp01(1f - (float)damage / Mathf.Max(1, hitPoints));
        public float LastHitAt => lastHitAt;

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            HitTargets.Register(this);
        }

        void OnDisable()
        {
            All.Remove(this);
            HitTargets.Unregister(this);
        }

        void Awake()
        {
            bobSeed = Random.Range(0f, 100f);
            mpb = new MaterialPropertyBlock();
            Build();
        }

        // ---------------------------------------------------------------- art

        void Build()
        {
            var hide = Mat(new Color(0.16f, 0.31f, 0.28f), 0.34f);
            var belly = Mat(new Color(0.42f, 0.50f, 0.34f), 0.22f);
            var spine = Mat(new Color(0.58f, 0.44f, 0.24f), 0.20f);
            var eye = Mat(new Color(0.92f, 0.78f, 0.20f), 0.80f);
            var pupil = Mat(new Color(0.05f, 0.04f, 0.05f), 0.60f);

            var root = new GameObject("Body");
            root.transform.SetParent(transform, false);
            body = root.transform;

            // Three coils breaking the surface, trailing away astern of the head.
            for (int i = 0; i < 3; i++)
            {
                float z = -2.5f - i * 3.4f;
                float scale = Mathf.Lerp(3.6f, 1.9f, i / 2f);
                var hump = Prim(PrimitiveType.Sphere, body,
                    new Vector3(scale, scale * 0.62f, scale * 1.5f), hide);
                // Mostly submerged: only the arch of each coil shows.
                hump.transform.localPosition = new Vector3(
                    Mathf.Sin(i * 1.7f) * 0.7f, -0.35f + (i == 0 ? 0.25f : 0f), z);
                hump.transform.localRotation = Quaternion.Euler(0f, Mathf.Sin(i * 2.1f) * 12f, 0f);

                // A ridge of plates along the top of each coil.
                for (int p = 0; p < 2; p++)
                {
                    var plate = Prim(PrimitiveType.Cube, body,
                        new Vector3(0.16f, scale * 0.42f, scale * 0.34f), spine);
                    plate.transform.localPosition = new Vector3(
                        hump.transform.localPosition.x,
                        scale * 0.24f,
                        z - scale * 0.35f + p * scale * 0.7f);
                    plate.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
                }
            }

            // Neck: three tapering segments rising out of the water.
            for (int i = 0; i < 3; i++)
            {
                float t = i / 2f;
                float s = Mathf.Lerp(2.5f, 1.5f, t);
                var seg = Prim(PrimitiveType.Sphere, body,
                    new Vector3(s, s * 1.25f, s), hide);
                seg.transform.localPosition = new Vector3(0f, 0.6f + i * 1.5f, 0.5f + i * 0.55f);
            }

            // Head: a long snout with a jaw under it.
            var head = Prim(PrimitiveType.Sphere, body, new Vector3(1.7f, 1.5f, 2.9f), hide);
            head.transform.localPosition = new Vector3(0f, 5.3f, 2.5f);
            head.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);

            var jaw = Prim(PrimitiveType.Cube, body, new Vector3(0.95f, 0.34f, 1.7f), belly);
            jaw.transform.localPosition = new Vector3(0f, 4.82f, 2.85f);
            jaw.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);

            for (int s = -1; s <= 1; s += 2)
            {
                var e = Prim(PrimitiveType.Sphere, body, Vector3.one * 0.52f, eye);
                e.transform.localPosition = new Vector3(s * 0.62f, 5.75f, 3.15f);
                var p = Prim(PrimitiveType.Sphere, body, new Vector3(0.2f, 0.34f, 0.2f), pupil);
                p.transform.localPosition = new Vector3(s * 0.78f, 5.78f, 3.35f);

                // Horns swept back off the skull. Short and thick — long thin
                // ones read as antennae from the gameplay camera.
                var horn = Prim(PrimitiveType.Cylinder, body, new Vector3(0.3f, 0.42f, 0.3f), spine);
                horn.transform.localPosition = new Vector3(s * 0.58f, 6.0f, 2.05f);
                horn.transform.localRotation = Quaternion.Euler(-46f, 0f, s * 20f);

                // Fins where the first coil meets the water.
                var fin = Prim(PrimitiveType.Cube, body, new Vector3(2.6f, 0.18f, 1.3f), spine);
                fin.transform.localPosition = new Vector3(s * 2.1f, -0.15f, -2.2f);
                fin.transform.localRotation = Quaternion.Euler(0f, s * 22f, s * -14f);
            }

            GetComponentsInChildren(true, skin);
            foreach (var r in skin)
                skinColor.Add(r != null && r.sharedMaterial != null
                    ? r.sharedMaterial.GetColor("_BaseColor") : Color.white);
        }

        static Material Mat(Color c, float smoothness)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            return m;
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

        // ------------------------------------------------------------- motion

        void Start() { restY = transform.position.y; }

        void Update()
        {
            float t = Time.time;

            // Ride the surface. Sample on demand — the ocean may not have been
            // ready when this spawned.
            if (Ocean2.OceanSampler.Ready)
            {
                float surface = Ocean2.OceanSampler.SampleImmediate(transform.position).height;
                restY = Mathf.Lerp(restY, surface, 1f - Mathf.Exp(-6f * Time.deltaTime));
            }

            float sink = 0f;
            if (!Alive)
            {
                float k = Mathf.Clamp01((t - diedAt) / sinkTime);
                sink = -k * k * 9f;              // slides under, accelerating
                body.localRotation = Quaternion.Slerp(
                    body.localRotation, Quaternion.Euler(-52f, 0f, 26f), 2.2f * Time.deltaTime);
            }

            // A slow idle sway, plus whatever writhe is left from the last hit.
            float sway = Mathf.Sin((t + bobSeed) * 0.7f) * 0.35f;
            transform.position = new Vector3(
                transform.position.x, restY + sway + sink, transform.position.z);

            if (Alive)
            {
                writhe = Mathf.MoveTowards(writhe, 0f, 2.4f * Time.deltaTime);
                float lash = Mathf.Sin((t - lastHitAt) * 26f) * writhe;
                body.localRotation = Quaternion.Euler(lash * 9f, lash * 14f, lash * 11f);

                // A hurt beast thrashes: the water around it shows it.
                var sim = Ocean2.DynamicWaterSim.Instance;
                if (sim != null)
                {
                    float agitation = 0.10f + writhe * 1.4f;
                    sim.Stamp(new Vector2(transform.position.x, transform.position.z),
                        hitRadius * 1.5f, agitation * Time.deltaTime * 6f,
                        agitation * Time.deltaTime * 3f);
                }
            }
            else if (t - diedAt > sinkTime + 1.5f)
            {
                Destroy(gameObject);
            }

            // Flash on the frame it was hit and briefly after. Driven through
            // _BaseColor rather than emission: these materials never enable the
            // _EMISSION keyword, so URP would drop an _EmissionColor override
            // on the floor and the hit would read as nothing at all.
            const float FlashTime = 0.35f;
            float since = t - lastHitAt;
            if (since < FlashTime)
            {
                float f = 1f - since / FlashTime;
                Tint(c => Color.Lerp(c, new Color(1f, 0.25f, 0.20f), f));
                flashClear = false;
            }
            else if (!flashClear)
            {
                Tint(c => c);
                flashClear = true;
            }
        }

        /// Push a colour through every piece of the beast, relative to whatever
        /// that piece started as, so the hide/spine/eye separation survives.
        void Tint(System.Func<Color, Color> f)
        {
            for (int i = 0; i < skin.Count; i++)
            {
                var r = skin[i];
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", f(skinColor[i]));
                r.SetPropertyBlock(mpb);
            }
        }

        // --------------------------------------------------------------- hits

        public bool TakeHit(Vector3 point, float damageAmount)
        {
            if (!Alive) return false;

            damage += Mathf.Max(1, Mathf.RoundToInt(damageAmount));
            lastHitAt = Time.time;
            writhe = 1f;

            Gore(point);

            if (damage >= hitPoints) Die();
            return true;
        }

        void Die()
        {
            diedAt = Time.time;
            HitTargets.Unregister(this);
            Ocean2.DynamicWaterSim.Splash(transform.position, 16f, 2.4f);
        }

        /// A puff of dark spray where the shot went in.
        void Gore(Vector3 at)
        {
            var go = new GameObject("Hit");
            go.transform.position = at;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.gravityModifier = 1.8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            main.playOnAwake = false;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.30f, 0.12f, 0.16f), new Color(0.14f, 0.22f, 0.20f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));
            mat.SetColor("_BaseColor", Color.white);
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));

            ps.Emit(26);
            Destroy(go, 2f);
        }

        /// Drop one in the water. Yaw is random so a row of them doesn't read
        /// as a formation.
        public static SeaMonster Spawn(Vector3 position, string name = "SeaMonster")
        {
            var go = new GameObject(name);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            return go.AddComponent<SeaMonster>();
        }
    }
}
