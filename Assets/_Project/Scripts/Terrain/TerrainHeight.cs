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
        public int storybookLandforms;
        public float seaLevel, worldRadius, worldEdgeFalloff;
        public float2 worldOffset;
        public int octaves; public float baseFrequency, lacunarity, gain;
        public float erosion, erosionAmount;
        public int maskOctaves; public float maskFrequency, maskThreshold, maskFalloff;
        public float maskStretch, maskGrainCos, maskGrainSin, maskWarp, maskWarpFrequency;
        public int regionalGrain;
        public float grainRegionFrequency, grainBlendDegrees;
        public float nearIslandScale, nearIslandRadius, farIslandRadius;
        public float seabedDepth, deepSeabedDepth, shelfBand;
        public float baseHeight, reliefHeight;
        public float skerryAmount, skerryFrequency, skerryThreshold, skerryClearance, skerryRelief;
        public float massifFrequency, massifMin, massifMax, massifBias, massifMaskStart;
        public float ridgeAmount, ridgeLow, ridgeHigh;
        public float uplandFrequency, uplandStart, uplandFull, plainRelief, lowlandDetail;
        public float shoreSlopeMin, shoreSlopeMax, shoreSlopeBias, shoreFrequency, shoreFlat, shoreTop, shoreBottom;
        public int detailOctaves; public float detailFrequency, detailAmplitude;
        public float beachHeight, beachBlendWidth;
        public float rockCharacterFrequency, rockBias, rockFrequency, rockRelief,
                     rockThresholdSoft, rockThresholdHard;
        public int rockOctaves;
        public float verdancyFrequency, verdancyBias, verdancyFloor, verdancyRockSuppress,
                     vegSlopeSoft, vegSlopeHard, vegRockSuppress;

        /// The authored home island. `homeIsle` is 0/1 rather than a bool so
        /// the struct stays the same shape everything else in here is.
        public int homeIsle;
        public float2 homeIsleCentre, homeIsleCoveDir;
        public float homeIsleRadius, homeIsleShape, homeIsleShapeFrequency;
        public float homeIsleTop, homeIsleRoll, homeIsleRollFrequency;
        public float homeIsleShoreRun, homeIsleForeshore;
        public float homeIsleFadeStart, homeIsleFadeEnd;
        public float homeIsleCoveMouth, homeIsleCoveHead;
        public float homeIsleCoveHalfMouth, homeIsleCoveHalfHead;
        public float homeIsleCoveHalfBasin, homeIsleCoveBasin;
        public float homeIsleCoveBank, homeIsleCoveFloor;

        /// Hand edits (Kevin's home spit, 2026-10-04), applied last in
        /// `TerrainHeight.Evaluate`. Filled by `From` for the one world they
        /// belong to; `default` (count 0) everywhere else. See `TerrainEdits`.
        public TerrainEdits edits;

        public const int MaskSeedOffset = 7919, DetailSeedOffset = 104729,
                         MassifSeedOffset = 15485863, RidgeSeedOffset = 4241,
                         ShoreSeedOffset = 611953, UplandSeedOffset = 2750159,
                         RockSeedOffset = 32452843, CragSeedOffset = 49979687,
                         SkerrySeedOffset = 86028121, VerdancySeedOffset = 22801763,
                         WarpSeedOffset = 15485867, WarpSeedOffsetB = 32452867,
                         HomeShapeSeedOffset = 24036583, HomeRollSeedOffset = 6972593,
                         HomeCoveSeedOffset = 3021377,
                         GrainSeedOffsetA = 67867967, GrainSeedOffsetB = 86028157,
                         MaskAxisSeedStep = 7907, MaskNearSeedOffset = 57885161;

        public static TerrainParams From(TerrainSettings s)
        {
            var p = new TerrainParams
            {
                storybookLandforms = s.storybookLandforms ? 1 : 0,
                seed = s.seed, seaLevel = s.seaLevel, worldRadius = s.worldRadius, worldEdgeFalloff = s.worldEdgeFalloff,
                worldOffset = new float2(s.worldOffset.x, s.worldOffset.y),
                octaves = s.octaves, baseFrequency = s.baseFrequency, lacunarity = s.lacunarity, gain = s.gain,
                erosion = math.max(0f, s.erosion), erosionAmount = math.saturate(s.erosionAmount),
                maskOctaves = s.maskOctaves, maskFrequency = s.maskFrequency, maskFalloff = s.maskFalloff,
                maskStretch = math.max(1f, s.maskStretch),
                maskGrainCos = math.cos(math.radians(s.maskGrainAngle)),
                maskGrainSin = math.sin(math.radians(s.maskGrainAngle)),
                maskWarp = math.max(0f, s.maskWarp), maskWarpFrequency = s.maskWarpFrequency,
                regionalGrain = s.regionalGrain ? 1 : 0,
                grainRegionFrequency = s.grainRegionFrequency, grainBlendDegrees = s.grainBlendDegrees,
                nearIslandScale = math.max(1f, s.nearIslandScale),
                nearIslandRadius = s.nearIslandRadius,
                farIslandRadius = math.max(s.nearIslandRadius + 1f, s.farIslandRadius),
                maskThreshold = TerrainHeight.ThresholdForLandRatio(s.landRatio, s.maskOctaves),
                seabedDepth = s.seabedDepth,
                deepSeabedDepth = s.deepSeabedDepth,
                shelfBand = math.max(0.001f, s.shelfBand),
                baseHeight = s.baseHeight, reliefHeight = s.reliefHeight,
                skerryAmount = s.skerryAmount, skerryFrequency = s.skerryFrequency,
                skerryThreshold = s.skerryThreshold, skerryClearance = s.skerryClearance,
                skerryRelief = math.max(1f, s.skerryRelief),
                massifFrequency = s.massifFrequency, massifMin = s.massifMin, massifMax = s.massifMax,
                massifBias = math.max(0.05f, s.massifBias), massifMaskStart = s.massifMaskStart,
                shoreSlopeMin = s.shoreSlopeMin, shoreSlopeMax = s.shoreSlopeMax,
                shoreSlopeBias = math.max(0.05f, s.shoreSlopeBias),
                shoreFrequency = s.shoreFrequency, shoreFlat = math.max(0f, s.shoreFlat),
                shoreTop = math.max(1f, s.shoreTop), shoreBottom = math.max(1f, s.shoreBottom),
                uplandFrequency = s.uplandFrequency, uplandStart = s.uplandStart,
                uplandFull = math.max(s.uplandStart + 0.01f, s.uplandFull),
                plainRelief = s.plainRelief, lowlandDetail = s.lowlandDetail,
                ridgeAmount = s.ridgeAmount, ridgeLow = s.ridgeLow,
                ridgeHigh = math.max(s.ridgeLow + 0.01f, s.ridgeHigh),
                detailOctaves = s.detailOctaves, detailFrequency = s.detailFrequency, detailAmplitude = s.detailAmplitude,
                beachHeight = s.beachHeight, beachBlendWidth = math.max(0.01f, s.beachBlendWidth),
                rockCharacterFrequency = s.rockCharacterFrequency,
                rockBias = math.max(0.05f, s.rockBias),
                rockFrequency = s.rockFrequency, rockOctaves = s.rockOctaves,
                rockRelief = math.max(0f, s.rockRelief),
                rockThresholdSoft = s.rockThresholdSoft,
                rockThresholdHard = math.min(s.rockThresholdHard, s.rockThresholdSoft),
                verdancyFrequency = s.verdancyFrequency,
                verdancyBias = math.max(0.05f, s.verdancyBias),
                verdancyFloor = s.verdancyFloor,
                verdancyRockSuppress = s.verdancyRockSuppress,
                vegSlopeSoft = s.vegSlopeSoft,
                vegSlopeHard = math.max(s.vegSlopeSoft + 0.01f, s.vegSlopeHard),
                vegRockSuppress = s.vegRockSuppress,

                homeIsle = s.homeIsle ? 1 : 0,
                homeIsleCentre = new float2(s.homeIsleCentre.x, s.homeIsleCentre.y),
                homeIsleRadius = math.max(10f, s.homeIsleRadius),
                homeIsleShape = math.max(0f, s.homeIsleShape),
                homeIsleShapeFrequency = s.homeIsleShapeFrequency,
                homeIsleTop = s.homeIsleTop,
                homeIsleRoll = math.max(0f, s.homeIsleRoll),
                homeIsleRollFrequency = s.homeIsleRollFrequency,
                homeIsleShoreRun = math.max(1f, s.homeIsleShoreRun),
                homeIsleForeshore = math.max(1f, s.homeIsleForeshore),
                homeIsleFadeStart = math.max(0f, s.homeIsleFadeStart),
                homeIsleFadeEnd = math.max(s.homeIsleFadeStart + 1f, s.homeIsleFadeEnd),
                // Same convention as Island's sectors: 0 = +Z, 90 = +X.
                homeIsleCoveDir = new float2(math.sin(math.radians(s.homeIsleCoveBearing)),
                                             math.cos(math.radians(s.homeIsleCoveBearing))),
                homeIsleCoveHead = s.homeIsleCoveHead,
                homeIsleCoveMouth = math.max(s.homeIsleCoveHead + 5f, s.homeIsleCoveMouth),
                homeIsleCoveHalfMouth = math.max(1f, s.homeIsleCoveHalfMouth),
                homeIsleCoveHalfHead = math.max(1f, s.homeIsleCoveHalfHead),
                homeIsleCoveHalfBasin = math.max(1f, s.homeIsleCoveHalfBasin),
                homeIsleCoveBasin = math.clamp(s.homeIsleCoveBasin,
                    s.homeIsleCoveHead + 2f, math.max(s.homeIsleCoveHead + 4f, s.homeIsleCoveMouth) - 2f),
                homeIsleCoveBank = math.max(1f, s.homeIsleCoveBank),
                homeIsleCoveFloor = s.homeIsleCoveFloor,
            };
            p.edits = TerrainEdits.For(p.seed, p.worldOffset);
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
        public float rock;      // metres of rock standing proud of the soil, 0 = none
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

        /// Measured by NoiseCheck at the shipped 5 octaves, 60k samples:
        /// erosion leaves the MEAN alone (0.4995 against Fbm01's 0.4998 —
        /// unlike the ridged field, which shifted it by a quarter of the
        /// range and flattened every island) but takes about 13% off the
        /// SPREAD, 0.1604 down to 0.1396. Left unremapped that is 13% less
        /// relief everywhere, which would read as the profile curve being
        /// wrong rather than as the noise being different.
        ///
        /// The spread saturates almost immediately — 0.1411 at erosion 0.5
        /// and still 0.1392 at erosion 8 — so the remap fades in over the
        /// first half unit and is constant after it.
        const float ErodedMean = 0.4995f, ErodedToFbmScale = 1.1490f,
                    ErodedSaturatesAt = 0.5f, FbmMean = 0.4998f;

        /// The eroded field, put back on Fbm01's mean and spread.
        ///
        /// `e` is the erosion actually applied at this point, which is faded
        /// by how far inland it is — so at the shoreline `e` is 0, the field
        /// is plain fBm bit for bit, the remap is the identity, and every
        /// coastline and beach slope this project has measured is untouched.
        public static float ErodedShaped(in float2 p, in TerrainParams prm, float e)
        {
            float raw = TerrainNoise.ErodedRaw(p, prm.seed, prm.octaves,
                prm.baseFrequency, prm.lacunarity, prm.gain, e) * 0.5f + 0.5f;
            float t = math.saturate(e / ErodedSaturatesAt);
            float mean = math.lerp(FbmMean, ErodedMean, t);
            float scale = math.lerp(1f, ErodedToFbmScale, t);
            return (raw - mean) * scale + FbmMean;
        }

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
            // ONE evaluation, not two. `ErodedRaw` at erosion 0 is `Fbm`
            // term for term (NoiseCheck: worst difference 1.8e-7), so fading
            // the EROSION by how far inland we are gives the coast its
            // untouched fBm for free, instead of computing both fields and
            // lerping between them.
            float n = ErodedShaped(p + prm.worldOffset, prm,
                prm.erosion * prm.erosionAmount * interior);
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
        /// **Extra small islands, added to the continentalness noise itself.**
        ///
        /// Kevin, looking at a 2.7 ha islet: *"i really like this size of
        /// islands. throw in a few more of those."*
        ///
        /// The lift goes on the NOISE, before the threshold -- not on the
        /// mask afterwards. `Seabed` is keyed on this same noise, and
        /// `OverSeabed` lerps from the seabed to the land BY the mask, so
        /// raising the mask on its own would have left deep-ocean floor under
        /// a new island and drawn a spire from -180 m straight up to the
        /// beach. Lifting the noise moves the shelf, the shoreline and the
        /// beach profile together, exactly as they move for any other island.
        ///
        /// Gated to open water by `away`, so an islet can never fuse onto an
        /// existing coast and quietly reshape an island the rest of the world
        /// has already been sited against.
        public static float Skerry(in float2 p, float c, in TerrainParams prm)
            => Skerry(SkerryDomain(p, prm), c, prm, 0);

        /// `domain` is SkerryDomain's, handed over by a caller that already
        /// has it (the trailing int only keeps the two overloads apart).
        static float Skerry(float2 domain, float c, in TerrainParams prm, int _)
        {
            if (prm.skerryAmount <= 0f) return 0f;
            // Islets get the SAME domain as the islands. They are the small
            // end of one archipelago, not a different world laid over it, so
            // they take the same grain and the same warp.
            float sRaw = TerrainNoise.Fbm01(domain,
                prm.seed + TerrainParams.SkerrySeedOffset, 2, prm.skerryFrequency, 2f, 0.5f);
            float peak = math.saturate((sRaw - prm.skerryThreshold)
                / math.max(0.01f, 1f - prm.skerryThreshold));
            float away = 1f - math.smoothstep(prm.maskThreshold - prm.skerryClearance,
                                              prm.maskThreshold, c);
            return prm.skerryAmount * peak * away;
        }

        /// Where the continentalness field is actually sampled: the island's
        /// SHAPE, as opposed to its size (maskFrequency) or how much of the
        /// world is land (landRatio).
        ///
        /// Plain fBm makes round lobed blobs. Two transforms turn them into
        /// the long, ragged, organic things a real archipelago is made of, and
        /// the ORDER matters:
        ///
        /// 1. **Elongate.** Compressing the domain along one axis by `s`
        ///    stretches features along it by `s`. The grain axis is GLOBAL and
        ///    constant on purpose. A per-island rotation is the obvious idea
        ///    and it is a trap: the transform uses absolute position, so out
        ///    at 10 km a hundredth of a radian of drift slides the sample
        ///    point 100 m and shreds the field. Anchoring the rotation to a
        ///    grid fixes that and puts a seam down every cell boundary.
        ///    Real archipelagos have a grain anyway — glacial, tectonic —
        ///    which is exactly what a constant axis draws.
        ///
        /// 2. **Then warp.** Warping AFTER the stretch keeps the wobble
        ///    isotropic, so it bends and re-aims the elongated shapes instead
        ///    of being stretched along with them. This is what stops every
        ///    island pointing the same way, and its finer octaves are the
        ///    bays, spits and hooks. Warping first would only have moved the
        ///    blobs around before elongating them.
        ///
        /// Both are pure functions of position, which they have to be: the
        /// height field runs in Burst on a streamed world and cannot look
        /// anything up.
        public static float2 MaskDomain(in float2 p, in TerrainParams prm)
        {
            float2 q = p + prm.worldOffset;
            float2 s = Stretch(q, new float2(prm.maskGrainCos, prm.maskGrainSin), prm.maskStretch);
            return s + MaskWarpOffset(s, prm);
        }

        /// Compresses the domain along `dir` by `stretch`, which stretches
        /// features along it. `dir` is a CONSTANT here -- see MaskDomain.
        static float2 Stretch(float2 q, float2 dir, float stretch)
            => stretch > 1.0001f ? q - dir * math.dot(q, dir) * (1f - 1f / stretch) : q;

        static float2 MaskWarpOffset(float2 q, in TerrainParams prm)
        {
            if (prm.maskWarp <= 0f) return float2.zero;
            // Two decorrelated fields, not one field read twice at an
            // offset: sampling the same noise twice a fixed distance apart
            // gives a warp whose x and y are correlated, and a correlated
            // warp slides the whole field diagonally instead of curdling
            // it.
            float wx = TerrainNoise.Fbm01(q, prm.seed + TerrainParams.WarpSeedOffset,
                3, prm.maskWarpFrequency, 2f, 0.5f) - 0.5f;
            float wy = TerrainNoise.Fbm01(q, prm.seed + TerrainParams.WarpSeedOffsetB,
                3, prm.maskWarpFrequency, 2f, 0.5f) - 0.5f;
            return new float2(wx, wy) * (2f * prm.maskWarp);
        }

        /// The continentalness field before any islets are added to it.
        ///
        /// With `regionalGrain` and `nearIslandScale` both off this is the
        /// original single field, bit for bit. With either on it is a BLEND
        /// of up to six whole fields -- three grain axes 60 degrees apart,
        /// each at a near-home and a far size -- which is how a pure
        /// function of position gets a grain that turns and an island size
        /// that grows with distance without the per-point rotation or
        /// rescale that shreds the field at range (see MaskDomain):
        ///
        /// - Each field is ordinary fBm on a CONSTANT stretch, so each one is
        ///   exactly as well behaved as the original.
        /// - The weights come from slow fields (a selector angle, distance
        ///   from home) and sum to one, so the blend is continuous everywhere.
        /// - Blending independent fields shrinks their spread (two at 50/50
        ///   has 0.71 of the sd), which would drown the land in every blend
        ///   band. Dividing by sqrt(sum w^2) puts the spread back, so the
        ///   landRatio threshold still holds everywhere and a blend band is
        ///   just land of mixed character, not a moat.
        /// - Every field has its own seed. Two stretches of the SAME noise
        ///   are nearly the same field close to the origin, and correlated
        ///   fields would make that normalisation overshoot.
        ///
        /// Only fields with weight are evaluated; most of the world sits in
        /// one region and one size band, so most samples pay for one field.
        public static float MaskNoiseBase(in float2 p, in TerrainParams prm)
            => MaskNoiseBase(p, prm, out _);

        static bool BlendedMask(in TerrainParams prm)
            => prm.regionalGrain != 0 || prm.nearIslandScale > 1.0001f;

        /// Where the islet field is read. With the single-field mask it is
        /// MaskDomain, exactly as before; with the blend there is no one
        /// grain to give it, so islets take the shared warp unstretched --
        /// they are too small for their grain to show.
        static float2 SkerryDomain(in float2 p, in TerrainParams prm)
        {
            if (!BlendedMask(prm)) return MaskDomain(p, prm);
            float2 q = p + prm.worldOffset;
            return q + MaskWarpOffset(q, prm);
        }

        /// Also hands back SkerryDomain, which costs a full warp and is
        /// already in hand here, so EvaluateBase does not pay for it twice.
        static float MaskNoiseBase(in float2 p, in TerrainParams prm, out float2 skerryDomain)
        {
            bool tiers = prm.nearIslandScale > 1.0001f;
            if (!BlendedMask(prm))
            {
                skerryDomain = MaskDomain(p, prm);
                return TerrainNoise.Fbm01(skerryDomain, prm.seed + TerrainParams.MaskSeedOffset,
                    prm.maskOctaves, prm.maskFrequency, 2f, 0.5f);
            }

            float2 q = p + prm.worldOffset;
            // Warp read on the unstretched domain, once, and shared: the
            // fields differ in stretch, not in wobble, so a coast in a blend
            // band bends one way rather than two.
            float2 warp = MaskWarpOffset(q, prm);
            skerryDomain = q + warp;
            float3 axisW = prm.regionalGrain != 0 ? GrainWeights(q, prm) : new float3(1f, 0f, 0f);
            float far = tiers ? math.smoothstep(prm.nearIslandRadius, prm.farIslandRadius, math.length(p)) : 1f;

            float sum = 0f, sq = 0f;
            for (int k = 0; k < 3; k++)
            {
                float a = axisW[k];
                if (a <= 0f) continue;
                float ang = math.atan2(prm.maskGrainSin, prm.maskGrainCos) + k * (math.PI / 3f);
                float2 qk = Stretch(q, new float2(math.cos(ang), math.sin(ang)), prm.maskStretch) + warp;
                int axisSeed = prm.seed + TerrainParams.MaskSeedOffset + k * TerrainParams.MaskAxisSeedStep;
                if (far > 0f)
                {
                    float w = a * far;
                    sum += w * (TerrainNoise.Fbm01(qk, axisSeed, prm.maskOctaves, prm.maskFrequency, 2f, 0.5f) - 0.5f);
                    sq += w * w;
                }
                if (far < 1f)
                {
                    float w = a * (1f - far);
                    sum += w * (TerrainNoise.Fbm01(qk, axisSeed + TerrainParams.MaskNearSeedOffset, prm.maskOctaves,
                        prm.maskFrequency * prm.nearIslandScale, 2f, 0.5f) - 0.5f);
                    sq += w * w;
                }
            }
            return 0.5f + sum / math.sqrt(math.max(sq, 1e-6f));
        }

        /// Which of the three grain axes a spot belongs to, as weights that
        /// sum to one. The selector is the ANGLE of a slow 2D noise vector,
        /// cut into three 120 degree sectors with `grainBlendDegrees` of
        /// blend at each border: an angle is uniform where a single noise
        /// value is not, so each grain gets a third of the sea. Where the
        /// vector is near zero its angle means nothing and spins, so the
        /// weights relax to a third each there instead of flickering.
        static float3 GrainWeights(float2 q, in TerrainParams prm)
        {
            float gx = TerrainNoise.Fbm01(q, prm.seed + TerrainParams.GrainSeedOffsetA,
                1, prm.grainRegionFrequency, 2f, 0.5f) - 0.5f;
            float gy = TerrainNoise.Fbm01(q, prm.seed + TerrainParams.GrainSeedOffsetB,
                1, prm.grainRegionFrequency, 2f, 0.5f) - 0.5f;
            float deg = math.degrees(math.atan2(gy, gx));
            float b = prm.grainBlendDegrees * 0.5f;
            float3 w = float3.zero;
            for (int k = 0; k < 3; k++)
            {
                float d = math.fmod(math.abs(deg - k * 120f), 360f);
                d = math.min(d, 360f - d);
                w[k] = 1f - math.smoothstep(60f - b, 60f + b, d);
            }
            w /= math.max(1e-4f, math.csum(w));
            float settle = math.smoothstep(0.015f, 0.05f, math.length(new float2(gx, gy)));
            return math.lerp(new float3(1f / 3f), w, settle);
        }

        public static float MaskNoise(in float2 p, in TerrainParams prm)
        {
            float c = MaskNoiseBase(p, prm, out float2 sd);
            return c + Skerry(sd, c, prm, 0);
        }

        /// How much of this spot's existence it owes to the islet field, in
        /// [0, 1]. The lever that keeps an islet a sandbank instead of a
        /// spire -- see `skerryRelief`.
        public static float SkerryShare(float skerryLift, in TerrainParams prm)
            => prm.skerryAmount <= 0f ? 0f
             : math.saturate(skerryLift / math.max(0.01f, prm.skerryAmount));

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

        /// Where the mountains are WITHIN an island, in [0, 1]: 0 is plain,
        /// 1 is full relief.
        ///
        /// The massif decides how tall an island is allowed to be. This
        /// decides how much of it actually is, and it exists because the land
        /// has to be lived on: measured before it, 45.9% of dry land was
        /// steeper than 36 degrees and only 17.7% was flat enough to stand a
        /// building on. That is a mountain range with a beach around it, not
        /// somewhere to put an outpost and run a wall.
        ///
        /// Biased hard toward lowland, so a mountain is an event on an island
        /// rather than the island's default state.
        public static float Upland01(in float2 p, in TerrainParams prm)
            => math.smoothstep(prm.uplandStart, prm.uplandFull,
                TerrainNoise.Fbm01(p + prm.worldOffset, prm.seed + TerrainParams.UplandSeedOffset,
                    3, prm.uplandFrequency, 2f, 0.5f));

        /// Per-island height character, in [0, 1].
        public static float Massif01(in float2 p, in TerrainParams prm)
            => TerrainNoise.Fbm01(p + prm.worldOffset, prm.seed + TerrainParams.MassifSeedOffset,
                2, prm.massifFrequency, 2f, 0.5f);

        /// **How rocky this island is**, in [0, 1].
        ///
        /// Island-scale and near-constant across one landmass, for the same
        /// reason the massif is: the height function is a pure function of
        /// world position running in Burst on streamed chunks, so an
        /// island's character cannot be a lookup -- it has to be a field
        /// slow enough that one landmass sits inside one value of it, and
        /// only changes out in the water between islands. Sample it any
        /// faster and a sandbank blends into a crag across the same beach,
        /// which is the one-shape problem wearing a costume.
        ///
        /// Biased so most islands are soft ground and a rocky one is an
        /// event -- the same rule this file already applies to peaks.
        /// Zeroed over the authored home island, and that one multiply is
        /// what keeps it grass and sand. `RockBreak` returns early at zero
        /// rockiness, so no stone stands proud, so the mesher's rock colour
        /// never fires -- the island is made of the right material rather
        /// than painted to look like it.
        public static float Rock01(in float2 p, in TerrainParams prm)
            => math.pow(TerrainNoise.Fbm01(p + prm.worldOffset,
                prm.seed + TerrainParams.RockSeedOffset, 2,
                prm.rockCharacterFrequency, 2f, 0.5f), prm.rockBias)
               * (1f - HomeIsleWeight(p, prm));

        /// **How green this island is**, in [0, 1].
        ///
        /// The third character axis, and the one that makes the others read
        /// as a KIND rather than as independent dice. Rock and soil are
        /// opposites — a crag island is not a wood — so verdancy is its own
        /// island-scale field *suppressed by rockiness*. Roll them
        /// independently and you get lush crags and bare downs, which is
        /// noise; correlate them and low-rock-lush and high-rock-bare emerge
        /// as recognisable island kinds without anything having to enumerate
        /// a list of them.
        public static float Verdancy01(in float2 p, in TerrainParams prm)
        {
            float v = math.pow(TerrainNoise.Fbm01(p + prm.worldOffset,
                prm.seed + TerrainParams.VerdancySeedOffset, 2,
                prm.verdancyFrequency, 2f, 0.5f), prm.verdancyBias);
            float rocky = Rock01(p, prm);
            // ...and zeroed over home for the same reason. IslandScenery reads
            // this once at the island's centre and multiplies every placement
            // chance by it, so a verdancy of zero is a wood that is never
            // planted rather than a wood that is planted and then culled.
            return math.lerp(prm.verdancyFloor, 1f,
                math.saturate(v * (1f - prm.verdancyRockSuppress * rocky)))
               * (1f - HomeIsleWeight(p, prm) * (prm.storybookLandforms != 0 ? 0f : 1f));
        }

        /// **Metres of rock standing PROUD of the soil here.** Zero almost
        /// everywhere.
        ///
        /// This is the difference between rock that is *revealed* and rock
        /// that *protrudes*, and it is the only place in this pipeline that
        /// is not a lerp, an add or a multiply of one smooth field. Those can
        /// only ever produce rounded ground with a grey shading rule on the
        /// steep parts -- rock as a colour. What the eye reads as a different
        /// material pushing through is the CREASE where two surfaces cross,
        /// and a crease is what `max` makes and `+` does not.
        ///
        /// The rock's own surface is the soil landform offset by a sharp
        /// ridged field: `max(soil, soil + relief*(ridge - threshold))`,
        /// which reduces to soil plus this. So soil buries rock in the
        /// hollows and rock breaks out on the shoulders on its own, and the
        /// threshold is the whole story -- high and only the sharpest ridges
        /// surface, low and the island is a crag.
        ///
        /// Gated by rockiness alone and NOT by `interior`, deliberately: a
        /// rocky island is supposed to be rocky down to the water, and a
        /// stack standing off its shore is this field surfacing where the
        /// soil is already below sea level. A soft island returns exactly
        /// zero, so every shore profile BeachProbe was tuned against is
        /// untouched on the islands that are meant to have beaches.
        public static float RockBreak(in float2 p, float rockiness, in TerrainParams prm)
        {
            if (prm.rockRelief <= 0f || rockiness <= 0.001f) return 0f;
            float rf = TerrainNoise.RidgedRaw(p + prm.worldOffset,
                prm.seed + TerrainParams.CragSeedOffset, prm.rockOctaves,
                prm.rockFrequency, 2.3f, 0.55f);
            float thr = math.lerp(prm.rockThresholdSoft, prm.rockThresholdHard, rockiness);
            return math.max(0f, prm.rockRelief * rockiness * (rf - thr));
        }

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
        public static float Amplitude(in float2 p, float interior, float upland, in TerrainParams prm)
        {
            float m = math.pow(Massif01(p, prm), prm.massifBias);
            float scale = math.lerp(prm.massifMin, prm.massifMax, m);
            // Relief is collapsed toward the lowland fraction away from the
            // uplands, and slope scales with relief -- which is the whole
            // point: this is the lever on how much of an island is usable.
            float mountain = prm.reliefHeight * math.lerp(1f, scale, interior);

            // The plain's relief is an ABSOLUTE height, not a fraction of the
            // mountain's. As a fraction it scaled with the massif, so a low
            // island's whole interior collapsed to within a couple of metres
            // of sea level and rendered as one enormous sand flat -- the land
            // was flat, which was the point, but it was flat at the wrong
            // ALTITUDE.
            //
            // Faded in by interior like everything else, so the fringe keeps
            // the amplitude the coastline position depends on.
            float plain = math.lerp(prm.reliefHeight, prm.plainRelief, interior);
            return math.lerp(plain, mountain, upland);
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
        public static float ShoreTerrace(float h, in float2 p, float interior, in TerrainParams prm)
        {
            float a = h - prm.seaLevel;
            if (a >= prm.shoreTop || a <= -prm.shoreBottom) return h;

            // Gated by how far inland this is, not by height alone.
            //
            // A beach is a COASTAL feature. Keyed purely on height it also
            // flattened every low inland acre, and once the interior was
            // flattened for building that was a third of all dry land sitting
            // under the sand line -- whole islands rendering as one sand
            // flat. Fading the compression out with the same interior weight
            // the massif uses confines the foreshore to the coastal ring,
            // where it belongs.
            float k = math.lerp(ShoreSlope(p, prm), 1f, interior);
            if (k >= 0.999f) return h;
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

        /// **The home island is authored, not found.**
        ///
        /// Every other island in this world is whatever the noise happened to
        /// put there, and that is right for a world you sail out into. It is
        /// wrong for the one place the player starts, because home has to
        /// satisfy things noise cannot be asked for: it must read WHOLE in the
        /// docked overview, it must be flat enough to build a village on, and
        /// it must have somewhere a 24 m ship can actually lie. Sliding
        /// `worldOffset` until a procedural island happens to do all three is
        /// how the home island has been chosen so far, and it moves every time
        /// any mask constant moves — see the 2026-09-09 entry where the pier
        /// ended up standing in open water with no error of any kind.
        ///
        /// So this stamps one over the top. It is a pure function of position
        /// like everything else here, it runs in Burst, and it is applied to
        /// the FINAL height rather than to any stage of the pipeline: the
        /// stages are coupled (the mask sets both the shoreline and the shelf,
        /// the seabed is keyed on the mask noise, the massif fades in by the
        /// mask) and reaching into the middle of them is what puts a spire up
        /// out of deep water. Overriding the answer cannot.
        ///
        /// `worldOffset` deliberately does NOT move it. The ship spawns at the
        /// world origin, so the island is placed in absolute metres and the
        /// origin lands in its cove every time; the offset goes on choosing
        /// the archipelago beyond the fade band and nothing else.

        /// How much of this point belongs to the authored island: 1 out to
        /// `homeIsleFadeStart`, 0 past `homeIsleFadeEnd`.
        ///
        /// The band is wide on purpose. Land the archipelago happens to leave
        /// inside it is pulled down into a shoal rather than half-drowned into
        /// a cliff, and home comes out standing alone in open water — which is
        /// what makes it read whole from the deck.
        public static float HomeIsleWeight(in float2 p, in TerrainParams prm)
        {
            if (prm.homeIsle == 0) return 0f;
            return 1f - math.smoothstep(prm.homeIsleFadeStart, prm.homeIsleFadeEnd,
                                        math.distance(p, prm.homeIsleCentre));
        }

        /// The ramp shape used both above and below the waterline: exactly 0
        /// at u = 0 and exactly 1 at u = 1, with a real but gentle slope at
        /// BOTH ends.
        ///
        /// A plain smoothstep is flat at both ends, and a foreshore that
        /// leaves the water at zero slope is a tidal flat whose waterline
        /// wanders tens of metres with the swell. A plain line creases where
        /// it meets the flat top. Mixing the two keeps a definite beach slope
        /// at the sea and still joins the plateau without a fold; the steepest
        /// part ends up in the middle, which is where a grassy bank belongs.
        static float ShoreRamp(float u)
        {
            u = math.saturate(u);
            const float Straight = 0.5f;
            return Straight * u + (1f - Straight) * math.smoothstep(0f, 1f, u);
        }

        /// The authored island's height at a point, metres above sea level
        /// (the caller adds `seaLevel`).
        public static float HomeIsleHeight(in float2 p, in TerrainParams prm)
        {
            if (HomePlateauSurface.Enabled(prm))
                return HomePlateauSurface.TryHeight(p, prm, out float authored) ? authored - prm.seaLevel : prm.seabedDepth;
            float2 q = p - prm.homeIsleCentre;

            // The coastline is the level set of r - R(p), not a radius as a
            // function of angle. Angle-keyed outlines seam at +/-pi and cannot
            // make a bay that turns back on itself; this one is closed and
            // organic for free, and it is the same trick MaskDomain's warp
            // plays on the archipelago.
            float wobble = TerrainNoise.Fbm01(q, prm.seed + TerrainParams.HomeShapeSeedOffset,
                3, prm.homeIsleShapeFrequency, 2f, 0.5f) - 0.5f;
            float t = math.length(q) - (prm.homeIsleRadius + 2f * prm.homeIsleShape * wobble);

            // The cove is a SECOND SHORELINE, and that is the whole design.
            //
            // It was a carve first -- lerp the ground down to a floor inside a
            // segment -- and a carve gives you walls. Measured: 5.2 m of land
            // dropping to -4.2 m across a 7 m soft edge is a slope of 1.3, so
            // the one part of the island a boat has to get into was the one
            // part rendering as cliff, on an island whose brief was flat grass
            // and sand. Widening the soft edge only makes a shallower wall.
            //
            // Written as a shore instead, the cove gets the island's own beach
            // profile for free: sand up the rim at the same gradient as the
            // outer coast, a real landing beach at the head where the walls
            // close in, and a bank below the waterline steep enough to give
            // deep water close in. The two shores are combined with `min`,
            // which is what stops the union ever RAISING the seabed -- lerping
            // toward a floor puts a bar across the mouth, and a berth behind a
            // bar is exactly what HarbourSite's approach test exists to reject.
            float h = math.min(Profile(t, prm.homeIsleShoreRun, prm.homeIsleTop,
                                       prm.homeIsleForeshore, prm.seabedDepth),
                               Profile(CoveInside(q, prm), prm.homeIsleShoreRun, prm.homeIsleTop,
                                       prm.homeIsleCoveBank, prm.homeIsleCoveFloor));

            // The meadow's roll rides on how high the ground already is, so it
            // is full strength on the flat top and exactly nothing at either
            // waterline. Anything that reaches a waterline MOVES that
            // waterline, which is the regression this pipeline has paid for
            // twice; keying it to the height it is modifying makes that
            // impossible rather than merely unlikely.
            if (prm.homeIsleRoll > 0f && h > 0f)
                h += prm.homeIsleRoll * (prm.storybookLandforms != 0 ? math.smoothstep(4.8f, 5.2f, h) * .3f : math.saturate(h / prm.homeIsleTop)) * 2f
                     * (TerrainNoise.Fbm01(q, prm.seed + TerrainParams.HomeRollSeedOffset, 2,
                            prm.homeIsleRollFrequency, 2f, 0.5f) - 0.5f);
            return h;
        }

        /// One shoreline's profile: `d` is metres INTO THE WATER, so it is
        /// negative on land and its magnitude there is how far inland you
        /// stand. Climbs to `top` over `run` inland, falls to `floor` over
        /// `reach` out to sea.
        ///
        /// The outer coast passes `r - R`, which is already that. The cove
        /// passes `CoveInside` directly, because being inside the cove IS
        /// being in the water -- negating it (the obvious reading of "metres
        /// inside") turns the whole island into a 0.16 ha sandbank at the
        /// head of the cove, with every gate still passing.
        static float Profile(float d, float run, float top, float reach, float floor)
            => d <= 0f ? top * ShoreRamp(-d / run) : floor * ShoreRamp(d / reach);

        /// Metres inside the cove: positive in the water, negative on the
        /// land around it. The distance to the cove's axis less the
        /// half-width there, so `Profile` can treat it as a second coastline.
        ///
        /// Three widths, not two, and the middle one is what makes it a cove
        /// rather than an inlet: it opens into a BASIN and closes again at a
        /// THROAT between two horns. Shelter is measured as how much of the
        /// seaward horizon is land, and a mouth that is the widest part of
        /// the water has none of it -- the first version tapered straight out
        /// from head to mouth and HarbourSite scored the berth 0.10.
        ///
        /// The capsule is closed at BOTH ends. Clamping only the head let the
        /// half-width run on past the mouth forever, and because the cove
        /// floor is deeper than the shallow foreshore it crosses, that ran a
        /// dead-straight gut a hundred metres out to sea -- a dredged channel
        /// on an island with nobody to dredge it. It has to end where the
        /// open foreshore is ALREADY deeper than the cove, or closing it just
        /// swaps a channel for a bar.
        static float CoveInside(in float2 q, in TerrainParams prm)
        {
            float2 dir = prm.homeIsleCoveDir;
            float along = math.dot(q, dir);
            float across = math.abs(q.x * dir.y - q.y * dir.x);

            float half;
            if (along <= prm.homeIsleCoveBasin)
                half = math.lerp(prm.homeIsleCoveHalfHead, prm.homeIsleCoveHalfBasin,
                    math.smoothstep(prm.homeIsleCoveHead, prm.homeIsleCoveBasin, along));
            else
                half = math.lerp(prm.homeIsleCoveHalfBasin, prm.homeIsleCoveHalfMouth,
                    math.smoothstep(prm.homeIsleCoveBasin, prm.homeIsleCoveMouth, along));

            // The HEAD is a rounded box, not a round cap, and that is a
            // harbour decision rather than a shape one.
            //
            // HarbourSite takes a site's seaward bearing from the local
            // downhill and then runs a straight 160 m approach along it. In a
            // 48 m cove any bearing more than about 12 degrees off the axis
            // crosses to the far bank inside that run and is rejected, so the
            // only berths a cove can ever offer are the ones on ground whose
            // downhill points straight out of it. On a round cap the normal
            // IS the angular position, so that is a 12-degree arc -- two or
            // three cells at a 6 m raster, however wide the cap is made.
            // Widening it from 15 m to 20 m changed nothing at all, which is
            // what said the model was wrong.
            //
            // A straight back beach points every one of its cells down the
            // axis. The corners are rounded by `Round` so the two spit tips
            // are still tips and not right angles.
            const float Round = 8f;
            float ex = across - math.max(1f, half - Round);
            float ey = (prm.homeIsleCoveHead + Round) - along;
            float ahead = math.max(0f, along - prm.homeIsleCoveMouth);
            float outside = math.sqrt(math.max(ex, 0f) * math.max(ex, 0f)
                                    + math.max(ey, 0f) * math.max(ey, 0f)
                                    + ahead * ahead);
            float inside = math.min(math.max(ex, ey), 0f);
            float d = Round - (outside + inside);

            // A rounded box has straight sides, and on the map they read as
            // exactly what they are: a trapezoid cut out of an otherwise
            // organic coast. The wobble is the same idea as the island's own
            // outline -- displace the distance field and its level set bends
            // -- but it is faded OFF over the back beach, because that beach
            // is straight for a reason. Wandering it by 4 m over 55 tilts the
            // local downhill by fifteen-odd degrees, and the approach test
            // rejects anything more than about twelve off the axis: the noise
            // that makes the cove look natural is the noise that would empty
            // it of berths.
            float clear = math.smoothstep(prm.homeIsleCoveHead + 6f,
                                          prm.homeIsleCoveHead + 22f, along);
            if (clear <= 0f) return d;
            return d + clear * 8f * (TerrainNoise.Fbm01(q,
                prm.seed + TerrainParams.HomeCoveSeedOffset, 2, 1f / 55f, 2f, 0.5f) - 0.5f);
        }

        /// Full pipeline with all intermediates. Order matters: the mask is
        /// applied BEFORE the beach blend so the blend sees real altitudes —
        /// every shoreline climbs from the seabed through the 0..beachHeight
        /// band, and that band is guaranteed smooth whatever the curve does.
        static TerrainSample EvaluateBase(in float2 p, in TerrainParams prm, in NativeArray<float> lut)
        {
            TerrainSample s;
            // The mask comes FIRST now: both the ridges and the massif are
            // faded in by how far inland the spot is, so the shape field
            // cannot be computed before we know that.
            float cBase = MaskNoiseBase(p, prm, out float2 skerryDomain);
            float lift = Skerry(skerryDomain, cBase, prm, 0);
            float c = cBase + lift;
            s.mask = MaskFromNoise(c, p, prm);
            float interior = Interior(s.mask, prm);
            float upland = Upland01(p, prm);

            // **An islet is a sandbank, not a sea stack.** Nothing else in
            // this pipeline knows how big a landmass is -- relief comes from
            // fields sampled per point -- so a 50 m islet was handed the same
            // mountain amplitude as a 500 m island and came out a 100 m
            // needle at 5% walkable. The islet field is the one place that
            // DOES know, because its own lift says so.
            float islet = SkerryShare(lift, prm);
            upland *= 1f - islet;
            // Ridges are an upland feature too. Carving crest lines across a
            // plain would put back exactly the slope the plain exists to
            // remove.
            s.noise01 = Noise01(p, prm, interior * upland);
            // The 25 m detail layer is damped on the lowlands. On its own it
            // carries a slope of about 0.24 -- steeper than the 0.176 a
            // building needs -- so no amount of flattening the landform makes
            // ground buildable while this is running at full amplitude over
            // the top of it.
            float detail = Detail(p, prm) * math.lerp(prm.lowlandDetail, 1f, upland);
            float amp = math.lerp(Amplitude(p, interior, upland, prm), prm.skerryRelief, islet);
            float land = prm.baseHeight + Terrace(s.noise01, lut) * amp;

            // Rock goes on BOTH paths, exactly as detail does. The beach
            // blend crossfades the two, so anything added to one and not the
            // other is a step in the shoreline -- the same trap the smooth
            // path's amplitude comment already warns about. It stays off the
            // seabed term: the ocean's storm envelope is depth-limited
            // against that shelf and reefs are placed against it.
            s.rock = RockBreak(p, Rock01(p, prm), prm);
            s.terraced = OverSeabed(land + detail + s.rock, s.mask, detail, c, prm);
            s.smooth = OverSeabed(Smooth(s.noise01, amp, prm) + detail + s.rock, s.mask, detail, c, prm);
            s.height = ShoreTerrace(BeachBlend(s.terraced, s.smooth, prm), p, interior, prm);

            // Home last, over the top of the finished answer. Rock01 already
            // returns zero here, so `s.rock` is zero without being touched --
            // the island is grass and sand by construction rather than by a
            // shading rule.
            float home = HomeIsleWeight(p, prm);
            if (home > 0f)
            {
                float hh = HomeIsleHeight(p, prm) + prm.seaLevel;
                s.height = math.lerp(s.height, hh, home);
                // The visualiser's two intermediates follow it, or the sheets
                // draw the island that was replaced.
                s.terraced = math.lerp(s.terraced, hh, home);
                s.smooth = math.lerp(s.smooth, hh, home);
            }
            return s;
        }

        // Inland terrain uses broad, continuous triangular planes before the
        // terrace profile. The same function serves mesh, collision, scatter,
        // navigation and depth queries; no decorative geometry hides old ground.
        public static TerrainSample Evaluate(in float2 p, in TerrainParams prm, in NativeArray<float> lut, bool includeAuthoredGround = true)
        {
            var s = EvaluateBase(p, prm, lut);
            float home = HomeIsleWeight(p, prm);
            if (prm.storybookLandforms != 0 && home < .9999f && s.height > prm.seaLevel + 8f)
            {
                const float span = 24f;
                // Shear the lattice to avoid a square checkerboard of faces.
                float2 q = new float2(p.x + .37f * p.y, p.y) / span;
                float2 cell = math.floor(q), f = math.frac(q);
                float2 a, b, c;
                float3 weights;
                if (f.x + f.y <= 1f)
                {
                    a = cell; b = cell + new float2(1,0); c = cell + new float2(0,1);
                    weights = new float3(1f-f.x-f.y, f.x, f.y);
                }
                else
                {
                    a = cell + 1f; b = cell + new float2(0,1); c = cell + new float2(1,0);
                    weights = new float3(f.x+f.y-1f, 1f-f.x, 1f-f.y);
                }
                float3 heights = new float3(
                    EvaluateBase(PlanePoint(a, span), prm, lut).height,
                    EvaluateBase(PlanePoint(b, span), prm, lut).height,
                    EvaluateBase(PlanePoint(c, span), prm, lut).height);
                // Do not let a macro triangle drag a cliff into a low beach.
                float inland = math.smoothstep(8f, 18f, s.height-prm.seaLevel);
                s.height = math.lerp(s.height, math.max(prm.seaLevel + 8f, math.dot(heights, weights)), inland * (1f-home));
            }
            if (prm.storybookLandforms != 0)
            {
                float h = s.height - prm.seaLevel;
                // Keep every existing waterline, underwater approach and beach.
                // A terrace is actual terrain: rendering, collision, slope tests
                // and the crew all sample this same answer.
                if (h > 4f)
                {
                    float inland = math.smoothstep(4f, 10f, h);
                    float relief = math.max(0f, h - 7f) * 1.65f;
                    float level = relief / 16f;
                    // Straight cliff ramps and broad shelves, like the authored home.
                    // This is shared by mesh, collision and placement queries.
                    float terrace = (math.floor(level) + math.saturate((math.frac(level) - .67f) / .27f)) * 16f;
                    float shaped = 7f + math.lerp(terrace, relief, .08f);
                    s.height = prm.seaLevel + math.lerp(h, shaped, inland * (1f - home));

                }
            }
            // The streamed grid stays submerged under the separate authored mesh.
            if (!includeAuthoredGround && HomePlateauSurface.Enabled(prm))
                s.height = math.lerp(s.height, prm.seaLevel + prm.seabedDepth, home);
            // Hand edits LAST, over everything above, so mesh, collider,
            // shore grid, populator and horizon all read the same ground.
            if (prm.edits.count > 0)
            {
                s.height = TerrainEdits.Apply(prm.edits, p, s.height, prm.seaLevel);
                s.terraced = TerrainEdits.Apply(prm.edits, p, s.terraced, prm.seaLevel);
                s.smooth = TerrainEdits.Apply(prm.edits, p, s.smooth, prm.seaLevel);
            }
            return s;
        }

        static float2 PlanePoint(float2 cell, float span)
            => new float2(cell.x - .37f * cell.y, cell.y) * span;

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
