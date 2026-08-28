using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SeaSick.Ocean
{
    /// The sea's weather as a place rather than a number: drifting patches of
    /// roughness that move over the water, and slow storm cells that decide
    /// where the sea is building and where it is easing.
    ///
    /// THE HARD CONSTRAINT this is shaped around: the FFT is spatially
    /// HOMOGENEOUS. One spectrum for the whole world at any instant, so two
    /// genuinely different seas can never be on screen at once. Everything
    /// spatial has to be an amplitude envelope over that one spectrum. The
    /// division of labour that falls out of it:
    ///
    ///   CELLS (kilometres, C# only, read at the ship) drive the SPECTRUM.
    ///   They change wave height, wavelength and steepness for real, and they
    ///   ease over SeaStateController's blend time, so a storm gathers over
    ///   half a minute instead of arriving.
    ///
    ///   PATCHES (hundreds of metres, GPU + Burst) drive the ENVELOPE, and
    ///   mostly the SHORT cascades -- patchCoupling is weak on the swell and
    ///   strong on the chop. That is what real roughness patches are: a slick
    ///   or a cat's-paw changes the texture of the water, and the swell rolls
    ///   straight through it. It also means patches cost nothing that has been
    ///   measured, because cascade 0 carries nearly all of Hs.
    ///
    /// ONE BAKED TILE, SAMPLED BY BOTH TWINS. The field is periodic value
    /// noise baked to a texture once, and the GPU and the Burst job both read
    /// that texture. A second analytic noise implementation in HLSL is exactly
    /// the parity drift DivergenceProbe exists to catch, and that class of bug
    /// has cost this project a fortnight before. There is one implementation
    /// and it never runs at play time.
    ///
    /// The tile TRANSLATES rather than evolving -- weather advects, so this is
    /// what real patches do -- and its drift MEANDERS on a closed form, so the
    /// path traced through the tile is never a straight line and never
    /// repeats. Closed form and not integrated, because the whole field has to
    /// stay a pure function of (seed, OceanTime, position) or OceanTime.Scrub
    /// stops making probes repeatable.
    [DefaultExecutionOrder(-96)]
    public class WeatherField : MonoBehaviour
    {
        public static WeatherField Instance { get; private set; }

        [Header("Roughness patches (the envelope)")]
        [Tooltip("Texels per edge of the baked tile. 256 over a 4 km tile is 16 m per texel against a smallest feature of 128 m -- the field is smooth, so this is generous.")]
        [SerializeField] int tileTexels = 256;
        [Tooltip("Metres the tile covers before it repeats, and so the distance after which the pattern of patches recurs. Its largest feature is an eighth of this: 4096 m gives patches of 512, 256 and 128 m, which is the size roughness reads at from a boat.")]
        [SerializeField] float tileMetres = 4096f;
        [Tooltip("How fast patches drift, m/s. measured by LivingSeaTrace, which reports both the stationary case and the case that matters -- at a 10 m/s cruise the ship crosses a patch in under a minute whatever the drift does, so this number only sets how fast the water changes when she is NOT making way.")]
        [SerializeField] float driftSpeed = 4.2f;
        [SerializeField] float driftHeadingDeg = 200f;
        [Tooltip("How far the drift wanders off a straight line, metres. Without it the tile would translate exactly and a fixed spot would see the same pattern return every tileMetres/driftSpeed -- 38 minutes. With it the path through the tile is a curve and never closes.")]
        [SerializeField] float meanderMetres = 350f;
        [SerializeField] float meanderPeriod = 430f;
        [Tooltip("The most a patch takes off the sea. Patches only ever REDUCE: an envelope above 1 asks for waves the depth limit and the seabed cannot hold (this is why farScale went 1.5 -> 1.0), and steeper water buys another Newton step, of which there is no budget left.")]
        [Range(0.2f, 1f)] [SerializeField] float patchLo = 0.55f;
        [Tooltip("How much each cascade feels a patch: swell, mid, chop. Weak on the swell on purpose -- a roughness patch is short-wave energy and the long swell rolls through it, which is both what the sea does and what keeps patches away from everything that has been measured and gated.")]
        [SerializeField] Vector3 patchCoupling = new Vector3(0.35f, 0.85f, 1f);

        [Header("Storm cells (the spectrum)")]
        [Tooltip("Metres across which a storm cell varies. The same baked tile read at a much larger scale, so there is still only one field to keep honest.")]
        [SerializeField] float cellMetres = 20000f;
        [Tooltip("How fast cells travel, m/s. Fast enough that the same route gives a different sea on the next voyage: at 5 m/s the field moves a third of a cell between two five-minute voyages.")]
        [SerializeField] float cellDriftSpeed = 5f;
        [SerializeField] float cellHeadingDeg = 195f;

        [Header("Bake")]
        [SerializeField] int seed = 20260828;
        [Tooltip("Contrast applied after the octaves are summed. Summed noise piles up around its midpoint; without this the field would be a permanent grey 0.5 and nothing would ever be a patch.")]
        [SerializeField] float contrast = 1.7f;
        [Tooltip("Bias applied at bake time, below 1 to push the field toward its top. Patches can only REDUCE, so a field centred on 0.5 costs the chop a quarter of its amplitude EVERYWHERE -- which would quietly undo the whole texture pass of 2026-08-25. Biased to about 0.72 mean, the default state of the water is the sea as authored and a patch is a slick passing through it, which is also what slicks actually are. Baked in rather than applied at sample time: this runs eight times per query inside the sampler's Newton loop and a pow there is not free.")]
        [Range(0.2f, 2f)] [SerializeField] float patchBias = 0.42f;

        NativeArray<float> tile;
        Texture2D tileTex;

        public NativeArray<float> Tile => tile;
        public int TileTexels => tileTexels;
        public Texture2D TileTexture => tileTex;
        public float InvTileMetres => tileMetres > 0f ? 1f / tileMetres : 0f;
        public float PatchLo => patchLo;
        public float3 PatchCoupling => new float3(patchCoupling.x, patchCoupling.y, patchCoupling.z);

        /// Tile-space offset for the patch field at the current OceanTime.
        public float2 PatchOffset => MeanderedOffset(OceanTime.Now, driftSpeed,
                                                     driftHeadingDeg, InvTileMetres);

        void OnEnable()
        {
            Instance = this;
            Bake();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (tile.IsCreated) tile.Dispose();
            if (tileTex != null) Destroy(tileTex);
        }

        /// Lets DivergenceProbe shrink the tile so the field varies hard across
        /// its 600 m sample disc. Same formula, harsher parameter -- the same
        /// reason the probe over-drives choppiness by 1.25.
        public void ConfigureForTest(float metres, int texels)
        {
            tileMetres = metres;
            tileTexels = texels;
            Bake();
        }

        // ---- the field -------------------------------------------------------

        /// Drift offset in TILE UNITS. A straight run plus a closed-form
        /// meander, so it is a pure function of time.
        static float2 OffsetAt(double t, float speed, float headingDeg, float invTile)
        {
            float rad = headingDeg * Mathf.Deg2Rad;
            float2 dir = new float2(Mathf.Cos(rad), Mathf.Sin(rad));
            return -dir * (speed * (float)t) * invTile;
        }

        float2 MeanderedOffset(double t, float speed, float headingDeg, float invTile)
        {
            float2 straight = OffsetAt(t, speed, headingDeg, invTile);
            float m = Mathf.Max(meanderPeriod, 1f);
            float2 wobble = new float2(
                Mathf.Sin((float)(t / m) * 2f * Mathf.PI),
                Mathf.Cos((float)(t / (m * 1.37f)) * 2f * Mathf.PI)) * meanderMetres;
            return straight + wobble * invTile;
        }

        /// 0..1 roughness at a world position — the patch field, as both twins
        /// read it.
        public float Patch01(float2 p) => Sample(p * InvTileMetres + PatchOffset);

        /// 0..1 storm-cell weight at a world position and time. C# only: this
        /// drives the spectrum, which is global, so it is only ever read at
        /// the ship and never needs a GPU twin.
        public float Cell01(float2 p, double t)
        {
            float inv = cellMetres > 0f ? 1f / cellMetres : 0f;
            float2 off = MeanderedOffset(t, cellDriftSpeed, cellHeadingDeg, inv);
            // A different scale and a different drift off the same tile. Read
            // 8x coarser than the patches, so a "cell" is several kilometres
            // even though the tile's own features are hundreds of metres.
            return Sample(p * inv + off + new float2(0.317f, 0.611f));
        }

        /// Wrapped bilinear, matching hardware Repeat filtering exactly: texel
        /// centres at (i + 0.5)/N, and a POSITIVE modulo, because world
        /// positions west of home make these coordinates negative and C#'s %
        /// does not.
        public float Sample(float2 uv)
        {
            if (!tile.IsCreated || tileTexels <= 0) return 0.5f;
            int n = tileTexels;
            float2 f = uv * n - 0.5f;
            int2 i0 = (int2)math.floor(f);
            float2 w = f - i0;
            int x0 = Mod(i0.x, n), y0 = Mod(i0.y, n);
            int x1 = Mod(i0.x + 1, n), y1 = Mod(i0.y + 1, n);
            float a = tile[y0 * n + x0], b = tile[y0 * n + x1];
            float c = tile[y1 * n + x0], d = tile[y1 * n + x1];
            return math.lerp(math.lerp(a, b, w.x), math.lerp(c, d, w.x), w.y);
        }

        static int Mod(int a, int n) { int r = a % n; return r < 0 ? r + n : r; }

        // ---- the bake --------------------------------------------------------

        void Bake()
        {
            int n = Mathf.Max(8, tileTexels);
            tileTexels = n;
            if (tile.IsCreated && tile.Length != n * n) tile.Dispose();
            if (!tile.IsCreated) tile = new NativeArray<float>(n * n, Allocator.Persistent);

            // Lattice periods over the tile. At 4096 m these are features of
            // 512, 256 and 128 m -- the scales a patch of rough water reads at
            // from a boat, and the size that makes one pass in a couple of
            // minutes at the drift speed above.
            int[] periods = { 8, 16, 32 };
            float[] gains = { 1f, 0.5f, 0.25f };
            float norm = 1f / (gains[0] + gains[1] + gains[2]);

            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float acc = 0f;
                    for (int o = 0; o < periods.Length; o++)
                        acc += gains[o] * Periodic(new float2(u, v) * periods[o], periods[o], seed + o * 7919);
                    float val = Mathf.Clamp01((acc * norm - 0.5f) * contrast + 0.5f);
                    tile[y * n + x] = Mathf.Pow(val, Mathf.Max(patchBias, 0.01f));
                }

            if (tileTex == null || tileTex.width != n)
            {
                if (tileTex != null) Destroy(tileTex);
                tileTex = new Texture2D(n, n, TextureFormat.RFloat, false, true)
                {
                    name = "OceanWeather",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                };
            }
            tileTex.SetPixelData(tile, 0);
            tileTex.Apply(false);
        }

        /// Periodic value noise with a quintic fade. Value rather than
        /// gradient noise because this is a soft amplitude field, and periodic
        /// because a tile that does not wrap has a seam the whole ocean would
        /// wear. Periodicity is free here: the lattice index is taken modulo
        /// the octave's period.
        static float Periodic(float2 p, int period, int s)
        {
            int2 i = (int2)math.floor(p);
            float2 f = p - i;
            f = f * f * f * (f * (f * 6f - 15f) + 10f);
            int x0 = Mod(i.x, period), y0 = Mod(i.y, period);
            int x1 = Mod(i.x + 1, period), y1 = Mod(i.y + 1, period);
            float a = Hash01(x0, y0, s), b = Hash01(x1, y0, s);
            float c = Hash01(x0, y1, s), d = Hash01(x1, y1, s);
            return math.lerp(math.lerp(a, b, f.x), math.lerp(c, d, f.x), f.y);
        }

        static float Hash01(int x, int y, int s)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + s * 1442695040);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }
}
