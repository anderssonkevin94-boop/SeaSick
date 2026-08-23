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

        /// Fbm remapped to [0, 1].
        public static float Fbm01(in float2 worldXZ, int seed, int octaves, float baseFrequency,
            float lacunarity, float gain)
            => Fbm(worldXZ, seed, octaves, baseFrequency, lacunarity, gain) * 0.5f + 0.5f;
    }
}
