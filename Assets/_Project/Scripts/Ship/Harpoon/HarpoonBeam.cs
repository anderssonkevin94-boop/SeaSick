using UnityEngine;

namespace SeaSick.Ship.Harpoon
{
    /// **What the gun lamp shows at night** (Kevin 2026-10-04: "like a
    /// flashlight", lighting the way forward). The ocean shader has no
    /// additional-light loop, and giving it one would cost the phone and set
    /// every lantern lighting the sea, so the spot alone shows nothing on the
    /// water. This fakes the two things a flashlight reads as:
    ///
    /// - **the pool**: a soft warm fan flat on the water ahead of the lamp,
    ///   from `lampPoolStart` m out to the lamp's reach, as wide as the spot.
    ///   It follows the swivel's yaw (the aim) but never its tilt.
    /// - **the cone**: a faint open cone from the lens, fading to nothing a
    ///   few metres out, so the beam reads in the air from the high camera.
    ///
    /// The water technique is `Combat/FiringArcs`'s wedge: an instance of the
    /// `Keep_ParticlesUnlit_Transparent` keep-alive material (so the shader is
    /// in the phone build), vertex alpha for the soft falloff, no depth write,
    /// queue 3050 so it draws after the sea, on a root of its own that follows
    /// the smoothed mean water height `lampPoolLift` m up (a pool tilted with
    /// the hull would dig into the sea on one side). Here it blends additively,
    /// since it is light.
    ///
    /// Owned by `HarpoonLamp`, which ticks it in LateUpdate. Two small meshes
    /// built once (rebuilt only when a knob that shapes them changes), colour
    /// through a property block, nothing allocated per frame; renderers off
    /// by day and wherever the gun is not sailing.
    public sealed class HarpoonBeam
    {
        // Pool: columns across the fan, rings out from the lamp.
        const int PoolSegments = 12;
        static readonly float[] PoolRingAt = { 0f, 0.12f, 0.35f, 0.65f, 1f };
        static readonly float[] PoolRingAlpha = { 0f, 1f, 0.8f, 0.4f, 0f };
        // Cone: sides round the axis, rings out from the lens.
        const int ConeSides = 10;
        static readonly float[] ConeRingAt = { 0f, 0.3f, 0.65f, 1f };
        static readonly float[] ConeRingAlpha = { 1f, 0.5f, 0.18f, 0f };
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
        readonly GameObject poolRoot;
        readonly MeshRenderer poolRenderer, coneRenderer;
        readonly Mesh poolMesh, coneMesh;
        readonly Material material;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly Vector3[] poolVerts, coneVerts;
        readonly Color[] poolColours, coneColours;

        float poolStart = -1f, poolEnd = -1f, poolHalf = -1f;
        float coneLength = -1f, coneHalf = -1f;
        float waterY;
        bool haveWaterY;

        /// Null when the particles shader is missing (the spot still works).
        public static HarpoonBeam Create(Transform lamp)
        {
            var mat = MakeMaterial();
            return mat != null ? new HarpoonBeam(lamp, mat) : null;
        }

        HarpoonBeam(Transform lamp, Material mat)
        {
            lampT = lamp;
            material = mat;
            gun = lamp.GetComponentInParent<HarpoonGun>();
            buoyancy = lamp.GetComponentInParent<SeaSick.Ocean.BuoyantBody>();
            hullForm = lamp.GetComponentInParent<SeaSick.Steamer.HullFormBody>();

            int poolCols = PoolSegments + 1;
            poolVerts = new Vector3[PoolRingAt.Length * poolCols];
            poolColours = new Color[poolVerts.Length];
            poolMesh = new Mesh { name = "HarpoonLampPool" };
            poolMesh.MarkDynamic();
            poolRoot = new GameObject("HarpoonLampPool");
            poolRenderer = MakeRenderer(poolRoot, poolMesh, mat);
            BuildGrid(poolMesh, PoolRingAt.Length, poolCols, PoolSegments);

            int coneCols = ConeSides + 1;
            coneVerts = new Vector3[ConeRingAt.Length * coneCols];
            coneColours = new Color[coneVerts.Length];
            coneMesh = new Mesh { name = "HarpoonLampCone" };
            coneMesh.MarkDynamic();
            var coneGo = new GameObject("Lamp_Cone");
            coneGo.transform.SetParent(lamp, false);
            coneRenderer = MakeRenderer(coneGo, coneMesh, mat);
            BuildGrid(coneMesh, ConeRingAt.Length, coneCols, ConeSides);

            Hide();
        }

