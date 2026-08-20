using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using SeaSick.Ocean2;

/// Validates the GPU ocean against oceanography, not against itself: for the
/// three canonical (wind, fetch) triples it measures significant wave height
/// from the actual displacement field (spatial variance summed across the
/// band-limited cascades) and peak period from a texel time series PSD, then
/// compares both to the analytic JONSWAP integrals computed here in C#.
/// If these disagree the spectrum synthesis is wrong, not the renderer.
/// Writes /tmp/seasick-spectrum2.txt. Edit mode, no play needed.
public static class SpectrumProbe2
{
    const string Out = "/tmp/seasick-spectrum2.txt";
    const float G = 9.81f;

    struct Case { public float u, fetchKm, hs, tp; public string name; }

    public static string Execute()
    {
        var cases = new[]
        {
            new Case { name = "Calm", u = 5f, fetchKm = 50f, hs = 0.78f, tp = 3.93f },
            new Case { name = "Normal", u = 12f, fetchKm = 100f, hs = 2.51f, tp = 6.62f },
            new Case { name = "Stormy", u = 22f, fetchKm = 200f, hs = 6.33f, tp = 10.21f },
        };

        var initial = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/_Project/Art/Shaders/Ocean/InitialSpectrum.compute");
        var evolve = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/_Project/Art/Shaders/Ocean/TimeEvolve.compute");
        var fftCs = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/_Project/Art/Shaders/Ocean/FFT.compute");
        if (initial == null || evolve == null || fftCs == null) return "FAIL: compute shaders missing";

        var cascades = new CascadeSet();
        cascades.Create(256, new[] { 512f, 128f, 32f }, 1337);
        var gen = new SpectrumGenerator(initial);
        var fft = new FFTCompute(fftCs);
        int kEvolve = evolve.FindKernel("TimeEvolve");
        int kResolve = evolve.FindKernel("ResolveOutputs");
        var settings = ScriptableObject.CreateInstance<OceanSpectrumSettings>();
        settings.swellHeight = 0f;
        settings.choppiness = 1f;

        var sb = new StringBuilder();
        bool allPass = true;

        foreach (var c in cases)
        {
            settings.windSpeed = c.u;
            settings.fetchKm = c.fetchKm;
            gen.Generate(cascades, settings);

            // --- Spatial Hs at a few times ---
            float varSum = 0f;
            int varSamples = 0;
            foreach (float t in new[] { 50f, 137f, 260f })
            {
                Step(evolve, kEvolve, kResolve, fft, cascades, settings, t);
                for (int slice = 0; slice < CascadeSet.Cascades; slice++)
                {
                    var req = AsyncGPUReadback.Request(cascades.Displacement, 0,
                        0, cascades.N, 0, cascades.N, slice, 1, TextureFormat.RGBAFloat);
                    req.WaitForCompletion();
                    var d = req.GetData<Vector4>();
                    double mean = 0, sq = 0;
                    for (int i = 0; i < d.Length; i++) { mean += d[i].y; }
                    mean /= d.Length;
                    for (int i = 0; i < d.Length; i++) { double e = d[i].y - mean; sq += e * e; }
                    varSum += (float)(sq / d.Length);
                }
                varSamples++;
            }
            float hsSpatial = 4f * Mathf.Sqrt(varSum / varSamples);

            // --- Temporal PSD averaged over 16 texels along a row ---
            // A single texel's periodogram is too noisy to pin the peak; the
            // average over spaced texels pins it well inside the 8% gate.
            const int Steps = 400;
            const float Dt = 0.6f;
            const int Cols = 32;
            int[] rows = { 53, 171 };
            var series = new float[Cols * rows.Length, Steps];
            for (int i = 0; i < Steps; i++)
            {
                Step(evolve, kEvolve, kResolve, fft, cascades, settings, i * Dt);
                for (int r = 0; r < rows.Length; r++)
                    for (int slice = 0; slice < CascadeSet.Cascades; slice++)
                    {
                        var req = AsyncGPUReadback.Request(cascades.Displacement, 0,
                            0, cascades.N, rows[r], 1, slice, 1, TextureFormat.RGBAFloat);
                        req.WaitForCompletion();
                        var row = req.GetData<Vector4>();
                        for (int cix = 0; cix < Cols; cix++)
                            series[r * Cols + cix, i] += row[cix * (cascades.N / Cols)].y;
                    }
            }
            float tpMeasured = PeakPeriodAveraged(series, Dt);

            // --- Analytic references ---
            AnalyticJonswap(c.u, c.fetchKm * 1000f, out float hsAna, out float tpAna,
                cascades.BandLow[0], cascades.BandHigh[2], settings.depth, out float hsBand);

            float hsErr = Mathf.Abs(hsSpatial - hsBand) / hsBand;
            float tpErr = Mathf.Abs(tpMeasured - tpAna) / tpAna;
            bool pass = hsErr < 0.10f && tpErr < 0.08f;
            allPass &= pass;

            sb.AppendLine($"[{c.name}] U={c.u} F={c.fetchKm}km");
            sb.AppendLine($"  table:     Hs={c.hs:F2}  Tp={c.tp:F2}");
            sb.AppendLine($"  analytic:  Hs={hsAna:F2} (band-limited {hsBand:F2})  Tp={tpAna:F2}");
            sb.AppendLine($"  measured:  Hs={hsSpatial:F2}  Tp={tpMeasured:F2}");
            sb.AppendLine($"  err:       Hs {hsErr:P1}  Tp {tpErr:P1}   {(pass ? "PASS" : "FAIL")}");
        }

        cascades.Release();
        Object.DestroyImmediate(settings);
        sb.AppendLine(allPass ? "ALL PASS" : "FAILURES PRESENT");
        System.IO.File.WriteAllText(Out, sb.ToString());
        return sb.ToString();
    }

