using UnityEngine;

namespace SeaSick.Steamer
{
    /// Writes the paddle wheels' four sample points into the probe registry
    /// BEFORE the ocean driver runs.
    ///
    /// The whole reason this is its own component is the execution order:
    /// `OceanPhysicsDriver` (-90) folds every registry handle into the step's
    /// one batch and stamps the answers back, and `PaddleDrive` (-70) reads
    /// them. A position written at -70 would be sampled a step late, against a
    /// hull that has since moved 30 cm at full speed -- so the write has to
    /// happen ahead of the driver, and a component can only have one order.
    /// Same split as `HullFormProbeFeeder` / `HullFormBody`.
    [DefaultExecutionOrder(-100)]
    public class PaddleProbeFeeder : MonoBehaviour
    {
        PaddleDrive drive;

        public void Bind(PaddleDrive owner) => drive = owner;

        void FixedUpdate()
        {
            if (drive != null && drive.isActiveAndEnabled) drive.WriteProbePositions();
        }
    }
}
