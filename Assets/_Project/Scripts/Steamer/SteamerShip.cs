using UnityEngine;

namespace SeaSick.Steamer
{
    /// Marks a PlayerShip that `SteamerBootstrap` converted, and owns what the
    /// conversion made at runtime.
    ///
    /// Two jobs, both small. It is the thing a probe or the audio looks for to
    /// know which ship it has been given, without inferring it from which
    /// components happen to be enabled. And it destroys the runtime material
    /// with the ship: domain reload is off, so a `new Material` nobody owns is
    /// not collected between plays -- it is leaked once per press of Play.
    public class SteamerShip : MonoBehaviour
    {
        public HullFormData Data { get; private set; }
        public HullFormBody Hull { get; private set; }
        public PaddleDrive Drive { get; private set; }
        public Material PaintMaterial { get; private set; }

        public void Bind(HullFormData data, HullFormBody hull, PaddleDrive drive,
                         Material paint)
        {
            Data = data;
            Hull = hull;
            Drive = drive;
            PaintMaterial = paint;
        }

        void OnDestroy()
        {
            if (PaintMaterial != null) Destroy(PaintMaterial);
            PaintMaterial = null;
        }
    }
}
