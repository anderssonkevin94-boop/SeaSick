using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace SeaSick.Ocean
{
    /// THE single source of truth for wave queries (D5). Buoyancy, camera,
    /// VFX, audio and gameplay all come through here; no system computes wave
    /// height any other way.
    ///
    /// SampleBatch is the real API — one Burst dispatch per FixedUpdate for
    /// every probe in the scene (OceanPhysicsDriver gathers them).
    /// SampleImmediate is a bounded main-thread convenience for one-shot,
    /// low-rate queries (camera clamp, splash tests): keep total calls under
    /// ~8 per frame; anything continuous belongs in the registry batch.
    public static class OceanSampler
    {
        static DisplacementReadback readback;
        static float2 invPatch;
        static NativeArray<float4> emptyIslands;
        static NativeArray<half4> emptyTex;
        static NativeArray<float> emptyShore;
        static NativeArray<half> emptyTurb;

        public static bool Ready =>
            readback != null && readback.Latest != null;

        /// OceanTime stamp of the surface physics currently reads — always a
        /// little behind the rendered one (2-3 frames of readback latency).
        public static double SurfaceTime =>
            readback?.Latest?.time ?? -1.0;

        public static void Bind(DisplacementReadback rb, float[] patchSizes)
        {
            readback = rb;
            invPatch = new float2(1f / patchSizes[0], 1f / patchSizes[1]);
            if (!emptyIslands.IsCreated)
                emptyIslands = new NativeArray<float4>(RegionField.MaxIslands, Allocator.Persistent);
            if (!emptyShore.IsCreated)
                emptyShore = new NativeArray<float>(1, Allocator.Persistent);
            if (!emptyTex.IsCreated)
                emptyTex = new NativeArray<half4>(1, Allocator.Persistent);
            if (!emptyTurb.IsCreated)
                emptyTurb = new NativeArray<half>(1, Allocator.Persistent);
        }

        public static void Unbind()
        {
            readback = null;
            if (emptyIslands.IsCreated) emptyIslands.Dispose();
            if (emptyShore.IsCreated) emptyShore.Dispose();
            if (emptyTex.IsCreated) emptyTex.Dispose();
            if (emptyTurb.IsCreated) emptyTurb.Dispose();
        }

        /// The raw surface: what is drawn. Everything that MEASURES the sea
        /// wants this, which is why it is the default.
        public static readonly float2 NoHullFilter = new float2(1f, 1f);

        public static OceanFieldData CurrentField() => CurrentField(NoHullFilter);

        public static OceanFieldData CurrentField(float2 hullFilter)
        {
            var region = RegionField.Instance;
            var latest = readback?.Latest;
            var prev = readback?.Previous;
            bool valid = latest != null;
            var l = valid ? latest : null;
            return new OceanFieldData
            {
                valid = valid,
                n = readback?.N ?? 1,
                invPatch = invPatch,
                disp0 = valid ? l.disp[0] : emptyTex,
                disp1 = valid ? l.disp[1] : emptyTex,
                deriv0 = valid ? l.deriv[0] : emptyTex,
                deriv1 = valid ? l.deriv[1] : emptyTex,
                prevDisp0 = prev != null ? prev.disp[0] : (valid ? l.disp[0] : emptyTex),
                prevDisp1 = prev != null ? prev.disp[1] : (valid ? l.disp[1] : emptyTex),
                turb0 = valid ? l.turb : emptyTurb,
                velDt = (latest != null && prev != null) ? (float)(latest.time - prev.time) : 0f,
                hullFilter = hullFilter,
                region = region != null ? region.Params : RegionFieldParams.Neutral,
                islands = region != null ? region.Islands : emptyIslands,
                shore = region != null && region.Shore.IsCreated ? region.Shore : emptyShore,
                weather = region != null && region.Weather.IsCreated ? region.Weather : emptyShore,
            };
        }

        /// How many immediate (main-thread, un-Bursted) queries have been made
        /// since start. Diagnostic: the cost probes difference it per frame,
        /// because every caller of this is "one-shot, low-rate" in its own
        /// mind and nobody has ever counted the total.
        public static long ImmediateCalls { get; private set; }

        /// Convenience single query — do not call in a loop.
        public static OceanSample SampleImmediate(Vector3 worldPos)
        {
            ImmediateCalls++;
            return CurrentField().Sample(new float3(worldPos.x, worldPos.y, worldPos.z));
        }

        /// The same one-shot query through a hull filter: the surface as a
        /// SHIP feels it rather than as it is drawn. See
        /// `OceanPhysicsDriver.HullFilterNow`.
        public static OceanSample SampleImmediate(Vector3 worldPos, float2 hullFilter)
        {
            ImmediateCalls++;
            return CurrentField(hullFilter).Sample(new float3(worldPos.x, worldPos.y, worldPos.z));
        }

        /// The real API: batch every query of the physics step into one job.
        public static JobHandle SampleBatch(
            NativeArray<float3> queries,
            NativeArray<OceanSample> results,
            JobHandle dependency) => SampleBatch(queries, results, dependency, NoHullFilter);

        /// One filter for the whole batch — what every measuring probe wants
        /// (WaveShot, WaveSizeProbe, SeaProfileProbe, DivergenceProbe).
        public static JobHandle SampleBatch(
            NativeArray<float3> queries,
            NativeArray<OceanSample> results,
            JobHandle dependency,
            float2 filter) =>
            Dispatch(queries, results, dependency, 0, filter, filter);

        /// Mixed batch: the first hullCount queries are hull probes and get
        /// hullFilter; everything after them keeps the raw surface. The physics
        /// driver lays its array out that way on purpose, so both populations
        /// ride one schedule and one fence instead of two.
        public static JobHandle SampleBatch(
            NativeArray<float3> queries,
            NativeArray<OceanSample> results,
            JobHandle dependency,
            int hullCount,
            float2 hullFilter) =>
            Dispatch(queries, results, dependency, hullCount, hullFilter, NoHullFilter);

        static JobHandle Dispatch(
            NativeArray<float3> queries,
            NativeArray<OceanSample> results,
            JobHandle dependency,
            int hullCount,
            float2 hullFilter,
            float2 restFilter)
        {
            var job = new OceanSampleJob
            {
                // Execute overwrites field.hullFilter per query; seeding it
                // with restFilter keeps the struct in a sane state rather than
                // a misleading one for anyone reading it in a debugger.
                field = CurrentField(restFilter),
                queries = queries,
                results = results,
                hullCount = hullCount,
                hullFilter = hullFilter,
                restFilter = restFilter,
            };
            return job.Schedule(queries.Length, dependency);
        }
    }
}
