using UnityEngine;

namespace SeaSick.Ocean
{
    /// Per-platform-tier knobs for the ocean. Two assets exist under
    /// _Project/Settings/Ocean (Mobile and PC); the active one is picked by the
    /// current quality level so the editor can rehearse the phone's settings by
    /// switching tiers. All tuning lives here rather than on scene components —
    /// scene serialization bakes component defaults forever, assets don't.
    [CreateAssetMenu(menuName = "SeaSick/Ocean Quality", fileName = "OceanQuality")]
    public class OceanQuality : ScriptableObject
    {
        [Header("Simulation")]
        [Tooltip("FFT resolution per cascade. 256 on PC, 128 on mobile.")]
        public int fftSize = 256;
        [Tooltip("Patch size in metres of each cascade, largest first.")]
        public float[] patchSizes = { 512f, 128f, 32f };

        [Header("Geometry")]
        public int clipmapRings = 7;
        [Tooltip("Cell size of the innermost clipmap grid, metres.")]
        public float innerCellSize = 0.5f;
        [Tooltip("World distance at which displacement fades to zero so the horizon stays flat.")]
        public float displacementFadeDistance = 500f;

        [Header("Foam & interaction")]
        public bool intersectionFoam = true;
        public int rippleSimResolution = 512;
        public float rippleSimExtent = 100f;

        [Header("Physics readback")]
        [Tooltip("How many cascades physics reads back (coarsest first). Hull probes never care about cascade 2's ripples.")]
        public int readbackCascades = 2;

        static OceanQuality active;

        /// The asset matching the current quality tier. Index 0 is the Mobile
        /// tier in this project's QualitySettings, everything else gets PC.
        public static OceanQuality Active
        {
            get
            {
                if (active == null)
                {
                    string name = QualitySettings.GetQualityLevel() == 0
                        ? "OceanQuality_Mobile" : "OceanQuality_PC";
                    active = Resources.Load<OceanQuality>("Ocean/" + name);
                }
                return active;
            }
        }

        /// Probes and setup scripts may force a tier.
        public static void Override(OceanQuality q) => active = q;
    }
}
