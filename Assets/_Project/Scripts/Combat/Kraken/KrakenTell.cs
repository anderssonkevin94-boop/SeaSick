using SeaSick.Ocean;
using UnityEngine;

namespace SeaSick.Combat
{
    /// **The swat's tell: a ring of foam on the water where the arm will
    /// land** (GDD §6 "The Kraken", step 2).
    ///
    /// Two meshes and a puff stream, laid flat on the sea at a fixed world
    /// point (the ring stays where it was laid; the ship moves): a white
    /// foam band of `radius` (the exact edge the hit test uses), which
    /// closes in from a wider ring over the first part of the windup and
    /// then pulses faster as the slam nears, and a dark disc inside it --
    /// the arm's shadow -- that deepens with the windup. Foam puffs churn
    /// along the band, thicker toward the slam. One ring per attack; the
    /// swat keeps a couple and reuses them.
    ///
    /// The ring's height rides the sea through one batched ocean probe at
    /// its centre (never a per-frame `SampleImmediate`), lifted a little so
    /// a swell over its 12 m does not bury half of it. Drawn after the water
    /// with depth test on: the ship's hull hides it where she is over it,
    /// the ring never paints over her.
    public class KrakenTell : MonoBehaviour
    {
        const int Segments = 72;
        const float Lift = 0.45f;

        public enum Mode { Hidden, Windup, Slammed, Cancelled }

        public Mode State { get; private set; } = Mode.Hidden;
        public Vector3 Centre { get; private set; }
        public float Radius { get; private set; }

        MeshFilter bandFilter, shadowFilter;
        MeshRenderer band, shadow;
        Mesh bandMesh, shadowMesh;
        Color32[] bandColors, shadowColors;
        Material bandMat, shadowMat;
        ParticleSystem puffs;
        OceanProbeRegistry.Handle probe;
        float waterY;
        bool haveWater;

        float progress;     // 0..1 through the windup
        float fadeT;        // seconds since slam/cancel
        float puffDebt;
        float shownRadius;

        public static KrakenTell Create(Transform parent)
        {
            var go = new GameObject("KrakenTell");
            // World-placed: not a child of the kraken (it moves and turns).
            var t = go.AddComponent<KrakenTell>();
            t.Build();
            go.SetActive(false);
            return t;
        }

        void Build()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            bandMat = new Material(shader) { name = "KrakenTellBand" };
            bandMat.SetColor("_BaseColor", Color.white);
            Transparent(bandMat, 3020);
            shadowMat = new Material(shader) { name = "KrakenTellShadow" };
            shadowMat.SetColor("_BaseColor", Color.white);
            Transparent(shadowMat, 3019);

            band = MakePart("Band", bandMat, out bandFilter);
            shadow = MakePart("Shadow", shadowMat, out shadowFilter);
            bandMesh = BuildAnnulus(out bandColors);
            shadowMesh = BuildDisc(out shadowColors);
            bandFilter.sharedMesh = bandMesh;
            shadowFilter.sharedMesh = shadowMesh;

            // Puffs on the band: the foam material the kraken's skirt uses.
            var pgo = new GameObject("Puffs");
            pgo.transform.SetParent(transform, false);
            puffs = pgo.AddComponent<ParticleSystem>();
            puffs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = puffs.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            var em = puffs.emission; em.enabled = false;
            var sh = puffs.shape; sh.enabled = false;
            var col = puffs.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = puffs.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            r.sharedMaterial = KrakenFx.FoamMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            puffs.Play();
        }

