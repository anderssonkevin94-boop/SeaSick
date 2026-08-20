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
            public bool inFlight;
            public bool ready;
        }

        readonly Slot[] slots = new Slot[Slots];
        readonly int n;

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
            // Retire finished flights.
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
                if (anyError) s.time = -1.0;
            }

            // Track the two newest completed slots.
            Latest = Previous = null;
            foreach (var s in slots)
            {
                if (!s.ready) continue;
                if (Latest == null || s.time > Latest.time) { Previous = Latest; Latest = s; }
                else if (Previous == null || s.time > Previous.time) Previous = s;
            }

            // Launch into a free slot that physics isn't reading.
            foreach (var s in slots)
            {
                if (s.inFlight || s == Latest || s == Previous) continue;
                s.ready = false;
                s.time = simTime;
                for (int c = 0; c < PhysicsCascades; c++)
                {
                    s.requests[c * 2] = AsyncGPUReadback.RequestIntoNativeArray(
                        ref s.disp[c], displacement, 0, 0, n, 0, n, c, 1,
                        TextureFormat.RGBAHalf);
                    s.requests[c * 2 + 1] = AsyncGPUReadback.RequestIntoNativeArray(
                        ref s.deriv[c], derivatives, 0, 0, n, 0, n, c, 1,
                        TextureFormat.RGBAHalf);
                }
                s.requests[PhysicsCascades * 2] = AsyncGPUReadback.RequestIntoNativeArray(
                    ref s.turb, turbulence, 0, 0, n, 0, n, 0, 1, TextureFormat.RHalf);
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
