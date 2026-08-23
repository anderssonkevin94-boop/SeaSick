using UnityEngine;

namespace SeaSick.Terrain
{
    /// All island-generation tuning. Lives in an asset, not on scene
    /// components, so changing a default here actually changes the world.
    /// Only the Noise section is consumed in step 1; the rest is declared now
    /// so the asset schema doesn't churn as the pipeline stages land.
    [CreateAssetMenu(menuName = "SeaSick/Terrain Settings", fileName = "TerrainSettings")]
    public class TerrainSettings : ScriptableObject
    {
        [Header("World")]
        public int seed = 1337;
        [Tooltip("Water surface height. The whole ocean stack assumes 0.")]
        public float seaLevel = 0f;
        [Tooltip("0 = unbounded. Otherwise everything beyond this radius (metres from origin) is ocean.")]
        public float worldRadius = 0f;
        [Tooltip("Land fades out over this many metres inside worldRadius.")]
        public float worldEdgeFalloff = 500f;

        [Header("Noise (fBm)")]
        [Range(1, 8)] public int octaves = 5;
        [Tooltip("Base frequency in 1/metres. 1/400 ≈ features ~400 m across.")]
        public float baseFrequency = 1f / 400f;
        public float lacunarity = 2f;
        [Range(0f, 1f)] public float gain = 0.5f;

        [Header("Island mask")]
        [Tooltip("Continentalness frequency, much lower than the base noise.")]
        public float maskFrequency = 1f / 2500f;
        [Range(1, 4)] public int maskOctaves = 3;
        [Range(0.01f, 0.6f), Tooltip("Fraction of the world that is land. 0.12 = sparse islands.")]
        public float landRatio = 0.15f;
        [Tooltip("Width of the mask's soft edge, in mask-noise units, so islands fade into the sea.")]
        [Range(0.01f, 0.5f)] public float maskFalloff = 0.1f;
        [Tooltip("Open-ocean floor height, metres (negative).")]
        public float seabedDepth = -12f;

        [Header("Terrace")]
        [Tooltip("Normalised noise (0..1) → base height in metres. Stepped = plateaus and cliffs, linear = smooth slopes.")]
        public AnimationCurve terraceCurve = AnimationCurve.Linear(0f, 3f, 1f, 60f);

        [Header("Detail")]
        public float detailFrequency = 1f / 25f;
        [Range(1, 4)] public int detailOctaves = 2;
        public float detailAmplitude = 1.5f;

        [Header("Beach blend")]
        [Tooltip("Height above sea level up to which terrain is fully smooth (sailable, landable).")]
        public float beachHeight = 5f;
        [Tooltip("Metres above beachHeight over which smooth blends into terraced. Smaller = sharper.")]
        public float beachBlendWidth = 4f;

        [Header("Look")]
        [Tooltip("Vertex colour turns to snow above this height.")]
        public float snowHeight = 45f;

        [Header("Chunks")]
        public float chunkSize = 128f;
        [Tooltip("Vertices per chunk edge at LOD 0.")]
        public int chunkResolution = 65;
    }
}
