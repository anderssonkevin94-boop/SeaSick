using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SeaSick.Ocean
{
    public struct OceanSample
    {
        public float height;        // world Y of the surface at the query XZ
        public float3 displacement; // full xyz displacement at the source point
        public float3 normal;
        public float3 velocity;     // orbital velocity of the surface
        public float foam;          // 0..1 (populated from M9)
    }

    /// The whole CPU-side sampling math in one blittable struct so the Burst
    /// job and the main-thread SampleImmediate path are literally the same
    /// code — a second implementation is how physics oceans go wrong.
    ///
    /// Mirrors the GPU exactly: bilinear-with-repeat over the readback texels,
    /// cascades 0-1 (cascade 2 is visual chop shorter than any hull probe),
    /// regional envelope multiplied in, and 3 Newton iterations to invert the
    /// horizontal choppiness displacement (the sea is not a heightfield).
    public struct OceanFieldData
    {
        /// Newton steps used to invert the horizontal choppiness displacement.
        /// Three was ample while crests were metres; a 60 m sea puts far more
        /// query points on or near a folding crest, where convergence is slow
        /// and the inverse may not be unique at all.
        ///
        /// Five, then SIX. The count tracks the surface's steepness, not its
        /// height: splitting the storm into two crossing trains doubled the
        /// median face angle (7.7 -> 15.4 deg) at an unchanged Hs, and the
        /// parity gate promptly failed at 6.79 cm with 2 of 5000 points over
        /// the 5 cm bar -- a handful of outliers on the sharpest crests, which
        /// is the signature of non-convergence rather than a wrong formula.
        /// The envelope term was untouched throughout (0.000267), which is how
        /// the three-way split identified it.
        ///
        /// If the sea is ever made steeper again, expect to pay another step.
        const int NewtonIterations = 6;

        [ReadOnly] public NativeArray<half4> disp0, disp1, deriv0, deriv1;
        [ReadOnly] public NativeArray<half4> prevDisp0, prevDisp1;
        [ReadOnly] public NativeArray<half> turb0;
        [ReadOnly] public NativeArray<float4> islands;
        [ReadOnly] public NativeArray<float> shore;
        public RegionFieldParams region;
        public int n;
        public float2 invPatch;   // 1/L0, 1/L1
        public float velDt;       // tLatest - tPrevious (0 = no velocity yet)
        public bool valid;

        // -- bilinear fetch with repeat wrap, matching GPU SampleLevel --
        static float4 F(half4 h) => new float4(h.x, h.y, h.z, h.w);

        float4 Bilinear(NativeArray<half4> tex, float2 uv)
        {
            float2 p = uv * n - 0.5f;
            int2 i0 = (int2)math.floor(p);
            float2 f = p - i0;
            i0 = ((i0 % n) + n) % n;
            int2 i1 = (i0 + 1) % n;
            float4 a = F(tex[i0.y * n + i0.x]);
            float4 b = F(tex[i0.y * n + i1.x]);
            float4 c = F(tex[i1.y * n + i0.x]);
            float4 d = F(tex[i1.y * n + i1.x]);
            return math.lerp(math.lerp(a, b, f.x), math.lerp(c, d, f.x), f.y);
        }

        // Raw, un-enveloped. Kept for the probe's diagnostic split.
        float4 SampleDispRaw(float2 xz) =>
            Bilinear(disp0, xz * invPatch.x) + Bilinear(disp1, xz * invPatch.y);

        // The envelope is now applied PER CASCADE, so it has to go inside the
        // sum rather than multiplying the total: cascade 1's chop survives
        // shallow water that flattens cascade 0's swell. See
        // RegionFieldParams.BottomCoupling.
        float4 SampleDisp(float2 xz, float2 e) =>
            e.x * Bilinear(disp0, xz * invPatch.x) + e.y * Bilinear(disp1, xz * invPatch.y);

        float4 SampleDeriv(float2 xz, float2 e) =>
            e.x * Bilinear(deriv0, xz * invPatch.x) + e.y * Bilinear(deriv1, xz * invPatch.y);

        float4 SamplePrevDisp(float2 xz, float2 e) =>
            e.x * Bilinear(prevDisp0, xz * invPatch.x) + e.y * Bilinear(prevDisp1, xz * invPatch.y);

        /// Diagnostic only. The raw cascade 0+1 displacement at a SOURCE
        /// point, with no envelope and no Newton inversion -- the same
        /// SampleDisp the real path uses, exposed so DivergenceProbe can split
        /// a parity failure into its envelope, readback and inversion parts
        /// instead of reporting one number that could be any of the three.
        public float4 SourceDisp(float2 p) => SampleDispRaw(p);

        /// Diagnostic only. The regional envelope at a source point.
        public float SourceEnv(float2 p) => region.Evaluate(p, islands, shore);

        public OceanSample Sample(float3 worldPos)
        {
            var result = new OceanSample { normal = math.up() };
            if (!valid) return result;

            float2 q = worldPos.xz;

            // Newton inversion of q = p + env(p) * D_xz(p). The Jacobian uses
            // the lambda-scaled derivative fields the foam math needs anyway;
            // dDz/dx == dDx/dz (both come from kx*kz/|k| h), so J is symmetric
            // with the cross term stored in Displacement.w.
            float2 p = q;
            float3 envC = new float3(1f, 1f, 1f);
            float4 d = float4.zero;
            for (int i = 0; i < NewtonIterations; i++)
            {
                envC = region.EvaluateCascades(p, islands, shore);
                // d and dv arrive already enveloped, per cascade, so the
                // Jacobian below carries no separate env factor.
                d = SampleDisp(p, envC.xy);
                float4 dv = SampleDeriv(p, envC.xy);
                float2 r = p + d.xz - q;
                float j00 = 1f + dv.z;
                float j11 = 1f + dv.w;
                float j01 = d.w;
                float det = j00 * j11 - j01 * j01;
                // det <= 0 is a folding crest: the surface self-intersects and
                // has no unique inverse there. Clamp and let the residual ride.
                float safeDet = math.abs(det) < 1e-3f ? (det < 0f ? -1e-3f : 1e-3f) : det;
                float2 step = new float2(
                    j11 * r.x - j01 * r.y,
                    -j01 * r.x + j00 * r.y) / safeDet;
                p -= step;
            }

            envC = region.EvaluateCascades(p, islands, shore);
            d = SampleDisp(p, envC.xy);
            result.height = d.y;
            result.displacement = new float3(d.x, d.y, d.z);

            float4 derivs = SampleDeriv(p, envC.xy);
            float2 slope = derivs.xy / math.max(new float2(1f, 1f) + derivs.zw, 0.1f);
            result.normal = math.normalize(new float3(-slope.x, 1f, -slope.y));

            // Foam from the cascade-0 turbulence readback (nearest texel is
            // plenty — foam feeds VFX intensity, not geometry).
            if (turb0.Length > 1)
            {
                float2 fuv = p * invPatch.x;
                float2 fp = fuv * n - 0.5f;
                int2 fi = (int2)math.floor(fp + 0.5f);
                fi = ((fi % n) + n) % n;
                result.foam = turb0[fi.y * n + fi.x];
            }

            if (velDt > 1e-5f)
            {
                float4 dPrev = SamplePrevDisp(p, envC.xy);
                float3 vel = new float3(d.x - dPrev.x, d.y - dPrev.y, d.z - dPrev.z)
                             / velDt;
                // A spectrum rebuild between the two readback slots makes the
                // finite difference read a surface JUMP as motion — hundreds
                // of m/s for one frame, which quadratic drag turns into a
                // catapult. Real orbital speeds are pi*Hs/Tp, single digits;
                // clamp to that scale.
                float speed = math.length(vel);
                if (speed > 8f) vel *= 8f / speed;
                result.velocity = vel;
            }
            return result;
        }
    }

    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    public struct OceanSampleJob : IJobParallelFor
    {
        public OceanFieldData field;
        [ReadOnly] public NativeArray<float3> queries;
        [WriteOnly] public NativeArray<OceanSample> results;

        public void Execute(int i) => results[i] = field.Sample(queries[i]);
    }

    public static class OceanSampleJobExt
    {
        public static JobHandle Schedule(this OceanSampleJob job, int count, JobHandle dep) =>
            IJobParallelForExtensions.Schedule(job, count, 16, dep);
    }
}