        static Material MakeMaterial()
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
            m.name = "HarpoonLampBeam";
            m.SetOverrideTag("RenderType", "Transparent");
            // Additive: light on dark water, never a tint over it.
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.Zero);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            // The cone is seen from inside and out.
            m.SetFloat("_Cull", 0f);
            m.renderQueue = 3050;
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
        void ShapePool(float start, float end, float halfDeg)
        {
            poolStart = start; poolEnd = end; poolHalf = halfDeg;
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
                    poolColours[v] = new Color(1f, 1f, 1f, PoolRingAlpha[k] * side);
                }
            }
            poolMesh.vertices = poolVerts;
            poolMesh.colors = poolColours;
            poolMesh.RecalculateBounds();
        }

        /// The open cone along local +Z from the lens.
        void ShapeCone(float length, float halfDeg)
        {
            coneLength = length; coneHalf = halfDeg;
            int cols = ConeSides + 1;
            float spread = Mathf.Tan(halfDeg * Mathf.Deg2Rad);
            for (int k = 0; k < ConeRingAt.Length; k++)
            {
                float z = ConeRingAt[k] * length;
                float r = LensRadius + z * spread;
                for (int i = 0; i < cols; i++)
                {
                    float a = i / (float)ConeSides * 2f * Mathf.PI;
                    int v = k * cols + i;
                    coneVerts[v] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z);
                    coneColours[v] = new Color(1f, 1f, 1f, ConeRingAlpha[k]);
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

        public void Tick(float night, Color colour, float dt)
        {
            if (night <= NightFloor || lampT == null || !Sailing()) { Hide(); return; }

            float spotHalf = HarpoonTuning.lampSpotAngleDeg * 0.5f;

            // The pool.
            float start = Mathf.Max(0f, HarpoonTuning.lampPoolStart);
            float end = Mathf.Max(start + 0.5f, HarpoonTuning.lampRange * HarpoonTuning.lampPoolReach);
            float half = Mathf.Clamp(spotHalf * HarpoonTuning.lampPoolWiden, 1f, 80f);
            if (start != poolStart || end != poolEnd || half != poolHalf) ShapePool(start, end, half);

            Vector3 at = lampT.position;
            float sea = buoyancy != null ? buoyancy.MeanWaterHeight
                      : hullForm != null ? hullForm.MeanWaterHeight : 0f;
            if (!haveWaterY) { waterY = sea; haveWaterY = true; }
            else waterY = Mathf.Lerp(waterY, sea, 1f - Mathf.Exp(-HeightFollow * dt));
            Vector3 aim = lampT.forward;
            aim.y = 0f;
            if (aim.sqrMagnitude < 1e-6f) aim = Vector3.forward;
            poolRoot.transform.SetPositionAndRotation(new Vector3(at.x, waterY + HarpoonTuning.lampPoolLift, at.z),
                                                      Quaternion.LookRotation(aim.normalized, Vector3.up));
            Color c = colour;
            c.a = HarpoonTuning.lampPoolAlpha * night;
            block.SetColor(BaseColorId, c);
            poolRenderer.SetPropertyBlock(block);
            if (!poolRenderer.enabled) poolRenderer.enabled = true;

            // The cone.
            float length = Mathf.Max(0.5f, HarpoonTuning.lampConeLength);
            float coneHalfDeg = Mathf.Clamp(spotHalf * HarpoonTuning.lampConeWiden, 0.5f, 80f);
            if (length != coneLength || coneHalfDeg != coneHalf) ShapeCone(length, coneHalfDeg);
            c.a = HarpoonTuning.lampConeAlpha * night;
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
            if (coneRenderer != null) Object.Destroy(coneRenderer.gameObject);
            if (poolMesh != null) Object.Destroy(poolMesh);
            if (coneMesh != null) Object.Destroy(coneMesh);
            if (material != null) Object.Destroy(material);
        }
    }
}
