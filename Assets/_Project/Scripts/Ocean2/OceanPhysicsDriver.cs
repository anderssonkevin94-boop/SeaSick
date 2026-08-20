using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SeaSick.Ocean2
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

            OceanSampler.SampleBatch(queries, results, default).Complete();

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
