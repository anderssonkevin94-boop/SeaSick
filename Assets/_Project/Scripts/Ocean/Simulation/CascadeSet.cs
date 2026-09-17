using UnityEngine;
using UnityEngine.Rendering;

namespace SeaSick.Ocean
{
    /// Owns every GPU resource of the simulation: one Tex2DArray per role with
    /// one slice per cascade, plus the band limits that keep the cascades from
    /// ever simulating the same wavenumber twice (the classic shimmer bug).
    public class CascadeSet
    {
        public const int Cascades = 3;

        public int N { get; private set; }
        public float[] PatchSizes { get; private set; }
        public float[] BandLow { get; private set; } = new float[Cascades];
        public float[] BandHigh { get; private set; } = new float[Cascades];

        // Persistent spectra (rebuilt on parameter change only)
        public RenderTexture H0 { get; private set; }
        public RenderTexture WaveData { get; private set; }
        public Texture2DArray Noise { get; private set; }

        /// Accumulated wave phase per k, one slice per cascade, in radians
        /// wrapped to [0, 2pi).
        ///
        /// It is NOT part of the spectrum and a rebuild must never touch it.
        /// That is the whole point: omega depends on `depth`, which the sea
        /// state blend lerps (200 m calm -> 50 m rough -> 30 m stormy), so
        /// every rebuild hands the simulation a slightly different omega. The
        /// old `sincos(omega * _OceanTime)` turned that into a phase JUMP of
        /// d_omega * t -- proportional to how long the session has been
        /// running. Carrying phase across the change makes a new omega alter
        /// the wave's SPEED instead of teleporting it.
        public RenderTexture Phase { get; private set; }

        // Per-frame working set
        public RenderTexture Spec0 { get; private set; }
        public RenderTexture Spec1 { get; private set; }
        public RenderTexture Scratch { get; private set; }

        // Final outputs, consumed by shader + readback
        public RenderTexture Displacement { get; private set; }
        public RenderTexture Derivatives { get; private set; }
        public RenderTexture Turbulence { get; private set; }
        public RenderTexture TurbulencePrev { get; private set; }

        /// Foam ping-pong: after the accumulate dispatch the roles swap.
        public void SwapTurbulence()
        {
            (Turbulence, TurbulencePrev) = (TurbulencePrev, Turbulence);
        }

        public void Create(int n, float[] patchSizes, int noiseSeed)
        {
            Release();
            N = n;
            PatchSizes = (float[])patchSizes.Clone();

            // Cascade i owns k in [low, high). The boundary to the next finer
            // cascade sits at half that cascade's patch size, so every band is
            // comfortably inside its grid's resolvable range. The finest band
            // stops at half Nyquist to keep texels per wavelength >= 4.
            for (int i = 0; i < Cascades; i++)
            {
                BandLow[i] = i == 0 ? 1e-4f : BandHigh[i - 1];
                BandHigh[i] = i < Cascades - 1
                    ? 2f * Mathf.PI / (PatchSizes[i + 1] * 0.5f)
                    : Mathf.PI * n / PatchSizes[i] * 0.5f;
            }

            H0 = NewArray(n, RenderTextureFormat.ARGBFloat, false);
            WaveData = NewArray(n, RenderTextureFormat.ARGBFloat, false);
            // Fresh, so it holds garbage: OceanRenderer seeds it on the first
            // step after a Create (firstSpectrumBuild does the same job for H0).
            Phase = NewArray(n, RenderTextureFormat.RFloat, false);
            Spec0 = NewArray(n, RenderTextureFormat.ARGBFloat, false);
            Spec1 = NewArray(n, RenderTextureFormat.ARGBFloat, false);
            Scratch = NewArray(n, RenderTextureFormat.ARGBFloat, false);
            Displacement = NewArray(n, RenderTextureFormat.ARGBHalf, true);
            Derivatives = NewArray(n, RenderTextureFormat.ARGBHalf, true);
            // FoamAccumulate writes only uint3(id.xy, 0) and Ocean.shader reads
            // only slice 0 -- the foam layer lives on the cascade-0 grid and is
            // never per-cascade -- so one slice is correct, not a shortcut.
            Turbulence = NewArray(n, RenderTextureFormat.RHalf, true, 1);
            TurbulencePrev = NewArray(n, RenderTextureFormat.RHalf, true, 1);

            Noise = BuildNoise(n, noiseSeed);
        }

        static RenderTexture NewArray(int n, RenderTextureFormat fmt, bool bilinearRepeat, int depth = Cascades)
        {
            var rt = new RenderTexture(n, n, 0, fmt, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = depth,
                enableRandomWrite = true,
                useMipMap = false,
                filterMode = bilinearRepeat ? FilterMode.Bilinear : FilterMode.Point,
                wrapMode = bilinearRepeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        /// Unit phasors (random phase, deterministic amplitude) from a fixed
        /// seed. Full complex gaussians are the textbook choice, but each
        /// band's energy lives in only ~10-30 k-bins here, so a gaussian
        /// amplitude draw swings realized Hs by +-25% per seed — the sea you
        /// tuned isn't the sea you get. Unit phasors pin realized energy to
        /// the spectrum exactly (measured by SpectrumProbe2); with dozens of
        /// modes summing, the surface statistics stay effectively gaussian.
        /// The whole ocean remains a pure function of (settings, seed, time).
        static Texture2DArray BuildNoise(int n, int seed)
        {
            var tex = new Texture2DArray(n, n, Cascades, TextureFormat.RGFloat, false, true);
            for (int slice = 0; slice < Cascades; slice++)
            {
                var rng = new System.Random(seed + slice * 7919);
                var data = new Vector2[n * n];
                for (int i = 0; i < data.Length; i++)
                {
                    float phi = (float)rng.NextDouble() * 2f * Mathf.PI;
                    data[i] = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi));
                }
                tex.SetPixelData(data, 0, slice);
            }
            tex.Apply(false, true);
            return tex;
        }

        public Vector4 PatchSizesVec =>
            new Vector4(PatchSizes[0], PatchSizes[1], PatchSizes[2], 0f);

        public void Release()
        {
            foreach (var rt in new[] { H0, WaveData, Phase, Spec0, Spec1, Scratch, Displacement, Derivatives, Turbulence, TurbulencePrev })
                if (rt != null) rt.Release();
            if (Noise != null) Object.DestroyImmediate(Noise);
            H0 = WaveData = Phase = Spec0 = Spec1 = Scratch = Displacement = Derivatives = Turbulence = TurbulencePrev = null;
            Noise = null;
        }
    }
}
