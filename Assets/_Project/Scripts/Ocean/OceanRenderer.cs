using UnityEngine;

namespace SeaSick.Ocean
{
    /// Orchestrator of the whole ocean: advances OceanTime, rebuilds the
    /// spectrum when told it's dirty, runs the per-frame evolve + IFFT +
    /// resolve chain, and publishes the result textures as shader globals.
    /// Plain Dispatch from Update, deliberately not a render-graph pass: the
    /// simulation has no camera dependency and must run exactly once per frame
    /// even when no camera happens to render the water.
    [DefaultExecutionOrder(-100)]
    public class OceanRenderer : MonoBehaviour
    {
        public static OceanRenderer Instance { get; private set; }

        [SerializeField] OceanSpectrumSettings settings;
        [SerializeField] ComputeShader initialSpectrumShader;
        [SerializeField] ComputeShader timeEvolveShader;
        [SerializeField] ComputeShader fftShader;
        [SerializeField] ComputeShader foamShader;
        [SerializeField] int noiseSeed = 1337;

        CascadeSet cascades;
        SpectrumGenerator spectrum;
        FFTCompute fft;
        DisplacementReadback readback;
        int evolveKernel = -1;
        int resolveKernel = -1;
        int foamKernel = -1;
        double lastFoamTime;
        bool spectrumDirty = true;

        public OceanSpectrumSettings Settings => settings;
        public CascadeSet Cascades => cascades;

        /// Call after mutating settings; the spectrum rebuilds next frame.
        public void MarkSpectrumDirty() => spectrumDirty = true;

        public void SetSettings(OceanSpectrumSettings s)
        {
            settings = s;
            spectrumDirty = true;
        }

        void OnEnable()
        {
            Instance = this;
            if (initialSpectrumShader == null || timeEvolveShader == null || fftShader == null)
            {
                Debug.LogError("OceanRenderer: compute shaders not assigned");
                enabled = false;
                return;
            }

            var q = OceanQuality.Active;
            cascades = new CascadeSet();
            cascades.Create(q != null ? q.fftSize : 256,
                q != null ? q.patchSizes : new[] { 512f, 128f, 32f }, noiseSeed);
            spectrum = new SpectrumGenerator(initialSpectrumShader);
            fft = new FFTCompute(fftShader);
            evolveKernel = timeEvolveShader.FindKernel("TimeEvolve");
            resolveKernel = timeEvolveShader.FindKernel("ResolveOutputs");
            if (foamShader != null) foamKernel = foamShader.FindKernel("FoamAccumulate");
            lastFoamTime = OceanTime.Now;
            spectrumDirty = true;

            readback = new DisplacementReadback(cascades.N);
            OceanSampler.Bind(readback, cascades.PatchSizes);
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            OceanSampler.Unbind();
            readback?.Dispose();
            readback = null;
            cascades?.Release();
            cascades = null;
        }

        void Update()
        {
            OceanTime.Advance(Time.deltaTime);
            StepSimulation();
        }

        /// One full simulation step at the current OceanTime. Public so probes
        /// can pause the clock, scrub, and re-step deterministically.
        public void StepSimulation()
        {
            if (settings == null || cascades == null) return;

            if (spectrumDirty)
            {
                spectrum.Generate(cascades, settings);
                spectrumDirty = false;
            }

            int n = cascades.N;
            int groups = Mathf.CeilToInt(n / 8f);

            timeEvolveShader.SetInt("_N", n);
            timeEvolveShader.SetFloat("_Time", (float)OceanTime.Now);
            timeEvolveShader.SetFloat("_Lambda", settings.choppiness);

            timeEvolveShader.SetTexture(evolveKernel, "H0", cascades.H0);
            timeEvolveShader.SetTexture(evolveKernel, "WaveData", cascades.WaveData);
            timeEvolveShader.SetTexture(evolveKernel, "Spec0", cascades.Spec0);
            timeEvolveShader.SetTexture(evolveKernel, "Spec1", cascades.Spec1);
            timeEvolveShader.Dispatch(evolveKernel, groups, groups, CascadeSet.Cascades);

            fft.Inverse(cascades.Spec0, cascades.Scratch, n, CascadeSet.Cascades);
            fft.Inverse(cascades.Spec1, cascades.Scratch, n, CascadeSet.Cascades);

            timeEvolveShader.SetTexture(resolveKernel, "Spatial0", cascades.Spec0);
            timeEvolveShader.SetTexture(resolveKernel, "Spatial1", cascades.Spec1);
            timeEvolveShader.SetTexture(resolveKernel, "Displacement", cascades.Displacement);
            timeEvolveShader.SetTexture(resolveKernel, "Derivatives", cascades.Derivatives);
            timeEvolveShader.Dispatch(resolveKernel, groups, groups, CascadeSet.Cascades);

            if (foamKernel >= 0)
            {
                float foamDt = Mathf.Max(0f, (float)(OceanTime.Now - lastFoamTime));
                lastFoamTime = OceanTime.Now;
                float halflife = Mathf.Max(0.5f, settings.foamHalflife);
                foamShader.SetInt("_N", n);
                foamShader.SetFloat("_Dt", foamDt);
                foamShader.SetFloat("_Threshold", settings.foamThreshold);
                foamShader.SetFloat("_DecayFactor",
                    Mathf.Exp(-0.6931472f * foamDt / halflife));
                foamShader.SetFloat("_Injection", settings.foamInjection);
                foamShader.SetVector("_PatchSizes", cascades.PatchSizesVec);
                foamShader.SetTexture(foamKernel, "Displacement", cascades.Displacement);
                foamShader.SetTexture(foamKernel, "Derivatives", cascades.Derivatives);
                foamShader.SetTexture(foamKernel, "TurbPrev", cascades.Turbulence);
                foamShader.SetTexture(foamKernel, "TurbOut", cascades.TurbulencePrev);
                foamShader.Dispatch(foamKernel, groups, groups, 1);
                cascades.SwapTurbulence();
            }

            Shader.SetGlobalTexture("_Ocean_Displacement", cascades.Displacement);
            Shader.SetGlobalTexture("_Ocean_Derivatives", cascades.Derivatives);
            Shader.SetGlobalTexture("_Ocean_Turbulence", cascades.Turbulence);
            Shader.SetGlobalVector("_Ocean_PatchSizes", cascades.PatchSizesVec);
            var q = OceanQuality.Active;
            float fadeEnd = q != null ? q.displacementFadeDistance : 500f;
            Shader.SetGlobalVector("_Ocean_FadeParams",
                new Vector4(fadeEnd * 0.6f, fadeEnd, 0f, 0f));
            RegionField.PublishNeutralIfAbsent();

            readback?.Tick(cascades.Displacement, cascades.Derivatives,
                cascades.Turbulence, OceanTime.Now);
        }
    }
}
