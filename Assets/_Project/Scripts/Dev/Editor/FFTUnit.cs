using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using SeaSick.Ocean;

/// Unit test for the LDS Stockham inverse FFT core. Feeds hand-built spectra
/// through FFTCompute and compares the returned field against a CPU reference
/// DFT written in the SAME convention the kernel claims: e^{+i2pi(kx*x+kz*y)/N},
/// no 1/N^2. Catches the classic sign, transpose, slice and conjugate-symmetry
/// bugs before they hide inside the full ocean -- and compute-shader errors are
/// silent here, so a frozen sea and a wrong sea look identical without it.
///
/// What it runs, at N = 64, 128 and 256 (64 proves N below the 128-thread
/// group size still works; 128 is mobile, 256 is PC):
///   * impulse       -- one non-zero spectral texel at (kx,0): a plane wave
///                      running along x. A transposed pass moves it to z.
///   * impulse-z     -- the same at (0,kz), the other half of that trap.
///   * sinusoid      -- one conjugate-symmetric PAIR: a real cosine field with
///                      an imaginary part that must come back ~0.
///   * multi-mode    -- three conjugate pairs at once (the original test).
///   * flat spectrum -- every texel 1+0i, whose inverse is a SPATIAL impulse of
///                      N^2 at the origin and 0 everywhere else. The only case
///                      here that exercises all N^2 modes and every butterfly.
///
/// Three slices are allocated and each gets the spectrum at a different
/// amplitude, so a kernel that ignores or crosses the array index fails.
/// The two complex lanes (xy and zw) carry DIFFERENT values, so a kernel that
/// copies one lane into the other fails too.
///
/// Writes /tmp/seasick-fftunit.txt. Runs in edit mode, no play needed.
public static class FFTUnit
{
    const int Slices = 3;
    static readonly int[] Sizes = { 64, 128, 256 };

    /// Relative to the largest |reference| in the case. log2(N) float stages
    /// of a transform whose flat-spectrum peak is N^2 = 65536 cannot be held
    /// to an absolute 1e-3; measured errors sit three decades under this.
    const float RelTol = 1e-4f;

    /// One spectral mode: amplitude (re1,im1) in the xy lane and (re2,im2) in
    /// the zw lane, at DFT-native wavenumber (kx,kz) -- texel n is wavenumber
    /// n, mirror N-n.
    struct Mode
    {
        public int kx, kz;
        public float re1, im1, re2, im2;
    }

    static Mode M(int kx, int kz, float re1, float im1, float re2, float im2)
        => new Mode { kx = kx, kz = kz, re1 = re1, im1 = im1, re2 = re2, im2 = im2 };

    public static string Execute()
    {
        var sb = new StringBuilder();
        var fftShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/_Project/Art/Shaders/Ocean/FFT.compute");
        if (fftShader == null) return "FAIL: FFT.compute not found";

        // A compute shader that failed to compile still loads as an asset and
        // still dispatches -- silently doing nothing. HasKernel is the only
        // cheap tell, so ask before trusting any number below.
        foreach (var k in new[] { "FFTHorizontal", "FFTVertical",
                                  "FFTHorizontal512", "FFTVertical512" })
            if (!fftShader.HasKernel(k))
                return $"FAIL: FFT.compute has no kernel '{k}' " +
                       "-- check the Unity log for \"Shader error in 'FFT'\"";

        var fft = new FFTCompute(fftShader);
        float worstRel = 0f;
        int cases = 0, failed = 0;

        foreach (int n in Sizes)
        {
            // Wavenumbers small enough to be legal at N = 64 as well.
            RunCase(sb, fft, n, "impulse   (kx=3, kz=0)", false, ref worstRel, ref cases, ref failed,
                M(3, 0, 1f, 0f, 0f, -0.5f));
            RunCase(sb, fft, n, "impulse-z (kx=0, kz=5)", false, ref worstRel, ref cases, ref failed,
                M(0, 5, 0.25f, 0.75f, -1f, 0.125f));
            RunCase(sb, fft, n, "sinusoid  (kx=7, kz=11)", true, ref worstRel, ref cases, ref failed,
                M(7, 11, 0.7f, 0.3f, -0.4f, 0.9f));
            RunCase(sb, fft, n, "multi-mode (3 pairs)", true, ref worstRel, ref cases, ref failed,
                M(1, 0, 0.7f, 0.3f, 0.3f, 0.7f),
                M(5, 3, -0.4f, 0.9f, 0.9f, -0.4f),
                M(0, 7, 1.1f, -0.2f, -0.2f, 1.1f));
            RunFlatCase(sb, fft, n, ref worstRel, ref cases, ref failed);
        }

        bool pass = failed == 0;
        string verdict = $"{(pass ? "PASS" : "FAIL")}: {cases - failed}/{cases} cases, " +
                         $"worst relative error = {worstRel:E3} (tol {RelTol:E0})";
        sb.AppendLine(verdict);
        System.IO.File.WriteAllText("/tmp/seasick-fftunit.txt", sb.ToString());
        return sb.ToString();
    }

