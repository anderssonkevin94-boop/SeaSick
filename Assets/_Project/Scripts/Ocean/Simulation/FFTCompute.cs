using UnityEngine;

namespace SeaSick.Ocean
{
    /// Drives the Stockham inverse FFT kernels: log2(N) horizontal passes then
    /// log2(N) vertical, ping-ponging between the data texture and a scratch.
    /// 2*log2(N) is even, so the result always lands back in `data`.
    /// Standalone by design so the FFTUnit probe can feed it a hand-built
    /// spectrum and check the output against an analytic sinusoid.
    public class FFTCompute
    {
        readonly ComputeShader shader;
        readonly int kernelH;
        readonly int kernelV;

        public FFTCompute(ComputeShader fftShader)
        {
            shader = fftShader;
            kernelH = shader.FindKernel("FFTHorizontal");
            kernelV = shader.FindKernel("FFTVertical");
        }

        public void Inverse(RenderTexture data, RenderTexture scratch, int n, int slices)
        {
            int log2N = Mathf.RoundToInt(Mathf.Log(n, 2f));
            shader.SetInt("_N", n);
            int groupsX = Mathf.CeilToInt(n / 2f / 128f);

            RenderTexture src = data, dst = scratch;
            for (int dim = 0; dim < 2; dim++)
            {
                int kernel = dim == 0 ? kernelH : kernelV;
                for (int p = 0; p < log2N; p++)
                {
                    shader.SetInt("_Ns", 1 << p);
                    shader.SetTexture(kernel, "Src", src);
                    shader.SetTexture(kernel, "Dst", dst);
                    shader.Dispatch(kernel, groupsX, n, slices);
                    (src, dst) = (dst, src);
                }
            }
            Debug.Assert(src == data, "FFT pass count should return result to the data texture");
        }
    }
}
