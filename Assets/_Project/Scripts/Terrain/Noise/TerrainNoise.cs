using Unity.Burst;
using Unity.Mathematics;

namespace SeaSick.Terrain
{
    /// Seeded 2D simplex noise + fBm, pure static, Burst-compatible.
    ///
    /// Why not Mathf.PerlinNoise: unseedable, mirrors across the origin, square
    /// lattice ridges. Why not FastNoiseLite: managed instance state, can't run
    /// inside Burst jobs (step 5). Why not noise.snoise: unseedable.
    ///
    /// Gradients are chosen by an integer hash of (lattice x, lattice y, seed),
    /// so there is no permutation table, no period, and the function is exact
    /// and non-mirrored across the full signed lattice range. Sample with
    /// ABSOLUTE world coordinates — never chunk-local — or chunk seams show.
    ///
    /// Precision note: inputs are float2 after frequency scaling. With base
    /// frequencies around 1/500 m this is clean to well beyond 100 km from
    /// origin; if the world ever exceeds that, scale in double before the call.
    [BurstCompile]
    public static class TerrainNoise
    {
        const float F2 = 0.36602540378f; // (sqrt(3)-1)/2
        const float G2 = 0.21132486540f; // (3-sqrt(3))/6
        const int OctaveSeedStride = 0x5bd1e995;
        const float OctaveRotation = 0.7f;          // radians, irrational-ish w.r.t. 60° lattice symmetry
        const float BaseRotC = 0.94496f, BaseRotS = 0.32719f; // ~19.1°

        /// Lowbias32-style integer mixer. Folds (x, y, seed) into a well-mixed uint.
        static uint Hash(int x, int y, int seed)
        {
            uint h = (uint)seed;
            h ^= (uint)x * 0x9E3779B1u; h = (h ^ (h >> 16)) * 0x7FEB352Du;
            h ^= (uint)y * 0x85EBCA77u; h = (h ^ (h >> 15)) * 0x846CA68Bu;
            return h ^ (h >> 16);
        }

        /// Dot of the corner's hashed gradient with the offset. 8 unit-ish
        /// gradients (axes + diagonals) — plenty for 2D simplex.
        static float Grad(int ix, int iy, int seed, float dx, float dy)
        {
            switch (Hash(ix, iy, seed) & 7u)
            {
                case 0: return  dx + dy;
                case 1: return -dx + dy;
                case 2: return  dx - dy;
                case 3: return -dx - dy;
                case 4: return  dx * 1.41421356f;
                case 5: return -dx * 1.41421356f;
                case 6: return  dy * 1.41421356f;
                default: return -dy * 1.41421356f;
            }
        }

        /// Single-octave 2D simplex noise in [-1, 1]. p is already frequency-scaled.
        public static float Simplex(in float2 p, int seed)
        {
            float s = (p.x + p.y) * F2;
            int i = (int)math.floor(p.x + s);
            int j = (int)math.floor(p.y + s);
            float t = (i + j) * G2;
            float x0 = p.x - (i - t);
            float y0 = p.y - (j - t);

            int i1 = x0 > y0 ? 1 : 0;
            int j1 = 1 - i1;

            float x1 = x0 - i1 + G2;
            float y1 = y0 - j1 + G2;
            float x2 = x0 - 1f + 2f * G2;
            float y2 = y0 - 1f + 2f * G2;

            float n = 0f;
            float t0 = 0.5f - x0 * x0 - y0 * y0;
            if (t0 > 0f) { t0 *= t0; n += t0 * t0 * Grad(i, j, seed, x0, y0); }
            float t1 = 0.5f - x1 * x1 - y1 * y1;
            if (t1 > 0f) { t1 *= t1; n += t1 * t1 * Grad(i + i1, j + j1, seed, x1, y1); }
            float t2 = 0.5f - x2 * x2 - y2 * y2;
            if (t2 > 0f) { t2 *= t2; n += t2 * t2 * Grad(i + 1, j + 1, seed, x2, y2); }

            // 70 is the conventional 2D simplex scale; with our gradient set
            // the observed range is ~[-1, 1].
            return 70f * n;
        }

