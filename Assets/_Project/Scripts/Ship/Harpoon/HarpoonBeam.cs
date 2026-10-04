using UnityEngine;

namespace SeaSick.Ship.Harpoon
{
    /// **What the gun lamp shows at night** (Kevin 2026-10-04: "like a
    /// flashlight", lighting the way forward). The ocean shader has no
    /// additional-light loop, and giving it one would cost the phone and set
    /// every lantern lighting the sea, so the spot alone shows nothing on the
    /// water. This fakes the flashlight as ONE continuous beam:
    ///
    /// - **the pool**: a warm fan flat on the water, from `lampPoolStart` m
    ///   ahead of the lamp's foot to the lamp's reach, as wide as the spot.
    ///   Alpha blended, so the sea's blue never tints the lamp's hue (additive
    ///   orange over blue read pink). It follows the swivel's yaw (the aim) but
    ///   never its tilt.
    /// - **the cone**: a faint additive shaft from the lens that comes DOWN to
    ///   the water `lampConeLength` m ahead of the foot, on the pool's own
    ///   centre line, so the two share one azimuth at any aim and the shaft
    ///   ends inside the pool. Its alpha eases from `lampConeAlpha` at the lens
    ///   to the pool's near-end alpha where it lands, so there is no step. It
    ///   is aimed from the lens to that water point, not along the lamp's
    ///   pitch, so a swell never lifts it off the pool.
    ///
    /// The water technique is `Combat/FiringArcs`' wedge: instances of the
    /// `Keep_ParticlesUnlit_Transparent` keep-alive material (so the shader is
    /// in the phone build), vertex alpha for the soft falloff, no depth write,
    /// queue 3050 so it draws after the sea, on a root of its own that follows
    /// the smoothed mean water height `lampPoolLift` m up (a pool tilted with
    /// the hull would dig into the sea on one side).
    ///
    /// Owned by `HarpoonLamp`, which ticks it in LateUpdate. Two small meshes
    /// (156 triangles) built once, rebuilt only when a knob that shapes them
    /// changes; the cone is a unit-length mesh whose root is scaled along its
    /// axis to reach the water. Colour through a property block, nothing
    /// allocated per frame; renderers off by day and wherever the gun is not
    /// sailing.
    public sealed class HarpoonBeam
    {
        // Pool: columns across the fan, rings out from the lamp.
        const int PoolSegments = 12;
        static readonly float[] PoolRingAt = { 0f, 0.12f, 0.35f, 0.65f, 1f };
        /// Ring 0 is the near end, scaled by `lampPoolNear`; the rest is fixed.
        static readonly float[] PoolRingAlpha = { 1f, 1f, 0.8f, 0.4f, 0f };
        // Cone: sides round the axis, rings out from the lens.
        const int ConeSides = 10;
        static readonly float[] ConeRingAt = { 0f, 0.3f, 0.65f, 1f };
        /// m: the cone's radius at the lens.
        const float LensRadius = 0.08f;
        /// 1/s: how fast the pool follows the water height (FiringArcs' heightFollow).
        const float HeightFollow = 2f;
        /// Below this Night01 the beam is off.
        const float NightFloor = 0.01f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        readonly Transform lampT;
        readonly HarpoonGun gun;
        readonly SeaSick.Ocean.BuoyantBody buoyancy;
        readonly SeaSick.Steamer.HullFormBody hullForm;
        readonly GameObject poolRoot, coneRoot;
        readonly MeshRenderer poolRenderer, coneRenderer;
        readonly Mesh poolMesh, coneMesh;
        readonly Material poolMaterial, coneMaterial;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly Vector3[] poolVerts, coneVerts;
        readonly Color[] poolColours, coneColours;

        float poolStart = -1f, poolEnd = -1f, poolHalf = -1f, poolAlpha = -1f, poolNear = -1f;
        float coneLand = -1f, coneHalf = -1f, coneAlpha = -1f;
        float coneLandAlpha = -1f;
        float waterY;
        bool haveWaterY;

        /// Null when the particles shader is missing (the spot still works).
        public static HarpoonBeam Create(Transform lamp)
        {
            var pool = MakeMaterial(false);
            if (pool == null) return null;
            return new HarpoonBeam(lamp, pool, MakeMaterial(true));
        }

