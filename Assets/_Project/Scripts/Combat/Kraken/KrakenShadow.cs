using SeaSick.Ocean;
using UnityEngine;

namespace SeaSick.Combat
{
    /// **The water over a kraken that has not surfaced yet** (GDD §6, "A
    /// warning, never an ambush"): the sea darkens in a huge soft blotch that
    /// glides along under the surface, and the water above it starts to
    /// bubble, more and more as the breach nears. Procedural and cheap, no
    /// art: two hand-fed particle systems on `FoamTexture.SoftPuff` through
    /// `Particles/Unlit` (which `Resources/Shaders/Keepalive` keeps in a
    /// build), a dozen or two live puffs at a time.
    ///
    /// `KrakenDirector` owns it: it moves the point and raises the
    /// intensity every frame, then `Finish`es it when the kraken surfaces (or
    /// the warning is called off) and the last puffs fade out on their own.
    /// The puffs sit a hair ABOVE the sampled swell, as the foam does, because
    /// anything just under the ocean surface is not drawn at all.
    public class KrakenShadow : MonoBehaviour
    {
        static Material shadowMat, bubbleMat;

        /// Half-length of the blotch along its heading, metres. A 65 m animal
        /// seen from under: the shadow reads as long as the ship.
        const float ShadowHalfLength = 17f;
        const float ShadowHalfWidth = 6f;
        const float BubbleRadius = 13f;
        /// Seconds the last puffs get to fade after `Finish`.
        const float LingerSeconds = 4f;

        Vector3 point;
        Vector3 heading = Vector3.forward;
        float intensity;
        bool haveShadowHeading;

        ParticleSystem shadow, bubbles;
        float shadowDebt, bubbleDebt;

        OceanProbeRegistry.Handle seaProbe;
        float waterY;
        bool haveWater;

        bool finishing;
        float finishAt;

        /// A new, empty tell. Nothing shows until `Set` has been called.
        public static KrakenShadow Create()
        {
            var go = new GameObject("KrakenShadow");
            return go.AddComponent<KrakenShadow>();
        }

        /// Where the dark water is (flat position; height is the swell's),
        /// how strong the signs are (0 = a first hint, 1 = about to breach)
        /// and which way the shadow is moving (flat; zero keeps the last).
        public void Set(Vector3 flatPoint, float intensity01, Vector3 travelDir)
        {
            point = flatPoint;
            intensity = Mathf.Clamp01(intensity01);
            travelDir.y = 0f;
            if (travelDir.sqrMagnitude > 0.01f) { heading = travelDir.normalized; haveShadowHeading = true; }
            if (seaProbe != null) seaProbe.position = point;
        }

        /// Stop emitting; the puffs already out fade, then this is destroyed.
        public void Finish()
        {
            if (finishing) return;
            finishing = true;
            finishAt = Time.time + LingerSeconds;
        }

        void Awake()
        {
            shadow = NewSystem("Shadow", ShadowMat(), 60, 0.36f, 0.35f);
            bubbles = NewSystem("Bubbles", BubbleMat(), 90, 0.75f, 0.2f);
        }

        void OnEnable() => EnsureProbe();

        void OnDisable()
        {
            OceanProbeRegistry.Unregister(seaProbe);
            seaProbe = null;
        }

