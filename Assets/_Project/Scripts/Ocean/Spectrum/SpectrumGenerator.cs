using UnityEngine;
using UnityEngine.Rendering;

namespace SeaSick.Ocean
{
    /// Pushes OceanSpectrumSettings into the InitialSpectrum kernels. Runs only
    /// when parameters change (SeaStateController throttles blending rebuilds);
    /// the per-frame cost of the ocean never includes spectrum synthesis.
    ///
    /// Two dispatches, not one. h0 at a texel needs the bin amplitude at +k and
    /// at -k, and -k at texel (i,j) is +k at the mirrored texel (N-i, N-j) in
    /// the fftshifted layout the kernel uses. Evaluating both in one pass ran
    /// the 4x4-supersampled JONSWAP twice per texel for a number the neighbour
    /// already had; pass 1 (CalcAmplitude) writes every texel's own amplitude
    /// to a scratch array and pass 2 (CalcInitialSpectrum) reads its own and
    /// its mirror's. Bit-identical output, half the spectral evaluations.
    public class SpectrumGenerator
    {
        readonly ComputeShader shader;
        readonly int kernel;
        readonly int ampKernel;

        /// The pass-1 scratch, shared by every generator in the process. It is
        /// pure scratch — written and consumed inside one Generate call, never
        /// read across frames — so one array for the whole process is right,
        /// and it means the editor probes that build their own generator do
        /// not each allocate an N^2 x 3 float array. Rebuilt if an ocean of a
        /// different N asks for it (WaveSizeProbe forces the PC tier at
        /// runtime, which is the one thing that changes N mid-session).
        static RenderTexture ampScratch;

        public SpectrumGenerator(ComputeShader initialSpectrumShader)
        {
            shader = initialSpectrumShader;
            ampKernel = shader.FindKernel("CalcAmplitude");
            kernel = shader.FindKernel("CalcInitialSpectrum");
        }

        /// Rebuild every cascade slice at once.
        public void Generate(CascadeSet cascades, OceanSpectrumSettings s)
            => Generate(cascades, s, 0, CascadeSet.Cascades);

        /// Rebuild ONE cascade slice. H0 and WaveData are written in place per
        /// slice, so the slices not touched keep their previous contents and
        /// the evolve/IFFT chain reads a whole, valid field every frame —
        /// which is what lets OceanRenderer spread a rebuild over three.
        public void Generate(CascadeSet cascades, OceanSpectrumSettings s, int slice)
            => Generate(cascades, s, Mathf.Clamp(slice, 0, CascadeSet.Cascades - 1), 1);

        void Generate(CascadeSet cascades, OceanSpectrumSettings s, int firstSlice, int sliceCount)
        {
            int n = cascades.N;
            shader.SetInt("_N", n);
            // SV_DispatchThreadID.z always starts at 0, so which slices this
            // dispatch is for has to be told to the kernel.
            shader.SetInt("_Slice", firstSlice);
            shader.SetFloat("_Depth", Mathf.Max(s.depth, 2f));
            shader.SetVector("_PatchSizes", cascades.PatchSizesVec);
            shader.SetVector("_BandLow", new Vector4(
                cascades.BandLow[0], cascades.BandLow[1], cascades.BandLow[2], 0f));
            shader.SetVector("_BandHigh", new Vector4(
                cascades.BandHigh[0], cascades.BandHigh[1], cascades.BandHigh[2], 0f));
            shader.SetFloat("_U10", Mathf.Max(s.windSpeed, 0.5f));
            shader.SetFloat("_FetchMeters", Mathf.Max(s.fetchKm, 1f) * 1000f);
            shader.SetFloat("_Gamma", s.gamma);
            shader.SetVector("_WindDir", s.WindDir);
            shader.SetFloat("_SwellHs", s.swellHeight);
            shader.SetFloat("_SwellK", 2f * Mathf.PI / Mathf.Max(s.swellWavelength, 10f));
            shader.SetVector("_SwellDir", s.SwellDir);
            shader.SetFloat("_SwellSharpness", s.swellSharpness);
            shader.SetFloat("_Swell2Hs", s.swell2Height);
            shader.SetFloat("_Swell2K", 2f * Mathf.PI / Mathf.Max(s.swell2Wavelength, 10f));
            shader.SetVector("_Swell2Dir", s.Swell2Dir);
            shader.SetFloat("_Swell2Sharpness", s.swell2Sharpness);
            shader.SetFloat("_WindSeaHs", s.windSeaHeight);
            shader.SetFloat("_WindSeaK", 2f * Mathf.PI / Mathf.Max(s.windSeaWavelength, 4f));
            // It IS the wind sea, so it runs with the wind — no separate
            // direction to author, and it crosses both swells by construction.
            shader.SetVector("_WindSeaDir", s.WindDir);
            shader.SetFloat("_WindSeaSharpness", s.windSeaSharpness);
            shader.SetFloat("_DetailGain", s.detailGain);
            shader.SetFloat("_DetailLambda", s.detailWavelength);

            // Every resource a kernel declares must be bound before its
            // dispatch — an unbound one skips the dispatch silently and the
            // sea just stops. Pass 1 touches only Amp; pass 2 touches the rest.
            var amp = AmpScratch(n);
            shader.SetTexture(ampKernel, "Amp", amp);
            shader.SetTexture(kernel, "Amp", amp);
            shader.SetTexture(kernel, "Noise", cascades.Noise);
            shader.SetTexture(kernel, "H0", cascades.H0);
            shader.SetTexture(kernel, "WaveData", cascades.WaveData);

            int groups = Mathf.CeilToInt(n / 8f);
            shader.Dispatch(ampKernel, groups, groups, sliceCount);
            shader.Dispatch(kernel, groups, groups, sliceCount);
        }

        /// R32_FLOAT, so pass 2 reads back exactly the float pass 1 wrote and
        /// h0 is unchanged to the bit. A half format here would quietly
        /// re-quantise every amplitude in the sea.
        static RenderTexture AmpScratch(int n)
        {
            if (ampScratch != null && ampScratch.width == n) return ampScratch;
            if (ampScratch != null) ampScratch.Release();
            ampScratch = new RenderTexture(n, n, 0, RenderTextureFormat.RFloat,
                                           RenderTextureReadWrite.Linear)
            {
                name = "OceanSpectrumAmp",
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = CascadeSet.Cascades,
                enableRandomWrite = true,
                useMipMap = false,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            ampScratch.Create();
            return ampScratch;
        }
    }
}
