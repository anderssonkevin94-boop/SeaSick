using UnityEngine;

namespace SeaSick.Ocean2
{
    /// Pushes OceanSpectrumSettings into the InitialSpectrum kernel. Runs only
    /// when parameters change (SeaStateController throttles blending rebuilds);
    /// the per-frame cost of the ocean never includes spectrum synthesis.
    public class SpectrumGenerator
    {
        readonly ComputeShader shader;
        readonly int kernel;

        public SpectrumGenerator(ComputeShader initialSpectrumShader)
        {
            shader = initialSpectrumShader;
            kernel = shader.FindKernel("CalcInitialSpectrum");
        }

        public void Generate(CascadeSet cascades, OceanSpectrumSettings s)
        {
            int n = cascades.N;
            shader.SetInt("_N", n);
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
            shader.SetTexture(kernel, "Noise", cascades.Noise);
            shader.SetTexture(kernel, "H0", cascades.H0);
            shader.SetTexture(kernel, "WaveData", cascades.WaveData);

            int groups = Mathf.CeilToInt(n / 8f);
            shader.Dispatch(kernel, groups, groups, CascadeSet.Cascades);
        }
    }
}
