using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace SeaSick.Terrain
{
    /// Blittable copy of the TerrainSettings fields the height function needs,
    /// so it can be handed to Burst jobs. Build with TerrainParams.From().
    public struct TerrainParams
    {
        public int seed;
        public float seaLevel, worldRadius, worldEdgeFalloff;
        public int octaves; public float baseFrequency, lacunarity, gain;
        public int maskOctaves; public float maskFrequency, maskThreshold, maskFalloff;
        public float seabedDepth;
        public float curveMin, curveMax; // endpoints of the terrace curve = the smooth (linear) height map
        public int detailOctaves; public float detailFrequency, detailAmplitude;
        public float beachHeight, beachBlendWidth;

        public const int MaskSeedOffset = 7919, DetailSeedOffset = 104729;

        public static TerrainParams From(TerrainSettings s)
        {
            var p = new TerrainParams
            {
                seed = s.seed, seaLevel = s.seaLevel, worldRadius = s.worldRadius, worldEdgeFalloff = s.worldEdgeFalloff,
                octaves = s.octaves, baseFrequency = s.baseFrequency, lacunarity = s.lacunarity, gain = s.gain,
                maskOctaves = s.maskOctaves, maskFrequency = s.maskFrequency, maskFalloff = s.maskFalloff,
                maskThreshold = TerrainHeight.ThresholdForLandRatio(s.landRatio, s.maskOctaves),
                seabedDepth = s.seabedDepth,
                curveMin = s.terraceCurve.Evaluate(0f), curveMax = s.terraceCurve.Evaluate(1f),
                detailOctaves = s.detailOctaves, detailFrequency = s.detailFrequency, detailAmplitude = s.detailAmplitude,
                beachHeight = s.beachHeight, beachBlendWidth = math.max(0.01f, s.beachBlendWidth),
            };
            return p;
        }
    }

    /// Every intermediate of the pipeline at one point, for the visualiser.
    public struct TerrainSample
    {
        public float noise01;   // stage 1: normalised fBm
        public float mask;      // stage 2: 0 = open ocean, 1 = full land
        public float terraced;  // stage 3+4(+6): curve(noise) + detail over the seabed, metres
        public float smooth;    // un-terraced linear height + detail over the seabed, metres
        public float height;    // final, metres, sea level = 0
    }

    /// The height pipeline, stage by stage. Every function is pure and samples
    /// by ABSOLUTE world position. The terrace AnimationCurve is passed in as
    /// a LUT (see TerrainCurveLut) because curves can't be evaluated in Burst.
    [BurstCompile]
    public static class TerrainHeight
    {
        public const int LutSize = 256;

        /// Stage 1. Normalised fBm in [0, 1].
        public static float Noise01(in float2 p, in TerrainParams prm)
            => TerrainNoise.Fbm01(p, prm.seed, prm.octaves, prm.baseFrequency, prm.lacunarity, prm.gain);

        /// Stage 2. Continentalness: low-frequency fBm thresholded with a soft
        /// edge, then clamped by the optional world radius.
        public static float Mask(in float2 p, in TerrainParams prm)
        {
            float c = TerrainNoise.Fbm01(p, prm.seed + TerrainParams.MaskSeedOffset, prm.maskOctaves,
                prm.maskFrequency, 2f, 0.5f);
            float m = math.smoothstep(prm.maskThreshold, prm.maskThreshold + prm.maskFalloff, c);
            if (prm.worldRadius > 0f)
            {
                float d = math.length(p);
                m *= 1f - math.smoothstep(prm.worldRadius - prm.worldEdgeFalloff, prm.worldRadius, d);
            }
            return m;
        }

        /// Stage 3. Terrace curve via LUT (linear interpolation between samples).
        public static float Terrace(float noise01, in NativeArray<float> lut)
        {
            float f = math.saturate(noise01) * (lut.Length - 1);
            int i = (int)f;
            int j = math.min(i + 1, lut.Length - 1);
            return math.lerp(lut[i], lut[j], f - i);
        }

        /// The un-terraced alternative: straight line between the curve's endpoints.
        public static float Smooth(float noise01, in TerrainParams prm)
            => math.lerp(prm.curveMin, prm.curveMax, math.saturate(noise01));

        /// Stage 4. High-frequency relief so plateaus aren't dead flat.
        public static float Detail(in float2 p, in TerrainParams prm)
            => prm.detailAmplitude * TerrainNoise.Fbm(p, prm.seed + TerrainParams.DetailSeedOffset,
                prm.detailOctaves, prm.detailFrequency, 2f, 0.5f);

        /// Stage 5. Smooth below the beach height, terraced above, blended over
        /// beachBlendWidth metres with a smoothstep. The weight is driven by the
        /// smooth height so it is itself continuous (no seam at the beach line).
        public static float BeachBlend(float terraced, float smooth, in TerrainParams prm)
        {
            float w = math.smoothstep(prm.beachHeight, prm.beachHeight + prm.beachBlendWidth, smooth - prm.seaLevel);
            return math.lerp(smooth, terraced, w);
        }

        /// Stage 6. Mask multiplies the land relief over the seabed so islands
        /// fade into shallows instead of ending in a wall. The seabed keeps a
        /// little of the detail so it isn't a plane.
        public static float OverSeabed(float land, float mask, float detail, in TerrainParams prm)
            => math.lerp(prm.seabedDepth + detail * 0.5f, land, mask) + prm.seaLevel;

        /// Full pipeline with all intermediates. Order matters: the mask is
        /// applied BEFORE the beach blend so the blend sees real altitudes —
        /// every shoreline climbs from the seabed through the 0..beachHeight
        /// band, and that band is guaranteed smooth whatever the curve does.
        [BurstCompile]
        public static TerrainSample Evaluate(in float2 p, in TerrainParams prm, in NativeArray<float> lut)
        {
            TerrainSample s;
            s.noise01 = Noise01(p, prm);
            s.mask = Mask(p, prm);
            float detail = Detail(p, prm);
            s.terraced = OverSeabed(Terrace(s.noise01, lut) + detail, s.mask, detail, prm);
            s.smooth = OverSeabed(Smooth(s.noise01, prm) + detail, s.mask, detail, prm);
            s.height = BeachBlend(s.terraced, s.smooth, prm);
            return s;
        }

        /// Height only.
        public static float Height(in float2 p, in TerrainParams prm, in NativeArray<float> lut)
            => Evaluate(p, prm, lut).height;

        /// The mask noise is ~Gaussian around 0.5 (sd measured by NoiseProbe:
        /// 0.27 for 1 octave, 0.176 for 3, 0.156 for 8, in [0,1] units).
        /// Threshold = the (1 - landRatio) quantile, so landRatio is the
        /// fraction of the world above it. The visualiser reports the measured
        /// fraction so the approximation can be checked.
        public static float ThresholdForLandRatio(float landRatio, int maskOctaves)
        {
            float sd = maskOctaves <= 1 ? 0.27f : maskOctaves <= 3 ? 0.176f : 0.16f;
            float q = math.clamp(1f - landRatio, 0.001f, 0.999f);
            return 0.5f + sd * InverseNormalCdf(q);
        }

        // Acklam's rational approximation, |error| < 1.2e-9.
        static float InverseNormalCdf(float p)
        {
            double a1 = -3.969683028665376e+01, a2 = 2.209460984245205e+02, a3 = -2.759285104469687e+02,
                   a4 = 1.383577518672690e+02, a5 = -3.066479806614716e+01, a6 = 2.506628277459239e+00;
            double b1 = -5.447609879822406e+01, b2 = 1.615858368580409e+02, b3 = -1.556989798598866e+02,
                   b4 = 6.680131188771972e+01, b5 = -1.328068155288572e+01;
            double c1 = -7.784894002430293e-03, c2 = -3.223964580411365e-01, c3 = -2.400758277161838e+00,
                   c4 = -2.549732539343734e+00, c5 = 4.374664141464968e+00, c6 = 2.938163982698783e+00;
            double d1 = 7.784695709041462e-03, d2 = 3.224671290700398e-01, d3 = 2.445134137142996e+00,
                   d4 = 3.754408661907416e+00;
            double pl = 0.02425, ph = 1 - pl, x;
            if (p < pl)
            {
                double q = math.sqrt(-2 * math.log(p));
                x = (((((c1 * q + c2) * q + c3) * q + c4) * q + c5) * q + c6) / ((((d1 * q + d2) * q + d3) * q + d4) * q + 1);
            }
            else if (p <= ph)
            {
                double q = p - 0.5, r = q * q;
                x = (((((a1 * r + a2) * r + a3) * r + a4) * r + a5) * r + a6) * q / (((((b1 * r + b2) * r + b3) * r + b4) * r + b5) * r + 1);
            }
            else
            {
                double q = math.sqrt(-2 * math.log(1 - p));
                x = -(((((c1 * q + c2) * q + c3) * q + c4) * q + c5) * q + c6) / ((((d1 * q + d2) * q + d3) * q + d4) * q + 1);
            }
            return (float)x;
        }
    }

    /// Bakes the inspector AnimationCurve into a NativeArray for Burst.
    public static class TerrainCurveLut
    {
        public static NativeArray<float> Bake(UnityEngine.AnimationCurve curve, Allocator allocator)
        {
            var lut = new NativeArray<float>(TerrainHeight.LutSize, allocator);
            for (int i = 0; i < lut.Length; i++)
                lut[i] = curve.Evaluate(i / (float)(lut.Length - 1));
            return lut;
        }
    }
}