    // ---- cases ------------------------------------------------------------

    /// Builds a spectrum from `modes` (optionally adding the conjugate mirror
    /// of each, which turns the field real), transforms it, and compares
    /// against the direct sum over the same modes. Sparse spectra are the only
    /// ones a CPU reference can afford at N=256: the sum runs over the handful
    /// of non-zero bins, not over all N^2.
    static void RunCase(StringBuilder sb, FFTCompute fft, int n, string label, bool mirror,
                        ref float worstRel, ref int cases, ref int failed, params Mode[] modes)
    {
        var spec = new Vector4[n * n];
        var terms = new List<Mode>();
        foreach (var m in modes)
        {
            int kx = m.kx, kz = m.kz;
            spec[kx + n * kz] += new Vector4(m.re1, m.im1, m.re2, m.im2);
            terms.Add(m);
            if (!mirror) continue;
            int mx = (n - kx) % n, mz = (n - kz) % n;
            spec[mx + n * mz] += new Vector4(m.re1, -m.im1, m.re2, -m.im2);
            terms.Add(M(mx, mz, m.re1, -m.im1, m.re2, -m.im2));
        }

        var field = Transform(fft, spec, n);

        float maxErr = 0f, peak = 0f;
        for (int s = 0; s < Slices; s++)
        {
            float amp = SliceAmp(s);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // Reference DFT, same convention as the kernel:
                    //   f(x,y) = sum_k A(k) * e^{+i 2pi (kx*x + kz*y) / N}
                    double r1 = 0, i1 = 0, r2 = 0, i2 = 0;
                    foreach (var t in terms)
                    {
                        double th = 2.0 * Mathf.PI * ((double)t.kx * x + (double)t.kz * y) / n;
                        double c = System.Math.Cos(th), sn = System.Math.Sin(th);
                        r1 += amp * (t.re1 * c - t.im1 * sn);
                        i1 += amp * (t.re1 * sn + t.im1 * c);
                        r2 += amp * (t.re2 * c - t.im2 * sn);
                        i2 += amp * (t.re2 * sn + t.im2 * c);
                    }
                    Vector4 got = field[x + n * y + n * n * s];
                    Accumulate(got, (float)r1, (float)i1, (float)r2, (float)i2, ref maxErr, ref peak);
                }
        }
        Report(sb, n, label, maxErr, peak, ref worstRel, ref cases, ref failed);
    }

    /// Flat spectrum (every bin 1+0i) -> spatial impulse of N^2 at the origin.
    /// Every one of the N^2 modes contributes, so this is the case that would
    /// catch a butterfly the sparse tests never light up; off the origin it is
    /// pure cancellation, which is also the strictest numerical check here.
    static void RunFlatCase(StringBuilder sb, FFTCompute fft, int n,
                            ref float worstRel, ref int cases, ref int failed)
    {
        var spec = new Vector4[n * n];
        for (int i = 0; i < spec.Length; i++) spec[i] = new Vector4(1f, 0f, 2f, 0f);

        var field = Transform(fft, spec, n);

        float maxErr = 0f, peak = 0f;
        for (int s = 0; s < Slices; s++)
        {
            float amp = SliceAmp(s);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool origin = x == 0 && y == 0;
                    float e = origin ? amp * n * n : 0f;
                    Vector4 got = field[x + n * y + n * n * s];
                    Accumulate(got, e, 0f, 2f * e, 0f, ref maxErr, ref peak);
                }
        }
        Report(sb, n, "flat spectrum -> spatial impulse", maxErr, peak,
               ref worstRel, ref cases, ref failed);
    }

    // ---- plumbing ---------------------------------------------------------

    /// Slice s carries the spectrum at this amplitude. Distinct per slice so a
    /// kernel that drops SV_GroupID.z, or bleeds between slices, shows up.
    static float SliceAmp(int s) => 1f + s;

    static void Accumulate(Vector4 got, float r1, float i1, float r2, float i2,
                           ref float maxErr, ref float peak)
    {
        maxErr = Mathf.Max(maxErr, Mathf.Abs(got.x - r1), Mathf.Abs(got.y - i1));
        maxErr = Mathf.Max(maxErr, Mathf.Abs(got.z - r2), Mathf.Abs(got.w - i2));
        peak = Mathf.Max(peak, Mathf.Abs(r1), Mathf.Abs(i1), Mathf.Abs(r2), Mathf.Abs(i2));
    }

    static void Report(StringBuilder sb, int n, string label, float maxErr, float peak,
                       ref float worstRel, ref int cases, ref int failed)
    {
        float rel = maxErr / Mathf.Max(1f, peak);
        bool ok = rel <= RelTol;
        cases++;
        if (!ok) failed++;
        worstRel = Mathf.Max(worstRel, rel);
        sb.AppendLine($"N={n,4}  {label,-34}  max|err| = {maxErr:E3}  " +
                      $"peak = {peak:E3}  rel = {rel:E3}  {(ok ? "ok" : "FAIL")}");
    }

    /// Uploads `spec` (scaled per slice), runs the inverse FFT and reads all
    /// three slices back. Index into the result: x + n*y + n*n*slice.
    static Vector4[] Transform(FFTCompute fft, Vector4[] spec, int n)
    {
        var data = NewArrayRT(n);
        var scratch = NewArrayRT(n);
        var upload = new Texture2D(n, n, TextureFormat.RGBAFloat, false, true);
        var scaled = new Vector4[spec.Length];

        for (int s = 0; s < Slices; s++)
        {
            float amp = SliceAmp(s);
            for (int i = 0; i < spec.Length; i++) scaled[i] = spec[i] * amp;
            upload.SetPixelData(scaled, 0);
            upload.Apply(false);
            Graphics.CopyTexture(upload, 0, 0, data, s, 0);
        }

        fft.Inverse(data, scratch, n, Slices);

        var req = AsyncGPUReadback.Request(data, 0, 0, n, 0, n, 0, Slices,
                                           TextureFormat.RGBAFloat);
        req.WaitForCompletion();
        // GetData with no layer argument hands back ONE layer (n*n), so the
        // three slices are gathered layer by layer into the flat array the
        // callers index as x + n*y + n*n*slice. An error reads as a total
        // miss, not a near-pass.
        var field = new Vector4[n * n * Slices];
        if (!req.hasError)
            for (int s = 0; s < Slices; s++)
            {
                var layer = req.GetData<Vector4>(s);
                int b = s * n * n;
                for (int i = 0; i < n * n; i++) field[b + i] = layer[i];
            }

        data.Release();
        scratch.Release();
        Object.DestroyImmediate(upload);
        return field;
    }

    static RenderTexture NewArrayRT(int n)
    {
        var rt = new RenderTexture(n, n, 0, RenderTextureFormat.ARGBFloat,
                                   RenderTextureReadWrite.Linear)
        {
            dimension = TextureDimension.Tex2DArray,
            volumeDepth = Slices,
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
        };
        rt.Create();
        return rt;
    }
}
