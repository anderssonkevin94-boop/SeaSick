using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ocean
{
    /// Ship-following ripple/wake simulation: a 2D wave-equation buffer
    /// (R = surface offset, G = contact foam) that the surface shader
    /// composites over the FFT sea. Hull footprints, cannonball splashes and
    /// monster thrash arrive as gaussian impulses; the sim spreads, decays
    /// and rim-damps them. Decoupled from the cascades by design: it
    /// composites, it does not feed back into the spectrum.
    public class DynamicWaterSim : MonoBehaviour
    {
        const int MaxImpulses = 64;

        public static DynamicWaterSim Instance { get; private set; }

        /// Probe access to the live field (R = offset, G = foam).
        public RenderTexture SimTexture => curr;
        public int Resolution => n;

        [SerializeField] ComputeShader rippleShader;
        [Tooltip("Wave speed of the ripples, m/s.")]
        [SerializeField] float waveSpeed = 6.5f;
        [SerializeField] float damping = 3.5f;       // amplitude e-fold, 1/s
        [SerializeField] float foamDecay = 1.4f;     // 1/s
        [Header("Player hull wake")]
        [SerializeField] float hullFoamRate = 1.6f;
        [SerializeField] float hullDisplaceRate = 0.9f;

        struct Impulse { public Vector2 uv; public float radius, height, foam; }

        RenderTexture curr, prev, scratch;
        ComputeBuffer impulseBuffer;
        readonly List<Impulse> queue = new List<Impulse>();
        readonly Impulse[] uploadScratch = new Impulse[MaxImpulses];
        int kStep = -1, kInject, kScroll, kClear;
        int n;
        float extent;
        Vector2 anchor;            // world position of texel (0,0)
        Transform follow;

        void OnEnable()
        {
            Instance = this;
            var q = OceanQuality.Active;
            n = q != null ? q.rippleSimResolution : 512;
            extent = q != null ? q.rippleSimExtent : 100f;

            if (rippleShader == null)
            {
                enabled = false;
                return;
            }
            kStep = rippleShader.FindKernel("Step");
            kInject = rippleShader.FindKernel("Inject");
            kScroll = rippleShader.FindKernel("Scroll");
            kClear = rippleShader.FindKernel("Clear");

            curr = NewRT(); prev = NewRT(); scratch = NewRT();
            impulseBuffer = new ComputeBuffer(MaxImpulses, sizeof(float) * 5);
            ClearAll();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (curr != null) curr.Release();
            if (prev != null) prev.Release();
            if (scratch != null) scratch.Release();
            impulseBuffer?.Dispose();
            impulseBuffer = null;
        }

        RenderTexture NewRT()
        {
            var rt = new RenderTexture(n, n, 0, RenderTextureFormat.RGHalf,
                RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        void ClearAll()
        {
            foreach (var rt in new[] { curr, prev, scratch })
            {
                rippleShader.SetInt("_N", n);
                rippleShader.SetTexture(kClear, "Dst", rt);
                rippleShader.Dispatch(kClear, Mathf.CeilToInt(n / 8f), Mathf.CeilToInt(n / 8f), 1);
            }
        }

        /// Direct impulse into the sim: negative height is a depression
        /// (hull trough), positive a mound. Radius in metres.
        public void Stamp(Vector2 worldPos, float radius, float foam, float displace)
        {
            if (queue.Count >= MaxImpulses) return;
            Vector2 uv = (worldPos - anchor) / extent;
            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) return;
            queue.Add(new Impulse
            {
                uv = uv,
                radius = Mathf.Clamp01(radius / extent),
                height = -displace,
                foam = foam,
            });
        }

        /// One-off splash ring (cannonballs, breaches, jettison).
        public static void Splash(Vector3 worldPos, float radius, float strength)
        {
            if (Instance == null) return;
            Instance.Stamp(new Vector2(worldPos.x, worldPos.z), radius,
                strength * 0.5f, -strength * 0.35f);
        }

        void LateUpdate()
        {
            if (kStep < 0) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) return;

            if (follow == null)
            {
                var motor = FindFirstObjectByType<Ship.ShipMotor>();
                if (motor != null) follow = motor.transform;
            }

            // Texel-snapped follow: scroll content when the anchor moves.
            Vector3 fp = follow != null ? follow.position : Vector3.zero;
            float texel = extent / n;
            Vector2 want = new Vector2(
                Mathf.Floor((fp.x - extent * 0.5f) / texel) * texel,
                Mathf.Floor((fp.z - extent * 0.5f) / texel) * texel);
            Vector2 delta = want - anchor;
            int ox = Mathf.RoundToInt(delta.x / texel);
            int oy = Mathf.RoundToInt(delta.y / texel);
            if (ox != 0 || oy != 0)
            {
                if (Mathf.Abs(ox) >= n || Mathf.Abs(oy) >= n) ClearAll();
                else
                {
                    ScrollRT(curr, ox, oy);
                    ScrollRT(prev, ox, oy);
                }
                anchor = want;
            }

            // Player hull footprint: depression + churned foam along the keel.
            if (follow != null)
            {
                var motor = follow.GetComponent<Ship.ShipMotor>();
                float k = motor != null ? Mathf.Clamp01(motor.CurrentSpeed / 12f) : 0f;
                if (k > 0.05f)
                {
                    Vector3 fwd = follow.forward; fwd.y = 0f; fwd = fwd.normalized;
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 p = fp + fwd * (5.5f - i * 6.5f);
                        Stamp(new Vector2(p.x, p.z), 3.2f,
                            hullFoamRate * k * dt, hullDisplaceRate * k * dt);
                    }
                }
            }

            // Impulses, then the wave step.
            rippleShader.SetInt("_N", n);
            int count = Mathf.Min(queue.Count, MaxImpulses);
            if (count > 0)
            {
                for (int i = 0; i < count; i++) uploadScratch[i] = queue[i];
                queue.Clear();
                impulseBuffer.SetData(uploadScratch, 0, 0, count);
                rippleShader.SetInt("_ImpulseCount", count);
                rippleShader.SetBuffer(kInject, "Impulses", impulseBuffer);
                rippleShader.SetTexture(kInject, "Curr", curr);
                rippleShader.Dispatch(kInject, Mathf.CeilToInt(count / 64f), 1, 1);
            }

            float c2 = waveSpeed * dt / texel;
            rippleShader.SetFloat("_C2Dt2", Mathf.Min(c2 * c2, 0.45f));
            rippleShader.SetFloat("_Damping", Mathf.Exp(-damping * dt));
            rippleShader.SetFloat("_FoamDecay", Mathf.Exp(-foamDecay * dt));
            rippleShader.SetTexture(kStep, "Curr", curr);
            rippleShader.SetTexture(kStep, "Prev", prev);
            rippleShader.Dispatch(kStep, Mathf.CeilToInt(n / 8f), Mathf.CeilToInt(n / 8f), 1);
            // Step writes both in place: Curr := new field, Prev := old field.
            // No swap needed.

            Shader.SetGlobalTexture("_Ocean_SimTex", curr);
            Shader.SetGlobalVector("_Ocean_SimRect",
                new Vector4(anchor.x, anchor.y, extent, texel));
        }

        void ScrollRT(RenderTexture rt, int ox, int oy)
        {
            rippleShader.SetInts("_ScrollOffset", ox, oy);
            rippleShader.SetTexture(kScroll, "Src", rt);
            rippleShader.SetTexture(kScroll, "Dst", scratch);
            rippleShader.Dispatch(kScroll, Mathf.CeilToInt(n / 8f), Mathf.CeilToInt(n / 8f), 1);
            // Copy back.
            Graphics.CopyTexture(scratch, rt);
        }
    }
}
