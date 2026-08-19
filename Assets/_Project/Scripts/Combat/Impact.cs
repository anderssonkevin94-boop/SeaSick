using UnityEngine;

namespace SeaSick.Combat
{
    /// The moment a shot bites. Deliberately short and bright: a flash and a
    /// hard scatter that are gone in under a second, then smoke that lingers a
    /// little. Long, slow explosions read as fireworks; this should read as
    /// something breaking.
    public static class Impact
    {
        public static void Burst(Vector3 at, float scale = 1f)
        {
            var go = new GameObject("Impact");
            go.transform.position = at;

            Flash(go.transform, scale);
            Smoke(go.transform, scale);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.35f);
            light.range = 22f * scale;
            light.intensity = 9f;
            light.shadows = LightShadows.None;   // one frame of drama, not a cost
            go.AddComponent<Fade>().Init(light, 0.16f);

            Object.Destroy(go, 2.6f);
        }

        static void Flash(Transform parent, float scale)
        {
            var go = new GameObject("Flash");
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f * scale, 2.1f * scale);
            main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 26f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.42f);
            main.gravityModifier = 0.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 70;
            main.playOnAwake = false;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.94f, 0.62f), new Color(1f, 0.45f, 0.12f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f * scale;

            // Shrink as they cool, so the burst collapses instead of drifting.
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.05f)));

            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);   // additive: it glows
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
            mat.SetColor("_BaseColor", Color.white);
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;

            ps.Emit(34);
        }

        static void Smoke(Transform parent, float scale)
        {
            var go = new GameObject("Smoke");
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f * scale, 2.4f * scale);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 6f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.6f);
            main.gravityModifier = -0.06f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 50;
            main.playOnAwake = false;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.32f, 0.30f, 0.29f, 0.9f), new Color(0.12f, 0.11f, 0.11f, 0.9f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f * scale;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.8f)));

            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial =
                new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));

            ps.Emit(16);
        }

        /// Rides the point light down to nothing. A light that simply vanishes
        /// pops; this one goes out.
        class Fade : MonoBehaviour
        {
            Light light;
            float life, age, start;

            public void Init(Light l, float seconds)
            {
                light = l;
                life = seconds;
                start = l.intensity;
            }

            void Update()
            {
                if (light == null) return;
                age += Time.deltaTime;
                float k = Mathf.Clamp01(age / life);
                light.intensity = start * (1f - k) * (1f - k);
                if (k >= 1f) light.enabled = false;
            }
        }
    }
}
