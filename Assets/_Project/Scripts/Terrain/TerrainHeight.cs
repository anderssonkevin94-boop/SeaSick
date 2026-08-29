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
        public float2 worldOffset;
        public int octaves; public float baseFrequency, lacunarity, gain;
        public int maskOctaves; public float maskFrequency, maskThreshold, maskFalloff;
        public float seabedDepth, deepSeabedDepth, shelfBand;
        public float baseHeight, reliefHeight;
        public float massifFrequency, massifMin, massifMax, massifBias, massifMaskStart;
        public float ridgeAmount, ridgeLow, ridgeHigh;
        public float shoreSlopeMin, shoreSlopeMax, shoreSlopeBias, shoreFrequency, shoreFlat, shoreTop, shoreBottom;
        public int detailOctaves; public float detailFrequency, detailAmplitude;
        public float beachHeight, beachBlendWidth;

        public const int MaskSeedOffset = 7919, DetailSeedOffset = 104729,
                         MassifSeedOffset = 15485863, RidgeSeedOffset = 4241,
                         ShoreSeedOffset = 611953;

        public static TerrainParams From(TerrainSettings s)
        {
            var p = new TerrainParams
            {
                seed = s.seed, seaLevel = s.seaLevel, worldRadius = s.worldRadius, worldEdgeFalloff = s.worldEdgeFalloff,
                worldOffset = new float2(s.worldOffset.x, s.worldOffset.y),
                octaves = s.octaves, baseFrequency = s.baseFrequency, lacunarity = s.lacunarity, gain = s.gain,
                maskOctaves = s.maskOctaves, maskFrequency = s.maskFrequency, maskFalloff = s.maskFalloff,
                maskThreshold = TerrainHeight.ThresholdForLandRatio(s.landRatio, s.maskOctaves),
                seabedDepth = s.seabedDepth,
                deepSeabedDepth = s.deepSeabedDepth,
                shelfBand = math.max(0.001f, s.shelfBand),
                baseHeight = s.baseHeight, reliefHeight = s.reliefHeight,
                massifFrequency = s.massifFrequency, massifMin = s.massifMin, massifMax = s.massifMax,
                massifBias = math.max(0.05f, s.massifBias), massifMaskStart = s.massifMaskStart,
                shoreSlopeMin = s.shoreSlopeMin, shoreSlopeMax = s.shoreSlopeMax,
                shoreSlopeBias = math.max(0.05f, s.shoreSlopeBias),
                shoreFrequency = s.shoreFrequency, shoreFlat = math.max(0f, s.shoreFlat),
                shoreTop = math.max(1f, s.shoreTop), shoreBottom = math.max(1f, s.shoreBottom),
                ridgeAmount = s.ridgeAmount, ridgeLow = s.ridgeLow,
                ridgeHigh = math.max(s.ridgeLow + 0.01f, s.ridgeHigh),
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

        /// Ridged noise remapped onto fBm's distribution.
        ///
        /// Measured, not guessed (NoiseProbe.Ridge, 240k samples at the
        /// asset's 5 octaves): RidgedRaw has mean 0.2586 and sd 0.2103 while
        /// Fbm01 has 0.5004 and 0.1605. Blending the raw field into fBm would
        /// therefore drop the mean of the land field by a quarter of its
        /// range -- every island would flatten, and it would look like the
        /// profile curve was wrong.
        ///
        /// The remap matches mean and spread and deliberately KEEPS the skew:
        /// ridged noise is right-tailed, so most ground sits mid-slope and a
        /// true summit is rare. That rarity is the point. Peaks you see
        /// everywhere are scenery; peaks you see occasionally are landmarks.
        const float RidgeRawMean = 0.2586f, RidgeToFbmScale = 0.7630f;

        public static float RidgeShaped(in float2 p, in TerrainParams prm)
            => math.saturate((TerrainNoise.RidgedRaw(p + prm.worldOffset, prm.seed + TerrainParams.RidgeSeedOffset,
                    prm.octaves, prm.baseFrequency, prm.lacunarity, prm.gain) - RidgeRawMean)
                * RidgeToFbmScale + 0.5f);

        /// Stage 1. The land's shape field, in [0, 1]: fBm low down, ridges up high.
        ///
        /// The blend is weighted by the fBm itself, which is what keeps this
        /// change out of the shallows. Below ridgeLow the field is pure fBm,
        /// bit-for-bit, so every coastline, every beach slope and every
        /// number HeightProbe and BeachProbe were tuned against is untouched;
        /// the ridges only exist above the height where the terrain stopped
        /// being a shoreline and started being a mountain.
        /// How far inland a spot is, in [0, 1]: 0 anywhere near the water,
        /// 1 deep in the island. Both the massif and the ridges are faded in
        /// by this, so every change this pass makes happens INLAND and the
        /// shoreline keeps the profile the beach work was measured against.
        public static float Interior(float mask, in TerrainParams prm)
            => math.smoothstep(prm.massifMaskStart, 1f, mask);

        public static float Noise01(in float2 p, in TerrainParams prm) => Noise01(p, prm, 1f);

        public static float Noise01(in float2 p, in TerrainParams prm, float interior)
        {
            float n = TerrainNoise.Fbm01(p + prm.worldOffset, prm.seed, prm.octaves, prm.baseFrequency, prm.lacunarity, prm.gain);
            if (prm.ridgeAmount <= 0f) return n;

            // Weighted by how far INLAND this is, not only by how high.
            //
            // Height alone was not enough, and the beach gate caught it: the
            // land/sea line is set by the MASK, not by this field, so a
            // mountain flank can run straight into the water with the shape
            // field already well above ridgeLow. Ridging there steepened the
            // shore -- beach-walkable went from 11 samples over 1 m/m to 19,
            // worst 1.26 to 1.60. Fading by the interior weight puts the
            // ridges where the massif already grows and leaves every coast
            // alone.
            float w = prm.ridgeAmount * math.smoothstep(prm.ridgeLow, prm.ridgeHigh, n) * interior;
            if (w <= 0f) return n;

            // ADDED to the base, not mixed into it, and the difference is the
            // whole summit.
            //
            // Lerping toward the ridged field was the obvious formulation and
            // it is wrong: the two fields are decorrelated by construction
            // (they must be -- ridging the SAME field anti-correlates, since
            // a high fBm means the octave values are large and 1-|n| is then
            // small, which turns every mountain into a valley). Averaging two
            // independent fields shrinks the variance of the result, and a
            // peak only survives if BOTH happen to be high there. Measured:
            // it took the island's summit DOWN from 60.7 m to 42.8 m while
            // supposedly making mountains.
            //
            // Adding the ridged field's deviation instead leaves the base's
            // own high ground intact and carves crest lines through it, and
            // the sum of two independent fields has MORE variance, not less
            // -- so the tall places get taller, which is the point.
            return SoftTop(n + w * (RidgeShaped(p, prm) - 0.5f));
        }

        /// Squeezes the top of the shape field into [0, 1) without ever
        /// flattening it.
        ///
        /// math.saturate here would clip, and a clipped shape field is a flat
        /// top -- the precise defect this pass exists to remove, reintroduced
        /// at the summits where it shows most. Measured at 0.75% of island
        /// ground before this. The knee compresses the excess asymptotically
        /// instead, so the order of two summits is always preserved: one
        /// stays higher than the other, however far past 1 they both went.
        static float SoftTop(float v)
        {
            const float Knee = 0.85f;
            if (v <= Knee) return math.max(v, 0f);
            float head = 1f - Knee;
            return Knee + head * (1f - math.exp(-(v - Knee) / head));
        }

        /// Stage 2a. The raw continentalness noise, BEFORE thresholding.
        /// The seabed needs this rather than the mask: the mask is exactly
        /// zero across the entire open ocean, so it cannot say how far from
        /// land you are, while the noise it is built from varies smoothly
        /// everywhere and can.
        public static float MaskNoise(in float2 p, in TerrainParams prm)
            => TerrainNoise.Fbm01(p + prm.worldOffset, prm.seed + TerrainParams.MaskSeedOffset,
                prm.maskOctaves, prm.maskFrequency, 2f, 0.5f);

        /// Stage 2. Continentalness: low-frequency fBm thresholded with a soft
        /// edge, then clamped by the optional world radius.
        public static float Mask(in float2 p, in TerrainParams prm)
            => MaskFromNoise(MaskNoise(p, prm), p, prm);

        public static float MaskFromNoise(float c, in float2 p, in TerrainParams prm)
        {
            float m = math.smoothstep(prm.maskThreshold, prm.maskThreshold + prm.maskFalloff, c);
            if (prm.worldRadius > 0f)
            {
                float d = math.length(p);
                m *= 1f - math.smoothstep(prm.worldRadius - prm.worldEdgeFalloff, prm.worldRadius, d);
            }
            return m;
        }

        /// Per-island height character, in [0, 1].
        public static float Massif01(in float2 p, in TerrainParams prm)
            => TerrainNoise.Fbm01(p + prm.worldOffset, prm.seed + TerrainParams.MassifSeedOffset,
                2, prm.massifFrequency, 2f, 0.5f);

        /// How many metres of relief this spot gets, and the reason islands
        /// stopped being interchangeable.
        ///
        /// The old pipeline had ONE height for the whole world: the curve
        /// topped out at 60 m everywhere, so the tallest possible land in the
        /// world was two and a half ship-lengths of a 24.2 m boat and every
        /// island was the same size as every other. Here a low-frequency
        /// field gives each island its own multiplier, biased so that big
        /// ones are rare.
        ///
        /// The multiplier is faded in by the island MASK, and that is what
        /// protects the coast. At the shoreline the mask is around 0.23, well
        /// below massifMaskStart, so the amplitude there is exactly
        /// reliefHeight -- the old ceiling -- and every shore profile is
        /// unchanged. The mountain grows inland, where the mask is 1, which
        /// is also how real islands are shaped: you do not meet the summit at
        /// the waterline.
        public static float Amplitude(in float2 p, float interior, in TerrainParams prm)
        {
            float m = math.pow(Massif01(p, prm), prm.massifBias);
            float scale = math.lerp(prm.massifMin, prm.massifMax, m);
            return prm.reliefHeight * math.lerp(1f, scale, interior);
        }

        /// Stage 3. Profile curve via LUT (linear interpolation between samples).
        public static float Terrace(float noise01, in NativeArray<float> lut)
        {
            float f = math.saturate(noise01) * (lut.Length - 1);
            int i = (int)f;
            int j = math.min(i + 1, lut.Length - 1);
            return math.lerp(lut[i], lut[j], f - i);
        }

        /// The un-terraced alternative: the profile ignored, relief straight
        /// off the noise. The beach blend reads this, so it must use the same
        /// amplitude as the terraced path or the two disagree about altitude
        /// and the blend puts a step where it was supposed to remove one.
        public static float Smooth(float noise01, float amplitude, in TerrainParams prm)
            => prm.baseHeight + math.saturate(noise01) * amplitude;

        /// Stage 4. High-frequency relief so plateaus aren't dead flat.
        public static float Detail(in float2 p, in TerrainParams prm)
            => prm.detailAmplitude * TerrainNoise.Fbm(p + prm.worldOffset, prm.seed + TerrainParams.DetailSeedOffset,
                prm.detailOctaves, prm.detailFrequency, 2f, 0.5f);

        /// Stage 5. Smooth below the beach height, terraced above, blended over
        /// beachBlendWidth metres with a smoothstep. The weight is driven by the
        /// smooth height so it is itself continuous (no seam at the beach line).
        public static float BeachBlend(float terraced, float smooth, in TerrainParams prm)
        {
            float w = math.smoothstep(prm.beachHeight, prm.beachHeight + prm.beachBlendWidth, smooth - prm.seaLevel);
            return math.lerp(smooth, terraced, w);
        }

        /// The floor under open water: deep offshore, the old shelf near land.
        ///
        /// A single global seabed cannot serve both jobs any more. Storm
        /// troughs run tens of metres below mean, so the open ocean has to be
        /// deeper than they reach or the sea clips straight through the
        /// seafloor -- but simply lowering seabedDepth would drag every
        /// island's shore down with it (the mask lerps land TOWARDS the floor)
        /// and turn the archipelago into spires, destroying the shore-slope
        /// distribution BeachProbe was tuned against.
        ///
        /// So the floor is driven by the island mask instead. Wherever the
        /// mask is meaningfully above zero the floor is exactly the shelf
        /// depth it has always been, so every shoreline profile is unchanged;
        /// only true open ocean drops away, down a continental slope nobody
        /// can see.
        /// Keyed on the mask NOISE, not the mask. The shoreline sits where
        /// lerp(floor, land, mask) crosses zero, which for a -12 m shelf under
        /// 40 m hills is mask ~= 0.23 -- so any transition keyed on the mask
        /// and wide enough to be a shelf is still steepening the beach when it
        /// gets there. Measured: HeightProbe's beach-walkable went from clean
        /// to 57 samples over 1 m per metre, worst 4.22. Keyed on the noise,
        /// the ramp finishes AT the land threshold, so every shoreline profile
        /// is bit-identical to before and the slope lives offshore where it
        /// belongs -- and the shelf can extend past the coast, which is what
        /// keeps storm seas off the beach once the envelope is depth-limited.
        public static float Seabed(float maskNoise, in TerrainParams prm)
            => math.lerp(prm.deepSeabedDepth, prm.seabedDepth,
                         math.smoothstep(prm.maskThreshold - prm.shelfBand,
                                         prm.maskThreshold, maskNoise));

        /// Stage 6. Mask multiplies the land relief over the seabed so islands
        /// fade into shallows instead of ending in a wall. The seabed keeps a
        /// little of the detail so it isn't a plane.
        public static float OverSeabed(float land, float mask, float detail, float maskNoise, in TerrainParams prm)
            => math.lerp(Seabed(maskNoise, prm) + detail * 0.5f, land, mask) + prm.seaLevel;

        /// How steep this stretch of coast is, as a fraction of the slope the
        /// raw terrain would have given: shoreSlopeMin at the sandiest,
        /// shoreSlopeMax at the rockiest. Low frequency, so a whole bay
        /// shares one character instead of alternating every few metres.
        public static float ShoreSlope(in float2 p, in TerrainParams prm)
            => math.lerp(prm.shoreSlopeMin, prm.shoreSlopeMax,
                math.pow(TerrainNoise.Fbm01(p + prm.worldOffset, prm.seed + TerrainParams.ShoreSeedOffset,
                    2, prm.shoreFrequency, 2f, 0.5f), prm.shoreSlopeBias));

        /// Lays a real foreshore into the last few metres either side of the
        /// waterline.
        ///
        /// Measured before this existed: the median beach was 6 m wide at a
        /// slope of 1:3, and 82% of coasts were steeper than 1:5. Nothing in
        /// the pipeline had ever set a beach's WIDTH -- beachHeight said how
        /// far up the sand was painted and the horizontal extent was whatever
        /// the mask gradient happened to give, so the sand was a stripe up a
        /// hillside. Real foreshores run 1:15 to 1:40.
        ///
        /// The fix is a monotone remap of height alone, which is what makes
        /// it safe: h -> h * (k + (1-k) * smoothstep(0, top, h)). Near the
        /// waterline the slope is multiplied by k, so the beach gets that
        /// much wider on the ground; by `shoreTop` the factor is exactly 1
        /// and its derivative is exactly 1, so the land above is untouched
        /// with no crease at the join. h = 0 maps to itself, so not one metre
        /// of coastline moves.
        ///
        /// Below water the same curve runs down to `shoreBottom`, which is
        /// set to the SHELF depth deliberately: the ocean's storm envelope is
        /// depth-limited against that shelf, so the transform has to be
        /// exactly identity by the time it reaches it. Deep water never sees
        /// this at all.
        public static float ShoreTerrace(float h, in float2 p, in TerrainParams prm)
        {
            float a = h - prm.seaLevel;
            if (a >= prm.shoreTop || a <= -prm.shoreBottom) return h;
            float k = ShoreSlope(p, prm);
            // The foreshore holds its gentle slope out to shoreFlat before
            // the land starts taking its own slope back. Starting the
            // recovery at the waterline instead let the factor double within
            // 7 m of height, which held the median beach at 1:7 when it was
            // asked for 1:20. Below water there is no flat band: the shelf is
            // only 12 m down and the whole transform has to be identity by
            // the time it gets there.
            float w = a >= 0f
                ? math.smoothstep(prm.shoreFlat, math.max(prm.shoreFlat + 1f, prm.shoreTop), a)
                : math.smoothstep(0f, prm.shoreBottom, -a);
            return prm.seaLevel + a * (k + (1f - k) * w);
        }

        /// Full pipeline with all intermediates. Order matters: the mask is
        /// applied BEFORE the beach blend so the blend sees real altitudes —
        /// every shoreline climbs from the seabed through the 0..beachHeight
        /// band, and that band is guaranteed smooth whatever the curve does.
        public static TerrainSample Evaluate(in float2 p, in TerrainParams prm, in NativeArray<float> lut)
        {
            TerrainSample s;
            // The mask comes FIRST now: both the ridges and the massif are
            // faded in by how far inland the spot is, so the shape field
            // cannot be computed before we know that.
            float c = MaskNoise(p, prm);
            s.mask = MaskFromNoise(c, p, prm);
            float interior = Interior(s.mask, prm);
            s.noise01 = Noise01(p, prm, interior);
            float detail = Detail(p, prm);
            float amp = Amplitude(p, interior, prm);
            float land = prm.baseHeight + Terrace(s.noise01, lut) * amp;
            s.terraced = OverSeabed(land + detail, s.mask, detail, c, prm);
            s.smooth = OverSeabed(Smooth(s.noise01, amp, prm) + detail, s.mask, detail, c, prm);
            s.height = ShoreTerrace(BeachBlend(s.terraced, s.smooth, prm), p, prm);
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
