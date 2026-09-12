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
        /// Horizontal residual |p + D(p) - q| the inversion finished with, in
        /// metres. ~0 means it converged; a converged sample that still
        /// disagrees with the renderer sits on a FOLD, where several source
        /// points share one spot and the question has more than one answer.
        /// Diagnostic -- DivergenceProbe splits its outliers on this.
        public float residual;
    }

    /// The whole CPU-side sampling math in one blittable struct so the Burst
    /// job and the main-thread SampleImmediate path are literally the same
    /// code — a second implementation is how physics oceans go wrong.
    ///
    /// Mirrors the GPU exactly: bilinear-with-repeat over the readback texels,
    /// cascades 0-1 (cascade 2 is visual chop shorter than any hull probe),
    /// regional envelope multiplied in, and a STEP-BOUNDED Newton inversion of
    /// the horizontal choppiness displacement (the sea is not a heightfield).
    /// See NewtonIterations for the count, the bound, and why the envelope is
    /// no longer re-evaluated on every iteration.
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
        /// Six, then SEVEN, when choppiness went 0.195 -> 0.70. Choppiness IS
        /// the horizontal displacement this loop inverts, so it is the single
        /// most direct control on how hard the inversion is: the gate went to
        /// 74 cm with 24 of 5000 over, again as a handful of outliers on the
        /// sharpest crests rather than a shifted distribution, and again with
        /// the envelope term unmoved at 0.000228.
        ///
        /// If the sea is ever made steeper again, expect to pay another step --
        /// and note the budget is nearly spent: 0.4 ms per 1000 queries is the
        /// ceiling and seven steps sit at about 0.35 (0.305 as measured).
        ///
        /// TEN NOW, WITH AN EARLY EXIT, AND THE LOOP IS BACKTRACKING NEWTON.
        /// The gate could not run from 08-28 to 09-11 (the probe sat in an
        /// Editor folder and AddComponent returned null), and when it ran
        /// again on the 09-10 sea it read `total: max 1130.52 cm, mean 0.575,
        /// p50 0.096, p99 1.06, p99.9 12.80; 5 of 5000 over the 6.5 cm bar`
        /// with env 0.000267 and disp 2.45 cm -- parity and readback clean, so
        /// all 11 m of it was this loop. At a folding crest det passes through
        /// zero, the old 1e-3 clamp divided the residual by it, and the "step"
        /// was hundreds of metres; under a hull that is a teleport.
        ///
        /// What it does now, and the numbers each part was measured to earn:
        ///  - Newton is TRUSTED wherever |det| >= FoldDet, capped only by
        ///    MaxStepMetres. Making every step earn its residual (halve on
        ///    growth) took one instant from 1.5 cm to 107 cm: Newton is not
        ///    monotone on a curved map, and a step that grows |r| for one
        ///    iteration often lands the next.
        ///  - On a fold (|det| < FoldDet) the step must shrink |r|: Newton,
        ///    /2, /4, then the fixed point r, /2, /4. FoldDet is 1e-3 and not
        ///    0.05: at 0.05 near-fold points that plain Newton converged were
        ///    diverted to the fixed point and stalled a metre out.
        ///  - The best point seen is what is returned, and a sample that
        ///    still sits more than 0.5 m out gets a second start: a damped
        ///    fixed point from q. One point in 5000 sat at a 14 m residual
        ///    from p = q with no Newton step improving it; the second start
        ///    took it to 20 cm.
        ///  - Converged samples leave early, which is what pays for ten.
        /// Result at storm +25 % choppiness: max 11 cm on the non-converged
        /// points (three of 5000, all at fold TIPS with 3-20 cm residuals --
        /// where the readback's 2 cm disagreement with the texture means the
        /// query has no exact inverse in the CPU's field), one genuine
        /// multi-valued fold reported separately, mean 0.250 cm, and
        /// SampleBatch 0.290 ms against 0.4 (was 0.305 at seven steps).
        /// `OceanSample.residual` is how DivergenceProbe tells the two apart.
        ///
        /// THE ENVELOPE IS NO LONGER EVALUATED EIGHT TIMES PER QUERY. It was,
        /// once per iteration plus once after -- up to 24 island distances and
        /// two 256^2 bilinear lookups each, and the dominant cost here. But the
        /// envelope varies over TENS of metres while Newton's later steps move
        /// p by centimetres, so inside the loop it is refreshed only when p has
        /// drifted more than 0.1 m since the last evaluation (accumulated, not
        /// just the last step, so a run of small steps cannot quietly carry a
        /// stale envelope away from p), and the FINAL evaluation at the
        /// converged p is unconditional, because the height returned is
        /// d(p, envC) and a stale envelope there is a height error outright.
        /// 0.1 m and not more: a percent of envelope on a 62 m sea is tens of
        /// centimetres against a 6.5 cm gate, and the envelope has 60 m island
        /// falloffs and ~9 m shore cells. In practice that is ~4 evaluations
        /// per query instead of 8: the first step at storm amplitude moves p
        /// by tens of metres, the next a couple, then centimetres.
        ///
        /// DO NOT "optimise" this further by hoisting the envelope out of the
        /// loop and evaluating it once at q. That changes the fixed point being
        /// inverted -- the probe deliberately varies the envelope across its
        /// disc -- and it is wrong exactly where p travels furthest.
        const int NewtonIterations = 10;
        /// Horizontal residual under which the inversion is done. A millimetre
        /// sideways is at most a millimetre of height on any slope the sea
        /// can hold, against a 6.5 cm gate.
        const float ConvergedMetres = 1e-3f;
        /// |det| below this is a fold or the edge of one: the Newton direction
        /// is noise there and the fixed-point step is used instead.
        const float FoldDet = 1e-3f;
        const float MaxStepMetres = 60f;

        [ReadOnly] public NativeArray<half4> disp0, disp1, deriv0, deriv1;
        [ReadOnly] public NativeArray<half4> prevDisp0, prevDisp1;
        [ReadOnly] public NativeArray<half> turb0;
        [ReadOnly] public NativeArray<float4> islands;
        [ReadOnly] public NativeArray<float> shore;
        [ReadOnly] public NativeArray<float> weather;
        public RegionFieldParams region;

        /// Per-cascade weight for what the HULL feels, cascades 0 and 1.
        /// (1,1) is the raw surface -- what is drawn, and what every probe
        /// measuring "the sea" wants.
        ///
        /// A 24 m hull does not respond to a 30 m wave the way a point does:
        /// it spans most of one, and the crest lifting the bow is cancelled by
        /// the trough under the stern. The classic result is that the heave
        /// force carries a sinc factor, sin(pi L / lambda) / (pi L / lambda) --
        /// about 0.29 for this hull against cascade 1's 16-64 m band, and 0.99
        /// against cascade 0's swell. Sampling the raw surface at probe points
        /// hands the hull short-wave forces a real ship would average away, and
        /// at storm steepness that is what drives her under.
        ///
        /// Applied ONLY on the physics batch (OceanPhysicsDriver passes it).
        /// SampleImmediate and every measuring probe keep (1,1), so the gate
        /// still compares the sampler against what is actually rendered.
        public float2 hullFilter;
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
        // The envelope and the hull filter both multiply per cascade, so they
        // fold into one weight per band.
        float2 W(float2 e) => e * hullFilter;

        float4 SampleDisp(float2 xz, float2 e)
        {
            float2 w = W(e);
            return w.x * Bilinear(disp0, xz * invPatch.x) + w.y * Bilinear(disp1, xz * invPatch.y);
        }

        float4 SampleDeriv(float2 xz, float2 e)
        {
            float2 w = W(e);
            return w.x * Bilinear(deriv0, xz * invPatch.x) + w.y * Bilinear(deriv1, xz * invPatch.y);
        }

        float4 SamplePrevDisp(float2 xz, float2 e)
        {
            float2 w = W(e);
            return w.x * Bilinear(prevDisp0, xz * invPatch.x) + w.y * Bilinear(prevDisp1, xz * invPatch.y);
        }

        /// Diagnostic only. The raw cascade 0+1 displacement at a SOURCE
        /// point, with no envelope and no Newton inversion -- the same
        /// SampleDisp the real path uses, exposed so DivergenceProbe can split
        /// a parity failure into its envelope, readback and inversion parts
        /// instead of reporting one number that could be any of the three.
        public float4 SourceDisp(float2 p) => SampleDispRaw(p);

        /// Diagnostic only. The regional envelope at a source point.
        public float SourceEnv(float2 p) => region.Evaluate(p, islands, shore, weather);

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
            float3 envC = region.EvaluateCascades(p, islands, shore, weather);
            // Metres p has moved since envC was last evaluated -- see the
            // header: refreshed past 0.1 m, exact again after the loop.
            float envDrift = 0f;
            float4 d = SampleDisp(p, envC.xy);
            float2 r = p + d.xz - q;
            float rLen = math.length(r);
            // The best point seen, returned if the loop ends anywhere worse.
            // A trusted Newton step off a fold edge can land 48 m out (measured)
            // and the give-up path must never hand THAT to the hull.
            float2 bestP = p; float3 bestEnv = envC; float4 bestD = d; float bestR = rLen;

            // Backtracking Newton. A Newton step only earns its place by
            // shrinking the residual; a trial that grows it is halved, twice,
            // and if the Newton DIRECTION is the problem (a folding crest,
            // where det ~ 0 and the direction is noise) the fallback is the
            // plain fixed-point step -r, which is bounded by |r| and points
            // the right way when J ~ I. If nothing improves, stop: the point
            // is on a fold, has no unique inverse, and the best p so far is
            // the answer. Converged points leave EARLY -- calm water is done
            // in two or three iterations, which is what pays for allowing
            // more on the hard ones.
            for (int i = 0; i < NewtonIterations && rLen > ConvergedMetres; i++)
            {
                float4 dv = SampleDeriv(p, envC.xy);
                float j00 = 1f + dv.z;
                float j11 = 1f + dv.w;
                float j01 = d.w;
                float det = j00 * j11 - j01 * j01;
                // Newton is not monotone in the residual on a curved map: a
                // healthy step can grow |r| for one iteration and land on the
                // answer the next, and rejecting it stalls a point that plain
                // Newton converged (measured: 1.5 cm -> 107 cm at one instant
                // when every step was made to earn its residual). So where
                // det is healthy the Newton step is TRUSTED, capped only by
                // MaxStepMetres. Where |det| < FoldDet -- a fold or its edge,
                // where the Newton direction is noise and the old code took
                // an 11 m step off a 1e-3 clamp -- the step is the plain
                // fixed point -r, bounded by |r|, and it has to shrink the
                // residual to be accepted (halved up to twice). If nothing
                // shrinks it, the point has no unique inverse and the best p
                // so far is the answer.
                bool fold = math.abs(det) < FoldDet;
                // 1e-3 and not higher: at 0.05 the near-fold points that plain
                // Newton DID converge (1.5 cm at one instant) were diverted to
                // the fixed point and stalled at a metre. Near a fold Newton
                // still works more often than not; it just must not be
                // allowed the 11 m step a clamped determinant hands it.
                float safeDet = fold ? (det < 0f ? -FoldDet : FoldDet) : det;
                float2 step = new float2(j11 * r.x - j01 * r.y, -j01 * r.x + j00 * r.y) / safeDet;
                float stepLen = math.length(step);
                if (stepLen > MaxStepMetres) { step *= MaxStepMetres / stepLen; stepLen = MaxStepMetres; }

                bool moved = false;
                // On a fold: Newton, /2, /4, then the fixed point r, /2, /4.
                // At a fold edge |dD| ~ 1 and the plain fixed point is not
                // contractive either; the damped ones usually are.
                int tries = fold ? 6 : 1;
                for (int k = 0; k < tries; k++)
                {
                    if (k == 3) { step = r; stepLen = rLen; }
                    float2 pT = p - step;
                    float3 envT = envC;
                    float driftT = envDrift + stepLen;
                    if (driftT > 0.1f)
                    {
                        envT = region.EvaluateCascades(pT, islands, shore, weather);
                        driftT = 0f;
                    }
                    float4 dT = SampleDisp(pT, envT.xy);
                    float2 rT = pT + dT.xz - q;
                    float rTLen = math.length(rT);
                    if (!fold || rTLen < rLen)
                    {
                        p = pT; envC = envT; envDrift = driftT; d = dT; r = rT; rLen = rTLen;
                        if (rLen < bestR) { bestP = p; bestEnv = envC; bestD = d; bestR = rLen; }
                        moved = true;
                        break;
                    }
                    step *= 0.5f; stepLen *= 0.5f;
                }
                if (!moved) break;
            }
            // A sample that never got anywhere (measured: one point in 5000
            // at storm +25 % choppiness sat at a 14 m residual from p = q and
            // no Newton step improved on it) gets a second, dumber start: a
            // DAMPED fixed point from q. It cannot diverge the way Newton can
            // and on a fold edge it usually walks down to a sheet. Rare, so
            // its cost is invisible in the batch.
            if (bestR > 0.5f)
            {
                float2 p2 = q; float3 env2 = envC; float drift2 = float.MaxValue;
                for (int i = 0; i < 8; i++)
                {
                    if (drift2 > 0.1f)
                    {
                        env2 = region.EvaluateCascades(p2, islands, shore, weather);
                        drift2 = 0f;
                    }
                    float4 d2 = SampleDisp(p2, env2.xy);
                    float2 r2 = p2 + d2.xz - q;
                    float r2Len = math.length(r2);
                    if (r2Len < bestR) { bestP = p2; bestEnv = env2; bestD = d2; bestR = r2Len; }
                    if (r2Len <= ConvergedMetres) break;
                    float2 st = r2 * 0.5f;
                    p2 -= st;
                    drift2 += r2Len * 0.5f;
                }
            }
            if (rLen > bestR) { p = bestP; envC = bestEnv; d = bestD; rLen = bestR; }

            // The FINAL envelope is always exact at the converged p: the height
            // below is d(p, envC), so a stale envC here is a height error, not
            // a convergence detail. One evaluation, no gate risk.
            envC = region.EvaluateCascades(p, islands, shore, weather);
            d = SampleDisp(p, envC.xy);
            result.height = d.y;
            result.displacement = new float3(d.x, d.y, d.z);
            result.residual = rLen;

            float4 derivs = SampleDeriv(p, envC.xy);
            float2 slope = derivs.xy / math.max(new float2(1f, 1f) + derivs.zw, 0.1f);
            result.normal = math.normalize(new float3(-slope.x, 1f, -slope.y));

            // Foam from the turbulence readback (nearest texel is plenty --
            // foam feeds VFX intensity, not geometry). The buffer is tiled by
            // CASCADE 1 since 2026-09-12: FoamAccumulate runs on the 128 m
            // patch (0.5 m/texel) instead of the 2048 m one (8 m/texel, which
            // aliased cascade 2 into noise). Same patch index as the shader's
            // _Ocean_Turbulence read, or the two foams disagree.
            if (turb0.Length > 1)
            {
                float2 fuv = p * invPatch.y;
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

        /// Queries [0, hullCount) get hullFilter, the rest get restFilter.
        /// One job instead of two back-to-back batches: the hull probes and
        /// everything else want DIFFERENT per-cascade weights but are otherwise
        /// the same math over disjoint slices of the same arrays, and splitting
        /// them into two schedules cost a fence between them every physics step.
        /// The field struct holds only NativeArray handles and blittable data,
        /// so the per-element copy below is a few registers, not a buffer.
        public int hullCount;
        public float2 hullFilter;
        public float2 restFilter;

        public void Execute(int i)
        {
            var f = field;
            f.hullFilter = i < hullCount ? hullFilter : restFilter;
            results[i] = f.Sample(queries[i]);
        }
    }

    public static class OceanSampleJobExt
    {
        /// Batch 4, not 16. The hull is 12-14 probes; at 16 the whole ship
        /// landed in a single batch on one worker while the rest of the pool
        /// idled, and the batch is ~0.4 ms of Newton iteration each.
        public static JobHandle Schedule(this OceanSampleJob job, int count, JobHandle dep) =>
            IJobParallelForExtensions.Schedule(job, count, 4, dep);
    }
}