        HarpoonBeam(Transform lamp, Material pool, Material cone)
        {
            lampT = lamp;
            poolMaterial = pool;
            coneMaterial = cone;
            gun = lamp.GetComponentInParent<HarpoonGun>();
            buoyancy = lamp.GetComponentInParent<SeaSick.Ocean.BuoyantBody>();
            hullForm = lamp.GetComponentInParent<SeaSick.Steamer.HullFormBody>();

            int poolCols = PoolSegments + 1;
            poolVerts = new Vector3[PoolRingAt.Length * poolCols];
            poolColours = new Color[poolVerts.Length];
            poolMesh = new Mesh { name = "HarpoonLampPool" };
            poolMesh.MarkDynamic();
            poolRoot = new GameObject("HarpoonLampPool");
            poolRenderer = MakeRenderer(poolRoot, poolMesh, pool);
            BuildGrid(poolMesh, PoolRingAt.Length, poolCols, PoolSegments);

            int coneCols = ConeSides + 1;
            coneVerts = new Vector3[ConeRingAt.Length * coneCols];
            coneColours = new Color[coneVerts.Length];
            coneMesh = new Mesh { name = "HarpoonLampCone" };
            coneMesh.MarkDynamic();
            // Unparented, posed from the lens each frame like the pool: the art's
            // `Lamp` casing node carries a 100x import scale (its mesh undoes
            // it), and a child cone inherited it -- a 900 m beam over the sky.
            coneRoot = new GameObject("HarpoonLampCone");
            coneRenderer = MakeRenderer(coneRoot, coneMesh, cone);
            BuildGrid(coneMesh, ConeRingAt.Length, coneCols, ConeSides);

            Hide();
        }

        /// Additive for the cone in the air; alpha blend for the pool on the water.
        static Material MakeMaterial(bool additive)
        {
            Material m;
            var keep = Resources.Load<Material>("Shaders/Keepalive/Keep_ParticlesUnlit_Transparent");
            if (keep != null) m = new Material(keep);
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) return null;
                m = new Material(shader);
                m.SetFloat("_Surface", 1f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            m.name = additive ? "HarpoonLampCone" : "HarpoonLampPool";
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (additive)
            {
                // Light in the air: adds to whatever is behind it.
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.Zero);
                m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            }
            else
            {
                // On the water: a plain alpha blend, so the sea's blue only dims
                // the lamp's colour and never mixes into it (additive went pink).
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }
            m.SetFloat("_ZWrite", 0f);
            // The cone is seen from inside and out.
            m.SetFloat("_Cull", 0f);
            // The cone draws after the pool, so it adds over it.
            m.renderQueue = additive ? 3051 : 3050;
            return m;
        }

