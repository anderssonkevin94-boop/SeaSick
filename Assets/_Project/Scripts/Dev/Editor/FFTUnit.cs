using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using SeaSick.Ocean2;

/// Unit test for the Stockham inverse FFT core: feeds hand-built spectra with
/// a handful of known modes through FFTCompute and compares the output field
/// against the analytic sum of sinusoids. Catches the classic sign, indexing
/// and conjugate-symmetry bugs before they hide inside the full ocean.
/// Writes /tmp/seasick-fftunit.txt. Runs in edit mode, no play needed.
public static class FFTUnit
{
    const int N = 64;

    public static string Execute()
    {
        var sb = new StringBuilder();
        var fftShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/_Project/Art/Shaders/Ocean/FFT.compute");
        if (fftShader == null) return "FAIL: FFT.compute not found";

        var data = NewArrayRT();
        var scratch = NewArrayRT();

        // Modes in DFT-native indexing (texel n = wavenumber n, mirror N-n).
        // (nx, nz, Ar, Ai) — packed identically into xy and zw so both complex
        // lanes are exercised.
        var modes = new[]
        {
            new Vector4(1, 0, 0.7f, 0.3f),
            new Vector4(5, 3, -0.4f, 0.9f),
            new Vector4(0, 7, 1.1f, -0.2f),
        };

        var spec = new Vector4[N * N];
        foreach (var m in modes)
        {
            int nx = (int)m.x, nz = (int)m.y;
            int mi = ((N - nx) % N) + N * ((N - nz) % N);
            spec[nx + N * nz] += new Vector4(m.z, m.w, m.z, m.w);
            spec[mi] += new Vector4(m.z, -m.w, m.z, -m.w); // conjugate mirror
        }
        var upload = new Texture2D(N, N, TextureFormat.RGBAFloat, false, true);
        upload.SetPixelData(spec, 0);
        upload.Apply(false);
        Graphics.CopyTexture(upload, 0, 0, data, 0, 0);

        new FFTCompute(fftShader).Inverse(data, scratch, N, 1);

        var req = AsyncGPUReadback.Request(data, 0, 0, N, 0, N, 0, 1, TextureFormat.RGBAFloat);
        req.WaitForCompletion();
        var field = req.GetData<Vector4>();

        float maxErr = 0f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float expected = 0f;
                foreach (var m in modes)
                {
                    float theta = 2f * Mathf.PI * (m.x * x + m.y * y) / N;
                    expected += 2f * (m.z * Mathf.Cos(theta) - m.w * Mathf.Sin(theta));
                }
                Vector4 got = field[x + N * y];
                maxErr = Mathf.Max(maxErr,
                    Mathf.Abs(got.x - expected),  // real lane of complex pair 1
                    Mathf.Abs(got.z - expected),  // real lane of complex pair 2
                    Mathf.Abs(got.y),             // imag lanes must be ~0
                    Mathf.Abs(got.w));
            }

        data.Release();
        scratch.Release();
        Object.DestroyImmediate(upload);

        bool pass = maxErr < 1e-3f;
        sb.AppendLine($"N={N}, {modes.Length} modes, max |error| = {maxErr:E3}");
        sb.AppendLine(pass ? "PASS" : "FAIL");
        System.IO.File.WriteAllText("/tmp/seasick-fftunit.txt", sb.ToString());
        return sb.ToString();
    }

    static RenderTexture NewArrayRT()
    {
        var rt = new RenderTexture(N, N, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        {
            dimension = TextureDimension.Tex2DArray,
            volumeDepth = 1,
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
        };
        rt.Create();
        return rt;
    }
}
