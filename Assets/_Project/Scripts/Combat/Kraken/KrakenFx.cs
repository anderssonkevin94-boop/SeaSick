using SeaSick.Ocean;
using UnityEngine;

namespace SeaSick.Combat
{
    /// **The kraken's white water that the ripple sim cannot reach.**
    ///
    /// `DynamicWaterSim` follows the ship and is only `rippleSimExtent` across:
    /// 60 m on the mobile tier, so ±30 m round the hull. The kraken surfaces
    /// 55 m out, which puts its body wholly outside the sim on the phone and
    /// every stamp there is dropped at the uv test. `Kraken` still stamps the
    /// sim (the near arms land inside it on desktop, and later swats land
    /// next to the ship where it matters most); this is what makes the
    /// breach and the foam read everywhere else: a burst of spray, a ring of
    /// foam puffs lying on the water, and a slow skirt of foam where arms cut
    /// the surface. Particles/Unlit on `FoamTexture.SoftPuff`, the same pair
    /// `StormSpray` uses, which `Resources/Shaders/Keepalive` keeps in a build.
    public static class KrakenFx
    {
        static Material sprayMat, foamMat;

        static Material SprayMat()
        {
            if (sprayMat != null) return sprayMat;
            sprayMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            sprayMat.SetColor("_BaseColor", new Color(0.97f, 0.99f, 1f, 1f));
            sprayMat.SetTexture("_BaseMap", FoamTexture.SoftPuff());
            MakeTransparent(sprayMat, 3010);
            return sprayMat;
        }

        static Material FoamMat()
        {
            if (foamMat != null) return foamMat;
            foamMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            foamMat.SetColor("_BaseColor", new Color(0.93f, 0.97f, 0.98f, 1f));
            foamMat.SetTexture("_BaseMap", FoamTexture.SoftPuff());
            MakeTransparent(foamMat, 3005);
            return foamMat;
        }

        static void MakeTransparent(Material m, int queue)
        {
            m.SetFloat("_Surface", 1f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = queue;
        }

        /// The breach: a column of spray thrown up round the head and a ring
        /// of foam spreading out on the water from `radius`. Fire-and-forget,
        /// destroys itself.
        public static void Breach(Vector3 waterPoint, float radius)
        {
            var go = new GameObject("KrakenBreach");
            go.transform.position = waterPoint;

            // Spray: thrown up and out from a wide disc, falling back. Small,
            // fast and streaked along their own velocity -- big soft puffs
            // read as a cloud sitting on the sea, not water thrown off it.
            var spray = NewSystem(go.transform, "Spray", SprayMat(), 420);
            var main = spray.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(14f, 30f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 2.6f);
            main.gravityModifier = 1.6f;
            var sr = spray.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Stretch;
            sr.velocityScale = 0.06f;
            sr.lengthScale = 1.2f;
            var shape = spray.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 24f;
            shape.radius = radius * 0.45f;
            spray.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            FadeOut(spray, 0.9f);
            var grow = spray.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.4f)));
            spray.Emit(380);

            // Foam ring: flat puffs on the water, rolling outward and fading.
            var ring = NewSystem(go.transform, "FoamRing", FoamMat(), 120);
            var rmain = ring.main;
            rmain.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6.5f);
            rmain.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
            rmain.startSize = new ParticleSystem.MinMaxCurve(4f, 8f);
            rmain.gravityModifier = 0f;
            var rshape = ring.shape;
            rshape.enabled = true;
            rshape.shapeType = ParticleSystemShapeType.Circle;
            rshape.radius = radius;
            rshape.radiusThickness = 0.15f;
            ring.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            ring.GetComponent<ParticleSystemRenderer>().renderMode =
                ParticleSystemRenderMode.HorizontalBillboard;
            FadeOut(ring, 0.65f);
            var rgrow = ring.sizeOverLifetime;
            rgrow.enabled = true;
            rgrow.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 1.6f)));
            var drag = ring.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 0.5f;
            drag.dampen = 0.04f;
            ring.Emit(100);

            Object.Destroy(go, 8f);
        }

        /// A world-space foam emitter the kraken feeds by hand with
        /// `EmitFoam` where its arms cut the water. Lives as long as it does.
        public static ParticleSystem FoamSkirt(Transform owner)
        {
            var ps = NewSystem(owner, "FoamSkirt", FoamMat(), 220);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            ps.GetComponent<ParticleSystemRenderer>().renderMode =
                ParticleSystemRenderMode.HorizontalBillboard;
            FadeOut(ps, 0.7f);
            var grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.5f)));
            return ps;
        }

        public static void EmitFoam(ParticleSystem skirt, Vector3 at, float size)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = at,
                velocity = new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f)),
                startSize = size * Random.Range(0.8f, 1.3f),
                rotation = Random.Range(0f, 360f),
                applyShapeToPosition = false,
            };
            skirt.Emit(p, 1);
        }

        static ParticleSystem NewSystem(Transform parent, string name, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;       // kept playing; emission is off, so this only keeps hand-emitted puffs simulating
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startColor = Color.white;
            var emission = ps.emission;
            emission.enabled = false;   // emitted by hand
            var shape = ps.shape;
            shape.enabled = false;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static void FadeOut(ParticleSystem ps, float peakAlpha)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peakAlpha, 0.08f),
                        new GradientAlphaKey(peakAlpha * 0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }
    }
}