        /// Fractal Brownian motion in [-1, 1]. Octave i: frequency
        /// baseFrequency * lacunarity^i, amplitude gain^i; the sum is divided by
        /// the total amplitude so the range is independent of octave count.
        /// Each octave uses a different derived seed so they decorrelate.
        public static float Fbm(in float2 worldXZ, int seed, int octaves, float baseFrequency,
            float lacunarity, float gain)
        {
            float freq = baseFrequency;
            float amp = 1f;
            float sum = 0f;
            float norm = 0f;
            // Each octave samples a rotated domain. 2D simplex has a mild
            // directional bias along its lattice; rotating per octave (by an
            // angle that never lands on a lattice symmetry) means no single
            // direction survives the sum, and the base rotation keeps the
            // first octave's bias off the world axes too.
            float2 p = worldXZ;
            float2 rot = new float2(math.cos(OctaveRotation), math.sin(OctaveRotation));
            p = new float2(p.x * BaseRotC - p.y * BaseRotS, p.x * BaseRotS + p.y * BaseRotC);
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Simplex(p * freq, seed + o * OctaveSeedStride);
                p = new float2(p.x * rot.x - p.y * rot.y, p.x * rot.y + p.y * rot.x);
                norm += amp;
                freq *= lacunarity;
                amp *= gain;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// Ridged fBm, raw. See TerrainHeight.RidgeShaped for the remap that
        /// makes this comparable to Fbm01 -- the raw distribution here is
        /// nothing like Gaussian and must not be mixed with fBm untreated.
        ///
        /// Where fBm makes rounded hills, this makes RIDGES. Folding each
        /// octave about zero (1 - |n|) turns the noise's zero crossings --
        /// which are dense, connected CURVES through the plane, not points --
        /// into sharp maxima, so the field grows crest lines with valleys
        /// between them instead of blobs. That is the whole difference
        /// between terrain that reads as rock and terrain that reads as
        /// dough, and no amount of extra octaves gets there: octaves of
        /// ordinary fBm add smaller blobs to bigger blobs.
        ///
        /// Each octave is weighted by the previous one (the classic ridged
        /// multifractal), so fine detail only appears where a ridge already
        /// is: crests get rough, valleys stay smooth. That is roughly how
        /// erosion distributes roughness in the real world, and it is also
        /// why the result cannot be normalised analytically -- the weighting
        /// makes the octave sum signal-dependent.
        public static float RidgedRaw(in float2 worldXZ, int seed, int octaves, float baseFrequency,
            float lacunarity, float gain)
        {
            float freq = baseFrequency;
            float amp = 1f;
            float sum = 0f;
            float norm = 0f;
            float weight = 1f;
            float2 p = worldXZ;
            float2 rot = new float2(math.cos(OctaveRotation), math.sin(OctaveRotation));
            p = new float2(p.x * BaseRotC - p.y * BaseRotS, p.x * BaseRotS + p.y * BaseRotC);
            for (int o = 0; o < octaves; o++)
            {
                float n = 1f - math.abs(Simplex(p * freq, seed + o * OctaveSeedStride));
                n *= n;                             // sharpen the crest
                n *= weight;                        // detail rides on the ridge below it
                weight = math.saturate(n * 2f);
                sum += amp * n;
                norm += amp;
                p = new float2(p.x * rot.x - p.y * rot.y, p.x * rot.y + p.y * rot.x);
                freq *= lacunarity;
                amp *= gain;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// The gradient vector `Grad` dots against. Same eight gradients, so
        /// `dot(GradVec(...), d)` is `Grad(..., d.x, d.y)` exactly.
        static float2 GradVec(int ix, int iy, int seed)
        {
            switch (Hash(ix, iy, seed) & 7u)
            {
                case 0: return new float2( 1f,  1f);
                case 1: return new float2(-1f,  1f);
                case 2: return new float2( 1f, -1f);
                case 3: return new float2(-1f, -1f);
                case 4: return new float2( 1.41421356f, 0f);
                case 5: return new float2(-1.41421356f, 0f);
                case 6: return new float2(0f,  1.41421356f);
                default: return new float2(0f, -1.41421356f);
            }
        }

        static void Corner(int ix, int iy, int seed, float dx, float dy, ref float n, ref float2 d)
        {
            float t = 0.5f - dx * dx - dy * dy;
            if (t <= 0f) return;
            float2 g = GradVec(ix, iy, seed);
            float gd = g.x * dx + g.y * dy;
            float t2 = t * t, t4 = t2 * t2;
            n += t4 * gd;
            // d/dd [ t^4 (g.d) ] with t = 0.5 - |d|^2, so dt/dd = -2d:
            //   -8 t^3 (g.d) d  +  t^4 g
            d += t4 * g - 8f * t2 * t * gd * new float2(dx, dy);
        }

        /// Simplex with its analytic derivative. Exact, not a finite
        /// difference: within one simplex cell the corner offsets are the
        /// sample point minus a constant, so the derivative is just the sum
        /// of the three corners' own derivatives and no chain rule through
        /// the skew is needed.
        public static float SimplexD(in float2 p, int seed, out float2 deriv)
        {
            float s = (p.x + p.y) * F2;
            int i = (int)math.floor(p.x + s);
            int j = (int)math.floor(p.y + s);
            float t = (i + j) * G2;
            float x0 = p.x - (i - t);
            float y0 = p.y - (j - t);

            int i1 = x0 > y0 ? 1 : 0;
            int j1 = 1 - i1;
            float x1 = x0 - i1 + G2, y1 = y0 - j1 + G2;
            float x2 = x0 - 1f + 2f * G2, y2 = y0 - 1f + 2f * G2;

            float n = 0f;
            float2 d = float2.zero;
            Corner(i, j, seed, x0, y0, ref n, ref d);
            Corner(i + i1, j + j1, seed, x1, y1, ref n, ref d);
            Corner(i + 1, j + 1, seed, x2, y2, ref n, ref d);
            deriv = 70f * d;
            return 70f * n;
        }

        /// **fBm that carves valleys instead of stacking blobs.**
        ///
        /// Each octave is damped by how steep the COARSER octaves already
        /// are: `n / (1 + erosion * |grad|^2)`. Fine detail therefore
        /// survives on flats and shoulders and is suppressed on steep faces,
        /// which is what real erosion does to roughness — material will not
        /// stay on a slope. What comes out is spurs with smooth flanks
        /// running down to V-shaped hollows between them, which is the
        /// structure the reference boards are made of and the one thing plain
        /// fBm and ridged noise both cannot produce: octaves of fBm add
        /// smaller blobs to bigger ones, and ridged noise makes crests
        /// without ever making a drainage.
        ///
        /// It is an approximation of erosion, not a simulation — real
        /// hydraulic erosion is a stateful sweep over a finite grid, and this
        /// height function is a pure function of world position running in
        /// Burst on streamed chunks. There is no water and nothing moves.
        /// What it reproduces is the *statistical* signature.
        ///
        /// The gradient is accumulated WITHOUT the frequency scaling, on
        /// purpose: with a base frequency of 1/400 the true gradient is
        /// ~1e-3 and `erosion` would have to be ~1e5 to bite, which is not a
        /// number anyone can tune. Accumulated scale-free, `erosion` is O(1).
        /// Each octave's derivative is rotated back out of that octave's own
        /// rotated domain first, or the sum would be of vectors in different
        /// frames.
        ///
        /// At erosion = 0 this is `Fbm` exactly, term for term.
        public static float ErodedRaw(in float2 worldXZ, int seed, int octaves, float baseFrequency,
            float lacunarity, float gain, float erosion)
        {
            float freq = baseFrequency;
            float amp = 1f, sum = 0f, norm = 0f;
            float2 grad = float2.zero;
            float2 p = worldXZ;
            float2 rot = new float2(math.cos(OctaveRotation), math.sin(OctaveRotation));
            p = new float2(p.x * BaseRotC - p.y * BaseRotS, p.x * BaseRotS + p.y * BaseRotC);
            float rc = BaseRotC, rs = BaseRotS;   // cumulative rotation
            for (int o = 0; o < octaves; o++)
            {
                float n = SimplexD(p * freq, seed + o * OctaveSeedStride, out float2 dq);
                sum += amp * n / (1f + erosion * math.lengthsq(grad));
                norm += amp;
                // Rotation transpose: back into the common frame.
                grad += amp * new float2(rc * dq.x + rs * dq.y, -rs * dq.x + rc * dq.y);
                p = new float2(p.x * rot.x - p.y * rot.y, p.x * rot.y + p.y * rot.x);
                float nc = rc * rot.x - rs * rot.y, ns = rs * rot.x + rc * rot.y;
                rc = nc; rs = ns;
                freq *= lacunarity;
                amp *= gain;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// Fbm remapped to [0, 1].
        public static float Fbm01(in float2 worldXZ, int seed, int octaves, float baseFrequency,
            float lacunarity, float gain)
            => Fbm(worldXZ, seed, octaves, baseFrequency, lacunarity, gain) * 0.5f + 0.5f;
    }
}