        MeshRenderer MakePart(string n, Material m, out MeshFilter f)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            f = go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        static void Transparent(Material m, int queue)
        {
            m.SetFloat("_Surface", 1f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = queue;
        }

        /// A unit annulus in four rings of vertices (0.62, 0.80, 1.0, 1.08 of
        /// the radius): alpha 0 -> full -> full -> 0, a broad bright band
        /// whose outer edge sits exactly on the hit radius. A thin ring
        /// hugging a 9 m hull vanished from the chase camera (first captures);
        /// this one reads at 60 m. Scaled by the transform.
        static Mesh BuildAnnulus(out Color32[] colors)
        {
            var m = new Mesh { name = "KrakenTellBand" };
            int n = Segments + 1;
            var v = new Vector3[n * 4];
            colors = new Color32[n * 4];
            float[] radii = { 0.62f, 0.80f, 1f, 1.08f };
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                for (int k = 0; k < 4; k++) v[i * 4 + k] = new Vector3(c * radii[k], 0f, s * radii[k]);
            }
            var tri = new int[Segments * 3 * 6];
            int t = 0;
            for (int i = 0; i < Segments; i++)
            for (int k = 0; k < 3; k++)
            {
                int a0 = i * 4 + k, a1 = a0 + 1, b0 = a0 + 4, b1 = b0 + 1;
                tri[t++] = a0; tri[t++] = b0; tri[t++] = a1;
                tri[t++] = a1; tri[t++] = b0; tri[t++] = b1;
            }
            m.vertices = v;
            m.triangles = tri;
            m.colors32 = colors;
            m.RecalculateBounds();
            m.bounds = new Bounds(Vector3.zero, new Vector3(2.4f, 0.5f, 2.4f));
            return m;
        }

        static Mesh BuildDisc(out Color32[] colors)
        {
            var m = new Mesh { name = "KrakenTellShadow" };
            var v = new Vector3[Segments + 2];
            colors = new Color32[Segments + 2];
            v[0] = Vector3.zero;
            for (int i = 0; i <= Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                v[i + 1] = new Vector3(Mathf.Cos(a) * 0.95f, 0f, Mathf.Sin(a) * 0.95f);
            }
            var tri = new int[Segments * 3];
            for (int i = 0; i < Segments; i++)
            {
                tri[i * 3] = 0; tri[i * 3 + 1] = i + 2; tri[i * 3 + 2] = i + 1;
            }
            m.vertices = v;
            m.triangles = tri;
            m.colors32 = colors;
            m.bounds = new Bounds(Vector3.zero, new Vector3(2.4f, 0.5f, 2.4f));
            return m;
        }

        // ----------------------------------------------------------- control

        public void Show(Vector3 centre, float radius)
        {
            Centre = new Vector3(centre.x, 0f, centre.z);
            Radius = Mathf.Max(0.5f, radius);
            progress = 0f;
            fadeT = 0f;
            shownRadius = Radius * 1.35f;
            haveWater = false;
            State = Mode.Windup;
            gameObject.SetActive(true);
            if (probe == null) probe = OceanProbeRegistry.Register(Centre);
            probe.position = Centre;
            if (OceanSampler.Ready)
            {
                waterY = OceanSampler.SampleImmediate(Centre).height;
                haveWater = true;
            }
            Place(0f);
        }

        /// 0..1 through the windup; the swat drives it.
        public void SetProgress(float t01) => progress = Mathf.Clamp01(t01);

        public void Slam() { if (State == Mode.Windup) { State = Mode.Slammed; fadeT = 0f; } }

        public void Cancel() { if (State == Mode.Windup) { State = Mode.Cancelled; fadeT = 0f; } }

