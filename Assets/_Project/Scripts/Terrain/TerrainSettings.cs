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
        [Tooltip("Added to world XZ before sampling, so a chosen island can be slid under the home position without changing the seed.")]
        public Vector2 worldOffset = Vector2.zero;

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
        [Tooltip("Shelf floor height around land, metres (negative). This is the depth every island's shore profile was tuned against.")]
        public float seabedDepth = -12f;
        [Tooltip("Open-ocean floor height, metres (negative). Must be deeper than the deepest storm trough, or the sea clips through the seafloor.")]
        public float deepSeabedDepth = -180f;
        [Tooltip("How far OUTSIDE the land threshold the shelf reaches, in mask-noise units. Bigger = a wider shelf and a continental slope further offshore.")]
        [Range(0.01f, 0.3f)] public float shelfBand = 0.12f;

        [Header("Relief")]
        [Tooltip("Normalised noise (0..1) → normalised relief (0..1). This is the island's PROFILE, not its height: metres come from baseHeight + reliefHeight * massif. Steps in it read as benches and cliffs; the overall climb is what gives an island a peak instead of a tabletop.")]
        public AnimationCurve profileCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Height of the lowest land, metres above sea level.")]
        public float baseHeight = 3f;
        [Tooltip("Relief at the COAST, metres. Kept at the old curve ceiling so every shoreline profile the beach work was measured against is unchanged; the massif multiplier grows the interior only.")]
        public float reliefHeight = 60f;

        [Header("Massif")]
        [Tooltip("Per-island height character. Low frequency so neighbouring islands differ: 1/5200 m.")]
        public float massifFrequency = 1f / 5200f;
        [Tooltip("Interior height multiplier at the flattest islands.")]
        public float massifMin = 0.55f;
        [Tooltip("Interior height multiplier at the tallest. 4 x 60 m = a 240 m massif, ten ship-lengths.")]
        public float massifMax = 4f;
        [Tooltip("Bias on the massif noise. Above 1 makes big islands RARE, which is what makes one a landmark rather than the norm.")]
        public float massifBias = 1.6f;
        [Tooltip("Island mask value at which the interior starts growing. Below this the coast keeps reliefHeight exactly, so the shore profile is untouched.")]
        [Range(0.1f, 0.9f)] public float massifMaskStart = 0.35f;

        [Header("Ridges")]
        [Tooltip("How much of the highland relief comes from ridged noise instead of fBm. 0 = the old rounded blobs, 1 = fully ridged.")]
        [Range(0f, 1f)] public float ridgeAmount = 0.85f;
        [Tooltip("Normalised height where ridges start appearing. Below it the field is pure fBm, which keeps every coastline exactly as it was.")]
        public float ridgeLow = 0.42f;
        [Tooltip("Normalised height where ridges are at full strength.")]
        public float ridgeHigh = 0.62f;

        [Header("Detail")]
        public float detailFrequency = 1f / 25f;
        [Range(1, 4)] public int detailOctaves = 2;
        public float detailAmplitude = 1.5f;

        [Header("Shore")]
        [Tooltip("Foreshore slope as a fraction of what the raw terrain gives, at the FLATTEST coasts. 0.08 turns a measured 1:3 into about 1:37.")]
        [Range(0.03f, 1f)] public float shoreSlopeMin = 0.05f;
        [Tooltip("Same, at the STEEPEST coasts. Keeps rock and shingle in the world so a sand beach means something.")]
        [Range(0.03f, 1f)] public float shoreSlopeMax = 0.6f;
        [Tooltip("Bias toward the flat end. Above 1 makes gentle sand the NORM and rock the exception, instead of averaging every coast into the same middling slope — which is what a straight mix gave: a median of 1:6 when the target was 1:20.")]
        public float shoreSlopeBias = 2.5f;
        [Tooltip("How the two are mixed along a coastline, 1/metres. Low frequency so a bay is all one kind of shore rather than alternating every few metres.")]
        public float shoreFrequency = 1f / 900f;
        [Tooltip("Raw height, metres, up to which the foreshore holds its gentle slope before the land starts recovering. Without this the recovery begins at the waterline and the factor has doubled by 7 m of height, which is why the first attempt only moved the median beach from 1:3 to 1:7.")]
        public float shoreFlat = 10f;
        [Tooltip("Height, metres, by which the land is back to its own slope. Everything above this is untouched, so the massifs keep their shape.")]
        public float shoreTop = 30f;
        [Tooltip("Depth, metres, by which the seabed is back to its own slope. Sits at the shelf depth on purpose: the ocean's depth limit is tuned against that shelf and must not move.")]
        public float shoreBottom = 12f;

        [Header("Beach blend")]
        [Tooltip("Height above sea level up to which terrain is fully smooth (sailable, landable).")]
        public float beachHeight = 5f;
        [Tooltip("Metres above beachHeight over which smooth blends into terraced. Smaller = sharper.")]
        public float beachBlendWidth = 4f;

        [Header("Look")]
        [Tooltip("Sand tops out here, metres above sea level — the berm. Not the same as beachHeight, which is where TERRACING starts: sand painted all the way up a blend band is what made the shore read as a yellow hillside.")]
        public float sandHeight = 3.2f;
        [Tooltip("Vertex colour turns to snow above this height. Sits above most islands on purpose: a snow cap should mark the one massif worth steering by, not every hill.")]
        public float snowHeight = 165f;

        [Header("Chunks")]
        public float chunkSize = 128f;
        [Tooltip("Vertices per chunk edge at LOD 0. (2^k)+1 so LOD strides divide evenly; max 129 for 16-bit indices.")]
        public int chunkResolution = 65;
        [Tooltip("Edge skirt drop, metres. Must clear the worst LOD crack and not one metre more: past that it is a curtain of edge-coloured geometry hanging in view at the boundary of the loaded region. Measured by TuneIslands.Skirt — worst crack 11.9 m at stride 4.")]
        public float skirtDepth = 14f;

        [Header("Streaming / LOD")]
        [Tooltip("Chunks loaded in every direction from the target's chunk.")]
        public int viewRadius = 8;
        [Tooltip("Chunks (Chebyshev distance) at full density.")]
        public int lod0Radius = 2;
        [Tooltip("Chunks at half density; beyond this, quarter density.")]
        public int lod1Radius = 4;
        [Tooltip("Mesh colliders only within this many chunks of the target.")]
        public int colliderRadius = 1;
        [Tooltip("Chunk builds in flight on worker threads at once.")]
        public int jobsInFlight = 2;

        void OnValidate()
        {
            chunkResolution = Mathf.Clamp(chunkResolution, 5, 129);
            // Force (2^k)+1 so lodStep 2 and 4 divide the cell count.
            int cells = Mathf.ClosestPowerOfTwo(chunkResolution - 1);
            chunkResolution = cells + 1;
        }
    }
}
