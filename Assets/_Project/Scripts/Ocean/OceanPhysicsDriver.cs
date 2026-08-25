using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SeaSick.Ocean
{
    /// The one place ocean queries happen during physics (D5): every
    /// BuoyantBody probe and every registry handle is gathered into a single
    /// SampleBatch per FixedUpdate, completed, and distributed back. Nothing
    /// else samples in a loop.
    [DefaultExecutionOrder(-90)]
    public class OceanPhysicsDriver : MonoBehaviour
    {
        static readonly List<BuoyantBody> bodies = new List<BuoyantBody>();

        NativeArray<float3> queries;
        NativeArray<OceanSample> results;
        Vector3[] scratch = new Vector3[64];

        public static void Register(BuoyantBody b)
        {
            if (!bodies.Contains(b)) bodies.Add(b);
        }

        public static void Unregister(BuoyantBody b) => bodies.Remove(b);

        void OnDisable()
        {
            if (queries.IsCreated) queries.Dispose();
            if (results.IsCreated) results.Dispose();
        }

        [Header("Hull-length filtering")]
        [Tooltip("How much of cascade 1 (16-64 m waves) the hull feels. A ship of length L averages a wave of length lambda over her waterline, which multiplies the heave force by roughly sin(pi L/lambda)/(pi L/lambda) -- about 0.29 for a 24 m hull against this band. 1 = the raw surface (what is drawn), 0 = the hull ignores that band entirely.")]
        [SerializeField, Range(0f, 1f)] float hullFeelsCascade1 = 0.3f;
        [Tooltip("Same for the long swell. A 280 m roller is nearly flat under 24 m of hull, so this should stay at 1 -- the swell is what she is supposed to ride.")]
        [SerializeField, Range(0f, 1f)] float hullFeelsCascade0 = 1f;

        float2 HullFilter() => new float2(hullFeelsCascade0, hullFeelsCascade1);

        void FixedUpdate()
        {
            if (!OceanSampler.Ready) return;

            var registry = OceanProbeRegistry.Handles;
            int count = registry.Count;
            foreach (var b in bodies) count += b.Probes.Count;
            if (count == 0) return;

            if (!queries.IsCreated || queries.Length != count)
            {
                if (queries.IsCreated) { queries.Dispose(); results.Dispose(); }
                queries = new NativeArray<float3>(count, Allocator.Persistent);
                results = new NativeArray<OceanSample>(count, Allocator.Persistent);
            }

            int cursor = 0;
            foreach (var b in bodies)
            {
                int n = b.Probes.Count;
                if (scratch.Length < n) scratch = new Vector3[Mathf.NextPowerOfTwo(n)];
                b.FillQueries(new System.Span<Vector3>(scratch, 0, n));
                for (int i = 0; i < n; i++)
                    queries[cursor + i] = scratch[i];
                cursor += n;
            }
            for (int i = 0; i < registry.Count; i++)
                queries[cursor + i] = registry[i].position;

            // TWO jobs over one array, split at the boundary between hull
            // probes and everything else.
            //
            // The hull gets the wave field FILTERED to what a 24 m ship can
            // actually feel: a hull spans most of a 30 m wave, so the crest
            // lifting the bow is largely cancelled by the trough under the
            // stern, and feeding probe points the raw surface hands her
            // short-wave forces a real hull averages away. At storm steepness
            // that is what drives her under. Flotsam and crates are small
            // enough to ride the chop and keep the raw field -- filtering a
            // floating crate would be wrong in the other direction.
            int hullCount = cursor;
            if (hullCount > 0)
                OceanSampler.SampleBatch(
                    queries.GetSubArray(0, hullCount),
                    results.GetSubArray(0, hullCount),
                    default, HullFilter()).Complete();
            int restCount = count - hullCount;
            if (restCount > 0)
                OceanSampler.SampleBatch(
                    queries.GetSubArray(hullCount, restCount),
                    results.GetSubArray(hullCount, restCount),
                    default).Complete();

            cursor = 0;
            var span = results.AsReadOnlySpan();
            foreach (var b in bodies)
            {
                int n = b.Probes.Count;
                b.ApplyForces(span.Slice(cursor, n));
                cursor += n;
            }
            for (int i = 0; i < registry.Count; i++)
                registry[i].sample = results[cursor + i];
        }
    }
}
