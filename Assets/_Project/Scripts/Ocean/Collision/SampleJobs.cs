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

        float4 SampleDisp(float2 xz) =>
            Bilinear(disp0, xz * invPatch.x) + Bilinear(disp1, xz * invPatch.y);

        float4 SampleDeriv(float2 xz) =>
            Bilinear(deriv0, xz * invPatch.x) + Bilinear(deriv1, xz * invPatch.y);

        float4 SamplePrevDisp(float2 xz) =>
            Bilinear(prevDisp0, xz * invPatch.x) + Bilinear(prevDisp1, xz * invPatch.y);

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
            float env = 1f;
            float4 d = float4.zero;
            for (int i = 0; i < 3; i++)
            {
                env = region.Evaluate(p, islands, shore);
                d = SampleDisp(p);
                float4 dv = SampleDeriv(p);
                float2 r = p + env * d.xz - q;
                float j00 = 1f + env * dv.z;
                float j11 = 1f + env * dv.w;
                float j01 = env * d.w;
                float det = j00 * j11 - j01 * j01;
                // det <= 0 is a folding crest: the surface self-intersects and
                // has no unique inverse there. Clamp and let the residual ride.
                float safeDet = math.abs(det) < 1e-3f ? (det < 0f ? -1e-3f : 1e-3f) : det;
                float2 step = new float2(
                    j11 * r.x - j01 * r.y,
                    -j01 * r.x + j00 * r.y) / safeDet;
                p -= step;
            }

            env = region.Evaluate(p, islands, shore);
            d = SampleDisp(p);
            result.height = env * d.y;
            result.displacement = new float3(env * d.x, env * d.y, env * d.z);

            float4 derivs = env * SampleDeriv(p);
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
                float4 dPrev = SamplePrevDisp(p);
                float3 vel = env * new float3(d.x - dPrev.x, d.y - dPrev.y, d.z - dPrev.z)
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