        static MeshRenderer MakeRenderer(GameObject go, Mesh mesh, Material mat)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return mr;
        }

        /// Triangles of a rings x cols grid, set once (only the vertices move).
        static void BuildGrid(Mesh mesh, int rings, int cols, int segments)
        {
            var tris = new int[(rings - 1) * segments * 6];
            int t = 0;
            for (int k = 0; k < rings - 1; k++)
                for (int i = 0; i < segments; i++)
                {
                    int a = k * cols + i, b = a + 1, c = a + cols, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            mesh.vertices = new Vector3[rings * cols];
            mesh.triangles = tris;
        }

        /// The fan on the water, local +Z = the aim, apex at the lamp.
        void ShapePool(float start, float end, float halfDeg, float alpha, float near)
        {
            poolStart = start; poolEnd = end; poolHalf = halfDeg; poolAlpha = alpha; poolNear = near;
            int cols = PoolSegments + 1;
            for (int i = 0; i < cols; i++)
            {
                float u = Mathf.Lerp(-1f, 1f, i / (float)PoolSegments);
                float a = u * halfDeg * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                // Soft sides: full down the aim, nothing at the spot's edge.
                float side = 1f - u * u;
                for (int k = 0; k < PoolRingAt.Length; k++)
                {
                    int v = k * cols + i;
                    poolVerts[v] = dir * Mathf.Lerp(start, end, PoolRingAt[k]);
                    poolColours[v] = new Color(1f, 1f, 1f, alpha * (k == 0 ? near : PoolRingAlpha[k]) * side);
                }
            }
            poolMesh.vertices = poolVerts;
            poolMesh.colors = poolColours;
            poolMesh.RecalculateBounds();
        }

        /// The open cone along local +Z, one unit long (the root's z scale makes it
        /// the lens-to-water distance). Its width is set for a run of `land` m;
        /// alpha eases from the lens value to the pool's own centre-line alpha at
        /// the point the cone lands (`PoolAlphaAt`), so the shaft fades into it.
        void ShapeCone(float land, float halfDeg, float alpha, float poolNearAlpha)
        {
            coneLand = land; coneHalf = halfDeg; coneAlpha = alpha;
            int cols = ConeSides + 1;
            float spread = Mathf.Tan(halfDeg * Mathf.Deg2Rad);
            for (int k = 0; k < ConeRingAt.Length; k++)
            {
                float z = ConeRingAt[k];
                float r = LensRadius + z * land * spread;
                float a8 = Mathf.Lerp(alpha, poolNearAlpha, z);
                for (int i = 0; i < cols; i++)
                {
                    float a = i / (float)ConeSides * 2f * Mathf.PI;
                    int v = k * cols + i;
                    coneVerts[v] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z);
                    coneColours[v] = new Color(1f, 1f, 1f, a8);
                }
            }
            coneMesh.vertices = coneVerts;
            coneMesh.colors = coneColours;
            coneMesh.RecalculateBounds();
        }

        /// Ashore, in the yard, or with the gun off: no beam.
        bool Sailing()
        {
            if (gun != null && !gun.isActiveAndEnabled) return false;
            if (SeaSick.CameraRig.IslandCam.Engaged || SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked)
                return false;
            return !SeaSick.UI.ModularYard.ShipyardModal.IsOpen;
        }

        /// The pool's centre-line alpha at t (0..1) along its length: what the cone must meet.
        static float PoolAlphaAt(float t, float alpha, float near)
        {
            for (int k = 1; k < PoolRingAt.Length; k++)
            {
                if (t > PoolRingAt[k]) continue;
                float f = Mathf.InverseLerp(PoolRingAt[k - 1], PoolRingAt[k], t);
                float a0 = k == 1 ? near : PoolRingAlpha[k - 1];
                return alpha * Mathf.Lerp(a0, PoolRingAlpha[k], f);
            }
            return 0f;
        }

        public void Tick(float night, Color colour, float dt)
        {
            if (night <= NightFloor || lampT == null || !Sailing()) { Hide(); return; }

            float spotHalf = HarpoonTuning.lampSpotAngleDeg * 0.5f;

            // The pool.
            float start = Mathf.Max(0f, HarpoonTuning.lampPoolStart);
            float end = Mathf.Max(start + 0.5f, HarpoonTuning.lampRange * HarpoonTuning.lampPoolReach);
            float half = Mathf.Clamp(spotHalf * HarpoonTuning.lampPoolWiden, 1f, 80f);
            float poolA = Mathf.Clamp01(HarpoonTuning.lampPoolAlpha);
            float near = Mathf.Clamp01(HarpoonTuning.lampPoolNear);
            if (start != poolStart || end != poolEnd || half != poolHalf || poolA != poolAlpha || near != poolNear)
                ShapePool(start, end, half, poolA, near);

            Vector3 at = lampT.position;
            float sea = buoyancy != null ? buoyancy.MeanWaterHeight
                      : hullForm != null ? hullForm.MeanWaterHeight : 0f;
            if (!haveWaterY) { waterY = sea; haveWaterY = true; }
            else waterY = Mathf.Lerp(waterY, sea, 1f - Mathf.Exp(-HeightFollow * dt));
            // One azimuth for both shapes: the lamp's yaw, never its tilt.
            Vector3 aim = lampT.forward;
            aim.y = 0f;
            aim = aim.sqrMagnitude < 1e-6f ? Vector3.forward : aim.normalized;
            float poolY = waterY + HarpoonTuning.lampPoolLift;
            poolRoot.transform.SetPositionAndRotation(new Vector3(at.x, poolY, at.z), Quaternion.LookRotation(aim, Vector3.up));
            Color c = colour;
            c.a = night;
            block.SetColor(BaseColorId, c);
            poolRenderer.SetPropertyBlock(block);
            if (!poolRenderer.enabled) poolRenderer.enabled = true;

            // The cone: from the lens down to the pool's centre line `land` m ahead
            // of the lamp's foot, where the pool is already at its near-end alpha.
            float land = Mathf.Max(0.5f, HarpoonTuning.lampConeLength);
            float coneHalfDeg = Mathf.Clamp(spotHalf * HarpoonTuning.lampConeWiden, 0.5f, 80f);
            float coneA = Mathf.Clamp01(HarpoonTuning.lampConeAlpha);
            float landAlpha = PoolAlphaAt(Mathf.InverseLerp(start, end, land), poolA, near);
            if (land != coneLand || coneHalfDeg != coneHalf || coneA != coneAlpha || landAlpha != coneLandAlpha)
            {
                coneLandAlpha = landAlpha;
                ShapeCone(land, coneHalfDeg, coneA, landAlpha);
            }
            Vector3 target = new Vector3(at.x + aim.x * land, poolY, at.z + aim.z * land);
            Vector3 toWater = target - at;
            float reach = toWater.magnitude;
            if (reach < 0.05f) toWater = aim;
            coneRoot.transform.SetPositionAndRotation(at, Quaternion.LookRotation(toWater, Vector3.up));
            coneRoot.transform.localScale = new Vector3(1f, 1f, Mathf.Max(0.05f, reach));
            block.SetColor(BaseColorId, c);
            coneRenderer.SetPropertyBlock(block);
            if (!coneRenderer.enabled) coneRenderer.enabled = true;
        }

        public void Hide()
        {
            if (poolRenderer != null && poolRenderer.enabled) poolRenderer.enabled = false;
            if (coneRenderer != null && coneRenderer.enabled) coneRenderer.enabled = false;
            haveWaterY = false;
        }

        public void Destroy()
        {
            if (poolRoot != null) Object.Destroy(poolRoot);
            if (coneRoot != null) Object.Destroy(coneRoot);
            if (poolMesh != null) Object.Destroy(poolMesh);
            if (coneMesh != null) Object.Destroy(coneMesh);
            if (poolMaterial != null) Object.Destroy(poolMaterial);
            if (coneMaterial != null) Object.Destroy(coneMaterial);
        }
    }
}