        /// Re-registered rather than assumed, as in `Kraken.EnsureProbe`: a
        /// domain reload mid-play empties the registry's static list while
        /// the component and this field survive.
        void EnsureProbe()
        {
            if (seaProbe != null && OceanProbeRegistry.Handles.Count > 0) return;
            seaProbe = OceanProbeRegistry.Register(point);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (finishing)
            {
                if (Time.time >= finishAt) Destroy(gameObject);
                return;
            }

            // The swell at the point, off the one batched query (never a
            // per-frame SampleImmediate), lerped so a step-old height does
            // not stair-step the puffs.
            EnsureProbe();
            seaProbe.position = point;
            if (seaProbe.sampledFrame != 0)
            {
                if (!haveWater) { waterY = seaProbe.sample.height; haveWater = true; }
                waterY = Mathf.Lerp(waterY, seaProbe.sample.height, 1f - Mathf.Exp(-4f * dt));
            }
            else if (!haveWater && OceanSampler.Ready)
            {
                waterY = OceanSampler.SampleImmediate(point).height;
                haveWater = true;
            }
            if (!haveWater) return;

            Vector3 along = haveShadowHeading ? heading : Vector3.forward;
            Vector3 across = new Vector3(along.z, 0f, -along.x);

            // The shadow: broad dark puffs strewn along its heading, each
            // living ~2 s, so the blotch drifts with the point instead of
            // being nailed to it. Faint at first, near full at the end.
            shadowDebt += Mathf.Lerp(3.5f, 8f, intensity) * dt;
            int guard = 0;
            while (shadowDebt >= 1f && guard++ < 6)
            {
                shadowDebt -= 1f;
                Vector3 at = point + along * Random.Range(-ShadowHalfLength, ShadowHalfLength)
                                   + across * Random.Range(-ShadowHalfWidth, ShadowHalfWidth);
                at.y = waterY + 0.35f;
                var p = new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = along * Random.Range(0f, 2f),
                    startSize = Random.Range(15f, 24f),
                    startLifetime = Random.Range(1.8f, 2.6f),
                    startColor = new Color(1f, 1f, 1f, Mathf.Lerp(0.55f, 1f, intensity)),
                    rotation = Random.Range(0f, 360f),
                    applyShapeToPosition = false,
                };
                shadow.Emit(p, 1);
            }

            // The bubbling: nothing much for the first seconds, a boil by
            // the end. Small white puffs, short-lived.
            bubbleDebt += (3f + 30f * intensity * intensity) * dt;
            guard = 0;
            while (bubbleDebt >= 1f && guard++ < 8)
            {
                bubbleDebt -= 1f;
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float r = BubbleRadius * Mathf.Sqrt(Random.value);
                Vector3 at = point + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                at.y = waterY + 0.3f;
                var p = new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = new Vector3(Random.Range(-0.4f, 0.4f), 0f, Random.Range(-0.4f, 0.4f)),
                    startSize = Random.Range(1.4f, 3.6f),
                    startLifetime = Random.Range(1f, 1.7f),
                    rotation = Random.Range(0f, 360f),
                    applyShapeToPosition = false,
                };
                bubbles.Emit(p, 1);
            }
        }

        // ----------------------------------------------------------- particles

        ParticleSystem NewSystem(string name, Material mat, int max, float peakAlpha, float fadeInAt)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;       // kept playing; emission is off, so this only keeps hand-emitted puffs simulating
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startColor = Color.white;
            main.gravityModifier = 0f;
            var emission = ps.emission;
            emission.enabled = false;   // emitted by hand
            var shape = ps.shape;
            shape.enabled = false;

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peakAlpha, fadeInAt),
                        new GradientAlphaKey(peakAlpha * 0.6f, 0.65f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            ps.Play();
            return ps;
        }

        static Material ShadowMat()
        {
            if (shadowMat != null) return shadowMat;
            shadowMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            shadowMat.SetColor("_BaseColor", new Color(0.015f, 0.04f, 0.07f, 1f));
            shadowMat.SetTexture("_BaseMap", FoamTexture.SoftPuff());
            MakeTransparent(shadowMat, 3003);
            return shadowMat;
        }

        static Material BubbleMat()
        {
            if (bubbleMat != null) return bubbleMat;
            bubbleMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            bubbleMat.SetColor("_BaseColor", new Color(0.9f, 0.97f, 0.98f, 1f));
            bubbleMat.SetTexture("_BaseMap", FoamTexture.SoftPuff());
            MakeTransparent(bubbleMat, 3006);
            return bubbleMat;
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
    }
}