        public void Hide()
        {
            State = Mode.Hidden;
            if (probe != null) { OceanProbeRegistry.Unregister(probe); probe = null; }
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        public bool Busy => State == Mode.Windup || ((State == Mode.Slammed || State == Mode.Cancelled) && fadeT < 0.6f);

        void OnDisable()
        {
            if (probe != null) { OceanProbeRegistry.Unregister(probe); probe = null; }
        }

        void OnDestroy()
        {
            if (bandMesh != null) Destroy(bandMesh);
            if (shadowMesh != null) Destroy(shadowMesh);
            if (bandMat != null) Destroy(bandMat);
            if (shadowMat != null) Destroy(shadowMat);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (State == Mode.Hidden) return;
            if (probe == null) probe = OceanProbeRegistry.Register(Centre);
            if (probe.sampledFrame != 0)
            {
                float h = probe.sample.height;
                waterY = haveWater ? Mathf.Lerp(waterY, h, 1f - Mathf.Exp(-6f * dt)) : h;
                haveWater = true;
            }
            probe.position = Centre;

            float bandA, shadowA, pulse = 1f;
            if (State == Mode.Windup)
            {
                // Close in over the first 40 %, then hold on the true radius.
                float close = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.4f, progress));
                shownRadius = Mathf.Lerp(Radius * 1.35f, Radius, close);
                // A pulse that quickens toward the slam: 1.2 -> 5 Hz.
                float hz = Mathf.Lerp(1.2f, 5f, progress * progress);
                pulse = 0.82f + 0.18f * Mathf.Sin(Time.time * hz * Mathf.PI * 2f);
                bandA = Mathf.Clamp01(progress * 5f) * pulse;
                shadowA = Mathf.Lerp(0.15f, 0.6f, progress);
                EmitPuffs(dt, Mathf.Lerp(10f, 40f, progress));
            }
            else
            {
                fadeT += dt;
                float k = State == Mode.Slammed ? fadeT / 0.5f : fadeT / 0.35f;
                if (State == Mode.Slammed) shownRadius = Radius * (1f + 0.5f * fadeT);
                bandA = Mathf.Clamp01(1f - k);
                shadowA = 0.6f * Mathf.Clamp01(1f - k * 2f);
                if (fadeT > 0.8f) { Hide(); return; }
            }
            Tint(bandColors, bandMesh, bandA, true);
            Tint(shadowColors, shadowMesh, shadowA, false);
            Place(dt);
        }

        void Place(float dt)
        {
            float y = (haveWater ? waterY : 0f) + Lift;
            transform.SetPositionAndRotation(new Vector3(Centre.x, y, Centre.z), Quaternion.identity);
            band.transform.localScale = new Vector3(shownRadius, 1f, shownRadius);
            shadow.transform.localScale = new Vector3(shownRadius, 1f, shownRadius);
            shadow.transform.localPosition = Vector3.down * 0.05f;
        }

        static void Tint(Color32[] cols, Mesh mesh, float alpha, bool isBand)
        {
            byte a = (byte)(255f * Mathf.Clamp01(alpha));
            if (isBand)
            {
                // inner 0, rise, edge full, outer 0. Warm white foam.
                for (int i = 0; i < cols.Length; i += 4)
                {
                    cols[i] = new Color32(255, 255, 255, 0);
                    cols[i + 1] = new Color32(250, 253, 255, a);
                    cols[i + 2] = new Color32(255, 255, 255, a);
                    cols[i + 3] = new Color32(255, 255, 255, 0);
                }
            }
            else
            {
                cols[0] = new Color32(8, 16, 22, a);
                for (int i = 1; i < cols.Length; i++) cols[i] = new Color32(8, 16, 22, (byte)(a * 0.7f));
            }
            mesh.colors32 = cols;
        }

        void EmitPuffs(float dt, float perSecond)
        {
            if (puffs == null) return;
            puffDebt += perSecond * dt;
            int guard = 0;
            float y = (haveWater ? waterY : 0f) + Lift - 0.1f;
            while (puffDebt >= 1f && guard++ < 6)
            {
                puffDebt -= 1f;
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float r = shownRadius * Random.Range(0.92f, 1.04f);
                var p = new ParticleSystem.EmitParams
                {
                    position = new Vector3(Centre.x + Mathf.Cos(ang) * r, y, Centre.z + Mathf.Sin(ang) * r),
                    velocity = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * -0.4f,
                    startSize = Random.Range(1.4f, 2.6f),
                    rotation = Random.Range(0f, 360f),
                    applyShapeToPosition = false,
                };
                puffs.Emit(p, 1);
            }
        }
    }
}
