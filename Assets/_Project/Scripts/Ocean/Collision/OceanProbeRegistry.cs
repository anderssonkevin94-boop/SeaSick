using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ocean
{
    /// Persistent surface queries for everything that rides the sea without a
    /// rigidbody — enemy hulls, the monster, drifting salvage, spray scans.
    /// Owners update a handle's position; OceanPhysicsDriver folds every
    /// handle into the one SampleBatch per physics step and the result is
    /// read back through the handle. This is how continuous consumers obey
    /// the no-per-object-Update-sampling rule.
    public static class OceanProbeRegistry
    {
        public class Handle
        {
            public Vector3 position;
            public OceanSample sample;
            public bool active = true;
            /// Time.frameCount of the physics step that last wrote `sample`,
            /// 0 until the first. A consumer that moves its handle every frame
            /// needs this to know whether `sample` still belongs to the
            /// position it is about to overwrite — Update can run several
            /// times between two physics steps.
            public int sampledFrame;
        }

        static readonly List<Handle> handles = new List<Handle>();

        public static IReadOnlyList<Handle> Handles => handles;

        public static Handle Register(Vector3 initialPosition)
        {
            var h = new Handle { position = initialPosition };
            handles.Add(h);
            return h;
        }

        public static void Unregister(Handle h)
        {
            if (h != null) handles.Remove(h);
        }

        public static void Clear() => handles.Clear();
    }
}
