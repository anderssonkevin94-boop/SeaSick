using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeaSick.Ocean
{
    /// Triple-buffered AsyncGPUReadback ring over the cascade textures physics
    /// needs (cascades 0-1, Displacement + Derivatives). Never blocks the GPU;
    /// each completed slot is stamped with the OceanTime its dispatch used, so
    /// consumers know exactly how stale the surface they ride is (2-3 frames).
    /// A slot is only re-requested when it is neither in flight nor one of the
    /// two newest completed ones — the two buffers physics reads are never
    /// locked by a pending request.
    public class DisplacementReadback
    {
        public const int Slots = 3;
        public const int PhysicsCascades = 2;

        public class Slot
        {
            public NativeArray<half4>[] disp = new NativeArray<half4>[PhysicsCascades];
            public NativeArray<half4>[] deriv = new NativeArray<half4>[PhysicsCascades];
            public NativeArray<half> turb;   // cascade 0 foam, for OceanSample.foam
            public AsyncGPUReadbackRequest[] requests = new AsyncGPUReadbackRequest[PhysicsCascades * 2 + 1];
            public double time = -1.0;
            /// Completion order, not sim time. See the Latest/Previous note.
            public long seq;
            public bool inFlight;
            public bool ready;
        }

        readonly Slot[] slots = new Slot[Slots];
        readonly int n;
        long nextSeq;

        public int N => n;
        public Slot Latest { get; private set; }
        public Slot Previous { get; private set; }

        public DisplacementReadback(int textureSize)
        {
            n = textureSize;
            for (int s = 0; s < Slots; s++)
            {
                slots[s] = new Slot();
                for (int c = 0; c < PhysicsCascades; c++)
                {
                    slots[s].disp[c] = new NativeArray<half4>(n * n, Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory);
                    slots[s].deriv[c] = new NativeArray<half4>(n * n, Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory);
                }
                slots[s].turb = new NativeArray<half>(n * n, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            }
        }

        /// Call once per frame right after the simulation dispatches, with the
        /// OceanTime those dispatches used.
        public void Tick(RenderTexture displacement, RenderTexture derivatives,
            RenderTexture turbulence, double simTime)
        {
            // Retire finished flights, copying the results into the slot's
            // persistent arrays. (RequestIntoNativeArray with region overloads
            // spams "NativeArray should not be undisposable" every frame —
            // megabytes of Editor.log per minute — so we Request + copy.)
            foreach (var s in slots)
            {
                if (!s.inFlight) continue;
                bool allDone = true, anyError = false;
                foreach (var r in s.requests)
                {
                    if (!r.done) { allDone = false; break; }
                    if (r.hasError) anyError = true;
                }
                if (!allDone) continue;
                s.inFlight = false;
                s.ready = !anyError;
                if (anyError) { s.time = -1.0; continue; }
                for (int c = 0; c < PhysicsCascades; c++)
                {
                    s.requests[c * 2].GetData<half4>().CopyTo(s.disp[c]);
                    s.requests[c * 2 + 1].GetData<half4>().CopyTo(s.deriv[c]);
                }
                s.requests[PhysicsCascades * 2].GetData<half>().CopyTo(s.turb);
                s.seq = ++nextSeq;
            }

            // Track the two most recently COMPLETED slots, by completion order
            // and never by s.time. Ranking by timestamp looks equivalent while
            // OceanTime only ever advances, and wedges the ring solid the
            // moment anything scrubs BACKWARDS: the fresh slot's time is lower
            // than the stale ones, so it never becomes Latest, so it is free,
            // so the next Tick recycles it -- forever. Physics then rides a
            // surface frozen at the highest time the ring ever saw. Probes
            // scrub backwards constantly (OceanTime.Scrub exists for it), and
            // this is what made DivergenceProbe return 26 cm, then no samples
            // at all, then 147 cm, from identical code.
            Latest = Previous = null;
            foreach (var s in slots)
            {
                if (!s.ready) continue;
                if (Latest == null || s.seq > Latest.seq) { Previous = Latest; Latest = s; }
                else if (Previous == null || s.seq > Previous.seq) Previous = s;
            }

            // Launch into a free slot that physics isn't reading.
            foreach (var s in slots)
            {
                if (s.inFlight || s == Latest || s == Previous) continue;
                s.ready = false;
                s.time = simTime;
                for (int c = 0; c < PhysicsCascades; c++)
                {
                    s.requests[c * 2] = AsyncGPUReadback.Request(
                        displacement, 0, 0, n, 0, n, c, 1, TextureFormat.RGBAHalf);
                    s.requests[c * 2 + 1] = AsyncGPUReadback.Request(
                        derivatives, 0, 0, n, 0, n, c, 1, TextureFormat.RGBAHalf);
                }
                s.requests[PhysicsCascades * 2] = AsyncGPUReadback.Request(
                    turbulence, 0, 0, n, 0, n, 0, 1, TextureFormat.RHalf);
                s.inFlight = true;
                break;
            }
        }

        public void Dispose()
        {
            // Disposing an array a readback still writes into is a crash;
            // drain the queue first, always.
            AsyncGPUReadback.WaitAllRequests();
            foreach (var s in slots)
            {
                for (int c = 0; c < PhysicsCascades; c++)
                {
                    if (s.disp[c].IsCreated) s.disp[c].Dispose();
                    if (s.deriv[c].IsCreated) s.deriv[c].Dispose();
                }
                if (s.turb.IsCreated) s.turb.Dispose();
            }
        }
    }
}
