using UnityEngine;

namespace SeaSick.Steamer
{
    /// Writes the hull's sample positions into its registry handles, and
    /// nothing else. It is a separate component for one reason: ORDER.
    ///
    ///   -100  this            handle.position = where each half-strip is NOW
    ///    -90  OceanPhysicsDriver  one batch, stamps handle.sample
    ///    -80  HullFormBody     reads samples taken at this step's pose
    ///
    /// A component has one execution order, and `HullFormBody` needs to sit
    /// AFTER the driver to read same-step samples. If it also wrote the
    /// positions, they would be a step stale by the time the driver used
    /// them -- 0.3 m at full speed, which is a standing bow-up couple of
    /// m g x 0.3 m that no probe would ever find the source of.
    ///
    /// Added by `HullFormBody.Configure`; do not place it by hand.
    [DefaultExecutionOrder(-100)]
    public class HullFormProbeFeeder : MonoBehaviour
    {
        // Not serialized and re-fetched lazily: a domain reload mid-play
        // nulls it while the component survives.
        HullFormBody body;

        void FixedUpdate()
        {
            if (body == null && !TryGetComponent(out body)) return;
            if (body.isActiveAndEnabled) body.WriteProbePositions();
        }
    }
}
