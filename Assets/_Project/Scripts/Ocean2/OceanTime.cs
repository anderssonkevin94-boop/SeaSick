using UnityEngine;

namespace SeaSick.Ocean2
{
    /// The one clock every piece of ocean code reads. Nothing in the ocean may
    /// touch Time.time: the simulation, the readback stamps, the probes and any
    /// future replay/network sync all need a clock that can be paused and
    /// scrubbed without the rest of the engine noticing.
    ///
    /// OceanRenderer advances it once per frame. Probes pin wave phase by
    /// pausing or scrubbing it — that is a supported use, not a hack.
    public static class OceanTime
    {
        /// Seconds of ocean time. Double so precision holds over long sessions
        /// (a float loses centimetre-scale phase after a few hours at sea).
        public static double Now { get; private set; }

        public static bool Paused { get; set; }

        /// Multiplier on advancement; 1 for gameplay. Probes may slow or speed.
        public static double Scale { get; set; } = 1.0;

        /// Advanced exactly once per rendered frame by the owner (OceanRenderer).
        public static void Advance(float deltaTime)
        {
            if (!Paused) Now += deltaTime * Scale;
        }

        /// Jump the ocean to an absolute time. The surface is a pure function
        /// of (spectrum, seed, time), so scrubbing is exact, not approximate.
        public static void Scrub(double t) => Now = t;
    }
}
