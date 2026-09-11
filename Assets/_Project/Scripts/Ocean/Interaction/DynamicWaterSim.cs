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
        /// Courant number each sub-step is held to. The hard 2D limit is
        /// sqrt(0.5) ~ 0.707; 0.5 leaves real margin for the damping, rim and
        /// impulse terms rather than the 0.67-0.71 the old clamp allowed.
        const float SafeCourant = 0.5f;

        public static DynamicWaterSim Instance { get; private set; }

        /// Probe access to the live field (R = offset, G = foam).
        public RenderTexture SimTexture => curr;
        public int Resolution => n;
        public float Extent => extent;

        [SerializeField] ComputeShader rippleShader;
        [Tooltip("Wave speed of the ripples, m/s.")]
        [SerializeField] float waveSpeed = 6.5f;
        [SerializeField] float damping = 3.5f;       // amplitude e-fold, 1/s
        [SerializeField] float foamDecay = 1.4f;     // 1/s
        [Header("Player hull wake")]
        [SerializeField] float hullFoamRate = 1.6f;
        [SerializeField] float hullDisplaceRate = 0.9f;

        struct Impulse { public Vector2 uv; public float radius, height, foam; }

        RenderTexture curr, prev, scratch, scrollTmp;
        ComputeBuffer impulseBuffer;
        readonly List<Impulse> queue = new List<Impulse>();
        readonly Impulse[] uploadScratch = new Impulse[MaxImpulses];
        int kStep = -1, kInject, kScroll, kClear;
        int n;
        float extent;
        Vector2 anchor;            // world position of texel (0,0)
        Transform follow;
        Ship.ShipMotor followMotor;
        float nextFollowLookup;

        // Seconds since the sim last received an impulse, and whether it has
        // been decided (and cleared) as flat. See the quiescence check in
        // LateUpdate: an idle field dispatching Step/Inject/Scroll every
        // frame for nothing is exactly the kind of waste this pass exists to
        // remove.
        float timeSinceImpulse;
        bool quiescent;

        /// Time for any impulse's amplitude to decay under ~0.1% of itself
        /// (ln(1000) ~= 6.91 e-foldings) — comfortably below one bit of an
        /// RGHalf texel. Derived from `damping` rather than a fixed constant
        /// so a re-tuned damping rate keeps the timeout honest.
        float QuietTimeout => Mathf.Max(0.5f, 6.91f / Mathf.Max(0.01f, damping));

        // Shader.PropertyToID cache for the per-frame Set* calls below —
        // both compute-shader properties (SetInt/SetFloat/SetTexture/
        // SetBuffer all have int-nameID overloads) and the two shader
        // globals published at the end of LateUpdate.
        static readonly int NId = Shader.PropertyToID("_N");
        static readonly int ScrollOffsetId = Shader.PropertyToID("_ScrollOffset");
        static readonly int ImpulseCountId = Shader.PropertyToID("_ImpulseCount");
        static readonly int ImpulsesId = Shader.PropertyToID("Impulses");
        static readonly int CurrId = Shader.PropertyToID("Curr");
        static readonly int InjectPrevId = Shader.PropertyToID("InjectPrev");
        static readonly int SrcId = Shader.PropertyToID("Src");
        static readonly int PrevTexId = Shader.PropertyToID("PrevTex");
        static readonly int DstId = Shader.PropertyToID("Dst");
        static readonly int InjectOriginId = Shader.PropertyToID("_InjectOrigin");
        static readonly int C2Dt2Id = Shader.PropertyToID("_C2Dt2");
        static readonly int DampingId = Shader.PropertyToID("_Damping");
        static readonly int FoamDecayId = Shader.PropertyToID("_FoamDecay");
        static readonly int SimTexId = Shader.PropertyToID("_Ocean_SimTex");
        static readonly int SimRectId = Shader.PropertyToID("_Ocean_SimRect");

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

            curr = NewRT(); prev = NewRT(); scratch = NewRT(); scrollTmp = NewRT();
            impulseBuffer = new ComputeBuffer(MaxImpulses, sizeof(float) * 5);
            ClearAll();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (curr != null) curr.Release();
            if (prev != null) prev.Release();
            if (scratch != null) scratch.Release();
            if (scrollTmp != null) scrollTmp.Release();
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
            foreach (var rt in new[] { curr, prev, scratch, scrollTmp })
            {
                rippleShader.SetInt(NId, n);
                rippleShader.SetTexture(kClear, DstId, rt);
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

            // Cached with a 1 s retry, not a scene scan every frame until
            // found: same pattern as Shipyard's voyage lookup.
            if (follow == null && Time.unscaledTime >= nextFollowLookup)
            {
                followMotor = FindFirstObjectByType<Ship.ShipMotor>();
                if (followMotor != null) follow = followMotor.transform;
                nextFollowLookup = Time.unscaledTime + 1f;
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
                // While quiescent the field is already all zero, so scrolling
                // zero into zero is nothing but a dispatch: skip it and only
                // keep the anchor bookkeeping current for whenever the next
                // impulse arrives.
                else if (!quiescent)
                {
                    ScrollRT(ref curr, ox, oy);
                    ScrollRT(ref prev, ox, oy);
                }
                anchor = want;
            }

            // Player hull footprint: depression + churned foam along the keel.
            if (follow != null)
            {
                // Normalised against the ship's own top speed, not an absolute
                // 12 m/s: that constant was the old hull's flat-out and pinned
                // the wake at maximum for the whole voyage the moment she got
                // faster than it.
                float k = followMotor != null
                    ? Mathf.Clamp01(followMotor.CurrentSpeed / Mathf.Max(followMotor.MaxSpeed, 1f)) : 0f;
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

            int count = Mathf.Min(queue.Count, MaxImpulses);
            if (count > 0) timeSinceImpulse = 0f;
            else timeSinceImpulse += dt;

            // Nothing has stirred the field for QuietTimeout seconds and it
            // was already cleared to flat: Step/Inject/Scroll would dispatch
            // three kernels a frame to move zeros around. Skip until the next
            // impulse wakes it (a hull footprint counts, so this only ever
            // fires at anchor or with no ship in the scene).
            if (quiescent)
            {
                if (count == 0)
                {
                    Shader.SetGlobalTexture(SimTexId, curr);
                    Shader.SetGlobalVector(SimRectId, new Vector4(anchor.x, anchor.y, extent, texel));
                    return;
                }
                quiescent = false; // an impulse just arrived; resume from the flat field.
            }

            // Impulses, then the wave step.
            rippleShader.SetInt(NId, n);
            if (count > 0)
            {
                for (int i = 0; i < count; i++) uploadScratch[i] = queue[i];
                queue.Clear();
                impulseBuffer.SetData(uploadScratch, 0, 0, count);
                rippleShader.SetInt(ImpulseCountId, count);
                rippleShader.SetBuffer(kInject, ImpulsesId, impulseBuffer);
                rippleShader.SetTexture(kInject, CurrId, curr);
                rippleShader.SetTexture(kInject, InjectPrevId, prev);

                // Dispatch over the union bounding box of this frame's
                // impulses (texel space, clamped to the field), not the whole
                // n x n field: a handful of ~16-texel splashes were paying for
                // every texel in a 512x512+ field regardless.
                int minX = n, minY = n, maxX = -1, maxY = -1;
                for (int i = 0; i < count; i++)
                {
                    Impulse imp = uploadScratch[i];
                    float cx = imp.uv.x * n, cy = imp.uv.y * n;
                    float r = Mathf.Max(1f, imp.radius * n);
                    minX = Mathf.Min(minX, Mathf.FloorToInt(cx - r));
                    minY = Mathf.Min(minY, Mathf.FloorToInt(cy - r));
                    maxX = Mathf.Max(maxX, Mathf.CeilToInt(cx + r));
                    maxY = Mathf.Max(maxY, Mathf.CeilToInt(cy + r));
                }
                minX = Mathf.Clamp(minX, 0, n - 1); minY = Mathf.Clamp(minY, 0, n - 1);
                maxX = Mathf.Clamp(maxX, 0, n - 1); maxY = Mathf.Clamp(maxY, 0, n - 1);
                rippleShader.SetInts(InjectOriginId, minX, minY);
                int injGroupsX = Mathf.CeilToInt((maxX - minX + 1) / 8f);
                int injGroupsY = Mathf.CeilToInt((maxY - minY + 1) / 8f);
                rippleShader.Dispatch(kInject, injGroupsX, injGroupsY, 1);
            }

            // CFL: the explicit 2D wave scheme is unstable past (c dt/dx)^2 = 0.5.
            // This used to run one step and CLAMP at 0.45 — 90% of the limit,
            // and on both tiers it sat there every frame. Marginal stability
            // plus impulse forcing is what produced the needles. Sub-step
            // instead, so the authored wave speed is preserved and each step
            // is comfortably inside the limit.
            float courant = waveSpeed * dt / texel;
            int steps = Mathf.Clamp(Mathf.CeilToInt(courant / SafeCourant), 1, 4);
            float dtSub = dt / steps;
            float cSub = waveSpeed * dtSub / texel;
            rippleShader.SetFloat(C2Dt2Id, Mathf.Min(cSub * cSub, SafeCourant * SafeCourant));
            rippleShader.SetFloat(DampingId, Mathf.Exp(-damping * dtSub));
            rippleShader.SetFloat(FoamDecayId, Mathf.Exp(-foamDecay * dtSub));
            // Ping-pong: Step reads curr+prev, writes scratch, then the three
            // buffers rotate. Updating curr in place raced neighbour reads
            // against writes across threads and intermittently blew the field
            // up into giant surface spikes.
            int groups = Mathf.CeilToInt(n / 8f);
            for (int i = 0; i < steps; i++)
            {
                rippleShader.SetTexture(kStep, SrcId, curr);
                rippleShader.SetTexture(kStep, PrevTexId, prev);
                rippleShader.SetTexture(kStep, DstId, scratch);
                rippleShader.Dispatch(kStep, groups, groups, 1);
                RenderTexture next = scratch;
                scratch = prev;
                prev = curr;
                curr = next;
            }

            Shader.SetGlobalTexture(SimTexId, curr);
            Shader.SetGlobalVector(SimRectId,
                new Vector4(anchor.x, anchor.y, extent, texel));

            // Field is active; if it just crossed into silence, force it flat
            // now rather than trusting sub-threshold decay to be exactly zero,
            // and stop dispatching until the next impulse arrives.
            if (timeSinceImpulse > QuietTimeout)
            {
                ClearAll();
                quiescent = true;
            }
        }

        /// Uses a dedicated buffer rather than `scratch`: this is called twice
        /// back to back (curr, then prev) and `scratch` is also the Step
        /// kernel's write target, so the old version chained
        /// dispatch -> CopyTexture -> dispatch -> CopyTexture through one
        /// resource for no reason.
        ///
        /// Swaps the reference instead of copying the field: `rt` becomes
        /// the just-scrolled `scrollTmp` and the old `rt` becomes the next
        /// scratch buffer. Two calls back to back (curr, then prev) still
        /// work — the second dispatch's Dst is whatever the first call left
        /// in scrollTmp (the stale, about-to-be-overwritten old curr), which
        /// is scratch, not data anything reads.
        void ScrollRT(ref RenderTexture rt, int ox, int oy)
        {
            rippleShader.SetInts(ScrollOffsetId, ox, oy);
            rippleShader.SetTexture(kScroll, SrcId, rt);
            rippleShader.SetTexture(kScroll, DstId, scrollTmp);
            rippleShader.Dispatch(kScroll, Mathf.CeilToInt(n / 8f), Mathf.CeilToInt(n / 8f), 1);
            RenderTexture old = rt;
            rt = scrollTmp;
            scrollTmp = old;
        }
    }
}
