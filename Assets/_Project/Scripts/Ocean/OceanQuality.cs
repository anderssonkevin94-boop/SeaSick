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

        /// The asset matching the current quality tier, chosen by the level's
        /// NAME.
        ///
        /// It used to be `GetQualityLevel() == 0 ? Mobile : PC`, and by
        /// 2026-09-09 that was **inverted**: the editor reported quality level
        /// 0, level 0 is named "PC", and the ocean duly loaded the phone's
        /// tier. So the whole sea was rendering at `displacementFadeDistance`
        /// 350 m with 5 clipmap rings and a 128 FFT while the project believed
        /// it was on the 2600 m / 8-ring / 256 tier — every wave dead by 350 m
        /// and a flat painted plane from there to the horizon. Measured with
        /// `CrestProbe`: mean displacement fade across a deck-level view,
        /// **0.00**, with not one pixel above 0.1.
        ///
        /// A quality level's INDEX is not stable — the list can be reordered in
        /// the inspector and Unity keeps a separate per-platform default — so
        /// an index is the wrong key for a decision this expensive. The name is
        /// the thing a person actually set, and it is what `ReadQuality`
        /// prints, which is how the disagreement was visible at all.
        public static OceanQuality Active
        {
            get
            {
                if (active == null)
                    active = Resources.Load<OceanQuality>("Ocean/" + TierAsset());
                return active;
            }
        }

        /// Which tier asset the current quality level asks for. Public so
        /// nothing has to duplicate the rule — a duplicated constant is a gate
        /// that silently stops gating, and `ReadQuality` had its own copy of
        /// this one, printing the wrong conclusion directly underneath the
        /// level name that contradicted it.
        public static string TierAsset()
        {
            string name = ActiveLevelName();
            if (name.IndexOf("Mobile", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "OceanQuality_Mobile";
            if (name.IndexOf("PC", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "OceanQuality_PC";
            // Neither name matched: fall back to what the hardware is, which is
            // the question the tier was always really about.
            return Application.isMobilePlatform ? "OceanQuality_Mobile" : "OceanQuality_PC";
        }

        public static string ActiveLevelName()
        {
            var names = QualitySettings.names;
            int i = QualitySettings.GetQualityLevel();
            return names != null && i >= 0 && i < names.Length ? names[i] : "";
        }

        /// Probes and setup scripts may force a tier.
        public static void Override(OceanQuality q) => active = q;
    }
}
