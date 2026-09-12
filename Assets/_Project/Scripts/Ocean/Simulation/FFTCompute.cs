using UnityEngine;

namespace SeaSick.Ocean
{
    /// Drives the LDS (groupshared) Stockham inverse FFT kernels: ONE dispatch
    /// per axis, not one per stage. Each thread group owns a whole row (or
    /// column), pulls its N texels into groupshared memory, runs all log2(N)
    /// butterfly stages there and writes the line back once -- so main memory
    /// is touched twice per axis instead of 2*log2(N) times. On PC that turns
    /// 16 dispatches per field into 2, and 32 per frame into 4.
    ///
    /// `scratch` is still required, and still the same size/format as `data`.
    /// A line-per-group transform could in principle run in place, but that
    /// would mean binding one RenderTexture as both SRV (Src) and UAV (Dst) in
    /// a single dispatch, which D3D11 refuses and silently resolves by
    /// unbinding one of them. So the horizontal pass writes data -> scratch and
    /// the vertical pass writes scratch -> data: the result still lands back in
    /// `data`, exactly as the old even pass count guaranteed, and the read and
    /// write textures are never the same resource.
    ///
    /// Standalone by design so the FFTUnit probe can feed it a hand-built
    /// spectrum and check the output against a CPU reference DFT.
    public class FFTCompute
    {
        /// Largest N the default kernels can hold in groupshared memory
        /// (2 * 256 * 16 B = 8 KB). Must match LDS_N on the first two
        /// #pragma kernel lines in FFT.compute.
        public const int MaxN = 256;

        /// Largest N the *512 kernel variants can hold (16 KB of LDS). Only
        /// selected when n > MaxN, so phones never pay the bigger request.
        public const int MaxNLarge = 512;

        readonly ComputeShader shader;
        readonly int kernelH;
        readonly int kernelV;
        readonly int kernelH512;
        readonly int kernelV512;

        static readonly int NId = Shader.PropertyToID("_N");
        static readonly int SrcId = Shader.PropertyToID("Src");
        static readonly int DstId = Shader.PropertyToID("Dst");

        int loggedBadN = -1;

        public FFTCompute(ComputeShader fftShader)
        {
            shader = fftShader;
            kernelH = shader.FindKernel("FFTHorizontal");
            kernelV = shader.FindKernel("FFTVertical");
            kernelH512 = shader.HasKernel("FFTHorizontal512")
                ? shader.FindKernel("FFTHorizontal512") : -1;
            kernelV512 = shader.HasKernel("FFTVertical512")
                ? shader.FindKernel("FFTVertical512") : -1;
        }

        /// In-place (as far as the caller is concerned) inverse FFT of every
        /// slice of `data`, using `scratch` as the between-axes buffer.
        /// Signature unchanged so OceanRenderer and FFTUnit compile untouched.
        public void Inverse(RenderTexture data, RenderTexture scratch, int n, int slices)
        {
            if (!ValidN(n)) return;

            bool large = n > MaxN;
            int kh = large ? kernelH512 : kernelH;
            int kv = large ? kernelV512 : kernelV;
            if (kh < 0 || kv < 0)
            {
                if (loggedBadN != n)
                {
                    loggedBadN = n;
                    Debug.LogError($"FFTCompute: no kernel variant for N={n} " +
                                   "(FFT.compute is missing the *512 kernels)");
                }
                return;
            }

            shader.SetInt(NId, n);

            // Horizontal: one group per (row, slice). Group id y = the row,
            // z = the slice; the group's 128 threads walk x.
            shader.SetTexture(kh, SrcId, data);
            shader.SetTexture(kh, DstId, scratch);
            shader.Dispatch(kh, 1, n, slices);

            // Vertical: one group per (column, slice). Group id y = the
            // COLUMN this time, z = the slice; the threads walk y.
            shader.SetTexture(kv, SrcId, scratch);
            shader.SetTexture(kv, DstId, data);
            shader.Dispatch(kv, 1, n, slices);
        }

        /// N must be a power of two in [2, 512]: the butterfly index math
        /// assumes it, and the groupshared line buffer is sized for it. A bad
        /// N would otherwise read as a frozen sea with no error anywhere --
        /// compute-shader failures in this project are silent.
        bool ValidN(int n)
        {
            bool ok = n >= 2 && n <= MaxNLarge && (n & (n - 1)) == 0;
            Debug.Assert(ok, $"FFTCompute: N must be a power of two in [2,{MaxNLarge}], got {n}");
            if (!ok && loggedBadN != n)
            {
                loggedBadN = n;
                Debug.LogError($"FFTCompute: N={n} is not a power of two in " +
                               $"[2,{MaxNLarge}]; skipping the transform");
            }
            return ok;
        }
    }
}
