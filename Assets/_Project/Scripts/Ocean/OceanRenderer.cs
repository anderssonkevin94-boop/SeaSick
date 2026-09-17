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
        // Which cascade slice the in-progress rebuild will do next, or -1 when
        // no rebuild is running. See the block in StepSimulation.
        int spectrumSlice = -1;
        bool firstSpectrumBuild = true;
        // Phase integrator bookkeeping. lastPhaseTime is ocean seconds, not
        // engine seconds, so a paused clock integrates a dt of zero on its own.
        double lastPhaseTime;
        bool phaseValid;
        int lastScrubCount;

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
        static readonly int PhaseId = Shader.PropertyToID("Phase");
        static readonly int PhaseDtId = Shader.PropertyToID("_PhaseDt");
        static readonly int SeedPhaseId = Shader.PropertyToID("_SeedPhase");

        public OceanSpectrumSettings Settings => settings;

        /// How many times h0 has been regenerated this session. A rebuild
        /// changes the surface DISCONTINUOUSLY — amplitudes step and, when the
        /// weather axes have drifted past their threshold, the whole field
        /// rotates — so anything investigating a jolt in the water wants to
        /// know whether one landed on the same frame. PerfHUD reads it.
        public int SpectrumRebuilds { get; private set; }

        /// Slice a spectrum rebuild over three frames, one cascade each.
        ///
        /// OFF, because slicing was drawing the ocean out of TWO DIFFERENT
        /// SEAS. The slice rebuilds one cascade per frame while the time
        /// evolution below dispatches all three EVERY frame, so for two frames
        /// of every rebuild the surface is built from cascades belonging to
        /// different spectra -- different directions, different amplitudes.
        /// The first-build case was spotted and paid whole ("a sliced one would
        /// show two frames of a sea missing cascades 1 and 2"); the same
        /// inconsistency on every LATER rebuild was not, because the old
        /// cascades are still there and merely belong to another sea.
        ///
        /// Measured by StepProbe.RebuildSweep, worst one-frame surface step in
        /// open water, 60 fps, Hs ~8 m, sliced vs whole:
        ///
        ///     rebuildHz   0.5    4.0     20.0
        ///     sliced    0.147  0.678    1.913 m
        ///     whole     0.175  0.165    0.088 m
        ///
        /// Sliced, the fault scales with the rebuild RATE, because at 20 Hz a
        /// rebuild starts every three frames and slicing takes three -- the sea
        /// is then permanently mid-rebuild. Whole, the rate-dependence is gone
        /// and the shipped 4 Hz improves 4.1x. This is the "only the ocean
        /// lags, the boat and the land are fine" report: nothing but the water
        /// goes through here.
        ///
        /// Left as a switch rather than deleted so the cost of the choice stays
        /// measurable -- see PerfProbe's sim-dispatch numbers.
        public static bool SliceSpectrumRebuild;

        /// Carry wave phase across a spectrum rebuild instead of recomputing it
        /// as omega * t from the absolute clock.
        ///
        /// ON. The full argument is in TimeEvolve.compute; the short version is
        /// that omega depends on `depth`, the sea state blend lerps depth from
        /// 200 m down to 30 m as the weather builds, and the old form turned
        /// every rebuild into a surface-wide phase jump of d_omega * t. Because
        /// it multiplies the SESSION clock the jump grows all evening, and
        /// because tanh saturates for short waves it lands entirely on the long
        /// swell -- "the big waves don't move smoothly in heavy weather", worse
        /// the longer you have been sailing.
        ///
        /// Left as a switch, and wired to the settings drawer in dev builds, so
        /// the A/B stays available at the keyboard: this artefact cannot be
        /// measured remotely (the editor throttles unfocused play mode to
        /// 10 fps) and it only exists in a full-sized sea.
        public static bool AccumulatePhase = true;

        /// How many times the phase field has been reseeded from omega * t
        /// rather than carried. Expected: once at startup, once per scrub, and
        /// every frame while AccumulatePhase is off. Anything else means the
        /// scrub guard below is firing on ordinary frames and the fix is not
        /// actually running -- PerfHUD and StepProbe read it.
        public int PhaseReseeds { get; private set; }

        public CascadeSet Cascades => cascades;

        /// Call after mutating settings; the spectrum rebuilds over the next
        /// three frames (one cascade slice each — see StepSimulation).
        public void MarkSpectrumDirty() => spectrumDirty = true;

        public void SetSettings(OceanSpectrumSettings s)
        {
            settings = s;
            spectrumDirty = true;
        }

        /// Rebuild all three cascades THIS frame, cost and all. The sliced
        /// rebuild finishes within three frames, which is inside the settling
        /// time every probe already waits out, so nothing calls this today; it
        /// exists for anything that scrubs OceanTime and reads the field back
        /// in the same frame, where three frames of latency would be three
        /// frames of the old sea.
        public void RebuildSpectrumNow()
        {
            if (spectrum == null || cascades == null || settings == null) return;
            spectrum.Generate(cascades, settings);
            spectrumDirty = false;
            spectrumSlice = -1;
            firstSpectrumBuild = false;
            SpectrumRebuilds++;
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
            // A fresh CascadeSet has empty H0, so the first build must be the
            // whole thing (see StepSimulation). WaveSizeProbe re-enables this
            // component to swap quality tiers, which is why this resets here
            // and not only at construction.
            spectrumSlice = -1;
            firstSpectrumBuild = true;
            // A fresh Phase texture holds garbage, so the first dispatch must
            // seed rather than carry.
            phaseValid = false;
            lastScrubCount = OceanTime.ScrubCount;

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

            // A rebuild is one dispatch over N^2 x 3 texels with a 4x4
            // supersampled JONSWAP in each, and SeaStateController asks for one
            // four times a second while the weather blends — which it does
            // most of the time. Landing that whole cost in the single frame
            // the dirty flag is seen is a hitch every 250 ms on a phone, so it
            // is spread over three frames, ONE CASCADE SLICE EACH.
            //
            // That is safe because H0 and WaveData are written in place per
            // slice: the slices not yet rebuilt still hold the previous h0, so
            // the evolve/IFFT chain reads a complete, valid field every frame.
            // The seam is that for two frames the sea is two-thirds new and
            // one-third old — 33 ms of a mismatch smaller than the step the
            // rebuild was going to make anyway.
            //
            // A dirty flag raised MID-pass is not honoured until the pass ends,
            // so every rebuild carries one settings snapshot across all three
            // slices; the worst a caller spamming MarkSpectrumDirty can do is
            // one full rebuild every three frames.
            if (spectrumDirty && spectrumSlice < 0)
            {
                spectrumDirty = false;
                // The very first build has no previous h0 to fall back on — a
                // sliced one would show two frames of a sea missing cascades 1
                // and 2 — so it is paid whole, once, at startup.
                if (firstSpectrumBuild || !SliceSpectrumRebuild) RebuildSpectrumNow();
                else spectrumSlice = 0;
            }
            if (spectrumSlice >= 0)
            {
                spectrum.Generate(cascades, settings, spectrumSlice);
                if (++spectrumSlice >= CascadeSet.Cascades)
                {
                    spectrumSlice = -1;
                    SpectrumRebuilds++;
                }
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

            // Seed the phase field rather than carrying it when there is
            // nothing valid to carry (first dispatch), when the clock has been
            // scrubbed (probes pin the sea that way, and seeding is what keeps
            // "scrub and re-step is exact" true), or when the fix is switched
            // off for an A/B. The dt guard is belt and braces for a clock moved
            // by something that did not go through Scrub.
            double now = OceanTime.Now;
            double phaseDt = now - lastPhaseTime;
            bool seedPhase = !phaseValid
                || !AccumulatePhase
                || OceanTime.ScrubCount != lastScrubCount
                || phaseDt < 0.0
                || phaseDt > 1.0;
            if (seedPhase) PhaseReseeds++;
            lastScrubCount = OceanTime.ScrubCount;
            lastPhaseTime = now;
            phaseValid = true;

            timeEvolveShader.SetFloat(PhaseDtId, seedPhase ? 0f : (float)phaseDt);
            timeEvolveShader.SetInt(SeedPhaseId, seedPhase ? 1 : 0);
            timeEvolveShader.SetTexture(evolveKernel, PhaseId, cascades.Phase);

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
