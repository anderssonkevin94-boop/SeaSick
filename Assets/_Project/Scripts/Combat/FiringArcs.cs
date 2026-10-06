using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Combat
{
    /// **Where each broadside can reach, drawn on the water** (2026-10-02).
    ///
    /// Kevin: "when I engage with the enemy I have a real hard time
    /// controlling the ship." Aiming is the tiller, but nothing showed the
    /// player what the tiller was aiming: which way to turn, how far, and
    /// whether a side was loaded. One flat wedge a side, from the middle of
    /// that side's guns, out to the reach auto-fire uses and +- the guns'
    /// traverse -- the exact gate `CannonBattery.InReach` fires on, so the
    /// wedge and the guns cannot disagree.
    ///
    /// Shown while the combat row is (`CannonBattery.EnemyInRangeNow`: a lock,
    /// or a hostile inside 1.5x gun range), faded over `fadeSeconds`.
    /// Dim: no loaded, manned gun on that side. Amber: loaded. Pulsing
    /// green-white: loaded AND the target is inside it -- with a lock, the
    /// guns are firing.
    ///
    /// Added by `CannonBattery.Start`. One mesh built once and shared by both
    /// sides (port is the starboard wedge mirrored), colour through a
    /// MaterialPropertyBlock, nothing allocated per frame. The material is an
    /// instance of `Keep_ParticlesUnlit_Transparent` loaded from Resources, so
    /// its shader is in the phone build (`Resources/Shaders/Keepalive/README.md`);
    /// Particles/Unlit because it multiplies vertex colour, which is what fades
    /// the wedge in from the hull and draws its rim.
    ///
    /// The wedges hang off a root of their own, not the ship: the hull rolls
    /// and pitches, and a 60 m plate tilted with her would dig into the sea
    /// on one side and float off it on the other. The root follows her yaw
    /// and the smoothed mean water height under her.
    public class FiringArcs : MonoBehaviour
    {
        const int Segments = 24;
        // Rings out from the apex, as a fraction of the reach, and the
        // vertex alpha at each: faint by the hull, fuller toward the end,
        // a bright band at the limit so the edge of reach reads at a glance.
        static readonly float[] RingAt = { 0.10f, 0.90f, 0.95f, 1f };
        static readonly float[] RingAlpha = { 0.08f, 0.32f, 1f, 1f };
        // The two straight sides get a little of the rim's weight too.
        const float EdgeAlpha = 0.55f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] float fadeSeconds = 0.3f;
        [Tooltip("Metres above the mean water under her. Enough to clear the ripples; crests still cut through in a big sea.")]
        [SerializeField] float lift = 0.35f;
        [Tooltip("1/s: how fast the plate follows the water height under her.")]
        [SerializeField] float heightFollow = 2f;
        [SerializeField] Color dimColour = new Color(0.72f, 0.80f, 0.88f, 0.22f);
        [SerializeField] Color loadedColour = new Color(1f, 0.66f, 0.16f, 0.5f);
        [SerializeField] Color bearsColour = new Color(0.50f, 1f, 0.50f, 0.75f);
        [SerializeField] Color bearsPulseColour = new Color(1f, 0.96f, 0.84f, 0.95f);
        [SerializeField] float pulseHz = 2.2f;

        CannonBattery battery;
        SeaSick.Ocean.BuoyantBody buoyancy;
        GameObject root;
        Transform portArc, starArc;
        MeshRenderer portRenderer, starRenderer;
        Mesh mesh;
        Material material;
        MaterialPropertyBlock block;
        float builtHalfDeg = -1f;
        SeaSick.Ship.ShipMotor motor;
        float shown;
        float waterY;
        bool haveWaterY;
        Color portColour, starColour;

        void Start()
        {
            battery = GetComponent<CannonBattery>();
            buoyancy = GetComponent<SeaSick.Ocean.BuoyantBody>();
            if (battery == null || !MakeMaterial()) { enabled = false; return; }

            block = new MaterialPropertyBlock();
            mesh = new Mesh { name = "FiringArc" };
            mesh.MarkDynamic();

            root = new GameObject("FiringArcs");
            starArc = MakeSide("Starboard", out starRenderer);
            portArc = MakeSide("Port", out portRenderer);
            portColour = starColour = dimColour;
            root.SetActive(false);
        }

        bool MakeMaterial()
        {
            var keep = Resources.Load<Material>("Shaders/Keepalive/Keep_ParticlesUnlit_Transparent");
            if (keep != null) material = new Material(keep);
            else
            {
                // Editor-only safety: the keep-alive is what ships it.
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null)
                {
                    Debug.LogError("[FiringArcs] Particles/Unlit missing (Resources/Shaders/Keepalive/Keep_ParticlesUnlit_Transparent.mat keeps it in a build)");
                    return false;
                }
                material = new Material(shader);
                material.SetFloat("_Surface", 1f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            material.name = "FiringArc";
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            // Port is the starboard mesh mirrored, which flips its winding.
            material.SetFloat("_Cull", 0f);
            // After the sea and its foam, so the plate is never fighting the
            // water surface for depth; it never writes depth itself.
            material.renderQueue = 3050;
            return true;
        }

        Transform MakeSide(string name, out MeshRenderer mr)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go.transform;
        }

        /// A unit-reach wedge about +x (starboard beam), +-`halfDeg` toward
        /// the bow. Rebuilt only if the traverse limit changes.
        void BuildMesh(float halfDeg)
        {
            builtHalfDeg = halfDeg;
            int rings = RingAt.Length;
            int cols = Segments + 1;
            var verts = new Vector3[rings * cols];
            var colours = new Color[verts.Length];
            for (int i = 0; i < cols; i++)
            {
                float a = Mathf.Lerp(-halfDeg, halfDeg, i / (float)Segments) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                bool edge = i == 0 || i == Segments;
                for (int k = 0; k < rings; k++)
                {
                    int v = k * cols + i;
                    verts[v] = dir * RingAt[k];
                    float alpha = edge ? Mathf.Max(RingAlpha[k], k == 0 ? RingAlpha[0] : EdgeAlpha) : RingAlpha[k];
                    colours[v] = new Color(1f, 1f, 1f, alpha);
                }
            }
            var tris = new int[(rings - 1) * Segments * 6];
            int t = 0;
            for (int k = 0; k < rings - 1; k++)
                for (int i = 0; i < Segments; i++)
                {
                    int a = k * cols + i, b = a + 1, c = a + cols, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.colors = colours;
            mesh.triangles = tris;
            var normals = new Vector3[verts.Length];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
            mesh.RecalculateBounds();
        }

        void LateUpdate()
        {
            if (root == null) return;
            float dt = Time.deltaTime;

            // Not at anchor, at a berth, or under the island view (2026-10-04:
            // a raider in range of the home pier drew both wedges over the bay
            // in the island camera at noon) -- the arcs are a sea-fight aid.
            if (motor == null) motor = GetComponentInParent<SeaSick.Ship.ShipMotor>();
            bool atSea = !(motor != null && motor.Anchored) && !SeaSick.CameraRig.IslandCam.Engaged;
            bool want = atSea && (!SeaSick.UI.Sheets.QuietSailingHud.Active || SeaSick.UI.Sheets.QuietSailingHud.WeaponsOpen) && battery.isActiveAndEnabled && battery.TotalGuns > 0
                        && battery.ArcRange > 1f && battery.EnemyInRangeNow;
            shown = Mathf.MoveTowards(shown, want ? 1f : 0f, dt / Mathf.Max(0.01f, fadeSeconds));
            if (shown <= 0f)
            {
                if (root.activeSelf) root.SetActive(false);
                haveWaterY = false;
                return;
            }
            if (!root.activeSelf) root.SetActive(true);

            float half = battery.ArcHalfDeg;
            if (!Mathf.Approximately(half, builtHalfDeg)) BuildMesh(half);

            // Flat, at her yaw, on the water under her.
            Vector3 p = transform.position;
            float sea = buoyancy != null ? buoyancy.MeanWaterHeight : 0f;
            if (!haveWaterY) { waterY = sea; haveWaterY = true; }
            else waterY = Mathf.Lerp(waterY, sea, 1f - Mathf.Exp(-heightFollow * dt));
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            root.transform.SetPositionAndRotation(new Vector3(p.x, waterY + lift, p.z),
                                                  Quaternion.LookRotation(fwd.normalized, Vector3.up));

            float reach = battery.ArcRange;
            var target = battery.ArcTarget;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseHz * 2f * Mathf.PI);
            float blend = 1f - Mathf.Exp(-12f * dt);

            UpdateSide(true, starArc, starRenderer, ref starColour, reach, target, pulse, blend);
            UpdateSide(false, portArc, portRenderer, ref portColour, reach, target, pulse, blend);
        }

        void UpdateSide(bool starboard, Transform arc, MeshRenderer mr, ref Color colour,
                        float reach, IHittable target, float pulse, float blend)
        {
            if (!battery.ArcApex(starboard, out Vector3 apex))
            {
                if (mr.enabled) mr.enabled = false;
                return;
            }
            if (!mr.enabled) mr.enabled = true;

            arc.localPosition = apex;
            arc.localScale = new Vector3(starboard ? reach : -reach, 1f, reach);

            bool loaded = (starboard ? battery.StarboardReady : battery.PortReady) > 0;
            bool bears = target != null && battery.SideReaches(starboard, target);
            Color wantColour = loaded
                ? (bears ? Color.Lerp(bearsColour, bearsPulseColour, pulse) : loadedColour)
                : (bears ? Color.Lerp(dimColour, bearsColour, 0.3f) : dimColour);
            colour = Color.Lerp(colour, wantColour, blend);

            Color c = colour;
            c.a *= shown;
            block.SetColor(BaseColorId, c);
            mr.SetPropertyBlock(block);
        }

        void OnDisable()
        {
            if (root != null) root.SetActive(false);
            shown = 0f;
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root);
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
