using UnityEngine;

namespace SeaSick.Ocean
{
    /// A round, soft-edged blob, built in code so there is no texture asset.
    ///
    /// Untextured particle quads have a hard rim, and this project has now
    /// grown grey RECTANGLES sliding over the sea twice — once from the storm
    /// mist, once from the hull foam, which was deliberately left opaque and
    /// untextured on the reasoning that it would "catch the sun and read as
    /// thrown water with mass". That holds under a bright sky and fails badly
    /// under a storm lid, where the sun is at a third of its strength and the
    /// quads read as wet cardboard.
    public static class FoamTexture
    {
        static Texture2D puff;

        public static Texture2D SoftPuff()
        {
            if (puff != null) return puff;
            const int R = 64;
            var tex = new Texture2D(R, R, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                name = "SS_SoftPuff"
            };
            var px = new Color[R * R];
            for (int y = 0; y < R; y++)
                for (int x = 0; x < R; x++)
                {
                    float dx = (x + 0.5f) / R * 2f - 1f;
                    float dy = (y + 0.5f) / R * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a = a * a * (3f - 2f * a);   // smooth all the way to nothing
                    px[y * R + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            puff = tex;
            return tex;
        }
    }
}
