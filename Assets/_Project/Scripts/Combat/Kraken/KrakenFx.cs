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
        static Material foamMat;

        static Material FoamMat()
        {
            if (foamMat != null) return foamMat;
            foamMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            foamMat.SetColor("_BaseColor", new Color(0.93f, 0.97f, 0.98f, 1f));
            foamMat.SetTexture("_BaseMap", FoamTexture.SoftPuff());
            MakeTransparent(foamMat, 3005);
            return foamMat;
        }

        internal static Material FoamMaterial() => FoamMat();

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

        // ------------------------------------------------------- splash look
        // Kevin on the step-1 captures (2026-10-03): the breach spray "reads
        // as soft white smudges, like drops on a camera lens". Three causes,
        // three fixes: the puff texture is all soft edge (now a HARD-edged
        // droplet and a hard-edged ragged sheet, both made here), the drops
        // were metres across (now 0.15-0.5 m streaks stretched along their
        // own velocity, under real gravity), and nothing capped a particle's
        // size on screen, so the few that flew past the lens filled it (now
        // `maxParticleSize`, a fraction of the viewport).
        static Material dropMat, sheetMat;
        static Texture2D dropTex, sheetTex;

        /// A round drop with a 1.5 px edge: stretched along its velocity it
        /// is a crisp streak, not a blur.
        static Texture2D DropTex()
        {
            if (dropTex != null) return dropTex;
            const int n = 32;
            dropTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "KrakenDrop", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float c = (n - 1) * 0.5f, r = n * 0.42f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01((r - d) / 1.5f);
                // A faint bright core, the rest flat white: water catching light.
                float core = 1f - 0.12f * Mathf.Clamp01(d / r);
                byte v = (byte)(255f * core);
                px[y * n + x] = new Color32(v, v, 255, (byte)(255f * a));
            }
            dropTex.SetPixels32(px);
            dropTex.Apply(true, true);
            return dropTex;
        }

        /// A ragged sheet of white water: a blob whose rim wanders with a few
        /// sines, a 2 px hard edge and a slightly thinner middle -- a torn
        /// curtain of spray at the waterline, readable at 100 m.
        static Texture2D SheetTex()
        {
            if (sheetTex != null) return sheetTex;
            const int n = 64;
            sheetTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "KrakenSheet", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);
                float rim = n * (0.36f + 0.05f * Mathf.Sin(ang * 3f + 0.7f)
                                       + 0.035f * Mathf.Sin(ang * 7f + 2.1f)
                                       + 0.02f * Mathf.Sin(ang * 13f));
                float a = Mathf.Clamp01((rim - d) / 2f);
                float body = Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(d / rim));   // thinner middle
                px[y * n + x] = new Color32(250, 252, 255, (byte)(255f * a * body));
            }
            sheetTex.SetPixels32(px);
            sheetTex.Apply(true, true);
            return sheetTex;
        }

        static Material DropMat()
        {
            if (dropMat != null) return dropMat;
            dropMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            dropMat.SetColor("_BaseColor", new Color(0.95f, 0.98f, 1f, 1f));
            dropMat.SetTexture("_BaseMap", DropTex());
            MakeTransparent(dropMat, 3012);
            return dropMat;
        }

        static Material SheetMat()
        {
            if (sheetMat != null) return sheetMat;
            sheetMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            sheetMat.SetColor("_BaseColor", new Color(0.96f, 0.98f, 1f, 1f));
            sheetMat.SetTexture("_BaseMap", SheetTex());
            MakeTransparent(sheetMat, 3011);
            return sheetMat;
        }

        /// The breach: a crown of streaked drops and torn white sheets thrown
        /// up round the head, and a ring of foam spreading out on the water
        /// from `radius`. Fire-and-forget, destroys itself.
        public static void Breach(Vector3 waterPoint, float radius)
        {
            var go = Splash(waterPoint, radius, 1.6f, "KrakenBreach");

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
        }

        /// A slam or a breach hitting the water: a crown of hard-edged drops
        /// streaked along their velocity, thrown up and out from a ring of
        /// `radius` and falling back under real gravity, plus a few big torn
        /// sheets of white water standing up at the waterline. `power` 1 = an
        /// arm slam; the breach uses more. Every particle is size-capped on
        /// screen so one passing the lens never fills it. Destroys itself.
        public static GameObject Splash(Vector3 waterPoint, float radius, float power, string name = "KrakenSplash")
        {
            var go = new GameObject(name);
            go.transform.position = waterPoint;
            float sp = Mathf.Sqrt(Mathf.Max(0.1f, power));

            // Drops: fast, small, streaked.
            var drops = NewSystem(go.transform, "Drops", DropMat(), 700);
            var main = drops.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 2.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(9f * sp, 22f * sp);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
            main.gravityModifier = 1f;
            var dr = drops.GetComponent<ParticleSystemRenderer>();
            dr.renderMode = ParticleSystemRenderMode.Stretch;
            dr.velocityScale = 0.09f;
            dr.lengthScale = 1f;
            dr.maxParticleSize = 0.015f;
            var shape = drops.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = radius * 0.55f;
            shape.radiusThickness = 0.35f;   // from the rim: a crown, not a column
            drops.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            Fade(drops, 1f, 0.05f, 0.75f);
            drops.Emit(Mathf.RoundToInt(Mathf.Clamp(260f * power, 60f, 680f)));

            // Sheets: a few big torn curtains standing up at the waterline.
            var sheets = NewSystem(go.transform, "Sheets", SheetMat(), 40);
            var sm = sheets.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(5f * sp, 10f * sp);
            sm.startSize = new ParticleSystem.MinMaxCurve(radius * 0.35f, radius * 0.7f);
            sm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            sm.gravityModifier = 0.8f;
            var shr = sheets.GetComponent<ParticleSystemRenderer>();
            shr.renderMode = ParticleSystemRenderMode.Billboard;
            shr.maxParticleSize = 0.09f;
            var sshape = sheets.shape;
            sshape.enabled = true;
            sshape.shapeType = ParticleSystemShapeType.Cone;
            sshape.angle = 18f;
            sshape.radius = radius * 0.6f;
            sshape.radiusThickness = 0.2f;
            sheets.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            Fade(sheets, 0.95f, 0.04f, 0.55f);
            var grow = sheets.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.25f)));
            sheets.Emit(Mathf.RoundToInt(Mathf.Clamp(9f * power, 5f, 22f)));

            Object.Destroy(go, 8f);
            return go;
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

        /// Alpha in fast to `peak`, held, out by the end; `holdUntil` is where
        /// the fade-out starts (0..1 of the life).
        static void Fade(ParticleSystem ps, float peak, float inAt, float holdUntil)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, inAt),
                        new GradientAlphaKey(peak, holdUntil), new GradientAlphaKey(0f, 1f) });
            col.color = g;
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
