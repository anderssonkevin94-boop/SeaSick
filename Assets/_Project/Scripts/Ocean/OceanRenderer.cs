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

        // Shader.PropertyToID cache for StepSimulation's per-frame sets —
        // both the compute-shader property names (used with SetInt/SetFloat/
        // SetVector/SetTexture, which take the int overload same as
        // Shader.SetGlobal*) and the shader globals published at the end.
        static readonly int NId = Shader.PropertyToID("_N");
        static readonly int OceanTimeId = Shader.PropertyToID("_OceanTime");
        static readonly int LambdaCId = Shader.PropertyToID("_LambdaC");
        static readonly int H0Id = Shader.PropertyToID("H0");
        static readonly int WaveDataId = Shader.PropertyToID("WaveData");
        static readonly int Spec0Id = Shader.PropertyToID("Spec0");
        static readonly int Spec1Id = Shader.PropertyToID("Spec1");
        static readonly int Spatial0Id = Shader.PropertyToID("Spatial0");
        static readonly int Spatial1Id = Shader.PropertyToID("Spatial1");
        static readonly int DisplacementId = Shader.PropertyToID("Displacement");
        static readonly int DerivativesId = Shader.PropertyToID("Derivatives");
        static readonly int DtId = Shader.PropertyToID("_Dt");
        static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
        static readonly int DecayFactorId = Shader.PropertyToID("_DecayFactor");
        static readonly int InjectionId = Shader.PropertyToID("_Injection");
        static readonly int InjectSharpId = Shader.PropertyToID("_InjectSharp");
        static readonly int PatchSizesId = Shader.PropertyToID("_PatchSizes");
        static readonly int TurbPrevId = Shader.PropertyToID("TurbPrev");
        static readonly int TurbOutId = Shader.PropertyToID("TurbOut");
        static readonly int OceanDisplacementId = Shader.PropertyToID("_Ocean_Displacement");
        static readonly int OceanDerivativesId = Shader.PropertyToID("_Ocean_Derivatives");
        static readonly int OceanTurbulenceId = Shader.PropertyToID("_Ocean_Turbulence");
        static readonly int OceanPatchSizesId = Shader.PropertyToID("_Ocean_PatchSizes");
        static readonly int OceanFadeParamsId = Shader.PropertyToID("_Ocean_FadeParams");

        public OceanSpectrumSettings Settings => settings;

        /// How many times h0 has been regenerated this session. A rebuild
        /// changes the surface DISCONTINUOUSLY — amplitudes step and, when the
        /// weather axes have drifted past their threshold, the whole field
        /// rotates — so anything investigating a jolt in the water wants to
        /// know whether one landed on the same frame. PerfHUD reads it.
        public int SpectrumRebuilds { get; private set; }
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
                SpectrumRebuilds++;
            }

            int n = cascades.N;
            int groups = Mathf.CeilToInt(n / 8f);

            timeEvolveShader.SetInt(NId, n);
            // Named _OceanTime, not _Time: the latter is a Unity built-in
            // shader global and colliding with it silently feeds the wrong
            // clock into the kernel.
            timeEvolveShader.SetFloat(OceanTimeId, (float)OceanTime.Now);
            // Choppiness is per cascade now. See `crestSharpen` on the
            // spectrum asset for why, and for which of the three are free.
            Vector3 cs = settings.crestSharpen;
            timeEvolveShader.SetVector(LambdaCId, new Vector4(
                settings.choppiness * Mathf.Max(0f, cs.x),
                settings.choppiness * Mathf.Max(0f, cs.y),
                settings.choppiness * Mathf.Max(0f, cs.z), 0f));

            timeEvolveShader.SetTexture(evolveKernel, H0Id, cascades.H0);
            timeEvolveShader.SetTexture(evolveKernel, WaveDataId, cascades.WaveData);
            timeEvolveShader.SetTexture(evolveKernel, Spec0Id, cascades.Spec0);
            timeEvolveShader.SetTexture(evolveKernel, Spec1Id, cascades.Spec1);
            timeEvolveShader.Dispatch(evolveKernel, groups, groups, CascadeSet.Cascades);

            fft.Inverse(cascades.Spec0, cascades.Scratch, n, CascadeSet.Cascades);
            fft.Inverse(cascades.Spec1, cascades.Scratch, n, CascadeSet.Cascades);

            timeEvolveShader.SetTexture(resolveKernel, Spatial0Id, cascades.Spec0);
            timeEvolveShader.SetTexture(resolveKernel, Spatial1Id, cascades.Spec1);
            timeEvolveShader.SetTexture(resolveKernel, DisplacementId, cascades.Displacement);
            timeEvolveShader.SetTexture(resolveKernel, DerivativesId, cascades.Derivatives);
            timeEvolveShader.Dispatch(resolveKernel, groups, groups, CascadeSet.Cascades);

            if (foamKernel >= 0)
            {
                float foamDt = Mathf.Max(0f, (float)(OceanTime.Now - lastFoamTime));
                lastFoamTime = OceanTime.Now;
                float halflife = Mathf.Max(0.5f, settings.foamHalflife);
                foamShader.SetInt(NId, n);
                foamShader.SetFloat(DtId, foamDt);
                foamShader.SetFloat(ThresholdId, settings.foamThreshold);
                foamShader.SetFloat(DecayFactorId,
                    Mathf.Exp(-0.6931472f * foamDt / halflife));
                foamShader.SetFloat(InjectionId, settings.foamInjection);
                foamShader.SetFloat(InjectSharpId,
                    Mathf.Max(0.5f, settings.foamInjectSharpness));
                foamShader.SetVector(PatchSizesId, cascades.PatchSizesVec);
                foamShader.SetTexture(foamKernel, DisplacementId, cascades.Displacement);
                foamShader.SetTexture(foamKernel, DerivativesId, cascades.Derivatives);
                foamShader.SetTexture(foamKernel, TurbPrevId, cascades.Turbulence);
                foamShader.SetTexture(foamKernel, TurbOutId, cascades.TurbulencePrev);
                foamShader.Dispatch(foamKernel, groups, groups, 1);
                cascades.SwapTurbulence();
            }

            Shader.SetGlobalTexture(OceanDisplacementId, cascades.Displacement);
            Shader.SetGlobalTexture(OceanDerivativesId, cascades.Derivatives);
            Shader.SetGlobalTexture(OceanTurbulenceId, cascades.Turbulence);
            Shader.SetGlobalVector(OceanPatchSizesId, cascades.PatchSizesVec);
            var q = OceanQuality.Active;
            float fadeEnd = q != null ? q.displacementFadeDistance : 500f;
            Shader.SetGlobalVector(OceanFadeParamsId,
                new Vector4(fadeEnd * 0.6f, fadeEnd, 0f, 0f));
            RegionField.PublishNeutralIfAbsent();

            readback?.Tick(cascades.Displacement, cascades.Derivatives,
                cascades.Turbulence, OceanTime.Now);
        }
    }
}
