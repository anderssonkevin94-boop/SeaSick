using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeaSick.Ocean
{
    /// AsyncGPUReadback ring over the cascade textures physics needs (cascades
    /// 0-1, Displacement + Derivatives). Never blocks the GPU; each completed
    /// slot is stamped with the OceanTime its dispatch used, so consumers know
    /// exactly how stale the surface they ride is. A slot is only re-requested
    /// when it is neither in flight nor one of the two newest completed ones —
    /// the two buffers physics reads are never locked by a pending request.
    public class DisplacementReadback
    {
        /// Ring depth, and it is not a free number: two slots are always
        /// pinned as Latest and Previous for physics to read, so the ring can
        /// only ever have (Slots - 2) requests in flight.
        ///
        /// At the original 3 that was ONE. A readback takes 2-3 frames, and a
        /// new request only launches on the tick after the last one lands, so
        /// the surface physics floats on refreshed every 3-4 FRAMES while the
        /// surface being drawn refreshed every one. Buoyancy runs at 50 Hz, so
        /// three or four FixedUpdates in a row applied the identical water and
        /// then it stepped -- a staircase under the hull, with the step size
        /// set by however far the sea moved while the readback was in flight.
        /// Nothing smoothed it: the job loads the previous slot only to
        /// difference it into a velocity, and the sampled HEIGHT is the raw
        /// Latest value.
        ///
        /// At 5 there are three in flight, so a completion lands about every
        /// frame and Latest advances at frame rate. That does not make the
        /// surface less STALE -- the latency is the GPU's, not the ring's --
        /// but a uniformly delayed surface is invisible, whereas a stepping
        /// one is exactly the complaint.
        ///
        /// The other cost is BANDWIDTH, and it is the one to watch: a slot's
        /// worth of readback is issued roughly once a frame now instead of
        /// once every three or four, so the GPU->CPU traffic triples. That is
        /// ~134 MB/s at the PC tier and ~33 MB/s at mobile's n=128 — nothing
        /// on unified memory, but it is the first thing to suspect if the fps
        /// row of PerfHUD moves after this change. Slots = 4 is the halfway
        /// fallback: two in flight, Latest advancing every frame and a half.
        ///
        /// Costs n^2 x 34 bytes per slot (2 cascades x half4 displacement +
        /// half4 derivatives, plus a half of foam): 2.2 MB a slot at the PC
        /// tier's n=256, 0.56 MB at mobile's n=128. Two extra slots is +4.5 MB
        /// on PC, +1.1 MB on the phone.
        public const int Slots = 5;
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