    static void Step(ComputeShader evolve, int kEvolve, int kResolve, FFTCompute fft,
        CascadeSet cascades, OceanSpectrumSettings settings, float t)
    {
        int n = cascades.N;
        int groups = Mathf.CeilToInt(n / 8f);
        evolve.SetInt("_N", n);
        evolve.SetFloat("_Time", t);
        evolve.SetFloat("_Lambda", settings.choppiness);
        evolve.SetTexture(kEvolve, "H0", cascades.H0);
        evolve.SetTexture(kEvolve, "WaveData", cascades.WaveData);
        evolve.SetTexture(kEvolve, "Spec0", cascades.Spec0);
        evolve.SetTexture(kEvolve, "Spec1", cascades.Spec1);
        evolve.Dispatch(kEvolve, groups, groups, CascadeSet.Cascades);
        fft.Inverse(cascades.Spec0, cascades.Scratch, n, CascadeSet.Cascades);
        fft.Inverse(cascades.Spec1, cascades.Scratch, n, CascadeSet.Cascades);
        evolve.SetTexture(kResolve, "Spatial0", cascades.Spec0);
        evolve.SetTexture(kResolve, "Spatial1", cascades.Spec1);
        evolve.SetTexture(kResolve, "Displacement", cascades.Displacement);
        evolve.SetTexture(kResolve, "Derivatives", cascades.Derivatives);
        evolve.Dispatch(kResolve, groups, groups, CascadeSet.Cascades);
    }

    /// Naive DFT power spectra averaged over columns, parabolic peak refine.
    static float PeakPeriodAveraged(float[,] x, float dt)
    {
        int cols = x.GetLength(0);
        int n = x.GetLength(1);
        var power = new double[n / 2];
        for (int c = 0; c < cols; c++)
        {
            double mean = 0;
            for (int i = 0; i < n; i++) mean += x[c, i];
            mean /= n;
            for (int bin = 1; bin < n / 2; bin++)
            {
                double re = 0, im = 0;
                for (int i = 0; i < n; i++)
                {
                    double a = 2 * Mathf.PI * bin * i / (double)n;
                    re += (x[c, i] - mean) * System.Math.Cos(a);
                    im -= (x[c, i] - mean) * System.Math.Sin(a);
                }
                power[bin] += re * re + im * im;
            }
        }
        // Boxcar-smooth before peak picking: single-bin periodogram values are
        // chi-squared noisy even after column averaging, and the raw argmax
        // jumps bins. Smoothing width ~ the JONSWAP peak width.
        var smooth = new double[n / 2];
        for (int bin = 1; bin < n / 2; bin++)
        {
            double acc = 0; int cnt = 0;
            for (int o = -3; o <= 3; o++)
            {
                int b = bin + o;
                if (b < 1 || b >= n / 2) continue;
                acc += power[b]; cnt++;
            }
            smooth[bin] = acc / cnt;
        }
        power = smooth;
        int best = 1;
        for (int bin = 2; bin < n / 2; bin++)
            if (power[bin] > power[best]) best = bin;
        double refined = best;
        if (best > 1 && best < n / 2 - 1)
        {
            double p0 = power[best - 1], p1 = power[best], p2 = power[best + 1];
            double denom = p0 - 2 * p1 + p2;
            if (System.Math.Abs(denom) > 1e-20)
                refined = best + 0.5 * (p0 - p2) / denom;
        }
        return (float)(n * dt / refined);
    }

    /// m0 = int S(w) dw by trapezoid; also the band-limited version matching
    /// what the cascades can actually carry, so truncation loss is explicit.
    static void AnalyticJonswap(float u, float fetch, out float hs, out float tp,
        float kLow, float kHigh, float depth, out float hsBand)
    {
        float wp = 22f * Mathf.Pow(G * G / (u * fetch), 1f / 3f);
        float alpha = 0.076f * Mathf.Pow(u * u / (fetch * G), 0.22f);
        float wLow = DispersionW(kLow, depth);
        float wHigh = DispersionW(kHigh, depth);

        double m0 = 0, m0Band = 0;
        const int Steps = 4000;
        float wMax = 8f;
        float dw = wMax / Steps;
        for (int i = 1; i <= Steps; i++)
        {
            float w = i * dw;
            float sigma = w <= wp ? 0.07f : 0.09f;
            float dwp = w - wp;
            float r = Mathf.Exp(-dwp * dwp / (2f * sigma * sigma * wp * wp));
            float s = alpha * G * G / Mathf.Pow(w, 5f)
                      * Mathf.Exp(-1.25f * Mathf.Pow(wp / w, 4f))
                      * Mathf.Pow(3.3f, r);
            m0 += s * dw;
            if (w >= wLow && w < wHigh) m0Band += s * dw;
        }
        hs = 4f * Mathf.Sqrt((float)m0);
        hsBand = 4f * Mathf.Sqrt((float)m0Band);
        tp = 2f * Mathf.PI / wp;
    }

    static float DispersionW(float k, float d)
    {
        return Mathf.Sqrt(G * k * (float)System.Math.Tanh(Mathf.Min(k * d, 20f)));
    }
}
